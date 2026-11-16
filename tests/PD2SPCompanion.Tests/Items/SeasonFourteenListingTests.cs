using System.Text.Json;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// Season 14's site says two things differently, and a listing is read as it says them (the S14 ladder:
/// 257 of 15,154 items flagged, every one the reading's): poison as the tooltip shows it - total damage and seconds -
/// and the item's real level, where Season 13's gave 96 to nearly all. Real ladder items, each silent read. Run with
/// the tables of Season 14, as the items are.
/// <para>
/// The items are kept beside this file (<c>SeasonFourteenListings.json</c>), copied out of the corpus's armories: a
/// fetch of the ladder replaces those, and a character who changed gear left these tests checking nothing, passing.
/// </para>
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SeasonFourteenListingTests(Game game)
{
    private static Item Listed(string key)
    {
        using var kept = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Items", "SeasonFourteenListings.json")));
        return TradeItems.Build(kept.RootElement.GetProperty(key)).Item;
    }

    private bool SeasonFourteen => game.Available && D2SLib.Model.TXT.StatEncoding.PreviousSeason == "S13";

    private static int Of(Item item, string stat) => ItemStats.Every(item).First(each => each.Stat == stat).Value;

    [GameFact]
    public void Poison_said_as_the_tooltip_shows_it_is_read_back_to_the_saves_numbers()
    {
        if (!SeasonFourteen)
            return;

        var charm = Listed("poison_charm");

        // "+13 Poison Damage over 2 Seconds": the small charm row's own 65 over 50 frames, not 13 over 2.
        Assert.Equal((65, 65, 50), (Of(charm, "poisonmindam"), Of(charm, "poisonmaxdam"), Of(charm, "poisonlength")));
        Assert.Empty(Legitimacy.Check(charm));
    }

    [GameTheory]
    [InlineData("pestilent_rare")]
    [InlineData("pestilence_rare")]
    public void A_poison_affix_is_found_by_every_number_that_shows_the_same(string key)
    {
        if (!SeasonFourteen)
            return;

        // 385 over 2 seconds is Pestilent's 1970, where the nearest number is 1971; 50 over 5 seconds is of Pestilence's
        // 103, where it is 102.
        Assert.Empty(Legitimacy.Check(Listed(key)));
    }

    [GameFact]
    public void A_circlet_is_searched_to_its_affix_level_and_keeps_a_prefix_for_its_sockets()
    {
        if (!SeasonFourteen)
            return;

        // A Diadem at item level 87 rolls at 99 (magic level 18): Priest's (+2 Paladin, level 90) is in reach, and its
        // 3 sockets want Artisan's - "of Memory" takes the mana per level as a suffix.
        Assert.Empty(Legitimacy.Check(Listed("diadem")));
    }

    [GameFact]
    public void A_shield_cubed_up_its_family_keeps_each_steps_grant()
    {
        if (!SeasonFourteen)
            return;

        // A Troll Nest that dropped a Bone Shield (its automagic, 2-4 magic damage reduction) holds 7: +2 a step, the
        // recipes naming the family's second type, bshi.
        Assert.Empty(Legitimacy.Check(Listed("shield")));
    }

    [GameFact]
    public void Season_13s_poison_numbers_are_the_saves_own()
    {
        if (!game.Available)
            return;

        // "Adds 20 Poison Damage over 50 Seconds" - Season 13's site's words on the save's 20 over 50 frames.
        var charm = Listed("blight_charm");

        Assert.Equal((20, 50), (Of(charm, "poisonmindam"), Of(charm, "poisonlength")));
    }
}
