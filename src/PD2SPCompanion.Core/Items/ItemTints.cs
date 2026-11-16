using PD2SPCompanion.Core.Graphics;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Which colour an item's inventory picture is drawn in: its dye, its unique or set row's own, or
/// its affixes'. The maps the colour goes through are <see cref="ColourMaps"/>'; this is the
/// item's side of it.
/// </summary>
public static class ItemTints
{
    /// <summary>
    /// Each magic affix's colour code where its <c>transform</c> is set, by the id an item stores:
    /// a prefix or suffix id is its data row, an automagic id its row counted from one.
    /// </summary>
    private static string?[] _prefixColours = [], _suffixColours = [], _autoColours = [];

    public static void Load(GameData game)
    {
        _prefixColours = Colours(game, "magicprefix");
        _suffixColours = Colours(game, "magicsuffix");
        _autoColours = Colours(game, "automagic");
    }

    private static string?[] Colours(GameData game, string name)
    {
        try
        {
            var (table, _) = game.ReadExcel(name);
            return [.. Enumerable.Range(0, table.Rows.Count).Select(row =>
                table.Value(row, "transform").Trim() == "1" && table.Value(row, "transformcolor").Trim() is { Length: > 0 } code
                    ? code
                    : null)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string? At(string?[] colours, int row) => row >= 0 && row < colours.Length ? colours[row] : null;

    /// <summary>
    /// A magic, rare or crafted item's colour from its affixes: <b>the first coloured prefix, in
    /// slot order; failing that, the first coloured suffix</b>; the automagic last. Measured on
    /// real items against the game: a Viper Bolt's first prefix (green)
    /// beat its second (dark gold); a Ghoul Razor's prefix (light gold) beat its suffix (black);
    /// Eagle Touch's second-slot prefix (dark gold) beat two coloured suffixes; Bramble Track, no
    /// prefix colour, took its suffix's. Where the automagic stands was not measured - none of
    /// those items has a coloured one.
    /// <para>
    /// <b>On the character it is the other way round</b> (<paramref name="suffixFirst"/>): the first
    /// coloured suffix, failing that the first coloured prefix. Measured against the appearance bytes
    /// the game wrote: PhySpear's crafted spear took "of Hypothermia"'s dark blue over
    /// two coloured prefixes; FigureTest's magic Short Sword "of Quickness"'s dark yellow over
    /// Massive's dark gold; its rare Breast Plate the first suffix's orange ("of Spikes") over the
    /// second's dark yellow and Saintly's dark gold. A suffix-less item taking its prefix's colour on
    /// the character is assumed from the inventory's rule, not measured.
    /// </para>
    /// </summary>
    private static string? AffixColour(D2SLib.Model.Save.Item item, bool suffixFirst = false)
    {
        string? First(UInt16[]? ids, string?[] colours)
        {
            foreach (int id in ids ?? [])
            {
                if (id > 0 && At(colours, id) is { } code)
                    return code;
            }
            return null;
        }

        string? found = suffixFirst
            ? First(item.MagicSuffixIds, _suffixColours) ?? First(item.MagicPrefixIds, _prefixColours)
            : First(item.MagicPrefixIds, _prefixColours) ?? First(item.MagicSuffixIds, _suffixColours);
        return found ?? (item.IsAutoAffix && item.AutoAffixId > 0 ? At(_autoColours, item.AutoAffixId - 1) : null);
    }

    /// <summary>
    /// How an item is tinted, or null. A dye first - it is put on the item, over whatever it was;
    /// then nothing for a skinned item, whose picture is its own (`invcap_se`, see
    /// ItemPresenter.Sprite); then a unique's or a set piece's own <c>invtransform</c> - the green
    /// of a Harlequin Crest, the orange of a Duriel's Shell (219 uniques, 113 set pieces in S13) -
    /// or a magic, rare or crafted item's affix colour (see <see cref="AffixColour"/>). All of it
    /// through the base's <c>InvTrans</c>: a base without one is never tinted (Alma Negra says
    /// black and is silver in game - its Parma has none). Checked against an in-game
    /// screenshot of stash, inventory and gear.
    /// </summary>
    /// <param name="worn">
    /// The colour on the character rather than in the inventory: the base's <c>Transform</c> and the
    /// row's <c>chrtransform</c> in place of <c>InvTrans</c> and <c>invtransform</c>, and a skin left out
    /// - it is an inventory picture. Matches the tint bytes the game writes into the save's appearance
    /// (CharacterFigure; checked on PhySpear).
    /// </param>
    public static Tint? Of(D2SLib.Model.Save.Item item, D2SLib.Model.TXT.TXTRow? baseRow,
                           D2SLib.Model.TXT.TXTRow? qualityRow = null, bool worn = false)
    {
        int table = int.TryParse(baseRow?.Get(worn ? "Transform" : "InvTrans")?.Value, out int t) ? t : 0;
        if (table <= 0)
            return null;

        int dye = ItemStats.Every(item)
            .FirstOrDefault(stat => string.Equals(stat.Stat, Dyes.Stat, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;

        if (Dyes.ColourOf(dye) is { } colour)
            return new Tint(table, colour);

        if (!worn && Skins.Has(item))
            return null;

        string? code = item.Quality is D2SLib.Model.Save.ItemQuality.Unique or D2SLib.Model.Save.ItemQuality.Set
            ? qualityRow?.Get(worn ? "chrtransform" : "invtransform")?.Value
            : item.Quality is D2SLib.Model.Save.ItemQuality.Magic or D2SLib.Model.Save.ItemQuality.Rare
                              or D2SLib.Model.Save.ItemQuality.Craft or D2SLib.Model.Save.ItemQuality.Tempered
                ? AffixColour(item, suffixFirst: worn)
                : null;

        return Dyes.RowOfCode(code) is { } own ? new Tint(table, own) : null;
    }
}
