using System.Globalization;
using Metaphor.Core;
using Metaphor.Features.Menus;

namespace Metaphor.Hooks;

// Issue #118: the tutorial pop-up's hooks. The functions, their arguments and the window's fields are our reading of
// the exe on one build (docs/findings/tutorial-popup-window-static.md); the class names are the game's own, from its
// run-time type information. Every function is in .ecode; the pages themselves are drawn from .text1, which nothing
// here touches. Every argument is observed pointer-sized (rule 9) and narrowed only for the log.

/// <summary>Shared by the tutorial listeners: the number an open reads through its second argument, and the window's
/// state read safely.</summary>
internal static class TutorialListening
{
    // The most pointers followed out of one object, and how much of each is read, for the page-index search.
    private const int MaxPointees = 8;
    private const int PointeeLength = 0x80;

    // The object's page shown, from 0: it stepped 0, 1, 2, 1, 0 with the page turns of a by-ear run, beside the page
    // count at +0xc (docs/raw-sources/tutorial-page-index-by-ear-2026-11-21.md).
    private const int PageAt = 0x8;

    /// <summary>The page the tutorial's object shows, from 0, or null when it can't be read. The object comes from the
    /// window's unverified layout, so it is read only through the guarded reader.</summary>
    public static int? Page(nint model)
    {
        var bytes = new byte[sizeof(int)];
        return model != 0 && ModuleMemoryReader.ReadBytesAvoidingGuardPages(model + PageAt, bytes)
            ? System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes)
            : null;
    }

    public static string Number(nint at) =>
        ModuleMemoryReader.ReadInt32(at) is { } number ? number.ToString(CultureInfo.InvariantCulture) : "?";

    public static string Byte(nint value) => ((byte)value).ToString(CultureInfo.InvariantCulture);

    /// <summary>The window's state, its bytes, or null when they can't be read. The window is the game's own argument;
    /// the string buffers it points to are read only through the guarded reader, since the layout is still a lead.
    /// </summary>
    public static (TutorialWindowState State, byte[] Bytes)? Window(nint window, CallerAddress module)
    {
        var bytes = new byte[TutorialWindowState.Length];
        if (!ModuleMemoryReader.ReadBytes(window, bytes) ||
            TutorialWindowState.Read(window, module.ModuleBase, module.ModuleSize, bytes, GuardedText) is not { } state)
        {
            return null;
        }
        return (state, bytes);
    }

    /// <summary>The bytes of the object the open builds for the tutorial, or null when they can't be read. The pointer
    /// comes from the window's unverified layout, so it is read only through the guarded reader.</summary>
    public static byte[]? ModelBytes(long model)
    {
        var bytes = new byte[ModelLength];
        return model != 0 && ModuleMemoryReader.ReadBytesAvoidingGuardPages((nint)model, bytes) ? bytes : null;
    }

    /// <summary>The object's fields, <c> model …</c>, or empty when its bytes are null.</summary>
    public static string Model(long model, byte[]? bytes, CallerAddress module) =>
        bytes is not null &&
            ObjectFields.Read(model, module.ModuleBase, module.ModuleSize, bytes, sizeof(long)) is { } fields
            ? $" model {fields}"
            : "";

    /// <summary>What the object in <paramref name="bytes"/> points to, one level down, for the page-index search: each
    /// field that looks like a heap pointer, read through the guarded reader, as <c> name+0x…→at=… +0x8=…</c>.
    /// </summary>
    public static string Pointees(string name, ReadOnlySpan<byte> bytes, int start, CallerAddress module)
    {
        var text = new System.Text.StringBuilder();
        var pointee = new byte[PointeeLength];
        foreach (var (offset, address) in ObjectFields.HeapPointers(bytes, start, module.ModuleBase, module.ModuleSize, MaxPointees))
        {
            if (ModuleMemoryReader.ReadBytesAvoidingGuardPages((nint)address, pointee) &&
                ObjectFields.Read(address, module.ModuleBase, module.ModuleSize, pointee, sizeof(long)) is { } fields)
            {
                text.Append(CultureInfo.InvariantCulture, $" {name}+0x{offset:x}->{fields}");
            }
        }
        return text.ToString();
    }

    // A string's heap buffer, by address and length (MsvcString caps the length), through the guarded reader.
    private static string? GuardedText(long address, int length)
    {
        var bytes = new byte[length];
        return ModuleMemoryReader.ReadBytesAvoidingGuardPages((nint)address, bytes)
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : null;
    }

    // The object's allocation size in both opens (the finding's step 4).
    private const int ModelLength = 0x98;
}

/// <summary>The tutorial pop-up window's open (<c>UUIC_Common_TutorialWindow</c>):
/// <c>void (window*, uint32* tutorial, bool force, uint8, uint8)</c>, the last on the stack. Logged before it runs:
/// the window, the tutorial number and the three flags.</summary>
[GameHook(GameBuilds.Steam18330018, DevOnly = true)]
internal sealed class TutorialWindowShowListener : IGameHook, IObserves5
{
    private readonly MessageLog? log;

    public TutorialWindowShowListener(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        log = services.Log;
    }

    public string SignatureName => "TutorialWindowShow";

    public void Observe(nint window, nint tutorial, nint force, nint fourth, nint fifth) =>
        log?.TutorialSeen("open", string.Create(CultureInfo.InvariantCulture,
            $"via={SignatureName} window=0x{window:x} id={TutorialListening.Number(tutorial)} force={TutorialListening.Byte(force)} b4={TutorialListening.Byte(fourth)} b5={TutorialListening.Byte(fifth)}"));
}

/// <summary>The controller's other open, on a second tutorial window, the one that shows a party member joining:
/// <c>void (window*, uint32* tutorial, bool force)</c>. After it runs it hands the window's tutorial number and object
/// to <see cref="TutorialPopups"/>, which speaks the pop-up; nothing here hooks this window's update, so its page turns
/// aren't followed. In a development run it also logs the number, the flag and the object.</summary>
/// <remarks>The open returns early, writing nothing, for a tutorial already seen unless forced; a real open writes the
/// new object to <c>+0x30</c>, 0 to the state at <c>+0x18</c> and the number to <c>+0x1c</c> (our reading of
/// <c>0xfae440</c> in Ghidra 12.2 on build 548ddc95). So an early return leaves the last object and number, which
/// <see cref="TutorialPopups"/> doesn't speak again. The object is the same class as the other window's, page at
/// <c>+0x8</c> and count at <c>+0xc</c> (docs/raw-sources/tutorial-page-index-by-ear-2026-11-21.md).</remarks>
[GameHook(GameBuilds.Steam18330018)]
internal sealed class TutorialWindowShowOtherHook : IGameHook, IObserves4WithResult
{
    private const int NumberAt = 0x1c;
    private const int ModelAt = 0x30;

    private readonly TutorialPopups popups;
    private readonly MessageLog? log;
    private readonly CallerAddress module = new();

    public TutorialWindowShowOtherHook(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        popups = services.TutorialPopups;
        log = services.Log;
    }

    public string SignatureName => "TutorialWindowShowOther";

    public void Observe(nint window, nint tutorial, nint force, nint unused4, nint result)
    {
        // The window is the game's own argument, as the open itself writes it.
        var model = ModuleMemoryReader.ReadPointer(window + ModelAt) ?? 0;
        if (ModuleMemoryReader.ReadInt32(window + NumberAt) is { } number)
        {
            popups.WindowUpdated(window, number, model, TutorialListening.Page(model), ended: false);
        }
        log?.TutorialSeen("open", string.Create(CultureInfo.InvariantCulture,
            $"via={SignatureName} window=0x{window:x} id={TutorialListening.Number(tutorial)} force={TutorialListening.Byte(force)}{TutorialListening.Model(model, TutorialListening.ModelBytes(model), module)}"));
    }
}

/// <summary>The tutorial pop-up window's per-frame update (<c>UUIC_Common_TutorialWindow</c>'s vtable slot 13):
/// <c>void (window*)</c>. After each run it hands the window's tutorial number, object, page and state to
/// <see cref="TutorialPopups"/>, which speaks a pop-up as it opens and each page it turns to. In a development run with
/// <see cref="OptInFile"/> in the mod's folder it also logs the window, its object and what each points to whenever
/// they change, so a page turn shows as the field that moves with it; it stops after <see cref="MaxRecords"/>
/// records.</summary>
[GameHook(GameBuilds.Steam18330018)]
internal sealed class TutorialWindowUpdateHook : IGameHook, IObserves4WithResult
{
    /// <summary>The most records one run writes.</summary>
    public const int MaxRecords = 2000;

    /// <summary>The file that turns the per-frame log on in a development run (#594's opt-in, now for the log alone).
    /// </summary>
    public const string OptInFile = "opt-in-tutorial-window.txt";

    // The window's fields the open writes and the update reads (the finding's items 1, 2 and 4): the state, 3 when
    // ended; the tutorial number; the object built for the tutorial.
    private const int StateAt = 0x18;
    private const int NumberAt = 0x1c;
    private const int ModelAt = TutorialWindowState.ModelAt;
    private const int Ended = 3;

    private readonly TutorialPopups popups;
    private readonly MessageLog? log;
    private readonly CallerAddress module = new();
    private readonly ChangeGate gate = new();
    private int records;

    public TutorialWindowUpdateHook(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        popups = services.TutorialPopups;
        log = services.Log is { } open && File.Exists(Path.Combine(services.ModFolder, OptInFile)) ? open : null;
    }

    public string SignatureName => "TutorialWindowUpdate";

    public void Observe(nint window, nint unused2, nint unused3, nint unused4, nint result)
    {
        // The window is the game's own argument, as the update itself reads it.
        if (ModuleMemoryReader.ReadInt32(window + StateAt) is not { } state ||
            ModuleMemoryReader.ReadInt32(window + NumberAt) is not { } number ||
            ModuleMemoryReader.ReadPointer(window + ModelAt) is not { } model)
        {
            return;
        }
        popups.WindowUpdated(window, number, model, TutorialListening.Page(model), ended: state == Ended);
        if (log is not null && records < MaxRecords)
        {
            LogChange(log, window);
        }
    }

    private void LogChange(MessageLog to, nint window)
    {
        if (TutorialListening.Window(window, module) is not { } read)
        {
            return;
        }
        var modelBytes = TutorialListening.ModelBytes(read.State.Model);
        var model = TutorialListening.Model(read.State.Model, modelBytes, module);
        // Only the window and its object decide a change: what they point to may hold timers.
        if (!gate.Changed(window, [.. TutorialWindowState.StateKey(read.Bytes), .. modelBytes ?? []]) ||
            Interlocked.Increment(ref records) > MaxRecords)
        {
            return;
        }
        var pointees = TutorialListening.Pointees("window", read.Bytes, TutorialWindowState.FieldsStart, module) +
            (modelBytes is null ? "" : TutorialListening.Pointees("model", modelBytes, sizeof(long), module));
        to.TutorialSeen("window", $"{read.State}{model}{pointees}");
    }
}
