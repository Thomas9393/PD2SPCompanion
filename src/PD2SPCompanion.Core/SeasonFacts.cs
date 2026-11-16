using D2SLib.Model.TXT;

using PD2SPCompanion.Core.Items;

namespace PD2SPCompanion.Core;

/// <summary>
/// What the code knows about one season that no table says, listed in one place. Each fact says where it is written and how it reads
/// against the installed tables now; <c>pd2spc seasonfacts</c> prints them and <c>tests/season.py</c>
/// compares them with the last season's answers, so a new season shows which to look at.
/// <para>
/// A fact is here when the code carries it as a list or a constant. Rules that read the tables -
/// corruption ids that share an outcome, the desecration pool - follow a new season by themselves
/// and are not.
/// </para>
/// </summary>
public static class SeasonFacts
{
    /// <param name="Name">What it is.</param>
    /// <param name="Where">The code that holds it.</param>
    /// <param name="Check">How it reads against the installed tables: a line that changes when the season does.</param>
    /// <param name="AtRollover">What to do when its line changes.</param>
    public sealed record Fact(string Name, string Where, Func<string> Check, string AtRollover);

    public static IReadOnlyList<Fact> All { get; } =
    [
        new("Materials tab", "Items/Materials.cs (Codes), MaterialsViewModel blocks",
            () => Missing(Materials.Codes, code => ItemBases.Of(code) is not null, $"{Materials.Codes.Count} codes"),
            "A code gone or a new stackable material: the tab's slots follow the game's stash file, so check it in game"),

        new("Materials tab, Season 14's", "Items/Materials.cs (Season14Codes)",
            () => Missing(Materials.Season14Codes, code => ItemBases.Of(code) is not null, $"{Materials.Season14Codes.Count} codes"),
            "A map or gem code renamed or gone: the slots are cells of the game's stash file, so read each in game from a numbered file"),

        new("Shared stash layouts", "Saves/SharedStash.cs (Layout.Known)",
            () => string.Join("; ", Saves.SharedStash.Layout.Known.Select(StashLayout)),
            "A new VERSION in the file, or a slot this one cannot place: map the file's cells in game from a numbered file"),

        new("Corruption rolls", "Items/Crafting/Corruptions.cs (Thousand, MapRoll)",
            () => $"{Corruptions.Thousand} on gear, {Corruptions.MapRoll} on maps; the recipes set {string.Join(", ", Corruptions.RollHeights())}",
            "The cube's corruptnum moved: every band reads against these tops, so check the odds on the corruption ladder"),

        new("Unidentified tail", "D2S Model/Save/Items.cs (Item.UnidentifiedTailSince)",
            () => $"stamp {D2SLib.Model.Save.Item.UnidentifiedTailSince} on",
            "A unique or set item unidentified carries a byte more from this stamp: check an unidentified one of the new season reads back"),

        new("Skinned uniques", "Items/Skins.cs (Uniques)",
            () => Missing(Skins.Uniques, HasUniqueRow, $"{Skins.Uniques.Count} uniques"),
            "New _se pictures in the archives (season.py B10) mean new skins: add them from the wiki's Cosmetics page"),

        new("Kadala's Heirloom", "Items/KadalasHeirloom.cs (RowKey, variants)",
            () => KadalasHeirloom.FileIndex is >= 0 and int row ? $"row {row}" : $"no row \"{KadalasHeirloom.RowKey}\"",
            "A moved row: an old ring keeps its FileIndex, so settle which row reads \"Kadala's Heirloom\" in game"),

        new("Desecration values", "Items/Crafting/Desecrations.cs (ForDamage, ForOthers)",
            () => $"{Desecrations.ForDamage} for enhanced damage, {Desecrations.ForOthers} the rest; "
                  + $"{Desecrations.ValueStat} {Bits(Desecrations.ValueStat)}",
            "A wider field or other values: check desecrated amulets from the market (checklist A3)"),

        new("Item version stamp", "the season's snapshot (season.json ItemVersion)",
            () => Saves.SeasonReading.CurrentStamp is { } stamp ? stamp.ToString() : "not known yet",
            "Taken from the first character of the season opened; a migration writes it"),

        new("Shipped item stamps", "SeasonSnapshot.cs (KnownStamps)",
            () => string.Join(", ", SeasonSnapshot.KnownStamps.OrderBy(known => known.Key, StringComparer.Ordinal).Select(known => $"{known.Key} {known.Value}"))
                  + (StatEncoding.PreviousSeason is { Length: > 0 } before && !SeasonSnapshot.KnownStamps.ContainsKey(before) ? $"; none for {before}, the season before" : ""),
            "Add the season's stamp once it is seen: a new install reads last season's items by this list alone"),

        new("Shipped class values", "SeasonSnapshot.cs (KnownClassValues)",
            () => string.Join(", ", SeasonSnapshot.KnownClassValues.Keys.Order(StringComparer.Ordinal))
                  + (StatEncoding.PreviousSeason is { Length: > 0 } before && !SeasonSnapshot.KnownClassValues.ContainsKey(before) ? $"; none for {before}, the season before" : ""),
            "Add the season's charstats.txt columns at the rollover: a new install moves a character's base by this list alone"),

        new("Previous-season columns", "the tables' Save Bits <season> column (StatEncoding.PreviousSeason)",
            () => StatEncoding.PreviousSeason is { Length: > 0 } previous ? previous : "none",
            "Names the season before: the vault and its migration follow it. None means the vault stays closed until the editor is updated"),

        new("Trade price starters", "Shell Services/Settings/TradePriceStarters.cs",
            () => "not checked: market prices, set by hand",
            "Measure the season's rates in game; raise Version"),

        new("Map seed starters", "Shell Services/Settings/MapSeedStarters.cs",
            () => "not checked: community seeds",
            "Check a few in game; raise Version when seeds change"),
    ];

    /// <summary>A stash layout's line: its version, and whether it places every materials slot there is to place.</summary>
    private static string StashLayout(Saves.SharedStash.Layout layout)
    {
        int slots = Materials.Codes.Count + Materials.Season14Codes.Count;
        int placed = Enumerable.Range(1, slots).Count(slot => layout.MaterialAt(slot) is not null);
        return $"v{layout.Version} places {placed}/{slots}";
    }

    /// <summary>Each fact's line, a failure said as one.</summary>
    public static IReadOnlyList<(Fact Fact, string State)> Read() =>
        [.. All.Select(fact =>
        {
            try
            {
                return (fact, fact.Check());
            }
            catch (Exception problem)
            {
                return (fact, $"failed: {problem.Message}");
            }
        })];

    private static string Missing(IEnumerable<string> listed, Func<string, bool> found, string what)
    {
        var missing = listed.Where(each => !found(each)).ToList();
        return missing.Count == 0 ? $"{what}, all in the tables" : $"{what}, not in the tables: {string.Join(", ", missing)}";
    }

    private static bool HasUniqueRow(string index) =>
        D2SLib.Core.TXT.ItemsTXT.UniqueTXT?.Rows.Any(row =>
            string.Equals(row["index"]?.Value?.Trim(), index, StringComparison.OrdinalIgnoreCase)) == true;

    private static string Bits(string stat) =>
        D2SLib.Core.TXT.ItemStatCostTXT[stat] is { } row ? $"{StatEncoding.SaveBits(row)} bits" : "not in the tables";
}
