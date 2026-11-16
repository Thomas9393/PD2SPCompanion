using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;

using System;

namespace PD2SPCompanion.Shell.ViewModels;

// A stack let go of on another of its kind joins it, as the game stacks one onto another: keys onto keys, runes onto
// runes. Up to the base's largest stack; what does not fit goes back where it came from, so a drop is never left
// half done in the hand.
//
//   carried                              let go on                           what happens
//   a stack out of a grid                a stack of its kind in a grid       as many as fit join it; the rest back
//   a count dragged out of the           the same                            one, or with Shift as many as fit,
//     materials tab                                                            out of the tab
//   either, onto a full stack, another   -                                   not this gesture's: the drop is what
//     kind, a magic map, arrows                                                it was before

public partial class VP_ViewModel_Inv_L
{
    /// <summary>
    /// The one item carried out of a grid, let go of on <paramref name="onto"/>: into it where it is a stack of the same
    /// kind with room (<see cref="Stacks.Joining"/>). False where it is not this gesture's - the drop is then whatever it
    /// was before. True where it was: joined, or refused in words on the strip and everything back where it came from.
    /// </summary>
    private bool TryStack(ItemTileViewModel? onto)
    {
        if (onto is null || _tabCarry is not null || Session.HeldGroup.Count > 0 || Session.Held is not { } held
            || ReferenceEquals(onto, held) || !InAGrid(onto))
            return false;

        var lying = held.Item;
        int joining = Stacks.Joining(onto.Item, lying);
        if (joining == 0)
            return false;

        bool heldShared = Session.HeldIsShared;
        bool ontoShared = IsInSharedStash(onto);

        void Back(string why)
        {
            CancelHold();
            IsDragCarrying = false;
            Session.Status = L($"{why} Put back.");
        }

        if (IsKept(onto))
        {
            Back(KeptSay(onto));
            return true;
        }

        // Into another file's stack: asked as that file asks of anything arriving.
        if (heldShared != ontoShared && (ontoShared ? Stash.Refuses(lying) : Session.Refuses(lying)) is { } refused)
        {
            Back(refused);
            return true;
        }

        int left = Stacks.Shown(lying.Quantity) - joining;
        if (left > 0)
        {
            lying.Quantity = (ushort)left;
            Session.CancelHold();
            MarkEdited(heldShared);
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

        Joined(onto, ontoShared, joining, left);
        return true;
    }

    /// <summary>
    /// A count dragged out of the materials tab, let go of on <paramref name="onto"/>: one into it, or with
    /// <paramref name="full"/> (Shift) as many as fit, where it is a stack of the tab's kind with room. False where it is
    /// not this gesture's.
    /// </summary>
    private bool TryStackFromTab(ItemTileViewModel? onto, bool full)
    {
        if (onto is null || _tabCarry is not { } slot || !InAGrid(onto))
            return false;

        string code = Stash.MaterialCode(slot);
        int room = string.Equals(onto.Item.Code.Trim(), code, StringComparison.OrdinalIgnoreCase) ? Stacks.RoomIn(onto.Item) : 0;
        if (room == 0)
            return false;

        EndTabCarry();
        if (IsKept(onto))
        {
            Session.Status = KeptSay(onto);
            return true;
        }

        int count = Math.Min(full ? room : 1, Stash.MaterialCount(slot));
        if (count == 0)
        {
            Session.Status = L($"No {PD2SPCompanion.Core.Items.Materials.NameOf(slot)} in the tab.");
            return true;
        }

        if (Stash.TakeMaterial(slot, count) is { } refused)
        {
            Session.Status = refused;
            return true;
        }

        Joined(onto, IsInSharedStash(onto), count, left: 0);
        return true;
    }

    /// <summary>
    /// Whether what is carried would join <paramref name="onto"/> if let go of on it - the one item out of a grid, or a
    /// count out of the materials tab: the stack it would go onto lights, as the item a filler would go into does.
    /// </summary>
    public bool WouldStack(ItemTileViewModel? onto)
    {
        if (onto is null || !InAGrid(onto) || IsKept(onto))
            return false;

        if (_tabCarry is { } slot)
            return string.Equals(onto.Item.Code.Trim(), Stash.MaterialCode(slot), StringComparison.OrdinalIgnoreCase)
                   && Stacks.RoomIn(onto.Item) > 0 && Stash.MaterialCount(slot) > 0;

        return Session.HeldGroup.Count == 0 && Session.Held is { } held && !ReferenceEquals(onto, held)
               && Stacks.Joining(onto.Item, held.Item) > 0;
    }

    /// <summary>A stack lying in a grid of a container: not worn, not in the belt, not in a socket, not on a corpse.</summary>
    private bool InAGrid(ItemTileViewModel tile) =>
        tile.Item.Mode == D2SLib.Model.Save.ItemMode.Stored && !Session.IsCorpseItem(tile.Item);

    /// <summary><paramref name="count"/> more on the stack, its file told, the strip saying what it holds now.</summary>
    private void Joined(ItemTileViewModel onto, bool shared, int count, int left)
    {
        onto.Item.Quantity = (ushort)(Stacks.Shown(onto.Item.Quantity) + count);
        IsDragCarrying = false;
        _sfxManager?.Method_ButtonSFX_Press();
        MarkEdited(shared);

        Session.RefreshContainers();
        Stash.RefreshPages();
        HoverOver(null);

        int now = Stacks.Shown(onto.Item.Quantity);
        Session.Status = left > 0 ? L($"{onto.Name}: {now} in the stack, {left} put back.") : L($"{onto.Name}: {now} in the stack.");
    }

    private void MarkEdited(bool shared)
    {
        if (shared)
            Stash.MarkEdited();
        else
            Session.MarkEdited();
    }
}
