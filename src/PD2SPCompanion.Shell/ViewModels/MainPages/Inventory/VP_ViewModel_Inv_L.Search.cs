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

// The search over the inventory, and the search panel over this page.
public partial class VP_ViewModel_Inv_L
{
    // ---- the search, over the inventory --------

    private readonly InventorySearch? _search;
    private Avalonia.Threading.DispatcherTimer? _searchPause;

    /// <summary>A moment after the search stops changing, as the vault list waits - not on every key.</summary>
    private void WhenSearchChanged()
    {
        _searchPause ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250),
            Avalonia.Threading.DispatcherPriority.Background, (_, _) =>
            {
                _searchPause!.Stop();
                ApplySearch();
            });

        _searchPause.Stop();
        _searchPause.Start();
    }

    /// <summary>Every tile the page has: the grids, what is worn or lies on the corpse, the shared stash, the materials tab.</summary>
    private IEnumerable<ItemTileViewModel> AllTiles() =>
        Session.AllGrids.SelectMany(grid => grid.Items)
            .Concat(Session.Equipped.Concat(Session.MercEquipped).Concat(Session.CorpseEquipped).Select(slot => slot.Item).OfType<ItemTileViewModel>())
            .Concat(Stash.Pages.SelectMany(page => page.Items))
            .Concat(Materials.Panels.SelectMany(panel => panel.Grid.Items));

    /// <summary>
    /// The open files' index written ahead of the first search of them (<see cref="ItemIndex.OfOpenFiles"/>):
    /// at idle once a character is open and its pages laid out (MainView.AfterCharacterPages). The first word
    /// typed after an open wrote it - 62 ms of that word's answer (a trace). After a change the
    /// next search writes it again, as it must.
    /// </summary>
    public void PrepareSearch()
    {
        if (!Session.IsLoaded)
            return;

        try
        {
            ItemIndex.OfOpenFiles.Hold(AllTiles().Where(tile => tile.Tally != 0).Select(tile => tile.Item), Session.ItemsStamp);
        }
        catch (Exception problem)
        {
            // The search writes it itself, and says what stops it.
            Log.Caught(problem);
        }
    }

    /// <summary>
    /// The search standing, over every item on the page: what it leaves out dims where it lies, and
    /// what it finds is listed, place by place, in the search's floating list (<see cref="ListFinds"/>).
    /// The same query the vault answers (<see cref="InventorySearch"/>). A materials slot holding none
    /// is not an item, so never found.
    /// </summary>
    private void ApplySearch()
    {
        var tiles = AllTiles().ToList();
        HashSet<Item>? found = null;
        try
        {
            found = _search?.Matching(tiles.Where(tile => tile.Tally != 0).Select(tile => tile.Item), Session.ItemsStamp);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Session.Status = L($"Search not applied: {problem.Message}");
        }

        foreach (var tile in tiles)
            tile.IsSearchMiss = found is not null && !found.Contains(tile.Item);

        ListFinds(found);
        OnPropertyChanged(nameof(IsSearching));
    }

    /// <summary>
    /// How many of the character's items the standing search finds, said to the Search page
    /// (<see cref="SearchResultsViewModel"/>) - which lists them itself (<see cref="FindsFor"/>).
    /// </summary>
    private void ListFinds(HashSet<Item>? found)
    {
        if (Search?.Results is not { } results)
            return;

        results.SetCharacter(found is null || found.Count == 0
            ? 0
            : AllTiles().Select(tile => tile.Item).Where(found.Contains).Distinct<Item>(ReferenceEqualityComparer.Instance).Count());
    }

    /// <summary>
    /// The character's finds of a search, place by place - what is worn, the mercenary's, then every container
    /// in the order the panes' pickers offer them. Asked of <paramref name="search"/>:
    /// the window's own box's (<see cref="QuickSearchViewModel"/>), which is not the standing search - nothing
    /// on the page is dimmed for it - or the Search page's, over the standing search, which hands in
    /// <paramref name="results"/> so each row says its number under the properties asked for.
    /// </summary>
    public IReadOnlyList<FoundPlace> FindsFor(InventorySearch search, SearchResultsViewModel? results = null)
    {
        var found = search.Matching(AllTiles().Where(tile => tile.Tally != 0).Select(tile => tile.Item), Session.ItemsStamp);
        if (found is null || found.Count == 0)
            return [];

        if (results is null)
            return PlacesOf(found, tile => tile.View.BaseName is { Length: > 0 } named && !named.Equals(tile.Name, StringComparison.Ordinal) ? named : "", out _);

        IReadOnlyDictionary<Item, double?[]>? values = null;
        try
        {
            values = search.ValuesOf(found);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }

        return PlacesOf(found, tile => results.Detail(tile, values?.GetValueOrDefault(tile.Item)), out _);
    }

    /// <summary>
    /// The character's own item a find of the window's search was - one read off this character's save while
    /// another was open, the character loaded since: found again by its id and base (a stack, which has no id,
    /// by its base alone), as a find that can be gone to. Null where it is not here.
    /// </summary>
    public FoundItem? FindOf(uint id, string code, bool compact)
    {
        if (AllTiles().FirstOrDefault(each => each.Item.Code == code && (compact || each.Item.Id == id)) is not { } tile)
            return null;

        var item = tile.Item;
        return new FoundItem(item, tile, "", "", inVault: false, () => GoToFound(item)) { Menu = () => MenuOfFind(item) };
    }

    /// <summary>The finds among the page's tiles, under the place each lies in, in the list's order.</summary>
    private List<FoundPlace> PlacesOf(HashSet<Item> found, Func<ItemTileViewModel, string> detail, out int count)
    {
        var places = new List<FoundPlace>();
        var listed = new HashSet<Item>(ReferenceEqualityComparer.Instance);

        void Add(string name, IEnumerable<ItemTileViewModel> tiles)
        {
            var items = new List<FoundItem>();
            foreach (var tile in tiles.Where(tile => found.Contains(tile.Item)).OrderBy(tile => tile.CellY).ThenBy(tile => tile.CellX))
            {
                var item = tile.Item;
                if (!listed.Add(item))
                    continue;

                items.Add(new FoundItem(item, tile, detail(tile), "", inVault: false, () => GoToFound(item)) { Menu = () => MenuOfFind(item) });
            }

            if (items.Count > 0)
                places.Add(new FoundPlace(name, items.Count, items));
        }

        Add(L("Equipped"), Session.Equipped.Select(slot => slot.Item).OfType<ItemTileViewModel>());
        Add(L("Corpse"), Session.CorpseEquipped.Select(slot => slot.Item).OfType<ItemTileViewModel>());
        Add(L("Mercenary"), Session.MercEquipped.Select(slot => slot.Item).OfType<ItemTileViewModel>());

        foreach (var section in Sections)
        {
            foreach (var choice in section.Pages ?? [])
            {
                if (!choice.IsSeparator)
                    Add(choice.Label, choice.IsMaterials ? (choice.Sheet?.Panels ?? Materials.Panels).SelectMany(panel => panel.Grid.Items) : choice.Grid?.Items ?? []);
            }
        }

        // What lies where no pane shows it - the belt's potions.
        Add(L("Belt"), AllTiles());

        count = listed.Count;
        return places;
    }

    /// <summary>
    /// A find of the list gone to: the container it lies in shown - in the first pane, unless a pane already
    /// shows it - and the item selected. By the item: every move makes the tiles again.
    /// </summary>
    public void GoToFound(Item item)
    {
        if (AllTiles().FirstOrDefault(each => ReferenceEquals(each.Item, item)) is not { } tile)
            return;

        foreach (var section in Sections)
        {
            var holding = section.Pages?.FirstOrDefault(page => !page.IsSeparator
                && (page.IsMaterials ? (page.Sheet?.Panels ?? Materials.Panels).SelectMany(panel => panel.Grid.Items) : page.Grid?.Items ?? []).Contains(tile));
            if (holding is null)
                continue;

            section.IsOpen = true;
            if (section.SelectedPage != holding && !(section.SecondShown && section.SecondPage == holding))
                section.SelectedPage = holding;
        }

        if (IsPicking)
            IsPicking = false;

        Session.SelectedItem = tile;
        WentToFound?.Invoke(tile);
    }

    /// <summary>
    /// A find was gone to: its tile is to be brought into sight, which is the view's to do - the page is taller
    /// than its room, and a find low in a stash page was selected out of sight.
    /// </summary>
    public event System.Action<ItemTileViewModel>? WentToFound;

    /// <summary>Whether a search stands over the page.</summary>
    public bool IsSearching => _search?.IsActive == true;

    /// <summary>
    /// The one search's form and its list of finds, which this page marks. The form is the
    /// window's (SearchWindow), and nothing here opens or closes it.
    /// </summary>
    public SearchPanelViewModel? Search { get; }

    /// <summary>
    /// Every tile the page shows judged again (<see cref="JudgeAllNow"/>): once for an act, just before the window draws
    /// - a move across files refreshed the page twice and judged it twice - and only while the page is seen: a skill
    /// point, a quest or a step of the experience slider judged every tile of the hidden page. The marks are drawn, nothing reads them, and a rebuild clears them before this puts them
    /// back.
    /// </summary>
    private void JudgeAll() => JudgeWork.Ask();

    private WhenShown? _judgeWork;

    private WhenShown JudgeWork => _judgeWork ??= new WhenShown(JudgeAllSoon);

    private void JudgeAllSoon()
    {
        if (_judgePosted)
            return;

        _judgePosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _judgePosted = false;
            if (!JudgeWork.PutOff())
                JudgeAllNow();
        }, DispatcherPriority.Normal);
    }

    private bool _judgePosted;

    /// <summary>
    /// Every tile the page shows, told whether the character can use it: red where it cannot, and
    /// its requirement lines red where they fall short.
    /// </summary>
    private void JudgeAllNow()
    {
        var tiles = Session.AllGrids.SelectMany(grid => grid.Items)
            .Concat(Session.Equipped.Concat(Session.MercEquipped).Select(slot => slot.Item).OfType<ItemTileViewModel>())
            .Concat(Stash.Pages.SelectMany(page => page.Items))
            .ToList();

        Session.JudgeTiles(tiles);

        // Held more than once: the duplicate badge, when the play rule asks for it.
        DuplicateIndex.Current?.Mark(tiles);
    }

    private void Rebuild()
    {
        ShowEquipped();

        if (!MercEquipped.SequenceEqual(Session.MercEquipped))
        {
            MercEquipped.Clear();
            foreach (var slot in Session.MercEquipped)
                MercEquipped.Add(slot);
        }

        // What a section was showing is the reader's, not the file's. Every move rebuilds,
        // and a rebuild makes new sections - so without this, moving one item sent the pane
        // back to the personal stash, folded the second away and lost the page you were
        // working on. Remembered by Key, because the stash renames itself with its picker.
        // The same section kept where the rebuild offers the same pages over the same grids: a new
        // one per move rebuilt the whole of it on screen - panes, pickers, purses and grids, 50 to
        // 85 ms and most of the layout after it (measured).
        StashPageChoice? personal = null;
        var offered = Session.IsLoaded ? ContainerChoices(out personal) : null;
        if (offered is not null && Sections is [{ Key: "Containers" } kept] && SameChoices(kept.Pages, offered))
        {
            kept.ContentsChanged();
            foreach (var purse in kept.Purses)
                purse.Reread();
            AfterSections();
            return;
        }

        var showing = Sections.ToDictionary(
            section => section.Key,
            section => (section.SelectedPage?.Label,
                        section.SecondPage?.Label,
                        section.SecondShown,
                        section.IsOpen));

        Sections.Clear();

        if (offered is not null && ContainersSection(offered, personal) is { } containers)
            Sections.Add(containers);

        // Nothing without a character, the shared stash included.
        //
        // It is a file of its own, but it is only ever reached through a character - which
        // is how the game works too, where you walk up to a stash in town. Tying the two
        // together means one commit point instead of two: the character's Save writes both
        // files, so an item moved between them cannot be half-saved, duplicated by writing
        // only the stash, or lost by writing only the character.
        

        // The sections are new objects on every rebuild, so each is told again.
        foreach (var section in Sections)
        {
            section.SideBySide = StashesSideBySide;

            if (!showing.TryGetValue(section.Key, out var was))
                continue;

            section.IsOpen = was.Item4;

            if (section.Pages is not { } choices)
                continue;

            // By label, since the choices themselves are new objects too.
            if (choices.FirstOrDefault(page => page.Label == was.Item1) is { } first)
                section.SelectedPage = first;

            if (choices.FirstOrDefault(page => page.Label == was.Item2) is { } second)
                section.SecondPage = second;

            section.SecondShown = was.Item3;
        }

        AfterSections();
    }

    /// <summary>What every rebuild tells the sections and says, kept or new.</summary>
    private void AfterSections()
    {
        foreach (var section in Sections)
        {
            section.IsPicking = IsPicking;
            section.Picks = PicksFor(section);
        }

        OnPropertyChanged(nameof(MaterialsShown));
        OnPropertyChanged(nameof(CanClearMaterials));

        ShowPicks();

        SayPanesMoved();

        OnPropertyChanged(nameof(WornCount));
        OnPropertyChanged(nameof(EquipmentHeader));
        OnPropertyChanged(nameof(HasMerc));
        OnPropertyChanged(nameof(MercWornCount));
        OnPropertyChanged(nameof(MercHeader));
        SayMerc();
        OnPropertyChanged(nameof(HasGolem));
        OnPropertyChanged(nameof(GolemShown));
        OnPropertyChanged(nameof(GolemSlots));
        OnPropertyChanged(nameof(GolemDetail));
        OnPropertyChanged(nameof(HasCorpse));
        OnPropertyChanged(nameof(CorpseSlots));
        OnPropertyChanged(nameof(CorpseDetail));
        OnPropertyChanged(nameof(HasCharacter));
        OnPropertyChanged(nameof(HasUnknownPages));
        OnPropertyChanged(nameof(UnknownPagesWarning));

        // A move may have made room, or taken it.
        _landing.SayRoomChanged();
    }
}
