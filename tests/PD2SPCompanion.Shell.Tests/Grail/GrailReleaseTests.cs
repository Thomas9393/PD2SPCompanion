using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Grail;

/// <summary>
/// A grail card chosen searches for its copies; ending that search lets the card go too (the
/// card stayed selected over a search that had ended), and chosen again it searches again.
/// The card's search is the standing search: its panel comes up, and the panel's cross ends it.
/// <para>
/// What the grail and the search decide between them. That the list on screen lets go of the card with them,
/// and that the cross is there to click, is the UI case's to show (units/grail-release.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GrailReleaseTests(Program program)
{
    private const string Piece = "Death Cleaver";

    private MainViewModel Main => program.Main;
    private SearchWindowViewModel Panel => Main.SearchWindow!;
    private VaultFilter Filter => program.Get<VaultService>().Filter;

    [CorpusFact]
    public void A_card_chosen_searches_for_its_copies_the_search_ended_lets_it_go_and_chosen_again_it_searches_again()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                var card = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                Assert.True(card.IsClickable, "the pad's vault has the piece found");

                grail.Selected = card;
                Assert.Same(card, grail.Selected);
                Assert.Equal(Piece, Filter.Name);
                Assert.False(Panel.IsShown, "the card's search is the grail's own: the Search page is not up for it");

                // The search ended from elsewhere.
                Panel.End();
                Assert.False(Filter.IsFiltering);
                Assert.Null(grail.Selected);

                grail.Selected = card;
                Assert.Same(card, grail.Selected);
                Assert.Equal(Piece, Filter.Name);
            }
            finally
            {
                Panel.End();
                GrailHands.Rest(grail);
            }
        });
    }

    [CorpusFact]
    public void A_search_typed_in_the_cards_place_lets_the_card_go_and_stays()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                grail.Selected = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                Assert.NotNull(grail.Selected);

                // Another search in its place is the user's own: the card goes, the search stays.
                Filter.Name = "shael";
                Assert.Null(grail.Selected);
                Assert.Equal("shael", Filter.Name);
            }
            finally
            {
                Panel.End();
                GrailHands.Rest(grail);
            }
        });
    }
}
