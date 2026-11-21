using System.Numerics;

namespace Metaphor.Core.Navigation;

/// <summary>One cell of a <see cref="WalkGraph"/>: a tile's index in <see cref="NavMeshFile.Tiles"/> and the cell's
/// column and row in that tile.</summary>
public readonly record struct NavCell(int Tile, int X, int Y);

/// <summary>What a bounded search found.</summary>
public enum Reachability
{
    /// <summary>A chain of linked cells joins the two.</summary>
    Reachable,

    /// <summary>Every cell linked to the start was visited and none was the goal.</summary>
    Unreachable,

    /// <summary>The search reached its cell limit first.</summary>
    GaveUp,
}

/// <summary>The walkable cells of one <see cref="NavMeshFile"/> as a graph (#179), for guidance ("is there a path",
/// which way first) and wall tones (how far to the edge along a heading). Pure and bounded: every search takes a cell
/// limit, and nothing calls the game. Directions are Recast's: 0 is −x, 1 is +z, 2 is +x, 3 is −z, and tile columns
/// run along x and rows along z (docs/raw-sources/fastlz-and-recast-layer-cells-2026-11-21.md).
/// <para>Links: a cell's own bits give its neighbours in the same tile, in its layer or (the high four) another. A
/// walkable neighbour in the next tile sets no bit upstream, so across a tile edge, and for another layer's
/// neighbour, this links the walkable cell there whose floor is nearest and within the agent's climb. That rule is
/// ours, not read from the game [NEEDS VERIFICATION: by ear, against walls the player bumps].</para>
/// <para>What the file can't hold: obstacles the game adds at run time (gates, gimmicks), so "reachable" may be wrong
/// behind a closed gate, and the cells are where an agent's centre may stand, so a ledge reads as an edge.</para>
/// </summary>
public sealed class WalkGraph
{
    /// <summary>The cell limit a search takes unless told otherwise.</summary>
    public const int DefaultMaxVisited = 200_000;

    /// <summary>The farthest <see cref="DistanceToEdge"/> looks, in world units.</summary>
    public const float MaxEdgeDistance = 100_000;

    private const int Walls = 4;
    private const byte NullArea = 0;
    private const float MaxCoordinate = 1e7f;

    private static readonly int[] OffsetX = [-1, 0, 1, 0];
    private static readonly int[] OffsetZ = [0, 1, 0, -1];

    private readonly NavMeshFile file;
    private readonly (int Column, int Row)[] tileOrigins;
    private readonly Dictionary<(int, int), List<int>> tilesAt = [];
    private readonly float climb;

    /// <summary>The graph of <paramref name="file"/>'s tiles. A tile whose bounds don't fit the file's grid is left out.
    /// </summary>
    public WalkGraph(NavMeshFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        this.file = file;
        climb = float.IsFinite(file.WalkableClimb) && file.WalkableClimb > file.CellHeight
            ? file.WalkableClimb
            : file.CellHeight;
        tileOrigins = new (int, int)[file.Tiles.Count];
        for (var i = 0; i < file.Tiles.Count; i++)
        {
            var tile = file.Tiles[i];
            if (Column(tile.Min.X, file.Origin.X, round: true) is not { } column ||
                Column(tile.Min.Z, file.Origin.Z, round: true) is not { } row)
            {
                tileOrigins[i] = (int.MinValue, int.MinValue);
                continue;
            }
            tileOrigins[i] = (column, row);
            var key = (FloorDiv(column, file.TileCells), FloorDiv(row, file.TileCells));
            if (!tilesAt.TryGetValue(key, out var list))
            {
                tilesAt[key] = list = [];
            }
            list.Add(i);
        }
    }

    /// <summary>The file this graph reads.</summary>
    public NavMeshFile File => file;

    /// <summary>The walkable cell under <paramref name="position"/> whose floor is nearest its height and within
    /// <paramref name="maxVertical"/> of it, or null when there is none (off the mesh, or inside the eroded border
    /// along a wall).</summary>
    public NavCell? CellAt(Vector3 position, float maxVertical)
    {
        if (Column(position.X, file.Origin.X, round: false) is not { } column ||
            Column(position.Z, file.Origin.Z, round: false) is not { } row)
        {
            return null;
        }
        return Nearest(column, row, position.Y, maxVertical, except: -1);
    }

    /// <summary>The cell <see cref="CellAt"/> gives, or failing that the nearest walkable cell within
    /// <paramref name="radiusCells"/> cells across (searched in rings outward, at most 32), or null.</summary>
    public NavCell? NearestCell(Vector3 position, float maxVertical, int radiusCells)
    {
        if (Column(position.X, file.Origin.X, round: false) is not { } column ||
            Column(position.Z, file.Origin.Z, round: false) is not { } row)
        {
            return null;
        }
        for (var r = 0; r <= Math.Clamp(radiusCells, 0, 32); r++)
        {
            NavCell? best = null;
            var bestDistance = float.MaxValue;
            for (var dz = -r; dz <= r; dz++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r ||
                        Nearest(column + dx, row + dz, position.Y, maxVertical, except: -1) is not { } cell)
                    {
                        continue;
                    }
                    var distance = Vector2.DistanceSquared(Flat(Centre(cell)), new(position.X, position.Z));
                    if (distance < bestDistance)
                    {
                        (best, bestDistance) = (cell, distance);
                    }
                }
            }
            if (best is not null)
            {
                return best;
            }
        }
        return null;
    }

    /// <summary>The middle of <paramref name="cell"/>'s floor, in world units.</summary>
    public Vector3 Centre(NavCell cell)
    {
        var (column, row) = tileOrigins[cell.Tile];
        return new(file.Origin.X + ((column + cell.X + 0.5f) * file.CellSize), Floor(cell),
            file.Origin.Z + ((row + cell.Y + 0.5f) * file.CellSize));
    }

    /// <summary>True when <paramref name="cell"/> names a cell of this graph that can be walked on.</summary>
    public bool IsWalkable(NavCell cell) =>
        cell.Tile >= 0 && cell.Tile < file.Tiles.Count && tileOrigins[cell.Tile].Column != int.MinValue &&
        file.Tiles[cell.Tile].Contains(cell.X, cell.Y) && file.Tiles[cell.Tile].Area(cell.X, cell.Y) != NullArea;

    /// <summary>The cell linked to <paramref name="cell"/> in <paramref name="direction"/> (0 to 3), or null when that
    /// side is an edge.</summary>
    public NavCell? Neighbour(NavCell cell, int direction)
    {
        if (!IsWalkable(cell) || direction is < 0 or >= Walls)
        {
            return null;
        }
        var tile = file.Tiles[cell.Tile];
        var links = tile.Links(cell.X, cell.Y);
        var x = cell.X + OffsetX[direction];
        var y = cell.Y + OffsetZ[direction];
        if ((links & (1 << direction)) != 0 && tile.Contains(x, y))
        {
            var same = new NavCell(cell.Tile, x, y);
            return IsWalkable(same) ? same : null;
        }
        var inOtherLayer = (links & (0x10 << direction)) != 0;
        if (!inOtherLayer && tile.Contains(x, y))
        {
            return null;
        }
        var (column, row) = tileOrigins[cell.Tile];
        return Nearest(column + x, row + y, Floor(cell), climb, except: tile.Contains(x, y) ? cell.Tile : -1);
    }

    /// <summary>Every cell linked to <paramref name="cell"/>, with the direction it lies in.</summary>
    public IEnumerable<(int Direction, NavCell Cell)> Neighbours(NavCell cell)
    {
        for (var direction = 0; direction < Walls; direction++)
        {
            if (Neighbour(cell, direction) is { } next)
            {
                yield return (direction, next);
            }
        }
    }

    /// <summary>Whether a chain of linked cells joins <paramref name="from"/> to <paramref name="to"/>, visiting at most
    /// <paramref name="maxVisited"/> cells.</summary>
    public Reachability IsReachable(NavCell from, NavCell to, int maxVisited = DefaultMaxVisited) =>
        Search(from, to, maxVisited).Outcome;

    /// <summary>The shortest chain of linked cells from <paramref name="from"/> to <paramref name="to"/>, both included,
    /// or null when there is none or the search gave up after <paramref name="maxVisited"/> cells.</summary>
    public IReadOnlyList<NavCell>? Path(NavCell from, NavCell to, int maxVisited = DefaultMaxVisited)
    {
        var (outcome, parents) = Search(from, to, maxVisited);
        if (outcome != Reachability.Reachable)
        {
            return null;
        }
        var path = new List<NavCell> { to };
        for (var at = to; at != from; at = parents[at])
        {
            path.Add(parents[at]);
        }
        path.Reverse();
        return path;
    }

    /// <summary>The direction, as a unit vector in the ground plane (x, z), of the first step on the shortest path from
    /// <paramref name="from"/> to <paramref name="to"/>, or null when there is no path, the search gave up, or the two
    /// are the same cell.</summary>
    public Vector2? FirstStep(NavCell from, NavCell to, int maxVisited = DefaultMaxVisited) =>
        Path(from, to, maxVisited) is { Count: > 1 } path
            ? Vector2.Normalize(Flat(Centre(path[1])) - Flat(Centre(path[0])))
            : null;

    /// <summary>How far, in world units, from <paramref name="position"/> along <paramref name="heading"/> (x, z) the
    /// walk leaves the linked cells: the distance to the first side with no link. That is
    /// <paramref name="maxDistance"/> (at most <see cref="MaxEdgeDistance"/>) when no edge comes sooner, and null when
    /// the position isn't on a walkable cell within <see cref="NavMeshFile.WalkableHeight"/> or the heading is zero.
    /// The cells are shrunk by the agent's radius, so the wall itself is about <see cref="NavMeshFile.WalkableRadius"/>
    /// farther [NEEDS VERIFICATION].</summary>
    public float? DistanceToEdge(Vector3 position, Vector2 heading, float maxDistance)
    {
        var length = heading.Length();
        if (!float.IsFinite(length) || length < 1e-6f || !float.IsFinite(maxDistance) || maxDistance < 0 ||
            CellAt(position, MaxVertical()) is not { } start)
        {
            return null;
        }
        var limit = Math.Min(maxDistance, MaxEdgeDistance);
        var direction = heading / length;
        // A grid walk (Amanatides and Woo) in cell units from the position: tNext is how far along the heading, in
        // cells, the next column or row boundary is.
        var u = (position.X - file.Origin.X) / file.CellSize;
        var v = (position.Z - file.Origin.Z) / file.CellSize;
        var stepX = direction.X > 0 ? 1 : -1;
        var stepZ = direction.Y > 0 ? 1 : -1;
        var nextX = Boundary(u, direction.X);
        var nextZ = Boundary(v, direction.Y);
        var deltaX = direction.X == 0 ? float.PositiveInfinity : 1 / Math.Abs(direction.X);
        var deltaZ = direction.Y == 0 ? float.PositiveInfinity : 1 / Math.Abs(direction.Y);
        var cell = start;
        var steps = (int)Math.Ceiling(limit / file.CellSize * 2) + 2;
        for (var i = 0; i < steps; i++)
        {
            var alongX = nextX <= nextZ;
            var t = alongX ? nextX : nextZ;
            if (t * file.CellSize >= limit)
            {
                return limit;
            }
            var side = alongX ? (stepX > 0 ? 2 : 0) : (stepZ > 0 ? 1 : 3);
            if (Neighbour(cell, side) is not { } next)
            {
                return t * file.CellSize;
            }
            cell = next;
            if (alongX)
            {
                nextX += deltaX;
            }
            else
            {
                nextZ += deltaZ;
            }
        }
        return limit;
    }

    private (Reachability Outcome, Dictionary<NavCell, NavCell> Parents) Search(NavCell from, NavCell to, int maxVisited)
    {
        var parents = new Dictionary<NavCell, NavCell>();
        if (!IsWalkable(from) || !IsWalkable(to))
        {
            return (Reachability.Unreachable, parents);
        }
        parents[from] = from;
        var queue = new Queue<NavCell>();
        queue.Enqueue(from);
        while (queue.TryDequeue(out var cell))
        {
            if (cell == to)
            {
                return (Reachability.Reachable, parents);
            }
            foreach (var (_, next) in Neighbours(cell))
            {
                if (parents.ContainsKey(next))
                {
                    continue;
                }
                if (parents.Count >= maxVisited)
                {
                    return (Reachability.GaveUp, parents);
                }
                parents[next] = cell;
                queue.Enqueue(next);
            }
        }
        return (Reachability.Unreachable, parents);
    }

    // The walkable cell at global column and row whose floor is nearest height and within maxVertical, skipping tile
    // except (the cell's own layer, when looking for another).
    private NavCell? Nearest(int column, int row, float height, float maxVertical, int except)
    {
        if (!tilesAt.TryGetValue((FloorDiv(column, file.TileCells), FloorDiv(row, file.TileCells)), out var list))
        {
            return null;
        }
        NavCell? best = null;
        var bestGap = maxVertical;
        foreach (var index in list)
        {
            var (originColumn, originRow) = tileOrigins[index];
            var cell = new NavCell(index, column - originColumn, row - originRow);
            if (index == except || !IsWalkable(cell))
            {
                continue;
            }
            var gap = Math.Abs(Floor(cell) - height);
            if (gap <= bestGap)
            {
                (best, bestGap) = (cell, gap);
            }
        }
        return best;
    }

    private float Floor(NavCell cell)
    {
        var tile = file.Tiles[cell.Tile];
        return tile.Min.Y + (tile.FloorHeight(cell.X, cell.Y) * file.CellHeight);
    }

    private float MaxVertical() => float.IsFinite(file.WalkableHeight) && file.WalkableHeight > 0
        ? file.WalkableHeight
        : file.CellHeight;

    // How far along the heading, in cells, the next boundary after coordinate c lies.
    private static float Boundary(float c, float d) => d switch
    {
        > 0 => (MathF.Floor(c) + 1 - c) / d,
        < 0 => (c - MathF.Floor(c)) / -d,
        _ => float.PositiveInfinity,
    };

    // The global cell column of coordinate value against origin, or null when it is out of range.
    private int? Column(float value, float origin, bool round)
    {
        var cells = (value - origin) / file.CellSize;
        if (!float.IsFinite(cells) || Math.Abs(cells) > MaxCoordinate)
        {
            return null;
        }
        return (int)(round ? MathF.Round(cells) : MathF.Floor(cells));
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);
}
