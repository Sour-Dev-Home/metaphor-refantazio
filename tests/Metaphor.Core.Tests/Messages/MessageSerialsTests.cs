using Metaphor.Core.Announcements;
using Metaphor.Core.Messages;

namespace Metaphor.Core.Tests.Messages;

/// <summary>Turning a message serial number into its message (#584): a file's serials are its first serial plus each
/// message's position in it (docs/findings/message-serials-from-file-positions.md). File names and labels follow the
/// game's; the text is made up.</summary>
[Trait("Category", "Unit")]
public sealed class MessageSerialsTests
{
    private const string Menu = "EN/message/system/Test_Menu.msg";

    // A message whose body is a line starting with "@", as the real system file has: it is text, not a label.
    private const string MenuSource = """
        //--------
        @Test_Menu_01
        {
        Pack
        <WAIT>
        }

        @Test_Dummy
        {
        @Test_Dummy
        <WAIT>
        }

        @Test_Menu_02 SEL 0
        {
        Maps<COLOR -1>
        <WAIT>
        }
        """;

    private static MessageSerials Serials(int count = 3) => new([new SerialRange("message/system/Test_Menu.msg", 500, count)]);

    [Fact]
    public void ASerialIsItsFilesFirstSerialPlusTheMessagesPosition()
    {
        var serials = Serials();
        serials.Observe(new MessageFileCompiled(Menu, 6, MenuSource));

        Assert.Equal("Test_Menu_01", serials.FindBySerial(500)?.Label);
        Assert.Equal("Test_Dummy", serials.FindBySerial(501)?.Label);
        Assert.Equal("Test_Menu_02", serials.FindBySerial(502)?.Label);
    }

    [Fact]
    public void ASerialOutsideEveryKnownFileFindsNothing()
    {
        var serials = Serials();
        serials.Observe(new MessageFileCompiled(Menu, 6, MenuSource));

        Assert.Null(serials.FindBySerial(499));
        Assert.Null(serials.FindBySerial(503));
    }

    [Fact]
    public void AFileIsKnownWhateverItsLanguageFolderOrCase()
    {
        var serials = Serials();
        serials.Observe(new MessageFileCompiled("JP/MESSAGE/system/test_menu.msg", 6, MenuSource));

        Assert.Equal("Test_Menu_02", serials.FindBySerial(502)?.Label);
    }

    // The table is pinned to a build: a file with another number of messages has been changed, so its positions can't
    // be trusted and nothing in it is found.
    [Fact]
    public void AFileWithAnotherMessageCountIsNotUsed()
    {
        var serials = Serials(count: 4);
        serials.Observe(new MessageFileCompiled(Menu, 6, MenuSource));

        Assert.Null(serials.FindBySerial(500));
    }

    [Fact]
    public void AFileNotInTheTableIsIgnored()
    {
        var serials = Serials();
        serials.Observe(new MessageFileCompiled("EN/message/system/Other.msg", 7, MenuSource));

        Assert.Null(serials.FindBySerial(500));
    }

    [Fact]
    public void ARecompiledFileReplacesTheOneBefore()
    {
        var serials = Serials();
        serials.Observe(new MessageFileCompiled(Menu, 6, MenuSource));
        serials.Observe(new MessageFileCompiled(Menu, 6, MenuSource.Replace("Pack", "Bag", StringComparison.Ordinal)));

        Assert.Equal("Bag\n<WAIT>", serials.FindBySerial(500)?.Text);
    }

    [Fact]
    public void TheTextIsCleanedOrNullWhenThereIsNone()
    {
        var serials = Serials();
        var cleaner = new TextCleaner(TextCleanerRules.None);
        serials.Observe(new MessageFileCompiled(Menu, 6, MenuSource));

        Assert.Equal("Maps", serials.TextOf(502, cleaner));
        Assert.Null(serials.TextOf(503, cleaner));
    }

    [Fact]
    public void TheLogDescriptionNamesTheLabelAndTheText()
    {
        var serials = Serials();
        serials.Observe(new MessageFileCompiled(Menu, 6, MenuSource));

        Assert.Equal("label=Test_Menu_02 text=\"Maps\"", serials.Describe(502));
        Assert.Equal("label=(unknown)", serials.Describe(9));
    }

    // Both system files, as the range tests at 0x9f4c90 and 0x9f4c60 hold them on build 548ddc95.
    [Fact]
    public void TheBuildsTableHoldsBothSystemFiles()
    {
        Assert.Contains(new SerialRange("message/system/System_Message.msg", 1142, 790), MessageSerials.Steam18330018);
        Assert.Contains(new SerialRange("message/system/System_Message_PC.msg", 11453, 354), MessageSerials.Steam18330018);
    }

    // The item and skill files (#247), as the range tests at 0x9f4a90-0x9f4ac0 hold them on build 548ddc95.
    [Fact]
    public void TheBuildsTableHoldsTheItemAndSkillFiles()
    {
        Assert.Contains(new SerialRange(MessageSerials.ItemNames, 5143, 773), MessageSerials.Steam18330018);
        Assert.Contains(new SerialRange(MessageSerials.ItemDescriptions, 5916, 833), MessageSerials.Steam18330018);
        Assert.Contains(new SerialRange(MessageSerials.SkillNames, 6749, 1086), MessageSerials.Steam18330018);
        Assert.Contains(new SerialRange(MessageSerials.SkillDescriptions, 7835, 1052), MessageSerials.Steam18330018);
    }

    [Fact]
    public void ASerialIsInAFileByItsRangeEvenBeforeTheFileIsCompiled()
    {
        var serials = Serials();

        Assert.True(serials.InFile(500, "message/system/Test_Menu.msg"));
        Assert.True(serials.InFile(502, "MESSAGE/system/test_menu.msg"));
        Assert.False(serials.InFile(503, "message/system/Test_Menu.msg"));
        Assert.False(serials.InFile(500, "message/system/Other.msg"));
    }
}
