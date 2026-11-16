using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;

using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

// A gem, a rune or a jewel let go of on an item with an empty socket goes into it, which makes a runeword this way too - the game's own gesture,
// and the one thing about sockets the editor could not do: it took them out, or made a whole runeword at once.
//
//   carried                              let go on                           what happens
//   one gem, rune or jewel               an item with an empty socket        into the next socket
//   a stack of gems or runes             the same                            one of the stack; the rest goes back
//   a count dragged out of the           the same                            one out of the tab
//     materials tab
//   a rune filling the last socket       a normal or superior base           the runeword, rolled (Socketing)
//   several at once, every one a gem,    an item with an empty socket        one after another, in reading order,
//     a rune or a jewel                                                        while it has a socket for the next
//   several at once, anything else       anything                            as before: no socketing
//     among them
//   anything                             an item with no empty socket        as before: the cell is taken
//
// The item may lie in any grid of the page - the character's or a shared page - or be worn, by the character or
// the mercenary. The vault's cards are neither a place to socket into nor a place to drag a filler from: that is
// another file, and a card is taken out before anything is done to it. It is the game's own action and costs the
// gem, so every play mode has it; what a lock keeps is left alone. An edit like any other: undone, saved.
public partial class VP_ViewModel_Inv_L
{
    /// <summary>The code what is carried would be socketed as - one item held, or a count out of the tab - or null.</summary>
    private string? CarriedFiller =>
        _tabCarry is { } slot ? PD2SPCompanion.Core.Items.Socketing.FillerCode(PD2SPCompanion.Core.Items.Materials.CodeAt(slot))
        : Session.Held is { } held && Session.HeldGroup.Count == 0 ? PD2SPCompanion.Core.Items.Socketing.FillerCode(held.Item.Code)
        : null;

    /// <summary>
    /// The socket guard, a button with a lock sign near the opaque socket button, as a user may drop by accident: while it is on, nothing let go of on an item is
    /// socketed - the item does not light, and the drop says so and puts everything back. Kept in the settings;
    /// off by default.
    /// </summary>
    public bool SocketGuard
    {
        get => Rules.SocketGuard;
        set => Rules.SocketGuard = value;
    }

    public string SocketGuardHint => SocketWords.Guard(SocketGuard);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleSocketGuard()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SocketGuard = !SocketGuard;
        Session.Status = SocketGuard ? L("Socket guard on.") : L("Socket guard off.");
    }

    /// <summary>
    /// Whether a drag of this leaves the selection where it is: a gem, a rune or a jewel lifted while the item
    /// selected - the one the Edit page shows - has an empty socket (a socketable let go of
    /// on the Edit page's row of sockets goes in). A drag selects what it lifts, the Edit page follows the
    /// selection, and the row aimed at was gone as the drag began.
    /// </summary>
    private bool KeepsEdited(string code) =>
        Session.SelectedItem is { TabSlot: null } shown
        && PD2SPCompanion.Core.Items.Socketing.FillerCode(code) is not null
        && PD2SPCompanion.Core.Items.Socketing.HasRoom(shown.Item);

    /// <summary>Whether what is carried would go into the item the Edit page shows, were it let go of on its Sockets section.</summary>
    public bool WouldSocketEdited(ItemTileViewModel? shown) =>
        shown is not null && ReferenceEquals(shown, Session.SelectedItem) && WouldSocket(shown);

    /// <summary>
    /// What is carried, let go of on the Edit page's Sockets section: into the next socket of the item it shows, as
    /// let go of on the item itself (<see cref="TrySocket"/>). False where it is not that gesture's - the release
    /// is then whatever it would have been.
    /// </summary>
    public bool DropOnEdited(ItemTileViewModel? shown)
    {
        if (shown is null || !ReferenceEquals(shown, Session.SelectedItem) || Session.SelectedItem is not { } host || !Fits(host))
            return false;

        IsDragCarrying = false;
        return TrySocket(host);
    }

    /// <summary>Whether a cell of a grid lies bare: no item over it, and neither the belt's nor the materials tab's - where a gem dragged out of a socket may be put down.</summary>
    public bool IsFreeRoom(GridCell cell) =>
        !Materials.Owns(cell) && !(!cell.Shared && cell.Page == CharacterSession.BeltPage)
        && GridFor(cell.Shared, cell.Page) is not null && HostAt(cell) is null;

    /// <summary>
    /// Several carried, and every one of them a gem, a rune or a jewel: they go in one after another. With anything else among them the group is
    /// carried as it always was - it is being moved, not socketed.
    /// </summary>
    private bool CarriesFillersOnly =>
        _tabCarry is null && Session.Held is { } lead && Session.HeldGroup.Count > 0
        && Session.HeldGroup.Select(each => each.Tile).Prepend(lead)
            .All(tile => tile.TabSlot is null && PD2SPCompanion.Core.Items.Socketing.FillerCode(tile.Item.Code) is not null);

    /// <summary>Whether what is carried would go into this item's socket - the guard aside.</summary>
    private bool Fits(ItemTileViewModel? host) =>
        host is { TabSlot: null } && !Session.IsHeld(host.Item)
        && (CarriedFiller is not null || CarriesFillersOnly || (RowFrom is { } from && !ReferenceEquals(host.Item, from)))
        && PD2SPCompanion.Core.Items.Socketing.HasRoom(host.Item);

    // ---- carried out of the Edit page's Sockets row onto another item ---------------------------
    //
    // The drag out of the row was the one carry of a gem that went into no item: it put the gem down on a bare cell
    // alone. Let go of on an item with an empty socket it goes in, as every other carried gem does - lit green under
    // the pointer the same way. Several carried, as many as it has room for, in the row's order; the rest stay in
    // their sockets. The socket guard stands in its way; the item it came from is no target.

    /// <summary>The item a gem is being carried out of the Sockets row of, while it is: the Edit page's. Null else.</summary>
    private Item? RowFrom =>
        PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared is { IsCarrying: true, FromRow: true } ? Session.SelectedItem?.Item : null;

    /// <summary>The compendium's item is being carried off its Grail card (SocketCarry, not from a row): it lands as an item.</summary>
    private bool CompendiumCarrying =>
        PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared is { IsCarrying: true, FromRow: false };

    /// <summary>The item lying over a cell of a grid, or null - for the row's carry, which knows cells and not tiles.</summary>
    public ItemTileViewModel? HostOver(GridCell? cell) => HostAt(cell);

    /// <summary>Whether gems carried out of <paramref name="from"/>'s Sockets row would go into <paramref name="host"/>.</summary>
    public bool TakesFromRow(ItemTileViewModel? host, ItemTileViewModel? from) =>
        !SocketGuard && host is { TabSlot: null } && from is not null && !ReferenceEquals(host.Item, from.Item)
        && PD2SPCompanion.Core.Items.Socketing.HasRoom(host.Item);

    /// <summary>
    /// Gems carried out of the Sockets row let go of on <paramref name="host"/>: as many as it has room for, in the row's
    /// order, turned out of their item (<paramref name="takeOut"/>, the row's own way, kept at once) and socketed into it
    /// one after another as dropped gems are (<see cref="SocketEach"/>); the rest stay in their sockets. The Edit page
    /// stays on the item they came from. True where it was this gesture's - done, or refused in words.
    /// </summary>
    public bool SocketFromRow(ItemTileViewModel host, IReadOnlyList<Item> gems, Func<IReadOnlyList<Item>, GridCell?, bool> takeOut)
    {
        if (Session.SelectedItem is not { } from || !TakesFromRow(host, from) || gems.Count == 0)
            return false;

        if (IsKept(host))
        {
            Session.Status = KeptSay(host);
            return true;
        }

        var into = host.Item;
        string hostName = host.Name;
        int room = Math.Max(0, into.TotalNumberOfSockets - into.SocketedItems.Count);
        var going = gems.Take(room).ToList();
        if (going.Count == 0 || !takeOut(going, null))
            return false;

        // What they lie loose as now: in Season 14 new items (CharacterSession.TurnedOut) - socketed, the old ones went nowhere.
        going = [.. going.Select(Session.TurnedOut)];
        int went = SocketEach(into, [.. going.Select(gem => (gem, AllTiles().FirstOrDefault(tile => ReferenceEquals(tile.Item, gem)) is { } lying && IsInSharedStash(lying)))]);

        if (AllTiles().FirstOrDefault(tile => ReferenceEquals(tile.Item, from.Item)) is { } again)
            Session.SelectedItem = again;

        // One in: its own line said it (TrySocket). Several, or some left behind: how many.
        int stayed = gems.Count - going.Count;
        if (went == going.Count && stayed > 0)
            Session.Status = L($"{went} of {gems.Count} socketed into {hostName}. {stayed} stayed.");
        else if (went > 1 && !into.IsRuneword)
            Session.Status = L($"{went} items socketed into {hostName}.");

        return true;
    }

    /// <summary>
    /// Several fillers let go of on an item: put back where they lay, and then each lifted and socketed by the
    /// one-at-a-time way (<see cref="TrySocket"/>), which knows a stack, another file's item and a version - in
    /// reading order, until the item has no socket for the next or one is refused in its own words. The rest
    /// stay where they were.
    /// </summary>
    private bool SocketGroup(ItemTileViewModel host)
    {
        var lead = Session.Held!;
        var carried = Session.HeldGroup.Select(each => (each.Tile.Item, Shared: each.Shared, Down: each.Dy, Across: each.Dx))
            .Prepend((lead.Item, Shared: Session.HeldIsShared, Down: 0, Across: 0))
            .OrderBy(each => each.Down).ThenBy(each => each.Across).ToList();
        var into = host.Item;
        string hostName = host.Name;

        CancelHold();
        IsDragCarrying = false;
        IsPicking = false;

        if (SocketGuard)
        {
            string guard = L("Socket guard on.");
            Session.Status = L($"{guard} Put back.");
            return true;
        }

        int went = SocketEach(into, [.. carried.Select(each => (each.Item, each.Shared))]);

        // The last one's line says a runeword made; else how many went in.
        if (went > 1 && !into.IsRuneword)
            Session.Status = L($"{went} items socketed into {hostName}.");

        return true;
    }

    /// <summary>
    /// Items lying in the grids lifted and socketed one after another, each by the one-at-a-time way: until the
    /// item has no socket for the next, or one is refused in its own words. How many went in.
    /// </summary>
    private int SocketEach(Item into, IReadOnlyList<(Item Item, bool Shared)> fillers)
    {
        int went = 0;
        foreach (var (item, shared) in fillers)
        {
            if (!PD2SPCompanion.Core.Items.Socketing.HasRoom(into)
                || AllTiles().FirstOrDefault(each => ReferenceEquals(each.Item, item)) is not { } tile
                || AllTiles().FirstOrDefault(each => ReferenceEquals(each.Item, into)) is not { } now)
            {
                break;
            }

            int before = into.SocketedItems.Count;
            Session.PickUp(tile, shared);
            if (!Session.IsHolding)
                break;

            if (!TrySocket(now))
            {
                CancelHold();
                break;
            }

            // Refused, in its own words on the strip: the rest stay where they are.
            if (into.SocketedItems.Count == before)
                break;

            went++;
        }

        return went;
    }

    // ---- pasted into an item's sockets -------
    //
    //   the clipboard holds                  pasted on the Sockets section        what happens
    //   gems, runes, jewels cut in a grid    of an item with an empty socket      each into the next socket, as dropped on it
    //   the same, copied                     the same, where the play mode        a second of each into the next socket;
    //                                          makes seconds                        the clipboard keeps them, to paste again
    //   a gem copied in a socket             the same                             a second of it
    //   anything else among them, a vault    -                                    Paste is dimmed
    //     card, a folder, a gem cut in a
    //     socket
    //
    // A paste is asked for, where a drop may be a slip: the socket guard is the drop's.

    /// <summary>A paste is under way: the socket guard, which is against a slip of the hand, does not stand in its way.</summary>
    private bool _deliberate;

    /// <summary>What the clipboard holds, where all of it would go into a socket: in reading order, tiles first. Null else.</summary>
    private List<Item>? ClipFillers(Item into)
    {
        if (Clip.IsEmpty || Clip.Cards.Count > 0 || Clip.Folders.Count > 0 || Clip.SocketedGems.Count > 0 || Clip.Items.Count == 0)
            return null;

        if (Clip.Items.Any(item => ReferenceEquals(item, into) || PD2SPCompanion.Core.Items.Socketing.FillerCode(item.Code) is null))
            return null;

        return [.. Clip.Items.OrderBy(item => item.Y).ThenBy(item => item.X)];
    }

    /// <summary>Whether what the clipboard holds would go into this item's sockets, pasted on its Sockets section.</summary>
    public bool FitsSockets(ItemTileViewModel? host) =>
        host is { TabSlot: null } && PD2SPCompanion.Core.Items.Socketing.HasRoom(host.Item)
        && ClipFillers(host.Item) is not null && (Clip.Kind == PD2SPCompanion.Shell.Services.Generic.ClipKind.Cut || Rules.Duplicates);

    /// <summary>
    /// The paste: every filler held into the item's next socket, while it has one. A cut is lifted from where it
    /// lies and socketed as a drop on the item sockets it; a copy is a second of each, made and put in. True
    /// where it was this paste's - done, or refused in words on the strip.
    /// </summary>
    public bool PasteIntoSockets(ItemTileViewModel? host)
    {
        if (host is null || !FitsSockets(host) || Session.IsHolding || ClipFillers(host.Item) is not { } fillers)
            return false;

        _sfxManager?.Method_ButtonSFX_Press();
        DisarmDelete();

        if (IsKept(host))
        {
            Session.Status = KeptSay(host);
            return true;
        }

        var into = host.Item;
        string hostName = host.Name;
        int went = 0;

        if (Clip.Kind == PD2SPCompanion.Shell.Services.Generic.ClipKind.Cut)
        {
            var lying = AllTiles().Where(tile => fillers.Contains(tile.Item)).ToList();

            // Locked since it was cut: a lock keeps it from every move. The cut stands, as on any paste a lock refuses.
            if (lying.FirstOrDefault(IsKept) is { } kept)
            {
                Session.Status = KeptSay(kept);
                return true;
            }

            Clip.Clear();
            IsPicking = false;
            _deliberate = true;
            try
            {
                went = SocketEach(into, [.. fillers.Select(item => (item, lying.FirstOrDefault(tile => ReferenceEquals(tile.Item, item)) is { } tile && IsInSharedStash(tile)))]);
            }
            finally
            {
                _deliberate = false;
            }
        }
        else
        {
            foreach (var original in fillers)
            {
                if (!PD2SPCompanion.Core.Items.Socketing.HasRoom(into) || !SocketCopy(into, original))
                    break;

                went++;
            }
        }

        if (went > 1 && !into.IsRuneword)
            Session.Status = L($"{went} items socketed into {hostName}.");

        return true;
    }

    /// <summary>A second of a copied filler, made and put into the item's next socket. False, said, where it cannot be.</summary>
    private bool SocketCopy(Item into, Item original)
    {
        if (AllTiles().FirstOrDefault(each => ReferenceEquals(each.Item, into)) is not { } host
            || PD2SPCompanion.Core.Items.Socketing.FillerCode(original.Code) is not { } code)
        {
            return false;
        }

        bool hostShared = IsInSharedStash(host);
        string hostName = host.Name;
        string stamp = host.Item.Version is { Length: > 0 } version ? version : (hostShared ? Stash.ItemEra : Session.ItemEra) ?? "";
        Item filler;

        // The thing itself where it is one - a jewel with its own properties; one of them where it is a stack.
        if (string.Equals(original.Code.Trim(), code, StringComparison.OrdinalIgnoreCase))
        {
            if ((Stash.Holds(original) ? Stash.CopyOf(original) : Session.CopyOf(original)) is not { } made)
            {
                Session.Status = L($"{ItemPresenter.Describe(original).Name} cannot be copied.");
                return false;
            }

            filler = made;
            if (filler.Version is { Length: > 0 } own && stamp.Length > 0 && !PD2SPCompanion.Core.Saves.ItemSeason.Mix(stamp, own))
            {
                Session.Status = L($"Require same version: item v{own}, {hostName} v{stamp}.");
                return false;
            }
        }
        else
        {
            filler = ItemForge.Material(code, 1, stamp);
            filler.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        }

        string fillerName = ItemPresenter.Describe(filler).Name;
        var word = PD2SPCompanion.Core.Items.Socketing.Put(into, filler, Random.Shared);
        Session.Made(filler);

        if (hostShared)
            Stash.MarkEdited();
        else
            Session.MarkEdited();

        Session.RefreshContainers();
        Stash.RefreshPages();

        Session.Status = word is null
            ? L($"{fillerName} socketed into {hostName}.")
            : L($"{fillerName} socketed into {hostName}: {PD2SPCompanion.Core.Items.Runewords.DisplayName(word)} made.");
        return true;
    }

    /// <summary>Whether what is carried would go into this item's socket, were it let go of now.</summary>
    public bool WouldSocket(ItemTileViewModel? host) => !SocketGuard && Fits(host);

    /// <summary>The item lying over a cell of a grid, what is carried left out - or null: an empty cell, the belt, the materials tab.</summary>
    private ItemTileViewModel? HostAt(GridCell? cell)
    {
        if (cell is null || Materials.Owns(cell) || (!cell.Shared && cell.Page == CharacterSession.BeltPage))
            return null;

        return GridFor(cell.Shared, cell.Page)?.Items.FirstOrDefault(tile =>
        {
            if (Session.IsHeld(tile.Item))
                return false;

            var (wide, tall) = ItemMover.Footprint(tile.Item);
            return cell.Column >= tile.Item.X && cell.Column < tile.Item.X + wide
                && cell.Row >= tile.Item.Y && cell.Row < tile.Item.Y + tall;
        });
    }

    /// <summary>The first cell an item in one of the page's grids lies on, or null - for a pointer over the item itself.</summary>
    public GridCell? CellOf(ItemTileViewModel tile) =>
        Showing().Where(grid => grid.Items.Contains(tile))
                 .SelectMany(grid => grid.Cells)
                 .FirstOrDefault(cell => cell.Column == tile.Item.X && cell.Row == tile.Item.Y);

    /// <summary>Lights the item a carried filler would go into, or the stack a carried one would join, cell by cell, green. True when it did.</summary>
    private bool LightHost(GridCell cell)
    {
        if (HostAt(cell) is not { } host || !(WouldSocket(host) || WouldStack(host)))
            return false;

        var (wide, tall) = ItemMover.Footprint(host.Item);
        Light(cell.Shared, cell.Page, host.Item.X, host.Item.Y, wide, tall, allowed: true);
        return true;
    }

    /// <summary>Lights a block of the cells of the page shown as <paramref name="shared"/>/<paramref name="page"/>: green where <paramref name="allowed"/>, red else.</summary>
    private void Light(bool shared, int page, int x, int y, int wide, int tall, bool allowed)
    {
        foreach (var grid in Showing().Where(grid => grid.Page == page && grid.Shared == shared))
        {
            foreach (var each in grid.Cells)
            {
                if (each.Column < x || each.Column >= x + wide || each.Row < y || each.Row >= y + tall)
                    continue;

                each.IsTarget = true;
                each.IsAllowed = allowed;
                each.IsIdle = false;
            }
        }
    }

    // ---- carried out of the Sockets row onto a cell ---------------------------------------------
    //
    // Let go of on a cell with no item there that takes them, gems out of the row land as a paste does: on the cell,
    // and the rest in the free room nearest it on its page (ItemLanding.Wish). Only the first was put on the cell;
    // the rest went beside the item they came out of - "a weird spread" - and nothing was lit before the drop.

    /// <summary>The cell a drop out of the Sockets row is wished onto - a cell of a page that takes items; null else.</summary>
    private (GridViewModel Grid, int X, int Y)? RowWish(GridCell cell)
    {
        if (Materials.Owns(cell) || (!cell.Shared && cell.Page is CharacterSession.BeltPage or CharacterSession.CorpsePage)
            || GridFor(cell.Shared, cell.Page) is not { } grid)
        {
            return null;
        }

        return (grid, cell.Column, cell.Row);
    }

    /// <summary>
    /// Gems carried out of the Sockets row let go of on <paramref name="cell"/>: <paramref name="eject"/> - the row's
    /// own way out - run with the cell wished, so they land on it and about it as a paste does. With no cell they
    /// land where what arrives lands.
    /// </summary>
    public void LandFromRow(GridCell? cell, Action eject)
    {
        if (cell is null || RowWish(cell) is not { } wish)
        {
            eject();
            return;
        }

        _landing.Wish = wish;
        try
        {
            eject();
        }
        finally
        {
            _landing.Wish = null;
        }
    }

    /// <summary>
    /// Lights where the gems carried out of the Sockets row would land, let go of on <paramref name="cell"/>: each
    /// one's cells green on the page under the pointer; the cell under it red where they find no room at all. False
    /// where the cell is none they land about.
    /// </summary>
    private bool LightRowLanding(GridCell cell)
    {
        // Out of the compendium as well: from no item, so beside none.
        var from = RowFrom;
        if ((from is null && !CompendiumCarrying) || RowWish(cell) is not { } wish)
            return false;

        var gems = PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.Parts.Select(part => part.Tile.Item).ToList();
        if (_landing.PlacesAt(wish, gems, from) is not { } spots)
        {
            Light(cell.Shared, cell.Page, cell.Column, cell.Row, 1, 1, allowed: false);
            return true;
        }

        for (int at = 0; at < spots.Count; at++)
        {
            var (wide, tall) = ItemMover.Footprint(gems[at]);
            Light(spots[at].Shared, spots[at].Page, spots[at].X, spots[at].Y, wide, tall, allowed: true);
        }

        return true;
    }

    /// <summary>
    /// What is carried, let go of on <paramref name="host"/>: into its next socket, where it is a filler and the
    /// item has one empty. False where it is not this gesture's - the drop is then whatever it was before.
    /// True where it was: socketed, or refused in words on the strip and everything back where it came from.
    /// </summary>
    private bool TrySocket(ItemTileViewModel? host)
    {
        if (host is not null && CarriesFillersOnly && Fits(host))
            return SocketGroup(host);

        if (host is null || !Fits(host) || CarriedFiller is not { } code)
            return false;

        int? tab = _tabCarry;
        var held = Session.Held;
        bool heldShared = Session.HeldIsShared;
        bool hostShared = IsInSharedStash(host);
        string hostName = host.Name;

        void Back(string why)
        {
            if (tab is not null)
                EndTabCarry();
            else
                CancelHold();

            IsDragCarrying = false;
            Session.Status = tab is null ? L($"{why} Put back.") : why;
        }

        if (SocketGuard && !_deliberate)
        {
            Back(L("Socket guard on."));
            return true;
        }

        if (IsKept(host))
        {
            Back(KeptSay(host));
            return true;
        }

        // What goes in is stamped as the item it goes into, in whose file it will be written.
        string stamp = host.Item.Version is { Length: > 0 } version ? version : (hostShared ? Stash.ItemEra : Session.ItemEra) ?? "";
        Item filler;

        if (tab is { } slot)
        {
            if (Stash.MaterialCount(slot) == 0)
            {
                Back(L($"No {PD2SPCompanion.Core.Items.Materials.NameOf(slot)} in the tab."));
                return true;
            }

            filler = PD2SPCompanion.Core.Items.Socketing.Single(code, stamp, Random.Shared);
            EndTabCarry();
            Stash.TakeMaterial(slot, 1);
        }
        else
        {
            var lying = held!.Item;
            var taken = PD2SPCompanion.Core.Items.Socketing.Take(lying, hostName, stamp, PD2SPCompanion.Core.Saves.ItemSeason.Mix, Random.Shared);
            if (taken.Refused is { } no)
            {
                Back(no);
                return true;
            }

            filler = taken.Filler!;

            // The thing itself, into another file's item: asked as that file asks of anything arriving.
            if (ReferenceEquals(filler, lying) && heldShared != hostShared && (hostShared ? Stash.Refuses(filler) : Session.Refuses(filler)) is { } refused)
            {
                Back(refused);
                return true;
            }

            if (taken.Left > 0)
            {
                lying.Quantity = (ushort)taken.Left;
                Session.CancelHold();
                if (heldShared)
                    Stash.MarkEdited();
                else
                    Session.MarkEdited();
            }
            else
            {
                if (ItemLift.Of(Holder(heldShared), lying) is not { } lift)
                {
                    Back(L("Item not moved."));
                    return true;
                }

                lift.Done();
                Session.PutDown();
            }
        }

        IsDragCarrying = false;
        string fillerName = ItemPresenter.Describe(filler).Name;
        var word = PD2SPCompanion.Core.Items.Socketing.Put(host.Item, filler, Random.Shared);

        _sfxManager?.Method_ButtonSFX_Press();
        if (hostShared)
            Stash.MarkEdited();
        else
            Session.MarkEdited();

        Session.RefreshContainers();
        Stash.RefreshPages();
        HoverOver(null);

        Session.Status = word is null
            ? L($"{fillerName} socketed into {hostName}.")
            : L($"{fillerName} socketed into {hostName}: {PD2SPCompanion.Core.Items.Runewords.DisplayName(word)} made.");
        return true;
    }
}
