using D2SLib.Model.Save;

using Xunit.Abstractions;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A crafted staff upgraded a tier, as the game did it (GambleTest: a crafted War Staff
/// "Dire Song" cubed with Ort + Amn + Perfect Sapphire beside an untouched copy): it became a Rune Staff, every
/// staff mod kept (+1 Cold Enchant, +3 Chilling Armor, +1 Hydra), and its faster cast rate went from 10 to 30 -
/// the staves' crafted recipe's own <c>cast2 20</c>. Nothing else moved but the current durability, rolled.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class StaffUpgradeTests(Game game, ITestOutputHelper output)
{
    private const string Stamp = "103";

    private static ItemStat Skill(int skill, int levels) => new()
    {
        Id = StatCatalogue.IdOf("item_singleskill"), Stat = "item_singleskill", Param = skill, Value = levels,
    };

    [CorpusFact]
    public void A_crafted_staff_keeps_its_staff_mods_and_gains_the_recipes_cast_rate()
    {
        if (!game.Available)
            return;

        var staff = ItemForge.BaseAs("wst", ItemQuality.Rare, Stamp, new Random(5)).Item;
        staff.Quality = ItemQuality.Craft;
        var list = ItemStats.Home(staff);
        list.Stats.RemoveAll(stat => stat.Stat is "item_singleskill" or "item_fastercastrate");
        // Cold Enchant 52, Chilling Armor 60, Hydra 62 - the control's three.
        list.Stats.AddRange([Skill(52, 1), Skill(60, 3), Skill(62, 1)]);
        Corruptions.AddStat(list, "item_fastercastrate", 10);

        BaseUpgrades.Upgrade(staff, new Random(1));

        Assert.Equal("8ws", staff.Code.Trim());
        var stats = ItemStats.Every(staff).ToList();
        Assert.Equal(30, stats.Where(stat => stat.Stat == "item_fastercastrate").Sum(stat => stat.Value));
        var skills = stats.Where(stat => stat.Stat == "item_singleskill").Select(stat => (stat.Param, stat.Value)).Order().ToList();
        Assert.Equal([(52, 1), (60, 3), (62, 1)], skills);
    }

    /// <summary>
    /// A rare Short Staff cubed in game with Ort + Amn + Perfect Sapphire (HandsTest): it became a Jo
    /// Staff, its stored automagic id still the tier it dropped with, so the credit for the climb finds the cast rate.
    /// </summary>
    [CorpusFact]
    public void A_rare_staff_cubed_in_game_keeps_its_automagic_id_and_the_credit_finds_the_recipes_cast_rate()
    {
        if (!game.Available || Game.Tables is not null)
            return;

        var save = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "HandsTestCubed.d2s")).Save;
        var staff = save.PlayerItemList.Items.First(each => each.Code.Trim() == "8ss");
        foreach (var line in ItemLines.Of(staff, ItemPresenter.Describe(staff), out _, out _))
            output.WriteLine(line.Text);

        Assert.Equal(42, staff.AutoAffixId);
        Assert.Equal(30, ItemStats.Every(staff).Where(stat => stat.Stat == "item_fastercastrate").Sum(stat => stat.Value));
        Assert.Contains(BaseUpgrades.Grants(staff), grant => grant.Min == 20 && grant.Max == 20);
    }
}
