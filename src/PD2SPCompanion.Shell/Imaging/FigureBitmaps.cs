using Avalonia.Media.Imaging;
using PD2SPCompanion.Core.Diagnostics;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Saves;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell.Imaging;

/// <summary>
/// A character's figure ready to draw: its frames, how fast they play, their size in the game's pixels,
/// the anchor (the feet) and the body's rows against it (weapons left out) - and how many times the
/// bitmaps were enlarged (<see cref="PixelArt.Scale2x(uint[], int, int, int)"/>), so a page draws them at
/// the game's size times its own scale.
/// </summary>
public sealed record FigureFrames(IReadOnlyList<Bitmap> Frames, double FramesPerSecond, int Width, int Height,
                                  int AnchorX, int AnchorY, int BodyTop, int BodyBottom, int Enlarged);

/// <summary>
/// The character drawn in its gear, for the cards: the look read
/// on the UI thread, where the parser's one layout lives, the layers decoded on a worker, the frames
/// made bitmaps back on the UI thread.
/// </summary>
public static class FigureBitmaps
{
    /// <summary>
    /// A save file's still figure and the look it was drawn from, by its path and the time it was written:
    /// the landing page rebuilds its cards each time the game saves. Only the first frame is kept - every
    /// card but the selected one shows it, and 50 characters' whole animations were some 45 MB of frames
    /// that never played. The selected card's animation is made from the look
    /// (<see cref="AnimatedAsync"/>) and let go when another is selected.
    /// </summary>
    private static readonly Dictionary<string, (DateTime Written, FigureFrames? Figure, FigureLook? Look)> OnDisk = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The install the figures were drawn under: another season's tables draw them again (SpriteBitmap's rule).</summary>
    private static int _drawnAt = -1;

    private static void SameInstall()
    {
        if (_drawnAt == PD2SPCompanion.Core.GameFiles.InstalledTables.Generation)
            return;
        OnDisk.Clear();
        _drawnAt = PD2SPCompanion.Core.GameFiles.InstalledTables.Generation;
    }

    /// <summary>A save's figure already drawn, while the file is as it was: a card made again shows it at once.</summary>
    public static FigureFrames? Drawn(string path)
    {
        SameInstall();
        try
        {
            return OnDisk.TryGetValue(path, out var kept) && kept.Written == File.GetLastWriteTimeUtc(path) ? kept.Figure : null;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The figure of a save on disk, or null where it cannot be read or drawn - the card keeps the class
    /// picture then. <paramref name="lookOf"/> answers what the character wears, from whoever keeps the save's
    /// read (SavedItems): the search reads every save too, and the two read each file once between them.
    /// </summary>
    public static async Task<FigureFrames?> OfFileAsync(string path, Func<FigureLook?> lookOf, int smooth = 1)
    {
        SameInstall();
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return null;
        }
        if (OnDisk.TryGetValue(path, out var kept) && kept.Written == written)
            return kept.Figure;

        if (lookOf() is not { } look)
        {
            // A stub, an older season's save, a file mid-write: the class picture stays.
            OnDisk[path] = (written, null, null);
            return null;
        }

        var figure = await ComposeAsync(look, smooth, still: true);
        OnDisk[path] = (written, figure, look);
        return figure;
    }

    /// <summary>
    /// The whole animation of a save whose still figure was drawn, from the look kept with it - no save
    /// read again, and the decoded layers are the loader's already. Null where there is no figure.
    /// </summary>
    /// <remarks>Enlarged twice, the smoothest: one figure at a time, and the landing page's large view shows it at up to three times.</remarks>
    public static async Task<FigureFrames?> AnimatedAsync(string path, int smooth = 2) =>
        OnDisk.TryGetValue(path, out var kept) && kept.Look is { } look ? await ComposeAsync(look, smooth) : null;

    /// <summary>
    /// A look composed off the UI thread, each frame enlarged with Scale2x <paramref name="smooth"/> times
    /// there too; null where the class has no pose for it. Twice (four times the size, some 8 MB a
    /// figure) for the one large card; once for a list of them.
    /// <para>
    /// The game itself, as PD2 runs it, draws through D2GL with a RetroArch shader the player picks - one
    /// is <c>anti-aliasingdvanced-aa</c> with sharpening (d2gl.json): edges softened,
    /// the inside kept sharp. Scale2x and a smooth scale down to the card's size is the same idea without
    /// a GPU shader.
    /// </para>
    /// </summary>
    public static async Task<FigureFrames?> ComposeAsync(FigureLook look, int smooth = 1, bool still = false)
    {
        if (FigureLoader.Current is not { } loader)
            return null;

        Figure? figure;
        List<uint[]> enlarged;
        try
        {
            (figure, enlarged) = await Task.Run(() =>
            {
                var composed = loader.Compose(look);
                var frames = composed is null
                    ? []
                    : composed.Frames.Take(still ? 1 : composed.Frames.Count)
                              .Select(frame => PixelArt.Scale2x(frame.Bgra, composed.Width, composed.Height, smooth)).ToList();
                return (composed, frames);
            });
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return null;
        }
        if (figure is null || figure.Frames.Count == 0)
            return null;
        if (figure.Missing.Count > 0)
            Log.Info("figure", $"{look.Token} {look.WeaponClass}: no art for {string.Join(", ", figure.Missing)}");

        int times = 1 << smooth;
        return new FigureFrames(
            [.. enlarged.Select(pixels => (Bitmap)SpriteBitmap.FromPixels(figure.Width * times, figure.Height * times, pixels))],
            figure.FramesPerSecond, figure.Width, figure.Height,
            figure.AnchorX, figure.AnchorY, figure.BodyTop, figure.BodyBottom, times);
    }

}
