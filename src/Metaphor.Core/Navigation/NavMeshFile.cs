// The layouts read here are recastnavigation's (Zlib, recastnavigation/recastnavigation at 9f4ce644, files
// DetourTileCacheBuilder.h, Sample_TempObstacles.cpp, RecastLayers.cpp) and Rirurin's reading of the Atlus header
// (GPL-3.0, rirurin/recast-navigation-rs at 8a9f8065, input_geometry.rs). No code is copied from either.
using System.Buffers.Binary;
using System.Numerics;

namespace Metaphor.Core.Navigation;

/// <summary>One decoded tile layer (#179): a grid of <see cref="Width"/> × <see cref="Height"/> cells, each with a
/// floor height, an area id and its neighbour bits, as Recast's <c>dtTileCacheLayerHeader</c> and its three byte grids
/// lay them out (docs/raw-sources/recast-tile-cache-set-and-atl-header-2026-11-21.md, from #639). Cell
/// <c>(x, y)</c> is at index <c>x + y * Width</c> and covers world <c>Min.X + x * cs</c> by <c>Min.Z + y * cs</c>
/// (docs/raw-sources/fastlz-and-recast-layer-cells-2026-11-21.md).</summary>
public sealed class NavTile
{
    private readonly byte[] grids;

    internal NavTile(int tx, int ty, int layer, Vector3 min, Vector3 max, int width, int height, byte[] grids)
    {
        Tx = tx;
        Ty = ty;
        Layer = layer;
        Min = min;
        Max = max;
        Width = width;
        Height = height;
        this.grids = grids;
    }

    /// <summary>The tile's column and row in the tile grid, and its layer (floors stacked in one tile).</summary>
    public int Tx { get; }

    /// <inheritdoc cref="Tx"/>
    public int Ty { get; }

    /// <inheritdoc cref="Tx"/>
    public int Layer { get; }

    /// <summary>The layer's bounds; <c>Min.Y</c> is the floor that cell heights count from.</summary>
    public Vector3 Min { get; }

    /// <inheritdoc cref="Min"/>
    public Vector3 Max { get; }

    /// <summary>The grid's size in cells.</summary>
    public int Width { get; }

    /// <inheritdoc cref="Width"/>
    public int Height { get; }

    /// <summary>Cell <c>(x, y)</c>'s floor, in cell heights above <c>Min.Y</c>.</summary>
    public byte FloorHeight(int x, int y) => grids[Index(x, y)];

    /// <summary>Cell <c>(x, y)</c>'s area id: 0 is not walkable, 63 Recast's plain walkable area, others the game's
    /// own (62 is common, its meaning [NEEDS VERIFICATION]).</summary>
    public byte Area(int x, int y) => grids[(Width * Height) + Index(x, y)];

    /// <summary>Cell <c>(x, y)</c>'s neighbour bits: bit <c>d</c> of the low four set when the neighbour in direction
    /// <c>d</c> is walkable in this layer, bit <c>d</c> of the high four when it is walkable in another layer.</summary>
    public byte Links(int x, int y) => grids[(2 * Width * Height) + Index(x, y)];

    /// <summary>True when <c>(x, y)</c> is inside the grid.</summary>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    private int Index(int x, int y) => Contains(x, y) ? x + (y * Width) : throw new ArgumentOutOfRangeException(nameof(x));
}

/// <summary>A field map's navmesh file, <c>COMMON/model/field/navi/f_&lt;a&gt;_&lt;b&gt;.NAV</c> in <c>base.cpk</c>
/// (#179): a Recast tile-cache set (magic <c>TEST</c>) in the Atlus variant, read on game build 548ddc95… (Steam build
/// 18330018) per docs/findings/field-navmesh-files-tile-cache-static.md (#639). The header's fields are at the offsets
/// that note gives; then a block nobody has described yet, which this skips by looking for the first tile; then
/// <c>numTiles</c> entries of a 4-byte ref, a 4-byte size and that many bytes, each a 0x38-byte <c>RLTD</c> layer
/// header and a FastLZ block of the three cell grids. Further runs of tiles after those
/// ([NEEDS VERIFICATION]: what they are) aren't read. A tile that doesn't decode is counted and skipped.</summary>
public sealed class NavMeshFile
{
    /// <summary>The largest file read. Most are under 1 MB; the largest of build 548ddc95… is 47,472,558 bytes
    /// (<c>f_1063_008.NAV</c>; docs/findings/field-navmesh-reader-decodes-every-file.md).</summary>
    public const int MaxFileSize = 64 * 1024 * 1024;

    /// <summary>The most tiles kept from one file; the finding's has 283.</summary>
    public const int MaxTiles = 16384;

    /// <summary>The most cells kept from one file, over all its tiles, so a hostile file can't decode to gigabytes.
    /// </summary>
    public const int MaxCells = 4 * 1024 * 1024;

    private const int AtlusVersion = 3;
    private const int UpstreamHeaderSize = 0x5c;
    private const int AtlusHeaderSize = 0xb0;
    private const int EntryHeaderSize = 8;
    private const int LayerHeaderSize = 0x38;
    private const int LayerVersion = 1;

    private NavMeshFile(int version, int tileCount, Vector3 origin, float cellSize, float cellHeight, int tileCells,
        float walkableHeight, float walkableRadius, float walkableClimb, IReadOnlyList<NavTile> tiles, int failedTiles)
    {
        Version = version;
        TileCount = tileCount;
        Origin = origin;
        CellSize = cellSize;
        CellHeight = cellHeight;
        TileCells = tileCells;
        WalkableHeight = walkableHeight;
        WalkableRadius = walkableRadius;
        WalkableClimb = walkableClimb;
        Tiles = tiles;
        FailedTiles = failedTiles;
    }

    /// <summary>The file's version: 3 for the Atlus header, 1 or 2 for upstream's.</summary>
    public int Version { get; }

    /// <summary>The tile count the header gives.</summary>
    public int TileCount { get; }

    /// <summary>The tile cache's origin (<c>cacheParams.orig</c>), where tile (0, 0) starts.</summary>
    public Vector3 Origin { get; }

    /// <summary>A cell's width and one height step, in world units (the unit [NEEDS VERIFICATION]).</summary>
    public float CellSize { get; }

    /// <inheritdoc cref="CellSize"/>
    public float CellHeight { get; }

    /// <summary>A tile's width in cells (<c>cacheParams.width</c>), 16 in the game's files.</summary>
    public int TileCells { get; }

    /// <summary>The agent the cells were built for: its height, radius (the walkable area is shrunk by it, upstream;
    /// that the game does too is [NEEDS VERIFICATION]) and the step it can climb, in world units.</summary>
    public float WalkableHeight { get; }

    /// <inheritdoc cref="WalkableHeight"/>
    public float WalkableRadius { get; }

    /// <inheritdoc cref="WalkableHeight"/>
    public float WalkableClimb { get; }

    /// <summary>The tiles that decoded, in file order.</summary>
    public IReadOnlyList<NavTile> Tiles { get; }

    /// <summary>How many of the first run's tiles didn't decode and were skipped.</summary>
    public int FailedTiles { get; }

    /// <summary>The file in <paramref name="bytes"/>, or null when it isn't a tile-cache set this reads: no
    /// <c>TEST</c> magic, a version other than 1 to 3, a header cut short, or a cell size that isn't a positive
    /// number. Never throws.</summary>
    public static NavMeshFile? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxFileSize || bytes.Length < UpstreamHeaderSize || !bytes.StartsWith("TEST"u8))
        {
            return null;
        }
        var version = Int(bytes, 4);
        var headerSize = version == AtlusVersion ? AtlusHeaderSize : UpstreamHeaderSize;
        if (version is < 1 or > AtlusVersion || bytes.Length < headerSize)
        {
            return null;
        }
        var tileCount = Int(bytes, 8);
        var origin = Vector(bytes, 0x28);
        var cellSize = Float(bytes, 0x34);
        var cellHeight = Float(bytes, 0x38);
        var tileCells = Int(bytes, 0x3c);
        if (!IsPositive(cellSize) || !IsPositive(cellHeight) || !float.IsFinite(origin.X) || !float.IsFinite(origin.Y) ||
            !float.IsFinite(origin.Z) || tileCells is < 1 or > 255 || tileCount < 0)
        {
            return null;
        }
        var (tiles, failed) = ReadTiles(bytes, headerSize, Math.Min(tileCount, MaxTiles));
        return new(version, tileCount, origin, cellSize, cellHeight, tileCells, Float(bytes, 0x44), Float(bytes, 0x48),
            Float(bytes, 0x4c), tiles, failed);
    }

    /// <summary>The file at <paramref name="path"/> in a CPK archive, or null when the archive hasn't it, it is bigger
    /// than <see cref="MaxFileSize"/>, or it doesn't parse.</summary>
    public static NavMeshFile? FromArchive(Stream archive, string path) =>
        CpkArchive.ReadFile(archive, path, MaxFileSize) is { } bytes ? Parse(bytes) : null;

    private static (List<NavTile> Tiles, int Failed) ReadTiles(ReadOnlySpan<byte> bytes, int headerSize, int count)
    {
        var tiles = new List<NavTile>();
        var failed = 0;
        var at = FirstTile(bytes, headerSize);
        var cells = 0;
        for (var i = 0; i < count && at >= 0; i++)
        {
            if (at > bytes.Length - EntryHeaderSize)
            {
                break;
            }
            var size = Int(bytes, at + 4);
            var data = at + EntryHeaderSize;
            if (size < LayerHeaderSize || size > bytes.Length - data || !bytes[data..].StartsWith("RLTD"u8))
            {
                // The run is broken: the next entry can't be found, so the rest of it is lost.
                failed += count - i;
                break;
            }
            if (Tile(bytes.Slice(data, size), MaxCells - cells) is { } tile)
            {
                tiles.Add(tile);
                cells += tile.Width * tile.Height;
            }
            else
            {
                failed++;
            }
            at = data + size;
        }
        return (tiles, failed);
    }

    // The first tile entry after the header: the first "RLTD" layer of version 1 whose entry size, 4 bytes before it,
    // fits the file. The block between the header and it is skipped (the finding's section 2).
    private static int FirstTile(ReadOnlySpan<byte> bytes, int headerSize)
    {
        var from = headerSize + EntryHeaderSize;
        while (from <= bytes.Length - LayerHeaderSize)
        {
            var found = bytes[from..].IndexOf("RLTD"u8);
            if (found < 0)
            {
                return -1;
            }
            var magic = from + found;
            var size = Int(bytes, magic - 4);
            if (magic <= bytes.Length - LayerHeaderSize && Int(bytes, magic + 4) == LayerVersion &&
                size >= LayerHeaderSize && size <= bytes.Length - magic)
            {
                return magic - EntryHeaderSize;
            }
            from = magic + 1;
        }
        return -1;
    }

    // One tile entry's data: the layer header, then its FastLZ block of width * height * 3 bytes.
    private static NavTile? Tile(ReadOnlySpan<byte> data, int cellsLeft)
    {
        if (Int(data, 4) != LayerVersion)
        {
            return null;
        }
        var width = data[0x30];
        var height = data[0x31];
        var min = Vector(data, 0x14);
        var max = Vector(data, 0x20);
        var cells = width * height;
        if (cells == 0 || cells > cellsLeft || !float.IsFinite(min.X) || !float.IsFinite(min.Y) ||
            !float.IsFinite(min.Z))
        {
            return null;
        }
        return FastLz.Decompress(data[LayerHeaderSize..], cells * 3) is { } grids
            ? new NavTile(Int(data, 8), Int(data, 0xc), Int(data, 0x10), min, max, width, height, grids)
            : null;
    }

    private static bool IsPositive(float value) => float.IsFinite(value) && value > 0;

    private static int Int(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]);

    private static float Float(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]);

    private static Vector3 Vector(ReadOnlySpan<byte> bytes, int at) =>
        new(Float(bytes, at), Float(bytes, at + 4), Float(bytes, at + 8));
}
