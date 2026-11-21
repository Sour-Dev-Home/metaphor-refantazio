using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Metaphor.Core;
using Metaphor.Core.Messages;

namespace Metaphor.Hooks;

// Issues #74 and #106: development-only hooks on the game's message lookups, to see how a speaker's displayed name is
// picked and whether the player's chosen name passes through; the serial lookup's is a release hook since #247. Each sees its function's result, after the original ran
// (IObserves4WithResult). The functions, their arguments and the names (ours, except CreateMsgFromString, which is
// MF.Toolkit's) are in docs/raw-sources/name-lookup-candidates-2026-11-16.md. Integers are pointer-sized (rule 9);
// only the low 32 bits of each result are meaningful where the function returns an int.

/// <summary>What the name listeners share: reading what a lookup was passed, safely, and where a pointer points.</summary>
internal sealed class NameListening
{
    /// <summary>How long a listener stays quiet about a lookup it has just logged. Some run every frame.</summary>
    private const long QuietMilliseconds = 2000;

    private readonly nint moduleStart;
    private readonly nint moduleEnd;

    private NameListening(nint moduleStart, nint moduleEnd)
    {
        this.moduleStart = moduleStart;
        this.moduleEnd = moduleEnd;
    }

    /// <summary>The game's main module, as the listeners see it; an empty range, so every pointer reads as heap, when it
    /// can't be found. Never throws, so a hook created with it never stops the loader.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A spike listener must never stop the other hooks.")]
    public static NameListening ForMainModule()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            if (process.MainModule is { } module)
            {
                return new NameListening(module.BaseAddress, module.BaseAddress + module.ModuleMemorySize);
            }
        }
        catch (Exception)
        {
        }
        return new NameListening(0, 0);
    }

    /// <summary>A fresh gate for one listener's repeats.</summary>
    public static RepeatGate NewGate() => new(QuietMilliseconds, () => Environment.TickCount64);

    /// <summary>Where <paramref name="pointer"/> points: an offset in the game module, or elsewhere.</summary>
    public string Location(nint pointer) =>
        pointer >= moduleStart && pointer < moduleEnd
            ? string.Create(CultureInfo.InvariantCulture, $"module+0x{pointer - moduleStart:x}")
            : pointer == 0 ? "null" : "heap";

    /// <summary>The 32-bit value at <paramref name="pointer"/>, or null.</summary>
    public static int? Int32At(nint pointer) => pointer == 0 ? null : ModuleMemoryReader.ReadInt32(pointer);

    /// <summary>The 16 bytes at <paramref name="pointer"/> as hex, or null. <c>ReadAt</c> with a base of the pointer and
    /// an offset of 0 copies from the pointer itself.</summary>
    public static string? Hash(nint pointer)
    {
        var bytes = new byte[16];
        return pointer != 0 && ModuleMemoryReader.ReadAt(pointer, 0, bytes)
            ? Convert.ToHexStringLower(bytes)
            : null;
    }

    public static string Number(string name, int? value) =>
        value is { } v ? string.Create(CultureInfo.InvariantCulture, $"{name}={v}") : $"{name}=(unreadable)";
}

/// <summary>A message label resolved to its serial number: the label's text and the number (-1 when not found). Labels
/// are identifiers like <c>CharaName_FO12_Mor_00</c>, not dialogue.</summary>
[GameHook(GameBuilds.Steam18330018, DevOnly = true)]
internal sealed class MessageSerialByLabelListener : IGameHook, IObserves4WithResult
{
    private readonly MessageLog? log;
    private readonly PlayerNameCheck playerName;
    private readonly RepeatGate repeats = NameListening.NewGate();

    public MessageSerialByLabelListener(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        log = services.Log;
        playerName = PlayerNameCheck.FromModFolder(services.ModFolder);
    }

    public string SignatureName => "MessageSerialByLabel";

    /// <summary>Its one argument is a C string (the function measures it and hashes it with MD5).</summary>
    public void Observe(nint label, nint unused2, nint unused3, nint unused4, nint result)
    {
        if (log is null || !repeats.LetThrough(label, (int)result))
        {
            return;
        }
        log.Resolved(SignatureName, $"{playerName.LabelField(ModuleMemoryReader.ReadAnsiString(label))} {NameListening.Number("serial", (int)result)}");
    }
}

/// <summary>A message resolved by the 16-byte hash of its label: the hash and the serial number. The speaker-name path
/// reaches it with a hash picked from a table by a character's number and the game's flags.</summary>
[GameHook(GameBuilds.Steam18330018, DevOnly = true)]
internal sealed class MessageSerialByHashListener : IGameHook, IObserves4WithResult
{
    private readonly MessageLog? log;
    private readonly RepeatGate repeats = NameListening.NewGate();

    public MessageSerialByHashListener(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        log = services.Log;
    }

    public string SignatureName => "MessageSerialByHash";

    public void Observe(nint hash, nint unused2, nint unused3, nint unused4, nint result)
    {
        if (log is null || !repeats.LetThrough(hash, (int)result))
        {
            return;
        }
        log.Resolved(SignatureName,
            $"hash={NameListening.Hash(hash) ?? "(unreadable)"} {NameListening.Number("serial", (int)result)}");
    }
}

/// <summary>A message fetched by serial number (#247): each fetch whose serial and flags can be read goes to
/// <see cref="HookServices.Messages"/>, for the battle lists, which say a row by the name fetched for it. In a Debug
/// build it also logs the number, the flags, whether a message came back, and the label and text the mod's serial table
/// gives that number (#584). Called from about 250 places, some every frame, so in Release it reads only while an
/// observer is listening, two numbers and nothing else, and the log drops repeats within the quiet window. Release since #247; until then a Debug-only listener.</summary>
[GameHook(GameBuilds.Steam18330018)]
internal sealed class GetMessageBySerialHook : IGameHook, IObserves4WithResult
{
    private readonly MessageEvents events;
    private readonly MessageLog? log;
    private readonly MessageSerials serials;
    private readonly RepeatGate repeats = NameListening.NewGate();

    public GetMessageBySerialHook(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        events = services.Messages;
        log = services.Log;
        serials = services.Serials;
    }

    public string SignatureName => "GetMessageBySerial";

    /// <summary>A pointer to the serial (the function reads 32 bits through it), then two pointers MF.Toolkit types as
    /// flags and configs.</summary>
    public void Observe(nint serial, nint flags, nint configs, nint unused4, nint result)
    {
        // Each read is a ReadProcessMemory call, so nothing is read unless an observer wants fetches now (the battle
        // lists stop listening while another screen is current) or the Debug log is on.
        if (log is null && !events.SerialsWanted)
        {
            return;
        }
        var number = NameListening.Int32At(serial);
        var flagBits = NameListening.Int32At(flags);
        if (number is { } read && flagBits is { } style)
        {
            events.Resolved(new SerialResolved(read, style));
        }
        if (log is null || !repeats.LetThrough(number ?? long.MinValue, flagBits ?? long.MinValue))
        {
            return;
        }
        log.Resolved(SignatureName,
            $"{NameListening.Number("serial", number)} {NameListening.Number("flags", flagBits)} message={(result == 0 ? "null" : "set")}" +
            (number is { } known ? $" {serials.Describe(known)}" : ""));
    }
}

/// <summary>A string the game turns into a message at run time (#106): only its length, whether it is the name the
/// maintainer typed (<see cref="PlayerNameCheck.FileName"/>), and where it was. Never its text: it may be the player's
/// chosen name. The first hook in the exe's <c>.text1</c> section, so it is off unless a run opts in.</summary>
[GameHook(GameBuilds.Steam18330018, DevOnly = true, OptInFile = "opt-in-create-msg-from-string.txt",
    OptInName = "message strings")]
internal sealed class CreateMsgFromStringListener : IGameHook, IObserves4WithResult
{
    private readonly MessageLog? log;
    private readonly NameListening listening = NameListening.ForMainModule();
    private readonly PlayerNameCheck playerName;
    private readonly RepeatGate repeats = NameListening.NewGate();

    public CreateMsgFromStringListener(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        log = services.Log;
        playerName = PlayerNameCheck.FromModFolder(services.ModFolder);
    }

    public string SignatureName => "CreateMsgFromString";

    /// <summary>MF.Toolkit's <c>CreateMsgFromString(nint str, MsgFlag flags, MsgConfig1*, MsgConfig2*)</c>; that the
    /// string is a C string is unverified.</summary>
    public void Observe(nint text, nint flags, nint config1, nint config2, nint result)
    {
        if (log is null || ModuleMemoryReader.ReadAnsiString(text) is not { } read)
        {
            return;
        }
        var sighting = playerName.Sight(read, listening.Location(text));
        // The match is in the key so a reused buffer that now holds the name is still logged.
        if (repeats.LetThrough(text, ((long)sighting.Match << 32) | (uint)sighting.Bytes))
        {
            log.PlayerNameSeen(SignatureName, sighting);
        }
    }
}
