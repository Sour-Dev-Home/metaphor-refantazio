using System.Diagnostics.CodeAnalysis;
using Reloaded.Hooks.Definitions;
// Both packages name an IReloadedHooks; the loader's controller is this one (as the template aliases it).
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace Metaphor.Hooks;

// The shapes a hook observes through (#88, architecture rule 14). A hook implements exactly one; the loader hooks the
// function with the adapter for that shape, which hands each call to Observe and then calls the original unchanged.
// Every argument is pointer-sized (rule 9); a double travels in an xmm register and stays a double. Every adapter
// returns rax, so a shape fits only a function returning an integer, a pointer or nothing: a float or double return
// (xmm0) needs a shape of its own. Evidence that one shared non-generic delegate per shape hooks a native function
// through Reloaded.Hooks: docs/raw-sources/shared-delegate-hook-spike-2026-11-16.md and
// docs/raw-sources/shared-delegate-hook-spike-shapes-2026-11-16.md.

/// <summary>A hook on a function of three pointer-sized arguments. A function that returns nothing fits too: the
/// adapter hands back whatever it left in rax, which its caller ignores.</summary>
internal interface IObserves3
{
    void Observe(nint first, nint second, nint third);
}

/// <summary>A hook on a function of four pointer-sized arguments.</summary>
internal interface IObserves4
{
    void Observe(nint first, nint second, nint third, nint fourth);
}

/// <summary>A hook on a function of five pointer-sized arguments.</summary>
internal interface IObserves5
{
    void Observe(nint first, nint second, nint third, nint fourth, nint fifth);
}

/// <summary>A hook on a function of six pointer-sized arguments (the last two on the stack).</summary>
internal interface IObserves6
{
    void Observe(nint first, nint second, nint third, nint fourth, nint fifth, nint sixth);
}

/// <summary>A hook on a function of one pointer-sized argument that wants what it returned: observed after the
/// original runs, with its <c>rax</c>.</summary>
internal interface IObserves1WithResult
{
    void Observe(nint first, nint result);
}

/// <summary>A hook on a function of two pointer-sized arguments, a <c>double</c>, then one more.</summary>
internal interface IObserves4WithDouble
{
    void Observe(nint first, nint second, double third, nint fourth);
}

/// <summary>A hook on a function of three pointer-sized arguments, a <c>double</c>, then one more.</summary>
internal interface IObserves5WithDouble
{
    void Observe(nint first, nint second, nint third, double fourth, nint fifth);
}

/// <summary>A hook on a function of up to four pointer-sized arguments that also sees what it returned: the adapter calls
/// the original first, then hands <c>Observe</c> the arguments as they were passed and the original's rax. A function
/// of fewer arguments fits too: the registers it doesn't read pass through unchanged and the observer ignores them.
/// </summary>
internal interface IObserves4WithResult
{
    void Observe(nint first, nint second, nint third, nint fourth, nint result);
}

/// <summary>A hook on a function of six pointer-sized arguments (the last two on the stack) that reads what it wrote:
/// the adapter calls the original first, passing all six on, then hands <c>Observe</c> the arguments as they were passed
/// and the original's rax (#247). A function of more than four arguments needs a shape that passes them all, since the
/// original reads the rest from the stack its caller built.</summary>
internal interface IObserves6WithResult
{
    void Observe(nint first, nint second, nint third, nint fourth, nint fifth, nint sixth, nint result);
}

/// <summary>A hook that changes what the game does (#58), rule 14's exception: it implements this instead of a shape,
/// and hooks its function itself. <see cref="Activate"/> must store the hook in a field before activating it, guard its
/// callback with a catch-all and always call the original (rules 8–11).</summary>
internal interface IChangesBehaviour
{
    /// <summary>Hooks the function at <paramref name="address"/>. Called at most once, by the loader.</summary>
    void Activate(IReloadedHooks hooks, nint address);
}

/// <summary>The hook loader's pass-through adapters (#88): the code every hook used to repeat. Each keeps its callback
/// in a field, creates the hook, stores it before activating it (rule 11), guards the observer with a catch-all, and
/// always calls the original. The loader keeps each adapter alive, and with it the hook it serves (rule 11 in
/// docs/coding-standards.md says why).</summary>
[SuppressMessage("Design", "CA1031", Justification = "An exception must never cross back into the game.")]
internal abstract class PassThrough
{
    private PassThrough(IGameHook hook) => Hook = hook;

    /// <summary>The hook this adapter hands each call to.</summary>
    public IGameHook Hook { get; }

    /// <summary>The adapter for the shape <paramref name="hook"/> observes, or null if it observes none.</summary>
    public static PassThrough? For(IGameHook hook) => hook switch
    {
        IObserves3 observer => new Adapter3(hook, observer),
        IObserves4 observer => new Adapter4(hook, observer),
        IObserves5 observer => new Adapter5(hook, observer),
        IObserves6 observer => new Adapter6(hook, observer),
        IObserves1WithResult observer => new Adapter1WithResult(hook, observer),
        IObserves4WithDouble observer => new Adapter4WithDouble(hook, observer),
        IObserves5WithDouble observer => new Adapter5WithDouble(hook, observer),
        IObserves4WithResult observer => new Adapter4WithResult(hook, observer),
        IObserves6WithResult observer => new Adapter6WithResult(hook, observer),
        IChangesBehaviour changer => new SelfHooked(hook, changer),
        _ => null,
    };

    /// <summary>Hands the hook the addresses of the functions it calls, then hooks the function at
    /// <paramref name="address"/>. Called at most once.</summary>
    public void Install(IReloadedHooks hooks, nint address, IReadOnlyDictionary<string, nint> calls)
    {
        Hook.UseCalls(calls);
        Activate(hooks, address);
    }

    private protected abstract void Activate(IReloadedHooks hooks, nint address);

    /// <summary>Not an adapter: hands the hooking to a hook that changes behaviour, so the loader still keeps it alive.
    /// </summary>
    private sealed class SelfHooked(IGameHook hook, IChangesBehaviour changer) : PassThrough(hook)
    {
        private protected override void Activate(IReloadedHooks hooks, nint address) => changer.Activate(hooks, address);
    }

    private sealed class Adapter3 : PassThrough
    {
        private delegate nint Function(nint first, nint second, nint third);

        private readonly IObserves3 observer;
        private readonly Function callback;
        private IHook<Function>? hook;

        public Adapter3(IGameHook hook, IObserves3 observer)
            : base(hook)
        {
            this.observer = observer;
            callback = OnCall;
        }

        private protected override void Activate(IReloadedHooks hooks, nint address)
        {
            // Stored before it goes live (rule 11).
            hook = hooks.CreateHook(callback, address);
            hook.Activate();
        }

        private nint OnCall(nint first, nint second, nint third)
        {
            try
            {
                observer.Observe(first, second, third);
            }
            catch (Exception)
            {
            }
            return hook!.OriginalFunction(first, second, third);
        }
    }

    private sealed class Adapter4 : PassThrough
    {
        private delegate nint Function(nint first, nint second, nint third, nint fourth);

        private readonly IObserves4 observer;
        private readonly Function callback;
        private IHook<Function>? hook;

        public Adapter4(IGameHook hook, IObserves4 observer)
            : base(hook)
        {
            this.observer = observer;
            callback = OnCall;
        }

        private protected override void Activate(IReloadedHooks hooks, nint address)
        {
            hook = hooks.CreateHook(callback, address);
            hook.Activate();
        }

        private nint OnCall(nint first, nint second, nint third, nint fourth)
        {
            try
            {
                observer.Observe(first, second, third, fourth);
            }
            catch (Exception)
            {
            }
            return hook!.OriginalFunction(first, second, third, fourth);
        }
    }

    private sealed class Adapter5 : PassThrough
    {
        private delegate nint Function(nint first, nint second, nint third, nint fourth, nint fifth);

        private readonly IObserves5 observer;
        private readonly Function callback;
        private IHook<Function>? hook;

        public Adapter5(IGameHook hook, IObserves5 observer)
            : base(hook)
        {
            this.observer = observer;
            callback = OnCall;
        }

        private protected override void Activate(IReloadedHooks hooks, nint address)
        {
            hook = hooks.CreateHook(callback, address);
            hook.Activate();
        }

        private nint OnCall(nint first, nint second, nint third, nint fourth, nint fifth)
        {
            try
            {
                observer.Observe(first, second, third, fourth, fifth);
            }
            catch (Exception)
            {
            }
            return hook!.OriginalFunction(first, second, third, fourth, fifth);
        }
    }

    private sealed class Adapter6 : PassThrough
    {
        private delegate nint Function(nint first, nint second, nint third, nint fourth, nint fifth, nint sixth);

        private readonly IObserves6 observer;
        private readonly Function callback;
        private IHook<Function>? hook;

        public Adapter6(IGameHook hook, IObserves6 observer)
            : base(hook)
        {
            this.observer = observer;
            callback = OnCall;
        }

        private protected override void Activate(IReloadedHooks hooks, nint address)
        {
            hook = hooks.CreateHook(callback, address);
            hook.Activate();
        }

        private nint OnCall(nint first, nint second, nint third, nint fourth, nint fifth, nint sixth)
        {
            try
            {
                observer.Observe(first, second, third, fourth, fifth, sixth);
            }
            catch (Exception)
            {
            }
            return hook!.OriginalFunction(first, second, third, fourth, fifth, sixth);
        }
    }

    private sealed class Adapter1WithResult : PassThrough
    {
        private delegate nint Function(nint first);

        private readonly IObserves1WithResult observer;
        private readonly Function callback;
        private IHook<Function>? hook;

        public Adapter1WithResult(IGameHook hook, IObserves1WithResult observer)
            : base(hook)
        {
            this.observer = observer;
            callback = OnCall;
        }

        private protected override void Activate(IReloadedHooks hooks, nint address)
        {
            hook = hooks.CreateHook(callback, address);
            hook.Activate();
        }

        private nint OnCall(nint first)
        {
            // The original runs first, so the observer sees what it returned; the game gets that value unchanged.
            var result = hook!.OriginalFunction(first);
            try
            {
                observer.Observe(first, result);
            }
            catch (Exception)
            {
            }
            return result;
        }
    }

    private sealed class Adapter4WithDouble : PassThrough
    {
        private delegate nint Function(nint first, nint second, double third, nint fourth);

        private readonly IObserves4WithDouble observer;
        private readonly Function callback;
        private IHook<Function>? hook;

        public Adapter4WithDouble(IGameHook hook, IObserves4WithDouble observer)
            : base(hook)
        {
            this.observer = observer;
            callback = OnCall;
        }

        private protected override void Activate(IReloadedHooks hooks, nint address)
        {
            hook = hooks.CreateHook(callback, address);
            hook.Activate();
        }

        private nint OnCall(nint first, nint second, double third, nint fourth)
        {
            try
            {
                observer.Observe(first, second, third, fourth);
            }
            catch (Exception)
            {
            }
            return hook!.OriginalFunction(first, second, third, fourth);
        }
    }

    private sealed class Adapter5WithDouble : PassThrough
    {
        private delegate nint Function(nint first, nint second, nint third, double fourth, nint fifth);

        private readonly IObserves5WithDouble observer;
        private readonly Function callback;
        private IHook<Function>? hook;

        public Adapter5WithDouble(IGameHook hook, IObserves5WithDouble observer)
            : base(hook)
        {
            this.observer = observer;
            callback = OnCall;
        }

        private protected override void Activate(IReloadedHooks hooks, nint address)
        {
            hook = hooks.CreateHook(callback, address);
            hook.Activate();
        }

        private nint OnCall(nint first, nint second, nint third, double fourth, nint fifth)
        {
            try
            {
                observer.Observe(first, second, third, fourth, fifth);
            }
            catch (Exception)
            {
            }
            return hook!.OriginalFunction(first, second, third, fourth, fifth);
        }
    }

    private sealed class Adapter4WithResult : PassThrough
    {
        private delegate nint Function(nint first, nint second, nint third, nint fourth);

        private readonly IObserves4WithResult observer;
        private readonly Function callback;
        private IHook<Function>? hook;

        public Adapter4WithResult(IGameHook hook, IObserves4WithResult observer)
            : base(hook)
        {
            this.observer = observer;
            callback = OnCall;
        }

        private protected override void Activate(IReloadedHooks hooks, nint address)
        {
            hook = hooks.CreateHook(callback, address);
            hook.Activate();
        }

        private nint OnCall(nint first, nint second, nint third, nint fourth)
        {
            var result = hook!.OriginalFunction(first, second, third, fourth);
            try
            {
                observer.Observe(first, second, third, fourth, result);
            }
            catch (Exception)
            {
            }
            return result;
        }
    }

    private sealed class Adapter6WithResult : PassThrough
    {
        private delegate nint Function(nint first, nint second, nint third, nint fourth, nint fifth, nint sixth);

        private readonly IObserves6WithResult observer;
        private readonly Function callback;
        private IHook<Function>? hook;

        public Adapter6WithResult(IGameHook hook, IObserves6WithResult observer)
            : base(hook)
        {
            this.observer = observer;
            callback = OnCall;
        }

        private protected override void Activate(IReloadedHooks hooks, nint address)
        {
            hook = hooks.CreateHook(callback, address);
            hook.Activate();
        }

        private nint OnCall(nint first, nint second, nint third, nint fourth, nint fifth, nint sixth)
        {
            var result = hook!.OriginalFunction(first, second, third, fourth, fifth, sixth);
            try
            {
                observer.Observe(first, second, third, fourth, fifth, sixth, result);
            }
            catch (Exception)
            {
            }
            return result;
        }
    }
}
