using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// What a worn item's requirements ask of the rest of the gear:
/// a piece counts where some order of putting them on reaches it from the character's own stats. Two Crystal Swords
/// in the hands of HandsTest, each asking 43 Strength.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class RequirementPathTests(Game game)
{
    private const int Dexterity = 200;

    private static (D2S Save, Item Right, Item Left) Hands()
    {
        var save = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "HandsTestCubed.d2s")).Save;
        var right = save.PlayerItemList.Items.First(item => item.Code.Trim() == "ssd");
        var left = save.PlayerItemList.Items.First(item => item.Code.Trim() == "hax");
        right.Code = "crs ";
        left.Code = "crs ";
        return (save, right, left);
    }

    private static void GivesStrength(Item item, int value) =>
        ItemStats.Home(item).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("strength"), Stat = "strength", Value = value });

    private static bool Met(D2S save, Item item, int strength) =>
        Requirements.Judge(save, WeaponSets.InHand, strength, Dexterity).Single(judged => ReferenceEquals(judged.Item, item)).Met;

    [CorpusFact]
    public void Two_pieces_that_hold_only_each_other_up_have_no_first_step()
    {
        if (!game.Available)
            return;

        var (save, right, left) = Hands();
        GivesStrength(right, 1);
        GivesStrength(left, 1);

        Assert.False(Met(save, right, 42));
        Assert.False(Met(save, left, 42));
    }

    [CorpusFact]
    public void A_piece_reached_through_another_counts()
    {
        if (!game.Available)
            return;

        // The right's own 43 is met; its 12 reach the left's 55.
        var (save, right, left) = Hands();
        left.Code = "lsd ";
        GivesStrength(right, 12);

        Assert.True(Met(save, right, 43));
        Assert.True(Met(save, left, 43));
        Assert.False(Met(save, left, 42));
    }

    [CorpusFact]
    public void A_piece_that_takes_strength_away_comes_last_and_the_one_it_undoes_drops()
    {
        if (!game.Available)
            return;

        var (save, right, left) = Hands();
        GivesStrength(right, -1);

        // The left asks 43 of 43 until the right's -1 is on. The right itself is met from the character's 43.
        Assert.True(Met(save, right, 43));
        Assert.False(Met(save, left, 43));
        Assert.True(Met(save, left, 44));
    }
}
