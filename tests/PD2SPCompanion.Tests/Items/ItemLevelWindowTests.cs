using System.Diagnostics;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The item levels the item level die rolls among: every level the legitimacy
/// check says nothing more against - so never under what the affixes need, and the item's own level among them
/// where it is a real one.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ItemLevelWindowTests(Game game)
{
    private const string Stamp = "103";

    [GameFact]
    public void The_window_starts_at_what_the_affixes_need()
    {
        if (!game.Available)
            return;

        var clock = Stopwatch.StartNew();
        for (int seed = 0; seed < 20; seed++)
        {
            var ring = ItemForge.BaseAs("rin", ItemQuality.Rare, Stamp, new Random(seed)).Item;
            byte held = ring.ItemLevel;

            var window = Legitimacy.LegalItemLevels(ring);

            Assert.Equal(held, ring.ItemLevel);
            Assert.NotEmpty(window);
            Assert.Contains((int)held, window);
            if (MagicAffixes.LeastItemLevel(ring) is { } least)
                Assert.True(window.Min() >= least, $"seed {seed}: window from {window.Min()}, the affixes need {least}");
        }

        // Twenty items, a hundred judgements each: quick enough for a click.
        Assert.True(clock.ElapsedMilliseconds < 20 * 400, $"{clock.ElapsedMilliseconds} ms for twenty windows");
    }

    [GameFact]
    public void A_unique_window_is_quick()
    {
        if (!game.Available)
            return;

        long slowest = 0;
        string which = "";
        foreach (int row in ItemForge.EnabledUniques().Take(120))
        {
            var unique = ItemForge.UniqueAt(row, Stamp).Item;
            var clock = Stopwatch.StartNew();
            Legitimacy.LegalItemLevels(unique);
            if (clock.ElapsedMilliseconds > slowest)
                (slowest, which) = (clock.ElapsedMilliseconds, ItemPresenter.Describe(unique).Name);
        }

        Assert.True(slowest < 150, $"{which}: {slowest} ms for its window");
    }

    [GameFact]
    public void A_plain_item_may_be_any_level()
    {
        if (!game.Available)
            return;

        var cap = ItemForge.BaseAs("cap", ItemQuality.Normal, Stamp, new Random(1)).Item;
        Assert.Equal(Enumerable.Range(1, 99), Legitimacy.LegalItemLevels(cap));
    }
}
