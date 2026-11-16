using System.Text.RegularExpressions;

namespace PD2SPCompanion.Core.Testbed;

/// <summary>What changing the drops did, so the panel can say it.</summary>
public sealed record DropReport(int EveryKill, int Materials, int HighRunes, int Specials = 0)
{
    public override string ToString() =>
        $"{EveryKill} kill classes always drop, {Materials} material weights and {HighRunes} high rune weights raised, "
        + $"{Specials} special item chances raised";
}

/// <summary>
/// Builds the loose <c>treasureclassex.txt</c> that changes what monsters drop, without writing
/// anything - <see cref="DensityInstaller"/> writes it, as it does the density tables.
/// <para>
/// Three changes, each kept to what it is meant to touch: what already drops several is approached carefully.
/// </para>
/// <list type="bullet">
/// <item><b>Every kill drops</b>: no-drop 0 on ordinary monsters' main class only - the classes
/// monstats names as <c>TreasureClass1</c> that make a single pick. Champions (-2), uniques (-3)
/// and bosses (5-7 picks) already drop several and are left alone; so are the nested classes,
/// several of which use no-drop on purpose (crafting infusions, the Void map's potions). The
/// engine drops at most 6 items a kill and scales no-drop by player count, which 0 makes moot.
/// Not a kill class that holds nothing but potions: the Void map's monsters (Rathma's map) drop
/// from "Map Void" - full rejuvenations, antidotes and mana, no drop 400 - and at 0 every kill in
/// the uber fight gave a potion, three in four of them full rejuvenations.</item>
/// <item><b>The drop panel's multiples</b> (DropBoosts), each entry its own: a material's weight wherever a
/// class lists it - the shard, the keys, the essences, the infusions, the map orbs, the scarab (the page's one
/// material box retired into them); a rune's in the rune chain, the fall-backs to the class below
/// left as they are; a gem
/// tier's wherever listed; a special item's or a zone's chance, its <c>Unique</c> x <c>Set</c> divided.</item>
/// <item><b>Chipped and flawed gems left out</b> of the champions' and uniques' drops (DropBoosts.LowGemsOut):
/// their weight 0 in every Good class.</item>
/// </list>
/// </summary>
public static class DropOverride
{
    /// <summary>The first high rune: Lem, #20.</summary>
    public const int FirstHighRune = 20;

    /// <summary>The last rune: Zod, #33.</summary>
    public const int LastRune = 33;

    private static Regex RuneClass => DropBoosts.RuneClass;

    private static Regex RuneCode => DropBoosts.RuneCode;

    /// <param name="boosts">
    /// The drop panel's multiples, by entry (DropBoosts): a rune's, a gem tier's, a material's, a special item's - and
    /// its switch leaving chipped and flawed gems out of the champions' drops.
    /// </param>
    public static (ModdedFile File, DropReport Report) Build(
        GameData game, bool everyKill, IReadOnlyDictionary<string, int>? boosts)
    {
        var (table, _) = game.ReadExcel("treasureclassex");

        int name = table.IndexOfColumn("Treasure Class"), picks = table.IndexOfColumn("Picks"),
            noDrop = table.IndexOfColumn("NoDrop"), unique = table.IndexOfColumn("Unique"), set = table.IndexOfColumn("Set");
        if (name < 0 || picks < 0 || noDrop < 0)
            throw new InvalidDataException("treasureclassex.txt has no Treasure Class, Picks or NoDrop column.");

        var slots = Enumerable.Range(1, 10)
            .Select(n => (Item: table.IndexOfColumn($"Item{n}"), Prob: table.IndexOfColumn($"Prob{n}")))
            .Where(slot => slot.Item >= 0 && slot.Prob >= 0)
            .ToList();

        var kills = everyKill ? KillClasses(game) : [];
        var potions = everyKill ? PotionCodes(game) : [];

        int killed = 0, raised = 0, runes = 0, specials = 0;
        foreach (string[] cells in table.Rows)
        {
            if (name >= cells.Length)
                continue;

            string className = cells[name].Trim();

            // A special item's or a zone's row: no item, and the chance PD2's code reads, one in Unique x Set.
            if (unique >= 0 && unique < cells.Length && DropBoosts.SpecialKeyOf(className) is { } special
                && DropBoosts.Of(boosts, special) is > 1 and int boost
                && slots.All(slot => slot.Item >= cells.Length || cells[slot.Item].Trim().Length == 0)
                && int.TryParse(cells[unique].Trim(), out int chance) && chance > 0)
            {
                int? second = set >= 0 && set < cells.Length && int.TryParse(cells[set].Trim(), out int held) && held > 0 ? held : null;
                var (lowered, setLowered) = DropBoosts.Divided(chance, second, boost);
                cells[unique] = lowered.ToString();
                if (setLowered != second && set >= 0 && set < cells.Length)
                    cells[set] = setLowered?.ToString() ?? "";
                specials++;
            }

            if (kills.Contains(className) && noDrop < cells.Length
                && int.TryParse(cells[noDrop].Trim(), out int nothing) && nothing > 0
                && picks < cells.Length && cells[picks].Trim() == "1"
                && !OnlyPotions(cells, slots, potions))
            {
                cells[noDrop] = "0";
                killed++;
            }

            foreach (var (item, prob) in slots)
            {
                if (Math.Max(item, prob) >= cells.Length || !int.TryParse(cells[prob].Trim(), out int weight))
                    continue;

                string code = cells[item].Trim();

                if (DropBoosts.LeftOut(boosts, className, code))
                {
                    cells[prob] = "0";
                    raised++;
                }
                else if (DropBoosts.WeightedKeyOf(code, RuneClass.IsMatch(className)) is { } entry
                         && DropBoosts.Of(boosts, entry) is > 1 and int times)
                {
                    // The panel's own: a high rune in the rune chain, the shard and the keys wherever listed.
                    cells[prob] = (weight * times).ToString();
                    if (RuneCode.IsMatch(code))
                        runes++;
                    else
                        raised++;
                }
            }
        }

        return (new ModdedFile("treasureclassex", DensityOverride.Serialize(table)),
                new DropReport(killed, raised, runes, specials));
    }

    /// <summary>The classes monstats names as an ordinary kill's drop, in any difficulty.</summary>
    private static HashSet<string> KillClasses(GameData game)
    {
        var (monsters, _) = game.ReadExcel("monstats");

        int[] columns = [.. new[] { "TreasureClass1", "TreasureClass1(N)", "TreasureClass1(H)" }
            .Select(monsters.IndexOfColumn)
            .Where(column => column >= 0)];

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] cells in monsters.Rows)
        {
            foreach (int column in columns)
            {
                if (column < cells.Length && cells[column].Trim() is { Length: > 0 } tc)
                    found.Add(tc);
            }
        }

        return found;
    }

    /// <summary>Whether every item a class lists is a potion - a class "every kill drops" leaves as it is.</summary>
    private static bool OnlyPotions(string[] cells, List<(int Item, int Prob)> slots, HashSet<string> potions)
    {
        var listed = slots
            .Where(slot => slot.Item < cells.Length && cells[slot.Item].Trim().Length > 0)
            .Select(slot => cells[slot.Item].Trim())
            .ToList();

        return listed.Count > 0 && listed.All(potions.Contains);
    }

    /// <summary>Every potion's code, by its type in misc.txt: healing, mana, rejuvenation, stamina, antidote, thawing.</summary>
    private static HashSet<string> PotionCodes(GameData game)
    {
        var (misc, _) = game.ReadExcel("misc");
        int code = misc.IndexOfColumn("code"), type = misc.IndexOfColumn("type");

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (code < 0 || type < 0)
            return found;

        foreach (string[] cells in misc.Rows)
        {
            if (Math.Max(code, type) < cells.Length
                && cells[type].Trim() is "hpot" or "mpot" or "rpot" or "spot" or "apot" or "wpot")
            {
                found.Add(cells[code].Trim());
            }
        }

        return found;
    }
}
