namespace Metaphor.Core.Battle;

/// <summary>Whether a battle is on (#631), for keys that belong to battle only.</summary>
public interface IBattleScene
{
    /// <summary>True only while the game reads as in a battle; false when it doesn't or the read failed.</summary>
    bool InBattle();
}

/// <summary>Tells a battle from the field's scene (#631): the class of <c>[[[fldScene_Root]+0x10]+0x10]</c> read
/// <c>fldScene_CmdBattle</c> entering and inside each battle of capture session 6, and other classes in free roam,
/// menus, the large map and loads, on build 548ddc95… (docs/raw-sources/field-state-map-session6-2026-11-17.md). That
/// it holds for the whole battle, results included, and for every kind of battle, is <c>[NEEDS VERIFICATION]</c> by
/// ear.</summary>
public static class BattleScene
{
    /// <summary>The field scene's class during a battle, as <see cref="RttiName.Of"/> names it.</summary>
    public const string CommandBattle = "fldScene_CmdBattle@fld@app";

    /// <summary>Whether <paramref name="sceneClass"/> is the battle scene; null, an unread class, is not.</summary>
    public static bool IsBattle(string? sceneClass) => string.Equals(sceneClass, CommandBattle, StringComparison.Ordinal);
}
