namespace Metaphor.Core.Battle;

/// <summary>Which side a battle unit is on (#247): a party member, an enemy, or unknown when the read failed.</summary>
public enum BattleSide
{
    Unknown,
    Ally,
    Enemy,
}

/// <summary>One target's share of a resolved battle action (#247), as the battle GUI turns it into a damage popup: the
/// hit unit (an identity, never read as text), the HP change and the HP the target has left, and the popup's kind. Read
/// from <c>BattleHitResult</c> on build 548ddc95… (docs/findings/battle-hit-result-hp-left-static.md). Numbers only.
/// </summary>
/// <param name="Target">The hit unit's record address, to tell one target from another.</param>
/// <param name="HpChange">The HP change: negative is damage (heard by the maintainer on 2026-11-21, #631).</param>
/// <param name="HpLeft">The HP the target has left, which the game clamps to 0..max HP.</param>
/// <param name="Kind">The popup's kind (<c>result+0x304</c>); which kind is a miss or a weakness is
/// <c>[NEEDS VERIFICATION]</c>.</param>
/// <param name="Side">Whether the target is a party member, by its record's place among the party's units.</param>
/// <param name="Slot">The party slot of a party member, from 0, or null.</param>
/// <param name="HpMax">The target's max HP, as the request's clamp holds it, or null when unreadable.</param>
/// <param name="Unit">A party member's unit number, the one its slot holds, or null.</param>
public sealed record BattleHit(
    long Target, int HpChange, int HpLeft, int Kind, BattleSide Side = BattleSide.Unknown, int? Slot = null,
    int? HpMax = null, int? Unit = null)
{
    /// <summary>The kinds the game sends a popup with a number for (docs/findings/battle-hit-result-hp-left-static.md):
    /// 1, <c>0x400</c> and <c>0x1000</c>. The others (4, <c>0x100</c>, <c>0x200</c>) send one without a number.
    /// </summary>
    public bool HasNumber => Kind is 1 or 0x400 or 0x1000;
}

/// <summary>Told of each target a battle action hit.</summary>
public interface IBattleHitObserver
{
    void Observe(BattleHit hit);
}

/// <summary>An observer that ignores every hit: what the battle hit hook reports to when nothing speaks them.</summary>
public sealed class NoBattleHits : IBattleHitObserver
{
    public void Observe(BattleHit hit)
    {
    }
}

/// <summary>A party member as the game data holds it now (#247): its slot from 0, its unit number, its HP, and its max
/// HP when a hit has shown it, else null.</summary>
public sealed record PartyMember(int Slot, int Unit, int Hp, int? HpMax);

/// <summary>Reads the party's members and HP from the game data; empty when it can't.</summary>
public interface IBattleParty
{
    IReadOnlyList<PartyMember> Members();
}
