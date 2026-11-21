using System.Collections.Concurrent;
using Metaphor.Core.Navigation;

namespace Metaphor.Core.Tests.Navigation;

/// <summary>Loading field maps' walk graphs lazily from a made-up archive (#179). Each test waits on the load's own
/// task before asserting (coding standards, judgement rule 4). No game data.</summary>
[Trait("Category", "Unit")]
public sealed class NavMeshSourceTests
{
    private readonly ConcurrentQueue<string> lines = new();
    private int opens;

    private static MemoryStream Archive() => CpkArchiveTests.Archive(
        ("COMMON/model/field/navi", "f_0000_014.NAV", Nav(1), Nav(1).Length),
        ("COMMON/model/field/navi", "f_0012_003.NAV", Nav(2), Nav(2).Length),
        ("COMMON/model/field/navi", "f_0001_001.NAV", "TEST"u8.ToArray(), 4));

    private static byte[] Nav(int tiles) =>
        NavBytes.File(Enumerable.Range(0, tiles).Select(t => NavBytes.Layer.Open(t, 0).Entry()));

    private NavMeshSource Source(int capacity = 4) => new(() =>
    {
        Interlocked.Increment(ref opens);
        return Archive();
    }, lines.Enqueue, capacity);

    [Theory]
    [InlineData(0, 14, "COMMON/model/field/navi/f_0000_014.NAV")]
    [InlineData(9999, 999, "COMMON/model/field/navi/f_9999_999.NAV")]
    public void AMapsFileIsNamedAsTheGameNamesThem(int a, int b, string path) =>
        Assert.Equal(path, NavMeshSource.ArchivePath(a, b));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(10000, 0)]
    [InlineData(0, 1000)]
    public void NumbersOutOfRangeNameNoFile(int a, int b)
    {
        Assert.Null(NavMeshSource.ArchivePath(a, b));
        Assert.Null(Source().Load(a, b));
        Assert.Null(Source().Get(a, b));
    }

    [Fact]
    public async Task TheFirstGetStartsTheReadAndALaterOneHasTheGraph()
    {
        var source = Source();

        var first = source.Get(12, 3);
        var graph = await source.Load(12, 3)!;

        Assert.True(first is null || first == graph);
        Assert.Equal(2, graph!.File.Tiles.Count);
        Assert.Same(graph, source.Get(12, 3));
        Assert.Equal(["navmesh: f_0012_003.NAV: 2 tiles read, 0 skipped"], lines);
        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task AMissingOrUnreadableFileIsReadOnceAndGivesNoGraph()
    {
        var source = Source();

        Assert.Null(await source.Load(7, 7)!);
        Assert.Null(await source.Load(1, 1)!);
        Assert.Null(await source.Load(7, 7)!);

        Assert.Null(source.Get(7, 7));
        Assert.Equal(["navmesh: no readable f_0007_007.NAV in the archive", "navmesh: no readable f_0001_001.NAV in the archive"],
            lines);
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task AnArchiveThatWontOpenIsLoggedNotThrown()
    {
        var source = new NavMeshSource(() => throw new IOException("locked"), lines.Enqueue);

        Assert.Null(await source.Load(0, 14)!);
        Assert.Equal(["navmesh: can't read f_0000_014.NAV: IOException"], lines);
    }

    [Fact]
    public async Task OnlyTheLastMapsUsedAreKept()
    {
        var source = Source(capacity: 2);

        await source.Load(0, 14)!;
        await source.Load(12, 3)!;
        await source.Load(0, 14)!;
        await source.Load(7, 7)!;
        await source.Load(0, 14)!;
        await source.Load(12, 3)!;

        Assert.Equal(4, opens);
    }
}
