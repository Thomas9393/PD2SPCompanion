using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Swapping two panes empties both, so a refusal there has the most to lose. The way back asks nothing of a file: asked
/// again, the file that refused an item once refuses it again, and that item is in no list. Each item comes back to its
/// page, its cell and its place in the list, so the file reads as it did.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SwapRefusedTests(Program program)
{
    private const string Odd = "50";

    private CharacterSession Session => program.Main.Session;
    private SharedStashSession Stash => program.Get<SharedStashSession>();
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    /// <summary>The item that gave the stash its era leaves with its page, is refused on the other, and would be refused again on its own.</summary>
    [CorpusFact]
    public void An_item_refused_twice_over_by_a_swap_of_two_shared_pages_is_still_on_its_page()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                var first = Stash.HeldItems.First();
                first.Version = Odd;
                int page = SharedStash.PageOf(first)!.Value;
                string empty = page == 5 ? "Shared Page 6" : "Shared Page 5";
                program.Panes($"Shared Page {page}", empty);
                var before = Stash.HeldItems.Select(item => (item, item.Location, item.X, item.Y, item.Id)).ToList();

                Page.SwapPanesCommand.Execute(program.Section());

                Assert.EndsWith("Nothing was swapped.", Session.Status);
                Assert.True(Stash.Holds(first), "the item is in no list");
                Assert.Equal(before, Stash.HeldItems.Select(item => (item, item.Location, item.X, item.Y, item.Id)).ToList());
                Assert.Empty(program.Grid(empty).Items);
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }
}
