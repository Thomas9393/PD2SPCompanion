using System;
using Avalonia.Threading;
using D2SLib.Model.Save;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

// Into the belt.
public partial class VP_ViewModel_Inv_L
{
    // ---- into the belt (see Belts for the rule) --------------------------

    /// <summary>One carried item's box in the belt, and the file it comes from.</summary>
    private sealed record BeltPlacement(ItemTileViewModel Tile, bool FromShared, int Box);

    /// <summary>
    /// The boxes what is carried would take in the belt, all of them or none, and why not.
    /// <paramref name="column"/> is the column it was dropped on - each item takes that column's
    /// lowest free box, and a full one fills the room - or null for the game's own choice: a
    /// column of its kind, else an empty one. Asked here, before anything moves: what goes in a
    /// belt at all, what the character refuses of an arrival, and the room.
    /// </summary>
    private (List<BeltPlacement> Plan, string? Why) PlanBelt(int? column)
    {
        var plan = new List<BeltPlacement>();
        if (Session.Held is not { } anchor)
            return (plan, L("Nothing held."));

        var all = new List<(ItemTileViewModel Tile, bool Shared)> { (anchor, Session.HeldIsShared) };
        all.AddRange(Session.HeldGroup.Select(each => (each.Tile, each.Shared)));

        foreach (var (tile, shared) in all)
        {
            if (!PD2SPCompanion.Core.Characters.Belts.Takes(tile.Item))
                return (plan, L($"Belt takes potions and scrolls, not {tile.Name}."));

            // Asked before anything leaves, what the file will ask of an arrival: the hover is red for it too.
            if (shared && Session.Refuses(tile.Item) is { } no)
                return (plan, no);
        }

        var taken = Session.BeltTaken(all.Select(each => each.Tile.Item));
        int rows = Session.BeltRows;

        foreach (var (tile, shared) in all)
        {
            string? kind = PD2SPCompanion.Core.Characters.Belts.KindOf(tile.Item);
            if (PD2SPCompanion.Core.Characters.Belts.BoxFor(kind, taken, rows, column) is not { } box)
                return (plan, all.Count == 1 ? L("No free space in the belt.") : L("No free space in the belt for all of them."));

            taken[box] = kind;
            plan.Add(new BeltPlacement(tile, shared, box));
        }

        return (plan, null);
    }

    /// <summary>
    /// What is carried, into the belt - dropped on one of its columns, or (null) on the belt
    /// worn, for the game's own choice of column. All of it or none: refused, everything goes
    /// back where it was.
    /// </summary>
    public void DropIntoBelt(int? column)
    {
        IsDragCarrying = false;

        if (!Session.IsHolding)
            return;

        var (plan, why) = PlanBelt(column);
        if (why is not null)
        {
            CancelHold();
            Session.Status = L($"{why} Put back.");
            return;
        }

        // The shared stash lets go of its own first; should the character still refuse the
        // lot, they go back to their pages.
        var shared = plan.Where(each => each.FromShared).ToList();
        var lifts = ItemLift.OfAll(shared.Select(each => ((IItemHolder)Stash, each.Tile.Item)));
        if (lifts.Count != shared.Count)
        {
            ItemLift.Back(lifts);

            CancelHold();
            Stash.RefreshPages();
            Session.Status = L("Item not moved. Put back.");
            return;
        }

        if (Session.IntoBelt([.. plan.Select(each => (each.Tile.Item, each.Box, each.FromShared))]) is { } refused)
        {
            ItemLift.Back(lifts);

            Session.CancelHold();
            Session.Status = L($"{refused} Put back.");
        }
        else
        {
            ItemLift.Done(lifts);
            _sfxManager?.Method_ButtonSFX_Press();
            Session.Status = plan.Count == 1
                ? L($"Moved {plan[0].Tile.Name} to the belt.")
                : L($"Moved {plan.Count} items to the belt.");
        }

        Stash.RefreshPages();
        HoverOver(null);
    }

    /// <summary>
    /// A paste over the belt - a potion, or a scroll of town portal or identify -
    /// by the menu of its empty box or Ctrl + V with the pointer on it: into that column, as a drop there.
    /// A cut is lifted and dropped (<see cref="DropIntoBelt"/>); a copy - of the character's, the shared stash's
    /// or the vault's - is a second of each put in, all of them or none. What a belt does not take is said and
    /// the clipboard kept; a cut of the vault's is withdrawn as anywhere.
    /// </summary>
    private void PasteIntoBelt(int column)
    {
        if (Session.IsHolding)
            return;

        DisarmDelete();
        if (Clip.IsEmpty || Clip.Folders.Count > 0 || (Clip.Cards.Count > 0 && Clip.Kind == ClipKind.Cut))
        {
            PasteNow(null, null);
            return;
        }

        if (Clip.SocketedGems.FirstOrDefault() is { } gem)
        {
            Session.Status = L($"Belt takes potions and scrolls, not {ItemPresenter.Describe(gem).Name}.");
            return;
        }

        _sfxManager?.Method_ButtonSFX_Press();
        if (Clip.Kind == ClipKind.Cut)
        {
            var tiles = AllTiles().Where(tile => Clip.Items.Contains(tile.Item)).ToList();
            if (tiles.Count == 0)
            {
                Clip.Clear();
                Session.Status = L("Nothing to paste.");
            }
            else if (tiles.FirstOrDefault(tile => !PD2SPCompanion.Core.Characters.Belts.Takes(tile.Item)) is { } other)
                Session.Status = L($"Belt takes potions and scrolls, not {other.Name}.");
            else if (tiles.FirstOrDefault(IsKept) is { } kept)
                Session.Status = KeptSay(kept);
            else if (LiftCut(tiles))
                DropIntoBelt(column);
            return;
        }

        if (!Rules.Duplicates)
        {
            Session.Status = L("This play mode makes no copies.");
            return;
        }

        var copies = new List<Item>();
        if (Clip.Cards.Count > 0)
        {
            if (_vaults.Store is not { } store)
                return;
            try
            {
                copies.AddRange(store.RowsOf(Clip.Cards).Select(row => ItemBlob.Restore(row.Blob, store.StatIdBits)));
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                Session.Status = problem.Message;
                return;
            }
        }
        else
        {
            foreach (var item in Clip.Items)
            {
                if ((Stash.Holds(item) ? Stash.CopyOf(item) : Session.CopyOf(item)) is not { } copy)
                {
                    Session.Status = L($"{ItemPresenter.Describe(item).Name} cannot be copied.");
                    return;
                }
                copies.Add(copy);
            }
        }

        if (NewIntoBelt(copies, column) is { } why)
        {
            Session.Status = why;
            return;
        }

        foreach (var copy in copies)
            Session.Made(copy);

        Session.Status = copies.Count == 1
            ? L($"Copied {ItemPresenter.Describe(copies[0]).Name} to the belt.")
            : L($"Copied {copies.Count} items to the belt.");
        HoverOver(null);
    }

    /// <summary>
    /// Items new to the character into the belt at a column - a paste's copies, a count out of the materials tab - each
    /// the box a drop there plans (PlanBelt), all of them or none, each a new id. Null when in, else why not.
    /// </summary>
    private string? NewIntoBelt(IReadOnlyList<Item> made, int column)
    {
        if (made.FirstOrDefault(each => !PD2SPCompanion.Core.Characters.Belts.Takes(each)) is { } refused)
            return L($"Belt takes potions and scrolls, not {ItemPresenter.Describe(refused).Name}.");

        var taken = Session.BeltTaken([]);
        var placing = new List<(Item Item, int Box, bool Arriving)>();
        foreach (var each in made)
        {
            string? kind = PD2SPCompanion.Core.Characters.Belts.KindOf(each);
            if (PD2SPCompanion.Core.Characters.Belts.BoxFor(kind, taken, Session.BeltRows, column) is not { } box)
                return made.Count == 1 ? L("No free space in the belt.") : L("No free space in the belt for all of them.");

            taken[box] = kind;
            each.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
            placing.Add((each, box, true));
        }

        return Session.IntoBelt(placing);
    }

    /// <summary>Over a belt column while carrying: light the boxes the lot would take, green, or the cell red.</summary>
    private void HoverBelt(GridCell cell)
    {
        if (Session.BeltGrid is not { } belt)
            return;

        var (plan, why) = PlanBelt(cell.Column);
        if (why is not null)
        {
            cell.IsTarget = true;
            cell.IsAllowed = false;
            cell.IsIdle = false;
            return;
        }

        foreach (var each in plan)
        {
            int column = each.Box % PD2SPCompanion.Core.Characters.Belts.Columns;
            int row = belt.Rows - 1 - each.Box / PD2SPCompanion.Core.Characters.Belts.Columns;

            foreach (var lit in belt.Cells.Where(lit => lit.Column == column && lit.Row == row))
            {
                lit.IsTarget = true;
                lit.IsAllowed = true;
                lit.IsIdle = false;
            }
        }
    }

    /// <summary>
    /// Whether the belt's contents show under the equipment: off to begin
    /// with. Its potions pick, delete, store and lift out as any container's items do.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EquipmentHasPicks))]
    [NotifyPropertyChangedFor(nameof(PageHasPicks))]
    private bool _showBelt;

    /// <summary>
    /// Hidden, the belt lets go of what was selected in it - picked or selected alone - so
    /// nothing unseen is deleted or stored with the rest.
    /// </summary>
    partial void OnShowBeltChanged(bool value)
    {
        if (value || Session.BeltGrid is not { } belt)
            return;

        if (Session.SelectedItem is { } chosen && belt.Items.Any(tile => ReferenceEquals(tile.Item, chosen.Item)))
            Session.SelectedItem = null;

        if (!IsPicking)
            return;

        foreach (var tile in belt.Items.Where(tile => tile.IsPicked))
            Pick(tile, false);

        SayPicksChanged();
        Normalize();
    }

    [RelayCommand]
    private void ToggleBelt()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        ShowBelt = !ShowBelt;
    }

    [RelayCommand]
    private void ToggleEquipment()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        EquipmentIsOpen = !EquipmentIsOpen;
    }

    [RelayCommand]
    private void ToggleMerc()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        MercIsOpen = !MercIsOpen;
    }

    [RelayCommand]
    private void ToggleSection(ContainerSectionViewModel section)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        section.IsOpen = !section.IsOpen;
    }

    /// <summary>
    /// The panel as it stands, with the weapon boxes filled from whichever set is on
    /// show. Both sets sit in the same two positions, so only one can be listed.
    /// </summary>
    private void ShowEquipped()
    {
        // Refilled only when the slots shown change - a weapon set swapped, a character opened. The
        // session keeps its slot objects, so an ordinary move leaves the panel as it is.
        // The pair in hand, always: the swap slots' pair is seen by swapping to it, as in the game.
        FollowTabInHand();
        var shown = Session.Equipped.Where(slot => !slot.IsSwap).ToList();
        if (!Equipped.SequenceEqual(shown))
        {
            Equipped.Clear();
            foreach (var slot in shown)
                Equipped.Add(slot);
        }

        OnPropertyChanged(nameof(WornCount));
        OnPropertyChanged(nameof(EquipmentHeader));
    }

    /// <summary>
    /// True while the sections are being rebuilt.
    /// <para>
    /// A rebuild used to be able to start another one half way through itself: it cleared
    /// the sections, then read the shared stash, and reading raised the event that rebuilds.
    /// The inner pass filled the list and the outer one then filled it again, so the page
    /// opened with <b>two</b> stash sections.
    /// </para>
    /// </summary>
    private bool _rebuilding;

    private void Refresh()
    {
        if (_rebuilding)
            return;

        // The tab takes its item era from the character, which may have just changed.
        RefreshMaterials();

        _rebuilding = true;
        try
        {
            Rebuild();
        }
        finally
        {
            _rebuilding = false;
        }

        // A rebuild forgets picks whose items are gone - deleted, or the file read again - and
        // what that leaves, if one, is the selection.
        Normalize();

        JudgeAll();
        ApplySearch();
        RefreshSheet();
    }
}
