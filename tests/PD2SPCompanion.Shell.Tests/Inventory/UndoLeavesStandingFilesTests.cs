using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// An undo puts back the files its step changed, and leaves alone one that stands as the earlier state already
/// has it: a file put back is read again
/// and every item of it made afresh, and an item edited on the character took the shared stash's nine pages with
/// it. A file with a change of its own waiting does not stand, and is put back as it always was.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class UndoLeavesStandingFilesTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private SharedStashSession Stash => program.Get<SharedStashSession>();
    private BasicEditor Basic => program.Get<VP_ViewModel_Inv_R>().Basic;
    private ItemTileViewModel Amulet => Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Neck && !slot.IsSwap).Item!;

    [CorpusFact]
    public void An_item_edit_undone_puts_the_character_back_and_leaves_the_shared_stash_as_it_is()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Casual");
        program.On(() =>
        {
            Assert.True(Stash.IsLoaded, "no shared stash to leave alone");
            var pages = Stash.Pages.SelectMany(page => page.Items).ToList();
            Assert.NotEmpty(pages);

            // As opened, both stand as their files have them.
            Assert.True(Session.StandsAs(Session.SavedBytes));
            Assert.True(Stash.StandsAs(Stash.SavedBytes!));
            byte[] before = Session.SavedBytes;

            Session.SelectedItem = Amulet;
            Basic.PersonalName = "Zedd";
            Assert.True(Amulet.Item.IsPersonalized);

            // A change waiting: the character is not as its file.
            Assert.False(Session.StandsAs(Session.SavedBytes));
            Assert.True(program.Saved(), "the name was not saved");
            Assert.False(Session.StandsAs(before));

            var history = program.Get<SessionHistory>();
            Assert.True(history.CanUndo);
            Assert.Null(history.Undo());

            // The character is back, its items made afresh; the stash's are the very ones it had.
            Assert.False(Amulet.Item.IsPersonalized);
            Assert.True(Session.StandsAs(before));
            var after = Stash.Pages.SelectMany(page => page.Items).ToList();
            Assert.Equal(pages.Count, after.Count);
            Assert.True(pages.Zip(after).All(pair => ReferenceEquals(pair.First, pair.Second)), "the shared stash was made afresh");

            // And forward again the same way.
            Assert.Null(history.Redo());
            Assert.True(Amulet.Item.IsPersonalized);
            after = [.. Stash.Pages.SelectMany(page => page.Items)];
            Assert.True(pages.Zip(after).All(pair => ReferenceEquals(pair.First, pair.Second)), "the shared stash was made afresh by the redo");

            Assert.Null(history.Undo());
            Assert.False(Amulet.Item.IsPersonalized);
        });
    }

    /// <summary>
    /// Undo by changed items: the file put back keeps every item the step left as it was -
    /// the same item, the same tile - and only the one it changed is made afresh. Nor is the vault, untouched, said
    /// to have changed.
    /// </summary>
    [CorpusFact]
    public void An_item_edit_undone_makes_only_that_item_afresh_and_leaves_the_vault_unsaid()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Casual");
        program.On(() =>
        {
            Session.SelectedItem = Amulet;
            var amulet = Amulet.Item;
            var others = Tiles().Where(tile => !ReferenceEquals(tile.Item, amulet)).ToList();
            Assert.NotEmpty(others);

            Basic.PersonalName = "Zedd";
            Assert.True(program.Saved(), "the name was not saved");

            int said = 0;
            var vaults = program.Get<VaultService>();
            void Count() => said++;
            vaults.Changed += Count;
            try
            {
                var history = program.Get<SessionHistory>();
                Assert.Null(history.Undo());
                Assert.False(Amulet.Item.IsPersonalized);

                // The edited one afresh; every other the very item and tile it was, and the selection still on the amulet.
                Assert.False(ReferenceEquals(Amulet.Item, amulet), "the item undone was kept");
                var now = Tiles().ToList();
                Assert.All(others, tile => Assert.Contains(now, each => ReferenceEquals(each, tile)));
                Assert.Same(Amulet, Session.SelectedItem);
                Assert.Equal(0, said);
            }
            finally
            {
                vaults.Changed -= Count;
            }
        });

        IEnumerable<ItemTileViewModel> Tiles() =>
            Session.Equipped.Select(slot => slot.Item).OfType<ItemTileViewModel>().Concat(Session.AllGrids.SelectMany(grid => grid.Items));
    }
}
