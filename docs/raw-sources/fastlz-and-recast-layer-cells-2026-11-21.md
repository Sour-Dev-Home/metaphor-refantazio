# FastLZ's decompressor, and how a Recast tile layer's cells sit in the world (#179)

- **Source:** two GitHub repositories, read through the GitHub API (`gh api repos/<repo>/contents/<file>?ref=<sha>`);
  nothing was built or run.
  - `ariya/FastLZ` (MIT, `LICENSE.MIT`) at `b1342dabcf5257ab303743c9332fe75e9147a011` (2024-08-02): `fastlz.c`,
    `LICENSE.MIT`.
  - `recastnavigation/recastnavigation` (Zlib) at `9f4ce64458dfae86e1239c525ddc219c4e9e06f1`, the commit
    `recast-tile-cache-set-and-atl-header-2026-11-21.md` (#639) read:
    `Recast/Include/Recast.h`, `Recast/Source/RecastLayers.cpp`, `DetourTileCache/Source/DetourTileCacheBuilder.cpp`,
    `DetourTileCache/Include/DetourTileCache.h`, `RecastDemo/Source/Sample_TempObstacles.cpp`.
- **Saved:** 2026-11-21.
- **Game build:** none; these are the libraries. The game's files are checked against them in
  `docs/findings/field-navmesh-files-tile-cache-static.md` (#639).

`src/Metaphor.Core/Navigation/FastLz.cs` ports FastLZ's decompressor and keeps its MIT notice. The rest is the layout
in our words with short quotes.

## FastLZ

- `LICENSE.MIT`: "Copyright (C) 2005-2020 Ariya Hidayat", the MIT permission notice, which "shall be included in all
  copies or substantial portions of the Software".
- `fastlz_decompress` (line 490): the level is `(first byte >> 5) + 1`; 1 and 2 are decoded, any other level returns
  0, the error. The first control byte is the first byte `& 31`.
- `fastlz1_decompress` (lines 283-320): a control byte below 32 is a literal run of `ctrl + 1` bytes. Otherwise
  `len = (ctrl >> 5) - 1`, and when it is 6 the next byte is added to it; the next byte is the low eight bits of the
  distance, `(ctrl & 31) << 8` the high five; the match copies `len + 3` bytes from `op - distance - 1`, byte by byte
  (it may overlap its own output). Each step checks `op + len <= op_limit` and `ref >= output` and returns 0 when
  either fails. The loop ends when the input is used up.
- `fastlz2_decompress` (lines 429-480): the same, except that a length of 6 adds bytes while each is 255, and a match
  whose distance byte is 255 with high bits 31 reads two more bytes as a 16-bit distance, counted from
  `op - ofs - 8191 - 1` (`MAX_L2_DISTANCE` 8191, line 135).
- `fastlz_compress` (line 482) picks level 1 for input under 65,536 bytes, so a 768-byte tile is level 1.
- Recast's sample compresses with `fastlz_compress` and decompresses with `fastlz_decompress` into a buffer of
  `maxBufferSize` (`Sample_TempObstacles.cpp` lines 356-380).

## A tile layer's cells

- `Sample_TempObstacles.cpp` lines 1480-1484: `TileCacheTileHeader` is `dtCompressedTileRef tileRef; int dataSize;`,
  and `DetourTileCache.h` line 7 has `typedef unsigned int dtCompressedTileRef;`, so the entry header is 8 bytes.
- `Sample_TempObstacles.cpp` lines 535-540: a tile's bounds are `bmin[0] + tileX * tcs` and `bmin[2] + tileY * tcs`,
  `tcs = tileSize * cs`: tile x runs along world x and tile y along world z.
- `RecastLayers.cpp` lines 497-507: a layer is the heightfield less its border (`lw = w - borderSize*2`), and its
  `bmin` moves in by the border, so cell `(x, y)` covers `bmin[0] + x*cs` and `bmin[2] + y*cs`. Lines 564-575:
  `layer->bmin[1] = bmin[1] + hmin*ch`; line 610: `heights[idx] = s.y - hmin`, so a cell's floor is
  `bmin[1] + height*ch`. Line 609: `idx = x+y*lw`.
- `RecastLayers.cpp` lines 615-644: bit `dir` of the low four (`con`) is set only when the neighbour is walkable, in
  the same layer, and inside this layer (`nx >= 0 && ny >= 0 && nx < lw && ny < lh`); bit `dir` of the high four
  (`portal`) when the neighbour is walkable in another layer, with no such bounds test. So a walkable neighbour in the
  next tile sets neither bit.
- `Recast.h` lines 1253-1267: `rcGetDirOffsetX` is `{ -1, 0, 1, 0 }` and `rcGetDirOffsetY` `{ 0, 1, 0, -1 }`, the
  second along world z ("Rename this to rcGetDirOffsetZ", line 1259). `DetourTileCacheBuilder.cpp` lines 49-57 has the
  same tables as `getDirOffsetX` and `getDirOffsetY`.
- `DetourTileCacheBuilder.cpp` lines 441-450: a cell's `con` and `portal` are `cons[ia] & 0xf` and `cons[ia] >> 4`.
