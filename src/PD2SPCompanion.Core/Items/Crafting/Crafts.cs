using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Changing which craft recipe an item was made by.
/// <para>
/// A crafted item is an ordinary rare with three or four properties the recipe put on it.
/// No recipe id is stored, so the family is read back from those properties - see
/// <see cref="AffixRanges.CraftOn"/> - and reforging is therefore exactly what it looks
/// like: take the old family's properties off, put the new family's on. The affixes the
/// item rolled are not touched at all.
/// </para>
/// </summary>
public static class Crafts
{
    /// <summary>What a family will put on the item, rolled.</summary>
    public sealed record CraftDraft(
        AffixRanges.CraftRecipe Recipe,
        IReadOnlyList<AffixRanges.Wrote> Values)
    {
        public string Family => Recipe.Family;

        /// <summary>What it grants, in the game's own words.</summary>
        public IReadOnlyList<string> Lines => Describe(Values);
    }

    /// <summary>Rolled values in the game's own words - a craft's draft, a runeword's.</summary>
    public static IReadOnlyList<string> Describe(IEnumerable<AffixRanges.Wrote> values) =>
        [.. values.Select(v =>
            StatText.Describe(
                v.Stat, v.Value, SkillNames.NameOf(StatText.SkillOf(v.Stat, v.SkillId, v.Param)),
                v.SkillLevel, v.MaxCharges)
            // A skill named by its parameter with no string of its own - a runeword's
            // "+2 to Static Field (Sorceress Only)" - composed as the game composes it.
            ?? ((v.SkillId is > 0 ? v.SkillId : v.Param) is int skill and > 0
                ? StatText.ByParameter(v.Stat, v.Value, skill)
                : null)
            ?? $"{v.Stat} {v.Value}")];

    /// <summary>
    /// What a family grants and the range each property rolls inside, for showing rather
    /// than for applying - the ranges are the table's own, not a roll.
    /// </summary>
    public static IReadOnlyList<string> Ranges(AffixRanges.CraftRecipe recipe)
    {
        var said = new List<(string Stat, string Words)>();
        var spans = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);

        foreach (var grant in recipe.Grants)
        {
            // Worded the way the affix list words its own grants, down to the fallbacks:
            // a damage property names no stat of its own and is worded by its function.
            IReadOnlyList<string> stats = AffixRanges.StatsOf(grant.Code);

            if (stats.Count == 0 && AffixRanges.WordingFor(grant.Code) is { } worded)
                stats = [worded];

            foreach (string stat in stats.DefaultIfEmpty(grant.Code))
            {
                var span = AffixRanges.RangeFor(grant.Code, stat, grant.Min, grant.Max, grant.Param);

                // The skill a grant names is in its parameter - Hitpower's "chance to cast level 4
                // Frost Nova when struck" - and read without it the line said "(none)".
                //
                // A packed one - a chance to cast - is not a range at all: its two numbers are the
                // chance and the level, as the row writes them. Hitpower's amulet is 5 and 4, "5%
                // Chance to cast level 4 Frost Nova when struck" on the wiki's chart; passed as a
                // span it said level 5.
                // A class's skills and a skill tree are named by the parameter the item keeps, which is not the
                // row's: worded without it a Bone said "to Amazon Skill Levels 2" and every tree was Javelin and
                // Spear Skills. Composed as a rolled line is, the range after it where there is one.
                string? bySubject =
                    (stat.Equals("item_addclassskills", StringComparison.OrdinalIgnoreCase)
                     || stat.Equals("item_addskill_tab", StringComparison.OrdinalIgnoreCase))
                    && StatText.ByParameter(stat, span.Max, AffixRanges.StoredParamFor(grant.Code, stat, grant.Param)) is { } named
                        ? span.Min == span.Max ? named
                            // A range in the number's own place, as every other line has it: "+1-2 to Barbarian Skill Levels".
                            : named.StartsWith("+" + span.Max + " ", StringComparison.Ordinal) ? "+" + span.Min + "-" + named[1..]
                            : $"{named} [{span.Min} - {span.Max}]"
                        : null;

                string words = bySubject
                    ?? (AffixRanges.IsPackedCode(grant.Code)
                        ? StatText.DescribeRange(stat, grant.Min, grant.Max, SkillNames.NameOf(grant.Param))
                        : StatText.DescribeRange(stat, span.Min, span.Max, SkillNames.NameOf(grant.Param)))
                    // A skill named by its parameter with no string of its own - a runeword's
                    // "+2 to Static Field (Sorceress Only)", item_singleskill - is composed the way
                    // a rolled line is (StatText.ByParameter); the range, where it has one, after it.
                    ?? (grant.Param > 0 && StatText.ByParameter(stat, span.Max, grant.Param) is { } composed
                        ? span.Min == span.Max ? composed : $"{composed} [{span.Min} - {span.Max}]"
                        : null)
                    ?? stat;

                // Wording that carries no number of its own gets the range put after it - not a packed one's,
                // whose numbers are a chance and a level: splash's 100-1 read "Melee Attacks Deal Splash Damage
                // 100" on the automagic line, where the game says no number. Nor a stat's whose
                // line the game prints bare (StatText.PrintsItsValue): "Cannot Be Frozen", "Knockback", "Half
                // Freeze Duration" - each read with a 1 after it in the rune wizard's tooltips.
                if (!words.Any(char.IsDigit) && !AffixRanges.IsPackedCode(grant.Code) && StatText.PrintsItsValue(stat))
                    words += span.Min == span.Max ? $" {span.Min}" : $" [{span.Min} - {span.Max}]";

                said.Add((stat, words));
                spans[stat] = (span.Min, span.Max);
            }
        }

        // A whole description group rolling one range is one line, where the first of it was:
        // Brilliant's four resistances are "All Resistances +5-10", as the wiki's chart says it.
        var grouped = StatText.GroupedRanges(spans);
        var lines = new List<string>();
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Damage as the game says it, the rule StatText.PhysicalDamage gives an item: one of a
        // dmg-max's three copies - the one-handed - and a dmg% pair rolling alike as one
        // "Enhanced Damage". Blood's weapon read "Enhanced Maximum Damage [50 - 80]" and then the
        // same for the minimum; the chart says "+[50-80]% Enhanced Damage".
        foreach (var copies in (string[][])
                 [
                     ["mindamage", "secondary_mindamage", "item_throw_mindamage"],
                     ["maxdamage", "secondary_maxdamage", "item_throw_maxdamage"],
                 ])
        {
            if (copies.FirstOrDefault(spans.ContainsKey) is { } kept)
                skipped.UnionWith(copies.Where(copy => copy != kept));
        }

        if (spans.TryGetValue("item_maxdamage_percent", out var most)
            && spans.TryGetValue("item_mindamage_percent", out var least) && most == least)
        {
            string span = most.Min == most.Max ? $"{most.Max}" : $"{most.Min}-{most.Max}";
            string words = StringTable.Current?.Get("strModEnhancedDamage")?.Trim() is { Length: > 0 } label
                ? label
                : "Enhanced Damage";

            int at = said.FindIndex(each => each.Stat.Equals("item_maxdamage_percent", StringComparison.OrdinalIgnoreCase)
                                            || each.Stat.Equals("item_mindamage_percent", StringComparison.OrdinalIgnoreCase));
            said[at] = ("", $"+{span}% {words}");
            skipped.Add("item_maxdamage_percent");
            skipped.Add("item_mindamage_percent");
        }

        // Elemental damage as the game says it: one line for the run, poison's numbers over its length - as an item's
        // own lines are said (StatText.ElementalDamage). A necromancer head's automod read "+1012 to Minimum Poison
        // Damage", "+1270 to Maximum ..." and a bare "poisonlength 50"; the game says "Adds
        // 198-248 poison damage over 2 seconds". Only where the numbers are fixed, as every automod's and craft's are.
        foreach (var (leader, run) in StatText.ElementalRuns)
        {
            if (!spans.TryGetValue(leader, out var low) || !spans.TryGetValue(run.Max, out var high))
                continue;

            (int Min, int Max) length = (0, 0);
            if (run.Length.Length > 0 && !spans.TryGetValue(run.Length, out length))
                continue;

            if (low.Min != low.Max || high.Min != high.Max || length.Min != length.Max
                || StatText.ElementalDamage(leader, low.Max, high.Max, length.Max) is not { Length: > 0 } together)
                continue;

            int at = said.FindIndex(each => each.Stat.Equals(leader, StringComparison.OrdinalIgnoreCase));
            said[at] = ("", together);
            skipped.Add(run.Max);
            if (run.Length.Length > 0)
                skipped.Add(run.Length);
        }

        foreach (var (stat, words) in said)
        {
            if (skipped.Contains(stat))
                continue;

            if (grouped.FirstOrDefault(group => group.Stats.Contains(stat, StringComparer.OrdinalIgnoreCase)) is { Text: { } text } whole)
            {
                lines.Add(text);
                skipped.UnionWith(whole.Stats);
                continue;
            }

            lines.Add(words);
        }

        return lines;
    }

    /// <summary>Rolls what a family would put on this item, without putting it there.</summary>
    public static CraftDraft Draft(AffixRanges.CraftRecipe recipe, Random? random)
    {
        var values = new List<AffixRanges.Wrote>();

        foreach (var grant in recipe.Grants)
        {
            int rolled = random is null || grant.Max <= grant.Min
                ? grant.Min
                : random.Next(grant.Min, grant.Max + 1);

            values.AddRange(
                AffixRanges.Written(grant.Code, grant.Min, grant.Max, rolled, grant.Param));
        }

        return new CraftDraft(recipe, values);
    }

    /// <summary>What a reforge did to one property.</summary>
    public sealed record Reforged(string Stat, int Was, int Now, bool Removed);

    /// <summary>
    /// Takes the old family's properties off and puts the chosen family's on.
    /// <para>
    /// Shaped like <see cref="MagicAffixes.Apply"/>, and for the same reason: what leaves
    /// and what arrives are worked out together, the item's remaining sources are asked
    /// what they still account for, and only then is anything written. A property both the
    /// old craft and an affix wrote keeps the affix's share rather than vanishing with the
    /// craft.
    /// </para>
    /// <para>
    /// The old family's properties come off <b>before</b> the remaining sources are asked,
    /// because a craft is identified by those very properties: with them gone the item no
    /// longer names any recipe, which is exactly the question being put.
    /// </para>
    /// <para>
    /// A base upgrade's fixed mod stays too (<see cref="UpgradeShares"/>): it has no range for the remaining
    /// sources to answer with, and a Rune Staff's <c>cast2 20</c> went with the family while
    /// <see cref="BaseUpgrades.Grants"/> still credited it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Reforged> Reforge(Item item, CraftDraft draft, Random random)
    {
        var told = new List<Reforged>();

        if (item.StatLists.FirstOrDefault() is not { } list)
            return told;

        // The likeliest where more than one recipe fits: an affix sharing a recipe's stat can make two fit.
        var leaving = AffixRanges.CraftLikelyOn(item);

        // What this is allowed to move: what the old recipe wrote, and what the new one
        // will. Nothing else on the item is in scope.
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Keyed (AffixRanges.KeyOf) by stat **and** what its parameter names, matching how Reroll answers. By the
        // bare name, two lines of one stat for two different subjects were treated as one.

        // A chance to cast or charges is a line by its skill <b>and level</b> - the game packs the two - and the
        // recipe's are its row's fixed numbers, not a roll: they come off and go on as lines of their own, never summed
        // with another source's. Keyed by skill alone, a Hitpower glove's "5% chance to cast level 4 Frost Nova when
        // struck" and an affix's at level 3 were added into one line at level 4 (the Forge's sweep).
        static bool Packs(AffixRanges.Wrote wrote) => wrote.SkillId is not null || wrote.MaxCharges is not null;

        foreach (var grant in leaving?.Grants ?? [])
        {
            foreach (var wrote in AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Min, grant.Param))
            {
                if (!Packs(wrote))
                {
                    touched.Add(AffixRanges.KeyOf(wrote));
                    continue;
                }

                if (list.Stats.FirstOrDefault(stat => ItemStats.SameSkillLine(stat, wrote)) is { } line)
                {
                    line.Value -= wrote.Value;
                    if (line.Value <= 0)
                        list.Stats.Remove(line);
                }
            }
        }

        foreach (var wrote in draft.Values.Where(wrote => !Packs(wrote)))
            touched.Add(AffixRanges.KeyOf(wrote));

        // Asked while the lines are whole: what a base upgrade put on each of them.
        var upgrades = UpgradeShares(item, leaving, touched);

        // Off, so the item stops naming the old family and the question below is answered
        // by the affixes and the base alone.
        foreach (string key in touched)
        {
            if (ItemStats.FindKeyed(item, key) is { } carried)
                ItemStats.Remove(item, carried);
        }

        var (others, held, packed) = AffixRanges.Reroll(item, random, withCorruption: true);

        // The chosen family's own numbers, added where two of its properties share a stat.
        var arriving = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var wrote in draft.Values.Where(wrote => !Packs(wrote)))
        {
            arriving[AffixRanges.KeyOf(wrote)] = arriving.TryGetValue(AffixRanges.KeyOf(wrote), out int already)
                ? already + wrote.Value
                : wrote.Value;
        }

        foreach (string key in touched)
        {
            string stat = ItemStats.NameIn(key);
            bool fromOthers = others.TryGetValue(key, out int rest);
            bool fromCraft = arriving.TryGetValue(key, out int mine);
            int upgraded = upgrades.GetValueOrDefault(key);

            if (!fromOthers && !fromCraft && upgraded == 0)
            {
                // Nothing grants it any more. It was the old craft's alone.
                told.Add(new Reforged(stat, 0, 0, Removed: true));
                continue;
            }

            int value = (fromOthers ? rest : 0) + (fromCraft ? mine : 0) + upgraded;

            // Held at what the field holds, as MagicAffixes.Apply holds its sum. A packed line's number is not a plain value.
            if (!packed.ContainsKey(key))
                value = Math.Min(value, StatFields.CeilingFor(item, stat));

            // A property with no range to roll inside keeps whatever its source says, and
            // an affix that still grants it has already said so.
            if (!fromCraft && upgraded == 0 && held.Contains(stat))
                continue;

            var carried = new ItemStat
            {
                Id = StatCatalogue.IdOf(stat),
                Stat = stat,
                Value = value,
            };

            if (packed.TryGetValue(key, out var theirs))
            {
                carried.SkillId = theirs.SkillId;
                carried.SkillLevel = theirs.SkillLevel;
                carried.MaxCharges = theirs.MaxCharges;
            }

            list.Stats.Add(carried);
            told.Add(new Reforged(stat, 0, value, Removed: false));
        }

        foreach (var wrote in draft.Values.Where(Packs))
        {
            ItemStats.AddToSkillLine(list, wrote);
            told.Add(new Reforged(wrote.Stat, 0, wrote.Value, Removed: false));
        }

        // A flat damage as the base holds it (DamageKinds): what every writer of an item ends on.
        DamageKinds.Settle(item);
        return told;
    }

    /// <summary>
    /// What a base upgrade's fixed mod holds of each line a reforge rebuilds, by the key the line answers to. The
    /// line less the least its rolled sources and the old recipe could have given, and never more than the
    /// upgrade's own grant (<see cref="BaseUpgrades.Grants"/>): <see cref="AffixRanges.Shares"/> counts the recipes as
    /// fixed too, every one that could have made the item, so its fixed part says nothing of the upgrade alone. A
    /// line that does not hold the grant keeps none of it, so a craft made on a list just emptied gains nothing.
    /// </summary>
    private static Dictionary<string, int> UpgradeShares(Item item, AffixRanges.CraftRecipe? leaving, IReadOnlySet<string> touched)
    {
        var shares = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var grants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var grant in BaseUpgrades.Grants(item))
        {
            foreach (var wrote in AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Max, grant.Param))
            {
                string key = AffixRanges.KeyOf(wrote);
                grants[key] = grants.GetValueOrDefault(key) + wrote.Value;
            }
        }

        if (grants.Count == 0)
            return shares;

        var oldRecipe = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var grant in leaving?.Grants ?? [])
        {
            foreach (var wrote in AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Min, grant.Param))
            {
                string key = AffixRanges.KeyOf(wrote);
                oldRecipe[key] = oldRecipe.GetValueOrDefault(key) + wrote.Value;
            }
        }

        var (_, rolledLeast) = AffixRanges.Shares(item);

        foreach (var (key, granted) in grants)
        {
            if (!touched.Contains(key) || ItemStats.FindKeyed(item, key) is not { } line)
                continue;

            int share = Math.Clamp(line.Value - rolledLeast.GetValueOrDefault(key) - oldRecipe.GetValueOrDefault(key), 0, granted);
            if (share > 0)
                shares[key] = share;
        }

        return shares;
    }

    /// <summary>
    /// Writes the item's automagic back onto it, for a caller that has just emptied the
    /// property list. See <see cref="AffixRanges.AutomagicOn"/> for why the recorded id is
    /// re-applied rather than a new tier picked.
    /// </summary>
    private static void RestoreAutomagic(Item item, Random random)
    {
        if (item.StatLists.FirstOrDefault() is not { } list)
            return;

        foreach (var wrote in AffixRanges.AutomagicOn(item, random))
            list.Stats.Add(ItemStats.LineFrom(wrote));
    }

    /// <summary>What making a craft out of a plain item came to.</summary>
    public sealed record Crafted(
        string Family,
        int RareFirst,
        int RareSecond,
        IReadOnlyList<(int Slot, bool Prefix, int Id)> Affixes);

    /// <summary>
    /// How many affixes the cube rolls onto a crafted item, drawn by its crafted item level.
    /// <para>
    /// <b>No table holds it.</b> The engine decides; the PD2 wiki (Crafting, unchanged from
    /// vanilla), Maxroll and planetdiablo agree:
    /// </para>
    /// <code>
    /// ilvl     1     2     3     4
    /// 1-30    40%   20%   20%   20%
    /// 31-50    -    60%   20%   20%
    /// 51-70    -     -    80%   20%
    /// 71+      -     -     -   100%
    /// </code>
    /// <para>
    /// The level sets the <i>fewest</i>, not the most: four can come at any level. Read the other
    /// way round as a ceiling, it flagged four real ladder crafts (a ring and gloves
    /// at ilvl 44-45 with four) - so the legitimacy check keeps one to four
    /// (<see cref="MagicAffixes.CountFor"/>).
    /// </para>
    /// </summary>
    public static int RollAffixCount(int itemLevel, Random random)
    {
        int fewest = itemLevel switch { <= 30 => 1, <= 50 => 2, <= 70 => 3, _ => 4 };
        if (fewest == 4)
            return 4;

        // Four, three and two take 20% each above the fewest; the fewest takes what is left.
        int roll = random.Next(100);
        return roll < 20 ? 4 : roll < 40 && fewest < 3 ? 3 : roll < 60 && fewest < 2 ? 2 : fewest;
    }

    /// <summary>
    /// A crafted item's level: half the character's and half the base's, each rounded down
    /// (the PD2 wiki; PD2's own "Craft Alvl" agrees on seven items).
    /// </summary>
    public static int CraftedItemLevel(int characterLevel, int baseItemLevel) =>
        Math.Clamp(characterLevel / 2 + baseItemLevel / 2, 1, 99);

    /// <summary>
    /// Turns a magic or rare item into a crafted one, the way the cube does: the affixes it
    /// came with are thrown away, new ones are rolled from the pool a crafted item may draw
    /// on, and the recipe's own properties go on top.
    /// <para>
    /// The old affixes cannot simply be kept. 148 prefixes and 100 suffixes are magic only
    /// - no rare or crafted item can carry them - so a magic item carrying one would become
    /// an item the game could not have made. The cube rerolls for the same reason.
    /// </para>
    /// <para>
    /// A magic item also has <b>no rare name</b>: its name is its prefix and suffix around
    /// the base. A crafted item is named the way a rare is, from two halves, so one is
    /// rolled here from the halves that item's type allows.
    /// </para>
    /// <para>
    /// The quality is set first and everything else asked afterwards, because what an item
    /// may roll depends on what it is: the affix pool filters on the rare flag, and asking
    /// it while the item was still magic would have offered exactly the affixes this is
    /// here to avoid.
    /// </para>
    /// </summary>
    /// <param name="characterLevel">
    /// The crafting character's level, which sets the crafted item level with the base's; null
    /// keeps the base's own level (a caller with no character).
    /// </param>
    public static Crafted CraftFrom(Item item, AffixRanges.CraftRecipe recipe, Random random, int? characterLevel = null)
    {
        item.Quality = ItemQuality.Craft;

        // The cube's item level, before anything that depends on it: the affix level, and so
        // the pool and the count, follow from it.
        if (characterLevel is { } level)
            item.ItemLevel = (byte)CraftedItemLevel(level, item.ItemLevel);

        // Made before anything is put in it. Both of the writers below - Apply and Reforge
        // - hand back an empty report rather than creating one, so an item that arrived
        // with no property list would have come out of here a crafted item with no
        // properties at all, and said nothing about it.
        ItemStats.Home(item);

        // A name of two halves, from the ones this item's type allows.
        var first = AffixNames.FirstNames(item);
        var second = AffixNames.SecondNames(item);

        int named = first.Count > 0 ? first[random.Next(first.Count)].Id : item.RarePrefixId;
        int also = second.Count > 0 ? second[random.Next(second.Count)].Id : item.RareSuffixId;

        item.RarePrefixId = (ushort)named;
        item.RareSuffixId = (ushort)also;

        // Off with the old, before the pool is asked: an affix still in a slot would have
        // its group counted as spent and the replacement drawn from somewhere else.
        for (int slot = 0; slot < 3; slot++)
        {
            if (item.MagicPrefixIds is { } prefixes && slot < prefixes.Length)
                prefixes[slot] = 0;

            if (item.MagicSuffixIds is { } suffixes && slot < suffixes.Length)
                suffixes[slot] = 0;
        }

        item.StatLists.FirstOrDefault()?.Stats.Clear();

        // The cube makes a new item, and a socket it holds empty does not come across: a helm with a socket corruption came
        // out of a Hitpower recipe with none (read in game). What is socketed stays: nothing here may lose a gem unasked.
        item.TotalNumberOfSockets = (byte)item.SocketedItems.Count;
        item.IsSocketed = item.TotalNumberOfSockets > 0;

        // That list held the base's own grants as well as the affixes'. The game keeps them:
        // a javelin crafted in game came out still carrying its automagic's splash and its
        // +2 skill tab, and the crafted affix's +1 added to the tab rather than replacing it.
        // Clearing without this left an item recording an automagic it did not carry.
        //
        // A staff mod is re-generated, like the automagic (5 of 50 live listings, and a crafted Archon
        // Staff with two item_singleskill lines), so a class base gets a fresh set, drawn at the crafted item level.
        // Rolled before the affixes: StaffMods.Put takes every skill line of the list off. A superior bonus does not
        // come into it: no craft recipe takes a superior base (CraftingEditor.CanCraftFrom).
        RestoreAutomagic(item, random);

        if (StaffMods.Rolls(item))
            StaffMods.Put(item, StaffMods.Roll(item, random));

        // Drawn as the cube draws them (MagicAffixes.Draw): a coin per pick for its side - 3+1 and 1+3
        // as well as 2+2 - by frequency, at or below the affix level, a pick finding nothing lost. Each
        // id is written into its slot as it is drawn, so the next draw knows its group is spent.
        var picks = MagicAffixes.Draw(item, MagicAffixes.RollCount(item, random), random);
        var chosen = picks
            .Where(each => each.Value.Id > 0)
            .Select(each => (each.Key.Slot, each.Key.Prefix, each.Value.Id))
            .ToList();

        // An affix id grants nothing on its own: Apply is what turns it into properties.
        MagicAffixes.Apply(item, picks, random);

        // And the recipe on top, through the same path a reforge takes.
        Reforge(item, Draft(recipe, random), random);

        return new Crafted(recipe.Family, named, also, chosen);
    }
}
