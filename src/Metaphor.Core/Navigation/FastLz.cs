// FastLZ's decompressor (levels 1 and 2), ported to C# from ariya/FastLZ, MIT licence, commit
// b1342dabcf5257ab303743c9332fe75e9147a011, file fastlz.c (fastlz_decompress, fastlz1_decompress, fastlz2_decompress).
// The port checks every read and write against its buffers and returns -1 instead of reading or writing past them.
// Notes: docs/raw-sources/fastlz-and-recast-layer-cells-2026-11-21.md. Its licence notice follows, as it requires
// without the author's email address after the copyright line, which the check allows only in a licence file. The
// verbatim notice, email included, is licenses/fastlz/LICENSE.MIT, which ships with the mod (ADR 0005).
//
//   FastLZ - Byte-aligned LZ77 compression library
//   Copyright (C) 2005-2020 Ariya Hidayat
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy
//   of this software and associated documentation files (the "Software"), to deal
//   in the Software without restriction, including without limitation the rights
//   to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//   copies of the Software, and to permit persons to whom the Software is
//   furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in
//   all copies or substantial portions of the Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//   THE SOFTWARE.

namespace Metaphor.Core.Navigation;

/// <summary>Decompresses FastLZ blocks, the compressor Recast's tile cache uses for each tile (#179). Bounded: the
/// output never grows past the buffer the caller gives, and malformed input gives -1 or null, never an exception.</summary>
public static class FastLz
{
    /// <summary>The largest output <see cref="Decompress(ReadOnlySpan{byte}, int)"/> allocates.</summary>
    public const int MaxOutput = 16 * 1024 * 1024;

    // fastlz.c's MAX_L2_DISTANCE: level 2's 16-bit distances count from here.
    private const int MaxL2Distance = 8191;

    /// <summary>Exactly <paramref name="expectedSize"/> bytes decompressed from <paramref name="input"/>, or null when
    /// the input is malformed, decodes to another size, or <paramref name="expectedSize"/> is outside 1 to
    /// <see cref="MaxOutput"/>.</summary>
    public static byte[]? Decompress(ReadOnlySpan<byte> input, int expectedSize)
    {
        if (expectedSize is <= 0 or > MaxOutput)
        {
            return null;
        }
        var output = new byte[expectedSize];
        return Decompress(input, output) == expectedSize ? output : null;
    }

    /// <summary>Decompresses <paramref name="input"/> into <paramref name="output"/> and returns the bytes written, or
    /// -1 when the input is empty, names a level other than 1 or 2, ends inside an instruction, refers to bytes before
    /// the output's start, or would write past <paramref name="output"/>'s end.</summary>
    public static int Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.IsEmpty)
        {
            return -1;
        }
        var level = (input[0] >> 5) + 1;
        if (level is not (1 or 2))
        {
            return -1;
        }
        var ip = 0;
        var op = 0;
        var ctrl = input[ip++] & 31;
        while (true)
        {
            if (ctrl >= 32)
            {
                var len = (ctrl >> 5) - 1;
                var ofs = (ctrl & 31) << 8;
                if (len == 7 - 1)
                {
                    int code;
                    do
                    {
                        if (ip >= input.Length || len > output.Length)
                        {
                            return -1;
                        }
                        code = input[ip++];
                        len += code;
                    }
                    while (level == 2 && code == 255);
                }
                if (ip >= input.Length)
                {
                    return -1;
                }
                var low = input[ip++];
                var distance = ofs + low;
                if (level == 2 && low == 255 && ofs == 31 << 8)
                {
                    if (ip > input.Length - 2)
                    {
                        return -1;
                    }
                    distance = ((input[ip] << 8) | input[ip + 1]) + MaxL2Distance;
                    ip += 2;
                }
                len += 3;
                var from = op - distance - 1;
                if (from < 0 || len > output.Length - op)
                {
                    return -1;
                }
                // Byte by byte, as fastlz_memmove does: a match may overlap the bytes it is writing.
                for (var i = 0; i < len; i++)
                {
                    output[op + i] = output[from + i];
                }
                op += len;
            }
            else
            {
                ctrl++;
                if (ctrl > output.Length - op || ctrl > input.Length - ip)
                {
                    return -1;
                }
                input.Slice(ip, ctrl).CopyTo(output[op..]);
                ip += ctrl;
                op += ctrl;
            }
            // Level 1 stops with fewer than two bytes left, level 2 when none are (fastlz.c lines 315 and 475).
            if (level == 1 ? ip > input.Length - 2 : ip >= input.Length)
            {
                return op;
            }
            ctrl = input[ip++];
        }
    }
}
