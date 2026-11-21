using System.Diagnostics.CodeAnalysis;
using Metaphor.Core.Announcements;
using Metaphor.Core.Input;
using Metaphor.Core.Menus;

namespace Metaphor.Features.Menus;

/// <summary>Which button and key the menu controls (#214) use. Buttons are bits of the game's own 16-bit pad mask and a
/// binding of several bits means all of them held together; keys are Windows virtual-key codes.</summary>
/// <param name="DetailsButtons">The buttons that read the focus's details.</param>
/// <param name="DetailsKey">The key that reads the focus's details.</param>
public sealed record MenuBindings(int DetailsButtons, int DetailsKey)
{
    /// <summary>The buttons that turn the reader on or off: the dialogue feature's binding, which the wiring hands in.
    /// None by default.</summary>
    public int ReaderButtons { get; init; }

    /// <summary>The key that turns the reader on or off, as <see cref="ReaderButtons"/>.</summary>
    public int ReaderKey { get; init; }

    /// <summary>R3 and F, the maintainer's choices: R3 on #214, as it did nothing on the main menu in session 4
    /// (docs/raw-sources/screen-class-session-2026-11-17.md); F on 2026-11-20, replacing Tab, which is the game's
    /// exit-game key on PC (maintainer, by ear). The game's mask puts XInput's right thumb at <c>0x0004</c> on game build
    /// 548ddc95… (Steam build 18330018), and its keyboard read skips codes only by two bit tables, below 32 and from
    /// <c>0xA6</c>, so it lists F (<c>0x46</c>) when held: docs/raw-sources/pad-and-keyboard-state-2026-11-16.md.
    /// Constants until #104's settings file can hold them.
    /// </summary>
    public static MenuBindings Default { get; } = new(0x0004, 0x46);
}

/// <summary>The menu controls (#214), while a menu is open: the details key asks the current screen's feature
/// (<see cref="IScreenDetails"/>) for the rest of the focus's panel, or says there are no more details, and the reader
/// binding turns the reader on or off, speaking the current screen and focus again when it comes on. Both are hidden from
/// the game then, and the game sees them as usual otherwise. A menu is open while the menu-screens module reads the
/// game's menus as open (#268) and has a screen entered and not left, by its own events (judgement rule 5), or while a
/// screen shown outside the menus, such as battle's skill list (#247), is entered and its cursor still called; so
/// elsewhere in the field, a battle or dialogue, R3, F and the reader binding reach the game. A press is an up-to-down edge of a binding held on any pad or the keyboard, as for the
/// dialogue controls, so a mirroring second pad can't fire it again. Menu speech goes through the reader, so it's
/// silent while the reader is off. The input frame (#301) hands it each pad and keyboard read from any of the game's
/// threads.</summary>
public sealed class MenuControls : IScreenObserver, IInputControls
{
    private const int KeyboardSource = -1;

    private readonly Lock gate = new();
    private readonly MenuBindings bindings;
    private readonly MenuScreens screens;
    private readonly IReaderSwitch reader;
    private readonly IScreenDetails[] details;
    private readonly Binding detailsBinding = new();
    private readonly Binding toggleBinding = new();
    private ScreenEvent? focus;

    private readonly Func<string, bool> shown;

    public MenuControls(
        MenuBindings bindings, MenuScreens screens, IReaderSwitch reader, IEnumerable<IScreenDetails> details)
        : this(bindings, screens, reader, details, _ => true)
    {
    }

    /// <summary>Controls that also answer on a screen shown outside the menus (<see cref="Screen.OutsideMenus"/>, #247),
    /// such as battle's skill list, but only while <paramref name="shown"/> says its screen class is on screen: its
    /// cursor still being called (<see cref="CursorEvents.CalledRecently"/>).</summary>
    public MenuControls(
        MenuBindings bindings, MenuScreens screens, IReaderSwitch reader, IEnumerable<IScreenDetails> details,
        Func<string, bool> shown)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(shown);
        this.bindings = bindings;
        this.screens = screens;
        this.reader = reader;
        this.details = [.. details];
        this.shown = shown;
    }

    /// <summary>Keeps the open screen and its focus: set when a screen is entered or its focus moves while the menus are
    /// open, cleared when it's left or a change comes while they are closed, so no focus outlives a menu visit.</summary>
    public void Observe(ScreenEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (gate)
        {
            if (!screens.MenusOpen && change.Screen?.OutsideMenus != true)
            {
                focus = null;
            }
            else if (change.Change != ScreenChange.Left)
            {
                focus = change;
            }
            else if (focus?.ScreenClass == change.ScreenClass)
            {
                focus = null;
            }
        }
    }

    /// <summary>Takes one read of <paramref name="pad"/>'s buttons, acts on a press, and returns the buttons to hand
    /// on: without the bound ones while a menu is open.</summary>
    public int Pad(int pad, int buttons) => Pad(pad, buttons, made: true);

    /// <summary>Takes one read of <paramref name="pad"/>'s buttons that the game reports as made or not (#225). A read
    /// not made is ignored, so leftover bits in it can't fire a binding, and its buttons are handed back
    /// unchanged.</summary>
    public int Pad(int pad, int buttons, bool made)
    {
        if (!made)
        {
            return buttons;
        }
        var detailsHeld = Holds(buttons, bindings.DetailsButtons);
        var toggleHeld = Holds(buttons, bindings.ReaderButtons);
        var hidden = Read(pad, detailsHeld, toggleHeld);
        // Only a binding held in full is hidden: part of a chord is an ordinary press to the game.
        return hidden
            ? buttons & ~((detailsHeld ? bindings.DetailsButtons : 0) | (toggleHeld ? bindings.ReaderButtons : 0))
            : buttons;
    }

    /// <summary>Takes one read of the keys held down, acts on a press, and returns the keys to hand on: without the
    /// bound ones while a menu is open.</summary>
    public IReadOnlyList<int> Keys(IReadOnlyList<int> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var hidden = Read(KeyboardSource, keys.Contains(bindings.DetailsKey), bindings.ReaderKey != 0 && keys.Contains(bindings.ReaderKey));
        return hidden ? [.. keys.Where(key => key != bindings.DetailsKey && key != bindings.ReaderKey)] : keys;
    }

    private static bool Holds(int buttons, int binding) => binding != 0 && (buttons & binding) == binding;

    /// <summary>Records what <paramref name="source"/> holds, acts on each press, and says whether a menu is open, so
    /// the bound input is hidden.</summary>
    private bool Read(int source, bool detailsHeld, bool toggleHeld)
    {
        bool detailsPressed;
        bool togglePressed;
        ScreenEvent? current;
        lock (gate)
        {
            // A screen shown outside the menus counts while its cursor is still called, so the keys reach the game once
            // it closes, though nothing leaves it (a battle list closes into the command panel, which has no cursor).
            current = focus is { } held && (held.Screen?.OutsideMenus == true ? shown(held.ScreenClass) : screens.MenusOpen)
                ? held
                : null;
            detailsPressed = detailsBinding.Update(source, detailsHeld) && current is not null;
            togglePressed = toggleBinding.Update(source, toggleHeld) && current is not null;
        }
        var open = current is not null;
        // Spoken outside the lock: the announcer may take its time.
        if (detailsPressed)
        {
            var text = details.Select(feature => DetailsOf(feature, current!)).FirstOrDefault(said => said is not null);
            reader.Announce(new Announcement(text ?? "No more details", Interrupt: true));
        }
        if (togglePressed)
        {
            reader.Toggle();
            if (reader.IsOn)
            {
                screens.RepeatCurrent();
            }
        }
        return open;
    }

    /// <summary>What <paramref name="feature"/> says of <paramref name="focus"/>, or null when it throws, so one
    /// feature's failure leaves the details to the rest, as <see cref="MenuScreens"/> isolates its observers.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "One feature's failure must not keep the details from the others, or reach the game.")]
    private static string? DetailsOf(IScreenDetails feature, ScreenEvent focus)
    {
        try
        {
            return feature.Details(focus);
        }
        catch (Exception)
        {
            return null;
        }
    }

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
