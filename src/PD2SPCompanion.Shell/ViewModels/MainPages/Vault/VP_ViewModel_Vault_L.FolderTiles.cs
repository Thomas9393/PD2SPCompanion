using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Core.Collection;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A folder of the folder shown, as a tile on its sheet: a fake item in the grid, handled as in Windows Explorer,
/// with a folder icon, its name and a summary of its content as the tree view has: four cells by four, the folder
/// over its name and what it holds - two wide was tried, and many short names were capped. It is selected, picked, carried and dropped onto as
/// an item of the sheet is; a double click or Enter goes into it. First on the sheet whatever its order: the
/// Sort menu orders the items, and folders always stand at the top. The strip of tiles
/// over the sheet stood for this until then, and was rarely used.
/// </summary>
public sealed partial class FolderTile(FolderPlace place, int x, int y) : ObservableObject
{
    /// <summary>
    /// The cells a tile takes: four by four - the step the sheet's width goes by (VaultLayout.Step), so a row of
    /// them fills the sheet at every width it has, twelve columns at the least.
    /// </summary>
    public const int Wide = VaultLayout.Step, High = 4;

    public static double Width => Wide * ItemTileViewModel.CellSize;

    public static double Height => High * ItemTileViewModel.CellSize;

    /// <summary>The folder: its row of the tree's own, so what lights the one lights the other.</summary>
    public FolderPlace Place { get; } = place;

    /// <summary>The column and the row of the sheet its top left cell is.</summary>
    public int X { get; } = x;

    public int Y { get; } = y;

    public double Left => X * ItemTileViewModel.CellSize;

    public double Top => Y * ItemTileViewModel.CellSize;

    public string Name => Place.Name;

    /// <summary>What it holds, as its row of the tree says it - a line to each part: "14 items" over "2 folders".</summary>
    public string CountLines => Place.ItemsLine.Replace(" · ", Environment.NewLine);

    /// <summary>The one selected: ringed, as an item's tile is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMarked))]
    private bool _isSelected;

    /// <summary>One of several selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMarked))]
    private bool _isPicked;

    public bool IsMarked => IsSelected || IsPicked;

    /// <summary>A search stands and nothing in this folder, or under it, is a find: faint, as an item that is none.</summary>
    [ObservableProperty] private bool _isSearchMiss;

    /// <summary>
    /// The name box over the tile's name, while the folder is being named there - F2, or its menu's Rename, as Explorer names a folder where it stands. Null otherwise,
    /// so the box is built as it appears and takes the keyboard then (FolderCard.Rename, the path's box, is the same).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenaming))]
    private FolderRename? _rename;

    public bool IsRenaming => Rename is not null;
}

/// <summary>A folder carried, as the window draws it under the pointer.</summary>
public sealed record FolderGhost(string Name, double Left, double Top);

// The folder's own folders, first on its sheet.
public sealed partial class VP_ViewModel_Vault_L
{
    /// <summary>The folders the folder shown holds, as tiles at the head of its sheet - in the tree's order, row by row.</summary>
    public ObservableCollection<FolderTile> SheetFolders { get; } = [];

    /// <summary>The folders that hold a find of the search standing, in them or anywhere under them; null while none stands.</summary>
    private HashSet<long>? _findsUnder;

    /// <summary>The folders picked before a reload, picked again on the tiles made anew.</summary>
    private HashSet<long>? _keptFolderPicks;

    /// <summary>The column the tiles' last row ends at, where it leaves room on the sheet; zero where it does not.</summary>
    private int _besideTiles;

    /// <summary>
    /// The tiles as the folders and the sheet's width now stand, and how many rows of the sheet they take: the
    /// items begin under them (Vault.Sheet's top). The same folders in the same places keep their tiles - a pick,
    /// a press under way, a tooltip stay as they are.
    /// </summary>
    private int LayFolderTiles()
    {
        _besideTiles = 0;
        var under = Under(_here).ToList();
        const int across = FolderTile.Wide;
        int per = Math.Max(1, SheetColumns / across);

        bool same = SheetFolders.Count == under.Count;
        for (int n = 0; same && n < under.Count; n++)
        {
            var tile = SheetFolders[n];
            same = ReferenceEquals(tile.Place, under[n]) && tile.X == n % per * across && tile.Y == n / per * FolderTile.High;
        }

        if (!same)
        {
            var picked = _keptFolderPicks ?? [.. SheetFolders.Where(tile => tile.IsPicked).Select(tile => tile.Place.Id!.Value)];
            foreach (var old in SheetFolders)
                old.PropertyChanged -= WhenFolderTileChanged;

            SheetFolders.Clear();
            for (int n = 0; n < under.Count; n++)
            {
                var tile = new FolderTile(under[n], n % per * across, n / per * FolderTile.High)
                {
                    IsPicked = IsPicking && picked.Contains(under[n].Id!.Value),
                };
                tile.PropertyChanged += WhenFolderTileChanged;
                SheetFolders.Add(tile);
            }
        }

        foreach (var tile in SheetFolders)
            tile.IsSearchMiss = _findsUnder is { } finds && !finds.Contains(tile.Place.Id!.Value);

        // Where the last row of tiles ends, short of the sheet's end: the items begin beside it (Vault.Sheet).
        _besideTiles = under.Count % per == 0 ? 0 : under.Count % per * across;
        return under.Count == 0 ? 0 : (under.Count + per - 1) / per * FolderTile.High;
    }

    private void WhenFolderTileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderTile.IsPicked))
            return;

        if (_tickingMany > 0)
            _ticksChanged = true;
        else
            SayTicksChanged();
    }

    /// <summary>Every folder that holds a find, and every folder such a one lies under: by the finds of each folder's own items.</summary>
    private HashSet<long> FoldersHolding(IReadOnlyDictionary<long, int> finds)
    {
        var parents = _folders.ToDictionary(folder => folder.Id, folder => folder.ParentId);
        var holding = new HashSet<long>();
        foreach (var (folder, count) in finds)
        {
            if (count <= 0)
                continue;

            for (long? at = folder; at is { } id && holding.Add(id); at = parents.GetValueOrDefault(id))
            {
            }
        }

        return holding;
    }

    // ---- selected, as an item is ---------------------------------------------------------------------------------

    /// <summary>The folder's tile selected, where it is one alone.</summary>
    public FolderTile? SelectedFolder => SelectedEntry as FolderTile;

    /// <summary>The folders acted on: the picked tiles, or the one selected.</summary>
    private IReadOnlyList<FolderTile> ActingFolders =>
        IsPicking
            ? [.. SheetFolders.Where(tile => tile.IsPicked)]
            : SelectedEntry is FolderTile one ? [one] : [];

    /// <summary>The same, as the folder commands are handed them.</summary>
    private IReadOnlyList<FolderCard> ActingFolderCards =>
        [.. ActingFolders.Select(tile => CardOf(tile.Place.Id!.Value)).OfType<FolderCard>()];

    /// <summary>Anything selected - items or folders: what the bin, Move to folder and Cut take.</summary>
    public bool HasSelection => HasActing || ActingFolders.Count > 0;

    /// <summary>A click on a folder's tile selects it, as a click on an item's does (Explorer's: a double click opens).</summary>
    [RelayCommand]
    private void ClickFolderTile(FolderTile tile)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SelectOnly(tile);
    }

    /// <summary>A double click on a tile, or Enter with it selected: its folder gone into.</summary>
    public void OpenFolderTile(FolderTile tile) => GoInto(tile.Place.Id);

    /// <summary>A right press on a tile: it is selected, unless it is one of those acted on already - as a card's (SelectForMenu).</summary>
    public void SelectForMenu(FolderTile tile)
    {
        if (!IsCarrying && !ActingFolders.Contains(tile))
            SelectOnly(tile);
    }

    /// <summary>F2 with a folder's tile selected: named where it stands.</summary>
    public void RenameSelectedFolder()
    {
        if (SelectedFolder is { } tile)
            RenameTile(tile);
    }

    /// <summary>
    /// Rename folder, on the tools line: the folder selected - one tile, as Explorer's Rename takes what is
    /// selected. With none it is dimmed. Not the folder SHOWN, where the path stands, which would be unlike
    /// Windows Explorer; the folder shown is named from the folder it lies in, on its tile, or by its menu.
    /// </summary>
    public bool CanRenameFolder => SelectedFolder is not null;

    public string RenameFolderHint => ShortcutText.Dimmed(ShortcutText.With(L("Rename folder"), L("F2")),
        CanRenameFolder ? null : L("Require one folder selected."));

    [RelayCommand]
    private void RenameFolder() => RenameSelectedFolder();

    /// <summary>
    /// A folder named by its id, wherever the page is: on its tile where the sheet shown has it - else the folder
    /// it lies in is gone to first, so that it has one. Its tile is selected and brought into sight, as a new
    /// folder's is in Explorer. A new folder, and Rename in the menu of a row of the tree or a step of the path.
    /// </summary>
    public void RenameFolderInPlace(long folder)
    {
        if (SheetFolders.All(tile => tile.Place.Id != folder))
        {
            if (_folders.FirstOrDefault(each => each.Id == folder) is not { } found)
                return;

            GoInto(found.ParentId);
        }

        if (SheetFolders.FirstOrDefault(tile => tile.Place.Id == folder) is not { } onSheet)
            return;

        SelectOnly(onSheet);
        PlaceSought?.Invoke(onSheet.Y, onSheet.X, FolderTile.Wide, FolderTile.High);
        RenameTile(onSheet);
    }

    /// <summary>
    /// A folder named on its tile: a name box over the name, kept by Enter or by leaving it, given up by Esc -
    /// the path's box's own ways (CommitRename, CancelRename), which take any folder by its card. It went into the
    /// folder and named it where the path stands, for the tiles' first hours.
    /// </summary>
    public void RenameTile(FolderTile tile)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (tile.IsRenaming || !SheetFolders.Contains(tile) || CardOf(tile.Place.Id!.Value) is not { } card)
            return;

        var rename = new FolderRename(card);
        card.Rename = rename;
        tile.Rename = rename;

        // Kept or given up, the card lets its box go: the tile's goes with it. (A name that changed reads the
        // vault again, and the tile is made anew; one that did not leaves this tile standing.)
        void Ended(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FolderCard.IsRenaming) || card.Rename is not null)
                return;

            card.PropertyChanged -= Ended;
            tile.Rename = null;
        }

        card.PropertyChanged += Ended;
    }

    // ---- found by the window's search ------------------------------------------------------------------------------

    /// <summary>
    /// The folders whose names answer a search:
    /// each with the folder it lies in, written as a path. Asked of the vault itself - the page may never have
    /// been shown.
    /// </summary>
    public IReadOnlyList<(long Id, string Name, string Where)> FoldersNamed(Func<string, bool> answers)
    {
        if (Store is not { } store)
            return [];

        var folders = store.Folders();
        var byId = folders.ToDictionary(folder => folder.Id);

        string Where(VaultFolder folder)
        {
            var steps = new List<string>();
            for (long? at = folder.ParentId; at is { } id && byId.TryGetValue(id, out var above) && steps.Count < 256; at = above.ParentId)
                steps.Insert(0, above.Name);

            steps.Insert(0, TopName);
            return string.Join(FolderPlace.Step, steps);
        }

        return [.. folders.Where(folder => answers(folder.Name)).Select(folder => (folder.Id, folder.Name, Where(folder)))];
    }

    // ---- carried ---------------------------------------------------------------------------------------------------

    /// <summary>The folders carried with the cards, or alone.</summary>
    private List<FolderTile> _carriedFolders = [];

    /// <summary>What the window draws under the pointer for them: a small tile each, fanned after the cards.</summary>
    public IReadOnlyList<FolderGhost> GhostFolders =>
        IsCarrying ? [.. _carriedFolders.Select((tile, n) => new FolderGhost(tile.Name, (_carried.Count + n) * 8, (_carried.Count + n) * 8))] : [];

    /// <summary>The folders this page's carry, or the other page's, has with it.</summary>
    private IReadOnlyList<FolderCard> CarriedFolderCards(VP_ViewModel_Vault_L? from) =>
        from is null ? [] : [.. from._carriedFolders.Select(tile => CardOf(tile.Place.Id!.Value)).OfType<FolderCard>()];

    // ---- deleted -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The bin, with folders among what is selected of which one at least holds something (AsksAboutContents; folders
    /// that hold nothing are asked about as items are, by the bin's second press). One folder alone has its
    /// three-way question. Several, or folders with items: one question for them all - the folders only, what they
    /// hold going to the vault's top, or everything - and what is selected beside them goes either way.
    /// </summary>
    private void DeleteWithFolders(Vault store, IReadOnlyList<FolderCard> folders, IReadOnlyList<ItemTileViewModel> loose)
    {
        DisarmDelete();

        if (folders is [var one] && loose.Count == 0)
        {
            AskToDelete(store, one);
            return;
        }

        var outer = Outermost(store, folders);
        var below = outer.SelectMany(folder => store.Subtree(folder.Id)).ToHashSet();
        var counts = store.ItemsPerFolder();
        int items = below.Sum(id => counts.GetValueOrDefault(id));
        int inner = below.Count - outer.Count;
        int count = folders.Count + loose.Count;

        string holds = (items, inner) switch
        {
            (_, 0) => Plural(items, "1 item", $"{items} items"),
            (0, _) => Plural(inner, "1 folder", $"{inner} folders"),
            _ => Plural(items, "1 item", $"{items} items") + " · " + Plural(inner, "1 folder", $"{inner} folders"),
        };

        _confirm.Ask(
            L("Delete selected"),
            L($"{count} selected. The folders hold {holds}."),
            L("DELETE FOLDERS"),
            () => DeleteFolders(store, outer, loose, everything: false),
            L("DELETE ALL"),
            () =>
            {
                if (_moves?.HasWithdrawals == true)
                    _moves.SaveNow?.Invoke();
                if (_moves?.HasWithdrawals == true)
                {
                    Notice = L("Saving. Try again.");
                    return;
                }

                DeleteFolders(store, outer, loose, everything: true);
            },
            confirmHint: L("Delete folders only.") + Environment.NewLine + L("Contents move to top level."),
            alternativeHint: L("Delete folders and contents."), deletes: true);
    }

    // ---- the tree, all of it folded or unfolded --------------------------------------------

    /// <summary>
    /// Every folder that holds folders unfolded, or folded - the whole tree, or what lies under one folder (its
    /// menu's). The session's, as every fold is.
    /// </summary>
    public void FoldAll(bool open, long? under = null)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        var within = under is { } top && Store is { } store ? store.Subtree(top).ToHashSet() : null;
        foreach (var place in Places)
        {
            if (place.Id is not { } id || !place.HoldsFolders || within?.Contains(id) == false)
                continue;

            place.IsOpen = open;
            KeepFolderOpen(id, open);
        }

        ShowPlaces();
    }

    [RelayCommand]
    private void UnfoldTree() => FoldAll(true);

    [RelayCommand]
    private void FoldTree() => FoldAll(false);

    /// <summary>Whether any folder holds folders: with none there is nothing to fold, and the two buttons are dim.</summary>
    public bool CanFoldTree => Places.Any(place => place.HoldsFolders);

    // ---- on the folder's map --------------------------------------------------------------------------------------

    /// <summary>The quality a folder's tile is drawn in on the map: the unique's gold, which a folder's own picture wears.</summary>
    private const int FolderQuality = 7;

    /// <summary>
    /// What the folder's map draws: its folders' tiles, where they stand at the sheet's head, and its items under
    /// them. A tile is no item and has no key: a number no item has.
    /// </summary>
    public IReadOnlyList<VaultMark> MapMarks => SheetFolders.Count == 0 ? Marks :
    [
        .. SheetFolders.Select(tile => new VaultMark(-1 - tile.Place.Id!.Value, tile.Y / Vault.PageRows, tile.X, tile.Y % Vault.PageRows,
                                                     FolderTile.Wide, FolderTile.High, FolderQuality)),
        .. Marks,
    ];
}
