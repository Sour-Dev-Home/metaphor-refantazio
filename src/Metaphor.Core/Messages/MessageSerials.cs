using Metaphor.Core.Announcements;

namespace Metaphor.Core.Messages;

/// <summary>A message file's serial numbers on one game build: its path from the <c>message</c> folder on, its first
/// serial and how many messages it holds.</summary>
public sealed record SerialRange(string File, int First, int Count);

/// <summary>Finds a game message by the serial number the game fetches it by.</summary>
public interface IMessageBySerial
{
    /// <summary>The message with this serial, or null when no known file holds it.</summary>
    GameMessage? FindBySerial(int serial);
}

/// <summary>The messages the game fetches by serial number (#584), from the message files it compiles. A file's
/// serials are its first serial plus each message's position in the file, and the game compiles its system files whole at
/// start-up, so their messages are known before any menu asks for one (docs/findings/message-serials-from-file-positions.md).
/// Only the files in its table are kept, and only when they hold the table's number of messages: another count means
/// another build's file, whose positions can't be trusted. Safe to use from several threads.</summary>
public sealed class MessageSerials : IMessageBySerial, IMessageFileObserver
{
    private const string MessageFolder = "message/";

    private static readonly TextCleaner Plain = new(TextCleanerRules.None);

    /// <summary>The files whose serials are known on game build 548ddc95… (Steam build 18330018): each first serial and
    /// count is the range test the game's message-file table holds for it, read from the exe, and matches the file as
    /// the game compiled it in play (docs/findings/message-serials-from-file-positions.md). The item and skill files'
    /// ranges are read from the same table (docs/findings/item-skill-message-serials-static.md); that the game compiles
    /// them with those counts is <c>[NEEDS VERIFICATION]</c>, and a file that doesn't is dropped.</summary>
    public static IReadOnlyList<SerialRange> Steam18330018 { get; } =
    [
        new("message/system/System_Message.msg", 1142, 790),
        new("message/system/System_Message_PC.msg", 11453, 354),
        new(ItemNames, 5143, 773),
        new(ItemDescriptions, 5916, 833),
        new(SkillNames, 6749, 1086),
        new(SkillDescriptions, 7835, 1052),
    ];

    /// <summary>The item names file: battle's item list resolves its rows' names from it (#247).</summary>
    public const string ItemNames = "message/system/Item_Message_1.msg";

    /// <summary>The item descriptions file.</summary>
    public const string ItemDescriptions = "message/system/Item_Message_2.msg";

    /// <summary>The skill names file: battle's skill list resolves its rows' names from it (#247).</summary>
    public const string SkillNames = "message/system/Skill_Message_1.msg";

    /// <summary>The skill descriptions file.</summary>
    public const string SkillDescriptions = "message/system/Skill_Message_2.msg";

    /// <summary>Whether <paramref name="serial"/> is in the range of the table's file named <paramref name="file"/>,
    /// whether or not the file was kept.</summary>
    public bool InFile(int serial, string file)
    {
        foreach (var range in ranges)
        {
            if (string.Equals(range.File, file, StringComparison.OrdinalIgnoreCase))
            {
                return serial >= range.First && serial < range.First + range.Count;
            }
        }
        return false;
    }

    private readonly SerialRange[] ranges;
    private readonly GameMessage[]?[] files;
    private readonly Lock gate = new();

    public MessageSerials(IEnumerable<SerialRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        this.ranges = [.. ranges];
        files = new GameMessage[]?[this.ranges.Length];
    }

    /// <summary>Keeps a compiled file's messages in order when its table entry names it; a file compiled again replaces
    /// the one before, and one with another count drops it.</summary>
    public void Observe(MessageFileCompiled file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var name = FromMessageFolder(file.Path);
        var index = Array.FindIndex(ranges, range => string.Equals(range.File, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }
        var messages = MessageLibrary.Parse(file.Source);
        lock (gate)
        {
            files[index] = messages.Count == ranges[index].Count ? [.. messages] : null;
        }
    }

    /// <summary>The message with this serial, or null when no file kept holds it.</summary>
    public GameMessage? FindBySerial(int serial)
    {
        lock (gate)
        {
            for (var i = 0; i < ranges.Length; i++)
            {
                var position = serial - ranges[i].First;
                if (files[i] is { } messages && position >= 0 && position < messages.Length)
                {
                    return messages[position];
                }
            }
        }
        return null;
    }

    /// <summary>The message's text as <paramref name="cleaner"/> says it, or null when it is unknown or empty.</summary>
    public string? TextOf(int serial, TextCleaner cleaner)
    {
        ArgumentNullException.ThrowIfNull(cleaner);
        return FindBySerial(serial) is { } message && cleaner.Clean(message.Text) is { Length: > 0 } text ? text : null;
    }

    /// <summary>For the dev log (#584): the message's label and its text with its tags dropped, or that it is unknown.
    /// </summary>
    public string Describe(int serial) =>
        FindBySerial(serial) is { } message
            ? $"label={message.Label} text=\"{LogText.Escaped(Plain.Clean(message.Text))}\""
            : "label=(unknown)";

    /// <summary>A compiled file's path from <c>message/</c> on, such as <c>message/system/X.msg</c> from
    /// <c>EN/message/system/X.msg</c>: the language folder before it differs by the game's language.</summary>
    public static string FromMessageFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var slashed = path.Replace('\\', '/');
        if (slashed.StartsWith(MessageFolder, StringComparison.OrdinalIgnoreCase))
        {
            return slashed;
        }
        var at = slashed.IndexOf("/" + MessageFolder, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? slashed : slashed[(at + 1)..];
    }
}
