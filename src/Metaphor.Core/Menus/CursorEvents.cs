using System.Diagnostics.CodeAnalysis;

namespace Metaphor.Core.Menus;

/// <summary>One input a menu's cursor manager handled (#94): the manager, its class (its vtable as a module offset, null
/// when unreadable), the command and what the handler returned, the cursor after it ran (<c>top</c> plus <c>row</c>) and
/// the manager's flags. Fields, command range and build: docs/raw-sources/ui-focus-candidates-2026-11-16.md. Numbers
/// only, no game text. <paramref name="ScreenClass"/> is the screen class of the choice that owns the manager, as its
/// run-time type information names it (<c>TitleChoice@ope@ui@app</c>), or null when no owner was seen or its class is
/// unreadable (docs/raw-sources/screen-class-session-2026-11-17.md). <paramref name="Count"/> is the list's item count the
/// manager keeps, read with this input since a manager can be reused by another screen, or null when unreadable
/// (docs/raw-sources/list-count-session6-2026-11-17.md). <paramref name="Mode"/> is the owner's own mode field, for a
/// choice that serves more than one list (<see cref="ScreenModes"/>), or null when it has none or it is unreadable.
/// </summary>
public sealed record CursorInput(
    long Manager, long? ClassRva, int Command, int Result, int Top, int Row, int Flags, string? ScreenClass = null,
    int? Count = null, int? Mode = null)
{
    /// <summary>The cursor's position: what the class's index getter returns.</summary>
    public int Index => Top + Row;

    /// <summary>Whether this is one of the four commands the handler moves the cursor for (<c>0x14</c>–<c>0x17</c>). A
    /// move can be refused and put back on the next call (docs/raw-sources/title-menu-labels-2026-11-16.md), so where
    /// the cursor settled is the next input that isn't a move.</summary>
    public bool IsMove => Command is >= 0x14 and <= 0x17;
}

/// <summary>Told of each menu cursor input that changed something.</summary>
public interface ICursorObserver
{
    void Observe(CursorInput input);
}

/// <summary>Hands each input the menu cursor hook decoded to its observers (#94), as <see cref="Messages.MessageEvents"/>
/// does for messages. The game calls the handler on every frame a menu is open, so an input equal to that manager's
/// last one is dropped, unless the manager went uncalled for <see cref="QuietMilliseconds"/> or more: then its screen is
/// back from behind another, and the input is told again so the return is heard (#557). Observers hear each input in
/// the order they were added, and one that throws never stops the rest. Add observers before the hooks go live; telling
/// them is safe from any game thread.</summary>
/// <remarks>That a screen's handler runs every frame, settled or not, is our reading of the cursor settling one frame
/// after each move (docs/raw-sources/title-menu-labels-2026-11-16.md); that it isn't called while another screen
/// covers it is <c>[NEEDS VERIFICATION]</c>: the title manager logged none of the four moves made in Settings
/// (docs/raw-sources/menu-return-session-2026-11-20.md, game build 548ddc95…), but a hidden handler called every frame
/// with the same input would leave the same log. If it is called, nothing is told again and nothing changes.</remarks>
public sealed class CursorEvents
{
    /// <summary>How long a manager goes uncalled before its same input counts as a return: many frames at any playable
    /// frame rate, far less than any visit to another screen.</summary>
    public const long QuietMilliseconds = 250;

    // Managers are game objects that come and go; the last inputs of this many are kept before starting afresh.
    private const int ManagersRemembered = 64;

    private readonly Lock gate = new();
    private readonly Dictionary<long, (CursorInput Input, long CalledAt)> lastByManager = [];
    private readonly Dictionary<string, long> lastByClass = new(StringComparer.Ordinal);
    private readonly Func<long> millisecondsNow;
    private ICursorObserver[] observers = [];

    /// <summary>Events timed by the system's millisecond tick.</summary>
    public CursorEvents()
        : this(() => Environment.TickCount64)
    {
    }

    /// <summary>Events timed by <paramref name="millisecondsNow"/>, a clock in milliseconds that never goes back.
    /// </summary>
    public CursorEvents(Func<long> millisecondsNow)
    {
        ArgumentNullException.ThrowIfNull(millisecondsNow);
        this.millisecondsNow = millisecondsNow;
    }

    /// <summary>Adds an observer of cursor inputs, after those already added.</summary>
    public void Observe(ICursorObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            observers = [.. observers, observer];
        }
    }

    /// <summary>Tells every observer that a manager handled <paramref name="input"/>, unless it equals that manager's
    /// last input and the manager was called less than <see cref="QuietMilliseconds"/> ago.</summary>
    public void Handled(CursorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = millisecondsNow();
        lock (gate)
        {
            var known = lastByManager.TryGetValue(input.Manager, out var last);
            if (!known && lastByManager.Count >= ManagersRemembered)
            {
                lastByManager.Clear();
            }
            lastByManager[input.Manager] = (input, now);
            if (input.ScreenClass is { } screenClass)
            {
                if (!lastByClass.ContainsKey(screenClass) && lastByClass.Count >= ManagersRemembered)
                {
                    lastByClass.Clear();
                }
                lastByClass[screenClass] = now;
            }
            if (known && last.Input == input && now - last.CalledAt < QuietMilliseconds)
            {
                return;
            }
        }
        TellEach(Volatile.Read(ref observers), input);
    }

    /// <summary>Whether a cursor of <paramref name="screenClass"/> was called less than
    /// <see cref="QuietMilliseconds"/> ago, repeats included: the game calls a shown screen's handler every frame, so a
    /// screen that went quiet has closed, as far as we know (#247).</summary>
    public bool CalledRecently(string screenClass)
    {
        ArgumentNullException.ThrowIfNull(screenClass);
        var now = millisecondsNow();
        lock (gate)
        {
            return lastByClass.TryGetValue(screenClass, out var calledAt) && now - calledAt < QuietMilliseconds;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "One observer's failure must not keep the input from the others, or reach the game.")]
    private static void TellEach(ICursorObserver[] observers, CursorInput input)
    {
        foreach (var observer in observers)
        {
            try
            {
                observer.Observe(input);
            }
            catch (Exception)
            {
            }
        }
    }
}
