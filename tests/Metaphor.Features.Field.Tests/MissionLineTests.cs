using Metaphor.Core.Announcements;
using Metaphor.Core.Messages;

namespace Metaphor.Features.Field.Tests;

/// <summary>The current mission line (#658): a text the game shows is the mission when it is one of the mission files'
/// <c>MissionText_</c> messages, spoken once when it changes and kept for "where am I". Made-up missions only.</summary>
[Trait("Category", "Unit")]
public sealed class MissionLineTests
{
    private const string Source = """
        @MissionText_Test_01
        {
        Walk to Testshire
        <WAIT>
        }

        @MissionText_Test_02
        {
        Queue up for <COLOR 3>mock bread<COLOR -1>
        <WAIT>
        }

        @Other_Label
        {
        Not a mission
        }
        """;

    private readonly RecordingAnnouncer announcer = new();
    private readonly List<string> log = [];
    private readonly MissionLine mission;

    public MissionLineTests()
    {
        mission = new MissionLine(announcer, new TextCleaner(TextCleanerRules.None), log.Add);
        mission.Observe(new MessageFileCompiled("EN/message/system/Mission_Message_2.msg", 26, Source));
    }

    [Fact]
    public void AMissionShownIsSpokenOnceAndKept()
    {
        Assert.Equal("Mission: Walk to Testshire", mission.Seen("Walk to Testshire"));
        Assert.Null(mission.Seen("Walk to Testshire"));

        Assert.Equal(new Announcement("Mission: Walk to Testshire", Interrupt: false), announcer.Said.Single());
        Assert.Equal("Walk to Testshire", mission.Current);
        Assert.Equal(["mission label=MissionText_Test_01 changed"], log);
    }

    [Fact]
    public void ANewMissionIsSpokenWithItsTagsDropped()
    {
        mission.Seen("Walk to Testshire");
        mission.Seen("Queue up for mock bread");

        Assert.Equal(["Mission: Walk to Testshire", "Mission: Queue up for mock bread"], announcer.Said.Select(said => said.Text));
        Assert.Equal("Queue up for mock bread", mission.Current);
    }

    [Fact]
    public void OtherTextAndOtherFilesAreNotMissions()
    {
        var other = new MissionLine(announcer, new TextCleaner(TextCleanerRules.None));
        other.Observe(new MessageFileCompiled("EN/message/system/System_Message.msg", 6, Source));

        Assert.Null(mission.Seen("Not a mission"));
        Assert.Null(mission.Seen(null));
        Assert.Null(mission.Seen(""));
        Assert.Null(other.Seen("Walk to Testshire"));

        Assert.Empty(announcer.Said);
        Assert.Null(mission.Current);
    }

    [Fact]
    public void AFileCompiledAgainReplacesItsMissions()
    {
        mission.Observe(new MessageFileCompiled("EN/message/system/Mission_Message_2.msg", 40,
            "@MissionText_Test_09\n{\nFind the mock tunnel\n}\n"));

        Assert.Null(mission.Seen("Walk to Testshire"));
        Assert.Equal("Mission: Find the mock tunnel", mission.Seen("Find the mock tunnel"));
    }
}
