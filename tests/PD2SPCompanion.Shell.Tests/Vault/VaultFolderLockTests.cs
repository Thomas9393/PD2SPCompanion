using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A vault folder's lock.
/// A locked folder's items take no pick: Ctrl + A says "Folder locked." and picks nothing - and does
/// again once it is let go. Every folder's lock stands by its row of the tree,
/// any folder's to set without opening it; the tools line keeps one for the folder
/// shown where the tree is folded away. A lock does not keep a folder out of Sort:
/// with no item having a place of its own the Sort menu is a way of looking, which a lock has nothing to keep
/// from, and the vault has no Close gaps.
/// <para>
/// What the page decides, asked as the locks and the Sort menu's lines ask, and read back from the page and the
/// vault. That the locks stand in a column of their own beside the tree and follow its scrolling, and that the
/// menu is there to press, are the UI cases' to show (units/vault-folder-lock.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultFolderLockTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    /// <summary>Small's two charms.</summary>
    private const string Fortune = "Small Charm of Fortune", Garnet = "Garnet Small Charm of Life";

    /// <summary>A line of the Sort menu picked: an order.</summary>
    private void SortMenu(string line) => Page.Order.Pick(Page.Order.Options.First(option => option.Label == line));

    public void Dispose() => _book.PutAway();

    [CorpusFact]
    public void The_lock_is_off_to_begin_with_and_on_says_what_it_holds_back()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            Assert.False(Page.IsFolderLocked);
            Assert.Equal("Folder lock off", Page.FolderLockHint);

            Page.ToggleFolderLockCommand.Execute(null);
            Assert.True(Page.IsFolderLocked);
            Assert.Equal("Folder lock on" + Environment.NewLine + "Items cannot be picked; Store all skips it", Page.FolderLockHint);
            Assert.True(VaultBook.Row(Page, "Small").IsLocked);
            Assert.False(VaultBook.Row(Page, "Grand").IsLocked);

            Page.ToggleFolderLockCommand.Execute(null);
            Assert.False(Page.IsFolderLocked);
            Assert.Equal("Folder lock off", Page.FolderLockHint);
        });
    }

    /// <summary>
    /// A locked folder's items cannot be picked, as a locked stash page's cannot: one lock, one
    /// rule. Select all held back alone; Ctrl + click, the rectangle and Invert picked there.
    /// </summary>
    [CorpusFact]
    public void A_locked_folders_items_cannot_be_picked()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            Page.ToggleFolderLockCommand.Execute(null);
            Assert.True(Page.IsFolderLocked);

            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Fortune));
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Garnet));
            Assert.Equal(0, Page.PickedCount);
            Assert.Null(Page.IsPicked(VaultBook.Card(Page, Fortune)));
            Assert.Equal("Folder locked.", Page.Notice);

            Page.Picks.Pick(Page.Picks.Options.First(option => option.Label == "Invert selection"));
            Assert.Equal(0, Page.PickedCount);

            // Ctrl + A says so, and picks nothing.
            Page.Notice = "";
            Page.SelectAll();
            Assert.Equal("Folder locked.", Page.Notice);
            Assert.False(Page.IsPicking);
            Assert.Equal("2 items", Page.PagesText);

            // The row of the empty room's menu is dimmed for it, and says why before it is picked.
            var all = Page.MenuForSpace().Row("Select all");
            Assert.False(all.IsEnabled);
            Assert.Equal("Folder locked.", all.Why);

            // Let go: picked as ever.
            Page.ToggleFolderLockCommand.Execute(null);
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Fortune));
            VaultHands.CtrlClick(Page, VaultBook.Card(Page, Garnet));
            Assert.Equal(2, Page.PickedCount);
        });
    }

    /// <summary>
    /// Store all's sorting passes a locked folder by (VaultMoves' writing; Vault.IsLocked): what the sorter would
    /// deliver into it goes where it was sent unsorted, as a kind with no rule does. The pad's personal stash holds
    /// grand charms, the sorter's for Charms\Grand, and a jewel, for Jewels\Magic.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Store_all_sorted_passes_a_locked_folder_by_and_delivers_into_the_others()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        int grand = 0, magic = 0, stored = 0;
        long grandId = 0;
        program.On(() =>
        {
            grandId = _book.Store.Folders().First(each => each.Name == "Grand").Id;
            (grand, magic, stored) = (_book.Holds("Grand"), _book.Holds("Magic"), VaultHands.Held(program));
            VaultBook.ToggleLock(Page, "Grand");
            Assert.True(_book.Store.IsLocked(grandId));

            program.Panes("Personal Stash");
            Assert.Contains(program.Grid("Personal Stash").Items, tile => tile.Name.Contains("Grand Charm"));
            Assert.Contains(program.Grid("Personal Stash").Items, tile => tile.Name.Contains("Jewel"));

            // Asked twice, as the button asks.
            var items = program.Page();
            items.StoreAllPress(VP_ViewModel_Inv_L.StoreAs.Sorted);
            Assert.True(items.StoreAllArmed);
            items.StoreAllPress(VP_ViewModel_Inv_L.StoreAs.Sorted);
        });

        Assert.True(program.Until(() => VaultHands.Held(program) > stored && !program.Get<VaultMoves>().HasPending, 20000), "Store all stored nothing");
        program.On(() =>
        {
            // The sorter delivered: the stash's jewel lies in Jewels\Magic.
            Assert.True(_book.Holds("Magic") > magic, "nothing was sorted into Magic");

            // And passed the locked folder by: Grand holds what it held, the stash's grand charms stored elsewhere.
            Assert.Equal(grand, _book.Holds("Grand"));
            var charms = _book.Store.RowsOf(_book.Store.AllKeys()).Where(row => row.Name.Contains("Grand Charm")).ToList();
            Assert.True(charms.Count > grand, "the grand charms were not stored");
            Assert.Equal(grand, charms.Count(row => row.FolderId == grandId));
        });
    }

    [CorpusFact]
    public void A_folders_lock_is_set_from_its_row_of_the_tree_without_the_folder_being_opened()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            Assert.False(VaultBook.Row(Page, "Grand").IsLocked);

            // Another folder's row: locked there, and the folder shown is neither left nor locked.
            VaultBook.ToggleLock(Page, "Grand");
            Assert.True(VaultBook.Row(Page, "Grand").IsLocked);
            Assert.True(_book.Store.IsLocked(VaultBook.Row(Page, "Grand").Id));
            Assert.True(VaultBook.Row(Page, "Small").IsHere);
            Assert.False(Page.IsFolderLocked);

            // The row of the folder shown: the same lock the tools line's button sets.
            VaultBook.ToggleLock(Page, "Small");
            Assert.True(Page.IsFolderLocked);
            Page.ToggleFolderLockCommand.Execute(null);
            Assert.False(VaultBook.Row(Page, "Small").IsLocked);

            VaultBook.ToggleLock(Page, "Grand");
            Assert.False(_book.Store.IsLocked(VaultBook.Row(Page, "Grand").Id));
        });
    }

    [CorpusFact]
    public void A_locked_folder_is_shown_in_the_order_picked_as_any_other()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            Page.ToggleFolderLockCommand.Execute(null);

            // The Sort menu is a way of looking: a lock keeps nothing from it. By name Garnet comes first; the
            // same line again, and the order is turned round.
            Page.Notice = "";
            SortMenu("Name");
            if (Page.Order.Descending)
                SortMenu("Name");
            Assert.Equal("Sorted by Name.", Page.Notice);
            Assert.Equal(0, VaultBook.Card(Page, Garnet).CellX);
            Assert.Equal(1, VaultBook.Card(Page, Fortune).CellX);

            SortMenu("Name");
            Assert.True(Page.Order.Descending);
            Assert.Equal(0, VaultBook.Card(Page, Fortune).CellX);
            Assert.Equal(1, VaultBook.Card(Page, Garnet).CellX);
        });
    }
}
