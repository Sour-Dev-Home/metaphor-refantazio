using System.Buffers.Binary;
using System.Globalization;
using Metaphor.Core;
using Metaphor.Core.Messages;
using Metaphor.Features.Field;

namespace Metaphor.Hooks;

/// <summary>Reads one of the field's singletons (#181): the pointer held at a global the loader locates by a
/// <c>mov reg,[rip+disp32]</c> (7 bytes) in an <c>.ecode</c> function that reads it, which it never hooks or calls. Game
/// build 548ddc95… (Steam build 18330018); the loads are in docs/findings/field-nearby-reads-static.md. Only read,
/// through <see cref="ModuleMemoryReader"/>.</summary>
internal sealed class FieldGlobal : IReadsGlobal
{
    // Set once by the loader before any hook goes live; a pointer-sized field is read and written whole.
    private nint global;

    private FieldGlobal(GlobalSpec spec) => Global = spec;

    /// <summary><c>fldWorld</c> (<c>0x2ee7510</c>), loaded at <c>+0x17</c> of the free-roam navmesh snap.</summary>
    public static FieldGlobal World() => new(new(
        "field world", [GameBuilds.Steam18330018], "FieldNavmeshSnap",
        LoadOffset: 0x17, DisplacementOffset: 3, InstructionLength: 7, TurnsOff: "the nearby list"));

    /// <summary><c>fldScene_Root</c> (<c>0x2ee7348</c>), loaded at <c>+0xd</c> of a scene-state reader.</summary>
    public static FieldGlobal SceneRoot() => new(new(
        "field scene", [GameBuilds.Steam18330018], "FieldSceneRootRead",
        LoadOffset: 0xd, DisplacementOffset: 3, InstructionLength: 7, TurnsOff: "the field keys"));

    /// <summary><c>fldDatabase</c> (<c>0x2ee7368</c>), loaded at <c>+0x9</c> of a door-kind check.</summary>
    public static FieldGlobal Database() => new(new(
        "field database", [GameBuilds.Steam18330018], "FieldDatabaseRead",
        LoadOffset: 0x9, DisplacementOffset: 3, InstructionLength: 7, TurnsOff: "the map id for where am I"));

    public GlobalSpec Global { get; }

    public void Locate(nint address) => global = address;

    /// <summary>The singleton, or null when the global is unknown, not made yet or unreadable.</summary>
    public nint? Object() =>
        global != 0 && ModuleMemoryReader.ReadPointer(global) is { } instance && instance != 0 ? instance : null;
}

/// <summary>The field around the player (#181), read on the press from the game's unit manager and copied into plain
/// values. On game build 548ddc95… (Steam build 18330018): the manager is <c>M = [fldWorld+0x20]</c>, the player
/// <c>[M]</c> with its position at <c>+0x80</c> and facing quaternion at <c>+0x90</c>; each list's <c>begin</c> and
/// <c>end</c> sit at fixed offsets in <c>M</c> and every unit's position at <c>+0x80</c>; free roam is the scene
/// <c>[[[fldScene_Root]+0x10]+0x10]</c> of class <c>fldScene_Idle</c>; the map id is <c>fldDatabase+0x48</c>
/// (docs/findings/field-position-facing-map-static.md, field-units-names-exits-static.md,
/// field-nearby-unit-names-static.md, field-player-frame-hook-static.md). A person's name is read only for NpcID kind 7,
/// whose index sits in the unit; the other kinds go through tables or calls not checked yet. Every read goes through
/// <see cref="ModuleMemoryReader"/>; nothing is hooked, called or written.</summary>
internal sealed class FieldMemory(
    FieldGlobal world, FieldGlobal sceneRoot, FieldGlobal database, DataTableRead people, GameNames names,
    MenusOpenRead menus, DialogueWindowPointer window) : IFieldReads
{
    private const string IdleScene = "fldScene_Idle@fld@app";
    private const int Manager = 0x20;
    private const int Position = 0x80;
    private const int Facing = 0x90;
    private const int MapIdOffset = 0x48;
    // 0x5ab030 returns this int when it isn't -1; otherwise it reads a table through .text1, which stays unread.
    private const int DoorReachOffset = 0x58;
    private const int MaxPerList = 1024;

    // Each list's begin offset in M; its end is 8 bytes on (docs/raw-sources/field-actors-2026-11-16.md).
    private static readonly (FieldKind Kind, int Begin)[] Lists =
    [
        (FieldKind.Enemy, 0x18), (FieldKind.Person, 0x98), (FieldKind.Chest, 0x118), (FieldKind.Door, 0x158),
    ];

    // fldNpc: the NpcID kind at +0x3630 and, for kind 7, the NpcID index at +0x365c (FldNpcGetCharaID's 0x67a650).
    private const int NpcBlock = 0x3630;
    private const int NpcBlockSize = 0x30;
    private const int NpcDirectKind = 7;
    private const int NpcDirectIndex = 0x2c;
    // NpcID.bin: 0x20-byte entries, up to 0x99d, the CharaID index a u16 at +0x4 (0xffff for none).
    private const int NpcEntrySize = 0x20;
    private const int NpcLast = 0x99d;
    // fldEnemy's u16 at +0x3634 indexes the enemy row (FldEnemyGetEnemyID's 0x65f8c0); logged only.
    private const int EnemyRow = 0x3634;
    // fldGmcDoor's link row index (FldDoorGetLinkFlag_ED's 0x699c00), -1 for none; logged only.
    private const int DoorLink = 0x28c4;

    private readonly CallerAddress module = new();
    // The idle scene's vtable once its class name has matched; a pointer-sized field is read and written whole.
    private nint idleVtable;

    public bool InFreeRoam() =>
        !menus.IsOpen() && window.Read() is null or 0 && SceneIsIdle();

    public int? MapId() =>
        database.Object() is { } db ? ModuleMemoryReader.ReadInt32(db + MapIdOffset) : null;

    public FieldView? Read()
    {
        if (UnitManager() is not { } manager || Pose(manager) is not { } pose)
        {
            return null;
        }
        var units = new List<FieldUnit>();
        foreach (var (kind, begin) in Lists)
        {
            ReadList(manager, kind, begin, (pose.X, pose.Y, pose.Z), units);
        }
        int? doorValue = database.Object() is { } db && ModuleMemoryReader.ReadInt32(db + DoorReachOffset) is { } value &&
            value != -1 ? value : null;
        return new FieldView(pose.X, pose.Y, pose.Z, pose.Heading, units, doorValue, MapId());
    }

    // The beacon's poll (#183): the same reads as Read's player, and no allocation.
    public FieldPose? ReadPose() => UnitManager() is { } manager ? Pose(manager) : null;

    private nint? UnitManager() =>
        world.Object() is { } fieldWorld && ModuleMemoryReader.ReadPointer(fieldWorld + Manager) is { } manager &&
        manager != 0 ? manager : null;

    private static FieldPose? Pose(nint manager)
    {
        if (ModuleMemoryReader.ReadPointer(manager) is not { } player || player == 0 ||
            Vector(player + Position) is not { } at ||
            ModuleMemoryReader.ReadVector4(player + Facing) is not (float x, float y, float z, float w))
        {
            return null;
        }
        // The session-6 reader's heading (docs/findings/field-position-facing-map-static.md).
        var heading = Math.Atan2(2 * (x * z + w * y), 1 - 2 * (x * x + y * y));
        return new FieldPose(at.X, at.Y, at.Z, heading);
    }

    private bool SceneIsIdle()
    {
        if (sceneRoot.Object() is not { } root || ModuleMemoryReader.ReadPointer(root + 0x10) is not { } holder ||
            holder == 0 || ModuleMemoryReader.ReadPointer(holder + 0x10) is not { } scene || scene == 0 ||
            ModuleMemoryReader.ReadPointer(scene) is not { } vtable)
        {
            return false;
        }
        // A vtable names its class for as long as the module is loaded, so once the idle scene's is known the beacon's
        // polls (#183) compare it instead of reading the class name again.
        if (vtable != 0 && vtable == idleVtable)
        {
            return true;
        }
        var idle = RttiName.Of(
            scene,
            module.ModuleBase,
            module.ModuleSize,
            address => ModuleMemoryReader.ReadPointer((nint)address),
            address => ModuleMemoryReader.ReadInt32((nint)address),
            address => ModuleMemoryReader.ReadAnsiString((nint)address, 128)) == IdleScene;
        if (idle)
        {
            idleVtable = vtable;
        }
        return idle;
    }

    private void ReadList(nint manager, FieldKind kind, int begin, (float X, float Y, float Z) player, List<FieldUnit> units)
    {
        if (ModuleMemoryReader.ReadPointer(manager + begin) is not { } first ||
            ModuleMemoryReader.ReadPointer(manager + begin + 8) is not { } end || first == 0 || end < first)
        {
            return;
        }
        var count = (int)Math.Min((end - first) / 8, MaxPerList);
        var pointers = new byte[count * 8];
        if (!ModuleMemoryReader.ReadBytes(first, pointers))
        {
            return;
        }
        for (var i = 0; i < count; i++)
        {
            var unit = (nint)BinaryPrimitives.ReadInt64LittleEndian(pointers.AsSpan(i * 8));
            if (unit == 0 || Vector(unit + Position) is not { } at)
            {
                continue;
            }
            var dx = at.X - player.X;
            var dz = at.Z - player.Z;
            // Only what the list can say: names are read for units within its radius.
            if (dx * dx + dz * dz > Nearby.Radius * Nearby.Radius)
            {
                continue;
            }
            var (name, raw) = Identify(kind, unit);
            units.Add(new FieldUnit(kind, at.X, at.Y, at.Z, name, raw));
        }
    }

    private (string? Name, string Raw) Identify(FieldKind kind, nint unit)
    {
        switch (kind)
        {
            case FieldKind.Person:
                var block = new byte[NpcBlockSize];
                if (!ModuleMemoryReader.ReadBytes(unit + NpcBlock, block))
                {
                    return (null, "npc=?");
                }
                var npcKind = BinaryPrimitives.ReadInt32LittleEndian(block);
                var index = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(NpcDirectIndex));
                int? chara = null;
                var entry = new byte[NpcEntrySize];
                if (npcKind == NpcDirectKind && index is >= 0 and <= NpcLast && people.ReadEntry(index, NpcEntrySize, entry))
                {
                    chara = BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(4));
                }
                var raw = string.Create(CultureInfo.InvariantCulture,
                    $"npcKind={npcKind} npcIndex={index} chara={chara?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
                return (chara is { } character ? names.Character(character) : null, raw);
            case FieldKind.Enemy:
                return (null, string.Create(CultureInfo.InvariantCulture,
                    $"enemyRow={Number(ModuleMemoryReader.ReadInt32(unit + EnemyRow) & 0xffff)}"));
            case FieldKind.Door:
                return (null, string.Create(CultureInfo.InvariantCulture,
                    $"link={Number(ModuleMemoryReader.ReadInt32(unit + DoorLink))}"));
            default:
                return (null, "");
        }
    }

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";

    private static (float X, float Y, float Z)? Vector(nint address)
    {
        if (ModuleMemoryReader.ReadVector3(address) is not (float x, float y, float z))
        {
            return null;
        }
        return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) ? (x, y, z) : null;
    }
}
