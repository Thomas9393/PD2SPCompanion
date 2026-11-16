using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.Tests.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Grail;

/// <summary>
/// The Grail page stores one copy of an entry in the game's Compendium and takes it out again, as the game's Uniques and
/// Sets tabs do:
/// the pad's Death Cleaver, which lies in the shared stash, off it and into the compendium - written by the next save,
/// on page 7, a file made where the mode had none - refused a second time; taken out onto the character's pages; and
/// a store undone puts it back where it lay. The filter shows what is owned.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GrailCompendiumTests(Program program)
{
    private const string Piece = "Death Cleaver";

    private string File => Path.Combine(program.Folder, "saves", Compendium.SoftcoreFile);

    private static bool IsPiece(D2SLib.Model.Save.Item item) => PD2SPCompanion.Core.Items.ItemPresenter.Describe(item).Name == Piece;

    private static int Copies(SharedStashSession stash) => stash.HeldItems.Count(IsPiece);

    /// <summary>
    /// The file gaining the item is written first: the compendium's write fails, so the
    /// shared stash is not written without the piece, and the piece is in neither file at no moment. With a directory in the
    /// file's place, which nothing moves a file over.
    /// </summary>
    [CorpusFact]
    public void A_store_whose_compendium_will_not_write_leaves_the_stash_file_with_the_item()
    {
        if (!program.Ready(changes: true))
            return;

        var stash = program.Get<SharedStashSession>();
        var saves = program.Get<SaveSet>();
        string stashFile = stash.Path;
        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                int onDisk = SharedStash.Read(stashFile).Items.Items.Count(IsPiece);
                Assert.True(onDisk >= 1, "the pad's shared stash holds the piece");
                byte[] written = System.IO.File.ReadAllBytes(stashFile);

                grail.Load();
                var card = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                card.IsFound = true;
                grail.StoreCommand.Execute(card);
                Assert.Equal(onDisk - 1, Copies(stash));

                Directory.CreateDirectory(File);
                try
                {
                    string? trouble = saves.Commit();

                    Assert.NotNull(trouble);
                    Assert.Contains("Compendium not saved", trouble);
                    Assert.Equal(written, System.IO.File.ReadAllBytes(stashFile));
                }
                finally
                {
                    Directory.Delete(File);
                    saves.Revert();
                }
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });
    }

    /// <summary>The other way: an item coming out of the compendium onto the character, whose write fails (PD2SPC_FAIL_SAVE).</summary>
    [CorpusFact]
    public void A_take_out_whose_character_will_not_write_leaves_the_compendium_file_with_the_item()
    {
        if (!program.Ready(changes: true))
            return;

        var saves = program.Get<SaveSet>();
        string failure = Path.Combine(program.Folder, "fail-save");
        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                grail.Load();
                var card = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                card.IsFound = true;
                grail.StoreCommand.Execute(card);
                Assert.True(program.Get<AutoSave>().Flush());
                byte[] written = System.IO.File.ReadAllBytes(File);

                card = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                grail.TakeOutCommand.Execute(card);
                Assert.Contains(program.Main.Session.HeldItems, IsPiece);

                System.IO.File.WriteAllText(failure, "");
                Environment.SetEnvironmentVariable("PD2SPC_FAIL_SAVE", failure);
                try
                {
                    string? trouble = saves.Commit();

                    Assert.NotNull(trouble);
                    Assert.Contains("test failure", trouble);
                    Assert.Equal(written, System.IO.File.ReadAllBytes(File));
                }
                finally
                {
                    Environment.SetEnvironmentVariable("PD2SPC_FAIL_SAVE", null);
                    System.IO.File.Delete(failure);
                    saves.Revert();
                }
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });
    }

    [CorpusFact]
    public void A_copy_held_is_stored_once_written_with_the_save_and_taken_out_again()
    {
        if (!program.Ready(changes: true))
            return;

        var stash = program.Get<SharedStashSession>();
        var compendium = program.Get<CompendiumSession>();
        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                Assert.True(compendium.IsLoaded, $"no compendium open: {compendium.Trouble}");
                Assert.False(System.IO.File.Exists(File), "the pad's mode has no compendium yet");
                int inStash = Copies(stash);
                Assert.True(inStash >= 1, "the pad's shared stash holds the piece");

                grail.Load();
                var card = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                Assert.True(card.CanStore, $"Store not offered: held here {card.HeldOnCharacter}, owned {card.IsOwned}");

                // The three states: undiscovered, the card faded and its picture not darkened on
                // top; discovered and not stored - marked so here, the pad's Season 13 stash keeps no discovery - the
                // card lit and its picture darkened, as the game draws one not stored; stored, whole.
                Assert.False(card.ShowsProperties);
                Assert.False(card.IsShaded);
                card.IsFound = true;
                Assert.True(card.ShowsProperties);
                Assert.True(card.IsShaded);
                grail.StoreCommand.Execute(card);
                Assert.False(card.IsShaded);

                Assert.Equal($"{Piece} stored in compendium.", program.Main.Session.Status);
                Assert.True(card.IsOwned);
                Assert.False(card.CanStore);
                Assert.Equal(inStash - 1, Copies(stash));
                Assert.Single(compendium.Items);

                // One kept of an entry: a second is refused, the first left where it is.
                grail.StoreCommand.Execute(card);
                Assert.Single(compendium.Items);

                // Written with the save: a file made, the item on the compendium's page.
                Assert.True(program.Get<AutoSave>().Flush());
                var written = Compendium.Read(File);
                Assert.Equal(Compendium.Page, Assert.Single(written.Items).Page);

                // The filter's Owned shows it and only it.
                grail.Search = "";
                grail.ShowOnlyCommand.Execute(GrailShow.Owned);
                Assert.Equal(Piece, Assert.Single(grail.Cards).Name);
                grail.ShowOnlyCommand.Execute(GrailShow.All);

                // Taken out: onto the character's pages, the compendium empty again.
                card = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                grail.TakeOutCommand.Execute(card);
                Assert.Equal($"{Piece} taken out of compendium.", program.Main.Session.Status);
                Assert.Empty(compendium.Items);
                Assert.False(card.IsOwned);
                Assert.Contains(program.Main.Session.HeldItems.Concat(stash.HeldItems), IsPiece);
                Assert.True(program.Get<AutoSave>().Flush());
                Assert.Empty(Compendium.Read(File).Items);
            }
            finally
            {
                grail.ShowOnlyCommand.Execute(GrailShow.All);
                GrailHands.Rest(grail);
            }
        });
    }

    /// <summary>
    /// The compendium's key, G, and the menus, for moving items to and from the grail
    /// showcase (G, anywhere, the right-click menu too, each of several that can): an item's menu offers it with its
    /// key, and not for an item the grail has no card for; the key stores each of what is selected that can go and says
    /// how many; the card's menu takes it out, and the card's key puts it in and takes it out again.
    /// </summary>
    [CorpusFact]
    public void An_items_menu_and_its_key_store_each_that_can_go_and_the_cards_menu_and_key_move_it_back()
    {
        if (!program.Ready(changes: true))
            return;

        var stash = program.Get<SharedStashSession>();
        var compendium = program.Get<CompendiumSession>();
        program.On(() =>
        {
            var page = program.Main.Inventory!;
            var grail = GrailHands.OnRight(program);
            try
            {
                int inStash = Copies(stash);
                var cleaver = program.Tiles().First(tile => IsPiece(tile.Item) && stash.HeldItems.Contains(tile.Item));
                // Beside it on its own stash page: the inventory's first, a tome, is locked there and is not picked.
                var other = program.Section().Pages!.First(each => each.Grid?.Items.Contains(cleaver) == true).Grid!.Items
                    .First(tile => tile.TabSlot is null && PD2SPCompanion.Core.Collection.GrailCatalog.EntryOf(tile.Item) is null);

                // The menu's row - no key on it, as on no worded row - and none where the grail has no card.
                Assert.Equal("", page.Menu(cleaver).Row("Store in compendium").Keys);
                Assert.Null(page.Menu(other).Find("Store in compendium"));

                // Both selected, each on a pane in sight, the key: the piece goes, the other stays, and the strip counts them.
                string PageOf(ItemTileViewModel tile) => program.Section().Pages!.First(each => each.Grid?.Items.Contains(tile) == true).Label;
                program.Panes(PageOf(cleaver), PageOf(other) == PageOf(cleaver) ? null : PageOf(other));
                (cleaver, other) = (program.TileOf(cleaver.Item), program.TileOf(other.Item));
                // A click, then a Ctrl + click, as a player picks them - the menus above selected what they were asked of.
                page.ClearSelection();
                page.ClickTileCommand.Execute(cleaver);
                VaultHands.CtrlClick(page, other);
                Assert.Equal(2, page.PickedCount);
                page.StoreInCompendium();
                Assert.Equal("1 of 2 stored in compendium.", program.Main.Session.Status);
                Assert.True(IsPiece(Assert.Single(compendium.Items)));
                Assert.Equal(inStash - 1, Copies(stash));
                Assert.Contains(other.Item, stash.HeldItems);

                // The card's menu takes it out, onto the character's pages.
                var card = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                var menu = grail.MenuFor(card);
                Assert.Equal("", menu.Row("Take out of compendium").Keys);
                menu.Pick("Take out of compendium");
                Assert.Empty(compendium.Items);

                // The card's key: in, and out again.
                grail.Transfer(card);
                Assert.Equal($"{Piece} stored in compendium.", program.Main.Session.Status);
                Assert.True(card.IsOwned);
                grail.Transfer(card);
                Assert.Empty(compendium.Items);
                Assert.Contains(program.Main.Session.HeldItems, IsPiece);
            }
            finally
            {
                page.ClearSelection();
                GrailHands.Rest(grail);
            }
        });
    }

    /// <summary>
    /// The tools' button, and drag and drop:
    /// the button says what G does, with its key; an item carried and let go of over the Grail
    /// page is stored; a card's item dragged off it - carried as from no socket row - and let go of on a cell of the
    /// Items page comes out there.
    /// </summary>
    [CorpusFact]
    public void The_button_stores_a_carried_item_let_go_over_the_grail_is_stored_and_a_cards_item_dragged_to_a_cell_lands_there()
    {
        if (!program.Ready(changes: true))
            return;

        var stash = program.Get<SharedStashSession>();
        var compendium = program.Get<CompendiumSession>();
        program.On(() =>
        {
            var page = program.Main.Inventory!;
            var grail = GrailHands.OnRight(program);
            try
            {
                var cleaver = program.Tiles().First(tile => IsPiece(tile.Item) && stash.HeldItems.Contains(tile.Item));
                string home = program.Section().Pages!.First(each => each.Grid?.Items.Contains(cleaver) == true).Label;
                var (x, y) = (cleaver.Item.X, cleaver.Item.Y);

                // The button: lit for the piece selected, its tooltip with the key; dimmed with nothing selected.
                program.Main.Session.SelectedItem = cleaver;
                Assert.True(page.CanStoreInCompendium);
                Assert.Equal("Store selected item in compendium (G)", page.StoreInCompendiumHint);
                page.ClearSelection();
                Assert.False(page.CanStoreInCompendium);

                // Carried, let go of over the Grail page: stored, nothing held.
                program.Main.Session.PickUp(program.TileOf(cleaver.Item), fromShared: true);
                Assert.True(program.Main.Session.IsHolding);
                Assert.True(page.DropOnCompendium());
                Assert.False(program.Main.Session.IsHolding);
                Assert.True(IsPiece(Assert.Single(compendium.Items)));

                // Dragged off its card onto the cell it lay on before, free now: out, there - not where the rule lands it.
                var card = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                Assert.True(grail.BeginCarry(card));
                Assert.True(PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared is { IsCarrying: true, FromRow: false });
                program.Panes(home);
                var cell = program.Choice(home).Grid!.Cells.First(each => each.Column == x && each.Row == y);
                grail.DropCarried(takeOut => page.LandFromRow(cell, takeOut));
                Assert.False(PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.IsCarrying);
                Assert.Empty(compendium.Items);
                Assert.Contains(program.Choice(home).Grid!.Items, tile => IsPiece(tile.Item) && tile.Item.X == x && tile.Item.Y == y);
            }
            finally
            {
                if (grail.Carried is not null)
                    grail.EndCarry();
                page.ClearSelection();
                GrailHands.Rest(grail);
            }
        });
    }

    [CorpusFact]
    public void A_store_undone_puts_the_item_back_where_it_lay_and_the_compendium_as_it_was()
    {
        if (!program.Ready(changes: true))
            return;

        var stash = program.Get<SharedStashSession>();
        var compendium = program.Get<CompendiumSession>();
        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                int inStash = Copies(stash);
                grail.StoreCommand.Execute(GrailHands.Card(grail, GrailViewModel.Uniques, Piece));
                Assert.True(program.Get<AutoSave>().Flush());
                Assert.Single(compendium.Items);

                Assert.True(program.Main.Home.UndoCommand.CanExecute(null));
                program.Main.Home.UndoCommand.Execute(null);

                Assert.Empty(compendium.Items);
                Assert.Equal(inStash, Copies(stash));
                Assert.False(GrailHands.Card(grail, GrailViewModel.Uniques, Piece).IsOwned);
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });
    }
}
