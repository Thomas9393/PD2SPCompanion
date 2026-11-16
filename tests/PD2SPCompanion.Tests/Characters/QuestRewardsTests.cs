namespace PD2SPCompanion.Tests.Characters;

/// <summary>What a quest completed from the Quests section does not hand out, for its confirm to say.</summary>
public sealed class QuestRewardsTests
{
    private static QuestProgress.Info Quest(string key) => QuestProgress.All.Single(each => each.Key == key);

    [Theory]
    [InlineData("ActI.TheSearchForCain", 0, "Akara's ring")]
    [InlineData("ActIII.BladeOfTheOldReligion", 1, "Ormus's corrupted ring")]
    [InlineData("ActV.RescueOnMountArreat", 2, "Qual-Kehk's runes")]
    [InlineData("ActV.PrisonOfIce", 0, "Anya's class rare")]
    [InlineData("ActV.PrisonOfIce", 2, "Anya's class rare|Anya's maps")]
    [InlineData("ActI.DenOfEvil", 0, "")]
    [InlineData("ActII.TheHoradricStaff", 0, "")]
    public void Item_rewards_not_given_are_named(string key, int difficulty, string expected) =>
        Assert.Equal(expected, string.Join("|", QuestProgress.NotGiven(Quest(key), difficulty)));

    [Theory]
    [InlineData("ActII.TheHoradricStaff", "Horadric Cube: from the chest in Act II, or in Travincal.")]
    [InlineData("ActIII.TheGuardian", "Mephisto's Soulstone: talk to Cain.")]
    [InlineData("ActI.DenOfEvil", null)]
    public void Where_to_get_a_quest_item_is_said(string key, string? expected) =>
        Assert.Equal(expected, QuestProgress.HowToGet(Quest(key)));

    [Fact]
    public void Every_named_quest_exists() =>
        Assert.All(new[] { "ActI.TheSearchForCain", "ActIII.BladeOfTheOldReligion", "ActV.RescueOnMountArreat", "ActV.PrisonOfIce", "ActII.TheHoradricStaff", "ActIII.TheGuardian" },
                   key => Assert.Single(QuestProgress.All, each => each.Key == key));
}
