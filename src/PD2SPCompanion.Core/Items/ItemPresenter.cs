using PD2SPCompanion.Core.Graphics;
using D2SLib.Model.Save;
using D2SLib.Model.TXT;

namespace PD2SPCompanion.Core.Items;

/// <summary>What an item looks like and is called, resolved from the live game tables.</summary>
/// <param name="BaseName">
/// What the base item is called, whatever the item on top of it is called. A unique keeps
/// its own name and is still a pair of Light Plated Boots, and upgrading the base changes
/// this and not the name.
/// </param>
public sealed record ItemView(
    string Code,
    string Name,
    string? SpriteName,
    int GridWidth,
    int GridHeight,
    string? Warning,
    string BaseName = "",
    Tint? SpriteTint = null);

/// <summary>
/// Turns a parsed item into the name, sprite and grid footprint the game would show.
/// Lives in Core so the CLI dump and the UI cannot drift apart.
/// </summary>
public static class ItemPresenter
{
    /// <summary>The one base whose row an item never names: an ear carries no code.</summary>
    private const string EarCode = "ear";

    /// <summary>
    /// A unique's or set piece's own row - the one <see cref="Describe"/> names it by, checked the same
    /// way - or null for any other item, an ear, or a row that is not the item's.
    /// </summary>
    public static TXTRow? QualityRow(Item item)
    {
        if (item.IsEar)
            return null;
        Name(item, D2SLib.Core.TXT.ItemsTXT.GetByCode(item.Code), out _, out var row);
        return row;
    }

    public static ItemView Describe(Item item)
    {
        // An ear is the one item with no code at all. Where every other item spends those
        // bits on a base, an ear spends them on whose ear it is - the class in FileIndex,
        // the level in EarLevel and the name after them - so asking the tables for its base
        // asks them about null, and that threw rather than saying anything useful.
        if (item.IsEar)
            return DescribeEar(item);

        var tables = D2SLib.Core.TXT.ItemsTXT;
        string code = item.Code?.Trim() ?? "";
        var baseRow = tables.GetByCode(item.Code);

        string name = Name(item, baseRow, out string? warning, out TXTRow? qualityRow);
        return new ItemView(
            code,
            name,
            Sprite(item, baseRow, qualityRow),
            Size(baseRow, "invwidth"),
            Size(baseRow, "invheight"),
            warning,
            BaseNameOf(baseRow, code),
            ItemTints.Of(item, baseRow, qualityRow));
    }

    /// <summary>
    /// What an ear is called and looks like.
    /// <para>
    /// The row is real - <c>ear</c> in <c>misc.txt</c>, one cell, sprite <c>invear</c>, named
    /// through the string table like any other base - it is only the item that does not point
    /// at it. So the row is fetched by the code the item would have had.
    /// </para>
    /// </summary>
    private static ItemView DescribeEar(Item item)
    {
        var row = D2SLib.Core.TXT.ItemsTXT.GetByCode(EarCode);
        string plain = BaseNameOf(row, EarCode);

        string whose = item.PlayerName?.Trim() ?? "";
        string name = whose.Length > 0 ? $"{whose}'s {plain}" : plain;

        return new ItemView(
            EarCode,
            name,
            Sprite(item, row, null),
            Math.Max(1, Size(row, "invwidth")),
            Math.Max(1, Size(row, "invheight")),
            null,
            plain);
    }

    /// <summary>
    /// Just how many cells an item takes up.
    /// <para>
    /// Separate from <see cref="Describe"/>, which is the whole picture - name, sprite, and
    /// for a unique or set item a table row found by scanning for the marker row each time.
    /// That costs about 23 microseconds, which is nothing to show one item and a great deal
    /// to ask once per item per cell while looking for somewhere to put something. Size
    /// needs two columns of one row and none of the rest.
    /// </para>
    /// </summary>
    public static (int Width, int Height) FootprintOf(Item item) => FootprintOf(item.IsEar ? EarCode : item.Code);

    /// <summary>The cells a base takes, by its code: one by one for a code the tables do not have.</summary>
    public static (int Width, int Height) FootprintOf(string code)
    {
        var row = D2SLib.Core.TXT.ItemsTXT.GetByCode(code);
        return (Size(row, "invwidth"), Size(row, "invheight"));
    }

    /// <summary>
    /// A cell by whichever of these names the row happens to use, or empty where it uses
    /// none of them. The row's own indexer throws on a column it does not carry, so asking
    /// it about a column that may not be there has to be done at arm's length.
    /// </summary>
    private static string Column(TXTRow row, params string[] names)
    {
        // Asked, not caught: a missing column was an exception each time.
        foreach (string name in names)
        {
            if (row.Get(name)?.Value?.Trim() is { Length: > 0 } value)
                return value;
        }

        return "";
    }

    /// <summary>
    /// A unique or set item's name comes from its FileIndex, which is a row number in
    /// uniqueitems.txt / setitems.txt. Rows are kept exactly as the file has them, so
    /// the resolved row is checked against the item's own code: a mismatch means the
    /// indexing is off, and saying so beats showing a confident wrong name.
    /// </summary>
    private static string Name(Item item, TXTRow? baseRow, out string? warning, out TXTRow? qualityRow)
    {
        warning = null;
        qualityRow = null;
        var tables = D2SLib.Core.TXT.ItemsTXT;
        string code = item.Code?.Trim() ?? "";

        qualityRow = item.Quality switch
        {
            ItemQuality.Unique => RowAt(tables.UniqueTXT, (int)item.FileIndex),
            ItemQuality.Set => RowAt(tables.SetItemsTXT, (int)item.FileIndex),
            _ => null,
        };

        if (qualityRow is not null)
        {
            // The two tables do not agree on what the column is called: a unique's base is
            // in "code" and a set item's in "item". Asked for the wrong one, the row throws
            // rather than answering null, and the whole character failed to open the moment
            // one set item was in it.
            string rowCode = Column(qualityRow, "code", "item");
            string label = qualityRow["index"]?.Value ?? "?";
            // Another tier of the row's own base is the same unique upgraded - a Skull Collector
            // cubed from a Rune Staff to an Archon Staff keeps its name in game. Only a base
            // of another family means the row is not this item's.
            // A row naming no base whose key the string table knows is a unique PD2's own code
            // builds on whatever base it picks - Kadala's Heirloom, row "Nethercrow" (see
            // KadalasHeirloom). A blank row the strings do not know ("unused") reads "An Evil Force"
            // in game, and stays unreliable here.
            if (rowCode.Length == 0 && StringTable.Current?.Get(label) is { Length: > 0 } built)
                return built;

            if (!ItemTier.SameFamily(rowCode, code))
            {
                warning = $"FileIndex {item.FileIndex} resolves to '{label}', whose code is "
                          + $"'{rowCode}' and not '{code}' — row indexing is off, name unreliable";
                qualityRow = null;
            }
            else
            {
                // `index` is a key, not a name. The game looks it up the same way a base's
                // `namestr` is looked up, and PD2 renames through it - so the key stays put
                // while the shown name changes.
                //
                // This went unnoticed for a long time because the uniques we checked by hand
                // resolve to themselves: Hotspur, Tyrael's Might, Eaglehorn, Azurewrath. The
                // ones that do not were found by reading real items off the trade site -
                // `Wraithflight` is shown as "Wraith Flight", `Wartraveler` as "War
                // Traveler", and two are not spacing at all: `Sigurd's Staunch` is shown as
                // "Siggard's Staunch" and `Merman's Speed` as "Merman's Sprocket". The table
                // even carries a typo, `Thudergod's Vigor`, which the string table corrects.
                return StringTable.Current?.Get(label) is { Length: > 0 } shown ? shown : label;
            }
        }

        string plain = BaseNameOf(baseRow, code);

        // A runeword is named by the recipe, with the base beneath it the way a rare is:
        // "Nadir" over "Cap". The item itself stays whatever quality it was made from, so
        // nothing above this catches it.
        if (Runewords.On(item) is { Name.Length: > 0 } word)
            return Runewords.GameName(word);

        // A magic or rare item is named by its affixes over its base - Bone Coat, Crimson
        // Amulet of Craftsmanship - which is the name the game puts at the top of the
        // tooltip with the base underneath. Composed from the base name, so this is the
        // one place it is built and nothing downstream composes it a second time.

        return AffixNames.RareNameOf(item)
               ?? AffixNames.MagicNameOf(item, plain)
               ?? plain;
    }

    /// <summary>What a base item is called, by its code - the same lookup an item's name uses.</summary>
    public static string BaseName(string code) =>
        BaseNameOf(D2SLib.Core.TXT.ItemsTXT.GetByCode(code.Trim()), code.Trim());

    /// <summary>
    /// What the base item is called.
    /// <para>
    /// The tables hold two names and only one is meant to be read. <c>name</c> is the
    /// internal one - Charm Large, amulet - while <c>namestr</c> is a key into the string
    /// tables, which is where the words a player sees live: Grand Charm, Amulet. PD2 ships
    /// its own table, so this is also how its renames arrive.
    /// </para>
    /// </summary>
    private static string BaseNameOf(TXTRow? baseRow, string code)
    {
        string? key = baseRow?["namestr"]?.Value?.Trim();
        if (key is { Length: > 0 } && StringTable.Current?.Get(key) is { Length: > 0 } shown)
            return shown;

        return baseRow?["name"]?.Value ?? code;
    }

    /// <summary>
    /// The sprite's base file name. A unique or set item may override the base item's
    /// graphic; items flagged as having multiple graphics pick one by GraphicId.
    /// </summary>
    private static string? Sprite(Item item, TXTRow? baseRow, TXTRow? qualityRow)
    {
        // A desecrated amulet is drawn from its own picture's desecrated twin - invamu2 becomes
        // invamu2d, from pd2assets.mpq. The game swaps it whenever desecrated is non-zero and
        // leaves the stored picture alone (tested in game).
        string? drawn = Drawn(item, baseRow, qualityRow);

        // A skin has a picture of its own (Skins.PictureOf): Season 12's the item's name with `_se`, Season 14's named
        // after the unique. Drawn as it is: the white Shako is white with its lining painted in, and a white tint
        // washed the lining out (a screenshot).
        if (drawn is not null && Skins.Has(item) && SpriteLoader.Current is { } sprites
            && Skins.PictureOf(item, drawn, sprites.Has) is { } skinned)
        {
            drawn = skinned;
        }

        return drawn is not null
               && Corruptions.IsDesecrated(item)
               && SpriteLoader.Current?.Has(drawn + "d") == true
            ? drawn + "d"
            : drawn;
    }

    /// <summary>
    /// Whether the item's picture is one of its base's several, picked by the number the item stores
    /// (<see cref="Drawn"/>): a ring, an amulet, a jewel or a charm whose own row names no picture. Most
    /// unique and set ones are that - 30 of the 37 unique rings, amulets, jewels and charms and all 12
    /// set ones name none (S13) - so their number changes what is drawn as a magic one's does; Annihilus,
    /// the Hellfire Torch and Seraph's Hymn wear their row's picture whatever the number says.
    /// </summary>
    public static bool PictureIsPicked(Item item)
    {
        if (item.IsEar || item.IsCompact || ItemTypes.VariantCount(item.Code) <= 1)
            return false;

        var baseRow = D2SLib.Core.TXT.ItemsTXT.GetByCode(item.Code);
        _ = Name(item, baseRow, out _, out TXTRow? qualityRow);

        if (!string.IsNullOrWhiteSpace(qualityRow?["invfile"]?.Value))
            return false;

        string? byQuality = item.Quality switch
        {
            ItemQuality.Unique => baseRow is null ? null : Column(baseRow, "uniqueinvfile"),
            ItemQuality.Set => baseRow is null ? null : Column(baseRow, "setinvfile"),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(byQuality);
    }

    private static string? Drawn(Item item, TXTRow? baseRow, TXTRow? qualityRow)
    {
        string? overridden = qualityRow?["invfile"]?.Value;
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden;

        // The base carries a second and a third picture, for when one of it is made unique
        // or set - a Buckler is invbuc, and a unique or set one is invbucu. That is where
        // most of this art lives: only 1 of 129 set rows names a picture of its own, while
        // 153 bases name one for their set and unique versions. Read from the item's own row
        // alone, every set buckler in the game showed as a plain buckler.
        string? byQuality = item.Quality switch
        {
            ItemQuality.Unique => baseRow is null ? null : Column(baseRow, "uniqueinvfile"),
            ItemQuality.Set => baseRow is null ? null : Column(baseRow, "setinvfile"),
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(byQuality))
            return byQuality;

        string? invfile = baseRow?["invfile"]?.Value;
        if (string.IsNullOrWhiteSpace(invfile))
            return null;

        if (!item.HasMultipleGraphics)
            return invfile;

        // The type names its graphics outright, so they are read rather than composed.
        // Composing them from the base's invfile went wrong twice: a jewel says invgswe
        // and its files are invjw1..6, and the charms interleave in a pattern that had to
        // be worked out. itemtypes.txt says both, in InvGfx1..6.
        if (ItemTypes.VariantGraphic(item.Code, item.GraphicId) is { } named)
            return named;

        string varied = $"{invfile}{item.GraphicId + 1}";
        return SpriteLoader.Current?.Has(varied) == false ? invfile : varied;
    }

    private static int Size(TXTRow? row, string column)
    {
        string? value = row?[column]?.Value;
        return int.TryParse(value, out int size) && size > 0 ? size : 1;
    }

    /// <summary>
    /// The base a unique's or set piece's own row names - the one it drops on, whatever tier the
    /// item has been cubed up to since - or null.
    /// </summary>
    public static string? RowBase(Item item)
    {
        D2SLib.Model.TXT.TXTFile? file = item.Quality switch
        {
            ItemQuality.Unique => D2SLib.Core.TXT.ItemsTXT.UniqueTXT,
            ItemQuality.Set => D2SLib.Core.TXT.ItemsTXT.SetItemsTXT,
            _ => null,
        };

        return RowAt(file, (int)item.FileIndex) is { } row && Column(row, "code", "item") is { Length: > 0 } code
            ? code
            : null;
    }

    /// <summary>
    /// A unique or set item's own name in its table - the <c>index</c> column, "Annihilus" -
    /// which is what a cube recipe names when it takes one item and not a type. Null for any
    /// other quality.
    /// </summary>
    public static string? QualityIndex(Item item)
    {
        D2SLib.Model.TXT.TXTFile? file = item.Quality switch
        {
            ItemQuality.Unique => D2SLib.Core.TXT.ItemsTXT.UniqueTXT,
            ItemQuality.Set => D2SLib.Core.TXT.ItemsTXT.SetItemsTXT,
            _ => null,
        };

        return RowAt(file, (int)item.FileIndex)?["index"]?.Value?.Trim() is { Length: > 0 } name
            ? name
            : null;
    }

    /// <summary>
    /// The row a unique or set item's <c>FileIndex</c> names.
    /// <para>
    /// Not the same number: the game does not count the <c>Expansion</c> marker row, so
    /// everything from it on answers to the row before its own. See
    /// <see cref="QualityRows"/>, which has the measurement. The marker is found by its
    /// name each time rather than cached, since the tables here are the parser's own and a
    /// season can move it.
    /// </para>
    /// </summary>
    private static TXTRow? RowAt(TXTFile? file, int index)
    {
        if (file is null || index < 0)
            return null;

        int row = QualityRows.RowOf(index, MarkerIn(file));

        return row >= 0 && row < file.Rows.Count ? file.Rows[row] : null;
    }

    /// <summary>Where the uncounted marker sits in one of the parser's own tables.</summary>
    private static int MarkerIn(TXTFile file)
    {
        for (int row = 0; row < file.Rows.Count; row++)
        {
            if (string.Equals(file.Rows[row]["index"]?.Value?.Trim(), "Expansion",
                              StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return -1;
    }
}
