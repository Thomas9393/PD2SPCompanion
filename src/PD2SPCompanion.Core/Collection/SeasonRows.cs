using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// Bringing an item carried from another season to <b>this season's rows</b>, where a rebalance moved them: the
/// rule for a migration (new stats from the new season added randomized; old stats stripped),
/// applied to the S14 rehearsal's rebalanced items, which follow S14's rows.
/// <para>
/// What <see cref="SeasonFit.Rescale"/> cannot do, because there is no range to keep a place in: a chance to cast
/// whose level the row changed (Amplify Damage 31 to 27), a property the row no longer grants (The Patriarch's
/// -40% Increased Attack Speed), one it newly grants, an affix the base may no longer carry (of Protecting on a
/// circlet), a value left outside a range that narrowed. Each is put the way the installed tables say, and said.
/// </para>
/// <para>
/// Only for an item the old season's tables found nothing wrong with: then every finding the new tables raise is the
/// season's, not the item's. Anything else - an item forged by hand, a test - keeps its numbers. Every roll is seeded
/// from the item (<see cref="SeasonFit.Seeded"/>), so the same vault migrated twice comes out the same.
/// </para>
/// </summary>
public static class SeasonRows
{
    /// <summary>
    /// Puts the item to this season's rows, with this season's tables installed, and answers what was changed, one
    /// line each, for the migration's report. <paramref name="before"/> is what the item's sources could roll under
    /// the old season's tables (<see cref="AffixRanges.For"/>, as the carry reads it): a property is new this season
    /// only where it had no source then. Without it nothing is added.
    /// </summary>
    public static IReadOnlyList<string> Follow(Item item, IReadOnlyDictionary<string, RollRange>? before)
    {
        var told = new List<string>();

        Affixes(item, told);
        Packed(item, told);
        Lines(item, told);
        if (before is not null)
            Added(item, before, told);

        if (told.Count > 0)
        {
            DamageKinds.Settle(item);
            ElementSkills.Settle(item);
        }

        return told;
    }

    /// <summary>Affixes this season does not put on the item - switched off, or not for this base - taken off.</summary>
    private static void Affixes(Item item, List<string> told)
    {
        if (!MagicAffixes.Carries(item) || !MagicAffixes.Judgeable(item))
            return;

        var picks = new Dictionary<(int Slot, bool Prefix), MagicAffixes.AffixPick>();

        foreach (bool prefix in new[] { true, false })
        {
            var ids = prefix ? item.MagicPrefixIds : item.MagicSuffixIds;
            for (int slot = 0; slot < (ids?.Length ?? 0); slot++)
            {
                int id = ids![slot];
                if (id == 0)
                    continue;

                var option = MagicAffixes.Find(id, prefix);
                bool offered = option is not null
                               && (MagicAffixes.Offers(item, prefix, slot, id, outgrown: true) || MagicAffixes.SameFamilyOtherTier(item, option));
                if (offered)
                    continue;

                picks[(slot, prefix)] = new MagicAffixes.AffixPick(0, new Dictionary<string, int>());
                string name = MagicAffixes.Names(option) ? option!.Name : (prefix ? L($"prefix {id}") : L($"suffix {id}"));
                told.Add(L($"{name} taken off: this season does not put it on this item."));
            }
        }

        if (picks.Count > 0)
            MagicAffixes.Apply(item, picks, SeasonFit.Seeded(item.Id, "affixes"));
    }

    /// <summary>A chance to cast or charges at the level and number the item's sources give this season.</summary>
    private static void Packed(Item item, List<string> told)
    {
        var packed = item.StatLists.SelectMany(list => list.Stats)
            .Where(stat => StatShapes.Of(stat.Stat) is StatShape.Chance or StatShape.Charges)
            .Select(stat => (Stat: stat, Was: Shape(stat))).ToList();

        AffixRanges.DerivePacked(item);

        foreach (var (stat, was) in packed)
        {
            if (Shape(stat) != was)
                told.Add(L($"{Worded(stat)}: as this season's row has it."));
        }
    }

    private static string Shape(ItemStat stat) => $"{stat.Value}/{stat.SkillLevel}/{stat.MaxCharges}";

    /// <summary>A line no source grants any more taken off; one outside a range that moved brought to its edge.</summary>
    private static void Lines(Item item, List<string> told)
    {
        foreach (var unfit in Legitimacy.UnfitOn(item))
        {
            var list = item.StatLists[unfit.List].Stats;

            if (unfit.Range is not { } range)
            {
                foreach (var stat in unfit.Stats)
                    list.Remove(stat);
                told.Add(L($"{Worded(unfit.Stats[0])} taken off: nothing on the item grants it this season."));
                continue;
            }

            // Two properties writing one line: no range says which of them moved.
            if (unfit.Stats.Count != 1)
                continue;

            var (low, high) = SeasonFit.Ordered(range);
            var line = unfit.Stats[0];
            int was = line.Value;
            line.Value = Math.Clamp(was, low, high);
            told.Add(L($"{Worded(line)}: was {was}, this season rolls {range}."));
        }
    }

    /// <summary>
    /// A property this season's row grants, that no source granted last season and the item lacks, rolled as the row
    /// rolls it: a unique's own list, a set piece's own list and its green lists, each against a fresh piece of the
    /// same row.
    /// </summary>
    private static void Added(Item item, IReadOnlyDictionary<string, RollRange> before, List<string> told)
    {
        // New is by the key - the stat with what it names: S14's Demon Machine casts "Multiple Shot Proc" where S13's
        // cast Multiple Shot, the same stat on another skill. A source drawing its subject (a random skill) registers
        // under the bare name (AffixRanges), and the bare name having had a source is enough.
        bool HadSource(ItemStat stat) => before.ContainsKey(AffixRanges.KeyOf(stat)) || before.ContainsKey(stat.Stat!);

        Item? fresh;
        try
        {
            var random = SeasonFit.Seeded(item.Id, "added");
            fresh = item.Quality switch
            {
                ItemQuality.Unique => ItemForge.UniqueAt((int)item.FileIndex, item.Version, RollAs.Random, random).Item,
                ItemQuality.Set => ItemForge.SetItemAt((int)item.FileIndex, item.Version, RollAs.Random, random).Item,
                _ => null,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            // A row this season does not have: nothing to add from.
            return;
        }

        if (fresh is null)
            return;

        for (int which = 0; which < Math.Min(item.StatLists.Count, fresh.StatLists.Count); which++)
        {
            var mine = item.StatLists[which].Stats;

            // By the key - the stat with what it names: Stormspire holds a Charged Bolt to cast beside the Static Field S14
            // made "Static Field Proc", and asked by the name alone the new one was never added. A
            // property drawing its subject (a random skill) is not new for naming another: HadSource asks its bare name.
            var held = mine.Where(stat => stat.Stat is { Length: > 0 }).Select(AffixRanges.KeyOf).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var arriving = fresh.StatLists[which].Stats
                .Where(stat => stat.Stat is { Length: > 0 } && !held.Contains(AffixRanges.KeyOf(stat)) && !HadSource(stat))
                .ToList();
            foreach (var stat in arriving)
                mine.Add(stat);

            // Worded once a line, as the item's tooltip words it: a flat damage arrives as several stats.
            foreach (var line in arriving.GroupBy(stat => stat.Stat).Select(group => group.First()))
            {
                // The same words going and coming: S14's "Multiple Shot Proc" is called Multiple Shot, so the report
                // said the one line was taken off and added. Said once, as what it is.
                string worded = Worded(line);
                int gone = told.IndexOf(L($"{worded} taken off: nothing on the item grants it this season."));
                if (gone >= 0)
                    told[gone] = L($"{worded}: now this season's skill of that name.");
                else
                    told.Add(L($"{worded} added: this season's row grants it."));
            }
        }
    }

    /// <summary>A line as a person reads it, the way the legitimacy check words it.</summary>
    private static string Worded(ItemStat stat)
    {
        string name = stat.Stat ?? "";
        return StatText.ByParameter(name, stat.Value, stat.SkillId ?? stat.Param ?? 0)
               ?? StatText.Describe(name, stat.Value, SkillNames.NameOf(StatText.SkillOf(name, stat.SkillId, stat.Param)),
                                    stat.SkillLevel, stat.MaxCharges)
               ?? name;
    }
}
