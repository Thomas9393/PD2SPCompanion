using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The game's Uniques tab as the Grail page lists it (<see cref="GrailCatalog.UniqueCards"/>; screenshots
/// in game): no jewels and no unique maps, one card a name - Arm of King Leoric once - and the specials the Ubers
/// and dungeon bosses drop shown and not counted. Under Season 14's tables that counts to the game's own 427.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class GrailCardsTests(Game game)
{
    [GameFact]
    public void The_tabs_cards_count_to_the_games_427_under_season_14()
    {
        if (!game.Available || Game.Tables is not null || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        var cards = GrailCatalog.UniqueCards();

        Assert.Equal(427, cards.Count(card => card.Counts));
        Assert.Equal(["Aidan's Scar", "Band of Skulls", "Cage of the Unsullied", "Dark Abyss", "Hadriel's Hand",
                      "Itherael's Path", "Overlord's Helm", "The Third Eye"],
                     cards.Where(card => !card.Counts).Select(card => card.Name).Order());

        Assert.Equal(2, Assert.Single(cards, card => card.Name == "Arm of King Leoric").Rows.Count);
        Assert.DoesNotContain(cards, card => card.Name == "Rainbow Facet");
        Assert.DoesNotContain(cards, card => card.Name == "Zhar's Sanctum");
        Assert.Contains(cards, card => card.Name == "Annihilus" && card.Counts);
    }

    /// <summary>
    /// The Sets tab's tiers, All / Common / Uncommon / Class (screenshot): the seven class sets,
    /// the classic game's sixteen common, the expansion's other nine uncommon - and every one of the 128 pieces in one.
    /// </summary>
    [GameFact]
    public void The_sets_tiers_are_the_seven_class_sets_the_classic_sixteen_and_the_expansions_nine_under_season_14()
    {
        if (!game.Available || Game.Tables is not null || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        var names = GrailCatalog.Sets().Select(entry => SetNames.Of(entry.Item)).OfType<string>().Distinct().ToList();
        Assert.Equal(128, GrailCatalog.Sets().Count);
        Assert.All(GrailCatalog.Sets(), entry => Assert.NotNull(SetNames.Of(entry.Item)));

        Assert.Equal(["Aldur's Watchtower", "Griswold's Legacy", "Immortal King", "M'avina's Battle Hymn", "Natalya's Odium",
                      "Tal Rasha's Wrappings", "Trang-Oul's Avatar"],
                     names.Where(name => SetNames.TierOf(name) == SetTier.Class).Order());
        Assert.Equal(16, names.Count(name => SetNames.TierOf(name) == SetTier.Common));
        Assert.Equal(9, names.Count(name => SetNames.TierOf(name) == SetTier.Uncommon));
        Assert.Equal(SetTier.Common, SetNames.TierOf("Civerb's Vestments"));
        Assert.Equal(SetTier.Uncommon, SetNames.TierOf("Cow King's Leathers"));
    }
}
