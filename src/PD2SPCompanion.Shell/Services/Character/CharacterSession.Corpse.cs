using System;
using System.Collections.Generic;
using System.Linq;
using D2SLib.Model.Save;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Services.Character;

// The corpse: a character that died and left the game without taking its body up keeps what it wore there.
public sealed partial class CharacterSession
{
    // The save has a list of corpses after the character's own items, each with where it lies and an item
    // list of its own: what the character wore as it died, each item still saying which slot it was in. The
    // character's own list then has nothing worn. In game the body is clicked and all of it is put back on.
    //
    // The parser read and wrote them and no page showed them: a character opened after such a death looked
    // stripped. Proven on a real save - a level 86 Amazon,
    // one corpse, twelve items (TestCorpus, specimens/corpse-twelve-worn-items-s13.d2s).
    //
    // Shown beside what is worn, and one way, out - every action allowed, editing in place too, as long as
    // no item goes in: an item on
    // the corpse is chosen and edited where it lies, dragged to a page or a worn slot, cut and pasted, put on,
    // stored in the vault, deleted - as any item is - and all of them are taken back at once (TakeFromCorpse).
    // Nothing goes onto a corpse: its grid takes no drop and no paste. For a day it was the other way: nothing
    // but the taking of all, and Cut was lit that could not be pasted.
    //
    // Its items are in a list of the corpse's own. ListHolding finds one there, so what takes an item out of the
    // character (Release, Remove, Wear) takes it off the corpse; what only gives it a new place (ItemMover.Place)
    // leaves it in the corpse's list with a place on a page - and SettleCorpses, after every edit and before
    // every rebuild and save, moves such a one to the character's list, counts the lists again and takes a
    // corpse left with nothing out of the save.

    /// <summary>What lies on the corpse, a slot an item: the list the pages count, pick and search by.</summary>
    public List<EquipSlotViewModel> CorpseEquipped { get; } = [];

    /// <summary>A page number no container of the save has: the corpse's grid answers to it, and nothing is placed on it.</summary>
    public const byte CorpsePage = 250;

    /// <summary>How wide the corpse's grid is at the most: an inventory's ten.</summary>
    public const int CorpseColumns = 10;

    /// <summary>
    /// What lies on the corpse, packed into a small grid as a page's items are - the tallest first, each in the
    /// first free room; a row of boxes, each its item's size, looked abrupt. The grid is as large as what lies
    /// there needs and no larger: as low as the tallest item where ten columns or fewer hold them at that, a row more where they do
    /// not - and no wider than holds them, so one armor is a grid of two by three. Null where there is no corpse.
    /// The cells are for the eye: the save gives a corpse's items no place but the slot each was worn in. Laid
    /// once and kept as large while the corpse lies, each item in its cell as others leave (FillCorpse).
    /// </summary>
    public GridViewModel? CorpseGrid
    {
        get => _corpseGrid;
        private set
        {
            if (ReferenceEquals(_corpseGrid, value))
                return;

            _corpseGrid = value;
            OnPropertyChanged();
        }
    }

    private GridViewModel? _corpseGrid;

    private IEnumerable<Item> OnCorpse => SaveItems.Walk(_save, ItemScope.Corpses);

    public bool HasCorpse => OnCorpse.Any();

    /// <summary>How many items lie on the corpse.</summary>
    public int CorpseCount => OnCorpse.Count();

    /// <summary>Whether this very item lies on the corpse.</summary>
    public bool IsCorpseItem(Item item) => OnCorpse.Any(each => ReferenceEquals(each, item));

    /// <summary>What a gesture that would put something on the corpse is answered with.</summary>
    public static string CorpseTakesNothing => L("Nothing goes onto the corpse.");

    /// <summary>
    /// Why an item is not the character's to lift, store or delete, or null: the Iron Golem's, handed back by
    /// dismissing the golem. (One on the corpse was kept apart too, for a day: it goes as any item does.)
    /// </summary>
    public string? KeptApart(Item item) => IsGolemItem(item) ? GolemKeepsItem : null;

    /// <summary>The corpse's list holding this very item, or null.</summary>
    private ItemList? CorpseHolding(Item item) =>
        (_save?.PlayerCorpses?.Corpses ?? []).Select(corpse => corpse.ItemList)
            .FirstOrDefault(list => list is not null && list.Items.Any(each => ReferenceEquals(each, item)));

    /// <summary>
    /// The corpses put in order (see the head of this file): an item given a place off the corpse, or put on - in
    /// one of the character's lists already - is the corpse's no longer; the lists are counted again; a corpse left
    /// with nothing is taken out. After every edit, and before every rebuild and save.
    /// </summary>
    private void SettleCorpses()
    {
        if (_save?.PlayerCorpses is not { Corpses.Count: > 0 } corpses || _save.PlayerItemList is not { } own)
        {
            _lying.Clear();
            ForgetCorpseGrid();
            return;
        }

        var hired = _save.MercenaryItemList?.ItemList;
        var seen = new HashSet<Item>(ReferenceEqualityComparer.Instance);
        foreach (var corpse in corpses.Corpses)
        {
            if (corpse.ItemList is not { } lying)
                continue;

            foreach (var item in lying.Items.ToList())
            {
                bool elsewhere = own.Items.Any(each => ReferenceEquals(each, item))
                                 || hired?.Items.Any(each => ReferenceEquals(each, item)) == true;

                // Given another place since it was first seen lying here - a page, the belt: the character's. Told
                // by the place itself, not by what kind of place a corpse's item should have: whatever the game
                // left on a corpse lies there until it is moved.
                var place = (item.Mode, item.Location, item.Page, item.X, item.Y);
                if (!_lying.TryGetValue(item, out var first))
                    _lying[item] = first = place;

                if (!elsewhere && place != first)
                {
                    own.Items.Add(item);
                    elsewhere = true;
                }

                if (elsewhere)
                    lying.Items.Remove(item);
                else
                    seen.Add(item);
            }

            lying.Count = (ushort)lying.Items.Count;
        }

        foreach (var gone in _lying.Keys.Where(item => !seen.Contains(item)).ToList())
            _lying.Remove(gone);

        corpses.Corpses.RemoveAll(corpse => corpse.ItemList is null || corpse.ItemList.Items.Count == 0);
        corpses.Count = (ushort)corpses.Corpses.Count;
        own.Count = (ushort)own.Items.Count;
        if (hired is not null)
            hired.Count = (ushort)hired.Items.Count;
    }

    /// <summary>Where each item on the corpse was when it was first seen lying there: what tells a moved one (SettleCorpses).</summary>
    private readonly Dictionary<Item, (ItemMode Mode, ItemLocation Location, byte Page, byte X, byte Y)> _lying =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// One item off the corpse into the first room on the character - before it is stored in the vault, which
    /// takes what the character's lists hold. Why not, or null; null too for an item that lies on no corpse.
    /// The caller marks the edit and draws again.
    /// </summary>
    public string? BringHome(Item item)
    {
        if (_save?.PlayerItemList is not { } own || CorpseHolding(item) is not { } lying)
            return null;

        var (page, cell) = RoomFor(item, []);
        if (page == 0)
            return L($"No free space for {ItemPresenter.Describe(item).Name}.");

        ItemMover.Place(item, page, cell.X, cell.Y);
        lying.Items.Remove(item);
        own.Items.Add(item);
        SettleCorpses();
        return null;
    }

    /// <summary>
    /// The grid that holds items of these sizes, in this order, and the cell of each: the lowest, and of those
    /// the narrowest - rows from the tallest item up, columns from the widest item to ten, the first that takes
    /// them all. It grows downward for as much as lies there: all a character can wear at once - four weapons of
    /// two by four among it, a Barbarian's two sets of two swords, the worst case - is ten by six.
    /// </summary>
    public static (int Columns, int Rows, IReadOnlyList<(int X, int Y)> Cells) Pack(IReadOnlyList<(int Width, int Height)> sizes)
    {
        int widest = Math.Clamp(sizes.Max(size => size.Width), 1, CorpseColumns);
        for (int rows = Math.Max(1, sizes.Max(size => size.Height)); ; rows++)
        {
            for (int columns = widest; columns <= CorpseColumns; columns++)
            {
                if (PD2SPCompanion.Core.Items.PageArrange.LayOut(sizes, columns, rows) is { } cells)
                    return (columns, rows, cells);
            }
        }
    }

    /// <summary>The corpse's items as tiles, where a rebuild draws them (RebuildTiles).</summary>
    private void FillCorpse()
    {
        var all = OnCorpse.ToList();
        if (all.Count == 0)
        {
            CorpseEquipped.Clear();
            CorpseGrid = null;
            ForgetCorpseGrid();
            return;
        }

        // Laid once, and kept: the grid is as large as what lay on the corpse when it was first seen, and each item
        // stays in its cell while others leave: emptied by drags, a grid that followed shrank to a tiny
        // bit, where it should disappear whole or stay the same. It stays the same, empty
        // cells where an item was, and is gone with the corpse. By an item's id, so an undo - new items, the same
        // ids - draws it as it was; laid afresh only where an item has no cell yet, and never smaller than before.
        bool known = all.All(item => item.Id != 0 && _corpseCells.ContainsKey(item.Id))
                     && all.Select(item => item.Id).Distinct().Count() == all.Count;
        var cellOf = new Dictionary<Item, (int X, int Y)>(ReferenceEqualityComparer.Instance);
        if (known && _corpseSize is not null)
        {
            foreach (var item in all)
                cellOf[item] = _corpseCells[item.Id];
        }
        else
        {
            // Packed: the tallest first, then the widest, each in the first free room (Pack).
            var order = all.Select((item, index) => (Item: item, Index: index, Size: ItemPresenter.FootprintOf(item)))
                           .OrderByDescending(each => each.Size.Height).ThenByDescending(each => each.Size.Width)
                           .ThenBy(each => each.Index)
                           .ToList();
            var (columns, rows, cells) = Pack([.. order.Select(each => each.Size)]);

            _corpseCells.Clear();
            for (int at = 0; at < order.Count; at++)
            {
                cellOf[order[at].Item] = cells[at];
                if (order[at].Item.Id != 0)
                    _corpseCells[order[at].Item.Id] = cells[at];
            }

            _corpseSize = (Math.Max(columns, _corpseSize?.Columns ?? 0), Math.Max(rows, _corpseSize?.Rows ?? 0));
        }

        // What is on the cursor has left its cell, as a lifted item leaves a page's: the cell stays, empty.
        var tiles = new List<ItemTileViewModel>();
        for (int index = 0; index < all.Count; index++)
        {
            if (!IsHeld(all[index]))
                tiles.Add(TileFor(all[index], index, cellOf[all[index]].X, cellOf[all[index]].Y));
        }

        var size = _corpseSize!.Value;
        var grid = _grids.For("Corpse", CorpsePage, size.Columns, size.Rows);
        grid.Fill(tiles);
        CorpseGrid = grid;

        if (CorpseEquipped.Count == tiles.Count
            && CorpseEquipped.Zip(tiles).All(pair => ReferenceEquals(pair.First.Item, pair.Second)))
        {
            return;
        }

        CorpseEquipped.Clear();
        foreach (var tile in tiles)
        {
            CorpseEquipped.Add(new EquipSlotViewModel(
                new EquipSlot(tile.Item.Location, "", tile.View.GridWidth, tile.View.GridHeight, 0, 0), tile));
        }
    }

    /// <summary>The cell each item of the corpse was first laid in, by its id, and how large the grid was laid: kept while the corpse lies.</summary>
    private readonly Dictionary<uint, (int X, int Y)> _corpseCells = [];

    private (int Columns, int Rows)? _corpseSize;

    /// <summary>No corpse, or another character: the next one is laid afresh.</summary>
    private void ForgetCorpseGrid()
    {
        _corpseCells.Clear();
        _corpseSize = null;
    }

    /// <summary>
    /// Takes everything off the corpse, as clicking the body does in game. Where the character wears nothing -
    /// the usual case: it has only just died - each item goes back to the slot it was in, which is how it stood
    /// before the death. Where something was put on since, nothing is put on over it: every item goes to the
    /// first room on the character - the inventory, the cube, the stash. What finds no room stays on the corpse,
    /// and a corpse left with nothing is gone from the save. One step of the undo. Says what it did.
    /// </summary>
    public string? TakeFromCorpse()
    {
        if (_save?.PlayerItemList is not { } list || _save.PlayerCorpses is not { } corpses || !HasCorpse)
            return null;

        bool bare = !list.Items.Any(item => item.Mode == ItemMode.Equipped);
        int worn = 0, carried = 0, left = 0;

        foreach (var corpse in corpses.Corpses)
        {
            if (corpse.ItemList is not { } lying)
                continue;

            foreach (var item in lying.Items.ToList())
            {
                if (bare && item.Mode == ItemMode.Equipped)
                {
                    worn++;
                }
                else
                {
                    var (page, cell) = RoomFor(item, []);
                    if (page == 0)
                    {
                        left++;
                        continue;
                    }

                    ItemMover.Place(item, page, cell.X, cell.Y);
                    carried++;
                }

                lying.Items.Remove(item);
                list.Items.Add(item);
            }

            lying.Count = (ushort)lying.Items.Count;
        }

        list.Count = (ushort)list.Items.Count;
        corpses.Corpses.RemoveAll(corpse => corpse.ItemList is null || corpse.ItemList.Items.Count == 0);
        corpses.Count = (ushort)corpses.Corpses.Count;

        string said = (worn, carried) switch
        {
            (0, 0) => L("Nothing taken from the corpse: no free space."),
            (_, 0) => Plural(worn, "Corpse: 1 item put back on.", $"Corpse: {worn} items put back on."),
            _ => Plural(worn + carried, "Corpse: 1 item taken.", $"Corpse: {worn + carried} items taken."),
        };
        if (left > 0 && worn + carried > 0)
            said += " " + Plural(left, "1 left: no free space.", $"{left} left: no free space.");

        // Said before anything is marked: the autosave names the step by the strip's last text.
        Status = said;
        if (worn + carried > 0)
        {
            if (SelectedItem is { } chosen && !AllTilesHold(chosen))
                SelectedItem = null;
            MarkEdited();
            Rebuild();
        }

        return said;
    }

    private bool AllTilesHold(ItemTileViewModel tile) => AllTiles.Any(each => ReferenceEquals(each, tile));
}
