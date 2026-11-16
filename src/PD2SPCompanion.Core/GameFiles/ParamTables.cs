namespace PD2SPCompanion.Core.GameFiles;

/// <summary>
/// The monsters a property can name, from <c>monstats.txt</c>.
/// <para>
/// <c>item_reanimate</c> keeps one of these in its parameter and a percentage in its value,
/// which is how "10% Chance to Reanimate as: Returned" is stored. The number is the row's
/// <c>hcIdx</c> - its place in the table - not the <c>Id</c> string beside it.
/// </para>
/// </summary>
public static class MonsterNames
{
    private static Dictionary<int, string> _byId = [];

    /// <summary>Every monster that can be named, in table order.</summary>
    public static IReadOnlyList<(int Id, string Name)> All { get; private set; } = [];

    public static string? NameOf(int id) =>
        _byId.TryGetValue(id, out string? name) ? name : null;

    public static void Load(GameData game)
    {
        var found = new Dictionary<int, string>();

        try
        {
            var (table, _) = game.ReadExcel("monstats");

            for (int row = 0; row < table.Rows.Count; row++)
            {
                string index = table.Value(row, "hcIdx").Trim();
                if (!int.TryParse(index, out int id))
                    continue;

                // The printed name is a string key, and plenty of monsters resolve to the
                // key itself - "Skeleton" is both. Treating that as a failed lookup, the way
                // a stat's wording has to, fell back to the internal id and put "skeleton1"
                // on the row. Here the key is a perfectly good name, so only an empty answer
                // is a failure.
                string key = table.Value(row, "NameStr").Trim();
                string shown = key.Length > 0 ? StringTable.Current?.Get(key) ?? "" : "";

                if (shown.Length == 0)
                    shown = key.Length > 0 ? key : table.Value(row, "Id").Trim();

                if (shown.Length > 0)
                    found.TryAdd(id, shown);
            }
        }
        catch (Exception)
        {
            // Without the table a monster shows as its number, which says less but says
            // nothing wrong.
        }

        _byId = found;
        All = [.. found.OrderBy(entry => entry.Key).Select(entry => (entry.Key, entry.Value))];
    }
}

/// <summary>
/// The states a property can name, from <c>states.txt</c>.
/// <para>
/// The <c>state</c> stat keeps one in its parameter and a single bit as its value - it is on
/// or it is not. PD2 uses them for the cosmetic auras a set or a unique paints on, which is
/// why the names read as internals rather than as anything the game prints: there is no
/// wording for a state anywhere in the tables.
/// </para>
/// </summary>
public static class StateNames
{
    private static Dictionary<int, string> _byId = [];

    public static IReadOnlyList<(int Id, string Name)> All { get; private set; } = [];

    public static string? NameOf(int id) =>
        _byId.TryGetValue(id, out string? name) ? name : null;

    /// <summary>
    /// The id a state's name stands for, or null where nothing does. <c>uniqueitems.txt</c>
    /// writes five of these into a <c>par</c> column by name - <c>dcloneset</c>,
    /// <c>maxlevelset</c>, <c>devaura</c>, <c>rathmaset</c> - the same way it writes skills.
    /// </summary>
    public static int? IdOf(string name)
    {
        foreach (var (id, known) in All)
        {
            if (string.Equals(known, name.Trim(), StringComparison.OrdinalIgnoreCase))
                return id;
        }

        return null;
    }

    public static void Load(GameData game)
    {
        var found = new Dictionary<int, string>();

        try
        {
            var (table, _) = game.ReadExcel("states");

            for (int row = 0; row < table.Rows.Count; row++)
            {
                string name = table.Value(row, "state").Trim();

                if (name.Length > 0 && int.TryParse(table.Value(row, "id").Trim(), out int id))
                    found.TryAdd(id, name);
            }
        }
        catch (Exception)
        {
            // As above: no names rather than wrong ones.
        }

        _byId = found;
        All = [.. found.OrderBy(entry => entry.Key).Select(entry => (entry.Key, entry.Value))];
    }
}
