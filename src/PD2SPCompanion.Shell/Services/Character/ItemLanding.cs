using D2SLib.Model.Save;

using PD2SPCompanion.Shell.ViewModels;


using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// Where an item the editor hands over lands - a duplicate, a gem turned out of its socket, on the
/// stash page open and not the first page; the same rules for ejection, around that item. In order: next to the item it came from,
/// where that sits in a grid; else the containers the item page's panes show, the first pane's
/// first; else the character's first room, inventory, cube, stash, as before.
/// <para>
/// <b>What arrives from outside lands the same way</b>: the Forge, trading and the vault's withdraw button land
/// the new item on the active stash page, the default path being only the fallback: <see cref="Welcome"/>
/// for the vault's Withdraw and a purchase, <see cref="Receive"/> for what the Forge makes. Until then they
/// went to the first room whatever page was open - a shared page shown, and the item in the inventory.
/// </para>
/// </summary>
public sealed class ItemLanding
{
    private readonly CharacterSession session;
    private readonly SharedStashSession stash;

    public ItemLanding(CharacterSession session, SharedStashSession stash)
    {
        this.session = session;
        this.stash = stash;

        // A belt coming off, or a smaller one going on: its potions land by this rule (SpillBelt).
        session.BeltSpill = SpillBelt;

        // A piece pushed off with no room on the character's own pages: the shared page a pane shows (Beyond).
        session.Beyond = Beyond;
    }

    /// <summary>
    /// Worn pieces pushed off by one put on, the character's own pages without room for them: the shared page a
    /// pane shows, where the rule finds room there for every one - still the rule,
    /// not a block - each asked after the ones before, so no two take one cell. Null with no
    /// room for one of them, or where the stash refuses one; else what puts them there, for the session to call
    /// once they are out of the character's lists. That answers too: an empty stash has no era until the first lands,
    /// and may then refuse the second. Refused, none stays on the stash and the session puts all back on.
    /// </summary>
    private Func<string?>? Beyond(IReadOnlyList<Item> pushed)
    {
        var promised = new Dictionary<GridViewModel, ItemMover.Occupancy>();
        var spots = new List<(Item Item, Spot Spot)>();
        foreach (var each in pushed)
        {
            if (Find(each, null, promised) is not { Shared: true } spot || stash.Refuses(each) is not null)
                return null;

            spots.Add((each, spot));
        }

        return () =>
        {
            var landed = new List<Item>();
            string? refused = null;
            foreach (var (item, spot) in spots)
            {
                refused = stash.Adopt(item, spot.Page, spot.X, spot.Y, rebuild: false, keepId: true);
                if (refused is not null)
                    break;

                landed.Add(item);
            }

            if (refused is not null)
            {
                foreach (var item in landed)
                    stash.Release(item, rebuild: false);

                stash.MarkEdited();
            }

            stash.RefreshPages();
            return refused;
        };
    }

    /// <summary>Where a stored item is - a page of the character's, or of the shared stash - or null: worn, belted, in neither.</summary>
    public Spot? WhereIs(Item item)
    {
        if (item.Mode != ItemMode.Stored)
            return null;

        if (session.Holds(item))
            return new Spot(false, item.Page, item.X, item.Y);

        return stash.Holds(item) && SharedStash.PageOf(item) is { } page ? new Spot(true, (byte)page, item.X, item.Y) : null;
    }

    /// <summary>A place, as the strip says where something went: "inventory", "cube", "stash", "shared page 5".</summary>
    public static string Named(Spot spot) =>
        spot.Shared ? L($"shared page {spot.Page}")
        : spot.Page switch
        {
            InventoryLayout.InventoryPage => L("inventory"),
            InventoryLayout.CubePage => L("cube"),
            _ => L("stash"),
        };

    /// <summary>
    /// The potions in the character's belt above its first <paramref name="rows"/> rows, landed by the rule - the
    /// page a pane shows, a shared one too, then the first room - all of them or none.
    /// <paramref name="dry"/>: only asked. Null where they fit, or have been moved; else why not.
    /// <paramref name="back"/>: what undoes it (<see cref="SpillBack"/>), where any moved.
    /// </summary>
    private string? SpillBelt(int rows, bool dry, out Action? back)
    {
        back = null;
        var excess = session.BeltItems.Where(item => item.X >= rows * PD2SPCompanion.Core.Characters.Belts.Columns)
                                      .OrderBy(item => item.X).ToList();
        if (excess.Count == 0)
            return null;

        var promised = new Dictionary<GridViewModel, ItemMover.Occupancy>();
        var places = new List<(Item Potion, Spot Spot)>();
        foreach (var potion in excess)
        {
            if (Find(potion, null, promised) is not { } spot || (spot.Shared && stash.Refuses(potion) is not null))
                return L("No free space for belt's potions.");
            places.Add((potion, spot));
        }

        if (dry)
            return null;

        // Before any moves: each one out lets the ones above it fall (CharacterSession.MarkEdited).
        var boxes = excess.Select(session.BoxOf).ToList();
        var moved = new HashSet<Item>(ReferenceEqualityComparer.Instance);

        foreach (var (potion, spot) in places)
        {
            if (ItemLift.Of(session, potion) is not { } lift)
                continue;

            if (lift.Into(spot.Shared ? stash : session, spot.Page, spot.X, spot.Y, keepId: true) is null)
            {
                moved.Add(potion);
                lift.Done();
            }
            else
            {
                lift.Back(); // In its box, not in no list: Welcome may refuse it too.
            }
        }

        session.RefreshContainers();
        stash.RefreshPages();

        if (moved.Count > 0)
            back = () => SpillBack([.. boxes.Where(box => moved.Contains(box.Potion))]);

        return null;
    }

    /// <summary>
    /// The potions a spill moved in their boxes again, the act it was for not happening. Each is taken from whichever
    /// file holds it now and goes back to the one place it came from, so none is lost or held twice; one no file holds
    /// any more (a reload meanwhile), or whose box has been taken, stays where it is.
    /// </summary>
    private void SpillBack(IReadOnlyList<CharacterSession.BeltBox> boxes)
    {
        var taken = session.BeltItems.Select(item => item.X).ToHashSet();
        var coming = new List<CharacterSession.BeltBox>();

        foreach (var box in boxes)
        {
            if (!taken.Contains(box.X)
                && (stash.Holds(box.Potion) ? stash.Release(box.Potion, rebuild: false)
                    : session.Holds(box.Potion) ? session.Release(box.Potion, rebuild: false)
                    : null) is not null)
            {
                coming.Add(box);
            }
        }

        session.PutBackInBelt(coming);
        stash.MarkEdited();

        session.RefreshContainers();
        stash.RefreshPages();
    }

    /// <summary>A place: the character's page, or the shared stash's (<see cref="Shared"/>), and the corner.</summary>
    public sealed record Spot(bool Shared, byte Page, int X, int Y);

    /// <summary>
    /// The containers the item page's panes are showing, first pane first, as the page sets them:
    /// the grids themselves, character or shared - never the materials tab.
    /// </summary>
    public Func<IReadOnlyList<GridViewModel>>? OpenContainers
    {
        get => _open;
        set
        {
            _open = value;

            // The session's own first room starts on the same pages, where they are the character's.
            session.OpenPages = value is null ? null : () => [.. value().Where(grid => !grid.Shared).Select(grid => grid.Page)];
        }
    }

    private Func<IReadOnlyList<GridViewModel>>? _open;

    /// <summary>
    /// Raised when where things can land may have changed - a pane shows another page, or the page
    /// was rebuilt after a move - so an answer given before (the item panel's "no room") is asked
    /// again. Opening a page with room left the panel refusing KEEP.
    /// </summary>
    public event Action? RoomChanged;

    /// <summary>Says where things can land may have changed; see <see cref="RoomChanged"/>.</summary>
    public void SayRoomChanged() => RoomChanged?.Invoke();

    /// <summary>
    /// The cell a paste was made on - the one under the pointer, or the one a menu was opened on (a paste
    /// went near the origin and not to the cursor, even with room there). While it is set, what lands is put there where it fits, and where it does not - the
    /// cell taken, or the next of several - in the free room nearest it on that page; only a page with no room
    /// at all leaves it to the rule below. Set and taken away by the paste (VP_ViewModel_Inv_L.PasteOn).
    /// </summary>
    public (GridViewModel Grid, int X, int Y)? Wish { get; set; }

    /// <summary>The place nearest the wished cell, for a thing of <paramref name="moving"/>'s size - or null: no wish, or no room on its page.</summary>
    private Spot? AtWish(Item moving, Dictionary<GridViewModel, ItemMover.Occupancy> promised)
    {
        if (Wish is not { } wish)
            return null;

        var map = Map(wish.Grid, moving, promised);
        var (width, height) = ItemMover.Footprint(moving);

        (int X, int Y)? best = null;
        (int, int, int) bestRank = default;
        for (int y = 0; y + height <= wish.Grid.Rows; y++)
        {
            for (int x = 0; x + width <= wish.Grid.Columns; x++)
            {
                if (!map.Fits(x, y, width, height))
                    continue;

                // Nearest by the further of the two ways, then by both, then in the game's reading order.
                int across = Math.Abs(x - wish.X), down = Math.Abs(y - wish.Y);
                var rank = (Math.Max(across, down), across + down, y * wish.Grid.Columns + x);
                if (best is null || rank.CompareTo(bestRank) < 0)
                {
                    best = (x, y);
                    bestRank = rank;
                }
            }
        }

        return best is { } cell ? Take(wish.Grid, cell, moving, promised) : null;
    }

    /// <summary>Where <paramref name="moving"/> lands, beside <paramref name="beside"/> where that is in a grid - or null, with no room anywhere.</summary>
    public Spot? Find(Item moving, Item? beside) => Find(moving, beside, []);

    /// <summary>
    /// Items brought in from elsewhere - the vault's Withdraw, a purchase - each handed to the first place the
    /// rule finds: the pages the panes show, a shared page among them, then the character's first room. Each is
    /// asked after the ones before, so no two take one cell; identifiers kept. One rebuild of each side at the end.
    /// </summary>
    /// <returns>
    /// How many went in, which found no room, how many were refused, and the first reason given - an item stamped for
    /// another era, say.
    /// </returns>
    public (int Placed, IReadOnlyList<Item> NoRoom, int RefusedCount, string? Refused) Welcome(IReadOnlyList<Item> items)
    {
        var promised = new Dictionary<GridViewModel, ItemMover.Occupancy>();
        int placed = 0, refusedCount = 0;
        var noRoom = new List<Item>();
        string? refused = null;

        foreach (var item in items)
        {
            if (Find(item, null, promised) is not { } spot)
            {
                noRoom.Add(item);
                continue;
            }

            string? why = spot.Shared
                ? stash.Adopt(item, spot.Page, spot.X, spot.Y, rebuild: false, keepId: true)
                : session.Adopt(item, spot.Page, spot.X, spot.Y, rebuild: false, keepId: true);
            if (why is not null)
            {
                refused ??= why;
                refusedCount++;
                continue;
            }

            placed++;
        }

        session.RefreshContainers();
        stash.RefreshPages();
        return (placed, noRoom, refusedCount, refused);
    }

    /// <summary>
    /// An item of the character's own - worn, the mercenary's, or carried - moved to where the rule lands it: a
    /// right click on a worn item takes it off. Refused as a drag of it would be - a belt
    /// whose potions have nowhere to go, what gives the Iron Golem its skill with no room for the golem's item
    /// - and where nothing has room. A belt's potions above its first row land first (SpillBelt). Its
    /// identifier kept. Null when moved, and <paramref name="landed"/> says where; else why not.
    /// </summary>
    public string? Move(Item item, out Spot? landed)
    {
        landed = null;
        if (session.CannotTakeOff(item) is { } stuck)
            return stuck;

        // A belt's potions first: found a place before them, the belt's own could be the one they take. Back again
        // where the belt then does not go.
        var potionsBack = session.SpillBeltOf(item);

        if (Find(item, null) is not { } spot)
        {
            potionsBack?.Invoke();
            return L("No free space.");
        }

        // Asked before it leaves the character: refused after, it would be nowhere.
        if (spot.Shared && stash.Refuses(item) is { } no)
        {
            potionsBack?.Invoke();
            return no;
        }

        if (ItemLift.Of(session, item) is not { } lift)
        {
            potionsBack?.Invoke();
            return L("Item not moved.");
        }

        string? refused = lift.Into(spot.Shared ? stash : session, spot.Page, spot.X, spot.Y, keepId: true);

        // Never in neither: back where it lay, should the page not take it after all.
        if (refused is null)
        {
            lift.Done();
        }
        else
        {
            lift.Back();
            potionsBack?.Invoke();
        }

        session.RefreshContainers();
        stash.RefreshPages();
        if (refused is null)
            landed = spot;
        return refused;
    }

    /// <summary>
    /// An item the session landed by itself - pushed off by one put on, the golem's given back - moved on to the
    /// shared page a pane shows, where the rule lands it there. The session's own first room already starts at
    /// the character's open pages (<see cref="CharacterSession.OpenPages"/>); a shared page is beyond it. Where it
    /// went, or null where it stays.
    /// </summary>
    public Spot? Rehome(Item item)
    {
        if (item.Mode != ItemMode.Stored || !session.Holds(item) || Find(item, null) is not { Shared: true })
            return null;

        return Move(item, out var landed) is null ? landed : null;
    }

    /// <summary>
    /// An item the editor made - the Forge's - handed over where the rule finds room, unselected: a new
    /// identifier, as <see cref="CharacterSession.Receive"/> and the shared stash's own have it. Null when it landed, and <paramref name="landed"/> says where; else why not, said as the status too.
    /// </summary>
    public string? Receive(Item item, string noRoom, out Spot? landed)
    {
        landed = Find(item, null);
        if (landed is not { } spot)
            return session.Status = noRoom;

        string? refused = spot.Shared
            ? stash.ReceiveAll([(item, spot.Page, spot.X, spot.Y)])
            : session.Receive(item, noRoom, select: false, at: (spot.Page, (spot.X, spot.Y)));
        if (refused is not null)
            landed = null;
        return refused;
    }

    /// <summary>
    /// Which of these would find no place if they all went at once, each after the ones before -
    /// the item panel's "no room" for gems turned out on KEEP, asked by the same rule they land by.
    /// Asked of the session's first rooms alone, a gem with a pane open on a page with room was
    /// refused.
    /// </summary>
    public IReadOnlyList<Item> WithoutRoom(IEnumerable<Item> items, Item? beside)
    {
        var promised = new Dictionary<GridViewModel, ItemMover.Occupancy>();
        return [.. items.Where(item => Find(item, beside, promised) is null)];
    }

    /// <summary>
    /// Where each copy of a batch lands - each beside the item it was made from - or null where any one would
    /// find no place: all of them or none (duplicate several at once, as the vault does;
    /// all or nothing, as moving several is). Each is asked after the ones before, so no two take one cell. A
    /// shared stash item's copy stays in the shared stash, as one copied alone does: beside it, else on the
    /// pages after its own, then before.
    /// </summary>
    public IReadOnlyList<Spot>? PlacesFor(IReadOnlyList<(Item Moving, Item Beside)> batch)
    {
        var promised = new Dictionary<GridViewModel, ItemMover.Occupancy>();
        var spots = new List<Spot>();
        foreach (var (moving, beside) in batch)
        {
            var spot = AtWish(moving, promised) ?? (GridHolding(beside) is { Shared: true } home
                ? InSharedStash(moving, beside, home, promised)
                : Find(moving, beside, promised));
            if (spot is null)
                return null;
            spots.Add(spot);
        }
        return spots;
    }

    /// <summary>
    /// Where <paramref name="items"/> would land were they let go of on a cell (<see cref="Wish"/>): the footprint lit
    /// under gems carried out of the Sockets row before they are let go of. Each beside <paramref name="beside"/> where the wished page has no room; null where one
    /// finds none anywhere. Asked only - nothing moves, and the wish there was before is back after.
    /// </summary>
    public IReadOnlyList<Spot>? PlacesAt((GridViewModel Grid, int X, int Y) wish, IReadOnlyList<Item> items, Item? beside)
    {
        var before = Wish;
        Wish = wish;
        try
        {
            if (beside is not null)
                return PlacesFor([.. items.Select(item => (item, beside))]);

            // From no item - the compendium's - by the rule Welcome lands them by.
            var promised = new Dictionary<GridViewModel, ItemMover.Occupancy>();
            var spots = new List<Spot>();
            foreach (var item in items)
            {
                if (Find(item, null, promised) is not { } spot)
                    return null;
                spots.Add(spot);
            }
            return spots;
        }
        finally
        {
            Wish = before;
        }
    }

    private Spot? InSharedStash(Item moving, Item beside, GridViewModel home, Dictionary<GridViewModel, ItemMover.Occupancy> promised)
    {
        if (ItemMover.FreeCellNear(moving, Map(home, moving, promised), beside) is { } near)
            return Take(home, near, moving, promised);

        foreach (var page in stash.Pages.Where(page => page != home)
                     .OrderBy(page => page.Page > home.Page ? 0 : 1).ThenBy(page => page.Page))
        {
            if (ItemMover.FirstFreeCell(moving, Map(page, moving, promised)) is { } cell)
                return Take(page, cell, moving, promised);
        }

        return null;
    }

    /// <summary>A page as it stands, plus what the batch has taken of it already.</summary>
    private static ItemMover.Occupancy Map(GridViewModel grid, Item moving, Dictionary<GridViewModel, ItemMover.Occupancy> promised)
    {
        if (!promised.TryGetValue(grid, out var map))
            promised[grid] = map = new ItemMover.Occupancy(Size(grid), Contents(grid, moving));
        return map;
    }

    /// <summary>A place found, taken in the batch's pages so the next is not given it too.</summary>
    private static Spot Take(GridViewModel grid, (int X, int Y) cell, Item moving, Dictionary<GridViewModel, ItemMover.Occupancy> promised)
    {
        var (width, height) = ItemMover.Footprint(moving);
        Map(grid, moving, promised).Block(cell.X, cell.Y, width, height);
        return new Spot(grid.Shared, grid.Page, cell.X, cell.Y);
    }

    /// <summary>
    /// The search, against pages as they stand plus what <paramref name="promised"/> has taken of
    /// them already - each place found is taken there too, so a batch never lands two in one cell.
    /// </summary>
    private Spot? Find(Item moving, Item? beside, Dictionary<GridViewModel, ItemMover.Occupancy> promised)
    {
        ItemMover.Occupancy Map(GridViewModel grid) => ItemLanding.Map(grid, moving, promised);
        Spot Take(GridViewModel grid, (int X, int Y) cell) => ItemLanding.Take(grid, cell, moving, promised);

        // Where a paste asked for it, before anything else.
        if (AtWish(moving, promised) is { } wished)
            return wished;

        // Next to the item it came from, on that item's own page.
        if (beside is not null && GridHolding(beside) is { } home
            && ItemMover.FreeCellNear(moving, Map(home), beside) is { } near)
            return Take(home, near);

        // The panes the page has open.
        foreach (var grid in OpenContainers?.Invoke() ?? [])
        {
            if (ItemMover.FirstFreeCell(moving, Map(grid)) is { } cell)
                return Take(grid, cell);
        }

        // The character's first room, in the order it always offered.
        foreach (string name in new[] { "Inventory", "Cube", "Stash" })
        {
            if (session.GridsNamed(name).FirstOrDefault() is { } grid
                && ItemMover.FirstFreeCell(moving, Map(grid)) is { } cell)
                return Take(grid, cell);
        }

        return null;
    }

    /// <summary>The grid a stored item sits in - the character's or the shared stash's - or null (worn, belted, the mercenary's).</summary>
    private GridViewModel? GridHolding(Item item) =>
        item.Mode != ItemMode.Stored ? null
        : session.AllGrids.Concat(stash.Pages).FirstOrDefault(grid => grid.Items.Any(tile => ReferenceEquals(tile.Item, item)));

    private static IEnumerable<Item> Contents(GridViewModel grid, Item moving) =>
        grid.Items.Select(tile => tile.Item).Where(item => !ReferenceEquals(item, moving));

    private static GridSize Size(GridViewModel grid) => new(grid.Columns, grid.Rows);
}
