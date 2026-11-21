using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Metaphor.Core;
using Metaphor.Core.Announcements;
using Metaphor.Core.Battle;

namespace Metaphor.Features.Battle;

/// <summary>A sound played before a hit's number, by the target's side (#631: the maintainer's sound cues, built as
/// their own feature). This feature only calls it.</summary>
public interface IHitCue
{
    void Before(BattleSide side);
}

/// <summary>Says each target a battle action hits (#247, wording by the maintainer on 2026-11-21): the number alone for
/// damage ("34"), "34 healed" for a gain, each queued after the last. Once the action's hits stop for
/// <see cref="ActionGapMilliseconds"/>, one summary of who changed: party members as "name HP" ("Strohl 89, Hulkenberg
/// 120"), then each enemy as "Enemy, 40", given an ordinal by the order the battle first hit them when it has hit
/// more than one ("Second enemy, 40"; a label never ends in a digit before the number, since "Ally 1 141" sounds like
/// 1141). Only hits whose popup carries a number are said. Safe to call from several threads.</summary>
/// <remarks>
/// <para>No safe read of a unit's name is known yet: a party member is named by <c>memberName</c> when it knows its unit
/// number, else "First ally" and so on by slot; an enemy is "Enemy". An enemy's ordinal is the order it was first hit,
/// not its place on screen, which isn't read.</para>
/// <para>The game gives no "action over" signal the mod reads, so an action ends at a quiet gap, and a battle at a long
/// one (<see cref="BattleGapMilliseconds"/>), which forgets its enemies; both lengths are guesses to check by ear.</para>
/// </remarks>
public sealed class HitLines : IBattleHitObserver, IDisposable
{
    /// <summary>How long the same hit told again is taken as the same one.</summary>
    public const long RepeatMilliseconds = 500;

    /// <summary>How long after an action's last hit its summary is said.</summary>
    public const long ActionGapMilliseconds = 1500;

    /// <summary>How long without a hit before the next hit starts a new battle.</summary>
    public const long BattleGapMilliseconds = 180_000;

    private readonly IAnnouncer announcer;
    private readonly TimeProvider time;
    private readonly Func<int, string?> memberName;
    private readonly IHitCue? cue;
    private readonly RepeatGate repeats;
    private readonly Lock gate = new();
    private readonly List<long> enemies = [];
    private readonly Dictionary<long, int> enemyHp = [];
    private readonly List<BattleHit> action = [];
    private readonly ITimer timer;
    private long lastHit = long.MinValue / 2;

    /// <summary>Hits said through <paramref name="announcer"/>, timed by <paramref name="time"/>; party members named
    /// by <paramref name="memberName"/> from their unit number, or null when unknown; <paramref name="cue"/> is played
    /// before each number, when there is one.</summary>
    public HitLines(IAnnouncer announcer, TimeProvider time, Func<int, string?> memberName, IHitCue? cue = null)
    {
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(memberName);
        this.announcer = announcer;
        this.time = time;
        this.memberName = memberName;
        this.cue = cue;
        repeats = new RepeatGate(RepeatMilliseconds, Now);
        timer = time.CreateTimer(_ => Summarize(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Observe(BattleHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (!hit.HasNumber || !repeats.LetThrough(hit.Target, ((long)hit.HpChange << 32) | (uint)hit.HpLeft))
        {
            return;
        }
        lock (gate)
        {
            var now = Now();
            if (now - lastHit > BattleGapMilliseconds)
            {
                enemies.Clear();
                enemyHp.Clear();
            }
            lastHit = now;
            if (hit.Side == BattleSide.Enemy)
            {
                if (!enemies.Contains(hit.Target))
                {
                    enemies.Add(hit.Target);
                }
                enemyHp[hit.Target] = hit.HpLeft;
            }
            action.Add(hit);
            timer.Change(TimeSpan.FromMilliseconds(ActionGapMilliseconds), Timeout.InfiniteTimeSpan);
        }
        cue?.Before(hit.Side);
        var number = hit.HpChange switch
        {
            < 0 => string.Create(CultureInfo.InvariantCulture, $"{-(long)hit.HpChange}"),
            > 0 => string.Create(CultureInfo.InvariantCulture, $"{hit.HpChange} healed"),
            _ => "0",
        };
        announcer.Announce(new Announcement(number, Interrupt: false));
    }

    /// <summary>The enemies this battle has hit, by label, with the HP each had left at its last hit, in the order first
    /// hit: for the status key.</summary>
    public IReadOnlyList<(string Label, int Hp)> KnownEnemies()
    {
        lock (gate)
        {
            return [.. enemies.Select(enemy => (EnemyLabel(enemy), enemyHp[enemy]))];
        }
    }

    /// <summary>A party member's name followed by <paramref name="rest"/> (a number): <c>memberName</c>'s, or "First
    /// ally," and so on by slot. The comma and the ordinal word keep the reader from running the slot into the number
    /// ("Ally 1 141" sounds like 1141).</summary>
    public string MemberSays(int slot, int? unit, string rest) =>
        unit is { } known && memberName(known) is { Length: > 0 } name
            ? $"{name} {rest}"
            : $"{Ordinal(slot + 1, "ally")}, {rest}";

    /// <summary>"First ally", "Second enemy", ... ; past the tenth, "11th ally".</summary>
    private static string Ordinal(int n, string noun) => n >= 1 && n <= Ordinals.Length
        ? $"{Ordinals[n - 1]} {noun}"
        : string.Create(CultureInfo.InvariantCulture, $"{n}th {noun}");

    private static readonly string[] Ordinals =
        ["First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth"];

    public void Dispose() => timer.Dispose();

    // The timer's callback runs on a thread-pool thread, where an exception would end the game.
    [SuppressMessage("Design", "CA1031", Justification = "An exception on the timer's thread would end the game.")]
    private void Summarize()
    {
        try
        {
            SummarizeAction();
        }
        catch (Exception)
        {
        }
    }

    private void SummarizeAction()
    {
        string? said;
        lock (gate)
        {
            // Each target once, at its last hit, in the order first hit; only who changed.
            var last = new Dictionary<long, BattleHit>();
            var order = new List<long>();
            var changed = new HashSet<long>();
            foreach (var hit in action)
            {
                if (!last.ContainsKey(hit.Target))
                {
                    order.Add(hit.Target);
                }
                last[hit.Target] = hit;
                if (hit.HpChange != 0)
                {
                    changed.Add(hit.Target);
                }
            }
            action.Clear();
            var targets = order.Where(changed.Contains).Select(target => last[target]).ToList();
            var allies = targets.Where(hit => hit.Side == BattleSide.Ally)
                .OrderBy(hit => hit.Slot ?? int.MaxValue)
                .Select(hit => hit.Slot is { } slot
                    ? MemberSays(slot, hit.Unit, hit.HpLeft.ToString(CultureInfo.InvariantCulture))
                    : string.Create(CultureInfo.InvariantCulture, $"Ally, {hit.HpLeft}"))
                .ToList();
            var others = targets.Where(hit => hit.Side != BattleSide.Ally)
                .Select(hit => string.Create(CultureInfo.InvariantCulture,
                    $"{(hit.Side == BattleSide.Enemy ? EnemyLabel(hit.Target) : "Target")}, {hit.HpLeft}"));
            var parts = (allies.Count > 0 ? [string.Join(", ", allies)] : Array.Empty<string>()).Concat(others).ToList();
            said = parts.Count > 0 ? string.Join(". ", parts) : null;
        }
        if (said is not null)
        {
            announcer.Announce(new Announcement(said, Interrupt: false));
        }
    }

    // Called under the gate.
    private string EnemyLabel(long enemy) =>
        enemies.Count > 1
            ? Ordinal(enemies.IndexOf(enemy) + 1, "enemy")
            : "Enemy";

    private long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();
}
