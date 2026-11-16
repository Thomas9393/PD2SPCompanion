using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// What each set is called - "Hsarus' Defense" over the belt that belongs to it.
/// <para>
/// Two tables deep: a set item's row names its set in the <c>set</c> column, and
/// <c>sets.txt</c> turns that into a string key, which the string tables turn into the name
/// the game prints. The item itself records only its own row.
/// </para>
/// </summary>
public static class SetNames
{
    /// <summary>The set an item's FileIndex belongs to, and what it is called.</summary>
    private static Dictionary<int, string> _byIndex = [];

    /// <summary>The bases each set's pieces are made on, by the set's name.</summary>
    private static Dictionary<string, List<string>> _basesOf = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether any piece of the set is a class item - an amazon bow, a barbarian helm - which is
    /// what makes the wiki's Class-Focused sets what they are, and which a new set carries too.
    /// </summary>
    public static bool IsClassSet(string name) =>
        _basesOf.TryGetValue(name, out var bases) && bases.Any(code => ItemTypes.ClassOfBase(code) is not null);

    /// <summary>The sets of the classic game, by name: <c>sets.txt</c>'s <c>version</c> 0, before its Expansion marker.</summary>
    private static HashSet<string> _classic = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Which of the Compendium's Sets tiers a set is in - the tab's All / Common / Uncommon / Class (a
    /// screenshot): a class set (<see cref="IsClassSet"/>) is Class; of the rest, the classic game's are
    /// Common and the expansion's Uncommon. In S14 that is 7 class sets, 16 common and 9 uncommon.
    /// </summary>
    public static SetTier TierOf(string name) =>
        IsClassSet(name) ? SetTier.Class : _classic.Contains(name) ? SetTier.Common : SetTier.Uncommon;

    /// <summary>The set this item belongs to, or null for one that belongs to none.</summary>
    public static string? Of(Item item) =>
        item.Quality is ItemQuality.Set && _byIndex.TryGetValue((int)item.FileIndex, out string? name)
            ? name
            : null;

    public static void Load(GameData game)
    {
        var found = new Dictionary<int, string>();
        var basesOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var classic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var (sets, _) = game.ReadExcel("sets");
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int row = 0; row < sets.Rows.Count; row++)
            {
                string index = sets.Value(row, "index").Trim();
                if (index.Length == 0)
                    continue;

                // The name is a string key, and like a monster's it is often the key itself.
                string key = sets.Value(row, "name").Trim();
                string shown = key.Length > 0 ? StringTable.Current?.Get(key) ?? "" : "";

                named[index] = shown.Length > 0 ? shown : index;
                if (sets.Value(row, "version").Trim() == "0")
                    classic.Add(named[index]);
            }

            var (items, _) = game.ReadExcel("setitems");

            // Keyed by what an item calls the row rather than by the row, because the
            // game does not count the Expansion marker - see QualityRows. Keyed by the row,
            // every set item past the marker took the previous set's name.
            int marker = QualityRows.MarkerIn(items);

            for (int row = 0; row < items.Rows.Count; row++)
            {
                string set = items.Value(row, "set").Trim();
                int index = QualityRows.IndexOf(row, marker);

                if (set.Length > 0 && index >= 0 && named.TryGetValue(set, out string? name))
                {
                    found[index] = name;

                    if (!basesOf.TryGetValue(name, out var bases))
                        basesOf[name] = bases = [];
                    bases.Add(items.Value(row, "item").Trim());
                }
            }
        }
        catch (Exception)
        {
            // Without the tables a set bonus is headed by what it is rather than by whose
            // it is, which says less and says nothing wrong.
        }

        _byIndex = found;
        _basesOf = basesOf;
        _classic = classic;
    }
}

/// <summary>The Compendium's Sets tiers, as its tab names them (<see cref="SetNames.TierOf"/>).</summary>
public enum SetTier { Common, Uncommon, Class }
