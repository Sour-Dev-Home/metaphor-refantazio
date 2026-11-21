using System.Buffers.Binary;
using System.Numerics;
using Metaphor.Core.Navigation;

namespace Metaphor.Core.Tests.Navigation;

/// <summary>Reading a field map's <c>.NAV</c> tile-cache set (#179), on made-up files in the layout of
/// docs/findings/field-navmesh-files-tile-cache-static.md. No game data.</summary>
[Trait("Category", "Unit")]
public sealed class NavMeshFileTests
{
    [Fact]
    public void TheHeaderAndEveryTileAreRead()
    {
        var file = NavMeshFile.Parse(NavBytes.File([NavBytes.Layer.Open(0, 0).Entry(), NavBytes.Layer.Open(1, 0).Entry()],
            cellSize: 10, cellHeight: 0.5f, climb: 2))!;

        Assert.Equal(3, file.Version);
        Assert.Equal(2, file.TileCount);
        Assert.Equal(10, file.CellSize);
        Assert.Equal(0.5f, file.CellHeight);
        Assert.Equal(4, file.TileCells);
        Assert.Equal((15f, 5f, 2f), (file.WalkableHeight, file.WalkableRadius, file.WalkableClimb));
        Assert.Equal(0, file.FailedTiles);
        Assert.Equal([0, 1], file.Tiles.Select(t => t.Tx));
        Assert.Equal(new Vector3(40, 0, 0), file.Tiles[1].Min);
    }

    [Fact]
    public void ATilesCellsComeFromItsThreeGrids()
    {
        var layer = NavBytes.Layer.Open(0, 0) with { Heights = [.. Enumerable.Range(0, 16).Select(i => (byte)i)] };

        var tile = NavMeshFile.Parse(NavBytes.File([layer.Blocked(1, 2).Entry()]))!.Tiles[0];

        Assert.Equal(9, tile.FloorHeight(1, 2));
        Assert.Equal(0, tile.Area(1, 2));
        Assert.Equal(63, tile.Area(2, 2));
        Assert.Equal(0, tile.Links(1, 2));
        // Cell (0, 2) links up (1), down (3) and right (2), and its right neighbour is blocked.
        Assert.Equal(0b1010, tile.Links(0, 2));
    }

    [Fact]
    public void TheBlockBeforeTheTilesIsSkipped()
    {
        // Bytes the reader can't describe, including a stray "RLTD" that isn't a tile entry.
        byte[] gap = [.. Enumerable.Repeat((byte)7, 40), .. "RLTD"u8, 9, 9, 9, 9, .. Enumerable.Repeat((byte)1, 30)];

        var file = NavMeshFile.Parse(NavBytes.File([NavBytes.Layer.Open(0, 0).Entry()], gap: gap))!;

        Assert.Single(file.Tiles);
    }

    [Fact]
    public void TilesAfterTheFirstRunAreNotRead()
    {
        var extra = NavBytes.File([NavBytes.Layer.Open(5, 5).Entry()])[0xb0..];

        var file = NavMeshFile.Parse(NavBytes.File([NavBytes.Layer.Open(0, 0).Entry()], trailing: [.. new byte[32], .. extra]))!;

        Assert.Equal([0], file.Tiles.Select(t => t.Tx));
    }

    [Fact]
    public void ATileThatDoesntDecodeIsSkippedAndCounted()
    {
        var broken = NavBytes.Layer.Open(1, 0).Entry(_ => [0x40, 1, 2, 3]);

        var file = NavMeshFile.Parse(NavBytes.File(
            [NavBytes.Layer.Open(0, 0).Entry(), broken, NavBytes.Layer.Open(2, 0).Entry()]))!;

        Assert.Equal([0, 2], file.Tiles.Select(t => t.Tx));
        Assert.Equal(1, file.FailedTiles);
    }

    [Fact]
    public void ATileOfTheWrongSizeIsSkipped()
    {
        var shortGrids = NavBytes.Layer.Open(1, 0).Entry(g => NavBytes.Compress(g.AsSpan(0, g.Length - 1)));

        var file = NavMeshFile.Parse(NavBytes.File([shortGrids, NavBytes.Layer.Open(2, 0).Entry()]))!;

        Assert.Equal([2], file.Tiles.Select(t => t.Tx));
        Assert.Equal(1, file.FailedTiles);
    }

    [Fact]
    public void AnEntryWhoseSizeRunsPastTheFileEndsTheRun()
    {
        var bytes = NavBytes.File([NavBytes.Layer.Open(0, 0).Entry(), NavBytes.Layer.Open(1, 0).Entry()]);
        var secondSize = 0xb0 + 8 + NavBytes.Layer.Open(0, 0).Entry().Length + 4;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(secondSize), int.MaxValue);

        var file = NavMeshFile.Parse(bytes)!;

        Assert.Single(file.Tiles);
        Assert.Equal(1, file.FailedTiles);
    }

    [Fact]
    public void AHeaderCountBeyondTheTilesReadsWhatIsThere()
    {
        var file = NavMeshFile.Parse(NavBytes.File([NavBytes.Layer.Open(0, 0).Entry()], headerTiles: 1_000_000))!;

        Assert.Single(file.Tiles);
        Assert.Equal(1_000_000, file.TileCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnUpstreamHeaderIsRead(int version)
    {
        var file = NavMeshFile.Parse(NavBytes.File([NavBytes.Layer.Open(0, 0).Entry()], version: version))!;

        Assert.Equal(version, file.Version);
        Assert.Single(file.Tiles);
    }

    [Fact]
    public void AFileWithNoTilesHasNone() => Assert.Empty(NavMeshFile.Parse(NavBytes.File([]))!.Tiles);

    [Fact]
    public void AnotherMagicIsNotRead()
    {
        var bytes = NavBytes.File([NavBytes.Layer.Open(0, 0).Entry()]);
        bytes[0] = (byte)'X';

        Assert.Null(NavMeshFile.Parse(bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void AnUnknownVersionIsNotRead(int version)
    {
        var bytes = NavBytes.File([]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), version);

        Assert.Null(NavMeshFile.Parse(bytes));
    }

    [Fact]
    public void AHeaderCutShortIsNotRead() => Assert.Null(NavMeshFile.Parse(NavBytes.File([]).AsSpan(0, 0xa0)));

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ACellSizeThatIsntPositiveIsNotRead(float cellSize) =>
        Assert.Null(NavMeshFile.Parse(NavBytes.File([], cellSize: cellSize)));

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void ATileWidthOutsideAByteIsNotRead(int cells) => Assert.Null(NavMeshFile.Parse(NavBytes.File([], tileCells: cells)));

    [Fact]
    public void AFileOverTheCapIsNotRead()
    {
        var bytes = new byte[NavMeshFile.MaxFileSize + 1];
        NavBytes.File([]).CopyTo(bytes, 0);

        Assert.Null(NavMeshFile.Parse(bytes));
    }

    [Fact]
    public void TheFileIsReadFromTheArchive()
    {
        var path = NavMeshSource.ArchivePath(14, 2)!;
        var bytes = NavBytes.File([NavBytes.Layer.Open(0, 0).Entry()]);
        using var archive = CpkArchiveTests.Archive(("COMMON/model/field/navi", "f_0014_002.NAV", bytes, bytes.Length));

        Assert.Single(NavMeshFile.FromArchive(archive, path)!.Tiles);
    }
}
