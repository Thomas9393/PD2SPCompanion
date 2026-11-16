using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Tests.Inventory;
using PD2SPCompanion.Shell.Tests.Vault;
using PD2SPCompanion.Shell.ViewModels;

using EditPad = PD2SPCompanion.Shell.Tests.Edit.EditPad;

namespace PD2SPCompanion.Shell.Tests.Items;

/// <summary>
/// The right-click menus of what is no item:
/// a pane's empty cell - Paste on that pane's page, Select all, Close
/// gaps where the page is one that is arranged; the vault sheet's empty room; a vault folder; the ways Store all
/// stores, listed under its arrow; and a character's card. An item's own menu is RightClickTests' and
/// TipStripTests'; the clipboard's rows are ItemClipboardTests'.
/// <para>
/// What each page puts in its menu and what a row then does, asked as the views ask. That a right click opens
/// them where the pointer is, is the UI cases' to show (units/right-click.ps1, vault-tree.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ItemMenusTests(Program program)
{
    private const string Ring = "Steel Ring of Skill", Stash = "Personal Stash", Shared = "Shared Page 5", Tome = "Tome of Town Portal";

    private MainViewModel Main => program.Main;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    [CorpusFact]
    public void A_panes_empty_cell_offers_Paste_there_Select_all_and_Close_gaps_where_the_page_is_arranged()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var section = VaultHands.Section(program);
            try
            {
                // The inventory is laid out by the player: no Close gaps. Nothing held: Paste is dimmed.
                section.SelectedPage = VaultHands.Choice(program, "Inventory");
                var menu = Page.MenuFor(section.SelectedPage!.Grid!.Cells[0]);
                Assert.Equal("Select all", menu.Words());
                Assert.False(menu.Row("Paste").IsEnabled);

                // The personal stash is arranged.
                section.SelectedPage = VaultHands.Choice(program, Stash);
                var stash = Page.MenuFor(section.SelectedPage!.Grid!.Cells[0]);
                Assert.Equal("Sort by; Close gaps; Select all", stash.Words());
                Assert.Equal(["Type", "Name", "Rarity", "Ascending", "Descending"], stash.Row("Sort by").More!.Select(line => line.Label));

                // Cut, and pasted by the menu of an empty cell of the same page: on that cell - a paste from a
                // menu lands where the menu was opened.
                var ring = VaultHands.Tile(program, Ring, Stash);
                Page.ClickTileCommand.Execute(ring);
                Page.CutCommand.Execute(null);
                var free = VaultHands.FreeCell(program, Stash);
                Assert.False(ring.CellX == free.Column && ring.CellY == free.Row);
                Page.MenuFor(free).Pick("Paste");
                ring = VaultHands.Tile(program, Ring, Stash);
                Assert.Equal((free.Column, free.Row), (ring.CellX, ring.CellY));
                Assert.True(ItemClipboard.Shared.IsEmpty);

                // Cut in the first pane, pasted by the menu of the second pane's empty cell: there, not on the first.
                program.Panes(Stash, Shared);
                Page.ClickTileCommand.Execute(VaultHands.Tile(program, Ring, Stash));
                Page.CutCommand.Execute(null);

                var across = Page.MenuFor(section.SecondPage!.Grid!.Cells[0]);
                Assert.True(across.Row("Paste").IsEnabled);
                Assert.Equal("1 item cut", across.Row("Paste").Why);
                across.Pick("Paste");

                Assert.NotNull(VaultHands.TileOrNone(program, Ring, Shared));
                Assert.Null(VaultHands.TileOrNone(program, Ring, Stash));
                Assert.True(ItemClipboard.Shared.IsEmpty);

                // Its Select all picks the section's.
                Page.MenuFor(section.SelectedPage!.Grid!.Cells[0]).Pick("Select all");
                Assert.True(Page.PickedCount > 1);
            }
            finally
            {
                Page.ClearSelection();
                section.SecondShown = false;
                section.SelectedPage = VaultHands.Choice(program, "Inventory");
            }
        });
    }

    [CorpusFact]
    public void Store_alls_arrow_lists_its_three_ways_and_each_asks_as_the_buttons_press_does()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var menu = Page.StoreAllMenu();
            Assert.Equal("Store all in open folder; Store all, sorted into folders; Store only crafting bases, sorted", menu.Words());
            Assert.Equal("LMB", menu.Row("Store all in open folder").Keys);
            Assert.Equal("Ctrl + LMB", menu.Row("Store all, sorted into folders").Keys);
            Assert.Equal("Shift + LMB", menu.Row("Store only crafting bases, sorted").Keys);

            try
            {
                menu.Pick("Store all, sorted into folders");
                Assert.True(Page.StoreAllArmed);
                Assert.Matches(@"^Store \d+ items? in vault, sorted\? .*Click green tick again to confirm\.$", Main.Session.Status);
                Assert.StartsWith("Confirm storing, sorted", Page.StoreAllHint);
            }
            finally
            {
                Page.StoreAllArmed = false;
            }
        });
    }

    [CorpusFact]
    public void The_vault_sheets_empty_room_offers_Paste_New_folder_and_Select_all()
    {
        if (!program.Ready(changes: true))
            return;

        var vault = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        program.On(() =>
        {
            var menu = vault.MenuForSpace();
            Assert.Equal(["Paste"], menu.Icons.Select(icon => icon.Label));
            Assert.Equal("New folder; Sort by; Select all", menu.Words());
            Assert.False(menu.Row("Paste").IsEnabled);

            // Sort by: the orders the Sort button lists, the one in force ticked; a line picked is that order.
            var orders = menu.Row("Sort by").More!;
            Assert.Equal(["Stored", "Name", "Rarity", "Type", "Ascending", "Descending"], orders.Select(line => line.Label));
            Assert.Contains(orders, line => line.IsOn && line.Label == vault.Order.By);
            string was = vault.Order.By;
            try
            {
                orders.First(line => line.Label == (was == "Name" ? "Type" : "Name")).Run();
                Assert.NotEqual(was, vault.Order.By);
                Assert.True(vault.MenuForSpace().Row("Sort by").More!.First(line => line.Label == vault.Order.By).IsOn);
            }
            finally
            {
                vault.Order.Pick(vault.Order.Options.First(option => option.Label == was));
            }

            menu.Pick("Select all");
            Assert.True(vault.CanCut);
            vault.ClearSelection();
        });
    }

    [CorpusFact]
    public void A_folders_menu_acts_on_the_folder_without_going_to_it()
    {
        if (!program.Ready(changes: true))
            return;

        var vault = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        program.On(() =>
        {
            long jewels = VaultHands.Place(vault, "Jewels").Id!.Value;
            long? shown = vault.Here;

            var menu = vault.MenuFor(jewels);
            Assert.StartsWith("Open; ", menu.Words());
            Assert.True(menu.Row("Open").IsMain);
            Assert.NotNull(menu.Find("Move to folder..."));

            // Locked and pinned from its menu: the folder shown is the folder shown still.
            menu.Pick("Lock");
            Assert.True(VaultHands.Place(vault, "Jewels").IsLocked);
            Assert.NotNull(vault.MenuFor(jewels).Find("Unlock"));
            vault.MenuFor(jewels).Pick("Unlock");

            vault.MenuFor(jewels).Pick("Pin to quick access");
            Assert.Contains(vault.QuickPlaces, place => place.Id == jewels);
            vault.MenuFor(jewels).Pick("Unpin from quick access");
            Assert.DoesNotContain(vault.QuickPlaces, place => place.Id == jewels);
            Assert.Equal(shown, vault.Here);

            // Open goes to it. The vault's top is neither renamed, cut nor deleted - and takes a paste.
            vault.MenuFor(jewels).Pick("Open");
            Assert.Equal(jewels, vault.Here);
            var top = vault.MenuFor((long?)null);
            Assert.Equal(["Paste"], top.Icons.Select(icon => icon.Label));
            Assert.Null(top.Find("Move to folder..."));
        });
    }

    [CorpusFact]
    public void A_character_cards_menu_opens_renames_clones_lists_its_backups_and_deletes()
    {
        if (!program.Ready(changes: true))
            return;

        var home = Main.Home;

        // A character open: the list is not to be picked from, and no card has a menu.
        program.On(() =>
        {
            Assert.False(home.IsBrowsable);
            Assert.True(home.MenuForSelected().IsEmpty);

            // Closed: the list, its cards to be chosen.
            home.CloseCommand.Execute(null);
        });
        Assert.True(program.Until(() => !Main.Session.IsLoaded && home.IsBrowsable && home.Characters.Count > 0), "the list of characters is not shown");

        using (EditPad.InMode(program, "Free"))
        program.On(() =>
        {
            home.SelectedIndex = 0;
            var menu = home.MenuForSelected();
            Assert.Equal("Open; Rename; Clone; Backups", menu.Words());
            Assert.Equal(["Delete"], menu.Icons.Select(icon => icon.Label));
            Assert.True(menu.Row("Open").IsMain);
            Assert.Equal("F2", menu.Row("Rename").Keys);

            // Rename and Clone open the card's name box, each as its own button does.
            menu.Pick("Rename");
            Assert.True(home.IsRenaming && !home.IsCloning);
            home.CancelRenameCommand.Execute(null);
            menu.Pick("Clone");
            Assert.True(home.IsRenaming && home.IsCloning);
            home.CancelRenameCommand.Execute(null);

            // Backups: the list, of this card's save - with no character open.
            Assert.False(home.ShowBackups);
            menu.Pick("Backups");
            Assert.True(home.ShowBackups);
            home.CloseBackupPickerCommand.Execute(null);
        });

        // Where the play mode edits no character, the card is opened, its backups listed, and deleted - no more.
        using (EditPad.InMode(program, "Legit"))
            program.On(() => Assert.Equal("Open; Backups", home.MenuForSelected().Words()));
    }

    [CorpusFact]
    public void A_find_has_Go_to_its_place_first_and_then_what_is_done_to_its_item_from_here()
    {
        if (!program.Ready())
            return;

        var vault = program.On(() => VaultHands.OnRight(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        program.On(() =>
        {
            // The character's: its item's own menu under the one row a find needs - one bold row, not two.
            var ring = VaultHands.Tile(program, Ring, Stash);
            var menu = Page.MenuOfFind(ring.Item);
            Assert.StartsWith("Go to its place; Equip; ", menu.Words());
            Assert.Single(menu.Rows, row => row.IsMain);
            Assert.True(menu.Rows[1].RuleAbove);
            Assert.Equal("Delete", menu.Icons[^1].Label);

            // The vault's, by its key - wherever its folder: withdrawn, moved, cut.
            var tome = VaultHands.Card(vault, Tome);
            var card = vault.MenuOfFind(tome.Key, tome.Row.FolderId, tome.Name);
            Assert.Equal("Go to its place; Withdraw; Move to folder...", card.Words());
            Assert.Equal(["Cut"], card.Icons.Select(icon => icon.Label));
            card.Pick("Cut");
            Assert.True(ItemClipboard.Shared.IsCut(tome));
            ItemClipboard.Shared.Clear();
        });
    }

    [CorpusFact]
    public void A_folders_menu_renames_moves_and_deletes_a_folder_that_is_not_the_one_shown()
    {
        if (!program.Ready(changes: true))
            return;

        var vault = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        program.On(() =>
        {
            try
            {
                // A folder made at the top, to be moved and deleted from afar: a tile of the sheet, selected, its
                // name box open on it - and the page staying at the top.
                vault.NewFolderCommand.Execute(null);
                Assert.Null(vault.Here);
                Assert.True(vault.SelectedFolder is { IsRenaming: true });
                long made = vault.SelectedFolder!.Place.Id!.Value;
                vault.CancelRename(vault.SelectedFolder.Rename!);
                string name = vault.Places.First(place => place.Id == made).Name;
                long charms = VaultHands.Place(vault, "Charms").Id!.Value;

                // Move to...: the picker, the folder shown among its places - the folder moved is not in it.
                vault.MenuFor(made).Pick("Move to folder...");
                Assert.True(vault.IsPickerOpen);
                Assert.Null(vault.Here);
                Assert.DoesNotContain(vault.Targets, target => target.Id == made);
                vault.MoveToCommand.Execute(vault.Targets.First(target => target.Id == charms));
                Assert.Equal(charms, vault.Places.First(place => place.Id == made).Parent);
                Assert.Null(vault.Here);

                // Rename: the name is typed on the folder's tile, so the folder it lies in is gone to first.
                vault.MenuFor(made).Pick("Rename");
                Assert.Equal(charms, vault.Here);
                Assert.True(vault.SelectedFolder is { IsRenaming: true } named && named.Place.Id == made);
                vault.CancelRename(vault.SelectedFolder!.Rename!);

                // Delete, from the top again: an empty folder goes at once, and the page stays where it is.
                vault.GoTo(null);
                vault.MenuFor(made).Pick("Delete");
                Assert.DoesNotContain(vault.Places, place => place.Id == made);
                Assert.Null(vault.Here);
                Assert.Equal($"Deleted {name}.", vault.Notice);

                // One no longer there has no menu.
                Assert.True(vault.MenuFor(made).IsEmpty);
            }
            finally
            {
                VaultHands.Rest(program);
            }
        });
    }

    [CorpusFact]
    public void A_folder_is_opened_on_the_other_page_from_its_menu_the_vault_put_on_both()
    {
        if (!program.Ready())
            return;

        var vault = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        try
        {
            program.On(() =>
            {
                long jewels = VaultHands.Place(vault, "Jewels").Id!.Value;
                Assert.False(vault.IsComparing);
                vault.MenuFor(jewels).Pick("Open on the other page");

                Assert.True(vault.IsComparing);
                Assert.Equal(jewels, Main.RightVault!.Here);
                Assert.Null(vault.Here);
            });
        }
        finally
        {
            program.On(() =>
            {
                VaultHands.Rest(program);
                Main.ShowRightCommand.Execute(MainViewModel.EditMark);
                Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark);
            });
        }
    }

    [CorpusFact]
    public void The_tools_Equip_takes_off_what_is_worn_puts_on_what_is_not_and_says_why_it_cannot()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            const string amulet = "Beast Necklace", charm = "Annihilus";
            var worn = Main.Session.Equipped.First(slot => slot.Item?.Name == amulet).Item!;

            // Nothing selected: dimmed, and saying what it is for.
            Page.ClearSelection();
            Assert.False(Page.CanEquip || Page.CanEquipMerc);
            Assert.Equal("Equip selected item", Page.EquipHint);

            // What is worn: the button takes it off; the mercenary's is not for it.
            Page.ClickWornCommand.Execute(worn);
            Assert.True(Page.EquipTakesOff && Page.CanEquip);
            Assert.Equal("Take off selected item", Page.EquipHint);
            Assert.False(Page.CanEquipMerc);
            Page.EquipSelectedCommand.Execute(null);
            Assert.Equal($"{amulet} taken off.", Main.Session.Status);

            // In a grid: the button puts it on.
            Page.ClickTileCommand.Execute(VaultHands.Tile(program, amulet, "Inventory"));
            Assert.True(Page.CanEquip && !Page.EquipTakesOff);
            Assert.Equal("Equip selected item", Page.EquipHint);
            Page.EquipSelectedCommand.Execute(null);
            Assert.Equal($"Equipped {amulet}.", Main.Session.Status);

            // What nobody wears: dimmed, and the tooltip says why.
            Page.ClickTileCommand.Execute(VaultHands.Tile(program, charm, "Inventory"));
            Assert.False(Page.CanEquip || Page.CanEquipMerc);
            Assert.Equal("Equip selected item" + Environment.NewLine + $"{charm} cannot be equipped.", Page.EquipHint);
            Assert.StartsWith("Equip selected item on mercenary" + Environment.NewLine, Page.EquipMercHint);
            Page.ClearSelection();
        });
    }

    [CorpusFact]
    public void A_vault_finds_Withdraw_takes_it_to_the_character_by_its_key()
    {
        if (!program.Ready(changes: true))
            return;

        var vault = program.On(() => VaultHands.OnRight(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        int stored = 0;
        program.On(() =>
        {
            stored = VaultHands.Held(program);
            var tome = VaultHands.Card(vault, Tome);
            var menu = vault.MenuOfFind(tome.Key, null, tome.Name);

            // The page gone elsewhere: the find's rows act by its key all the same.
            VaultHands.GoTo(vault, "Jewels");
            menu.Pick("Withdraw");
        });

        Assert.True(program.Until(() => VaultHands.Held(program) == stored - 1), "the find was not withdrawn");
    }

    [CorpusFact]
    public void Copy_in_an_items_menu_is_the_tools_Copy_it_says_so_and_hands_the_games_text_over()
    {
        if (!program.Ready(changes: true))
            return;

        using (EditPad.InMode(program, "Free"))
        {
            program.On(() =>
            {
                var section = VaultHands.Section(program);
                section.SelectedPage = VaultHands.Choice(program, Stash);
                var ring = VaultHands.Tile(program, Ring, Stash);
                Page.ClickTileCommand.Execute(ring);

                // The game's text is the view's to put on the system's clipboard: handed over, here caught.
                var was = Page.TextCopied;
                string? text = null;
                Page.TextCopied = copied => text = copied;
                try
                {
                    // The menu's row held the copy and neither said so nor wrote the text.
                    Page.MenuFor(ring).Pick("Copy");
                    Assert.False(string.IsNullOrEmpty(text));
                    Assert.Equal($"{Ring} copied.", Main.Session.Status);
                    Assert.False(ItemClipboard.Shared.IsEmpty);

                    // The button's and the key's command: the same one.
                    text = null;
                    ItemClipboard.Shared.Clear();
                    Page.CopySelectedCommand.Execute(null);
                    Assert.False(string.IsNullOrEmpty(text));
                    Assert.False(ItemClipboard.Shared.IsEmpty);
                }
                finally
                {
                    Page.TextCopied = was;
                    ItemClipboard.Shared.Clear();
                }
            });
        }
    }

    [CorpusFact]
    public void A_locked_pages_Sort_and_Close_gaps_are_dimmed_and_say_why()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var section = VaultHands.Section(program);
            section.SelectedPage = VaultHands.Choice(program, Stash);
            Assert.Null(section.SelectedPage!.ArrangeBlocked);

            Page.TogglePageLockCommand.Execute(VaultHands.Choice(program, Stash));
            try
            {
                // Dimmed, with why - on the pane's buttons (bound to the page's ArrangeBlocked) and in the menu of
                // its empty room. They stood lit and refused on the strip.
                var stash = VaultHands.Choice(program, Stash);
                Assert.Equal("Page locked.", stash.ArrangeBlocked);

                var menu = Page.MenuFor(stash.Grid!.Cells[0]);
                Assert.Equal("[Sort by]; [Close gaps]; Select all", menu.Words());
                Assert.Equal("Page locked.", menu.Row("Sort by").Why);
                Assert.Equal("Page locked.", menu.Row("Close gaps").Why);
            }
            finally
            {
                Page.TogglePageLockCommand.Execute(VaultHands.Choice(program, Stash));
            }

            var open = VaultHands.Choice(program, Stash);
            Assert.Null(open.ArrangeBlocked);
            Assert.Equal("Sort by; Close gaps; Select all", Page.MenuFor(open.Grid!.Cells[0]).Words());
        });
    }

    [CorpusFact]
    public void An_unidentified_item_has_Identify_in_its_menu_on_the_Items_page_and_in_the_vault()
    {
        if (!program.Ready(changes: true))
            return;

        // The Items page, in Legit: identifying is no edit a play mode holds back.
        using (EditPad.InMode(program, "Legit"))
        {
            program.On(() =>
            {
                var section = VaultHands.Section(program);
                section.SelectedPage = VaultHands.Choice(program, Stash);
                var ring = VaultHands.Tile(program, Ring, Stash);
                Page.ClickTileCommand.Execute(ring);
                Assert.Null(Page.MenuFor(ring).Find("Identify"));

                // Unidentified, as a save holds one: the flag, the item whole under it.
                ring.Item.IsIdentified = false;
                Page.MenuFor(ring).Pick("Identify");

                Assert.True(ring.Item.IsIdentified);
                Assert.Equal($"{Ring} identified.", Main.Session.Status);
                Assert.Null(Page.MenuFor(ring).Find("Identify"));

                // The tools' Identify: there only while the selected item is unidentified.
                Assert.False(Page.CanIdentify);
                ring.Item.IsIdentified = false;
                Assert.True(Page.CanIdentify);
                Assert.Equal("Identify", Page.IdentifyHint);
                Page.IdentifySelectedCommand.Execute(null);
                Assert.True(ring.Item.IsIdentified);
                Assert.False(Page.CanIdentify);
            });
        }

        // The vault: put back in its place, identified.
        var vault = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(vault, Tome) is not null), "the vault's cards were not listed");

        program.On(() =>
        {
            VaultHands.GoTo(vault, "Small");
            const string Fortune = "Small Charm of Fortune";
            var charm = VaultHands.Card(vault, Fortune);
            charm.Item.IsIdentified = false;
            Assert.NotNull(program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>().Store!.Replace(charm.Key, charm.Item, out _));
            program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>().SayChanged();

            charm = VaultHands.Card(vault, Fortune);
            Assert.False(charm.Item.IsIdentified);
            vault.ClickTileCommand.Execute(charm);
            Assert.True(vault.CanIdentify);
            vault.MenuFor(charm).Pick("Identify");

            Assert.Equal($"{Fortune} identified.", vault.Notice);
            charm = VaultHands.Card(vault, Fortune);
            Assert.True(charm.Item.IsIdentified);
            Assert.Null(vault.MenuFor(charm).Find("Identify"));
            Assert.False(vault.CanIdentify);
        });
    }
}
