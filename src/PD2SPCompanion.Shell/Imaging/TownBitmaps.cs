using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;

using PD2SPCompanion.Core.Graphics;

namespace PD2SPCompanion.Shell.Imaging;

/// <summary>
/// The towns' backdrops as bitmaps (Core's TownBackdrops), one per act: cut on a worker thread, the bitmap
/// made on the UI thread, kept while the tables it was read with are the ones installed.
/// <para>
/// The game's own pixels, not enlarged with Scale2x as the figure's are: the card's smoothing alone draws
/// the town a little soft behind a sharp figure, chosen from the two side by
/// side - and enlarged it cost some 30 MB of memory once drawn, where this costs nothing that can be
/// measured: no time, no idle CPU, no memory.
/// </para>
/// </summary>
public static class TownBitmaps
{
    private static readonly Dictionary<int, Task<Bitmap?>> Kept = [];

    /// <summary>The install the bitmaps were drawn under.</summary>
    private static int _drawnAt = -1;

    /// <summary>The backdrop of an act, 1 to 5, or null where the install gives none. Asked on the UI thread.</summary>
    public static Task<Bitmap?> OfAsync(int act)
    {
        if (_drawnAt != PD2SPCompanion.Core.GameFiles.InstalledTables.Generation)
        {
            Kept.Clear();
            _drawnAt = PD2SPCompanion.Core.GameFiles.InstalledTables.Generation;
        }

        if (!Kept.TryGetValue(act, out var drawn))
            Kept[act] = drawn = DrawAsync(act);
        return drawn;
    }

    private static async Task<Bitmap?> DrawAsync(int act)
    {
        if (TownBackdrops.Current is not { } towns)
            return null;

        try
        {
            var picture = await Task.Run(() => towns.Of(act));
            return picture is null ? null : SpriteBitmap.FromPixels(picture.Width, picture.Height, picture.Bgra);
        }
        catch (Exception problem)
        {
            // No backdrop is no loss: the figure stands on black, as it did.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            return null;
        }
    }
}
