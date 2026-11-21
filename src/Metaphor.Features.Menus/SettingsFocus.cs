using Metaphor.Core.Announcements;
using Metaphor.Core.Menus;
using Metaphor.Core.Messages;

namespace Metaphor.Features.Menus;

/// <summary>Speaks the Settings category list and names its pages (#559, parent #96): on opening the list, "Settings"
/// and the focused category, then each category's text from the game's message file and "n of 5"; on opening the System, Sound, Graphics or Display page, that page's
/// category text and the focused row, then on each move the row: its name when <see cref="SettingsPageRows"/> knows it,
/// then its position (#560). Each
/// announcement cuts off the last. The text is looked up at run time (<see cref="MessageLibrary"/>), so nothing here is
/// game content. Safe to call from several threads.</summary>
/// <remarks>Labels, classes and which row opens which page on build 548ddc95:
/// docs/raw-sources/settings-category-labels-session1-2026-11-20.md.</remarks>
public sealed class SettingsFocus : IScreenObserver
{
    // The category list, opened from the title or the field's quick menu, and from the main menu in play: one list.
    private static readonly Screen List = new("Settings", "?$ConfigTopChoicePc@VConfigTopChoicePcLayoutInit@ui@app@@@ope@ui@app");
    private static readonly Screen ListInPlay = new("Settings", "?$ConfigTopChoicePc@VConfigTopChoicePcLayout@ui@app@@@ope@ui@app");

    // The pages the list's first four rows open, in row order.
    private static readonly Screen[] Pages =
    [
        new("System settings", SettingsPages.Classes[0]),
        new("Sound settings", SettingsPages.Classes[1]),
        new("Graphics settings", SettingsPages.Classes[2]),
        new("Display settings", SettingsPages.Classes[3]),
    ];

    // The list's rows in cursor order, and the message labels that name them; a page is named by its row's label.
    private const int CategoryCount = 5;
    private const string CategoryLabel = "System_category_name_PC_0";

    // The list's name, said when it opens. The game draws no heading from its message files there, so the word is ours,
    // English only: the game's own word for the item that opens the list. Approved by the maintainer on 2026-11-20 (#559).
    private const string ListName = "Settings";

    /// <summary>The category list (both its classes) and the four pages, for the menu-screens module.</summary>
    public static IReadOnlyList<Screen> Screens { get; } = [List, ListInPlay, .. Pages];

    private readonly IMessageLookup messages;
    private readonly IAnnouncer announcer;
    private readonly TextCleaner cleaner;
    private readonly IReadOnlyList<SettingsPageRows> rows;

    /// <summary>Speaks the pages' rows known on game build 548ddc95 (<see cref="SettingsPageRows.Steam18330018"/>).
    /// </summary>
    public SettingsFocus(IMessageLookup messages, IAnnouncer announcer, TextCleaner cleaner)
        : this(messages, announcer, cleaner, SettingsPageRows.Steam18330018)
    {
    }

    /// <summary>Speaks the pages' rows as <paramref name="rows"/> names them; any other row by its position.</summary>
    public SettingsFocus(
        IMessageLookup messages, IAnnouncer announcer, TextCleaner cleaner, IReadOnlyList<SettingsPageRows> rows)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(cleaner);
        ArgumentNullException.ThrowIfNull(rows);
        this.messages = messages;
        this.announcer = announcer;
        this.cleaner = cleaner;
        this.rows = rows;
    }

    /// <summary>Speaks the focus when the list or a page is entered or its focus moves.</summary>
    public void Observe(ScreenEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Change == ScreenChange.Left || change.Index < 0 || Said(change) is not { } text)
        {
            return;
        }
        announcer.Announce(new Announcement(text, Interrupt: true));
    }

    private string? Said(ScreenEvent change)
    {
        if (change.Screen == List || change.Screen == ListInPlay)
        {
            if (change.Index >= CategoryCount)
            {
                return null;
            }
            var item = Join(CategoryText(change.Index), $"{change.Index + 1} of {CategoryCount}");
            return change.Change == ScreenChange.Entered ? $"{ListName}, {item}" : item;
        }
        var page = Array.IndexOf(Pages, change.Screen);
        if (page < 0)
        {
            return null;
        }
        var position = change.Total is { } total ? $"{change.Index + 1} of {total}" : $"{change.Index + 1}";
        var row = Join(RowText(page, change.Index, change.Total), position);
        return change.Change == ScreenChange.Entered ? Join(CategoryText(page), row) : row;
    }

    // The row's name, cleaned, or null when the page's rows at this count aren't known, the row's label isn't, or its
    // message is missing or empty. The value isn't read yet (#558's checks 2 and 3), so #96's "name, value" is the name.
    private string? RowText(int page, int index, int? total) =>
        total is { } count && rows.FirstOrDefault(known => known.Page == page && known.Total == count) is { } pageRows
            && pageRows.NameLabel(index) is { } label && messages.Find(label) is { } message
            && cleaner.Clean(message.Text) is { Length: > 0 } text
            ? text
            : null;

    private static string Join(string? name, string position) => name is null ? position : $"{name}, {position}";

    // The category's text, cleaned, or null when the message is missing or empty.
    private string? CategoryText(int index) =>
        messages.Find(CategoryLabel + (index + 1)) is { } message && cleaner.Clean(message.Text) is { Length: > 0 } text
            ? text
            : null;
}
