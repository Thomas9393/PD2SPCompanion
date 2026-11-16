namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// A Diablo II 256-colour palette. DC6 sprites store palette indices, not colours,
/// so one of these is needed to turn a sprite into pixels.
/// </summary>
public sealed class Palette
{
    /// <summary>Colours as packed BGRA, which is the order a bitmap wants them in.</summary>
    private readonly uint[] _colours = new uint[256];

    public static Palette? Current { get; private set; }

    public static Palette Load(GameData game)
    {
        var result = game.Read(GameData.PalettePath)
            ?? throw new FileNotFoundException($"'{GameData.PalettePath}' is not in any loaded archive.");

        var palette = FromDat(result.Data);
        Current = palette;
        return palette;
    }

    /// <summary>
    /// A palette from its <c>pal.dat</c>, installed nowhere: the game has one for each screen - the loading
    /// screen its own (FrontEnd) - where the items' is the one <see cref="Current"/> holds.
    /// </summary>
    public static Palette FromDat(byte[] data)
    {
        if (data.Length < 768)
            throw new InvalidDataException($"palette is {data.Length} bytes; expected at least 768.");

        var palette = new Palette();
        for (int i = 0; i < 256; i++)
        {
            // pal.dat is 256 BGR triplets.
            byte b = data[i * 3];
            byte g = data[i * 3 + 1];
            byte r = data[i * 3 + 2];
            palette._colours[i] = (uint)(0xFF << 24 | r << 16 | g << 8 | b);
        }

        // Index 0 is the transparent colour in D2's sprites.
        palette._colours[0] = 0x00000000;

        return palette;
    }

    public uint this[byte index] => _colours[index];
}
