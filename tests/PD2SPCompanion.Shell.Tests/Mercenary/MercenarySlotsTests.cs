using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Mercenary;

/// <summary>
/// Every mercenary slot, off and on again by a drag (the mercenary's belt would not come
/// off - "Require belt's upper rows empty", the character's belt rule asked of the mercenary's, and all the mercenary's
/// slots are tested). Each of the six pieces the pad's mercenary wears is carried to the
/// character's stash and back onto the slot it left, the character's own belt holding potions above its bottom
/// row throughout.
/// <para>
/// What the page does with a carry begun on a mercenary's piece and let go of on a cell or a slot, asked as the
/// view asks (BeginCarry, DropCarried, DropOnSlot) and read back from the session and the file. That a drag of
/// the mouse lifts a piece off the mercenary's panel and finds the slot under it again is the UI case's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class MercenarySlotsTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    private GridViewModel Stash =>
        Page.Sections.First(section => section.Pages is not null).Pages!.First(page => page.Label == "Personal Stash").Grid!;

    /// <summary>The first cell of the stash with that much room under and beside it free.</summary>
    private GridCell? Room(int wide, int tall)
    {
        var grid = Stash;
        bool Taken(int column, int row) => grid.Items.Any(tile =>
            column >= tile.CellX && column < tile.CellX + tile.View.GridWidth && row >= tile.CellY && row < tile.CellY + tile.View.GridHeight);

        for (int row = 0; row + tall <= grid.Rows; row++)
        {
            for (int column = 0; column + wide <= grid.Columns; column++)
            {
                if (!Enumerable.Range(0, wide * tall).Any(at => Taken(column + at % wide, row + at / wide)))
                    return new GridCell(grid.Page, column, row);
            }
        }

        return null;
    }

    [CorpusFact]
    public void Each_of_the_six_pieces_comes_off_by_a_drag_into_the_stash_and_goes_back_on_its_slot()
    {
        if (!program.Ready(changes: true))
            return;

        // An empty stash, as pd2spc clear leaves one: what the character wears and its belt stay.
        Pad.SavedInGame(program, save =>
        {
            save.PlayerItemList.Items.RemoveAll(item => item.Mode == ItemMode.Stored);
            save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;
        });
        Assert.True(program.Until(() => Stash.Items.Count == 0), "the emptied stash was not read");

        program.On(() =>
        {
            var worn = Session.MercEquipped.Where(slot => slot.IsFilled).Select(slot => (slot.Slot.Location, slot.Item!.Name)).ToList();
            Assert.Equal(6, worn.Count);

            // More potions than a belt's bottom row holds: the character's belt rule would refuse its own belt.
            Assert.True(Session.BeltItems.Count > 4, "the character's belt holds no potions above its bottom row");

            foreach (var (at, name) in worn)
            {
                var piece = Session.MercEquipped.First(slot => slot.Slot.Location == at).Item!;
                var cell = Room(piece.View.GridWidth, piece.View.GridHeight);
                Assert.True(cell is not null, $"no room in the stash for {name}");

                Assert.True(Page.BeginCarry(piece));
                Assert.True(Session.IsHolding, $"{name} was not lifted: {Session.Status}");
                Page.DropCarried(cell, pack: false);

                Assert.False(Session.IsHolding);
                Assert.Equal(5, Session.MercEquipped.Count(slot => slot.IsFilled));
                var stored = Stash.Items.FirstOrDefault(tile => tile.Name == name);
                Assert.True(stored is not null, $"{name} did not come off: {Session.Status}");

                // On again: back onto the slot it left.
                Assert.True(Page.BeginCarry(stored!));
                Page.DropOnSlot(Session.MercEquipped.First(slot => slot.Slot.Location == at), mercenary: true);

                Assert.Equal($"Equipped {name}.", Session.Status);
                Assert.Equal(name, Session.MercEquipped.First(slot => slot.Slot.Location == at).Item?.Name);
                Assert.DoesNotContain(Stash.Items, tile => tile.Name == name);
            }

            Assert.Equal(6, Pad.File(program, Program.Open).MercenaryItemList.ItemList.Items.Count);
            Assert.True(Pad.ReadsBack(program, Program.Open), "the save does not read back");
        });
    }
}
