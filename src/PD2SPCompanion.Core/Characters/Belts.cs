using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// The belt: how many potion boxes a belt gives, what may go in one, and which box it goes to.
/// <para>
/// <b>Size.</b> <c>armor.txt</c>'s <c>belt</c> column names a row of <c>belts.txt</c>, whose
/// <c>numboxes</c> is 4, 8, 12 or 16 - one to four rows of four. No belt worn is the
/// <c>default</c> row's 4.
/// </para>
/// <para>
/// <b>What goes in.</b> Item types <c>itemtypes.txt</c> marks <c>Beltable</c>, a type taking its
/// parents' flag through <c>Equiv1</c>/<c>Equiv2</c>. In PD2 that is potions of every kind,
/// elixirs and scrolls.
/// </para>
/// <para>
/// <b>Where it goes</b> (from the game's Shift+click): a box index is
/// <c>row * 4 + column</c>, row 0 the bottom. A column fills from the bottom up. Asked for a
/// column, an item takes that column's lowest free box. Asked for none, it goes to a column
/// whose potions are its own kind, else to an empty column, else nowhere - "Col 2 has 1 hp:
/// Shift + click a hp will go to Col 2. Unless Col 2 is full, and also Col 3 and 4 are either
/// full or not hp related, then hp go to Col 1." A column asked for and full "fills the room":
/// the same again, and then any column with a free box.
/// </para>
/// <para>
/// <b>Nothing floats.</b> Take a potion out and the ones above it drop a box, as in play -
/// see <see cref="Settle"/>.
/// </para>
/// </summary>
public static class Belts
{
    private static int[] _boxes = [];
    private static HashSet<string> _beltable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What no belt holds: the bottom row.</summary>
    public const int Default = 4;

    /// <summary>Boxes to a row.</summary>
    public const int Columns = 4;

    public static void Load(GameData game)
    {
        try
        {
            var (belts, _) = game.ReadExcel("belts");
            var found = new List<int>();
            for (int row = 0; row < belts.Rows.Count; row++)
                found.Add(int.TryParse(belts.Value(row, "numboxes").Trim(), out int boxes) ? boxes : 0);
            _boxes = [.. found];
        }
        catch (Exception)
        {
            // Without the table every belt holds the default row: less shown, nothing wrong.
            _boxes = [];
        }

        _beltable = ReadBeltable(game);
    }

    /// <summary>The item types that go in a belt, each with its parents' flag.</summary>
    private static HashSet<string> ReadBeltable(GameData game)
    {
        var beltable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var (types, _) = game.ReadExcel("itemtypes");
            var parents = new Dictionary<string, (string First, string Second, bool Flagged)>(StringComparer.OrdinalIgnoreCase);

            for (int row = 0; row < types.Rows.Count; row++)
            {
                string code = types.Value(row, "Code").Trim();
                if (code.Length > 0)
                {
                    parents[code] = (types.Value(row, "Equiv1").Trim(), types.Value(row, "Equiv2").Trim(),
                                     types.Value(row, "Beltable").Trim() == "1");
                }
            }

            bool Flagged(string type, HashSet<string> walked) =>
                walked.Add(type) && parents.TryGetValue(type, out var entry)
                && (entry.Flagged
                    || (entry.First.Length > 0 && Flagged(entry.First, walked))
                    || (entry.Second.Length > 0 && Flagged(entry.Second, walked)));

            foreach (string type in parents.Keys)
            {
                if (Flagged(type, new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                    beltable.Add(type);
            }
        }
        catch (Exception)
        {
            // Without the table nothing is known to go in a belt, so nothing is put in one.
        }

        return beltable;
    }

    /// <summary>The boxes a belt base gives - its <c>belt</c> row's <c>numboxes</c> - or <see cref="Default"/>.</summary>
    public static int BoxesFor(string? code)
    {
        string row = D2SLib.Core.TXT.ItemsTXT.GetByCode(code?.Trim() ?? "")?.Get("belt")?.Value?.Trim() ?? "";
        return int.TryParse(row, out int index) && index >= 0 && index < _boxes.Length && _boxes[index] > 0
            ? _boxes[index]
            : Default;
    }

    /// <summary>Rows of four a belt base gives - one with none worn.</summary>
    public static int RowsFor(string? code) => Math.Max(1, (BoxesFor(code) + Columns - 1) / Columns);

    /// <summary>An item's own type (<c>hpot</c>, <c>mpot</c>, <c>rpot</c>, <c>scro</c>...), which is its kind in a belt.</summary>
    public static string? KindOf(Item item) =>
        D2SLib.Core.TXT.ItemsTXT.GetByCode(item.Code?.Trim() ?? "")?.Get("type")?.Value?.Trim() is { Length: > 0 } type
            ? type
            : null;

    /// <summary>Whether an item may go in a belt.</summary>
    public static bool Takes(Item item) => KindOf(item) is { } kind && _beltable.Contains(kind);

    /// <summary>
    /// The box an item of <paramref name="kind"/> goes to, or null for none - see the class
    /// notes. <paramref name="taken"/> is every box in use and the kind in it.
    /// </summary>
    /// <param name="column">The column it was dropped on; null for the game's own choice.</param>
    public static int? BoxFor(string? kind, IReadOnlyDictionary<int, string?> taken, int rows, int? column)
    {
        int? Lowest(int col)
        {
            for (int row = 0; row < rows; row++)
            {
                int box = row * Columns + col;
                if (!taken.ContainsKey(box))
                    return box;
            }

            return null;
        }

        // What a column holds: the kind of its bottom potion, or null while it is empty.
        (bool Empty, string? Kind) Column(int col)
        {
            for (int row = 0; row < rows; row++)
            {
                if (taken.TryGetValue(row * Columns + col, out string? held))
                    return (false, held);
            }

            return (true, null);
        }

        if (column is { } asked && asked is >= 0 and < Columns && Lowest(asked) is { } there)
            return there;

        for (int col = 0; col < Columns; col++)
        {
            var (empty, held) = Column(col);
            if (!empty && kind is not null && string.Equals(held, kind, StringComparison.OrdinalIgnoreCase)
                && Lowest(col) is { } same)
            {
                return same;
            }
        }

        for (int col = 0; col < Columns; col++)
        {
            if (Column(col).Empty)
                return col;
        }

        // A column asked for and full fills the room: any box left.
        if (column is not null)
        {
            for (int col = 0; col < Columns; col++)
            {
                if (Lowest(col) is { } any)
                    return any;
            }
        }

        return null;
    }

    /// <summary>
    /// Drops every belt item to the lowest boxes of its column, keeping their order - a potion
    /// taken out lets the ones above it fall, as in play. True when anything moved.
    /// </summary>
    public static bool Settle(IEnumerable<Item> beltItems)
    {
        bool moved = false;

        foreach (var column in beltItems.GroupBy(item => item.X % Columns))
        {
            int row = 0;
            foreach (var item in column.OrderBy(item => item.X / Columns))
            {
                int box = row * Columns + column.Key;
                if (item.X != box)
                {
                    item.X = (byte)box;
                    moved = true;
                }

                row++;
            }
        }

        return moved;
    }
}
