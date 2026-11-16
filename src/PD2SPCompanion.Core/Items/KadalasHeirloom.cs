using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Kadala's Heirloom, Gheed's map event ring - a unique whose row gives it nothing.
/// <para>
/// <b>Its row is <c>uniqueitems</c>' "Nethercrow"</b>: no base code, no properties, not enabled,
/// and the string table renames the key to "Kadala's Heirloom". Settled in game (a
/// test character): a ring written with FileIndex 352 reads "Kadala's Heirloom"; the five
/// "unused" rows beside it read "An Evil Force", the game's missing-string text.
/// </para>
/// <para>
/// PD2's event code gives it one of six variants and three lines every one has. Stated here as the
/// staff mods' 1-3 is - there is no table to read - from the PD2 wiki's page (a
/// screenshot), and matched by all five on the ladder's top 50: fire, cold, magic and
/// enhanced damage variants, gold 56-72, magic find 21-30, prices 6-10, level requirement 75.
/// </para>
/// </summary>
public static class KadalasHeirloom
{
    /// <summary>The row's key, which the string table shows as the ring's name.</summary>
    public const string RowKey = "Nethercrow";

    private static readonly (string Code, int Min, int Max, int Param)[] Every =
    [
        ("gold%", 50, 75, 0), ("mag%", 20, 30, 0), ("cheap", 5, 10, 0), ("levelreq", 75, 75, 0),
    ];

    /// <summary>The variants, each told by the one stat only it writes.</summary>
    private static readonly (string Tells, (string Code, int Min, int Max, int Param)[] Grants)[] Variants =
    [
        ("item_elemskill_fire", [("fireskill", 1, 1, 0), ("res-cold", -10, -20, 0), ("res-fire", 30, 40, 0)]),
        ("item_elemskill_cold", [("coldskill", 1, 1, 0), ("res-fire", -10, -20, 0), ("res-cold", 30, 40, 0)]),
        ("item_elemskill_lightning", [("ltngskill", 1, 1, 0), ("res-pois", -10, -20, 0), ("res-ltng", 30, 40, 0)]),
        ("item_elemskill_poison", [("poisskill", 1, 1, 0), ("res-ltng", -10, -20, 0), ("res-pois", 30, 40, 0)]),
        // Magic damage reduction 9-10 by the wiki; a hardcore ring holds 7 (a level 99 Necromancer).
        // The item wins: 7-10 until one lower is seen.
        ("item_elemskill_magic", [("magskill", 1, 1, 0), ("res-all", -3, -5, 0), ("red-mag", 7, 10, 0)]),
        ("item_maxdamage_percent", [("dmg%", 30, 30, 0), ("res-all", -3, -5, 0), ("red-dmg", 9, 10, 0)]),
    ];

    private static object? _table;
    private static int _fileIndex = -1;

    /// <summary>The FileIndex of the row, counted as <see cref="ItemForge.Unique"/> counts; -1 without one.</summary>
    public static int FileIndex
    {
        get
        {
            var table = D2SLib.Core.TXT.ItemsTXT.UniqueTXT;
            if (table is null)
                return -1;
            if (ReferenceEquals(table, _table))
                return _fileIndex;

            int counted = -1, found = -1;
            foreach (var row in table.Rows)
            {
                string index = row["index"]?.Value?.Trim() ?? "";
                if (index.Length == 0 || index.Equals("Expansion", StringComparison.OrdinalIgnoreCase))
                    continue;
                counted++;
                if (index.Equals(RowKey, StringComparison.OrdinalIgnoreCase))
                {
                    found = counted;
                    break;
                }
            }

            (_table, _fileIndex) = (table, found);
            return found;
        }
    }

    public static bool Is(Item item) =>
        item.Quality == ItemQuality.Unique && FileIndex >= 0 && item.FileIndex == (uint)FileIndex;

    /// <summary>
    /// What this ring's properties came from: the lines every one has, and the variant its own
    /// lines name - or every variant, where they name none.
    /// </summary>
    public static IEnumerable<(string Code, int Min, int Max, int Param)> GrantsFor(Item item)
    {
        var held = ItemStats.Every(item).Select(stat => stat.Stat ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var variant = Variants.FirstOrDefault(each => held.Contains(each.Tells));

        return variant.Grants is { } grants
            ? Every.Concat(grants)
            : Every.Concat(Variants.SelectMany(each => each.Grants));
    }
}
