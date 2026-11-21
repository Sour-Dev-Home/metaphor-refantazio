# CriFsV2Lib's reading of the CRI CPK container and `@UTF` table

- **Source:** <https://github.com/Sewer56/CriFsV2Lib> at commit `169b001c748dfffc28c9fc14fcec269dd45e6eec`
  (2024-02-03, the default branch's tip), fetched with `gh api repos/Sewer56/CriFsV2Lib/contents/<file>`. Files:
  `CriFsV2Lib/Structs/CriTableMetadata.cs`, `CriFsV2Lib/Structs/CriColumnFlags.cs`,
  `CriFsV2Lib/Utilities/Parsing/TableContainerReader.cs`, `CriFsV2Lib/Utilities/Parsing/TocFinder.cs`,
  `CriFsV2Lib/Utilities/Parsing/TocReader.cs`. The repository has no licence file (`gh api .../license` returns 404),
  so nothing is copied: this note records the layout in our words, with short quotes.
- **Saved:** 2026-11-21.
- **Game build:** none named by the source. Checked against `base.cpk` of game build
  `548ddc955c176867f062c44f94c03dd9ac33caefb278a5ac388e792aedf09504` (Steam build 18330018), below.

## The layout

**Container** (`TableContainerReader.ReadTable`): 16 bytes, then the table. It skips 8 bytes, reads the table's size as
a 32-bit integer at `+0x8` in the machine's order (little-endian on PC), skips 4 more, then reads that many bytes
(`TableContainerSize = 16`). A table that doesn't start `@UTF` is encrypted and decrypted first; we don't read those.

**Table header** (`CriTableMetadata`), big-endian, offsets counted from `+0x8` (`BaseOffset = 0x08`):

| Offset | Field |
|---|---|
| `+0x09` | encoding: `0` Shift-JIS, otherwise UTF-8 |
| `+0x0A` | rows offset (16-bit) |
| `+0x0C` | string pool offset (32-bit) |
| `+0x10` | data pool offset (32-bit) |
| `+0x18` | column count (16-bit) |
| `+0x1A` | row size in bytes (16-bit) |
| `+0x1C` | row count (32-bit) |

Columns start at `+0x20` (`ColumnOffset = 0x20`).

**Columns** (`CriColumnFlags.cs`): one flag byte, whose low four bits are the type
(`Byte = 0` … `Int64 = 7`, `Single = 8`, `Double = 9`, `String = 10`, `RawData = 11`, `Guid = 12`) and whose high bits
are `HasName = 16`, `HasDefaultValue = 32`, `IsRowStorage = 64`. A named column is followed by its name, a 32-bit offset
into the string pool; a column with a default is followed by the value. Value sizes (`GetValueLength`): 1, 1, 2, 2, 4,
4, 8, 8, 4, 8, 4 (a string-pool offset), 8, 16. Numbers are big-endian (`BinaryPrimitives.ReverseEndianness`).

**Rows** (`TocReader.ReadToc`): each row is `row size` bytes from the rows offset; a column stored per row takes the
next value in it, in column order, and other columns read their default.

**The archive** (`TocFinder.FindToc`): the archive's first table (at file offset 0) has one row with `TocOffset` and
`ContentOffset`; "`if (tocOffset < contentOffset) contentOffset = tocOffset;`". The table of contents (a container at
`TocOffset`) has a row per file with `DirName`, `FileName`, `FileSize`, `ExtractSize`, `FileOffset` and `UserString`;
"`file.FileOffset = column.ReadNumberLong(valuePtr) + contentOffset;`", the lower of the two offsets.

**Compression** (`CpkHelper.ExtractFileNoDecompression`): a stored file whose data starts with CRILAYLA's header is
compressed (`CriLayla.IsCompressed`); we don't read those.

## Checked on the game's archive

Python 3.12.10, a throwaway reader written to the layout above and kept outside the repository, on 2026-11-21: the
archive's first table has `TocOffset` 2048, `ContentOffset` 3,863,248, `TocSize` 3,861,176, `Files` 96,883, `Version`
7, `Revision` 14, `Tvers` "CPKMC2.50.02, DLL3.30.02"; its tables start `@UTF` (not encrypted). The table of contents
lists `COMMON/ui/tutorial/TutorialIDPC.bin` with `FileSize` equal to `ExtractSize` (46,160 bytes, stored), and its bytes
at `FileOffset + TocOffset` start with the table's own magic. `src/Metaphor.Core/CpkArchive.cs` read the same file from
the same archive (`dotnet fsi` over the Debug build, about 15 ms).
