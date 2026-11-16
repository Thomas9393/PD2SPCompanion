using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>Small, clear vault findings.</summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultReviewFixesTests(Program program)
{
    private VaultFilter Filter => program.Get<VaultService>().Filter;

    /// <summary>Gold staged is pending, or a switch of vault would credit it to the other vault at the next save.</summary>
    [CorpusFact]
    public void Gold_staged_holds_a_switch_of_vault()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var moves = program.Get<VaultMoves>();
            Assert.False(moves.HasPending);

            moves.StageGold(PD2SPCompanion.Core.Collection.MoveRecovery.CarriedPurse, 5);
            Assert.True(moves.HasPending);

            moves.StageGold(PD2SPCompanion.Core.Collection.MoveRecovery.CarriedPurse, -5);
            Assert.False(moves.HasPending);
        });
    }

    /// <summary>A preset asked over a badge's search asks the preset, and no longer that one id too.</summary>
    [CorpusFact]
    public void A_preset_ends_a_badges_search()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Filter.SameId = 0x12345678;
            Filter.Apply(new SearchPreset { ItemName = "fortune" });
            Assert.False(Filter.HasSameId);
            Filter.Clear();
        });
    }

    /// <summary>Only duplicates stands in what a snapshot keeps, so leaving the page and coming back keeps asking it.</summary>
    [CorpusFact]
    public void Only_duplicates_comes_back_with_the_search()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            bool offered = Filter.OffersDuplicates;
            try
            {
                Filter.OffersDuplicates = true;
                Filter.Duplicates = VaultFilter.DuplicateChoices[1];

                var kept = Filter.Snapshot();
                Assert.False(kept.AsksTheSameAs(new SearchPreset()));
                Filter.Empty();
                Assert.NotEqual(true, Filter.Duplicates.Flag);

                Filter.Apply(kept);
                Assert.Equal(true, Filter.Duplicates.Flag);

                // A preset that does not ask it lets it go.
                Filter.Apply(new SearchPreset());
                Assert.NotEqual(true, Filter.Duplicates.Flag);
            }
            finally
            {
                Filter.Clear();
                Filter.OffersDuplicates = offered;
            }
        });
    }
}
