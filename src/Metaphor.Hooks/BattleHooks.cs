using System.Collections.Concurrent;
using System.Globalization;
using Metaphor.Core;
using Metaphor.Core.Battle;

namespace Metaphor.Hooks;

/// <summary>The party as the game data block holds it (#247): each slot's unit and its HP at unit <c>+0x0C</c>, the
/// offset session 6 checked (docs/findings/battle-turn-icons-and-ui-components-static.md), and the max HP the last hit
/// on it showed. Reads through <see cref="GameDataRead"/> and <see cref="ModuleMemoryReader"/> only.</summary>
internal sealed class BattlePartyMemory(GameDataRead data) : IBattleParty
{
    private const int HpAt = 0x0C;

    private readonly ConcurrentDictionary<long, int> maxByUnit = new();

    public IReadOnlyList<PartyMember> Members()
    {
        var members = new List<PartyMember>();
        for (var slot = 0; slot < GameDataRead.SlotCount; slot++)
        {
            if (data.SlotUnit(slot) is { } unit && data.UnitAddress(unit) is { } address &&
                ModuleMemoryReader.ReadInt32(address + HpAt) is { } hp)
            {
                members.Add(new PartyMember(slot, unit, hp, maxByUnit.TryGetValue(address, out var max) ? max : null));
            }
        }
        return members;
    }

    /// <summary>The party slot and unit number whose unit is at <paramref name="record"/>, or null for none.</summary>
    public (int Slot, int Unit)? Find(long record)
    {
        for (var slot = 0; slot < GameDataRead.SlotCount; slot++)
        {
            if (data.SlotUnit(slot) is { } unit && data.UnitAddress(unit) is { } address && address == record)
            {
                return (slot, unit);
            }
        }
        return null;
    }

    /// <summary>Keeps the max HP a hit showed for the unit at <paramref name="record"/>.</summary>
    public void Saw(long record, int max) => maxByUnit[record] = max;
}

/// <summary>Reports each target a resolved battle action hits (#247): the battle GUI's <c>BattleHitResult</c>,
/// <c>(request*, gui*, result*, target*, ushort index, int mode)</c> in <c>.ecode</c>, which runs once per target and
/// writes into its request the HP change and the HP the target has left before it sends the damage popup. Observed after
/// it returns, so those are written: the request's <c>+0x30</c> (HP change), <c>+0x38</c> (max HP, by how it clamps)
/// and <c>+0x40</c> (HP left, clamped to 0..max), and the result's kind at <c>+0x304</c>, a short. The target's unit
/// record is <c>*(*(target+0x18)+0x8)</c>; one at a party slot's unit is an ally, any other an enemy. Function,
/// arguments and offsets are our reading of the exe on build 548ddc95… (docs/findings/battle-hit-result-hp-left-static.md);
/// the HP change and HP left were heard right by the maintainer on 2026-11-21 (#631). A call whose numbers can't be read
/// is skipped. In a Debug build the raw values go to the message log for the by-ear check.</summary>
[GameHook(GameBuilds.Steam18330018)]
internal sealed class BattleHitResultHook : IGameHook, IObserves6WithResult
{
    private const int HpChangeAt = 0x30;
    private const int MpChangeAt = 0x34;
    private const int HpMaxAt = 0x38;
    private const int HpLeftAt = 0x40;
    private const int MpLeftAt = 0x44;
    private const int FlagsAt = 0x300;
    private const int KindAt = 0x304;
    private const int UnitHolderAt = 0x18;
    private const int RecordAt = 0x8;

    private readonly IBattleHitObserver hits;
    private readonly BattlePartyMemory party;
    private readonly MessageLog? log;

    public BattleHitResultHook(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        hits = services.BattleHits;
        party = services.BattleParty;
        log = services.Log;
    }

    public string SignatureName => "BattleHitResult";

    /// <summary>Six arguments, the last two on the stack, so the shape passes them all on to the original.</summary>
    public void Observe(nint request, nint gui, nint result, nint target, nint index, nint mode, nint unused)
    {
        if (ModuleMemoryReader.ReadInt32(request + HpChangeAt) is not { } hpChange ||
            ModuleMemoryReader.ReadInt32(request + HpLeftAt) is not { } hpLeft ||
            ModuleMemoryReader.ReadInt32(result + KindAt) is not { } kindAndSub)
        {
            return;
        }
        // The kind is the low short; the high short is the sub-kind at +0x306, which picks the popup's style.
        var kind = kindAndSub & 0xFFFF;
        var hpMax = ModuleMemoryReader.ReadInt32(request + HpMaxAt);
        long? record = ModuleMemoryReader.ReadPointer(target + UnitHolderAt) is { } holder && holder != 0
            ? ModuleMemoryReader.ReadPointer(holder + RecordAt)
            : null;
        var member = record is { } known ? party.Find(known) : null;
        var side = record is null or 0 ? BattleSide.Unknown : member is null ? BattleSide.Enemy : BattleSide.Ally;
        if (member is not null && record is { } seen && hpMax is { } max)
        {
            party.Saw(seen, max);
        }
        log?.Requested(SignatureName, string.Create(CultureInfo.InvariantCulture,
            $"target=0x{target:x} record=0x{record ?? 0:x} side={side} slot={member?.Slot} unit={member?.Unit} index={index & 0xFFFF} mode={(int)mode} kind=0x{kind:x} sub=0x{(kindAndSub >> 16) & 0xFFFF:x} flags=0x{ModuleMemoryReader.ReadInt32(result + FlagsAt) ?? 0:x} hp={hpChange} hpLeft={hpLeft} hpMax={hpMax} mp={ModuleMemoryReader.ReadInt32(request + MpChangeAt)} mpLeft={ModuleMemoryReader.ReadInt32(request + MpLeftAt)}"));
        hits.Observe(new BattleHit(record is { } identity and not 0 ? identity : target, hpChange, hpLeft, kind, side,
            member?.Slot, hpMax, member?.Unit));
    }
}
