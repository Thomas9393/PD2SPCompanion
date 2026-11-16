using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A stack let go of on another of its kind joins it, as the game stacks one onto another: out of the materials tab
/// one, or with Shift as many as fit; out of a grid as many as fit, the rest back where it came from. Another kind,
/// or a full stack, is not that gesture's. Asked of the inventory page as the view asks at the release that ends a
/// drag, and read back from the items and the tab's counts.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class StackJoinTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    private static int Slot(string code) => Materials.SlotFor(code)!.Value;

    private int InTab(string code) => Page.Stash.MaterialCount(Slot(code));

    private static int Count(ItemTileViewModel tile) => Stacks.Shown(tile.Item.Quantity);

    /// <summary>Every tile of that name in the inventory, in reading order.</summary>
    private List<ItemTileViewModel> Named(string name) => [.. program.Choice("Inventory").Grid!.Items.Where(tile => tile.Name == name)];

    /// <summary>One El Rune out of the tab onto a free cell of the inventory: the stack the cases join onto.</summary>
    private ItemTileViewModel OneEl()
    {
        Assert.True(Page.BeginCarry(Page.Materials.TileFor(Slot("r01"))!));
        Page.DropFromTab(program.FreeCell("Inventory"), full: false);
        return Named("El Rune").Last();
    }

    [CorpusFact]
    public void Out_of_the_tab_one_joins_a_stack_and_with_Shift_as_many_as_fit()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Inventory", "Materials");
            Assert.True(InTab("r01") > 3, "the pad's tab holds El");
            var stack = OneEl();
            int tab = InTab("r01");
            int most = Stacks.MaximumFor(stack.Item.Code)!.Value;

            Assert.True(Page.BeginCarry(Page.Materials.TileFor(Slot("r01"))!));
            Page.DropFromTab(null, full: false, onItem: stack);
            Assert.Equal(2, Count(stack));
            Assert.Equal(tab - 1, InTab("r01"));
            Assert.Equal("El Rune: 2 in the stack.", Session.Status);

            Assert.True(Page.BeginCarry(Page.Materials.TileFor(Slot("r01"))!));
            Page.DropFromTab(null, full: true, onItem: stack);
            int joined = Math.Min(most - 2, tab - 1);
            Assert.Equal(2 + joined, Count(stack));
            Assert.Equal(tab - 1 - joined, InTab("r01"));
        });
    }

    [CorpusFact]
    public void A_stack_carried_onto_another_joins_it_and_where_it_does_not_all_fit_the_rest_goes_back()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Inventory", "Materials");
            var onto = OneEl();
            var carried = OneEl();
            Assert.NotSame(onto, carried);

            Assert.True(Page.BeginCarry(carried));
            Assert.True(Page.WouldStack(onto));
            Page.DropCarried(null, pack: false, onItem: onto);
            Assert.Equal(2, Count(onto));
            Assert.False(Session.IsHolding);
            Assert.Single(Named("El Rune"));

            // Three more onto one short of full: one joins, two go back where they lay.
            int most = Stacks.MaximumFor(onto.Item.Code)!.Value;
            onto.Item.Quantity = (ushort)(most - 1);
            var three = OneEl();
            three.Item.Quantity = 3;
            var (x, y) = (three.Item.X, three.Item.Y);

            Assert.True(Page.BeginCarry(three));
            Page.DropCarried(null, pack: false, onItem: program.TileOf(onto.Item));
            Assert.Equal(most, Stacks.Shown(onto.Item.Quantity));
            Assert.False(Session.IsHolding);
            var back = Named("El Rune").Single(tile => !ReferenceEquals(tile.Item, onto.Item));
            Assert.Equal((2, x, y), (Count(back), back.Item.X, back.Item.Y));
            Assert.Equal($"El Rune: {most} in the stack, 2 put back.", Session.Status);
        });
    }

    [CorpusFact]
    public void Another_kind_does_not_join_and_the_drop_is_what_it_was()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Inventory", "Materials");
            var el = OneEl();
            Assert.True(Page.BeginCarry(Page.Materials.TileFor(Slot("r03"))!));
            Page.DropFromTab(program.FreeCell("Inventory"), full: false);
            var tir = Named("Tir Rune").Last();

            Assert.True(Page.BeginCarry(tir));
            Assert.False(Page.WouldStack(el));
            Page.DropCarried(null, pack: false, onItem: el);
            Assert.Equal(1, Count(el));
            Assert.False(Session.IsHolding);
            Assert.True(program.Holds("Tir Rune", "Inventory"));
        });
    }
}
