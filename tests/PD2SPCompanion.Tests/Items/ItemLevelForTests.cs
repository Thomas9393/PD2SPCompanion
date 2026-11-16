namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The item level an affix level asks of a base is the inverse of <see cref="MagicAffixes.AffixLevel(string?, int)"/>:
/// worked out as affix level + qlvl / 2, it missed the item level raised to the base's and the turn near 99 - a Vortex
/// Shield (qlvl 85) rolls at 71 or more at any item level, and the picker said "item level 72" for a level 30 affix
/// (a Paladin shield).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ItemLevelForTests(Game game)
{
    [GameTheory]
    [InlineData("paf")] // Vortex Shield, qlvl 85: raised and past the turn at every item level
    [InlineData("7wa")] // Berserker Axe: the turn
    [InlineData("ci0")] // Circlet: a magic level
    [InlineData("hax")] // Hand Axe: plain
    public void It_is_the_lowest_item_level_whose_affix_level_reaches_it(string code)
    {
        if (!game.Available)
            return;

        var item = AffixSweepTests.Fresh(code);
        for (int affixLevel = 1; affixLevel <= 99; affixLevel++)
        {
            int needs = MagicAffixes.ItemLevelFor(item, affixLevel);
            if (MagicAffixes.AffixLevel(code, 99) < affixLevel)
            {
                Assert.Equal(99, needs);
                continue;
            }

            Assert.True(MagicAffixes.AffixLevel(code, needs) >= affixLevel, $"{code} {affixLevel}: {needs} short");
            Assert.True(needs == 1 || MagicAffixes.AffixLevel(code, needs - 1) < affixLevel, $"{code} {affixLevel}: {needs} not the lowest");
        }

        if (code == "paf")
        {
            Assert.Equal(1, MagicAffixes.ItemLevelFor(item, 30));
            Assert.Equal(86, MagicAffixes.ItemLevelFor(item, 72));
        }
    }
}
