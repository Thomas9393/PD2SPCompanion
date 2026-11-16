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

// Picking: several items chosen at once, by click, drag rectangle or menu.
public partial class VP_ViewModel_Inv_L
{
    /// <summary>
    /// Several selected: two or more picks, for deleting, storing or moving them together - or a
    /// rectangle or a Ctrl-click under way, which may yet leave one or none (see
    /// <see cref="Normalize"/>). There is no mode to turn on: one selected is <c>Session.SelectedItem</c>,
    /// several are these picks, and never both. No item is looked at while it lasts, so the
    /// detail page says how many instead. The picks are items rather than tiles, since every
    /// rebuild makes new tiles.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanCut))]
    [NotifyPropertyChangedFor(nameof(CutHint))]
    [NotifyPropertyChangedFor(nameof(CopyHint))]
    [NotifyPropertyChangedFor(nameof(CanStore))]
    [NotifyPropertyChangedFor(nameof(DeleteHint))]
    [NotifyPropertyChangedFor(nameof(StoreHint))]
    private bool _isPicking;

    private readonly HashSet<Item> _picked = new(ReferenceEqualityComparer.Instance);

    public int PickedCount => _picked.Count;

    partial void OnIsPickingChanged(bool value)
    {
        DisarmDelete();

        // The item panel beside the page says how many are selected, rather than standing empty.
        Session.IsPicking = value;

        if (value)
        {
            // Nothing on the cursor and nothing looked at: several selected edits nothing, and
            // the detail page says how many for as long as it lasts.
            if (Session.IsHolding)
                Session.CancelHold();

            Session.SelectedItem = null;
        }
        else
        {
            // Back to one or none: every pick goes, so nothing stays lit - whatever one is left
            // is made the selection by the caller (Normalize).
            _picked.Clear();
        }

        foreach (var section in Sections)
            section.IsPicking = value;

        ShowPicks();
    }

    /// <summary>
    /// Every tile a pick could be on: what is worn, the belt, the inventory, the cube, and every
    /// page of the stash section, shown or not. Not the materials tab, whose tiles are counts.
    /// </summary>
    private IEnumerable<ItemTileViewModel> PickableTiles()
    {
        // The belt's potions, which the equipment panel shows on request.
        foreach (var tile in Session.BeltGrid?.Items ?? [])
            yield return tile;

        // What the character and the mercenary wear, and the Iron Golem's item beside the mercenary's - its slot is
        // a pick list of the mercenary's section, and a pick on it was in no list.
        foreach (var tile in Session.Equipped.Concat(Session.MercEquipped).Select(slot => slot.Item).OfType<ItemTileViewModel>())
            yield return tile;

        if (Session.GolemTile is { } golem)
            yield return golem;

        // What lies on the corpse, beside what is worn.
        foreach (var tile in Session.CorpseEquipped.Select(slot => slot.Item).OfType<ItemTileViewModel>())
            yield return tile;

        foreach (var section in Sections)
        {
            if (section.Pages is not { } pages)
            {
                foreach (var tile in section.Grid.Items)
                    yield return tile;
                continue;
            }

            foreach (var page in pages)
            {
                if (page.Grid is { } grid)
                {
                    foreach (var tile in grid.Items)
                        yield return tile;
                }
            }
        }
    }

    /// <summary>
    /// A pane turned to another page: the picks on a page no pane shows are let go of, as the belt's are when it is
    /// put away - nothing out of sight is deleted, stored or copied with the rest. After the
    /// change has settled: a swap of the two panes passes through a moment where one page is shown by neither.
    /// </summary>
    private void DropUnseenPicks(ContainerSectionViewModel section)
    {
        if (!IsPicking || section.Pages is not { } pages)
            return;

        var shown = ShownIn(section).ToHashSet();
        var unseen = pages.Where(page => page.Grid is not null).SelectMany(page => page.Grid!.Items)
            .Where(tile => tile.IsPicked && !shown.Contains(tile)).ToList();
        if (unseen.Count == 0)
            return;

        foreach (var tile in unseen)
            Pick(tile, false);

        SayPicksChanged();
        Normalize();
    }

    /// <summary>What a section is showing: its first pane, and its second where that is open.</summary>
    private static IEnumerable<ItemTileViewModel> ShownIn(ContainerSectionViewModel section)
    {
        if (section.ShowsGrid)
        {
            foreach (var tile in section.Grid.Items)
                yield return tile;
        }

        if (section.ShowSecond && section.SecondShowsGrid && section.SecondGrid is { } second)
        {
            foreach (var tile in second.Items)
                yield return tile;
        }
    }

    private List<ItemTileViewModel> PickedTiles() =>
        PickableTiles().Where(tile => _picked.Contains(tile.Item)).ToList();

    /// <summary>
    /// One pick taken or let go. Other picks are another question: armed for three, the bin does not go on to
    /// delete the ten a Ctrl+A made since - it asks again, as the vault's does.
    /// </summary>
    private void Pick(ItemTileViewModel tile, bool picked)
    {
        if (picked ? _picked.Add(tile.Item) : _picked.Remove(tile.Item))
            DisarmDelete();

        tile.IsPicked = picked;
    }

    /// <summary>
    /// A drag rectangle, a Ctrl- or Shift-click or a section's menu is starting on this page
    /// (DragPick): picking comes on by itself, as Explorer's selection grows without a mode.
    /// With <paramref name="keepSelected"/>, the item selected on its own becomes the first pick,
    /// as Explorer's Ctrl+click adds to the selection there is. Not while an item is on the
    /// cursor - that press is a drop's.
    /// </summary>
    public bool BeginDragPick(bool keepSelected)
    {
        if (Session.IsHolding)
            return false;

        if (IsPicking)
            return true;

        var kept = keepSelected ? Session.SelectedItem : null;

        IsPicking = true;

        if (kept is not null && Materials.SlotOf(kept) is null && !IsKept(kept)
            && PickableTiles().FirstOrDefault(tile => ReferenceEquals(tile.Item, kept.Item)) is { } tile)
        {
            Pick(tile, true);
        }

        SayPicksChanged();
        return true;
    }

    // The rest of the drag rectangle's side (IDragPicker), with the page's rules: the materials
    // tab's tiles are counts and take no tick, and what a lock keeps - the inventory's or a page's -
    // is never one of several.
    public bool? IsPicked(object item) =>
        TileOf(item) is { } tile && Materials.SlotOf(tile) is null && !IsKept(tile) ? tile.IsPicked : null;

    public void SetPicked(object item, bool picked)
    {
        if (TileOf(item) is not { } tile || Materials.SlotOf(tile) is not null || (picked && IsKept(tile)))
            return;

        // The selection stays in one section: a pick in another starts afresh there.
        if (picked && !tile.IsPicked && PickedTiles().FirstOrDefault() is { } other
            && !Equals(SectionOf(other), SectionOf(tile)))
        {
            foreach (var each in PickableTiles())
                each.IsPicked = false;
            _picked.Clear();
        }

        Pick(tile, picked);
        SayPicksChanged();
    }

    /// <summary>Every pick goes, on every grid and stash page - a plain drag starts afresh.</summary>
    public void ClearPicks()
    {
        foreach (var tile in PickableTiles())
            tile.IsPicked = false;

        if (_picked.Count > 0)
            DisarmDelete();
        _picked.Clear();
        SayPicksChanged();
    }

    /// <summary>A click on empty space, in a section or on the page: nothing selected.</summary>
    public void ClickedEmpty() => ClearSelection();

    /// <summary>Whether anything here is selected: one item, or several picked (the Forge leaves the page for either).</summary>
    public bool HasItemSelection => IsPicking ? PickedCount > 0 : Session.SelectedItem is not null;

    /// <summary>Nothing selected: not several, not one.</summary>
    public void ClearSelection()
    {
        if (IsPicking)
            IsPicking = false;

        Session.SelectedItem = null;
    }

    public void EndPick() => Normalize();

    /// <summary>
    /// A Ctrl- or Shift-click on what takes no pick. What the inventory lock keeps says so, and
    /// the selection stays as it was; a materials count is only ever looked at, so it is looked at.
    /// </summary>
    public void RefusedPick(object item)
    {
        if (TileOf(item) is not { } tile)
            return;

        if (Materials.SlotOf(tile) is not null)
        {
            ClickTile(tile);
            return;
        }

        if (IsKept(tile))
            Session.Status = KeptSay(tile);
    }

    /// <summary>
    /// Several selected, or one, or none - never several of one. A Ctrl-click, a rectangle or a
    /// menu that leaves a single pick makes it the selection, and the editor opens on it; none
    /// leaves nothing selected. Not while something is carried: the picks are on the cursor.
    /// </summary>
    private void Normalize()
    {
        if (!IsPicking || Session.IsHolding || _picked.Count >= 2)
            return;

        var one = PickedTiles().FirstOrDefault();
        IsPicking = false;
        Session.SelectedItem = one;
    }

    /// <summary>
    /// Which section a tile is in: what the character wears with its belt, the mercenary's, or a
    /// containers section - the one a selection stays inside.
    /// </summary>
    private object? SectionOf(ItemTileViewModel tile)
    {
        var item = tile.Item;

        if (Session.Equipped.Any(slot => ReferenceEquals(slot.Item?.Item, item))
            || Session.CorpseEquipped.Any(slot => ReferenceEquals(slot.Item?.Item, item))
            || Session.BeltGrid?.Items.Any(each => ReferenceEquals(each.Item, item)) == true)
        {
            return Session.Equipped;
        }

        if (Session.MercEquipped.Any(slot => ReferenceEquals(slot.Item?.Item, item))
            || ReferenceEquals(Session.GolemTile?.Item, item))
        {
            return Session.MercEquipped;
        }

        foreach (var section in Sections)
        {
            var grids = section.Pages is { } pages ? pages.Select(page => page.Grid).OfType<GridViewModel>() : [section.Grid];
            if (grids.Any(grid => grid.Items.Any(each => ReferenceEquals(each.Item, item))))
                return section.Key;
        }

        return null;
    }

    /// <summary>
    /// A section's menu, all, none or the others, over <paramref name="tiles"/>: picking comes on
    /// as a Ctrl-click's does, keeping what was selected, and whatever it leaves is the selection.
    /// </summary>
    private void PickOver(IEnumerable<ItemTileViewModel> tiles, Func<ItemTileViewModel, bool> wanted)
    {
        if (!BeginDragPick(keepSelected: true))
            return;

        foreach (var tile in tiles.ToList())
        {
            if (!IsKept(tile))
                SetPicked(tile, wanted(tile));
        }

        SayPicksChanged();
        Normalize();
    }

    // ---- an item out (PD2SPCompanion.Core.Trade.GameClipboard.Write) -------------------------

    /// <summary>
    /// Ctrl + C, the game's own key over an item: what is selected - or every one of several - as the text the
    /// game copies, each with the editor's key that holds the item whole, one object after another as the
    /// game's copies are when pasted together. The Forge's Paste from game makes the first again. Null, and
    /// said, where nothing is selected or an item cannot be written. Nothing is changed.
    /// </summary>
    public string? CopyText()
    {
        var going = IsPicking ? PickedTiles() : Session.SelectedItem is { TabSlot: null } one ? [one] : (List<ItemTileViewModel>)[];
        if (going.Count == 0)
        {
            Session.Status = L("No item selected.");
            return null;
        }

        if (!PD2SPCompanion.Core.Collection.Vault.TryInstalledSeason(out string season, out string why))
        {
            Session.Status = why;
            return null;
        }

        var texts = new List<string>();
        string? refused = null;
        foreach (var tile in going)
        {
            if (PD2SPCompanion.Core.Trade.GameClipboard.Write(tile.Item, PD2SPCompanion.Shell.Services.Vault.VaultService.StatIdBits, season, out string? no) is { } text)
                texts.Add(text);
            else
                refused ??= L($"{tile.Name} not copied: {no}");
        }

        if (texts.Count == 0)
        {
            Session.Status = refused ?? L("Nothing copied.");
            return null;
        }

        _sfxManager?.Method_ButtonSFX_Press();
        Session.Status = (texts.Count == 1 ? L($"{going[0].Name} copied.") : L($"{texts.Count} items copied."))
                 + (refused is null ? "" : " " + refused);
        return string.Join(Environment.NewLine, texts);
    }

    /// <summary>
    /// What "all" takes: everything - or, while a search stands, what it found. The rest is stepped back on
    /// the page, and selecting it with the finds was the opposite of what the search was for.
    /// </summary>
    private bool IsWanted(ItemTileViewModel tile) => !IsSearching || !tile.IsSearchMiss;

    /// <summary>
    /// The search list's Select found: the finds among what the panes show become the selection. Answers how
    /// many there were - none, where the finds lie in containers no pane shows, or are worn.
    /// </summary>
    public int SelectFound()
    {
        if (!IsSearching)
            return 0;

        int found = 0;
        foreach (var section in Sections)
        {
            var finds = ShownIn(section).Where(tile => !tile.IsSearchMiss && !IsKept(tile)).ToList();
            if (finds.Count == 0)
                continue;

            PickOver(ShownIn(section), tile => !tile.IsSearchMiss);
            found += finds.Count;
        }

        return found;
    }

    private PickMenu PicksFor(ContainerSectionViewModel section) => new(
        all: () => PickOver(ShownIn(section), IsWanted),
        none: () => PickOver(ShownIn(section), _ => false),
        invert: () => PickOver(ShownIn(section), tile => !tile.IsPicked));

    /// <summary>Everything a section shows, picked - Ctrl+A over it. While a search stands, what it found there.</summary>
    public void SelectAll(ContainerSectionViewModel section) => PickOver(ShownIn(section), IsWanted);

    /// <summary>The tile an item of a picking surface is: a grid's own, or a worn slot's - empty, none.</summary>
    private static ItemTileViewModel? TileOf(object item) => item switch
    {
        ItemTileViewModel tile => tile,
        EquipSlotViewModel slot => slot.Item,
        _ => null,
    };

    /// <summary>All, none or the others of what the character wears, with the belt while it shows.</summary>
    public PickMenu EquippedPicks => _equippedPicks ??= WornPicks(EquippedTiles);

    /// <summary>The same for the mercenary's.</summary>
    public PickMenu MercPicks => _mercPicks ??= WornPicks(MercTiles);

    private PickMenu? _equippedPicks, _mercPicks;

    private IEnumerable<ItemTileViewModel> EquippedTiles() =>
        Session.Equipped.Select(slot => slot.Item).OfType<ItemTileViewModel>()
            .Concat(ShowBelt ? Session.BeltGrid?.Items ?? [] : []);

    private IEnumerable<ItemTileViewModel> MercTiles() =>
        Session.MercEquipped.Select(slot => slot.Item).OfType<ItemTileViewModel>()
            .Concat(GolemShown && Session.GolemTile is { } golem ? [golem] : []);

    private PickMenu WornPicks(Func<IEnumerable<ItemTileViewModel>> tiles) => new(
        all: () => PickOver(tiles(), IsWanted),
        none: () => PickOver(tiles(), _ => false),
        invert: () => PickOver(tiles(), tile => !tile.IsPicked));

    /// <summary>Ctrl+A over what is worn, or what the mercenary wears.</summary>
    public void SelectAllEquipped() => PickOver(EquippedTiles(), IsWanted);

    public void SelectAllMerc() => PickOver(MercTiles(), IsWanted);

    /// <summary>Ctrl+A with the pointer over no section: all of the section what is selected is in.</summary>
    public void SelectAllAroundSelection()
    {
        var anchor = IsPicking ? PickedTiles().FirstOrDefault() : Session.SelectedItem;
        if (anchor is null || Materials.SlotOf(anchor) is not null)
            return;

        var section = SectionOf(anchor);
        if (ReferenceEquals(section, Session.Equipped))
            SelectAllEquipped();
        else if (ReferenceEquals(section, Session.MercEquipped))
            SelectAllMerc();
        else if (Sections.FirstOrDefault(each => Equals(each.Key, section)) is { } containers)
            SelectAll(containers);
    }

    /// <summary>
    /// Whether something worn, or in the belt while it shows, is selected or picked; a single
    /// selection too. Part of PageHasPicks, which does the dimming.
    /// </summary>
    public bool EquipmentHasPicks => Session.Equipped.Any(slot => slot.Item?.IsMarked == true)
        || (ShowBelt && Session.BeltGrid?.Items.Any(tile => tile.IsMarked) == true);

    public bool MercHasPicks => Session.MercEquipped.Any(slot => slot.Item?.IsMarked == true);

    /// <summary>
    /// Something selected or picked anywhere on the page: worn, mercenary and container items all
    /// step back together, not only the marked one's own section.
    /// </summary>
    public bool PageHasPicks => EquipmentHasPicks || MercHasPicks || Sections.Any(section => section.HasPicks);
}
