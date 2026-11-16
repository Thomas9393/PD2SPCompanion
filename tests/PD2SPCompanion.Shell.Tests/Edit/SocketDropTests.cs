using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A gem, a rune or a jewel let go of on an item with an empty socket goes into it:
/// a count carried out of the materials tab, one of a
/// stack, a single rune - onto an item in a grid or a worn one - the last rune making the runeword, each said
/// on the strip; and on an item with no socket left the same drop is no socketing at all. In Legit, as the
/// game's own action that costs the rune.
/// <para>
/// What the inventory page decides, asked of it as the view asks at the release that ends a drag (BeginCarry,
/// then DropFromTab, DropCarried or DropOnSlot with the item under the pointer) and read back from the items
/// and the tab's counts. That a drag of the mouse ends in those calls, with the right item under it, is the UI
/// cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SocketDropTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    private static int Slot(string code) => Materials.SlotFor(code)!.Value;

    private int InTab(string code) => Page.Stash.MaterialCount(Slot(code));

    /// <summary>One of a count carried out of the materials tab and let go of on an item.</summary>
    private void DropFromTab(string code, Item onto)
    {
        Assert.True(Page.BeginCarry(Page.Materials.TileFor(Slot(code))!));
        Assert.True(Page.IsTabCarrying);
        Page.DropFromTab(null, full: false, onItem: program.TileOf(onto));
    }

    /// <summary>An item of a grid carried and let go of on an item lying in a grid.</summary>
    private void Drop(ItemTileViewModel carried, Item onto)
    {
        Assert.True(Page.BeginCarry(carried));
        Page.DropCarried(null, pack: false, onItem: program.TileOf(onto));
    }

    [CorpusFact]
    public void Runes_out_of_the_materials_tab_go_into_the_sockets_and_the_last_makes_the_runeword()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Personal Stash", "Materials");
            var sword = program.GivePlain("ssd", 2);
            int tir = InTab("r03"), el = InTab("r01");
            Assert.True(tir > 0 && el > 0, "the pad's tab holds Tir and El");

            DropFromTab("r03", sword);
            Assert.Equal("Tir Rune socketed into Short Sword.", Session.Status);
            Assert.Equal(tir - 1, InTab("r03"));
            Assert.Equal(1, sword.Filled());
            Assert.False(sword.IsRuneword);
            Assert.False(Page.IsTabCarrying);

            DropFromTab("r01", sword);
            Assert.Equal("El Rune socketed into Short Sword: Steel made.", Session.Status);
            Assert.Equal(el - 1, InTab("r01"));
            Assert.Equal(2, sword.Filled());
            Assert.True(sword.IsRuneword);
            Assert.Equal("Steel", program.TileOf(sword).Name);
        });
    }

    [CorpusFact]
    public void On_an_item_with_no_socket_left_the_same_drop_moves_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Personal Stash", "Materials");
            var full = program.Select("Insight", "Personal Stash");
            Assert.Equal(full.TotalNumberOfSockets, full.Filled());
            int el = InTab("r01");
            string said = Session.Status;

            Assert.False(Page.WouldSocket(program.TileOf(full)));
            DropFromTab("r01", full);

            Assert.Equal(el, InTab("r01"));
            Assert.Equal(4, full.Filled());
            Assert.False(Page.IsTabCarrying);
            Assert.Equal(said, Session.Status);
        });
    }

    [CorpusFact]
    public void One_of_a_stack_on_a_shared_page_goes_in_and_the_stack_goes_with_its_last()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Personal Stash", "Shared Page 8");
            var cap = program.GivePlain("cap", 2);

            // Each of the two stacks holds one rune: the stack goes with it.
            Drop(program.Tile("Nef Rune", "Shared Page 8"), cap);
            Assert.Equal("Nef Rune socketed into Cap.", Session.Status);
            Assert.False(program.Holds("Nef Rune", "Shared Page 8"));
            Assert.Equal(1, cap.Filled());

            Drop(program.Tile("Tir Rune", "Shared Page 8"), cap);
            Assert.Equal("Tir Rune socketed into Cap: Nadir made.", Session.Status);
            Assert.True(cap.IsRuneword);
            Assert.Equal(2, cap.Filled());
            Assert.False(Session.IsHolding);
        });
    }

    [CorpusFact]
    public void A_single_rune_from_the_inventory_goes_into_an_item_in_the_stash()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Personal Stash", "Inventory");
            var buckler = program.GivePlain("buc", 1);

            Drop(program.Tile("Hel Rune", "Inventory"), buckler);

            Assert.Equal("Hel Rune socketed into Buckler.", Session.Status);
            Assert.Equal(1, buckler.Filled());
            Assert.False(program.Holds("Hel Rune", "Inventory"));
        });
    }

    [CorpusFact]
    public void One_of_a_stack_of_several_goes_into_a_worn_item_and_the_rest_stays_where_it_was()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            // A full stack out of the tab into the inventory first: Shift held as the count is let go of on a cell.
            program.Panes("Inventory", "Materials");
            Assert.True(Page.BeginCarry(Page.Materials.TileFor(Slot("r01"))!));
            Page.DropFromTab(program.FreeCell("Inventory"), full: true);
            var stack = program.Tile("El Rune", "Inventory");
            int many = Stacks.Shown(stack.Item.Quantity);
            Assert.True(many > 1, $"a stack of several El in the inventory ({many}): {Session.Status}");

            var helm = program.Worn(ItemLocation.Head);
            string name = helm.Item!.Name;
            Assert.Equal(0, helm.Item.Item.Filled());

            Assert.True(Page.BeginCarry(stack));
            Page.DropOnSlot(helm);

            Assert.Equal($"El Rune socketed into {name}.", Session.Status);
            var worn = program.Worn(ItemLocation.Head).Item!.Item;
            Assert.Equal(name, program.Worn(ItemLocation.Head).Item!.Name);
            Assert.Equal(1, worn.Filled());
            Assert.Equal(many - 1, Stacks.Shown(program.Tile("El Rune", "Inventory").Item.Quantity));
            Assert.False(Session.IsHolding);
        });
    }

    /// <summary>
    /// Several selected and carried, every one a filler:
    /// let go of on an item they go in one after another, where "several at once" was no socketing.
    /// </summary>
    [CorpusFact]
    public void Two_runes_selected_and_carried_together_both_go_into_the_item_they_are_let_go_of_on()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Personal Stash", "Shared Page 8");
            var cap = program.GivePlain("cap", 2);
            var nef = program.Tile("Nef Rune", "Shared Page 8");
            var tir = program.Tile("Tir Rune", "Shared Page 8");

            // Picked as Ctrl + click picks, and one of them dragged: the two are carried.
            Page.ClickTileCommand.Execute(nef);
            Vault.VaultHands.CtrlClick(Page, tir);
            Assert.Equal(2, Page.PickedCount);
            Assert.True(Page.BeginCarry(nef));
            Assert.True(Session.IsHolding);
            Assert.Single(Session.HeldGroup);
            Assert.True(Page.WouldSocket(program.TileOf(cap)));

            Page.DropCarried(null, pack: false, onItem: program.TileOf(cap));

            Assert.Equal(2, cap.Filled());
            Assert.False(Session.IsHolding);
            Assert.False(program.Holds("Nef Rune", "Shared Page 8") || program.Holds("Tir Rune", "Shared Page 8"));
            Assert.True(Session.Status is "2 items socketed into Cap." || Session.Status.EndsWith("Nadir made.", StringComparison.Ordinal), Session.Status);
        });
    }

    [CorpusFact]
    public void A_group_with_something_else_in_it_is_carried_as_it_was_and_sockets_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            program.Panes("Personal Stash", "Inventory");
            var cap = program.GivePlain("cap", 2);
            var hel = program.Tile("Hel Rune", "Inventory");
            var charm = program.Tile("Annihilus", "Inventory");

            Page.ClickTileCommand.Execute(hel);
            Vault.VaultHands.CtrlClick(Page, charm);
            if (Page.PickedCount == 2 && Page.BeginCarry(hel) && Session.HeldGroup.Count == 1)
            {
                Assert.False(Page.WouldSocket(program.TileOf(cap)));
                Page.CancelHold();
            }

            Assert.Equal(0, cap.Filled());
            Assert.True(program.Holds("Hel Rune", "Inventory"));
        });
    }

    /// <summary>
    /// The sockets take gems, runes and jewels and nothing else:
    /// a ring let go of on an item with an empty socket, or on that item's Sockets
    /// section, is not socketed - the item does not light for it, nothing goes in, and the ring is back where it
    /// lay.
    /// </summary>
    [CorpusFact]
    public void A_ring_let_go_of_on_an_item_with_an_empty_socket_or_on_its_Sockets_section_is_not_socketed()
    {
        if (!program.Ready(changes: true))
            return;

        using var legit = program.InMode("Legit");
        program.On(() =>
        {
            const string ringName = "Steel Ring of Skill";
            program.Panes("Personal Stash", "Inventory");
            var cap = program.GivePlain("cap", 2);
            var host = program.TileOf(cap);
            int held = program.CountIn("Personal Stash");

            // On the item where it lies.
            Assert.True(Page.BeginCarry(program.Tile(ringName, "Personal Stash")));
            Assert.True(Session.IsHolding);
            Assert.False(Page.WouldSocket(program.TileOf(cap)));
            Page.DropCarried(null, pack: false, onItem: program.TileOf(cap));

            Assert.Equal(0, cap.Filled());
            Assert.False(Session.IsHolding);
            Assert.True(program.Holds(ringName, "Personal Stash"));
            Assert.Equal(held, program.CountIn("Personal Stash"));

            // On the Sockets section of the item the Edit page shows.
            program.Select("Cap", "Personal Stash");
            host = program.TileOf(cap);
            Assert.True(Page.BeginCarry(program.Tile(ringName, "Personal Stash")));
            Assert.False(Page.WouldSocketEdited(host));
            Assert.False(Page.DropOnEdited(host));
            Assert.Equal(0, cap.Filled());
            Page.CancelHold();
            Assert.True(program.Holds(ringName, "Personal Stash"));
            Assert.Equal(held, program.CountIn("Personal Stash"));

            // A charm, the same.
            Assert.True(Page.BeginCarry(program.Tile("Annihilus", "Inventory")));
            if (Session.IsHolding)
            {
                Assert.False(Page.WouldSocket(program.TileOf(cap)));
                Page.CancelHold();
            }

            Assert.Equal(0, cap.Filled());
        });
    }
}
