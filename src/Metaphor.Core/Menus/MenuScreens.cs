using System.Diagnostics.CodeAnalysis;

namespace Metaphor.Core.Menus;

/// <summary>A screen the mod knows (spec #211): its name, and the screen class that identifies it, as the game's
/// run-time type information names it (such as <c>TitleChoice@ope@ui@app</c>). Each screen's feature declares its
/// own. <paramref name="OutsideMenus"/> marks a screen the game also shows while its menus read closed, such as a
/// yes/no dialog in a conversation or New Game's difficulty list (#540): it is told then too, and each new manager
/// enters it afresh, since nothing leaves it there.</summary>
public sealed record Screen(string Name, string ScreenClass, bool OutsideMenus = false);

/// <summary>What happened to a screen.</summary>
public enum ScreenChange
{
    /// <summary>Its cursor settled after another screen's: <see cref="ScreenEvent.Index"/> is the focus.</summary>
    Entered,

    /// <summary>Another screen's cursor settled: <see cref="ScreenEvent.Index"/> is this screen's last focus.</summary>
    Left,

    /// <summary>Its cursor settled on another item: <see cref="ScreenEvent.Index"/> is the new focus.</summary>
    FocusMoved,
}

/// <summary>One change to a screen: which (<paramref name="Screen"/>, null for one the mod doesn't know), its screen
/// class, the focus's position from 0, and the list's item count the game keeps (<see cref="CursorInput.Count"/>), null
/// when unreadable. <paramref name="Mode"/> is which list a choice that serves more than one shows
/// (<see cref="CursorInput.Mode"/>), null when it has none or it is unreadable.</summary>
public sealed record ScreenEvent(
    ScreenChange Change, Screen? Screen, string ScreenClass, int Index, int? Count = null, int? Mode = null)
{
    /// <summary>The longest list taken as a count (#289). The longest seen in session 6 had 145 items
    /// (docs/raw-sources/list-count-session6-2026-11-17.md); a larger value is a screen whose field isn't the count, as
    /// far as we know (<c>[NEEDS VERIFICATION]</c>: whether any list is longer).</summary>
    public const int LongestList = 999;

    /// <summary>The list's total when it is known, holds the focus and is no longer than <see cref="LongestList"/>,
    /// else null.</summary>
    public int? Total => Count is { } count && Index >= 0 && Index < count && count <= LongestList ? count : null;
}

/// <summary>Told of each screen entered, left, and focus moved.</summary>
public interface IScreenObserver
{
    void Observe(ScreenEvent change);
}

/// <summary>A screen's feature that answers the details key (#214).</summary>
public interface IScreenDetails
{
    /// <summary>The rest of the focused item's panel when <paramref name="focus"/> is on this feature's screen, or null
    /// when the screen isn't its own.</summary>
    string? Details(ScreenEvent focus);
}

/// <summary>Turns menu cursor inputs into screen entered, screen left and focus moved (spec #211). The screen is the
/// input's screen class, never its manager: managers are made per visit and one cursor class serves nearly every list
/// (docs/raw-sources/screen-class-session-2026-11-17.md). Only inputs where the cursor settled count (a move can be put
/// back, <see cref="CursorInput.IsMove"/>); an input without a screen class is skipped. Observers hear each change in
/// the order they were added, and one that throws never stops the rest. Add observers before the hooks go live. It also
/// knows whether the game has its menus open (#268), from the reads the input hooks hand it: only while they are open
/// does a cursor count, except on a screen shown outside the menus (<see cref="Screen.OutsideMenus"/>, #540), and when
/// they close, the current screen is left. A screen's mode (<see cref="CursorInput.Mode"/>)
/// changing on the same item is a focus move, so a choice's other list is heard even where the index stays. A screen
/// outside the menus told its manager's same input twice running (so shown again after going quiet) is entered afresh.
/// </summary>
public sealed class MenuScreens : ICursorObserver
{
    private readonly Dictionary<string, Screen> known;
    private readonly Lock gate = new();
    private IScreenObserver[] observers = [];
    private string? currentClass;
    private long currentManager;
    private CursorInput? currentManagerLast;
    private int currentIndex;
    private int? currentCount;
    private int? currentMode;
    private bool menusOpen;

    /// <summary>A module that tags each screen whose class is one of <paramref name="knownScreens"/>' with it.</summary>
    public MenuScreens(IEnumerable<Screen> knownScreens)
    {
        ArgumentNullException.ThrowIfNull(knownScreens);
        known = knownScreens.ToDictionary(screen => screen.ScreenClass, StringComparer.Ordinal);
    }

    /// <summary>Adds an observer of screen changes, after those already added.</summary>
    public void Observe(IScreenObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            observers = [.. observers, observer];
        }
    }

    public void Observe(CursorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var outside = input.ScreenClass is { } named && known.GetValueOrDefault(named)?.OutsideMenus == true;
        ScreenEvent[] changes;
        lock (gate)
        {
            // CursorEvents tells a manager the same input twice running only after it went quiet (#557), as the game
            // calls a shown screen's handler every frame: the screen was closed and shown again.
            var shownAgain = input.Manager == currentManager && input == currentManagerLast;
            if (input.Manager == currentManager)
            {
                currentManagerLast = input;
            }
            if (input.IsMove || input.ScreenClass is not { } screenClass || (!menusOpen && !outside))
            {
                return;
            }
            currentManagerLast = input;
            if (screenClass == currentClass && (!outside || input.Manager == currentManager))
            {
                var focusMoved = input.Index != currentIndex || input.Mode != currentMode;
                // Nothing leaves a screen outside the menus, so one shown again on the same manager and focus is
                // entered afresh, as battle's item list was in the by-ear run of 2026-11-21.
                if (!focusMoved && !(outside && shownAgain))
                {
                    return;
                }
                changes = [Change(focusMoved ? ScreenChange.FocusMoved : ScreenChange.Entered, screenClass, input.Index,
                    input.Count, input.Mode)];
            }
            else
            {
                var entered = Change(ScreenChange.Entered, screenClass, input.Index, input.Count, input.Mode);
                changes = currentClass is { } left
                    ? [Change(ScreenChange.Left, left, currentIndex, currentCount, currentMode), entered]
                    : [entered];
            }
            currentClass = screenClass;
            currentManager = input.Manager;
            currentIndex = input.Index;
            currentCount = input.Count;
            currentMode = input.Mode;
        }
        var each = Volatile.Read(ref observers);
        foreach (var change in changes)
        {
            TellEach(each, change);
        }
    }

    /// <summary>Whether the last read (<see cref="MenusRead"/>) found the game's menus open: false before any read.
    /// </summary>
    public bool MenusOpen => Volatile.Read(ref menusOpen);

    /// <summary>Takes one read of whether the game has its menus open; a read that failed is closed, so menu-only
    /// controls give their input back to the game. When the menus close, the current screen is left at its last focus
    /// and forgotten: nothing repeats it, and the next cursor to settle once they open, even on the same screen, enters
    /// afresh. A cursor that settles while the menus are closed (a dialogue's choice, a battle's commands) is no menu
    /// screen and tells nothing, unless its screen is one shown outside the menus.</summary>
    /// <remarks>What the game's state is, and that it reads open on the title screen too, is in
    /// docs/raw-sources/menus-open-session6-2026-11-17.md.</remarks>
    public void MenusRead(bool open)
    {
        ScreenEvent? left = null;
        lock (gate)
        {
            if (open == menusOpen)
            {
                return;
            }
            Volatile.Write(ref menusOpen, open);
            if (!open && currentClass is { } screenClass)
            {
                left = Change(ScreenChange.Left, screenClass, currentIndex, currentCount, currentMode);
                currentClass = null;
            }
        }
        if (left is not null)
        {
            TellEach(Volatile.Read(ref observers), left);
        }
    }

    /// <summary>Tells every observer the current screen as entered again at its focus, as when the reader is turned
    /// back on (#214); nothing when no screen has been entered.</summary>
    public void RepeatCurrent()
    {
        ScreenEvent change;
        lock (gate)
        {
            if (currentClass is not { } screenClass)
            {
                return;
            }
            change = Change(ScreenChange.Entered, screenClass, currentIndex, currentCount, currentMode);
        }
        TellEach(Volatile.Read(ref observers), change);
    }

    private ScreenEvent Change(ScreenChange change, string screenClass, int index, int? count, int? mode) =>
        new(change, known.GetValueOrDefault(screenClass), screenClass, index, count, mode);

    [SuppressMessage("Design", "CA1031", Justification = "One observer's failure must not keep the change from the others, or reach the game.")]
    private static void TellEach(IScreenObserver[] observers, ScreenEvent change)
    {
        foreach (var observer in observers)
        {
            try
            {
                observer.Observe(change);
            }
            catch (Exception)
            {
            }
        }
    }
}
