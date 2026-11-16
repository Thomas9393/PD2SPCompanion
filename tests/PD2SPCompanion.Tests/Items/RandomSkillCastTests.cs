using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// Ormus' Robes' random Sorceress skill and its cast:
/// the Forge left both lines off. Made now as 126 S13 ladder robes hold them - one skill of the pool,
/// +2, cast at 12% (8% with a cooldown) and level 26 less one per six levels of its requirement past six - and the
/// legitimacy check holds a robe to it. Season 14 adds two skills to the pool (350 robes on the market).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class RandomSkillCastTests(Game game)
{
    private static Item Ormus(Random? random = null)
    {
        int row = ItemForge.EnabledUniques().First(each => ItemForge.UniqueAt(each, "103").Item.Code.Trim() == "uui"
                                                           && ItemPresenter.Describe(ItemForge.UniqueAt(each, "103").Item).Name == "Ormus' Robes");
        return ItemForge.UniqueAt(row, "103", random is null ? RollAs.Maximum : RollAs.Random, random).Item;
    }

    [GameFact]
    public void A_forged_Ormus_grants_a_pool_skill_and_casts_the_same_one_and_is_silent()
    {
        if (!game.Available)
            return;

        for (int seed = 0; seed < 20; seed++)
        {
            var robe = Ormus(new Random(seed));
            var grant = Assert.Single(ItemStats.Every(robe), stat => stat.Stat == "item_singleskill");
            var cast = Assert.Single(ItemStats.Every(robe), stat => stat.Stat == "item_skilloncast");

            int skill = grant.Param!.Value;
            Assert.Contains(skill, RandomSkillCast.Pool);
            Assert.Equal(2, grant.Value);
            Assert.Equal(skill, cast.SkillId);
            Assert.DoesNotContain(Legitimacy.Judge(robe), finding => finding.Subject == "Random skill");
        }

        // Frozen Orb, required level 30: 12% at level 22. Blizzard has a cooldown: 8%.
        Assert.Equal((12, 22), (RandomSkillCast.Chance(64, 12), RandomSkillCast.Level(64, 26)));
        Assert.Equal(8, RandomSkillCast.Chance(59, 12));
    }

    /// <summary>
    /// Season 14's pool and casts as 350 ladder robes on the market hold them: twenty skills, Telekinesis and
    /// Fire Wall new, each cast at the chance and level seen - a genuine robe of each is silent.
    /// </summary>
    [GameTheory]
    [InlineData(36, 12, 26)] [InlineData(38, 12, 26)] [InlineData(39, 12, 26)] [InlineData(43, 12, 26)]
    [InlineData(44, 12, 26)] [InlineData(45, 12, 26)] [InlineData(46, 8, 26)] [InlineData(47, 12, 25)]
    [InlineData(48, 12, 25)] [InlineData(49, 12, 25)] [InlineData(51, 8, 25)] [InlineData(53, 12, 23)]
    [InlineData(55, 12, 24)] [InlineData(56, 8, 23)] [InlineData(59, 8, 23)] [InlineData(62, 12, 22)]
    [InlineData(64, 12, 22)] [InlineData(369, 12, 23)] [InlineData(376, 8, 23)] [InlineData(383, 12, 25)]
    public void Every_season_14_skill_is_in_the_pool_and_casts_as_the_market_robes_do(int skill, int chance, int level)
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        Assert.Contains(skill, RandomSkillCast.Pool);
        Assert.Equal((chance, level), (RandomSkillCast.Chance(skill, 12), RandomSkillCast.Level(skill, 26)));

        var robe = Ormus();
        var stats = robe.StatLists[0].Stats;
        stats.Single(stat => stat.Stat == "item_singleskill").Param = skill;
        var cast = stats.Single(stat => stat.Stat == "item_skilloncast");
        (cast.SkillId, cast.Value, cast.SkillLevel) = (skill, chance, level);
        Assert.DoesNotContain(Legitimacy.Judge(robe), finding => finding.Subject == "Random skill");
    }

    [GameFact]
    public void A_robe_whose_cast_is_off_its_rule_is_said()
    {
        if (!game.Available)
            return;

        var robe = Ormus();
        var cast = ItemStats.Every(robe).Single(stat => stat.Stat == "item_skilloncast");
        cast.SkillLevel += 5;
        Assert.Contains(Legitimacy.Judge(robe), finding => finding.Subject == "Random skill");
    }
}
