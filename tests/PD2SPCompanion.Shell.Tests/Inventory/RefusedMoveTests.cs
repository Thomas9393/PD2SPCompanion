using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// A move a file refuses leaves the item where it lay: on its own shared page, not on the page every shared item's
/// Page field names; in its own slot and list, not on page 0 or in no list at all.
/// The refusals are the two a file gives - a stamp that does not mix with what it holds, and a file read as last season's.
/// Asked as the view asks at the press and the release, and read back from the items and the slots.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class RefusedMoveTests(Program program)
{
    private const string Ring = "Steel Ring of Skill", Odd = "50";

    private CharacterSession Session => program.Main.Session;
    private SharedStashSession Stash => program.Get<SharedStashSession>();
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    private string StashFile => Path.Combine(program.Folder, "saves", SharedStashSession.SoftcoreFile);

    /// <summary>The character written, for the test, in a stamp nothing else here mixes with: whatever arrives is refused.</summary>
    private void StampTheCharacter()
    {
        var first = Session.HeldItems.First(item => item.Version is { Length: > 0 });
        first.Version = Odd;
        Assert.Equal(Odd, Session.ItemEra);
    }

    /// <summary>The ring moved onto a shared page, for a test to lift it from there.</summary>
    private Item RingOnSharedPage(int page)
    {
        program.Panes("Personal Stash", $"Shared Page {page}");
        var tile = program.Tile(Ring, "Personal Stash");
        var item = tile.Item;
        Page.SendAcross(tile);
        Assert.Equal(page, SharedStash.PageOf(item));
        return item;
    }

    /// <summary>The stash as last season's game wrote it, noticed as a file the game saved (LastSeasonGuardTests): it takes nothing from elsewhere.</summary>
    private void StashReadAsLastSeason()
    {
        byte[] before = SharedStash.Read(StashFile).Write(lastSeason: true);
        Session.UseMyLayout();
        File.WriteAllBytes(StashFile, before);
        program.Main.Sync!.Check();
        Assert.True(Stash.IsLastSeason, "the stash did not open as last season's");
    }

    /// <summary>A test with this season's stamp unknown, as on a new season's first launch; the stamps put back after.</summary>
    private void WithStampsUnknown(Action test)
    {
        var stamps = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps(null, null);
        try
        {
            test();
        }
        finally
        {
            SeasonReading.SetStamps(stamps.CurrentStamp, stamps.PreviousStamp);
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void An_item_refused_on_a_slot_stays_on_its_own_shared_page_and_cell()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                var ring = RingOnSharedPage(2);
                var (x, y, id) = (ring.X, ring.Y, ring.Id);
                var slot = Page.SlotAt(ring, false, 0).Slot!;
                StampTheCharacter();

                Assert.True(Page.BeginCarry(Page.Stash.Pages.SelectMany(each => each.Items).First(each => ReferenceEquals(each.Item, ring))));
                Page.DropOnSlot(slot);

                Assert.StartsWith("Require same version", Session.Status);
                Assert.True(Stash.Holds(ring), "the ring is in no list");
                Assert.Equal(2, SharedStash.PageOf(ring));
                Assert.Equal((x, y, id), (ring.X, ring.Y, ring.Id));
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void An_item_refused_by_the_belt_stays_on_its_own_shared_page()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                foreach (var tile in Session.BeltGrid!.Items.ToList())
                    Assert.Null(Session.Delete(tile));

                program.Panes("Personal Stash", "Shared Page 3");
                var potion = program.GivePlain("hp1", 0);
                Page.SendAcross(program.TileOf(potion));
                Assert.Equal(3, SharedStash.PageOf(potion));
                StampTheCharacter();

                Assert.True(Page.BeginCarry(program.TileOf(potion)));
                Page.DropCarried(Session.BeltGrid!.Cells.First(cell => cell.Column == 0), pack: false);

                Assert.StartsWith("Require same version", Session.Status);
                Assert.True(Stash.Holds(potion), "the potion is in no list");
                Assert.Equal(3, SharedStash.PageOf(potion));
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void Worn_items_in_a_group_the_shared_stash_refuses_stay_on_and_nothing_leaves()
    {
        if (!program.Ready(changes: true))
            return;

        WithStampsUnknown(() => program.On(() =>
        {
            StashReadAsLastSeason();
            program.Panes("Shared Page 5");

            var helm = program.Worn(ItemLocation.Head).Item!;
            var armor = program.Worn(ItemLocation.Torso).Item!;
            var (one, two) = (helm.Item, armor.Item);
            int held = Session.ItemCount;

            Page.ClickTileCommand.Execute(helm);
            Assert.True(Page.BeginDragPick(keepSelected: true));
            Page.SetPicked(armor, true);
            Page.EndPick();
            Assert.True(Page.BeginCarry(helm));
            Assert.Single(Session.HeldGroup);

            Page.DropCarried(program.Cell("Shared Page 5", 0, 0), pack: false);

            Assert.Contains("read as last season's", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Equal(held, Session.ItemCount);
            Assert.Same(one, program.Worn(ItemLocation.Head).Item?.Item);
            Assert.Same(two, program.Worn(ItemLocation.Torso).Item?.Item);
            Assert.Empty(program.Grid("Shared Page 5").Items);
        }));
    }

    [CorpusFact]
    public void A_group_refused_after_it_left_goes_back_each_to_its_own_slot_page_and_place_in_the_list()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                // Two on the shared stash for the era to fall to, once the first of them has left.
                var other = RingOnSharedPage(2);
                program.Panes("Shared Page 5");
                var firstOfStash = Page.Stash.Pages.SelectMany(each => each.Items).First(tile => ReferenceEquals(tile.Item, Stash.HeldItems.First()));
                Assert.NotSame(other, firstOfStash.Item);
                int page = SharedStash.PageOf(firstOfStash.Item)!.Value;
                var lay = (firstOfStash.Item.X, firstOfStash.Item.Y);

                // Worn and the stash's first in a stamp of their own: both pass the plan, which asks of the stash as it
                // stands; with that first item let go, the stash's era is another, and the file refuses them.
                var helm = program.Worn(ItemLocation.Head).Item!;
                var item = helm.Item;
                var mode = (item.Mode, item.Location);
                int held = Session.ItemCount;
                item.Version = Odd;
                firstOfStash.Item.Version = Odd;
                Assert.Equal(Odd, Stash.ItemEra);

                var lying = firstOfStash.Item;
                Session.PickUpGroup(helm, false, [new CarriedItem(firstOfStash, true, 0, 0)]);
                Page.DropCarried(program.FreeCell("Shared Page 5"), pack: true);

                Assert.StartsWith("Moved 0 of 2", Session.Status);
                Assert.Same(item, program.Worn(ItemLocation.Head).Item?.Item);
                Assert.Equal(mode, (item.Mode, item.Location));
                Assert.Equal(held, Session.ItemCount);
                Assert.True(Stash.Holds(lying), "the stash's item is in no list");
                Assert.Equal(page, SharedStash.PageOf(lying));
                Assert.Equal(lay, (lying.X, lying.Y));
                Assert.Same(lying, Stash.HeldItems.First());
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }

    [CorpusTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_worn_item_the_shared_stash_refuses_stays_worn_and_no_stray_is_made(bool mercenary)
    {
        if (!program.Ready(changes: true))
            return;

        WithStampsUnknown(() => program.On(() =>
        {
            StashReadAsLastSeason();
            program.Panes("Shared Page 5");

            var slot = (mercenary ? Session.MercEquipped : Session.Equipped).First(each => each.Item is not null && !each.IsSwap);
            var item = slot.Item!.Item;
            var was = (item.Mode, item.Location, item.Page);

            Assert.True(Page.BeginCarry(slot.Item!));
            Page.DropCarried(program.Cell("Shared Page 5", 0, 0), pack: false);

            Assert.Contains("read as last season's", Session.Status);
            Assert.False(Session.IsHolding);
            Assert.Equal(was, (item.Mode, item.Location, item.Page));
            Assert.Equal(mercenary, Session.IsMercenaryItem(item));
            Assert.Empty(Session.UnknownPages);
            Assert.Empty(program.Grid("Shared Page 5").Items);
        }));
    }

    /// <summary>A Forge item is from another file: asked of the character as the shared stash asks it.</summary>
    [CorpusFact]
    public void A_forge_item_the_character_refuses_stays_out()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                program.Panes("Personal Stash");
                StampTheCharacter();
                var made = PD2SPCompanion.Core.Items.ItemForge.BaseAs("rin", ItemQuality.Normal, "105", new Random(5)).Item;

                string? why = program.Get<ItemLanding>().Receive(made, "No room.", out var landed);

                Assert.StartsWith("Require same version", why);
                Assert.Null(landed);
                Assert.False(Session.Holds(made));
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }
}
