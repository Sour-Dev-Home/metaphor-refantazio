using System.Buffers.Binary;
using System.Text;

namespace Metaphor.Core;

/// <summary>Reads one stored (uncompressed) file out of a CRI CPK archive, such as the game's <c>base.cpk</c> (#118). The
/// layout is CriFsV2Lib's reading of it (docs/raw-sources/cri-cpk-table-layout-2026-11-21.md): a 16-byte container whose
/// <c>+0x8</c> is the table's size, then an <c>@UTF</c> table. The archive's own table, at 0, gives
/// <c>TocOffset</c> and <c>ContentOffset</c>; the table of contents, at <c>TocOffset</c>, gives each file's
/// <c>DirName</c>, <c>FileName</c>, <c>FileSize</c>, <c>ExtractSize</c> and <c>FileOffset</c>, counted from the lower
/// of the two offsets. Encrypted tables and compressed files aren't read.</summary>
public static class CpkArchive
{
    /// <summary>The largest table read, so a damaged size can't ask for gigabytes.</summary>
    public const int MaxTableSize = 64 * 1024 * 1024;

    private const int ContainerSize = 16;

    /// <summary>The bytes of the file at <paramref name="path"/> (<c>DIR/NAME</c>, as the table of contents names it),
    /// or null when the archive has no such file, it is compressed or bigger than <paramref name="maxSize"/>, or the
    /// archive isn't one this reads. Never throws on a damaged archive; the stream's own failures propagate.</summary>
    public static byte[]? ReadFile(Stream archive, string path, int maxSize)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(path);
        var slash = path.LastIndexOf('/');
        var (dir, name) = slash < 0 ? ("", path) : (path[..slash], path[(slash + 1)..]);
        if (Table(archive, 0) is not { RowCount: > 0 } header ||
            header.Number(0, "TocOffset") is not { } tocOffset || header.Number(0, "ContentOffset") is not { } content ||
            Table(archive, tocOffset) is not { } toc)
        {
            return null;
        }
        for (var row = 0; row < toc.RowCount; row++)
        {
            if (toc.Text(row, "FileName") != name || (toc.Text(row, "DirName") ?? "") != dir)
            {
                continue;
            }
            // Every offset within the archive, so their sum can't overflow or point before its start.
            if (toc.Number(row, "FileSize") is not { } size || toc.Number(row, "ExtractSize") != size ||
                toc.Number(row, "FileOffset") is not { } offset || size < 0 || size > maxSize ||
                !Within(archive, offset) || !Within(archive, tocOffset) || !Within(archive, content))
            {
                return null;
            }
            var bytes = Read(archive, offset + Math.Min(tocOffset, content), (int)size);
            return bytes is null || bytes.AsSpan().StartsWith("CRILAYLA"u8) ? null : bytes;
        }
        return null;
    }

    // The @UTF table in the container at offset, or null.
    private static UtfTable? Table(Stream archive, long offset)
    {
        if (Read(archive, offset, ContainerSize) is not { } container)
        {
            return null;
        }
        var size = BinaryPrimitives.ReadInt32LittleEndian(container.AsSpan(8));
        return size is > 0 and <= MaxTableSize && Read(archive, offset + ContainerSize, size) is { } table
            ? UtfTable.Parse(table)
            : null;
    }

    private static bool Within(Stream archive, long offset) => offset >= 0 && offset <= archive.Length;

    private static byte[]? Read(Stream archive, long offset, int length)
    {
        if (offset < 0 || offset > archive.Length - length)
        {
            return null;
        }
        archive.Position = offset;
        var bytes = new byte[length];
        archive.ReadExactly(bytes);
        return bytes;
    }
}

/// <summary>A CRI <c>@UTF</c> table (big-endian), as CriFsV2Lib reads one
/// (docs/raw-sources/cri-cpk-table-layout-2026-11-21.md): the header, the columns from <c>+0x20</c>, then fixed-size
/// rows. A column has a flag byte (its type in the low four bits; <c>0x10</c> named, <c>0x20</c> a default value
/// follows, <c>0x40</c> stored in each row), a name, and its default. Every read is bounds-checked.</summary>
public sealed class UtfTable
{
    private const int BaseOffset = 8;
    private const int ColumnsStart = 0x20;
    private const byte Named = 0x10;
    private const byte HasDefault = 0x20;
    private const byte PerRow = 0x40;
    private const byte StringType = 10;

    private readonly byte[] bytes;
    private readonly int rowsStart;
    private readonly int rowSize;
    private readonly int stringPool;
    private readonly Dictionary<string, Column> columns;

    private UtfTable(byte[] bytes, int rowsStart, int rowSize, int stringPool, int rowCount, Dictionary<string, Column> columns)
    {
        this.bytes = bytes;
        this.rowsStart = rowsStart;
        this.rowSize = rowSize;
        this.stringPool = stringPool;
        this.columns = columns;
        RowCount = rowCount;
    }

    /// <summary>How many rows the table has.</summary>
    public int RowCount { get; }

    /// <summary>The table in <paramref name="bytes"/>, from its <c>@UTF</c> magic, or null when they aren't one or a
    /// column or row runs past their end.</summary>
    public static UtfTable? Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return ParseChecked(bytes);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            // A column list that runs past the end: refused, never thrown into the game.
            return null;
        }
    }

    /// <summary>The integer in <paramref name="column"/> at <paramref name="row"/>, or null when there's no such column
    /// or it isn't an integer.</summary>
    public long? Number(int row, string column)
    {
        if (Value(row, column) is not { } at || at.Column.Type > 7)
        {
            return null;
        }
        var span = bytes.AsSpan(at.Offset);
        return at.Column.Type switch
        {
            0 => span[0],
            1 => (sbyte)span[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(span),
            3 => BinaryPrimitives.ReadInt16BigEndian(span),
            4 => BinaryPrimitives.ReadUInt32BigEndian(span),
            5 => BinaryPrimitives.ReadInt32BigEndian(span),
            _ => BinaryPrimitives.ReadInt64BigEndian(span),
        };
    }

    /// <summary>The string in <paramref name="column"/> at <paramref name="row"/>, or null when there's no such column,
    /// it isn't a string, or its text runs past the table. Decoded as UTF-8, which matches the archive's ASCII paths.
    /// </summary>
    public string? Text(int row, string column) =>
        Value(row, column) is { Column.Type: StringType } at
            ? StringAt(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at.Offset)))
            : null;

    private static UtfTable? ParseChecked(byte[] bytes)
    {
        if (bytes.Length < ColumnsStart || !bytes.AsSpan().StartsWith("@UTF"u8))
        {
            return null;
        }
        var span = bytes.AsSpan();
        var rowsStart = BinaryPrimitives.ReadUInt16BigEndian(span[0x0a..]) + BaseOffset;
        var stringPool = BinaryPrimitives.ReadInt32BigEndian(span[0x0c..]) + BaseOffset;
        var columnCount = BinaryPrimitives.ReadUInt16BigEndian(span[0x18..]);
        var rowSize = BinaryPrimitives.ReadUInt16BigEndian(span[0x1a..]);
        var rowCount = BinaryPrimitives.ReadInt32BigEndian(span[0x1c..]);
        // Rows of no bytes would let a damaged count ask for billions of rows from a short table.
        if (stringPool < BaseOffset || stringPool > bytes.Length || rowCount < 0 || (rowSize == 0 && rowCount > 0) ||
            (long)rowsStart + (long)rowSize * rowCount > bytes.Length)
        {
            return null;
        }
        var columns = new Dictionary<string, Column>(StringComparer.Ordinal);
        var at = ColumnsStart;
        var inRow = 0;
        for (var i = 0; i < columnCount; i++)
        {
            var flags = span[at];
            var type = (byte)(flags & 0x0f);
            var length = ValueLength(type);
            at++;
            string? name = null;
            if ((flags & Named) != 0)
            {
                name = StringAt(bytes, stringPool, BinaryPrimitives.ReadInt32BigEndian(span[at..]));
                at += sizeof(int);
            }
            int? defaultAt = null;
            if ((flags & HasDefault) != 0)
            {
                defaultAt = at;
                at += length;
            }
            int? rowOffset = null;
            if ((flags & PerRow) != 0)
            {
                rowOffset = inRow;
                inRow += length;
            }
            if (length == 0 || inRow > rowSize || at > bytes.Length)
            {
                return null;
            }
            if (name is not null)
            {
                columns.TryAdd(name, new(type, rowOffset, defaultAt));
            }
        }
        return new(bytes, rowsStart, rowSize, stringPool, rowCount, columns);
    }

    // CriFsV2Lib's GetValueLength; 0 for a type it doesn't know.
    private static int ValueLength(byte type) => type switch
    {
        0 or 1 => 1,
        2 or 3 => 2,
        4 or 5 or 8 or 10 => 4,
        6 or 7 or 9 or 11 => 8,
        12 => 16,
        _ => 0,
    };

    private (Column Column, int Offset)? Value(int row, string column)
    {
        if (row < 0 || row >= RowCount || !columns.TryGetValue(column, out var found))
        {
            return null;
        }
        return found.RowOffset is { } inRow ? (found, rowsStart + row * rowSize + inRow)
            : found.DefaultAt is { } fixedAt ? (found, fixedAt)
            : null;
    }

    private string? StringAt(int offset) => StringAt(bytes, stringPool, offset);

    private static string? StringAt(byte[] bytes, int pool, int offset)
    {
        if (offset < 0 || pool + (long)offset >= bytes.Length)
        {
            return null;
        }
        var text = bytes.AsSpan(pool + offset);
        var end = text.IndexOf((byte)0);
        return end < 0 ? null : Encoding.UTF8.GetString(text[..end]);
    }

    private sealed record Column(byte Type, int? RowOffset, int? DefaultAt);
}
