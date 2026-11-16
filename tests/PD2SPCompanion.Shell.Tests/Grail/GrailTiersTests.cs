using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Grail;

/// <summary>
/// The Sets tab's tiers, All / Common / Uncommon / Class, as the game's icons over its tab (screenshot):
/// a tier shows its set pieces only, the drop-down lists its sets only, and a set of another tier chosen in
/// it goes back to any. The Uniques tab has no tiers: a tier chosen leaves its list as it was.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GrailTiersTests(Program program)
{
    [CorpusFact]
    public void A_tier_shows_its_set_pieces_and_lists_its_sets_and_a_set_of_another_goes_back_to_any()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                grail.ShowTabCommand.Execute(GrailViewModel.Sets);
                int all = grail.Cards.Count;

                grail.ShowTierCommand.Execute(GrailShow.Class);
                Assert.NotEmpty(grail.Cards);
                Assert.True(grail.Cards.Count < all);
                Assert.All(grail.Cards, card => Assert.Equal(SetTier.Class, card.Tier));
                Assert.Contains(grail.Cards, card => card.SetName == "Immortal King");
                var listed = grail.QuickChoices.Where(choice => !choice.IsAny).Select(choice => choice.Code!).ToList();
                Assert.Equal(7, listed.Count);
                Assert.All(listed, set => Assert.Equal(SetTier.Class, SetNames.TierOf(set)));

                // A class set chosen, then Common: the drop-down goes back to any, and only common pieces show.
                grail.Quick = grail.QuickChoices.First(choice => choice.Code == "Immortal King");
                Assert.All(grail.Cards, card => Assert.Equal("Immortal King", card.SetName));
                grail.ShowTierCommand.Execute(GrailShow.Common);
                Assert.True(grail.Quick?.IsAny);
                Assert.All(grail.Cards, card => Assert.Equal(SetTier.Common, card.Tier));
                Assert.Contains(grail.Cards, card => card.SetName == "Civerb's Vestments");

                // All again: every piece.
                grail.ShowTierCommand.Execute(GrailShow.All);
                Assert.Equal(all, grail.Cards.Count);

                // The Uniques tab keeps no tier: a tier chosen on Sets does not narrow it.
                grail.ShowTierCommand.Execute(GrailShow.Class);
                grail.ShowTabCommand.Execute(GrailViewModel.Uniques);
                Assert.Contains(grail.Cards, card => card.Tier is null);
                Assert.DoesNotContain(grail.Cards, card => card.Tier is not null);
            }
            finally
            {
                grail.ShowTierCommand.Execute(GrailShow.All);
                grail.Quick = PD2SPCompanion.Shell.Services.Vault.VaultFilter.Any;
                GrailHands.Rest(grail);
            }
        });
    }
}
