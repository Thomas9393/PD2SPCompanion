using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Threading;

namespace PD2SPCompanion.Shell.Services.Character;

// What the pages draw: the grids, worn slots and belt, their tiles, and the selection.
public sealed partial class CharacterSession
{

    /// <summary>Every container the character has; a page picks the ones it shows.</summary>
    public List<GridViewModel> AllGrids { get; } = [];

    /// <summary>The grids kept across rebuilds, so their controls are too (GridViewModel).</summary>
    private readonly GridCache _grids = new();

    /// <summary>
    /// What the character is wearing, one entry per slot of the game's own equipment
    /// panel, empty slots included. The slots are fixed, so the page lays them out
    /// rather than reading positions from the items.
    /// </summary>
    public List<EquipSlotViewModel> Equipped { get; } = [];

    /// <summary>The page number the belt's grid answers to: the mode the save gives belted items.</summary>
    public const byte BeltPage = 2;

    /// <summary>What is in the belt, laid out as the game draws it - the equipment panel shows it on request.</summary>
    public GridViewModel? BeltGrid
    {
        get => _beltGrid;
        private set
        {
            _beltGrid = value;
            OnPropertyChanged();
        }
    }

    private GridViewModel? _beltGrid;

    /// <summary>What the mercenary is wearing, laid out the same way.</summary>
    public List<EquipSlotViewModel> MercEquipped { get; } = [];

    /// <summary>
    /// Builds the containers again. For a caller that moved a batch quietly and wants the
    /// one rebuild at the end rather than one per item.
    /// </summary>
    public void RefreshContainers() => Rebuild();

    /// <summary>
    /// The tiles of the last rebuild, by the item they stand for, with the item's bytes when it was
    /// drawn. A rebuild keeps a tile whose item is unchanged - same index, same cell, same bytes -
    /// instead of describing the item and drawing its picture again, and the grids keep the control
    /// that draws it (GridViewModel.Fill). Every tile used to be new on every move: 200 descriptions
    /// and 200 controls three times a move, 0.2 to 1.1 s before the page settled (measured).
    /// </summary>
    private Dictionary<Item, (ItemTileViewModel Tile, byte[] Bytes)> _tiles = new(ReferenceEqualityComparer.Instance);

    private Dictionary<Item, (ItemTileViewModel Tile, byte[] Bytes)> _tilesNow = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Fills a worn-slot list keeping its slot objects: the first rebuild makes them, later ones put
    /// the item in and put out any target lit under a carried item. A new list per rebuild built the
    /// worn panels again with every move.
    /// </summary>
    private static void FillSlots(IList<EquipSlotViewModel> slots, IReadOnlyList<EquipSlot> definitions,
                                  Func<EquipSlot, ItemTileViewModel?> tileFor)
    {
        if (slots.Count != definitions.Count || !slots.Zip(definitions).All(pair => pair.First.Slot.Equals(pair.Second)))
        {
            slots.Clear();
            foreach (var definition in definitions)
                slots.Add(new EquipSlotViewModel(definition, tileFor(definition)));
            return;
        }

        foreach (var slot in slots)
        {
            slot.Item = tileFor(slot.Slot);
            slot.IsTarget = false;
            slot.IsAllowed = false;
        }
    }

    private ItemTileViewModel TileFor(Item item, int index, int cellX, int cellY)
    {
        byte[] bytes = ContentBytes(item);

        // The same item with the same contents keeps its tile wherever it now is: moved, the tile is
        // moved. The tile draws the item, not its place, and kept, the selection stays on it.
        if (_tiles.TryGetValue(item, out var kept) && kept.Bytes.AsSpan().SequenceEqual(bytes))
        {
            kept.Tile.ResetMarks();
            kept.Tile.MoveTo(index, cellX, cellY);
            _tilesNow[item] = kept;
            return kept.Tile;
        }

        var tile = new ItemTileViewModel(item, index, cellX, cellY);
        _tilesNow[item] = (tile, bytes);
        return tile;
    }

    /// <summary>
    /// The item as the file holds it, its place left out - where it sits, worn or stored, which page:
    /// a move changes those and nothing a tile draws. Written with them cleared and put straight back.
    /// </summary>
    private byte[] ContentBytes(Item item)
    {
        var (mode, location, x, y, page) = (item.Mode, item.Location, item.X, item.Y, item.Page);
        try
        {
            item.Mode = default;
            item.Location = default;
            item.X = 0;
            item.Y = 0;
            item.Page = 0;
            return Item.Write(item, _save!.Header.Version);
        }
        finally
        {
            (item.Mode, item.Location, item.X, item.Y, item.Page) = (mode, location, x, y, page);
        }
    }

    private void Rebuild()
    {
        SettleCorpses();
        AllGrids.Clear();
        if (_save is null)
        {
            _tiles.Clear();
            return;
        }

        _tilesNow = new(ReferenceEqualityComparer.Instance);
        try
        {
            RebuildTiles();
        }
        finally
        {
            // What is on the cursor is drawn by no grid, and comes back down as the same tile: the
            // selection stays on it through a drag, and the item panel is not built again on the drop.
            foreach (var carried in HeldGroup.Select(each => each.Tile).Prepend(Held).OfType<ItemTileViewModel>())
            {
                if (!_tilesNow.ContainsKey(carried.Item) && _tiles.TryGetValue(carried.Item, out var kept))
                    _tilesNow[carried.Item] = kept;
            }

            // Only what this rebuild drew is kept for the next: a tile for an item gone goes too.
            _tiles = _tilesNow;
        }
    }

    private void RebuildTiles()
    {
        var items = _save!.PlayerItemList?.Items ?? [];

        // Only the stored items' tiles: the worn and the belt's are made where they are placed.
        var tiles = items.Select((item, index) => (item, index))
                         .Where(entry => entry.item.Mode == ItemMode.Stored)
                         .Select(entry => TileFor(entry.item, entry.index, entry.item.X, entry.item.Y))
                         .ToList();

        // Equipped items carry no grid coordinates; the slot they are worn in places them.
        var worn = items.Select((item, index) => (item, index))
                        .Where(entry => entry.item.Mode == ItemMode.Equipped)
                        .ToList();

        FillSlots(Equipped, EquipSlotViewModel.Slots, slot =>
        {
            // What is on the cursor has left its slot, as a lifted item leaves its cell.
            var found = worn.FirstOrDefault(entry => entry.item.Location == slot.Location
                                                     && !IsHeld(entry.item));
            return found.item is null ? null : TileFor(found.item, found.index, 0, 0);
        });

        // The mercenary's own items are a list of their own in the save, so they are
        // indexed separately - the indices are not comparable with the character's.
        Mercenary = MercenaryProfile.Read(_save, _game);

        var hired = (_save.MercenaryItemList?.ItemList?.Items ?? [])
            .Select((item, index) => (item, index))
            .ToList();

        FillSlots(MercEquipped, EquipSlotViewModel.MercSlots, slot =>
        {
            var found = hired.FirstOrDefault(entry => entry.item.Location == slot.Location
                                                      && !IsHeld(entry.item));
            return found.item is null ? null : TileFor(found.item, found.index, 0, 0);
        });

        // What a standing Iron Golem was made of: in neither list, the save keeps it with the golem.
        GolemTile = Skills?.GolemItem is { } made ? TileFor(made, 0, 0, 0) : null;

        // What lies on the corpse: in neither list either (CharacterSession.Corpse.cs).
        FillCorpse();

        // Every container the character has, whether or not anything is in it: an empty
        // cube is a fact about the character, not a reason to leave the section out.
        foreach (var (page, name, size) in Pages)
        {
            var grid = _grids.For(name, page, size.Columns, size.Rows);

            // Only the inventory is divided, and only when it is tall enough to be.
            if (page == InventoryLayout.InventoryPage && CharmArea.Applies(size))
                grid.DividerTop = CharmArea.FirstRow(size) * ItemTileViewModel.CellSize;
            var shown = new List<ItemTileViewModel>();
            // The inventory in the save's order: which of two unique charms counts (CharmArea.IdleAt).
            var inventory = page == InventoryLayout.InventoryPage
                ? items.Where(item => item.Mode == ItemMode.Stored && item.Page == page).ToList()
                : [];
            foreach (var tile in tiles.Where(t => t.Item.Page == page))
            {
                // What is on the cursor is not in the container it came from - a group's too.
                if (IsHeld(tile.Item))
                    continue;

                tile.IdleCharm = page == InventoryLayout.InventoryPage
                    ? CharmArea.IdleAt(tile.Item, inventory, size, tile.Item.Y, tile.View.GridHeight)
                    : CharmIdle.None;

                shown.Add(tile);
            }
            grid.Fill(shown);
            AllGrids.Add(grid);
        }

        // Anything stored somewhere none of the containers above covers.
        UnknownPages =
        [
            .. items.Where(item => item.Mode == ItemMode.Stored
                                   && !Pages.Any(entry => entry.Page == item.Page))
                    .GroupBy(item => item.Page)
                    .OrderBy(group => group.Key)
                    .Select(group => (Page: group.Key, Count: group.Count())),
        ];

        // Belt slots are a flat index in X; fold it into a 4-wide grid, as many rows as the belt
        // worn gives (belts.txt) - or as its potions need, should they sit past it - and the
        // bottom row at the bottom, as the game draws it.
        var beltItems = items.Select((item, index) => (item, index))
                             .Where(entry => entry.item.Mode == ItemMode.Belt && !IsHeld(entry.item))
                             .ToList();
        var wornBelt = items.FirstOrDefault(item => item.Mode == ItemMode.Equipped && item.Location == ItemLocation.Waist);
        int beltRows = Math.Max(
            (Belts.BoxesFor(wornBelt?.Code) + 3) / 4,
            beltItems.Count == 0 ? 1 : beltItems.Max(entry => entry.item.X / 4) + 1);

        var belt = _grids.For("Belt", BeltPage, 4, beltRows);
        belt.Fill([.. beltItems.Select(entry =>
            TileFor(entry.item, entry.index, entry.item.X % 4, beltRows - 1 - entry.item.X / 4))]);
        BeltGrid = belt;
        if (belt.Items.Count > 0)
            AllGrids.Add(belt);

        ShowFigureSoon();
        Changed?.Invoke();

        // Every move rebuilds, and the tiles are new objects each time, so the selection
        // has to be found again by the item behind it. Without this the panel jumped to
        // some other item on every pick-up, which made it useless while moving things.
        //
        // Every tile, not only the grids': a worn item is in no container, so looking for it
        // there alone lost the selection the moment anything rebuilt.
        //
        // And the shared stash's: an item on a shared page shown on the Edit page is no tile of the character's, and
        // a rebuild for anything else - a socketed gem's copy landing in the inventory - left the page on no item.
        var showing = AllTiles.Concat(SharedTiles?.Invoke() ?? []).FirstOrDefault(tile => ReferenceEquals(tile.Item, _showing));

        // What is on the cursor has left its container, so it is not among the tiles - but
        // it is still the item being looked at.
        if (showing is null && Held is not null && ReferenceEquals(Held.Item, _showing))
            showing = Held;

        // Whatever was being looked at, and nothing otherwise. A character opens with no
        // selection: picking one for the page meant the editor arrived already pointed at
        // an item nobody asked about, and the first thing it showed was a lie about what
        // was chosen.
        SelectedItem = showing;
    }

    /// <summary>The item the detail panel is showing, remembered across rebuilds.</summary>
    private Item? _showing;

    /// <summary>The shared stash's tiles, where a rebuild looks for the item shown too (SharedStashSession joins it).</summary>
    public Func<IEnumerable<ItemTileViewModel>>? SharedTiles { get; set; }

    /// <summary>
    /// Every tile on screen, wherever it is drawn. The grids are not all of them: the worn
    /// slots and the mercenary's hold tiles of their own, built separately and standing for
    /// items that are in no container at all.
    /// </summary>
    private IEnumerable<ItemTileViewModel> AllTiles =>
        AllGrids.SelectMany(grid => grid.Items)
            .Concat(Equipped.Select(slot => slot.Item).OfType<ItemTileViewModel>())
            .Concat(MercEquipped.Select(slot => slot.Item).OfType<ItemTileViewModel>())
            .Concat(GolemTile is { } golem ? [golem] : [])
            .Concat(CorpseEquipped.Select(slot => slot.Item).OfType<ItemTileViewModel>());

    /// <summary>
    /// The tile of the item that is <paramref name="was"/> after an undo or a redo has made every item afresh
    /// (the undo is the Edit page's way back, so the page stays on its item). The same
    /// base under the same id; an item with no id of its own - a gem, a potion - by where it lies. Null when
    /// it is not among these: gone with the step, or the shared stash's (<paramref name="among"/>).
    /// </summary>
    public ItemTileViewModel? TileLike(Item was, IEnumerable<ItemTileViewModel>? among = null)
    {
        var tiles = (among ?? AllTiles).ToList();
        bool SamePlace(Item item) =>
            item.Mode == was.Mode && item.Location == was.Location && item.Page == was.Page && item.X == was.X && item.Y == was.Y;

        return tiles.FirstOrDefault(tile => tile.Item.Code == was.Code && was.Id != 0 && tile.Item.Id == was.Id && SamePlace(tile.Item))
               ?? tiles.FirstOrDefault(tile => tile.Item.Code == was.Code && was.Id != 0 && tile.Item.Id == was.Id)
               ?? tiles.FirstOrDefault(tile => tile.Item.Code == was.Code && was.Id == 0 && tile.Item.Id == 0 && SamePlace(tile.Item));
    }

    /// <summary>The grids a page shows, in the order it names them.</summary>
    public IEnumerable<GridViewModel> GridsNamed(params string[] names) =>
        names.SelectMany(name => AllGrids.Where(grid => grid.Name == name));

    /// <summary>
    /// Says the selected item has been written to, so what is drawn from it is drawn again.
    /// The facts beside it are built when an item is chosen, and an edit does not choose a
    /// new one.
    /// </summary>
    public void SelectedItemChanged()
    {
        if (SelectedItem is not { } tile)
            return;

        // Every tile standing for this item, not only the one the selection points at.
        // An equipped item is drawn by a tile of its own, and a rebuild makes new ones, so
        // the selection can easily hold a tile that is no longer on screen - which left the
        // worn slot drawing sockets the item had just lost.
        foreach (var other in AllTiles)
        {
            if (ReferenceEquals(other.Item, tile.Item))
                other.Rebuilt();
        }

        tile.Rebuilt();
        OnSelectedItemChanged(null, tile);
    }

    partial void OnSelectedItemChanged(ItemTileViewModel? oldValue, ItemTileViewModel? newValue)
    {
        _showing = newValue?.Item;

        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;

        // The item panel builds its own property rows (VP_ViewModel_Inv_R.BuildRows), which stage an edit
        // until KEEP. A second set built here on every select - each row with its affix ranges - would be
        // shown nowhere.
        SelectedFacts.Clear();
        if (newValue is null)
            return;

        foreach (string fact in ItemFacts.Of(newValue))
            SelectedFacts.Add(fact);
    }
}
