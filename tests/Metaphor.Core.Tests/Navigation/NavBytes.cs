using System.Buffers.Binary;
using System.Numerics;

namespace Metaphor.Core.Tests.Navigation;

/// <summary>Made-up FastLZ blocks and <c>.NAV</c> files for the tests (#179), written from the formats in
/// docs/raw-sources/fastlz-and-recast-layer-cells-2026-11-21.md and the header offsets in
/// docs/findings/field-navmesh-files-tile-cache-static.md. The encoder is ours, a slow greedy one, not FastLZ's.
/// No game data.</summary>
internal static class NavBytes
{
    /// <summary>FastLZ instructions for <paramref name="data"/> at <paramref name="level"/> (1 or 2): literal runs, and
    /// with <paramref name="matches"/> back-references to the longest earlier match of 3 bytes or more.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> data, int level = 1, bool matches = true)
    {
        var output = new List<byte>();
        var literals = new List<byte>();
        void Flush()
        {
            for (var at = 0; at < literals.Count; at += 32)
            {
                var run = Math.Min(32, literals.Count - at);
                output.Add((byte)(run - 1));
                output.AddRange(literals.GetRange(at, run));
            }
            literals.Clear();
        }
        var maxDistance = level == 1 ? 8192 : 8191 + 65535;
        var maxLength = level == 1 ? 264 : 4096;
        var ip = 0;
        while (ip < data.Length)
        {
            var (length, distance) = (0, 0);
            // The first instruction must be a literal: its first byte carries the level.
            for (var from = Math.Max(0, ip - maxDistance); matches && output.Count + literals.Count > 0 && from < ip; from++)
            {
                var n = 0;
                while (ip + n < data.Length && n < maxLength && data[from + n] == data[ip + n])
                {
                    n++;
                }
                if (n > length)
                {
                    (length, distance) = (n, ip - from);
                }
            }
            if (length < 3)
            {
                literals.Add(data[ip++]);
                continue;
            }
            Flush();
            Match(output, length, distance - 1, level);
            ip += length;
        }
        Flush();
        if (level == 2)
        {
            output[0] |= 1 << 5;
        }
        return [.. output];
    }

    // A match of length bytes from distance + 1 back: length 3 to 8 in the top three bits as length - 2, longer ones
    // as 7 there and the rest in the following byte(s), then the distance.
    private static void Match(List<byte> output, int length, int distance, int level)
    {
        var far = level == 2 && distance >= 8191;
        var high = far ? 31 : distance >> 8;
        var field = Math.Min(length - 2, 7);
        output.Add((byte)((field << 5) | high));
        if (field == 7)
        {
            var rest = length - 9;
            for (; level == 2 && rest >= 255; rest -= 255)
            {
                output.Add(255);
            }
            output.Add((byte)rest);
        }
        if (far)
        {
            var beyond = distance - 8191;
            output.Add(255);
            output.Add((byte)(beyond >> 8));
            output.Add((byte)beyond);
        }
        else
        {
            output.Add((byte)distance);
        }
    }

    /// <summary>One made-up tile layer: its place, size, and the three cell grids.</summary>
    public sealed record Layer(int Tx, int Ty, int LayerIndex, Vector3 Min, int Width, int Height, byte[] Heights,
        byte[] Areas, byte[] Links)
    {
        /// <summary>A <paramref name="size"/> × <paramref name="size"/> layer of tile (tx, ty) in a file whose origin
        /// is 0 and cell size <paramref name="cellSize"/>, every cell walkable at floor <paramref name="floor"/> and
        /// linked to every neighbour inside the tile, as upstream sets the bits.</summary>
        public static Layer Open(int tx, int ty, int size = 4, float cellSize = 10, float floor = 0, int layer = 0)
        {
            var cells = size * size;
            var links = new byte[cells];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    links[x + (y * size)] = (byte)((x > 0 ? 1 : 0) | (y < size - 1 ? 2 : 0) | (x < size - 1 ? 4 : 0) |
                        (y > 0 ? 8 : 0));
                }
            }
            return new(tx, ty, layer, new(tx * size * cellSize, floor, ty * size * cellSize), size, size,
                new byte[cells], Enumerable.Repeat((byte)63, cells).ToArray(), links);
        }

        /// <summary>This layer with cell (x, y) not walkable, and its neighbours' links to it cut.</summary>
        public Layer Blocked(int x, int y)
        {
            var areas = (byte[])Areas.Clone();
            var links = (byte[])Links.Clone();
            areas[x + (y * Width)] = 0;
            links[x + (y * Width)] = 0;
            int[] dx = [-1, 0, 1, 0];
            int[] dy = [0, 1, 0, -1];
            for (var d = 0; d < 4; d++)
            {
                var (nx, ny) = (x + dx[d], y + dy[d]);
                if (nx >= 0 && ny >= 0 && nx < Width && ny < Height)
                {
                    links[nx + (ny * Width)] &= (byte)~(1 << ((d + 2) % 4));
                }
            }
            return this with { Areas = areas, Links = links };
        }

        /// <summary>The layer's tile entry data: the 0x38-byte header and the compressed grids.</summary>
        public byte[] Entry(Func<byte[], byte[]>? compress = null)
        {
            var header = new byte[0x38];
            "RLTD"u8.CopyTo(header);
            Int(header, 4, 1);
            Int(header, 8, Tx);
            Int(header, 0xc, Ty);
            Int(header, 0x10, LayerIndex);
            Vec(header, 0x14, Min);
            Vec(header, 0x20, Min + new Vector3(Width, 0, Height));
            header[0x30] = (byte)Width;
            header[0x31] = (byte)Height;
            byte[] grids = [.. Heights, .. Areas, .. Links];
            return [.. header, .. (compress ?? (g => Compress(g)))(grids)];
        }
    }

    /// <summary>A version-3 file: the Atlus header, <paramref name="gap"/> bytes of the undescribed block, the first
    /// run of tiles (whose count the header gives), then <paramref name="trailing"/> bytes.</summary>
    public static byte[] File(IEnumerable<byte[]> entries, float cellSize = 10, float cellHeight = 1, int tileCells = 4,
        byte[]? gap = null, byte[]? trailing = null, int version = 3, int? headerTiles = null, float climb = 2)
    {
        var list = entries.ToList();
        var header = new byte[version == 3 ? 0xb0 : 0x5c];
        "TEST"u8.CopyTo(header);
        Int(header, 4, version);
        Int(header, 8, headerTiles ?? list.Count);
        Float(header, 0x34, cellSize);
        Float(header, 0x38, cellHeight);
        Int(header, 0x3c, tileCells);
        Int(header, 0x40, tileCells);
        Float(header, 0x44, 15);
        Float(header, 0x48, 5);
        Float(header, 0x4c, climb);
        var bytes = new List<byte>(header);
        bytes.AddRange(gap ?? []);
        for (var i = 0; i < list.Count; i++)
        {
            var entry = new byte[8];
            Int(entry, 0, 0x1000 + i);
            Int(entry, 4, list[i].Length);
            bytes.AddRange(entry);
            bytes.AddRange(list[i]);
        }
        bytes.AddRange(trailing ?? []);
        return [.. bytes];
    }

    private static void Int(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), v);

    private static void Float(byte[] b, int at, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(at), v);

    private static void Vec(byte[] b, int at, Vector3 v)
    {
        Float(b, at, v.X);
        Float(b, at + 4, v.Y);
        Float(b, at + 8, v.Z);
    }
}
