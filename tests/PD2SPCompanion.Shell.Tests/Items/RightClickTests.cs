using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Items;

/// <summary>
/// What the rows of an item's right-click menu do,
/// each saying what it did on the strip: a worn item comes
/// off, onto the page a pane shows; an item in a grid is put on - on the mercenary, on the other slot that takes
/// it - or stored in the vault; and a vault card the same way round. A belt comes off with the potions above
/// its first row; what one item pushes off another's slot lands on the shared page a pane shows, and is
/// refused only while nothing has room.
/// <para>
/// What the page decides, asked of it as the menu's rows ask (Equip, TakeOff, Wear, and the rows themselves)
/// and read back from the character, the shared stash and the vault as the program holds them. That a right
/// click of the mouse opens the menu on a tile is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class RightClickTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private VP_ViewModel_Vault_L Vault => Main.RightVault!;
    private ItemLanding Landing => program.Get<ItemLanding>();

    private bool Ready() => program.Ready(changes: true);

    private EquipSlotViewModel Worn(ItemLocation at) => Session.Equipped.First(slot => slot.Slot.Location == at && !slot.IsSwap);

    private string NameAt(ItemLocation at) => Worn(at).Item!.Name;

    /// <summary>The one section of the Items page, whose panes pick any container.</summary>
    private ContainerSectionViewModel Section => Page.Sections.First(section => section.Pages is not null);

    private StashPageChoice Choice(string label) => Section.Pages!.First(page => page.Label == label);

    /// <summary>An item's tile in the grid of the container it lies in - the inventory, unless another is named.</summary>
    private ItemTileViewModel InGrid(string name, string container = "Inventory") =>
        Choice(container).Grid!.Items.First(tile => tile.Name == name);

    private bool IsInGrid(string name, string container = "Inventory") =>
        Choice(container).Grid!.Items.Any(tile => tile.Name == name);

    private ItemLocation? WornAt(string name) =>
        Session.Equipped.FirstOrDefault(slot => !slot.IsSwap && slot.Item?.Name == name)?.Slot.Location;

    private int InVault => program.Get<VaultService>().Store!.Count();

    /// <summary>
    /// A vault page's cards, its sheet read as the window reads it: a stretch of the sheet reads its items
    /// when it is drawn, and every reload makes the stretches anew.
    /// </summary>
    private static IReadOnlyList<ItemTileViewModel> CardsOf(VP_ViewModel_Vault_L vault)
    {
        foreach (var row in vault.PageRows.ToList())
            _ = row.Pages;

        return vault.Cards;
    }

    private bool HasCard(string name) => CardsOf(Vault).Any(card => card.Name == name);

    private ItemTileViewModel Card(string name) => CardsOf(Vault).First(card => card.Name == name);

    /// <summary>The vault shown on the right page, its cards listed.</summary>
    private void VaultBeside()
    {
        // Show: what the view calls as the page comes into sight.
        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Vault.Show();
        });
        Assert.True(program.Until(() => CardsOf(Vault).Count > 0), "the vault's cards were not listed");
    }

    [CorpusFact]
    public void A_worn_item_taken_off_comes_off_into_the_inventory()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            string gloves = NameAt(ItemLocation.Gloves);
            Page.TakeOff(Worn(ItemLocation.Gloves));

            Assert.Equal($"{gloves} taken off.", Session.Status);
            Assert.False(Worn(ItemLocation.Gloves).IsFilled);
            Assert.True(IsInGrid(gloves));
        });
    }

    [CorpusFact]
    public void A_belts_code_line_says_how_many_potions_it_holds()
    {
        if (!program.Ready())
            return;

        // What decides which potions leave when one belt takes another's place, and no tooltip of the game's says it.
        program.On(() => Assert.Contains(ItemFacts.Of(Worn(ItemLocation.Waist).Item!), line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^Code: \w+ \(\w+, 16 slots\)$")));
    }

    [CorpusFact]
    public void A_ring_goes_on_the_first_finger_by_Equip_and_the_other_by_Equip_in_other_slot()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            // Both fingers bare, so each click has both slots to choose from.
            string ring = NameAt(ItemLocation.LeftFinger);
            Page.TakeOff(Worn(ItemLocation.LeftFinger));
            if (Worn(ItemLocation.RightFinger).IsFilled)
                Page.TakeOff(Worn(ItemLocation.RightFinger));

            Page.Equip(InGrid(ring));
            Assert.Equal($"Equipped {ring}.", Session.Status);
            var first = WornAt(ring);
            Assert.True(first is ItemLocation.LeftFinger or ItemLocation.RightFinger);

            Page.TakeOff(Worn(first!.Value));
            Page.Equip(InGrid(ring), index: 1);
            Assert.Equal($"Equipped {ring}.", Session.Status);
            var other = WornAt(ring);
            Assert.True(other is ItemLocation.LeftFinger or ItemLocation.RightFinger);
            Assert.NotEqual(first, other);
        });
    }

    [CorpusFact]
    public void With_a_shared_page_shown_what_comes_off_lands_there_and_the_strip_says_so()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            Section.SelectedPage = Choice("Shared Page 5");
            Assert.Empty(Choice("Shared Page 5").Grid!.Items);

            string amulet = NameAt(ItemLocation.Neck);
            var item = Worn(ItemLocation.Neck).Item!.Item;
            Page.TakeOff(Worn(ItemLocation.Neck));

            Assert.Equal($"{amulet} taken off to shared page 5.", Session.Status);
            Assert.True(IsInGrid(amulet, "Shared Page 5"));
            Assert.True(Landing.WhereIs(item) is { Shared: true, Page: 5 });
        });
    }

    [CorpusFact]
    public void A_smaller_belt_put_on_sends_off_only_the_rows_it_lacks_and_comes_off_with_its_second_row()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            string belt = NameAt(ItemLocation.Waist);
            Assert.Equal(6, Session.BeltItems.Count);
            Assert.NotNull(program.Give("sash", "Sash"));

            // Two rows of the belt's four: the one potion above them leaves the belt.
            Page.Equip(InGrid("Sash"));
            Assert.Equal($"Equipped Sash. {belt} to inventory.", Session.Status);
            Assert.Equal(ItemLocation.Waist, WornAt("Sash"));
            Assert.True(IsInGrid(belt));
            Assert.Equal(5, Session.BeltItems.Count);

            // Off with potions in its second row: they land, it is not refused.
            Page.TakeOff(Worn(ItemLocation.Waist));
            Assert.Equal("Sash taken off.", Session.Status);
            Assert.Equal(3, Session.BeltItems.Count);
            Assert.True(IsInGrid("Sash"));

            Page.Equip(InGrid(belt));
            Assert.Equal($"Equipped {belt}.", Session.Status);
        });
    }

    [CorpusFact]
    public void The_menus_Store_in_vault_stores_as_the_tools_button_does_whichever_page_is_beside()
    {
        if (!Ready())
            return;


        // The chord it took the place of (Shift + right click) asked for the vault page beside; the row is the
        // tools' Store, which never did.
        string gloves = "";
        int stored = 0;
        program.On(() =>
        {
            gloves = NameAt(ItemLocation.Gloves);
            Page.TakeOff(Worn(ItemLocation.Gloves));
            stored = InVault;

            Page.Menu(InGrid(gloves)).Pick("Store in vault");
        });

        Assert.True(program.Until(() => InVault == stored + 1), "the gloves were not stored");
        program.On(() => Assert.False(IsInGrid(gloves)));
    }

    [CorpusFact]
    public void A_vault_cards_Equip_withdraws_it_and_puts_it_on_and_what_nobody_wears_has_no_such_row()
    {
        if (!Ready())
            return;


        VaultBeside();
        string gloves = "";
        int stored = 0;
        program.On(() =>
        {
            Assert.Equal("Withdraw; [To other page]; Move to folder...", Vault.Menu(Card("Tome of Town Portal")).Words());
            Vault.Wear(Card("Tome of Town Portal"));
            Assert.Equal("Tome of Town Portal cannot be equipped.", Vault.Notice);

            gloves = NameAt(ItemLocation.Gloves);
            Page.TakeOff(Worn(ItemLocation.Gloves));
            stored = InVault;
            Page.Menu(InGrid(gloves)).Pick("Store in vault");
        });
        Assert.True(program.Until(() => InVault == stored + 1 && HasCard(gloves)), "the gloves' card was not listed");

        program.On(() =>
        {
            var menu = Vault.Menu(Card(gloves));
            Assert.StartsWith("Withdraw; Equip; Equip on mercenary; ", menu.Words());
            menu.Pick("Equip");
        });
        Assert.True(program.Until(() => InVault == stored), "the card was not withdrawn");
        program.On(() =>
        {
            Assert.Equal($"{gloves} moved and equipped.", Vault.Notice);
            Assert.Equal(ItemLocation.Gloves, WornAt(gloves));
        });
    }

    [CorpusFact]
    public void A_card_put_on_over_a_worn_piece_swaps_the_piece_pushed_off_goes_into_the_vault()
    {
        if (!Ready())
            return;


        VaultBeside();
        string belt = "";
        int stored = 0;
        program.On(() =>
        {
            belt = NameAt(ItemLocation.Waist);
            Assert.NotNull(program.Give("sash", "Sash"));
            stored = InVault;
            Page.Menu(InGrid("Sash")).Pick("Store in vault");
        });
        Assert.True(program.Until(() => InVault == stored + 1 && HasCard("Sash")), "the Sash's card was not listed");

        program.On(() => Vault.Wear(Card("Sash")));
        Assert.True(program.Until(() => HasCard(belt)), "the belt pushed off is not in the vault");
        program.On(() =>
        {
            Assert.Equal("Sash moved and equipped.", Vault.Notice);
            Assert.Equal(ItemLocation.Waist, WornAt("Sash"));
            Assert.False(IsInGrid(belt));

            // One out, one in.
            Assert.Equal(stored + 1, InVault);
        });
    }

    [CorpusFact]
    public void A_cards_Withdraw_takes_it_to_the_character()
    {
        if (!Ready())
            return;

        VaultBeside();
        int stored = 0;
        program.On(() =>
        {
            stored = InVault;
            Vault.Menu(Card("Tome of Town Portal")).Pick("Withdraw");
        });

        Assert.True(program.Until(() => InVault == stored - 1), "the tome was not withdrawn");
        program.On(() => Assert.StartsWith("1 item moved to character.", Vault.Notice));
    }

    [CorpusFact]
    public void A_card_on_the_left_vault_page_is_not_put_on_the_inventory_is_out_of_sight()
    {
        if (!Ready())
            return;

        var left = Main.Vault!;
        program.On(() =>
        {
            Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
            left.Show();
        });
        Assert.True(program.Until(() => CardsOf(left).Count > 0), "the left vault's cards were not listed");

        program.On(() =>
        {
            int stored = InVault;
            var tome = CardsOf(left).First(card => card.Name == "Tome of Town Portal");

            left.Wear(tome);
            Assert.Equal("Require inventory page open.", left.Notice);
            Assert.Equal(stored, InVault);

            // A piece of gear's Equip is dimmed there, and says why; Withdraw is the tools' own, in sight or not.
            var gear = CardsOf(left).FirstOrDefault(card => left.MenuFor(card).Find("Equip") is not null);
            if (gear is not null)
            {
                var equip = left.MenuFor(gear).Row("Equip");
                Assert.False(equip.IsEnabled);
                Assert.Equal("Require inventory page open.", equip.Why);
            }

            Assert.True(left.Menu(tome).Row("Withdraw").IsEnabled);
        });
    }

    [CorpusFact]
    public void The_mercenarys_piece_comes_off_and_goes_back_on_by_Equip_on_mercenary()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            var slot = Session.MercEquipped.First(each => each.IsFilled);
            string hired = slot.Item!.Name;

            Page.TakeOff(slot);
            Assert.Equal($"{hired} taken off.", Session.Status);
            Assert.True(IsInGrid(hired));
            Assert.DoesNotContain(Session.MercEquipped, each => each.Item?.Name == hired);

            Page.Equip(InGrid(hired), mercenary: true);
            Assert.Equal($"Equipped {hired}.", Session.Status);
            Assert.Contains(Session.MercEquipped, each => each.Item?.Name == hired);
            Assert.False(IsInGrid(hired));
        });
    }

    [CorpusFact]
    public void What_one_pushes_off_lands_on_the_shared_page_shown()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            Section.SelectedPage = Choice("Shared Page 5");
            string boots = NameAt(ItemLocation.Feet);
            Assert.NotNull(program.Give("war trav", "War Traveler"));
            Assert.True(IsInGrid("War Traveler", "Shared Page 5"), "forged to the shared page shown");

            Page.Equip(InGrid("War Traveler", "Shared Page 5"));
            Assert.Equal($"Equipped War Traveler. {boots} to shared page 5.", Session.Status);
            Assert.Equal(ItemLocation.Feet, WornAt("War Traveler"));
            Assert.True(IsInGrid(boots, "Shared Page 5"));
        });
    }

    [CorpusFact]
    public void A_piece_pushed_off_is_refused_only_while_nothing_has_room_a_shared_page_beside_takes_it()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            string weapon = NameAt(ItemLocation.RightHand);
            Assert.NotNull(program.Give("short sword", "Short Sword"));

            // The character's own pages have no room for the large weapon, and no shared page is shown.
            Page.Equip(InGrid("Short Sword"));
            Assert.Equal($"No free space for {weapon}. Put back.", Session.Status);
            Assert.Equal(ItemLocation.RightHand, WornAt(weapon));
            Assert.True(IsInGrid("Short Sword"));

            // A second pane, on a shared page: it takes the weapon, and the strip says where it went.
            Section.SecondShown = true;
            Section.SecondPage = Choice("Shared Page 5");
            Page.Equip(InGrid("Short Sword"));
            Assert.Equal($"Equipped Short Sword. {weapon} to shared page 5.", Session.Status);
            Assert.Equal(ItemLocation.RightHand, WornAt("Short Sword"));
            Assert.True(IsInGrid(weapon, "Shared Page 5"));
        });
    }
}
