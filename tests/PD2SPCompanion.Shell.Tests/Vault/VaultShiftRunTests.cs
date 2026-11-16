using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A Shift+click's run on the vault's sheet is the folder's, in reading order: the sheet is
/// drawn 15 rows at a time, and the run stopped at the edge of the stretch the click was in
/// (IDragPicker.ReadingOrder, which DragPick asks before the list a card is drawn in). The pad's folders are a
/// stretch long at most: here the run of two that lie side by side, whichever it is asked from. The run across
/// the edge of a stretch, which was the defect, is VaultLongSheetTests'.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultShiftRunTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    private const string Fortune = "Small Charm of Fortune", Garnet = "Garnet Small Charm of Life";

    public void Dispose() => _book.PutAway();

    [CorpusFact]
    public void A_run_is_the_folders_cards_from_the_one_to_the_other_as_they_lie()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            // By name: Garnet, then Fortune.
            VaultBook.GoTo(Page, "Small");
            Page.Order.Pick(Page.Order.Options.First(option => option.Label == "Name"));
            if (Page.Order.Descending)
                Page.Order.Pick(Page.Order.Options.First(option => option.Label == "Name"));

            var fortune = VaultBook.Card(Page, Fortune);
            var garnet = VaultBook.Card(Page, Garnet);

            // The run is the folder's cards as they lie, whichever of the two it is asked from.
            var run = ((IDragPicker)Page).ReadingOrder(fortune, garnet)?.ToList();
            Assert.NotNull(run);
            Assert.Equal([garnet, fortune], run);
            Assert.Equal(run, ((IDragPicker)Page).ReadingOrder(garnet, fortune)?.ToList());
        });
    }
}
