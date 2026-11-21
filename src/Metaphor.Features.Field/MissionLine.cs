using Metaphor.Core.Announcements;
using Metaphor.Core.Messages;

namespace Metaphor.Features.Field;

/// <summary>The current mission line at the top right of the field (#658): a text the game shows is the mission when it
/// is one of the <c>MissionText_</c> messages of the mission files, which the game compiles whole at start-up on game
/// build 548ddc95… (Steam build 18330018) (docs/raw-sources/mission-line-message-log-2026-11-21.md). A new one is
/// spoken once, and the last one is kept for "where am I". [NEEDS VERIFICATION]: which call carries the line's text as
/// the HUD shows it; none the mod hooks fetched a mission message in that session, so only the Debug build's text-pane
/// probe feeds this for now. Safe to call from several threads.</summary>
public sealed class MissionLine : IMessageFileObserver
{
    private const string FilePrefix = "message/system/Mission_Message_";
    private const string LabelPrefix = "MissionText_";

    private readonly IAnnouncer announcer;
    private readonly TextCleaner cleaner;
    private readonly Action<string>? log;
    private readonly Lock gate = new();
    private readonly Dictionary<string, Dictionary<string, string>> files = new(StringComparer.OrdinalIgnoreCase);
    private string? current;

    /// <summary>A mission line that speaks through <paramref name="announcer"/>, says texts as
    /// <paramref name="cleaner"/> cleans them, and writes each change's label to <paramref name="log"/> when there is one
    /// (Debug builds).</summary>
    public MissionLine(IAnnouncer announcer, TextCleaner cleaner, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(cleaner);
        this.announcer = announcer;
        this.cleaner = cleaner;
        this.log = log;
    }

    /// <summary>The mission last shown, or null before one.</summary>
    public string? Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    /// <summary>Keeps a mission file's <c>MissionText_</c> messages by their spoken text; a file compiled again replaces
    /// its own.</summary>
    public void Observe(MessageFileCompiled file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Source is null || file.Path is null)
        {
            return;
        }
        var name = MessageSerials.FromMessageFolder(file.Path);
        if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var missions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in MessageLibrary.Parse(file.Source))
        {
            if (message.Label.StartsWith(LabelPrefix, StringComparison.Ordinal) &&
                cleaner.Clean(message.Text) is { Length: > 0 } text)
            {
                missions.TryAdd(text, message.Label);
            }
        }
        lock (gate)
        {
            files[name] = missions;
        }
    }

    /// <summary>The game showed <paramref name="text"/>. Speaks it when it is a mission other than the current one, and
    /// returns what was said, or null.</summary>
    public string? Seen(string? text)
    {
        if (text is null || cleaner.Clean(text) is not { Length: > 0 } shown)
        {
            return null;
        }
        string? label = null;
        lock (gate)
        {
            if (shown == current)
            {
                return null;
            }
            foreach (var missions in files.Values)
            {
                if (missions.TryGetValue(shown, out label))
                {
                    break;
                }
            }
            if (label is null)
            {
                return null;
            }
            current = shown;
        }
        log?.Invoke($"mission label={label} changed");
        var said = Spoken(shown);
        announcer.Announce(new Announcement(said, Interrupt: false));
        return said;
    }

    /// <summary>How a mission is said: after the word the HUD's box is headed with.</summary>
    public static string Spoken(string mission) => $"Mission: {mission}";
}
