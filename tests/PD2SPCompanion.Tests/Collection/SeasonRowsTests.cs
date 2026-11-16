using D2SLib.Model.Save;
using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// An item carried to a season whose rows moved, following them (<see cref="SeasonRows.Follow"/>),
/// as the S14 rehearsal's rebalanced items were. Each case is a forged item - silent under the installed
/// tables - put the way a rebalance leaves an old item, and then brought back to its row.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SeasonRowsTests(Game game)
{
    private static IEnumerable<ItemStat> Stats(Item item) => item.StatLists.SelectMany(list => list.Stats);

    /// <summary>The first enabled unique, made at the top of its ranges, that has a line answering to <paramref name="wanted"/>.</summary>
    private static (Item Item, ItemStat Line) UniqueWith(Func<Item, ItemStat, bool> wanted)
    {
        foreach (string name in ItemForge.UniqueNames())
        {
            var item = ItemForge.Unique(name, "105").Item;
            if (Legitimacy.Check(item).Count > 0)
                continue;
            if (Stats(item).FirstOrDefault(stat => stat.Stat is { Length: > 0 } && wanted(item, stat)) is { } line)
                return (item, line);
        }

        throw new InvalidOperationException("No unique has such a line.");
    }

    [GameFact]
    public void A_line_no_source_grants_any_more_is_taken_off()
    {
        if (!game.Available)
            return;

        var (item, line) = UniqueWith((_, _) => true);
        var stray = new ItemStat { Stat = "item_magicbonus", Id = 80, Value = 25 };
        if (Stats(item).Any(stat => stat.Stat == stray.Stat))
            stray = new ItemStat { Stat = "item_goldbonus", Id = 79, Value = 25 };
        item.StatLists[0].Stats.Add(stray);
        Assert.NotEmpty(Legitimacy.Check(item));

        var told = SeasonRows.Follow(item, AffixRanges.For(item));

        Assert.DoesNotContain(stray, item.StatLists[0].Stats);
        Assert.Contains(line, Stats(item));
        Assert.Empty(Legitimacy.Check(item));
        Assert.Single(told);
    }

    [GameFact]
    public void A_value_left_outside_a_range_is_brought_to_its_edge()
    {
        if (!game.Available)
            return;

        var (item, line) = UniqueWith((item, stat) =>
            AffixRanges.OwnOf(item).TryGetValue(AffixRanges.KeyOf(stat), out var range) && range.Max > range.Min
            && item.StatLists[0].Stats.Contains(stat) && item.StatLists[0].Stats.Count(each => AffixRanges.KeyOf(each) == AffixRanges.KeyOf(stat)) == 1);
        var range = AffixRanges.OwnOf(item)[AffixRanges.KeyOf(line)];
        line.Value = SeasonFit.Ordered(range).High + 3;

        SeasonRows.Follow(item, AffixRanges.For(item));

        Assert.Equal(SeasonFit.Ordered(range).High, line.Value);
    }

    [GameFact]
    public void A_chance_to_cast_takes_the_level_its_row_gives()
    {
        if (!game.Available)
            return;

        var (item, line) = UniqueWith((_, stat) => StatShapes.Of(stat.Stat) == StatShape.Chance);
        int level = line.SkillLevel ?? 0;
        line.SkillLevel = level + 4;

        var told = SeasonRows.Follow(item, AffixRanges.For(item));

        Assert.Equal(level, line.SkillLevel);
        Assert.Single(told);
    }

    [GameFact]
    public void A_property_the_row_grants_and_the_old_season_did_not_is_added_rolled()
    {
        if (!game.Available)
            return;

        var (item, line) = UniqueWith((item, stat) =>
            item.StatLists[0].Stats.Contains(stat) && StatShapes.Of(stat.Stat) == StatShape.Value
            && item.StatLists[0].Stats.Count(each => each.Stat == stat.Stat) == 1);
        var ranges = AffixRanges.For(item);
        item.StatLists[0].Stats.Remove(line);

        // As last season's row was: without it.
        var before = ranges.Where(pair => ItemStats.NameIn(pair.Key) != line.Stat).ToDictionary(pair => pair.Key, pair => pair.Value);
        SeasonRows.Follow(item, before);

        var back = Assert.Single(item.StatLists[0].Stats, stat => stat.Stat == line.Stat);
        Assert.True(ranges[AffixRanges.KeyOf(back)].Holds(back.Value));
    }

    [GameFact]
    public void A_property_missing_that_the_old_season_also_granted_is_not_added()
    {
        if (!game.Available)
            return;

        var (item, line) = UniqueWith((item, stat) =>
            item.StatLists[0].Stats.Contains(stat) && StatShapes.Of(stat.Stat) == StatShape.Value
            && item.StatLists[0].Stats.Count(each => each.Stat == stat.Stat) == 1);
        var before = AffixRanges.For(item);
        item.StatLists[0].Stats.Remove(line);

        SeasonRows.Follow(item, before);

        Assert.DoesNotContain(item.StatLists[0].Stats, stat => stat.Stat == line.Stat);
    }

    [GameFact]
    public void An_affix_the_base_may_not_carry_is_taken_off()
    {
        if (!game.Available)
            return;

        var item = ItemForge.BaseAs("ci0", ItemQuality.Magic, "105", new Random(5)).Item;
        int foreign = Enumerable.Range(1, 2000)
            .First(id => MagicAffixes.Find(id, true) is { } option && MagicAffixes.Names(option)
                         && !MagicAffixes.Offers(item, true, 0, id, outgrown: true) && !MagicAffixes.SameFamilyOtherTier(item, option));
        item.MagicPrefixIds[0] = (ushort)foreign;

        var told = SeasonRows.Follow(item, AffixRanges.For(item));

        Assert.Equal(0, item.MagicPrefixIds[0]);
        Assert.Contains(told, line => line.Contains(MagicAffixes.Find(foreign, true)!.Name));
    }

    [GameFact]
    public void An_item_its_rows_account_for_is_left_as_it_is()
    {
        if (!game.Available)
            return;

        var (item, _) = UniqueWith((_, _) => true);
        string was = string.Join(";", Stats(item).Select(stat => $"{stat.Stat}={stat.Value}/{stat.SkillLevel}"));

        Assert.Empty(SeasonRows.Follow(item, AffixRanges.For(item)));
        Assert.Equal(was, string.Join(";", Stats(item).Select(stat => $"{stat.Stat}={stat.Value}/{stat.SkillLevel}")));
    }

    [GameFact]
    public void A_chance_to_cast_on_another_skill_is_added_beside_one_the_item_keeps()
    {
        if (!game.Available)
            return;

        // An item with two chances to cast on striking - Stormspire's Static Field and Charged Bolt: S14 made the first
        // "Static Field Proc", and asked by the stat's name alone the new line would never be added.
        var (item, line) = UniqueWith((item, stat) =>
            stat.Stat == "item_skillonhit" && item.StatLists[0].Stats.Count(each => each.Stat == "item_skillonhit") > 1);
        string key = AffixRanges.KeyOf(line);
        var before = AffixRanges.For(item).Where(pair => pair.Key != key).ToDictionary(pair => pair.Key, pair => pair.Value);
        item.StatLists[0].Stats.Remove(line);

        SeasonRows.Follow(item, before);

        Assert.Single(item.StatLists[0].Stats, stat => AffixRanges.KeyOf(stat) == key);
    }
}
