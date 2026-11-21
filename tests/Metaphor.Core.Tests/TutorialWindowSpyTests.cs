using System.Buffers.Binary;
using System.Text;

namespace Metaphor.Core.Tests;

/// <summary>What the #118 tutorial pop-up listeners decode: the window's fields and strings, an object's fields, and
/// MSVC strings. Made-up objects and text only.</summary>
[Trait("Category", "Unit")]
public sealed class TutorialWindowSpyTests
{
    private const long ModuleBase = 0x140000000;
    private const long ModuleSize = 0x20000000;
    private const long HeapText = 0x7000;

    private static string? NoHeap(long address, int length) => null;

    private static string? Heap(long address, int length) => address == HeapText ? new string('h', length) : null;

    private static void PutString(Span<byte> at, string? inline, long size, long capacity, long pointer = 0)
    {
        if (inline is not null)
        {
            Encoding.UTF8.GetBytes(inline, at);
        }
        else
        {
            BinaryPrimitives.WriteInt64LittleEndian(at, pointer);
        }
        BinaryPrimitives.WriteInt64LittleEndian(at[0x10..], size);
        BinaryPrimitives.WriteInt64LittleEndian(at[0x18..], capacity);
    }

    private static byte[] Window(int state = 1)
    {
        var bytes = new byte[TutorialWindowState.Length];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, ModuleBase + 0x22576a0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x18), state);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x1C), 42);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x4C), 7);
        PutString(bytes.AsSpan(0x60), "a/b", 3, 15);
        PutString(bytes.AsSpan(0x80), null, 20, 31, HeapText);
        PutString(bytes.AsSpan(0xA0), "", 0, 15);
        PutString(bytes.AsSpan(0xC0), null, 0, 0);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(0xE0), 0x9000);
        return bytes;
    }

    [Fact]
    public void AWindowIsReadFromItsFieldsStringsAndModel()
    {
        var state = TutorialWindowState.Read(0x40, ModuleBase, ModuleSize, Window(), Heap)!;

        Assert.Equal(0x22576a0, state.Window.ClassRva);
        Assert.Equal((TutorialWindowState.FieldsEnd - TutorialWindowState.FieldsStart) / 4, state.Window.Fields.Count);
        Assert.Equal([1, 42], state.Window.Fields.Take(2));
        Assert.Equal(7, state.Window.Fields[^1]);
        Assert.Equal(["a/b", new string('h', 20), "", null], state.Strings);
        Assert.Equal(0x9000, state.Model);
    }

    [Fact]
    public void AWindowIsLoggedOnOneLine()
    {
        var bytes = Window();
        PutString(bytes.AsSpan(0x60), "a\"\n", 3, 15);

        var line = TutorialWindowState.Read(0x40, ModuleBase, ModuleSize, bytes, NoHeap)!.ToString();

        Assert.StartsWith("at=0x40 class=0x22576a0 +0x18=0x1,0x2a,0x0,", line);
        Assert.EndsWith("strings=\"a\\x22\\x0a\",(none),\"\",(none) model=0x9000", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void TooFewBytesReadAsNoWindow()
    {
        Assert.Null(TutorialWindowState.Read(0x40, ModuleBase, ModuleSize, new byte[TutorialWindowState.Length - 1], NoHeap));
        Assert.Empty(TutorialWindowState.StateKey(new byte[TutorialWindowState.Length - 1]));
    }

    [Fact]
    public void TheStateKeyChangesWithTheFieldsButNotTheVtable()
    {
        var first = Window();
        var otherVtable = Window();
        BinaryPrimitives.WriteInt64LittleEndian(otherVtable, 1);

        Assert.Equal(TutorialWindowState.StateKey(first), TutorialWindowState.StateKey(otherVtable));
        Assert.NotEqual(TutorialWindowState.StateKey(first), TutorialWindowState.StateKey(Window(state: 2)));
    }

    [Fact]
    public void AnObjectsFieldsStartWhereAsked()
    {
        var bytes = new byte[0x10];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, 5);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 3);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), -1);

        var fields = ObjectFields.Read(0x50, ModuleBase, ModuleSize, bytes, 8)!;

        Assert.Null(fields.ClassRva);
        Assert.Equal([3, -1], fields.Fields);
        Assert.Equal("at=0x50 class=? +0x8=0x3,0xffffffff", fields.ToString());
        Assert.Null(ObjectFields.Read(0x50, ModuleBase, ModuleSize, bytes, 4));
        Assert.Null(ObjectFields.Read(0x50, ModuleBase, ModuleSize, bytes.AsSpan(0, 10), 8));
    }

    [Theory]
    [InlineData(20L, 15L)]
    [InlineData(-1L, 15L)]
    [InlineData(300L, 400L)]
    [InlineData(0L, 3L)]
    public void BytesThatAreNotAStringReadAsNone(long size, long capacity)
    {
        var bytes = new byte[MsvcString.Size];
        PutString(bytes, "abc", size, capacity);

        Assert.Null(MsvcString.Read(bytes, Heap));
    }

    [Fact]
    public void AHeapStringIsReadThroughItsPointer()
    {
        var bytes = new byte[MsvcString.Size];
        PutString(bytes, null, 16, 16, HeapText);

        Assert.Equal(new string('h', 16), MsvcString.Read(bytes, Heap));
        Assert.Null(MsvcString.Read(bytes, NoHeap));
        Assert.Null(MsvcString.Read(bytes.AsSpan(0, MsvcString.Size - 1), Heap));
    }

    [Fact]
    public void HeapPointersAreTheAlignedUserAddressesOutsideTheModule()
    {
        var bytes = new byte[0x48];
        long[] values = [0x7000_1234_5000, ModuleBase + 0x100, 0x20, 0x7000_0000_0004, -1, 0x2_0000_0000, 0x8000_0000_0000, 0, 0x3000_0000];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * 8), values[i]);
        }

        Assert.Equal([(0, 0x7000_1234_5000L), (0x28, 0x2_0000_0000L), (0x40, 0x3000_0000L)],
            ObjectFields.HeapPointers(bytes, 0, ModuleBase, ModuleSize, max: 8));
        // A start between fields moves to the next aligned one; the limit stops the search.
        Assert.Equal([(0x28, 0x2_0000_0000L)], ObjectFields.HeapPointers(bytes, 0x4, ModuleBase, ModuleSize, max: 1));
        Assert.Equal([(0x28, 0x2_0000_0000L), (0x40, 0x3000_0000L)],
            ObjectFields.HeapPointers(bytes, 0x9, ModuleBase, ModuleSize, max: 8));
    }
}
