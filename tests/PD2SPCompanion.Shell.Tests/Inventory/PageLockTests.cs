using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// A stash page's lock (a page kept for maps stays out of a dump into the vault). The
/// inventory keeps its own lock and every other container page has a page lock; a page locked says so, is
/// remembered in the settings and is locked still when the character is opened again; and "Fast inventory
/// storing", which asks twice and takes every container page, leaves a locked page holding all it held.
/// <para>
/// What the page decides, asked through the lock's and the store's own commands and read back from the pages,
/// the vault and the settings file. That each pane draws the lock that is its page's, and that the lock button
/// and the store button's green tick reach the page, are the UI cases' (units/page-lock.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PageLockTests(Program program)
{
    private const string Locked = "Shared Page 8", Key = "shared:pd2_shared.stash:Page 8";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private EditorSettings Settings => program.Get<EditorSettings>();

    private static bool FileRemembers() => File.ReadAllText(EditorSettings.FilePath).Contains(Key, StringComparison.Ordinal);

    /// <summary>The lock is a setting: a test that turns it on turns it off.</summary>
    private void Unlock() => program.On(() =>
    {
        if (Session.IsLoaded && program.Choice(Locked).IsLocked)
            Page.TogglePageLockCommand.Execute(program.Choice(Locked));
        else
            Settings.LockPage(Key, false);
    });

    /// <summary>A rune handed to the character on a page, that page shown.</summary>
    private D2SLib.Model.Save.Item RuneOn(string page)
    {
        program.Panes(page);
        var rune = PD2SPCompanion.Core.Items.ItemForge.Material("r01", 1, Session.ItemEra!);
        rune.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        Assert.Null(program.Get<ItemLanding>().Receive(rune, "no room", out _));
        Assert.Contains(program.Grid(page).Items, tile => ReferenceEquals(tile.Item, rune));
        return rune;
    }

    private void LockOf(string page, bool on)
    {
        if (program.Choice(page).IsLocked != on)
            Page.TogglePageLockCommand.Execute(program.Choice(page));
    }

    [CorpusFact]
    public void A_locked_page_keeps_what_the_menu_would_send_into_the_materials_tab()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                var rune = RuneOn(Locked);
                int slot = Page.Stash.Takes(rune)!.Value.Slot, count = Page.Stash.MaterialCount(slot);
                program.Panes(Locked, "Materials");
                LockOf(Locked, true);

                Page.SendAcross(program.Grid(Locked).Items.First(tile => ReferenceEquals(tile.Item, rune)));

                Assert.Contains("is on a locked page.", Session.Status);
                Assert.Equal(count, Page.Stash.MaterialCount(slot));
                Assert.Contains(program.Grid(Locked).Items, tile => ReferenceEquals(tile.Item, rune));
            });
        }
        finally
        {
            Unlock();
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void A_filler_cut_and_then_locked_stays_where_it_lies_pasted_on_sockets_and_the_cut_stands()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                program.Panes("Cube");
                var host = PD2SPCompanion.Shell.Tests.Edit.EditPad.GivePlain(program, "cap", 2);
                var rune = RuneOn(Locked);

                Session.SelectedItem = program.Grid(Locked).Items.First(tile => ReferenceEquals(tile.Item, rune));
                Page.CutCommand.Execute(null);
                LockOf(Locked, true);

                Assert.True(Page.PasteIntoSockets(program.Grid("Cube").Items.First(tile => ReferenceEquals(tile.Item, host))));

                Assert.Contains("is on a locked page.", Session.Status);
                Assert.Empty(host.SocketedItems);
                Assert.Contains(program.Grid(Locked).Items, tile => ReferenceEquals(tile.Item, rune));
                Assert.False(PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.IsEmpty);
            });
        }
        finally
        {
            program.On(PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.Clear);
            Unlock();
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void The_inventory_keeps_its_own_lock_and_the_other_container_pages_have_a_page_lock()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            // The inventory's row of the list has the inventory's own lock, not a page's.
            Assert.True(program.Choice("Inventory").IsInventory);
            Assert.False(program.Choice("Inventory").CanLock);
            Assert.StartsWith("Inventory lock", Page.InventoryLockHint);

            Assert.True(program.Choice("Cube").CanLock);
            Assert.Equal("Page lock off", program.Choice("Cube").LockHint);
            Assert.True(program.Choice("Personal Stash").CanLock);
            Assert.True(program.Choice(Locked).CanLock);

            // The tab is counts: nothing there for Store all to take.
            Assert.False(program.Choice("Materials").CanLock);
        });
    }

    [CorpusFact]
    public void A_page_locked_says_so_and_is_remembered_in_the_settings()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                program.Panes(Locked);
                Assert.False(program.Choice(Locked).IsLocked);
                Assert.Equal("Page lock off", program.Choice(Locked).LockHint);
                Assert.False(FileRemembers());

                Page.TogglePageLockCommand.Execute(program.Choice(Locked));
                Assert.Equal("Shared Page 8 locked.", Session.Status);
                Assert.True(program.Choice(Locked).IsLocked);
                Assert.StartsWith("Page lock on", program.Choice(Locked).LockHint);
                Assert.True(Settings.IsPageLocked(Key));
                Assert.True(FileRemembers(), "the lock is not in the settings file");

                Page.TogglePageLockCommand.Execute(program.Choice(Locked));
                Assert.Equal("Shared Page 8 unlocked.", Session.Status);
                Assert.False(program.Choice(Locked).IsLocked);
                Assert.False(FileRemembers(), "the lock is still in the settings file");
            });
        }
        finally
        {
            Unlock();
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void A_page_locked_is_locked_still_when_the_character_is_opened_again()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                Page.TogglePageLockCommand.Execute(program.Choice(Locked));
                Assert.True(program.Choice(Locked).IsLocked);

                // The pages are made anew, and read their locks from the settings.
                Main.Home.SwitchTo(program.SaveOf(Program.Open));
            });

            Assert.True(program.Until(() => Session.IsLoaded && Page.Sections.Any(section => section.Pages is not null), 15000), "the character was not opened again");
            program.On(() =>
            {
                Assert.True(program.Choice(Locked).IsLocked);
                Assert.StartsWith("Page lock on", program.Choice(Locked).LockHint);
                Assert.False(program.Choice("Shared Page 7").IsLocked);
            });
        }
        finally
        {
            Unlock();
        }
    }

    [CorpusFact]
    public void Fast_inventory_storing_asks_twice_and_leaves_a_locked_page_holding_all_it_held()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            int held = 0, stored = 0;
            program.On(() =>
            {
                held = program.Grid(Locked).Items.Count;
                Assert.True(held > 0, "Shared Page 8 holds nothing to begin with");
                Assert.True(program.Grid("Shared Page 1").Items.Count > 0);
                stored = program.InVault();

                Page.TogglePageLockCommand.Execute(program.Choice(Locked));
                Assert.StartsWith("Fast inventory storing", Page.StoreAllHint);

                // The first press only asks.
                Page.StoreEverythingCommand.Execute(null);
                Assert.Matches(@"^Store \d+ items in vault\? \d+ from inventory\. Click green tick again to confirm\.$", Session.Status);
                Assert.True(Page.StoreAllArmed);
                Assert.StartsWith("Confirm storing", Page.StoreAllHint);
                Assert.False(program.Get<VaultMoves>().HasPending);

                // The second, on the tick, stores.
                Page.StoreEverythingCommand.Execute(null);
                Assert.False(Page.StoreAllArmed);
            });

            Assert.True(program.Until(() => program.InVault() > stored && !program.Get<VaultMoves>().HasPending, 20000), "Store all stored nothing");
            program.On(() =>
            {
                Assert.Equal(held, program.Grid(Locked).Items.Count);
                Assert.Empty(program.Grid("Shared Page 1").Items);
                Assert.Empty(program.Grid("Personal Stash").Items);
            });
        }
        finally
        {
            Unlock();
        }
    }

    [CorpusFact]
    public void Store_all_never_takes_the_charm_area_and_the_inventory_lock_blocks_every_store_from_the_inventory()
    {
        if (!program.Ready(changes: true))
            return;

        List<D2SLib.Model.Save.Item> Inventory() => [.. program.Grid("Inventory").Items.Select(tile => tile.Item)];
        bool InCharmArea(D2SLib.Model.Save.Item item) =>
            CharmArea.Holds(new GridSize(program.Grid("Inventory").Columns, program.Grid("Inventory").Rows), item.Y, ItemMover.Footprint(item).Height);
        void StoreAll()
        {
            Page.StoreEverythingCommand.Execute(null);
            Page.StoreEverythingCommand.Execute(null);
        }

        int stored = 0;
        List<D2SLib.Model.Save.Item> held = [];
        program.On(() =>
        {
            held = Inventory();
            Assert.Contains(held, InCharmArea);
            Assert.Contains(held, item => !InCharmArea(item));
            stored = program.InVault();

            // Locked: the Store button says why, for one and for all of the inventory, and a store is refused.
            Page.InventoryLock = true;
            Page.ClickTileCommand.Execute(program.Grid("Inventory").Items.First(tile => !TouchesCharmArea(tile.Item)));
            Assert.False(Page.CanStore);
            Assert.Equal("Inventory locked.", Page.StoreHint);
            Page.Store(VP_ViewModel_Inv_L.StoreAs.Open);
            Assert.EndsWith("is locked in the inventory.", Session.Status);
            Assert.False(program.Get<VaultMoves>().HasPending);

            StoreAll();
        });

        Assert.True(program.Until(() => program.InVault() > stored && !program.Get<VaultMoves>().HasPending, 20000), "Store all stored nothing");
        program.On(() =>
        {
            Assert.Equal(held.Count, Inventory().Count);
            stored = program.InVault();

            // Open: Store all empties the rest of it and leaves the charm area.
            Page.InventoryLock = false;
            StoreAll();
        });

        Assert.True(program.Until(() => program.InVault() > stored && !program.Get<VaultMoves>().HasPending, 20000), "Store all stored nothing with the lock off");
        program.On(() =>
        {
            Assert.Equal(held.Count(InCharmArea), Inventory().Count(InCharmArea));
            Assert.DoesNotContain(Inventory(), item => !InCharmArea(item) && item.Code.Trim() is not ("tbk" or "ibk" or "rtp" or "rid" or "key" or "rkey" or "box"));
        });
    }

    /// <summary>The inventory's lock is a page's lock: no road out, the bin's and a drawn selection's included.</summary>
    [CorpusFact]
    public void A_locked_inventory_lets_nothing_out_and_Store_all_says_it_stays()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var items = program.Grid("Inventory").Items;
            var upper = items.First(tile => !TouchesCharmArea(tile.Item) && tile.Item.Code.Trim() is not ("tbk" or "ibk" or "rtp" or "rid" or "key" or "rkey" or "box"));
            int held = items.Count;
            Page.InventoryLock = true;
            try
            {
                Assert.True(Page.BeginCarry(upper));
                Assert.Contains("is locked in the inventory.", Session.Status);
                Assert.False(Session.IsHolding);

                Page.ClickTileCommand.Execute(upper);
                Page.DeleteItemCommand.Execute(null);
                Assert.Contains("is locked in the inventory.", Session.Status);
                Assert.False(Page.DeleteArmed);

                Page.CutCommand.Execute(null);
                Assert.Contains("is locked in the inventory.", Session.Status);

                Page.SelectAll(Page.Sections.First());
                Assert.DoesNotContain(items, tile => tile.IsPicked);
                Assert.Equal(held, items.Count);

                Page.StoreEverythingCommand.Execute(null);
                Assert.Contains("Inventory locked, stays. Click green tick again to confirm.", Session.Status);
            }
            finally
            {
                Page.StoreAllArmed = false;
                Page.InventoryLock = false;
            }
        });
    }

    /// <summary>Store all is the fast dump; Select all and Store is several deliberate steps, and takes the charm area and the tools.</summary>
    [CorpusFact]
    public void Select_all_with_the_lock_off_picks_the_charm_area_and_the_tools_too()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Inventory");
            var charm = program.Grid("Inventory").Items.First(tile => TouchesCharmArea(tile.Item));
            Page.SelectAll(Page.Sections.First());
            Assert.True(charm.IsPicked);
        });
        program.PanesAsFound();
    }

    /// <summary>The bin asks a page's lock as every other road out does.</summary>
    [CorpusFact]
    public void The_bin_refuses_an_item_on_a_locked_page()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() =>
            {
                program.Panes(Locked);
                var tile = program.Grid(Locked).Items.First();
                Page.ClickTileCommand.Execute(tile);
                Page.TogglePageLockCommand.Execute(program.Choice(Locked));
                Page.DeleteItemCommand.Execute(null);
                Assert.Contains("is on a locked page.", Session.Status);
                Assert.False(Page.DeleteArmed);
            });
        }
        finally
        {
            Unlock();
            program.PanesAsFound();
        }
    }

    private bool TouchesCharmArea(D2SLib.Model.Save.Item item)
    {
        var grid = program.Grid("Inventory");
        var (_, height) = PD2SPCompanion.Core.Characters.ItemMover.Footprint(item);
        return PD2SPCompanion.Core.Characters.CharmArea.Touches(new(grid.Columns, grid.Rows), item.Y, height);
    }
}
