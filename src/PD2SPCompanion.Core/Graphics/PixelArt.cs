namespace PD2SPCompanion.Core.Graphics;

/// <summary>A picture as BGRA pixels, row by row.</summary>
public sealed record Picture(int Width, int Height, uint[] Bgra);

/// <summary>
/// What is done to the game's pictures, pixel by pixel - one place for it (the frame's recipes had
/// grown a toolbox of their own): enlarging, cutting and turning, joining, blending, toning, and the yes-or-no
/// masks a shape is told with. Each takes a picture and gives a new one; none reads the game's files - the
/// recipes that do are FrameArt (the window frame) and FrontEnd (the logo).
/// </summary>
public static class PixelArt
{
    // ---- enlarging ---------------------------------------------------------------------------------------
    // The game's sprites on a screen many times their size: scaled plainly they turn to blocks, smoothed plainly
    // to blur; Scale2x (Andrea Mazzoleni's, also known as EPX) doubles a sprite while rounding the stair-steps
    // of its edges - each pixel becomes four, and a corner takes its neighbours' colour where two of them agree
    // across it. A page then scales the result down smoothly to the size it wants, so the edges come out soft
    // and the inside stays sharp.

    /// <summary>The picture twice the size, each way; transparency is a colour like any other.</summary>
    public static uint[] Scale2x(uint[] pixels, int width, int height)
    {
        var output = new uint[width * 2 * height * 2];
        int stride = width * 2;
        for (int y = 0; y < height; y++)
        {
            int up = Math.Max(y - 1, 0), down = Math.Min(y + 1, height - 1);
            for (int x = 0; x < width; x++)
            {
                int left = Math.Max(x - 1, 0), right = Math.Min(x + 1, width - 1);
                uint p = pixels[y * width + x];
                uint a = pixels[up * width + x];      // above
                uint b = pixels[y * width + right];   // right
                uint c = pixels[y * width + left];    // left
                uint d = pixels[down * width + x];    // below

                uint e0 = p, e1 = p, e2 = p, e3 = p;
                if (a != d && c != b)
                {
                    if (c == a) e0 = a;
                    if (a == b) e1 = b;
                    if (c == d) e2 = c;
                    if (d == b) e3 = d;
                }

                int at = y * 2 * stride + x * 2;
                output[at] = e0;
                output[at + 1] = e1;
                output[at + stride] = e2;
                output[at + stride + 1] = e3;
            }
        }
        return output;
    }

    /// <summary>Scale2x as many times as asked: 1 doubles, 2 makes it four times the size.</summary>
    public static uint[] Scale2x(uint[] pixels, int width, int height, int times)
    {
        for (int i = 0; i < times; i++)
        {
            pixels = Scale2x(pixels, width, height);
            width *= 2;
            height *= 2;
        }
        return pixels;
    }

    // ---- cutting and turning ---------------------------------------------------------------------------------

    /// <summary>A rectangle of the picture, from its left and top, its width and height.</summary>
    public static Picture Window(Picture picture, int left, int top, int width, int height)
    {
        var cut = new uint[width * height];
        for (int y = 0; y < height; y++)
            Array.Copy(picture.Bgra, (top + y) * picture.Width + left, cut, y * width, width);
        return new Picture(width, height, cut);
    }

    /// <summary>The picture upside down.</summary>
    public static Picture FlipUpsideDown(Picture picture)
    {
        var flipped = new uint[picture.Bgra.Length];
        for (int y = 0; y < picture.Height; y++)
            Array.Copy(picture.Bgra, y * picture.Width, flipped, (picture.Height - 1 - y) * picture.Width, picture.Width);
        return picture with { Bgra = flipped };
    }

    /// <summary>The picture turned a quarter clockwise: as wide as it was tall.</summary>
    public static Picture TurnClockwise(Picture picture)
    {
        int width = picture.Height, height = picture.Width;
        var turned = new uint[picture.Bgra.Length];
        for (int y = 0; y < picture.Height; y++)
            for (int x = 0; x < picture.Width; x++)
                turned[x * width + (width - 1 - y)] = picture.Bgra[y * picture.Width + x];
        return new Picture(width, height, turned);
    }

    // ---- joining ---------------------------------------------------------------------------------------------

    /// <summary>A DC6's frames put together as its panel: <paramref name="columns"/> to a row, left to right, top down.</summary>
    public static Picture Grid(IReadOnlyList<Dc6Frame> frames, int columns, Palette palette)
    {
        int width = frames.Take(columns).Sum(f => f.Width);
        int height = frames.Where((_, i) => i % columns == 0).Sum(f => f.Height);
        var canvas = new uint[width * height];
        for (int i = 0, top = 0; i < frames.Count; i += columns)
        {
            for (int c = 0, left = 0; c < columns && i + c < frames.Count; c++)
            {
                var frame = frames[i + c];
                var pixels = frame.ToBgra(palette);
                for (int y = 0; y < frame.Height; y++)
                    Array.Copy(pixels, y * frame.Width, canvas, (top + y) * width + left, frame.Width);
                left += frame.Width;
            }
            top += frames[i].Height;
        }
        return new Picture(width, height, canvas);
    }

    /// <summary>One picture over another of the same width, the last <paramref name="fade"/> rows of the first fading into the second's first.</summary>
    public static Picture Stacked(Picture top, Picture bottom, int fade)
    {
        int width = top.Width, height = top.Height + bottom.Height - fade;
        var stacked = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                stacked[y * width + x] = y < top.Height - fade ? top.Bgra[y * width + x]
                    : y >= top.Height ? bottom.Bgra[(y - top.Height + fade) * width + x]
                    : Mix(top.Bgra[y * width + x], bottom.Bgra[(y - top.Height + fade) * width + x],
                          (double)(y - top.Height + fade) / fade);
            }
        }
        return new Picture(width, height, stacked);
    }

    /// <summary>
    /// The picture made to tile: <paramref name="fade"/> columns and rows shorter, its first ones faded from what
    /// followed its last, so its right edge runs on into its left and its bottom into its top.
    /// </summary>
    public static Picture Seamless(Picture picture, int fade)
    {
        int width = picture.Width - fade, height = picture.Height - fade;
        var across = new uint[width * picture.Height];
        for (int y = 0; y < picture.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint own = picture.Bgra[y * picture.Width + x];
                across[y * width + x] = x >= fade ? own
                    : Mix(picture.Bgra[y * picture.Width + width + x], own, (double)x / fade);
            }
        }

        var tile = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint own = across[y * width + x];
                tile[y * width + x] = y >= fade ? own : Mix(across[(height + y) * width + x], own, (double)y / fade);
            }
        }
        return new Picture(width, height, tile);
    }

    /// <summary>
    /// <paramref name="copies"/> copies side by side, every other one mirrored, as one picture - so they meet in exact
    /// pixels. Laid as a brush's tiles they met in a dark pair of columns at every join, whatever the tile's size or
    /// scaling (measured); one long strip has no joins on screen.
    /// </summary>
    public static Picture MirroredAcross(Picture picture, int copies)
    {
        int width = picture.Width * copies;
        var strip = new uint[width * picture.Height];
        for (int copy = 0; copy < copies; copy++)
        {
            bool mirrored = copy % 2 == 1;
            for (int y = 0; y < picture.Height; y++)
                for (int x = 0; x < picture.Width; x++)
                    strip[y * width + copy * picture.Width + x] =
                        picture.Bgra[y * picture.Width + (mirrored ? picture.Width - 1 - x : x)];
        }
        return new Picture(width, picture.Height, strip);
    }

    /// <summary>The same, the copies one under another, every other one upside down.</summary>
    public static Picture MirroredDown(Picture picture, int copies)
    {
        var strip = new uint[picture.Bgra.Length * copies];
        for (int copy = 0; copy < copies; copy++)
        {
            bool mirrored = copy % 2 == 1;
            for (int y = 0; y < picture.Height; y++)
                Array.Copy(picture.Bgra, (mirrored ? picture.Height - 1 - y : y) * picture.Width,
                           strip, (copy * picture.Height + y) * picture.Width, picture.Width);
        }
        return new Picture(picture.Width, picture.Height * copies, strip);
    }

    // ---- blending and toning ---------------------------------------------------------------------------------

    /// <summary>
    /// Two pixels blended, channel by channel: <paramref name="firstShare"/> of the first and <paramref name="secondShare"/>
    /// of the second, rounded and held at white; opaque. The one blend the art is made with -
    /// a mix is shares that sum to one, the fire over the logo is one and one.
    /// </summary>
    public static uint Blend(uint first, double firstShare, uint second, double secondShare)
    {
        uint Channel(int shift) =>
            (uint)Math.Min(255, Math.Round(firstShare * ((first >> shift) & 0xFF) + secondShare * ((second >> shift) & 0xFF))) << shift;
        return 0xFF000000 | Channel(16) | Channel(8) | Channel(0);
    }

    /// <summary>Two opaque pixels mixed, <paramref name="share"/> of the second.</summary>
    public static uint Mix(uint first, uint second, double share) => Blend(first, 1 - share, second, share);

    /// <summary>Every colour channel put through <paramref name="curve"/>, as a curves layer would; alpha kept.</summary>
    public static Picture Toned(Picture picture, Func<int, int> curve)
    {
        var table = new uint[256];
        for (int v = 0; v < 256; v++)
            table[v] = (uint)Math.Clamp(curve(v), 0, 255);

        var toned = new uint[picture.Bgra.Length];
        for (int i = 0; i < toned.Length; i++)
        {
            uint pixel = picture.Bgra[i];
            toned[i] = (pixel & 0xFF000000) | table[(pixel >> 16) & 0xFF] << 16 | table[(pixel >> 8) & 0xFF] << 8 | table[pixel & 0xFF];
        }
        return new Picture(picture.Width, picture.Height, toned);
    }

    /// <summary>A curve through the points given, straight between them.</summary>
    public static int Through(int value, (int In, int Out)[] points)
    {
        for (int i = 1; i < points.Length; i++)
        {
            var (from, to) = (points[i - 1], points[i]);
            if (value <= to.In)
                return (int)Math.Round(from.Out + (double)(value - from.In) * (to.Out - from.Out) / Math.Max(1, to.In - from.In));
        }
        return points[^1].Out;
    }

    /// <summary>Each row's colour scaled from <paramref name="top"/> on the first row to <paramref name="bottom"/> on the last, evenly between.</summary>
    public static Picture Shaded(Picture picture, double top, double bottom)
    {
        var shaded = new uint[picture.Bgra.Length];
        for (int y = 0; y < picture.Height; y++)
        {
            double gain = top + (bottom - top) * y / Math.Max(1, picture.Height - 1);
            int Scaled(int channel) => Math.Min(255, (int)Math.Round(channel * gain));
            var curve = Enumerable.Range(0, 256).Select(Scaled).ToArray();
            for (int x = 0; x < picture.Width; x++)
            {
                uint pixel = picture.Bgra[y * picture.Width + x];
                shaded[y * picture.Width + x] = (pixel & 0xFF000000) | (uint)curve[(pixel >> 16) & 0xFF] << 16
                                                | (uint)curve[(pixel >> 8) & 0xFF] << 8 | (uint)curve[pixel & 0xFF];
            }
        }
        return new Picture(picture.Width, picture.Height, shaded);
    }

    /// <summary>Every channel of every pixel moved toward the picture's average by what <paramref name="grain"/> leaves out.</summary>
    public static Picture Softened(Picture picture, double grain)
    {
        double[] average = new double[3];
        foreach (uint pixel in picture.Bgra)
            for (int c = 0; c < 3; c++)
                average[c] += (pixel >> (c * 8)) & 0xFF;
        for (int c = 0; c < 3; c++)
            average[c] /= picture.Bgra.Length;

        var softened = new uint[picture.Bgra.Length];
        for (int i = 0; i < softened.Length; i++)
        {
            uint pixel = picture.Bgra[i], result = 0xFF000000;
            for (int c = 0; c < 3; c++)
                result |= (uint)Math.Clamp(Math.Round(average[c] + grain * (((pixel >> (c * 8)) & 0xFF) - average[c])), 0, 255) << (c * 8);
            softened[i] = result;
        }
        return new Picture(picture.Width, picture.Height, softened);
    }

    // ---- masks: a shape as yes or no for every pixel, row by row ---------------------------------------------

    /// <summary>Each set cell and its eight neighbours set.</summary>
    public static bool[] Dilated(bool[] cells, int width, int height) => Neighbours(cells, width, height, any: true);

    /// <summary>Each cell kept only where it and its eight neighbours are set; past the edge counts as unset.</summary>
    public static bool[] Eroded(bool[] cells, int width, int height) => Neighbours(cells, width, height, any: false);

    private static bool[] Neighbours(bool[] cells, int width, int height, bool any)
    {
        var result = new bool[cells.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool hit = !any;
                for (int dy = -1; dy <= 1 && hit != any; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        bool set = nx >= 0 && ny >= 0 && nx < width && ny < height && cells[ny * width + nx];
                        if (set == any)
                        {
                            hit = any;
                            break;
                        }
                    }
                }
                result[y * width + x] = hit;
            }
        }
        return result;
    }

    /// <summary>Every cell not in <paramref name="walls"/> reached from the edges, side to side and up and down.</summary>
    public static bool[] Flooded(bool[] walls, int width, int height)
    {
        var reached = new bool[walls.Length];
        var queue = new Queue<int>();
        void Enter(int x, int y)
        {
            int at = y * width + x;
            if (!walls[at] && !reached[at])
            {
                reached[at] = true;
                queue.Enqueue(at);
            }
        }
        for (int x = 0; x < width; x++)
        {
            Enter(x, 0);
            Enter(x, height - 1);
        }
        for (int y = 0; y < height; y++)
        {
            Enter(0, y);
            Enter(width - 1, y);
        }
        while (queue.Count > 0)
        {
            int at = queue.Dequeue(), x = at % width, y = at / width;
            if (x > 0) Enter(x - 1, y);
            if (x < width - 1) Enter(x + 1, y);
            if (y > 0) Enter(x, y - 1);
            if (y < height - 1) Enter(x, y + 1);
        }
        return reached;
    }

    /// <summary>The largest group of set cells touching, corners included; the rest unset.</summary>
    public static bool[] Largest(bool[] cells, int width, int height)
    {
        var group = new int[cells.Length];
        int best = 0, bestSize = 0, next = 0;
        var queue = new Queue<int>();
        for (int start = 0; start < cells.Length; start++)
        {
            if (!cells[start] || group[start] != 0)
                continue;
            int id = ++next, size = 0;
            group[start] = id;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue(), x = at % width, y = at / width;
                size++;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy, near = ny * width + nx;
                        if (nx >= 0 && ny >= 0 && nx < width && ny < height && cells[near] && group[near] == 0)
                        {
                            group[near] = id;
                            queue.Enqueue(near);
                        }
                    }
                }
            }
            if (size > bestSize)
                (best, bestSize) = (id, size);
        }
        return [.. group.Select(g => g == best && best != 0)];
    }
}
