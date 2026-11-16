using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Settings;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A step of the path over a vault page: a folder on the way to the one shown, gone to by a click, and the
/// folders it holds listed from the chevron after it - Explorer's address bar.
/// </summary>
public sealed class FolderCrumb(long? id, string name, bool holdsFolders, bool isLast)
{
    /// <summary>The folder, or null for the vault itself.</summary>
    public long? Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>It holds folders: the chevron after it lists them.</summary>
    public bool HoldsFolders { get; } = holdsFolders;

    /// <summary>The folder shown: the path's last step.</summary>
    public bool IsLast { get; } = isLast;

    public bool Same(FolderCrumb other) =>
        Id == other.Id && Name == other.Name && HoldsFolders == other.HoldsFolders && IsLast == other.IsLast;
}

/// <summary>
/// Where a vault page is and the ways about it: the path as crumbs, Back and Forward over where the page has been, the folder tree at
/// its left - and whether the page shows it. (The folder's own folders are tiles of
/// the sheet itself: VP_ViewModel_Vault_L.FolderTiles.cs.)
/// </summary>
public sealed partial class VP_ViewModel_Vault_L
{
    // ---- which page this is, and its look -------------------------------------------------------------

    /// <summary>The book page this vault page is - "Left", "Right": its look is kept under the name.</summary>
    public string PageName { get; init; } = LeftPage;

    private VaultLook? _look;

    /// <summary>What this page shows of its tree and its map, and how wide the tree is.</summary>
    private VaultLook Look => _look ??= Settings.VaultLookOf(PageName);

    /// <summary>The folder tree at the page's left: every folder, gone to by a click, dropped onto by a drag.</summary>
    public bool ShowsTree
    {
        get => Look.Tree;
        set
        {
            if (Look.Tree == value)
                return;

            Look.Tree = value;
            Settings.KeepVaultLooks();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsTreeNow));
            OnPropertyChanged(nameof(TreeHint));
        }
    }

    /// <summary>The tree is on, there is a vault to show the folders of, and the page has room for it.</summary>
    public bool ShowsTreeNow => ShowsTree && ShowsPages && !RoomHidesTree;

    // ---- a page too narrow -------------------------------------------
    // Where the sheet would show fewer than twelve columns - the fewest it has - the page makes room itself, as
    // Explorer puts its navigation pane away at its narrowest: the map gives way to the plain scroll bar; the tree
    // is squeezed, and put away where even its least would not fit. The toggles keep what was asked for, and it
    // all comes back with the room. The view works out the room (VP_View_Vault_L.Fit); these are what it came to.

    /// <summary>No room for the map beside twelve columns of the sheet: the plain scroll bar for the while.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMapNow))]
    [NotifyPropertyChangedFor(nameof(MapHint))]
    private bool _roomHidesMap;

    /// <summary>No room for the tree at its least beside twelve columns of the sheet: put away for the while.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTreeNow))]
    [NotifyPropertyChangedFor(nameof(TreeHint))]
    private bool _roomHidesTree;

    partial void OnRoomHidesMapChanged(bool value) => MapChanged?.Invoke();

    /// <summary>The map is on and the page has room for it.</summary>
    public bool ShowsMapNow => IsMinimap && !RoomHidesMap;

    /// <summary>The map's button: what it is, and under it why there is none while the page has no room for one.</summary>
    public string MapHint => IsMinimap && RoomHidesMap
        ? L("Minimap") + Environment.NewLine + L("Hidden: page too narrow")
        : L("Minimap");

    public string TreeHint => ShowsTree && RoomHidesTree
        ? L("Folder tree") + Environment.NewLine + L("Hidden: page too narrow")
        : L("Folder tree");

    [RelayCommand]
    private void ToggleTree()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        ShowsTree = !ShowsTree;
    }

    /// <summary>How wide the tree is: dragged by its edge, kept when the edge is let go of.</summary>
    public double TreeWidth
    {
        get => Math.Clamp(Look.TreeWidth, VaultLook.NarrowestTree, VaultLook.WidestTree);
        set
        {
            double width = Math.Clamp(value, VaultLook.NarrowestTree, VaultLook.WidestTree);
            if (Look.TreeWidth == width)
                return;

            Look.TreeWidth = width;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TreePaneWidth));
        }
    }

    /// <summary>The tree's edge has been let go of: its width is kept.</summary>
    public void KeepTreeWidth() => Settings.KeepVaultLooks();

    // ---- the grips --------------------------------------------------------------------
    // The tree section had no hint that its width can be dragged: a thumb now, dragged left to the end hides the whole
    // tree, and toggled back it has some minimum width. The tree has a grip on its edge. Dragged past half its least width it shuts under the pointer -
    // still held, and back if the pointer comes back; let go of there it is put away, as its button puts it
    // away, and comes back at the width it had before the drag. (The folder strip had the same, while there was one.)

    /// <summary>The tree's edge is held past the tree's least width: shut for the while, put away if let go of there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreePaneWidth))]
    private bool _treeDraggedShut;

    /// <summary>The tree with its edge: the tree's width, or the edge alone while it is dragged shut.</summary>
    public double TreePaneWidth => TreeDraggedShut ? VaultLook.EdgeWidth : TreeWidth;

    // ---- back and forward --------------------------------------------------------------------------------

    /// <summary>
    /// Where the page has been, the last first to go back to, and where Back has come from: Explorer's two
    /// arrows. A page's own, and the session's - not kept between runs.
    /// </summary>
    private readonly List<long?> _wayBack = [], _wayForward = [];

    /// <summary>More than anyone goes back through; the oldest go first.</summary>
    private const int WaysKept = 64;

    private bool IsThere(long? folder) => folder is not { } id || _folders.Any(each => each.Id == id);

    private bool Leads(List<long?> way) => way.Exists(to => to != _here && IsThere(to));

    public bool CanGoBack => Leads(_wayBack);

    public bool CanGoForward => Leads(_wayForward);

    [RelayCommand]
    private void GoBack() => GoBy(_wayBack, _wayForward);

    [RelayCommand]
    private void GoForward() => GoBy(_wayForward, _wayBack);

    /// <summary>The last folder of a way that is still there - one deleted since is passed over.</summary>
    private void GoBy(List<long?> way, List<long?> other)
    {
        while (way.Count > 0)
        {
            long? to = way[^1];
            way.RemoveAt(way.Count - 1);

            if (to == _here || !IsThere(to))
                continue;

            other.Add(_here);
            GoInto(to, byWay: true);
            return;
        }

        ShowWays();
    }

    /// <summary>Another vault: its folders are not this one's to go back to.</summary>
    private void ForgetWays()
    {
        _wayBack.Clear();
        _wayForward.Clear();
    }

    // ---- the path, and the folders of the folder shown -----------------------------------------------------

    /// <summary>The path to the folder shown, the vault first: each a step to click.</summary>
    public ObservableCollection<FolderCrumb> Crumbs { get; } = [];

    /// <summary>The folder shown is in place - its crumbs, its row in the tree: the view brings the row into sight.</summary>
    public event Action? HereShown;

    private IEnumerable<FolderPlace> Under(long? folder) =>
        Places.Where(place => place.Id is not null && place.Parent == folder);

    /// <summary>The crumbs and the two arrows, as the folders and the folder shown now stand.</summary>
    private void ShowWays()
    {
        if (_wayBack.Count > WaysKept)
            _wayBack.RemoveRange(0, _wayBack.Count - WaysKept);

        var steps = new List<FolderPlace>();
        var byId = Places.Where(place => place.Id is not null).ToDictionary(place => place.Id!.Value);
        for (long? at = _here; at is { } id && byId.TryGetValue(id, out var step) && steps.Count < 256; at = step.Parent)
            steps.Insert(0, step);

        var crumbs = new List<FolderCrumb> { new(null, TopName, Under(null).Any(), steps.Count == 0) };
        crumbs.AddRange(steps.Select((step, n) => new FolderCrumb(step.Id, step.Name, step.HoldsFolders, n == steps.Count - 1)));

        if (Crumbs.Count != crumbs.Count || !Crumbs.Zip(crumbs).All(pair => pair.First.Same(pair.Second)))
        {
            Crumbs.Clear();
            foreach (var crumb in crumbs)
                Crumbs.Add(crumb);
        }

        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        HereShown?.Invoke();
    }

    // ---- a list of folders, under a crumb's chevron or the path's end --------------------------------------

    /// <summary>The folders the open list offers: one folder's own, or every folder as the tree has them.</summary>
    [ObservableProperty] private IReadOnlyList<FolderPlace> _menuPlaces = [];

    [ObservableProperty] private bool _isMenuOpen;

    /// <summary>The chevron after a crumb: the folders that step holds.</summary>
    public void ListUnder(FolderCrumb crumb)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        MenuPlaces = [.. Under(crumb.Id).Select(place => new FolderPlace(place.Id, place.Name, 1, place.Path, place.Items) { IsFlat = true, IsHere = place.IsHere || place.HoldsHere })];
        IsMenuOpen = MenuPlaces.Count > 0;
    }

    /// <summary>The chevron at the path's end: every folder, folded as the tree is - the way anywhere with the tree put away.</summary>
    public void ListEverything()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        MenuPlaces = Places;
        IsMenuOpen = true;
    }

    /// <summary>A folder picked from the open list, or a row of the tree.</summary>
    public void GoToPlace(FolderPlace place)
    {
        IsMenuOpen = false;
        GoInto(place.Id);
    }

    public void GoToCrumb(FolderCrumb crumb) => GoInto(crumb.Id);

    // ---- a drag over a folder: a row of the tree, its tile on the sheet --------------------------------------

    private FolderPlace? _dropTarget;

    /// <summary>What is carried is over a folder, or over none: the one it would go into is lit.</summary>
    public void HoverFolder(FolderPlace? place)
    {
        // A folder carried is no place to let go of itself on.
        if (place is not null && Carrier is { } from && from._carriedFolders.Any(tile => tile.Place.Id == place.Id))
            place = null;

        if (ReferenceEquals(place, _dropTarget))
            return;

        if (_dropTarget is not null)
            _dropTarget.IsDropTarget = false;

        _dropTarget = place;
        if (place is not null)
            place.IsDropTarget = true;
    }

    /// <summary>Held over a folded folder of the tree: it opens, so what it holds can be dropped onto.</summary>
    public void Unfold(FolderPlace place)
    {
        if (place.Id is not { } id || !place.HoldsFolders || place.IsOpen)
            return;

        place.IsOpen = true;
        KeepFolderOpen(id, true);
        ShowPlaces();
    }

    /// <summary>
    /// What the inventory carries, let go of on a folder: stored into it. The inventory stores into the folder
    /// it is given, or the one shown where it is given none - and the vault's top is no folder to give, so for
    /// the moment of the drop the one shown is said to be the one dropped on.
    /// </summary>
    public void DropFromInventoryOn(FolderPlace place)
    {
        long? shown = _vaults.CurrentFolder;
        _vaults.CurrentFolder = place.Id;
        try
        {
            DropFromInventory(place.Id);
        }
        finally
        {
            _vaults.CurrentFolder = shown;
        }
    }

    /// <summary>The carried cards let go of on a folder: moved into it, as Move to folder moves them.</summary>
    public void DropCarriedOnFolder(FolderPlace place)
    {
        // This page's cards or the other's: from the other, the folder shown here is somewhere to move to as well.
        var from = Carrier;
        IReadOnlyList<ItemTileViewModel> going = from?._carried ?? [];
        var folders = CarriedFolderCards(from);

        // Let go of on one of the folders carried: nothing goes into itself, and nothing is said of it.
        bool onItself = folders.Any(folder => folder.Id == place.Id);
        EndCarryOver();

        if (from is null || onItself)
            return;

        // The items into it, where it is not the folder they lie in; the folders, wherever they are not in it already.
        IReadOnlyList<long> keys = place.Id != from._here ? [.. going.Select(card => card.Key)] : [];
        if (keys.Count + folders.Count > 0)
            MoveInto(keys, folders, place.Id, place.Name);
    }
}
