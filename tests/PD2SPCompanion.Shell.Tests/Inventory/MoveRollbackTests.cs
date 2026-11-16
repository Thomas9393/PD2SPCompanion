using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Every way a move across files comes back when a file refuses after the item has left: as it lay, once, in its own list
/// and place in it, and with nothing else disturbed. A refusal at that point is the rare one, since every move asks first,
/// so these turn it on. Either the item that leaves is the one that gave a file its era (<see cref="StampFirstOfStash"/>),
/// or the era of the file it is going to turns as it leaves (<see cref="TurnTheStashWhenTheCharacterLetsGo"/>). Asked as
/// the view asks, and read back from the items and their lists.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class MoveRollbackTests(Program program)
{
    private const string Odd = "50";

    private CharacterSession Session => program.Main.Session;
    private SharedStashSession Stash => program.Get<SharedStashSession>();
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    /// <summary>What a list holds and where each lies: the same items, in the same order, on the same cells.</summary>
    private static List<(Item Item, ItemMode Mode, ItemLocation Location, byte Page, byte X, byte Y, uint Id)> Lay(IEnumerable<Item> list) =>
        [.. list.Select(item => (item, item.Mode, item.Location, item.Page, item.X, item.Y, item.Id))];

    private List<(Item Item, ItemMode Mode, ItemLocation Location, byte Page, byte X, byte Y, uint Id)> StashLay() => Lay(Stash.HeldItems);

    private List<(Item Item, ItemMode Mode, ItemLocation Location, byte Page, byte X, byte Y, uint Id)> CharacterLay() => Lay(Session.HeldItems);

    private static void Same<T>(List<T> before, List<T> now)
    {
        Assert.Equal(before.Count, now.Count);
        for (int each = 0; each < before.Count; each++)
            Assert.Equal(before[each], now[each]);
    }

    /// <summary>The stash's first item in a stamp of its own: it passes the asking, and with it gone the stash's era is another, which refuses it.</summary>
    private Item StampFirstOfStash()
    {
        var first = Stash.HeldItems.First();
        first.Version = Odd;
        Assert.Equal(Odd, Stash.ItemEra);
        return first;
    }

    /// <summary>A shared page with nothing on it, other than this one.</summary>
    private static string EmptyPageBeside(int page) => page == 5 ? "Shared Page 6" : "Shared Page 5";

    /// <summary>
    /// The stash's era turned the moment the character has let go of <paramref name="leaving"/>, so everything asked before
    /// was let through and only the taking in refuses. Once.
    /// </summary>
    private void TurnTheStashWhenTheCharacterLetsGo(Item leaving)
    {
        void Turn(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
        {
            if (!Session.Holds(leaving))
            {
                Session.PropertyChanged -= Turn;
                Stash.HeldItems.First().Version = Odd;
            }
        }

        Session.PropertyChanged += Turn;
        _untune = () => Session.PropertyChanged -= Turn;
    }

    /// <summary>The character's era turned once its first item has left: what is put back on it would be refused by any Adopt.</summary>
    private void TurnTheCharacterWhenAnyLeaves()
    {
        int count = Session.ItemCount;

        void Turn(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
        {
            if (Session.ItemCount < count)
            {
                Session.PropertyChanged -= Turn;
                Session.HeldItems.First(item => item.Version is { Length: > 0 }).Version = Odd;
            }
        }

        Session.PropertyChanged += Turn;
        _untune = () => Session.PropertyChanged -= Turn;
    }

    /// <summary>What takes a turn off again, should a test end before it ran.</summary>
    private Action? _untune;

    private void Finally(Action test)
    {
        try
        {
            program.On(test);
        }
        finally
        {
            _untune?.Invoke();
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void A_drop_on_a_cell_refused_after_the_stash_let_go_stays_on_its_page_cell_and_place_in_the_list()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            var first = StampFirstOfStash();
            int page = SharedStash.PageOf(first)!.Value;
            string empty = EmptyPageBeside(page);
            program.Panes($"Shared Page {page}", empty);
            var before = StashLay();

            Assert.True(Page.BeginCarry(Page.Stash.Pages.SelectMany(each => each.Items).First(tile => ReferenceEquals(tile.Item, first))));
            Page.DropCarried(program.FreeCell(empty), pack: false);

            Assert.StartsWith("Require same version", Session.Status);
            Assert.EndsWith("Put back.", Session.Status);
            Assert.False(Session.IsHolding);
            Same(before, StashLay());
            Assert.Empty(program.Grid(empty).Items);
        });
    }

    [CorpusFact]
    public void A_worn_item_dropped_on_a_shared_cell_and_refused_after_it_left_is_worn_again_as_it_stood()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            program.Panes("Inventory", "Shared Page 5");
            var helm = program.Worn(ItemLocation.Head).Item!;
            var (item, before, stashBefore) = (helm.Item, CharacterLay(), StashLay());
            Assert.True(Page.BeginCarry(helm));

            TurnTheStashWhenTheCharacterLetsGo(item);
            Page.DropCarried(program.Cell("Shared Page 5", 0, 0), pack: false);

            Assert.StartsWith("Require same version", Session.Status);
            Assert.False(Session.IsHolding);
            Same(before, CharacterLay());
            Same(stashBefore, StashLay());
            Assert.Same(item, program.Worn(ItemLocation.Head).Item?.Item);
        });
    }

    [CorpusFact]
    public void A_send_across_refused_after_the_stash_let_go_stays_as_it_lay()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            var first = StampFirstOfStash();
            int page = SharedStash.PageOf(first)!.Value;
            string empty = EmptyPageBeside(page);
            program.Panes($"Shared Page {page}", empty);
            var before = StashLay();

            Page.SendAcross(Page.Stash.Pages.SelectMany(each => each.Items).First(tile => ReferenceEquals(tile.Item, first)));

            Assert.StartsWith("Require same version", Session.Status);
            Same(before, StashLay());
            Assert.Empty(program.Grid(empty).Items);
        });
    }

    [CorpusFact]
    public void A_send_across_from_the_character_refused_after_it_left_stays_as_it_lay()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            program.Panes("Personal Stash", "Shared Page 5");
            var tile = program.Grid("Personal Stash").Items.First();
            var (before, stashBefore) = (CharacterLay(), StashLay());

            TurnTheStashWhenTheCharacterLetsGo(tile.Item);
            Page.SendAcross(tile);

            Assert.StartsWith("Require same version", Session.Status);
            Same(before, CharacterLay());
            Same(stashBefore, StashLay());
        });
    }

    [CorpusFact]
    public void A_worn_item_taken_off_and_refused_by_the_shared_page_after_it_left_is_worn_again()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            program.Panes("Shared Page 5");
            var item = program.Worn(ItemLocation.Head).Item!.Item;
            var (before, stashBefore) = (CharacterLay(), StashLay());

            TurnTheStashWhenTheCharacterLetsGo(item);
            string? why = program.Get<ItemLanding>().Move(item, out var landed);

            Assert.StartsWith("Require same version", why);
            Assert.Null(landed);
            Same(before, CharacterLay());
            Same(stashBefore, StashLay());
        });
    }

    [CorpusFact]
    public void An_item_refused_on_a_slot_is_back_in_its_place_in_the_stashs_list()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            var ring = program.Tile("Steel Ring of Skill", "Personal Stash").Item;
            program.Panes("Personal Stash", "Shared Page 2");
            Page.SendAcross(program.TileOf(ring));
            var slot = Page.SlotAt(ring, false, 0).Slot!;
            var worn = slot.Item?.Item;

            // Moved to the front of the list by a stash whose order says so: its place is what is looked at.
            Session.HeldItems.First(item => item.Version is { Length: > 0 }).Version = Odd;
            var before = StashLay();

            Assert.True(Page.BeginCarry(Page.Stash.Pages.SelectMany(each => each.Items).First(tile => ReferenceEquals(tile.Item, ring))));
            Page.DropOnSlot(slot);

            Assert.StartsWith("Require same version", Session.Status);
            Same(before, StashLay());
            Assert.False(Session.Holds(ring));
            Assert.Same(worn, Session.Equipped.First(each => each.Slot.Location == slot.Slot.Location && !each.IsSwap).Item?.Item);
        });
    }

    /// <summary>One of a group gone from the stash while carried: the file cannot let go of it, and the one it did goes back, for all of them or none.</summary>
    [CorpusFact]
    public void A_belt_drop_one_of_whose_group_the_stash_cannot_let_go_of_moves_none()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            program.Panes("Inventory", "Shared Page 3");
            foreach (var tile in Session.BeltGrid!.Items.ToList())
                Assert.Null(Session.Delete(tile));

            var (one, two) = (program.GivePlain("hp1", 0), program.GivePlain("hp1", 0));
            Page.SendAcross(program.TileOf(one));
            Page.SendAcross(program.TileOf(two));

            Session.PickUpGroup(TileOnStash(one), true, [new CarriedItem(TileOnStash(two), true, 1, 0)]);
            Assert.NotNull(Stash.Release(two));
            var before = StashLay();

            Page.DropCarried(Session.BeltGrid!.Cells.First(cell => cell.Column == 0), pack: false);

            Assert.Equal("Item not moved. Put back.", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Empty(Session.BeltItems);
            Same(before, StashLay());
        });
    }

    private ItemTileViewModel TileOnStash(Item item) =>
        Page.Stash.Pages.SelectMany(each => each.Items).First(tile => ReferenceEquals(tile.Item, item));

    /// <summary>
    /// One taken, then the stash's era turned so that the next is refused: "Moved 1 of 2", that one back on its page and
    /// cell, at the place in the list it held before either left.
    /// </summary>
    [CorpusFact]
    public void A_group_of_which_one_is_refused_after_it_left_lands_the_rest_and_puts_that_one_back()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            var first = Stash.HeldItems.First();
            var second = Stash.HeldItems.Skip(1).First(item => SharedStash.PageOf(item) == SharedStash.PageOf(first));
            int page = SharedStash.PageOf(first)!.Value;
            string empty = EmptyPageBeside(page);
            program.Panes($"Shared Page {page}", empty);

            int count = Stash.HeldItems.Count();
            var lay = new[] { first, second }.ToDictionary(item => item, item => (item.X, item.Y, Stash.OriginOf(item)!.Value.Index));

            bool bothOut = false;
            void Turn(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
            {
                int now = Stash.HeldItems.Count();
                if (now == count - 2)
                {
                    bothOut = true;
                }
                else if (bothOut && now == count - 1)
                {
                    Stash.PropertyChanged -= Turn;
                    Stash.HeldItems.First(item => item.Version is { Length: > 0 }).Version = Odd;
                }
            }

            Session.PickUpGroup(TileOnStash(first), true, [new CarriedItem(TileOnStash(second), true, 1, 0)]);
            Stash.PropertyChanged += Turn;
            _untune = () => Stash.PropertyChanged -= Turn;
            Page.DropCarried(program.FreeCell(empty), pack: true);

            Assert.StartsWith("Moved 1 of 2", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Equal(count, Stash.HeldItems.Count());

            var landed = Assert.Single(new[] { first, second }, item => SharedStash.PageOf(item) != page);
            var back = ReferenceEquals(landed, first) ? second : first;
            Assert.Equal(empty, $"Shared Page {SharedStash.PageOf(landed)}");
            Assert.Equal(1, Stash.HeldItems.Count(item => ReferenceEquals(item, back)));
            Assert.Equal(1, Stash.HeldItems.Count(item => ReferenceEquals(item, landed)));
            Assert.Equal(page, SharedStash.PageOf(back));
            Assert.Equal(lay[back], (back.X, back.Y, Stash.OriginOf(back)!.Value.Index));
        });
    }

    /// <summary>One of a group gone from the stash while carried: the one it can let go of lands, and nothing is doubled.</summary>
    [CorpusFact]
    public void A_group_one_of_which_the_stash_cannot_let_go_of_lands_the_other_and_doubles_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            var first = Stash.HeldItems.First();
            var second = Stash.HeldItems.Skip(1).First(item => SharedStash.PageOf(item) == SharedStash.PageOf(first));
            string empty = EmptyPageBeside(SharedStash.PageOf(first)!.Value);
            program.Panes($"Shared Page {SharedStash.PageOf(first)}", empty);

            Session.PickUpGroup(TileOnStash(first), true, [new CarriedItem(TileOnStash(second), true, 1, 0)]);
            Assert.NotNull(Stash.Release(second));
            int count = Stash.HeldItems.Count();

            Page.DropCarried(program.FreeCell(empty), pack: true);

            Assert.False(Session.IsHolding);
            Assert.Equal(count, Stash.HeldItems.Count());
            Assert.Equal(1, Stash.HeldItems.Count(item => ReferenceEquals(item, first)));
            Assert.DoesNotContain(Stash.HeldItems, item => ReferenceEquals(item, second));
            Assert.Equal(empty, $"Shared Page {SharedStash.PageOf(first)}");
        });
    }

    /// <summary>The sort's way back on the character's own page, its era turned once the first item has left: every item in its list and cell.</summary>
    [CorpusFact]
    public void A_sort_of_a_character_page_refused_leaves_every_item_as_it_lay()
    {
        if (!program.Ready(changes: true))
            return;

        var order = program.On(() => (Page.FirstOrder.By, Page.FirstOrder.Descending));
        try
        {
            program.On(() =>
            {
                program.Panes("Personal Stash");
                var before = CharacterLay();
                TurnTheCharacterWhenAnyLeaves();

                Page.FirstOrder.Pick(Page.FirstOrder.Options.First(option => option.Label == "Type"));

                Assert.EndsWith("Nothing moved.", Session.Status);
                var now = CharacterLay();
                Assert.Equal(before.Count, now.Count);
                Assert.True(before.Select(each => each.Item).SequenceEqual(now.Select(each => each.Item), ReferenceEqualityComparer.Instance), "the items do not stand as they did");
                Assert.Equal(before.Select(each => (each.Mode, each.Page, each.X, each.Y)), now.Select(each => (each.Mode, each.Page, each.X, each.Y)));
            });
        }
        finally
        {
            _untune?.Invoke();
            program.On(() =>
            {
                Page.FirstOrder.By = order.By;
                Page.FirstOrder.Descending = order.Descending;
            });
            program.PanesAsFound();
        }
    }

    /// <summary>A swap the file refuses part way, the way back asked nothing that can refuse: every item on its own page and cell, none doubled.</summary>
    [CorpusFact]
    public void A_swap_refused_after_both_sides_left_puts_every_item_back_on_its_page_and_cell()
    {
        if (!program.Ready(changes: true))
            return;

        Finally(() =>
        {
            program.Panes("Personal Stash", "Shared Page 5");
            var here = program.Grid("Personal Stash").Items.Select(tile => tile.Item).ToList();
            var cells = here.Select(item => (item, item.X, item.Y, item.Page)).ToList();
            var (count, stashCount) = (Session.ItemCount, Stash.HeldItems.Count());

            TurnTheStashWhenTheCharacterLetsGo(here[0]);
            Page.SwapPanesCommand.Execute(program.Section());

            Assert.EndsWith("Nothing was swapped.", Session.Status);
            Assert.Equal(count, Session.ItemCount);
            Assert.Equal(stashCount, Stash.HeldItems.Count());
            foreach (var (item, x, y, page) in cells)
            {
                Assert.True(Session.Holds(item), "an item of the page is in no list");
                Assert.False(Stash.Holds(item), "an item of the page is held twice");
                Assert.Equal((x, y, page), (item.X, item.Y, item.Page));
            }

            Assert.Empty(program.Grid("Shared Page 5").Items);
        });
    }
}
