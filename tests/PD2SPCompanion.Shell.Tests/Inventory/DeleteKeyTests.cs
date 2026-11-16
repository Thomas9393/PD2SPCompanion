using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// The Delete key is the bin, in the personal and shared stash and
/// everywhere else: asked once it asks, asked again it deletes and says
/// so - an item in the personal stash, one in the shared stash (whose save had put "Saved 130 items" over
/// "Deleted ..."), two picked at once, and a vault card on the right page.
/// <para>
/// What the pages decide, asked as the key asks them (the bin's own command, where the page says there is
/// something to delete) and read back from the containers, the vault and the strips. That the key itself
/// reaches the inventory's bin, and the vault's while the vault was pressed in last, are the UI cases'
/// (units/delete-key.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class DeleteKeyTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private VP_ViewModel_Vault_L Vault => Main.RightVault!;

    /// <summary>The Delete key, as the inventory's view answers it.</summary>
    private void PressDelete()
    {
        Assert.True(Page.CanDelete);
        Page.DeleteItemCommand.Execute(null);
    }

    /// <summary>A tile selected, Delete twice: asked, then gone, said on the strip.</summary>
    private void DeleteTheFirstOf(string container)
    {
        program.Panes(container);
        var tile = program.Grid(container).Items.OrderBy(each => each.Item.Y).ThenBy(each => each.Item.X).First();
        string name = tile.Name;
        int count = program.Count(container, name);

        Page.ClickTileCommand.Execute(tile);
        PressDelete();
        Assert.Equal($"Delete {name}? Click red tick again to confirm.", Session.Status);
        Assert.True(Page.DeleteArmed);
        Assert.StartsWith("Confirm deletion", Page.DeleteHint);
        Assert.Equal(count, program.Count(container, name));

        PressDelete();
        Assert.Equal($"Deleted {name}.", Session.Status);
        Assert.False(Page.DeleteArmed);
        Assert.Equal(count - 1, program.Count(container, name));
        Assert.Null(Session.SelectedItem);

        // The save that follows says nothing over it.
        Assert.True(program.Saved(), "the delete was not saved");
        Assert.Equal($"Deleted {name}.", Session.Messages.Text);
    }

    [CorpusFact]
    public void Delete_twice_deletes_an_item_of_the_personal_stash()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() => DeleteTheFirstOf("Personal Stash"));
    }

    [CorpusFact]
    public void Delete_twice_deletes_an_item_of_the_shared_stash_and_the_save_leaves_what_the_strip_said()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() => DeleteTheFirstOf("Shared Page 1"));
    }

    [CorpusFact]
    public void With_two_picked_the_first_Delete_asks_for_both_and_the_second_deletes_both()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            string first = "Silver Jewel of Malice", second = "Shadow Eye";
            int held = program.Grid("Personal Stash").Items.Count;

            // A click, then Ctrl and a click.
            Page.ClickTileCommand.Execute(program.Tile(first, "Personal Stash"));
            Assert.True(Page.BeginDragPick(keepSelected: true));
            Page.SetPicked(program.Tile(second, "Personal Stash"), true);
            Page.EndPick();
            Assert.Equal(2, Page.PickedCount);

            PressDelete();
            Assert.Equal("Delete 2 selected items? Click red tick again to confirm.", Session.Status);
            Assert.Equal(held, program.Grid("Personal Stash").Items.Count);

            PressDelete();
            Assert.Equal("Deleted 2 items.", Session.Status);
            Assert.False(program.Holds("Personal Stash", first));
            Assert.False(program.Holds("Personal Stash", second));
            Assert.Equal(held - 2, program.Grid("Personal Stash").Items.Count);
            Assert.False(Page.IsPicking);
        });
    }

    [CorpusFact]
    public void Delete_twice_deletes_a_vault_card_on_the_right_page()
    {
        if (!program.Ready(changes: true))
            return;

        const string charm = "Small Charm of Fortune";
        long? was = program.On(() => Vault.Here);
        try
        {
            program.On(() =>
            {
                Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
                Vault.Show();
                Vault.GoTo(program.Folder("Small"));
            });
            Assert.True(program.Until(() => InventoryPad.CardsOf(Vault).Any(card => card.Name == charm)), "Small's cards were not listed");

            program.On(() =>
            {
                int stored = program.InVault();
                Vault.SelectOnly(InventoryPad.CardsOf(Vault).First(card => card.Name == charm));

                // The key, as the vault's view answers it.
                Assert.True(Vault.CanDeleteSelection);
                Vault.DeleteCommand.Execute(null);
                Assert.Equal($"Delete {charm}? Click red tick again to confirm.", Session.RightMessages.Text);
                Assert.True(Vault.DeleteArmed);
                Assert.Equal(stored, program.InVault());

                Assert.True(Vault.CanDeleteSelection);
                Vault.DeleteCommand.Execute(null);
                Assert.Equal($"Deleted {charm}.", Session.RightMessages.Text);
                Assert.Equal(stored - 1, program.InVault());
            });
            Assert.True(program.Until(() => InventoryPad.CardsOf(Vault).All(card => card.Name != charm)), "the card is still listed");
        }
        finally
        {
            program.On(() => Vault.GoTo(was));
        }
    }
}
