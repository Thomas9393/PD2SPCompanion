using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// A belt's potions above its first row land when it is lifted or a smaller one goes on (ItemLanding.SpillBelt), and come
/// back where the act then does not happen: the belt put back, or refused. None lost, none held twice, the character's
/// items in the order they stood. Asked as the view asks at the press and the release, of the program as it holds them.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class BeltSpillBackTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private SharedStashSession Stash => program.Get<SharedStashSession>();

    private EquipSlotViewModel Waist => Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Waist && !slot.IsSwap);

    /// <summary>Every item of the character's, this very one, in the order it stands - and its belt potions' boxes.</summary>
    private (List<Item> Items, List<(Item Potion, byte X)> Boxes) Now() =>
        ([.. Session.HeldItems], [.. Session.BeltItems.Select(potion => (potion, potion.X)).OrderBy(box => box.X)]);

    private void AsItWas((List<Item> Items, List<(Item Potion, byte X)> Boxes) before)
    {
        var now = Now();
        Assert.Equal(before.Items.Count, now.Items.Count);
        Assert.True(before.Items.SequenceEqual(now.Items, ReferenceEqualityComparer.Instance), "the items do not stand as they did");
        Assert.Equal(before.Boxes.Select(box => box.X), now.Boxes.Select(box => box.X));
        Assert.True(before.Boxes.Select(box => box.Potion).SequenceEqual(now.Boxes.Select(box => box.Potion), ReferenceEqualityComparer.Instance));
    }

    private List<ItemTileViewModel> OnSharedPage => [.. program.Grid("Shared Page 5").Items];

    [CorpusFact]
    public void A_belt_lifted_and_put_back_has_its_potions_in_their_boxes()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory");
            var before = Now();
            Assert.True(before.Boxes.Count > 3, "the pad's belt holds potions above its first row");

            Assert.True(Page.BeginCarry(Waist.Item!));
            Assert.True(Session.BeltItems.Count < before.Boxes.Count, "the potions did not land");

            Page.CancelHold();

            Assert.False(Session.IsHolding);
            AsItWas(before);
        });
    }

    [CorpusFact]
    public void Put_back_they_come_off_the_shared_page_they_landed_on()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Shared Page 5");
            Assert.Empty(OnSharedPage);
            int stored = Stash.HeldItems.Count();
            var before = Now();

            Assert.True(Page.BeginCarry(Waist.Item!));
            Assert.True(Stash.HeldItems.Count() > stored, "the potions did not land on the shared page");

            Page.CancelHold();

            AsItWas(before);
            Assert.Equal(stored, Stash.HeldItems.Count());
            Assert.Empty(OnSharedPage);
            Assert.False(Stash.HasUnsavedChanges);
        });
    }

    [CorpusFact]
    public void Let_go_of_on_its_own_slot_it_has_gone_nowhere()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory");
            var before = Now();

            Assert.True(Page.BeginCarry(Waist.Item!));
            Page.DropOnSlot(Waist);

            Assert.False(Session.IsHolding);
            Assert.Equal(before.Boxes.Select(box => box.X), Now().Boxes.Select(box => box.X));
            Assert.Equal(before.Items.Count, Now().Items.Count);
            Assert.True(Waist.IsFilled);
        });
    }

    [CorpusFact]
    public void Put_down_elsewhere_they_stay_out_and_none_is_lost_or_doubled()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Shared Page 5");
            int total = Session.HeldItems.Count() + Stash.HeldItems.Count();
            int potions = Session.BeltItems.Count;
            var belt = Waist.Item!.Item;

            Assert.True(Page.BeginCarry(Waist.Item!));
            Page.DropCarried(program.Cell("Shared Page 5", 6, 6), pack: false);

            Assert.False(Session.IsHolding);
            Assert.True(Stash.Holds(belt));
            Assert.True(Session.BeltItems.Count < potions);

            var all = Session.HeldItems.Concat(Stash.HeldItems).ToList();
            Assert.Equal(total, all.Count);
            Assert.Equal(total, all.Distinct<Item>(ReferenceEqualityComparer.Instance).Count());
        });
    }

    [CorpusFact]
    public void A_smaller_belt_refused_for_its_requirements_moves_no_potion()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory");
            var sash = program.Give("sash", "Sash");
            Assert.NotNull(sash);
            Corruptions.AddStat(ItemStats.Home(sash!), "item_levelreq", 99);
            var place = (sash!.Mode, sash.Page, sash.X, sash.Y);
            var worn = Waist.Item!.Item;
            var before = Now();

            string? said = Session.Wear(sash, ItemLocation.Waist, arriving: false, force: false, out bool went);

            Assert.False(went);
            Assert.StartsWith("Require", said);
            Assert.Equal(place, (sash.Mode, sash.Page, sash.X, sash.Y));
            Assert.Same(worn, Waist.Item!.Item);
            AsItWas(before);
        });
    }

    /// <summary>A potion the character refuses on its way out of the belt stays in its box: it is in no list otherwise.</summary>
    [CorpusFact]
    public void A_potion_refused_as_the_belt_spills_stays_in_its_box()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory");
            foreach (var potion in Session.BeltItems)
                potion.Version = "51";
            Session.HeldItems.First(item => !Session.BeltItems.Contains(item) && item.Version is { Length: > 0 }).Version = "50";
            Assert.Equal("50", Session.ItemEra);
            var before = Now();

            Page.BeginCarry(Waist.Item!);
            Page.CancelHold();

            Assert.False(Session.IsHolding);
            AsItWas(before);
        });
    }
}
