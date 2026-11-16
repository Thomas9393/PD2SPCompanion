using Avalonia.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// The landing mark between the inventory and the vault:
/// carried to the inventory, the cells an item would take are
/// lit - allowed where it would lie there, refused where it would not - the cells it has left go out, and let
/// go of where it was lit it lies on that cell. Carried to the vault there is no cell to light:
/// the sheet is framed as somewhere to let go, and the
/// item is stored into the folder it shows.
/// <para>
/// What the two pages decide, asked as their views ask while the pointer moves and when it lets go (HoverSheet
/// and DropFromInventory on the vault's sheet, HoverArriving and DropFromVault on the inventory's cells) and
/// read back from the cells and from where the item lies. That the lit cells are drawn green and red under a
/// real drag, and that the drag's release finds the cell, are the UI cases' (units/drag-landing-marks.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class LandingMarksTests(Program program)
{
    private const string Stash = "Personal Stash", Ring = "Steel Ring of Skill", Tome = "Tome of Town Portal";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private VP_ViewModel_Vault_L Vault => Main.RightVault!;

    /// <summary>The personal stash in the pane, the vault beside it with its cards listed.</summary>
    private void StashBesideVault()
    {
        program.On(() =>
        {
            program.Panes(Stash);
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Vault.Show();
        });
        Assert.True(program.Until(() => InventoryPad.CardsOf(Vault).Any(card => card.Name == Tome)), "the vault's cards were not listed");
    }

    /// <summary>The cells of a grid that are lit, as "column,row", and whether each is allowed.</summary>
    private static List<(string Cell, bool Allowed)> Lit(GridViewModel grid) =>
        [.. grid.Cells.Where(cell => cell.IsTarget).Select(cell => ($"{cell.Column},{cell.Row}", cell.IsAllowed))];

    [CorpusFact]
    public void Carried_from_the_stash_over_the_vaults_sheet_the_sheet_is_framed_and_it_is_stored_into_the_folder()
    {
        if (!program.Ready(changes: true))
            return;

        StashBesideVault();
        program.On(() =>
        {
            Assert.False(Vault.IsSheetTarget);

            Assert.True(Page.BeginCarry(program.Tile(Ring, Stash)));
            Assert.True(Session.IsHolding);

            // Over the sheet: framed - an item has no cell of it to land on, and the sheet has none to light. Off it: not.
            Vault.HoverSheet(true);
            Assert.True(Vault.IsSheetTarget);
            Vault.HoverSheet(false);
            Assert.False(Vault.IsSheetTarget);

            Vault.HoverSheet(true);
            Vault.DropFromInventory(null);
            Assert.False(Session.IsHolding);
            Assert.False(Vault.IsSheetTarget);
        });

        Assert.True(program.Until(() => InventoryPad.CardsOf(Vault).Any(card => card.Name == Ring)), "the ring is not in the vault");
        program.On(() =>
        {
            var row = InventoryPad.CardsOf(Vault).First(card => card.Name == Ring).Row;
            Assert.Null(row.FolderId);
            Assert.False(program.Holds(Stash, Ring));
        });
    }

    [CorpusFact]
    public void Carried_from_the_vault_over_the_stash_the_cells_are_lit_refused_over_another_item_and_allowed_where_it_has_room()
    {
        if (!program.Ready(changes: true))
            return;

        StashBesideVault();
        program.On(() =>
        {
            var stash = program.Grid(Stash);
            Assert.True(Vault.BeginCarry(InventoryPad.CardsOf(Vault).First(card => card.Name == Tome)));
            Assert.True(Page.IsVaultCarrying);
            Assert.True(Page.IsTakingDrops);

            // A tome is one cell by two. On 0,2 its second cell is the Ancient Armor's: both lit, refused.
            Assert.Null(program.At(Stash, 0, 2));
            Assert.NotNull(program.At(Stash, 0, 3));
            Page.HoverArriving(program.Cell(Stash, 0, 2));
            Assert.Equal([("0,2", false), ("0,3", false)], Lit(stash));

            // On 8,4 and the cell under it nothing lies: lit, allowed - and the cells it has left are lit no more.
            Page.HoverArriving(program.Cell(Stash, 8, 4));
            Assert.Equal([("8,4", true), ("8,5", true)], Lit(stash));

            Page.DropFromVault(program.Cell(Stash, 8, 4));
            Assert.False(Vault.IsCarrying);
        });

        Assert.True(program.Until(() => program.Holds(Stash, Tome)), "the tome is not in the stash");
        program.On(() =>
        {
            var tome = program.Tile(Tome, Stash).Item;
            Assert.Equal((8, 4), ((int)tome.X, (int)tome.Y));

            // Nothing stays lit once it is put down.
            Assert.Empty(Lit(program.Grid(Stash)));
        });
    }
}
