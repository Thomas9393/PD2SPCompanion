using D2SLib.Model.Save;

using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// An item dragged onto the belt (PlanBelt, DropIntoBelt, HoverBelt): the column it is let go of, its lowest free box, a
/// full column filling the room, what a belt does not take refused and put back, several carried all or none, and no box
/// past the rows the worn belt has. Asked as the view asks at the press, the hover and the release, with the boxes read
/// back from the saved file, which is where the game reads them. That a real drag finds the cell is a window's to show.
/// <para>
/// BeltArrivalsTests has the tab's potion and the paste; this is the drag of the character's own.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class BeltDropTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    private const string Potion = "hp1";

    /// <summary>The boxes held, as the saved file has them; the parser left as the open character's.</summary>
    private List<int> BoxesInFile()
    {
        Assert.True(program.Saved(), "the belt was not saved");
        var read = D2SLib.Core.ReadD2S(File.ReadAllBytes(program.SaveOf(Program.Open)));
        var boxes = read.PlayerItemList.Items.Where(item => item.Mode == ItemMode.Belt).Select(item => (int)item.X).Order().ToList();
        Session.UseMyLayout();
        return boxes;
    }

    private List<int> Boxes() => [.. Session.BeltItems.Select(item => (int)item.X).Order()];

    /// <summary>The belt emptied, as a start.</summary>
    private void EmptyTheBelt()
    {
        foreach (var tile in Session.BeltGrid!.Items.ToList())
            Assert.Null(Session.Delete(tile));
        Assert.Empty(Session.BeltItems);
    }

    /// <summary>A potion handed to the character, loose, and its tile.</summary>
    private ItemTileViewModel LoosePotion()
    {
        var item = program.GivePlain(Potion, 0);
        return program.TileOf(item);
    }

    private GridCell Column(int column) => Session.BeltGrid!.Cells.First(cell => cell.Column == column);

    private void Drop(ItemTileViewModel tile, int column)
    {
        Assert.True(Page.BeginCarry(tile), $"{tile.Name} was not lifted");
        Page.DropCarried(Column(column), pack: false);
    }

    [CorpusFact]
    public void A_potion_takes_the_lowest_free_box_of_the_column_it_is_let_go_of_on()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            EmptyTheBelt();
            int rows = Session.BeltRows;
            Assert.True(rows >= 2, $"the pad's belt has {rows} rows");

            var first = LoosePotion();
            Drop(first, 2);
            Assert.Equal($"Moved {first.Name} to the belt.", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Equal([2], Boxes());

            Drop(LoosePotion(), 2);
            Assert.Equal([2, 2 + Belts.Columns], Boxes());

            Drop(LoosePotion(), 0);
            Assert.Equal([0, 2, 2 + Belts.Columns], Boxes());

            // In the file, where the game reads them; each once.
            Assert.Equal(Boxes(), BoxesInFile());
        });
    }

    [CorpusFact]
    public void A_full_column_fills_the_room_and_a_full_belt_refuses_and_puts_back()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            EmptyTheBelt();
            int rows = Session.BeltRows, boxes = rows * Belts.Columns;

            // A column filled, then one more on it: in, elsewhere.
            for (int row = 0; row < rows; row++)
                Drop(LoosePotion(), 1);
            Assert.Equal(Enumerable.Range(0, rows).Select(row => row * Belts.Columns + 1), Boxes());

            Drop(LoosePotion(), 1);
            Assert.Equal(rows + 1, Boxes().Count);
            Assert.Equal(rows + 1, Boxes().Distinct().Count());

            // Everything else, all on the one column.
            while (Boxes().Count < boxes)
                Drop(LoosePotion(), 1);
            Assert.Equal(Enumerable.Range(0, boxes), Boxes());

            var last = LoosePotion();
            byte page = last.Item.Page, x = last.Item.X, y = last.Item.Y;
            Drop(last, 1);

            Assert.Equal("No free space in the belt. Put back.", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Equal((ItemMode.Stored, page, x, y), (last.Item.Mode, (byte)last.Item.Page, (byte)last.Item.X, (byte)last.Item.Y));
            Assert.Equal(Enumerable.Range(0, boxes), BoxesInFile());
        });
    }

    [CorpusFact]
    public void What_a_belt_does_not_take_is_refused_with_its_name_and_put_back()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            var before = Boxes();
            var tile = program.Grid("Personal Stash").Items.First(each => !Belts.Takes(each.Item) && !each.Item.IsEar);
            var (mode, page, x, y) = (tile.Item.Mode, tile.Item.Page, tile.Item.X, tile.Item.Y);

            Drop(tile, 0);

            Assert.Equal($"Belt takes potions and scrolls, not {tile.Name}. Put back.", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Equal((mode, page, x, y), (tile.Item.Mode, tile.Item.Page, tile.Item.X, tile.Item.Y));
            Assert.Equal(before, Boxes());
        });
    }

    [CorpusFact]
    public void Several_carried_go_in_all_of_them_or_none()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            EmptyTheBelt();
            int boxes = Session.BeltRows * Belts.Columns;

            void Carry(params ItemTileViewModel[] tiles)
            {
                Page.ClickTileCommand.Execute(tiles[0]);
                Assert.True(Page.BeginDragPick(keepSelected: true));
                foreach (var tile in tiles.Skip(1))
                    Page.SetPicked(tile, true);
                Page.EndPick();
                Assert.True(Page.BeginCarry(tiles[0]));
                Assert.Equal(tiles.Length - 1, Session.HeldGroup.Count);
            }

            // Two: both in, up the column dropped on.
            var (one, two) = (LoosePotion(), LoosePotion());
            Carry(one, two);
            Page.DropCarried(Column(3), pack: false);
            Assert.Equal("Moved 2 items to the belt.", Session.Status);
            Assert.Equal([3, 3 + Belts.Columns], Boxes());

            // A ring among them: none go, and the words name it.
            var (potion, ring) = (LoosePotion(), program.Grid("Personal Stash").Items.First(each => !Belts.Takes(each.Item)));
            var ringAt = (ring.Item.Page, ring.Item.X, ring.Item.Y);
            Carry(potion, ring);
            Page.DropCarried(Column(0), pack: false);
            Assert.Equal($"Belt takes potions and scrolls, not {ring.Name}. Put back.", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Equal(ItemMode.Stored, potion.Item.Mode);
            Assert.Equal(ringAt, (ring.Item.Page, ring.Item.X, ring.Item.Y));
            Assert.Equal(2, Boxes().Count);

            // One box left and two carried: none go.
            while (Boxes().Count < boxes - 1)
                Drop(LoosePotion(), 0);

            var (third, fourth) = (program.TileOf(potion.Item), LoosePotion());
            Carry(third, fourth);
            Page.DropCarried(Column(0), pack: false);
            Assert.Equal("No free space in the belt for all of them. Put back.", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Equal(boxes - 1, Boxes().Count);
            Assert.Equal(ItemMode.Stored, third.Item.Mode);
            Assert.Equal(ItemMode.Stored, fourth.Item.Mode);
        });
    }

    [CorpusFact]
    public void A_small_belts_missing_rows_are_never_written()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            EmptyTheBelt();

            // A sash in the worn belt's place: the rows are read from what is worn.
            var worn = program.Worn(ItemLocation.Waist).Item!.Item;
            worn.Code = "lbl ";
            Assert.Equal(2, Session.BeltRows);

            for (int each = 0; each < 2 * Belts.Columns; each++)
                Drop(LoosePotion(), 0);
            Assert.Equal(Enumerable.Range(0, 2 * Belts.Columns), Boxes());

            var over = LoosePotion();
            Drop(over, 0);
            Assert.Equal("No free space in the belt. Put back.", Session.Status);
            Assert.Equal(ItemMode.Stored, over.Item.Mode);
            Assert.All(BoxesInFile(), box => Assert.InRange(box, 0, 2 * Belts.Columns - 1));
        });
    }

    [CorpusFact]
    public void Hovering_a_column_lights_the_boxes_a_drop_would_take_and_a_refusal_lights_red()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            EmptyTheBelt();
            int rows = Session.BeltRows;
            var belt = Session.BeltGrid!;

            // Shown under the equipment, as it must be to be hovered; the page's own, and let go of at the end.
            Page.ShowBelt = true;

            Assert.True(Page.BeginCarry(LoosePotion()));
            Page.HoverOver(Column(2));

            // The bottom row, the column hovered: nothing else.
            var lit = Assert.Single(belt.Cells, cell => cell.IsTarget);
            Assert.Equal((2, rows - 1), (lit.Column, lit.Row));
            Assert.True(lit.IsAllowed);

            Page.HoverOver(null);
            Assert.DoesNotContain(belt.Cells, cell => cell.IsTarget);
            Page.CancelHold();

            var ring = program.Grid("Personal Stash").Items.First(each => !Belts.Takes(each.Item));
            Assert.True(Page.BeginCarry(ring));
            var over = Column(1);
            Page.HoverOver(over);
            Assert.True(over.IsTarget);
            Assert.False(over.IsAllowed);
            Page.HoverOver(null);
            Page.CancelHold();
            Page.ShowBelt = false;
        });
    }
}
