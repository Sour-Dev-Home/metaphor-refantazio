using System.Globalization;
using Metaphor.Core.Announcements;
using Metaphor.Core.Battle;
using Metaphor.Core.Input;

namespace Metaphor.Features.Battle;

/// <summary>The status key (#631, the maintainer's choice of 2026-11-21): in a battle, H on the keyboard or L3 on the
/// pad, pressed alone, says every party member "name HP of max" ("of max" once a hit has shown it), then the enemies
/// this battle has hit, as <see cref="HitLines"/> labels them, with the HP each had left, and the press is hidden from
/// the game. Outside a battle the key does nothing and reaches the game: L3 recentres the world map's cursor (heard by
/// the maintainer, 2026-11-21). Any other read, a chord with H or L3 included, reaches it unchanged. A press is an
/// up-to-down edge on any pad or the keyboard, so a mirroring second pad can't fire it again; whether a battle is on is
/// asked once, at that edge, and holds until every source lets go. Safe to call from any of the game's threads.
/// </summary>
/// <remarks>L3 is the game's mask bit <c>0x0002</c>, XInput's left thumb, and H is key code <c>0x48</c>, on build
/// 548ddc95… (docs/raw-sources/pad-and-keyboard-state-2026-11-16.md). Whether the game uses either in battle is
/// <c>[NEEDS VERIFICATION]</c> by ear: the battle inventory names the bottom bar's other buttons but not the one for
/// its Status entry, which might be L3 (docs/raw-sources/battle-inventory-2026-11-17.md).</remarks>
public sealed class BattleStatus : IInputControls
{
    /// <summary>L3 in the game's pad mask.</summary>
    public const int LeftThumb = 0x0002;

    /// <summary>The H key.</summary>
    public const int KeyH = 0x48;

    private const int KeyboardSource = -1;

    private readonly IBattleParty party;
    private readonly HitLines hits;
    private readonly IBattleScene scene;
    private readonly IAnnouncer announcer;
    private readonly Lock gate = new();
    private readonly HashSet<int> holding = [];
    private bool inBattle;

    public BattleStatus(IBattleParty party, HitLines hits, IBattleScene scene, IAnnouncer announcer)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(announcer);
        this.party = party;
        this.hits = hits;
        this.scene = scene;
        this.announcer = announcer;
    }

    public int Pad(int pad, int buttons, bool made)
    {
        if (!made)
        {
            return buttons;
        }
        return Read(pad, buttons == LeftThumb) ? 0 : buttons;
    }

    public IReadOnlyList<int> Keys(IReadOnlyList<int> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return Read(KeyboardSource, keys.Count == 1 && keys[0] == KeyH) ? [] : keys;
    }

    /// <summary>What the key says now.</summary>
    public string Status()
    {
        var members = party.Members().OrderBy(member => member.Slot).Select(member => member.HpMax is { } max
            ? hits.MemberSays(member.Slot, member.Unit, string.Create(CultureInfo.InvariantCulture, $"{member.Hp} of {max}"))
            : hits.MemberSays(member.Slot, member.Unit, string.Create(CultureInfo.InvariantCulture, $"{member.Hp} HP")));
        var enemies = hits.KnownEnemies()
            .Select(enemy => string.Create(CultureInfo.InvariantCulture, $"{enemy.Label}, {enemy.Hp}"));
        var parts = new[] { string.Join(", ", members) }.Where(part => part.Length > 0).Concat(enemies).ToList();
        return parts.Count > 0 ? string.Join(". ", parts) : "No party status";
    }

    // Whether this read is the key held in a battle, so hidden from the game.
    private bool Read(int source, bool held)
    {
        bool pressed;
        bool hidden;
        lock (gate)
        {
            var before = holding.Count > 0;
            if (held)
            {
                holding.Add(source);
            }
            else
            {
                holding.Remove(source);
            }
            pressed = !before && holding.Count > 0;
            if (pressed)
            {
                inBattle = scene.InBattle();
            }
            else if (holding.Count == 0)
            {
                inBattle = false;
            }
            pressed &= inBattle;
            hidden = held && inBattle;
        }
        if (pressed)
        {
            announcer.Announce(new Announcement(Status(), Interrupt: true));
        }
        return hidden;
    }
}
