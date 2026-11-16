using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;

using PD2SPCompanion.Core.GameFiles;
using PD2SPCompanion.Core.Graphics;

namespace PD2SPCompanion.Shell.Imaging;

/// <summary>
/// The game's art as bitmaps (Core's FrontEnd and FrameArt): the Diablo II logo, still and burning, and the
/// window frame. Read from the player's install, enlarged with Scale2x as the figures are; nothing where no
/// game folder is set yet, which is a first run's setup.
/// </summary>
public static class FrontEndArt
{
    /// <summary>The resource the logo is kept under: Image.D2LogoBG (Styles/CommonControls/Image.axaml) shows it.</summary>
    public const string LogoKey = "[Art] D2 Logo";

    /// <summary>The resource the burning logo's frames are kept under: the landing's header plays them (FrameImage).</summary>
    public const string BurningLogoKey = "[Art] D2 Logo Burning";

    /// <summary>
    /// The art the pages and the window wear, put where they look for it: the logos (<see cref="LogoKey"/>,
    /// <see cref="BurningLogoKey"/>) and the window frame's brushes (<see cref="CutFrame"/>). Called once
    /// the tables are open - behind the splash, or after a first run's setup - so what was built before it
    /// shows it once it is there.
    /// <para>
    /// In two halves: the pictures are cut and enlarged on a worker thread (<see cref="Cut"/> - plain arithmetic
    /// on pixels, Core's FrontEnd and FrameArt), and only the bitmaps and the resources are made on the UI
    /// thread, which the cutting held for 60-125 ms behind the splash (240 ms in a Debug build). Built once:
    /// every caller is handed the one task.
    /// </para>
    /// </summary>
    public static Task Provide(GameData? game)
    {
        if (game is null || Application.Current is not { } app)
            return Task.CompletedTask;
        return _provided ??= ProvideOnce(app, game);
    }

    private static Task? _provided;

    private static async Task ProvideOnce(Application app, GameData game)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var laid = await Task.Run(() => Cut(game));
        long cut = timer.ElapsedMilliseconds;

        // Back on the UI thread: each picture made a bitmap and put under its key.
        timer.Restart();
        foreach (var lay in laid)
        {
            try
            {
                lay(app);
            }
            catch (Exception problem)
            {
                // No logo, no frame, is no loss: the pages are the same without them.
                PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            }
        }
        PD2SPCompanion.Core.Diagnostics.Log.Info("Start", $"Art: cut in {cut} ms off the UI thread, laid in {timer.ElapsedMilliseconds} ms on it");
    }

    /// <summary>
    /// Every picture cut, turned and enlarged - no bitmap, no resource, so any thread may run it - and for each
    /// what puts it in place, to be run on the UI thread.
    /// </summary>
    private static List<Action<Application>> Cut(GameData game)
    {
        var laid = new List<Action<Application>>();
        try
        {
            if (FrontEnd.Logo(game) is { } logo)
            {
                var enlarged = Enlarged(FrontEnd.CentredOnLetters(logo));
                laid.Add(app => app.Resources[LogoKey] = Bitmap(enlarged));
            }

            // Drawn larger than the header shows it, so not enlarged: thirty frames at their own size.
            var burning = FrontEnd.BurningLogo(game);
            if (burning.Count > 0)
                laid.Add(app => app.Resources[BurningLogoKey] = (IReadOnlyList<Bitmap>)[.. burning.Select(Bitmap)]);
        }
        catch (Exception problem)
        {
            // No logo is no loss: the pages are the same without their watermark.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }

        CutFrame(game, laid);
        return laid;
    }

    /// <summary>The window frame's corner square (MainView's four UI_Corner images).</summary>
    public const string CornerKey = "[Art] Frame Corner";

    /// <summary>The gold ornament on the spine between the pages (MainView's two UI_Mid_Decor images).</summary>
    public const string OrnamentKey = "[Art] Frame Ornament";

    /// <summary>The window frame's border band, as tall on screen as the band the editor shipped (32).</summary>
    private const double BorderHeight = 32;

    /// <summary>
    /// The upright bar's art on screen, as wide as the shipped one's (48): the frame's bands are drawn at that scale,
    /// and the pillar's texture stays as coarse as theirs. The pillar itself is narrower and shows the art's middle
    /// (the view's "[UI] Frame BarArt" against "[UI] Frame BarWidth").
    /// </summary>
    private const double BarWidth = 48;

    /// <summary>
    /// Copies in the border's strip and the bar's: 12 bands are 3600 long and 8 bars about 2000, past any side of
    /// the window, so the brush's own join - which drew a dark line - stays off screen.
    /// </summary>
    private const int BorderCopies = 12, BarCopies = 8;

    /// <summary>
    /// The window frame's brushes (MainView's SubFrame borders and bar), cut from the resolution mod's pieces
    /// (Core's FrameArt) and enlarged with Scale2x: the border one band turned four ways, every other copy
    /// mirrored as the shipped one was; the bar one, every other copy flipped. Where the mod's archive is not
    /// installed the frame is simply not drawn.
    /// </summary>
    private static void CutFrame(GameData game, List<Action<Application>> laid)
    {
        try
        {
            if (FrameArt.Border(game) is { } border)
            {
                // One long strip of mirrored copies (PixelArt.MirroredAcross): the brush's own tiles met in a dark
                // line at every join. Longer than any side of the window, so no join is on screen.
                var band = Enlarged(PixelArt.MirroredAcross(border, BorderCopies));
                double width = Whole(BorderHeight * border.Width / border.Height) * BorderCopies;
                laid.Add(app =>
                {
                    var bitmap = Bitmap(band);
                    foreach (var (side, turn) in new[] { ("T", 180), ("B", 0), ("L", 90), ("R", -90) })
                    {
                        app.Resources[$"[UI] ImageBrush - SubFrame_Border#1 - {side}"] = new ImageBrush(bitmap)
                        {
                            TileMode = TileMode.FlipX,
                            DestinationRect = new RelativeRect(0, 0, width, BorderHeight, RelativeUnit.Absolute),
                            Transform = TransformOperations.Parse($"rotate({turn}deg)"),
                        };
                    }
                });
            }

            if (FrameArt.Corner(game) is { } corner)
            {
                var enlarged = Enlarged(corner);
                laid.Add(app => app.Resources[CornerKey] = Bitmap(enlarged));
            }
            if (FrameArt.Ornament(game) is { } ornament)
            {
                var enlarged = Enlarged(ornament);
                laid.Add(app => app.Resources[OrnamentKey] = Bitmap(enlarged));
            }

            // The stone backgrounds, twice their pixels on screen as the shipped ones were (512 for 256).
            if (FrameArt.WindowBackground(game) is { } window)
                laid.Add(Tiled("[UI] ImageBrush - Background#1", window));
            if (FrameArt.PageBackground(game) is { } page)
                laid.Add(Tiled("[UI] ImageBrush - Background#2", page));

            if (FrameArt.Bar(game) is { } bar)
            {
                var strip = Enlarged(PixelArt.MirroredDown(bar, BarCopies));
                double length = Whole(BarWidth * bar.Height / bar.Width) * BarCopies;
                laid.Add(app => app.Resources["[UI] ImageBrush - SubFrame_Bar#1"] = new ImageBrush(Bitmap(strip))
                {
                    TileMode = TileMode.FlipY,
                    DestinationRect = new RelativeRect(0, 0, BarWidth, length, RelativeUnit.Absolute),
                });
            }
        }
        catch (Exception problem)
        {
            // No frame is no loss: the pages are the same within it.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
    }

    /// <summary>
    /// A tile's size rounded to a multiple of four: whole pixels at 100, 125, 150 and 175% scaling. A tile is drawn
    /// into a picture of whole pixels, so a fractional one - the border's 301.2, 376.5 pixels at 125% - left a dark
    /// line where every two copies met. The stretch is under one per cent.
    /// </summary>
    private static double Whole(double size) => Math.Max(4, Math.Round(size / 4) * 4);

    /// <summary>A stone tile enlarged with Scale2x, to be laid under <paramref name="key"/> at twice its pixels, tiled plain.</summary>
    private static Action<Application> Tiled(string key, Picture picture)
    {
        var enlarged = Enlarged(picture);
        return app => app.Resources[key] = new ImageBrush(Bitmap(enlarged))
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, Whole(picture.Width * 2), Whole(picture.Height * 2), RelativeUnit.Absolute),
        };
    }

    /// <summary>Twice the size, its edges kept (Scale2x): the pages draw these larger than the game did. Pixels only.</summary>
    private static Picture Enlarged(Picture picture) =>
        new(picture.Width * 2, picture.Height * 2, PixelArt.Scale2x(picture.Bgra, picture.Width, picture.Height));

    /// <summary>A picture as a bitmap: the UI thread's half.</summary>
    private static Bitmap Bitmap(Picture picture) => SpriteBitmap.FromPixels(picture.Width, picture.Height, picture.Bgra);
}
