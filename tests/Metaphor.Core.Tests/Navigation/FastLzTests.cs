using Metaphor.Core.Navigation;

namespace Metaphor.Core.Tests.Navigation;

/// <summary>The FastLZ port (#179), on blocks written by hand from the format and by the tests' own encoder
/// (docs/raw-sources/fastlz-and-recast-layer-cells-2026-11-21.md).</summary>
[Trait("Category", "Unit")]
public sealed class FastLzTests
{
    [Fact]
    public void ALiteralRunIsCopied() =>
        Assert.Equal("abc"u8.ToArray(), FastLz.Decompress([0x02, (byte)'a', (byte)'b', (byte)'c'], 3));

    [Fact]
    public void AMatchMayOverlapTheBytesItWrites() =>
        // "a", then copy 3 from one back: "aaaa".
        Assert.Equal("aaaa"u8.ToArray(), FastLz.Decompress([0x00, (byte)'a', 0x20, 0x00], 4));

    [Fact]
    public void ALongLevelOneMatchTakesItsLengthFromTheNextByte() =>
        // Length 9 + 1 = 10 from one back.
        Assert.Equal(Enumerable.Repeat((byte)'z', 11).ToArray(), FastLz.Decompress([0x00, (byte)'z', 0xe0, 0x01, 0x00], 11));

    [Fact]
    public void ALevelTwoLengthAddsBytesWhileEachIs255() =>
        // 9 + 255 + 1 = 265 from one back.
        Assert.Equal(Enumerable.Repeat((byte)'q', 266).ToArray(),
            FastLz.Decompress([0x20, (byte)'q', 0xe0, 0xff, 0x01, 0x00], 266));

    [Fact]
    public void ALevelTwoFarMatchReadsASixteenBitDistance()
    {
        var data = new byte[9000];
        new Random(179).NextBytes(data.AsSpan(0, 100));
        data.AsSpan(0, 100).CopyTo(data.AsSpan(8900));
        var block = NavBytes.Compress(data, level: 2);

        Assert.Contains((byte)0xff, block);
        Assert.Equal(data, FastLz.Decompress(block, data.Length));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EncodedDataComesBackWhole(int level)
    {
        var data = new byte[3000];
        var random = new Random(level);
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(random.Next(4) == 0 ? random.Next(256) : i % 7);
        }

        var block = NavBytes.Compress(data, level);

        Assert.True(block.Length < data.Length);
        Assert.Equal(data, FastLz.Decompress(block, data.Length));
    }

    [Fact]
    public void AnEmptyBlockIsRefused() => Assert.Equal(-1, FastLz.Decompress([], new byte[4]));

    [Theory]
    [InlineData(0x40)]
    [InlineData(0xe0)]
    public void ALevelOtherThanOneOrTwoIsRefused(byte first) => Assert.Equal(-1, FastLz.Decompress([first, 0, 0], new byte[4]));

    [Fact]
    public void ALiteralRunCutShortIsRefused() => Assert.Equal(-1, FastLz.Decompress([0x05, 1, 2], new byte[8]));

    [Fact]
    public void AMatchBeforeTheOutputsStartIsRefused() => Assert.Equal(-1, FastLz.Decompress([0x00, 1, 0x20, 0x05], new byte[8]));

    [Fact]
    public void AMatchMissingItsDistanceIsRefused() => Assert.Equal(-1, FastLz.Decompress([0x20, 1, 0xe0, 0xff], new byte[600]));

    [Fact]
    public void OutputPastTheBufferIsRefused()
    {
        Assert.Equal(-1, FastLz.Decompress([0x02, 1, 2, 3], new byte[2]));
        Assert.Equal(-1, FastLz.Decompress([0x00, 1, 0x20, 0x00], new byte[3]));
    }

    [Fact]
    public void AnOutputOfAnotherSizeThanExpectedIsNull()
    {
        Assert.Null(FastLz.Decompress([0x02, 1, 2, 3], 4));
        Assert.Null(FastLz.Decompress([0x02, 1, 2, 3], 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(FastLz.MaxOutput + 1)]
    public void AnExpectedSizeOutsideTheCapIsNull(int size) => Assert.Null(FastLz.Decompress([0x00, 1], size));
}
