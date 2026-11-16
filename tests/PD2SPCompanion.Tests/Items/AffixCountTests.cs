using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// How many affixes a fresh roll gives, and on which side:
/// a rare by PD2's floors by item level, a rare jewel four, a magic item
/// two from 65 / 85 / 90, and a craft's picks each flipping a coin for their side - 3+1 and 1+3 as well as 2+2.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class AffixCountTests(Game game)
{
    private const string Stamp = "103";

    private static HashSet<int> Counts(Item item) =>
        [.. Enumerable.Range(0, 400).Select(seed => MagicAffixes.RollCount(item, new Random(seed)))];

    private static Item At(string code, ItemQuality quality, int itemLevel)
    {
        var item = ItemForge.BaseAs(code, quality, Stamp, new Random(5)).Item;
        item.ItemLevel = (byte)itemLevel;
        return item;
    }

    [GameFact]
    public void A_rare_rolls_at_least_its_item_levels_floor()
    {
        if (!game.Available)
            return;

        Assert.Equal([3, 4, 5, 6], Counts(At("rin", ItemQuality.Rare, 44)).Order());
        Assert.Equal([4, 5, 6], Counts(At("rin", ItemQuality.Rare, 45)).Order());
        Assert.Equal([5, 6], Counts(At("rin", ItemQuality.Rare, 65)).Order());
        Assert.Equal([6], Counts(At("rin", ItemQuality.Rare, 85)).Order());

        // A rare jewel is four, whatever its level.
        Assert.Equal([4], Counts(At("jew", ItemQuality.Rare, 30)));
        Assert.Equal([4], Counts(At("jew", ItemQuality.Rare, 90)));
    }

    [GameFact]
    public void A_magic_item_rolls_two_from_its_kinds_level()
    {
        if (!game.Available)
            return;

        Assert.Equal([1, 2], Counts(At("cap", ItemQuality.Magic, 64)).Order());
        Assert.Equal([2], Counts(At("cap", ItemQuality.Magic, 65)));
        Assert.Equal([1, 2], Counts(At("rin", ItemQuality.Magic, 84)).Order());
        Assert.Equal([2], Counts(At("rin", ItemQuality.Magic, 85)));
        Assert.Equal([2], Counts(At("jew", ItemQuality.Magic, 85)));
        Assert.Equal([1, 2], Counts(At("cm1", ItemQuality.Magic, 89)).Order());
        Assert.Equal([2], Counts(At("cm3", ItemQuality.Magic, 90)));

        // One affix is a suffix two times in three, as the game's half and quarter make it.
        int suffixes = 0;
        for (int seed = 0; seed < 600; seed++)
        {
            var ring = At("rin", ItemQuality.Magic, 30);
            Array.Clear(ring.MagicPrefixIds!);
            Array.Clear(ring.MagicSuffixIds!);
            if (MagicAffixes.Draw(ring, 1, new Random(seed)).Keys.Single() is { Prefix: false })
                suffixes++;
        }
        Assert.InRange(suffixes, 340, 460);
    }

    [GameFact]
    public void A_craft_splits_its_four_by_a_coin_per_pick()
    {
        if (!game.Available)
            return;

        var splits = new HashSet<(int Prefixes, int Suffixes)>();
        for (int seed = 0; seed < 200; seed++)
        {
            var amulet = At("amu", ItemQuality.Rare, 80);
            amulet.Quality = ItemQuality.Craft;
            Array.Clear(amulet.MagicPrefixIds!);
            Array.Clear(amulet.MagicSuffixIds!);

            var picks = MagicAffixes.Draw(amulet, 4, new Random(seed));
            splits.Add((picks.Count(each => each.Key.Prefix), picks.Count(each => !each.Key.Prefix)));
        }

        Assert.Contains((3, 1), splits);
        Assert.Contains((2, 2), splits);
        Assert.Contains((1, 3), splits);
        Assert.DoesNotContain(splits, split => split.Prefixes > 3 || split.Suffixes > 3);
    }
}
