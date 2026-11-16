using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The legitimacy check remembers each item's findings by its bytes: what it
/// remembers must be what the rules say, and an item changed in place must be judged again. On an S13 character, so
/// pinned to S13's tables: under S14's its items are the season before's, which the check leaves alone.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class LegitimacyTests(Game game)
{
    private static List<Item> ItemsOf(string path)
    {
        var (save, _) = SaveVerifier.Load(path);
        return [.. save.PlayerItemList.Items
            .SelectMany(item => item.SocketedItems.Prepend(item))
            .Where(item => !item.IsCompact)];
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Remembered_findings_are_the_rules_findings()
    {
        if (!game.Pinned("S13"))
            return;

        foreach (var item in ItemsOf(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s")))
        {
            var first = Legitimacy.Check(item).Select(each => each.Said).ToList();
            var again = Legitimacy.Check(item).Select(each => each.Said).ToList();
            var fresh = Legitimacy.Judge(item).Select(each => each.Said).ToList();
            Assert.Equal(fresh, first);
            Assert.Equal(fresh, again);
        }
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void An_item_changed_in_place_is_judged_again()
    {
        if (!game.Pinned("S13"))
            return;

        // A unique with a ranged property: pushed far past its range, it must read as wrong at once.
        var item = ItemsOf(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"))
            .First(each => each.Quality == ItemQuality.Unique && Legitimacy.Judge(each).Count == 0
                           && each.StatLists.FirstOrDefault()?.Stats.Any(stat => stat.Value > 0) == true);

        Assert.Empty(Legitimacy.Check(item));

        var stat = item.StatLists[0].Stats.First(each => each.Value > 0);
        stat.Value += 500;

        Assert.NotEmpty(Legitimacy.Check(item));
    }
}
