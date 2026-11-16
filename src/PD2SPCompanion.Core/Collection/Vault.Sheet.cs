namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// A vault item as its folder's sheet needs it before the item itself is read: its key, its name (what a typed
/// letter jumps to), the cells it takes and its quality - the game's number, which is its colour on the small map.
/// </summary>
public sealed record VaultLaid(long Key, string Name, int Width, int Height, int Quality);

/// <summary>
/// An item as a folder's sheet and its small map draw it: where the layout put it and the room it takes - the row
/// as a stretch of the sheet, counted from 0, and the row within it (<see cref="Vault.PageRows"/>) - and its
/// quality. Nothing of the item itself is read for it. Where it lies is the layout's word (<see cref="VaultLayout"/>),
/// made when the folder is shown; the vault keeps no place for an item.
/// </summary>
public sealed record VaultMark(long Key, int Page, int X, int Y, int Width, int Height, int Quality)
{
    /// <summary>The row of the sheet its top is on, from 0.</summary>
    public int Row => Page * Vault.PageRows + Y;
}

// A folder's sheet, laid out by a rule.
//
// A vault item once had a place of its own - a stretch of its folder's sheet, a column and a row, written
// in its row of the file (format 16, 17) - with everything that came of it: arrivals settled into the first free room,
// a drop on a cell, Sort as an act that wrote every place, Close gaps, a width the vault kept, and a restore that
// could find an item's place taken. After some days of use nobody cared about it so long as the content was readable
// immediately, and people tend to use sort or auto fit instead of arranging by hand (which folders partly cover).
// Format 19 drops the three columns and the width; a folder is laid out as it is shown,
// from its order, at the width of the page showing it.
public sealed partial class Vault
{
    /// <summary>
    /// How many rows a stretch of the sheet holds: what a view builds at a time. Fifteen, a shared stash page's
    /// rows - the pages a folder was in the older layout became its stretches.
    /// </summary>
    public const int PageRows = 15;

    /// <summary>The most stretches a view makes of a folder - a guard, far past anything a folder holds (35,000 items were some 640).</summary>
    public const int PageCap = 5000;

    /// <summary>The cells an item of this base takes.</summary>
    public static (int Width, int Height) FootprintOf(string baseCode)
    {
        var (width, height) = baseCode.Length > 0 ? Items.ItemPresenter.FootprintOf(baseCode) : (1, 1);
        return (Math.Clamp(width, 1, VaultLayout.WidestItem), Math.Clamp(height, 1, PageRows));
    }

    /// <summary>
    /// Every item of a folder in an order - Stored, Name, Rarity or Type, either way (<see cref="Orders"/>) - as
    /// the layout needs them (<see cref="VaultLayout"/>). Nothing of the items themselves is read for it: a
    /// folder of thousands is listed at once, and its items read a stretch at a time as they come into sight.
    /// </summary>
    public IReadOnlyList<VaultLaid> Footprints(long? folder, string by, bool descending)
    {
        var found = new List<VaultLaid>();

        using var read = _db.CreateCommand();
        read.CommandText = $"""
            SELECT i.key, COALESCE(m.name, ''), COALESCE(m.base_code, ''), COALESCE(m.quality, 0)
            FROM item i LEFT JOIN item_meta m ON m.item_key = i.key
            WHERE i.folder_id IS $folder
            ORDER BY {OrderOf(by, descending)};
            """;
        read.Parameters.AddWithValue("$folder", Db(folder));

        using var row = read.ExecuteReader();
        while (row.Read())
        {
            var (width, height) = FootprintOf(row.GetString(2));
            found.Add(new VaultLaid(row.GetInt64(0), row.GetString(1), width, height, row.GetInt32(3)));
        }

        return found;
    }

    /// <summary>
    /// A folder laid out: its items in the order, each where the rule puts it on a sheet so many columns wide -
    /// as marks, in the order a reader meets them (row by row, left to right) - and how many rows it takes.
    /// <paramref name="top"/> rows are left free over them and counted in: the folder's own folders stand there,
    /// first on its sheet, a folder as a fake item in the grid.
    /// </summary>
    /// <param name="besideFrom">
    /// The column the tiles' last row ends at, where it does not fill the sheet: items are laid beside it first, in
    /// the room the row leaves (<see cref="VaultLayout.Lay"/>'s <c>beside</c>). Zero for none.
    /// </param>
    public (IReadOnlyList<VaultMark> Marks, IReadOnlyList<VaultLaid> Items, int Rows) Sheet(long? folder, string by, bool descending, int columns, int top = 0,
                                                                                           int besideFrom = 0, int tileRows = 4)
    {
        var items = Footprints(folder, by, descending);
        (int, int)? beside = besideFrom > 0 && top >= tileRows ? (besideFrom, tileRows) : null;
        var sheet = VaultLayout.Lay([.. items.Select(item => new VaultFootprint(item.Key, item.Width, item.Height))], columns, VaultLayout.Look, beside);
        var quality = items.ToDictionary(item => item.Key, item => item.Quality);
        top = Math.Max(0, top);

        return ([.. sheet.Spots
                .OrderBy(spot => spot.Y).ThenBy(spot => spot.X)
                .Select(spot => new VaultMark(spot.Key, (spot.Y + top) / PageRows, spot.X, (spot.Y + top) % PageRows, spot.Width, spot.Height, quality[spot.Key]))],
            items, sheet.Rows + top);
    }

    /// <summary>
    /// What a search finds in one folder, by key: the folder's own items only - what the folder shown lights,
    /// and where on its small map its finds out of sight are.
    /// </summary>
    public IReadOnlyList<long> HitsIn(long? folder, VaultQuery query)
    {
        var found = new List<long>();

        using var read = _db.CreateCommand();
        read.CommandText = $"""
            SELECT i.key FROM item i
            LEFT JOIN item_meta m ON m.item_key = i.key
            WHERE i.folder_id IS $folder AND {Condition(query)}
            ORDER BY i.key;
            """;
        read.Parameters.AddWithValue("$folder", Db(folder));
        Bind(read, query);

        using var row = read.ExecuteReader();
        while (row.Read())
            found.Add(row.GetInt64(0));

        return found;
    }

    /// <summary>
    /// How many items a search finds in each folder that holds any, by the folder's id, and at the top
    /// (<paramref name="atTop"/>): which folders to say hold a find without opening them.
    /// </summary>
    public IReadOnlyDictionary<long, int> HitsPerFolder(VaultQuery query, out int atTop)
    {
        var found = new Dictionary<long, int>();
        atTop = 0;

        using var read = _db.CreateCommand();
        read.CommandText = $"""
            SELECT i.folder_id, COUNT(*) FROM item i
            LEFT JOIN item_meta m ON m.item_key = i.key
            WHERE {Condition(query)}
            GROUP BY i.folder_id;
            """;
        Bind(read, query);

        using var row = read.ExecuteReader();
        while (row.Read())
        {
            if (row.IsDBNull(0))
                atTop = row.GetInt32(1);
            else
                found[row.GetInt64(0)] = row.GetInt32(1);
        }

        return found;
    }
}
