using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The Demonic Cube on Ormus' Robes (the PD2 wiki, Chests: "The same skill will be chosen for both
/// random affixes"): another skill on both lines, with the chance and the level that skill has by the rule.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class OrmusRerollTests(Game game)
{
    [GameFact]
    public void The_cube_draws_another_skill_on_both_lines_and_its_chance_and_level()
    {
        if (!game.Available)
            return;

        var robes = ItemForge.Unique("Ormus' Robes", "105", RollAs.Maximum).Item;
        var stats = robes.StatLists.SelectMany(list => list.Stats).ToList();
        var grant = stats.Single(stat => stat.Stat == "item_singleskill");
        var cast = stats.Single(stat => stat.Stat == "item_skilloncast");
        var source = AffixRanges.RandomSkillCastsOn(robes).Single();

        var random = new Random(7);
        var seen = new HashSet<int>();
        for (int roll = 0; roll < 40; roll++)
        {
            int was = grant.Param!.Value;
            DemonicCube.Reroll(robes, random);

            Assert.NotEqual(was, grant.Param);
            Assert.Contains(grant.Param!.Value, RandomSkillCast.Pool);
            Assert.Equal(grant.Param, cast.SkillId);
            Assert.Equal(RandomSkillCast.Chance(grant.Param!.Value, source.Min), cast.Value);
            Assert.Equal(RandomSkillCast.Level(grant.Param!.Value, source.Max), cast.SkillLevel);
            seen.Add(grant.Param!.Value);
        }

        Assert.True(seen.Count > 5);
    }

    [GameFact]
    public void Another_item_has_nothing_to_draw()
    {
        if (!game.Available)
            return;

        Assert.False(RandomSkillCast.Reroll(ItemForge.Unique("The Stone of Jordan", "105", RollAs.Maximum).Item, new Random(1)));
    }
}
