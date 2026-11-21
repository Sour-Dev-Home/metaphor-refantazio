using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Metaphor.Core;
using Metaphor.Core.Input;
using Reloaded.Hooks.Definitions;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace Metaphor.Hooks;

// The dialogue (#58) and menu (#214) controls' input: hooks on the game's own per-frame reads of a pad and of the
// keyboard. They change what the game sees, so they are written the long way (architecture rule 14's exception,
// IChangesBehaviour). Each is thin: it calls the original, reads the state the original just filled, asks the input
// frame (Metaphor.Core.Input.InputFrame, #301) what to hand the game, writes that back if it differs, and logs. The
// frame holds every rule: menus before dialogue, a read not made fires nothing (#225), only what a binding holds is
// hidden. It asks GameInputReads for the dialogue window (DialogueWindowPointer, #232) and whether the menus are open
// (MenusOpenRead, #268), whose globals the loader locates on its own (#300); a reader whose signature didn't match reads
// as unknown without keeping either hook out. Every read goes through ModuleMemoryReader (rule 10); the one write is
// WriteProcessMemory on this process into the state the original has just filled, and the frame's answer only clears
// bits or drops keys from it. WriteProcessMemory fails instead of faulting, but it also writes through read-only pages,
// so it is safe only on memory known to be the game's own writable data, never on an arbitrary pointer. In Debug builds
// each change of a read is logged, as #58's input spike did (numbers only, no game text). The functions, their arguments
// and the names (ours) are in docs/raw-sources/input-route-2026-11-16.md; the layout of what they write is in
// docs/raw-sources/pad-and-keyboard-state-2026-11-16.md.

/// <summary>The game state the input frame asks about, read from the globals the loader located (#300): unknown when a
/// global wasn't located or a read failed.</summary>
internal sealed class GameInputReads(DialogueWindowPointer window, MenusOpenRead menus) : IGameInputState
{
    public long? DialogueWindow() => window.Read();

    public bool MenusOpen() => menus.IsOpen();

    /// <summary>The input frame over <paramref name="services"/>' readers, menu screens and controls; the menu controls
    /// take each read first, then the battle status key (#631), then the dialogue controls, the field's (#181) last
    /// when there are, and its key watcher.</summary>
    public static InputFrame Frame(HookServices services)
    {
        var game = new GameInputReads(services.DialogueWindow, services.MenusOpen);
        IInputControls menus =
            services.BattleStatus is { } status ? new ChainedControls(services.MenuControls, status) : services.MenuControls;
        return new(game, services.MenuScreens, menus, services.DialogueControls,
            services.FieldControls ?? InputFrame.NoControls, services.KeyWatcher);
    }
}

/// <summary>The pad read: <c>(32-bit pad number, pointer to the game's pad state) → al</c>; it writes the game's own
/// 16-bit button mask at <c>+0</c>.</summary>
[GameHook(GameBuilds.Steam18330018)]
[SuppressMessage("Design", "CA1031", Justification = "An exception must never cross back into the game.")]
internal sealed class PadReadHook : IGameHook, IChangesBehaviour
{
    // Both arguments pointer-sized (rule 9): the pad number's upper half passes through as the game left it.
    private delegate nint Function(nint pad, nint state);

    private static readonly nint CurrentProcess = -1;

    private readonly InputFrame frame;
    private readonly MessageLog? log;
    private readonly InputStateChanges changes = new();
    private readonly Function callback;
    private IHook<Function>? hook;

    public PadReadHook(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        frame = GameInputReads.Frame(services);
        log = services.Log;
        callback = OnCall;
    }

    public string SignatureName => "PadRead";

    public void Activate(IReloadedHooks hooks, nint address)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        // Stored before it goes live (rule 11).
        hook = hooks.CreateHook(callback, address);
        hook.Activate();
    }

    // https://learn.microsoft.com/windows/win32/api/memoryapi/nf-memoryapi-writeprocessmemory
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);

    private nint OnCall(nint pad, nint state)
    {
        var result = hook!.OriginalFunction(pad, state);
        try
        {
            if (ModuleMemoryReader.ReadInt32(state) is { } value)
            {
                var buttons = value & 0xFFFF;
                // A zero al is taken as "no read made" (#225): Steam Input's virtual pad 1 returns it while holding
                // leftover bits (tests/fixtures/sessions/steam-input-pad1-noise.txt). What the return means is
                // [NEEDS VERIFICATION], pending capture evening #229 (likely "pad not connected").
                var answer = frame.Pad((int)pad, buttons, made: (result & 0xFF) != 0);
                if (answer.Buttons != buttons)
                {
                    var mask = new byte[2];
                    BinaryPrimitives.WriteUInt16LittleEndian(mask, (ushort)answer.Buttons);
                    WriteProcessMemory(CurrentProcess, state, mask, (nuint)mask.Length, out _);
                }
                if (log is not null)
                {
                    Log(answer.Window, (int)pad, buttons, answer.Buttons, result);
                }
            }
        }
        catch (Exception)
        {
        }
        return result;
    }

    private void Log(long? window, int pad, int buttons, int handed, nint result)
    {
        if (changes.Changed("msgwindow", window?.ToString(CultureInfo.InvariantCulture) ?? "?"))
        {
            log!.WindowRead(window);
        }
        var source = string.Create(CultureInfo.InvariantCulture, $"pad{pad}");
        var text = string.Create(CultureInfo.InvariantCulture,
            $"buttons=0x{buttons:x4} handed=0x{handed:x4} returned={result & 0xFF}");
        if (changes.Changed(source, text))
        {
            log!.InputChanged(source, text);
        }
    }
}

/// <summary>The keyboard read: <c>(32-bit value, pointer to the game's 15-byte keyboard state) → al</c>; it writes a
/// count of held keys at <c>+4</c> and up to six key codes from <c>+8</c>.</summary>
[GameHook(GameBuilds.Steam18330018)]
[SuppressMessage("Design", "CA1031", Justification = "An exception must never cross back into the game.")]
internal sealed class KeyboardReadHook : IGameHook, IChangesBehaviour
{
    private delegate nint Function(nint first, nint state);

    private const int CountOffset = 4;
    private const int SlotsOffset = 8;

    private static readonly nint CurrentProcess = -1;

    private readonly InputFrame frame;
    private readonly MessageLog? log;
    private readonly Func<bool> nameEntryUp;
    private readonly InputStateChanges changes = new();
    private readonly Function callback;
    private IHook<Function>? hook;

    public KeyboardReadHook(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        frame = GameInputReads.Frame(services);
        log = services.Log;
        nameEntryUp = services.NameEntryUp;
        callback = OnCall;
    }

    public string SignatureName => "KeyboardRead";

    public void Activate(IReloadedHooks hooks, nint address)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        // Stored before it goes live (rule 11).
        hook = hooks.CreateHook(callback, address);
        hook.Activate();
    }

    // https://learn.microsoft.com/windows/win32/api/memoryapi/nf-memoryapi-writeprocessmemory
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(nint process, nint address, byte[] buffer, nuint size, out nuint written);

    private nint OnCall(nint first, nint state)
    {
        var result = hook!.OriginalFunction(first, state);
        try
        {
            var slots = new byte[GameKeyList.Slots];
            if (ModuleMemoryReader.ReadInt32(state + CountOffset) is { } count &&
                ModuleMemoryReader.ReadBytes(state + SlotsOffset, slots))
            {
                var keys = GameKeyList.Read(count, slots);
                // The window is read here too: the keyboard may be read on frames, or a thread, the pad isn't.
                var handed = frame.Keys(keys).Keys;
                if (handed.Count != keys.Count)
                {
                    // The count and the slots after it, in one write.
                    var written = new byte[SlotsOffset - CountOffset + GameKeyList.Slots];
                    BinaryPrimitives.WriteInt32LittleEndian(written, handed.Count);
                    GameKeyList.Write(handed).CopyTo(written, SlotsOffset - CountOffset);
                    WriteProcessMemory(CurrentProcess, state + CountOffset, written, (nuint)written.Length, out _);
                }
                if (log is not null)
                {
                    // On a name entry screen the held keys may spell the player's name (#585): counts only then.
                    var hide = nameEntryUp();
                    var text = string.Create(CultureInfo.InvariantCulture,
                        $"keys=0x{ModuleMemoryReader.ReadInt32(state) ?? 0:x8} held={Codes(keys, hide)} handed={Codes(handed, hide)} returned={result & 0xFF}");
                    if (changes.Changed("keyboard", text))
                    {
                        log.InputChanged("keyboard", text);
                    }
                }
            }
        }
        catch (Exception)
        {
        }
        return result;
    }

    private static string Codes(IReadOnlyList<int> keys, bool countOnly) =>
        keys.Count == 0 ? "-"
        : countOnly ? string.Create(CultureInfo.InvariantCulture, $"{keys.Count}keys")
        : string.Join(',', keys.Select(key => key.ToString("x2", CultureInfo.InvariantCulture)));
}
