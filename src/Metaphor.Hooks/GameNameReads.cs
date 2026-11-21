using Metaphor.Core;
using Metaphor.Core.Messages;

namespace Metaphor.Hooks;

/// <summary>Reads the game-data block <c>G</c> (#81, #107): the story flags and the typed names. <c>G</c> is the pointer
/// at the global the <c>.ecode</c> flag test (<c>0x3b0ab0</c>) loads with <c>mov rcx,[rip+disp32]</c> (7 bytes) at
/// <c>+0x15</c>; its signature keeps the flags' offset <c>0x22c5c</c>. The names' offsets come from the new-game reset
/// (docs/raw-sources/game-names-static-2026-11-20.md) and session 1 (docs/findings/player-name-in-game-data.md). Game
/// build 548ddc95… (Steam build 18330018). Only read, through <see cref="ModuleMemoryReader"/>; nothing is hooked,
/// called or written.</summary>
internal sealed class GameDataRead : IReadsGlobal
{
    private const int Flags = 0x22c5c;
    private const int PlayerName = 0x24770;
    // [NEEDS VERIFICATION] by ear: the protagonist's by elimination (docs/raw-sources/game-names-static-2026-11-20.md).
    private const int HeroName = 0x2472c;
    private const int LastFlag = 0xfffe;

    private static readonly GlobalSpec Spec = new(
        "game data", [GameBuilds.Steam18330018], "StoryFlagTest",
        LoadOffset: 0x15, DisplacementOffset: 3, InstructionLength: 7, TurnsOff: "speaker aliases and typed names");

    // Set once by the loader before any hook goes live; a pointer-sized field is read and written whole.
    private nint global;

    public GlobalSpec Global => Spec;

    public void Locate(nint address) => global = address;

    /// <summary>Whether story flag <paramref name="flag"/> is set: bit <c>flag &amp; 31</c> of the dword at
    /// <c>G + 0x22c5c + (flag &gt;&gt; 5) × 4</c>; null when unknown.</summary>
    public bool? StoryFlag(int flag)
    {
        if (flag is < 0 or > LastFlag || Data() is not { } data ||
            ModuleMemoryReader.ReadInt32(data + Flags + (flag >> 5) * 4) is not { } word)
        {
            return null;
        }
        return (word & (1 << (flag & 31))) != 0;
    }

    /// <summary>The field of a typed name, or null when unknown.</summary>
    public byte[]? TypedName(TypedName name)
    {
        if (Data() is not { } data)
        {
            return null;
        }
        var field = new byte[TypedNameText.Size];
        return ModuleMemoryReader.ReadBytes(data + (name == Core.Messages.TypedName.Player ? PlayerName : HeroName), field)
            ? field
            : null;
    }

    // The party (#247), in the same block: unit n at G+0x228 + n × 0x16A8 (0x3b0eb0), party slot s's unit number at
    // G+0x1c748 + s × 4, 0 for none (0x3b0e30, which 0x3e5ac0 calls per slot before 0x3b0eb0). Static, build 548ddc95…
    // (docs/findings/battle-party-units-static.md). The units end where the slots begin, so unit numbers run 1 to 19.
    private const int Units = 0x228;
    private const int UnitSize = 0x16A8;
    private const int PartySlots = 0x1c748;
    private const int LastUnit = (PartySlots - Units) / UnitSize - 1;

    /// <summary>The party slots read: four, the party's size (docs/raw-sources/menu-screens-inventory-2026-11-17.md).
    /// </summary>
    public const int SlotCount = 4;

    /// <summary>The unit number party slot <paramref name="slot"/> holds, or null when empty or unreadable.</summary>
    public int? SlotUnit(int slot) =>
        slot is >= 0 and < SlotCount && Data() is { } data &&
        ModuleMemoryReader.ReadInt32(data + PartySlots + slot * 4) is { } unit && unit is > 0 and <= LastUnit
            ? unit
            : null;

    /// <summary>Party unit <paramref name="unit"/>'s address, or null when unknown.</summary>
    public nint? UnitAddress(int unit) =>
        unit is > 0 and <= LastUnit && Data() is { } data ? data + Units + unit * UnitSize : null;

    private nint? Data() =>
        global != 0 && ModuleMemoryReader.ReadPointer(global) is { } data && data != 0 ? data : null;
}

/// <summary>Reads one of the game's data tables loaded from <c>COMMON/init/data</c> (#81, #107): the pointer at a
/// global, then its entries from <c>[[global]+0x40]+0x10</c>, as the table getters <c>0x898df0</c> and
/// <c>0x898e80</c> read them. The global is loaded with <c>lea rcx,[rip+disp32]</c> (7 bytes) in the <c>.ecode</c>
/// function that checks each table is loaded (<c>0x89b3b0</c>; docs/raw-sources/game-names-static-2026-11-20.md).
/// Game build 548ddc95… (Steam build 18330018). Only read, through <see cref="ModuleMemoryReader"/>.</summary>
internal sealed class DataTableRead : IReadsGlobal
{
    private const int Entries = 0x40;
    private const int FirstEntry = 0x10;

    // Set once by the loader before any hook goes live; a pointer-sized field is read and written whole.
    private nint global;

    private DataTableRead(GlobalSpec spec) => Global = spec;

    /// <summary><c>CharaID.bin</c>, whose load is at <c>+0x4</c>.</summary>
    public static DataTableRead Characters() => new(new(
        "character name table", [GameBuilds.Steam18330018], "ResourceTablesLoaded",
        LoadOffset: 0x4, DisplacementOffset: 3, InstructionLength: 7, TurnsOff: "speaker aliases and character names"));

    /// <summary><c>FollowerTable.bin</c>, whose load is at <c>+0xa4</c>.</summary>
    public static DataTableRead Followers() => new(new(
        "follower table", [GameBuilds.Steam18330018], "ResourceTablesLoaded",
        LoadOffset: 0xa4, DisplacementOffset: 3, InstructionLength: 7, TurnsOff: "follower names"));

    /// <summary><c>NpcID.bin</c> (#181), whose load is at <c>+0x2c</c>: its global <c>0x2eebc38</c> is the one
    /// <c>FldNpcGetCharaID</c>'s table getter <c>0x8990c0</c> reads (docs/findings/field-units-names-exits-static.md;
    /// docs/findings/field-nearby-reads-static.md).</summary>
    public static DataTableRead People() => new(new(
        "person table", [GameBuilds.Steam18330018], "ResourceTablesLoaded",
        LoadOffset: 0x2c, DisplacementOffset: 3, InstructionLength: 7, TurnsOff: "people's names in the nearby list"));

    public GlobalSpec Global { get; }

    public void Locate(nint address) => global = address;

    /// <summary>Fills <paramref name="buffer"/> from entry <paramref name="index"/> of <paramref name="size"/> bytes;
    /// false when unknown or unreadable.</summary>
    public bool ReadEntry(int index, int size, Span<byte> buffer)
    {
        if (global == 0 || index < 0 || ModuleMemoryReader.ReadPointer(global) is not { } table || table == 0 ||
            ModuleMemoryReader.ReadPointer(table + Entries) is not { } entries || entries == 0)
        {
            return false;
        }
        return ModuleMemoryReader.ReadBytes(entries + FirstEntry + (nint)index * size, buffer);
    }
}

/// <summary>The game memory <see cref="GameNames"/> reads, from the three globals above.</summary>
internal sealed class GameNameMemory(GameDataRead data, DataTableRead characters, DataTableRead followers)
    : IGameNameMemory
{
    // FollowerTable.bin entries are 0x1e bytes; the character number is the ushort at +0 (0xa01bc0).
    private const int FollowerSize = 0x1e;

    public byte[]? CharacterEntry(int character)
    {
        var entry = new byte[Core.Messages.CharacterEntry.Size];
        return character is >= 0 and <= Core.Messages.CharacterEntry.Last &&
            characters.ReadEntry(character, Core.Messages.CharacterEntry.Size, entry)
            ? entry
            : null;
    }

    public int? FollowerCharacter(int follower)
    {
        var number = new byte[2];
        return follower is >= 0 and <= GameNames.LastFollower && followers.ReadEntry(follower, FollowerSize, number)
            ? number[0] | number[1] << 8
            : null;
    }

    public bool? StoryFlag(int flag) => data.StoryFlag(flag);

    public byte[]? TypedName(TypedName name) => data.TypedName(name);
}
