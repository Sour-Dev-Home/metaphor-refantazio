using System.Diagnostics.CodeAnalysis;
using Metaphor.Core.Announcements;

namespace Metaphor.Features.Field;

/// <summary>Says a door or chest as it comes into reach (#644), the moment the game's action prompt would show: "Door, in
/// reach". It's said once, and again only after the thing has left reach and come back. Reach is the game's own test
/// (<see cref="Nearby.InReach"/>), so people stay out until theirs is found. The game's prompt itself, and the target it
/// picks, sit behind <c>.text1</c> and aren't read (docs/findings/field-interaction-prompt-static.md); its words from
/// <c>AccessPointName.msg</c> are <c>[NEEDS VERIFICATION]</c>, so the kind word stands in.
/// <para>The field controls call <see cref="Check"/> on every keyboard read, which the game makes once a frame by its
/// poll sites (that they run every frame is [NEEDS VERIFICATION]: docs/raw-sources/input-route-2026-11-16.md). It reads the field only in free
/// roam and at most once per <see cref="CheckIntervalMilliseconds"/>; the calls between allocate nothing. The callout
/// queues behind what is being said rather than cutting it off: it's news the player didn't ask for, and the end of a
/// line just spoken may still be playing as free roam comes back.</para></summary>
public sealed class ReachCallouts
{
    /// <summary>The least time between two reads of the field: ten a second, quick enough for a walk.</summary>
    public const long CheckIntervalMilliseconds = 100;

    private readonly Lock gate = new();
    private readonly IFieldReads field;
    private readonly IAnnouncer announcer;
    private readonly Func<long> milliseconds;
    private HashSet<Spot> inReach = [];
    private HashSet<Spot> seen = [];
    private long checkedAt;
    private bool checkedOnce;

    /// <summary>Callouts that read <paramref name="field"/>, speak through <paramref name="announcer"/> and keep time by
    /// <paramref name="milliseconds"/>.</summary>
    public ReachCallouts(IFieldReads field, IAnnouncer announcer, Func<long> milliseconds)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(milliseconds);
        this.field = field;
        this.announcer = announcer;
        this.milliseconds = milliseconds;
    }

    /// <summary>Reads the field when a check is due and the player roams freely, and says what has come into reach since
    /// the last read. What stays in reach through a menu or a dialogue isn't said again after it; an unreadable field
    /// changes nothing. A read that throws says nothing, so it never reaches the game.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A failed read must never reach the game; it says nothing.")]
    public void Check()
    {
        string? text;
        lock (gate)
        {
            var now = milliseconds();
            if (checkedOnce && now - checkedAt < CheckIntervalMilliseconds)
            {
                return;
            }
            checkedOnce = true;
            checkedAt = now;
            try
            {
                text = field.InFreeRoam() && field.Read() is { } view ? Entered(view) : null;
            }
            catch (Exception)
            {
                return;
            }
        }
        if (text is not null)
        {
            announcer.Announce(new Announcement(text, Interrupt: false));
        }
    }

    /// <summary>Swaps in what is in reach now and returns what came into it, or null for nothing.</summary>
    private string? Entered(FieldView view)
    {
        var things = Nearby.InReach(view);
        seen.Clear();
        List<string>? entered = null;
        foreach (var thing in things)
        {
            var unit = thing.Unit;
            var spot = new Spot(unit.Kind, unit.X, unit.Y, unit.Z);
            seen.Add(spot);
            if (!inReach.Contains(spot))
            {
                (entered ??= []).Add($"{unit.Name ?? Nearby.KindWord(unit.Kind)}, in reach");
            }
        }
        (inReach, seen) = (seen, inReach);
        return entered is null ? null : string.Join(". ", entered);
    }

    /// <summary>A thing told apart from the others by its kind and position: doors and chests stay put. One that moves,
    /// such as a door that swings, may be said again [NEEDS VERIFICATION].</summary>
    private readonly record struct Spot(FieldKind Kind, float X, float Y, float Z);
}
