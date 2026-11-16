using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A folder longer than a stretch of its sheet.
/// The sheet is drawn fifteen rows at a time, and the pad's own folders are
/// a stretch long at most - so what only a second stretch shows had no test: the item a view keeps in sight when
/// the page's width changes and the folder is laid out again (KeyNearRow, RowOf); a Shift + click's run across
/// the edge of a stretch, where it stopped; and the layout held while the pointer is on the sheet, as items leave
/// it one after another and when one arrives.
/// <para>
/// Forty armours, two cells by three, put into the pad's empty folder as <c>pd2spc vault --add</c> puts one, and
/// the sheet twelve columns wide, its least: six to a shelf of three rows, thirty to a
/// stretch, the folder running into a second. In the order stored, the n-th lies in column 2 x (n mod 6) of row
/// 3 x (n / 6). What the page decides,
/// asked as the view asks.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultLongSheetTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    private const int Count = 40;

    public void Dispose()
    {
        if (program.Available)
        {
            program.On(() =>
            {
                Page.HoldSheet(false);
                Page.SetSheetColumns(20);
            });
        }

        _book.PutAway();
    }

    /// <summary>One more armour in a folder, as the vault's own deposit puts it; its key.</summary>
    private long Armour(long folder, int n)
    {
        var item = ItemForge.PlainBase("qui", 0, _book.Session.ItemEra!).Item;
        item.Id = (uint)(900_000_000 + n);
        return _book.Store.Add(item, folder, out string? refused) ?? throw new InvalidOperationException(refused);
    }

    /// <summary>The long folder shown on the left page, ten columns wide, in the order stored; its items' keys in that order.</summary>
    private (long Folder, List<long> Keys) LongFolder()
    {
        _book.OpenLeft();
        return program.On(() =>
        {
            long folder = _book.Store.Folders().First(each => each.Name == "New folder").Id;
            Assert.Equal(0, _book.Store.Count(folder));
            var keys = Enumerable.Range(0, Count).Select(n => Armour(folder, n)).ToList();
            _book.Vaults.SayChanged();

            Page.SetSheetColumns(12);
            Page.GoTo(folder);
            var stored = Page.Order.Options.First(option => option.Label == "Stored");
            if (Page.Order.By != stored.Label)
                Page.Order.Pick(stored);
            if (Page.Order.Descending)
                Page.Order.Pick(stored);

            Assert.Equal(Count, Page.Marks.Count);
            Assert.Equal(keys, Page.Marks.Select(mark => mark.Key));
            return (folder, keys);
        });
    }

    private ItemTileViewModel Card(long key) => VaultBook.CardsOf(Page).First(card => card.Key == key);

    [CorpusFact]
    public void The_item_nearest_a_row_is_found_again_where_another_width_lays_it()
    {
        if (!program.Ready(changes: true))
            return;

        var (_, keys) = LongFolder();
        program.On(() =>
        {
            // Twelve columns: six to a shelf of three rows. The thirty-first is the first of row 15, the second stretch's first.
            Assert.Equal(0, Page.RowOf(keys[0]));
            Assert.Equal(15, Page.RowOf(keys[30]));
            Assert.Equal(keys[30], Page.KeyNearRow(15));

            // A row an item does not begin on: the next that does. Past the end: the last.
            Assert.Equal(keys[30], Page.KeyNearRow(13));
            Assert.Equal(keys[36], Page.KeyNearRow(16));
            Assert.Equal(keys[^1], Page.KeyNearRow(500));

            // Twenty columns: ten to a shelf. The same item is on row 9 now - what the view scrolls to.
            Page.SetSheetColumns(20);
            Assert.Equal(20, Page.SheetColumns);
            Assert.Equal(9, Page.RowOf(keys[30]));
            Assert.Equal(keys[30], Page.KeyNearRow(9));

            // An item the folder does not hold has no row.
            Assert.Null(Page.RowOf(-1));
        });
    }

    [CorpusFact]
    public void A_Shift_clicks_run_goes_on_across_the_edge_of_a_stretch()
    {
        if (!program.Ready(changes: true))
            return;

        var (_, keys) = LongFolder();
        program.On(() =>
        {
            // The third lies in the first stretch, the thirty-eighth in the second.
            Assert.True(Page.RowOf(keys[2]) < PD2SPCompanion.Core.Collection.Vault.PageRows);
            Assert.True(Page.RowOf(keys[37]) >= PD2SPCompanion.Core.Collection.Vault.PageRows);

            // What the page gives is the cards of every stretch from the one to the other, as a reader meets them
            // (DragPick picks from the one to the other in it): both stretches' here, so the whole folder.
            var order = ((IDragPicker)Page).ReadingOrder(Card(keys[2]), Card(keys[37]))?.Cast<ItemTileViewModel>().Select(card => card.Key).ToList();
            Assert.NotNull(order);
            Assert.Equal(keys, order);

            // So the run from the third to the thirty-eighth is those thirty-six, in the folder's order.
            int from = order.IndexOf(keys[2]), to = order.IndexOf(keys[37]);
            Assert.Equal(keys[2..38], order[from..(to + 1)]);

            // Asked from the other end, the same.
            var back = ((IDragPicker)Page).ReadingOrder(Card(keys[37]), Card(keys[2]))?.Cast<ItemTileViewModel>().Select(card => card.Key).ToList();
            Assert.Equal(order, back);
        });
    }

    [CorpusFact]
    public void Held_the_layout_keeps_every_place_as_items_leave_one_after_another_and_is_made_afresh_when_one_arrives()
    {
        if (!program.Ready(changes: true))
            return;

        var (folder, keys) = LongFolder();
        program.On(() =>
        {
            (int, int?) At(long key) => (Card(key).CellX, Page.RowOf(key));
            Assert.Equal((4, 0), At(keys[2]));
            Assert.Equal((0, 15), At(keys[30]));

            // The pointer on the sheet. The first deleted, then the second: the third is where it lay, and so is
            // one of the second stretch.
            Page.HoldSheet(true);
            foreach (long gone in keys[..2])
            {
                Page.ClickTileCommand.Execute(Card(gone));
                Page.DeleteCommand.Execute(null);
                Page.DeleteCommand.Execute(null);
                Assert.Null(Page.RowOf(gone));
                Assert.Equal((4, 0), At(keys[2]));
                Assert.Equal((0, 15), At(keys[30]));
            }

            Assert.Equal(Count - 2, Page.Marks.Count);

            // One moved to another folder leaves its place empty as one deleted does.
            _book.Store.MoveItems([keys[3]], null);
            _book.Vaults.SayChanged();
            Assert.Null(Page.RowOf(keys[3]));
            Assert.Equal((4, 0), At(keys[2]));
            Assert.Equal((0, 15), At(keys[30]));

            // One arrives: held or not, the folder is laid out afresh - the new one has to lie somewhere.
            long came = Armour(folder, Count);
            _book.Vaults.SayChanged();
            Assert.Equal((0, 0), At(keys[2]));
            Assert.Equal(Page.Marks[^1].Key, came);

            // And with nothing stale the pointer's leaving changes nothing.
            Page.HoldSheet(false);
            Assert.Equal((0, 0), At(keys[2]));
            Assert.Equal(Count - 2, Page.Marks.Count);
        });
    }

    [CorpusFact]
    public void The_pointer_gone_the_folder_closes_up()
    {
        if (!program.Ready(changes: true))
            return;

        var (_, keys) = LongFolder();
        program.On(() =>
        {
            Page.HoldSheet(true);
            Page.ClickTileCommand.Execute(Card(keys[0]));
            Page.DeleteCommand.Execute(null);
            Page.DeleteCommand.Execute(null);
            Assert.Equal(2, Card(keys[1]).CellX);
            Assert.Equal(15, Page.RowOf(keys[30]));

            // Thirty-nine left: the second is first, and the thirty-first ends the shelf above the one it began.
            Page.HoldSheet(false);
            Assert.Equal((0, 0), (Card(keys[1]).CellX, Page.RowOf(keys[1]) ?? -1));
            Assert.Equal((10, 12), (Card(keys[30]).CellX, Page.RowOf(keys[30]) ?? -1));
        });
    }
}
