using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.Input;

using D2SLib.Model.Save;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Character;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The inventory page's right-click menu, and the two tools that say in sight what it holds. A right click on an item was an act until then -
/// put on, and with Ctrl, Alt, Shift and Ctrl + Shift four more, as the game has them for want of a menu (PD2
/// uses those combo keys in game because Diablo II has no native right-click menu). Every one of them is
/// a row of the menu now, and Equip and Equip on mercenary are buttons on the tools line: nothing is reached by
/// a chord alone.
/// </summary>
public sealed partial class VP_ViewModel_Inv_L
{
    /// <summary>
    /// A right press on an item: it is selected, as Explorer's right click selects - unless it is one of several
    /// picked, which the menu then speaks for. So the menu's rows are the tools' own commands, on what is selected.
    /// </summary>
    public void SelectForMenu(ItemTileViewModel tile)
    {
        if (Session.IsHolding || (IsPicking ? tile.IsPicked : ReferenceEquals(Session.SelectedItem, tile)))
            return;

        Select(tile);
    }

    /// <summary>The unidentified among what an item's menu acts on: the item, or the picked it is one of.</summary>
    private IReadOnlyList<ItemTileViewModel> Unidentified(ItemTileViewModel tile) =>
        [.. (MovedWith(tile) > 1 ? PickedTiles() : [tile]).Where(each => each.TabSlot is null && !each.Item.IsIdentified)];

    /// <summary>
    /// Identify, in an item's menu: the unidentified among what the menu acts on are
    /// identified - the flag alone, a save holding an unidentified item whole, its properties rolled. In every
    /// play mode, as Cain does it for nothing; one step of the undo. There is no way back but the undo.
    /// </summary>
    public void Identify(ItemTileViewModel tile)
    {
        var going = Unidentified(tile);
        if (going.Count == 0)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        foreach (var each in going)
            each.Item.IsIdentified = true;

        // Said before anything is marked: the autosave names the step by the strip's last text.
        Session.Status = going is [var one]
            ? L($"{ItemPresenter.Describe(one.Item).Name} identified.")
            : L($"{going.Count} items identified.");

        // Written in place: each tile draws its item again, and the file that holds it is marked - the
        // character's, or the shared stash's for one on its pages.
        foreach (var each in going)
        {
            each.Rebuilt();
            Stash.Redraw(each.Item);
        }

        Stash.MarkEdited();
        Session.MarkEdited();
        Session.SelectedItemChanged();
        SayIdentify();
    }

    // ---- Identify on the tools line, shown only when the selected item is unidentified: otherwise it is underused
    // and wastes space - at the line's end, so nothing moves
    // when it comes and goes.

    /// <summary>The unidentified among what is selected or picked: what the tools' Identify would identify.</summary>
    private IReadOnlyList<ItemTileViewModel> ToIdentify => MenuTile() is { } tile ? Unidentified(tile) : [];

    /// <summary>Shown only while something selected is unidentified.</summary>
    public bool CanIdentify => ToIdentify.Count > 0;

    public string IdentifyHint => ActMenu.Counted(L("Identify"), ToIdentify.Count);

    [RelayCommand]
    private void IdentifySelected()
    {
        if (MenuTile() is { } tile)
            Identify(tile);
    }

    private void SayIdentify()
    {
        OnPropertyChanged(nameof(CanIdentify));
        OnPropertyChanged(nameof(IdentifyHint));
    }

    /// <summary>What the Menu key's menu is for: the item selected, or the first of several picked.</summary>
    public ItemTileViewModel? MenuTile() =>
        IsPicking ? PickedTiles().FirstOrDefault() : Session.SelectedItem;

    /// <summary>The worn slot an item is in - the character's or the mercenary's - or null for one in a grid.</summary>
    private EquipSlotViewModel? SlotOf(ItemTileViewModel tile) =>
        Session.Equipped.Concat(Session.MercEquipped).FirstOrDefault(slot => ReferenceEquals(slot.Item, tile));

    /// <summary>
    /// Whether an item can be put on, on the character or the mercenary: how many slots take it, and where none
    /// does, why - and whether it is gear at all, which a gem or a potion is not: for those the menu has no
    /// Equip to dim. <c>Does</c>: what putting it on the first of the slots does, in the row's words - "Equip",
    /// or what comes off for it (<see cref="EquipDoes"/>).
    /// </summary>
    public (int Slots, string? Why, bool IsGear, string Does) EquipChoice(Item item, bool mercenary)
    {
        var (slots, why) = SlotsFor(item, mercenary);

        // One not the character's yet - on a shared page, in the vault - is asked as the drop asks it (Wear's arriving):
        // offered where the file would then refuse it, Equip ended in a refusal.
        if (slots.Count > 0 && !Session.Holds(item) && Session.Refuses(item) is { } refused)
            return (0, refused, true, L("Equip"));

        if (slots.Count > 0)
            return (slots.Count, null, true, EquipDoes(item, slots[0], mercenary));

        // No slot is its kind's: every slot said "not for that slot", which SlotsFor words as this.
        bool gear = why != L($"{ItemPresenter.Describe(item).Name} cannot be equipped.");
        return (0, why, gear, L("Equip"));
    }

    /// <summary>
    /// What putting an item on a slot does, as the menu's row says it: "Equip" on a bare slot, else what comes
    /// off for it - "Replace Veil of Steel", and both pieces where a weapon held in both hands takes two off.
    /// (The tooltip's strip said it of the right click, until the right click became the menu's.)
    /// </summary>
    public string EquipDoes(Item item, EquipSlotViewModel slot, bool mercenary)
    {
        var names = Session.PushedOffBy(item, slot.Slot.Location, mercenary).Select(off => ItemPresenter.Describe(off).Name).ToList();
        return names switch
        {
            [var one] => L($"Replace {one}"),
            [var one, var two, ..] => L($"Replace {one} and {two}"),
            _ => L("Equip"),
        };
    }

    /// <summary>
    /// An item put on, as a drag onto its slot puts it on - lifted and dropped there: the same checks, the same
    /// words. <paramref name="index"/>: which of the slots that take it (<see cref="SlotAt"/>), 0 the one meant
    /// first, 1 the other - the second ring, the other hand.
    /// </summary>
    public void Equip(ItemTileViewModel tile, bool mercenary = false, int index = 0)
    {
        if (Session.IsHolding || tile.TabSlot is not null)
            return;

        var (slot, why) = SlotAt(tile.Item, mercenary, index);
        if (slot is null)
        {
            _sfxManager?.Method_ButtonSFX_Press();
            Session.Status = why ?? L("Not equipped.");
            return;
        }

        if (IsKept(tile))
        {
            _sfxManager?.Method_ButtonSFX_Press();
            Session.Status = KeptSay(tile);
            return;
        }

        Session.PickUp(tile, IsInSharedStash(tile));
        if (Session.IsHolding)
            DropOnSlot(slot, mercenary);
    }

    /// <summary>
    /// The menu of the item right-clicked - selected by the press, or one of several picked. One item: Equip, in
    /// the other slot where two take it, on the mercenary - or Take off, for one that is worn. Then where it can
    /// go: the vault, the pane across. Delete is an icon at the menu's head. Several: what is done to several.
    /// </summary>
    public ActMenu MenuFor(ItemTileViewModel tile)
    {
        var icons = new List<MenuAct>();
        var rows = new List<MenuAct>();
        bool several = MovedWith(tile) > 1;

        // A count of the materials tab: it goes across, and nothing else is done to it here.
        if (tile.TabSlot is not null)
        {
            if (AcrossFrom(tile) is not null)
                rows.Add(new(ActMenu.Counted(L("To other pane"), SentAcrossWith(tile)), MenuGlyph.SwapAcross, () => SendAcross(tile)));
            return new ActMenu(icons, rows);
        }

        // The Iron Golem's item: the save keeps it with the golem, so the one thing done to it here is the golem's
        // dismissal, which gives it back. It had no menu by the mouse, though its tooltip names one, and by the
        // Menu key the menu of an item not worn, every row of which was refused.
        if (Session.IsGolemItem(tile.Item))
        {
            rows.Add(new(L("Dismiss Iron Golem"), MenuGlyph.Exit, () => DismissGolemCommand.Execute(null)) { IsMain = true });
            return new ActMenu(icons, rows);
        }

        // One on the corpse: all of them taken at once, first - and then what is done to any item, this one alone
        // (one way, out).
        if (Session.IsCorpseItem(tile.Item))
            rows.Add(new(L("Take all from corpse"), MenuGlyph.BattleGear, () => TakeFromCorpseCommand.Execute(null)) { IsMain = true });

        if (SlotOf(tile) is { } worn)
        {
            rows.Add(new(ActMenu.Counted(L("Take off"), TakenOffWith(tile)), MenuGlyph.BattleGear, () => TakeOff(worn)) { IsMain = true });
        }
        else if (!several)
        {
            var own = EquipChoice(tile.Item, mercenary: false);
            if (own.IsGear)
            {
                rows.Add(new(own.Does, MenuGlyph.BattleGear, () => Equip(tile)) { IsMain = true, IsEnabled = own.Slots > 0, Why = own.Why ?? "" });
                if (own.Slots > 1)
                    rows.Add(new(L("Equip in other slot"), MenuGlyph.BattleGear, () => Equip(tile, index: 1)));

                var hired = EquipChoice(tile.Item, mercenary: true);
                rows.Add(new(L("Equip on mercenary"), MenuGlyph.PersonPlus, () => Equip(tile, mercenary: true))
                {
                    IsEnabled = hired.Slots > 0,
                    Why = hired.Why ?? "",
                });
            }
        }

        // Identify, where one of them is not: the row is there only for those.
        if (Unidentified(tile) is { Count: > 0 } unknown)
            rows.Add(new(ActMenu.Counted(L("Identify"), unknown.Count), MenuGlyph.Magnifier, () => Identify(tile)) { RuleAbove = rows.Count > 0 });

        // Store, or for what is marked to leave already the mark taken back - the button's own two meanings.
        bool leaving = (several ? PickedTiles() : [tile]) is { Count: > 0 } going && going.All(each => _moves.IsLeaving(each.Item));
        rows.Add(new(ActMenu.Counted(leaving ? L("Keep out of vault") : L("Store in vault"), MovedWith(tile)), MenuGlyph.ToVault, () => StoreToVaultCommand.Execute(null))
        {
            RuleAbove = rows.Count > 0,
            IsEnabled = CanStore,
            Why = StoreBlocked || StoreLocked ? StoreHint : "",
        });

        // Sorted into the folders the rules name: Ctrl on the tools' Store, in sight here.
        if (!leaving)
        {
            rows.Add(new(ActMenu.Counted(L("Store sorted"), MovedWith(tile)), MenuGlyph.ToVault, () => Store(StoreAs.Sorted)) { IsEnabled = CanStore });

            // And as a crafting base: Shift on the tools' Store, which no row had - nothing is reached by a chord alone.
            rows.Add(new(ActMenu.Counted(L("Store as crafting base"), MovedWith(tile)), MenuGlyph.ToVault, () => Store(StoreAs.CraftingBase)) { IsEnabled = CanStore });
        }

        // Into the compendium, where one of them has a card on the Grail: each that can go, one a card - G the same.
        if (CompendiumRow(tile) is { } compendium)
            rows.Add(compendium);

        // Into the materials tab, where one of them is something the tab counts - the tools' button the same.
        if (MaterialsRow(tile) is { } materials)
            rows.Add(materials);

        if (AcrossFrom(tile) is not null)
            rows.Add(new(ActMenu.Counted(L("To other pane"), SentAcrossWith(tile)), MenuGlyph.SwapAcross, () => SendAcross(tile)));

        // The clipboard's three, then the bin - as Windows' menu heads its own. Copy only where a copy can be
        // pasted: where the play mode makes seconds.
        icons.Add(new(L("Cut"), MenuGlyph.Cut, () => CutCommand.Execute(null)) { Keys = L("Ctrl + X"), IsEnabled = CanCut });
        if (Rules.Duplicates)
            icons.Add(new(L("Copy"), MenuGlyph.Duplicate, () => CopySelectedCommand.Execute(null)) { Keys = L("Ctrl + C"), IsEnabled = CanCut });
        // On a worn item: onto its slot, as a drop there; on any other, where a paste goes.
        icons.Add(new(L("Paste"), MenuGlyph.Paste, () =>
        {
            if (SlotOf(tile) is { } worn)
                PasteOnSlot(worn, Session.MercEquipped.Contains(worn));
            else
                PasteCommand.Execute(null);
        }) { Keys = L("Ctrl + V"), IsEnabled = CanPaste, Why = CanPaste ? Clip.Says : "" });
        // Asked in the menu (MenuAct.Confirm): by the time it runs the answer is yes, as the bin's second press is.
        icons.Add(new(L("Delete"), MenuGlyph.Bin, () =>
        {
            DeleteArmed = true;
            DeleteItemCommand.Execute(null);
        }) { Keys = L("Del"), IsBin = true, Confirm = L("Confirm"), IsEnabled = CanDelete });

        return new ActMenu(icons, rows);
    }

    /// <summary>
    /// The menu of a row of the pages' list: shown in the first pane, as a click on it shows it, or in the second -
    /// which Ctrl + click does without the menu - and its lock.
    /// </summary>
    public ActMenu MenuFor(StashPageChoice page)
    {
        if (SectionOf(page) is not { } section)
            return new ActMenu([], []);

        bool second = section.SecondShown && ReferenceEquals(section.SecondPage, page);
        var rows = new List<MenuAct>
        {
            new(L("Show in pane I"), MenuGlyph.TopPane, () => ShowPageCommand.Execute(page)) { IsMain = true },
            new(second ? L("Close pane II") : L("Show in pane II"), MenuGlyph.SidePane, () => ShowSecond(page)) { Keys = L("Ctrl + LMB") },
        };

        if (page.CanLock)
        {
            rows.Add(new(page.IsLocked ? L("Unlock") : L("Lock"), page.IsLocked ? MenuGlyph.Unlock : MenuGlyph.Lock,
                         () => TogglePageLockCommand.Execute(page)) { RuleAbove = true });
        }

        // The inventory's own lock, which its row has beside it and its menu had not.
        else if (page.IsInventory)
        {
            rows.Add(new(InventoryLock ? L("Unlock") : L("Lock"), InventoryLock ? MenuGlyph.Unlock : MenuGlyph.Lock,
                         () => ToggleInventoryLockCommand.Execute(null)) { RuleAbove = true });
        }

        return new ActMenu([], rows);
    }

    /// <summary>
    /// The menu of a pane's empty room - a cell with nothing on it: Paste, there; Select all, of its section; and
    /// Close gaps, where the page is one that is arranged.
    /// </summary>
    public ActMenu MenuFor(GridCell cell)
    {
        // An empty box of the belt: Paste, into its column (PasteIntoBelt).
        if (!cell.Shared && cell.Page == CharacterSession.BeltPage)
            return new ActMenu([new(L("Paste"), MenuGlyph.Paste, () => PasteOn(null, cell)) { Keys = L("Ctrl + V"), IsEnabled = CanPaste, Why = CanPaste ? Clip.Says : "" }], []);

        var section = Sections.FirstOrDefault(each => each.Pages is not null
            && (each.SelectedPage?.Grid?.Cells.Contains(cell) == true || each.ShowSecond && each.SecondPage?.Grid?.Cells.Contains(cell) == true));
        if (section is null)
            return new ActMenu([], []);

        bool second = section.SelectedPage?.Grid?.Cells.Contains(cell) != true;
        var page = second ? section.SecondPage : section.SelectedPage;

        var icons = new List<MenuAct>
        {
            // At this cell: a paste from a menu lands where the menu was opened (PasteOn).
            new(L("Paste"), MenuGlyph.Paste, () => PasteOn(page, cell)) { Keys = L("Ctrl + V"), IsEnabled = CanPaste, Why = CanPaste ? Clip.Says : "" },
        };
        // In the order the vault's sheet has them (New folder there, first): Sort by, Close gaps, Select all last.
        var rows = new List<MenuAct>();
        if (page?.CanArrange == true)
        {
            // The pane's own Sort and Close gaps buttons, here.
            // Dimmed on a locked page, as the buttons are.
            string why = page.ArrangeBlocked ?? "";
            rows.Add(ActMenu.SortBy(second ? SecondOrder : FirstOrder, why));
            rows.Add(new(L("Close gaps"), MenuGlyph.CloseGaps, () => CloseGapsCommand.Execute(second ? "second" : null))
            {
                IsEnabled = why.Length == 0,
                Why = why,
            });
        }

        rows.Add(new(L("Select all"), MenuGlyph.SelectAll, () => SelectAll(section)) { Keys = L("Ctrl + A") });
        return new ActMenu(icons, rows);
    }

    /// <summary>
    /// The ways Fast inventory storing stores, listed under the arrow joined to it (its Ctrl
    /// and Shift clicks were told in its tooltip alone): each asks as the button's own press asks - the green tick
    /// confirms. A row says what is stored and where it goes, which is all that tells the three apart - "Store
    /// all", "Store all, sorted" and "Store crafting bases only" read as three things with no place said, which was
    /// confusing wording, and under another name than the button's.
    /// </summary>
    public ActMenu StoreAllMenu() => new([],
    [
        new(L("Store all in open folder"), MenuGlyph.ToVault, () => StoreAllPress(StoreAs.Open)) { IsMain = true, Keys = L("LMB") },
        new(L("Store all, sorted into folders"), MenuGlyph.ToVault, () => StoreAllPress(StoreAs.Sorted)) { Keys = L("Ctrl + LMB") },
        new(L("Store only crafting bases, sorted"), MenuGlyph.ToVault, () => StoreAllPress(StoreAs.CraftingBase)) { Keys = L("Shift + LMB") },
    ]);

    /// <summary>
    /// The menu of a find on the Search page (after Explorer's results, which have the
    /// file's own menu and "Open file location"): Go to its place first - what a click on the row does - then
    /// the item's own rows, the item selected for them as any right click selects.
    /// </summary>
    public ActMenu MenuOfFind(Item item)
    {
        var go = new MenuAct(L("Go to its place"), MenuGlyph.Open, () => GoToFound(item)) { IsMain = true };
        if (AllTiles().FirstOrDefault(each => ReferenceEquals(each.Item, item)) is not { } tile)
            return new ActMenu([], [go]);

        SelectForMenu(tile);
        var own = MenuFor(tile);
        return new ActMenu(own.Icons, [go, .. own.Rows.Select((row, at) => row.Lesser(ruleAbove: at == 0))]);
    }

    // ---- into the compendium: a hotkey for transfers to the grail showcase -----

    /// <summary>Stores into the compendium and takes out of it - see CompendiumMoves. Null in the designer.</summary>
    private readonly CompendiumMoves? _deposits;

    /// <summary>What the menu of this item, or with none the key, acts on: the picked it is one of, else it - not a count of the materials tab.</summary>
    private IReadOnlyList<Item> ForCompendium(ItemTileViewModel? tile)
    {
        IEnumerable<ItemTileViewModel> tiles = tile is null
            ? IsPicking ? PickedTiles() : Session.SelectedItem is { } one ? [one] : []
            : MovedWith(tile) > 1 ? PickedTiles() : [tile];
        return [.. tiles.Where(each => each.TabSlot is null).Select(each => each.Item)];
    }

    /// <summary>
    /// The menu's Store in compendium: there where one of what it acts on has a card on the Grail - a unique or a set
    /// piece the tabs have - counting those that can go, dimmed with the first one's reason where none can.
    /// </summary>
    private MenuAct? CompendiumRow(ItemTileViewModel tile)
    {
        if (_deposits is null)
            return null;

        var items = ForCompendium(tile);
        if (!items.Any(item => GrailCatalog.EntryOf(item) is { } entry && GrailCatalog.CardRowsOf(entry.Kind, entry.Row).Count > 0))
            return null;

        var (going, why) = CompendiumGoing(items);
        return new(ActMenu.Counted(L("Store in compendium"), going), MenuGlyph.ArrowDown, () => StoreInCompendium(tile))
        {
            IsEnabled = going > 0,
            Why = why ?? "",
        };
    }

    /// <summary>
    /// How many of these can go into the compendium now, and where none can, the first one's reason - or the game's, which
    /// holds the files a store writes, as it holds Store into the vault.
    /// </summary>
    private (int Going, string? Why) CompendiumGoing(IReadOnlyList<Item> items)
    {
        if (_deposits is null)
            return (0, null);
        if (StoreBlocked)
            return (0, _sync?.BlockedFor(L("store")) ?? GameSync.Blocked(L("store")));

        var why = items.Select(_deposits.WhyNot).ToList();
        int going = why.Count(each => each is null);
        // Nothing selected says nothing, as Equip's tooltip then does.
        return (going, going > 0 ? null : why.FirstOrDefault(each => each is not null));
    }

    /// <summary>
    /// Into the compendium, each that can go of the items picked or the one selected - by the menu, the tools' button or
    /// the key, G. The Grail page need not be up: the compendium is the mode's, and the strip says what went.
    /// </summary>
    public void StoreInCompendium(ItemTileViewModel? tile = null)
    {
        if (_deposits is null || Session.IsHolding || ForCompendium(tile) is not { Count: > 0 } items)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        if (_deposits.Store(items) > 0)
            ClearSelection();
    }

    /// <summary>
    /// What is carried let go of on the Grail page: put
    /// back where it was lifted from - nothing had moved - and then into the compendium, each that can go, as G stores
    /// them. False where nothing was carried.
    /// </summary>
    public bool DropOnCompendium()
    {
        if (_deposits is null || Session.Held is not { } lead)
            return false;

        IReadOnlyList<Item> items = [lead.Item, .. Session.HeldGroup.Select(each => each.Tile.Item)];
        CancelHold();
        if (_deposits.Store(items) > 0)
            ClearSelection();
        return true;
    }

    // The tools' button: there wherever there is a compendium to store into, dimmed with the reason where none can go.

    public bool HasCompendium => _deposits is not null;

    public bool CanStoreInCompendium => CompendiumGoing(ForCompendium(null)).Going > 0;

    public string StoreInCompendiumHint
    {
        get
        {
            // As the vault's Store words it: the item selected, or how many picked.
            var (_, why) = CompendiumGoing(ForCompendium(null));
            string does = IsPicking ? L($"Store {Selected(PickedCount)} in compendium") : L("Store selected item in compendium");
            return ShortcutText.Dimmed(ShortcutText.With(does, L("G")), why);
        }
    }

    [RelayCommand]
    private void StoreInCompendiumSelected() => StoreInCompendium();

    /// <summary>The selection or the compendium changed: the button says what it would do now.</summary>
    private void SayCompendium()
    {
        OnPropertyChanged(nameof(CanStoreInCompendium));
        OnPropertyChanged(nameof(StoreInCompendiumHint));
    }

    // ---- the tools line: Equip or Take off, and Equip on mercenary ----------------------------------------------

    /// <summary>The one item selected, where it is an item and not a count of the materials tab.</summary>
    private ItemTileViewModel? OneSelected => !IsPicking && Session.SelectedItem is { TabSlot: null } tile ? tile : null;

    /// <summary>The item selected is worn: the Equip button takes it off.</summary>
    public bool EquipTakesOff => OneSelected is { } tile && SlotOf(tile) is not null;

    public bool CanEquip => OneSelected is { } tile && Session.KeptApart(tile.Item) is null
                            && (SlotOf(tile) is not null || EquipChoice(tile.Item, mercenary: false).Slots > 0);

    /// <summary>
    /// Why the item selected is neither put on nor taken off, or null: the Iron Golem's while the golem stands,
    /// or one on the corpse - and the tools say so.
    /// </summary>
    private string? SelectedKeptApart => OneSelected is { } tile ? Session.KeptApart(tile.Item) : null;

    /// <summary>What the button does to the item selected, and where it can do nothing, why.</summary>
    public string EquipHint
    {
        get
        {
            if (EquipTakesOff)
                return L("Take off selected item");

            string does = L("Equip selected item");
            if (SelectedKeptApart is { } apart)
                return does + Environment.NewLine + apart;

            return OneSelected is { } tile && EquipChoice(tile.Item, mercenary: false) is { Slots: 0, Why: { } why }
                ? does + Environment.NewLine + why
                : does;
        }
    }

    [RelayCommand]
    private void EquipSelected()
    {
        if (OneSelected is not { } tile)
            return;

        if (SlotOf(tile) is { } worn)
            TakeOff(worn);
        else
            Equip(tile);
    }

    /// <summary>A grid's item selected that the mercenary can wear. Dimmed with none hired, and the tooltip says so.</summary>
    public bool CanEquipMerc => OneSelected is { } tile && SlotOf(tile) is null && Session.KeptApart(tile.Item) is null
                                && EquipChoice(tile.Item, mercenary: true).Slots > 0;

    public string EquipMercHint
    {
        get
        {
            string does = L("Equip selected item on mercenary");
            if (!HasMerc)
                return does + Environment.NewLine + L("No mercenary.");
            if (SelectedKeptApart is { } apart)
                return does + Environment.NewLine + apart;

            return OneSelected is { } tile && SlotOf(tile) is null && EquipChoice(tile.Item, mercenary: true) is { Slots: 0, Why: { } why }
                ? does + Environment.NewLine + why
                : does;
        }
    }

    [RelayCommand]
    private void EquipSelectedOnMerc()
    {
        if (OneSelected is { } tile && SlotOf(tile) is null)
            Equip(tile, mercenary: true);
    }

    /// <summary>The selection changed, or what is worn: the two buttons say what they would do now.</summary>
    private void SayEquipTools()
    {
        OnPropertyChanged(nameof(EquipTakesOff));
        OnPropertyChanged(nameof(CanEquip));
        OnPropertyChanged(nameof(EquipHint));
        OnPropertyChanged(nameof(CanEquipMerc));
        OnPropertyChanged(nameof(EquipMercHint));
        SayIdentify();
        SayClipboard();
        SayCompendium();
        SayMaterialsStore();
    }
}
