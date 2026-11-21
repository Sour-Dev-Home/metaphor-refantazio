using FsCheck;
using FsCheck.Fluent;
using Metaphor.Core.Navigation;

namespace Metaphor.Core.Tests.Navigation;

/// <summary>Properties of the navmesh readers (ADR 0015, #179): for any bytes, FastLZ and the <c>.NAV</c> reader never
/// throw, never write past their bounds, and give a documented result. Inputs are random and mutated made-up files; no
/// game data.</summary>
[Trait("Category", "Unit")]
[Trait("Category", "Property")]
public sealed class NavMeshPropertyTests
{
    private static readonly Gen<byte[]> Data = Gen.Frequency(
        (3, Gen.ArrayOf(Gen.Choose(0, 255).Select(b => (byte)b))),
        (2, Gen.ArrayOf(Gen.Elements((byte)0, (byte)1, (byte)2, (byte)0xff), 2000)),
        (1, Gen.Choose(0, 3000).Select(n => Enumerable.Range(0, n).Select(i => (byte)(i % 5)).ToArray())));

    // A made-up file of a few tiles with some of its bytes overwritten.
    private static readonly Gen<byte[]> DamagedFiles =
        from tiles in Gen.Choose(0, 4)
        from edits in Gen.ArrayOf(
            from at in Gen.Choose(0, int.MaxValue)
            from value in Gen.Choose(0, 255)
            select (at, value), 6)
        from cut in Gen.Choose(0, 50)
        select Damage(NavBytes.File(Enumerable.Range(0, tiles).Select(t => NavBytes.Layer.Open(t, 0).Entry())), edits, cut);

    [Fact]
    public void FastLzNeverWritesPastItsBufferOrThrows() =>
        PropertyRun.Check(Prop.ForAll(Data.ToArbitrary(), Gen.Choose(0, 600).ToArbitrary(), (input, room) =>
        {
            var output = new byte[room + 8];
            output.AsSpan(room).Fill(0xa5);

            var written = FastLz.Decompress(input, output.AsSpan(0, room));

            Assert.InRange(written, -1, room);
            Assert.All(output[room..], b => Assert.Equal(0xa5, b));
        }));

    [Fact]
    public void FastLzGivesBackWhatTheEncoderWrote() =>
        PropertyRun.Check(Prop.ForAll(Data.Where(d => d.Length > 0).ToArbitrary(), Gen.Elements(1, 2).ToArbitrary(),
            (data, level) => Assert.Equal(data, FastLz.Decompress(NavBytes.Compress(data, level), data.Length))));

    [Fact]
    public void TheNavReaderNeverThrowsAndKeepsItsCaps() =>
        PropertyRun.Check(Prop.ForAll(Gen.OneOf(Data, DamagedFiles).ToArbitrary(), bytes =>
        {
            if (NavMeshFile.Parse(bytes) is not { } file)
            {
                return;
            }
            Assert.True(file.Tiles.Count <= NavMeshFile.MaxTiles);
            Assert.True(file.Tiles.Sum(t => t.Width * t.Height) <= NavMeshFile.MaxCells);
            var graph = new WalkGraph(file);
            for (var i = 0; i < file.Tiles.Count; i++)
            {
                foreach (var (_, next) in graph.Neighbours(new NavCell(i, 0, 0)))
                {
                    Assert.True(graph.IsWalkable(next));
                }
                _ = graph.DistanceToEdge(graph.Centre(new NavCell(i, 0, 0)), new(1, 1), 100);
            }
        }));

    private static byte[] Damage(byte[] file, (int At, int Value)[] edits, int cut)
    {
        foreach (var (at, value) in edits)
        {
            file[at % file.Length] = (byte)value;
        }
        return file[..Math.Max(0, file.Length - cut)];
    }
}
