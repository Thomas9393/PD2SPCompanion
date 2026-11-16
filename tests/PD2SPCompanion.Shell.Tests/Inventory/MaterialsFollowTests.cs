using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// The materials tab follows what is carried out of it and into it, and is drawn again only when what it shows
/// changed (not on every edit and move, for counts no move touched): an El Rune dragged
/// out of the tab into the inventory, and the tab says one fewer; that rune then moved a cell in the inventory
/// - nothing the tab shows - and the tab is the same tiles, saying the same, and still answers a click. And
/// several at once back into the tab (it was "Not several at once"): two runes picked
/// and dragged onto the tab are both counted in.
/// <para>
/// What the page decides, asked as the view asks at the press and the release of a drag (BeginCarry,
/// DropFromTab, DropCarried, DropCarriedOnTab) and read back from the tab's counts and tiles and the grids.
/// That a real drag out of the tab finds its cell, and one onto the tab finds the tab, are the UI cases'
/// (units/materials-follow.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class MaterialsFollowTests(Program program)
{
    private const string Stash = "Personal Stash", Rune = "El Rune";

    /// <summary>The El Rune's slot of the tab.</summary>
    private static int El => Materials.SlotFor("r01s") ?? throw new InvalidOperationException("the tab has no El Rune");

    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    private ItemTileViewModel TabRune => Page.Materials.TileFor(El)!;

    private int InTab => Page.Stash.MaterialCount(El);

    /// <summary>One rune carried out of the tab and let go of on a cell, as a drag does it.</summary>
    private void DragOut(string container, int column, int row)
    {
        Assert.True(Page.BeginCarry(TabRune));
        Assert.True(Page.IsTabCarrying);
        Assert.False(Session.IsHolding);

        Page.DropFromTab(program.Cell(container, column, row), full: false);
        Assert.False(Page.IsTabCarrying);
    }

    [CorpusFact]
    public void A_rune_dragged_out_of_the_tab_lands_on_its_cell_and_the_tab_says_one_fewer()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory", "Materials");
            int had = InTab, loose = program.Count("Inventory", Rune);
            Assert.Equal(had, TabRune.Tally);

            // Nothing leaves the tab until it lands.
            Assert.True(Page.BeginCarry(TabRune));
            Assert.Equal(had, InTab);
            Page.DropFromTab(program.Cell("Inventory", 2, 2), full: false);

            Assert.Equal("Moved 1 El Rune to character.", Session.Status);
            Assert.Equal(had - 1, InTab);
            Assert.Equal(had - 1, TabRune.Tally);
            Assert.Equal(loose + 1, program.Count("Inventory", Rune));
            Assert.Equal(Rune, program.At("Inventory", 2, 2)?.Name);
        });
    }

    [CorpusFact]
    public void A_move_the_tab_has_nothing_to_do_with_leaves_its_tiles_as_they_are_and_answering_a_click()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory", "Materials");
            DragOut("Inventory", 2, 2);
            var drawn = TabRune;
            int had = InTab;

            // The loose rune, a cell along.
            Assert.True(Page.BeginCarry(program.At("Inventory", 2, 2)!));
            Page.DropCarried(program.Cell("Inventory", 3, 2), pack: false);
            Assert.Equal(Rune, program.At("Inventory", 3, 2)?.Name);
            Assert.Null(program.At("Inventory", 2, 2));

            Assert.Same(drawn, TabRune);
            Assert.Equal(had, TabRune.Tally);

            Page.ClickTileCommand.Execute(TabRune);
            Assert.Same(drawn, Session.SelectedItem);
            Assert.Equal(El, Session.SelectedItem?.TabSlot);
        });
    }

    [CorpusFact]
    public void Two_runes_picked_and_dragged_onto_the_tab_are_both_counted_in()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            // In the personal stash, which no lock keeps: the two free cells left of the first ring.
            program.Panes(Stash, "Materials");
            DragOut(Stash, 0, 1);
            DragOut(Stash, 0, 2);
            int now = InTab;
            Assert.Equal(2, program.Count(Stash, Rune));

            Page.ClickTileCommand.Execute(program.At(Stash, 0, 1)!);
            Assert.True(Page.BeginDragPick(keepSelected: true));
            Page.SetPicked(program.At(Stash, 0, 2)!, true);
            Page.EndPick();
            Assert.Equal("Store 2 selected items in vault", Page.StoreHint);

            Assert.True(Page.BeginCarry(program.At(Stash, 0, 1)!));
            Assert.Single(Session.HeldGroup);
            Page.DropCarriedOnTab();

            Assert.Equal("Moved 2 materials to materials tab.", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Equal(now + 2, InTab);
            Assert.Equal(now + 2, TabRune.Tally);
            Assert.Equal(0, program.Count(Stash, Rune));
        });
    }
}
