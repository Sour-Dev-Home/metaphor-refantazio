using System.Diagnostics;
using System.Runtime.InteropServices;
using Metaphor.Core;

namespace Metaphor.Hooks;

/// <summary>The hook layer's only native memory reads: the main module's readable regions, raw copies for
/// <see cref="ChunkedScan"/>, and safe reads through pointers the game hands hooks. Only reads: nothing is written,
/// patched or reprotected, which ADR 0002 allows ("Our own Reloaded-II mod reading the running game's memory").</summary>
internal static unsafe class ModuleMemoryReader
{
    // GetCurrentProcess's pseudo-handle, so no real handle to the game is opened.
    // https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getcurrentprocess
    private static readonly nint CurrentProcess = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nuint VirtualQuery(nint address, out MemoryBasicInformation buffer, nuint length);

    // Copies through the kernel, which returns false on a page it can't read instead of faulting, so a page whose
    // protection changes after VirtualQuery can't crash the game.
    // https://learn.microsoft.com/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address, byte* buffer, nuint size, out nuint read);

    /// <summary>The main module's base address and its committed, readable stretches as RVAs and lengths, for
    /// <see cref="ChunkedScan"/> with <see cref="ReadAt"/> (#39). Nothing is copied here.</summary>
    public static (nint Base, IReadOnlyList<(long Rva, long Length)> Regions) MainModuleRegions()
    {
        using var process = Process.GetCurrentProcess();
        var module = process.MainModule ?? throw new InvalidOperationException("the game process has no main module");
        var start = module.BaseAddress;
        return (start, [.. ReadableSpans(start, start + module.ModuleMemorySize)
            .Select(span => ((long)(span.Start - start), (long)span.Length))]);
    }

    /// <summary>Copies <paramref name="buffer"/>.Length bytes from <paramref name="rva"/> past <paramref name="moduleBase"/>
    /// through <c>ReadProcessMemory</c>, which fails instead of faulting; false unless every byte arrived.</summary>
    public static bool ReadAt(nint moduleBase, long rva, Span<byte> buffer) => Copy(moduleBase + (nint)rva, buffer);

    /// <summary>The main module's base address.</summary>
    public static nint MainModuleBase()
    {
        using var process = Process.GetCurrentProcess();
        return process.MainModule?.BaseAddress ?? throw new InvalidOperationException("the game process has no main module");
    }

    // The readers below are for hooks reading through pointers the game hands them. They all copy through
    // ReadProcessMemory, because .NET can't catch the access violation a bad pointer would cause: a failed copy gives
    // null instead.

    /// <summary>Reads one pointer-sized value at <paramref name="address"/>, or null when it can't be read.</summary>
    public static nint? ReadPointer(nint address)
    {
        nint value = 0;
        return ReadProcessMemory(CurrentProcess, address, (byte*)&value, (nuint)sizeof(nint), out var read) &&
            read == (nuint)sizeof(nint)
            ? value
            : null;
    }

    /// <summary>Reads one 32-bit value at <paramref name="address"/>, or null when it can't be read.</summary>
    public static int? ReadInt32(nint address)
    {
        var value = 0;
        return ReadProcessMemory(CurrentProcess, address, (byte*)&value, sizeof(int), out var read) && read == sizeof(int)
            ? value
            : null;
    }

    /// <summary>Reads three consecutive 32-bit floats at <paramref name="address"/>, such as a position, or null when
    /// they can't be read. Allocates nothing.</summary>
    public static (float X, float Y, float Z)? ReadVector3(nint address)
    {
        var values = stackalloc float[3];
        return ReadProcessMemory(CurrentProcess, address, (byte*)values, 3 * sizeof(float), out var read) &&
            read == 3 * sizeof(float)
            ? (values[0], values[1], values[2])
            : null;
    }

    /// <summary>Reads four consecutive 32-bit floats at <paramref name="address"/>, such as a quaternion, or null when
    /// they can't be read. Allocates nothing.</summary>
    public static (float X, float Y, float Z, float W)? ReadVector4(nint address)
    {
        var values = stackalloc float[4];
        return ReadProcessMemory(CurrentProcess, address, (byte*)values, 4 * sizeof(float), out var read) &&
            read == 4 * sizeof(float)
            ? (values[0], values[1], values[2], values[3])
            : null;
    }

    /// <summary>Fills <paramref name="buffer"/> from <paramref name="address"/>; false unless every byte arrived.</summary>
    public static bool ReadBytes(nint address, Span<byte> buffer) => Copy(address, buffer);

    /// <summary>Fills <paramref name="buffer"/> from <paramref name="address"/> as <see cref="ReadBytes"/> does, but only
    /// when <c>VirtualQuery</c> says every byte is committed, readable and unguarded; false otherwise, without touching
    /// the memory. For addresses nobody vouches for, such as the live inspector's (#393): Microsoft documents that a system
    /// service touching a guard page fails and clears the guard, so a guard page is never handed to
    /// <c>ReadProcessMemory</c>, although one Windows build was seen to leave it guarded
    /// (docs/raw-sources/windows-guard-pages-and-virtualquery-2026-11-19.md). A page that changes between the query and
    /// the copy is not covered.</summary>
    public static bool ReadBytesAvoidingGuardPages(nint address, Span<byte> buffer) =>
        PageRegion.AllReadable(address, buffer.Length, Query) && Copy(address, buffer);

    /// <summary>Reads a NUL-terminated ANSI string at <paramref name="address"/>, up to <paramref name="maxBytes"/>,
    /// as <see cref="MemoryText.ReadAnsi"/> does; null when its start can't be read.</summary>
    public static string? ReadAnsiString(nint address, int maxBytes = 512) => MemoryText.ReadAnsi(CopyAt, address, maxBytes);

    /// <summary>Reads <paramref name="length"/> bytes at <paramref name="address"/> as UTF-8, as
    /// <see cref="MemoryText.ReadUtf8"/> does; null when the length is negative or over <paramref name="maxBytes"/>, or
    /// the bytes can't all be read.</summary>
    public static string? ReadUtf8(nint address, int length, int maxBytes = 16 << 20) =>
        MemoryText.ReadUtf8(CopyAt, address, length, maxBytes);

    private static bool CopyAt(long address, Span<byte> buffer) => Copy((nint)address, buffer);

    private static bool Copy(nint address, Span<byte> buffer)
    {
        fixed (byte* target = buffer)
        {
            return ReadProcessMemory(CurrentProcess, address, target, (nuint)buffer.Length, out var read) &&
                read == (nuint)buffer.Length;
        }
    }

    /// <summary>The region holding <paramref name="address"/>, or null when <c>VirtualQuery</c> fails, as it does above
    /// the highest address the process can reach.
    /// https://learn.microsoft.com/windows/win32/api/memoryapi/nf-memoryapi-virtualquery</summary>
    private static PageRegion? Query(long address)
    {
        if (VirtualQuery((nint)address, out var region, (nuint)sizeof(MemoryBasicInformation)) == 0)
        {
            return null;
        }
        // A region's size never reaches 2^63 bytes in a 64-bit address space, so the cast can't wrap.
        return new PageRegion(region.BaseAddress, (long)region.RegionSize, region.State, region.Protect);
    }

    /// <summary>The committed, readable, unguarded stretches of [<paramref name="start"/>, <paramref name="end"/>),
    /// with touching regions merged.</summary>
    private static List<(nint Start, int Length)> ReadableSpans(nint start, nint end)
    {
        var spans = new List<(nint Start, int Length)>();
        nint spanStart = 0, spanEnd = 0;
        for (var address = start; address < end;)
        {
            var region = Query(address) ??
                throw new InvalidOperationException($"VirtualQuery failed at module offset 0x{address - start:x}");
            var regionEnd = (nint)Math.Min(region.Base + region.Size, end);
            var readable = region.IsReadable;
            if (readable && address == spanEnd && spanEnd != 0)
            {
                spanEnd = regionEnd;
            }
            else if (readable)
            {
                Add();
                (spanStart, spanEnd) = (address, regionEnd);
            }
            address = regionEnd;
        }
        Add();
        return spans;

        void Add()
        {
            if (spanEnd > spanStart)
            {
                // A module's size is an int (ProcessModule.ModuleMemorySize), so a span inside it always fits.
                spans.Add((spanStart, checked((int)(spanEnd - spanStart))));
            }
        }
    }
}
