using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// A paste over a worn slot, equipped or a mercenary's: as a drop there. What the page
/// decides, asked as the view asks (Copy, the Cut command and PasteOnSlot) and read back from the slots and the grids.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PasteOnSlotTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private static ItemClipboard Clip => ItemClipboard.Shared;

    /// <summary>Something of the inventory's or the personal stash's that a slot of the wearer takes, and that slot.</summary>
    private (ItemTileViewModel Tile, EquipSlotViewModel Slot) Gear(bool mercenary)
    {
        foreach (string page in new[] { "Inventory", "Personal Stash" })
        {
            foreach (var tile in program.Grid(page).Items)
            {
                if (tile.TabSlot is null && Page.SlotAt(tile.Item, mercenary, 0).Slot is { } slot)
                    return (tile, slot);
            }
        }

        throw new InvalidOperationException("nothing here goes on a slot");
    }

    [CorpusTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cut_pasted_on_a_slot_goes_on_it(bool mercenary)
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            try
            {
                var (tile, slot) = Gear(mercenary);
                var item = tile.Item;
                Session.SelectedItem = tile;
                Page.CutCommand.Execute(null);
                Page.PasteOnSlot(slot, mercenary);

                Assert.Same(item, (mercenary ? Session.MercEquipped : Session.Equipped).First(each => each.Slot.Location == slot.Slot.Location).Item?.Item);
                Assert.True(Clip.IsEmpty);
            }
            finally
            {
                Clip.Clear();
            }
        });
    }

    [CorpusFact]
    public void A_copy_pasted_on_a_slot_puts_a_second_on_and_the_first_stays()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            try
            {
                var (tile, slot) = Gear(mercenary: false);
                var original = tile.Item;
                Session.SelectedItem = tile;
                Page.Copy();
                Page.PasteOnSlot(slot, mercenary: false);

                var worn = Session.Equipped.First(each => each.Slot.Location == slot.Slot.Location).Item?.Item;
                Assert.NotNull(worn);
                Assert.NotSame(original, worn);
                Assert.Equal(original.Code, worn.Code);
                Assert.NotEqual(original.Id, worn.Id);
                Assert.Contains(program.Grid("Inventory").Items.Concat(program.Grid("Personal Stash").Items), each => ReferenceEquals(each.Item, original));
                Assert.StartsWith("Copied", Session.Status);
            }
            finally
            {
                Clip.Clear();
            }
        });
    }

    [CorpusFact]
    public void What_a_slot_does_not_take_is_said_and_the_clipboard_kept()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            try
            {
                var (tile, _) = Gear(mercenary: false);
                var other = Session.Equipped.First(slot => Session.CannotWear(tile.Item, slot.Slot.Location) is not null);
                var was = other.Item?.Item;
                Session.SelectedItem = tile;
                Page.CutCommand.Execute(null);
                Page.PasteOnSlot(other, mercenary: false);

                Assert.Same(was, Session.Equipped.First(slot => slot.Slot.Location == other.Slot.Location).Item?.Item);
                Assert.False(Session.IsHolding);
                Assert.False(Clip.IsEmpty);
                Assert.Equal(Session.CannotWear(tile.Item, other.Slot.Location), Session.Status);
            }
            finally
            {
                Clip.Clear();
            }
        });
    }

    [CorpusFact]
    public void A_bare_slots_menu_has_paste()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var menu = Page.MenuFor(Session.Equipped.First(), mercenary: false);
            Assert.Contains(menu.Icons, act => act.Label == "Paste");
        });
    }
}
