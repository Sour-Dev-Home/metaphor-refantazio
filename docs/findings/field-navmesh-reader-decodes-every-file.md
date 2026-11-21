# The mod's `.NAV` reader decodes every tile of every field navmesh file

- **Game build:** `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018)
- **Method:** Reading the game's data files from `base.cpk` offline, as
  `docs/findings/field-navmesh-files-tile-cache-static.md` (#639) does; nothing extracted or kept
- **Sources:** [`fastlz-and-recast-layer-cells-2026-11-21.md`](../raw-sources/fastlz-and-recast-layer-cells-2026-11-21.md),
  `recast-tile-cache-set-and-atl-header-2026-11-21.md` (#639)

`NavMeshFile` (`src/Metaphor.Core/Navigation/`, #179) reads all 374 `.NAV` files in `COMMON/model/field/navi/`, and
every tile of each file's first run decodes: 433,629 tiles, none skipped. The five files where #639's throwaway decoder
failed a tile (`f_0001_003`, `f_0155_004`, `f_0183_042`, `f_3040_001`, `f_3040_002`) decode whole, so that was the
decoder's fault, not a variant of the format.

## Evidence

- 374 files parse; 373 have tiles and one has none, as #639 found. Every first run holds the header's `numTiles`.
- Two files are far larger than the rest: `f_0150_001.NAV` (31,671,784 bytes, 3,607 tiles) and `f_1063_008.NAV`
  (47,472,558 bytes, 4,765 tiles). The reader's size cap is 64 MB for them. What fills them, the block before the
  tiles or the later runs, wasn't measured `[NEEDS VERIFICATION]`.
- In `f_0000_014.NAV` (283 tiles, cell size 15, climb 70): the walk graph has 196,198 links, 12,000 of them across a
  tile edge or between layers, every one symmetric (if A links to B in a direction, B links back to A). The cells form
  10 groups, the largest 47,745 cells. Reading the file from the archive took 6 ms, the table of contents included.
- Cross-tile links are the reader's own rule (the nearest walkable floor within the climb); upstream Recast sets no
  bit for them. Whether they match where the player can walk is `[NEEDS VERIFICATION]` by ear.

## Steps

.NET SDK 9.0.319, 2026-11-21: a throwaway console program outside the repository, referencing the branch's
`Metaphor.Core.dll`, read the archive's table of contents with `UtfTable`, passed each `.NAV` file's bytes to
`NavMeshFile.Parse` and counted files, tiles and skipped tiles. For `f_0000_014.NAV` it read the file with
`NavMeshFile.FromArchive`, timed it, built a `WalkGraph`, and counted links, cross-tile links, asymmetric links and
flood-filled groups. It printed counts only.
