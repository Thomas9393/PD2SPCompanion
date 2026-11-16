using D2SLib.Model.Save;
using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// Carrying values by their place in a range (<see cref="SeasonFit.Rescale"/>). The S14 rehearsal on a real vault
/// sent every set piece with a property in several green lists to be checked: each list's number was held
/// against the range of the whole and moved to it, though the set rows had not changed.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SeasonFitTests(Game game)
{
    private static string KeyOf(ItemStat stat) => AffixRanges.KeyOf(stat.Stat!, stat.Param ?? 0);

    private static IEnumerable<ItemStat> Stats(Item item) =>
        item.StatLists.SelectMany(list => list.Stats).Where(stat => stat.Stat is { Length: > 0 });

    private static string Values(Item item) => string.Join(";", Stats(item).Select(stat => $"{stat.Stat}={stat.Value}"));

    /// <summary>The first set piece holding one property in more than one list - Aldur's pieces' Strength, in S13 and S14.</summary>
    private static Item SetPieceWithARepeat() =>
        ItemForge.SetItems().Select(row => ItemForge.SetItemAt(row, "103").Item)
            .First(item => Stats(item).GroupBy(KeyOf).Any(group => group.Count() > 1));

    [GameFact]
    public void Ranges_that_did_not_change_move_nothing()
    {
        if (!game.Available)
            return;

        var item = SetPieceWithARepeat();
        string was = Values(item);
        var ranges = AffixRanges.For(item);

        Assert.Equal(0, SeasonFit.Rescale(item, ranges));
        Assert.Equal(was, Values(item));
    }

    [GameFact]
    public void A_property_in_several_lists_keeps_its_numbers_when_its_range_moves()
    {
        if (!game.Available)
            return;

        var item = SetPieceWithARepeat();
        string repeated = Stats(item).GroupBy(KeyOf).First(group => group.Count() > 1).Key;
        string was = Values(item);

        // As though last season's whole had been half of this season's.
        var before = AffixRanges.For(item).ToDictionary(
            pair => pair.Key, pair => pair.Key == repeated ? pair.Value with { Min = pair.Value.Min / 2, Max = pair.Value.Max / 2 } : pair.Value);

        Assert.Equal(0, SeasonFit.Rescale(item, before));
        Assert.Equal(was, Values(item));
    }

    [GameFact]
    public void A_property_in_one_list_moves_with_its_range()
    {
        if (!game.Available)
            return;

        // The first unique with a property that rolls in a range of its own.
        static ItemStat? Ranged(Item item, IReadOnlyDictionary<string, RollRange> ranges) =>
            Stats(item).GroupBy(KeyOf).Where(group => group.Count() == 1).Select(group => group.Single())
                .FirstOrDefault(stat => SeasonFit.KindOf(stat.Stat!) == FieldKind.Magnitude
                                        && ranges.TryGetValue(KeyOf(stat), out var range) && range.Min > 1 && range.Max > range.Min + 1);

        var (item, ranges, found) = ItemForge.UniqueNames()
            .Select(name => ItemForge.Unique(name, "103").Item)
            .Select(each => (Item: each, Ranges: AffixRanges.For(each)))
            .Select(each => (each.Item, each.Ranges, Stat: Ranged(each.Item, each.Ranges)))
            .First(each => each.Stat is not null);
        var single = found!;
        var now = ranges[KeyOf(single)];

        // Last season it rolled up to half as high; at its top then, at its top now.
        var before = ranges.ToDictionary(pair => pair.Key,
            pair => pair.Key == KeyOf(single) ? new RollRange(now.Min / 2, now.Max / 2) : pair.Value);
        single.Value = now.Max / 2;

        Assert.Equal(1, SeasonFit.Rescale(item, before));
        Assert.Equal(now.Max, single.Value);
    }

    [GameFact]
    public void A_skills_levels_keep_their_place_in_a_range_that_moved()
    {
        if (!game.Available)
            return;

        // The first unique with a "+N to a skill" that rolls in a range - Halaberd's Reign's Battle Orders, 2-3 in S14.
        var (item, line, range) = ItemForge.UniqueNames()
            .Select(name => ItemForge.Unique(name, "103").Item)
            .SelectMany(each => Stats(each).Where(stat => stat.Stat == "item_singleskill")
                .Select(stat => (Item: each, Line: stat, Range: AffixRanges.For(each).GetValueOrDefault(KeyOf(stat)))))
            .First(each => each.Range is { } found && found.Max > found.Min);

        // Last season it rolled one lower at each end; at its top then, at its top now.
        var before = AffixRanges.For(item).ToDictionary(pair => pair.Key,
            pair => pair.Key == KeyOf(line) ? new RollRange(range!.Min - 1, range.Max - 1) : pair.Value);
        line.Value = range!.Max - 1;

        Assert.Equal(1, SeasonFit.Rescale(item, before));
        Assert.Equal(range.Max, line.Value);
    }
}
