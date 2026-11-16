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

// Sockets: adding, removing, and gems turned out.
public sealed partial class CharacterSession
{

    /// <summary>
    /// The most sockets the selected item could take, from its base and its level.
    /// </summary>
    public int MaximumSockets(ItemTileViewModel tile) => ItemBases.MaximumSockets(tile.Item);

    /// <summary>
    /// Takes one gem, rune or jewel out of an item and puts it in the inventory. Returns
    /// null when it went, or why it would not.
    /// <para>
    /// A socketed item is nested inside its parent and is not in the character's own list,
    /// so this is a move between two lists rather than a change of coordinates - which is
    /// why it cannot go through the ordinary placement path.
    /// </para>
    /// </summary>
    public string? Unsocket(ItemTileViewModel tile, Item gem) => Unsocket(tile, gem, null, null);

    /// <summary>
    /// <see cref="Unsocket(ItemTileViewModel, Item)"/>, landing where it is told, around that
    /// item - a place on the character (<paramref name="at"/>),
    /// or handed to <paramref name="elsewhere"/>, which puts it in the shared stash and says why
    /// not. Neither: the first room, as before.
    /// </summary>
    public string? Unsocket(ItemTileViewModel tile, Item gem, (byte Page, (int X, int Y) Cell)? at, Func<Item, string?>? elsewhere)
    {
        if (_save?.PlayerItemList is not { } list)
            return Status = L("No character open.");

        if (elsewhere is not null)
        {
            int hole = tile.Item.SocketedItems.IndexOf(gem);
            if (!tile.Item.SocketedItems.Remove(gem))
                return Status = L("Not in current item.");
            if (!tile.Item.IsCompact)
                tile.Item.NumberOfSocketedItems = (byte)tile.Item.SocketedItems.Count;

            // In the form it lies loose in: Season 14 keeps a socketed rune compact and a loose one as a stack.
            var leaving = ItemForge.Loose(gem);
            _turnedOut.AddOrUpdate(gem, leaving);
            if (elsewhere(leaving) is { } refused)
            {
                // Back in its own socket rather than nowhere - or the last, which moved it on the art.
                tile.Item.SocketedItems.Insert(hole, gem);
                if (!tile.Item.IsCompact)
                    tile.Item.NumberOfSocketedItems = (byte)tile.Item.SocketedItems.Count;
                return Status = refused;
            }

            ItemForge.Renumber(tile.Item);

            MarkEdited();
            Rebuild();
            Status = L($"Unsocketed {ItemPresenter.Describe(leaving).Name} to the shared stash.");
            return null;
        }

        var (page, cell) = at ?? RoomFor(gem, []);
        if (page == 0)
            return Status = L("No free space.");

        if (!tile.Item.SocketedItems.Remove(gem))
            return Status = L("Not in current item.");

        ItemForge.Renumber(tile.Item);

        // Never on a compact item: there the same bit is a quest flag, and recomputing it
        // from an empty socket list would write zero over it. Unreachable today, since a
        // compact item has nothing to unsocket, and guarded so it stays that way.
        if (!tile.Item.IsCompact)
            tile.Item.NumberOfSocketedItems = (byte)tile.Item.SocketedItems.Count;

        var loose = ItemForge.Loose(gem);
        _turnedOut.AddOrUpdate(gem, loose);
        ItemMover.Place(loose, page, cell.X, cell.Y);
        list.Items.Add(loose);

        // The count is the list's own and excludes anything nested, so adding a gem that
        // was nested raises it by exactly one.
        list.Count++;

        MarkEdited();
        Rebuild();
        Status = L($"Unsocketed {ItemPresenter.Describe(loose).Name} to {NameOfPage(page).ToLowerInvariant()}.");
        return null;
    }

    /// <summary>
    /// What a gem turned out of its socket lies loose as: in Season 14 a new item (<see cref="ItemForge.Loose"/>), so a
    /// caller that goes on with the gem - a cut pasted on the vault, stored there - goes on with this. The gem itself
    /// where it was never turned out, or lies loose as itself.
    /// </summary>
    public Item TurnedOut(Item gem) => _turnedOut.TryGetValue(gem, out var loose) ? loose : gem;

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Item, Item> _turnedOut = [];

    /// <summary>
    /// The containers a turned-out gem is offered, in the order it is offered them. The
    /// inventory first because that is where a hand would put it, then the cube, then the
    /// character's own stash.
    /// </summary>
    private byte[] Somewhere => [InventoryLayout.InventoryPage, InventoryLayout.CubePage, _layout.StashPage];

    /// <summary>
    /// The character's own pages the item page's panes show, first pane first (ItemLanding's to say). What the
    /// session lands by itself starts there, these last three kinds of arrival
    /// consistent with the others - an item pushed off by one put on, the golem's item given back, a gem
    /// turned out with nowhere beside its item - and only then the inventory, the cube, the stash. A shared page
    /// is another file's, and beyond the session: ItemLanding moves what fell here on to it (Rehome).
    /// </summary>
    public Func<IReadOnlyList<byte>>? OpenPages { get; set; }

    /// <summary>Where the session looks for room, in order: the open pages of the character's own, then the three as ever.</summary>
    private IEnumerable<byte> LandingOrder => (OpenPages?.Invoke() ?? []).Where(Somewhere.Contains).Concat(Somewhere).Distinct();

    /// <summary>
    /// Where this gem would go, given a set of others already promised a place. Returns a
    /// page of 0 when nothing will take it.
    /// <para>
    /// The promised ones matter: turning out three gems into two free cells fits the first
    /// two and not the third, and asking each in turn against the unchanged contents would
    /// say all three were fine.
    /// </para>
    /// </summary>
    private (byte Page, (int X, int Y) Cell) RoomFor(Item gem, IReadOnlyList<(byte Page, Item Item)> promised)
    {
        if (_save?.PlayerItemList is not { } list)
            return (0, (0, 0));

        foreach (byte page in LandingOrder)
        {
            var contents = list.Items
                .Where(item => item.Mode == ItemMode.Stored && item.Page == page)
                .Concat(promised.Where(held => held.Page == page).Select(held => held.Item));

            if (ItemMover.FirstFreeCell(gem, contents, SizeOfPage(page)) is { } cell)
                return (page, cell);
        }

        return (0, (0, 0));
    }

    /// <summary>
    /// Which of these gems would have nowhere to go, if they were all turned out at once.
    /// Asked before anything moves, so the panel can say so before it is too late to
    /// reconsider.
    /// </summary>
    public IReadOnlyList<Item> WithoutRoom(IEnumerable<Item> gems)
    {
        var promised = new List<(byte Page, Item Item)>();
        var stuck = new List<Item>();

        foreach (var gem in gems)
        {
            var (page, cell) = RoomFor(gem, promised);
            if (page == 0)
            {
                stuck.Add(gem);
                continue;
            }

            // Placed only in the reckoning - the item itself is not touched. Its own flags too: this
            // stand-in, built bare, was the flagless item that once took the editor down while a rune
            // was turned out - measured for room, it was asked whether it was a runeword (Item.Flags
            // has materialised itself since).
            var pretend = new Item
            {
                X = (byte)cell.X,
                Y = (byte)cell.Y,
                Page = page,
                Mode = ItemMode.Stored,
                Code = gem.Code,
                Flags = (System.Collections.BitArray)gem.Flags.Clone(),
            };
            promised.Add((page, pretend));
        }

        return stuck;
    }

}
