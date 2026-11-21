using System.Numerics;
using Metaphor.Core.Navigation;

namespace Metaphor.Core.Tests.Navigation;

/// <summary>The walk graph over a <c>.NAV</c> file's cells (#179): finding a cell, its links within a tile, across
/// tiles and between layers, bounded searches, and the distance to an edge. Made-up 4 × 4 tiles of 10-unit cells from
/// the origin; no game data.</summary>
[Trait("Category", "Unit")]
public sealed class WalkGraphTests
{
    private static WalkGraph Graph(params NavBytes.Layer[] layers) =>
        new(NavMeshFile.Parse(NavBytes.File(layers.Select(l => l.Entry()), climb: 2))!);

    // Two tiles side by side along x.
    private static readonly WalkGraph TwoTiles = Graph(NavBytes.Layer.Open(0, 0), NavBytes.Layer.Open(1, 0));

    [Fact]
    public void APositionFindsTheCellUnderIt()
    {
        Assert.Equal(new NavCell(0, 1, 2), TwoTiles.CellAt(new(15, 0, 25), maxVertical: 5));
        Assert.Equal(new NavCell(1, 0, 3), TwoTiles.CellAt(new(41, 0, 39), maxVertical: 5));
    }

    [Theory]
    [InlineData(-1, 0, 5)]
    [InlineData(85, 0, 5)]
    [InlineData(5, 0, -0.1f)]
    [InlineData(5, 30, 5)]
    [InlineData(float.NaN, 0, 5)]
    [InlineData(1e30f, 0, 5)]
    public void APositionOffTheCellsFindsNone(float x, float y, float z) =>
        Assert.Null(TwoTiles.CellAt(new(x, y, z), maxVertical: 5));

    [Fact]
    public void ACellsCentreIsTheMiddleOfItsFloor()
    {
        var graph = Graph(NavBytes.Layer.Open(1, 0, floor: 7) with { Heights = Enumerable.Repeat((byte)3, 16).ToArray() });

        Assert.Equal(new Vector3(55, 10, 25), graph.Centre(new NavCell(0, 1, 2)));
    }

    [Fact]
    public void NeighboursInsideATileFollowTheLinkBits()
    {
        var graph = Graph(NavBytes.Layer.Open(0, 0).Blocked(2, 1));

        Assert.Equal(new NavCell(0, 0, 1), graph.Neighbour(new NavCell(0, 1, 1), 0));
        Assert.Equal(new NavCell(0, 1, 2), graph.Neighbour(new NavCell(0, 1, 1), 1));
        Assert.Null(graph.Neighbour(new NavCell(0, 1, 1), 2));
        Assert.Equal(new NavCell(0, 1, 0), graph.Neighbour(new NavCell(0, 1, 1), 3));
        Assert.Equal([0, 1, 3], graph.Neighbours(new NavCell(0, 1, 1)).Select(n => n.Direction));
    }

    [Fact]
    public void AWalkableCellInTheNextTileIsLinked()
    {
        Assert.Equal(new NavCell(1, 0, 2), TwoTiles.Neighbour(new NavCell(0, 3, 2), 2));
        Assert.Equal(new NavCell(0, 3, 2), TwoTiles.Neighbour(new NavCell(1, 0, 2), 0));
        Assert.Null(TwoTiles.Neighbour(new NavCell(0, 0, 2), 0));
    }

    [Fact]
    public void TheNextTilesCellBeyondTheClimbIsNotLinked()
    {
        var graph = Graph(NavBytes.Layer.Open(0, 0), NavBytes.Layer.Open(1, 0, floor: 3));

        Assert.Null(graph.Neighbour(new NavCell(0, 3, 2), 2));
    }

    [Fact]
    public void APortalBitLinksTheOtherLayersCell()
    {
        var ground = NavBytes.Layer.Open(0, 0);
        var links = (byte[])ground.Links.Clone();
        links[1 + (1 * 4)] = (byte)((links[1 + (1 * 4)] & ~4) | 0x40);
        var upper = NavBytes.Layer.Open(0, 0, floor: 1, layer: 1);
        var high = NavBytes.Layer.Open(0, 0, floor: 40, layer: 2);

        var graph = Graph(ground with { Links = links }, high, upper);

        Assert.Equal(new NavCell(2, 2, 1), graph.Neighbour(new NavCell(0, 1, 1), 2));
        Assert.Null(graph.Neighbour(new NavCell(0, 0, 1), 0));
    }

    [Fact]
    public void ACellWithNoBitTowardAnInsideNeighbourHasNone()
    {
        var ground = NavBytes.Layer.Open(0, 0);
        var links = (byte[])ground.Links.Clone();
        links[1 + (1 * 4)] &= unchecked((byte)~4);

        Assert.Null(Graph(ground with { Links = links }, NavBytes.Layer.Open(0, 0, layer: 1)).Neighbour(new NavCell(0, 1, 1), 2));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(0, 0, -1)]
    public void ACellOutsideTheGraphHasNoNeighbours(int tile, int x, int y) =>
        Assert.Empty(TwoTiles.Neighbours(new NavCell(tile, x, y)));

    [Fact]
    public void NoDirectionOutsideZeroToThreeHasANeighbour()
    {
        Assert.Null(TwoTiles.Neighbour(new NavCell(0, 1, 1), 4));
        Assert.Null(TwoTiles.Neighbour(new NavCell(0, 1, 1), -1));
    }

    [Fact]
    public void LinkedCellsAcrossTilesAreReachable() =>
        Assert.Equal(Reachability.Reachable, TwoTiles.IsReachable(new NavCell(0, 0, 0), new NavCell(1, 3, 3)));

    [Fact]
    public void CellsBehindAWallAreUnreachable()
    {
        var walled = NavBytes.Layer.Open(0, 0);
        for (var y = 0; y < 4; y++)
        {
            walled = walled.Blocked(2, y);
        }

        var graph = Graph(walled);

        Assert.Equal(Reachability.Unreachable, graph.IsReachable(new NavCell(0, 0, 0), new NavCell(0, 3, 0)));
        Assert.Equal(Reachability.Reachable, graph.IsReachable(new NavCell(0, 0, 0), new NavCell(0, 1, 3)));
        Assert.Equal(Reachability.Unreachable, graph.IsReachable(new NavCell(0, 0, 0), new NavCell(0, 2, 0)));
    }

    [Fact]
    public void ASearchGivesUpAtItsCellLimit() =>
        Assert.Equal(Reachability.GaveUp, TwoTiles.IsReachable(new NavCell(0, 0, 0), new NavCell(1, 3, 3), maxVisited: 5));

    [Fact]
    public void ThePathIsTheShortestChainOfLinkedCells()
    {
        var path = TwoTiles.Path(new NavCell(0, 2, 1), new NavCell(1, 1, 1))!;

        Assert.Equal([new NavCell(0, 2, 1), new NavCell(0, 3, 1), new NavCell(1, 0, 1), new NavCell(1, 1, 1)], path);
    }

    [Fact]
    public void APathToItselfIsOneCell() =>
        Assert.Equal([new NavCell(0, 1, 1)], TwoTiles.Path(new NavCell(0, 1, 1), new NavCell(0, 1, 1)));

    [Fact]
    public void TheFirstStepPointsAlongThePath()
    {
        Assert.Equal(new Vector2(1, 0), TwoTiles.FirstStep(new NavCell(0, 2, 1), new NavCell(1, 1, 1)));
        Assert.Equal(new Vector2(0, -1), TwoTiles.FirstStep(new NavCell(0, 2, 3), new NavCell(0, 2, 0)));
    }

    [Fact]
    public void ThereIsNoFirstStepWithoutAPathOrWhenAlreadyThere()
    {
        Assert.Null(TwoTiles.FirstStep(new NavCell(0, 1, 1), new NavCell(0, 1, 1)));
        Assert.Null(TwoTiles.FirstStep(new NavCell(0, 1, 1), new NavCell(9, 1, 1)));
        Assert.Null(TwoTiles.Path(new NavCell(0, 0, 0), new NavCell(1, 3, 3), maxVisited: 5));
    }

    [Theory]
    [InlineData(1, 0, 65)]
    [InlineData(-1, 0, 15)]
    [InlineData(0, 1, 25)]
    [InlineData(0, -1, 15)]
    public void TheEdgeIsWhereTheLinksEnd(float x, float z, float expected) =>
        Assert.Equal(expected, TwoTiles.DistanceToEdge(new(15, 0, 15), new(x, z), 1000)!.Value, 3);

    [Fact]
    public void ABlockedCellIsAnEdge() =>
        Assert.Equal(25, Graph(NavBytes.Layer.Open(0, 0).Blocked(3, 1)).DistanceToEdge(new(5, 0, 15), new(2, 0), 1000)!.Value, 3);

    [Fact]
    public void ADiagonalHeadingWalksToTheCorner() =>
        Assert.Equal(35 * MathF.Sqrt(2), Graph(NavBytes.Layer.Open(0, 0)).DistanceToEdge(new(5, 0, 5), new(1, 1), 1000)!.Value, 3);

    [Fact]
    public void NoEdgeWithinTheLimitGivesTheLimit()
    {
        Assert.Equal(10, TwoTiles.DistanceToEdge(new(15, 0, 15), new(1, 0), 10));
        Assert.Equal(0, TwoTiles.DistanceToEdge(new(15, 0, 15), new(1, 0), 0));
    }

    [Fact]
    public void AnUnboundedLimitStillStopsAtTheEdge() =>
        Assert.Equal(65, TwoTiles.DistanceToEdge(new(15, 0, 15), new(1, 0), float.MaxValue)!.Value, 3);

    [Fact]
    public void NoDistanceWithoutACellOrAHeading()
    {
        Assert.Null(TwoTiles.DistanceToEdge(new(-50, 0, 15), new(1, 0), 100));
        Assert.Null(TwoTiles.DistanceToEdge(new(15, 0, 15), Vector2.Zero, 100));
        Assert.Null(TwoTiles.DistanceToEdge(new(15, 0, 15), new(float.NaN, 0), 100));
        Assert.Null(TwoTiles.DistanceToEdge(new(15, 0, 15), new(1, 0), -1));
    }

    [Fact]
    public void TheNearestCellIsFoundWhenThePositionIsOnABlockedOne()
    {
        var graph = Graph(NavBytes.Layer.Open(0, 0).Blocked(1, 1).Blocked(0, 1).Blocked(0, 0).Blocked(1, 0));

        Assert.Null(graph.CellAt(new(12, 0, 12), 5));
        Assert.Equal(new NavCell(0, 2, 1), graph.NearestCell(new(19, 0, 12), 5, radiusCells: 2));
        Assert.Null(graph.NearestCell(new(5, 0, 5), 5, radiusCells: 0));
    }
}
