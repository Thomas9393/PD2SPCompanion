using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The vault's folder as one sheet, laid out by a rule:
/// no item
/// has a place of its own. The sheet is as wide as the page has room for, in fours and never fewer than
/// twelve; a card let go of on its own sheet stays in its folder; while the pointer is on the sheet an item that
/// leaves leaves its place empty, and the folder closes up as the pointer goes; a click on an item selects it;
/// and the Sort menu is a way of looking - the folder laid out again in the order picked, turned round by the
/// same order picked again, with nothing written and no step of the history made.
/// <para>
/// What the page decides, asked as the view asks (SetSheetColumns, HoldSheet, BeginCarry, DropCarriedOnSheet, the
/// tile's command) and read back from the cards. How many columns fit a page, the pointer coming and going, and
/// everything the scroll does are the UI cases' to show (units/vault-sheet.ps1). The rule that lays a folder out
/// is Core's (VaultLayoutTests).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultSheetTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    private const string Tome = "Tome of Town Portal", Fortune = "Small Charm of Fortune", Garnet = "Garnet Small Charm of Life";

    public void Dispose()
    {
        program.On(() =>
        {
            Page.HoldSheet(false);
            Page.SetSheetColumns(20);
        });
        _book.PutAway();
    }

    [CorpusFact]
    public void The_sheet_is_as_wide_as_the_page_says_in_fours_and_never_fewer_than_four()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            Assert.Equal(20, Page.SheetColumns);
            Assert.Equal(20, VaultBook.Sheet(Page).Grid.Columns);

            // The folder's one item lies in the first cell beside its folders' tiles' last row (FolderTile, four
            // rows of them), whatever the width.
            var tome = VaultBook.Card(Page, Tome);
            Assert.Equal(FirstItemAt(20), (tome.CellX, Page.RowOf(tome.Key)));
            Assert.Contains(tome, VaultBook.Sheet(Page).Grid.Items);

            Page.SetSheetColumns(31);
            Assert.Equal(28, Page.SheetColumns);
            Assert.Equal(28, VaultBook.Sheet(Page).Grid.Columns);
            Assert.Equal(FirstItemAt(28), (VaultBook.Card(Page, Tome).CellX, Page.RowOf(VaultBook.Card(Page, Tome).Key)));

            // Four at the least, one of a folder's tiles: the item still under them.
            Page.SetSheetColumns(7);
            Assert.Equal(4, Page.SheetColumns);
            Assert.Equal(4, VaultBook.Sheet(Page).Grid.Columns);
            Assert.Equal(0, VaultBook.Card(Page, Tome).CellX);
            Page.SetSheetColumns(1);
            Assert.Equal(4, Page.SheetColumns);
            Page.SetSheetColumns(20);
        });
    }

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

    /// <summary>The vault's file and its log, as they stand: a write to the vault changes one of them.</summary>
    private string Written()
    {
        string file = _book.Store.FilePath;
        return string.Join("|", new[] { file, file + "-wal" }.Select(each =>
            File.Exists(each) ? $"{new FileInfo(each).Length}@{File.GetLastWriteTimeUtc(each).Ticks}" : "-"));
    }

    [CorpusFact]
    public void Sorted_a_folders_items_lie_side_by_side_in_the_order_picked_and_the_same_order_again_turns_it_round()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            var name = Page.Order.Options.First(option => option.Label == "Name");
            (int, int) At(string item) => (VaultBook.Card(Page, item).CellX, VaultBook.Card(Page, item).CellY);

            Page.Order.Pick(name);
            if (Page.Order.Descending)
                Page.Order.Pick(name);
            Assert.Equal("Sorted by Name.", Page.Notice);
            Assert.Equal("Name", Page.Order.By);

            // By name: Garnet before Small, the two in one lane's two columns.
            Assert.Equal((0, 0), At(Garnet));
            Assert.Equal((1, 0), At(Fortune));

            // The same line again: turned round.
            Page.Notice = "";
            Page.Order.Pick(name);
            Assert.True(Page.Order.Descending);
            Assert.Equal("Sorted by Name.", Page.Notice);
            Assert.Equal((0, 0), At(Fortune));
            Assert.Equal((1, 0), At(Garnet));
        });
    }

    /// <summary>
    /// No item has a place for a sort to write: a sort that wrote every item's was a step of the history
    /// that Undo took back. The order itself is kept in the settings, as a look is.
    /// </summary>
    [CorpusFact]
    public void A_sort_writes_nothing_to_the_vault_and_is_no_step_of_the_history()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            var history = program.Get<PD2SPCompanion.Shell.Services.Character.SessionHistory>();
            int steps = history.Rows.Count;
            string written = Written();

            foreach (string line in new[] { "Name", "Name", "Rarity", "Type", "Stored" })
                Page.Order.Pick(Page.Order.Options.First(option => option.Label == line));

            Assert.Equal("Sorted by Stored.", Page.Notice);
            Assert.Equal(written, Written());
            Assert.Equal(steps, history.Rows.Count);
            Assert.False(program.Get<PD2SPCompanion.Shell.Services.Vault.VaultMoves>().HasPending);
        });
    }

    [CorpusFact]
    public void A_card_let_go_of_on_its_own_sheet_stays_in_its_folder()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            int stored = VaultHands.Held(program);
            Assert.True(Page.BeginCarry(VaultBook.Card(Page, Tome)));

            // Over its own sheet it would go nowhere: the sheet is not framed as somewhere to let go.
            Page.HoverSheet(true);
            Assert.False(Page.IsSheetTarget);

            Page.DropCarriedOnSheet();
            Assert.False(Page.IsCarrying);
            Assert.Equal(stored, VaultHands.Held(program));
            Assert.Null(_book.Held(Tome).FolderId);
        });
    }

    [CorpusFact]
    public void While_the_pointer_is_on_the_sheet_an_item_that_leaves_leaves_its_place_empty()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            VaultBook.GoTo(Page, "Small");
            Page.Order.Pick(Page.Order.Options.First(option => option.Label == "Name"));
            if (Page.Order.Descending)
                Page.Order.Pick(Page.Order.Options.First(option => option.Label == "Name"));
            Assert.Equal(1, VaultBook.Card(Page, Fortune).CellX);

            // The pointer on the sheet; the first of the two deleted: the second stays where it lay.
            Page.HoldSheet(true);
            Page.ClickTileCommand.Execute(VaultBook.Card(Page, Garnet));
            Page.DeleteCommand.Execute(null);
            Page.DeleteCommand.Execute(null);
            Assert.False(VaultBook.HasCard(Page, Garnet));
            Assert.Equal((1, 0), (VaultBook.Card(Page, Fortune).CellX, VaultBook.Card(Page, Fortune).CellY));

            // The pointer gone: the folder closes up.
            Page.HoldSheet(false);
            Assert.Equal((0, 0), (VaultBook.Card(Page, Fortune).CellX, VaultBook.Card(Page, Fortune).CellY));
        });
    }

    [CorpusFact]
    public void A_click_on_an_item_selects_it_for_the_Edit_page()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            var card = VaultBook.Card(Page, Tome);
            Assert.Null(Page.Selected);

            Page.ClickTileCommand.Execute(card);

            Assert.Same(card, Page.Selected);
            Assert.True(card.IsSelected);
            Assert.Same(card, _book.Vaults.Chosen);
        });
    }
}
