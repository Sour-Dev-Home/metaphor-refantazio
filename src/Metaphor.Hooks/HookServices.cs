using Metaphor.Core;
using Metaphor.Core.Announcements;
using Metaphor.Core.Cues;
using Metaphor.Core.Input;
using Metaphor.Core.Menus;
using Metaphor.Core.Messages;
using Metaphor.Core.Settings;
using Metaphor.Features.Dialogue;
using Metaphor.Features.Field;
using Metaphor.Features.Menus;

namespace Metaphor.Hooks;

/// <summary>What hooks are handed through their constructor (#85, architecture rule 12): the entry point builds it once
/// speech has started and the dev log is open, and the loader passes it to each hook it creates.</summary>
/// <remarks>Not a positional record: its generated <c>Deconstruct</c> writes through <c>out</c> parameters, which
/// architecture rule 8 flags in the hook layer.</remarks>
internal sealed record HookServices
{
    /// <summary>Where the message hooks report what the game compiled and asked for (#86).</summary>
    public required MessageEvents Messages { get; init; }

    /// <summary>Where the menu cursor hook reports each input a menu's cursor handled (#94).</summary>
    public required CursorEvents Cursors { get; init; }

    /// <summary>#55's message log, for the listening hooks that remain: null in Release builds, or when the file
    /// couldn't be opened.</summary>
    public required MessageLog? Log { get; init; }

    /// <summary>The last dialogue lines (#126), for the repeat key (#58) and the backlog fallback (#114). Empty unless
    /// the dialogue feature is feeding it.</summary>
    public DialogueHistory DialogueHistory { get; init; } = new();

    /// <summary>The dialogue controls (#58): the input hooks' frame (#301) hands them each read and, with it, the
    /// dialogue window's message pointer (#232).</summary>
    public required DialogueControls DialogueControls { get; init; }

    /// <summary>The menu controls (#214): the input hooks' frame (#301) hands them each read before the dialogue
    /// controls.</summary>
    public required MenuControls MenuControls { get; init; }

    /// <summary>The menu screens (#211): the input hooks' frame (#301) hands them each read of <see cref="MenusOpen"/>
    /// (#268) before the menu controls take the read.</summary>
    public required MenuScreens MenuScreens { get; init; }

    /// <summary>Reads whether the game has its menus open (#268); the loader locates what it reads (#300).</summary>
    public MenusOpenRead MenusOpen { get; init; } = new();

    /// <summary>Where the name entry hook reports each update of the box (#585): nowhere unless the menus feature is
    /// speaking it.</summary>
    public INameEntryObserver NameEntry { get; init; } = new NoNameEntry();

    /// <summary>What the input hooks' frame (#301) shows each keyboard read to, unchanged: the name entry's Enter hint
    /// (#585), or nothing.</summary>
    public IKeyWatcher? KeyWatcher { get; init; }

    /// <summary>Whether a name entry screen is up (#585), so the keyboard hook's dev log gives a key count, not the
    /// codes of a name being typed.</summary>
    public Func<bool> NameEntryUp { get; init; } = () => false;

    /// <summary>Reads the message the dialogue window holds (#232); the loader locates what it reads (#300).</summary>
    public DialogueWindowPointer DialogueWindow { get; init; } = new();

    /// <summary>Reads the story flags and typed names (#81, #107); the loader locates what it reads.</summary>
    public GameDataRead GameData { get; init; } = new();

    /// <summary>Reads the character name table (#81); the loader locates what it reads.</summary>
    public DataTableRead CharacterTable { get; init; } = DataTableRead.Characters();

    /// <summary>Reads the follower table (#107); the loader locates what it reads.</summary>
    public DataTableRead FollowerTable { get; init; } = DataTableRead.Followers();

    /// <summary>Every reader of a game global, which the loader hands each global it locates before any hook goes in.
    /// </summary>
    public IReadOnlyList<IReadsGlobal> Globals =>
        [MenusOpen, DialogueWindow, GameData, CharacterTable, FollowerTable, Voice, FieldWorld, FieldScene, FieldDatabase,
            PersonTable];

    /// <summary>Reads <c>fldWorld</c> for the nearby list (#181); the loader locates it.</summary>
    public FieldGlobal FieldWorld { get; init; } = FieldGlobal.World();

    /// <summary>Reads the field's scene root for free roam (#181); the loader locates it.</summary>
    public FieldGlobal FieldScene { get; init; } = FieldGlobal.SceneRoot();

    /// <summary>Reads the field database for the map id (#181); the loader locates it.</summary>
    public FieldGlobal FieldDatabase { get; init; } = FieldGlobal.Database();

    /// <summary>Reads the person table for people's names (#181); the loader locates it.</summary>
    public DataTableRead PersonTable { get; init; } = DataTableRead.People();

    /// <summary>The field controls (#181): the input hooks' frame (#301) hands them what the dialogue controls leave.
    /// None unless the services speak.</summary>
    public FieldControls? FieldControls { get; init; }

    /// <summary>The place names "where am I" says (#181): the message lookup hook feeds it.</summary>
    public FieldPlace? FieldPlace { get; init; }

    /// <summary>The field reads (#181), for the map id beside each place-name lookup.</summary>
    public FieldMemory? Field { get; init; }

    /// <summary>Reads the game's voice language for the auto voiced-lines mode (#538); the loader locates what it reads
    /// (#300).</summary>
    public VoiceLanguageRead Voice { get; init; } = new();

    /// <summary>Names each cursor manager's screen class (#208): the choice cursor getter hook feeds it, and the menu
    /// cursor hook asks it for each input.</summary>
    public ScreenClassLookup ScreenClasses { get; init; } = new();

    /// <summary>The messages the game fetches by serial (#584), from the files it compiled; empty unless the message
    /// file hook is feeding it.</summary>
    public MessageSerials Serials { get; init; } = new([]);

    /// <summary>Where features play the mod's sound cues (#636, ADR 0019); silent unless the entry point starts one.
    /// </summary>
    public ICuePlayer Cues { get; init; } = new SilentCuePlayer();

    /// <summary>The travel map's destination panel (#545): the panel hook hands it each destination shown.</summary>
    public required TravelMapDestination TravelMap { get; init; }

    /// <summary>The field map's name strip (#586): its composer and set hooks hand it each place's name and showing.
    /// </summary>
    public required FieldMapName FieldMapName { get; init; }

    /// <summary>The mod's folder, outside the repository, where a development run leaves files for its hooks.</summary>
    public required string ModFolder { get; init; }

    /// <summary>Services for the mod in <paramref name="modFolder"/> whose requested dialogue lines and opened choices
    /// are spoken through <paramref name="announcer"/>, voiced lines as <paramref name="settings"/> say. The library
    /// hears each compiled file first, speech each requested line and choice first, and the menu screens (#208, which
    /// tell the title menu, #94, the main menu and any other screen, #97) and a choice's focus (#95) each cursor input; the report log (#188) and the message log,
    /// when there are, hear every event last.</summary>
    /// <remarks>Features that play sound cues (#636) are handed <paramref name="cues"/>.</remarks>
    public static HookServices Speaking(
        IAnnouncer announcer, ICuePlayer cues, SettingsReloader settings, ReportLog? report, MessageLog? log,
        string modFolder)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(settings);
        var library = new MessageLibrary();
        // Serials as build 548ddc95 numbers them; the message file hook that feeds it installs on that build only.
        var serials = new MessageSerials(MessageSerials.Steam18330018);
        var filesById = new MessageFilesById();
        var history = new DialogueHistory();
        var events = new MessageEvents();
        var cursors = new CursorEvents();
        // Speakers and name tags are named as the screen shows them (#81, #107), read from the game when a line is
        // shown; a read that fails falls back to the file's speaker, and drops the tag as before.
        var gameData = new GameDataRead();
        var characterTable = DataTableRead.Characters();
        var followerTable = DataTableRead.Followers();
        var names = new GameNames(new GameNameMemory(gameData, characterTable, followerTable), library);
        var cleaner = new TextCleaner(GameNames.Rules, names.Placeholder);
        // Lines go through the reader on/off switch; the repeat and the switch's own state are spoken either way.
        // Auto follows the game's voice language, read on each line (#538); unknown speaks the full line.
        var reader = new DialogueReader(announcer);
        var voiceLanguage = new VoiceLanguageRead();
        var dialogue = new DialogueAnnouncer(
            reader, cleaner, history, () => settings.Current.VoicedLines, voiceLanguage.Read);
        var controls = new DialogueControls(DialogueBindings.Default, history, reader, announcer, dialogue.EndConversation);
        // Menus speak through the reader too (#214), and their controls toggle it with the dialogue's binding.
        var screens = new MenuScreens(KnownScreens.All);
        screens.Observe(new TitleMenuFocus(library, reader, cleaner));
        screens.Observe(new MainMenuFocus(reader));
        // A yes/no dialog hears each scripted choice and each select window too, to say the text of one opened outside
        // the scripts (#569).
        var yesNo = new YesNoDialogFocus(library, reader, cleaner);
        screens.Observe(yesNo);
        events.ObserveChoices(yesNo);
        events.ObserveSelects(yesNo);
        screens.Observe(new DifficultyFocus(library, reader, cleaner));
        screens.Observe(new SettingsFocus(library, reader, cleaner));
        screens.Observe(new QuickMenuFocus(library, reader, cleaner));
        screens.Observe(new UnknownScreenFocus(reader));
        var menuBindings = MenuBindings.Default with
        {
            ReaderButtons = DialogueBindings.Default.ReaderButtons,
            ReaderKey = DialogueBindings.Default.ReaderKey,
        };
        var menuControls = new MenuControls(menuBindings, screens, reader, []);
        // Name entry (#585): the title and refusals through the reader, typed characters as private announcements.
        var nameEntry = new NameEntrySpeech(library, reader, cleaner, () => Environment.TickCount64);
        screens.Observe(menuControls);
        events.ObserveFiles(library);
        events.ObserveFiles(serials);
        events.ObserveFiles(filesById);
        var lines = new DialogueLines(new ShownSpeakers(library, names), dialogue);
        events.ObserveLines(lines);
        events.ObserveShownLines(lines);
        events.ObserveShownLines(nameEntry);
        // A choice's prompt, spoken as it opens, isn't spoken again when the window shows it (#541).
        events.ObserveChoices(lines);
        var choices = new DialogueChoices(library, dialogue);
        events.ObserveChoices(choices);
        var choiceFocus = new ChoiceFocus(library, announcer, cleaner);
        events.ObserveChoices(choiceFocus);
        events.ObserveLines(choiceFocus);
        // A reply choice opened outside the scripts reaches only the select window call (#541).
        var selectChoices = new SelectWindowChoices(lines, choices, choiceFocus);
        events.ObserveChoices(selectChoices);
        events.ObserveSelects(selectChoices);
        cursors.Observe(screens);
        cursors.Observe(choiceFocus);
        // The field's nearby list and "where am I" (#181), read on the press and spoken straight to the announcer.
        var menusOpen = new MenusOpenRead();
        var window = new DialogueWindowPointer();
        var fieldWorld = FieldGlobal.World();
        var fieldScene = FieldGlobal.SceneRoot();
        var fieldDatabase = FieldGlobal.Database();
        var personTable = DataTableRead.People();
        var field = new FieldMemory(fieldWorld, fieldScene, fieldDatabase, personTable, names, menusOpen, window);
        var place = new FieldPlace(cleaner);
        events.ObserveFiles(place);
        var fieldControls = new FieldControls(
            FieldBindings.Default, field, place, announcer, cues, () => Environment.TickCount64,
            log is null ? null : log.FieldRead);
        if (report is not null)
        {
            events.ObserveFiles(report);
            events.ObserveLines(report);
            events.ObserveChoices(report);
            cursors.Observe(report);
        }
        if (log is not null)
        {
            events.ObserveFiles(log);
            events.ObserveLines(log);
            events.ObserveChoices(log);
            events.ObserveSelects(log);
            cursors.Observe(log);
        }
        return new()
        {
            Messages = events,
            Cursors = cursors,
            Log = log,
            ModFolder = modFolder,
            Serials = serials,
            TravelMap = new TravelMapDestination(filesById, reader, cleaner, () => Environment.TickCount64),
            FieldMapName = new FieldMapName(reader, cleaner),
            DialogueHistory = history,
            DialogueControls = controls,
            MenuControls = menuControls,
            MenuScreens = screens,
            NameEntry = nameEntry,
            KeyWatcher = nameEntry,
            NameEntryUp = nameEntry.OnScreen,
            GameData = gameData,
            CharacterTable = characterTable,
            FollowerTable = followerTable,
            Voice = voiceLanguage,
            MenusOpen = menusOpen,
            DialogueWindow = window,
            FieldWorld = fieldWorld,
            FieldScene = fieldScene,
            FieldDatabase = fieldDatabase,
            PersonTable = personTable,
            Field = field,
            FieldPlace = place,
            FieldControls = fieldControls,
            Cues = cues,
        };
    }
}

/// <summary>A name entry observer that ignores every update: what the hook reports to when no feature listens.</summary>
internal sealed class NoNameEntry : INameEntryObserver
{
    public void Observe(NameEntryFrame frame)
    {
    }
}
