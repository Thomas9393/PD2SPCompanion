using D2SLib.Model.Save;
using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A runeword's stored id across a season (<see cref="Migration.Fit"/>): the id is its key's string index, which moves
/// when a season's string tables grow. ShockSacri is an S13 character before and after the S14 client loaded it:
/// the game re-stamped his Ferocity 3261 -> 3391.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class MigrationRunewordTests(Game game)
{
    private static string Specimen(string when) =>
        Path.Combine(Game.Corpus, "specimens", "s14-launch-2026-10-23", when, "ShockSacri.d2s");

    private static Item Ferocity(string when)
    {
        var (save, _) = SaveVerifier.Load(Specimen(when));
        // His mercenary wears it.
        return save.PlayerItemList.Items.Concat(save.MercenaryItemList?.ItemList?.Items ?? [])
            .Single(item => Runewords.On(item)?.Key == "Ferocity");
    }

    [CorpusFact]
    public void A_runeword_stored_under_other_strings_is_stamped_with_this_seasons()
    {
        if (!game.Available || !File.Exists(Specimen("before")))
            return;

        var item = Ferocity("before");
        int right = Runewords.IdOf(Runewords.On(item)!)!.Value;
        item.RunewordId = (uint)(right + 7);

        var change = Assert.Single(Migration.Fit(item), each => each.Stat == "runeword");

        Assert.Equal(right + 7, change.Was);
        Assert.Equal(right, change.Now);
        Assert.Equal((uint)right, item.RunewordId);
    }

    [CorpusFact]
    public void A_runeword_already_stored_under_this_seasons_strings_is_left_alone()
    {
        if (!game.Available || !File.Exists(Specimen("before")))
            return;

        var item = Ferocity("before");
        item.RunewordId = (uint)Runewords.IdOf(Runewords.On(item)!)!.Value;

        Assert.DoesNotContain(Migration.Fit(item), each => each.Stat == "runeword");
    }

    [CorpusFact]
    public void Under_season_14_an_S13_runeword_comes_across_as_the_game_wrote_it()
    {
        // Season 14's own strings only: with another season's tables laid over, the id differs by design.
        if (!game.Available || Game.Tables is not null || !File.Exists(Specimen("live")))
            return;

        uint written = Ferocity("live").RunewordId;
        var carried = Ferocity("before");
        Assert.NotEqual(written, carried.RunewordId);

        Migration.Fit(carried);

        Assert.Equal(written, carried.RunewordId);
    }
}
