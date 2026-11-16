using System;
using Avalonia.Threading;
using D2SLib.Model.Save;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

// The inventory lock, clearing the materials tab, and storing to, and deleting from, the vault.
public partial class VP_ViewModel_Inv_L
{
    // ---- the inventory lock and the page locks ------------------------------------------------

    /// <summary>
    /// The tools Store all leaves in the inventory: both tomes, the key and the three unlimited versions PD2 adds
    /// (<c>rtp</c>, <c>rid</c>, <c>rkey</c>), and the Horadric Cube.
    /// </summary>
    private static readonly HashSet<string> LockedTools =
        new(["tbk", "ibk", "rtp", "rid", "key", "rkey", "box"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The inventory lock, as a stash page's: nothing of the inventory is picked, dragged, cut or stored, by any road.
    /// Store all's own rule for the charm area and the tools does not hang on it (<see cref="IsCharmOrTool"/>). Kept in
    /// the settings; off by default, as a page's, or a new player could not drag an item out of their own inventory.
    /// </summary>
    public bool InventoryLock
    {
        get => Rules.Settings.InventoryLock;
        set
        {
            if (Rules.Settings.InventoryLock == value)
                return;

            Rules.Settings.InventoryLock = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InventoryLockHint));

            // Locking lets go of any pick it now covers - and what that leaves is the selection.
            if (value)
            {
                foreach (var tile in PickableTiles().Where(tile => tile.IsPicked && IsKept(tile)).ToList())
                    Pick(tile, false);
            }

            SayPicksChanged();
            Normalize();
        }
    }

    /// <summary>
    /// What the inventory's lock says under the pointer. What it keeps, off as well as on:
    /// in the list of pages it is one faint lock among a dozen, and off it said only that it was.
    /// </summary>
    public string InventoryLockHint => InventoryLock
        ? L("Inventory lock on") + Environment.NewLine + L("Items cannot be picked; Store all skips it")
        : L("Inventory lock off");

    [RelayCommand]
    private void ToggleInventoryLock()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        InventoryLock = !InventoryLock;
        Session.Status = InventoryLock ? L("Inventory lock on.") : L("Inventory lock off.");
    }

    /// <summary>
    /// Whether a lock keeps this tile where it is: on a locked page, or in a locked inventory.
    /// </summary>
    private bool IsKept(ItemTileViewModel tile) => IsOnLockedPage(tile) || IsInventoryKept(tile);

    /// <summary>What a refusal says of a kept tile: which lock keeps it.</summary>
    private string KeptSay(ItemTileViewModel tile) =>
        IsOnLockedPage(tile) ? L($"{tile.Name} is on a locked page.") : L($"{tile.Name} is locked in the inventory.");

    /// <summary>The pages locked now, across every section.</summary>
    private IEnumerable<StashPageChoice> LockedPages() =>
        Sections.SelectMany(section => section.Pages ?? []).Where(page => page.IsLocked && page.Grid is not null);

    private bool IsOnLockedPage(ItemTileViewModel tile) =>
        LockedPages().Any(page => page.Grid!.Items.Any(each => ReferenceEquals(each.Item, tile.Item)));

    /// <summary>A page's lock, from the button beside its pane's picker.</summary>
    [RelayCommand]
    private void TogglePageLock(StashPageChoice? page)
    {
        if (page?.LockKey is not { } key)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        bool locked = !page.IsLocked;
        Rules.Settings.LockPage(key, locked);

        // Every section offering this page shows it so; a rebuilt one reads it back from the settings.
        foreach (var each in Sections.SelectMany(section => section.Pages ?? []).Where(each => each.LockKey == key))
            each.IsLocked = locked;
        page.IsLocked = locked;

        // Locking lets go of any pick it now covers, as the inventory lock does.
        if (locked && page.Grid is { } grid)
        {
            foreach (var tile in grid.Items.Where(tile => tile.IsPicked).ToList())
                Pick(tile, false);
        }

        foreach (var section in Sections)
            section.SaySomethingMoved();

        SayArrangeBlocked();
        SayPicksChanged();
        Normalize();
        Session.Status = locked ? L($"{page.Label} locked.") : L($"{page.Label} unlocked.");
    }

    /// <summary>Whether the inventory lock keeps this tile: all of a locked inventory.</summary>
    private bool IsInventoryKept(ItemTileViewModel tile) => InventoryLock && IsInInventory(tile);

    private bool IsInInventory(ItemTileViewModel tile) =>
        Session.GridsNamed("Inventory").FirstOrDefault() is { } inventory && inventory.Items.Any(each => ReferenceEquals(each.Item, tile.Item));

    /// <summary>
    /// Whether Store all leaves this tile: in the inventory, touching the charm area whatever it is, or one of the tools.
    /// Only Store all, the fast dump that is easy to get wrong: Select all and Store is several deliberate steps, and takes
    /// what was selected. A tool on any other page goes with the rest.
    /// </summary>
    private bool IsCharmOrTool(ItemTileViewModel tile)
    {
        if (!IsInInventory(tile) || Session.GridsNamed("Inventory").FirstOrDefault() is not { } inventory)
            return false;

        var item = tile.Item;
        if (LockedTools.Contains(item.Code?.Trim() ?? ""))
            return true;

        var (_, height) = ItemMover.Footprint(item);
        return CharmArea.Touches(new GridSize(inventory.Columns, inventory.Rows), item.Y, height);
    }

    /// <summary>Whether a pane is showing the materials tab - the one place its clear button shows.</summary>
    public bool MaterialsShown => Sections.Any(section => section.ShowsMaterials || section.SecondShowsMaterials);

    /// <summary>Whether the tab holds anything to clear.</summary>
    public bool CanClearMaterials =>
        MaterialsShown && Enumerable.Range(1, PD2SPCompanion.Core.Items.Materials.Every).Any(slot => Stash.MaterialCount(slot) > 0);

    /// <summary>The clear's second click is awaited - the delete button's way of asking twice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClearMaterialsHint))]
    private bool _clearMaterialsArmed;

    public string ClearMaterialsHint => ClearMaterialsArmed
        ? L("Confirm clearing") + Environment.NewLine + L("Click elsewhere to cancel")
        : L("Clear materials tab");

    /// <summary>
    /// Empties the materials tab: every rune, gem and material at once, so asked first, as
    /// every bin asks (a bin deletes a thing and always asks; a cross or a brush takes something off and never does).
    /// The undo takes it back all the same.
    /// </summary>
    [RelayCommand]
    private void ClearMaterials()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        // Refused while the game runs, as every act that ends in a write is: at either press.
        if (_sync?.RefusedWhileRunning(L("clear materials")) == true)
        {
            ClearMaterialsArmed = false;
            return;
        }

        if (!ClearMaterialsArmed)
        {
            ClearMaterialsArmed = true;
            Session.Status = L("Clear materials tab? Click red tick again to confirm.");
            return;
        }

        ClearMaterialsArmed = false;
        int held = Stash.ClearMaterials();
        Session.Status = held > 0 ? L($"Materials tab cleared: {held:N0}.") : L("Materials tab already empty.");
        OnPropertyChanged(nameof(CanClearMaterials));
    }

    /// <summary>
    /// Lights the picks on the tiles there are now, and forgets any pick whose item is gone -
    /// deleted, or the character closed.
    /// </summary>
    private void ShowPicks()
    {
        var present = new HashSet<Item>(ReferenceEqualityComparer.Instance);

        foreach (var tile in PickableTiles())
        {
            present.Add(tile.Item);
            tile.IsPicked = _picked.Contains(tile.Item);
        }

        // What is on the cursor is out of its grid, not gone: a carried group stays picked.
        if (Session.Held is { } held)
            present.Add(held.Item);
        foreach (var each in Session.HeldGroup)
            present.Add(each.Tile.Item);

        // Nor is an item gone that a file still holds: sent from the character to a shared page it is off the
        // character's grids one rebuild before it is on the stash's, and several sent there were forgotten.
        int before = _picked.Count;
        _picked.RemoveWhere(item => !present.Contains(item) && !Session.Holds(item) && !Stash.Holds(item));
        if (_picked.Count != before)
            DisarmDelete();
        SayPicksChanged();
        ShowMoves();
    }

    /// <summary>The vault's gold has changed, or a game started: every purse shows it again.</summary>
    private void SayVaultGold()
    {
        foreach (var purse in Sections.SelectMany(section => section.Purses))
            purse.SayVault();
    }

    /// <summary>
    /// The arrows: up on what goes into the vault with the next save, down on what came out of
    /// it. Lit on the tiles there are now, as the picks are.
    /// </summary>
    private void ShowMoves()
    {
        if (_moves is null)
            return;

        foreach (var tile in PickableTiles())
        {
            tile.IsLeaving = _moves.IsLeaving(tile.Item);
            tile.IsArriving = _moves.IsArriving(tile.Item);
        }
    }

    private void SayPicksChanged()
    {
foreach (var section in Sections)
        {
            section.SayPicks();
        }

        SaySellChoice();
        OnPropertyChanged(nameof(PickedCount));
        OnPropertyChanged(nameof(FactsText));
        OnPropertyChanged(nameof(EquipmentHasPicks));
        OnPropertyChanged(nameof(MercHasPicks));
        OnPropertyChanged(nameof(PageHasPicks));
        Session.PickedCount = PickedCount;
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanStore));
        OnPropertyChanged(nameof(DeleteHint));
        OnPropertyChanged(nameof(StoreHint));
        OnPropertyChanged(nameof(StoreTip));
        SayEquipTools();
    }

    /// <summary>
    /// A copy of the selected item into the vault, into the folder the vault page is showing.
    /// Nothing leaves the character: the vault is a library, and storing is a copy.
    /// </summary>
    [RelayCommand]
    private void StoreToVault() => Store(StoreAs.Open);

    /// <summary>Where Store puts an item: the open folder, sorted (Ctrl), or as a crafting base (Shift).</summary>
    public enum StoreAs { Open, Sorted, CraftingBase }

    /// <summary>Whether the vault list on the other page is carrying cards - asked of it, joined by the main view model.</summary>
    public Func<bool>? VaultCarrying { get; set; }

    public bool IsVaultCarrying => VaultCarrying?.Invoke() == true;

    /// <summary>
    /// Whether the grids take a drop - their cell targets over the tiles, the worn items' tooltips
    /// kept shut: something held here, or cards carried from the vault list.
    /// </summary>
    public bool IsTakingDrops => Session.IsHolding || IsVaultCarrying || IsTabCarrying;

    /// <summary>The vault list began or ended a carry: the grids take drops, or stop - and a slot lit under it goes out.</summary>
    public void VaultCarryChanged()
    {
        OnPropertyChanged(nameof(IsTakingDrops));
        if (!IsVaultCarrying)
        {
            HoverSlot(null);
            HoverArriving(null);
        }
    }

    /// <summary>The first of the vault's carried cards, as an item: the one that lands on the cell under the pointer.</summary>
    public Func<D2SLib.Model.Save.Item?>? VaultCarriedLead { get; set; }

    /// <summary>Cards from the vault let go of on a cell of this page: the vault withdraws them there.</summary>
    public event Action<GridCell>? VaultDropRequested;

    public void DropFromVault(GridCell cell) => VaultDropRequested?.Invoke(cell);

    /// <summary>The item a card carried from the vault holds - asked of it, joined by the main view model.</summary>
    public Func<D2SLib.Model.Save.Item?>? VaultCarriedItem { get; set; }

    /// <summary>
    /// A card from the vault let go of on a worn slot - the character's, or with the flag the
    /// mercenary's: the vault withdraws it and puts it on.
    /// </summary>
    public event Action<D2SLib.Model.Save.ItemLocation, bool>? VaultSlotDropRequested;

    public void DropFromVaultOnSlot(EquipSlotViewModel slot, bool mercenary)
    {
        HoverSlot(null);
        VaultSlotDropRequested?.Invoke(slot.Slot.Location, mercenary);
    }

    /// <summary>The folder a drop on the vault list names, while its store runs; null is the one shown.</summary>
    private long? _storeInto;

    /// <summary>
    /// The store in hand is a drop or a paste, which only ever sends: what is marked already goes where this one says.
    /// Store's own button and menu row, pressed again on what is marked, take the mark back.
    /// </summary>
    private bool _storeSends;

    /// <summary>
    /// An item of the character's stored into a folder of the vault as Store stores it - the play mode's rule
    /// and all: the vault's half of a swap, the piece a vault card pushed off going where the card came from
    /// (as an item of the inventory's put on leaves its cell to the one it
    /// pushes off). Whether it is on its way; refused, Store has said why and it stays on the character.
    /// </summary>
    public bool StoreInto(Item item, long? folder)
    {
        var tiles = AllTiles().Where(tile => ReferenceEquals(tile.Item, item)).Take(1).ToList();
        if (tiles.Count == 0)
            return false;

        _storeInto = folder;
        _storeSends = true;
        try
        {
            Store(StoreAs.Open, tiles);
        }
        finally
        {
            _storeInto = null;
            _storeSends = false;
        }

        return _moves.IsLeaving(item);
    }

    /// <summary>
    /// What is carried, dropped on the vault list on the other page: put back where it came from - it never
    /// moved - and stored as Store stores it, the play mode's rule and all, into
    /// <paramref name="folder"/> (a folder card dropped on) or the folder the list shows. Ctrl and
    /// Shift as on Store: <paramref name="how"/>.
    /// </summary>
    public void StoreCarried(long? folder, StoreAs how)
    {
        if (!Session.IsHolding || Session.Held is not { } held)
            return;

        var items = new HashSet<Item>(ReferenceEqualityComparer.Instance) { held.Item };
        foreach (var each in Session.HeldGroup)
            items.Add(each.Tile.Item);

        IsDragCarrying = false;
        Session.CancelHold();
        Stash.RefreshPages();
        HoverOver(null);

        // The put back rebuilt the page: the tiles are new, found again by their items.
        var tiles = AllTiles().Where(tile => items.Contains(tile.Item)).ToList();

        _storeInto = folder;
        _storeSends = true;
        try
        {
            Store(how, tiles);
        }
        finally
        {
            _storeInto = null;
            _storeSends = false;
        }
    }

    /// <summary>
    /// Store, and its modified clicks:
    /// Ctrl sorts by the rules file, Shift files a magic or rare item a craft takes under Crafting
    /// Bases. What Shift does not take stays - and leaves the picks, as a play mode's refusal does.
    /// </summary>
    public void Store(StoreAs how, List<ItemTileViewModel>? tiles = null)
    {
        List<ItemTileViewModel> going = tiles
            ?? (IsPicking
                ? PickedTiles()
                : Session.SelectedItem is { TabSlot: null } tile ? [tile] : []);

        if (going.Count == 0)
            return;

        _storeHow = how;
        _storeRules = how == StoreAs.Open ? null : SmartSort.Current();
        _sortTo = new Dictionary<Item, IReadOnlyList<string>>(ReferenceEqualityComparer.Instance);

        if (_storeRules is { } rules)
        {
            var notBases = new List<ItemTileViewModel>();

            foreach (var each in going)
            {
                var path = how == StoreAs.Sorted
                    ? SmartSort.Classify(each.Item, rules)
                    : SmartSort.ClassifyAsCraftingBase(each.Item, rules);

                if (path is null)
                    notBases.Add(each);
                else
                    _sortTo[each.Item] = path;
            }

            if (notBases.Count > 0)
            {
                foreach (var each in notBases)
                {
                    going.Remove(each);
                    if (IsPicking)
                        Pick(each, false);
                }

                if (IsPicking)
                {
                    SayPicksChanged();

                    // One left is the selection - what goes is already counted in going.
                    Normalize();
                }

                if (going.Count == 0)
                {
                    _sfxManager?.Method_ButtonSFX_Press();
                    Session.Status = notBases.Count == 1
                        ? L($"{notBases[0].Name} is not a crafting base.")
                        : L("None of these is a crafting base.");
                    return;
                }
            }
        }

        DisarmDelete();
        _sfxManager?.Method_ButtonSFX_Press();

        if (_vaults.Store is not { } store)
        {
            Session.Status = _vaults.Trouble;
            return;
        }

        // Asked of the process at the click, not only of the last look.
        if (_sync?.RefusedWhileRunning(L("store")) == true)
            return;

        // Staged, as every store is: the item leaves the character when it is saved - one in, one out.
        StageStore(going);
        SayRulesProblem();
    }

    /// <summary>
    /// Store all, in the tool row behind Store, with Ctrl for the smart sort and Shift for crafting bases:
    /// everything on every container page - inventory, cube, the character's stash and every
    /// shared page - shown or not; never what is worn, the mercenary's or the golem's. It stood on the section's
    /// header, storing what the panes showed, with Alt for every page and Alt mixed with Ctrl and Shift - read
    /// too often to use. Never the materials tab, whose tiles are counts, and never what a lock keeps - the
    /// inventory, or a locked page. Never the charm area or the tools, the lock on or off.
    /// </summary>
    public void StoreAll(StoreAs how)
    {
        var tiles = StoreAllTiles();

        if (tiles.Count == 0)
        {
            _sfxManager?.Method_ButtonSFX_Press();
            Session.Status = L("Nothing to store.");
            return;
        }

        Store(how, tiles);
    }

    /// <summary>What Store all takes, before a way of storing narrows it.</summary>
    private List<ItemTileViewModel> StoreAllTiles() =>
        [.. Sections
            .SelectMany(section => (section.Pages ?? []).Where(page => page.Grid is not null).SelectMany(page => page.Grid!.Items))
            .Where(tile => !IsKept(tile) && !IsCharmOrTool(tile))];

    /// <summary>
    /// What Store all will take, said at its first press, the inventory by name: a lock left off or on is what a dump goes
    /// wrong by, and this is the moment to see it. Counted as <see cref="Store(StoreAs, List{ItemTileViewModel}?)"/> narrows it.
    /// </summary>
    private string StoreAllAsks(StoreAs how, List<ItemTileViewModel> going)
    {
        int count = going.Count, fromInventory = going.Count(IsInInventory);
        string asks = how switch
        {
            StoreAs.Sorted => Plural(count, "Store 1 item in vault, sorted?", $"Store {count} items in vault, sorted?"),
            StoreAs.CraftingBase => Plural(count, "Store 1 crafting base in vault?", $"Store {count} crafting bases in vault?"),
            _ => Plural(count, "Store 1 item in vault?", $"Store {count} items in vault?"),
        };

        if (InventoryLock)
            asks += " " + L("Inventory locked, stays.");
        else if (fromInventory > 0)
            asks += " " + Plural(fromInventory, "1 from inventory.", $"{fromInventory} from inventory.");

        return asks + " " + L("Click green tick again to confirm.");
    }

    [RelayCommand]
    private void StoreEverything() => StoreAllPress(StoreAs.Open);

    /// <summary>
    /// "Fast inventory storing" asks twice, as the bin does (it acted at once for a day, by a rule since given up): the first press -
    /// plain, Ctrl or Shift - says how and turns the button into a green tick; the second, on the tick, stores
    /// that way; a press anywhere else takes the question back (BinButton).
    /// </summary>
    public void StoreAllPress(StoreAs how)
    {
        if (!StoreAllArmed)
        {
            _sfxManager?.Method_ButtonSFX_Press();

            var going = StoreAllTiles().Where(tile => !ItemBases.IsQuestItem(tile.Item.Code)).ToList();
            if (how != StoreAs.Open && SmartSort.Current() is { } rules)
            {
                going = [.. going.Where(tile => (how == StoreAs.Sorted
                    ? SmartSort.Classify(tile.Item, rules)
                    : SmartSort.ClassifyAsCraftingBase(tile.Item, rules)) is not null)];
            }

            if (going.Count == 0)
            {
                Session.Status = L("Nothing to store.");
                return;
            }

            _storeAllHow = how;
            StoreAllArmed = true;
            Session.Status = StoreAllAsks(how, going);
            return;
        }

        StoreAllArmed = false;
        StoreAll(_storeAllHow);
    }

    /// <summary>Whether the button has been pressed once and waits for its second press.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoreAllHint))]
    private bool _storeAllArmed;

    /// <summary>How the armed button will store: as its first press said.</summary>
    private StoreAs _storeAllHow;

    /// <summary>Store all's tooltip, then its two modified clicks - as Store's.</summary>
    public string StoreAllHint => StoreBlocked
        ? (_sync?.BlockedFor(L("store")) ?? GameSync.Blocked(L("store")))
        : StoreAllArmed
        ? _storeAllHow switch
          {
              StoreAs.Sorted => L("Confirm storing, sorted"),
              StoreAs.CraftingBase => L("Confirm storing crafting bases"),
              _ => L("Confirm storing"),
          } + Environment.NewLine + L("Click elsewhere to cancel")
        : L("Fast inventory storing")
          + Environment.NewLine + L("Store all inventory, cube and stashes items in vault")
          + Environment.NewLine + L("Skips charm area, tomes, keys and cube")
          + Environment.NewLine + L("Ctrl + LMB: sorted")
          + Environment.NewLine + L("Shift + LMB: crafting bases only");

    /// <summary>How the store in hand was asked for.</summary>
    private StoreAs _storeHow;

    /// <summary>The rules the store in hand sorts by; null for a plain store.</summary>
    private SmartSort.Rules? _storeRules;

    /// <summary>Where each item of the store in hand is sorted to. Empty for a plain store.</summary>
    private Dictionary<Item, IReadOnlyList<string>> _sortTo = new(ReferenceEqualityComparer.Instance);

    /// <summary>Where the message says one item went: its folder, or the vault.</summary>
    private string Where(Item one) =>
        _storeHow != StoreAs.Open && _sortTo.TryGetValue(one, out var path) ? string.Join(FolderPlace.Step, path)
        : _storeInto is { } folder && _vaults.Store?.Folders().FirstOrDefault(each => each.Id == folder) is { } named ? named.Name
        : L("vault");

    /// <summary>How several went: into the vault, sorted, or as crafting bases.</summary>
    private string StoredText(int stored, int of) => _storeHow switch
    {
        StoreAs.Sorted => L($"{stored} of {of} stored in vault, sorted."),
        StoreAs.CraftingBase => L($"{stored} of {of} stored in vault as crafting bases."),
        _ => L($"{stored} of {of} stored in vault."),
    };

    /// <summary>
    /// What is wrong with the rules file, after the store's own message: how many lines were skipped, and the first
    /// (one was said, and the rest left unsaid).
    /// </summary>
    private void SayRulesProblem()
    {
        if (_storeRules?.Problems is { Count: > 0 } problems)
        {
            Session.Status += " " + (problems.Count == 1
                ? $"{SmartSort.FileName} {problems[0]}"
                : L($"{SmartSort.FileName}: {problems.Count} lines skipped, the first {problems[0]}"));
        }
    }

    /// <summary>
    /// What the Trade bookmark's Sell tab offers: the one item selected here - worn too - not a
    /// count in the materials tab, and nothing while several are picked (no Mark for sell: the selling part updates
    /// on the single item selected).
    /// </summary>
    public event Action<Item?>? SellChoiceChanged;

    private void SaySellChoice() =>
        SellChoiceChanged?.Invoke(!IsPicking && Session.SelectedItem is { TabSlot: null } tile ? tile.Item : null);

    /// <summary>
    /// Under the Move rule: marks the items to go into the vault with the next save, or - when
    /// every one of them is marked already - takes the marks back. Nothing leaves until then, so
    /// Undo has nothing to repair.
    /// </summary>
    private void StageStore(List<ItemTileViewModel> going)
    {
        // Store pressed again on what is already going takes it back; a drop only ever sends.
        if (!_storeSends && going.All(tile => _moves.IsLeaving(tile.Item)))
        {
            foreach (var tile in going)
                _moves.Keep(tile.Item);

            Session.Status = going.Count == 1
                ? L($"{going[0].Name} stays.")
                : L($"{going.Count} items stay.");
            return;
        }

        // One just withdrawn is saved first, then stores as any other.
        if (going.Any(tile => _moves.IsArriving(tile.Item)))
            _moves.SaveNow?.Invoke();

        int marked = 0;
        string? firstRefusal = null;

        // Told once, after all of them, and before what the strip says: see VaultMoves.Together.
        using (_moves.Together())
        {
            foreach (var each in going)
            {
                var item = each.Item;

                if (_moves.IsLeaving(item))
                {
                    // A drop or a paste names where it goes now: the newest wins, as the strip says.
                    if (_storeSends)
                        _moves.Store(item, _storeInto ?? _vaults.CurrentFolder, _sortTo.GetValueOrDefault(item));

                    marked++;
                    continue;
                }

                // Still the vault's until the save that brings it in: storing it again now would put
                // one row in and take the other out, all for nothing.
                if (_moves.IsArriving(item))
                {
                    firstRefusal ??= L($"{each.Name} has just come out of the vault. Try again.");
                    continue;
                }

                if (Session.CannotTakeOff(item) is { } stuck)
                {
                    firstRefusal ??= $"{each.Name}: {stuck}";
                    continue;
                }

                // A worn belt marked to go: its potions above the first row land now - the save that takes
                // the belt would leave them in rows no belt gives.
                Session.SpillBeltOf(item);

                // The golem's own item, and a worn one that gives it its skill: a store is settled by the save, and
                // the golem would be written standing without it. Dismissed first, both go.
                if (Session.IsGolemItem(item) || (item.Mode == ItemMode.Equipped && Session.GolemGoesWith(item)))
                {
                    firstRefusal ??= $"{each.Name}: {CharacterSession.GolemKeepsItem}";
                    continue;
                }

                // One on the corpse: into the character's first room first - the store takes what the character's
                // own lists hold, at the save (CharacterSession.BringHome).
                if (Session.BringHome(item) is { } noRoom)
                {
                    firstRefusal ??= noRoom;
                    continue;
                }

                // Moving it out is what a lock refuses; a copy leaves it where it is.
                if (IsKept(each))
                {
                    firstRefusal ??= KeptSay(each);
                    continue;
                }

                // The vault does not hand quest items out, so one moved in could never come back.
                if (ItemBases.IsQuestItem(item.Code))
                {
                    firstRefusal ??= L($"{each.Name}: quest item, stays on character.");
                    continue;
                }

                _moves.Store(item, _storeInto ?? _vaults.CurrentFolder, _sortTo.GetValueOrDefault(item));
                marked++;
            }
        }

        if (going.Count == 1)
            Session.Status = firstRefusal ?? L($"{going[0].Name} stored in {Where(going[0].Item)}.");
        else
            Session.Status = StoredText(marked, going.Count)
                             + (firstRefusal is null ? "" : $" {firstRefusal}");
    }

    private readonly ItemLanding _landing;

    /// <summary>
    /// The containers the panes show, first pane first - where a duplicate or an ejected gem lands
    /// when it has nowhere beside its item (ItemLanding). The materials tab takes neither.
    /// </summary>
    private IReadOnlyList<GridViewModel> OpenContainers()
    {
        var open = new List<GridViewModel>();
        if (Sections.FirstOrDefault(section => section.Key == "Containers") is not { } containers)
            return open;

        if (containers.SelectedPage is { IsSeparator: false, IsMaterials: false, Grid: { } first })
            open.Add(first);
        if (containers.ShowSecond && containers.SecondPage is { IsSeparator: false, IsMaterials: false, Grid: { } second }
            && !open.Contains(second))
            open.Add(second);
        return open;
    }

    /// <summary>
    /// A copy of the selected item, selected in its place. A shared stash item stays in the shared
    /// stash, beside it. The character's lands by ItemLanding: beside it where it is in a grid,
    /// else in the container a pane has open - a worn or mercenary item's copy on the stash page
    /// being looked at - else the first room. Asked for by a copy pasted (PasteCopies): it has no
    /// button or key of its own.
    /// </summary>
    private void DuplicateItem()
    {
        if (IsPicking)
        {
            DuplicatePicked();
            return;
        }

        if (Session.SelectedItem is not { TabSlot: null } tile)
            return;

        DisarmDelete();
        _sfxManager?.Method_ButtonSFX_Press();

        // On its own page, beside it - but for a paste that asked for a cell, which the landing rule answers.
        bool shared = Stash.Holds(tile.Item);
        if (shared && _landing.Wish is null)
        {
            Stash.Duplicate(tile);
            return;
        }

        if (Session.IsHolding)
        {
            Session.Status = L("Require held item placed.");
            return;
        }

        if ((shared ? Stash.CopyOf(tile.Item) : Session.CopyOf(tile.Item)) is not { } copy)
        {
            Session.Status = L($"{tile.Name} cannot be copied.");
            return;
        }

        if (_landing.Find(copy, tile.Item) is not { } spot)
        {
            Session.Status = L("No free space.");
            return;
        }

        string? refused = spot.Shared
            ? Stash.Receive(copy, spot.Page, spot.X, spot.Y)
            : Session.Receive(copy, L("No free space."), at: (spot.Page, (spot.X, spot.Y)));
        if (refused is null)
            Session.Status = spot.Shared ? L($"Copied {tile.Name} to shared page {spot.Page}.") : L($"Copied {tile.Name}.");
    }

    /// <summary>
    /// A copy of every pick, each beside its original by the same rule as one alone (ItemLanding), and all of
    /// them or none - where one would find no room, nothing is copied, as the vault's Duplicate takes its picks. One step to undo.
    /// </summary>
    private void DuplicatePicked()
    {
        var picks = PickedTiles();
        if (picks.Count == 0)
            return;

        DisarmDelete();
        _sfxManager?.Method_ButtonSFX_Press();

        if (Session.IsHolding)
        {
            Session.Status = L("Require held item placed.");
            return;
        }

        var batch = new List<(Item Moving, Item Beside)>();
        foreach (var tile in picks)
        {
            // A shared stash pick is copied in the stash's layout, as one alone is (SharedStashSession.CopyOf).
            if ((Stash.Holds(tile.Item) ? Stash.CopyOf(tile.Item) : Session.CopyOf(tile.Item)) is not { } copy)
            {
                Session.Status = L($"{tile.Name} cannot be copied.");
                return;
            }
            batch.Add((copy, tile.Item));
        }

        if (_landing.PlacesFor(batch) is not { } spots)
        {
            Session.Status = L($"No free space for all {picks.Count}.");
            return;
        }

        var own = new List<(Item Item, byte Page, (int X, int Y) Cell)>();
        var shared = new List<(Item Copy, int Page, int X, int Y)>();
        for (int at = 0; at < batch.Count; at++)
        {
            var spot = spots[at];
            if (spot.Shared)
                shared.Add((batch[at].Moving, spot.Page, spot.X, spot.Y));
            else
                own.Add((batch[at].Moving, spot.Page, (spot.X, spot.Y)));
        }

        // All asked before any lands, on both sides, so a refusal leaves nothing copied anywhere.
        string? refused = Stash.RefusesAny(shared.Select(each => each.Copy)) ?? Session.RefusesAny(own.Select(each => each.Item))
            ?? Stash.ReceiveAll(shared) ?? Session.ReceiveAll(own);
        Session.Status = refused ?? Plural(picks.Count, "Copied 1 item.", $"Copied {picks.Count} items.");

        // The rebuilds made new tiles; the picks that are left, if any, are the selection.
        Normalize();
    }

    [RelayCommand]
    private void DeleteItem()
    {
        if (IsPicking)
        {
            DeletePicked();
            return;
        }

        if (Session.SelectedItem is not { TabSlot: null } tile)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        // Locked is locked: the one road out that did not ask.
        if (IsKept(tile))
        {
            Session.Status = KeptSay(tile);
            return;
        }

        // First click only asks. The tick that replaces the bin is what deletes.
        if (!DeleteArmed)
        {
            DeleteArmed = true;
            Session.Status = L($"Delete {tile.Name}? Click red tick again to confirm.");
            return;
        }

        DeleteArmed = false;

        // A shared stash item is the stash's to let go of. Asked of the character, it was
        // "not one of this character's items" and stayed where it was.
        if (IsInSharedStash(tile))
        {
            if (ReferenceEquals(Session.Held, tile))
                Session.CancelHold();

            if (Stash.Release(tile.Item) is null)
            {
                Session.Status = L($"{tile.Name} not in shared stash.");
                return;
            }

            if (ReferenceEquals(Session.SelectedItem, tile))
                Session.SelectedItem = null;

            Session.Status = L($"Deleted {tile.Name}.");
            return;
        }

        Session.Delete(tile);
    }

    /// <summary>
    /// Deletes every pick, asking twice as a single delete does. Each from the file that holds
    /// it - the shared stash's items from the stash, the rest from the character - with one
    /// rebuild of each rather than one an item.
    /// </summary>
    private void DeletePicked()
    {
        var going = PickedTiles();
        if (going.Count == 0)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        if (!DeleteArmed)
        {
            DeleteArmed = true;
            Session.Status = L($"Delete {Selected(going.Count)}? Click red tick again to confirm.");
            return;
        }

        DeleteArmed = false;

        var shared = going.Where(IsInSharedStash).ToList();
        var own = going.Where(tile => !shared.Contains(tile)).ToList();

        int gone = 0;
        foreach (var tile in shared)
        {
            if (Stash.Release(tile.Item, rebuild: false) is not null)
                gone++;
        }

        if (shared.Count > 0)
            Stash.RefreshPages();

        gone += Session.Delete(own);

        Session.Status = gone == going.Count
            ? Plural(gone, "Deleted 1 item.", $"Deleted {gone} items.")
            : L($"Deleted {gone} of {going.Count}.");

        // The rebuilds forgot the picks that went; what is left, if anything, is the selection.
        Normalize();
    }

    /// <summary>
    /// The shared stash's materials tab, drawn as the game draws it. See
    /// <see cref="MaterialsViewModel"/>.
    /// </summary>
    public MaterialsViewModel Materials { get; } = new();

    private void RefreshMaterials()
    {
        OnPropertyChanged(nameof(CanClearMaterials));

        // Only once a stash is open. This page is built while the app starts, before any
        // table is loaded - and naming a tile needs the tables, which took the whole app down
        // on start the first time this ran.
        if (!Stash.IsLoaded)
        {
            Materials.Clear();
            return;
        }

        // New tiles, told again what the search found - after the redraw below.
        Avalonia.Threading.Dispatcher.UIThread.Post(ApplySearch, Avalonia.Threading.DispatcherPriority.Background);

        // A redraw makes new tiles, so a tab tile being looked at is looked at again, as the
        // same slot on its new tile - the panel would otherwise hold one nothing draws.
        int? looking = Session.SelectedItem?.TabSlot;

        Materials.Refresh(Stash.MaterialCount, PD2SPCompanion.Core.Saves.ItemSeason.ForNew(Session.ItemEra ?? Stash.ItemEra ?? ""), season14: Stash.KeepsSeason14,
                          page: Stash.Pages.FirstOrDefault() is { } shared ? (shared.Columns, shared.Rows) : null);

        if (looking is { } slot && Materials.TileFor(slot) is { } again)
            Session.SelectedItem = again;
    }

    /// <summary>
    /// The chosen tab slot, moved out into a grid pane the way moving an item moves all of it:
    /// its whole count, as stacks of the size the game makes, as many as there is room for.
    /// What does not fit stays in the tab and is said.
    /// <para>
    /// The stacks land first and the count comes down after, by what landed - so a pane with
    /// no room, or a file that refuses, takes nothing out of the tab.
    /// </para>
    /// <para>
    /// A stack at a time: a click takes a stack of one, Shift a full stack,
    /// as a drag out of the tab does; it used to take the whole count. Never merged into a stack
    /// already there - stacking may change next season.
    /// </para>
    /// </summary>
    private void TransferOutOfTab(StashPageChoice to, ItemTileViewModel clicked)
    {
        if (clicked.TabSlot is not { } slot || to.Grid is not { } grid)
        {
            Session.Status = L("Require material selected.");
            return;
        }

        string name = PD2SPCompanion.Core.Items.Materials.NameOf(slot);
        int held = Stash.MaterialCount(slot);
        if (held == 0)
        {
            Session.Status = L($"No {name} in the tab.");
            return;
        }

        // Stamped as this season stamps, where the file it lands in may hold it (ItemSeason.ForNew).
        string? era = to.IsCharacter ? Session.ItemEra ?? Stash.ItemEra : Stash.ItemEra ?? Session.ItemEra;
        if (era is null)
        {
            Session.Status = L("Require an item in that stash.");
            return;
        }

        string code = Stash.MaterialCode(slot);
        int stack = Stacks.MaximumFor(code) ?? 1;
        var page = new ItemMover.Occupancy(new GridSize(grid.Columns, grid.Rows), grid.Items.Select(tile => tile.Item));

        int moved = 0;
        int want = Math.Min(held, stack);
        string? refused = null;

        while (moved < want)
        {
            var item = ItemForge.Material(code, Math.Min(stack, want - moved), PD2SPCompanion.Core.Saves.ItemSeason.ForNew(era));

            // A new item gets a new identifier; the file only re-rolls one that clashes.
            item.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);

            if (ItemMover.FirstFreeCell(item, page) is not { } cell)
                break;

            if (Holder(to).Adopt(item, to.Grid!.Page, cell.X, cell.Y, rebuild: false) is { } trouble)
            {
                refused = trouble;
                break;
            }

            var (width, height) = ItemMover.Footprint(item);
            page.Block(cell.X, cell.Y, width, height);
            moved += Stacks.IsStackable(code) ? Stacks.Shown(item.Quantity) : 1;
        }

        if (moved > 0)
            Stash.TakeMaterial(slot, moved);

        Session.RefreshContainers();
        Stash.RefreshPages();

        Session.Status = moved == 0
            ? refused ?? L($"{to.Label} has no room for {name}.")
            : L($"Moved {moved} {name} to {to.Label}.")
              + (refused is null ? "" : $" {refused}");
    }
}
