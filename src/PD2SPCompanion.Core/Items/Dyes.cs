namespace PD2SPCompanion.Core.Items;

/// <summary>
/// PD2's dye: <c>transform_dye</c> (stat 477, 5 bits), written by the <c>dye</c> property. It
/// tints an item's picture and prints nothing - a corrupted Annihilus is red because every one of
/// its Corrupted Worldstone Shard outcomes writes <c>dye 10</c>. The trade site does not list it.
/// <para>
/// <b>The value is a row of <c>colors.txt</c> counted from one</b>, 0 being no dye: 10 is row 9,
/// Crystal Red, the red a corrupted Annihilus shows in game. Three colours agree: Lilith's
/// Mirror writes <c>dye 4</c> and <c>dye 21</c>, rows 3 and 20, Black and Bright White - the
/// wiki's "white &amp; black" - and 21 only fits a 21-row table counted from one.
/// </para>
/// </summary>
public static class Dyes
{
    public const string Stat = "transform_dye";

    /// <summary>
    /// Lilith's Mirror's mark (stat 486, 1 bit, prints "Mirrored"). The recipe (cubemain rows
    /// 1600-1614, 2303-2320: magic, rare and crafted equipment and quivers, jewels switched off)
    /// has two outputs: the item itself gets <c>dye 4</c> and <c>mirrored 1</c>, the copy
    /// <c>dye 21</c> and <c>mirrored 1</c> - Black and Bright White, the wiki's "white &amp;
    /// black". Neither can go in a cube recipe again.
    /// </summary>
    public const string MirroredStat = "mirrored";

    /// <summary>The dye the mirrored original takes, and the dye its copy takes.</summary>
    public const int MirrorOriginal = 4, MirrorCopy = 21;

    public static bool IsMirrored(D2SLib.Model.Save.Item item) =>
        ItemStats.Every(item).Any(stat =>
            string.Equals(stat.Stat, MirroredStat, StringComparison.OrdinalIgnoreCase) && stat.Value > 0);

    private static string[] _names = [];
    private static string[] _codes = [];

    public static void Load(GameData game)
    {
        _names = [];
        _codes = [];

        try
        {
            var (table, _) = game.ReadExcel("colors");
            _names = [.. Enumerable.Range(0, table.Rows.Count).Select(row => table.Value(row, "Transform Color").Trim())];
            _codes = [.. Enumerable.Range(0, table.Rows.Count).Select(row => table.Value(row, "Code").Trim())];
        }
        catch (Exception)
        {
            // No colour table, no colour names: the number is still said.
        }
    }

    /// <summary>Every colour, by its dye value - the <c>colors.txt</c> row plus one - for a list to pick from.</summary>
    public static IReadOnlyList<(int Dye, string Name)> Colours =>
        [.. _names.Select((name, row) => (row + 1, name)).Where(each => each.name.Length > 0)];

    /// <summary>The <c>colors.txt</c> row a dye value names, or null for none or one past the table.</summary>
    public static int? ColourOf(int dye) => dye > 0 && dye - 1 < _names.Length ? dye - 1 : null;

    /// <summary>The <c>colors.txt</c> row a colour code names - <c>bwht</c> is 20 - or null.</summary>
    public static int? RowOfCode(string? code)
    {
        int at = code is null ? -1 : Array.FindIndex(_codes, each => string.Equals(each, code.Trim(), StringComparison.OrdinalIgnoreCase));
        return at >= 0 ? at : null;
    }

    /// <summary>The colour's code - <c>cred</c> - as <c>invtransform</c> columns name one.</summary>
    public static string? CodeOf(int dye) => ColourOf(dye) is { } row ? _codes[row] : null;

    /// <summary>The line a dye reads as - the game prints none, so this is the editor's own.</summary>
    public static string Describe(int dye) =>
        ColourOf(dye) is { } row ? L($"{_names[row]} tint") : L($"Tint {dye}");
}
