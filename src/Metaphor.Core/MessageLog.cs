using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Metaphor.Core.Menus;
using Metaphor.Core.Messages;

namespace Metaphor.Core;

/// <summary>The #55 spike's log of what the game's message functions saw: each message file compiled, with its source
/// text, and each message looked up, with when. Read after a play session to find which call carries the line on
/// screen. Debug builds only; it holds game text, so it stays in the local mod folder and is never committed. Safe to
/// call from any game thread, and never throws.</summary>
public sealed class MessageLog(TextWriter writer, Func<long> millisecondsSinceStart)
    : IMessageFileObserver, ILineRequestObserver, IChoiceRequestObserver, ISelectRequestObserver, ICursorObserver
{
    /// <summary>The log's file name in the mod's folder.</summary>
    public const string FileName = "message-log.txt";

    /// <summary>The function a <see cref="LineRequested"/> came from, as its record names it (#86).</summary>
    private const string LineRequestFunction = "MsgRequestByName";

    private readonly object gate = new();

    /// <summary>A message file the game compiled: the number it gave it, its path, and its source text.</summary>
    public void Observe(MessageFileCompiled file) => Write(() =>
    {
        var source = file.Source ?? "";
        var record = new StringBuilder(Line($"{millisecondsSinceStart()} ms compile id={file.Id} path={file.Path} chars={source.Length}"));
        foreach (var line in source.ReplaceLineEndings("\n").Split('\n'))
        {
            record.Append("  | ").Append(line).AppendLine();
        }
        return record.ToString();
    });

    /// <summary>A message the game looked up, by message number and label index.</summary>
    public void LookedUp(long messageId, int labelIndex) =>
        Write(() => Line($"{millisecondsSinceStart()} ms lookup id={messageId} label={labelIndex}"));

    /// <summary>A function the game registered for its scripts: its script name, and where the native function is in
    /// the game module. <paramref name="viaUserdata"/> says the address came from the userdata the game pushed (a game
    /// function behind a shared trampoline) and not from the registered function itself.</summary>
    public void Registered(string name, long rva, bool viaUserdata) =>
        Write(() => Line($"{millisecondsSinceStart()} ms register name={name} rva=0x{rva:x} {(viaUserdata ? "userdata" : "direct")}"));

    /// <summary>A message the game's scripts asked for: which request function ran, and what it was passed, as the
    /// listening hook formatted it.</summary>
    public void Requested(string function, string arguments) =>
        Write(() => Line($"{millisecondsSinceStart()} ms request via={function} {arguments}"));

    /// <summary>A choice the game's scripts opened (#82): the select request's kind, the prompt's label as
    /// <c>first</c> and the choice's label as <c>second</c> (docs/raw-sources/spike-session-1-2026-11-16.md).</summary>
    public void Observe(ChoiceRequested choice) => Write(() => Line(
        $"{millisecondsSinceStart()} ms select kind={choice.Kind} first={Quoted(choice.PromptLabel)} second={Quoted(choice.ChoiceLabel)}"));

    /// <summary>A select window any game code opened (#569): the request's kind, the prompt's label and the choice's
    /// label.</summary>
    public void Observe(SelectRequested window) => Write(() => Line(
        $"{millisecondsSinceStart()} ms select-window kind={window.Kind} prompt={Quoted(window.PromptLabel)} choice={Quoted(window.ChoiceLabel)}"));

    /// <summary>The selection the game's scripts read back (#93): the value the selected-ID function returned, as 32
    /// bits.</summary>
    public void SelectedId(int id) => Write(() => Line($"{millisecondsSinceStart()} ms selected id={id}"));

    private static string Quoted(string? text) => text is null ? "(unreadable)" : $"\"{LogText.Escaped(text)}\"";

    /// <summary>A sound cue the game set on a CRI ADX2 player (#93, #103): the cue's number, the cue sheet and player
    /// handles, and the calling code (<see cref="CallerTrace"/>). Numbers only, no game text.</summary>
    public void CueSet(int cueId, long acb, long player, CallerTrace caller) => Write(() =>
        Line($"{millisecondsSinceStart()} ms cue id={cueId} acb=0x{acb:x} player=0x{player:x} {caller}"));

    /// <summary>A UI text pane drawn in a new state (#93, second sitting), with the text a probe found behind it, if any:
    /// where it was (<c>pane+0x…</c> or <c>elem+0x…</c>) and the text. Game text, so this log stays local.</summary>
    public void PaneDrawn(PaneDrawn pane, string? textAt, string? text) => Write(() =>
        Line($"{millisecondsSinceStart()} ms pane {pane} text={(text is null ? "(none)" : $"{textAt} {Quoted(text)}")}"));

    /// <summary>A message the game resolved by key (#74, #106): which lookup ran, its arguments and its result, as the
    /// listening hook formatted them.</summary>
    public void Resolved(string function, string arguments) =>
        Write(() => Line($"{millisecondsSinceStart()} ms resolve via={function} {arguments}"));

    /// <summary>A string that could be the player's chosen name (#106): only its length, how it compares with the
    /// typed name and where it was, never its text.</summary>
    public void PlayerNameSeen(string function, PlayerNameSighting sighting) =>
        Write(() => Line($"{millisecondsSinceStart()} ms player-name via={function} {sighting}"));

    /// <summary>A name entry screen's box, when its length changed (#585): which screen and the length, never the typed
    /// text (<see cref="Menus.NameEntryFrame.ToString"/>).</summary>
    public void NameEntryChanged(Menus.NameEntryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Write(() => Line($"{millisecondsSinceStart()} ms name-entry {frame}"));
    }

    /// <summary>A pad's or the keyboard's state as the game read it, when it changed (#58): the source and the state as
    /// the listening hook formatted it. Bit masks only, no game text.</summary>
    public void InputChanged(string source, string state) =>
        Write(() => Line($"{millisecondsSinceStart()} ms input {source} {state}"));

    /// <summary>A read of the message the dialogue window holds, when it changed (#232): its pointer, or <c>?</c> when it
    /// couldn't be read. A pointer only, no game text; <see cref="Replay.SessionLogConverter"/> keeps it.</summary>
    public void WindowRead(long? currentMessage) => Write(() =>
        Line($"{millisecondsSinceStart()} ms msgwindow current={(currentMessage is { } message ? $"0x{message:x}" : "?")}"));

    /// <summary>A dialogue window hook ran (#206, #232): <c>open</c> for <c>MsgOpen</c>, <c>close</c> for
    /// <c>MsgClose</c>, to set beside the <c>msgwindow</c> records.</summary>
    public void WindowHook(string which) =>
        Write(() => Line($"{millisecondsSinceStart()} ms msgwindow-hook {which}"));

    /// <summary>A tutorial pop-up event (#118): which one (<c>open</c>, or <c>window</c> for a new window state) and its
    /// details as the listening hook formatted them. Numbers, and the window's strings, which are file paths.</summary>
    public void TutorialSeen(string what, string details) =>
        Write(() => Line($"{millisecondsSinceStart()} ms tutorial {what} {details}"));

    /// <summary>A map screen's place text (#545, #586): which part (<c>panel</c>, <c>compose</c> or <c>strip</c>) and
    /// its details as the hook formatted them: numbers, raw reference bytes and the text that was said or read.</summary>
    public void MapSeen(string what, string details) =>
        Write(() => Line($"{millisecondsSinceStart()} ms map {what} {details}"));

    /// <summary>An input a menu's cursor manager handled that changed something (#93, #94). Numbers only, no game
    /// text.</summary>
    public void Observe(CursorInput input) => Write(() => Line(
        $"{millisecondsSinceStart()} ms cursor mgr=0x{input.Manager:x} class={(input.ClassRva is { } rva ? $"0x{rva:x}" : "?")} cmd=0x{input.Command:x} result={input.Result} index={input.Index} top={input.Top} row={input.Row} flags=0x{input.Flags:x} screen={input.ScreenClass ?? "?"} count={input.Count?.ToString(CultureInfo.InvariantCulture) ?? "?"} mode={input.Mode?.ToString(CultureInfo.InvariantCulture) ?? "?"}"));

    /// <summary>A settings page's working copy of its values, when it changed (#560): the page's choice and the bytes
    /// from <see cref="Menus.SettingsPages.WorkingCopyOffset"/> on, as hex. Numbers only, no game text.</summary>
    public void SettingsCopy(long choice, string bytes) => Write(() => Line(
        $"{millisecondsSinceStart()} ms settings-copy choice=0x{choice:x} at=+0x{Menus.SettingsPages.WorkingCopyOffset:x} bytes={bytes}"));

    /// <summary>A line the game asked for by label, with its kind.</summary>
    public void Observe(LineRequested line) => Write(() =>
        Line($"{millisecondsSinceStart()} ms request via={LineRequestFunction} kind={line.Kind} text=\"{LogText.Escaped(line.Label)}\""));

    private static string Line(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture) + Environment.NewLine;

    /// <summary>Builds and writes one record under the lock. Building it is inside the try too, so nothing a caller
    /// passes can make this throw.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A spike log must never reach the game.")]
    private void Write(Func<string> record)
    {
        lock (gate)
        {
            try
            {
                writer.Write(record());
                writer.Flush();
            }
            catch (Exception)
            {
                // A lost line in a spike log is better than a crashed game.
            }
        }
    }
}
