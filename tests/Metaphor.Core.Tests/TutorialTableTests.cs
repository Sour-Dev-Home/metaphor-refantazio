using System.Buffers.Binary;
using Metaphor.Core.Messages;

namespace Metaphor.Core.Tests;

/// <summary>The tutorial table (#118) maps a tutorial number to its title's and pages' label hashes, in the layout of
/// docs/findings/tutorial-table-maps-numbers-to-labels.md. Made-up labels only.</summary>
[Trait("Category", "Unit")]
public sealed class TutorialTableTests
{
    private static byte[] Hash(string label) => Convert.FromHexString(MessageLibrary.LabelHash(label));

    private static string Hex(string label) => MessageLibrary.LabelHash(label);

    /// <summary>A table of these entries: each a title label and up to four page labels, null for an unused slot as
    /// the game leaves one (the MD5 of an empty label).</summary>
    public static byte[] Bytes(params (string Title, string?[] Pages)[] entries)
    {
        var bytes = new byte[0x10 + entries.Length * 0xe0];
        "TUTO"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = bytes.AsSpan(0x10 + i * 0xe0, 0xe0);
            BinaryPrimitives.WriteInt32LittleEndian(entry, 6);
            Hash(entries[i].Title).CopyTo(entry[4..]);
            for (var slot = 0; slot < 4; slot++)
            {
                var label = slot < entries[i].Pages.Length ? entries[i].Pages[slot] : null;
                Hash(label ?? "").CopyTo(entry[(0x14 + slot * 0x32)..]);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[(0x14 + slot * 0x32 + 0x30)..], 0xffff);
            }
        }
        return bytes;
    }

    [Fact]
    public void EachNumberNamesItsTitleAndItsPagesInOrder()
    {
        var table = TutorialTable.Parse(Bytes(
            ("Made_Title_01", ["Made_Page_01_01"]),
            ("Made_Title_02", ["Made_Page_02_01", "Made_Page_02_02", "Made_Page_02_03"])))!;

        Assert.Equal(2, table.Count);
        var second = table.Entry(1)!;
        Assert.Equal(Hex("Made_Title_02"), second.TitleHash);
        Assert.Equal([Hex("Made_Page_02_01"), Hex("Made_Page_02_02"), Hex("Made_Page_02_03")], second.PageHashes);
        Assert.Single(table.Entry(0)!.PageHashes);
    }

    [Fact]
    public void AnAllZeroSlotIsUnusedToo()
    {
        var bytes = Bytes(("Made_Title_01", ["Made_Page_01_01", "Made_Page_01_02"]));
        bytes.AsSpan(0x10 + 0x14 + 0x32, 16).Clear();

        Assert.Equal([Hex("Made_Page_01_01")], TutorialTable.Parse(bytes)!.Entry(0)!.PageHashes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void ANumberOutsideTheTableHasNoEntry(int number) =>
        Assert.Null(TutorialTable.Parse(Bytes(("A", []), ("B", [])))!.Entry(number));

    [Fact]
    public void BytesWithoutTheMagicOrShorterThanTheCountAreNotATable()
    {
        var bytes = Bytes(("A", []), ("B", []));
        var wrongMagic = (byte[])bytes.Clone();
        wrongMagic[0] = (byte)'X';

        Assert.Null(TutorialTable.Parse(wrongMagic));
        Assert.Null(TutorialTable.Parse(bytes.AsSpan(..^1)));
        Assert.Null(TutorialTable.Parse(bytes.AsSpan(..8)));
    }

    [Fact]
    public async Task TheSourceHasNoTableUntilItLoadsThenSaysWhatItRead()
    {
        var source = new TutorialTableSource();
        var bytes = Bytes(("A", []));
        using var opened = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        string? said = null;

        var load = source.LoadInBackground(() =>
        {
            opened.Set();
            release.Wait();
            return new MemoryStream(bytes);
        }, line => said = line);
        opened.Wait();
        Assert.Null(source.Table);
        release.Set();
        await load;

        // Not an archive: the table stays null, and the line says so by the file's name alone.
        Assert.Null(source.Table);
        Assert.Equal($"tutorial table: no readable {TutorialTable.ArchivePath} in the archive", said);
    }

    [Fact]
    public async Task TheSourceReadsTheTableFromTheGamesArchive()
    {
        var source = new TutorialTableSource();
        var table = Bytes(("Made_Title_01", ["Made_Page_01_01"]));
        var slash = TutorialTable.ArchivePath.LastIndexOf('/');
        string? said = null;

        await source.LoadInBackground(() => CpkArchiveTests.Archive(
            ("COMMON/other", "Other.bin", [1, 2, 3], 3),
            (TutorialTable.ArchivePath[..slash], TutorialTable.ArchivePath[(slash + 1)..], table, table.Length)),
            line => said = line);

        Assert.Equal(Hex("Made_Title_01"), source.Table?.Entry(0)?.TitleHash);
        Assert.Equal("tutorial table: 1 tutorials", said);
    }

    [Fact]
    public async Task AFailedOpenIsReportedNotThrown()
    {
        var source = new TutorialTableSource();
        string? said = null;

        await source.LoadInBackground(() => throw new IOException("made up"), line => said = line);

        Assert.Null(source.Table);
        Assert.Equal("tutorial table: can't read the archive: IOException", said);
    }
}
