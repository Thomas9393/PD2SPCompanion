using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A folder's own folders as tiles of its sheet, each a fake item in the grid
/// handled as in Windows Explorer:
/// they stand first on the sheet whatever its order, four cells by four
/// in rows across it, and the items begin under them; a click selects one and it is opened by its own act; they are picked with the
/// items, carried into another folder, cut and pasted, and deleted as a folder is; the arrows step from them to
/// the items; a search leaves the ones that hold a find and steps the rest back. And the tree, all of it unfolded
/// or folded at once.
/// <para>
/// What the page decides, asked as the view asks. That a tile is drawn over its cells, that a double click opens
/// it and that a drag carries it are the UI cases' to show (units/vault-folder-tiles.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultFolderTilesTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    private ConfirmService Confirm => program.Get<ConfirmService>();

    private const string Tome = "Tome of Town Portal";

    public void Dispose()
    {
        program.On(() =>
        {
            if (Confirm.IsAsking)
                Confirm.RejectCommand.Execute(null);
            ItemClipboard.Shared.Clear();
            Page.SetSheetColumns(20);
        });
        _book.PutAway();
    }

    private FolderTile Tile(string name) => Page.SheetFolders.First(tile => tile.Name == name);

    /// <summary>
    /// Where a folder's first item lies, by column and row of the whole sheet: beside its tiles' last row where that
    /// row leaves room, else under them.
    /// </summary>
    private (int X, int Row) FirstItemAt(int columns)
    {
        int tiles = Page.SheetFolders.Count, per = columns / FolderTile.Wide;
        int rows = (tiles + per - 1) / per;
        return tiles % per != 0 ? (tiles % per * FolderTile.Wide, (rows - 1) * FolderTile.High) : (0, rows * FolderTile.High);
    }


    private List<string> Tiles => [.. Page.SheetFolders.Select(tile => tile.Name)];

    /// <summary>The folder a folder lies in, by their names, as the vault holds it; "" at the top.</summary>
    private string ParentOf(string name)
    {
        var folders = _book.Store.Folders();
        var folder = folders.First(each => each.Name == name);
        return folder.ParentId is { } id ? folders.First(each => each.Id == id).Name : "";
    }

    [CorpusFact]
    public void A_folders_folders_stand_first_on_its_sheet_four_cells_by_four_and_its_items_begin_beside_the_last_row()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            // The vault's own folders, in the tree's order, side by side from the sheet's first cell.
            var top = Page.Places.Where(place => place.Id is not null && place.Parent is null).Select(place => place.Name).ToList();
            Assert.True(top.Count >= 2, "the pad's vault has folders at its top");
            Assert.Equal(top, Tiles);
            Assert.All(Page.SheetFolders.Select((tile, n) => (tile, n)), each =>
            {
                Assert.Equal((each.n * FolderTile.Wide, 0), (each.tile.X, each.tile.Y));
                Assert.Same(Page.Places.First(place => place.Id == each.tile.Place.Id), each.tile.Place);
            });

            // The items beside the last row of them, where it leaves room, else under them.
            var tome = VaultBook.Card(Page, Tome);
            Assert.Equal(FirstItemAt(Page.SheetColumns), (tome.CellX, Page.RowOf(tome.Key)));

            // A row of them fills the sheet to its end, at every width a sheet has: it is a whole number of tiles wide.
            foreach (int asked in new[] { 12, 16, 20, 24, 28, 30, 7 })
            {
                Page.SetSheetColumns(asked);
                Assert.Equal(0, Page.SheetColumns % FolderTile.Wide);
                Assert.True(Page.SheetColumns >= 4 && Page.SheetColumns <= Math.Max(4, asked));
            }

            // The narrowest sheet, four columns: a tile a row, each under the one before, the items under the last.
            Page.SetSheetColumns(4);
            Assert.All(Page.SheetFolders.Select((tile, n) => (tile, n)), each =>
                Assert.Equal((0, each.n * FolderTile.High), (each.tile.X, each.tile.Y)));
            // By its row of the whole sheet: five tiles are twenty rows, past the first stretch of fifteen.
            Assert.Equal(top.Count * FolderTile.High, Page.RowOf(VaultBook.Card(Page, Tome).Key));

            // Twelve columns: three tiles a row, the rest in rows under them, the items under those.
            Page.SetSheetColumns(12);
            const int per = 3;
            Assert.All(Page.SheetFolders.Select((tile, n) => (tile, n)), each =>
                Assert.Equal((each.n % per * FolderTile.Wide, each.n / per * FolderTile.High), (each.tile.X, each.tile.Y)));
            Assert.Equal(FirstItemAt(12), (VaultBook.Card(Page, Tome).CellX, Page.RowOf(VaultBook.Card(Page, Tome).Key)));
            Page.SetSheetColumns(20);

            // Whatever the order (folders are always at the top): the
            // Sort menu orders the items, and the folders stand where they stood.
            foreach (string by in PD2SPCompanion.Core.Collection.Vault.Orders)
            {
                (Page.Order.By, Page.Order.Descending) = (by, true);
                Assert.Equal(top, Tiles);
                Assert.Equal((0, 0), (Page.SheetFolders[0].X, Page.SheetFolders[0].Y));
                Assert.Equal(FirstItemAt(Page.SheetColumns), (VaultBook.Card(Page, Tome).CellX, Page.RowOf(VaultBook.Card(Page, Tome).Key)));
            }

            // What a tile says under its name is its row of the tree's line, a part to a line.
            Assert.Equal("2 folders", Tile("Charms").CountLines);
            Assert.Equal("1 item" + Environment.NewLine + "1 folder", Tile("Jewels").CountLines);

            // A folder that holds none has none, and its items begin at the sheet's first cell.
            VaultBook.GoTo(Page, "Small");
            Assert.Empty(Page.SheetFolders);
            Assert.Equal(0, VaultBook.CardsOf(Page).Min(card => card.CellY));
        });
    }

    [CorpusFact]
    public void A_click_selects_a_tile_and_its_own_act_opens_it_and_it_is_picked_with_the_items()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            var charms = Tile("Charms");
            Page.ClickFolderTileCommand.Execute(charms);

            // Selected, as an item is: ringed, the folder shown still the vault's top - and what the bin, Move to
            // folder and Cut take. Withdraw is an item's.
            Assert.Same(charms, Page.SelectedFolder);
            Assert.True(charms.IsSelected);
            Assert.Equal("Vault", VaultBook.Shown(Page));
            Assert.Null(Page.Selected);
            Assert.True(Page.CanDeleteSelection);
            Assert.True(Page.CanMove);
            Assert.True(Page.CanCut);
            Assert.False(Page.CanCopy);
            Assert.False(Page.HasActing);

            // An item clicked: that one alone.
            Page.ClickTileCommand.Execute(VaultBook.Card(Page, Tome));
            Assert.Null(Page.SelectedFolder);
            Assert.False(charms.IsSelected);

            // Ctrl + click on a tile: picked with the item.
            VaultHands.CtrlClick(Page, charms);
            Assert.True(Page.IsPicking);
            Assert.Equal(2, Page.PickedCount);
            Assert.True(charms.IsPicked);
            Assert.Contains("2 selected", Page.PagesText);

            // All: every folder and every item of the folder shown.
            Page.SelectAll();
            Assert.Equal(Page.SheetFolders.Count + VaultBook.CardsOf(Page).Count, Page.PickedCount);
            Assert.All(Page.SheetFolders, tile => Assert.True(tile.IsPicked));

            Page.ClearSelection();
            Assert.All(Page.SheetFolders, tile => Assert.False(tile.IsMarked));

            // Its own act - a double click, Enter: gone into.
            Page.ClickFolderTileCommand.Execute(Tile("Charms"));
            Page.OpenFolderTile(Page.SelectedFolder!);
            Assert.Equal(@"Vault\Charms", VaultBook.Shown(Page));
            Assert.Equal(["Grand", "Small"], Tiles);
            Assert.Null(Page.SelectedFolder);
        });
    }

    [CorpusFact]
    public void The_arrows_step_from_the_tiles_to_the_items_under_them()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            // Nothing selected: the sheet's first, which is a folder.
            Page.StepSelection(1, 0);
            Assert.Same(Page.SheetFolders[0], Page.SelectedFolder);

            Page.StepSelection(1, 0);
            Assert.Same(Page.SheetFolders[1], Page.SelectedFolder);

            Page.StepSelection(-1, 0);
            Assert.Same(Page.SheetFolders[0], Page.SelectedFolder);

            // Down from the first tile: the item under it.
            Page.StepSelection(0, 1);
            Assert.Null(Page.SelectedFolder);
            Assert.Equal(Tome, Page.Selected?.Name);

            // And up again: a folder.
            Page.StepSelection(0, -1);
            Assert.NotNull(Page.SelectedFolder);
        });
    }

    [CorpusFact]
    public void A_tile_carried_onto_another_goes_into_it_and_cut_it_is_moved_by_the_paste()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            Assert.Equal("", ParentOf("Jewels"));

            // Carried: the folder alone, drawn as a folder under the pointer.
            Assert.True(Page.BeginCarry(Tile("Jewels")));
            Assert.True(Page.IsCarrying);
            Assert.Empty(Page.GhostParts);
            Assert.Equal(["Jewels"], Page.GhostFolders.Select(ghost => ghost.Name));

            // Over itself it lights nothing; let go of on itself, nothing moves and nothing is said.
            Page.HoverFolder(Tile("Jewels").Place);
            Assert.False(Tile("Jewels").Place.IsDropTarget);
            Page.DropCarriedOnFolder(Tile("Jewels").Place);
            Assert.False(Page.IsCarrying);
            Assert.Equal("", ParentOf("Jewels"));
            Assert.Equal("", Page.Notice);

            // Let go of on another folder's tile: into it.
            Assert.True(Page.BeginCarry(Tile("Jewels")));
            Page.HoverFolder(Tile("Charms").Place);
            Assert.True(Tile("Charms").Place.IsDropTarget);
            Page.DropCarriedOnFolder(Tile("Charms").Place);
            Assert.False(Page.IsCarrying);
            Assert.Equal("Charms", ParentOf("Jewels"));
            Assert.Equal("Moved to Charms.", Page.Notice);
            Assert.DoesNotContain("Jewels", Tiles);

            // Cut there, with an item beside it, and pasted at the vault's top: both moved, in the one paste.
            VaultBook.GoTo(Page, "Charms");
            Page.ClickFolderTileCommand.Execute(Tile("Jewels"));
            Page.CutCommand.Execute(null);
            Assert.True(ItemClipboard.Shared.IsCut(Tile("Jewels").Place.Id));
            Assert.True(Tile("Jewels").Place.IsCut);
            Assert.Equal("Folder cut", ItemClipboard.Shared.Says);
            Assert.Equal("Jewels cut. Paste to move it.", Page.Notice);

            Page.GoTo(null);
            Page.PasteCommand.Execute(null);
            Assert.Equal("", ParentOf("Jewels"));
            Assert.True(ItemClipboard.Shared.IsEmpty);
            Assert.Contains("Jewels", Tiles);

            // Pasted where it is already: it stays, under its own name.
            Page.ClickFolderTileCommand.Execute(Tile("Jewels"));
            Page.CutCommand.Execute(null);
            Page.PasteCommand.Execute(null);
            Assert.Equal("Already in this folder.", Page.Notice);
            Assert.Single(_book.Store.Folders(), folder => folder.Name.StartsWith("Jewels", StringComparison.Ordinal));
        });
    }

    [CorpusFact]
    public void A_folder_is_named_on_its_tile_by_F2_and_by_its_menu_and_Esc_gives_the_name_up()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            Page.ClickFolderTileCommand.Execute(Tile("Jewels"));
            Page.RenameSelectedFolder();

            // Named where it stands: a box on the tile, the page going nowhere.
            var jewels = Tile("Jewels");
            Assert.True(jewels.IsRenaming);
            Assert.Equal("Jewels", jewels.Rename!.Name);
            Assert.Equal("Vault", VaultBook.Shown(Page));

            // Esc: the name as it was, the box gone.
            Page.CancelRename(jewels.Rename);
            Assert.False(jewels.IsRenaming);
            Assert.Contains(_book.Store.Folders(), folder => folder.Name == "Jewels");

            // Its menu's Rename: the same box, on the tile.
            Page.MenuFor(jewels.Place.Id).Pick("Rename");
            Assert.True(Tile("Jewels").IsRenaming);
            Assert.Equal("Vault", VaultBook.Shown(Page));

            // Enter, or the box left: kept - the tile under its new name, and still the one selected.
            var box = Tile("Jewels").Rename!;
            box.Name = "Gems";
            Page.CommitRename(box);
            Assert.DoesNotContain(_book.Store.Folders(), folder => folder.Name == "Jewels");
            Assert.Contains("Gems", Tiles);
            Assert.False(Tile("Gems").IsRenaming);
            Assert.Equal("Gems", Page.SelectedFolder?.Name);
            Assert.Equal("Vault", VaultBook.Shown(Page));

            // A folder with no tile on the sheet shown - the one shown itself, by its row of the tree or its step
            // of the path: the folder it lies in is gone to, and it is named on its tile there.
            VaultBook.GoTo(Page, "Gems");
            Page.MenuFor(Page.Here).Pick("Rename");
            Assert.Equal("Vault", VaultBook.Shown(Page));
            Assert.True(Page.SelectedFolder is { Name: "Gems", IsRenaming: true });
            Page.CancelRename(Page.SelectedFolder!.Rename!);

            // The tools' Rename folder is the selected folder's too - and dimmed with none selected.
            Assert.True(Page.CanRenameFolder);
            Page.RenameFolderCommand.Execute(null);
            Assert.True(Tile("Gems").IsRenaming);
            Page.CancelRename(Tile("Gems").Rename!);
            Page.ClearSelection();
            Assert.False(Page.CanRenameFolder);
        });
    }

    [CorpusFact]
    public void The_bin_deletes_a_tiles_folder_as_a_folder_is_deleted_and_asks_once_for_several()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            int folders = _book.Store.Folders().Count;

            // One that holds something: the folder's own three-way question.
            Page.ClickFolderTileCommand.Execute(Tile("Jewels"));
            Assert.Contains("Delete folder", Page.DeleteHint);
            Page.DeleteCommand.Execute(null);
            Assert.True(Confirm.IsAsking);
            Assert.Equal("Delete folder", Confirm.Title);
            Confirm.RejectCommand.Execute(null);
            Assert.Equal(folders, _book.Store.Folders().Count);

            // A folder and an item together - the item Ctrl + clicked, the folder selected staying so: one question
            // for both, and the folder alone goes with what was picked - what it holds moving to the top.
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Tome));
            Assert.Equal(2, Page.PickedCount);
            int held = _book.Store.Count();
            Page.DeleteCommand.Execute(null);
            Assert.True(Confirm.IsAsking);
            Assert.Equal("Delete selected", Confirm.Title);
            Assert.Equal("DELETE FOLDERS", Confirm.ConfirmLabel);
            Confirm.AcceptCommand.Execute(null);

            Assert.DoesNotContain(_book.Store.Folders(), folder => folder.Name == "Jewels");
            Assert.Equal(held - 1, _book.Store.Count());
            Assert.False(VaultBook.HasCard(Page, Tome));
            Assert.DoesNotContain("Jewels", Tiles);
            Assert.False(Page.IsPicking);
        });
    }

    [CorpusFact]
    public void The_whole_tree_is_unfolded_and_folded_at_once_and_a_folders_menu_does_it_for_what_lies_under_it()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            try
            {
                Assert.True(Page.CanFoldTree);
                Assert.False(VaultBook.Row(Page, "Small").IsShown);

                Page.UnfoldTreeCommand.Execute(null);
                Assert.All(Page.Places.Where(place => place.HoldsFolders), place => Assert.True(place.IsOpen));
                Assert.All(Page.Places, place => Assert.True(place.IsShown));

                Page.FoldTreeCommand.Execute(null);
                Assert.All(Page.Places.Where(place => place.HoldsFolders), place => Assert.False(place.IsOpen));
                Assert.False(VaultBook.Row(Page, "Small").IsShown);

                // A folder's menu: what lies under it, itself with it - and no other.
                var menu = Page.MenuFor(VaultBook.Row(Page, "Charms").Id);
                Assert.Contains("Unfold all inside", menu.Words());
                Assert.Contains("Fold all inside", menu.Words());
                menu.Pick("Unfold all inside");
                Assert.True(VaultBook.Row(Page, "Charms").IsOpen);
                Assert.True(VaultBook.Row(Page, "Small").IsShown);
                Assert.False(VaultBook.Row(Page, "Jewels").IsOpen);

                // The vault's own row: the whole tree. A folder holding none has neither row.
                Assert.Contains("Unfold all", Page.MenuFor((long?)null).Words());
                Assert.DoesNotContain("Unfold all", Page.MenuFor(VaultBook.Row(Page, "Small").Id).Words());
            }
            finally
            {
                Page.FoldTreeCommand.Execute(null);
            }
        });
    }

    /// <summary>A folder made at the vault's top that holds nothing, under a name.</summary>
    private void MakeEmpty(string name)
    {
        Page.GoTo(null);
        Page.NewFolderCommand.Execute(null);
        var rename = Page.SelectedFolder!.Rename!;
        rename.Name = name;
        Page.CommitRename(rename);
    }

    [CorpusFact]
    public void A_bin_always_asks_an_empty_folder_by_its_second_press_or_in_its_menu_and_one_that_holds_something_once()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            MakeEmpty("Hollow");
            MakeEmpty("Husk");
            long hollow = Tile("Hollow").Place.Id!.Value, jewels = Tile("Jewels").Place.Id!.Value;

            // In its menu the empty folder's bin asks where it stands - on its tile, its row of the tree. The one
            // that holds something has its question for the asking, and the menu's own is left out: one question,
            // not two.
            Assert.True(Page.MenuFor(hollow, onSheet: true).Row("Delete").Asks);
            Assert.True(Page.MenuFor(hollow).Row("Delete").Asks);
            Assert.False(Page.MenuFor(jewels, onSheet: true).Row("Delete").Asks);

            // An item's menu the same: asked in the menu - but not with a folder that holds something beside it.
            Page.ClickTileCommand.Execute(VaultBook.Card(Page, Tome));
            Assert.True(Page.MenuFor(VaultBook.Card(Page, Tome)).Row("Delete").Asks);
            Page.ClickFolderTileCommand.Execute(Tile("Jewels"));
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Tome));
            Assert.False(Page.MenuFor(VaultBook.Card(Page, Tome)).Row("Delete").Asks);
            Page.ClearSelection();

            // The tools' bin: the first press arms it, nothing gone; the second deletes. No question.
            Page.ClickFolderTileCommand.Execute(Tile("Hollow"));
            Page.DeleteCommand.Execute(null);
            Assert.False(Confirm.IsAsking);
            Assert.True(Page.DeleteArmed);
            Assert.Contains("Hollow", Tiles);

            Page.DeleteCommand.Execute(null);
            Assert.False(Confirm.IsAsking);
            Assert.DoesNotContain("Hollow", Tiles);
            Assert.Equal("Deleted Hollow.", Page.Notice);

            // An empty folder and an item together: asked the same way, and both go.
            Page.ClickFolderTileCommand.Execute(Tile("Husk"));
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Tome));
            Page.DeleteCommand.Execute(null);
            Assert.False(Confirm.IsAsking);
            Assert.True(Page.DeleteArmed);
            Assert.Contains("Husk", Tiles);

            Page.DeleteCommand.Execute(null);
            Assert.DoesNotContain("Husk", Tiles);
            Assert.False(VaultBook.HasCard(Page, Tome));
            Assert.Equal("Deleted 2 selected.", Page.Notice);
        });
    }

    [CorpusFact]
    public void The_menu_of_a_tile_among_several_selected_is_the_selections_and_only_a_tiles_menu_says_the_keys()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            MakeEmpty("Hollow");
            long hollow = Tile("Hollow").Place.Id!.Value, jewels = Tile("Jewels").Place.Id!.Value;

            // A tile's menu says the keys: the right press selected it, and the keys act on what is selected. A row
            // of the tree's says none - Ctrl + X, F2 and Del do nothing to it.
            var ofTile = Page.MenuFor(jewels, onSheet: true);
            Assert.Equal(["Ctrl + X", "F2", "Del"], new[] { "Cut", "Rename", "Delete" }.Select(act => ofTile.Row(act).Keys));
            var ofRow = Page.MenuFor(jewels);
            Assert.Equal(["", "", ""], new[] { "Cut", "Rename", "Delete" }.Select(act => ofRow.Row(act).Keys));

            // Two tiles selected, the menu of one: the selection's, as an item's menu is - Rename, which names
            // one, dimmed; Move counted; the bin asked by the question of the one that holds something.
            Page.ClickFolderTileCommand.Execute(Tile("Hollow"));
            VaultHands.CtrlClick(Page, Tile("Jewels"));
            Assert.Equal(2, Page.PickedCount);

            var menu = Page.MenuFor(hollow, onSheet: true);
            Assert.False(menu.Row("Rename").IsEnabled);
            Assert.Equal("Move to folder... (2)", menu.Row("Move to folder...").Label);
            Assert.False(menu.Row("Delete").Asks);

            menu.Pick("Cut");
            Assert.True(ItemClipboard.Shared.IsCut(hollow));
            Assert.True(ItemClipboard.Shared.IsCut(jewels));
            Assert.Equal("2 cut. Paste to move them.", Page.Notice);

            // The same folder's row of the tree: that folder alone, whatever is selected on the sheet.
            ItemClipboard.Shared.Clear();
            Page.MenuFor(hollow).Pick("Cut");
            Assert.True(ItemClipboard.Shared.IsCut(hollow));
            Assert.False(ItemClipboard.Shared.IsCut(jewels));
            ItemClipboard.Shared.Clear();
        });
    }

    [CorpusFact]
    public void A_folder_moved_where_its_name_is_taken_says_the_name_it_landed_under()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            // A second Jewels, inside Charms.
            VaultBook.GoTo(Page, "Charms");
            Page.NewFolderCommand.Execute(null);
            var rename = Page.SelectedFolder!.Rename!;
            rename.Name = "Jewels";
            Page.CommitRename(rename);

            // The top's Jewels cut, and pasted beside it: there as "Jewels (2)" - which is said (it was not).
            Page.GoTo(null);
            Page.ClickFolderTileCommand.Execute(Tile("Jewels"));
            Page.CutCommand.Execute(null);
            VaultBook.GoTo(Page, "Charms");
            Page.PasteCommand.Execute(null);

            Assert.Contains("Jewels (2)", Tiles);
            Assert.Contains("Jewels is now Jewels (2).", Page.Notice);
        });
    }
}
