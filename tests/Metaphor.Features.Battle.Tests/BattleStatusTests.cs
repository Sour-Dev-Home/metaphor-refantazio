using Metaphor.Core.Announcements;
using Metaphor.Core.Battle;

namespace Metaphor.Features.Battle.Tests;

/// <summary>The status key (#631): in a battle, H or L3 pressed alone says the party's HP, then the enemies this battle
/// has hit, and is hidden from the game; anything else, and the key outside a battle, reaches it. Made-up party and
/// numbers only.</summary>
[Trait("Category", "Unit")]
public sealed class BattleStatusTests : IDisposable
{
    private const int A = 0x4000;

    private readonly RecordingAnnouncer announcer = new();
    private readonly ManualTime time = new();
    private readonly Party party = new();
    private readonly Scene scene = new() { On = true };
    private readonly HitLines hits;
    private readonly BattleStatus status;

    public BattleStatusTests()
    {
        hits = new HitLines(new SilentAnnouncer(), time, unit => unit == 3 ? "Rowan" : null);
        status = new BattleStatus(party, hits, scene, announcer);
    }

    [Fact]
    public void OutsideABattleL3AndHReachTheGameAndSayNothing()
    {
        scene.On = false;
        party.Now = [new PartyMember(0, 3, 89, 120)];

        Assert.Equal(BattleStatus.LeftThumb, status.Pad(0, BattleStatus.LeftThumb, made: true));
        Assert.Equal([BattleStatus.KeyH], status.Keys([BattleStatus.KeyH]));

        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void APressKeepsWhereItStartedUntilReleased()
    {
        scene.On = false;
        status.Pad(0, BattleStatus.LeftThumb, made: true);
        scene.On = true;

        Assert.Equal(BattleStatus.LeftThumb, status.Pad(0, BattleStatus.LeftThumb, made: true));
        Assert.Empty(announcer.Said);

        status.Pad(0, 0, made: true);
        Assert.Equal(0, status.Pad(0, BattleStatus.LeftThumb, made: true));
        Assert.Single(announcer.Said);
    }

    [Fact]
    public void ABattleSceneIsTheFieldsCommandBattleScene()
    {
        Assert.True(BattleScene.IsBattle("fldScene_CmdBattle@fld@app"));
        Assert.False(BattleScene.IsBattle("fldScene_Idle@fld@app"));
        Assert.False(BattleScene.IsBattle("fldScene_LargeMap@fld@app"));
        Assert.False(BattleScene.IsBattle(null));
    }

    [Fact]
    public void TheSceneIsAskedOncePerPress()
    {
        status.Pad(0, BattleStatus.LeftThumb, made: true);
        status.Pad(0, BattleStatus.LeftThumb, made: true);
        status.Pad(0, 0, made: true);

        Assert.Equal(1, scene.Asked);
    }

    public void Dispose() => hits.Dispose();

    [Fact]
    public void L3AloneSaysEachMemberThenTheEnemiesAndIsHidden()
    {
        party.Now = [new PartyMember(0, 3, 89, 120), new PartyMember(1, 7, 40, null)];
        hits.Observe(new BattleHit(0xa0, -5, 55, 1, BattleSide.Enemy));

        Assert.Equal(0, status.Pad(0, BattleStatus.LeftThumb, made: true));

        Assert.Equal(new Announcement("Rowan 89 of 120, Second ally, 40 HP. Enemy, 55", Interrupt: true), announcer.Said[^1]);
    }

    [Fact]
    public void HAloneSaysItToo()
    {
        party.Now = [new PartyMember(0, 3, 89, 120)];

        Assert.Empty(status.Keys([BattleStatus.KeyH]));

        Assert.Equal(["Rowan 89 of 120"], announcer.Said.Select(said => said.Text));
    }

    [Fact]
    public void AChordWithTheKeyReachesTheGameAndSaysNothing()
    {
        Assert.Equal(BattleStatus.LeftThumb | A, status.Pad(0, BattleStatus.LeftThumb | A, made: true));
        Assert.Equal([BattleStatus.KeyH, 0x41], status.Keys([BattleStatus.KeyH, 0x41]));

        Assert.Empty(announcer.Said);
    }

    [Fact]
    public void AHeldKeyOrASecondPadMirroringItSpeaksOnce()
    {
        status.Pad(0, BattleStatus.LeftThumb, made: true);
        status.Pad(0, BattleStatus.LeftThumb, made: true);
        status.Pad(1, BattleStatus.LeftThumb, made: true);

        Assert.Single(announcer.Said);
    }

    [Fact]
    public void WithNothingReadItSaysSo()
    {
        status.Keys([BattleStatus.KeyH]);

        Assert.Equal(["No party status"], announcer.Said.Select(said => said.Text));
    }

    private sealed class Party : IBattleParty
    {
        public IReadOnlyList<PartyMember> Now { get; set; } = [];

        public IReadOnlyList<PartyMember> Members() => Now;
    }

    private sealed class Scene : IBattleScene
    {
        public bool On { get; set; }

        public int Asked { get; private set; }

        public bool InBattle()
        {
            Asked++;
            return On;
        }
    }
}
