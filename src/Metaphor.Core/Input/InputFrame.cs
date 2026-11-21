using Metaphor.Core.Menus;

namespace Metaphor.Core.Input;

/// <summary>The game state the input frame asks about on each read (#301), which the hook layer reads from the globals
/// the loader locates (#300).</summary>
public interface IGameInputState
{
    /// <summary>The message the dialogue window holds: non-zero while a line or a choice is up, 0 when it's closed, null
    /// when it's unknown or couldn't be read (#232).</summary>
    long? DialogueWindow();

    /// <summary>Whether the game has its menus open; false when it's unknown or couldn't be read (#268).</summary>
    bool MenusOpen();
}

/// <summary>Controls that take each pad and keyboard read, act on a press, and say what to hand on.</summary>
public interface IInputControls
{
    /// <summary>Takes one read of <paramref name="pad"/>'s buttons that the game reports as made or not (#225), and
    /// returns the buttons to hand on.</summary>
    int Pad(int pad, int buttons, bool made);

    /// <summary>Takes one read of the keys held down, and returns the keys to hand on.</summary>
    IReadOnlyList<int> Keys(IReadOnlyList<int> keys);
}

/// <summary>Two input controls as one (#631): each read goes to the first, and what it hands on to the second.</summary>
public sealed class ChainedControls(IInputControls first, IInputControls second) : IInputControls
{
    public int Pad(int pad, int buttons, bool made) => second.Pad(pad, first.Pad(pad, buttons, made), made);

    public IReadOnlyList<int> Keys(IReadOnlyList<int> keys) => second.Keys(first.Keys(keys));
}

/// <summary>Input controls that act only while the dialogue window holds a message, so take each read of it.</summary>
public interface IWindowInputControls : IInputControls
{
    /// <summary>One read of the message the dialogue window holds, as <see cref="IGameInputState.DialogueWindow"/>.
    /// </summary>
    void WindowRead(long? currentMessage);
}

/// <summary>Watches each keyboard read without changing it, such as the name entry's Enter hint (#585).</summary>
public interface IKeyWatcher
{
    /// <summary>Takes one read of the keys held down, as the game read them.</summary>
    void Keys(IReadOnlyList<int> keys);
}

/// <summary>What the frame hands the game for one pad read, and the window read it was decided on.</summary>
public readonly record struct PadAnswer(int Buttons, long? Window);

/// <summary>What the frame hands the game for one keyboard read, and the window read it was decided on.</summary>
public readonly record struct KeysAnswer(IReadOnlyList<int> Keys, long? Window);

/// <summary>The per-frame input rules (#301), behind the pad and keyboard hooks: on each read it hands the dialogue
/// controls a read of the window and the menu screens a read of whether the menus are open, then the read to the menu
/// controls first and what they leave to the dialogue controls, so in a menu the reader binding toggles once, through
/// them. A pad read the game reports as not made (#225) fires nothing and comes back unchanged. The answer only ever
/// clears bits or drops keys from the read, so the hook's write-back can't add input the game didn't see. It keeps no
/// state of its own; the controls lock theirs, so any of the game's threads may call it.</summary>
public sealed class InputFrame
{
    private readonly IGameInputState game;
    private readonly MenuScreens screens;
    private readonly IInputControls menus;
    private readonly IWindowInputControls dialogue;
    private readonly IInputControls field;
    private readonly IKeyWatcher? watcher;

    public InputFrame(IGameInputState game, MenuScreens screens, IInputControls menus, IWindowInputControls dialogue)
        : this(game, screens, menus, dialogue, NoControls)
    {
    }

    /// <summary>Controls that hand every read on unchanged, for a frame without field controls.</summary>
    public static IInputControls NoControls => PassThrough.Instance;

    /// <summary>A frame whose <paramref name="field"/> controls (#181) get what the dialogue controls leave;
    /// <paramref name="watcher"/>, when given, sees each keyboard read first, as the game read it, and changes
    /// nothing.</summary>
    public InputFrame(
        IGameInputState game, MenuScreens screens, IInputControls menus, IWindowInputControls dialogue,
        IInputControls field, IKeyWatcher? watcher = null)
    {
        this.watcher = watcher;
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentNullException.ThrowIfNull(menus);
        ArgumentNullException.ThrowIfNull(dialogue);
        ArgumentNullException.ThrowIfNull(field);
        this.game = game;
        this.screens = screens;
        this.menus = menus;
        this.dialogue = dialogue;
        this.field = field;
    }

    /// <summary>Takes one read of <paramref name="pad"/>'s 16-bit <paramref name="buttons"/>, made or not, and returns
    /// the buttons to hand the game: a subset of them.</summary>
    public PadAnswer Pad(int pad, int buttons, bool made)
    {
        var window = ReadState();
        var handed = field.Pad(pad, dialogue.Pad(pad, menus.Pad(pad, buttons, made), made), made);
        return new(handed & buttons, window);
    }

    /// <summary>Takes one read of the keys held down and returns the keys to hand the game: <paramref name="keys"/>
    /// itself when none is dropped, or the ones kept, in their order.</summary>
    public KeysAnswer Keys(IReadOnlyList<int> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var window = ReadState();
        watcher?.Keys(keys);
        var handed = field.Keys(dialogue.Keys(menus.Keys(keys)));
        return new(Kept(keys, handed), window);
    }

    /// <summary>Controls that hand every read on unchanged, for a frame without field controls.</summary>
    private sealed class PassThrough : IInputControls
    {
        public static PassThrough Instance { get; } = new();

        public int Pad(int pad, int buttons, bool made) => buttons;

        public IReadOnlyList<int> Keys(IReadOnlyList<int> keys) => keys;
    }

    private long? ReadState()
    {
        var window = game.DialogueWindow();
        dialogue.WindowRead(window);
        screens.MenusRead(game.MenusOpen());
        return window;
    }

    private static IReadOnlyList<int> Kept(IReadOnlyList<int> keys, IReadOnlyList<int> handed)
    {
        if (ReferenceEquals(keys, handed))
        {
            return keys;
        }
        var kept = keys.Where(handed.Contains).ToList();
        return kept.Count == keys.Count ? keys : kept;
    }
}
