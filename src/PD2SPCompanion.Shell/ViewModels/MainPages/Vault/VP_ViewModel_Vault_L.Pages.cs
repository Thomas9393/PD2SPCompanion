using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One stretch of a folder's sheet (<see cref="Vault.PageRows"/> rows of it, the vault's width across), and
/// which it is, from 0: what the view builds at a time. An item whose top is on it is its item, though the
/// item reach down into the next.
/// </summary>
public sealed class VaultPageViewModel(int number, int columns)
{
    public int Number { get; } = number;

    public GridViewModel Grid { get; } = new("Vault", (byte)(number % 256), columns, Vault.PageRows) { RunsOn = true };

    /// <summary>Whether its items have been read: a stretch is read when it first comes into sight.</summary>
    public bool IsLoaded { get; set; }
}

/// <summary>
/// One row of the view: a stretch of the sheet - which, known at once; its grid and its items made and read
/// when the row is first built. The rows out of sight are not built, so a folder of thousands opens as fast
/// as one of ten, and a row is a few numbers until then: a folder of 640 pages made 96,000 cells for rows
/// nobody had scrolled to.
/// </summary>
public sealed class VaultPageRow(int first, int count, Func<int, int, IReadOnlyList<VaultPageViewModel>> pages)
{
    /// <summary>The stretch it is, from 0; one a row since the sheet.</summary>
    public int First { get; } = first;

    public int Count { get; } = count;

    private IReadOnlyList<VaultPageViewModel>? _pages;

    public IReadOnlyList<VaultPageViewModel> Pages => _pages ??= pages(First, Count);
}

// The vault's folder as one sheet: a grid of the vault's width, as long as it needs, drawn a stretch at a time with no seam between -
// the pages it was are gone, and their machinery is what draws it.
public sealed partial class VP_ViewModel_Vault_L
{
    /// <summary>A stretch as it is drawn: its rows of cells, with nothing above or below - the next begins where it ends.</summary>
    public static double PageHeight => Vault.PageRows * ItemTileViewModel.CellSize;

    /// <summary>A row of the view: what a jump of the scroll by a stretch moves by.</summary>
    public static double PageRowHeight => PageHeight;

    /// <summary>
    /// The sheet's width in columns: as many as the page has room for, in fours and never fewer than twelve
    /// (VaultLayout.Columns; the view says the room, <see cref="SetSheetColumns"/>). Not the vault's own, set
    /// in Settings: a folder is laid out by a rule, at whatever width it is shown.
    /// </summary>
    public int SheetColumns { get; private set; } = VaultLayout.DefaultColumns;

    /// <summary>The room the sheet has across, from the view, in columns: the folder is laid out again at it.</summary>
    public void SetSheetColumns(int columns)
    {
        columns = VaultLayout.Fit(columns);
        if (columns == SheetColumns)
            return;

        SheetColumns = columns;
        OnPropertyChanged(nameof(SheetColumns));
        if (Store is not null && ShowsPages)
            Reload();
    }

    /// <summary>The item whose top is nearest under a row of the sheet: what a view keeps in sight through a reflow.</summary>
    public long? KeyNearRow(int row) =>
        Marks.FirstOrDefault(mark => mark.Row >= row)?.Key ?? (Marks.Count > 0 ? Marks[^1].Key : null);

    /// <summary>The row of the sheet an item's top is on, or null where the folder shown does not hold it.</summary>
    public int? RowOf(long key) => _spots.TryGetValue(key, out var mark) ? mark.Row : null;

    // ---- the layout, held while the pointer is on the sheet -----------------------------------------------------

    /// <summary>Where each item of the folder shown lies, by its key: the layout as last made (or held).</summary>
    private Dictionary<long, VaultMark> _spots = [];

    /// <summary>The folder's items in its order, with their names: what a typed letter jumps through.</summary>
    private IReadOnlyList<VaultLaid> _laid = [];

    /// <summary>The rows the layout takes.</summary>
    private int _sheetRows;

    /// <summary>What the layout held was made for: the folder, the order and the columns. Another, and it is made afresh.</summary>
    private (long? Folder, string By, bool Descending, int Columns, int Top, int Beside)? _laidAs;

    private bool _sheetHeld, _sheetStale;

    /// <summary>
    /// The pointer is on the sheet, or has left it. While it is on, an item that leaves the folder leaves its place empty and nothing else moves -
    /// three runes clicked out one after another, and the fourth is still under the pointer; as it leaves, the
    /// folder closes up. A browser's row of tabs waits the same way.
    /// </summary>
    public void HoldSheet(bool held)
    {
        if (_sheetHeld == held)
            return;

        _sheetHeld = held;
        if (!held && _sheetStale && Store is not null && ShowsPages)
            Reload();
    }

    /// <summary>The folder's pages, a row of them to an entry: used pages, one empty, and on to fill the window.</summary>
    public ObservableCollection<VaultPageRow> PageRows { get; } = [];

    /// <summary>
    /// Whether the folder is shown as its pages: whenever the vault is read. A search does not take them away -
    /// it lights its finds on them and says which other folders hold more.
    /// </summary>
    public bool ShowsPages => Store is not null && !HasTrouble;

    // ---- a search, on the pages -----------------------------------------------------------------------

    /// <summary>The items of the folder shown that the search found, by key; null while none stands.</summary>
    private HashSet<long>? _hitKeys;

    /// <summary>How many the search finds in the vault, every folder: the list's count of what it does not list.</summary>
    private int _found;

    /// <summary>The finds of the folder shown, for its small map; null while no search stands.</summary>
    public IReadOnlySet<long>? HitKeys => _hitKeys;

    /// <summary>The folder, its pages or the search's finds were read again, or the map turned on: the view draws it again.</summary>
    public event Action? MapChanged;

    // ---- the small map ---------------------------------------------------------------------------------

    /// <summary>
    /// The pages are scrolled by a small map of the folder in place of the plain scroll bar (a button toggles between
    /// the simple scroll bar and one like VS Code's with a mini preview window). Remembered between runs.
    /// </summary>
    public bool IsMinimap
    {
        get => Look.Minimap;
        set
        {
            if (Look.Minimap == value)
                return;

            Look.Minimap = value;
            Settings.KeepVaultLooks();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsMapNow));
            OnPropertyChanged(nameof(MapHint));
            MapChanged?.Invoke();
        }
    }

    [RelayCommand]
    private void ToggleMinimap()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        IsMinimap = !IsMinimap;
    }

    /// <summary>Every item of the folder shown as the map draws it, read with the folder.</summary>
    public IReadOnlyList<VaultMark> Marks { get; private set; } = [];

    /// <summary>How many pages the scroll holds: those used, the empty one, and on to fill the window.</summary>
    public int PagesShown => PageRows.Sum(row => row.Count);

    /// <summary>
    /// Which rows of the sheet are in sight with the scroll this far down and this much of it showing, of how
    /// many: said beside the folder's map while its window is dragged, as a long list says where a dragged
    /// thumb is.
    /// </summary>
    public string PagesInSight(double top, double height)
    {
        int rows = PagesShown * Vault.PageRows;
        int first = Math.Min(rows, Math.Max(0, (int)(top / ItemTileViewModel.CellSize)) + 1);
        int last = Math.Min(rows, (int)(Math.Max(0, top + height - 1) / ItemTileViewModel.CellSize) + 1);
        return L($"Rows {first} - {last} / {rows}");
    }

    private readonly Dictionary<int, VaultPageViewModel> _pages = [];

    /// <summary>One stretch a row: the sheet is as wide as the vault says, whatever room there is.</summary>
    private const int _pagesAcross = 1;

    private int _rowsInView = 1, _pagesUsed;

    /// <summary>The room the sheet has, from the view: how many stretches of it are in sight.</summary>
    public void SetPagesViewport(double height)
    {
        int rows = Math.Max(1, (int)Math.Ceiling(height / PageRowHeight));
        if (rows == _rowsInView)
            return;

        _rowsInView = rows;
        RebuildPageRows();
    }

    /// <summary>The folder read again: every page forgotten, to be read as it comes into sight.</summary>
    private void ReloadPages(Vault store)
    {
        _pages.Clear();

        // The folder's own folders first, as tiles (LayFolderTiles); its items under them, laid out by the rule
        // (Vault.Sheet, VaultLayout), in the order the Sort menu says, as wide as the page.
        int top = LayFolderTiles();
        var (marks, laid, rows) = store.Sheet(_here, Order.By, Order.Descending, SheetColumns, top, _besideTiles, FolderTile.High);
        var made = (_here, Order.By, Order.Descending, SheetColumns, top, _besideTiles);

        // Held (HoldSheet), and all that changed is that some have left: they leave their places empty.
        if (_sheetHeld && _laidAs == made && laid.Count < _spots.Count && laid.All(item => _spots.ContainsKey(item.Key)))
        {
            var left = laid.Select(item => item.Key).ToHashSet();
            marks = [.. Marks.Where(mark => left.Contains(mark.Key))];
            rows = _sheetRows;
            _sheetStale = true;
        }
        else
        {
            _sheetStale = false;
        }

        _laidAs = made;
        _laid = laid;
        _sheetRows = rows;
        _spots = marks.ToDictionary(mark => mark.Key);
        _pagesUsed = marks.Count == 0 ? 0 : marks.Max(mark => mark.Page) + 1;
        Marks = marks;
        OnPropertyChanged(nameof(Marks));
        OnPropertyChanged(nameof(HitKeys));
        RebuildPageRows();
    }

    /// <summary>
    /// The rows for the stretches there are and the room there is. What a folder shows of its sheet: the
    /// stretches its layout takes - an item on the last with a top may reach into one more - and on to the
    /// window's end; so an empty folder shows what fits and scrolls nowhere.
    /// </summary>
    private void RebuildPageRows()
    {
        if (!ShowsPages)
        {
            PageRows.Clear();
            return;
        }

        int taken = Math.Max(_pagesUsed, (_sheetRows + Vault.PageRows - 1) / Vault.PageRows);
        int shown = Math.Max(taken, _pagesAcross * _rowsInView);
        shown = Math.Min(Vault.PageCap, (shown + _pagesAcross - 1) / _pagesAcross * _pagesAcross);

        var rows = new List<VaultPageRow>();
        for (int first = 0; first < shown; first += _pagesAcross)
            rows.Add(new VaultPageRow(first, Math.Min(shown, first + _pagesAcross) - first, PagesOf));

        // In place, row for row: a list emptied and filled again forgets how far down it was scrolled, and a
        // reload follows every change to the vault.
        for (int i = 0; i < rows.Count; i++)
        {
            if (i < PageRows.Count)
                PageRows[i] = rows[i];
            else
                PageRows.Add(rows[i]);
        }

        while (PageRows.Count > rows.Count)
            PageRows.RemoveAt(PageRows.Count - 1);

        // The map draws as many pages as the scroll holds, as many side by side.
        MapChanged?.Invoke();
    }

    /// <summary>A page of the folder, made where it has not been asked for before.</summary>
    private VaultPageViewModel PageAt(int number)
    {
        if (!_pages.TryGetValue(number, out var page))
            _pages[number] = page = new VaultPageViewModel(number, SheetColumns);

        return page;
    }

    /// <summary>A row's pages, made and read: what a row asks when it is built.</summary>
    private IReadOnlyList<VaultPageViewModel> PagesOf(int first, int count)
    {
        var pages = Enumerable.Range(first, count).Select(PageAt).ToList();
        LoadPages(pages);
        return pages;
    }

    /// <summary>Reads the items of pages not read yet, and lays each on its page.</summary>
    private void LoadPages(IReadOnlyList<VaultPageViewModel> pages)
    {
        var wanted = pages.Where(page => !page.IsLoaded).ToList();
        if (wanted.Count == 0 || Store is not { } store)
            return;

        foreach (var page in wanted)
            page.IsLoaded = true;

        // Past the last used page there is nothing to read.
        if (wanted[0].Number >= _pagesUsed)
            return;

        // The items the layout put on those stretches, read by their keys, in the order they lie.
        var tiles = wanted.ToDictionary(page => page.Number, _ => new List<ItemTileViewModel>());
        var lying = Marks.Where(mark => tiles.ContainsKey(mark.Page)).ToList();
        if (lying.Count == 0)
            return;

        Dictionary<long, VaultRow> read;
        try
        {
            read = store.RowsOf(lying.Select(mark => mark.Key)).ToDictionary(row => row.Key);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return;
        }

        foreach (var mark in lying)
        {
            if (!read.TryGetValue(mark.Key, out var row))
                continue;

            var onPage = tiles[mark.Page];
            int x = mark.X, y = mark.Y;

            ItemTileViewModel card;
            try
            {
                card = ItemTileViewModel.OfVault(row, Cards.Count, store.StatIdBits);
            }
            catch (Exception)
            {
                continue;
            }

            card.MoveTo(card.Index, x, y);
            card.IsSearchMiss = _hitKeys is not null && !_hitKeys.Contains(card.Key);
            card.IsLeaving = _moves?.IsWithdrawing(card.Key) ?? false;
            if (_keptPicks?.Contains(card.Key) == true)
                card.IsPicked = true;
            card.PropertyChanged += WhenCardChanged;
            Cards.Add(card);
            onPage.Add(card);

            if (card.Key == _keptKey)
            {
                _keptKey = null;
                SelectedEntry = card;
            }
        }

        foreach (var page in wanted)
            page.Grid.Fill(tiles[page.Number]);

        DuplicateIndex.Current?.Mark(tiles.Values.SelectMany(each => each));
    }

    // ---- the folder shown ---------------------------------------------------------------------------------
    // The tools line has no tools for the folder SHOWN - Rename, Move and Delete folder: a folder has a
    // card of its own on its folder's sheet, a tile, and every tool acts on
    // what is selected, as Explorer's do. A folder is
    // named by Rename folder, moved by Move to folder and deleted by the bin, its tile selected; the folder
    // shown itself by the menu of its row of the tree or its step of the path. VP_ViewModel_Vault_L.FolderTiles.cs.

    /// <summary>The folder the one shown is in, kept so that deleting the one shown lands there.</summary>
    private long? _hereParent;

    /// <summary>How many items the folder holds, and how many are selected, at the row's end.</summary>
    public string PagesText =>
        Plural(_itemsHere, "1 item", $"{_itemsHere} items")
        + (_hitKeys is { } hits ? " · " + L($"{hits.Count} found") : "")
        + (IsPicking ? " · " + L($"{PickedCount} selected") : "");

    /// <summary>
    /// The carried cards let go of on the path: the move picker opens for them, to say which folder - a row
    /// of the tree or a folder's tile takes a drop itself (<see cref="DropCarriedOnFolder"/>).
    /// </summary>
    public void MoveCarriedByPicker()
    {
        IReadOnlyList<ItemTileViewModel> going = Carrier?._carried ?? [];
        var folders = CarriedFolderCards(Carrier);
        EndCarryOver();

        if (going.Count + folders.Count > 0)
            OpenMoveFor(going, folders);
    }

    // ---- sorting --------------------------------------------------------------------------------------

    /// <summary>
    /// The sort menu, with a folder's pages shown: the folder is laid out in the order picked - a way of looking
    /// at it, which changes nothing in the vault and is no step of the history (not an
    /// act, writing every item's place). Every folder is shown in it, on this page.
    /// </summary>
    private void SortFolder()
    {
        if (!ShowsPages || Store is null)
            return;

        Notice = L($"Sorted by {L(Order.By)}.");
        Reload();
        ScrolledHome?.Invoke();
    }

    // ---- the folder's lock ----------------------------------------------------------------
    // No Close gaps beside Sort: with no item having a place of its own there are no gaps
    // of anyone's making to close - the rule lays the folder out.

    /// <summary>
    /// Whether the folder shown is locked: its items take no pick - so nothing done to several reaches them - and
    /// Store all's sorting delivers past it (Vault.IsLocked). Read off its row of the tree, which the reload has
    /// asked. It kept a folder out of Sort and Close gaps too, while items had places of their own.
    /// </summary>
    public bool IsFolderLocked => Places.FirstOrDefault(place => place.Id == _here)?.IsLocked == true;

    public string FolderLockHint => IsFolderLocked
        ? L("Folder lock on") + Environment.NewLine + L("Items cannot be picked; Store all skips it")
        : L("Folder lock off");

    [RelayCommand]
    private void ToggleFolderLock() => SetFolderLock(_here, !IsFolderLocked);

    private void SetFolderLock(long? folder, bool locking)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        if (Store is not { } store)
            return;

        try
        {
            store.SetLocked(folder, locking);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Notice = problem.Message;
            return;
        }

        // Locking lets go of several selected, as locking a stash page does: they were what Select all takes.
        if (locking && folder == _here && IsPicking)
            IsPicking = false;

        ReloadBoth();
    }

    /// <summary>
    /// The vault item an entry of this page is - a tile of its own, as the pages hand the picker - or null: a
    /// folder's row is none, nor is a tile that is not this page's.
    /// </summary>
    public ItemTileViewModel? CardOf(object? item) => item is ItemTileViewModel tile && Cards.Contains(tile) ? tile : null;

    // ---- moving: a drag let go of on the sheet ----------------------------------------------------------------
    // A card has no cell to land on - lit green or red under it, a group keeping its shape. With no
    // item having a place of its own, the sheet is one thing to let go on: the folder it shows.

    /// <summary>The card the drag took hold of: the one a group withdrawn to the inventory lies about.</summary>
    private ItemTileViewModel? _grabbed;

    /// <summary>
    /// What is carried is over this page's sheet and would come into the folder it shows: the sheet is framed.
    /// The inventory's held item, or the other vault page's cards where it shows another folder - this page's
    /// own cards, over their own sheet, go nowhere.
    /// </summary>
    [ObservableProperty] private bool _isSheetTarget;

    /// <summary>What is carried is over the sheet, or off it - and, off it, its footprint lets go too.</summary>
    public void HoverSheet(bool over)
    {
        IsSheetTarget = over && (Session.IsHolding || (Carrier is { } from && from._carried.Count + from._carriedFolders.Count > 0 && from._here != _here));
        if (!over)
            HoverFootprint(null, 0, 0);
    }

    // ---- the footprint: the cells under what is carried --------------------------------------------------------
    // The sheet framed in green as a whole was too strong, and missed the green footprint -
    // the cells under what is carried, lit as they were while an item had a cell to land on.
    // Under the pointer, given an item now lands where the folder's rule lays it: a cue of where it
    // is let go of, not of where it will lie. Green: the sheet takes whatever it is given.

    private readonly List<GridCell> _lit = [];

    /// <summary>The cell the footprint was last lit from: lit again only when it changes.</summary>
    private (int Page, int Column, int Row)? _footprintAt;

    /// <summary>
    /// What is carried, over a cell of a page of this sheet, or over none: the cells it covers are lit. The inventory's
    /// held item by its own size; the vault's cards - this page's or the other's - in the shape they lie in, about
    /// the one the drag took hold of.
    /// </summary>
    public void HoverFootprint(VaultPageViewModel? page, int column, int row)
    {
        (int, int, int)? at = page is null ? null : (page.Number, column, row);
        if (at == _footprintAt)
            return;

        _footprintAt = at;
        foreach (var cell in _lit)
        {
            cell.IsTarget = false;
            cell.IsAllowed = false;
        }

        _lit.Clear();

        if (page is null)
            return;

        int top = page.Number * Vault.PageRows + row;
        if (Carrier is { _carried: { Count: > 0 } carried } from)
        {
            var held = from._grabbed is not null && carried.Contains(from._grabbed) ? from._grabbed : carried[0];
            if (!from._spots.TryGetValue(held.Key, out var heldAt))
            {
                Light(top, column, held.View.GridWidth, held.View.GridHeight);
                return;
            }

            foreach (var card in carried)
            {
                if (from._spots.TryGetValue(card.Key, out var cardAt))
                    Light(top + cardAt.Row - heldAt.Row, column + cardAt.X - heldAt.X, card.View.GridWidth, card.View.GridHeight);
            }
        }
        else if (Session.Held is { } item)
        {
            Light(top, column, item.View.GridWidth, item.View.GridHeight);
        }
    }

    /// <summary>A block of the sheet lit green, from a row of it and a column: each cell on the stretch its row is on.</summary>
    private void Light(int top, int column, int wide, int tall)
    {
        for (int x = column; x < column + wide; x++)
        {
            for (int at = top; at < top + tall; at++)
            {
                if (x < 0 || at < 0 || x >= SheetColumns || !_pages.TryGetValue(at / Vault.PageRows, out var under))
                    continue;

                int index = at % Vault.PageRows * under.Grid.Columns + x;
                if (x >= under.Grid.Columns || index >= under.Grid.Cells.Count)
                    continue;

                var cell = under.Grid.Cells[index];
                cell.IsTarget = true;
                cell.IsAllowed = true;
                _lit.Add(cell);
            }
        }
    }

    /// <summary>
    /// The carried cards let go of on the sheet: carried from the other vault page, they come into this page's
    /// folder (direct transfer in between); this page's own stay where they are.
    /// </summary>
    public void DropCarriedOnSheet()
    {
        var from = Carrier;
        IReadOnlyList<ItemTileViewModel> going = from?._carried ?? [];
        var folders = CarriedFolderCards(from);
        EndCarryOver();

        if (from is not null && going.Count + folders.Count > 0 && from._here != _here)
            MoveInto([.. going.Select(card => card.Key)], folders, _here, Places.FirstOrDefault(place => place.Id == _here)?.Name ?? L("Vault"));
    }

    /// <summary>Every page of the folder read: what "select all" needs, the pages never scrolled to with the rest.</summary>
    private void LoadEveryPage() =>
        LoadPages([.. Enumerable.Range(0, _pagesUsed).Select(PageAt)]);

    /// <summary>An item was gone to - as a find: the view brings its stretch and its column into sight.</summary>
    public event Action<int, int>? PageSought;

    /// <summary>An item was stepped to by an arrow key: the view scrolls no further than brings it into sight (row, column, cells across and down).</summary>
    public event Action<int, int, int, int>? PlaceSought;

    /// <summary>
    /// The arrow keys on the sheet: the selection goes to
    /// the nearest item that way, a step aside weighed twice a step ahead - Explorer's icon view, and the
    /// inventory's arrows (ItemPanels.Nearest) - and the view follows it. With nothing selected, the folder's
    /// first item. By where the items lie on the sheet, not by what is drawn: a stretch out of sight is read.
    /// </summary>
    public void StepSelection(int across, int down)
    {
        // Everything on the sheet, by where it lies: the folder's folders, which are tiles of it, and its items.
        var spots = new List<(object What, int X, int Row, int Width, int Height)>(SheetFolders.Count + Marks.Count);
        spots.AddRange(SheetFolders.Select(tile => ((object)tile, tile.X, tile.Y, FolderTile.Wide, FolderTile.High)));
        spots.AddRange(Marks.Select(mark => ((object)mark, mark.X, mark.Row, mark.Width, mark.Height)));
        if (spots.Count == 0)
            return;

        static (double X, double Y) Middle((object What, int X, int Row, int Width, int Height) spot) =>
            (spot.X + spot.Width / 2.0, spot.Row + spot.Height / 2.0);

        int from = SelectedEntry switch
        {
            ItemTileViewModel held => spots.FindIndex(spot => spot.What is VaultMark mark && mark.Key == held.Key),
            FolderTile tile => spots.FindIndex(spot => ReferenceEquals(spot.What, tile)),
            _ => -1,
        };
        int to = -1;

        if (from < 0)
        {
            to = spots.IndexOf(spots.OrderBy(spot => spot.Row).ThenBy(spot => spot.X).First());
        }
        else
        {
            var (hereX, hereY) = Middle(spots[from]);
            double best = double.MaxValue;
            for (int at = 0; at < spots.Count; at++)
            {
                if (at == from)
                    continue;

                var (x, y) = Middle(spots[at]);
                double ahead = (x - hereX) * across + (y - hereY) * down;
                double aside = Math.Abs((x - hereX) * down) + Math.Abs((y - hereY) * across);
                if (ahead <= 0.01)
                    continue;

                double score = ahead + 2 * aside;
                if (score < best)
                    (best, to) = (score, at);
            }
        }

        if (to < 0)
            return;

        var reached = spots[to];
        if (reached.What is FolderTile folder)
        {
            SelectedEntry = folder;
            PlaceSought?.Invoke(reached.Row, reached.X, reached.Width, reached.Height);
        }
        else if (reached.What is VaultMark mark)
        {
            LoadPages([PageAt(mark.Page)]);
            if (Cards.FirstOrDefault(card => card.Key == mark.Key) is { } found)
            {
                SelectedEntry = found;
                PlaceSought?.Invoke(reached.Row, reached.X, reached.Width, reached.Height);
            }
        }
    }

    // ---- the search's finds, listed ------------------------------------------------------------------

    /// <summary>
    /// The vault's finds of a search, for the Search page's list: the first <paramref name="most"/> by name,
    /// under their folders, and how many there are in all. Asked of the vault directly - the page is left as it
    /// is. With <paramref name="results"/>, each row says its number under the properties the search asks for -
    /// what tells five charms of one name apart.
    /// </summary>
    public (IReadOnlyList<FoundPlace> Places, int Found) FindsFor(VaultQuery query, int most, SearchResultsViewModel? results = null)
    {
        if (Store is not { } store)
            return ([], 0);

        int found = store.CountEverywhere(query);
        if (found == 0)
            return ([], 0);

        var rows = store.Page(query, "Name", false, 0, most);
        var wants = results?.Wants ?? [];
        var values = wants.Count > 0
            ? store.ValuesOf([.. rows.Select(row => row.Key)], [.. wants.Select(want => want.Want)])
            : null;

        var places = new List<FoundPlace>();
        foreach (var folder in rows.GroupBy(row => row.FolderId))
        {
            var items = new List<FoundItem>();
            foreach (var row in folder)
            {
                ItemTileViewModel card;
                try
                {
                    card = ItemTileViewModel.OfVault(row, 0, store.StatIdBits);
                }
                catch (Exception)
                {
                    continue;
                }

                long key = row.Key;
                long? at = row.FolderId;
                string detail = results is not null
                    ? results.Detail(card, values?.GetValueOrDefault(key))
                    : card.View.BaseName is { Length: > 0 } named && !named.Equals(card.Name, StringComparison.Ordinal) ? named : "";
                string called = card.Name;
                items.Add(new FoundItem(key, card, detail, "", inVault: true, () => GoToFound(key, at)) { Menu = () => MenuOfFind(key, at, called) });
            }

            if (items.Count > 0)
                places.Add(new FoundPlace(L("Vault") + PathOf(folder.Key)[TopName.Length..], items.Count, items));
        }

        return (places, found);
    }

    /// <summary>The most of the vault's finds read for the box's short list: a row each, by its kept name, no item made.</summary>
    private const int MostRanked = 2000;

    /// <summary>The most finds made into items to tell a find by its base from a find by a stat: what the box's list made before it was short.</summary>
    private const int MostWeighed = 60;

    /// <summary>
    /// The vault's best finds for the window's own search box, and how many there are in all: the
    /// <paramref name="most"/> that answer what was typed best (<see cref="FindRank"/>), each saying its folder.
    /// The names are the vault's own rows', so a find by its name costs no item made; only where the names
    /// leave room are finds read as items, to put a base's before a stat's. Asked of the vault directly - the
    /// standing search and the page are left as they are.
    /// </summary>
    public (IReadOnlyList<FoundItem> Items, int Found) BestFor(VaultQuery query, Func<string, string?, int> rank, int most)
    {
        if (Store is not { } store)
            return ([], 0);

        int found = store.CountEverywhere(query);
        if (found == 0)
            return ([], 0);

        FoundItem? Made(VaultRow row)
        {
            try
            {
                var card = ItemTileViewModel.OfVault(row, 0, store.StatIdBits);
                long key = row.Key;
                long? at = row.FolderId;
                string detail = card.View.BaseName is { Length: > 0 } named && !named.Equals(card.Name, StringComparison.Ordinal) ? named : "";
                // "Crafting Bases\Weapons": the folder's path under the vault's own name - the group's heading
                // says "Vault" - and nothing where it lies at the vault's top.
                string path = PathOf(at)[TopName.Length..].TrimStart('\\', '/', ' ');
                string called = card.Name;
                return new FoundItem(key, card, detail, path, inVault: true, () => GoToFound(key, at))
                {
                    FullWhere = PathOf(at),
                    Menu = () => MenuOfFind(key, at, called),
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        var rows = store.Page(query, "Name", false, 0, MostRanked);
        var best = rows.Select(row => (Row: row, Rank: rank(row.Name, null)))
                       .Where(each => each.Rank <= FindRank.Named)
                       .OrderBy(each => each.Rank)
                       .Take(most)
                       .Select(each => Made(each.Row)).OfType<FoundItem>().ToList();

        if (best.Count < most)
        {
            best.AddRange(rows.Where(row => rank(row.Name, null) > FindRank.Named)
                              .Take(MostWeighed)
                              .Select(Made).OfType<FoundItem>()
                              .OrderBy(item => rank(item.Name, item.Tile.View.BaseName))
                              .Take(most - best.Count));
        }

        return (best, found);
    }

    /// <summary>
    /// A find of the list gone to: its folder shown, its page brought into sight, the item selected. The page
    /// is asked for once the folder's rows are laid out - at once it was scrolled to against the folder before.
    /// </summary>
    public void GoToFound(long key, long? folder)
    {
        // The list was made by the page the search was with; that page may have gone out of sight since, and
        // the find is gone to on the vault page that is there - the left, where neither is yet.
        if (Twin is { } twin && InSight?.Invoke() == false && (twin.InSight?.Invoke() == true || !IsPrimary))
        {
            twin.GoToFound(key, folder);
            return;
        }

        if (Store is not { } store)
            return;

        if (folder != _here)
            GoInto(folder);

        if (IsPicking)
            IsPicking = false;

        if (!_spots.TryGetValue(key, out var spot))
            return;

        int on = spot.Page, column = spot.X;

        LoadPages([PageAt(on)]);
        if (Cards.FirstOrDefault(card => card.Key == key) is { } found)
        {
            SelectedEntry = found;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => PageSought?.Invoke(on, column), Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>The item selected before a reload, to select again when its page is read.</summary>
    private long? _keptKey;

    /// <summary>A click on an item of a page selects it, as a click on its card did.</summary>
    [RelayCommand]
    private void ClickTile(ItemTileViewModel tile)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SelectedEntry = CardOf(tile);
    }
}
