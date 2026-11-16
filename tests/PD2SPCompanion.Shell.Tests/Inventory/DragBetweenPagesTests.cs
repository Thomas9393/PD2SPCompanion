using Avalonia.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Carrying items off the inventory page (drags between pages only between the inventory
/// and the vault, none to the Edit page, the grail or trade): what is carried and let go of where nothing
/// takes it goes back where it was, and nothing is written; two picked, carried and dropped on the vault
/// beside the inventory, are both stored.
/// <para>
/// What the pages decide, asked as the views ask at the release (CancelHold, the window's release that nothing
/// took; the vault's DropFromInventory) and read back from the stash, the vault and the character's file.
/// That a real drag's release over the Edit page and over the vault's sheet reaches those is the UI cases'
/// (units/drag-between-pages.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class DragBetweenPagesTests(Program program)
{
    private const string Stash = "Personal Stash", First = "Steel Ring of Skill", Second = "Felicitous Ring of Regrowth";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private VP_ViewModel_Vault_L Vault => Main.RightVault!;

    [CorpusFact]
    public void Let_go_of_where_nothing_takes_it_what_is_carried_goes_back_and_nothing_is_written()
    {
        if (!program.Ready())
            return;

        try
        {
            var written = File.GetLastWriteTimeUtc(program.SaveOf(Program.Open));
            program.On(() =>
            {
                program.Panes(Stash);
                string cells = program.Cells(Stash);
                int held = Session.ItemCount, steps = program.Get<SessionHistory>().Rows.Count;

                Assert.True(Page.BeginCarry(program.Tile(First, Stash)));
                Assert.True(Page.IsDragCarrying);
                Assert.True(Session.IsHolding);

                // The window's release, over a page that takes no items.
                Page.CancelHold();
                Assert.False(Session.IsHolding);
                Assert.False(Page.IsDragCarrying);
                Assert.Equal("Placed back.", Session.Status);
                Assert.Equal(cells, program.Cells(Stash));
                Assert.Equal(held, Session.ItemCount);

                Assert.False(program.Get<SaveSet>().HasChanges);
                Assert.True(program.Saved());
                Assert.Equal(steps, program.Get<SessionHistory>().Rows.Count);
            });
            Assert.Equal(written, File.GetLastWriteTimeUtc(program.SaveOf(Program.Open)));
        }
        finally
        {
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void Two_picked_and_dropped_on_the_vault_beside_the_inventory_are_both_stored()
    {
        if (!program.Ready(changes: true))
            return;

        int stored = 0, held = 0;
        program.On(() =>
        {
            program.Panes(Stash);
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Vault.Show();
            stored = program.InVault();
            held = Session.ItemCount;

            // One selected, then the second with Ctrl: the Store button counts them.
            Page.ClickTileCommand.Execute(program.Tile(First, Stash));
            Assert.Equal("Store selected item in vault", Page.StoreHint);
            Assert.True(Page.BeginDragPick(keepSelected: true));
            Page.SetPicked(program.Tile(Second, Stash), true);
            Page.EndPick();
            Assert.Equal("Store 2 selected items in vault", Page.StoreHint);

            // A drag begun on one of them carries both.
            Assert.True(Page.BeginCarry(program.Tile(First, Stash)));
            Assert.Single(Session.HeldGroup);

            Vault.DropFromInventory(null);
            Assert.False(Session.IsHolding);
        });

        Assert.True(program.Until(() => program.InVault() == stored + 2 && Session.ItemCount == held - 2), "the two rings were not stored");
        program.On(() =>
        {
            Assert.False(program.Holds(Stash, First));
            Assert.False(program.Holds(Stash, Second));
        });
        Assert.True(program.Until(() => InventoryPad.CardsOf(Vault).Count(card => card.Name is First or Second) == 2), "the rings' cards were not listed");
    }
}
