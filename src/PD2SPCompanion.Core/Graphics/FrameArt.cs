using static PD2SPCompanion.Core.Graphics.PixelArt;

namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// The editor's window frame, cut at run time from the panel pieces of the resolution mod PD2 ships
/// (SlashDiablo's SGD2FreeRes, <c>ProjectD2\SGD2FreeRes.mpq</c>). The editor ships no hand-made Photoshop
/// work over upscales of these pieces,
/// so its recipes are done here instead: the band and the bar cut from their pieces, turned or flipped,
/// and laid over the mod's stone so the filigree stands on grey rather than black. The pages' stone alone comes
/// from the base game's blank menu panel (<see cref="PageBackground"/>). The recipes only: what they cut, turn,
/// blend and tone with is PixelArt's.
/// </summary>
public static class FrameArt
{
    private const string Panel = @"data\SGD2FreeResolution\ui\panel\";

    /// <summary>
    /// The columns trimmed off each end of the border band: its first and last are a darker bevel, and the brush
    /// lays every other copy mirrored, so two of them met in a line down the band (the seam showed).
    /// </summary>
    private const int BorderEnds = 1;

    /// <summary>The same for the bar: three darkening columns at its start, two touched with gold at its end.</summary>
    private const int BarStart = 3, BarEnd = 2;

    /// <summary>The border's source: a panel bar whose filigree band is rows 6 to 32 (gold and black edges above, a stone strip below).</summary>
    public const string BorderPiece = Panel + "D2MRFancyPanelInvertHorizontalBar.dc6";

    /// <summary>The bar's source: a fancy bar whose filigree core is rows 6 to 53, between its edge lines.</summary>
    public const string BarPiece = Panel + "D2MRFancyHorizontalBar.dc6";

    /// <summary>The stone the filigree is laid over.</summary>
    public const string Stone = Panel + "D2MRStoneBack.dc6";

    /// <summary>
    /// The border band: the panel bar's filigree band, turned upside down, over
    /// stone - as wide as the piece, 27 rows. Null where the mod's archive is not installed.
    /// </summary>
    public static Picture? Border(GameData game) =>
        Piece(game, BorderPiece) is { } piece && Piece(game, Stone) is { } stone
            ? Shaded(Toned(OverStone(FlipUpsideDown(Window(piece, BorderEnds, 6, piece.Width - 2 * BorderEnds, 27)), stone), FrameLevels),
                     BorderInner, BorderOuter)
            : null;

    /// <summary>
    /// The shading across the border band, from its first row - the edge the pages sit against - to its last, the
    /// window's edge. The hand-made band was lit from the page side and fell off toward the window ("a
    /// gradient shadow"): its brightest filigree 135 at the inner edge, 83 at the outer; the cut band ran the other
    /// way. These are the two profiles divided, row by row, after <see cref="FrameLevels"/>. The bar's was flat.
    /// </summary>
    private const double BorderInner = 1.2, BorderOuter = 0.7;

    /// <summary>
    /// The black point the border and the bar are levelled to: what lies at 28 and below goes black, the rest is
    /// stretched to fill the range. The cut frame read too bright beside the hand-made one; matching
    /// the two screenshots' brightness percentiles gave exactly this - 39 to 12, 61 to 37, 108 to 90, 163 to 151,
    /// each within three of the hand-made one's. The gaps in the filigree go near black again, as there.
    /// </summary>
    private const int FrameBlack = 28;

    private static int FrameLevels(int channel) => Math.Max(0, channel - FrameBlack) * 255 / (255 - FrameBlack);

    /// <summary>
    /// The upright bar: the fancy bar's filigree core, turned a quarter clockwise, over
    /// stone - 48 columns, as long as the piece. Null where the mod's archive is not installed.
    /// </summary>
    public static Picture? Bar(GameData game) =>
        Piece(game, BarPiece) is { } piece && Piece(game, Stone) is { } stone
            ? Toned(OverStone(TurnClockwise(Window(piece, BarStart, 6, piece.Width - BarStart - BarEnd, 48)), stone), FrameLevels)
            : null;

    /// <summary>The corner square's source: 60 pixels, a gold line round its edge, then dark, then a silver line.</summary>
    public const string CornerPiece = Panel + "D2MRFancyBorderCorner.dc6";

    /// <summary>
    /// The frame's corner square: the corner piece with its gold edge and the dark
    /// lines inside it cut off, the silver line kept - pixels 3 to 56 each way, as the shipped Corner_U_S was -
    /// and darkened to nine tenths, as the hand-made one measured beside it. Null where the mod's archive is not installed.
    /// </summary>
    public static Picture? Corner(GameData game) =>
        Piece(game, CornerPiece) is { } piece ? Toned(Window(piece, 3, 3, 54, 54), channel => channel * 9 / 10) : null;

    /// <summary>
    /// The ornament's source: the Neo interface strip, 80 wide and 554 tall in three frames, one under another -
    /// silver filigree on stone, a gold medallion across its middle (rows 224 to 327, an empty row each side).
    /// </summary>
    public const string OrnamentPiece = Panel + "NeoD2MRFancyBorderInterfaceLeft.dc6";

    /// <summary>The rows cut around the medallion: from the empty row over it to the one under it.</summary>
    private const int OrnamentTop = 223, OrnamentRows = 106;

    /// <summary>
    /// The gold ornament on the spine between the pages (the Neo strip's medallion,
    /// its background and the other parts gone). Gold is told by its warmth (red over blue by 10 or more) and its
    /// light (35 or more); its small gaps closed; the stone, the silver strip and all else reached from the cut's
    /// edges without crossing gold made clear - so the medallion keeps its dark inside. Its darks then crushed as
    /// the hand-made one's were (<see cref="OrnamentCurve"/>). 96% the outline of the hand-made one; 76 by 102. Null where the
    /// mod's archive is not installed.
    /// </summary>
    public static Picture? Ornament(GameData game)
    {
        if (game.Read(OrnamentPiece) is not { } data || Dc6.Read(data.Data) is not { Count: > 0 } frames)
            return null;

        var strip = Grid(frames, 1, Palette.Current ?? Palette.Load(game));
        if (strip.Height < OrnamentTop + OrnamentRows)
            return null;
        var cut = Window(strip, 0, OrnamentTop, strip.Width, OrnamentRows);

        int width = cut.Width, height = cut.Height;
        var gold = new bool[width * height];
        for (int i = 0; i < gold.Length; i++)
        {
            uint pixel = cut.Bgra[i];
            int red = (int)((pixel >> 16) & 0xFF), green = (int)((pixel >> 8) & 0xFF), blue = (int)(pixel & 0xFF);
            gold[i] = pixel >> 24 != 0 && red - blue >= 10 && (red + green + blue) / 3.0 >= 35;
        }
        gold = Eroded(Eroded(Dilated(Dilated(gold, width, height), width, height), width, height), width, height);

        var outside = Flooded(gold, width, height);
        var kept = Largest(outside.Select(o => !o).ToArray(), width, height);

        int left = width, right = -1, top = height, bottom = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (kept[y * width + x])
                    (left, right, top, bottom) = (Math.Min(left, x), Math.Max(right, x), Math.Min(top, y), Math.Max(bottom, y));
        if (right < 0)
            return null;

        var ornament = Toned(cut, OrnamentCurve);
        var clear = new uint[ornament.Bgra.Length];
        for (int i = 0; i < clear.Length; i++)
            clear[i] = kept[i] ? ornament.Bgra[i] | 0xFF000000 : 0;
        return Window(new Picture(width, height, clear), left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// The ornament's darks crushed, its gold left: through 12 to 0, 36 to 25, 53 to 41, 64 to 60, and from 80 on as it
    /// was - the hand-made ornament against the cut one, percentile by percentile. A black point would have
    /// dimmed the gold as well.
    /// </summary>
    private static int OrnamentCurve(int channel) => Through(channel, [(0, 0), (12, 0), (36, 25), (53, 41), (64, 60), (80, 80), (255, 255)]);

    /// <summary>The window's stone: 256 pixels, a gold line along its top and its left (two rows, two columns).</summary>
    public const string WindowStone = Panel + "D2MRChooseResolutionBack.dc6";

    /// <summary>
    /// The window's background: the resolution chooser's stone with the gold cut
    /// off its two edged sides - 254 pixels, tiled. Null where the mod's archive is not installed.
    /// </summary>
    public static Picture? WindowBackground(GameData game) =>
        Piece(game, WindowStone) is { } piece ? Window(piece, 2, 2, piece.Width - 2, piece.Height - 2) : null;

    /// <summary>
    /// The base game's blank menu panel (Blizzard's own, d2data.mpq): smoky stone with the Diablo II logo embossed
    /// across its middle and an ornament at three corners - 373 pixels in four frames, two to a row. PD2 ships its
    /// own as <c>blankbckg2</c>, the same stone with the season's name added; this one never changes with a season.
    /// </summary>
    public const string BlankPanel = @"data\global\ui\BIGMENU\blankbckg.dc6";

    /// <summary>The rows the cross-fades run over: wide enough that no join shows in the smoke.</summary>
    private const int Fade = 24;

    /// <summary>
    /// The pages' background: the blank panel's stone without its logo (the hand-made tile was the panel with the logo
    /// painted out - a paper-like dark seamless texture is what matters). The clean band over
    /// the logo and the one under it, between the corner ornaments and short of the panel's darkened edges, stacked
    /// and cross-faded, then made to meet itself: 184 by 164, laid plain, never mirrored - mirrored smoke drew
    /// faces. Its grain at <see cref="PageGrain"/> of its strength. Null where the archive does not hold the panel.
    /// </summary>
    public static Picture? PageBackground(GameData game)
    {
        if (game.Read(BlankPanel) is not { } data || Dc6.Read(data.Data) is not { Count: >= 4 } frames)
            return null;

        var panel = Grid(frames, 2, Palette.Current ?? Palette.Load(game));
        var stone = Stacked(Window(panel, 118, 8, 208, 88), Window(panel, 118, 238, 208, 124), Fade);
        return Softened(Seamless(stone, Fade), PageGrain);
    }

    /// <summary>
    /// How much of the stone's grain the pages keep: the texture as if laid at half opacity over a plain fill of
    /// its own average colour. At full strength it was too strong behind the pages' text.
    /// </summary>
    private const double PageGrain = 0.5;

    /// <summary>A panel piece as a picture: its DC6's first frame, or null where no archive holds it.</summary>
    private static Picture? Piece(GameData game, string path)
    {
        if (game.Read(path) is not { } data)
            return null;
        var frame = Dc6.Read(data.Data)[0];
        var palette = Palette.Current ?? Palette.Load(game);
        return new Picture(frame.Width, frame.Height, frame.ToBgra(palette));
    }

    /// <summary>
    /// The filigree kept where it is lit and the stone where the piece is near black, blended by brightness
    /// (fully stone at 18 and below, fully filigree from 58): what the hand-made Photoshop step did, chosen by
    /// comparing the result with it. The stone is darkened to three quarters, tiled under the piece.
    /// </summary>
    private static Picture OverStone(Picture piece, Picture stone)
    {
        var blended = new uint[piece.Bgra.Length];
        for (int y = 0; y < piece.Height; y++)
        {
            for (int x = 0; x < piece.Width; x++)
            {
                uint over = piece.Bgra[y * piece.Width + x];
                uint under = stone.Bgra[(y % stone.Height) * stone.Width + x % stone.Width];
                double light = (((over >> 16) & 0xFF) + ((over >> 8) & 0xFF) + (over & 0xFF)) / 3.0;
                double keep = Math.Clamp((light - 18) / 40, 0, 1);
                blended[y * piece.Width + x] = Blend(over, keep, under, (1 - keep) * 0.75);
            }
        }
        return new Picture(piece.Width, piece.Height, blended);
    }
}
