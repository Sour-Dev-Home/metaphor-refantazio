using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Metaphor.Core;

// Issue #118: what the development-only tutorial pop-up listeners decode, kept here so it is tested without the game.
// The offsets are our reading of the exe on one build (docs/findings/tutorial-popup-window-static.md) and are leads
// until a run shows them.

/// <summary>The tutorial pop-up's window (<c>app::ui::UUIC_Common_TutorialWindow</c>, #118), from its first
/// <see cref="Length"/> bytes: its class (vtable), the 32-bit fields from <c>+0x18</c> to <c>+0x50</c> (the update's
/// state is <c>+0x18</c>, the open writes the tutorial number to <c>+0x1C</c>), the four strings of the array at
/// <c>+0x60</c> (the open fills them with movie paths) and the object at <c>+0xE0</c> the open builds for the tutorial.
/// What each field means is a lead, not a finding.</summary>
public sealed record TutorialWindowState(ObjectFields Window, IReadOnlyList<string?> Strings, long Model)
{
    /// <summary>The window's size, from its deleting destructor.</summary>
    public const int Length = 0xe8;

    /// <summary>Where the logged 32-bit fields start.</summary>
    public const int FieldsStart = 0x18;

    /// <summary>Where the logged 32-bit fields end (exclusive).</summary>
    public const int FieldsEnd = 0x50;

    /// <summary>Where the string array's elements start.</summary>
    public const int StringsStart = 0x60;

    /// <summary>How many strings the array holds.</summary>
    public const int StringCount = 4;

    /// <summary>Where the pointer to the tutorial's object is.</summary>
    public const int ModelAt = 0xe0;

    /// <summary>The window's state in <paramref name="bytes"/>, its first <see cref="Length"/> bytes, or null when fewer
    /// were given. <paramref name="readText"/> reads a string's heap buffer by address and length.</summary>
    public static TutorialWindowState? Read(
        long window, long moduleBase, long moduleSize, ReadOnlySpan<byte> bytes, Func<long, int, string?> readText)
    {
        ArgumentNullException.ThrowIfNull(readText);
        if (bytes.Length < Length ||
            ObjectFields.Read(window, moduleBase, moduleSize, bytes[..FieldsEnd], FieldsStart) is not { } fields)
        {
            return null;
        }
        var strings = new string?[StringCount];
        for (var i = 0; i < StringCount; i++)
        {
            var at = StringsStart + i * MsvcString.Size;
            strings[i] = MsvcString.Read(bytes[at..(at + MsvcString.Size)], readText);
        }
        return new(fields, strings, BinaryPrimitives.ReadInt64LittleEndian(bytes[ModelAt..]));
    }

    /// <summary>The bytes whose change makes a window worth logging again: everything after its first fields.</summary>
    public static byte[] StateKey(ReadOnlySpan<byte> bytes) =>
        bytes.Length < Length ? [] : bytes[FieldsStart..Length].ToArray();

    /// <summary>The log's form: the window's <see cref="ObjectFields"/>, then <c>strings="…",(none),…
    /// model=0x…</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Window} strings={string.Join(',', Strings.Select(s => s is null ? "(none)" : $"\"{LogText.Escaped(s)}\""))} model=0x{Model:x}");
}

/// <summary>An object of unknown layout as 32-bit fields (#118): its address, its class (the vtable's offset in the
/// module, when it points into it) and its fields from a given offset to the end of the bytes read.</summary>
public sealed record ObjectFields(long Address, long? ClassRva, int Start, IReadOnlyList<int> Fields)
{
    /// <summary>The object in <paramref name="bytes"/>, its fields from <paramref name="start"/>; null when the bytes
    /// don't reach past the vtable and the start.</summary>
    public static ObjectFields? Read(long address, long moduleBase, long moduleSize, ReadOnlySpan<byte> bytes, int start)
    {
        if (start < sizeof(long) || bytes.Length < start + sizeof(int))
        {
            return null;
        }
        var vtable = BinaryPrimitives.ReadInt64LittleEndian(bytes);
        var fields = new int[(bytes.Length - start) / sizeof(int)];
        for (var i = 0; i < fields.Length; i++)
        {
            fields[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes[(start + i * sizeof(int))..]);
        }
        return new(address, vtable >= moduleBase && vtable - moduleBase < moduleSize ? vtable - moduleBase : null, start,
            fields);
    }

    /// <summary>The fields of <paramref name="bytes"/>, 8-aligned from <paramref name="start"/>, that look like heap
    /// pointers: 8-aligned user-space addresses outside the module. At most <paramref name="max"/>, in field order.
    /// Only a lead for what to read next (#118); the reader still has to treat each as unverified.</summary>
    public static IReadOnlyList<(int Offset, long Address)> HeapPointers(
        ReadOnlySpan<byte> bytes, int start, long moduleBase, long moduleSize, int max)
    {
        var found = new List<(int, long)>();
        for (var at = start + ((sizeof(long) - start % sizeof(long)) % sizeof(long));
             at + sizeof(long) <= bytes.Length && found.Count < max; at += sizeof(long))
        {
            var value = BinaryPrimitives.ReadInt64LittleEndian(bytes[at..]);
            var inModule = value >= moduleBase && value - moduleBase < moduleSize;
            if (value is >= LowestHeap and < HighestUser && value % sizeof(long) == 0 && !inModule)
            {
                found.Add((at, value));
            }
        }
        return found;
    }

    // The 64 KiB Windows never maps at the bottom, and the top of x64 user space.
    private const long LowestHeap = 0x10000;
    private const long HighestUser = 0x7fff_ffff_0000;

    /// <summary>The log's form: <c>at=0x… class=0x… +0x18=0x…,0x…,…</c>, the fields in order from the start.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"at=0x{Address:x} class={(ClassRva is { } rva ? $"0x{rva:x}" : "?")} +0x{Start:x}={string.Join(',', Fields.Select(f => $"0x{f:x}"))}");
}

/// <summary>Reads a <c>std::string</c> in MSVC's layout: a 16-byte buffer that holds the text while the capacity is under
/// 16 and a pointer to it otherwise, then the size and the capacity, 8 bytes each (microsoft/STL's <c>xstring</c>,
/// <c>_String_val</c>). The tutorial window's open tests <c>capacity &gt; 15</c> before taking the pointer (finding's
/// step 5); that every string here keeps this layout is [NEEDS VERIFICATION].</summary>
public static class MsvcString
{
    /// <summary>The object's size.</summary>
    public const int Size = 0x20;

    /// <summary>The longest text read; a longer size is taken as not a string.</summary>
    public const int MaxLength = 256;

    // The inline buffer's size; an empty string has a capacity of one less.
    private const int BufferSize = 16;

    /// <summary>The text of the string in <paramref name="bytes"/>, or null when they don't look like one (a size over
    /// its capacity or over <see cref="MaxLength"/>, a capacity under 15, or heap text <paramref name="readText"/>
    /// couldn't read). <paramref name="readText"/> reads a heap buffer by address and length.</summary>
    public static string? Read(ReadOnlySpan<byte> bytes, Func<long, int, string?> readText)
    {
        ArgumentNullException.ThrowIfNull(readText);
        if (bytes.Length < Size)
        {
            return null;
        }
        var size = BinaryPrimitives.ReadInt64LittleEndian(bytes[0x10..]);
        var capacity = BinaryPrimitives.ReadInt64LittleEndian(bytes[0x18..]);
        if (size < 0 || size > capacity || size > MaxLength || capacity < BufferSize - 1)
        {
            return null;
        }
        return capacity < BufferSize
            ? Encoding.UTF8.GetString(bytes[..(int)size])
            : readText(BinaryPrimitives.ReadInt64LittleEndian(bytes), (int)size);
    }
}
