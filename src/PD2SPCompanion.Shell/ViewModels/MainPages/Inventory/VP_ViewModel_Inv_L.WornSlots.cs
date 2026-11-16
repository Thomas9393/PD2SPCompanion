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

// Onto a worn slot.
public partial class VP_ViewModel_Inv_L
{
    // ---- onto a worn slot ------

    /// <summary>
    /// Whether what is carried would go on at <paramref name="slot"/>: one item, whose slot, class
    /// and hands allow it there, and from another file one the character does not refuse (what the drop asks) - or a
    /// potion over the belt worn, which fills the belt.
    /// </summary>
    private bool GoesOn(EquipSlotViewModel slot, bool mercenary)
    {
        bool Takes(Item item, bool arriving) =>
            (mercenary ? Session.CannotWearMercenary(item, slot.Slot.Location) : Session.CannotWear(item, slot.Slot.Location)) is null
            && (!arriving || Session.Refuses(item) is null);

        // A card carried from the vault: the one item it holds, should the slot take it.
        if (IsVaultCarrying)
            return VaultCarriedItem?.Invoke() is { } coming && Takes(coming, arriving: true);

        if (Session.Held is not { } held)
            return false;

        if (!mercenary && slot.Slot.Location == D2SLib.Model.Save.ItemLocation.Waist && PD2SPCompanion.Core.Characters.Belts.Takes(held.Item))
            return PlanBelt(null).Why is null;

        return Session.HeldGroup.Count == 0 && Takes(held.Item, Session.HeldIsShared);
    }

    private EquipSlotViewModel? _hoveredSlot;

    /// <summary>The worn slot under the pointer while carrying, lit green or red - null lets go of it.</summary>
    public void HoverSlot(EquipSlotViewModel? slot, bool mercenary = false)
    {
        if (ReferenceEquals(_hoveredSlot, slot))
            return;

        if (_hoveredSlot is not null)
            _hoveredSlot.IsTarget = false;

        _hoveredSlot = slot;
        if (slot is null || !(IsTakingDrops || IsTabCarrying || RowFrom is not null))
            return;

        HoverOver(null);
        slot.IsAllowed = GoesOn(slot, mercenary) || WouldSocket(slot.Item);
        slot.IsTarget = true;
    }

    /// <summary>
    /// What is carried, dropped on a worn slot: put on - what was there taken off to where the item
    /// came from, or the first room - or, a potion on the belt worn, into the belt by the game's
    /// rule. One item at a time; refused, everything goes back where it was.
    /// <para>
    /// <b>What it pushes off is never what refuses it</b> while anything has room: the strip says where it goes instead. The
    /// character's pages full, it lands on the shared page a pane shows (CharacterSession.Beyond), and the
    /// strip names the place - "Equipped Short Sword. Stone Barb to shared page 5."
    /// </para>
    /// </summary>
    public void DropOnSlot(EquipSlotViewModel slot, bool mercenary = false)
    {
        IsDragCarrying = false;
        HoverSlot(null);

        if (Session.Held is not { } held)
            return;

        var at = slot.Slot.Location;
        if (!mercenary && at == D2SLib.Model.Save.ItemLocation.Waist && PD2SPCompanion.Core.Characters.Belts.Takes(held.Item))
        {
            DropIntoBelt(column: null);
            return;
        }

        // Several fillers on a worn item with an empty socket: into it, one after another (Socketing.SocketGroup).
        if (Session.HeldGroup.Count > 0 && TrySocket(slot.Item))
            return;

        if (Session.HeldGroup.Count > 0)
        {
            CancelHold();
            Session.Status = L("Not several at once. Put back.");
            return;
        }

        // On a worn item with an empty socket, a gem, a rune or a jewel: into it (Socketing).
        if (TrySocket(slot.Item))
            return;

        if ((mercenary ? Session.CannotWearMercenary(held.Item, at) : Session.CannotWear(held.Item, at)) is { } refused)
        {
            CancelHold();
            Session.Status = L($"{refused} Put back.");
            return;
        }

        var item = held.Item;
        bool shared = Session.HeldIsShared;
        (byte Page, int X, int Y) origin = (item.Page, item.X, item.Y);
        // What comes off for it, named before anything moves: the piece worn there, and the other hand's for a
        // weapon held in both.
        var displaced = Session.PushedOffBy(item, at, mercenary)
            .Select(pushed => (Item: pushed, Name: ItemPresenter.Describe(pushed).Name)).ToList();

        ItemLift? lift = null;
        if (shared)
        {
            // Its era is Wear's to check, as for every item arriving: refused, it goes back below.
            lift = ItemLift.Of(Stash, item);
            if (lift is null)
            {
                CancelHold();
                Session.Status = L("Item not moved. Put back.");
                return;
            }
        }

        string? said = Session.Wear(item, at, arriving: shared, force: Rules.ForcesEquip, out bool worn, mercenary);
        if (!worn)
        {
            lift?.Back();

            CancelHold();
            Session.Status = L($"{said} Put back.");
        }
        else
        {
            lift?.Done();

            // The rebuild finds the selection again by its item: what was put on stays selected.
            _sfxManager?.Method_ButtonSFX_Press();
            Session.Status = PushedOffSays(said ?? L($"Equipped {held.Name}."), displaced,
                pushed => shared || pushed.Page != origin.Page || pushed.X != origin.X || pushed.Y != origin.Y);
        }

        Stash.RefreshPages();
        HoverOver(null);
    }

    /// <summary>
    /// What an item put on pushed off: those <paramref name="rehome"/> names on to the shared page a pane shows, as
    /// everything that arrives lands (ItemLanding; the session saw to its own pages), and where each is now, said after
    /// <paramref name="done"/>.
    /// </summary>
    private string PushedOffSays(string done, IEnumerable<(Item Item, string Name)> displaced, Func<Item, bool> rehome)
    {
        foreach (var (pushed, pushedName) in displaced)
        {
            if (rehome(pushed))
                _landing.Rehome(pushed);

            if (_landing.WhereIs(pushed) is { } spot)
                done += " " + L($"{pushedName} to {ItemLanding.Named(spot)}.");
        }

        return done;
    }

    /// <summary>The vault's cards by their keys, withdrawn and put on a worn slot (joined by the main view model): a cut of the vault's pasted on a slot.</summary>
    public Action<IReadOnlyCollection<long>, ItemLocation, bool>? WearPasted { get; set; }

    /// <summary>
    /// A paste over a worn slot, the character's or the mercenary's, by
    /// the slot's menu or Ctrl + V with the pointer on it: as a drop there. A filler on a worn item with an open socket
    /// goes in. A cut is lifted and dropped (<see cref="DropOnSlot"/>); a copy, of the character's, the shared stash's
    /// or the vault's, is a second of it put on; what comes off lands as under a drop. One item at a time. What the slot
    /// does not take is said and the clipboard kept.
    /// </summary>
    public void PasteOnSlot(EquipSlotViewModel slot, bool mercenary)
    {
        if (Session.IsHolding)
            return;

        DisarmDelete();
        if (PasteIntoSockets(slot.Item))
            return;

        if (Clip.IsEmpty || Clip.Folders.Count > 0)
        {
            PasteNow(null, null);
            return;
        }

        _sfxManager?.Method_ButtonSFX_Press();
        if (Clip.SocketedGems.FirstOrDefault() is { } gem)
        {
            Session.Status = L($"{ItemPresenter.Describe(gem).Name} cannot be equipped.");
            return;
        }

        if (Clip.Items.Count + Clip.Cards.Count > 1)
        {
            Session.Status = L("Not several at once.");
            return;
        }

        var at = slot.Slot.Location;
        string? Refused(Item item) => mercenary ? Session.CannotWearMercenary(item, at) : Session.CannotWear(item, at);

        if (Clip.Kind == ClipKind.Cut)
        {
            if (Clip.Cards.Count > 0)
            {
                var keys = Clip.Cards.ToList();
                Clip.Clear();
                WearPasted?.Invoke(keys, at, mercenary);
                return;
            }

            if (AllTiles().FirstOrDefault(tile => Clip.Items.Contains(tile.Item)) is not { } lying)
            {
                Clip.Clear();
                Session.Status = L("Nothing to paste.");
            }
            else if (ReferenceEquals(slot.Item?.Item, lying.Item))
                Session.Status = L($"{lying.Name} is equipped there already.");
            else if (IsKept(lying))
                Session.Status = KeptSay(lying);
            else if (!(at == ItemLocation.Waist && !mercenary && PD2SPCompanion.Core.Characters.Belts.Takes(lying.Item)) && Refused(lying.Item) is { } no)
                Session.Status = no;
            else if (LiftCut([lying]))
                DropOnSlot(slot, mercenary);
            return;
        }

        if (!Rules.Duplicates)
        {
            Session.Status = L("This play mode makes no copies.");
            return;
        }

        Item copy;
        if (Clip.Cards.Count > 0)
        {
            if (_vaults.Store is not { } store)
                return;
            try
            {
                copy = ItemBlob.Restore(store.RowsOf(Clip.Cards).First().Blob, store.StatIdBits);
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                Session.Status = problem.Message;
                return;
            }

            copy.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        }
        else
        {
            var original = Clip.Items.First();
            if ((Stash.Holds(original) ? Stash.CopyOf(original) : Session.CopyOf(original)) is not { } made)
            {
                Session.Status = L($"{ItemPresenter.Describe(original).Name} cannot be copied.");
                return;
            }
            copy = made;
        }

        if (Refused(copy) is { } refused)
        {
            Session.Status = refused;
            return;
        }

        var displaced = Session.PushedOffBy(copy, at, mercenary).Select(pushed => (Item: pushed, Name: ItemPresenter.Describe(pushed).Name)).ToList();
        string? said = Session.Wear(copy, at, arriving: true, force: Rules.ForcesEquip, out bool worn, mercenary);
        if (!worn)
        {
            Session.Status = said ?? L("Not equipped.");
            return;
        }

        Session.Made(copy);
        Session.Status = PushedOffSays(said ?? L($"Copied {ItemPresenter.Describe(copy).Name}."), displaced, _ => true);
        Stash.RefreshPages();
        HoverOver(null);
    }

    /// <summary>A worn slot's menu, bare: Paste, onto it. A worn item's own menu has its Paste row do the same (MenuFor).</summary>
    public ActMenu MenuFor(EquipSlotViewModel slot, bool mercenary) =>
        new([new(L("Paste"), MenuGlyph.Paste, () => PasteOnSlot(slot, mercenary)) { Keys = L("Ctrl + V"), IsEnabled = CanPaste, Why = CanPaste ? Clip.Says : "" }], []);

    // ---- the right click ---------------------------------------------------
    //
    //   on a worn item, the character's or the mercenary's      it comes off
    //   on an item in a grid                                    put on the character
    //   Ctrl                                                    put on the mercenary
    //   Alt                                                     put on the other slot that takes it - the second ring, the other hand
    //   Shift                                                   to the vault, only while the vault is on the right page
    //   Ctrl + Shift                                            to the pane across, only while a second pane is shown (SendAcross)
    //
    // - the game's own chords, which it has for want of a menu. A right click opens the item's
    // menu, each of these a row of it (VP_ViewModel_Inv_L.Menus.cs), and the chords are gone. What is here is
    // what the rows run: which slot takes an item, and taking one off. Every one says what it did, or why not,
    // on the strip.

    /// <summary>Whether the vault is the right page (the main view model's to say): Shift + right click stores there, and only then.</summary>
    public Func<bool>? VaultIsBeside { get; set; }

    /// <summary>
    /// The worn slot a right click puts <paramref name="item"/> on - the character's, or the mercenary's -
    /// where a drag would name one (what is there comes off, as under a drop). Of the two weapon sets, the
    /// one in hand. None, and why, where no slot takes it. <paramref name="alternative"/>: the other slot that
    /// takes it - the second ring, the other hand, a plain right click going to the default slot - and the only one, where there is no other.
    /// <para>
    /// <b>Which of several</b>: an item of a clear type, like a shield, is compared with the equipped shield first and
    /// not with the sword, falling back only when, for example, dual wielding swords. The hands take a weapon or a shield either side,
    /// so the first slot that takes it is not the one meant. In order:
    /// <list type="number">
    /// <item>for a shield or a quiver, the slot that holds one - it takes that one's place;</item>
    /// <item>an empty slot - the off hand first, for a shield or a quiver;</item>
    /// <item>a slot holding the same kind - a weapon for a weapon, a ring for a ring;</item>
    /// <item>any other that takes it - the off hand first again: a shield over two swords takes the second.</item>
    /// </list>
    /// The alternative is the next in that order.
    /// </para>
    /// </summary>
    public (EquipSlotViewModel? Slot, string? Why) SlotFor(Item item, bool mercenary, bool alternative = false) =>
        SlotAt(item, mercenary, alternative ? 1 : 0);

    /// <summary>
    /// The slot at <paramref name="index"/> of those that take the item, in <see cref="SlotsFor"/>'s order, the
    /// count coming round to the first: 0 the default, 1 the alternative, and on - what Alt counts through with
    /// the comparison open (TipActions).
    /// </summary>
    public (EquipSlotViewModel? Slot, string? Why) SlotAt(Item item, bool mercenary, int index)
    {
        var (slots, why) = SlotsFor(item, mercenary);
        return slots.Count == 0 ? (null, why) : (slots[((index % slots.Count) + slots.Count) % slots.Count], null);
    }

    /// <summary>Every worn slot that takes the item - the character's, or the mercenary's - in the order <see cref="SlotFor"/> describes; none, and why.</summary>
    public (IReadOnlyList<EquipSlotViewModel> Slots, string? Why) SlotsFor(Item item, bool mercenary)
    {
        var slots = mercenary
            ? Session.MercEquipped
            : [.. Session.Equipped.Where(slot => !slot.IsSwap)];

        var taking = new List<EquipSlotViewModel>();
        string? why = null;
        foreach (var slot in slots)
        {
            string? refused = mercenary ? Session.CannotWearMercenary(item, slot.Slot.Location) : Session.CannotWear(item, slot.Slot.Location);
            if (refused is null)
                taking.Add(slot);

            // The reason that says most: any but "not for that slot", which every other slot gives.
            else if (why is null || why == L("Not for that slot."))
                why = refused;
        }

        if (taking.Count > 0)
        {
            // What the item is, for the hands: what goes in the off hand, a weapon, or neither.
            static string KindOf(Item of)
            {
                var line = PD2SPCompanion.Core.Items.ItemTypes.LineageOfBase(of.Code);
                bool Is(string type) => System.Linq.Enumerable.Contains(line, type, StringComparer.OrdinalIgnoreCase);
                return Is("shld") || Is("bowq") || Is("xboq") ? "off" : Is("weap") ? "weapon" : "";
            }

            string kind = KindOf(item);
            bool SameKind(EquipSlotViewModel slot) => slot.Item is { } worn && KindOf(worn.Item) == kind;
            IEnumerable<EquipSlotViewModel> OffHandFirst(IEnumerable<EquipSlotViewModel> slots) => kind == "off"
                ? slots.OrderBy(slot => slot.Slot.Location is D2SLib.Model.Save.ItemLocation.LeftHand or D2SLib.Model.Save.ItemLocation.SwapLeft ? 0 : 1)
                : slots;

            var order = new List<EquipSlotViewModel>();
            void Then(IEnumerable<EquipSlotViewModel> slots) => order.AddRange(slots.Where(slot => !order.Contains(slot)).ToList());

            if (kind == "off")
                Then(taking.Where(SameKind));
            Then(OffHandFirst(taking.Where(slot => !slot.IsFilled)));
            Then(taking.Where(SameKind));
            Then(OffHandFirst(taking));

            return (order, null);
        }

        string name = ItemPresenter.Describe(item).Name;
        return ([], why is null || why == L("Not for that slot.") ? L($"{name} cannot be equipped.") : why);
    }

    /// <summary>
    /// A right click on a worn item - the character's or the mercenary's - takes it off, as the game's own control does:
    /// onto the page a pane shows, then the first room, as everything
    /// that arrives lands (ItemLanding). Refused as a drag of it is: what the inventory lock keeps, a belt whose
    /// potions have nowhere to go, no room.
    /// </summary>
    public void TakeOff(EquipSlotViewModel slot)
    {
        if (slot.Item is not { } tile || Session.IsHolding)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        // One of several picked that are worn: all of them come off, or none (moves take the
        // whole selection, equipping one item).
        var group = TakenOffWith(tile) > 1 ? PickedTiles().Where(IsWorn).ToList() : [tile];
        if (group.FirstOrDefault(IsKept) is { } kept)
        {
            Session.Status = KeptSay(kept);
            return;
        }

        if (group.Count > 1)
        {
            if (group.Select(each => Session.CannotTakeOff(each.Item)).FirstOrDefault(no => no is not null) is { } stuck)
            {
                Session.Status = stuck;
                return;
            }

            if (_landing.WithoutRoom(group.Select(each => each.Item), null).Count > 0)
            {
                Session.Status = L($"No free space for all {group.Count}.");
                return;
            }

            int off = group.Count(each => _landing.Move(each.Item, out _) is null);
            Session.Status = Plural(off, $"{off} item taken off.", $"{off} items taken off.");
            return;
        }

        string name = tile.Name;
        if (_landing.Move(tile.Item, out var landed) is { } refused)
        {
            Session.Status = refused;
            return;
        }

        Session.Status = landed is { Shared: true } shared
            ? L($"{name} taken off to shared page {shared.Page}.")
            : L($"{name} taken off.");
    }

    private static bool IsWorn(ItemTileViewModel tile) => tile.Item.Mode == D2SLib.Model.Save.ItemMode.Equipped;

    /// <summary>How many a move begun on this item takes: every pick, where it is one of several; else itself.</summary>
    public int MovedWith(ItemTileViewModel tile) => IsPicking && tile.IsPicked ? Math.Max(1, PickedTiles().Count) : 1;

    /// <summary>How many a right click on this worn item takes off: every worn pick, where it is one of several.</summary>
    public int TakenOffWith(ItemTileViewModel tile) => IsPicking && tile.IsPicked ? Math.Max(1, PickedTiles().Count(IsWorn)) : 1;
}
