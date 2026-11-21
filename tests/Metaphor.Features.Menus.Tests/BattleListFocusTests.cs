using Metaphor.Core.Announcements;
using Metaphor.Core.Menus;
using Metaphor.Core.Messages;

namespace Metaphor.Features.Menus.Tests;

/// <summary>Battle's skill and item lists (#247): each row the cursor settles on is said by the name the list fetched
/// for it by serial, in the order it fetched them, and "n of total", while the menus read closed. Hook events in,
/// announcements out; made-up file text and serials only.</summary>
[Trait("Category", "Unit")]
public sealed class BattleListFocusTests
{
    private const string SkillClass = "BTL_SkillSelect@ope@ui@app";
    private const string ItemClass = "BTL_ItemSelect@ope@ui@app";
    private const string DifficultyClass = "UUIC_Common_DifficultySelect@ui@app";
    private const int Settle = 0x1;
    private const int SkillFirst = 700;
    private const int ItemFirst = 300;
    private const int DescriptionFlags = BattleListFocus.DescriptionFlags;
    private const int SkillDescriptionFirst = 900;
    private const int ItemDescriptionFirst = 400;
    private const long Later = BattleListFocus.RunGapMilliseconds + 1;

    private readonly RecordingAnnouncer announcer = new();
    private readonly MessageEvents messages = new();
    private readonly CursorEvents cursors;
    private readonly MenuScreens screens = new(KnownScreens.All);
    private readonly BattleListFocus skills;
    private readonly BattleListFocus items;
    private long now = 50_000;

    public BattleListFocusTests()
    {
        cursors = new CursorEvents(() => now);
        var serials = new MessageSerials([
            new SerialRange(MessageSerials.SkillNames, SkillFirst, 3),
            new SerialRange(MessageSerials.ItemNames, ItemFirst, 2),
            new SerialRange(MessageSerials.SkillDescriptions, SkillDescriptionFirst, 2),
            new SerialRange(MessageSerials.ItemDescriptions, ItemDescriptionFirst, 2),
        ]);
        var cleaner = new TextCleaner(TextCleanerRules.None);
        messages.ObserveFiles(serials);
        // A battle: the menus read closed.
        screens.MenusRead(open: false);
        cursors.Observe(screens);
        skills = BattleListFocus.Skills(serials, announcer, cleaner, () => now);
        items = BattleListFocus.Items(serials, announcer, cleaner, () => now);
        screens.Observe(skills);
        screens.Observe(items);
        screens.Observe(new UnknownScreenFocus(announcer));
        messages.ObserveSerials(skills);
        messages.ObserveSerials(items);
        messages.Compiled(new MessageFileCompiled("EN/message/system/Skill_Message_1.msg", 20,
            Source("Skill", "Spark", "Gust", "Frost <COLOR 3>")));
        messages.Compiled(new MessageFileCompiled("EN/message/system/Item_Message_1.msg", 21,
            Source("Item", "Tonic", "Salve")));
        messages.Compiled(new MessageFileCompiled("EN/message/system/Skill_Message_2.msg", 22,
            Source("SkillHelp", "Burns one foe", "Cuts all foes")));
        messages.Compiled(new MessageFileCompiled("EN/message/system/Item_Message_2.msg", 23,
            Source("ItemText", "Heals a little", "Heals more")));
    }

    private void Described(params int[] serials)
    {
        foreach (var serial in serials)
        {
            messages.Resolved(new SerialResolved(serial, DescriptionFlags));
        }
    }

    private static string Source(string prefix, params string[] texts) => string.Join("\n\n",
        texts.Select((text, at) => $"//--------\n@{prefix}_{at:000}\n{{\n{text}\n<WAIT>\n}}"));

    private void Cursor(string screenClass, int index, int? count, long manager = 0xa0) =>
        cursors.Handled(new CursorInput(manager, ClassRva: 0x100, Settle, Result: 1, Top: index, Row: 0, Flags: 0x46,
            screenClass, count));

    private void Fetched(params int[] serials)
    {
        foreach (var serial in serials)
        {
            messages.Resolved(new SerialResolved(serial, BattleListFocus.NameFlags));
        }
    }

    private IEnumerable<string> Said() => announcer.Said.Select(said => said.Text);

    [Fact]
    public void TheSkillListIsHeadedThenEachRowIsSaidByTheNameFetchedForIt()
    {
        // The list fetches its rows' names as it opens, here not in file order.
        Fetched(SkillFirst + 2, SkillFirst, SkillFirst + 1);

        Cursor(SkillClass, 0, count: 3);
        Cursor(SkillClass, 1, count: 3);
        Cursor(SkillClass, 2, count: 3);

        Assert.Equal(["Skills, Frost, 1 of 3", "Spark, 2 of 3", "Gust, 3 of 3"], Said());
        Assert.All(announcer.Said, said => Assert.True(said.Interrupt));
    }

    [Fact]
    public void TheItemListIsHeadedItsOwnWay()
    {
        Fetched(ItemFirst + 1);

        Cursor(ItemClass, 0, count: 1);

        Assert.Equal(["Items, Salve, 1 of 1"], Said());
    }

    [Fact]
    public void AnUnreadableCountLeavesThePositionAlone()
    {
        Fetched(SkillFirst);

        Cursor(SkillClass, 0, count: null);

        Assert.Equal(["Skills, Spark, 1"], Said());
    }

    // The cursor can settle before the row's name is fetched: the row is said once it is.
    [Fact]
    public void ARowWhoseNameIsNotFetchedYetIsSaidWhenItIs()
    {
        Cursor(SkillClass, 0, count: 2);
        Assert.Empty(Said());

        Fetched(SkillFirst + 1);

        Assert.Equal(["Skills, Gust, 1 of 2"], Said());
    }

    [Fact]
    public void ARowIsSaidOnceEvenWhenItsNameIsFetchedAgain()
    {
        Cursor(SkillClass, 0, count: 2);
        Fetched(SkillFirst, SkillFirst, SkillFirst + 1);

        Assert.Equal(["Skills, Spark, 1 of 2"], Said());
    }

    // A row a focus move brings in, fetched soon after it, adds to the rows.
    [Fact]
    public void ANameFetchedSoonAfterAFocusMoveAddsARow()
    {
        Fetched(SkillFirst);
        Cursor(SkillClass, 0, count: 2);
        now += Later;
        Cursor(SkillClass, 1, count: 2);
        Fetched(SkillFirst + 2);

        Assert.Equal(["Skills, Spark, 1 of 2", "Frost, 2 of 2"], Said());
    }

    // A description, another file's name, or a name fetched with other flags is no row.
    [Fact]
    public void OnlyTheListsOwnNamesFetchedWithTheNameFlagsAreRows()
    {
        messages.Resolved(new SerialResolved(SkillFirst, DescriptionFlags));
        Fetched(ItemFirst, SkillFirst + 2);

        Cursor(SkillClass, 0, count: 1);

        Assert.Equal(["Skills, Frost, 1 of 1"], Said());
    }

    // The next member's list fetches its names before its cursor settles, and its cursor is a new manager.
    [Fact]
    public void TheListOpenedAgainSaysTheNamesItFetchedNotTheLastVisits()
    {
        Fetched(SkillFirst, SkillFirst + 1);
        Cursor(SkillClass, 0, count: 2);
        now += 5_000;
        Fetched(SkillFirst + 2);
        Cursor(SkillClass, 0, count: 1, manager: 0xc0);

        Assert.Equal(["Skills, Spark, 1 of 2", "Skills, Frost, 1 of 1"], Said());
    }

    [Fact]
    public void TheListOpenedAgainWaitsForItsOwnNamesWhenItsCursorComesFirst()
    {
        Fetched(SkillFirst);
        Cursor(SkillClass, 0, count: 1);
        now += 5_000;
        Cursor(SkillClass, 0, count: 1, manager: 0xc0);
        Fetched(SkillFirst + 1);

        Assert.Equal(["Skills, Spark, 1 of 1", "Skills, Gust, 1 of 1"], Said());
    }

    [Fact]
    public void EnteringAnotherScreenForgetsTheRows()
    {
        Fetched(SkillFirst);
        Cursor(DifficultyClass, 0, count: 5, manager: 0xb0);
        Cursor(SkillClass, 0, count: 1, manager: 0xc0);

        Assert.DoesNotContain(Said(), said => said.Contains("Spark", StringComparison.Ordinal));
    }

    // A field menu fetches item names while it is current; they are no battle list's rows, and the list stops
    // listening then, so the hook can skip its reads.
    [Fact]
    public void NamesFetchedWhileAnotherScreenIsCurrentAreNoRows()
    {
        Cursor(DifficultyClass, 0, count: 5, manager: 0xb0);
        Assert.False(messages.SerialsWanted);
        Fetched(ItemFirst, ItemFirst + 1);
        screens.MenusRead(open: true);
        screens.MenusRead(open: false);
        Assert.True(messages.SerialsWanted);

        Cursor(ItemClass, 0, count: 2, manager: 0xc0);

        Assert.DoesNotContain(Said(), said => said.Contains("Tonic", StringComparison.Ordinal));
    }

    // Names fetched long before the list opened came from something else: the list waits for its own.
    [Fact]
    public void NamesFetchedLongBeforeTheListOpensAreNoRows()
    {
        Fetched(ItemFirst);
        now += 5_000;
        Cursor(ItemClass, 0, count: 1);
        Assert.Empty(Said());

        Fetched(ItemFirst + 1);

        Assert.Equal(["Items, Salve, 1 of 1"], Said());
    }

    // The reader turned back on enters the current screen again; its rows stay.
    [Fact]
    public void TheListEnteredAgainWhileOnItKeepsItsRows()
    {
        Fetched(SkillFirst);
        Cursor(SkillClass, 0, count: 1);
        now += 5_000;

        screens.RepeatCurrent();

        Assert.Equal(["Skills, Spark, 1 of 1", "Skills, Spark, 1 of 1"], Said());
    }

    // A row fetched from a file the game didn't compile, or compiled with another count, has no known text.
    [Fact]
    public void ARowWithNoKnownTextIsSaidByItsPosition()
    {
        var serials = new MessageSerials([new SerialRange(MessageSerials.SkillNames, SkillFirst, 3)]);
        var alone = new MenuScreens(KnownScreens.All);
        var spoken = new RecordingAnnouncer();
        var skills = BattleListFocus.Skills(serials, spoken, new TextCleaner(TextCleanerRules.None), () => now);
        alone.Observe(skills);
        skills.Observe(new SerialResolved(SkillFirst, BattleListFocus.NameFlags));

        alone.Observe(new CursorInput(0xa0, 0x100, Settle, 1, 0, 0, 0x46, SkillClass, Count: 3));

        Assert.Equal(["Skills, 1 of 3"], spoken.Said.Select(said => said.Text));
    }

    // The by-ear run of 2026-11-21: backing out of the skill list to battle's commands tells nothing (they are no
    // screen), so the skill list stays current until the item list's cursor settles, after the item list fetched its
    // names. Those names are the item list's rows all the same.
    [Fact]
    public void TheItemListOpenedAfterTheSkillListSaysItsFirstRow()
    {
        Fetched(SkillFirst);
        Cursor(SkillClass, 0, count: 1);
        now += 80_000;
        Fetched(ItemFirst);
        Described(ItemDescriptionFirst);
        Fetched(ItemFirst + 1);
        Described(ItemDescriptionFirst + 1);

        Cursor(ItemClass, 0, count: 2, manager: 0xc0);

        Assert.Equal(["Skills, Spark, 1 of 1", "Items, Tonic, 1 of 2"], Said());
        Assert.Equal("Heals a little", items.Details(Focus(ItemClass, 0)));
    }

    // The same run's second opening: the item list closed and opened again on the same manager, its cursor settling on
    // the same row with no other cursor between, after it fetched its names afresh.
    [Fact]
    public void TheListOpenedAgainOnTheSameManagerAndRowIsHeadedAgain()
    {
        Fetched(ItemFirst, ItemFirst + 1);
        Cursor(ItemClass, 0, count: 2);
        now += 16;
        Cursor(ItemClass, 0, count: 2);
        now += 30_000;
        Fetched(ItemFirst, ItemFirst + 1);

        Cursor(ItemClass, 0, count: 2);

        Assert.Equal(["Items, Tonic, 1 of 2", "Items, Tonic, 1 of 2"], Said());
    }

    [Fact]
    public void TheSkillListOpenedAfterTheItemListSaysItsFirstRow()
    {
        Fetched(ItemFirst);
        Cursor(ItemClass, 0, count: 1);
        now += 80_000;
        Fetched(SkillFirst + 1);

        Cursor(SkillClass, 0, count: 1, manager: 0xc0);

        Assert.Equal(["Items, Tonic, 1 of 1", "Skills, Gust, 1 of 1"], Said());
    }

    // The details key (#247): the list fetches each row's description right after its name as it opens (the item
    // list, by-ear run of 2026-11-21), so each row's details are the description fetched after its name.
    [Fact]
    public void TheDetailsAreTheFocusedRowsDescription()
    {
        Fetched(ItemFirst);
        Described(ItemDescriptionFirst);
        Fetched(ItemFirst + 1);
        Described(ItemDescriptionFirst + 1);
        Cursor(ItemClass, 0, count: 2);
        Assert.Equal("Heals a little", items.Details(Focus(ItemClass, 0)));

        Cursor(ItemClass, 1, count: 2);
        Assert.Equal("Heals more", items.Details(Focus(ItemClass, 1)));

        // The help line fetched again on a focus move, with no name before it, changes no row's.
        Cursor(ItemClass, 0, count: 2);
        Described(ItemDescriptionFirst + 1);
        Assert.Equal("Heals a little", items.Details(Focus(ItemClass, 0)));
    }

    [Fact]
    public void ARowWhoseDescriptionWasNotFetchedHasNoDetails()
    {
        Fetched(SkillFirst, SkillFirst + 1);
        Described(SkillDescriptionFirst);
        Cursor(SkillClass, 0, count: 2);

        Assert.Null(skills.Details(Focus(SkillClass, 0)));
        Assert.Equal("Burns one foe", skills.Details(Focus(SkillClass, 1)));
    }

    [Fact]
    public void TheListOpenedAgainForgetsTheLastVisitsDescriptions()
    {
        Fetched(SkillFirst);
        Described(SkillDescriptionFirst);
        Cursor(SkillClass, 0, count: 1);
        now += 5_000;
        Fetched(SkillFirst + 1);
        Cursor(SkillClass, 0, count: 1, manager: 0xc0);

        Assert.Null(skills.Details(Focus(SkillClass, 0)));
    }

    [Fact]
    public void ThereAreNoDetailsOffTheListOrBeforeADescription()
    {
        Fetched(SkillFirst);
        Cursor(SkillClass, 0, count: 1);
        Assert.Null(skills.Details(Focus(SkillClass, 0)));

        Fetched(SkillFirst);
        Described(SkillDescriptionFirst);
        Assert.Equal("Burns one foe", skills.Details(Focus(SkillClass, 0)));
        Cursor(DifficultyClass, 0, count: 5, manager: 0xb0);

        Assert.Null(skills.Details(Focus(SkillClass, 0)));
    }

    private static ScreenEvent Focus(string screenClass, int index) =>
        new(ScreenChange.FocusMoved, KnownScreens.All.First(screen => screen.ScreenClass == screenClass), screenClass, index);

    [Fact]
    public void BothListsAreKnownScreensShownOutsideTheMenus()
    {
        Assert.Contains(BattleListFocus.SkillList, KnownScreens.All);
        Assert.Contains(BattleListFocus.ItemList, KnownScreens.All);
        Assert.True(BattleListFocus.SkillList.OutsideMenus);
        Assert.True(BattleListFocus.ItemList.OutsideMenus);
    }
}
