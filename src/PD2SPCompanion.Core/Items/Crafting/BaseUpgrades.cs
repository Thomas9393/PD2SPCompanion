using System.Text;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Moving an item's base one tier up - normal to exceptional, exceptional to elite - as PD2's
/// Horadric Cube upgrade recipes do.
/// <para>
/// <b>What the game does</b> (<c>cubemain.txt</c>): rows whose output is
/// <c>useitem,mod,exc</c> or <c>useitem,mod,eli</c>, for unique, set, rare and crafted items
/// only - no white, magic or runeword. The item is kept, its base changes, and some rows add a
/// mod: staves <c>cast2 20</c>, spiked shields thorns 14 then 64, bone shields <c>red-mag 2</c>,
/// arrows <c>dmg-max 8</c>, bolts <c>pierce-phys 2</c>. Those are exactly the step between the
/// tiers of the base's own automagic (Staff Tier 1-3: 10, 30, 50 cast rate) - the item keeps
/// the automagic it dropped with, and the recipe adds what the new tier's would have given.
/// A unique or set piece has no automagic at all, and gets the mod anyway.
/// </para>
/// </summary>
public static class BaseUpgrades
{
    /// <param name="Type">The item type or base code the row takes - <c>staf</c>, <c>armo</c>, <c>8ls</c>.</param>
    /// <param name="From">The tier upgraded from: 0 normal, 1 exceptional.</param>
    /// <param name="Quality">The quality the row takes - <c>uni</c>, <c>set</c>, <c>rar</c>, <c>crf</c>.</param>
    private sealed record Step(string Type, int From, string Quality, IReadOnlyList<CorruptionGrant> Mods);

    private static List<Step> _steps = [];

    /// <summary>Each tiered base's <c>auto prefix</c> group, by code.</summary>
    private static Dictionary<string, string> _autoGroupOf = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The qualities the game's own recipes take.</summary>
    public static bool GameUpgrades(ItemQuality quality) =>
        quality is ItemQuality.Unique or ItemQuality.Set or ItemQuality.Rare or ItemQuality.Craft;

    /// <summary>
    /// Whether this item can go up a tier. <paramref name="anyItem"/> lets in what no recipe
    /// takes - white, superior, magic, runeword - which free editing allows.
    /// <para>
    /// Never a quest item: the Staff of Kings and the Horadric Staff share the staves' family
    /// codes and would step up into a Gothic Staff, losing the quest.
    /// </para>
    /// </summary>
    public static bool CanUpgrade(Item item, bool anyItem) =>
        !item.IsCompact
        && !ItemBases.IsQuestItem(item.Code)
        && ItemTier.Next(item.Code) is not null
        && (anyItem || (GameUpgrades(item.Quality) && !item.IsRuneword));

    /// <summary>
    /// The current durability an upgrade leaves: rolled, from half the new maximum to one under it. See
    /// <see cref="Upgrade"/> for what was measured.
    /// </summary>
    private static int Rolled(int most, Random random) =>
        ItemBases.DurabilityRoll(most) is (int low, int high) ? random.Next(low, high + 1) : most;

    /// <summary>
    /// Moves the item's base one tier up, and returns what changed. Everything else is kept -
    /// quality, affixes, the unique or set row, runeword, sockets and what fills them.
    /// <list type="bullet">
    /// <item>Defence keeps its place in the base's range: a roll at 40%
    /// of the old range lands at 40% of the new one; one above the range - <c>maxac + 1</c>, an
    /// armour that spawned with enhanced defence - stays one above; an ethereal one is taken
    /// back to its roll first and the quarter put on again.</item>
    /// <item>Durability follows the new base, and <b>the current figure is rolled anew</b>, between half the
    /// new maximum and one under it - it does not follow what the item had. Measured in game:
    /// Circlets at 35, 30, 25 and 5 of 35 came out Coronets at 20, 22, 20 and 17 of 30; a Coronet at 20 of
    /// 30 a Tiara at 20 of 25; that Tiara a Diadem at 10 of 20. Six rolls, every one at or over half the
    /// maximum and under it. The editor kept the figure's share of the maximum, which the first of them -
    /// a full Circlet come out two thirds full - already said was wrong. (The bounds are what six rolls
    /// show, not the game's code: a roll outside them is not ruled out.)</item>
    /// <item>The recipe's mod is added where the game adds one.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> Upgrade(Item item, Random random)
    {
        var told = new List<string>();
        string from = item.Code.Trim();
        if (ItemTier.Next(from) is not { } to || ItemTier.Rank(from) is not { } rank)
            return told;

        var was = ItemBases.Of(from);
        var now = ItemBases.Of(to);

        if (item.Armor > 0 && was is { MinimumDefense: { } low, MaximumDefense: { } high }
                           && now is { MinimumDefense: { } newLow, MaximumDefense: { } newHigh })
        {
            int stored = item.Armor;
            int roll = item.IsEthereal ? ItemBases.BehindEthereal(stored) : stored;
            int moved = Placed(roll, low, high, newLow, newHigh);
            item.Armor = (ushort)Math.Clamp(item.IsEthereal ? ItemBases.Ethereal(moved) : moved, 0, 1747);
            told.Add($"defense {stored} -> {item.Armor}");
        }

        // Into a base with none - a Phase Blade: the ethereal one on the market, cubed up from a
        // Crystal Sword, stores 0 of 0.
        if (item.MaxDurability > 0 && now is { Durability: 0 })
        {
            told.Add($"durability {item.Durability}/{item.MaxDurability} -> none");
            item.MaxDurability = 0;
            item.Durability = 0;
        }
        else if (item.MaxDurability > 0 && was is { Durability: > 0 } && now is { Durability: > 0 })
        {
            int most = item.MaxDurability, left = item.Durability;
            int newMost = most == was.Durability
                ? now.Durability.Value
                : (int)Math.Round((double)most * now.Durability.Value / was.Durability.Value);
            newMost = Math.Clamp(newMost, 1, 255);

            item.MaxDurability = (ushort)newMost;
            item.Durability = (ushort)Rolled(newMost, random);
            told.Add($"durability {left}/{most} -> {item.Durability}/{item.MaxDurability}");
        }

        item.Code = to.PadRight(4, ' ');
        told.Insert(0, $"{from} -> {to}");

        if (StepFor(item.Quality, from, rank) is { Mods.Count: > 0 } step)
        {
            var values = new List<AffixRanges.Wrote>();
            foreach (var mod in step.Mods)
            {
                int value = mod.Max > mod.Min ? random.Next(mod.Min, mod.Max + 1) : mod.Min;
                values.AddRange(AffixRanges.Written(mod.Code, mod.Min, mod.Max, value, mod.Param));
            }

            ItemStats.WriteValues(item, values);
            told.Add($"recipe adds {string.Join(", ", values.Select(each => $"{each.Stat} {each.Value}"))}");
        }

        // A flat damage as the base holds it (DamageKinds): what every writer of an item ends on.
        DamageKinds.Settle(item);
        return told;
    }

    /// <summary>
    /// What upgrades have added to this item: the recipe's mod for every tier it has climbed
    /// since it dropped. Where it dropped is its unique or set row's base, or else the tier its
    /// automagic belongs to - Staff Tier 1 on a Gothic Staff climbed once. Nothing where
    /// neither says.
    /// </summary>
    public static IEnumerable<CorruptionGrant> Grants(Item item)
    {
        string code = item.Code?.Trim() ?? "";
        if (ItemTier.Rank(code) is not { } rank || rank == 0 || ItemTier.Family(code) is not { } family)
            yield break;

        int? start = null;
        if (item.Quality is ItemQuality.Unique or ItemQuality.Set)
        {
            if (ItemPresenter.RowBase(item) is { } dropped && ItemTier.SameFamily(dropped, code))
                start = ItemTier.Rank(dropped);
        }
        else if (item.IsAutoAffix && AffixRanges.AutomagicGroupOf(item.AutoAffixId) is { } group)
        {
            int at = family.ToList().FindIndex(each =>
                string.Equals(_autoGroupOf.GetValueOrDefault(each), group, StringComparison.OrdinalIgnoreCase));
            start = at >= 0 ? at : null;
        }

        for (int tier = start ?? rank; tier < rank; tier++)
        {
            if (StepFor(item.Quality, family[tier], tier) is { } step)
            {
                foreach (var mod in step.Mods)
                    yield return mod;
            }
        }
    }

    /// <summary>
    /// A roll's place in the old range, carried to the new one. At the top of a base with no
    /// range at all (a Ghoul Fringe's 3-3), it is the top. Outside the range: one above stays one
    /// above; anything else - a roll from an older season - keeps its ratio to the old maximum.
    /// </summary>
    private static int Placed(int roll, int low, int high, int newLow, int newHigh)
    {
        if (roll == high + 1)
            return newHigh + 1;
        if (roll > high || roll < low)
            return (int)Math.Round((double)roll * newHigh / Math.Max(1, high));
        if (high == low)
            return newHigh;

        return newLow + (int)Math.Round((double)(roll - low) * (newHigh - newLow) / (high - low));
    }

    /// <summary>
    /// The recipe that takes this base up from <paramref name="tier"/>: the first whose type
    /// the base is, of this quality where one is, else of any - a free upgrade of a white staff
    /// gets the cast rate a rare one would.
    /// </summary>
    private static Step? StepFor(ItemQuality quality, string code, int tier)
    {
        // Both its types, as the cube asks: the Bone Shield family's recipes name its second, bshi - by the first alone
        // no step was found, a Troll Nest cubed up from a Bone Shield lost the +2 magic damage reduction each step adds,
        // and its 7 took an "of Guarding" suffix it did not have (a level 93 Druid, the S14 ladder).
        var ancestry = ItemTypes.LineageOfBase(code);
        bool Takes(Step step) =>
            step.From == tier
            && (step.Type.Equals(code, StringComparison.OrdinalIgnoreCase) || ancestry.Contains(step.Type));

        string wanted = quality switch
        {
            ItemQuality.Unique => "uni",
            ItemQuality.Set => "set",
            ItemQuality.Rare => "rar",
            ItemQuality.Craft => "crf",
            _ => "",
        };

        return _steps.FirstOrDefault(step => Takes(step) && step.Quality == wanted)
               ?? _steps.FirstOrDefault(Takes);
    }

    public static void Load(GameData game)
    {
        var steps = new List<Step>();
        var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var rows = Rows(game, "cubemain");
            if (rows.Count > 0)
            {
                string[] header = rows[0];
                int input = Array.IndexOf(header, "input 1");
                int output = Array.IndexOf(header, "output");
                int live = Array.IndexOf(header, "enabled");

                var slots = Enumerable.Range(1, 5)
                    .Select(n => (
                        Code: Array.IndexOf(header, $"mod {n}"),
                        Low: Array.IndexOf(header, $"mod {n} min"),
                        High: Array.IndexOf(header, $"mod {n} max"),
                        Spot: Array.IndexOf(header, $"mod {n} param")))
                    .Where(slot => slot.Code >= 0 && slot.Low >= 0 && slot.High >= 0)
                    .ToArray();

                foreach (string[] cells in rows.Skip(1))
                {
                    if (input < 0 || output < 0 || cells.Length <= Math.Max(input, output))
                        continue;
                    if (live >= 0 && (cells.Length <= live || cells[live].Trim() != "1"))
                        continue;

                    string[] made = cells[output].Trim().Trim('"').Split(',');
                    if (made.Length < 2 || made[0].Trim() != "useitem"
                        || made[^1].Trim() is not ("exc" or "eli"))
                        continue;

                    string[] taken = cells[input].Trim().Trim('"').Split(',');
                    int from = taken.Contains("bas") ? 0 : taken.Contains("exc") ? 1 : -1;
                    if (from < 0)
                        continue;

                    string quality = taken.FirstOrDefault(each => each is "uni" or "set" or "rar" or "crf") ?? "";
                    var mods = new List<CorruptionGrant>();

                    foreach (var (code, low, high, spot) in slots)
                    {
                        if (cells.Length <= high || cells[code].Trim() is not { Length: > 0 } what)
                            continue;

                        _ = int.TryParse(cells[low].Trim(), out int min);
                        int max = int.TryParse(cells[high].Trim(), out int top) ? top : min;
                        int param = spot >= 0 && cells.Length > spot && int.TryParse(cells[spot].Trim(), out int p) ? p : 0;
                        mods.Add(new CorruptionGrant(what, min, Math.Max(min, max), param));
                    }

                    steps.Add(new Step(taken[0].Trim(), from, quality, mods));
                }
            }

            foreach (string table in new[] { "weapons", "armor", "misc" })
            {
                var lines = Rows(game, table);
                if (lines.Count == 0)
                    continue;

                int code = Array.IndexOf(lines[0], "code");
                int auto = Array.IndexOf(lines[0], "auto prefix");
                if (code < 0 || auto < 0)
                    continue;

                foreach (string[] cells in lines.Skip(1))
                {
                    if (cells.Length > Math.Max(code, auto) && cells[auto].Trim() is { Length: > 0 } group
                        && cells[code].Trim() is { Length: > 0 } id)
                        groups[id] = group;
                }
            }
        }
        catch (Exception problem)
        {
            // Without the tables the upgrade still moves the base; it adds no mod.
            Log.Caught(problem);
        }

        _steps = steps;
        _autoGroupOf = groups;
    }

    private static IReadOnlyList<string[]> Rows(GameData game, string table) => game.Rows(table);
}
