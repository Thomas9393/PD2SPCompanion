using D2SLib.Model.Save;

using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Every way into a character asks one question (<see cref="CharacterSession.Refuses(Item)"/>), so all of them agree: a cell
/// (Adopt), a copy (Receive), a worn slot (Wear) and the belt (IntoBelt). The refusals are the two a file gives, a stamp that does not mix
/// with what it holds and a file read as last season's. An item refused ends where it was, once: on its own shared page
/// and cell, its tab count untouched, a copy's original where it lay.
/// <para>
/// The character is the pad's own, written again in last season's encoding as the command line tool's <c>lastseason</c>
/// writes it, and read as a file the game saved. Asked as the view asks at the press, the hover and the release.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ArrivalRuleTests(Program program)
{
    private const string Odd = "50", Potion = "hp1", LastSeason = "read as last season's";

    private CharacterSession Session => program.Main.Session;
    private SharedStashSession Stash => program.Get<SharedStashSession>();
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private static ItemClipboard Clip => ItemClipboard.Shared;

    private GridCell Column(int column) => Session.BeltGrid!.Cells.First(cell => cell.Column == column);

    private void EmptyTheBelt()
    {
        foreach (var tile in Session.BeltGrid!.Items.ToList())
            Assert.Null(Session.Delete(tile));
    }

    /// <summary>A potion given to the character, sent across to the shared page the second pane shows.</summary>
    private Item PotionOnSharedPage()
    {
        var potion = program.GivePlain(Potion, 0);
        Page.SendAcross(program.TileOf(potion));
        Assert.True(Stash.Holds(potion), "the potion did not reach the shared page");
        return potion;
    }

    /// <summary>The character's file in last season's encoding, read again as a file the game saved: it takes nothing from elsewhere.</summary>
    private void CharacterReadAsLastSeason(string first, string? second = null)
    {
        Assert.True(program.Saved(), "the character was not saved");
        string path = program.SaveOf(Program.Open);
        var (save, _) = SaveVerifier.Load(path);
        save.Layout = new D2SLib.ParserLayout(true, 10, true);
        byte[] bytes = D2SLib.Model.Save.D2S.Write(save);
        Session.UseMyLayout();
        File.WriteAllBytes(path, bytes);
        program.Main.Sync!.Check();
        Assert.True(Session.IsLastSeason, "the character did not open as last season's");

        // A load builds the panes and the files' items again.
        program.Panes(first, second);
    }

    /// <summary>The item the shared stash holds under this id, as it stands since a load.</summary>
    private Item OnTheStash(uint id) => Stash.HeldItems.First(each => each.Id == id);

    /// <summary>A test with this season's stamp unknown, as on a new season's first launch; the stamps and the panes as they were after.</summary>
    private void WithStampsUnknown(Action test)
    {
        var stamps = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps(null, null);
        try
        {
            program.On(test);
        }
        finally
        {
            SeasonReading.SetStamps(stamps.CurrentStamp, stamps.PreviousStamp);
            program.PanesAsFound();
        }
    }

    /// <summary>An item on the shared stash as it was, exactly once, and none of the character's.</summary>
    private void StillOnItsPage(Item item, int page, (byte, byte) cell, int index)
    {
        Assert.Equal(1, Stash.HeldItems.Count(each => ReferenceEquals(each, item)));
        Assert.False(Session.Holds(item), "the character holds it too");
        Assert.Equal(page, SharedStash.PageOf(item));
        Assert.Equal(cell, (item.X, item.Y));
        Assert.Equal(index, Stash.OriginOf(item)!.Value.Index);
        Assert.False(Session.IsHolding);
    }

    private (int Page, (byte, byte) Cell, int Index) WhereIs(Item item) =>
        (SharedStash.PageOf(item)!.Value, (item.X, item.Y), Stash.OriginOf(item)!.Value.Index);

    [CorpusFact]
    public void The_belt_of_a_last_season_character_refuses_an_arrival_and_none_of_the_lot_goes_in()
    {
        if (!program.Ready(changes: true))
            return;

        WithStampsUnknown(() =>
        {
            program.Panes("Inventory");
            EmptyTheBelt();
            var mine = program.GivePlain(Potion, 0);
            CharacterReadAsLastSeason("Inventory");

            var own = Session.HeldItems.First(item => item.Code == mine.Code && item.Mode == ItemMode.Stored);
            var at = (own.Mode, own.X, own.Y);
            var arriving = ItemForge.PlainBase(Potion, 0, Session.ItemEra!).Item;
            int count = Session.ItemCount;

            string? said = Session.IntoBelt([(own, 0, false), (arriving, 1, true)]);

            Assert.Contains(LastSeason, said);
            Assert.Equal(said, Session.Status);
            Assert.False(Session.Holds(arriving), "the arrival went in");
            Assert.Equal(count, Session.ItemCount);
            Assert.Equal(at, (own.Mode, own.X, own.Y));
            Assert.Empty(Session.BeltItems);
        });
    }

    [CorpusFact]
    public void A_potion_dragged_from_a_shared_page_to_a_last_season_belt_stays_on_its_page_and_the_column_lights_red()
    {
        if (!program.Ready(changes: true))
            return;

        WithStampsUnknown(() =>
        {
            program.Panes("Inventory", "Shared Page 3");
            EmptyTheBelt();
            var potion = PotionOnSharedPage();
            var (page, cell, index) = WhereIs(potion);
            CharacterReadAsLastSeason("Inventory", "Shared Page 3");
            potion = OnTheStash(potion.Id);
            Page.ShowBelt = true;

            Assert.True(Page.BeginCarry(program.TileOf(potion)));
            var over = Column(1);
            Page.HoverOver(over);
            Assert.True(over.IsTarget);
            Assert.False(over.IsAllowed);
            Page.HoverOver(null);

            Page.DropCarried(Column(1), pack: false);

            Assert.Contains(LastSeason, Session.Status);
            Assert.EndsWith("Put back.", Session.Status);
            Assert.Empty(Session.BeltItems);
            StillOnItsPage(potion, page, cell, index);
            Page.ShowBelt = false;
        });
    }

    [CorpusFact]
    public void Several_potions_from_a_shared_page_all_stay_where_they_are_at_a_last_season_belt()
    {
        if (!program.Ready(changes: true))
            return;

        WithStampsUnknown(() =>
        {
            program.Panes("Inventory", "Shared Page 3");
            EmptyTheBelt();
            var (one, two) = (PotionOnSharedPage(), PotionOnSharedPage());
            var (firstAt, secondAt) = (WhereIs(one), WhereIs(two));
            CharacterReadAsLastSeason("Inventory", "Shared Page 3");
            (one, two) = (OnTheStash(one.Id), OnTheStash(two.Id));

            Session.PickUpGroup(program.TileOf(one), true, [new CarriedItem(program.TileOf(two), true, 1, 0)]);
            Page.DropCarried(Column(0), pack: false);

            Assert.Contains(LastSeason, Session.Status);
            Assert.Empty(Session.BeltItems);
            StillOnItsPage(one, firstAt.Page, firstAt.Cell, firstAt.Index);
            StillOnItsPage(two, secondAt.Page, secondAt.Cell, secondAt.Index);
        });
    }

    /// <summary>The control: what the file held when read is no arrival, wherever it has been since.</summary>
    [CorpusFact]
    public void A_last_season_characters_own_potion_comes_back_from_a_shared_page_into_its_belt()
    {
        if (!program.Ready(changes: true))
            return;

        WithStampsUnknown(() =>
        {
            program.Panes("Inventory", "Shared Page 3");
            EmptyTheBelt();
            program.GivePlain(Potion, 0);
            CharacterReadAsLastSeason("Inventory", "Shared Page 3");

            var own = Session.HeldItems.First(item => item.Code.Trim() == Potion && item.Mode == ItemMode.Stored);
            Page.SendAcross(program.TileOf(own));
            Assert.True(Stash.Holds(own), "the potion did not reach the shared page");

            Assert.True(Page.BeginCarry(program.TileOf(own)));
            Page.DropCarried(Column(2), pack: false);

            Assert.StartsWith("Moved", Session.Status);
            Assert.False(Stash.Holds(own), "the shared page still holds it");
            Assert.Equal(1, Session.BeltItems.Count(item => ReferenceEquals(item, own)));
            Assert.Equal(ItemMode.Belt, own.Mode);
        });
    }

    [CorpusFact]
    public void A_potion_out_of_the_materials_tab_is_refused_by_a_last_season_belt_and_its_count_stands()
    {
        if (!program.Ready(changes: true))
            return;

        WithStampsUnknown(() =>
        {
            program.Panes("Inventory", "Materials");
            EmptyTheBelt();
            int slot = Materials.SlotFor("rvl") ?? throw new InvalidOperationException("the tab has no rejuvenation");
            if (Page.Stash.MaterialCount(slot) == 0)
                Page.Stash.AddMaterial(slot, 2);
            int had = Page.Stash.MaterialCount(slot);
            CharacterReadAsLastSeason("Inventory", "Materials");

            Assert.True(Page.BeginCarry(Page.Materials.TileFor(slot)!));
            Page.DropFromTab(Column(0), full: false);

            Assert.Contains(LastSeason, Session.Status);
            Assert.Equal(had, Page.Stash.MaterialCount(slot));
            Assert.Empty(Session.BeltItems);
        });
    }

    [CorpusFact]
    public void A_copy_pasted_on_a_last_season_belt_is_refused_the_original_stays_and_the_clipboard_is_kept()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        WithStampsUnknown(() =>
        {
            try
            {
                program.Panes("Inventory");
                EmptyTheBelt();
                program.GivePlain(Potion, 0);
                CharacterReadAsLastSeason("Inventory");

                var original = program.Choice("Inventory").Grid!.Items.First(tile => tile.Item.Code.Trim() == Potion);
                var at = (original.Item.Mode, original.Item.X, original.Item.Y);
                int count = Session.ItemCount;

                Session.SelectedItem = original;
                Page.Copy();
                Page.PasteOn(null, Column(0));

                Assert.Contains(LastSeason, Session.Status);
                Assert.Equal(count, Session.ItemCount);
                Assert.Empty(Session.BeltItems);
                Assert.Equal(at, (original.Item.Mode, original.Item.X, original.Item.Y));
                Assert.False(Clip.IsEmpty);
            }
            finally
            {
                Clip.Clear();
            }
        });
    }

    /// <summary>The character's other two ways in, as they always asked, now by the one question: said the same, and the item where it was.</summary>
    [CorpusFact]
    public void A_last_season_character_refuses_at_a_cell_and_on_a_slot_with_the_same_words()
    {
        if (!program.Ready(changes: true))
            return;

        WithStampsUnknown(() =>
        {
            program.Panes("Personal Stash", "Shared Page 2");
            var ring = program.Tile("Steel Ring of Skill", "Personal Stash").Item;
            Page.SendAcross(program.TileOf(ring));
            Assert.True(Stash.Holds(ring), "the ring did not reach the shared page");
            var (page, cell, index) = WhereIs(ring);
            var slot = Page.SlotAt(ring, false, 0).Slot!;
            var location = slot.Slot.Location;
            CharacterReadAsLastSeason("Personal Stash", "Shared Page 2");
            ring = OnTheStash(ring.Id);

            string refusal = Session.Refuses(ring)!;
            Assert.Contains(LastSeason, refusal);

            // A cell, by the pane's move.
            Page.SendAcross(program.TileOf(ring));
            Assert.Equal(refusal, Session.Status);
            StillOnItsPage(ring, page, cell, index);

            // A slot, by a drop.
            var fresh = Session.Equipped.First(each => each.Slot.Location == location && !each.IsSwap);
            var worn = fresh.Item?.Item;
            Assert.True(Page.BeginCarry(program.TileOf(ring)));
            Page.DropOnSlot(fresh);
            Assert.Equal($"{refusal} Put back.", Session.Status);
            StillOnItsPage(ring, page, cell, index);
            Assert.Same(worn, Session.Equipped.First(each => each.Slot.Location == location && !each.IsSwap).Item?.Item);
        });
    }

    /// <summary>
    /// What would be refused is not offered: the menu's Equip, and the vault's Equip beside it, which asks the same,
    /// say the file's reason and put nothing on - where they offered Equip and the drop then refused. The character's
    /// own item is asked nothing, as Wear asks it nothing.
    /// </summary>
    [CorpusFact]
    public void Equip_is_not_offered_for_an_item_the_character_would_refuse_and_says_why()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var arriving = ItemForge.BaseAs("cap", ItemQuality.Normal, Odd, new Random(5)).Item;
            var (slots, why, gear, _) = Page.EquipChoice(arriving, mercenary: false);
            Assert.Equal(0, slots);
            Assert.True(gear);
            Assert.Equal($"Require same version: item v{Odd}, character v{Session.ItemEra}.", why);

            var own = Session.Equipped.First(each => each.Slot.Location == ItemLocation.Head && !each.IsSwap).Item?.Item
                      ?? throw new InvalidOperationException("the pad's character wears a helm");
            Assert.True(Page.EquipChoice(own, mercenary: false).Slots > 0);
        });
    }

    /// <summary>Adopt and Wear on a stamp that does not mix, called as the sessions' callers call them: said as before, and nothing taken in.</summary>
    [CorpusFact]
    public void A_stamp_that_does_not_mix_is_refused_at_a_cell_and_on_a_slot()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            var arriving = ItemForge.BaseAs("cap", ItemQuality.Normal, Odd, new Random(5)).Item;
            int count = Session.ItemCount;
            var slot = Session.Equipped.First(each => each.Slot.Location == ItemLocation.Head && !each.IsSwap);
            var worn = slot.Item?.Item;

            string? atCell = Session.Adopt(arriving, program.Choice("Personal Stash").Grid!.Page, 0, 0, rebuild: false);
            string? onSlot = Session.Wear(arriving, ItemLocation.Head, arriving: true, force: true, out bool wearing);

            Assert.Equal($"Require same version: item v{Odd}, character v{Session.ItemEra}.", atCell);
            Assert.Equal(atCell, onSlot);
            Assert.False(wearing);
            Assert.False(Session.Holds(arriving));
            Assert.Equal(count, Session.ItemCount);
            Assert.Same(worn, Session.Equipped.First(each => each.Slot.Location == ItemLocation.Head && !each.IsSwap).Item?.Item);
        });
    }

    /// <summary>
    /// The way back itself: the plan passed, the stash let go, and only then the character refuses, its first item's stamp
    /// turned as the potion leaves. Back on its page, its cell and its place in the list; with no such turn the plan would
    /// have refused first and this path would go unwalked.
    /// </summary>
    [CorpusFact]
    public void A_belt_refused_after_the_stash_let_go_puts_the_potion_back_as_it_lay()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                program.Panes("Inventory", "Shared Page 3");
                EmptyTheBelt();
                var potion = PotionOnSharedPage();
                var (page, cell, index) = WhereIs(potion);

                void Turn(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
                {
                    if (!Stash.Holds(potion))
                    {
                        Stash.PropertyChanged -= Turn;
                        Session.HeldItems.First(item => item.Version is { Length: > 0 }).Version = Odd;
                    }
                }

                Assert.True(Page.BeginCarry(program.TileOf(potion)));
                Stash.PropertyChanged += Turn;
                Page.DropCarried(Column(0), pack: false);

                Assert.StartsWith("Require same version", Session.Status);
                Assert.EndsWith("Put back.", Session.Status);
                Assert.Empty(Session.BeltItems);
                StillOnItsPage(potion, page, cell, index);
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }
}
