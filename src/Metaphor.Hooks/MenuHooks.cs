using System.Diagnostics;
using Metaphor.Core;
using Metaphor.Core.Menus;

namespace Metaphor.Hooks;

// Menu cursor events (#94): one hook decodes each input a menu's cursor manager handles and hands it to
// HookServices.Cursors, whose observers are MenuScreens (#211), which turns them into the screen changes the menu
// features speak, and the dialogue's ChoiceFocus, plus the report and the log when they are on. The loader's adapter
// passes every call through unchanged (#88).

/// <summary>Reads whether the game has its menus open (#268): the global <c>CampStatus_IsClosed</c> tests is non-null
/// exactly while a field menu, or the title screen, is open (docs/raw-sources/menus-open-session6-2026-11-17.md, game
/// build 548ddc95…, Steam build 18330018). One pointer read through <see cref="ModuleMemoryReader"/> per call, no list
/// walk. The global's address comes from the code that loads it: the <c>mov rcx,[rip+disp32]</c> (7 bytes) at
/// <c>+9</c> in <c>CampStatusIsOpenBody</c>, in <c>.ecode</c>, which the loader locates on its own (#300, after the
/// build check) and only reads, never hooks or calls. Until then, when it wasn't located, and when a read fails, the
/// menus read as closed, so the menu controls hand their keys to the game.</summary>
internal sealed class MenusOpenRead : IReadsGlobal
{
    private static readonly GlobalSpec Spec = new(
        "menus-open flag", [GameBuilds.Steam18330018], "CampStatusIsOpenBody",
        LoadOffset: 9, DisplacementOffset: 3, InstructionLength: 7, TurnsOff: "menu controls");

    // Set once by the loader before any hook goes live. A pointer-sized field is read and written whole, so another
    // thread sees 0, as closed, or the address.
    private nint global;

    public GlobalSpec Global => Spec;

    public void Locate(nint address) => global = address;

    /// <summary>Whether the menus are open now: false when the global is unknown or unreadable.</summary>
    public bool IsOpen() =>
        global is var at && at != 0 && ModuleMemoryReader.ReadPointer(at) is { } handle && handle != 0;
}

/// <summary>Reports each input the game's shared menu cursor handler takes: <c>int (CursorManagerBase* this, const
/// int* command)</c>, shared by four cursor manager classes, which moves the cursor for commands <c>0x14</c>–<c>0x17</c>
/// and puts it back if the move is refused. The function, its arguments and the field offsets are our reading of the
/// exe (docs/raw-sources/ui-focus-candidates-2026-11-16.md); that the index follows the menu's focus is from
/// docs/raw-sources/spike-session-2-2026-11-16.md. Observed after it returns, so the cursor is where the input left it.
/// Each input carries its screen class from <see cref="ScreenClassLookup"/> (#208), its list's item count (#289) and its
/// owner's mode for a choice that serves two lists (#210), each null when unreadable.</summary>
[GameHook(GameBuilds.Steam18330018)]
internal sealed class MenuCursorHook : IGameHook, IObserves4WithResult
{
    // The cursor's top (scroll) and row, whose sum the class's index getter returns, and the flags the handler tests.
    private const int TopOffset = 0x60;
    private const int RowOffset = 0x64;
    private const int FlagsOffset = 0xAC;

    // The list's item count (#289), matched to the rows on screen on six screens; read per input, since a manager can be
    // reused by a later screen (docs/raw-sources/list-count-session6-2026-11-17.md, game build 548ddc95…).
    private const int CountOffset = 0x58;

    private readonly CursorEvents events;
    private readonly ScreenClassLookup screenClasses;
    private readonly nint? moduleBase;
    private readonly MessageLog? log;
    private readonly ChangedBytes settingsCopies = new();

    public MenuCursorHook(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        events = services.Cursors;
        screenClasses = services.ScreenClasses;
        log = services.Log;
        using var process = Process.GetCurrentProcess();
        // Without the main module the class is logged as unreadable; the loader isn't stopped.
        moduleBase = process.MainModule?.BaseAddress;
    }

    public string SignatureName => "CursorManagerInput";

    /// <summary>Only the low 32 bits of the result are read. An input whose command or cursor can't be read is
    /// skipped.</summary>
    public void Observe(nint manager, nint command, nint third, nint fourth, nint result)
    {
        if (ModuleMemoryReader.ReadInt32(command) is not { } commandValue ||
            ModuleMemoryReader.ReadInt32(manager + TopOffset) is not { } top ||
            ModuleMemoryReader.ReadInt32(manager + RowOffset) is not { } row)
        {
            return;
        }
        long? classRva = moduleBase is { } module && ModuleMemoryReader.ReadPointer(manager) is { } vtable
            ? vtable - module
            : null;
        var screenClass = screenClasses.Of(manager);
        events.Handled(new CursorInput(
            manager, classRva, commandValue, (int)result, top, row, ModuleMemoryReader.ReadInt32(manager + FlagsOffset) ?? 0,
            screenClass, ModuleMemoryReader.ReadInt32(manager + CountOffset), screenClasses.ModeOf(manager, screenClass)));
        LogSettingsCopy(manager, screenClass);
    }

    // Debug builds only (the log is null in Release): a settings page's working copy, when it changed since the page's
    // last input, so a session shows which bytes a value change moves (#560, #558's check 2). One bounded read through
    // ModuleMemoryReader, which fails instead of faulting; nothing when the owner is unknown or the read fails.
    private void LogSettingsCopy(nint manager, string? screenClass)
    {
        if (log is null || !SettingsPages.IsPage(screenClass) || screenClasses.Owners.OwnerOf(manager) is not { } owner)
        {
            return;
        }
        // An array, not stackalloc: a stack buffer's loads are raw-memory reads outside the reader (architecture rule 10).
        var bytes = new byte[SettingsPages.WorkingCopyLength];
        if (ModuleMemoryReader.ReadBytes((nint)(owner + SettingsPages.WorkingCopyOffset), bytes)
            && settingsCopies.Changed(owner, bytes) is { } hex)
        {
            log.SettingsCopy(owner, hex);
        }
    }
}
