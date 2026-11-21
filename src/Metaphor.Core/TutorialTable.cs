using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Metaphor.Core;

/// <summary>A tutorial pop-up's messages (#118): the MD5 of its title's label and of each page's label, in page order,
/// as 32 upper-case hex digits (the key <see cref="Messages.MessageLibrary.FindByLabelHash"/> takes).</summary>
public sealed record TutorialEntry(string TitleHash, IReadOnlyList<string> PageHashes);

/// <summary>The game's tutorial table, <c>COMMON/ui/tutorial/TutorialIDPC.bin</c> (#118): for each tutorial number,
/// the labels of its title and pages, by MD5. Layout on game build 548ddc95… (Steam build 18330018), read offline
/// (docs/findings/tutorial-table-maps-numbers-to-labels.md): <c>TUTO</c>, the entry count at <c>+0x8</c>, then
/// <c>0xE0</c>-byte entries from <c>+0x10</c>, each a 32-bit field, the title's label hash at <c>+0x4</c> and four
/// <c>0x32</c>-byte page slots from <c>+0x14</c> whose first 16 bytes are the page's label hash, the MD5 of an empty
/// label when the slot is unused.</summary>
public sealed class TutorialTable
{
    /// <summary>Where the game keeps the table in its archive.</summary>
    public const string ArchivePath = "COMMON/ui/tutorial/TutorialIDPC.bin";

    /// <summary>The largest table read: the game's is 46,160 bytes.</summary>
    public const int MaxSize = 1024 * 1024;

    private const int EntriesStart = 0x10;
    private const int EntrySize = 0xe0;
    private const int TitleAt = 0x4;
    private const int SlotsStart = 0x14;
    private const int SlotSize = 0x32;
    private const int SlotCount = 4;
    private const int HashSize = 16;

    private static readonly byte[] EmptyLabel = Convert.FromHexString(Messages.MessageLibrary.LabelHash(""));

    private readonly IReadOnlyList<TutorialEntry> entries;

    private TutorialTable(IReadOnlyList<TutorialEntry> entries) => this.entries = entries;

    /// <summary>How many tutorials the table has.</summary>
    public int Count => entries.Count;

    /// <summary>The table in <paramref name="bytes"/>, or null when they don't start with its magic or are shorter than
    /// its count says.</summary>
    public static TutorialTable? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < EntriesStart || !bytes.StartsWith("TUTO"u8))
        {
            return null;
        }
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        if (count < 0 || count > (bytes.Length - EntriesStart) / EntrySize)
        {
            return null;
        }
        var entries = new TutorialEntry[count];
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.Slice(EntriesStart + i * EntrySize, EntrySize);
            var pages = new List<string>();
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var hash = entry.Slice(SlotsStart + slot * SlotSize, HashSize);
                if (!hash.SequenceEqual(EmptyLabel) && hash.ContainsAnyExcept((byte)0))
                {
                    pages.Add(Convert.ToHexString(hash));
                }
            }
            entries[i] = new(Convert.ToHexString(entry.Slice(TitleAt, HashSize)), pages);
        }
        return new(entries);
    }

    /// <summary>Tutorial <paramref name="number"/>'s entry, or null when the table has none.</summary>
    public TutorialEntry? Entry(int number) => number >= 0 && number < entries.Count ? entries[number] : null;

    /// <summary>The table read from the game's archive, or null when the archive hasn't it or it doesn't parse.</summary>
    public static TutorialTable? FromArchive(Stream archive) =>
        CpkArchive.ReadFile(archive, ArchivePath, MaxSize) is { } bytes ? Parse(bytes) : null;
}

/// <summary>The tutorial table once it has loaded (#118), read off the game's thread so a pop-up never waits on the
/// archive. <see cref="Table"/> is null until then, and stays null when it couldn't be read.</summary>
public sealed class TutorialTableSource
{
    private volatile TutorialTable? table;

    /// <summary>The table, or null while it loads or when it couldn't be read.</summary>
    public TutorialTable? Table => table;

    /// <summary>Reads the table from the archive <paramref name="open"/> opens, on the thread pool, and tells
    /// <paramref name="done"/> what came of it: a line for the log naming the file, never its folder.</summary>
    public Task LoadInBackground(Func<Stream> open, Action<string> done)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(done);
        return Task.Run(() => done(Load(open)));
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed read leaves tutorials untitled; it must not reach the game.")]
    private string Load(Func<Stream> open)
    {
        try
        {
            using var archive = open();
            table = TutorialTable.FromArchive(archive);
            return table is { } read
                ? $"tutorial table: {read.Count} tutorials"
                : $"tutorial table: no readable {TutorialTable.ArchivePath} in the archive";
        }
        catch (Exception e)
        {
            return $"tutorial table: can't read the archive: {e.GetType().Name}";
        }
    }
}
