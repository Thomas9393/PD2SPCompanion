namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// One decoded DCC frame: 8-bit palette indices, row-major, top-down. <see cref="Left"/> and
/// <see cref="Top"/> place its top-left pixel against the unit's anchor (its feet), which is how the
/// layers of one figure line up.
/// </summary>
public sealed record DccFrame(int Left, int Top, int Width, int Height, byte[] Indices);

/// <summary>
/// Reader for Diablo II's DCC format - the animated graphics of characters and monsters, one file per
/// body part (head, torso, right arm...) per armour weight per animation. Every direction is a bit
/// stream of seven sub-streams, and its frames are cut into 4x4 cells that a frame either repeats from
/// the frame before ("equal cell") or redraws from a cell-sized palette of up to four colours.
/// <para>
/// Decoded in the order the community's notes on the format give (Paul Siramy's DCC write-up, and
/// OpenDiablo2's decoder that follows it): first every frame's cells into a pixel buffer of four
/// colours each, then the frames themselves out of that buffer. Optional per-frame data and bottom-up
/// frames - neither in the character files - are refused rather than guessed at.
/// </para>
/// </summary>
public static class Dcc
{
    /// <summary>A field's width in bits, by the four-bit code a direction stores.</summary>
    private static readonly int[] FieldBits = [0, 1, 2, 4, 6, 8, 10, 12, 14, 16, 20, 24, 26, 28, 30, 32];

    /// <summary>How many directions the file holds.</summary>
    public static int Directions(byte[] data)
    {
        Check(data);
        return data[2];
    }

    /// <summary>Every frame of one direction.</summary>
    public static IReadOnlyList<DccFrame> Read(byte[] data, int direction)
    {
        Check(data);
        int directions = data[2];
        int framesPerDirection = BitConverter.ToInt32(data, 3);
        if (direction < 0 || direction >= directions)
            throw new ArgumentOutOfRangeException(nameof(direction), $"the file has {directions} directions");
        if (framesPerDirection <= 0 || framesPerDirection > 1024)
            throw new InvalidDataException($"DCC claims {framesPerDirection} frames a direction, which is not plausible.");

        int start = BitConverter.ToInt32(data, 15 + direction * 4);
        int end = direction + 1 < directions ? BitConverter.ToInt32(data, 15 + (direction + 1) * 4) : data.Length;
        if (start <= 0 || end > data.Length || end <= start)
            throw new InvalidDataException($"DCC direction {direction} runs {start}..{end} in a {data.Length}-byte file.");

        return new Direction(data, start, end, framesPerDirection).Decode();
    }

    private static void Check(byte[] data)
    {
        // Signature 0x74, version 6; then directions (byte), frames a direction, a tag, the size a DC6 of it would take.
        if (data.Length < 19 || data[0] != 0x74)
            throw new InvalidDataException("not a DCC file (no 0x74 signature).");
    }

    /// <summary>A little-endian bit stream: the lowest bit of each byte first.</summary>
    private sealed class Bits(byte[] data, long start, long end, string name = "direction")
    {
        private readonly long _start = start;
        private long _at = start;

        public long At => _at;

        public uint Read(int count)
        {
            uint value = 0;
            for (int i = 0; i < count; i++, _at++)
            {
                if (_at >= end)
                    throw new InvalidDataException($"DCC {name} stream read past its end ({end - _start} bits).");
                value |= (uint)((data[_at >> 3] >> (int)(_at & 7)) & 1) << i;
            }
            return value;
        }

        public int Signed(int count)
        {
            if (count == 0)
                return 0;
            long value = Read(count);
            return (int)((value & (1L << (count - 1))) != 0 ? value - (1L << count) : value);
        }

        /// <summary>The next <paramref name="count"/> bits as a stream of their own, skipped here.</summary>
        public Bits Take(long count, string what)
        {
            if (_at + count > end)
                throw new InvalidDataException("DCC sub-stream runs past the direction.");
            var taken = new Bits(data, _at, _at + count, what);
            _at += count;
            return taken;
        }

        public Bits Rest(string what) => Take(end - _at, what);
    }

    private sealed record Box(int Left, int Top, int Width, int Height)
    {
        public int Right => Left + Width;
        public int Bottom => Top + Height;
    }

    /// <summary>A cell's four colours (pixel-value codes first, palette indices once mapped), and whose cell it is.</summary>
    private sealed class Entry
    {
        public readonly int[] Colours = new int[4];
        public int Frame;
        public int Cell;
    }

    private sealed class Direction(byte[] data, int start, int end, int frames)
    {
        private Box[] _boxes = [];
        private Box _box = new(0, 0, 0, 0);
        private int _cellsAcross;
        private Bits _equal = null!, _mask = null!, _encoding = null!, _raw = null!, _codes = null!;
        private bool _hasEqual, _hasEncoding;
        private readonly List<int> _values = [];
        private readonly List<Entry> _entries = [];

        public IReadOnlyList<DccFrame> Decode()
        {
            var bits = new Bits(data, (long)start * 8, (long)end * 8);
            ReadHeader(bits);
            if (_box.Width <= 0 || _box.Height <= 0)
                return [.. _boxes.Select(b => new DccFrame(b.Left, b.Top, 0, 0, []))];

            _cellsAcross = 1 + (_box.Width - 1) / 4;
            FillPixelBuffer();
            foreach (var entry in _entries)
                for (int i = 0; i < 4; i++)
                    entry.Colours[i] = entry.Colours[i] < _values.Count ? _values[entry.Colours[i]] : 0;
            return MakeFrames();
        }

        private void ReadHeader(Bits bits)
        {
            bits.Read(32); // the decoded size
            int compression = (int)bits.Read(2);
            int variable0 = FieldBits[bits.Read(4)];
            int width = FieldBits[bits.Read(4)];
            int height = FieldBits[bits.Read(4)];
            int x = FieldBits[bits.Read(4)];
            int y = FieldBits[bits.Read(4)];
            int optional = FieldBits[bits.Read(4)];
            int coded = FieldBits[bits.Read(4)];

            _boxes = new Box[frames];
            for (int f = 0; f < frames; f++)
            {
                bits.Read(variable0);
                int w = (int)bits.Read(width);
                int h = (int)bits.Read(height);
                int left = bits.Signed(x);
                int bottom = bits.Signed(y);
                if (bits.Read(optional) != 0)
                    throw new NotSupportedException("DCC frame carries optional data; not handled.");
                bits.Read(coded);
                if (bits.Read(1) != 0)
                    throw new NotSupportedException("DCC frame is stored bottom-up; not handled.");
                // The offset names the frame's bottom row; its top is the height above it.
                _boxes[f] = new Box(left, bottom - h + 1, w, h);
            }

            var drawn = _boxes.Where(b => b.Width > 0 && b.Height > 0).ToList();
            if (drawn.Count > 0)
            {
                int l = drawn.Min(b => b.Left), t = drawn.Min(b => b.Top);
                _box = new Box(l, t, drawn.Max(b => b.Right) - l, drawn.Max(b => b.Bottom) - t);
            }

            _hasEqual = (compression & 2) != 0;
            _hasEncoding = (compression & 1) != 0;
            long equalSize = _hasEqual ? bits.Read(20) : 0;
            long maskSize = bits.Read(20);
            long encodingSize = 0, rawSize = 0;
            if (_hasEncoding)
            {
                encodingSize = bits.Read(20);
                rawSize = bits.Read(20);
            }
            for (int i = 0; i < 256; i++)
                if (bits.Read(1) != 0)
                    _values.Add(i);

            _equal = bits.Take(equalSize, "equal cell");
            _mask = bits.Take(maskSize, "pixel mask");
            _encoding = bits.Take(encodingSize, "encoding type");
            _raw = bits.Take(rawSize, "raw pixel");
            _codes = bits.Rest("pixel code");
            _hasEqual = equalSize > 0;
            _hasEncoding = encodingSize > 0;
        }

        /// <summary>
        /// A frame's cell sizes along one axis: the first cell reaches the direction's next 4-pixel line,
        /// the rest are 4 wide, and a last cell of a single pixel joins the one before it.
        /// </summary>
        private static int[] CellSizes(int offset, int length)
        {
            if (length <= 0)
                return [];
            int first = 4 - offset % 4;
            if (length - first <= 1)
                return [length];
            int rest = length - first - 1;
            int count = 2 + rest / 4 - (rest % 4 == 0 ? 1 : 0);
            var sizes = new int[count];
            sizes[0] = first;
            for (int i = 1; i < count - 1; i++)
                sizes[i] = 4;
            sizes[count - 1] = length - first - 4 * (count - 2);
            return sizes;
        }

        // Stage one: each frame cell either repeats what its direction cell last held (an equal cell), or
        // takes a new entry of up to four colour codes - those its pixel mask names read anew, the others
        // kept from the entry before.
        private void FillPixelBuffer()
        {
            int cellsDown = 1 + (_box.Height - 1) / 4;
            var last = new Entry?[_cellsAcross * cellsDown];
            var read = new int[4];

            for (int f = 0; f < frames; f++)
            {
                var b = _boxes[f];
                int[] across = CellSizes(b.Left - _box.Left, b.Width);
                int[] down = CellSizes(b.Top - _box.Top, b.Height);
                int originX = (b.Left - _box.Left) / 4, originY = (b.Top - _box.Top) / 4;

                for (int cy = 0; cy < down.Length; cy++)
                    for (int cx = 0; cx < across.Length; cx++)
                    {
                        int cell = originX + cx + (originY + cy) * _cellsAcross;
                        var before = last[cell];
                        int mask = 0x0F;
                        if (before is not null)
                        {
                            if (_hasEqual && _equal.Read(1) != 0)
                                continue;
                            mask = (int)_mask.Read(4);
                        }

                        Array.Clear(read);
                        int previous = 0, decoded = 0, wanted = System.Numerics.BitOperations.PopCount((uint)mask);
                        // One encoding bit for the cell, not one a colour: raw codes, or steps from the last.
                        bool raw = wanted > 0 && _hasEncoding && _encoding.Read(1) != 0;
                        for (int i = 0; i < wanted; i++)
                        {
                            if (raw)
                                read[i] = (int)_raw.Read(8);
                            else
                            {
                                read[i] = previous;
                                uint step;
                                do
                                {
                                    step = _codes.Read(4);
                                    read[i] += (int)step;
                                } while (step == 15);
                            }
                            // A code equal to the one before ends the cell's list; the rest are 0.
                            if (read[i] == previous)
                            {
                                read[i] = 0;
                                break;
                            }
                            previous = read[i];
                            decoded++;
                        }

                        var entry = new Entry { Frame = f, Cell = cx + cy * across.Length };
                        int next = decoded - 1;
                        for (int i = 0; i < 4; i++)
                            entry.Colours[i] = (mask & (1 << i)) != 0
                                ? (next >= 0 ? read[next--] : 0)
                                : before!.Colours[i];
                        _entries.Add(entry);
                        last[cell] = entry;
                    }
            }
        }

        // Stage two: draw each frame on one canvas the size of the direction - a new entry's cell from
        // its colours (one colour fills it, otherwise one or two bits a pixel pick among them), an equal
        // cell copied from where its direction cell was last drawn, or cleared when the sizes differ.
        private IReadOnlyList<DccFrame> MakeFrames()
        {
            int w = _box.Width;
            var canvas = new byte[w * _box.Height];
            int cellsDown = 1 + (_box.Height - 1) / 4;
            var lastW = new int[_cellsAcross * cellsDown];
            var lastH = new int[lastW.Length];
            var lastX = new int[lastW.Length];
            var lastY = new int[lastW.Length];
            Array.Fill(lastW, -1);
            Array.Fill(lastH, -1);

            var result = new List<DccFrame>(frames);
            int next = 0;
            for (int f = 0; f < frames; f++)
            {
                var b = _boxes[f];
                int[] across = CellSizes(b.Left - _box.Left, b.Width);
                int[] down = CellSizes(b.Top - _box.Top, b.Height);

                int y0 = b.Top - _box.Top;
                for (int cy = 0; cy < down.Length; cy++)
                {
                    int x0 = b.Left - _box.Left;
                    int ch = down[cy];
                    for (int cx = 0; cx < across.Length; cx++)
                    {
                        int cw = across[cx];
                        int cell = x0 / 4 + y0 / 4 * _cellsAcross;

                        if (next < _entries.Count && _entries[next].Frame == f && _entries[next].Cell == cx + cy * across.Length)
                        {
                            var entry = _entries[next++];
                            if (entry.Colours[0] == entry.Colours[1])
                                Fill(canvas, w, x0, y0, cw, ch, (byte)entry.Colours[0]);
                            else
                            {
                                int width = entry.Colours[1] == entry.Colours[2] ? 1 : 2;
                                for (int y = 0; y < ch; y++)
                                    for (int x = 0; x < cw; x++)
                                        canvas[(y0 + y) * w + x0 + x] = (byte)entry.Colours[_codes.Read(width)];
                            }
                        }
                        else if (cw != lastW[cell] || ch != lastH[cell])
                            Fill(canvas, w, x0, y0, cw, ch, 0);
                        else if (lastX[cell] != x0 || lastY[cell] != y0)
                            for (int y = 0; y < ch; y++)
                                Array.Copy(canvas, (lastY[cell] + y) * w + lastX[cell], canvas, (y0 + y) * w + x0, cw);

                        lastW[cell] = cw;
                        lastH[cell] = ch;
                        lastX[cell] = x0;
                        lastY[cell] = y0;
                        x0 += cw;
                    }
                    y0 += ch;
                }

                var pixels = new byte[b.Width * b.Height];
                for (int y = 0; y < b.Height; y++)
                    Array.Copy(canvas, (b.Top - _box.Top + y) * w + (b.Left - _box.Left), pixels, y * b.Width, b.Width);
                result.Add(new DccFrame(b.Left, b.Top, b.Width, b.Height, pixels));
            }
            return result;
        }

        private static void Fill(byte[] canvas, int stride, int x0, int y0, int w, int h, byte value)
        {
            for (int y = 0; y < h; y++)
                Array.Fill(canvas, value, (y0 + y) * stride + x0, w);
        }
    }
}
