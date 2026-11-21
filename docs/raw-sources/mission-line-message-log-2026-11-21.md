# Mission line: its messages compile at start-up, and no hooked call fetches them (#658)

- **Source:** the maintainer's by-ear run of 2026-11-21, Debug mod build (mod version `0.0.1-unstable.3`), its
  `message-log.txt` (317 922 lines, last written 12:22 local time), re-read with grep and awk on the maintainer's
  machine; the exe on disk, read with `tools/Metaphor.SignatureCheck --bytes` (this repository at the commit that adds
  this note), `grep -abo` and the shared Ghidra 12.2 project (`scripts/dev/ghidra.ps1`). The log holds game text, so it
  stays local; only labels, serials, ids, counts, addresses and class names are kept here, plus the two mission lines
  the maintainer quoted in #658.
- **Saved:** 2026-11-21.
- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018).

## Steps

1. `grep -n -i "royal capital\|provisions"` on the log, then the `compile` record before each hit.
2. Each file's messages counted from its logged source, as in
   [`quick-menu-serials-run-2026-11-20.md`](quick-menu-serials-run-2026-11-20.md) step 2.
3. Every `resolve`, `lookup` and `request` record, by kind and by serial or id; every distinct `label=(unknown)` serial.
4. `--bytes <exe> 0x9f3e00 0x1000`: every `lea eax,[rcx-imm32]` (`8D 81`) followed by a `cmp`, the message-file range
   tests of [`message-serials-from-file-positions.md`](../findings/message-serials-from-file-positions.md).
5. `grep -abo -i "mission[A-Za-z_0-9]*"` and `.?AV…Mission…` type names on the exe; file offsets turned into RVAs with
   the section table; then `ghidra.ps1 global`, `vtable` and `decompile` on what they named.

## What it shows

- **The mission lines are `MissionText_` messages in four system files.** `EN/message/system/Mission_Message_1.msg` to
  `_4.msg` compile at start-up (compile ids 25–28, 8146–8147 ms), with 12, 83, 101 and 61 messages. The two lines
  quoted in #658 are there: "Queue up for provisions" is `MissionText_Field_01_03_00` (`_2.msg`) and "Reach the Royal
  Capital" is `MissionText_Dungeon00_01` (`_3.msg`). Each body ends with `<WAIT>`.
- **No hooked call fetched one.** The run has 497 `resolve via=GetMessageBySerial`, 20 `lookup` (all `id=3`, the map
  names) and 166 `request` records. None names a `MissionText_` label or a mission line's text; outside the compile
  records neither quoted line appears at all, though the maintainer saw the line change in that run.
- **The mission files have no range test where the others do.** The range tests at `0x9f4b00`–`0x9f4c70` give runs of
  14, 14, 39, 13, 119, … messages from serial 9242 on: `BadStatus_Message_1` and `_2`, `System_General`,
  `FastBattle_Message`, then `ShopName_Message` (119) at 9322. None holds 12, 83, 101 or 61 messages, so the mission
  files' serials, if any, aren't there `[NEEDS VERIFICATION]`. The `label=(unknown)` serials the run resolved fall in
  the files before (`CommandBattle`, `Equip`, `System_General`), not after 9321.
- **The HUD's mission box is its own UI class.** The exe holds `app::ui::UUIC_Field_Mission` (vtable `0x2259380`) and
  `app::ui::layout::field::mission::MainMission` (vtable `0x21aadf8`), with `SubMediumMission`, `SubSmallMission` and
  `FieldMissionLayout`. `MainMission`'s slots 17, 18 and 25 (`0xc993b0`, `0xc998e0`, `0xc99c70`) call through thunks
  into `.text1` and read globals there; the string `MissionText_Field_Map` (RVA `0x180d5e8`, beside
  `game\mission\mission.cpp` and `game_mission`) has no reference in analysed code. So the line's text is most likely
  fetched in `.text1`, which is off-limits `[NEEDS VERIFICATION]`. `app::dat::Mission::vf0` (`0x3c6100`) reads the
  path of `COMMON/mission/source/bin/Mission_Main_Data.bin`, one of four mission data tables (`Main`, `Field`,
  `Dungeon`, `Battle`).

## What it leaves open

- Which call carries the line's text as the HUD draws it, and when it changes. Next lead: a Debug run with the
  `ui-text-panes` opt-in, which #658 wires to the mission line: a `field mission label=… changed` record beside a
  mission change means the line is a text pane.
- Whether the mission data tables name the current mission by label or index, which would let a read, not a hook, say
  it (#191).
