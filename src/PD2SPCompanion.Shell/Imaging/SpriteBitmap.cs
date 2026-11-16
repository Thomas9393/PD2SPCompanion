using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PD2SPCompanion.Core.Graphics;
using System;
using System.Collections.Generic;

namespace PD2SPCompanion.Shell.Imaging;

/// <summary>Turns a decoded DC6 sprite into something Avalonia can draw.</summary>
public static class SpriteBitmap
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The install the pictures were drawn under: a season that redraws an item keeps its key.</summary>
    private static int _drawnAt = -1;

    public static Bitmap? From(Sprite? sprite)
    {
        if (sprite is null)
            return null;

        // Pictures are the tables' too: another install,
        // drawn again. Bitmaps already on screen are left to their controls.
        if (_drawnAt != PD2SPCompanion.Core.GameFiles.InstalledTables.Generation)
        {
            Cache.Clear();
            _drawnAt = PD2SPCompanion.Core.GameFiles.InstalledTables.Generation;
        }

        // By the sprite's key, not its file: a dyed Annihilus and a plain one are one file.
        if (Cache.TryGetValue(sprite.Key, out var cached))
            return cached;

        var bitmap = FromPixels(sprite.Width, sprite.Height, sprite.Bgra);
        Cache[sprite.Key] = bitmap;
        return bitmap;
    }

    /// <summary>BGRA pixels, row by row, as a bitmap - an item's picture, a character figure's frame.</summary>
    public static WriteableBitmap FromPixels(int width, int height, uint[] bgra)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using (var buffer = bitmap.Lock())
        {
            unsafe
            {
                for (int y = 0; y < height; y++)
                {
                    var row = (uint*)((byte*)buffer.Address + y * buffer.RowBytes);
                    for (int x = 0; x < width; x++)
                        row[x] = bgra[y * width + x];
                }
            }
        }

        return bitmap;
    }
}
