using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Grail;

/// <summary>
/// A grail card marked "In compendium" where the mode's Compendium holds a copy of its row:
/// a real compendium, Arkaine's Valor in it,
/// put in the run's save folder - and nothing marked without one. Read only: the file is the same after.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GrailOwnedTests(Program program)
{
    private static string Specimen =>
        Path.Combine(Program.Corpus, "specimens", "s14-discovery", "after-flamebellow", "pd2_shared.compendium");

    private string Placed => Path.Combine(program.Folder, "saves", "pd2_shared.compendium");

    [CorpusFact]
    public void A_card_whose_item_lies_in_the_compendium_says_so_and_no_other_does()
    {
        if (!program.Ready() || !File.Exists(Specimen))
            return;

        File.Copy(Specimen, Placed, overwrite: true);
        byte[] before = File.ReadAllBytes(Placed);
        program.On(() =>
        {
            // Read again, as the save folder's watcher reads it when the game writes it.
            program.Get<PD2SPCompanion.Shell.Services.Character.CompendiumSession>().Load();
            var grail = GrailHands.OnRight(program);
            try
            {
                grail.Load();
                var arkaine = GrailHands.Card(grail, GrailViewModel.Uniques, "Arkaine's Valor");
                Assert.True(arkaine.IsOwned);

                grail.Search = "";
                Assert.Equal(1, grail.Cards.Count(card => card.IsOwned));
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });

        Assert.Equal(before, File.ReadAllBytes(Placed));
        File.Delete(Placed);
        program.On(() =>
        {
            program.Get<PD2SPCompanion.Shell.Services.Character.CompendiumSession>().Load();
            var grail = GrailHands.OnRight(program);
            grail.Load();
            Assert.DoesNotContain(grail.Cards, card => card.IsOwned);
        });
    }
}
