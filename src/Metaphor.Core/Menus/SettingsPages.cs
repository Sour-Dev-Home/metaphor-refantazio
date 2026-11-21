namespace Metaphor.Core.Menus;

// Issue #560: the hook layer reads and logs what this names, kept here so it is tested without the game.

/// <summary>The four settings pages (System, Sound, Graphics, Display, in the category list's order) and where each keeps
/// its working copy of the values, on game build 548ddc95… (Steam build 18330018).</summary>
public static class SettingsPages
{
    /// <summary>The pages' screen classes as run-time type information names them
    /// (docs/raw-sources/settings-category-labels-session1-2026-11-20.md).</summary>
    public static IReadOnlyList<string> Classes { get; } =
    [
        "?$ConfigMenuChoicePcT@$0HG@@ope@ui@app",
        "?$ConfigMenuChoicePcT@$0HH@@ope@ui@app",
        "?$ConfigMenuChoicePcT@$0HI@@ope@ui@app",
        "?$ConfigMenuChoicePcT@$0HJ@@ope@ui@app",
    ];

    /// <summary>A page keeps a dword per setting ID in a container at <c>+0x4c8</c> of its choice, its length at
    /// <c>+0x4d0</c> (docs/findings/settings-screens-inventory-static.md). The container reaches its data through a
    /// virtual call, so where the dwords sit is <c>[NEEDS VERIFICATION]</c>; the Debug log dumps
    /// <see cref="WorkingCopyLength"/> bytes from here on so a session shows which bytes change (#558's check 2).</summary>
    public const int WorkingCopyOffset = 0x4c8;

    /// <summary>How many bytes the Debug log dumps: the container's header and, if its data is inline, about 60 IDs
    /// (the PC settings go up to 49).</summary>
    public const int WorkingCopyLength = 0x100;

    /// <summary>Whether <paramref name="screenClass"/> is one of the four pages.</summary>
    public static bool IsPage(string? screenClass) => screenClass is not null && Classes.Contains(screenClass);
}

/// <summary>Gives each object's bytes as hex the first time and whenever they change, and null otherwise, so a log of
/// them holds only changes. Keeps at most 64 objects, then starts over. Safe to use from several threads.</summary>
public sealed class ChangedBytes
{
    private const int MaxObjects = 64;

    private readonly Dictionary<long, string> last = [];
    private readonly Lock gate = new();

    /// <summary>The bytes as lower-case hex when they are new for <paramref name="key"/> or differ from its last, else
    /// null.</summary>
    public string? Changed(long key, ReadOnlySpan<byte> bytes)
    {
        var hex = Convert.ToHexStringLower(bytes);
        lock (gate)
        {
            if (last.TryGetValue(key, out var before) && before == hex)
            {
                return null;
            }
            if (last.Count >= MaxObjects && !last.ContainsKey(key))
            {
                last.Clear();
            }
            last[key] = hex;
            return hex;
        }
    }
}
