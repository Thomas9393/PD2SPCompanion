using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.Input;

using D2SLib.Model.Save;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The inventory page's half of the clipboard (<see cref="ItemClipboard"/>): Ctrl + X
/// marks what is selected, Ctrl + C holds it to be copied, Ctrl + V pastes what is held - here, on the page the
/// first pane shows. A cut of the character's own is carried there as a drag would carry it; a cut of the
/// vault's is withdrawn; a copy is a second of the item, where the play mode makes seconds. Three buttons on the
/// tools line, where Duplicate stood - whose key Ctrl + V was and whose work a copy pasted now is.
/// </summary>
public sealed partial class VP_ViewModel_Inv_L
{
    private static ItemClipboard Clip => ItemClipboard.Shared;

    /// <summary>What is selected, as items: the picks, or the one - never a count of the materials tab.</summary>
    private List<ItemTileViewModel> SelectedTiles() =>
        IsPicking ? [.. PickedTiles().Where(tile => tile.TabSlot is null)]
        : Session.SelectedItem is { TabSlot: null } one ? [one] : [];

    public bool CanCut => SelectedTiles().Count > 0;

    public bool CanPaste => !Clip.IsEmpty;

    public string CutHint => ShortcutText.Dimmed(ShortcutText.With(L("Cut"), L("Ctrl + X")), CanCut ? null : L("No item selected."));

    public string CopyHint => ShortcutText.Dimmed(ShortcutText.With(L("Copy"), L("Ctrl + C")), CanCut ? null : L("No item selected."));

    /// <summary>Paste, and under it what is held: "3 items cut".</summary>
    public string PasteHint => ShortcutText.With(L("Paste"), L("Ctrl + V")) + (Clip.IsEmpty ? "" : Environment.NewLine + Clip.Says);

    /// <summary>The clipboard changed, or the selection: the three buttons say what they would do now.</summary>
    private void SayClipboard()
    {
        OnPropertyChanged(nameof(CanCut));
        OnPropertyChanged(nameof(CutHint));
        OnPropertyChanged(nameof(CopyHint));
        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(PasteHint));
    }

    /// <summary>
    /// Ctrl + X: what is selected is marked to be moved by the paste - dimmed, and nothing moves yet. What the
    /// inventory lock keeps is not cut, and says so.
    /// </summary>
    [RelayCommand]
    private void Cut()
    {
        var tiles = SelectedTiles();
        if (tiles.Count == 0 || Session.IsHolding)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        if (tiles.FirstOrDefault(IsKept) is { } kept)
        {
            Session.Status = KeptSay(kept);
            return;
        }

        Clip.HoldItems(ClipKind.Cut, tiles.Select(tile => tile.Item));
        Session.Status = Plural(tiles.Count, "1 item cut. Paste to move it.", $"{tiles.Count} items cut. Paste to move them.");
    }

    /// <summary>The game's text of a copy, for the system's clipboard: the view's to write, which has the window's.</summary>
    public Action<string>? TextCopied { get; set; }

    /// <summary>
    /// Copy, by every way to it - the tools' button, Ctrl + C and the menu's row: the game's text of what is
    /// selected on the system's clipboard (<see cref="CopyText"/>, which says what it copied), and the selection
    /// held to be pasted as a second of itself (<see cref="Copy"/>). One command where there were two routes: the
    /// menu's row held the copy, and neither said so nor wrote the text.
    /// </summary>
    [RelayCommand]
    private void CopySelected()
    {
        if (CopyText() is { } text)
            TextCopied?.Invoke(text);
        Copy();
    }

    /// <summary>
    /// Ctrl + C: what is selected is held to be made a second of by the paste, where the play mode makes seconds.
    /// The view puts the game's text of it on the system's clipboard besides, as Ctrl + C did before
    /// (<see cref="CopyText"/>), which says what it copied.
    /// </summary>
    public void Copy()
    {
        var tiles = SelectedTiles();
        if (tiles.Count > 0 && Rules.Duplicates)
            Clip.HoldItems(ClipKind.Copy, tiles.Select(tile => tile.Item));
    }

    /// <summary>The vault's withdrawal of cards it holds by key (joined by the main view model): a cut of the vault's, pasted here.</summary>
    public Action<IReadOnlyCollection<long>>? WithdrawPasted { get; set; }

    /// <summary>
    /// Ctrl + V: what the clipboard holds, here. A cut of the character's goes to the page the first pane shows,
    /// in its first room, as a drag onto that page's row takes it; a cut of the vault's is withdrawn; a copy is
    /// a second of each, beside it or on the page shown - where the play mode makes seconds. A folder is the
    /// vault's to paste.
    /// </summary>
    [RelayCommand]
    private void Paste() => PasteOn(null);

    /// <summary>
    /// The paste lands where it was asked for (the first version's key took a pane from the pointer and no cell, and
    /// a paste went near the origin, not to the cursor, even with room there): on <paramref name="at"/> - the cell a menu was
    /// opened on, or the one under the pointer as Ctrl + V is pressed - on the page <paramref name="onto"/>.
    /// A cut keeps its shape about that cell as a drop does, and goes to the page's first room, said, where it
    /// does not fit there; a copy, and what comes out of the vault, lands on the cell or in the free room
    /// nearest it (ItemLanding.Wish). With no cell - the tools' button, the pointer over no grid - the first
    /// room of the first pane's page, as before.
    /// </summary>
    public void PasteOn(StashPageChoice? onto, GridCell? at = null)
    {
        // Never on the corpse: a paste with the pointer over its grid lands as one with the pointer nowhere does.
        if (at is { Shared: false, Page: CharacterSession.CorpsePage })
            at = null;

        // Over the belt: into it, at that column, as a drop there (PasteIntoBelt).
        if (at is { Shared: false, Page: CharacterSession.BeltPage })
        {
            PasteIntoBelt(at.Column);
            return;
        }

        if (at is null || GridFor(at.Shared, at.Page) is not { } grid)
        {
            PasteNow(onto, at);
            return;
        }

        _landing.Wish = (grid, at.Column, at.Row);
        try
        {
            PasteNow(onto, at);
        }
        finally
        {
            _landing.Wish = null;
        }
    }

    private void PasteNow(StashPageChoice? onto, GridCell? at)
    {
        if (Session.IsHolding)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        DisarmDelete();

        if (Clip.IsEmpty)
        {
            Session.Status = L("Nothing to paste.");
            return;
        }

        if (Clip.Folders.Count > 0)
        {
            Session.Status = L("A folder is pasted in the vault.");
            return;
        }

        // A gem cut in its socket: turned out, onto the cell the menu was opened on, else into the first room of
        // the page pasted on - by the Edit page that shows its item, which undoes a runeword it was a rune of.
        if (Clip.SocketedGems.Count > 0 && Clip.SocketHost is { } host)
        {
            var gems = Clip.SocketedGems.ToList();
            var into = at is not null && IsFreeRoom(at) ? at : FirstRoomOn(onto ?? PastePage);
            Clip.Clear();
            if (Clip.EjectSocketed?.Invoke(host, gems, into) is null)
                Session.Status = L("Nothing to paste.");
            return;
        }

        if (Clip.Cards.Count > 0)
        {
            if (Clip.Kind == ClipKind.Cut)
            {
                var keys = Clip.Cards.ToList();
                Clip.Clear();
                WithdrawPasted?.Invoke(keys);
            }
            else
            {
                PasteCopiesOfCards();
            }

            return;
        }

        var tiles = AllTiles().Where(tile => Clip.Items.Contains(tile.Item)).ToList();

        // Copied where it is no tile - a gem in a socket: a second of it, landed as anything that arrives lands.
        if (tiles.Count == 0 && Clip.Kind == ClipKind.Copy && Clip.Items.Count > 0)
        {
            PasteCopiesOfSocketed();
            return;
        }

        if (tiles.Count == 0)
        {
            Clip.Clear();
            Session.Status = L("Nothing to paste.");
            return;
        }

        if (Clip.Kind == ClipKind.Copy)
            PasteCopies(tiles);
        else
            PasteCut(tiles, onto, at);
    }

    /// <summary>The page of the section's that shows a grid - a pane's, the first or the second: what Ctrl + V pastes on, the pointer over it.</summary>
    public StashPageChoice? PageShowing(GridViewModel? grid)
    {
        if (grid is null || Sections.FirstOrDefault(section => section.Pages is not null) is not { } section)
            return null;

        return ReferenceEquals(section.SelectedPage?.Grid, grid) ? section.SelectedPage
            : section.ShowSecond && ReferenceEquals(section.SecondPage?.Grid, grid) ? section.SecondPage
            : null;
    }

    /// <summary>The page a paste lands on: the first pane's, where it shows a grid.</summary>
    private StashPageChoice? PastePage =>
        Sections.FirstOrDefault(section => section.Pages is not null)?.SelectedPage is { IsSeparator: false, IsMaterials: false, Grid: not null } page
            ? page : null;

    /// <summary>
    /// The cut items carried to the page the first pane shows and let go there, as a drag onto its row
    /// (<see cref="DropCarriedOnPage"/>): its first room, several packed - or put back, with no room. One step
    /// of the history.
    /// </summary>
    private void PasteCut(List<ItemTileViewModel> tiles, StashPageChoice? onto, GridCell? at = null)
    {
        if ((onto is { IsSeparator: false, IsMaterials: false, Grid: not null } ? onto : PastePage) is not { Grid: { } grid } page)
        {
            Session.Status = L("Show a page to paste on.");
            return;
        }

        if (tiles.FirstOrDefault(IsKept) is { } kept)
        {
            Session.Status = KeptSay(kept);
            return;
        }

        // Where it lies already - but for a cell named on that page, which is somewhere else on it.
        if (at is null && tiles.All(tile => grid.Items.Contains(tile)))
        {
            Session.Status = L("Already on this page.");
            return;
        }

        if (!LiftCut(tiles))
            return;

        // At the cell the menu was opened on, where it fits there: one item, or several in their shape about it.
        if (at is not null)
        {
            if (Session.HeldGroup.Count == 0)
            {
                PlaceAt(at);
                if (!Session.IsHolding)
                    return;
            }
            else if (PlanGroup(at, pack: false) is { Why: null } fits)
            {
                MoveGroup(at, fits.Plan);
                HoverOver(null);
                return;
            }

            // It does not: the page's first room, and the strip says it went there instead.
            DropCarriedOnPage(page);
            Session.Status = L($"No room there. {Session.Status}");
            return;
        }

        DropCarriedOnPage(page);
    }

    /// <summary>
    /// The cut items selected, then lifted as the drag lifts them - one, or one with the rest about it - and the
    /// clipboard let go. False where nothing was lifted (a belt that cannot come off says why), and the cut stands.
    /// </summary>
    private bool LiftCut(List<ItemTileViewModel> tiles)
    {
        IsPicking = false;
        var lead = tiles[0];
        Session.SelectedItem = lead;
        if (tiles.Count == 1)
        {
            Session.PickUp(lead, IsInSharedStash(lead));
        }
        else
        {
            Session.PickUpGroup(lead, IsInSharedStash(lead),
                [.. tiles.Skip(1).Select(each => new CarriedItem(each, IsInSharedStash(each), each.Item.X - lead.Item.X, each.Item.Y - lead.Item.Y))]);
        }

        if (!Session.IsHolding)
            return false;

        if (Session.HeldIsShared || Session.HeldGroup.Any(each => each.Shared))
            Stash.RefreshPages();

        Clip.Clear();
        return true;
    }

    /// <summary>A second of each copied item of the character's, as Duplicate made them: all of them, or none.</summary>
    private void PasteCopies(List<ItemTileViewModel> tiles)
    {
        if (!Rules.Duplicates)
        {
            Session.Status = L("This play mode makes no copies.");
            return;
        }

        // Duplicate's own work, on what is selected: the copied ones are selected for it.
        if (tiles.Count == 1)
        {
            IsPicking = false;
            Session.SelectedItem = tiles[0];
        }
        else
        {
            ClearSelection();
            BeginDragPick(keepSelected: false);
            foreach (var tile in tiles)
                SetPicked(tile, true);
            EndPick();
        }

        DuplicateItem();
    }

    /// <summary>The first bare cell of a page's grid, for one cell's worth: where a gem pasted on the page goes. Null with no page, or no room.</summary>
    private GridCell? FirstRoomOn(StashPageChoice? page)
    {
        if (page is not { IsSeparator: false, IsMaterials: false, Grid: { } grid })
            return null;

        var room = new ItemMover.Occupancy(new GridSize(grid.Columns, grid.Rows), grid.Items.Select(each => each.Item));
        return room.FirstFree(1, 1) is { } spot ? grid.Cells.FirstOrDefault(each => each.Column == spot.X && each.Row == spot.Y) : null;
    }

    /// <summary>A second of each copied gem that lies in a socket, on the character: the item it is in keeps its own.</summary>
    private void PasteCopiesOfSocketed()
    {
        if (!Rules.Duplicates)
        {
            Session.Status = L("This play mode makes no copies.");
            return;
        }

        int made = 0;
        string? refused = null, last = null;
        foreach (var gem in Clip.Items)
        {
            if (Session.CopyOf(gem) is not { } copy)
            {
                refused ??= L("Not all could be copied.");
                continue;
            }

            // Not selected as it lands: the item whose socket was copied stays the one on the Edit page.
            if (Land(copy, select: false) is { } no)
            {
                refused ??= no;
            }
            else
            {
                made++;
                last = ItemPresenter.Describe(copy).Name;
            }
        }

        Session.Status = CopiedSays(made, last) + (refused is null ? "" : " " + refused);
    }

    /// <summary>What a paste of copies made: one by its name, several by their count - the vault's words (SayCopied).</summary>
    private static string CopiedSays(int made, string? only) =>
        made == 1 && only is { Length: > 0 } ? L($"Copied {only}.") : Plural(made, "Copied 1 item.", $"Copied {made} items.");

    /// <summary>An item made here - a copy - landed as anything that arrives lands (ItemLanding). Null, or why not.</summary>
    private string? Land(Item copy, bool select = true)
    {
        if (_landing.Find(copy, null) is not { } spot)
            return L("No free space.");

        string? no = spot.Shared
            ? Stash.Receive(copy, spot.Page, spot.X, spot.Y, select: select)
            : Session.Receive(copy, L("No free space."), select: select, at: (spot.Page, (spot.X, spot.Y)));
        if (no is null)
            Session.Made(copy);

        return no;
    }

    /// <summary>
    /// A second of each copied card of the vault's, on the character: read back from its row under a new id
    /// and landed as anything that arrives lands (ItemLanding). The vault keeps its own.
    /// </summary>
    private void PasteCopiesOfCards()
    {
        if (!Rules.Duplicates)
        {
            Session.Status = L("This play mode makes no copies.");
            return;
        }

        if (_vaults.Store is not { } store)
            return;

        int made = 0;
        string? refused = null, last = null;
        foreach (var row in store.RowsOf(Clip.Cards))
        {
            Item copy;
            try
            {
                copy = ItemBlob.Restore(row.Blob, store.StatIdBits);
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                refused ??= problem.Message;
                continue;
            }

            copy.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
            if (Land(copy) is { } no)
            {
                refused ??= no;
                break;
            }

            made++;
            last = ItemPresenter.Describe(copy).Name;
        }

        Session.Status = CopiedSays(made, last) + (refused is null ? "" : " " + refused);
    }

    /// <summary>
    /// Items of the character's stored into a folder of the vault, as Store stores them - the play mode's rule
    /// and all: a cut of the character's pasted on a vault page (joined by the main view model).
    /// </summary>
    public void StorePasted(IReadOnlyCollection<Item> items, long? folder)
    {
        var tiles = AllTiles().Where(tile => items.Contains(tile.Item)).ToList();
        if (tiles.Count == 0)
            return;

        // Into that folder, the vault's top too: Store's "the folder shown" is the vault service's, which is
        // told the folder pasted in for as long as the store takes.
        long? shown = _vaults.CurrentFolder;
        _vaults.CurrentFolder = folder;
        _storeSends = true;
        try
        {
            Store(StoreAs.Open, tiles);
        }
        finally
        {
            _storeSends = false;
            _vaults.CurrentFolder = shown;
        }
    }
}
