# The field's scene names a battle, and one .ecode function's load locates it

- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018)
- **Method:** Opening the shipped executable in a disassembler as-is, accepting that protected parts may be unreadable
- **Sources:** [`field-state-map-session6-2026-11-17.md`](../raw-sources/field-state-map-session6-2026-11-17.md),
  [`field-state-and-map-2026-11-16.md`](../raw-sources/field-state-and-map-2026-11-16.md)

A battle is on while the class of `[[[fldScene_Root]+0x10]+0x10]` is `fldScene_CmdBattle@fld@app`. `fldScene_Root` is
the global at `0x2ee7348`, loaded by the `mov rcx,[rip+disp32]` at `+0xd` of `0x522db0`, in `.ecode`. The status key
of #631 acts only then, so L3 reaches the world map again.

## Evidence

- **The scene.** In capture session 6 that class read `fldScene_CmdBattle` on 8 records, entering and inside a battle,
  and `fldScene_Idle`, `fldScene_Camp`, `fldScene_LargeMap`, `fldScene_LoadResource` or others everywhere else (session
  6 raw source). That it holds through the whole battle, the results screen included, and in every kind of battle
  (boss, scripted) is `[NEEDS VERIFICATION]`. A Debug build logs a `BattleScene` record with the class read on each
  status-key press, for that check.
- **The global.** `fldScene_Root` is stored at `0x2ee7348` (field state raw source).
- **The load.** The bytes at `0x522db0` are `48 83 EC 28 80 3D <disp32> 00 75 1E 48 8B 0D 84 45 9C 02 ...`: the
  `48 8B 0D` at `+0xd` ends at `0x522dc4`, and `0x522dc4 + 0x29c4584 = 0x2ee7348`. The function is a real body, not a
  jump stub into `.text1`; the mod only reads its bytes to find the global, never hooks or calls it.
- **The signature.** `FieldSceneRootRead` in `src/Metaphor.Core/hook-signatures.json` matches once on disk
  (`scripts/check.sh signatures`).

## Steps

1. `dotnet run --project tools/Metaphor.SignatureCheck -- --bytes <exe> 0x522db0 41` (.NET SDK 9.0.319), 2026-11-21:
   the bytes above.
2. `bash scripts/check.sh signatures <exe>`: one match for `FieldSceneRootRead`.
3. Read the session 6 raw source's scene table for which classes appear where.

Nothing in `.text1`, `.xcode` or `.xtext` was read.
