using System.Diagnostics.CodeAnalysis;

namespace Metaphor.Core.Messages;

/// <summary>A message file the game compiled: its path, the number the game gave it, and its source text.</summary>
public sealed record MessageFileCompiled(string Path, int Id, string Source);

/// <summary>A line the game's scripts asked for by label, and the kind of request it came with.</summary>
public sealed record LineRequested(int Kind, string Label);

/// <summary>A line the game put in its message window by any route, scripted or not (#526): the request's kind and the
/// MD5 of the line's label, as 32 upper-case hex digits. Some event scenes show lines this way without asking for them
/// by label (docs/raw-sources/msg-window-show-line-2026-11-20.md).</summary>
public sealed record LineShown(int Kind, string LabelHash);

/// <summary>A choice the game's scripts opened (#82): the request's kind, the label of the prompt asked before it and
/// the label of the message holding its options, each null when it couldn't be read.</summary>
public sealed record ChoiceRequested(int Kind, string? PromptLabel, string? ChoiceLabel);

/// <summary>A select window any game code opened (#569), the scripts' choices included: the request's kind, the label
/// of the prompt shown with it and the label of the message holding its options, each null when it couldn't be read.
/// The scripts' choices also arrive, just before, as a <see cref="ChoiceRequested"/>.</summary>
public sealed record SelectRequested(int Kind, string? PromptLabel, string? ChoiceLabel);

/// <summary>A message the game fetched by its serial number (#247): the serial and the first dword of the flags it was
/// asked with, which pick a style, not a message (docs/findings/message-serials-from-file-positions.md). A value, so a
/// lookup the game makes many times a frame allocates nothing.</summary>
public readonly record struct SerialResolved(int Serial, int Flags);

/// <summary>Told of each message the game fetches by serial number.</summary>
public interface ISerialResolveObserver
{
    void Observe(SerialResolved resolve);

    /// <summary>Whether it wants fetches now; the hook skips its reads when no observer does. True unless the observer
    /// says otherwise.</summary>
    bool Listening => true;
}

/// <summary>Told of each message file the game compiles.</summary>
public interface IMessageFileObserver
{
    void Observe(MessageFileCompiled file);
}

/// <summary>Told of each line the game asks for by label.</summary>
public interface ILineRequestObserver
{
    void Observe(LineRequested line);
}

/// <summary>Told of each line the game puts in its message window.</summary>
public interface ILineShownObserver
{
    void Observe(LineShown line);
}

/// <summary>Told of each choice the game opens.</summary>
public interface IChoiceRequestObserver
{
    void Observe(ChoiceRequested choice);
}

/// <summary>Told of each select window the game opens.</summary>
public interface ISelectRequestObserver
{
    void Observe(SelectRequested window);
}

/// <summary>Hands each message event a hook decoded to its observers (#86), so one hook per game function serves
/// them all. Observers hear each event in the order they were added, and one that throws never stops the rest. Add
/// observers before the hooks go live; telling them is safe from any game thread.</summary>
public sealed class MessageEvents
{
    private readonly Lock gate = new();
    private IMessageFileObserver[] fileObservers = [];
    private ILineRequestObserver[] lineObservers = [];
    private IChoiceRequestObserver[] choiceObservers = [];
    private ILineShownObserver[] shownObservers = [];
    private ISelectRequestObserver[] selectObservers = [];
    private ISerialResolveObserver[] serialObservers = [];

    /// <summary>Adds an observer of messages fetched by serial, after those already added.</summary>
    public void ObserveSerials(ISerialResolveObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            serialObservers = [.. serialObservers, observer];
        }
    }

    /// <summary>Whether any serial observer wants fetches now (<see cref="ISerialResolveObserver.Listening"/>), so the
    /// hook, which runs from about 250 places, reads nothing when none does. One that throws counts as not listening.
    /// </summary>
    [SuppressMessage("Design", "CA1031", Justification = "One observer's failure must not reach the game.")]
    public bool SerialsWanted
    {
        get
        {
            foreach (var observer in Volatile.Read(ref serialObservers))
            {
                try
                {
                    if (observer.Listening)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                }
            }
            return false;
        }
    }

    /// <summary>Tells every serial observer that the game fetched a message by serial. The game does so from about 250
    /// places, some every frame, so this takes no lock and makes no closure.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "One observer's failure must not keep the event from the others, or reach the game.")]
    public void Resolved(SerialResolved resolve)
    {
        foreach (var observer in Volatile.Read(ref serialObservers))
        {
            try
            {
                observer.Observe(resolve);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>Adds an observer of compiled message files, after those already added.</summary>
    public void ObserveFiles(IMessageFileObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            fileObservers = [.. fileObservers, observer];
        }
    }

    /// <summary>Adds an observer of requested lines, after those already added.</summary>
    public void ObserveLines(ILineRequestObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            lineObservers = [.. lineObservers, observer];
        }
    }

    /// <summary>Adds an observer of lines put in the message window, after those already added.</summary>
    public void ObserveShownLines(ILineShownObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            shownObservers = [.. shownObservers, observer];
        }
    }

    /// <summary>Adds an observer of opened choices, after those already added.</summary>
    public void ObserveChoices(IChoiceRequestObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            choiceObservers = [.. choiceObservers, observer];
        }
    }

    /// <summary>Adds an observer of opened select windows, after those already added.</summary>
    public void ObserveSelects(ISelectRequestObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            selectObservers = [.. selectObservers, observer];
        }
    }

    /// <summary>Tells every file observer that the game compiled <paramref name="file"/>.</summary>
    public void Compiled(MessageFileCompiled file) => TellEach(Volatile.Read(ref fileObservers), observer => observer.Observe(file));

    /// <summary>Tells every line observer that the game asked for <paramref name="line"/>.</summary>
    public void Requested(LineRequested line) => TellEach(Volatile.Read(ref lineObservers), observer => observer.Observe(line));

    /// <summary>Tells every shown-line observer that the game put <paramref name="line"/> in its message window.</summary>
    public void Shown(LineShown line) => TellEach(Volatile.Read(ref shownObservers), observer => observer.Observe(line));

    /// <summary>Tells every choice observer that the game opened <paramref name="choice"/>.</summary>
    public void ChoiceOpened(ChoiceRequested choice) =>
        TellEach(Volatile.Read(ref choiceObservers), observer => observer.Observe(choice));

    /// <summary>Tells every select observer that the game opened <paramref name="window"/>.</summary>
    public void SelectOpened(SelectRequested window) =>
        TellEach(Volatile.Read(ref selectObservers), observer => observer.Observe(window));

    [SuppressMessage("Design", "CA1031", Justification = "One observer's failure must not keep the event from the others, or reach the game.")]
    private static void TellEach<T>(T[] observers, Action<T> tell)
    {
        foreach (var observer in observers)
        {
            try
            {
                tell(observer);
            }
            catch (Exception)
            {
            }
        }
    }
}
