using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The vault's path row and its sheet's picking:
/// the row counts the folder's items and what is selected; the list of every
/// folder under the path folds as the tree does; Ctrl + click and Ctrl + A pick, and a drag of one picked
/// carries them all; a letter typed goes to the item of that name; the folder shown is named, moved and
/// deleted by its tools, a new one gone into and named where the path stands; and what is let go of on the
/// path opens the move picker.
/// <para>
/// What the page decides, asked as the view asks - the picking as DragPick hands it over (BeginDragPick,
/// SetPicked, EndPick), the name box as its keys do (CommitRename) - and read back from the page and the
/// vault. That Ctrl + click, a drag, the keys and what is typed reach the sheet and the name box, and that a
/// chevron of the list folds without the row being gone to, are the UI cases' to show
/// (units/vault-folder-row.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultFolderRowTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    private string Shown => VaultBook.Shown(Page);

    private const string Fortune = "Small Charm of Fortune", Garnet = "Garnet Small Charm of Life";

    private ItemTileViewModel Tile(string name) => VaultBook.Card(Page, name);

    private FolderPlace Listed(string name) => Page.MenuPlaces.First(place => place.Name == name);

    /// <summary>Ctrl + click on an item of the sheet, as the picking hands it to the page.</summary>
    private void CtrlClick(string name)
    {
        var tile = Tile(name);
        Assert.True(Page.BeginDragPick(keepSelected: true));
        Page.SetPicked(tile, Page.IsPicked(tile) != true);
        Page.EndPick();
    }

    /// <summary>What is typed into the name box on the selected folder's tile, and Enter.</summary>
    private void Name(string name)
    {
        Assert.True(Page.SelectedFolder is { IsRenaming: true }, "no folder's tile is being named");
        var rename = Page.SelectedFolder!.Rename!;
        rename.Name = name;
        Page.CommitRename(rename);
    }

    private bool HasFolder(string name) => _book.Store.Folders().Any(folder => folder.Name == name);

    public void Dispose() => _book.PutAway();

    [CorpusFact]
    public void The_row_counts_the_folders_items()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            Assert.Equal("1 item", Page.PagesText);

            // By the list of every folder, open under the path.
            Page.ListEverything();
            Page.GoToPlace(Listed("Small"));

            Assert.Equal(@"Vault\Charms\Small", Shown);
            Assert.Equal("2 items", Page.PagesText);
        });
    }

    [CorpusFact]
    public void A_folder_of_the_list_brings_its_folders_in_and_puts_them_away_the_list_staying_open_and_the_folder_shown_where_it_is()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultHands.FoldAll(Page);
            VaultBook.GoTo(Page, "Small");
            Page.ListEverything();

            // The list opens as the tree is - folded, the folded folder the one shown lies in lit in its place
            // (not the folder shown kept unfolded in its own list, its chevron dead).
            Assert.False(Listed("Charms").IsOpen);
            Assert.False(Listed("Small").IsShown);
            Assert.True(Listed("Charms").IsTreeHere);
            Assert.True(Listed("Charms").CanFold);

            Assert.False(Listed("Magic").IsShown);
            Page.FoldPlaceCommand.Execute(Listed("Jewels"));
            Assert.True(Listed("Magic").IsShown);
            Assert.True(Listed("Jewels").IsShown);
            Assert.True(Page.IsMenuOpen);
            Assert.Equal(@"Vault\Charms\Small", Shown);

            Page.FoldPlaceCommand.Execute(Listed("Jewels"));
            Assert.False(Listed("Magic").IsShown);

            // And the folder shown comes into the list by its own folder's chevron.
            Page.FoldPlaceCommand.Execute(Listed("Charms"));
            Assert.True(Listed("Small").IsShown);
            Assert.True(Listed("Small").IsTreeHere);
            Page.FoldPlaceCommand.Execute(Listed("Charms"));
        });
    }

    [CorpusFact]
    public void Ctrl_click_picks_on_the_sheet_and_Ctrl_A_picks_the_folders_items_the_row_saying_how_many()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");

            // One picked is the selection: several begin at two.
            CtrlClick(Fortune);
            Assert.Equal(Fortune, Page.Selected?.Name);
            Assert.Equal("2 items", Page.PagesText);

            CtrlClick(Garnet);
            Assert.True(Page.IsPicking);
            Assert.Null(Page.Selected);
            Assert.Equal("2 items · 2 selected", Page.PagesText);

            Page.ClearSelection();
            Assert.Equal("2 items", Page.PagesText);

            Page.SelectAll();
            Assert.Equal("2 items · 2 selected", Page.PagesText);
        });
    }

    [CorpusFact]
    public void A_drag_of_one_of_two_picked_carries_both_and_onto_a_folder_both_go_into_it()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            CtrlClick(Fortune);
            CtrlClick(Garnet);

            // Taken hold of by one: both are carried. Let go of on their own sheet, both stay (no item has a cell
            // to be moved to); on a folder's row of the tree, both go into it.
            Assert.True(Page.BeginCarry(Tile(Fortune)));
            Assert.Equal(2, Page.GhostParts.Count);
            Page.DropCarriedOnSheet();
            Assert.Equal(2, _book.Holds("Small"));

            Assert.True(Page.BeginCarry(Tile(Fortune)));
            Page.DropCarriedOnFolder(VaultBook.Row(Page, "Grand"));
            Assert.Equal(0, _book.Holds("Small"));
            Assert.Equal(3, _book.Holds("Grand"));
        });
    }

    [CorpusFact]
    public void Rename_folder_names_the_folder_selected_on_its_tile_and_none_with_none_selected()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            // Explorer's Rename is what is selected's - it had renamed itself, not the
            // folder selected: in a folder with nothing selected the tool is dimmed, and names nothing.
            VaultBook.GoTo(Page, "Charms");
            Assert.False(Page.CanRenameFolder);
            Page.RenameFolderCommand.Execute(null);
            Assert.DoesNotContain(Page.SheetFolders, tile => tile.IsRenaming);

            // A folder's tile selected: named there, the page staying on the folder it lies in.
            Page.ClickFolderTileCommand.Execute(Page.SheetFolders.First(tile => tile.Name == "Small"));
            Assert.True(Page.CanRenameFolder);
            Page.RenameFolderCommand.Execute(null);
            Name("Tiny");

            Assert.Equal(@"Vault\Charms", Shown);
            Assert.True(HasFolder("Tiny"));
            Assert.False(HasFolder("Small"));
            Assert.Equal("Tiny", Page.SelectedFolder?.Name);
            Assert.False(Page.SelectedFolder!.IsRenaming);
        });
    }

    [CorpusFact]
    public void A_new_folder_is_a_tile_named_where_it_stands_and_being_empty_is_deleted_at_the_bins_second_press()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");

            // Made in the folder shown, as Explorer makes one: a tile, selected, its name box open - and the page
            // where it was.
            Page.NewFolderCommand.Execute(null);
            Assert.Equal(@"Vault\Charms\Small", Shown);
            Assert.Equal("New folder", Page.SelectedFolder?.Name);
            Name("Inner");

            Assert.Equal(@"Vault\Charms\Small", Shown);
            Assert.True(HasFolder("Inner"));
            Assert.Equal("Inner", Page.SelectedFolder?.Name);
            Assert.Equal("Empty", Page.SelectedFolder!.CountLines);

            // An empty folder is asked about as an item is, by the bin's second press - a bin always asks (
            // it went at the first). No question besides: it holds nothing to settle.
            Page.DeleteCommand.Execute(null);
            Assert.False(program.Get<ConfirmService>().IsAsking);
            Assert.True(Page.DeleteArmed);
            Assert.True(HasFolder("Inner"));

            Page.DeleteCommand.Execute(null);
            Assert.False(program.Get<ConfirmService>().IsAsking);
            Assert.Equal("Deleted Inner.", Page.Notice);
            Assert.Equal(@"Vault\Charms\Small", Shown);
            Assert.False(HasFolder("Inner"));
        });
    }

    [CorpusFact]
    public void The_folder_shown_moved_by_its_menu_goes_under_another_and_is_still_the_folder_shown()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");

            // By its menu - its row of the tree, its step of the path: the tools line has no Move folder for the folder
            // shown (a tool acts on what is selected; Move to folder takes a selected folder).
            Page.MenuFor(Page.Here).Pick("Move to folder...");
            Assert.True(Page.IsPickerOpen);
            Assert.Equal("Move Small", Page.MoveTitle);

            // A folder goes nowhere inside itself: it is not among the places offered.
            Assert.DoesNotContain(Page.Targets, target => target.Name == "Small");

            Page.MoveToCommand.Execute(Page.Targets.First(target => target.Name == "Jewels"));
            Assert.False(Page.IsPickerOpen);
            Assert.Equal(@"Vault\Jewels\Small", Shown);
            Assert.Equal("2 items", Page.PagesText);
        });
    }

    [CorpusFact]
    public void A_card_let_go_of_on_the_path_opens_the_move_picker_for_it()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");

            Assert.True(Page.BeginCarry(Tile(Fortune)));
            Page.MoveCarriedByPicker();

            Assert.False(Page.IsCarrying);
            Assert.True(Page.IsPickerOpen);
            Assert.Equal($"Move {Fortune}", Page.MoveTitle);

            // The folder the item is in is no place to move it.
            Assert.DoesNotContain(Page.Targets, target => target.Name == "Small");

            Page.MoveToCommand.Execute(Page.Targets.First(target => target.Name == "Vault"));
            Assert.Equal("Moved to Vault.", Page.Notice);
            Assert.Equal("1 item", Page.PagesText);
            Assert.Equal(2, _book.Store.Count(null));
        });
    }
}
