using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Metaphor.Core.Navigation;

/// <summary>Field maps' walk graphs, read lazily from the game's archive (#179): the first <see cref="Get"/> for a map
/// starts reading its <c>.NAV</c> file on the thread pool and returns null; later calls return the graph once it has
/// loaded. Read-only: the archive is opened, read and closed for each file. The last <see cref="Capacity"/> maps used
/// are kept, and one that couldn't be read is remembered as missing, so a map is read at most once while kept.
/// Which file belongs to the map the player is on is [NEEDS VERIFICATION]
/// (docs/findings/field-navmesh-files-tile-cache-static.md, from #639), so callers name the file's two numbers.
/// </summary>
public sealed class NavMeshSource
{
    /// <summary>Where the game keeps the files in its archive.</summary>
    public const string Folder = "COMMON/model/field/navi";

    private readonly Func<Stream> open;
    private readonly Action<string> log;
    private readonly Dictionary<string, Task<WalkGraph?>> loads = [];
    private readonly LinkedList<string> used = [];
    private readonly Lock gate = new();

    /// <summary>A source that opens the archive with <paramref name="open"/> and tells <paramref name="log"/> what came
    /// of each read: a line naming the file, never its folder on disk.</summary>
    public NavMeshSource(Func<Stream> open, Action<string> log, int capacity = 4)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(log);
        this.open = open;
        this.log = log;
        Capacity = Math.Max(1, capacity);
    }

    /// <summary>How many maps are kept.</summary>
    public int Capacity { get; }

    /// <summary>The archive path of map file <c>f_&lt;a&gt;_&lt;b&gt;.NAV</c>, as the game names them
    /// (<c>f_0000_014.NAV</c>), or null when <paramref name="a"/> isn't 0 to 9999 or <paramref name="b"/> 0 to 999.
    /// </summary>
    public static string? ArchivePath(int a, int b) => a is >= 0 and <= 9999 && b is >= 0 and <= 999
        ? string.Create(CultureInfo.InvariantCulture, $"{Folder}/f_{a:D4}_{b:D3}.NAV")
        : null;

    /// <summary>Map <c>f_&lt;a&gt;_&lt;b&gt;</c>'s graph if it has loaded; otherwise null, starting its read if none
    /// has started. Null too when the file is missing or unreadable. Never blocks on the archive.</summary>
    public WalkGraph? Get(int a, int b)
    {
        var load = Load(a, b);
        return load is { IsCompletedSuccessfully: true } ? load.Result : null;
    }

    /// <summary>The read of map <c>f_&lt;a&gt;_&lt;b&gt;</c>, started if it hasn't been, or null when the numbers are
    /// out of range. Its result is the graph, or null when the file couldn't be read.</summary>
    public Task<WalkGraph?>? Load(int a, int b)
    {
        if (ArchivePath(a, b) is not { } path)
        {
            return null;
        }
        lock (gate)
        {
            if (loads.TryGetValue(path, out var load))
            {
                used.Remove(path);
                used.AddFirst(path);
                return load;
            }
            load = Task.Run(() => Read(path));
            loads[path] = load;
            used.AddFirst(path);
            while (used.Count > Capacity)
            {
                loads.Remove(used.Last!.Value);
                used.RemoveLast();
            }
            return load;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed read leaves the map without a graph; it must not reach the game.")]
    private WalkGraph? Read(string path)
    {
        var name = path[(Folder.Length + 1)..];
        try
        {
            using var archive = open();
            if (NavMeshFile.FromArchive(archive, path) is not { } file)
            {
                log($"navmesh: no readable {name} in the archive");
                return null;
            }
            log(string.Create(CultureInfo.InvariantCulture,
                $"navmesh: {name}: {file.Tiles.Count} tiles read, {file.FailedTiles} skipped"));
            return new WalkGraph(file);
        }
        catch (Exception e)
        {
            log($"navmesh: can't read {name}: {e.GetType().Name}");
            return null;
        }
    }
}
