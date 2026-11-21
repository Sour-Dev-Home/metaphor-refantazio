namespace Metaphor.Features.Battle.Tests;

/// <summary>A clock the test moves by hand; its timers fire when it passes their due time.</summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        timers.Add(timer);
        return timer;
    }

    public void Advance(long milliseconds)
    {
        now += TimeSpan.FromMilliseconds(milliseconds);
        foreach (var timer in timers.ToList())
        {
            timer.FireIfDue(now);
        }
    }

    private sealed class ManualTimer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            due = dueTime == Timeout.InfiniteTimeSpan ? null : time.now + dueTime;
            return true;
        }

        public void FireIfDue(DateTimeOffset at)
        {
            if (due is { } when && when <= at)
            {
                due = null;
                callback(state);
            }
        }

        public void Dispose() => due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
