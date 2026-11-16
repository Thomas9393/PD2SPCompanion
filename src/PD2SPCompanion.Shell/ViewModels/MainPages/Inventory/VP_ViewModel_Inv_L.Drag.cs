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

// Carrying by drag, one item or the picks.
public partial class VP_ViewModel_Inv_L
{
    // ---- carrying by drag, one item or the picks --------------

    /// <summary>What the cursor carries was lifted by a drag, and goes down on the release.</summary>
    public bool IsDragCarrying { get; private set; }

    /// <summary>
    /// What the ghost draws: the one item, or a group in the shape it will land in. A group from
    /// several places has no shape of its own, so it is drawn fanned.
    /// </summary>
    public IReadOnlyList<GhostPart> GhostParts
    {
        get
        {
            if (Session.Held is not { } held)
                return [];

            var parts = new List<GhostPart> { new(held, 0, 0) };
            bool shaped = !GroupIsMixed();
            int fan = 0;

            foreach (var each in Session.HeldGroup)
            {
                fan++;
                parts.Add(shaped
                    ? new GhostPart(each.Tile, each.Dx * ItemTileViewModel.CellSize, each.Dy * ItemTileViewModel.CellSize)
                    : new GhostPart(each.Tile, fan * GhostPart.Fan, fan * GhostPart.Fan));
            }

            return parts;
        }
    }

    /// <summary>Whether a held group keeps no shape: from several containers, or worn or belted.</summary>
    private bool GroupIsMixed() =>
        Session.Held is { } anchor
        && (anchor.Item.Mode != ItemMode.Stored
            || Session.HeldGroup.Any(each => each.Tile.Item.Mode != ItemMode.Stored
                                             || each.Shared != Session.HeldIsShared
                                             || each.Tile.Item.Page != anchor.Item.Page));

    /// <summary>
    /// A drag has left this item (DragPick): carry it - with every pick, where it is one.
    /// An item not picked is selected alone and carried, as Explorer's
    /// drag of an unselected file is. What the inventory lock keeps is refused outright - no
    /// rectangle in its place - and a count in the materials tab is left to the rectangle.
    /// </summary>
    public bool BeginCarry(object item)
    {
        if (TileOf(item) is not { } tile || Session.IsHolding)
            return false;

        // A count in the materials tab: a drag carries one of it out - to
        // land as a stack of one where it is let go (DropFromTab).
        if (Materials.SlotOf(tile) is { } slot)
        {
            if (Stash.MaterialCount(slot) == 0)
            {
                Session.Status = L($"No {PD2SPCompanion.Core.Items.Materials.NameOf(slot)} in the tab.");
                return true;
            }

            _sfxManager?.Method_ButtonSFX_Press();
            if (!KeepsEdited(Stash.MaterialCode(slot)))
                Session.SelectedItem = tile;
            _tabCarry = slot;
            TabCarryChanged();
            return true;
        }

        if (IsKept(tile))
        {
            Session.Status = KeptSay(tile);
            return true;
        }

        List<ItemTileViewModel> group = IsPicking && tile.IsPicked ? PickedTiles() : [tile];

        _sfxManager?.Method_ButtonSFX_Press();

        if (group.Count <= 1)
        {
            if (IsPicking)
                IsPicking = false;

            if (!KeepsEdited(tile.Item.Code))
                Session.SelectedItem = tile;

            // Refused (a belt that cannot come off says why), the drag ends there.
            Session.PickUp(tile, IsInSharedStash(tile));
            if (!Session.IsHolding)
                return true;
        }
        else
        {
            var rest = group
                .Where(each => !ReferenceEquals(each, tile))
                .Select(each => new CarriedItem(each, IsInSharedStash(each), each.Item.X - tile.Item.X, each.Item.Y - tile.Item.Y))
                .ToList();

            Session.PickUpGroup(tile, IsInSharedStash(tile), rest);
        }

        // The shared pages hide what is carried from them. Carrying only the character's own leaves
        // them as they were, and laying all nine out again was 7 ms of a move, 14 on a slower PC
        // (measured).
        if (Session.HeldIsShared || Session.HeldGroup.Any(each => each.Shared))
            Stash.RefreshPages();
        IsDragCarrying = true;
        OnPropertyChanged(nameof(GhostParts));
        return true;
    }

    /// <summary>
    /// The release that ends a drag: everything down where the pointer is, or everything back
    /// where it came from - never some of it: if the fit fails, the movement is reverted altogether.
    /// <paramref name="pack"/> is Shift: into any free cells, as the arrows do.
    /// <para>
    /// <paramref name="onItem"/>: the item the pointer is over, where it is over one - its tile takes the pointer,
    /// so no cell is found there. A gem, a rune or a jewel let go of on it goes into its socket (Socketing).
    /// </para>
    /// </summary>
    public void DropCarried(GridCell? cell, bool pack, ItemTileViewModel? onItem = null)
    {
        IsDragCarrying = false;

        if (!Session.IsHolding)
            return;

        // Nothing is put on a corpse, nor into an item that lies on it: what is carried stays in hand.
        if (cell is { Shared: false, Page: CharacterSession.CorpsePage } || onItem is not null && Session.IsCorpseItem(onItem.Item))
        {
            Session.Status = CharacterSession.CorpseTakesNothing;
            return;
        }

        // A belt column: one item or several, by the belt's own rule.
        if (cell is { Shared: false } && cell.Page == CharacterSession.BeltPage)
        {
            DropIntoBelt(cell.Column);
            return;
        }

        // Several carried, every one a gem, a rune or a jewel, on an item with an empty socket: into it, one after
        // another (Socketing.SocketGroup). Any other group is not that gesture's, and goes on as it always did.
        if (Session.HeldGroup.Count > 0 && TrySocket(onItem ?? HostAt(cell)))
            return;

        if (Session.HeldGroup.Count == 0)
        {
            // On an item with an empty socket, a gem, a rune or a jewel: into it (Socketing). On a stack of its kind:
            // onto it (Stacking).
            if (TrySocket(onItem ?? HostAt(cell)) || TryStack(onItem ?? HostAt(cell)))
                return;

            if (cell is null)
            {
                CancelHold();
                return;
            }

            PlaceAt(cell);
            if (Session.IsHolding)
            {
                string why = Session.Status;
                CancelHold();
                Session.Status = L($"{why} Put back.");
            }

            return;
        }

        var (plan, refused) = cell is null ? ([], L("Nothing there.")) : PlanGroup(cell, pack);
        if (refused is not null || cell is null)
        {
            CancelHold();
            Session.Status = L($"{refused} Put back.");
            return;
        }

        MoveGroup(cell, plan);
        HoverOver(null);
    }

    private StashPageChoice? _hoveredPage;

    /// <summary>The row of the pages' list under what is carried, framed: what a release there would move to.</summary>
    public void HoverPage(StashPageChoice? page)
    {
        if (ReferenceEquals(_hoveredPage, page))
            return;

        if (_hoveredPage is not null)
            _hoveredPage.IsDropTarget = false;
        _hoveredPage = page;
        if (page is not null)
            page.IsDropTarget = true;
    }

    /// <summary>
    /// What is carried let go on a row of the pages' list: onto that page, in its first free room - several packed, largest first - without
    /// the page being shown, as a card let go on a row of the vault's tree goes to that folder. On the row of
    /// the page it came from, or with no room there, it is put back.
    /// </summary>
    public void DropCarriedOnPage(StashPageChoice page)
    {
        HoverPage(null);

        if (page.IsMaterials)
        {
            DropCarriedOnTab();
            return;
        }

        IsDragCarrying = false;
        if (!Session.IsHolding || Session.Held is not { } anchor)
            return;

        if (page.Grid is not { } grid || LiftedFrom(grid, anchor))
        {
            CancelHold();
            return;
        }

        if (Session.HeldGroup.Count == 0)
        {
            var room = new ItemMover.Occupancy(new GridSize(grid.Columns, grid.Rows), grid.Items.Select(each => each.Item));
            var (width, height) = ItemMover.Footprint(anchor.Item);
            if (room.FirstFree(width, height) is not { } spot
                || grid.Cells.FirstOrDefault(each => each.Column == spot.X && each.Row == spot.Y) is not { } cell)
            {
                CancelHold();
                Session.Status = L($"No free space in {page.Label}. Put back.");
                return;
            }

            PlaceAt(cell);
            if (Session.IsHolding)
            {
                string why = Session.Status;
                CancelHold();
                Session.Status = L($"{why} Put back.");
            }

            return;
        }

        var first = grid.Cells.First();
        var (plan, refused) = PlanGroup(first, pack: true);
        if (refused is not null)
        {
            CancelHold();
            Session.Status = L($"{refused} Put back.");
            return;
        }

        MoveGroup(first, plan);
        HoverOver(null);
    }

    /// <summary>
    /// Whether what is carried was lifted from this container. Asked of the item, which still says where it
    /// lay: a container's own list has let go of what is on the cursor (CharacterSession.RebuildTiles), so
    /// asking the list said no of every page - and an item let go on its own page's row was not put back but
    /// sent to the page's first free room.
    /// </summary>
    private bool LiftedFrom(GridViewModel grid, ItemTileViewModel carried) =>
        grid.Shared == Session.HeldIsShared
        && (grid.Shared
            ? PD2SPCompanion.Core.Saves.SharedStash.PageOf(carried.Item) == grid.Page
            : carried.Item.Mode == ItemMode.Stored && carried.Item.Page == grid.Page);

    /// <summary>Whether a tile is one of the materials tab's counts - every slot has one, empty or not.</summary>
    public bool IsTabTile(ItemTileViewModel tile) => Materials.SlotOf(tile) is not null;

    /// <summary>
    /// What is carried, let go of on a count in the materials tab: into the tab, its own slot, as a
    /// drop on the tab's cells is (PutMaterial). The tab has a tile on every slot, drawn over the
    /// cells, so a release there never finds a cell - and every drag onto the tab was put back.
    /// Several at once go together or not at all, as a group goes anywhere (PutMaterials; it was one at a time).
    /// </summary>
    public void DropCarriedOnTab()
    {
        IsDragCarrying = false;

        if (!Session.IsHolding)
            return;

        if (Session.HeldGroup.Count > 0)
        {
            PutMaterials();
            if (Session.IsHolding)
            {
                string refused = Session.Status;
                CancelHold();
                Session.Status = L($"{refused} Put back.");
            }
            return;
        }

        PutMaterial();
        if (Session.IsHolding)
        {
            string why = Session.Status;
            CancelHold();
            Session.Status = L($"{why} Put back.");
        }
    }

    /// <summary>Where one carried item would land: its own file, the cell it would take.</summary>
    private sealed record Placement(ItemTileViewModel Tile, bool FromShared, int X, int Y);

    /// <summary>
    /// Where a held group would land with the held item on <paramref name="cell"/>, and why not.
    /// Its shape kept, where it has one and <paramref name="pack"/> is not asked; otherwise packed
    /// into the free cells, largest first. Everything that could refuse is asked here, before
    /// anything moves: the item era a file takes, a belt that cannot come off, the room.
    /// </summary>
    private (List<Placement> Plan, string? Why) PlanGroup(GridCell cell, bool pack)
    {
        var plan = new List<Placement>();
        if (Session.Held is not { } anchor)
            return (plan, L("Nothing held."));

        var all = new List<(ItemTileViewModel Tile, bool Shared, int Dx, int Dy)> { (anchor, Session.HeldIsShared, 0, 0) };
        all.AddRange(Session.HeldGroup.Select(each => (each.Tile, each.Shared, each.Dx, each.Dy)));

        if (Materials.Owns(cell))
            return (plan, L("Materials tab takes one at a time."));

        // The belt has a plan of its own (PlanBelt); its callers go there first.
        if (!cell.Shared && cell.Page == CharacterSession.BeltPage)
            return (plan, L("Nothing there."));

        if (GridFor(cell.Shared, cell.Page) is not { } grid)
            return (plan, L("Nothing there."));

        string? era = cell.Shared ? Stash.ItemEra : Session.ItemEra;
        foreach (var (tile, shared, _, _) in all)
        {
            if (shared != cell.Shared && era is { Length: > 0 } wanted
                && tile.Item.Version is { Length: > 0 } version && !PD2SPCompanion.Core.Saves.ItemSeason.Mix(wanted, version))
            {
                return (plan, cell.Shared
                    ? L($"Require same version: item v{version}, shared stash v{wanted}.")
                    : L($"Require same version: item v{version}, character v{wanted}."));
            }

            // What the file itself asks of an arrival, its last season's encoding too: refused after it left, it would be nowhere.
            if ((cell.Shared ? Stash.Refuses(tile.Item) : Session.Refuses(tile.Item)) is { } no)
                return (plan, no);

            if (Session.CannotTakeOff(tile.Item) is { } stuck)
                return (plan, stuck);
        }

        var carried = all.Select(each => each.Tile.Item).ToHashSet(ReferenceEqualityComparer.Instance);
        var room = new ItemMover.Occupancy(
            new GridSize(grid.Columns, grid.Rows),
            grid.Items.Select(each => each.Item).Where(item => !carried.Contains(item)));

        if (!pack && !GroupIsMixed())
        {
            string? why = null;
            foreach (var (tile, shared, dx, dy) in all)
            {
                var (width, height) = ItemMover.Footprint(tile.Item);
                int x = cell.Column + dx, y = cell.Row + dy;

                if (room.Fits(x, y, width, height))
                    room.Block(x, y, width, height);
                else
                    why ??= L("Does not fit there.");

                plan.Add(new Placement(tile, shared, x, y));
            }

            return (plan, why);
        }

        foreach (var (tile, shared, _, _) in all.OrderByDescending(each =>
                 {
                     var (width, height) = ItemMover.Footprint(each.Tile.Item);
                     return width * height;
                 }))
        {
            var (width, height) = ItemMover.Footprint(tile.Item);
            if (room.FirstFree(width, height) is not { } spot)
                return (plan, L("No free space for all of them."));

            room.Block(spot.X, spot.Y, width, height);
            plan.Add(new Placement(tile, shared, spot.X, spot.Y));
        }

        return (plan, null);
    }

    /// <summary>
    /// Carries out a plan that fits: every item let go by its own file, then taken in by the
    /// target's at its cell - two passes, so no item is ever checked against where another has
    /// just left. One rebuild at the end. Should a file still refuse one, it goes back to its own
    /// file and its own place there, its slot where it was worn.
    /// </summary>
    private void MoveGroup(GridCell cell, List<Placement> plan)
    {
        string where = GridFor(cell.Shared, cell.Page)?.Name ?? "container";
        var cells = plan.ToDictionary(
            each => each.Tile.Item,
            each => (each.X, each.Y),
            (System.Collections.Generic.IEqualityComparer<D2SLib.Model.Save.Item>)ReferenceEqualityComparer.Instance);

        Session.PutDown();

        var lifts = ItemLift.OfAll(plan.Select(each => (Holder(each.FromShared), each.Tile.Item)));

        int moved = 0;
        string? refused = null;
        var back = new List<ItemLift>();

        foreach (var lift in lifts)
        {
            var (x, y) = cells[lift.Item];
            if (lift.Into(Holder(cell.Shared), cell.Page, x, y) is not { } why)
            {
                lift.Done();
                moved++;
                continue;
            }

            refused ??= why;
            back.Add(lift);
        }

        // After the rest have landed, so a cell one of them took is known.
        ItemLift.Back(back);

        Session.RefreshContainers();
        Stash.RefreshPages();

        Session.Status = refused is null
            ? L($"Moved {moved} items to the {where.ToLowerInvariant()}.")
            : L($"Moved {moved} of {plan.Count}. {refused}");
    }
}
