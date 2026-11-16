namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A forged "Reanimate as" raises the monster its row names, where an item
/// that reanimates defaulted to the skeleton. properties.txt's function 24 keeps the monster in the row's
/// parameter, and the writer dropped it: monster 0, a Skeleton, on every one.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ForgedReanimateTests(Game game)
{
    [GameTheory]
    [InlineData("Demon Machine", 492, 6)]
    [InlineData("Tomb Reaver", 1, 10)]
    [InlineData("Darkforge Spawn", 312, 5)]
    [InlineData("Leoric's Mithril Blade", 440, 8)]
    public void A_forged_reanimate_raises_its_own_monster(string name, int monster, int chance)
    {
        if (!game.Available)
            return;

        var item = ItemForge.Unique(name, "103").Item;
        var line = Assert.Single(ItemStats.Every(item), stat => stat.Stat == "item_reanimate");
        Assert.Equal(monster, line.Param);
        Assert.Equal(chance, line.Value);
        Assert.Empty(Legitimacy.Check(item));
    }

    /// <summary>
    /// Two rows, two lines, as the game writes them: a Sacred Totem the Demonic Cube remade in game holds
    /// "6% Reanimate as: Fallen" and "3% Reanimate as: Fallen Shaman". The editor merged them into one
    /// Serpent Magus, as the PD2 site's data shows them - the site's way, not the game's. A totem built from the
    /// site's one line is split back.
    /// </summary>
    [GameFact]
    public void Sacred_Totems_two_rows_are_two_lines_and_the_sites_one_is_split()
    {
        if (!game.Available)
            return;

        var totem = ItemForge.Unique("Sacred Totem", "103").Item;
        Assert.Equal([(19, 6), (58, 3)], ItemStats.Every(totem).Where(stat => stat.Stat == "item_reanimate").Select(stat => (stat.Param ?? 0, stat.Value)));
        Assert.Empty(Legitimacy.Check(totem));

        // As the site gives it: one line, 19 + 58, the Fallen's chance.
        var list = totem.StatLists[0].Stats;
        list.RemoveAll(stat => stat.Stat == "item_reanimate" && stat.Param == 58);
        list.Single(stat => stat.Stat == "item_reanimate").Param = 77;
        AffixRanges.SplitSiteReanimate(totem);
        Assert.Equal([(19, 6), (58, 3)], ItemStats.Every(totem).Where(stat => stat.Stat == "item_reanimate").Select(stat => (stat.Param ?? 0, stat.Value)));
        Assert.Empty(Legitimacy.Check(totem));
    }
}
