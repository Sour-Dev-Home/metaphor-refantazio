# The tutorial table maps each tutorial number to its title's and pages' label hashes

- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018)
- **Method:** Reading the game's data files from `base.cpk` offline, as
  [`voice-clip-families.md`](voice-clip-families.md) did; no ADR 0002 row covers data files, which no protection
  guards. The live check is our own Reloaded-II mod's message log.
- **Sources:** [`cri-cpk-table-layout-2026-11-21.md`](../raw-sources/cri-cpk-table-layout-2026-11-21.md),
  [`tutorial-popup-by-ear-2026-11-21.md`](../raw-sources/tutorial-popup-by-ear-2026-11-21.md)

For #118: `COMMON/ui/tutorial/TutorialIDPC.bin` holds one entry per tutorial number, and each entry holds the MD5 of
its title's label and of up to four page labels. Every hash names a label of `System_Tutorial_1.msg` (titles) or
`System_Tutorial_2.msg` (pages), which the mod's message library already finds by hash. Tutorial 46, the pop-up of the
by-ear run, names a title and three pages: the run's page count. The mod reads the table from the archive at run time
(`src/Metaphor.Core/TutorialTable.cs`); nothing from it is committed.

## Evidence

1. **Layout.** `TUTO`, a 32-bit count at `+0x8` (206, which matches the open's loop bound `0xce` in
   [`tutorial-popup-window-static.md`](tutorial-popup-window-static.md)), then 206 entries of `0xE0` bytes from `+0x10`
   (`0x10 + 206 × 0xE0` = 46,160, the file's size). An entry: a 32-bit field (0 to 6), a 16-byte hash at `+0x4`, then four `0x32`-byte slots from `+0x14`: a 16-byte hash, 32 bytes
   (all zero in all 206 entries; probably a movie name, by the open's per-page movie lookup `[NEEDS VERIFICATION]`),
   then a 16-bit field (`0xffff` or another of 156 values); the entry's last 32-bit field holds 0, 1 or `0x100`.
   What those fields mean is `[NEEDS VERIFICATION]`.
2. **The hashes are labels.** All 206 titles and every used slot match the MD5 of a label in the two English tutorial
   files; none is left unmatched. An unused slot holds the MD5 of the empty string. `TutorialID.bin`, the same size,
   is the console twin: the exe names only the PC file (one string, at `0x17e8830`; no `.ecode` instruction loads it, so
   it is read from `.text1`).
3. **Numbers follow `COMMON/event/Tutorial.dat`.** That file lists 206 `Name = number` lines, 0 to 205, in the table's
   order; 137 of the names give the title label by inserting `Title`, but 69 don't (`_00` to `_10` number one apart,
   and families with sub-numbers), so the table, not the name, is the mapping.
4. **Tutorial 46.** Its entry names a title and three pages, the first page's label a PC variant. The by-ear run heard
   `id=46` and three pages (model `+0x8`'s second field) on the pop-up whose title matches that title's message.

## Steps

Python 3.12.10, throwaway scripts kept outside the repository (not committed), on 2026-11-21:

1. Listed `base.cpk`'s table of contents (layout in the raw source) for names containing `tutorial`, and extracted
   `COMMON/event/Tutorial.dat`, `COMMON/ui/tutorial/TutorialID.bin`, `TutorialIDPC.bin`, `Tutorial_Data.bin` and
   `EN/message/system/System_Tutorial_1.msg` and `_2.msg` (all stored, none compressed) to a scratch folder outside
   the repository.
2. Hashed every `@label` of the two message files with MD5 (UTF-8) and decoded each `TutorialIDPC.bin` entry against
   them, then compared each entry's title label with its `Tutorial.dat` name.
3. Found the exe's string `COMMON/ui/tutorial/TutorialIDPC.bin` (file offset `0x17e7643`, `.udata`), and scanned
   `.ecode` for RIP-relative `lea`/`mov` operands and absolute pointers to it: none.
4. `src/Metaphor.Core/CpkArchive.cs` and `TutorialTable.cs`, read through `dotnet fsi` against the archive: 206
   entries, tutorial 46's title hash equal to `MessageLibrary.LabelHash` of its title label, three pages.

No game text is quoted; the scratch copies were deleted.
