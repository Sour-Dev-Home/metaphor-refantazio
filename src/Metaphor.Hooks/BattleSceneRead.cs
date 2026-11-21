using Metaphor.Core;
using Metaphor.Core.Battle;

namespace Metaphor.Hooks;

/// <summary>Whether a battle is on (#631), from the field's current scene: the class of
/// <c>[[[fldScene_Root]+0x10]+0x10]</c> is <see cref="BattleScene.CommandBattle"/> in a battle
/// (docs/raw-sources/field-state-map-session6-2026-11-17.md). The global <c>fldScene_Root</c> (<c>0x2ee7348</c>,
/// docs/raw-sources/field-state-and-map-2026-11-16.md) is loaded by the <c>mov rcx,[rip+disp32]</c> (7 bytes) at
/// <c>+0xd</c> of <c>0x522db0</c>, an <c>.ecode</c> function the loader locates by its signature and only reads, never
/// hooks or calls (docs/findings/battle-scene-signal-static.md). Game build 548ddc95… (Steam build 18330018). Every read
/// goes through <see cref="ModuleMemoryReader"/>; nothing is written. A Debug build logs each answer with the class it
/// read, for the by-ear check of #631.</summary>
internal sealed class BattleSceneRead : IReadsGlobal, IBattleScene
{
    private const int SceneHolder = 0x10;
    private const int Scene = 0x10;
    private const int LongestClassName = 128;

    private static readonly GlobalSpec Spec = new(
        "battle scene", [GameBuilds.Steam18330018], "FieldSceneRootRead",
        LoadOffset: 0xd, DisplacementOffset: 3, InstructionLength: 7, TurnsOff: "the battle status key");

    private readonly CallerAddress module = new();

    // Set once by the loader before any hook goes live; a pointer-sized field is read and written whole.
    private nint rootGlobal;

    /// <summary>Where a Debug build logs each answer; null for none.</summary>
    public MessageLog? Log { get; set; }

    public GlobalSpec Global => Spec;

    public void Locate(nint address) => rootGlobal = address;

    public bool InBattle()
    {
        var sceneClass = SceneClass();
        var battle = BattleScene.IsBattle(sceneClass);
        Log?.Requested("BattleScene", $"class={sceneClass ?? "none"} battle={battle}");
        return battle;
    }

    // The field scene's class, or null when the global is unknown, the scene doesn't exist or a read failed.
    private string? SceneClass()
    {
        if (rootGlobal == 0 || ModuleMemoryReader.ReadPointer(rootGlobal) is not { } root || root == 0 ||
            ModuleMemoryReader.ReadPointer(root + SceneHolder) is not { } holder || holder == 0 ||
            ModuleMemoryReader.ReadPointer(holder + Scene) is not { } scene || scene == 0)
        {
            return null;
        }
        return RttiName.Of(
            scene,
            module.ModuleBase,
            module.ModuleSize,
            address => ModuleMemoryReader.ReadPointer((nint)address),
            address => ModuleMemoryReader.ReadInt32((nint)address),
            address => ModuleMemoryReader.ReadAnsiString((nint)address, LongestClassName));
    }
}
