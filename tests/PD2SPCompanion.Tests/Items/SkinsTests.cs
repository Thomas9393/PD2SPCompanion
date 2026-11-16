using PD2SPCompanion.Core.Graphics;

using Xunit;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The special skins (<see cref="Skins"/>): Season 12's five drawn from the item's own
/// picture with "_se", Season 14's eighteen from a picture named after the unique - Buriza's `invburiza_se`, which has
/// no plain twin - and every one of the 23 a unique that may carry the stat, so the legitimacy check says nothing.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SkinsTests(Game game)
{
    private static D2SLib.Model.Save.Item Skinned(string index, bool skinned = true, int value = 5)
    {
        var entry = GrailCatalog.Uniques().First(each => ItemPresenter.QualityIndex(each.Item) == index);
        var item = ItemForge.UniqueAt(entry.Row, "").Item;
        if (skinned)
            Corruptions.AddStat(item.StatLists[0], Skins.Stat, value);
        return item;
    }

    private static string[] SkinLines(D2SLib.Model.Save.Item item) =>
        [.. ItemLines.Of(item, ItemPresenter.Describe(item), out _, out _).Where(line => line.Tone == ItemLine.Skin).Select(line => line.Text)];

    /// <summary>
    /// The skin's line as Season 14's game prints it (descfunc 30, no string; test items seen in game): the
    /// anniversary five at 5 still read "5th Anniversary Edition", a Special Edition at 1 reads nothing.
    /// </summary>
    [GameFact]
    public void An_anniversary_skin_is_said_and_a_special_edition_is_not()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        Assert.Equal(["5th Anniversary Edition"], SkinLines(Skinned("Harlequin Crest")));
        Assert.Empty(SkinLines(Skinned("Buriza-Do Kyanon", value: Skins.SpecialEdition)));
    }

    [GameTheory]
    [InlineData("Harlequin Crest", "invcap_se")]
    [InlineData("Buriza-Do Kyanon", "invburiza_se")]
    [InlineData("Headhunter's Glory", "invheadhunter_se")]
    [InlineData("Wartraveler", "invwartravellers_se")]
    public void A_skinned_unique_is_drawn_with_its_skin(string index, string picture)
    {
        if (!game.Available || SpriteLoader.Current?.Has(picture) != true)
            return;

        Assert.Equal(picture, ItemPresenter.Describe(Skinned(index)).SpriteName);
        Assert.NotEqual(picture, ItemPresenter.Describe(Skinned(index, skinned: false)).SpriteName);
    }

    /// <summary>
    /// Ormus' Robes' skin of three by the element of its random Sorceress skill - read off the skill's param, where the
    /// stat holds it (encode 1): read as SkillId, every robe took the fire one. Which skin the game draws
    /// is not yet seen; this pins the editor's reading of the robe.
    /// </summary>
    [GameTheory]
    [InlineData(44, "invormus_c_se")]
    [InlineData(56, "invormus_f_se")]
    [InlineData(48, "invormus_l_se")]
    public void A_skinned_Ormus_is_drawn_by_its_skill_element(int skill, string picture)
    {
        if (!game.Available || SpriteLoader.Current?.Has(picture) != true)
            return;

        var robe = Skinned("Ormus' Robes");
        robe.StatLists[0].Stats.Single(stat => stat.Stat == "item_singleskill").Param = skill;
        Assert.Equal(picture, ItemPresenter.Describe(robe).SpriteName);
    }

    [GameFact]
    public void Every_unique_that_can_drop_skinned_is_a_row_and_carries_the_stat_unflagged()
    {
        if (!game.Available)
            return;

        Assert.Equal(23, Skins.Uniques.Count);
        foreach (string index in Skins.Uniques)
        {
            var item = Skinned(index);
            Assert.True(Skins.CanHave(item), index);
            Assert.DoesNotContain(Legitimacy.Check(item), finding => finding.Said.Contains(Skins.Stat));
        }
    }
}
