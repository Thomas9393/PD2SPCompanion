using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// How a lift ends, on the real files: back as it lay and only once, nothing after Done, out of a file it had landed in
/// first, and in its list's own order however many went. Never lost, never held twice.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ItemLiftTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private SharedStashSession Stash => program.Get<SharedStashSession>();

    private int Times(Item item) =>
        Session.HeldItems.Count(each => ReferenceEquals(each, item)) + Stash.HeldItems.Count(each => ReferenceEquals(each, item));

    private static List<(Item, ItemMode, ItemLocation, byte, byte, byte, uint)> Lay(IEnumerable<Item> list) =>
        [.. list.Select(item => (item, item.Mode, item.Location, item.Page, item.X, item.Y, item.Id))];

    [CorpusFact]
    public void Back_twice_puts_the_item_back_once()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var item = Stash.HeldItems.First();
            var before = Lay(Stash.HeldItems);

            var lift = ItemLift.Of(Stash, item)!;
            Assert.Equal(0, Times(item));

            lift.Back();
            lift.Back();

            Assert.Equal(before, Lay(Stash.HeldItems));
        });
    }

    [CorpusFact]
    public void After_Done_Back_leaves_the_item_where_it_went()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var item = Stash.HeldItems.First();
            var lift = ItemLift.Of(Stash, item)!;
            Assert.Null(lift.Into(Stash, 5, 0, 0, keepId: true));
            lift.Done();
            lift.Back();

            Assert.Equal(1, Times(item));
            Assert.Equal(5, SharedStash.PageOf(item));
        });
    }

    [CorpusFact]
    public void A_file_that_does_not_hold_the_item_lifts_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var item = Stash.HeldItems.First();
            int count = Session.ItemCount;

            Assert.Null(ItemLift.Of(Session, item));
            Assert.Empty(ItemLift.OfAll([((IItemHolder)Session, item)]));

            Assert.Equal(1, Times(item));
            Assert.Equal(count, Session.ItemCount);
        });
    }

    [CorpusFact]
    public void Back_after_landing_in_another_file_takes_it_out_of_there_with_its_own_id()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var item = program.Grid("Personal Stash").Items.First().Item;
            var (before, stashBefore) = (Lay(Session.HeldItems), Lay(Stash.HeldItems));

            // An id the stash holds: taken in, the item is given another, and back it is its own again.
            uint id = item.Id;
            var other = Stash.HeldItems.First();
            uint otherId = other.Id;
            other.Id = id;

            var lift = ItemLift.Of(Session, item)!;
            Assert.Null(lift.Into(Stash, 5, 0, 0));
            Assert.Equal(1, Times(item));
            Assert.NotEqual(id, item.Id);
            Assert.True(Stash.Holds(item));

            lift.Back();
            other.Id = otherId;

            Assert.Equal(before, Lay(Session.HeldItems));
            Assert.Equal(stashBefore, Lay(Stash.HeldItems));
        });
    }

    [CorpusFact]
    public void Several_back_stand_in_their_list_as_they_did_whatever_the_order_they_left_in()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var before = Lay(Stash.HeldItems);
            var items = Stash.HeldItems.Take(3).Reverse().ToList();

            var lifts = ItemLift.OfAll(items.Select(item => ((IItemHolder)Stash, item)));
            Assert.Equal(3, lifts.Count);
            Assert.True(items.SequenceEqual(lifts.Select(lift => lift.Item), ReferenceEqualityComparer.Instance), "not in the order given");

            // Some landed, on the file they came from: they come out of there first.
            Assert.Null(lifts[0].Into(Stash, 5, 0, 0, keepId: true));
            ItemLift.Back(lifts);

            Assert.Equal(before, Lay(Stash.HeldItems));
        });
    }

    /// <summary>A worn item's slot and its list are part of how it lay.</summary>
    [CorpusFact]
    public void A_worn_item_lifted_and_back_is_worn_in_its_slot()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var before = Lay(Session.HeldItems);
            var helm = program.Worn(ItemLocation.Head).Item!.Item;

            var lift = ItemLift.Of(Session, helm)!;
            Assert.False(Session.Holds(helm));
            lift.Back();

            Assert.Equal(before, Lay(Session.HeldItems));
            Assert.Equal(1, Times(helm));
        });
    }
}
