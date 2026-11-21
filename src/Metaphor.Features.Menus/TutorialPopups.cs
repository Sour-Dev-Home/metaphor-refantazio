using System.Globalization;
using Metaphor.Core;
using Metaphor.Core.Announcements;
using Metaphor.Core.Messages;

namespace Metaphor.Features.Menus;

/// <summary>Speaks a tutorial pop-up as it opens (#118): "title, n pages", then its first page's text, cutting off what
/// was being said; then each page it turns to, "Page n of m. text". The title and pages are the messages the game's tutorial table names for the pop-up's tutorial
/// number (<see cref="TutorialTable"/>), found by label hash in the files the game compiled. A title that isn't
/// compiled is said as "Tutorial", and so is a pop-up whose number the table hasn't, with no page count. Safe to call
/// from several threads.</summary>
/// <remarks>A pop-up is told apart by its window's own fields: the open writes the tutorial number to the window and
/// builds the object it keeps for it (docs/findings/tutorial-popup-window-static.md). A window whose object is gone or
/// whose state says ended is closed, so the next object it holds is a new pop-up. That those fields mean this is a lead
/// from the exe and one run (#594) <c>[NEEDS VERIFICATION]</c>. The page shown is the object's own field, which
/// stepped 0, 1, 2, 1, 0 with the page turns of a by-ear run (docs/raw-sources/tutorial-page-index-by-ear-2026-11-21.md).
/// </remarks>
public sealed class TutorialPopups
{
    /// <summary>What a pop-up is said as when its title can't be found.</summary>
    public const string Untitled = "Tutorial";

    private readonly IMessageLookup messages;
    private readonly IAnnouncer announcer;
    private readonly TextCleaner cleaner;
    private readonly Func<TutorialTable?> table;
    // The game has two pop-up windows (#118); more than this many remembered means stale ones, so all are forgotten.
    private const int MaxWindows = 8;

    /// <summary>How long a window that hasn't updated still counts as on screen. A window can stop updating without
    /// saying it ended (the party-join one isn't followed at all), and the per-frame update runs many times inside
    /// this.</summary>
    public const long StaleMilliseconds = 1_000;

    private readonly Lock gate = new();
    private readonly Func<long> clock;
    // Each window's pop-up as last spoken: its number, its object, its page, what was said for it, when it was said
    // and when the window last updated.
    private readonly Dictionary<long, Shown> shown = [];
    private long spoken;

    /// <param name="messages">The compiled messages, by label hash.</param>
    /// <param name="announcer">Where the pop-up is spoken.</param>
    /// <param name="cleaner">Turns a message's text into speech.</param>
    /// <param name="table">The tutorial table, or null while it loads or when it couldn't be read.</param>
    /// <param name="clock">Milliseconds, for <see cref="StaleMilliseconds"/>; the system's tick count when null.</param>
    public TutorialPopups(
        IMessageLookup messages, IAnnouncer announcer, TextCleaner cleaner, Func<TutorialTable?> table, Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(cleaner);
        ArgumentNullException.ThrowIfNull(table);
        this.messages = messages;
        this.announcer = announcer;
        this.cleaner = cleaner;
        this.table = table;
        this.clock = clock ?? (() => Environment.TickCount64);
    }

    /// <summary>What was last said for the pop-up on screen, for the repeat binding to say again (by-ear finding,
    /// 2026-11-21): the open's "title, n pages. text" or the last page turned to. Null while no window holding a
    /// pop-up has updated within <see cref="StaleMilliseconds"/>. With two up, the one spoken last.</summary>
    public string? OnScreen
    {
        get
        {
            var now = clock();
            lock (gate)
            {
                string? said = null;
                var latest = long.MinValue;
                foreach (var popUp in shown.Values)
                {
                    if (popUp.Said is not null && now - popUp.Updated < StaleMilliseconds && popUp.Spoken > latest)
                    {
                        said = popUp.Said;
                        latest = popUp.Spoken;
                    }
                }
                return said;
            }
        }
    }

    /// <summary>The pop-up window <paramref name="window"/> after one of its updates or opens: the tutorial number it holds, the
    /// object it keeps for that tutorial (0 for none) and whether its state says it has ended. A window holding a new
    /// object, or a new number, is a pop-up that just opened, and is spoken once with the page it shows. The same pop-up
    /// on another <paramref name="page"/> (counted from 0; null when it couldn't be read) has turned a page, which is
    /// spoken as "Page n of m. text".</summary>
    public void WindowUpdated(long window, int number, long model, int? page, bool ended)
    {
        string? said;
        lock (gate)
        {
            if (ended || model == 0)
            {
                shown.Remove(window);
                return;
            }
            var now = clock();
            var known = shown.TryGetValue(window, out var last) && last.Number == number && last.Model == model;
            if (known && (page is null || page == last.Page))
            {
                shown[window] = last with { Updated = now };
                return;
            }
            if (!known && shown.Count >= MaxWindows)
            {
                shown.Clear();
            }
            var entry = table()?.Entry(number);
            said = !known
                ? Opened(entry, page ?? 0)
                : entry is not null && page is { } turned && turned >= 0 && turned < entry.PageHashes.Count
                    ? Turned(entry, turned)
                    : null;
            shown[window] = said is null
                ? last with { Page = page, Updated = now }
                : new Shown(number, model, page, said, ++spoken, now);
        }
        // Spoken outside the lock: the announcer may take its time.
        if (said is not null)
        {
            announcer.Announce(new Announcement(said, Interrupt: true));
        }
    }

    private readonly record struct Shown(int Number, long Model, int? Page, string? Said, long Spoken, long Updated);

    private string Opened(TutorialEntry? entry, int page)
    {
        if (entry is null)
        {
            return Untitled;
        }
        var title = Text(entry.TitleHash) ?? Untitled;
        var pages = entry.PageHashes.Count;
        var said = string.Create(CultureInfo.InvariantCulture, $"{title}, {pages} {(pages == 1 ? "page" : "pages")}");
        return page >= 0 && page < pages && Text(entry.PageHashes[page]) is { } shown ? $"{said}. {shown}" : said;
    }

    private string Turned(TutorialEntry entry, int page)
    {
        var said = string.Create(CultureInfo.InvariantCulture, $"Page {page + 1} of {entry.PageHashes.Count}");
        return Text(entry.PageHashes[page]) is { } shown ? $"{said}. {shown}" : said;
    }

    private string? Text(string labelHash) =>
        messages.FindByLabelHash(labelHash) is { } message && cleaner.Clean(message.Text) is { Length: > 0 } text
            ? text
            : null;
}
