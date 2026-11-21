using System.Globalization;
using Metaphor.Core.Announcements;
using Metaphor.Core.Menus;
using Metaphor.Core.Messages;

namespace Metaphor.Features.Menus;

/// <summary>Speaks battle's skill list or item list (#247): on entering it "Skills, name, n of total", then on each focus
/// move "name, n of total", each cutting off the last. A row's name is the message the list fetched by serial for it,
/// from the names file (<see cref="MessageSerials"/>). The names fetched in one run, in the order fetched, are taken as
/// the rows in cursor order, as the quick menu's are (docs/findings/message-serials-from-file-positions.md). A row whose
/// name hasn't been fetched yet is said once it is; one fetched with no known text is said by its position. Safe to call
/// from several threads.</summary>
/// <remarks>
/// <para>On build 548ddc95 the lists are <c>BTL_SkillSelect</c> and <c>BTL_ItemSelect</c>, shown while the menus read
/// closed; they fetch a row's name with flags 9 and its description with flags 7 (#247's by-ear run of 2026-11-20).
/// That the fetch order is the cursor order on these lists is <c>[NEEDS VERIFICATION]</c> by ear.</para>
/// <para>A run starts with a name fetched more than <see cref="RunGapMilliseconds"/> after the list's last focus or
/// fetch, so a list opened again, for another member, starts afresh, even though the game fetches its names before its
/// cursor settles. A fetch soon after a focus move adds to the run, as for rows a scroll brings in; whether the game
/// fetches those, and in what order, is unknown, so a long list may be said by position, or wrongly, past its first
/// rows.</para>
/// </remarks>
public sealed class BattleListFocus : IScreenObserver, ISerialResolveObserver, IScreenDetails
{
    /// <summary>The flags the battle lists fetch a row's name with.</summary>
    public const int NameFlags = 9;

    /// <summary>The flags they fetch the focused row's description with, for its help line.</summary>
    public const int DescriptionFlags = 7;

    /// <summary>How long after the list's last focus or fetch a name fetch starts a new run of rows: many frames, far
    /// less than a turn (<see cref="CursorEvents.QuietMilliseconds"/>).</summary>
    public const long RunGapMilliseconds = CursorEvents.QuietMilliseconds;

    // The most rows kept: the longest list taken as a count (ScreenEvent.LongestList).
    private const int MaxRows = ScreenEvent.LongestList;

    private readonly MessageSerials serials;
    private readonly IAnnouncer announcer;
    private readonly TextCleaner cleaner;
    private readonly string namesFile;
    private readonly string descriptionsFile;
    private readonly Func<long> millisecondsNow;
    private readonly Lock gate = new();
    private readonly List<int> rows = [];
    // Each row's description by its name's serial, and the name fetched last, for the next description to pair with.
    private readonly Dictionary<int, int> descriptions = [];
    private int? lastName;
    private ScreenEvent? waiting;
    private long lastActivity = long.MinValue / 2;
    private bool onList;
    private bool otherScreen;

    // Steps, so a Left can tell whether the rows were fetched for the visit it ends or after it.
    private long step;
    private long runStep;
    private long focusStep;

    private BattleListFocus(
        Screen screen, string namesFile, string descriptionsFile, MessageSerials serials, IAnnouncer announcer,
        TextCleaner cleaner, Func<long> millisecondsNow)
    {
        this.descriptionsFile = descriptionsFile;
        ArgumentNullException.ThrowIfNull(serials);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(cleaner);
        ArgumentNullException.ThrowIfNull(millisecondsNow);
        Screen = screen;
        this.namesFile = namesFile;
        this.serials = serials;
        this.announcer = announcer;
        this.cleaner = cleaner;
        this.millisecondsNow = millisecondsNow;
    }

    /// <summary>Battle's skill list, by its owner's class on build 548ddc95 (#247).</summary>
    public static Screen SkillList { get; } = new("Skills", "BTL_SkillSelect@ope@ui@app", OutsideMenus: true);

    /// <summary>Battle's item list, by its owner's class on build 548ddc95 (#247).</summary>
    public static Screen ItemList { get; } = new("Items", "BTL_ItemSelect@ope@ui@app", OutsideMenus: true);

    /// <summary>The screen this one speaks.</summary>
    public Screen Screen { get; }

    /// <summary>The skill list, its names from the skill names file, timed by <paramref name="millisecondsNow"/>.
    /// </summary>
    public static BattleListFocus Skills(
        MessageSerials serials, IAnnouncer announcer, TextCleaner cleaner, Func<long> millisecondsNow) =>
        new(SkillList, MessageSerials.SkillNames, MessageSerials.SkillDescriptions, serials, announcer, cleaner,
            millisecondsNow);

    /// <summary>The item list, its names from the item names file, timed by <paramref name="millisecondsNow"/>.
    /// </summary>
    public static BattleListFocus Items(
        MessageSerials serials, IAnnouncer announcer, TextCleaner cleaner, Func<long> millisecondsNow) =>
        new(ItemList, MessageSerials.ItemNames, MessageSerials.ItemDescriptions, serials, announcer, cleaner,
            millisecondsNow);

    /// <summary>The focused row's description for the details key (#247): the description fetched from the list's
    /// descriptions file right after the row's name. Null on another screen or for a row with none.</summary>
    /// <remarks>On build 548ddc95 the item list, opening, fetched each row's name then its description, row by row
    /// (name 5143, description 5916, name 5144, description 5917: the maintainer's by-ear run of 2026-11-21). That the
    /// skill list fetches them in the same order is <c>[NEEDS VERIFICATION]</c>; its names and descriptions don't sit
    /// at the same file positions (docs/findings/item-skill-message-serials-static.md), so positions can't pair them.
    /// The skill's cost is not read.</remarks>
    public string? Details(ScreenEvent focus)
    {
        ArgumentNullException.ThrowIfNull(focus);
        int? serial = null;
        lock (gate)
        {
            if (focus.Screen == Screen && onList && focus.Index >= 0 && focus.Index < rows.Count
                && descriptions.TryGetValue(rows[focus.Index], out var found))
            {
                serial = found;
            }
        }
        return serial is { } known ? serials.TextOf(known, cleaner) : null;
    }

    /// <summary>Speaks the focus when the list is entered or its focus moves, or waits for its row's name. Leaving the
    /// list forgets the rows its visit used; entering another screen forgets them all.</summary>
    public void Observe(ScreenEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Announcement? said;
        lock (gate)
        {
            if (change.Screen != Screen)
            {
                // While another screen is current (a field menu, the title), its fetches are none of this list's. The
                // other battle list is the exception: backing out of it to battle's commands tells nothing, as they are
                // no screen, so it stays current until this list's cursor settles, after this list fetched its names
                // (by-ear run of 2026-11-21). Its own fetches are from other files, which this list ignores anyway.
                if (change.Change == ScreenChange.Entered)
                {
                    Forget();
                    onList = false;
                    var otherList = change.Screen == SkillList || change.Screen == ItemList;
                    Volatile.Write(ref otherScreen, !otherList);
                }
                else if (change.Change == ScreenChange.Left)
                {
                    Volatile.Write(ref otherScreen, false);
                }
                return;
            }
            Volatile.Write(ref otherScreen, false);
            waiting = null;
            if (change.Change == ScreenChange.Left)
            {
                onList = false;
                // Rows fetched after this visit's last focus belong to the next visit, which fetched them already.
                if (runStep < focusStep)
                {
                    Forget();
                }
                return;
            }
            var now = millisecondsNow();
            // The list fetches its names just before its cursor settles: rows fetched long before it opened came from
            // something else. Entered again while on the list (the reader turned back on) keeps them.
            if (change.Change == ScreenChange.Entered && !onList && now - lastActivity > RunGapMilliseconds)
            {
                Forget();
            }
            onList = true;
            lastActivity = now;
            focusStep = ++step;
            said = LineFor(change);
            if (said is null && change.Index >= 0)
            {
                waiting = change;
            }
        }
        if (said is not null)
        {
            announcer.Announce(said);
        }
    }

    /// <summary>Whether this list wants the game's fetches: not while another screen is current, so the hook can skip
    /// its reads then.</summary>
    public bool Listening => !Volatile.Read(ref otherScreen);

    /// <summary>Keeps each row name the game fetches from the names file, in order, and speaks a focus that was waiting
    /// for it. Ignored while another screen is current.</summary>
    public void Observe(SerialResolved resolve)
    {
        if (Volatile.Read(ref otherScreen))
        {
            return;
        }
        if (resolve.Flags == DescriptionFlags && serials.InFile(resolve.Serial, descriptionsFile))
        {
            lock (gate)
            {
                // A description is the row's whose name was fetched just before it; one after no name (the help line
                // fetched again) changes nothing.
                if (lastName is { } name)
                {
                    descriptions[name] = resolve.Serial;
                    lastName = null;
                }
            }
            return;
        }
        if (resolve.Flags != NameFlags || !serials.InFile(resolve.Serial, namesFile))
        {
            return;
        }
        Announcement? said = null;
        lock (gate)
        {
            var now = millisecondsNow();
            if (rows.Count == 0 || now - lastActivity > RunGapMilliseconds)
            {
                rows.Clear();
                descriptions.Clear();
                runStep = ++step;
            }
            lastActivity = now;
            if (rows.Count >= MaxRows || rows.Contains(resolve.Serial))
            {
                lastName = rows.Contains(resolve.Serial) ? resolve.Serial : null;
                return;
            }
            rows.Add(resolve.Serial);
            lastName = resolve.Serial;
            if (waiting is { } focus && (said = LineFor(focus)) is not null)
            {
                waiting = null;
                focusStep = ++step;
            }
        }
        if (said is not null)
        {
            announcer.Announce(said);
        }
    }

    // Called under the gate.
    private void Forget()
    {
        rows.Clear();
        descriptions.Clear();
        lastName = null;
        waiting = null;
    }

    // The line for a focus whose row's name has been fetched, or null while it hasn't. Called under the gate.
    private Announcement? LineFor(ScreenEvent focus)
    {
        if (focus.Index < 0 || focus.Index >= rows.Count)
        {
            return null;
        }
        var position = focus.Total is { } total
            ? string.Create(CultureInfo.InvariantCulture, $"{focus.Index + 1} of {total}")
            : string.Create(CultureInfo.InvariantCulture, $"{focus.Index + 1}");
        var item = serials.TextOf(rows[focus.Index], cleaner) is { } name ? $"{name}, {position}" : position;
        return new Announcement(focus.Change == ScreenChange.Entered ? $"{Screen.Name}, {item}" : item, Interrupt: true);
    }
}
