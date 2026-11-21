# The party's slots hold unit numbers, and each unit sits at a fixed place in the game data block

- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018)
- **Method:** Opening the shipped executable in a disassembler as-is, accepting that protected parts may be unreadable
- **Sources:** none, see Steps; builds on
  [`battle-turn-icons-and-ui-components-static.md`](battle-turn-icons-and-ui-components-static.md) and
  [`battle-hit-result-hp-left-static.md`](battle-hit-result-hp-left-static.md)

Party slot `s` holds a unit number at `G+0x1c748 + s × 4`, 0 for an empty slot. Unit `n` is at `G+0x228 + n × 0x16A8`.
`G` is the same block the mod's story-flag read already uses. So a hit's target is a party member exactly when its unit
record is one of the slots' units. For #247's hit summary and status key. Static only. Which name goes with a unit
number is not known.

## Evidence

- **Slots.** `0x3b0e30(s)` returns the dword at `G+0x1c748 + s × 4`. `0x3e5ac0` calls it in a loop over slots, skips
  a 0, and passes the number to `0x3b0eb0`.
- **Units.** `0x3b0eb0(n)` returns `G+0x228 + n × 0x16A8`, the address the turn-icons note calls party unit `n`. The units
  end where the slots begin, so numbers 1 to 19 fit (`(0x1c748 − 0x228) / 0x16A8` is 20).
- **The same `G`.** `0x3b0eb0` reads the pointer at `0x272e310`. The story-flag test `0x3b0ab0` loads it too: its
  `mov rcx,[rip+disp32]` at `+0x15` (`48 8B 0D 44 D8 37 02`) targets `0x3b0acc + 0x237d844 = 0x272e310`.
- **Four slots.** The party holds four members, as the Party menu shows
  ([`menu-screens-inventory-2026-11-17.md`](../raw-sources/menu-screens-inventory-2026-11-17.md)). The loop bound in
  `0x3e5ac0` wasn't read. `[NEEDS VERIFICATION]` whether battle reads more slots.
- **Not found: names.** A name helper near the serial lookup, `0xefd980`, turns an id into a name through `0x3c9b70` and
  `0x3ca570`, a virtual lookup that wasn't followed. Which numbering those ids use, and whether a unit number is a
  `CharaID.bin` or `FollowerTable.bin` index, is `[NEEDS VERIFICATION]`. So the mod names a party member by slot. A
  Debug build logs each hit's slot and unit, so a by-ear run can pair them with names.

## Steps

Ghidra 12.2 shared project (`scripts/dev/ghidra.ps1`), build above, 2026-11-21:

1. `decompile 0x3b0eb0`, `decompile 0x3b0e30`, `decompile 0x3e5ac0` (its first loop).
2. `tools/Metaphor.SignatureCheck --bytes <exe> 0x3b0ac5 8`: the load of `G` in the flag test.
3. `decompile 0xefd980`, `0x3c9b70`, `0x3ca570`, and `callers 0xa01bc0`: the name path, not followed further.

Nothing in `.text1`, `.xcode` or `.xtext` was decompiled.
