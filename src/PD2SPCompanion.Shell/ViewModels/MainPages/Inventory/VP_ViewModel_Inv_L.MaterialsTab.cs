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

// Carrying out of the materials tab.
public partial class VP_ViewModel_Inv_L
{
    // ---- carrying out of the materials tab ------------------------------

    /// <summary>The tab slot a drag is carrying one of, or null. Nothing leaves the tab until it lands.</summary>
    private int? _tabCarry;

    /// <summary>Whether a drag is carrying a material out of the tab.</summary>
    public bool IsTabCarrying => _tabCarry is not null;

    /// <summary>What the ghost draws while carrying out of the tab: the material's own tile.</summary>
    public IReadOnlyList<GhostPart> TabGhostParts =>
        _tabCarry is { } slot && Materials.TileFor(slot) is { } tile ? [new GhostPart(tile, 0, 0)] : [];

    private void TabCarryChanged()
    {
        OnPropertyChanged(nameof(IsTabCarrying));
        OnPropertyChanged(nameof(TabGhostParts));
        OnPropertyChanged(nameof(IsTakingDrops));
    }

    /// <summary>The drag out of the tab let go of with nothing taking it: nothing moved.</summary>
    public void EndTabCarry()
    {
        if (_tabCarry is null)
            return;

        _tabCarry = null;
        TabCarryChanged();
    }

    /// <summary>
    /// The drag out of the tab let go of on a cell: a stack of one there - a full stack with
    /// <paramref name="full"/> (Shift) - on an empty cell of a grid; on a stack of its kind, onto it (Stacking). The
    /// stack lands first and the count comes down after, so a refusal takes nothing.
    /// <para>
    /// Let go of on an item with an empty socket - <paramref name="onItem"/>, lying in a grid or worn - a gem
    /// or a rune goes into it, one out of the tab (Socketing).
    /// </para>
    /// </summary>
    public void DropFromTab(GridCell? cell, bool full, ItemTileViewModel? onItem = null)
    {
        if (_tabCarry is not { } slot)
            return;

        if (TrySocket(onItem ?? HostAt(cell)) || TryStackFromTab(onItem ?? HostAt(cell), full))
            return;

        EndTabCarry();

        // A belt column - on its free box, or on a potion in it, whose tile hides the cell (a
        // rejuvenation out of the tab could not be dropped on the belt).
        int? beltColumn = cell is { Shared: false, Page: CharacterSession.BeltPage } ? cell.Column
            : cell is null && onItem?.Item is { Mode: ItemMode.Belt } inBelt ? inBelt.X % PD2SPCompanion.Core.Characters.Belts.Columns
            : null;

        // Back on the tab, or nowhere a grid takes it: nothing moved.
        if (beltColumn is null && (cell is null || Materials.Owns(cell)))
            return;

        string name = PD2SPCompanion.Core.Items.Materials.NameOf(slot);
        if (Stash.MaterialCount(slot) == 0)
        {
            Session.Status = L($"No {name} in the tab.");
            return;
        }

        // Stamped as this season stamps, where the file it lands in may hold it (ItemSeason.ForNew).
        bool shared = beltColumn is null && cell is { Shared: true };
        string? era = shared ? Stash.ItemEra ?? Session.ItemEra : Session.ItemEra ?? Stash.ItemEra;
        if (era is null)
        {
            Session.Status = L("Require an item in that stash.");
            return;
        }

        // A belt box holds one, Shift or not.
        string code = Stash.MaterialCode(slot);
        int count = full && beltColumn is null ? Math.Min(Stash.MaterialCount(slot), Stacks.MaximumFor(code) ?? 1) : 1;
        var item = ItemForge.Material(code, count, PD2SPCompanion.Core.Saves.ItemSeason.ForNew(era));
        item.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);

        if (beltColumn is { } column)
        {
            if (NewIntoBelt([item], column) is { } refused)
            {
                Session.Status = refused;
                return;
            }

            _sfxManager?.Method_ButtonSFX_Press();
            Stash.TakeMaterial(slot, count);
            Stash.RefreshPages();
            HoverOver(null);
            Session.Status = L($"Moved {count} {name} to the belt.");
            return;
        }

        if (cell is null)
            return;

        if (RoomAt(cell, item) is { } no)
        {
            Session.Status = no;
            return;
        }

        if (Holder(cell.Shared).Adopt(item, cell.Page, cell.Column, cell.Row, rebuild: false) is { } trouble)
        {
            Session.Status = trouble;
            return;
        }

        _sfxManager?.Method_ButtonSFX_Press();
        Stash.TakeMaterial(slot, count);

        Session.RefreshContainers();
        Stash.RefreshPages();
        string where = cell.Shared ? NameOfSharedPage(cell.Page) : L("character");
        Session.Status = L($"Moved {count} {name} to {where}.");
    }

    /// <summary>
    /// The item clicked, moved from a grid pane into the tab: it goes to its own slot, and its
    /// whole stack is counted.
    /// </summary>
    private void TransferIntoTab(StashPageChoice from, ItemTileViewModel clicked)
    {
        if (clicked is not { TabSlot: null } tile
            || from.Grid is not { } grid
            || !grid.Items.Any(each => ReferenceEquals(each.Item, tile.Item)))
        {
            Session.Status = L($"Require item selected in {from.Label}.");
            return;
        }

        // A lock keeps it from every move, into the tab as into the other pane (PutInTab).
        if (IsKept(tile))
        {
            Session.Status = KeptSay(tile);
            return;
        }

        if (Stash.Takes(tile.Item) is not { } taken)
        {
            Session.Status = L($"Materials tab does not take {tile.Name}.");
            return;
        }

        string name = PD2SPCompanion.Core.Items.Materials.NameOf(taken.Slot);
        if (Stash.MaterialCount(taken.Slot) + taken.Quantity > PD2SPCompanion.Core.Items.Materials.FieldMaximum)
        {
            Session.Status = L($"Materials tab max: {PD2SPCompanion.Core.Items.Materials.FieldMaximum:N0} {name}.");
            return;
        }

        // Out of its file first, then into the count - the order that cannot lose it.
        if (ItemLift.Of(Holder(from), tile.Item) is not { } lift)
        {
            Session.Status = L("Item not moved.");
            return;
        }

        Stash.AddMaterial(taken.Slot, taken.Quantity);
        lift.Done();

        Session.RefreshContainers();
        Stash.RefreshPages();

        Session.Status = L($"Moved {taken.Quantity} {name} to materials tab.");
    }

    // ---- into the tab from where it lies: any item the tab can store offers it on a right click, and the tools' button,
    // always available, seeks all qualified items from the inventory and the shared pages and dumps them in the tab - the
    // new season's in-game button does the same, whose name and key both take: "Deposit materials", R ----------------------

    /// <summary>
    /// What the item menu's Deposit materials acts on: the picked its item is one of, else that item - those of them
    /// the tab takes, never a count of the tab itself.
    /// </summary>
    private List<ItemTileViewModel> ForMaterials(ItemTileViewModel tile) =>
        Stash.IsLoaded
            ? [.. (MovedWith(tile) > 1 ? PickedTiles() : [tile]).Where(each => each.TabSlot is null && Stash.Takes(each.Item) is not null)]
            : [];

    /// <summary>
    /// What the tools' button sweeps: every item the tab takes on every container page - inventory, cube, the character's
    /// stash and every shared page, shown or not - as Store all walks them; never what is worn, the mercenary's, the
    /// golem's or the belt's, and never what a lock keeps.
    /// </summary>
    private List<ItemTileViewModel> AllMaterials() =>
        Stash.IsLoaded
            ? [.. Sections.SelectMany(section => (section.Pages ?? []).Where(page => page.Grid is not null).SelectMany(page => page.Grid!.Items))
                .Where(tile => tile.TabSlot is null && !IsKept(tile) && Stash.Takes(tile.Item) is not null)]
            : [];

    /// <summary>The tools' button and R: always there, dimmed only where there is no shared stash with a tab to store into.</summary>
    public bool CanStoreInMaterials => Stash.IsLoaded;

    public string StoreInMaterialsHint =>
        ShortcutText.Dimmed(ShortcutText.With(L("Deposit materials"), L("R")), Stash.IsLoaded ? null : L("No shared stash open."));

    /// <summary>The tools' button, and R on the page.</summary>
    [RelayCommand]
    public void StoreInMaterialsSelected()
    {
        if (Session.IsHolding)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        if (AllMaterials() is not { Count: > 0 } tiles)
        {
            Session.Status = L("No materials to deposit.");
            return;
        }

        PutInTab(tiles);
    }

    /// <summary>The item menu's row: there where one of what it acts on is taken by the tab, counting those.</summary>
    private MenuAct? MaterialsRow(ItemTileViewModel tile) =>
        ForMaterials(tile) is { Count: > 0 } going
            ? new(ActMenu.Counted(L("Deposit materials"), going.Count), MenuGlyph.Deposit, () => StoreInMaterials(tile))
            : null;

    /// <summary>The shared stash came or went: the button acts or is dimmed.</summary>
    private void SayMaterialsStore()
    {
        OnPropertyChanged(nameof(CanStoreInMaterials));
        OnPropertyChanged(nameof(StoreInMaterialsHint));
    }

    /// <summary>The item menu's Deposit materials: each the tab takes of the menu's item, or of the picked it is one of.</summary>
    public void StoreInMaterials(ItemTileViewModel tile)
    {
        if (Session.IsHolding || ForMaterials(tile) is not { Count: > 0 } tiles)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        PutInTab(tiles);
    }

    /// <summary>
    /// Each of these out of its file and onto its count - from the character's grids, the shared stash's pages alike. One
    /// whose count would pass the tab's most, or that a lock keeps, stays where it is; the strip says how many went.
    /// </summary>
    private void PutInTab(IReadOnlyList<ItemTileViewModel> tiles)
    {
        int moved = 0, total = 0, left = 0;
        string? lastName = null, kept = null;
        foreach (var each in tiles)
        {
            // A lock keeps it where it is - a locked page, the inventory lock - as it keeps it from every move.
            if (IsKept(each))
            {
                kept ??= KeptSay(each);
                left++;
                continue;
            }

            // The count as it stands, every one moved before this already on it.
            var (slot, quantity) = Stash.Takes(each.Item)!.Value;
            if (Stash.MaterialCount(slot) + quantity > PD2SPCompanion.Core.Items.Materials.FieldMaximum)
            {
                left++;
                continue;
            }

            // Out of its file first, then into the count - the order that cannot lose it.
            if (ItemLift.Of(Holder(IsInSharedStash(each)), each.Item) is not { } lift)
            {
                left++;
                continue;
            }

            Stash.AddMaterial(slot, quantity);
            lift.Done();
            moved++;
            total += quantity;
            lastName = PD2SPCompanion.Core.Items.Materials.NameOf(slot);
        }

        if (moved > 0)
            ClearSelection();

        Session.RefreshContainers();
        Stash.RefreshPages();

        Session.Status = moved == 0
            ? kept ?? L($"Materials tab max: {PD2SPCompanion.Core.Items.Materials.FieldMaximum:N0}. Nothing moved.")
            : moved == 1 && left == 0
            ? L($"Moved {total} {lastName} to materials tab.")
            : left == 0
            ? L($"Moved {total} materials to materials tab.")
            : L($"Moved {total} materials to materials tab; {left} not moved.");
    }

    /// <summary>
    /// Putting the held item on the tab: it goes to its own slot wherever on the tab it was
    /// dropped, the way the game files a rune among the runes. Anything the tab does not keep
    /// is refused where it is, still in hand.
    /// </summary>
    private void PutMaterial()
    {
        if (Session.Held is not { } tile)
            return;

        if (Stash.Takes(tile.Item) is not { } taken)
        {
            Session.Status = L($"Materials tab does not take {tile.Name}.");
            return;
        }

        string name = PD2SPCompanion.Core.Items.Materials.NameOf(taken.Slot);
        if (Stash.MaterialCount(taken.Slot) + taken.Quantity > PD2SPCompanion.Core.Items.Materials.FieldMaximum)
        {
            Session.Status = L($"Materials tab max: {PD2SPCompanion.Core.Items.Materials.FieldMaximum:N0} {name}.");
            return;
        }

        _sfxManager?.Method_ButtonSFX_Press();

        // Out of whichever file holds it, then into the count - the order that cannot lose
        // it, since the count only rises once the item is really gone.
        bool shared = Session.HeldIsShared;
        if (ItemLift.Of(Holder(shared), tile.Item) is not { } lift)
        {
            Session.Status = L("Item not moved.");
            return;
        }

        Stash.AddMaterial(taken.Slot, taken.Quantity);
        lift.Done();

        Session.PutDown();
        Session.Status = L($"Moved {taken.Quantity} {name} to materials tab.");

        Session.RefreshContainers();
        Stash.RefreshPages();
        HoverOver(null);
    }

    /// <summary>
    /// A held group put on the tab: every one to its own slot, or - where the tab does not keep one of
    /// them, or a slot would pass its most - none, all still in hand, as a group dropped on a grid is all
    /// or nothing. Asked of the whole group before anything moves:
    /// two stacks of one rune count together against that rune's slot.
    /// </summary>
    private void PutMaterials()
    {
        if (Session.Held is not { } anchor)
            return;

        var carried = new List<(ItemTileViewModel Tile, bool Shared)> { (anchor, Session.HeldIsShared) };
        carried.AddRange(Session.HeldGroup.Select(each => (each.Tile, each.Shared)));

        var taken = new List<(D2SLib.Model.Save.Item Item, bool Shared, int Slot, int Quantity)>();
        var bySlot = new Dictionary<int, int>();
        foreach (var (tile, shared) in carried)
        {
            if (Stash.Takes(tile.Item) is not { } takes)
            {
                Session.Status = L($"Materials tab does not take {tile.Name}.");
                return;
            }

            taken.Add((tile.Item, shared, takes.Slot, takes.Quantity));
            bySlot[takes.Slot] = bySlot.GetValueOrDefault(takes.Slot) + takes.Quantity;
        }

        foreach (var (slot, quantity) in bySlot)
        {
            if (Stash.MaterialCount(slot) + quantity > PD2SPCompanion.Core.Items.Materials.FieldMaximum)
            {
                Session.Status = L($"Materials tab max: {PD2SPCompanion.Core.Items.Materials.FieldMaximum:N0} {PD2SPCompanion.Core.Items.Materials.NameOf(slot)}.");
                return;
            }
        }

        _sfxManager?.Method_ButtonSFX_Press();

        // Each out of its file, then into the count - the order that cannot lose it. One the file will
        // not let go of stays where it is; the rest go.
        int moved = 0, total = 0;
        foreach (var each in taken)
        {
            if (ItemLift.Of(Holder(each.Shared), each.Item) is not { } lift)
                continue;

            Stash.AddMaterial(each.Slot, each.Quantity);
            lift.Done();
            moved++;
            total += each.Quantity;
        }

        Session.PutDown();
        Session.Status = moved == taken.Count
            ? L($"Moved {total} materials to materials tab.")
            : L($"Moved {total} materials to materials tab; {taken.Count - moved} not moved.");

        Session.RefreshContainers();
        Stash.RefreshPages();
        HoverOver(null);
    }

    /// <summary>
    /// Clicking an item selects it, alone, and the editor opens on it - Explorer's click. It never lifts it:
    /// moving takes a drag. Several selected become this one, and clicking the one selected
    /// keeps it. What the inventory lock keeps is selected and edited like anything else; it
    /// only never moves. A materials count is looked at the same way, its count changed in the
    /// panel.
    /// </summary>
    [RelayCommand]
    private void ClickTile(ItemTileViewModel tile) => Select(tile);

    /// <summary>One item selected, and nothing else - the click's, and a refused pick's.</summary>
    private void Select(ItemTileViewModel tile)
    {
        // A click never comes mid-drag; should one, what is carried is the drag's to put down.
        if (Session.IsHolding)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        if (IsPicking)
            IsPicking = false;

        Session.SelectedItem = tile;
    }

    /// <summary>
    /// Whether a tile is one of the shared stash's rather than the character's.
    /// <para>
    /// Asked of the stash itself rather than carried on the tile, because the tile is built
    /// by whichever session owns it and a flag set in two places is a flag that can
    /// disagree. Nine pages, so the search costs nothing worth saving.
    /// </para>
    /// </summary>
    private bool IsInSharedStash(ItemTileViewModel tile) =>
        Stash.Pages.Any(page => page.Items.Contains(tile));

    /// <summary>
    /// Clicking something worn: selected alone, as a container's item is. Dragged, it comes off
    /// into a container - the character's or the mercenary's. Nothing is
    /// ever put on.
    /// </summary>
    [RelayCommand]
    private void ClickWorn(ItemTileViewModel? tile)
    {
        if (tile is not null)
            Select(tile);
    }

    /// <summary>
    /// Putting the held item down on a cell.
    /// <para>
    /// The cell says which file it is in, and that decides what putting it down means. Both
    /// in the character: the item only changes where it says it is. Across the two: it is
    /// let go by one file and taken in by the other, exactly as the move buttons do it -
    /// which is why both go through <see cref="ItemLift"/> rather than each having a way
    /// of their own.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void PlaceAt(GridCell cell)
    {
        if (Session.Held is not { } tile)
            return;

        if (Materials.Owns(cell))
        {
            PutMaterial();
            return;
        }

        _sfxManager?.Method_ButtonSFX_Press();

        // The belt: its own rule for which box (see Belts), not the cell under the pointer.
        if (!cell.Shared && cell.Page == CharacterSession.BeltPage)
        {
            DropIntoBelt(cell.Column);
            return;
        }

        // Neither end is the shared stash: the character's own business, and it has said
        // how to do it since long before the stash was here.
        if (!cell.Shared && !Session.HeldIsShared)
        {
            Session.PlaceHeld(cell.Page, cell.Column, cell.Row);
            HoverOver(null);
            return;
        }

        if (RoomAt(cell, tile.Item) is { } no)
        {
            Session.Status = no;
            return;
        }

        // Asked before it leaves: nothing is let go of until the file it is going to has agreed.
        if ((cell.Shared ? Stash.Refuses(tile.Item) : Session.Refuses(tile.Item)) is { } refused)
        {
            Session.Status = refused;
            return;
        }

        // Out of one file and into the other. Moving within the shared stash goes the same
        // way: the page an item is on is written into the item, so it is re-placed rather
        // than nudged.
        if (ItemLift.Of(Holder(Session.HeldIsShared), tile.Item) is not { } lift)
        {
            Session.Status = L("Item not moved.");
            return;
        }

        if (lift.Into(Holder(cell.Shared), cell.Page, cell.Column, cell.Row) is { } trouble)
        {
            // Back where it came from rather than nowhere at all: a file's era can turn on the item that just left it.
            lift.Back();
            Session.Status = trouble;
            return;
        }

        lift.Done();
        Session.PutDown();
        string where = cell.Shared ? NameOfSharedPage(cell.Page) : L("character");
        Session.Status = L($"Moved {tile.Name} to {where}.");

        Session.RefreshContainers();
        Stash.RefreshPages();
        HoverOver(null);
    }

    private static string NameOfSharedPage(byte page) => L($"shared page {page}");

    /// <summary>Whether an item fits at a cell, or why it does not.</summary>
    private string? RoomAt(GridCell cell, D2SLib.Model.Save.Item item)
    {
        var contents = ContentsOf(cell.Shared, cell.Page).Where(each => !ReferenceEquals(each, item));
        var size = SizeOf(cell.Shared, cell.Page);

        var check = ItemMover.CanPlace(item, contents, size, cell.Column, cell.Row);
        return check.Allowed ? null : check.Reason ?? L("Does not fit there.");
    }

    /// <summary>The file a shared cell or item is in, or the character.</summary>
    private IItemHolder Holder(bool shared) => shared ? Stash : Session;

    /// <summary>The file a pane shows a page of.</summary>
    private IItemHolder Holder(StashPageChoice pane) => Holder(!pane.IsCharacter);

    private GridCell? _hovered;

    /// <summary>
    /// Lights the block a card carried from the vault would take from this cell, green where it would go there
    /// and red where it would not (between the inventory and the vault the landing's green mark was missing;
    /// only a worn slot lit until then). The first card's: the rest of several find
    /// their own room. Nothing over the belt or the materials tab, which take no drop from the vault's cells.
    /// </summary>
    public void HoverArriving(GridCell? cell)
    {
        if (ReferenceEquals(_hovered, cell))
            return;

        _hovered = cell;

        foreach (var grid in Showing())
        {
            foreach (var each in grid.Cells)
                each.IsTarget = false;
        }

        if (cell is null || VaultCarriedLead?.Invoke() is not { } arriving || Materials.Owns(cell)
            || (!cell.Shared && cell.Page == CharacterSession.BeltPage))
        {
            return;
        }

        bool allowed = RoomAt(cell, arriving) is null;
        var (wide, tall) = ItemMover.Footprint(arriving);

        foreach (var grid in Showing())
        {
            if (grid.Page != cell.Page || grid.Shared != cell.Shared)
                continue;

            foreach (var lit in grid.Cells)
            {
                if (lit.Column < cell.Column || lit.Column >= cell.Column + wide || lit.Row < cell.Row || lit.Row >= cell.Row + tall)
                    continue;

                lit.IsTarget = true;
                lit.IsAllowed = allowed;
                lit.IsIdle = false;
            }
        }
    }

    /// <summary>
    /// Lights the block the held item would occupy from this cell, in green when it would
    /// go there and red when it would not.
    /// </summary>
    public void HoverOver(GridCell? cell)
    {
        if (ReferenceEquals(_hovered, cell))
            return;

        _hovered = cell;

        foreach (var grid in Showing())
        {
            foreach (var each in grid.Cells)
                each.IsTarget = false;
        }

        foreach (var each in Materials.Cells)
            each.IsTarget = false;

        // A count out of the tab lights only the item it would be socketed into; a gem out of the Sockets row that
        // item, or else where it would land.
        if (cell is not null && (IsTabCarrying || RowFrom is not null))
        {
            if (!LightHost(cell) && RowFrom is not null)
                LightRowLanding(cell);
            return;
        }

        // The compendium's item off its Grail card: where it would land, as an item - it is never socketed.
        if (cell is not null && CompendiumCarrying)
        {
            LightRowLanding(cell);
            return;
        }

        if (cell is null || Session.Held is not { } held)
            return;

        // Over the belt: the boxes its rule gives, one item or the lot.
        if (!cell.Shared && cell.Page == CharacterSession.BeltPage)
        {
            HoverBelt(cell);
            return;
        }

        // A group: every cell it would take, green where the lot fits, red where it does not.
        if (Session.HeldGroup.Count > 0)
        {
            var (plan, refused) = PlanGroup(cell, pack: false);
            if (plan.Count == 0)
                plan.Add(new Placement(held, Session.HeldIsShared, cell.Column, cell.Row));

            foreach (var each in plan)
            {
                var (wide, tall) = ItemMover.Footprint(each.Tile.Item);
                foreach (var grid in Showing())
                {
                    if (grid.Page != cell.Page || grid.Shared != cell.Shared)
                        continue;

                    foreach (var lit in grid.Cells)
                    {
                        if (lit.Column < each.X || lit.Column >= each.X + wide || lit.Row < each.Y || lit.Row >= each.Y + tall)
                            continue;

                        lit.IsTarget = true;
                        lit.IsAllowed = refused is null;
                        lit.IsIdle = false;
                    }
                }
            }

            return;
        }

        // Over the tab: light the slot the held item would go to, green, or the cell under
        // the pointer red when the tab does not keep it.
        if (Materials.Owns(cell))
        {
            int? home = Stash.Takes(held.Item)?.Slot;
            foreach (var each in Materials.Cells)
            {
                bool lit = home is { } slot ? Materials.SlotAt(each) == slot : ReferenceEquals(each, cell);
                if (!lit)
                    continue;

                each.IsTarget = true;
                each.IsAllowed = home is not null;
                each.IsIdle = false;
            }
            return;
        }

        // Over an item with an empty socket, a gem, a rune or a jewel: the item lights, not the cells the
        // carried one would cover.
        if (LightHost(cell))
            return;

        bool allowed = RoomAt(cell, held.Item) is null;
        int width = held.View.GridWidth;
        int height = held.View.GridHeight;

        // Fits, but a charm landing outside the charm section will not do anything. Only
        // the character's own inventory has a charm line; the shared stash has none.
        bool idle = allowed && !cell.Shared
                    && Session.WouldBeIdleCharm(held, cell.Page, cell.Row, height);

        foreach (var grid in Showing())
        {
            // Both halves, because page numbers are only unique within a file: the first
            // shared page is 1 and so is the character's inventory. Lighting by number
            // alone lit the wrong grid as well as the right one.
            if (grid.Page != cell.Page || grid.Shared != cell.Shared)
                continue;

            foreach (var each in grid.Cells)
            {
                if (each.Column < cell.Column || each.Column >= cell.Column + width)
                    continue;
                if (each.Row < cell.Row || each.Row >= cell.Row + height)
                    continue;

                each.IsTarget = true;
                each.IsAllowed = allowed;
                each.IsIdle = idle;
            }
        }
    }

    /// <summary>Every grid on the page, both panes of every section.</summary>
    private IEnumerable<GridViewModel> Showing()
    {
        foreach (var section in Sections)
        {
            yield return section.Grid;

            // The second pane is a grid like any other, and was left out of the highlight
            // entirely - hovering over it lit nothing at all.
            if (section.SecondShown && section.SecondGrid is { } second)
                yield return second;
        }

        // The belt, while the equipment panel shows it: a place things are dropped too.
        if (ShowBelt && Session.BeltGrid is { } belt)
            yield return belt;
    }

    /// <summary>The container a cell belongs to, in the file it belongs to.</summary>
    private GridViewModel? GridFor(bool shared, byte page) =>
        shared
            ? Stash.Pages.FirstOrDefault(each => each.Page == page)
            : Session.AllGrids.FirstOrDefault(each => !each.Shared && each.Page == page);

    private IEnumerable<D2SLib.Model.Save.Item> ContentsOf(bool shared, byte page) =>
        GridFor(shared, page)?.Items.Select(tile => tile.Item) ?? [];

    private GridSize SizeOf(bool shared, byte page) =>
        GridFor(shared, page) is { } grid
            ? new GridSize(grid.Columns, grid.Rows)
            : new GridSize(0, 0);

    /// <summary>What a drag carries put back, nothing moved: a count out of the tab, or an item held. Whether there was any.</summary>
    public bool PutBackCarried()
    {
        if (IsTabCarrying)
        {
            EndTabCarry();
            return true;
        }

        if (!Session.IsHolding)
            return false;

        CancelHold();
        return true;
    }

    /// <summary>Dropping whatever is held, back where it came from.</summary>
    public void CancelHold()
    {
        if (!Session.IsHolding)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        IsDragCarrying = false;
        Session.CancelHold();
        Stash.RefreshPages();
        HoverOver(null);
    }
}
