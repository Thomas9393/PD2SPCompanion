using System.Text.RegularExpressions;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Tips;

/// <summary>
/// The strip at the foot of an item's tooltip (TipActions, MainViewModel.TipFor), and the
/// right-click menu it names (VP_ViewModel_Inv_L.MenuFor): the strip says "RMB - Menu", and the menu's rows
/// say what each would do - "Replace Veil of Steel", "Equip on mercenary". A shield is aimed
/// at the worn shield, a weapon at the worn weapon, a weapon held in both hands at both - and takes both off;
/// one of several worn items picked says how many come off; a vault card says "Withdraw". And a set piece's
/// bonus lines name their tier in words.
/// <para>
/// Asked as the tooltip asks (<see cref="Tooltip"/>), on the pad's PhySpearZon: Laying of Hands and a Veil of
/// Steel worn, a second Veil of Steel in the inventory, Naj's Circlet on the mercenary. That the strip is drawn
/// under the item, in its tooltip, is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class TipStripTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    private ContainerSectionViewModel Section => Page.Sections.First(section => section.Pages is not null);

    private StashPageChoice Choice(string label) => Section.Pages!.First(page => page.Label == label);

    private EquipSlotViewModel Worn(ItemLocation at) => Session.Equipped.First(slot => slot.Slot.Location == at && !slot.IsSwap);

    private ItemTileViewModel InGrid(string name, string container = "Inventory") =>
        Choice(container).Grid!.Items.First(tile => tile.Name == name);

    /// <summary>What a test does with a tooltip of its own, the comparison shut; the tooltip gone after it.</summary>
    private void WithTip(Action<Tooltip> test) => program.On(() =>
    {
        using var tip = new Tooltip();
        test(tip);
    });

    [CorpusFact]
    public void A_worn_items_strip_names_the_menu_whose_first_row_is_Take_off()
    {
        if (!program.Ready())
            return;

        WithTip(tip =>
        {
            var gloves = Worn(ItemLocation.Gloves).Item!;
            tip.Over(gloves, TipActions.Worn);
            Assert.Equal("RMB - Menu", tip.Strip);
            Assert.False(tip.Info!.Offers);

            var menu = Page.Menu(gloves);
            Assert.Equal("Take off; Store in vault; Store sorted; Store as crafting base; [Store in compendium]", menu.Words());
            Assert.True(menu.Rows[0].IsMain);
            Assert.Equal("Delete", menu.Icons[^1].Label);
        });
    }

    [CorpusFact]
    public void A_set_pieces_bonus_lines_name_their_tier_in_words()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var lines = Worn(ItemLocation.Gloves).Item!.Tooltip.Select(line => line.Text).ToList();
            Assert.Equal("Laying of Hands", Worn(ItemLocation.Gloves).Item!.Name);
            Assert.Contains(lines, line => line.EndsWith("(2 Items)"));

            // Not a bare 2 run on after the words, as it once was.
            Assert.DoesNotContain(lines, line => Regex.IsMatch(line, @"\D2$"));
        });
    }

    [CorpusFact]
    public void An_item_in_the_grid_names_the_menu_whose_rows_say_what_it_replaces_and_offer_the_mercenary()
    {
        if (!program.Ready())
            return;


        WithTip(tip =>
        {
            tip.Over(InGrid("Veil of Steel"));
            Assert.Equal("RMB - Menu; Tab - Compare", tip.Strip);
            Assert.True(tip.Info!.Offers);
            Assert.Equal("Replace Veil of Steel; Equip on mercenary; Store in vault; Store sorted; Store as crafting base; Store in compendium",
                     Page.Menu(InGrid("Veil of Steel")).Words());

            // What nobody wears has no Equip to dim.
            Assert.Equal("Store in vault; Store sorted; Store as crafting base", Page.Menu(InGrid("Tome of Town Portal")).Words());

            // Shut to begin with: nothing compared, no caption.
            Assert.Equal("", tip.Caption);
            Assert.Empty(tip.Compared);
        });
    }

    [CorpusFact]
    public void A_ring_with_both_fingers_bare_says_Equip_and_offers_the_other_slot()
    {
        if (!program.Ready(changes: true))
            return;

        WithTip(tip =>
        {
            Page.TakeOff(Worn(ItemLocation.LeftFinger));
            Assert.False(Worn(ItemLocation.RightFinger).IsFilled);

            tip.Over(InGrid("Nagelring"));
            Assert.Equal("RMB - Menu; Tab - Compare", tip.Strip);
            Assert.StartsWith("Equip; Equip in other slot; ", Page.Menu(InGrid("Nagelring")).Words());
        });
    }

    [CorpusFact]
    public void A_shield_is_aimed_at_the_worn_shield_a_weapon_at_the_worn_weapon_and_one_held_in_both_hands_at_both()
    {
        if (!program.Ready(changes: true))
            return;

        // The second weapon set, whose hands are bare: its tab swaps it to hand, once the boxes have faded.
        program.On(() => Page.ShowSetIICommand.Execute(null));
        Assert.True(program.Until(() => !Page.IsSwapping && Session.TabInHand == 2), "the second weapon set was not swapped to hand");

        try
        {
            WithTip(tip =>
            {
                // Forged to the shared page shown, which has room for all of it.
                Section.SelectedPage = Choice("Shared Page 5");
                ItemTileViewModel Forged(string typed, string name)
                {
                    Assert.NotNull(program.Give(typed, name));
                    return InGrid(name, "Shared Page 5");
                }

                // What the menu's first row says of each: what putting it on would take off.
                string First(ItemTileViewModel tile) => Page.Menu(tile).Rows[0].Label;

                Assert.StartsWith("Equip; Equip in other slot; ", Page.Menu(Forged("short sword", "Short Sword")).Words());
                Page.Equip(InGrid("Short Sword", "Shared Page 5"));
                Assert.Equal("Equipped Short Sword.", Session.Status);

                Assert.Equal("Equip", First(Forged("buckler", "Buckler")));
                Page.Equip(InGrid("Buckler", "Shared Page 5"));
                Assert.Equal("Equipped Buckler.", Session.Status);

                Assert.Equal("Replace Buckler", First(Forged("small shield", "Small Shield")));
                Assert.Equal("Replace Short Sword", First(Forged("javelin", "Javelin")));

                // Held in both hands, over the sword and the shield: both come off.
                var staff = Forged("short staff", "Short Staff");
                tip.Over(staff);
                Assert.Equal("Replace Short Sword and Buckler", First(staff));

                TipActions.Toggle();
                Assert.Equal("Equipped - Weapon", tip.Caption);
                Assert.Equal(["Short Sword", "Buckler"], tip.Compared);
                TipActions.Toggle();

                Page.Equip(staff);
                Assert.Equal("Equipped Short Staff. Short Sword to shared page 5. Buckler to shared page 5.", Session.Status);
            });
        }
        finally
        {
            program.On(() => Section.SelectedPage = Choice("Inventory"));
        }
    }

    [CorpusFact]
    public void One_of_several_worn_items_picked_says_how_many_come_off_and_Take_off_takes_them_all()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            WithTip(tip =>
            {
                // The ring clicked, then the amulet with Ctrl: two picked.
                Page.ClickTileCommand.Execute(Worn(ItemLocation.LeftFinger).Item);
                Assert.True(Page.BeginDragPick(keepSelected: true));
                Page.SetPicked(Worn(ItemLocation.Neck).Item!, true);
                Page.EndPick();
                Assert.Equal(2, Page.PickedCount);

                // The menu of one of them speaks for both, and the press that opens it leaves them picked.
                var menu = Page.Menu(Worn(ItemLocation.LeftFinger).Item!);
                Assert.Equal(2, Page.PickedCount);
                Assert.Equal("Take off (2); Store in vault (2); Store sorted (2); Store as crafting base (2); [Store in compendium]", menu.Words());

                menu.Pick("Take off");
                Assert.Equal("2 items taken off.", Session.Status);
                Assert.False(Worn(ItemLocation.LeftFinger).IsFilled);
                Assert.False(Worn(ItemLocation.Neck).IsFilled);
            });
        }
        finally
        {
            program.On(Page.ClearSelection);
        }
    }

    [CorpusFact]
    public void A_vault_card_nothing_wears_offers_Withdraw_and_with_the_vault_on_both_pages_the_other_page()
    {
        if (!program.Ready())
            return;

        var vault = Main.RightVault!;
        IReadOnlyList<ItemTileViewModel> Cards()
        {
            foreach (var row in vault.PageRows.ToList())
                _ = row.Pages;

            return vault.Cards;
        }

        try
        {
            program.On(() =>
            {
                Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
                vault.Show();
            });
            Assert.True(program.Until(() => Cards().Any(card => card.Name == "Tome of Town Portal")), "the tome's card was not listed");

            WithTip(tip =>
            {
                var tome = Cards().First(card => card.Name == "Tome of Town Portal");
                tip.Over(tome);
                Assert.Equal("RMB - Menu", tip.Strip);
                // The other page is the inventory's: the row is dimmed, and says what it needs.
                Assert.Equal("Withdraw; [To other page]; Move to folder...", vault.Menu(tome).Words());
                Assert.Equal("Require vault on both pages.", vault.Menu(tome).Row("To other page").Why);

                // The vault on both pages: a card goes to the other page's folder too.
                Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
                tip.Over(tome);
                Assert.Equal("RMB - Menu", tip.Strip);
                Assert.Equal("Withdraw; To other page; Move to folder...", vault.Menu(tome).Words());
            });
        }
        finally
        {
            program.On(() =>
            {
                Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark);
                Main.ShowRightCommand.Execute(MainViewModel.EditMark);
            });
        }
    }
}
