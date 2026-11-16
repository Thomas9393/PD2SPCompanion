using D2SLib.Model.Save;

using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A drag of the mouse between the real pages (<see cref="PageBench"/>): a card of the vault's let go of on a cell of the
/// Items page is on the character at that cell; an item of the character's let go of on the vault's sheet is a card there.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PageBenchTests(Program program) : IDisposable
{
    private const string Stash = "Personal Stash", Cap = "Cap";

    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Right;

    public void Dispose()
    {
        if (program.Available)
            Inventory.InventoryPad.PanesAsFound(program);

        _book.PutAway();
    }

    /// <summary>A cap in the pad's empty folder, and that folder on the page beside the Items page.</summary>
    private void Shelf()
    {
        _book.OpenBoth();
        program.On(() =>
        {
            program.Main.ShowPageByKey("Inventory_L");
            var cap = ItemForge.PlainBase("cap", 0, _book.Session.ItemEra!).Item;
            cap.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
            long folder = _book.Store.Folders().First(each => each.Name == "New folder").Id;
            Assert.NotNull(_book.Store.Add(cap, folder, out _));
            _book.Vaults.SayChanged();
            program.Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            VaultBook.GoTo(Page, "New folder");
        });
        _book.Until(() => VaultBook.HasCard(Page, Cap), "the cap's card");
    }

    [CorpusFact]
    public void A_card_dragged_onto_a_cell_of_the_Items_page_lands_there_and_comes_back_by_a_drag_onto_the_sheet()
    {
        if (!program.Ready(changes: true))
            return;

        Shelf();
        Item? landed = null;
        int stored = program.On(() => Inventory.InventoryPad.InVault(program));

        // Out of the vault, onto a free cell.
        program.On(() =>
        {
            using var bench = new PageBench(program);
            program.Panes(Stash);
            PageBench.Pump(10);
            var card = VaultBook.Card(Page, Cap);
            var grid = program.Choice(Stash).Grid!;
            var free = ItemMover.FirstFreeCell(card.Item, new ItemMover.Occupancy(new GridSize(grid.Columns, grid.Rows), grid.Items.Select(tile => tile.Item)))!.Value;
            var from = bench.At(card);
            var to = bench.AtCell(Stash, free.X, free.Y);

            bench.Press(from);
            bench.MoveTo(from, to);
            Assert.True(Page.IsCarrying, "the drag did not lift the card");
            Assert.True(program.Main.Inventory!.IsVaultCarrying);
            bench.Release(to);

            Assert.False(Page.IsCarrying);
            landed = program.Tile(Cap, Stash).Item;
            Assert.Equal(free, ((int)landed.X, (int)landed.Y));
        });
        Assert.True(program.On(() => Inventory.InventoryPad.Saved(program)), "the withdrawal was not saved");
        Assert.True(program.Until(() => Inventory.InventoryPad.InVault(program) == stored - 1), "the card's row stayed");

        // Back, from the cell onto the sheet.
        program.On(() =>
        {
            using var bench = new PageBench(program);
            program.Panes(Stash);
            VaultBook.GoTo(Page, "New folder");
            PageBench.Pump(10);
            var inventory = program.Main.Inventory!;
            var from = bench.At(program.TileOf(landed!));
            var to = bench.AtSheet();

            bench.Press(from);
            bench.MoveTo(from, to);
            Assert.True(inventory.Session.IsHolding, "the drag did not lift the cap");
            bench.Release(to);

            Assert.False(inventory.Session.IsHolding);
            Assert.False(program.Holds(Cap, Stash));
        });
        Assert.True(program.Until(() => Inventory.InventoryPad.InVault(program) == stored && VaultBook.HasCard(Page, Cap)), "the cap's card did not come back");
    }
}
