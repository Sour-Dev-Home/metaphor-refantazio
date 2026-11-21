using Metaphor.Core.Announcements;
using Metaphor.Core.Messages;

namespace Metaphor.Features.Field.Tests;

/// <summary>The field controls (#181): N and L on the keyboard, LB with D-pad Down, Up and Left on the pad, acting and
/// hidden from the game only on the exact press in free roam. A stand-in field and made-up names only.</summary>
[Trait("Category", "Unit")]
public sealed class FieldControlsTests
{
    private const int KeyN = 0x4E;
    private const int KeyL = 0x4C;
    private const int KeyW = 0x57;
    private const int Lb = 0x0400;
    private const int Up = 0x0010;
    private const int Down = 0x0040;
    private const int Left = 0x0080;
    private const int A = 0x4000;

    private readonly RecordingAnnouncer announcer = new();
    private readonly StandInField field = new();
    private readonly FieldPlace place = new(new TextCleaner(TextCleanerRules.None));
    private readonly List<string> log = [];
    private readonly FieldControls controls;
    private long now = 1000;

    public FieldControlsTests()
    {
        controls = new FieldControls(FieldBindings.Default, field, place, announcer, () => now, log.Add);
        field.View = new FieldView(0, 0, 0, 0,
        [
            new(FieldKind.Door, 0, 0, 900),
            new(FieldKind.Chest, 0, 0, 200),
            new(FieldKind.Person, 500, 0, 0, "Testa"),
        ]);
    }

    private sealed class StandInField : IFieldReads
    {
        public bool Roaming { get; set; } = true;

        public FieldView? View { get; set; }

        public int? Map { get; set; } = 3;

        public int FreeRoamReads { get; private set; }

        public bool InFreeRoam()
        {
            FreeRoamReads++;
            return Roaming;
        }

        public FieldView? Read() => View;

        public int? MapId() => Map;
    }

    private IEnumerable<string> Said() => announcer.Said.Select(said => said.Text);

    private void Tap(int buttons)
    {
        controls.Pad(0, buttons, made: true);
        controls.Pad(0, Lb, made: true);
    }

    [Fact]
    public void NSpeaksTheWholeListAndIsHidden()
    {
        var handed = controls.Keys([KeyW, KeyN]);

        Assert.Equal([KeyW], handed);
        Assert.Equal(
            new Announcement("3 nearby. Chest, 2 steps ahead, in reach. Testa, 5 steps left. Door, 9 steps ahead", Interrupt: true),
            announcer.Said.Single());
    }

    [Fact]
    public void HoldingNSpeaksOnceUntilItIsLetGo()
    {
        controls.Keys([KeyN]);
        controls.Keys([KeyN]);
        controls.Keys([]);
        controls.Keys([KeyN]);

        Assert.Equal(2, announcer.Said.Count);
    }

    [Fact]
    public void LbWithDownStepsThroughTheListAndWraps()
    {
        Tap(Lb | Down);
        Tap(Lb | Down);
        Tap(Lb | Down);
        Tap(Lb | Down);
        Tap(Lb | Up);

        Assert.Equal(
        [
            "1 of 3. Chest, 2 steps ahead, in reach",
            "2 of 3. Testa, 5 steps left",
            "3 of 3. Door, 9 steps ahead",
            "1 of 3. Chest, 2 steps ahead, in reach",
            "3 of 3. Door, 9 steps ahead",
        ], Said());
    }

    [Fact]
    public void AStepAfterAPauseStartsAgainAtTheNearest()
    {
        Tap(Lb | Down);
        Tap(Lb | Down);
        now += FieldControls.StepMemoryMilliseconds + 1;
        Tap(Lb | Down);

        Assert.StartsWith("1 of 3.", Said().Last(), StringComparison.Ordinal);
    }

    [Fact]
    public void UpWithNoListStartsAtTheFarthest()
    {
        Tap(Lb | Up);

        Assert.Equal("3 of 3. Door, 9 steps ahead", Said().Single());
    }

    [Fact]
    public void OnlyTheChordsDpadButtonIsHidden()
    {
        Assert.Equal(Lb | A, controls.Pad(0, Lb | Down | A, made: true));
        Assert.Equal(Down, controls.Pad(0, Down, made: true));
        Assert.Equal(Lb, controls.Pad(0, Lb, made: true));
    }

    [Fact]
    public void OutsideFreeRoamEverythingReachesTheGameAndNothingIsSaid()
    {
        field.Roaming = false;

        Assert.Equal(Lb | Down, controls.Pad(0, Lb | Down, made: true));
        Assert.Equal([KeyN, KeyL], controls.Keys([KeyN, KeyL]));
        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void ThePressFiresWhenFreeRoamComesWhileHeldOnlyAfterALetGo()
    {
        field.Roaming = false;
        controls.Keys([KeyN]);
        field.Roaming = true;
        controls.Keys([KeyN]);

        Assert.Single(announcer.Said);
    }

    [Fact]
    public void FreeRoamIsReadOnlyWhileABindingIsHeld()
    {
        controls.Pad(0, Lb | A, made: true);
        controls.Keys([KeyW]);
        controls.Pad(0, Lb | Down, made: false);

        Assert.Equal(0, field.FreeRoamReads);
    }

    [Fact]
    public void LAndLbWithLeftSayThePlace()
    {
        place.Observe(new MessageFileCompiled("EN/message/MapName.msg", 4, "@Map_00\n{\nTestshire\n}\n"));
        place.LookedUp(FieldPlace.MapNameEntry, 0, mapId: 3);

        Assert.Equal([], controls.Keys([KeyL]));
        controls.Keys([]);
        Assert.Equal(Lb, controls.Pad(0, Lb | Left, made: true));

        Assert.Equal(["Testshire", "Testshire"], Said());
    }

    [Fact]
    public void WhereAmIAddsTheCurrentMission()
    {
        var mission = new MissionLine(new RecordingAnnouncer(), new TextCleaner(TextCleanerRules.None));
        mission.Observe(new MessageFileCompiled("EN/message/system/Mission_Message_3.msg", 27,
            "@MissionText_Test_01\n{\nReach mock harbour\n}\n"));
        var withMission = new FieldControls(FieldBindings.Default, field, place, announcer, () => now, mission: mission);
        place.Observe(new MessageFileCompiled("EN/message/MapName.msg", 4, "@Map_00\n{\nTestshire\n}\n"));

        withMission.Keys([KeyL]);
        withMission.Keys([]);
        place.LookedUp(FieldPlace.MapNameEntry, 0, mapId: 3);
        mission.Seen("Reach mock harbour");
        withMission.Keys([KeyL]);

        Assert.Equal(["Place not known yet", "Testshire. Mission: Reach mock harbour"], Said());
    }

    [Fact]
    public void AnUnknownPlaceOrUnreadableFieldSaysSo()
    {
        field.View = null;

        controls.Keys([KeyL]);
        controls.Keys([KeyN]);

        Assert.Equal(["Place not known yet", "Can't read the field"], Said());
    }

    [Fact]
    public void NothingNearbySaysSo()
    {
        field.View = new FieldView(0, 0, 0, 0, [new(FieldKind.Enemy, 0, 0, (float)Nearby.Radius * 2)]);

        controls.Keys([KeyN]);

        Assert.Equal("Nothing nearby", Said().Single());
    }

    [Fact]
    public void EachPressLogsItsNumbersWithoutNames()
    {
        controls.Keys([KeyN]);

        Assert.StartsWith("nearby player=(0,0,0) heading=0 map=? doorValue=? units=Person:1,Door:1,Chest:1,Enemy:0", log[0], StringComparison.Ordinal);
        Assert.Equal(4, log.Count);
        Assert.Contains("kind=Chest at=(0,0,200) distance=200 bearing=0 reach=True named=False", log[1], StringComparison.Ordinal);
        Assert.DoesNotContain(log, line => line.Contains("Testa", StringComparison.Ordinal));
    }
}
