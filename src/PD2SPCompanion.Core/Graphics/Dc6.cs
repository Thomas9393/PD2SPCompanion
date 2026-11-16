namespace PD2SPCompanion.Core.Graphics;

/// <summary>One decoded DC6 frame: 8-bit palette indices, row-major, top-down.</summary>
public sealed record Dc6Frame(int Width, int Height, int OffsetX, int OffsetY, byte[] Indices)
{
    /// <summary>
    /// Expands the frame to BGRA pixels using a palette - through a colour map first, where
    /// one is given, which is how the game tints an item. Index 0 stays see-through either way.
    /// </summary>
    public uint[] ToBgra(Palette palette, byte[]? remap = null)
    {
        var pixels = new uint[Width * Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte index = Indices[i];
            pixels[i] = palette[remap is null || index == 0 ? index : remap[index]];
        }
        return pixels;
    }
}

/// <summary>
/// Reader for Diablo II's DC6 sprite format. Inventory graphics are single-frame DC6
/// files of palette indices, run-length encoded per scanline and stored bottom-up.
/// </summary>
public static class Dc6
{
    private const byte EndOfLine = 0x80;

    public static IReadOnlyList<Dc6Frame> Read(byte[] data)
    {
        // Header: version, flags, encoding, 4 termination bytes, directions, frames.
        int directions = BitConverter.ToInt32(data, 16);
        int framesPerDirection = BitConverter.ToInt32(data, 20);
        int frameCount = directions * framesPerDirection;
        if (frameCount <= 0 || frameCount > 4096)
            throw new InvalidDataException($"DC6 claims {frameCount} frames, which is not plausible.");

        var frames = new List<Dc6Frame>(frameCount);
        for (int i = 0; i < frameCount; i++)
        {
            int pointer = BitConverter.ToInt32(data, 24 + i * 4);
            frames.Add(ReadFrame(data, pointer));
        }
        return frames;
    }

    private static Dc6Frame ReadFrame(byte[] data, int offset)
    {
        bool flipped = BitConverter.ToInt32(data, offset) != 0;
        int width = BitConverter.ToInt32(data, offset + 4);
        int height = BitConverter.ToInt32(data, offset + 8);
        int offsetX = BitConverter.ToInt32(data, offset + 12);
        int offsetY = BitConverter.ToInt32(data, offset + 16);
        int length = BitConverter.ToInt32(data, offset + 28);
        int body = offset + 32;

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            throw new InvalidDataException($"DC6 frame is {width}x{height}, which is not plausible.");

        var indices = new byte[width * height];

        // Unflipped frames are stored bottom row first, so writing starts at the last row.
        int x = 0;
        int y = flipped ? 0 : height - 1;

        for (int i = 0; i < length; i++)
        {
            byte command = data[body + i];

            if (command == EndOfLine)
            {
                x = 0;
                y += flipped ? 1 : -1;
                continue;
            }

            if ((command & EndOfLine) != 0)
            {
                // High bit set: a run of transparent pixels, which stay at index 0.
                x += command & 0x7F;
                continue;
            }

            // Otherwise a literal run of that many palette indices.
            for (int run = 0; run < command; run++)
            {
                i++;
                if (y >= 0 && y < height && x < width)
                    indices[y * width + x] = data[body + i];
                x++;
            }
        }

        return new Dc6Frame(width, height, offsetX, offsetY, indices);
    }
}
