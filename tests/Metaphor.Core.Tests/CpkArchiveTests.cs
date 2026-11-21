using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Metaphor.Core.Tests;

/// <summary>Reading one stored file out of a CRI CPK archive (#118), in the layout CriFsV2Lib reads
/// (docs/raw-sources/cri-cpk-table-layout-2026-11-21.md). Made-up archives built here; no game data.</summary>
[Trait("Category", "Unit")]
public sealed class CpkArchiveTests
{
    private const int MaxSize = 1024;

    // A column: its name, its type code, and either a value per row or one default for all.
    private sealed record Column(string Name, byte Type, bool PerRow, object? Default = null);

    /// <summary>An @UTF table as CriFsV2Lib reads one: big-endian, offsets counted from +8.</summary>
    private static byte[] Table(List<Column> columns, List<object[]> rows)
    {
        var strings = new MemoryStream();
        var stringAt = new Dictionary<string, int>();
        int Str(string s)
        {
            if (!stringAt.TryGetValue(s, out var at))
            {
                at = (int)strings.Length;
                stringAt[s] = at;
                strings.Write(Encoding.UTF8.GetBytes(s));
                strings.WriteByte(0);
            }
            return at;
        }
        Str("<NULL>");
        byte[] Value(byte type, object value) => type switch
        {
            4 => BigEndian(4, w => BinaryPrimitives.WriteUInt32BigEndian(w, Convert.ToUInt32(value, CultureInfo.InvariantCulture))),
            6 => BigEndian(8, w => BinaryPrimitives.WriteUInt64BigEndian(w, Convert.ToUInt64(value, CultureInfo.InvariantCulture))),
            10 => BigEndian(4, w => BinaryPrimitives.WriteInt32BigEndian(w, Str((string)value))),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        var columnBytes = new MemoryStream();
        foreach (var column in columns)
        {
            columnBytes.WriteByte((byte)(0x10 | column.Type | (column.PerRow ? 0x40 : 0) | (column.Default is null ? 0 : 0x20)));
            columnBytes.Write(BigEndian(4, w => BinaryPrimitives.WriteInt32BigEndian(w, Str(column.Name))));
            if (column.Default is { } value)
            {
                columnBytes.Write(Value(column.Type, value));
            }
        }
        var rowBytes = new MemoryStream();
        var rowSize = 0;
        foreach (var row in rows)
        {
            var start = rowBytes.Length;
            var perRow = columns.Where(c => c.PerRow).ToList();
            for (var i = 0; i < perRow.Count; i++)
            {
                rowBytes.Write(Value(perRow[i].Type, row[i]));
            }
            rowSize = (int)(rowBytes.Length - start);
        }
        var rowsOffset = 0x20 + (int)columnBytes.Length;
        var stringsOffset = rowsOffset + (int)rowBytes.Length;
        var size = stringsOffset + (int)strings.Length;
        var table = new byte[size];
        "@UTF"u8.CopyTo(table);
        BinaryPrimitives.WriteInt32BigEndian(table.AsSpan(4), size - 8);
        table[9] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(0x0a), (ushort)(rowsOffset - 8));
        BinaryPrimitives.WriteInt32BigEndian(table.AsSpan(0x0c), stringsOffset - 8);
        BinaryPrimitives.WriteInt32BigEndian(table.AsSpan(0x10), size - 8);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(0x18), (ushort)columns.Count);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(0x1a), (ushort)rowSize);
        BinaryPrimitives.WriteInt32BigEndian(table.AsSpan(0x1c), rows.Count);
        columnBytes.ToArray().CopyTo(table, 0x20);
        rowBytes.ToArray().CopyTo(table, rowsOffset);
        strings.ToArray().CopyTo(table, stringsOffset);
        return table;
    }

    private static byte[] BigEndian(int length, Action<Span<byte>> write)
    {
        var bytes = new byte[length];
        write(bytes);
        return bytes;
    }

    private static byte[] Container(string magic, byte[] table)
    {
        var bytes = new byte[16 + table.Length];
        Encoding.ASCII.GetBytes(magic).CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), table.Length);
        table.CopyTo(bytes, 16);
        return bytes;
    }

    /// <summary>An archive with its table of contents at 0x100, its content at 0x400 and these files one after another
    /// from there, each offset counted from the table of contents (the lower of the two).</summary>
    internal static MemoryStream Archive(params (string Dir, string Name, byte[] Data, int ExtractSize)[] files) =>
        Archive(headerContentOffset: null, files);

    /// <summary>The same, with the header's <c>ContentOffset</c> replaced by <paramref name="headerContentOffset"/>
    /// when given, and each file offset counted from the lower of it and the table of contents.</summary>
    private static MemoryStream Archive(ulong? headerContentOffset, params (string Dir, string Name, byte[] Data, int ExtractSize)[] files)
    {
        const int TocOffset = 0x100;
        const int ContentOffset = 0x400;
        var countedFrom = headerContentOffset is { } given ? Math.Min(TocOffset, (long)given) : TocOffset;
        var starts = new int[files.Length];
        var end = ContentOffset;
        for (var i = 0; i < files.Length; i++)
        {
            starts[i] = end;
            end += files[i].Data.Length;
        }
        var header = Table(
            [new("TocOffset", 6, PerRow: true), new("ContentOffset", 6, PerRow: true), new("Files", 4, PerRow: true)],
            [[TocOffset, headerContentOffset ?? ContentOffset, files.Length]]);
        var toc = Table(
            [new("DirName", 10, PerRow: true), new("FileName", 10, PerRow: true), new("FileSize", 4, PerRow: true),
             new("ExtractSize", 4, PerRow: true), new("FileOffset", 6, PerRow: true), new("UserString", 10, PerRow: false, "<NULL>")],
            files.Select((f, i) => new object[] { f.Dir, f.Name, f.Data.Length, f.ExtractSize, (ulong)(starts[i] - countedFrom) }).ToList());
        var bytes = new byte[end];
        Container("CPK ", header).CopyTo(bytes, 0);
        Container("TOC ", toc).CopyTo(bytes, TocOffset);
        for (var i = 0; i < files.Length; i++)
        {
            files[i].Data.CopyTo(bytes, starts[i]);
        }
        return new MemoryStream(bytes);
    }

    private static (string, string, byte[], int) Stored(string dir, string name, string text) =>
        (dir, name, Encoding.ASCII.GetBytes(text), text.Length);

    [Fact]
    public void ReadsTheNamedFileFromItsOffset()
    {
        using var archive = Archive(Stored("A/b", "one.bin", "first"), Stored("A/b", "two.bin", "second"));

        Assert.Equal("second"u8.ToArray(), CpkArchive.ReadFile(archive, "A/b/two.bin", MaxSize));
    }

    [Fact]
    public void AFileOutsideAnyFolderIsFoundByItsName()
    {
        using var archive = Archive(Stored("", "root.bin", "top"));

        Assert.Equal("top"u8.ToArray(), CpkArchive.ReadFile(archive, "root.bin", MaxSize));
    }

    [Theory]
    [InlineData("A/b/three.bin")]
    [InlineData("A/c/one.bin")]
    [InlineData("one.bin")]
    public void AMissingFileIsNull(string path)
    {
        using var archive = Archive(Stored("A/b", "one.bin", "first"));

        Assert.Null(CpkArchive.ReadFile(archive, path, MaxSize));
    }

    [Fact]
    public void ACompressedFileIsNotRead()
    {
        using var archive = Archive(("A", "packed.bin", "CRILAYLApacked"u8.ToArray(), 0x200));
        using var marked = Archive(("A", "packed.bin", "CRILAYLApacked"u8.ToArray(), 14));

        Assert.Null(CpkArchive.ReadFile(archive, "A/packed.bin", MaxSize));
        Assert.Null(CpkArchive.ReadFile(marked, "A/packed.bin", MaxSize));
    }

    [Fact]
    public void AFileOverTheLimitIsNotRead()
    {
        using var archive = Archive(Stored("A", "big.bin", "too long"));

        Assert.Null(CpkArchive.ReadFile(archive, "A/big.bin", maxSize: 4));
    }

    [Fact]
    public void AnArchiveCutShortIsNull()
    {
        using var whole = Archive(Stored("A", "one.bin", "first"));
        var bytes = whole.ToArray();

        foreach (var length in new[] { 0, 8, 0x30, 0x110, 0x180, 0x402 })
        {
            using var cut = new MemoryStream(bytes[..length]);
            Assert.Null(CpkArchive.ReadFile(cut, "A/one.bin", MaxSize));
        }
    }

    [Fact]
    public void AnEncryptedOrUnknownTableIsNull()
    {
        using var archive = Archive(Stored("A", "one.bin", "first"));
        var bytes = archive.ToArray();
        bytes[0x110] ^= 0xff;

        Assert.Null(CpkArchive.ReadFile(new MemoryStream(bytes), "A/one.bin", MaxSize));
    }

    [Fact]
    public void ANegativeContentOffsetIsRefused()
    {
        // Offsets counted from -0x10 still land on the file's bytes, and the file's own offset (its start + 0x10) is
        // inside the archive, so only the content offset's check refuses it.
        using var archive = Archive(headerContentOffset: unchecked((ulong)-0x10L), Stored("A", "one.bin", new string('x', 0x40)));

        Assert.Null(CpkArchive.ReadFile(archive, "A/one.bin", MaxSize));
    }

    [Fact]
    public void AFileOffsetPastTheArchiveIsRefused()
    {
        // Past the end alone, but added to a negative content offset it would land inside.
        using var archive = Archive(headerContentOffset: unchecked((ulong)-0x10000L), Stored("A", "one.bin", "first"));

        Assert.Null(CpkArchive.ReadFile(archive, "A/one.bin", MaxSize));
    }

    [Fact]
    public void RowsOfNoBytesAreRefused()
    {
        var table = Table([new("Shared", 4, PerRow: false, 7)], [[]]);
        BinaryPrimitives.WriteInt32BigEndian(table.AsSpan(0x1c), int.MaxValue);

        Assert.Null(UtfTable.Parse(table));
    }

    [Fact]
    public void ATableWhoseColumnsRunPastItsEndIsRefused()
    {
        var table = Table([new("Name", 10, PerRow: true)], [["x"]]);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(0x18), 0x7fff);

        Assert.Null(UtfTable.Parse(table));
    }

    [Fact]
    public void ATableWhoseRowsRunPastItsEndIsRefused()
    {
        var table = Table([new("Name", 10, PerRow: true)], [["x"]]);
        BinaryPrimitives.WriteInt32BigEndian(table.AsSpan(0x1c), 0x10000);

        Assert.Null(UtfTable.Parse(table));
    }

    [Fact]
    public void AColumnsDefaultServesEveryRow()
    {
        var table = UtfTable.Parse(Table(
            [new("Name", 10, PerRow: true), new("Shared", 4, PerRow: false, 7)], [["x"], ["y"]]))!;

        Assert.Equal(2, table.RowCount);
        Assert.Equal("y", table.Text(1, "Name"));
        Assert.Equal(7, table.Number(1, "Shared"));
        Assert.Null(table.Number(0, "Name"));
        Assert.Null(table.Text(0, "Shared"));
        Assert.Null(table.Text(2, "Name"));
        Assert.Null(table.Text(0, "Missing"));
    }
}
