namespace PD2SPCompanion.Core.Graphics;

/// <summary>
/// Which colour map, and which colour in it, an inventory picture is drawn through.
/// </summary>
/// <param name="Table">The base's <c>InvTrans</c>, 1 to 8 - see <see cref="ColourMaps"/>.</param>
/// <param name="Colour">The <c>colors.txt</c> row, 0 to 20.</param>
public sealed record Tint(int Table, int Colour);

/// <summary>
/// The game's item colour maps - how a picture is tinted without a picture of its own.
/// <para>
/// A base names one of eight files in its <c>InvTrans</c> column (1 grey, 2 grey2, 3 gold,
/// 4 brown, 5 greybrown, 6 invgrey, 7 invgrey2, 8 invgreybrown - a small charm is 8), and
/// each file holds one 256-byte map per <c>colors.txt</c> row. Drawing tinted is sending every
/// pixel's palette index through the map before the palette: the shape stays, the hues move.
/// </para>
/// </summary>
public static class ColourMaps
{
    private static readonly string[] Files =
        ["grey", "grey2", "gold", "brown", "greybrown", "invgrey", "invgrey2", "invgreybrown"];

    private static Dictionary<int, byte[]> _tables = [];

    /// <summary>
    /// Built aside and put in place whole: the character figures read the maps on a worker thread, and
    /// emptied and refilled in place a draw at that moment could meet a half-filled table.
    /// </summary>
    public static void Load(GameData game)
    {
        var tables = new Dictionary<int, byte[]>();
        for (int n = 0; n < Files.Length; n++)
        {
            if (game.Read($@"data\global\items\Palette\{Files[n]}.dat") is { } found)
                tables[n + 1] = found.Data;
        }
        _tables = tables;
    }

    /// <summary>The map for a tint, or null where its file or its colour is not there.</summary>
    public static byte[]? For(Tint tint)
    {
        if (!_tables.TryGetValue(tint.Table, out var data) || tint.Colour < 0)
            return null;

        int start = tint.Colour * 256;
        return start + 256 <= data.Length ? data[start..(start + 256)] : null;
    }
}
