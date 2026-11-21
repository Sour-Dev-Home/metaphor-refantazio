using Metaphor.Core.Announcements;
using Metaphor.Core.Menus;

namespace Metaphor.Features.Menus.Tests;

/// <summary>The details key on a screen shown outside the menus (#247), such as battle's skill list: it answers, and is
/// hidden from the game, only while that screen's cursor is still called, so F and R3 reach the game once the list has
/// closed into a screen with no cursor. Made-up screens and text only.</summary>
[Trait("Category", "Unit")]
public sealed class MenuControlsOutsideMenusTests
{
    private const string ListClass = "ListChoice@test";
    private const string MenuClass = "MenuChoice@test";
    private const int Settle = 0x1;
    private const int R3 = 0x0004;
    private const int KeyF = 0x46;

    private static readonly Screen List = new("List", ListClass, OutsideMenus: true);
    private static readonly Screen Menu = new("Menu", MenuClass);

    private readonly RecordingAnnouncer announcer = new();
    private readonly CursorEvents cursors;
    private readonly MenuScreens screens = new([List, Menu]);
    private readonly MenuControls controls;
    private long now = 1_000;

    public MenuControlsOutsideMenusTests()
    {
        cursors = new CursorEvents(() => now);
        // A battle: the menus read closed.
        screens.MenusRead(open: false);
        cursors.Observe(screens);
        controls = new MenuControls(
            MenuBindings.Default, screens, new SwitchedReader(announcer), [new ListDetails()], cursors.CalledRecently);
        screens.Observe(controls);
    }

    private void Cursor(string screenClass, int row) =>
        cursors.Handled(new CursorInput(0xa0, ClassRva: 0x100, Settle, Result: 1, Top: 0, Row: row, Flags: 0, screenClass));

    [Fact]
    public void WhileTheListIsShownTheDetailsKeyAnswersAndIsHidden()
    {
        Cursor(ListClass, row: 1);

        Assert.Equal(0, controls.Pad(0, R3));
        Assert.Empty(controls.Keys([KeyF]));
        Assert.Equal(new Announcement("Details of row 2", Interrupt: true), announcer.Said[^1]);
    }

    [Fact]
    public void OnceTheListsCursorGoesQuietTheDetailsKeyReachesTheGame()
    {
        Cursor(ListClass, row: 0);
        now += CursorEvents.QuietMilliseconds;

        Assert.Equal(R3, controls.Pad(0, R3));
        Assert.Equal([KeyF], controls.Keys([KeyF]));
        Assert.Empty(announcer.Said);
    }

    // A screen of the menus still needs them open.
    [Fact]
    public void AMenuScreensCursorWhileTheMenusAreClosedDoesNotTakeTheKey()
    {
        Cursor(MenuClass, row: 0);

        Assert.Equal(R3, controls.Pad(0, R3));
    }

    private sealed class ListDetails : IScreenDetails
    {
        public string? Details(ScreenEvent focus) => focus.Screen == List ? $"Details of row {focus.Index + 1}" : null;
    }

    private sealed class SwitchedReader(IAnnouncer announcer) : IReaderSwitch
    {
        public bool IsOn { get; private set; } = true;

        public void Announce(Announcement announcement) => announcer.Announce(announcement);

        public void Toggle() => IsOn = !IsOn;
    }
}
