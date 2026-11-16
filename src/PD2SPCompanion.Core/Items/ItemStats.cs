using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Adding to and taking from an item's list of properties.
/// <para>
/// Removing one is not simply dropping it from a list. A handful of stats are written as a
/// run: the reader, on meeting the first, reads the next one or two without their own ids,
/// because that is how the game stores them. Take one away and the reader still expects
/// what followed it, so the rest of the item is read from the wrong bit onwards - which
/// does not throw, it produces a different item.
/// </para>
/// </summary>
public static class ItemStats
{
    /// <summary>
    /// Every id that has to go with this one, itself included. A stat in the middle of a
    /// run takes the whole run with it: half a run is not a thing the file can hold.
    /// <para>
    /// The runs come from <see cref="ItemStatList.Runs"/>, which is the table the reader
    /// itself reads ahead on, rather than being restated here. Restated is how the two
    /// drift, and a run the writer knows about and this does not is an item that cannot be
    /// read back.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> RunContaining(int id)
    {
        foreach (var (first, following) in ItemStatList.Runs)
        {
            if (id >= first && id <= first + following)
                return [.. Enumerable.Range(first, following + 1)];
        }

        return [id];
    }

    /// <summary>True when removing this stat would take others with it.</summary>
    public static bool IsInRun(int id) => RunContaining(id).Count > 1;

    /// <summary>
    /// Every property the item carries, wherever it keeps it.
    /// <para>
    /// An item can have more than one property list, and for a runeword <b>the first one is
    /// empty</b>: a Nadir cap keeps nothing in list 0 and all seven of its properties in list
    /// 1. Code that reached for the first list saw an item with no properties at all, and
    /// quietly did nothing to it.
    /// </para>
    /// </summary>
    public static IEnumerable<ItemStat> Every(Item item) =>
        item.StatLists.SelectMany(list => list.Stats);

    /// <summary>
    /// Puts each of the item's property lists in id order, **as the game writes them**. The game keeps a
    /// list sorted and saves it so; the editor added to the end, and a map given its recipes here was
    /// written 442, 372, 370 ... 500, 375, 273 where the game's save of the same map, minutes later, read
    /// 273, 276, 370 ... 495, 500 (a Bone Brow - the one difference between the two
    /// files, found when that map's portal showed no random event). What every writer of an item ends on:
    /// the builders through <see cref="DamageKinds.Settle"/>, the item panel on KEEP.
    /// <para>
    /// A stable sort, so two lines of one id keep their order, and a run - cold damage's three, a
    /// poison's - stays together, being three ids in a row. A list holding a property with no id is left
    /// as it is rather than guessed at.
    /// </para>
    /// </summary>
    public static void InIdOrder(Item item)
    {
        foreach (var list in item.StatLists)
        {
            if (list.Stats.Any(stat => stat.Id is null))
                continue;

            bool ordered = true;
            for (int at = 1; at < list.Stats.Count && ordered; at++)
                ordered = list.Stats[at - 1].Id <= list.Stats[at].Id;

            if (ordered)
                continue;

            var sorted = list.Stats.OrderBy(stat => stat.Id).ToList();
            list.Stats.Clear();
            list.Stats.AddRange(sorted);
        }
    }

    /// <summary>
    /// Whether two properties are the same line of an item: one stat, and every field that tells two lines of
    /// it apart - the parameter, the skill, its level, the tree, the class. By name and parameter alone a
    /// Hitpower helm's "12% chance to cast level 4 Charged Bolt when struck" and "5% ... Frost Nova" were one
    /// line (the skill of a chance to cast is kept beside the parameter, not in it), and the item panel's staged
    /// copy wrote the second's 5% over the first: "not rolled" on a craft the game had made.
    /// <para>
    /// The tree and the class are the parameter said twice (see <see cref="Subject"/>), and a line holds them
    /// both ways only once it has been read from a file: every writer here makes a skill tree's line with its
    /// parameter alone. Compared field by field, a rare just made on the Forge and the page's copy of it - read
    /// back from its bytes - were two lines of one tree: the copy wore the tree twice ("+2 to Masteries is
    /// outside 1"), and a redraw of its affixes took one off and left the other, which nothing accounted for
    /// (a roll on the Forge).
    /// </para>
    /// </summary>
    public static bool SameLine(ItemStat one, ItemStat other) =>
        string.Equals(one.Stat, other.Stat, StringComparison.OrdinalIgnoreCase)
        && Subject(one) == Subject(other)
        && one.SkillId == other.SkillId
        && one.SkillLevel == other.SkillLevel;

    /// <summary>
    /// What a line's parameter names, however the line came to be: the parameter where it has one, and a skill
    /// tree's from the two fields a read splits it into - the class three bits above the tree.
    /// </summary>
    private static int Subject(ItemStat stat) =>
        stat.Param ?? (((stat.CharacterClass ?? 0) << 3) | (stat.SkillTab ?? 0));

    /// <summary>
    /// Whether two lines agree field by field, the parameter as held. Apart from <see cref="SameLine"/> on a skill tree:
    /// a line read from a file splits its parameter into tree and class and this calls it another line. For lines that
    /// came the same way: the ones a listing's sockets are taken off, where a null parameter and a nought differ too.
    /// </summary>
    public static bool SameFields(ItemStat one, ItemStat other) =>
        Named(one, other.Stat) && one.Param == other.Param && one.SkillId == other.SkillId && one.SkillLevel == other.SkillLevel;

    /// <summary>
    /// <see cref="SameFields"/> and the charges it holds: what a list of one making may sum into one line. A charge's
    /// number is two fields, so two charged lines of a skill and level that hold different charges stay two.
    /// </summary>
    public static bool SameFieldsAndCharges(ItemStat one, ItemStat other) =>
        SameFields(one, other) && one.MaxCharges == other.MaxCharges;

    /// <summary>
    /// Whether a value <see cref="WriteValues"/> writes lands on this line. A field the value leaves blank asks nothing
    /// of the line, and a parameter the line lacks counts as nought: a row's plain number finds the stat's line, whatever
    /// else that line holds. A value naming a skill finds that skill, and that level where it names one.
    /// </summary>
    public static bool LandsOn(ItemStat line, AffixRanges.Wrote wrote) =>
        Named(line, wrote.Stat)
        && (wrote.SkillId is null || (line.SkillId == wrote.SkillId && (wrote.SkillLevel is null || line.SkillLevel == wrote.SkillLevel)))
        && (wrote.Param is null || (line.Param ?? 0) == wrote.Param);

    /// <summary>
    /// Whether a recipe's packed line is this one: the skill and the level exactly, the game packing the two, so a blank
    /// on either side matches only a blank. The parameter is no part of it: nothing packed holds one.
    /// </summary>
    public static bool SameSkillLine(ItemStat line, AffixRanges.Wrote wrote) =>
        Named(line, wrote.Stat) && line.SkillId == wrote.SkillId && line.SkillLevel == wrote.SkillLevel;

    /// <summary>
    /// Whether a map recipe's value left this line: by the stat, and the skill where the value names one, at any level.
    /// The parameter is no part of it.
    /// </summary>
    public static bool SameSkill(ItemStat line, AffixRanges.Wrote wrote) =>
        Named(line, wrote.Stat) && (wrote.SkillId is null || line.SkillId == wrote.SkillId);

    /// <summary>
    /// Whether this line can be the value an automagic row writes: by the stat, and the parameter only where both name
    /// one. A line with no parameter passes here where <see cref="LandsOn"/> reads it as nought. The skill is no part of it.
    /// </summary>
    public static bool ParamAgrees(ItemStat line, AffixRanges.Wrote wrote) =>
        Named(line, wrote.Stat) && (wrote.Param is null || line.Param is null || line.Param == wrote.Param);

    private static bool Named(ItemStat line, string? stat) =>
        string.Equals(line.Stat, stat, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The one number that says what a line is for, the half of a key beside the stat's name: the skill of a chance to
    /// cast or a charge, else the parameter, else nought.
    /// </summary>
    public static int SkillOrParam(ItemStat stat) => stat.SkillId ?? stat.Param ?? 0;

    /// <summary>The same, for what a property writes.</summary>
    public static int SkillOrParam(AffixRanges.Wrote wrote) => wrote.SkillId ?? wrote.Param ?? 0;

    /// <summary>
    /// A line holding what a property writes. A parameter is left null when the property has none, because that is what
    /// the reader gives back: a nought changes no bits in the file, but it makes a freshly made item compare unequal to
    /// the same item read from disk, which turned a clean round trip into what looked like 468 broken items.
    /// </summary>
    public static ItemStat LineFrom(AffixRanges.Wrote wrote) => new()
    {
        Id = StatCatalogue.IdOf(wrote.Stat),
        Stat = wrote.Stat,
        Value = wrote.Value,
        SkillId = wrote.SkillId,
        SkillLevel = wrote.SkillLevel,
        MaxCharges = wrote.MaxCharges,
        Param = wrote.Param,
    };

    /// <summary>
    /// A corruption's numbers onto an item - a desecration's too, which writes the same way: added to a line the item
    /// already has, a new line otherwise. Returns the item's own list, made where the item had none.
    /// <para>
    /// A packed value on a line of its skill <b>overwrites</b> it, where <see cref="AddToSkillLine"/> adds. Which the
    /// game does there is not settled, so each keeps what it did.
    /// </para>
    /// </summary>
    public static ItemStatList WriteValues(Item item, IReadOnlyList<AffixRanges.Wrote> values)
    {
        var list = Home(item);

        foreach (var wrote in values)
        {
            string stat = wrote.Stat;

            // A corruption can land on a property the item already had - enhanced damage is
            // the plain case - and the game keeps one line carrying both. So an existing
            // number is added to rather than replaced.
            //
            // Not where the property packs a skill: "10% chance to cast Nova" and "5% to
            // cast Frost Nova" are one stat and two properties, and adding the numbers
            // would leave one skill casting at the other's chance. Another skill takes a
            // line of its own.
            // And not where the parameter names the subject - "+2 to Summoning Skills" is not
            // "+1 to Fire Skills" (item_addskill_tab, two trees): matched by name alone the two
            // merged into one line.
            // Nor where the skill is the same and its level is not: the game packs the two together, so a crafted
            // glove's recipe "5% chance to cast level 3 Frost Nova when struck" and an affix's at level 4 are two
            // lines - taken for one, the affix's wrote over the recipe's.
            // And never a set piece's green bonus list: those count only with enough of the set worn, and a mod is the
            // item's own. A runeword's second list is live, and is searched.
            var already = (item.Quality == ItemQuality.Set ? list.Stats : Every(item)).FirstOrDefault(each => LandsOn(each, wrote));

            if (already is not null)
            {
                if (wrote.SkillId is null)
                {
                    // Held at what the field holds: Cannot Be Frozen is one bit, and a second
                    // grant of it leaves it at 1 - a Saracen's Chance on the market, corrupted
                    // and desecrated with it, holds 1.
                    already.Value = Math.Min(already.Value + wrote.Value, StatFields.CeilingFor(item, stat));
                }
                else
                {
                    already.Value = wrote.Value;
                    already.SkillLevel = wrote.SkillLevel;
                    already.MaxCharges = wrote.MaxCharges;
                }

                continue;
            }

            // Held at what the field holds on a line of its own too: a map's "monsters cannot be
            // frozen, and have 10-15% more life" is one property writing its roll to both stats,
            // and Cannot Be Frozen is one bit - the game keeps 1, and 12 was written here.
            list.Stats.Add(LineFrom(wrote.SkillId is null ? wrote with { Value = Math.Min(wrote.Value, StatFields.CeilingFor(item, stat)) } : wrote));
        }

        // A flat damage as the base holds it (DamageKinds): what every writer of an item ends on.
        DamageKinds.Settle(item);
        return list;
    }

    /// <summary>
    /// A recipe's chance or charges onto a list: on a line of the same skill and level, <b>added</b> to it - one stat of
    /// one packed parameter summed - else a line of their own. See <see cref="WriteValues"/>, which overwrites there.
    /// </summary>
    public static void AddToSkillLine(ItemStatList list, AffixRanges.Wrote wrote)
    {
        if (list.Stats.FirstOrDefault(stat => SameSkillLine(stat, wrote)) is { } line)
            line.Value += wrote.Value;
        else
            list.Stats.Add(LineFrom(wrote));
    }

    /// <summary>The one holding this property, or null where none does.</summary>
    public static ItemStatList? ListHolding(Item item, string stat) =>
        item.StatLists.FirstOrDefault(list => list.Stats.Any(each =>
            string.Equals(each.Stat, stat, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Where a property added by hand goes: the item's own list, made if it has none. The
    /// first is the base's own, whatever a runeword or a set has put in the ones after it.
    /// </summary>
    public static ItemStatList Home(Item item)
    {
        if (item.StatLists.FirstOrDefault() is { } first)
            return first;

        var made = new ItemStatList();
        item.StatLists.Add(made);

        return made;
    }

    /// <summary>
    /// A maximum durability as the game holds the item to it: raised by the item's own durability
    /// properties, a percentage and then a flat amount. Duriel's Shell stores 50 and adds 100, so
    /// it holds 146 against 150 - read against the stored 50, the item panel pulled 146 down to 50.
    /// The percentage is floored, as the game does: an Infinity storing 84 with +20% shows "84 of
    /// 100" in game, where rounding up said 101.
    /// </summary>
    /// <param name="maximum">The stored maximum, or another to raise the same way.</param>
    public static int MaxDurabilityOf(Item item, int maximum)
    {
        int percent = Every(item).Where(stat => stat.Stat == "item_maxdurability_percent").Sum(stat => stat.Value);
        int flat = Every(item).Where(stat => stat.Stat == "maxdurability").Sum(stat => stat.Value);

        return maximum * (100 + percent) / 100 + flat;
    }

    /// <summary>The property by this name, wherever it is kept.</summary>
    /// <remarks>
    /// <b>By name alone</b>, so where an item carries a stat twice for two different subjects
    /// this answers with whichever comes first. Halaberd's Reign holds <c>item_singleskill</c>
    /// for Battle Cry and again for Battle Orders; asked by name it hands back one of them and
    /// the caller writes over both. Use <see cref="FindKeyed"/> anywhere the parameter matters.
    /// </remarks>
    public static ItemStat? Find(Item item, string stat) =>
        Every(item).FirstOrDefault(each =>
            string.Equals(each.Stat, stat, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The property answering to this key - the stat, and the parameter where the parameter
    /// names <i>what</i> was granted. <see cref="AffixRanges.KeyOf(string,int)"/> builds them.
    /// </summary>
    public static ItemStat? FindKeyed(Item item, string key) =>
        Every(item).FirstOrDefault(each =>
            string.Equals(AffixRanges.KeyOf(each), key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The stat name a key was built from.</summary>
    public static string NameIn(string key) =>
        key.IndexOf('#') is var at && at >= 0 ? key[..at] : key;

    /// <summary>
    /// Removes a stat from the item, and whatever is stored alongside it. Returns the ids
    /// that went, or nothing when the stat was not found.
    /// </summary>
    public static IReadOnlyList<int> Remove(Item item, ItemStat stat)
    {
        if (stat.Id is not { } id)
            return [];

        var run = RunContaining(id);
        var went = new List<int>();

        foreach (var list in item.StatLists)
        {
            // Only the list actually holding it; a set item has several.
            if (!list.Stats.Contains(stat))
                continue;

            foreach (int member in run)
            {
                // The line asked for itself, not the first of its id: a stat carried twice for two
                // subjects lost the wrong one - a javelin's own Javelin skills went and a rerolled
                // Warcries prefix's stayed (rollcheck --twice).
                var found = member == id ? stat : list.Stats.FirstOrDefault(entry => entry.Id == member);
                if (found is not null && list.Stats.Remove(found))
                    went.Add(member);
            }

            break;
        }

        return went;
    }
}
