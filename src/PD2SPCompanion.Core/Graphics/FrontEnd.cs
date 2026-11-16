namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// The game's own front-end art, read from the player's install: the Diablo II logo, still and burning. The
/// editor ships no game art, so these
/// come from the archives, as every item picture does. (The game's loading screen, read the
/// same way, takes its palette from data\global\palette\
/// loading\pal.dat - the units, act, sky and menu palettes all paint it wrong.)
/// </summary>
public static class FrontEnd
{
    /// <summary>The logo in stone, in two halves placed by their offsets; the copy the editor shipped was this.</summary>
    public const string LogoLeft = @"data\global\ui\FrontEnd\D2LogoBlackLeft.DC6";
    public const string LogoRight = @"data\global\ui\FrontEnd\D2LogoBlackRight.DC6";

    /// <summary>The fire over the logo, in the same two halves: drawn added to what is under it, as flame is.</summary>
    public const string FireLeft = @"data\global\ui\FrontEnd\D2LogoFireLeft.DC6";
    public const string FireRight = @"data\global\ui\FrontEnd\D2LogoFireRight.DC6";

    /// <summary>
    /// The title screen's burning logo: each of its frames the stone halves with the fire's added over them,
    /// on one canvas every frame shares, so it burns in place. Meant for a black ground - the landing's header.
    /// Empty where the archives do not hold it.
    /// </summary>
    public static IReadOnlyList<Picture> BurningLogo(GameData game)
    {
        var files = new[] { LogoLeft, LogoRight, FireLeft, FireRight };
        var layers = new List<IReadOnlyList<Dc6Frame>>();
        foreach (string file in files)
        {
            if (game.Read(file) is not { } data)
                return [];
            layers.Add(Dc6.Read(data.Data));
        }

        var palette = Palette.Current ?? Palette.Load(game);
        int count = layers.Min(layer => layer.Count);
        var all = layers.SelectMany(layer => layer.Take(count)).ToList();
        int x0 = all.Min(f => f.OffsetX), x1 = all.Max(f => f.OffsetX + f.Width);
        int y0 = all.Min(f => f.OffsetY - f.Height), y1 = all.Max(f => f.OffsetY);
        int width = x1 - x0, height = y1 - y0;

        var frames = new List<Picture>(count);
        for (int n = 0; n < count; n++)
        {
            var canvas = new uint[width * height];
            for (int layer = 0; layer < layers.Count; layer++)
            {
                bool fire = layer >= 2;
                var frame = layers[layer][n];
                var pixels = frame.ToBgra(palette);
                int left0 = frame.OffsetX - x0, top0 = frame.OffsetY - frame.Height - y0;
                for (int y = 0; y < frame.Height; y++)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        uint pixel = pixels[y * frame.Width + x];
                        if (pixel >> 24 == 0)
                            continue;
                        int at = (top0 + y) * width + left0 + x;
                        // Flame is added to what is under it, each channel held at white.
                        canvas[at] = fire ? PixelArt.Blend(canvas[at], 1, pixel, 1) : pixel;
                    }
                }
            }
            frames.Add(new Picture(width, height, canvas));
        }
        return frames;
    }

    /// <summary>
    /// The logo with clear rows added under it (or over it) so that its letters - DIABLO, not the tall II that
    /// runs the whole height - sit at the picture's middle: centred on a page, it is the letters the eye
    /// centres. The plain logo's letters are rows 42 to 100 of 122, ten rows low, and every page showed it low.
    /// The letters are told as the rows with ink across the outer thirds.
    /// </summary>
    public static Picture CentredOnLetters(Picture logo)
    {
        int third = logo.Width / 3, top = -1, bottom = -1;
        for (int y = 0; y < logo.Height; y++)
        {
            int ink = 0;
            for (int x = 0; x < logo.Width; x++)
            {
                if ((x < third || x >= 2 * third) && logo.Bgra[y * logo.Width + x] >> 24 > 64)
                    ink++;
            }
            if (ink > logo.Width / 10)
            {
                if (top < 0)
                    top = y;
                bottom = y;
            }
        }
        if (top < 0)
            return logo;

        // Twice the letters' distance from the middle, on the side away from them.
        int shift = (top + bottom) - (logo.Height - 1);
        if (shift == 0)
            return logo;

        int height = logo.Height + Math.Abs(shift);
        var padded = new uint[logo.Width * height];
        Array.Copy(logo.Bgra, 0, padded, shift < 0 ? -shift * logo.Width : 0, logo.Bgra.Length);
        return new Picture(logo.Width, height, padded);
    }

    /// <summary>
    /// The logo's first frame, its two halves put together where their offsets place them (the left half
    /// starts at -169, the right at 9, both on one line), or null where the archives do not hold them.
    /// </summary>
    public static Picture? Logo(GameData game)
    {
        if (game.Read(LogoLeft) is not { } left || game.Read(LogoRight) is not { } right)
            return null;

        var palette = Palette.Current ?? Palette.Load(game);
        var halves = new[] { Dc6.Read(left.Data)[0], Dc6.Read(right.Data)[0] };

        // A DC6 frame is placed by its left edge and its bottom line.
        int x0 = halves.Min(f => f.OffsetX), x1 = halves.Max(f => f.OffsetX + f.Width);
        int y0 = halves.Min(f => f.OffsetY - f.Height), y1 = halves.Max(f => f.OffsetY);
        int width = x1 - x0, height = y1 - y0;
        var canvas = new uint[width * height];

        foreach (var half in halves)
        {
            var pixels = half.ToBgra(palette);
            int left0 = half.OffsetX - x0, top0 = half.OffsetY - half.Height - y0;
            for (int y = 0; y < half.Height; y++)
            {
                for (int x = 0; x < half.Width; x++)
                {
                    uint pixel = pixels[y * half.Width + x];
                    if (pixel >> 24 != 0)
                        canvas[(top0 + y) * width + left0 + x] = pixel;
                }
            }
        }

        return new Picture(width, height, canvas);
    }
}
