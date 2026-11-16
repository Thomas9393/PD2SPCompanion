using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A corruption made before a patch re-dealt it keeps what it gave: S14's second patch moved Half Freeze on boots,
/// gloves and belts from id 34 to 92. Boots carrying id 34 and Half Freeze read it as their corruption's with the build
/// before kept (the corpus's <c>builds</c>, as the app's season snapshot keeps it), and as nothing without.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CorruptionEarlierBuildTests(Game game)
{
    [GameFact]
    public void Boots_corrupted_before_the_patch_keep_their_half_freeze_where_the_build_before_is_kept()
    {
        var earlier = Game.EarlierCubemains().ToList();
        if (!game.Available || earlier.Count == 0)
            return;

        var boots = ItemForge.BaseAs("xlb", ItemQuality.Rare, Game.Stamp, new Random(5)).Item;
        var own = ItemStats.Home(boots);
        Corruptions.AddStat(own, Corruptions.IdStat, 34);
        Corruptions.AddStat(own, "item_halffreezeduration", 1);

        try
        {
            Corruptions.LoadEarlier(earlier);
            Assert.Contains("item_halffreezeduration", Corruptions.StatsOn(boots), StringComparer.OrdinalIgnoreCase);

            Corruptions.LoadEarlier([]);
            Assert.DoesNotContain("item_halffreezeduration", Corruptions.StatsOn(boots), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Game.UseEarlierBuilds();
        }
    }
}
