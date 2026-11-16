using D2SLib.Model.Save;

using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// One rule for what lands: every entrance asks what <see cref="CharacterSession.Refuses(Item)"/> asks, a worn slot lights
/// what a drop on it would do, and a piece pushed off onto a shared page that turns it away is never in no list. Never
/// lost, a copy allowed.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class LandingOneRuleTests(Program program)
{
    private const string Ring = "Steel Ring of Skill", Other = "Felicitous Ring of Regrowth", Odd = "50";

    private CharacterSession Session => program.Main.Session;
    private SharedStashSession Stash => program.Get<SharedStashSession>();
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;
    private static ItemClipboard Clip => ItemClipboard.Shared;

    private void StampTheCharacter()
    {
        Session.HeldItems.First(item => item.Version is { Length: > 0 }).Version = Odd;
        Assert.Equal(Odd, Session.ItemEra);
    }

    private void Test(Action test)
    {
        using var free = program.InMode("Free");
        try
        {
            program.On(test);
        }
        finally
        {
            program.On(Clip.Clear);
            program.PanesAsFound();
        }
    }

    /// <summary>A base stamped as the character's own items are, in no list yet.</summary>
    private Item Made(string code) => ItemForge.BaseAs(code, ItemQuality.Normal, Session.ItemEra!, new Random(5)).Item;

    private Item PutOn(string code, ItemLocation at)
    {
        var item = Made(code);
        Assert.Null(Session.Wear(item, at, arriving: true, force: true, out bool worn));
        Assert.True(worn);
        return item;
    }

    /// <summary>Every free cell of the character's own pages taken by a potion, so what is pushed off has to go beyond them.</summary>
    private void FillTheCharactersPages()
    {
        var potion = Made("hp1");
        foreach (string name in new[] { "Inventory", "Cube", "Personal Stash" })
        {
            var grid = program.Choice(name).Grid!;
            var room = new ItemMover.Occupancy(new GridSize(grid.Columns, grid.Rows), grid.Items.Select(tile => tile.Item));
            while (ItemMover.FirstFreeCell(potion, room) is { } cell)
            {
                Assert.Null(Session.Adopt(Session.CopyOf(potion)!, grid.Page, cell.X, cell.Y, rebuild: false, keepId: true));
                room.Block(cell.X, cell.Y, 1, 1);
            }
        }

        Session.RefreshContainers();
    }

    private void EmptyTheSharedStash()
    {
        // Socketed ones are listed too: they leave with the item they are in.
        foreach (var item in Stash.HeldItems.ToList())
            Stash.Release(item, rebuild: false);

        Stash.RefreshPages();
        Assert.Null(Stash.ItemEra);
    }

    /// <summary>
    /// Two pieces pushed off by a bow, the character's pages full and the shared stash empty: neither is refused while it is
    /// asked, as an empty stash has no era. The first to land gives it one, which the second's stamp does not mix with.
    /// </summary>
    [CorpusFact]
    public void Pieces_pushed_off_that_the_empty_stash_then_refuses_are_worn_again_and_nothing_is_lost()
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Shared Page 5");
            var right = PutOn("ssd", ItemLocation.RightHand);
            var left = PutOn("buc", ItemLocation.LeftHand);
            FillTheCharactersPages();
            EmptyTheSharedStash();
            left.Version = Odd;
            int held = Session.ItemCount;

            var bow = Made("sbw");
            Assert.Equal([right, left], Session.PushedOffBy(bow, ItemLocation.RightHand));

            string? said = Session.Wear(bow, ItemLocation.RightHand, arriving: true, force: true, out bool worn);

            Assert.True(Session.Holds(left) || Stash.Holds(left), "the second piece is in no list");
            Assert.True(Session.Holds(left), "the second piece is not back on");
            Assert.True(Session.Holds(right), "the first piece stayed on the stash");
            Assert.False(worn);
            Assert.Equal($"Require same version: item v{Odd}, stash v{right.Version}.", said);
            Assert.Equal((ItemMode.Equipped, ItemLocation.RightHand), (right.Mode, right.Location));
            Assert.Equal((ItemMode.Equipped, ItemLocation.LeftHand), (left.Mode, left.Location));
            Assert.False(Session.Holds(bow));
            Assert.Equal(held, Session.ItemCount);
            Assert.Empty(Stash.HeldItems);
        });
    }

    [CorpusFact]
    public void Pieces_pushed_off_that_the_shared_page_takes_all_land_there()
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Shared Page 5");
            var right = PutOn("ssd", ItemLocation.RightHand);
            var left = PutOn("buc", ItemLocation.LeftHand);
            FillTheCharactersPages();
            EmptyTheSharedStash();

            var bow = Made("sbw");
            Session.Wear(bow, ItemLocation.RightHand, arriving: true, force: true, out bool worn);

            Assert.True(worn);
            Assert.True(Session.Holds(bow));
            Assert.True(Stash.Holds(right) && Stash.Holds(left), "a piece did not reach the shared page");
            Assert.Equal(5, SharedStash.PageOf(right));
            Assert.Equal(5, SharedStash.PageOf(left));
        });
    }

    [CorpusFact]
    public void Receive_asks_what_every_entrance_asks()
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Personal Stash");
            var page = program.Choice("Personal Stash").Grid!.Page;
            var odd = ItemForge.BaseAs("rin", ItemQuality.Normal, Odd, new Random(5)).Item;
            int held = Session.ItemCount;
            string refusal = Session.Refuses(odd)!;
            Assert.StartsWith("Require same version", refusal);

            Assert.Equal(refusal, Session.Receive(odd, "no room"));
            Assert.Equal(refusal, Session.Receive(odd, "no room", select: false, at: (page, (0, 0))));
            Assert.Equal(held, Session.ItemCount);
            Assert.False(Session.Holds(odd));

            var good = Made("rin");
            var cell = program.FreeCell("Personal Stash");
            Assert.Equal(refusal, Session.ReceiveAll([(good, page, (cell.Column, cell.Row)), (odd, page, (cell.Column, cell.Row))]));
            Assert.Equal(held, Session.ItemCount);
            Assert.False(Session.Holds(good), "one of a refused batch landed");

            Assert.Null(Session.Receive(good, "no room", select: false));
            Assert.True(Session.Holds(good));
        });
    }

    /// <summary>A batch on a file that holds nothing: the first gives it the era the second is asked against, before any lands.</summary>
    [CorpusFact]
    public void A_batch_the_empty_stash_would_turn_away_half_way_lands_none()
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Shared Page 5");
            EmptyTheSharedStash();
            var first = Made("rin");
            var second = ItemForge.BaseAs("rin", ItemQuality.Normal, Odd, new Random(5)).Item;
            bool made = false;
            void Heard(Item _) => made = true;
            Stash.ItemMade += Heard;

            try
            {
                Assert.Null(Stash.Refuses(first));
                Assert.Null(Stash.Refuses(second));

                string? said = Stash.ReceiveAll([(first, 5, 0, 0), (second, 5, 2, 0)]);

                Assert.Equal($"Require same version: item v{Odd}, stash v{first.Version}.", said);
                Assert.Empty(Stash.HeldItems);
                Assert.False(made, "a copy that did not land was said to be made");
            }
            finally
            {
                Stash.ItemMade -= Heard;
            }
        });
    }

    [CorpusFact]
    public void Several_of_the_characters_own_refused_leave_nothing_copied_on_either_file()
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Personal Stash", "Shared Page 2");
            Page.SendAcross(program.Tile(Other, "Personal Stash"));
            Page.ClearSelection();
            Assert.True(Page.BeginDragPick(keepSelected: false));
            Page.SetPicked(program.Tile(Ring, "Personal Stash"), true);
            Page.SetPicked(program.Tile(Other, "Shared Page 2"), true);
            Page.EndPick();
            StampTheCharacter();
            int held = Session.ItemCount, there = Stash.HeldItems.Count();

            Page.Copy();
            Assert.Equal(2, Clip.Items.Count);
            Page.PasteOn(program.Choice("Personal Stash"));

            Assert.Contains("Require same version", Session.Status);
            Assert.Equal(held, Session.ItemCount);
            Assert.Equal(there, Stash.HeldItems.Count());
        });
    }

    /// <summary>The hover says what the drop then does: green where it goes on, red where it would be put back.</summary>
    [CorpusTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_worn_slot_lights_red_for_an_item_of_the_shared_stash_the_drop_would_refuse(bool mercenary)
    {
        if (!program.Ready(changes: true))
            return;

        Test(() =>
        {
            program.Panes("Personal Stash", "Shared Page 2");

            // A piece the slot takes, on a shared page: the mercenary's own taken off and sent across.
            Item item;
            EquipSlotViewModel slot;
            if (mercenary)
            {
                slot = Session.MercEquipped.First(each => each.IsFilled);
                var location = slot.Slot.Location;
                item = slot.Item!.Item;
                Assert.Null(program.Get<ItemLanding>().Move(item, out _));
                if (!Stash.Holds(item))
                    Page.SendAcross(program.TileOf(item));
                slot = Session.MercEquipped.First(each => each.Slot.Location == location);
            }
            else
            {
                item = program.Tile(Ring, "Personal Stash").Item;
                Page.SendAcross(program.TileOf(item));
                slot = Page.SlotAt(item, false, 0).Slot!;
            }

            Assert.True(Stash.Holds(item), "the piece did not reach the shared page");
            Assert.True(Page.BeginCarry(program.TileOf(item)));

            Page.HoverSlot(slot, mercenary);
            Assert.True(slot.IsTarget);
            Assert.True(slot.IsAllowed, "the slot refused a piece it takes");
            Page.HoverSlot(null);

            StampTheCharacter();
            Page.HoverSlot(slot, mercenary);
            Assert.True(slot.IsTarget);
            Assert.False(slot.IsAllowed, "the slot lit green for what the drop refuses");
            Page.HoverSlot(null);

            Page.DropOnSlot(slot, mercenary);
            Assert.StartsWith("Require same version", Session.Status);
            Assert.True(Stash.Holds(item));
        });
    }

    /// <summary>A card of the vault's carried over a slot: the character's own items may be anything, but this one is arriving.</summary>
    [CorpusFact]
    public void A_worn_slot_lights_red_for_a_vault_card_the_drop_would_refuse()
    {
        if (!program.Ready(changes: true))
            return;

        var vault = program.Main.RightVault!;
        program.On(() =>
        {
            var vaults = program.Get<VaultService>();
            var cap = Made("cap");
            cap.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
            Assert.NotNull(vaults.Store!.Add(cap, vaults.CurrentFolder, out _));
            vaults.SayChanged();
            program.Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            vault.Show();
        });
        Assert.True(program.Until(() => vault.Cards().Any(card => card.Name == "Cap")), "the cap's card was not listed");

        Test(() =>
        {
            var card = vault.Cards().First(each => each.Name == "Cap");
            var slot = Page.SlotAt(card.Item, false, 0).Slot!;
            Assert.True(vault.BeginCarry(card));

            try
            {
                Assert.NotNull(Page.VaultCarriedItem?.Invoke());

                Page.HoverSlot(slot);
                Assert.True(slot.IsAllowed, "the slot refused a card it takes");
                Page.HoverSlot(null);

                StampTheCharacter();
                Page.HoverSlot(slot);
                Assert.True(slot.IsTarget);
                Assert.False(slot.IsAllowed, "the slot lit green for what the drop refuses");
                Page.HoverSlot(null);
            }
            finally
            {
                vault.EndCarry();
            }
        });
    }
}
