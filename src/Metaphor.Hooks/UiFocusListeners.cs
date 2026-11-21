using System.Globalization;
using Metaphor.Core;
using Metaphor.Features.Field;

namespace Metaphor.Hooks;

// Issue #93, second sitting: development-only hooks to find where menu focus lives. The functions, their arguments and
// the field offsets are our reading of the exe on one build (docs/raw-sources/ui-focus-candidates-2026-11-16.md); the
// names are the game's own class names from its run-time type information, with the function's role ours. Every
// argument is observed pointer-sized (rule 9) and narrowed only for the log.

/// <summary>The draw of the game's UI text pane (<c>app::ui::TextPane</c>, its 22nd virtual function):
/// <c>void (TextPane* this)</c>. It reads the pane's colour, position and text element and hands them to the text
/// element's draw, which another pane class (the title's licence text) shares. It runs for every visible text pane
/// every frame, so a pane is logged only when its draw state changes, with any text a probe finds behind it, to tell a
/// highlighted item by its colour (the lead from Persona-4-Golden-Access's <c>UiTextSpy</c>,
/// docs/raw-sources/persona-4-golden-access-2026-11-16.md). Opt-in: a hook on a per-frame draw is the riskiest of the
/// sitting's, and its log holds game text, which stays in the local log.</summary>
[GameHook(GameBuilds.Steam18330018, DevOnly = true, OptInFile = "opt-in-ui-text-panes.txt", OptInName = "text panes")]
internal sealed class TextPaneDrawListener : IGameHook, IObserves3
{
    // How much of the pane and of its text element the text probe looks through.
    private const int ProbeBytes = 0x100;

    private readonly MessageLog? log;
    private readonly MissionLine? mission;
    private readonly CallerAddress module = new();
    private readonly ChangeGate gate = new();

    public TextPaneDrawListener(HookServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        log = services.Log;
        mission = services.Mission;
    }

    public string SignatureName => "TextPaneDraw";

    public void Observe(nint pane, nint second, nint third)
    {
        if (log is null)
        {
            return;
        }
        var bytes = new byte[PaneDrawn.Length];
        if (!ModuleMemoryReader.ReadBytes(pane, bytes) || !gate.Changed(pane, PaneDrawn.StateKey(bytes)) ||
            PaneDrawn.Read(pane, module.ModuleBase, module.ModuleSize, bytes) is not { } drawn)
        {
            return;
        }
        var found = Probe(pane, "pane");
        if (found.Text is null && drawn.Element != 0)
        {
            found = Probe((nint)drawn.Element, "elem");
        }
        log.PaneDrawn(drawn, found.At, found.Text);
        // #658's probe: whether the HUD's mission line is a text pane. A mission's text, once per change, is spoken
        // and logged by its label.
        mission?.Seen(found.Text);
    }

    private static (string? At, string? Text) Probe(nint target, string name)
    {
        var fields = new byte[ProbeBytes];
        if (!ModuleMemoryReader.ReadBytes(target, fields) ||
            TextProbe.FirstString(fields, address => ModuleMemoryReader.ReadAnsiString((nint)address)) is not { } found)
        {
            return (null, null);
        }
        return (string.Create(CultureInfo.InvariantCulture, $"{name}+0x{found.Offset:x}"), found.Text);
    }
}
