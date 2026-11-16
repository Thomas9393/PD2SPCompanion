using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// What comes into the belt besides a drag of the character's own: a potion dragged out of the
/// materials tab, and a paste over it (a potion or a scroll of
/// TP or ID) - each into the column it is let go of or pasted on, and what a
/// belt does not take refused with the clipboard kept.
/// <para>
/// What the page decides, asked as the view asks (BeginCarry and DropFromTab, Copy and PasteOn) and read back from
/// the belt's items and the tab's counts.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class BeltArrivalsTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private static ItemClipboard Clip => ItemClipboard.Shared;

    /// <summary>A belt column with a free box: the cell of it in the belt's grid, and how many it holds.</summary>
    private (GridCell Cell, int Held) FreeColumn()
    {
        int rows = Session.BeltRows;
        var held = Session.BeltItems.GroupBy(item => item.X % PD2SPCompanion.Core.Characters.Belts.Columns)
                                    .ToDictionary(column => column.Key, column => column.Count());
        int free = Enumerable.Range(0, PD2SPCompanion.Core.Characters.Belts.Columns).First(column => held.GetValueOrDefault(column) < rows);
        return (Session.BeltGrid!.Cells.First(cell => cell.Column == free), held.GetValueOrDefault(free));
    }

    private int InColumn(int column) =>
        Session.BeltItems.Count(item => item.X % PD2SPCompanion.Core.Characters.Belts.Columns == column);

    [CorpusFact]
    public void A_rejuvenation_dragged_out_of_the_materials_tab_goes_into_the_belt_column_it_is_let_go_of_on()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory", "Materials");
            int slot = Materials.SlotFor("rvl") ?? throw new InvalidOperationException("the tab has no rejuvenation");
            if (Page.Stash.MaterialCount(slot) == 0)
                Page.Stash.AddMaterial(slot, 2);
            int had = Page.Stash.MaterialCount(slot), belt = Session.BeltItems.Count;
            var (cell, held) = FreeColumn();

            // Shift or not, a belt box holds one.
            Assert.True(Page.BeginCarry(Page.Materials.TileFor(slot)!));
            Page.DropFromTab(cell, full: true);

            Assert.Equal($"Moved 1 {Materials.NameOf(slot)} to the belt.", Session.Status);
            Assert.Equal(had - 1, Page.Stash.MaterialCount(slot));
            Assert.Equal(belt + 1, Session.BeltItems.Count);
            Assert.Equal(held + 1, InColumn(cell.Column));
        });
    }

    [CorpusFact]
    public void A_belt_potion_copied_is_pasted_into_the_column_under_the_pointer_and_a_ring_is_refused()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            try
            {
                int belt = Session.BeltItems.Count;
                var (cell, held) = FreeColumn();
                var potion = Session.BeltGrid!.Items.First();

                Session.SelectedItem = potion;
                Page.Copy();
                Page.PasteOn(null, cell);

                Assert.Equal($"Copied {potion.Name} to the belt.", Session.Status);
                Assert.Equal(belt + 1, Session.BeltItems.Count);
                Assert.Equal(held + 1, InColumn(cell.Column));

                // Not a potion nor a scroll: said, nothing in, the clipboard kept.
                var ring = program.Grid("Inventory").Items.First(tile => !PD2SPCompanion.Core.Characters.Belts.Takes(tile.Item));
                Session.SelectedItem = ring;
                Page.Copy();
                Page.PasteOn(null, cell);

                Assert.Equal($"Belt takes potions and scrolls, not {ring.Name}.", Session.Status);
                Assert.Equal(belt + 1, Session.BeltItems.Count);
                Assert.False(Clip.IsEmpty);
            }
            finally
            {
                Clip.Clear();
            }
        });
    }
}
