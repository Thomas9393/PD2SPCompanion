using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// The corpse: a character that died and left the game without
/// taking its body up keeps what it wore on a corpse, in a list of the save's own - read and written, and shown
/// by no page, so the character looked stripped. On a real save, a level 86 Amazon with
/// one corpse of twelve items (TestCorpus, specimens/corpse-twelve-worn-items-s13.d2s): they are listed beside
/// what is worn, packed into a small grid, and Take all from corpse puts all of them back on, the corpse gone
/// from the save. One way, out (no item in): one
/// item is dragged to a page, put on, deleted or stored as any item is - and nothing goes onto the corpse.
/// <para>
/// What the session and the page decide, asked as the view asks. That the panel is drawn over the worn slots and
/// its button is there to press is the UI case's to show (units/corpse.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CorpseTests(Program program)
{
    private const string Dead = "LightFury";

    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    private static string Specimen => Path.Combine(Program.Corpus, "specimens", "corpse-twelve-worn-items-s13.d2s");

    /// <summary>The specimen laid in the pad's saves and opened; gone again with the test (Made.Gone).</summary>
    private void OpenTheDead()
    {
        program.On(() => File.Copy(Specimen, program.SaveOf(Dead), overwrite: true));
        Made.Open(program, Dead);
    }

    [CorpusFact]
    public void What_lies_on_the_corpse_is_shown_in_a_small_grid_and_taken_back_on_all_at_once()
    {
        if (!program.Ready(changes: true) || !File.Exists(Specimen))
            return;

        try
        {
            OpenTheDead();
            program.On(() =>
            {
                // Twelve on the corpse, nothing worn: the save as the game left it.
                Assert.True(Session.HasCorpse);
                Assert.Equal(12, Session.CorpseCount);
                Assert.True(Page.HasCorpse);
                Assert.Equal(12, Page.CorpseSlots.Count);
                Assert.Equal("12 items on the corpse", Page.CorpseDetail);
                Assert.All(Session.Equipped, slot => Assert.Null(slot.Item));

                // Packed into a small grid, as large as they need and no larger - ten wide at the most: every item
                // in it, none over another.
                var grid = Session.CorpseGrid!;
                Assert.InRange(grid.Columns, 1, CharacterSession.CorpseColumns);
                Assert.Equal(12, grid.Items.Count);
                var taken = new HashSet<(int, int)>();
                foreach (var tile in grid.Items)
                {
                    Assert.True(tile.CellX >= 0 && tile.CellX + tile.View.GridWidth <= grid.Columns, $"{tile.Name} is out of the grid");
                    Assert.True(tile.CellY >= 0 && tile.CellY + tile.View.GridHeight <= grid.Rows, $"{tile.Name} is under the grid");
                    for (int x = 0; x < tile.View.GridWidth; x++)
                    {
                        for (int y = 0; y < tile.View.GridHeight; y++)
                            Assert.True(taken.Add((tile.CellX + x, tile.CellY + y)), $"{tile.Name} lies over another");
                    }
                }

                Assert.True(grid.Rows <= 6, $"twelve worn items pack into six rows at the most ({grid.Rows})");

                // One of them: chosen as any item is, and its menu begins with the taking of all.
                var first = Page.CorpseSlots[0].Item!;
                Assert.True(Session.IsCorpseItem(first.Item));
                Assert.Equal(ItemMode.Equipped, first.Item.Mode);
                Page.ClickTileCommand.Execute(first);
                Assert.Same(first, Session.SelectedItem);
                Assert.StartsWith("Take all from corpse; ", Page.MenuFor(first).Words());

                // Taken: all twelve back in the slots they were in, the corpse gone from the save.
                int held = Session.ItemIds.Count;
                Page.TakeFromCorpseCommand.Execute(null);
                Assert.Equal("Corpse: 12 items put back on.", Session.Status);
                Assert.False(Session.HasCorpse);
                Assert.False(Page.HasCorpse);
                Assert.Empty(Page.CorpseSlots);
                Assert.Null(Session.CorpseGrid);
                Assert.Equal(12, Session.Equipped.Count(slot => slot.Item is not null));
                Assert.True(Session.ItemIds.Count >= held);

                // Written, and read back off the bytes: no corpse, twelve worn.
                Assert.True(program.Get<AutoSave>().Flush(), "not written");
                var read = D2SLib.Core.ReadD2S(File.ReadAllBytes(program.SaveOf(Dead)));
                Assert.Equal(0, read.PlayerCorpses.Count);
                Assert.Empty(read.PlayerCorpses.Corpses);
                Assert.Equal(12, read.PlayerItemList.Items.Count(item => item.Mode == ItemMode.Equipped));
            });
        }
        finally
        {
            Made.Gone(program, Dead);
        }
    }

    [CorpusFact]
    public void One_way_out_an_item_leaves_the_corpse_as_any_item_moves_and_nothing_goes_onto_it()
    {
        if (!program.Ready(changes: true) || !File.Exists(Specimen))
            return;

        try
        {
            OpenTheDead();
            program.On(() =>
            {
                var grid = Session.CorpseGrid!;
                int carriedBefore = Session.AllGrids.First(each => each.Page == 1).Items.Count;
                var size = (grid.Columns, grid.Rows);
                var cells = grid.Items.ToDictionary(tile => tile.Item.Id, tile => (tile.CellX, tile.CellY));

                // In: refused. Something carried and let go of on the corpse's grid, or on an item lying there,
                // stays in hand; nothing lands.
                Session.PickUp(Session.AllGrids.First(each => each.Page == 1).Items.First());
                Page.DropCarried(grid.Cells[0], pack: false);
                Assert.True(Session.IsHolding);
                Assert.Equal("Nothing goes onto the corpse.", Session.Status);
                Page.DropCarried(null, pack: false, onItem: Page.CorpseSlots[0].Item);
                Assert.True(Session.IsHolding);
                Session.CancelHold();
                Assert.Equal(12, Session.CorpseCount);

                // Out, dragged: lifted off the corpse it is gone from its grid, and set down on a page it is the
                // character's - in its list, on that page.
                var dragged = Page.CorpseSlots.Select(slot => slot.Item!).First(tile => tile.View.GridWidth == 1 && tile.View.GridHeight == 1);
                Session.PickUp(dragged);
                Assert.True(Session.IsHolding);
                Assert.DoesNotContain(Session.CorpseGrid!.Items, tile => ReferenceEquals(tile.Item, dragged.Item));
                var free = program.FreeCell("Inventory");
                Assert.Null(Session.PlaceHeld(1, free.Column, free.Row));
                Assert.False(Session.IsCorpseItem(dragged.Item));
                Assert.Equal(ItemMode.Stored, dragged.Item.Mode);
                Assert.Equal(11, Session.CorpseCount);

                // The grid stays as it was laid - as large, every other item in its cell - while items leave
                // (it shrank as it emptied).
                Assert.Equal(size, (Session.CorpseGrid!.Columns, Session.CorpseGrid.Rows));
                Assert.All(Session.CorpseGrid.Items, tile => Assert.Equal(cells[tile.Item.Id], (tile.CellX, tile.CellY)));
                Assert.Equal(carriedBefore + 1, Session.AllGrids.First(each => each.Page == 1).Items.Count);

                // Out, put on: by the tools' Equip, into the slot that takes it - worn, and off the corpse.
                var armor = Page.CorpseSlots.Select(slot => slot.Item!).First(tile => tile.Item.Location == ItemLocation.Torso);
                var worn = armor.Item;
                Page.ClickTileCommand.Execute(armor);
                Assert.True(Page.CanEquip);
                Page.EquipSelectedCommand.Execute(null);
                Assert.False(Session.IsCorpseItem(worn));
                Assert.Same(worn, Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Torso).Item?.Item);
                Assert.Equal(10, Session.CorpseCount);

                // Out, cut and pasted: on the character, as a cut of any item lands.
                var cut = Page.CorpseSlots[0].Item!.Item;
                Page.ClickTileCommand.Execute(Page.CorpseSlots[0].Item!);
                Assert.True(Page.CanCut);
                Page.CutCommand.Execute(null);
                Page.PasteCommand.Execute(null);
                Assert.False(Session.IsCorpseItem(cut));
                Assert.Equal(ItemMode.Stored, cut.Mode);
                Assert.Equal(9, Session.CorpseCount);

                // Out, deleted: gone, as any item is.
                var doomed = Page.CorpseSlots[0].Item!;
                uint gone = doomed.Item.Id;
                Assert.Null(Session.Delete(doomed));
                Assert.Equal(8, Session.CorpseCount);

                // Still as large with eight left, each where it lay.
                Assert.Equal(size, (Session.CorpseGrid!.Columns, Session.CorpseGrid.Rows));
                Assert.All(Session.CorpseGrid.Items, tile => Assert.Equal(cells[tile.Item.Id], (tile.CellX, tile.CellY)));

                // The rest taken at once: something is worn now, so nothing is put on over it - each to a room.
                Page.TakeFromCorpseCommand.Execute(null);
                Assert.Equal("Corpse: 8 items taken.", Session.Status);
                Assert.False(Session.HasCorpse);
                Assert.Null(Session.CorpseGrid);

                // Written, and read back: no corpse; one worn; nothing in two lists.
                Assert.True(program.Get<AutoSave>().Flush(), "not written");
                var read = D2SLib.Core.ReadD2S(File.ReadAllBytes(program.SaveOf(Dead)));
                Assert.Equal(0, read.PlayerCorpses.Count);
                Assert.Equal(1, read.PlayerItemList.Items.Count(item => item.Mode == ItemMode.Equipped));
                var ids = read.PlayerItemList.Items.Where(item => !item.IsCompact).Select(item => item.Id).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
                Assert.DoesNotContain(gone, ids);
            });
        }
        finally
        {
            Made.Gone(program, Dead);
        }
    }

    [CorpusFact]
    public void The_corpses_grid_takes_the_most_a_character_can_wear_and_is_no_larger_than_what_lies_there()
    {
        // The worst there is: a Barbarian's two sets of two swords, each two by four, with
        // armor, helm, gloves, boots, belt, two rings and an amulet - 55 cells. Tallest first, as the session lays them.
        (int Width, int Height)[] most =
        [
            (2, 4), (2, 4), (2, 4), (2, 4), (2, 3), (2, 2), (2, 2), (2, 2), (2, 1), (1, 1), (1, 1), (1, 1),
        ];
        var (columns, rows, cells) = CharacterSession.Pack(most);
        Assert.Equal(CharacterSession.CorpseColumns, columns);
        Assert.Equal(6, rows);
        Assert.Equal(most.Length, cells.Count);

        var taken = new HashSet<(int, int)>();
        for (int index = 0; index < most.Length; index++)
        {
            Assert.True(cells[index].X + most[index].Width <= columns && cells[index].Y + most[index].Height <= rows);
            for (int x = 0; x < most[index].Width; x++)
            {
                for (int y = 0; y < most[index].Height; y++)
                    Assert.True(taken.Add((cells[index].X + x, cells[index].Y + y)));
            }
        }

        // One armor alone: a grid of its own size.
        Assert.Equal((2, 3), (CharacterSession.Pack([(2, 3)]).Columns, CharacterSession.Pack([(2, 3)]).Rows));

        // And more than any character wears is still laid: the grid only grows downward.
        var heap = Enumerable.Repeat((2, 4), 14).ToArray();
        var tall = CharacterSession.Pack(heap);
        Assert.Equal(CharacterSession.CorpseColumns, tall.Columns);
        Assert.Equal(12, tall.Rows);
    }

    [CorpusFact]
    public void An_item_picked_up_off_the_corpse_as_the_program_closes_is_still_on_the_corpse()
    {
        if (!program.Ready(changes: true) || !File.Exists(Specimen))
            return;

        // The edge of an item picked up and the app closed: an item on the cursor
        // has not left where it was - it is no edit until it is put down - so closing with one in hand writes
        // nothing, and the save opens as it was.
        try
        {
            OpenTheDead();
            byte[] before = File.ReadAllBytes(program.SaveOf(Dead));

            program.On(() =>
            {
                // The last of them in hand: the grid stands, as large and empty of it, and the panel with it.
                var one = Page.CorpseSlots[0].Item!;
                var size = (Session.CorpseGrid!.Columns, Session.CorpseGrid.Rows);
                Session.PickUp(one);
                Assert.True(Session.IsHolding);
                Assert.True(Session.HasCorpse);
                Assert.Equal(size, (Session.CorpseGrid!.Columns, Session.CorpseGrid.Rows));
                Assert.Equal(11, Session.CorpseGrid.Items.Count);

                // Closed with it in hand, as the window's closing does: what waits is written - nothing does.
                Assert.True(program.Get<AutoSave>().Flush());
                Session.Close();
            });

            Assert.Equal(before, File.ReadAllBytes(program.SaveOf(Dead)));

            Made.Open(program, Dead);
            program.On(() =>
            {
                Assert.False(Session.IsHolding);
                Assert.Equal(12, Session.CorpseCount);
                Assert.Equal(12, Session.CorpseGrid!.Items.Count);
            });
        }
        finally
        {
            Made.Gone(program, Dead);
        }
    }
}
