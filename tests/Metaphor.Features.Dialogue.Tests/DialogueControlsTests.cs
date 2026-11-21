using Metaphor.Core.Announcements;
using Metaphor.Core.Messages;

namespace Metaphor.Features.Dialogue.Tests;

/// <summary>The dialogue controls (#58): the repeat and reader on/off presses, seen through what the game is handed back
/// and what the player hears.</summary>
[Trait("Category", "Unit")]
public sealed class DialogueControlsTests
{
    private const int X = 0x8000;
    private const int A = 0x4000;
    private const int LeftBumper = 0x400;
    private const int RightBumper = 0x800;
    private const int R = 0x52;
    private const int T = 0x54;
    private const int Enter = 0x0D;

    // A message pointer the window held in session 5 (dialogue-window-flag-session5-2026-11-17.md); any non-zero value
    // means a message is on screen.
    private const long Message = 0x1315afe20;

    private readonly RecordingAnnouncer announcer = new();
    private readonly DialogueHistory history = new();
    private readonly DialogueReader reader;
    private readonly DialogueControls controls;

    public DialogueControlsTests()
    {
        reader = new DialogueReader(announcer);
        controls = new DialogueControls(DialogueBindings.Default, history, reader, announcer);
    }

    private static Announcement Said(string text) => new(text, Interrupt: true);

    [Fact]
    public void PressingXDuringDialogueRepeatsTheLastLine()
    {
        history.Add("Ada: One.");
        history.Add("Ada: Two.");
        controls.WindowRead(Message);

        controls.Pad(0, 0);
        controls.Pad(0, X);

        Assert.Equal([Said("Ada: Two.")], announcer.Said);
    }

    // #526: a scene whose lines weren't read replayed the opening's last line, from the window before.
    [Fact]
    public void AfterTheWindowClosesRepeatDoesNotSpeakThePreviousWindowsLine()
    {
        history.Add("More: Last line of the opening.");
        controls.WindowRead(Message);
        controls.WindowRead(0);
        controls.WindowRead(Message + 0x10);

        controls.Pad(0, X);

        Assert.Equal([Said("No dialogue to repeat")], announcer.Said);
    }

    [Fact]
    public void AfterTheWindowClosesRepeatSpeaksTheNewWindowsLine()
    {
        history.Add("More: Last line of the opening.");
        controls.WindowRead(Message);
        controls.WindowRead(null);
        history.Add("Gallica: A new line.");
        controls.WindowRead(Message);

        controls.Pad(0, X);

        Assert.Equal([Said("Gallica: A new line.")], announcer.Said);
    }

    [Fact]
    public void HoldingXRepeatsOnce()
    {
        history.Add("Ada: One.");
        controls.WindowRead(Message);

        controls.Pad(0, X);
        controls.Pad(0, X);
        controls.Pad(0, X);

        Assert.Single(announcer.Said);
    }

    [Fact]
    public void ReleasingAndPressingAgainRepeatsAgain()
    {
        history.Add("Ada: One.");
        controls.WindowRead(Message);

        controls.Pad(0, X);
        controls.Pad(0, 0);
        controls.Pad(0, X);

        Assert.Equal(2, announcer.Said.Count);
    }

    [Fact]
    public void XIsHiddenFromTheGameWhileTheWindowIsOpenAndOtherButtonsAreNot()
    {
        controls.WindowRead(Message);

        Assert.Equal(A, controls.Pad(0, X | A));
    }

    [Fact]
    public void AReadNotMadeIsHandedBackUnchangedAndRepeatsNothing()
    {
        history.Add("Ada: One.");
        controls.WindowRead(Message);

        Assert.Equal(X | A, controls.Pad(1, X | A, made: false));
        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void OutsideDialogueXKeepsItsJobAndRepeatsNothing()
    {
        history.Add("Ada: One.");

        Assert.Equal(X, controls.Pad(0, X));
        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void AfterTheWindowClosesXIsHandedToTheGameAgain()
    {
        controls.WindowRead(Message);
        controls.WindowRead(0);

        Assert.Equal(X, controls.Pad(0, X));
    }

    [Fact]
    public void AButtonAlreadyHeldWhenTheWindowOpensDoesNotRepeat()
    {
        history.Add("Ada: One.");
        controls.Pad(0, X);
        controls.WindowRead(Message);

        controls.Pad(0, X);

        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void ASecondPadHoldingXWhileTheFirstAlreadyDoesNotRepeatAgain()
    {
        // Steam Input's virtual pad can report the same press as the physical one (spike-session-2-2026-11-16.md).
        history.Add("Ada: One.");
        controls.WindowRead(Message);

        controls.Pad(0, X);
        controls.Pad(1, X);
        controls.Pad(0, X);
        controls.Pad(1, X);

        Assert.Single(announcer.Said);
    }

    [Fact]
    public void APadThatOnlyChangesItsSticksNeverRepeats()
    {
        // The game's mask holds buttons only (input-route-2026-11-16.md); a noisy pad repeats the same mask.
        history.Add("Ada: One.");
        controls.WindowRead(Message);

        for (var frame = 0; frame < 100; frame++)
        {
            controls.Pad(1, 0);
        }

        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void RepeatingWithNoLineYetSaysSo()
    {
        controls.WindowRead(Message);

        controls.Pad(0, X);

        Assert.Equal([Said("No dialogue to repeat")], announcer.Said);
    }

    [Fact]
    public void RRepeatsTheLastLineAndIsHiddenFromTheGame()
    {
        history.Add("Ada: One.");
        controls.WindowRead(Message);

        var handed = controls.Keys([Enter, R]);

        Assert.Equal([Enter], handed);
        Assert.Equal([Said("Ada: One.")], announcer.Said);
    }

    [Fact]
    public void OutsideDialogueRIsHandedToTheGame()
    {
        Assert.Equal([R], controls.Keys([R]));
        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void HoldingRRepeatsOnce()
    {
        history.Add("Ada: One.");
        controls.WindowRead(Message);

        controls.Keys([R]);
        controls.Keys([R]);

        Assert.Single(announcer.Said);
    }

    [Fact]
    public void BothBumpersTogetherTurnTheReaderOffAndSayIt()
    {
        controls.WindowRead(Message);

        controls.Pad(0, LeftBumper);
        controls.Pad(0, LeftBumper | RightBumper);

        Assert.False(reader.On);
        Assert.Equal([Said("Dialogue reader off")], announcer.Said);
    }

    [Fact]
    public void BothBumpersAreHiddenFromTheGameDuringDialogue()
    {
        controls.WindowRead(Message);

        Assert.Equal(A, controls.Pad(0, LeftBumper | RightBumper | A));
    }

    [Fact]
    public void OneBumperAloneDoesNotTurnTheReaderOff()
    {
        controls.WindowRead(Message);

        controls.Pad(0, LeftBumper);

        Assert.True(reader.On);
    }

    [Fact]
    public void TTurnsTheReaderOffThenOn()
    {
        controls.WindowRead(Message);

        controls.Keys([T]);
        controls.Keys([]);
        controls.Keys([T]);

        Assert.True(reader.On);
        Assert.Equal([Said("Dialogue reader off"), Said("Dialogue reader on")], announcer.Said);
    }

    [Fact]
    public void TheRepeatIsSpokenWhileTheReaderIsOff()
    {
        history.Add("Ada: One.");
        controls.WindowRead(Message);
        controls.Keys([T]);

        controls.Keys([R]);

        Assert.Equal(Said("Ada: One."), announcer.Said[^1]);
    }

    // #206, #232: with no reading of the window, the keys do nothing and the game keeps them.

    [Fact]
    public void BeforeTheWindowIsReadTheKeysDoNothingAndTheGameKeepsThem()
    {
        history.Add("Ada: One.");

        Assert.Equal(X | A, controls.Pad(0, X | A));
        Assert.Equal([R, Enter], controls.Keys([R, Enter]));
        Assert.Equal(LeftBumper | RightBumper, controls.Pad(0, LeftBumper | RightBumper));
        Assert.True(reader.On);
        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void AnUnreadableWindowLeavesTheKeysToTheGame()
    {
        history.Add("Ada: One.");
        controls.WindowRead(Message);
        controls.WindowRead(null);

        Assert.Equal(X, controls.Pad(0, X));
        Assert.Equal([T], controls.Keys([T]));
        Assert.True(reader.On);
        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void AnUnreadableWindowThatBecomesReadableAgainActs()
    {
        history.Add("Ada: One.");
        controls.WindowRead(null);
        controls.WindowRead(Message);

        Assert.Equal(0, controls.Pad(0, X));
        Assert.Equal([Said("Ada: One.")], announcer.Said);
    }

    // The by-ear finding of 2026-11-21: on a tutorial pop-up's page 2, X and R should say that page again.
    [Fact]
    public void WhileAPopUpIsOnScreenXRepeatsItsPageAndIsHidden()
    {
        var withPopUp = new DialogueControls(
            DialogueBindings.Default, history, reader, announcer, onScreen: () => "Page 2 of 3. Second.");
        history.Add("Ada: One.");

        Assert.Equal(A, withPopUp.Pad(0, X | A));

        Assert.Equal([Said("Page 2 of 3. Second.")], announcer.Said);
    }

    [Fact]
    public void WhileAPopUpIsOverDialogueRRepeatsThePopUp()
    {
        var withPopUp = new DialogueControls(
            DialogueBindings.Default, history, reader, announcer, onScreen: () => "Walking About, 3 pages. Press to walk.");
        history.Add("Ada: One.");
        withPopUp.WindowRead(Message);

        Assert.Equal([Enter], withPopUp.Keys([Enter, R]));

        Assert.Equal([Said("Walking About, 3 pages. Press to walk.")], announcer.Said);
    }

    [Fact]
    public void OnceThePopUpHasGoneXRepeatsTheLastLineAgain()
    {
        var onScreen = "Page 2 of 3. Second.";
        var withPopUp = new DialogueControls(DialogueBindings.Default, history, reader, announcer, onScreen: () => onScreen);
        history.Add("Ada: One.");
        withPopUp.WindowRead(Message);
        withPopUp.Pad(0, X);
        withPopUp.Pad(0, 0);

        onScreen = null;
        withPopUp.Pad(0, X);

        Assert.Equal([Said("Page 2 of 3. Second."), Said("Ada: One.")], announcer.Said);
    }

    [Fact]
    public void APopUpOutsideDialogueLeavesTheReaderBindingToTheGame()
    {
        var withPopUp = new DialogueControls(DialogueBindings.Default, history, reader, announcer, onScreen: () => "Page 1 of 1.");

        Assert.Equal(LeftBumper | RightBumper, withPopUp.Pad(0, LeftBumper | RightBumper));
        Assert.Equal([T], withPopUp.Keys([T]));
        Assert.Empty(announcer.Said);
    }
}
