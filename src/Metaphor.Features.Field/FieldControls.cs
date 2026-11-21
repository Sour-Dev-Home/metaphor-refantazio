using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Metaphor.Core.Announcements;
using Metaphor.Core.Cues;
using Metaphor.Core.Input;

namespace Metaphor.Features.Field;

/// <summary>Which keys and buttons the field controls (#181) use. Buttons are bits of the game's own 16-bit pad mask;
/// keys are Windows virtual-key codes.</summary>
/// <param name="NearbyKey">The key that speaks the whole nearby list.</param>
/// <param name="PlaceKey">The key that says where the player is.</param>
/// <param name="HoldButtons">The button held for the pad's field chords.</param>
/// <param name="NextButton">With <paramref name="HoldButtons"/>: the next thing on the list.</param>
/// <param name="PreviousButton">With <paramref name="HoldButtons"/>: the thing before.</param>
/// <param name="PlaceButton">With <paramref name="HoldButtons"/>: where the player is.</param>
/// <param name="GuideKey">The key that starts the beacon toward the list's current thing (#183), or stops it.</param>
/// <param name="GuideButton">With <paramref name="HoldButtons"/>: as <paramref name="GuideKey"/>.</param>
public sealed record FieldBindings(
    int NearbyKey, int PlaceKey, int HoldButtons, int NextButton, int PreviousButton, int PlaceButton, int GuideKey,
    int GuideButton)
{
    /// <summary>N and L, and LB with the D-pad's Down, Up and Left: the maintainer's choices on #181 (2026-11-21). G and
    /// LB with the D-pad's Right for the beacon (#183) are proposed, pending the maintainer's choice. The game's mask
    /// puts LB at <c>0x0400</c> and the D-pad's Up, Right, Down and Left at <c>0x0010</c>, <c>0x0020</c>,
    /// <c>0x0040</c> and <c>0x0080</c> on game build 548ddc95… (Steam build 18330018), and its keyboard read lists any
    /// held key from 8 to <c>0xEF</c>, N (<c>0x4E</c>), L (<c>0x4C</c>) and G (<c>0x47</c>) among them:
    /// docs/raw-sources/pad-and-keyboard-state-2026-11-16.md.</summary>
    public static FieldBindings Default { get; } = new(0x4E, 0x4C, 0x0400, 0x0040, 0x0010, 0x0080, 0x47, 0x0020);
}

/// <summary>The field controls (#181), while the player roams the field: the nearby key speaks the list of things
/// nearby (<see cref="Nearby"/>), the pad's next and previous chords step through it one thing at a time, and the place
/// key and chord say where the player is (<see cref="FieldPlace"/>). The game state is read on the press, on the thread
/// that handed it the read, and copied into plain values. Only the exact press is hidden from the game, and only in free
/// roam: the key, or the chord's D-pad button (the held button stays, so the game never sees it let go and pressed
/// again). Elsewhere everything reaches the game. A press is an up-to-down edge of a binding held on any pad or the
/// keyboard, as for the menu controls. Speech goes to the announcer directly: each is an answer the player asked for.
/// The guide key and chord start the <see cref="Beacon"/> toward the thing last stepped to, or the nearest once that
/// step is forgotten, and stop it when it's on; every read of the pad or keyboard moves it on.
/// </summary>
public sealed class FieldControls : IInputControls
{
    /// <summary>How long the pad's place on the list lasts after a step; the next step after that starts again at the
    /// nearest.</summary>
    public const long StepMemoryMilliseconds = 10_000;

    private const int KeyboardSource = -1;

    private readonly Lock gate = new();
    private readonly FieldBindings bindings;
    private readonly IFieldReads field;
    private readonly FieldPlace place;
    private readonly IAnnouncer announcer;
    private readonly Func<long> milliseconds;
    private readonly Action<string>? log;
    private readonly Binding nearby = new();
    private readonly Binding next = new();
    private readonly Binding previous = new();
    private readonly Binding where = new();
    private readonly Binding guide = new();
    private readonly Beacon beacon;
    private int position = -1;
    private long steppedAt;
    private BeaconTarget? current;

    /// <summary>Field controls on <paramref name="bindings"/> that read <paramref name="field"/>, name the place through
    /// <paramref name="place"/>, speak through <paramref name="announcer"/>, guide on <paramref name="cues"/>, keep time
    /// by <paramref name="milliseconds"/> and write what each press read, as numbers, to <paramref name="log"/> when
    /// there is one (Debug builds).</summary>
    public FieldControls(
        FieldBindings bindings, IFieldReads field, FieldPlace place, IAnnouncer announcer, ICuePlayer cues,
        Func<long> milliseconds, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(milliseconds);
        this.bindings = bindings;
        this.field = field;
        this.place = place;
        this.announcer = announcer;
        this.milliseconds = milliseconds;
        this.log = log;
        beacon = new Beacon(field, cues, announcer, milliseconds);
    }

    private enum Press
    {
        None,
        List,
        Next,
        Previous,
        Place,
        Guide,
    }

    /// <summary>Takes one read of <paramref name="pad"/>'s buttons, made or not (#225), acts on a press, moves the
    /// beacon on, and returns the buttons to hand on: without a chord's D-pad button while it is held in free roam.
    /// </summary>
    public int Pad(int pad, int buttons, bool made)
    {
        if (!made)
        {
            beacon.Tick();
            return buttons;
        }
        var holding = Holds(buttons, bindings.HoldButtons);
        var nextHeld = holding && Holds(buttons, bindings.NextButton);
        var previousHeld = holding && Holds(buttons, bindings.PreviousButton);
        var placeHeld = holding && Holds(buttons, bindings.PlaceButton);
        var guideHeld = holding && Holds(buttons, bindings.GuideButton);
        var roaming = (nextHeld || previousHeld || placeHeld || guideHeld) && field.InFreeRoam();
        Press press;
        lock (gate)
        {
            var nextPressed = next.Update(pad, nextHeld && roaming);
            var previousPressed = previous.Update(pad, previousHeld && roaming);
            var placePressed = where.Update(pad, placeHeld && roaming);
            var guidePressed = guide.Update(pad, guideHeld && roaming);
            press = nextPressed ? Press.Next : previousPressed ? Press.Previous : placePressed ? Press.Place :
                guidePressed ? Press.Guide : Press.None;
        }
        Act(press);
        beacon.Tick();
        if (!roaming)
        {
            return buttons;
        }
        return buttons & ~((nextHeld ? bindings.NextButton : 0) | (previousHeld ? bindings.PreviousButton : 0) |
            (placeHeld ? bindings.PlaceButton : 0) | (guideHeld ? bindings.GuideButton : 0));
    }

    /// <summary>Takes one read of the keys held down, acts on a press, moves the beacon on, and returns the keys to hand
    /// on: without the field's keys while held in free roam.</summary>
    public IReadOnlyList<int> Keys(IReadOnlyList<int> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var listHeld = keys.Contains(bindings.NearbyKey);
        var placeHeld = keys.Contains(bindings.PlaceKey);
        var guideHeld = keys.Contains(bindings.GuideKey);
        var roaming = (listHeld || placeHeld || guideHeld) && field.InFreeRoam();
        Press press;
        lock (gate)
        {
            var listPressed = nearby.Update(KeyboardSource, listHeld && roaming);
            var placePressed = where.Update(KeyboardSource, placeHeld && roaming);
            var guidePressed = guide.Update(KeyboardSource, guideHeld && roaming);
            press = listPressed ? Press.List : placePressed ? Press.Place : guidePressed ? Press.Guide : Press.None;
        }
        Act(press);
        beacon.Tick();
        return roaming
            ? [.. keys.Where(key => key != bindings.NearbyKey && key != bindings.PlaceKey && key != bindings.GuideKey)]
            : keys;
    }

    private static bool Holds(int buttons, int binding) => binding != 0 && (buttons & binding) == binding;

    /// <summary>Reads the game for <paramref name="press"/> and speaks the answer, outside the lock: the announcer may
    /// take its time. A read that throws says nothing, so it never reaches the game.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A failed read must never reach the game; it says nothing.")]
    private void Act(Press press)
    {
        if (press == Press.None)
        {
            return;
        }
        if (press == Press.Guide)
        {
            Guide();
            return;
        }
        string text;
        try
        {
            text = press == Press.Place ? Place() : List(press);
        }
        catch (Exception)
        {
            return;
        }
        announcer.Announce(new Announcement(text, Interrupt: true));
    }

    /// <summary>Stops the beacon when it's on; otherwise starts it toward the thing last stepped to, while the step is
    /// remembered, or else the nearest. A read that throws starts nothing and says nothing.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A failed read must never reach the game; it says nothing.")]
    private void Guide()
    {
        if (beacon.Active)
        {
            beacon.Stop();
            return;
        }
        BeaconTarget? target;
        lock (gate)
        {
            target = position >= 0 && milliseconds() - steppedAt <= StepMemoryMilliseconds ? current : null;
        }
        if (target is null)
        {
            try
            {
                target = Nearest();
            }
            catch (Exception)
            {
                return;
            }
            if (target is null)
            {
                return;
            }
        }
        beacon.Start(target);
    }

    /// <summary>The nearest thing on a fresh list, or null after saying why there's none.</summary>
    private BeaconTarget? Nearest()
    {
        if (field.Read() is not { } view)
        {
            announcer.Announce(new Announcement("Can't read the field", Interrupt: true));
            return null;
        }
        var things = Nearby.List(view);
        if (things.Count == 0)
        {
            announcer.Announce(new Announcement("Nothing nearby", Interrupt: true));
            return null;
        }
        return new BeaconTarget(things[0].Unit, view.DoorReachValue);
    }

    private string Place()
    {
        var map = field.MapId();
        var name = place.Name(map);
        log?.Invoke(string.Create(CultureInfo.InvariantCulture, $"place map={Number(map)} known={name is not null}"));
        return name ?? "Place not known yet";
    }

    private string List(Press press)
    {
        if (field.Read() is not { } view)
        {
            log?.Invoke("nearby unreadable");
            return "Can't read the field";
        }
        var things = Nearby.List(view);
        Log(view, things);
        if (things.Count == 0)
        {
            return "Nothing nearby";
        }
        if (press == Press.List)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{things.Count} nearby. ") +
                string.Join(". ", things.Select(thing => thing.Text()));
        }
        int at;
        lock (gate)
        {
            var now = milliseconds();
            var fresh = position < 0 || now - steppedAt > StepMemoryMilliseconds;
            steppedAt = now;
            position = fresh
                ? (press == Press.Next ? 0 : things.Count - 1)
                : ((press == Press.Next ? position + 1 : position - 1) % things.Count + things.Count) % things.Count;
            at = position;
            current = new BeaconTarget(things[at].Unit, view.DoorReachValue);
        }
        return string.Create(CultureInfo.InvariantCulture, $"{at + 1} of {things.Count}. {things[at].Text()}");
    }

    private void Log(FieldView view, IReadOnlyList<NearbyThing> things)
    {
        if (log is null)
        {
            return;
        }
        var counts = string.Join(',', Enum.GetValues<FieldKind>()
            .Select(kind => string.Create(CultureInfo.InvariantCulture, $"{kind}:{view.Units.Count(unit => unit.Kind == kind)}")));
        log(string.Create(CultureInfo.InvariantCulture,
            $"nearby player=({view.X:0.#},{view.Y:0.#},{view.Z:0.#}) heading={view.Heading:0.###} map={Number(view.MapId)} doorValue={Number(view.DoorReachValue)} units={counts}"));
        foreach (var thing in things)
        {
            var unit = thing.Unit;
            log(string.Create(CultureInfo.InvariantCulture,
                $"nearby kind={unit.Kind} at=({unit.X:0.#},{unit.Y:0.#},{unit.Z:0.#}) distance={thing.Distance:0.#} bearing={thing.Bearing:0.###} reach={thing.InReach} named={unit.Name is not null} {unit.Raw}"));
        }
    }

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    /// <summary>One binding's state across sources: which hold it now.</summary>
    private sealed class Binding
    {
        private readonly HashSet<int> holding = [];

        /// <summary>Records whether <paramref name="source"/> holds the binding, and says whether that made it go from
        /// held nowhere to held somewhere.</summary>
        public bool Update(int source, bool held)
        {
            var before = holding.Count > 0;
            if (held)
            {
                holding.Add(source);
            }
            else
            {
                holding.Remove(source);
            }
            return !before && holding.Count > 0;
        }
    }
}
