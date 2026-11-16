using Avalonia.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// Between the inventory and the vault's sheet:
/// an item carried from the inventory and let go of on the sheet is stored
/// into the folder it shows - on no cell of it; carried from the sheet onto an inventory cell it
/// is withdrawn there.
/// <para>
/// What the two pages decide: the drop's own methods, asked as the views' release handlers ask them. That the
/// sheet is framed while an item is over it is the landing mark's (Inventory/LandingMarksTests); that a drag of
/// the mouse reaches these is the UI cases' to show (units/vault-sheet-inventory.ps1, units/drag-landing-marks.ps1).
/// Store in vault from the menu of the sheet's item is the right click's (Items/RightClickTests).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultSheetInventoryTests(Program program)
{
    /// <summary>An item of the inventory's upper half that is no tool: one the inventory lock, on by default, lets a drag take.</summary>
    private const string Ring = "The Stone of Jordan";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    [CorpusFact]
    public void Let_go_of_on_the_sheet_an_item_is_stored_into_its_folder_and_carried_back_it_is_withdrawn_to_the_cell()
    {
        if (!program.Ready(changes: true))
            return;


        var right = program.On(() => VaultHands.OnRight(program));
        Assert.True(program.Until(() => VaultHands.CardsOf(right).Count > 0), "the vault's cards were not listed");

        int held = 0, stored = 0;
        program.On(() =>
        {
            VaultHands.Section(program).SelectedPage = VaultHands.Choice(program, "Inventory");
            held = Session.ItemCount;
            stored = VaultHands.Held(program);

            // Into the vault: let go of on the sheet.
            Assert.True(Page.BeginCarry(VaultHands.Tile(program, Ring, "Inventory")));
            Assert.True(Session.IsHolding, Session.Status);
            right.DropFromInventory(null);

            Assert.Equal($"{Ring} stored in vault.", Session.Status);
            Assert.False(Session.IsHolding);
        });

        Assert.True(program.Until(() => VaultHands.Held(program) == stored + 1 && VaultHands.CardOrNone(right, Ring) is not null), "the ring was not stored");
        program.On(() =>
        {
            var card = VaultHands.Card(right, Ring);
            Assert.Null(card.Row.FolderId);
            Assert.Equal(held - 1, Session.ItemCount);
            Assert.Null(VaultHands.TileOrNone(program, Ring, "Inventory"));

            // Out again: from the sheet onto an empty cell of the inventory.
            var cell = VaultHands.FreeCell(program, "Inventory");
            Assert.True(right.BeginCarry(card));
            Assert.True(Page.IsTakingDrops);
            Page.DropFromVault(cell);

            Assert.Equal("1 item moved to character.", right.Notice);
            Assert.False(right.IsCarrying);
            var back = VaultHands.Tile(program, Ring, "Inventory");
            Assert.Equal((cell.Column, cell.Row), (back.CellX, back.CellY));
            Assert.Equal(held, Session.ItemCount);
        });

        Assert.True(program.Until(() => VaultHands.Held(program) == stored), "the ring's row did not leave the vault");
        program.On(() => Assert.Null(VaultHands.CardOrNone(right, Ring)));
    }
}
