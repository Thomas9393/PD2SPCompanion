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

// Items coming and going: copies, arrivals from elsewhere, releases, deletes, strays.
public sealed partial class CharacterSession
{

    /// <summary>
    /// A detached copy of an item, made the way the file makes one - written to bits and
    /// read back. For asking what an item <i>would</i> be if it were changed, without
    /// changing it. Null when there is no character open or the item will not round-trip.
    /// </summary>
    public Item? Copy(Item item)
    {
        if (_save is not { } save)
            return null;

        try
        {
            return Item.Read(Item.Write(item, save.Header.Version), save.Header.Version);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A deep copy of an item, written to bits and read back so nothing nested is shared with the
    /// original - or null when it will not copy. The copy keeps the original's id: whoever lands it
    /// gives it a new one.
    /// </summary>
    public Item? CopyOf(Item item)
    {
        if (_save is not { } save)
            return null;

        try
        {
            UseOwnLayout();
            return Item.Read(Item.Write(item, save.Header.Version), save.Header.Version);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>An item of the character's made by the editor in place - a craft.</summary>
    public void Made(Item item) => ItemMade?.Invoke(item);

    /// <summary>
    /// Raised for every item the editor makes out of nothing here - a copy, a craft.
    /// All of them come through <see cref="Receive"/>, so a new way of making one is heard too.
    /// </summary>
    public event Action<Item>? ItemMade;

    /// <summary>
    /// Gives the character an item that is new to it - a copy, a craft - in the first cell with
    /// room, and selects it.
    /// <para>
    /// One place for it, because it was written out twice before the materials tab needed it
    /// a third time. The item gets a <b>new identifier</b>, since it is a new item. The cell is
    /// asked for the item itself and not for whatever it was made from, because an item never
    /// collides with itself - asking where the original fits answers "where it already is",
    /// and a copy was once laid straight on top of it.
    /// </para>
    /// </summary>
    /// <param name="select">
    /// Selected, as a copy or a craft is; false leaves it where it landed, unselected - the
    /// grail's Forge, whose page shows no character.
    /// </param>
    /// <param name="at">A place already found for it - a copy's, next to its original; else the first room.</param>
    /// <returns>Null when handed over, otherwise why not, as the status: <see cref="Refuses(Item)"/>, or <paramref name="noRoom"/>.</returns>
    public string? Receive(Item item, string noRoom, bool select = true, (byte Page, (int X, int Y) Cell)? at = null)
    {
        if (_save is not { PlayerItemList: { } list })
            return Status = L("No character open.");

        if (Refuses(item) is { } refused)
            return Status = refused;

        var (page, cell) = at ?? RoomFor(item, []);
        if (page == 0)
            return Status = noRoom;

        item.Id = NewItemId();
        ItemMover.Place(item, page, cell.X, cell.Y);

        list.Items.Add(item);
        list.Count++;

        ItemMade?.Invoke(item);

        MarkEdited();
        Rebuild();

        // Rebuild made a fresh tile for it; the item itself is how to find that tile.
        if (select && AllGrids.SelectMany(grid => grid.Items)
                      .FirstOrDefault(each => ReferenceEquals(each.Item, item)) is { } made)
        {
            SelectedItem = made;
        }

        return null;
    }

    /// <summary>How many items the character holds - worn, belted, carried and stashed; what is in sockets is not counted.</summary>
    public int ItemCount => _save?.PlayerItemList?.Items.Count ?? 0;

    /// <summary>
    /// Several new items at once, each at the place already found for it (ItemLanding.PlacesFor) - a duplicate
    /// of several picks - each under a new identifier, with one rebuild for them all. All of them or none
    /// (<see cref="RefusesAny"/>). Nothing is selected. Null when handed over.
    /// </summary>
    public string? ReceiveAll(IReadOnlyList<(Item Item, byte Page, (int X, int Y) Cell)> landing)
    {
        if (_save is not { PlayerItemList: { } list })
            return Status = L("No character open.");
        if (landing.Count == 0)
            return null;

        if (RefusesAny(landing.Select(each => each.Item)) is { } refused)
            return Status = refused;

        foreach (var (item, page, cell) in landing)
        {
            item.Id = NewItemId();
            ItemMover.Place(item, page, cell.X, cell.Y);
            list.Items.Add(item);
            list.Count++;
            ItemMade?.Invoke(item);
        }

        MarkEdited();
        Rebuild();
        return null;
    }

    /// <summary>
    /// The item era this character is written in, or null when it holds nothing to tell by.
    /// <para>
    /// A save holds <b>one</b> era of item version stamps - or this season's beside the season
    /// before's, which the game mixes (<see cref="PD2SPCompanion.Core.Saves.ItemSeason.Mix"/>).
    /// Seven pre-S10 items among eleven current ones made the game refuse a character outright,
    /// with nothing else wrong, so anything arriving from another file is checked against this first.
    /// </para>
    /// </summary>
    public string? ItemEra =>
        (_save?.PlayerItemList?.Items ?? [])
            .Select(item => item.Version)
            .FirstOrDefault(version => version is { Length: > 0 });

    /// <summary>
    /// Why an item from elsewhere cannot come into this character, or null: its version, and last season's encoding, as
    /// the shared stash's own Refuses. The one question every way in asks first - <see cref="Adopt"/>, <see cref="Receive"/>,
    /// <see cref="Wear"/> and <see cref="IntoBelt"/> - so a rule added here holds at all of them. A copy of an item the
    /// character holds is asked too: it is a new item in the file, as the shared stash's own copies are.
    /// </summary>
    public string? Refuses(Item item) => Refuses(item, ItemEra);

    private string? Refuses(Item item, string? era)
    {
        if (_save is null)
            return L("No character open.");

        if (era is not null && item.Version is { Length: > 0 } arriving && !PD2SPCompanion.Core.Saves.ItemSeason.Mix(era, arriving))
            return L($"Require same version: item v{arriving}, character v{era}.");

        return LastSeasonGuard.Refuses(_lastSeasonOwn, item, L("Character"));
    }

    /// <summary>
    /// What <see cref="Refuses(Item)"/> says of the first that would be turned away, were these to arrive one after another:
    /// a file holding nothing is given its era by the first to land, and may then refuse the next. Null where all may come.
    /// </summary>
    public string? RefusesAny(IEnumerable<Item> items)
    {
        string? era = ItemEra;
        foreach (var item in items)
        {
            if (Refuses(item, era) is { } refused)
                return refused;

            era ??= item.Version is { Length: > 0 } stamp ? stamp : null;
        }

        return null;
    }

    /// <summary>
    /// Takes an item in from somewhere else and puts it at a cell. The caller has already
    /// found the room; this only does the paperwork.
    /// </summary>
    /// <param name="keepId">
    /// Never give the item a new identifier, even when one here already has it - the vault's
    /// withdraw, where two items sharing an id is the whole point: it is how a duplicate shows.
    /// </param>
    public string? Adopt(Item item, byte page, int x, int y, bool rebuild = true, bool keepId = false)
    {
        if (_save is not { PlayerItemList: { } list })
            return Status = L("No character open.");

        if (Refuses(item) is { } refused)
            return Status = refused;

        // Kept unless it clashes. A moved item is the same item, and reassigning made it
        // unrecognisable across the move - which is exactly what made checking a save hard.
        // Duplicate is the case that genuinely needs a new one, and it has its own.
        if (!keepId && list.Items.Any(each => each.Id == item.Id))
            item.Id = NewItemId();

        ItemMover.Place(item, page, x, y);

        list.Items.Add(item);
        list.Count = (ushort)list.Items.Count;

        MarkEdited();
        if (rebuild)
            Rebuild();

        return null;
    }

    /// <summary>As <see cref="Adopt(Item, byte, int, int, bool, bool)"/>: the shared stash numbers its pages in an int, the character in a byte.</summary>
    string? IItemHolder.Adopt(Item item, int page, int x, int y, bool rebuild, bool keepId) =>
        Adopt(item, (byte)page, x, y, rebuild, keepId);

    /// <summary>
    /// Takes in items dropped from the vault on one cell: one item where it was dropped, several packed into that container from its first
    /// free cells - all of them or none, as a drag's move is. Identifiers kept, as
    /// <see cref="ItemLanding.Welcome"/> keeps them. Only the inventory, the cube and the stash take them, as
    /// withdraw's own choice of room does.
    /// </summary>
    /// <returns>
    /// How many went in, which found no room, how many the character refused, and the first reason it gave - an item
    /// stamped for another era, say. Nothing placed, and every item in NoRoom, when they do not all fit.
    /// </returns>
    public (int Placed, IReadOnlyList<Item> NoRoom, int RefusedCount, string? Refused) WelcomeAt(
        IReadOnlyList<Item> items, byte page, int x, int y, IReadOnlyList<(int Dx, int Dy)>? shape = null)
    {
        if (_save?.PlayerItemList is not { } list)
            return (0, items, 0, L("No character open."));

        if (!Somewhere.Contains(page))
            return (0, items, 0, L("Inventory, cube or stash only."));

        var contents = list.Items.Where(each => each.Mode == ItemMode.Stored && each.Page == page).ToList();
        var size = SizeOfPage(page);
        var spots = new List<(Item Item, int X, int Y)>();

        if (items.Count == 1)
        {
            if (!ItemMover.CanPlace(items[0], contents, size, x, y).Allowed)
                return (0, items, 0, null);

            spots.Add((items[0], x, y));
        }
        else if (shape is not null && ItemMover.ShapedSpots(items, shape, contents, size, x, y) is { } shaped)
        {
            // Several, as they lay about the one held: a group keeps its shape between
            // the vault and the inventory, as within either - where they do not all fit so, packed as below.
            spots.AddRange(shaped);
        }
        else
        {
            foreach (var item in items)
            {
                if (ItemMover.FirstFreeCell(item, contents, size) is not { } cell)
                    return (0, items, 0, null);

                // Not the character's yet: where it will go is only its own until it is taken in.
                ItemMover.Place(item, page, cell.X, cell.Y);
                contents.Add(item);
                spots.Add((item, cell.X, cell.Y));
            }
        }

        int placed = 0, refusedCount = 0;
        string? refused = null;

        foreach (var (item, atX, atY) in spots)
        {
            if (Adopt(item, page, atX, atY, rebuild: false, keepId: true) is { } why)
            {
                refused ??= why;
                refusedCount++;
                continue;
            }

            placed++;
        }

        Rebuild();
        return (placed, [], refusedCount, refused);
    }

    /// <summary>
    /// Which of these ids the <b>other</b> characters in the save folder hold, and by whom - the
    /// open one is skipped, since its file is not what it holds now.
    /// <para>
    /// Only <c>.d2s</c>, in the one folder, read once, and nothing kept but ids - a sweep that
    /// cannot say what it will open is not one to run. A character that will not read says
    /// nothing either way: this is for warnings, not a proof.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<uint, string> HeldByOthers(IReadOnlySet<uint> ids)
    {
        var found = new Dictionary<uint, string>();
        if (ids.Count == 0)
            return found;

        string folder = SaveFolder;
        if (folder.Length == 0 || !Directory.Exists(folder))
            return found;

        foreach (string path in Directory.GetFiles(folder, "*.d2s"))
        {
            if (string.Equals(path, CharacterPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var (other, _) = SaveVerifier.Load(path);

                foreach (var item in ItemsAnotherHolds(other))
                {
                    if (item.Id != 0 && ids.Contains(item.Id))
                        found.TryAdd(item.Id, other.Name);
                }
            }
            catch (Exception)
            {
                // Says nothing either way; see above.
            }
        }

        return found;
    }

    /// <summary>What another character holds, its mercenary's gear too: an item there is as taken as one in its stash.</summary>
    public static IEnumerable<Item> ItemsAnotherHolds(D2S other) =>
        SaveItems.Walk(other, ItemScope.Own | ItemScope.Mercenary | ItemScope.Sockets);

    /// <summary>Whether this very item is the mercenary's.</summary>
    public bool IsMercenaryItem(Item item) =>
        _save?.MercenaryItemList?.ItemList?.Items.Any(each => ReferenceEquals(each, item)) == true;

    /// <summary>Whether this very item is one of the character's own, worn, carried or stored - or its mercenary's.</summary>
    public bool Holds(Item item) => ListHolding(item) is not null;

    /// <summary>The ids the character's own items carry - worn, carried and stored.</summary>
    public IReadOnlySet<uint> ItemIds =>
        (_save?.PlayerItemList?.Items ?? []).Where(item => !item.IsCompact).Select(item => item.Id).ToHashSet();

    /// <summary>
    /// Lets an item go to somewhere else. It is taken out of this character and handed back
    /// for the caller to put down; nothing is destroyed.
    /// </summary>
    public Item? Release(Item item, bool rebuild = true)
    {
        // Worn or carried, the character's or the mercenary's; a belt refused as Delete refuses it.
        if (ListHolding(item) is not { } list || CannotTakeOff(item) is not null)
            return null;

        SpillBeltOf(item);
        if (!list.Items.Remove(item))
            return null;

        list.Count = (ushort)list.Items.Count;

        if (ReferenceEquals(SelectedItem?.Item, item))
            SelectedItem = null;

        MarkEdited();
        if (rebuild)
            Rebuild();

        return item;
    }

    /// <summary>Where an item of the character's or its mercenary's lies now, for <see cref="PutBack"/>; null where neither holds it.</summary>
    public ItemOrigin? OriginOf(Item item) =>
        ListHolding(item) is { } list ? ItemOrigin.Of(item, list) : null;

    /// <summary>
    /// An item let go of and refused elsewhere, back where it lay: its own list and place in it, its slot or its cell. It
    /// was this character's, so nothing is asked of it - <see cref="Adopt"/> would ask, and refused too it would be in no
    /// list, and on a page none draws where it was worn. Only where something moved in the meantime has taken its cell does
    /// it go to the first room; with none anywhere it stays on that cell, over the other, rather than be lost. No rebuild.
    /// </summary>
    public void PutBack(Item item, ItemOrigin origin)
    {
        origin.Restore(item);

        if (origin.Mode == ItemMode.Stored
            && !ItemMover.CanPlace(item, origin.List.Items.Where(each => !ReferenceEquals(each, item) && each.Mode == ItemMode.Stored && each.Page == origin.Page),
                                   SizeOfPage(origin.Page), origin.X, origin.Y).Allowed
            && RoomFor(item, []) is { Page: not 0 } room)
        {
            ItemMover.Place(item, room.Page, room.Cell.X, room.Cell.Y);
        }

        origin.List.Count = (ushort)origin.List.Items.Count;
        MarkEdited();
    }

    /// <summary>
    /// Takes an item out of the character altogether. In memory only until the character is
    /// saved, and undone by reloading like any other edit.
    /// </summary>
    public string? Delete(ItemTileViewModel tile)
    {
        if (Remove(tile) is { } refused)
            return Status = refused;

        MarkEdited();
        Rebuild();
        Status = L($"Deleted {tile.Name}.");
        return null;
    }

    /// <summary>
    /// Deletes several at once, with one rebuild after the lot rather than one each. What is
    /// worn, or not this character's, is left. Returns how many went.
    /// </summary>
    public int Delete(IEnumerable<ItemTileViewModel> tiles)
    {
        int gone = 0;
        foreach (var tile in tiles.ToList())
        {
            if (Remove(tile) is null)
                gone++;
        }

        if (gone > 0)
        {
            MarkEdited();
            Rebuild();
        }

        return gone;
    }

    /// <summary>Takes one item out of the character, or says why not. No rebuild.</summary>
    private string? Remove(ItemTileViewModel tile)
    {
        if (_save?.PlayerItemList is null)
            return L("No character open.");

        if (CannotTakeOff(tile.Item) is { } refused)
            return refused;

        if (ReferenceEquals(Held, tile))
            CancelHold();

        if (KeptApart(tile.Item) is { } apart)
            return apart;

        SpillBeltOf(tile.Item);

        // Worn or carried, the character's or the mercenary's.
        if (ListHolding(tile.Item) is not { } list || !list.Items.Remove(tile.Item))
            return L($"{tile.Name} not on character.");

        list.Count = (ushort)list.Items.Count;

        if (ReferenceEquals(SelectedItem, tile))
            SelectedItem = null;

        return null;
    }

    /// <summary>
    /// An identifier for an item the editor made. Random across the whole field, which is
    /// how the game's own are: nothing counts them or expects them in any order.
    /// </summary>
    private static uint NewItemId() => (uint)Random.Shared.NextInt64(1, uint.MaxValue);

    /// <summary>Storage pages as the game numbers them. The sizes come from the table,
    /// not from here - PD2's inventory is 10x8, not vanilla's 10x4.</summary>
    private (byte Page, string Name, GridSize Size)[] Pages =>
    [
        (InventoryLayout.InventoryPage, "Inventory", _layout.Inventory),
        (_layout.StashPage, "Stash", _layout.Stash),
        (InventoryLayout.CubePage, "Cube", _layout.Cube),
    ];

    /// <summary>
    /// Stored items on a page no container here draws.
    /// <para>
    /// The page numbers are the one part of an item's position that is guessed rather than
    /// read from a table, and a wrong guess hides items in silence - which is exactly what
    /// happened with the stash. This is the tell: anything the character is carrying that
    /// this page cannot account for is counted and said out loud.
    /// </para>
    /// </summary>
    public IReadOnlyList<(byte Page, int Count)> UnknownPages { get; private set; } = [];

    /// <summary>
    /// Puts every item stored on a page no container covers into the first container with room
    /// - inventory, cube, stash - where it can be seen, kept or thrown out. A repair, not an edit:
    /// the item is the same item, only its place changes.
    /// <para>
    /// A jewel bought from the trade site with the wrong socket marks came
    /// back from the game's own save as a loose item on page 0, in no grid, invisible in game and
    /// here alike.
    /// </para>
    /// </summary>
    /// <returns>Null when every one found room, otherwise what is still out of sight.</returns>
    public string? RehomeStrays()
    {
        if (_save?.PlayerItemList is not { } list)
            return Status = L("No character open.");

        var strays = list.Items
            .Where(item => item.Mode == ItemMode.Stored && !Pages.Any(entry => entry.Page == item.Page))
            .ToList();

        int moved = 0;
        foreach (var item in strays)
        {
            var (page, cell) = RoomFor(item, []);
            if (page == 0)
                continue;

            ItemMover.Place(item, page, cell.X, cell.Y);
            moved++;
        }

        if (moved > 0)
        {
            MarkEdited();
            Rebuild();
        }

        int left = strays.Count - moved;
        return Status = left == 0
            ? Plural(moved, $"Moved {moved} item into view.", $"Moved {moved} items into view.")
            : Plural(left, $"No free space for {left} item.", $"No free space for {left} items.");
    }

}
