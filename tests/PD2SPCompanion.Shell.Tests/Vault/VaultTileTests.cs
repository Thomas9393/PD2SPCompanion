using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A vault item is its tile, one model between the vault and the
/// inventory. A card stood between the two, with
/// the row and a second copy of the tile's marks: the tile holds the row now, and what the page marks - ticked,
/// selected, leaving - is marked on the very thing that is drawn.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultTileTests(Program program)
{
    private const string Shown = "Shared Page 5", Tome = "Tome of Town Portal";

    private MainViewModel Main => program.Main;
    private VP_ViewModel_Vault_L Vault => Main.RightVault!;

    private ItemTileViewModel TheTome()
    {
        program.On(() =>
        {
            program.Panes(Shown);
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Vault.Show();
        });
        Assert.True(program.Until(() => InventoryPad.CardsOf(Vault).Any(card => card.Name == Tome)), "the vault's cards were not listed");
        return program.On(() => InventoryPad.CardsOf(Vault).First(card => card.Name == Tome));
    }

    [CorpusFact]
    public void A_vault_items_tile_holds_its_row_and_a_characters_tile_has_none()
    {
        if (!program.Ready())
            return;

        var tome = TheTome();
        program.On(() =>
        {
            try
            {
                Assert.True(tome.IsVaults);
                Assert.Equal(tome.Row.Key, tome.Key);
                Assert.Equal(tome.Row.OriginId, tome.Item.Id);
                Assert.Same(tome, Vault.CardOf(tome));
                Assert.Same(tome, program.Get<VaultService>().Store!.RowsOf([tome.Key]).Select(row => Vault.Cards.First(card => card.Key == row.Key)).Single());

                // Ticked and selected on the page: the tile drawn is the one marked.
                Vault.SetPicked(tome, true);
                Assert.True(tome.IsPicked);
                Assert.Equal(true, Vault.IsPicked(tome));

                // An item a character holds is no vault item, and is none of this page's.
                var held = Main.Session.AllGrids.SelectMany(grid => grid.Items).First();
                Assert.False(held.IsVaults);
                Assert.Null(held.Added);
                Assert.Null(Vault.CardOf(held));
                Assert.Throws<InvalidOperationException>(() => held.Row);
            }
            finally
            {
                VaultHands.Rest(program);
            }
        });
    }

    [CorpusFact]
    public void An_item_leaving_the_vault_wears_its_arrow_until_the_save()
    {
        if (!program.Ready(changes: true))
            return;

        var tome = TheTome();
        int stored = program.On(() => program.InVault());
        program.On(() =>
        {
            Assert.Empty(program.Grid(Shown).Items);
            Assert.False(tome.IsLeaving);

            Vault.SelectOnly(tome);
            Vault.WithdrawCommand.Execute(null);

            // Out with the next save, and marked so where it still lies: the arrow is the tile's, which is what is drawn.
            Assert.Equal(stored, program.InVault());
            Assert.True(InventoryPad.CardsOf(Vault).First(card => card.Key == tome.Key).IsLeaving, "a leaving item wears no arrow");
        });
        Assert.True(program.Until(() => program.InVault() == stored - 1), "the tome's row did not leave the vault");
    }
}
