using System.Globalization;

namespace Metaphor.Features.Field;

/// <summary>One nearby thing as the list says it (#181).</summary>
/// <param name="Unit">The unit.</param>
/// <param name="Distance">Its distance across the ground, in the game's units.</param>
/// <param name="Bearing">Its direction from the player's facing, in radians, left positive, from −π to π.</param>
/// <param name="InReach">Whether the game's own reach test for its kind passes.</param>
public sealed record NearbyThing(FieldUnit Unit, double Distance, double Bearing, bool InReach)
{
    /// <summary>"Chest, 4 steps ahead-left, in reach": its name or kind, distance and direction.</summary>
    public string Text()
    {
        var steps = Nearby.Steps(Distance);
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{Unit.Name ?? Nearby.KindWord(Unit.Kind)}, {steps} {(steps == 1 ? "step" : "steps")} {Nearby.Direction(Bearing)}");
        return InReach ? text + ", in reach" : text;
    }
}

/// <summary>The nearby list (#181), per the maintainer's decisions of 2026-11-21 on #181: things within about 30 metres,
/// nearest first, at most 8, each with its kind or name, distance and direction from the player's facing.</summary>
public static class Nearby
{
    /// <summary>The most things one list holds.</summary>
    public const int MaxThings = 8;

    /// <summary>Game units in one spoken step. [NEEDS VERIFICATION]: the game's units per metre aren't measured yet, so
    /// the list speaks rough steps, taken as 100 units each, until a session measures them with the dev log.</summary>
    public const double UnitsPerStep = 100;

    /// <summary>The list's radius: the maintainer's "about 30 m", as 30 steps until a metre is measured.</summary>
    public const double Radius = 30 * UnitsPerStep;

    // The reach tests of fldGmcDoor::vf44 (0x6992a0) and fldGimmick::vf44 (0x697d00), which chests keep, on game build
    // 548ddc95… (Steam build 18330018): ground distance below the limit, height difference below 300, and within 90°
    // of facing (docs/findings/field-interaction-prompt-static.md).
    private const double NearDoorReach = 150;
    private const double FarReach = 300;
    private const double HeightReach = 300;
    private const double FacingReach = Math.PI / 2;

    /// <summary>The things of <paramref name="view"/> within <see cref="Radius"/>, nearest first, at most
    /// <see cref="MaxThings"/>.</summary>
    public static IReadOnlyList<NearbyThing> List(FieldView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return [.. view.Units
            .Select(unit => Place(view, unit))
            .Where(thing => thing.Distance <= Radius)
            .OrderBy(thing => thing.Distance)
            .Take(MaxThings)];
    }

    /// <summary>The things of <paramref name="view"/> the game's reach test passes for, nearest first, however many and
    /// whatever else is nearer (#644).</summary>
    public static IReadOnlyList<NearbyThing> InReach(FieldView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return [.. view.Units
            .Select(unit => Place(view, unit))
            .Where(thing => thing.InReach)
            .OrderBy(thing => thing.Distance)];
    }

    /// <summary>Spoken steps for a distance, at least one.</summary>
    public static int Steps(double distance) => Math.Max(1, (int)Math.Round(distance / UnitsPerStep));

    /// <summary>What a nameless thing is called. Doors are "Door", never "Exit", until a session proves which leave the
    /// map (maintainer, 2026-11-21).</summary>
    public static string KindWord(FieldKind kind) => kind switch
    {
        FieldKind.Person => "Person",
        FieldKind.Door => "Door",
        FieldKind.Chest => "Chest",
        FieldKind.Enemy => "Enemy",
        _ => "Thing",
    };

    /// <summary>One of eight directions for a bearing, left positive: ahead, ahead-left, left, behind-left, behind,
    /// behind-right, right, ahead-right.</summary>
    public static string Direction(double bearing)
    {
        var sector = (int)Math.Round(Normalised(bearing) / (Math.PI / 4));
        return sector switch
        {
            0 => "ahead",
            1 => "ahead-left",
            2 => "left",
            3 => "behind-left",
            -1 => "ahead-right",
            -2 => "right",
            -3 => "behind-right",
            _ => "behind",
        };
    }

    /// <summary>The ground reach of <paramref name="kind"/> by the game's test, or null when the game has none for it
    /// that the mod can read (people's isn't found, enemies aren't used). A door's is 150 when the field database's value
    /// is 1 or 2 and 300 otherwise; unknown is taken as 150, so "in reach" is never said too early.</summary>
    public static double? ReachLimit(FieldKind kind, int? doorReachValue) => kind switch
    {
        FieldKind.Door => doorReachValue is null or 1 or 2 ? NearDoorReach : FarReach,
        FieldKind.Chest => FarReach,
        _ => null,
    };

    private static NearbyThing Place(FieldView view, FieldUnit unit)
    {
        var dx = unit.X - view.X;
        var dz = unit.Z - view.Z;
        var distance = Math.Sqrt(dx * dx + dz * dz);
        var bearing = Normalised(Math.Atan2(dx, dz) - view.Heading);
        var inReach = ReachLimit(unit.Kind, view.DoorReachValue) is { } limit && distance < limit &&
            Math.Abs(unit.Y - view.Y) < HeightReach && Math.Abs(bearing) < FacingReach;
        return new(unit, distance, bearing, inReach);
    }

    private static double Normalised(double angle)
    {
        var turned = Math.IEEERemainder(angle, 2 * Math.PI);
        return turned <= -Math.PI ? turned + 2 * Math.PI : turned;
    }
}
