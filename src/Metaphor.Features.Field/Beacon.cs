using System.Diagnostics.CodeAnalysis;
using Metaphor.Core.Announcements;
using Metaphor.Core.Cues;

namespace Metaphor.Features.Field;

/// <summary>What the beacon guides to: a unit as the nearby list read it, and the door reach value read with it.</summary>
/// <param name="Unit">The target, at the position it had when picked.</param>
/// <param name="DoorReachValue">As <see cref="FieldView.DoorReachValue"/>, for the target's reach test.</param>
public sealed record BeaconTarget(FieldUnit Unit, int? DoorReachValue);

/// <summary>A repeating tone toward a target picked from the nearby list (#183), in a straight line: panned by its
/// bearing from the player's facing, higher ahead than behind (stereo alone can't tell them apart), and pulsing faster
/// as it nears. It stops on arrival (the game's reach test for the target's kind, or within
/// <see cref="ArrivalDistance"/>), on <see cref="Stop"/>, and silently on leaving free roam or when the player can't be
/// read. <see cref="Tick"/> is called on every input read; it reads the field at most once per
/// <see cref="PollMilliseconds"/>, and allocates nothing between a pulse's start and the next.</summary>
public sealed class Beacon
{
    /// <summary>How often the field is read while guiding.</summary>
    public const long PollMilliseconds = 50;

    /// <summary>How long each pulse sounds.</summary>
    public const long PulseMilliseconds = 80;

    /// <summary>The ground distance, in game units, that counts as arrived for any kind. Running covers about 700 units a
    /// second (by-ear walk of 2026-11-21, on PR #643), so this is about a fifth of a second's run, and the doors' near
    /// reach.</summary>
    public const double ArrivalDistance = 150;

    /// <summary>The gap between pulse starts at <see cref="ArrivalDistance"/> or nearer.</summary>
    public const long NearIntervalMilliseconds = 200;

    /// <summary>The gap between pulse starts at the nearby list's radius or farther.</summary>
    public const long FarIntervalMilliseconds = 1200;

    /// <summary>The pitch straight ahead: A5.</summary>
    public const double AheadHertz = 880;

    /// <summary>The pitch straight behind: A4, an octave down.</summary>
    public const double BehindHertz = 440;

    /// <summary>The pulses' volume, within the sound cue volume.</summary>
    public const int Volume = 80;

    private readonly Lock gate = new();
    private readonly IFieldReads field;
    private readonly ICuePlayer cues;
    private readonly IAnnouncer announcer;
    private readonly Func<long> milliseconds;
    private BeaconTarget? target;
    private volatile bool active;
    private bool polled;
    private long polledAt;
    private double distance;
    private double bearing;
    private bool sounding;
    private long pulseEndsAt;
    private long nextPulseAt;

    /// <summary>A beacon that reads <paramref name="field"/>, plays on <paramref name="cues"/>' guidance channel, speaks
    /// through <paramref name="announcer"/> and keeps time by <paramref name="milliseconds"/>.</summary>
    public Beacon(IFieldReads field, ICuePlayer cues, IAnnouncer announcer, Func<long> milliseconds)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(milliseconds);
        this.field = field;
        this.cues = cues;
        this.announcer = announcer;
        this.milliseconds = milliseconds;
    }

    /// <summary>Whether the beacon is guiding.</summary>
    public bool Active => active;

    /// <summary>From -1 (left) to +1 (right) for a bearing, left positive: full left or right at the side, centred
    /// ahead and behind.</summary>
    public static double Pan(double bearing) => -Math.Sin(bearing);

    /// <summary>The pitch for a bearing: <see cref="AheadHertz"/> ahead, <see cref="BehindHertz"/> behind, halfway at
    /// the side.</summary>
    public static double Hertz(double bearing) =>
        BehindHertz + ((AheadHertz - BehindHertz) * (1 + Math.Cos(bearing)) / 2);

    /// <summary>The gap between pulse starts for a ground distance: straight-line from
    /// <see cref="NearIntervalMilliseconds"/> at <see cref="ArrivalDistance"/> to <see cref="FarIntervalMilliseconds"/>
    /// at the nearby list's radius, held beyond both.</summary>
    public static long IntervalMilliseconds(double distance)
    {
        var t = Math.Clamp((distance - ArrivalDistance) / (Nearby.Radius - ArrivalDistance), 0, 1);
        return NearIntervalMilliseconds + (long)Math.Round((FarIntervalMilliseconds - NearIntervalMilliseconds) * t);
    }

    /// <summary>Starts guiding to <paramref name="next"/>, in place of any target before, and says so.</summary>
    public void Start(BeaconTarget next)
    {
        ArgumentNullException.ThrowIfNull(next);
        lock (gate)
        {
            target = next;
            polled = false;
            sounding = false;
            nextPulseAt = milliseconds();
            active = true;
        }
        announcer.Announce(new Announcement($"Guiding to {Name(next)}", Interrupt: true));
    }

    /// <summary>The player's cancel: stops guiding and says so. Does nothing when idle.</summary>
    public void Stop()
    {
        if (End())
        {
            announcer.Announce(new Announcement("Guidance off", Interrupt: true));
        }
    }

    /// <summary>Moves the beacon on to now: reads the field when a poll is due, stops on arrival or on leaving free
    /// roam, and starts or ends a pulse when one is due. Returns at once when idle. A read that throws stops it
    /// silently, so nothing reaches the game.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A failed read must never reach the game; it stops the beacon.")]
    public void Tick()
    {
        if (!active)
        {
            return;
        }
        string? reached = null;
        try
        {
            lock (gate)
            {
                if (target is not { } guided)
                {
                    return;
                }
                var now = milliseconds();
                if (!polled || now - polledAt >= PollMilliseconds)
                {
                    polled = true;
                    polledAt = now;
                    if (!field.InFreeRoam() || field.ReadPose() is not { } pose)
                    {
                        EndLocked();
                        return;
                    }
                    var (far, toward, inReach) = Nearby.Measure(pose, guided.Unit, guided.DoorReachValue);
                    if (inReach || far <= ArrivalDistance)
                    {
                        EndLocked();
                        reached = Name(guided);
                        return;
                    }
                    distance = far;
                    bearing = toward;
                }
                if (sounding && now >= pulseEndsAt)
                {
                    cues.Silence(ToneChannel.Guidance);
                    sounding = false;
                }
                if (now >= nextPulseAt)
                {
                    cues.Hold(ToneChannel.Guidance, new HeldTone(Hertz(bearing), Pan(bearing), Volume));
                    sounding = true;
                    pulseEndsAt = now + PulseMilliseconds;
                    nextPulseAt = now + IntervalMilliseconds(distance);
                }
            }
        }
        catch (Exception)
        {
            End();
            return;
        }
        finally
        {
            if (reached is not null)
            {
                announcer.Announce(new Announcement($"Reached {reached}", Interrupt: true));
            }
        }
    }

    private static string Name(BeaconTarget guided) => guided.Unit.Name ?? Nearby.KindWord(guided.Unit.Kind);

    private bool End()
    {
        lock (gate)
        {
            if (target is null)
            {
                return false;
            }
            EndLocked();
            return true;
        }
    }

    private void EndLocked()
    {
        target = null;
        active = false;
        sounding = false;
        cues.Silence(ToneChannel.Guidance);
    }
}
