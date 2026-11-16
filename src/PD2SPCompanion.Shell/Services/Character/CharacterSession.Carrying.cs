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

// An item or a group on the cursor: picking up, placing, putting back.
public sealed partial class CharacterSession
{

    /// <summary>
    /// The item lifted onto the cursor, not yet put down. Nothing in the save changes
    /// while something is held - the item still says where it was - so picking one up and
    /// changing your mind is not an edit. A worn belt's potions above its first row do land when it is lifted, so
    /// that the page shows them and the drop finds them there; putting it back returns them (<see cref="_spillBack"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHolding))]
    private ItemTileViewModel? _held;

    public bool IsHolding => Held is not null;

    /// <summary>
    /// The rest of a group on the cursor, beside <see cref="Held"/> - the item the drag started
    /// on, which the others keep their offsets from.
    /// Empty for one item. Like the one, nothing in either file changes until they are put down.
    /// </summary>
    public IReadOnlyList<CarriedItem> HeldGroup { get; private set; } = [];

    partial void OnHeldChanged(ItemTileViewModel? value)
    {
        if (value is null)
        {
            HeldGroup = [];

            // Down, wherever it went: what the belt spilled stays spilled. CancelHold has asked for it back by now.
            _spillBack = null;
        }
    }

    /// <summary>What puts back the potions a belt on the cursor spilled when it was lifted; null with none.</summary>
    private Action? _spillBack;

    /// <summary>The belt's potions in their boxes again: it is not leaving after all.</summary>
    private void PutSpillBack()
    {
        var back = _spillBack;
        _spillBack = null;
        back?.Invoke();
    }

    /// <summary>Whether this item is on the cursor - held, or one of a held group.</summary>
    public bool IsHeld(Item item) =>
        ReferenceEquals(Held?.Item, item) || HeldGroup.Any(each => ReferenceEquals(each.Tile.Item, item));

    /// <summary>Lifts several at once: <paramref name="anchor"/> the one the drag started on.</summary>
    public void PickUpGroup(ItemTileViewModel anchor, bool anchorShared, IReadOnlyList<CarriedItem> rest)
    {
        if (Held is not null)
            return;

        // The Iron Golem's item is handed back by dismissing the golem, not carried off - nor is one on the corpse.
        if (rest.Select(each => each.Tile).Prepend(anchor).Select(each => KeptApart(each.Item)).FirstOrDefault(why => why is not null) is { } apart)
        {
            Status = apart;
            return;
        }

        // A worn belt among them: its potions above the first row land first, or it stays (BeltSpill). Every one asked
        // before any moves: the belt's potions landed, and a later one refused, they stayed out with nothing lifted.
        var own = rest.Where(each => !each.Shared).Select(each => each.Tile).Concat(anchorShared ? [] : [anchor]).ToList();
        if (own.Select(tile => CannotTakeOff(tile.Item)).FirstOrDefault(why => why is not null) is { } refused)
        {
            Status = refused;
            return;
        }

        foreach (var tile in own)
            _spillBack += SpillBeltOf(tile.Item);

        Held = anchor;
        HeldIsShared = anchorShared;
        HeldGroup = rest;
        Rebuild();
    }

    /// <summary>
    /// Whether what is held came out of the shared stash rather than this character.
    /// <para>
    /// The cursor is one hand for both files - there is only ever one item on it, and a
    /// second hand would be a second copy of everything that draws and cancels it. Where it
    /// goes back down is the part that differs, so the hand remembers where it came from and
    /// the page routes the drop.
    /// </para>
    /// </summary>
    public bool HeldIsShared { get; private set; }

    /// <summary>
    /// Lifts an item out of its container onto the cursor, for a drag - the only way anything is
    /// held since a click stopped lifting. Says
    /// nothing while it is carried: the drop says what happened.
    /// </summary>
    public void PickUp(ItemTileViewModel tile, bool fromShared = false)
    {
        // Worn - the character's or the mercenary's - comes off as it does in game, into a
        // container. Nothing is put on.
        if (Held is not null)
            return;

        // The Iron Golem's item is handed back by dismissing the golem, not carried off - nor is one on the corpse.
        if (KeptApart(tile.Item) is { } apart)
        {
            Status = apart;
            return;
        }

        if (tile.Item.Mode is not (ItemMode.Stored or ItemMode.Equipped or ItemMode.Belt))
            return;

        if (CannotTakeOff(tile.Item) is { } refused)
        {
            Status = refused;
            return;
        }

        if (!fromShared)
            _spillBack = SpillBeltOf(tile.Item);

        Held = tile;
        HeldIsShared = fromShared;
        Rebuild();
    }

    /// <summary>
    /// Everything the character holds: worn, carried and stored, its mercenary's, and what sits in
    /// the sockets of all of those - a unique jewel in a helm is held as much as the helm is.
    /// </summary>
    public IEnumerable<Item> HeldItems =>
        SaveItems.Walk(_save, ItemScope.Own | ItemScope.Mercenary | ItemScope.Sockets);

    /// <summary>
    /// Whether an item put here would be a charm that does nothing: outside the charm section, or a second of a unique
    /// charm (CharmArea.IdleAt). Not a refusal - the game permits it, and so does this.
    /// </summary>
    public bool WouldBeIdleCharm(ItemTileViewModel tile, byte page, int y, int height) =>
        IdleCharmAt(tile, page, y, height) != CharmIdle.None;

    private CharmIdle IdleCharmAt(ItemTileViewModel tile, byte page, int y, int height) =>
        page == InventoryLayout.InventoryPage
            ? CharmArea.IdleAt(tile.Item,
                               (_save?.PlayerItemList?.Items ?? []).Where(item => item.Mode == ItemMode.Stored && item.Page == page),
                               SizeOfPage(page), y, height)
            : CharmIdle.None;

    /// <summary>
    /// Puts the held item down. Returns null when it went, or why it would not.
    /// </summary>
    public string? PlaceHeld(byte page, int x, int y)
    {
        if (Held is not { } tile)
            return L("Nothing held.");

        var size = SizeOfPage(page);
        var contents = (_save?.PlayerItemList?.Items ?? [])
            .Where(item => item.Mode == ItemMode.Stored && item.Page == page);

        var check = ItemMover.CanPlace(tile.Item, contents, size, x, y);
        if (!check.Allowed)
        {
            Status = check.Reason ?? L("Does not fit there.");
            return Status;
        }

        ItemMover.Place(tile.Item, page, x, y);

        // Off the mercenary: into the character's own list, where everything carried is.
        if (_save?.MercenaryItemList?.ItemList is { } hired && hired.Items.Remove(tile.Item)
            && _save.PlayerItemList is { } own)
        {
            hired.Count = (ushort)hired.Items.Count;
            own.Items.Add(tile.Item);
            own.Count = (ushort)own.Items.Count;
        }

        var idle = IdleCharmAt(tile, page, y, tile.View.GridHeight);

        Held = null;
        HeldIsShared = false;
        Status = idle != CharmIdle.None
            ? idle == CharmIdle.AnotherCounts
                ? L($"Moved {tile.Name}. One already active: no effect.")
                : L($"Moved {tile.Name}. Above charm line: no effect.")
            : L($"Moved {tile.Name} to {NameOfPage(page).ToLowerInvariant()}.");

        MarkEdited();
        Rebuild();
        return null;
    }

    /// <summary>
    /// Empties the hand, for a caller that has already put the item somewhere itself.
    /// <para>
    /// A drop into the shared stash is that caller: the item leaves this file entirely, so
    /// there is nothing here to place and nothing here to mark edited - the stash marks
    /// itself. Only the cursor is this session's to clear.
    /// </para>
    /// </summary>
    public void PutDown()
    {
        Held = null;
        HeldIsShared = false;
    }

    /// <summary>Puts the held item back. It never left, so nothing has to be undone but what a belt spilled.</summary>
    public void CancelHold()
    {
        if (Held is null)
            return;

        PutSpillBack();

        Held = null;
        HeldIsShared = false;
        Status = L("Placed back.");
        Rebuild();
    }

    private GridSize SizeOfPage(byte page) =>
        Pages.FirstOrDefault(entry => entry.Page == page) is { Size: var size and not null }
            ? size
            : _layout.Inventory;

    private string NameOfPage(byte page) =>
        Pages.FirstOrDefault(entry => entry.Page == page) is { Name: { } name } ? name : "container";

}
