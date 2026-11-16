using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The window's own search: one box in the top row, over everything at once. What is typed is looked for among the open
/// character's items, the vault's, the other characters' on disk, the grail's entries, the settings and the
/// sections - by name, base, stats and what is in the sockets, or by a pattern - and the finds are listed under
/// the box as it is typed.
/// <para>
/// <b>It is fast and short-lived</b>, where a local search can keep a long list. Its list is a few of each group - the ones that answer best
/// (<see cref="FindRank"/>) - and it is there only while the list is: a find gone to, Esc or a click elsewhere
/// puts the list away <i>and empties the box</i> (<see cref="Close"/>). Nothing of it lingers to step through;
/// the two arrows, the count beside the box and the funnel that stood in it are gone with that.
/// </para>
/// <para>
/// <b>The long list is the page's.</b> A group that found more than it lists ends in a line that goes to where
/// all of them are (<see cref="FoundSeeAll"/>): the items' to the Search page, with what was typed as its
/// conditions; a grail tab's to that tab, with it in the grail's own box. Ctrl + Enter is the first of those
/// from the keyboard.
/// </para>
/// <para>
/// <b>Typing changes nothing on the pages.</b> This is not the standing search (<see cref="VaultFilter"/>,
/// the Search page's conditions, whose finds the pages mark): it asks with a filter of its own. Going to an
/// item puts Edit on the right page where the Search page or the Grail was there, so the item gone to is not
/// one their search left dimmed (<see cref="MainViewModel.GoToFind"/>).
/// </para>
/// <para>
/// <b>A find on another character cannot be gone to without loading it</b>, so its row asks, over itself -
/// "Load Gravecopy?", a tick and a cross (<see cref="FoundItem.IsAsking"/>).
/// </para>
/// <para>
/// <b>The list is in groups that fold</b>: the open character, the vault, the other
/// characters, the grail, the settings, the sections - each a heading with its count, folded and unfolded by a
/// click and left as it was (<see cref="FoldMemory"/>).
/// </para>
/// <para>
/// <b>The Search page's results are one of these too</b> (<see cref="IsPanel"/>): asked by the standing search,
/// of the character and the vault only, listed whole under their places, and stepped through by F3.
/// </para>
/// </summary>
public sealed partial class QuickSearchViewModel : ObservableObject
{
    /// <summary>The most the box lists of the open character's finds, and of the vault's.</summary>
    public const int MostMine = 10, MostVault = 10;

    /// <summary>The most it lists of each other character's finds.</summary>
    public const int MostOther = 3;

    /// <summary>The most it lists of each of the grail's tabs.</summary>
    public const int MostTab = 4;

    /// <summary>The most it lists of the settings, and of the sections.</summary>
    public const int MostTargets = 5;

    /// <summary>The most of another character's finds read by name to choose its few from: a save's items are not ranked whole.</summary>
    private const int MostWeighed = 30;

    private readonly MainViewModel _main;
    private readonly VaultService _vaults;
    private readonly ISFXManager? _sfxManager;

    /// <summary>
    /// What is asked: the box's own question - its text and nothing else, which asking does not make the
    /// standing search - or, for the Search page's finder, the standing search itself.
    /// </summary>
    private readonly VaultFilter _asked;
    private readonly InventorySearch _mine;
    private readonly SavedSearches _theirs;
    private readonly DispatcherTimer _soon;
    private int _round;

    /// <summary>
    /// This is the standing search's finder (<see cref="SearchWindowViewModel"/>), not the box's: it is asked by
    /// the standing search itself, with everything its conditions hold; its list is the Search page's results,
    /// whole; and a find clicked is handed to the page (<see cref="Picked"/>).
    /// </summary>
    public bool IsPanel { get; }

    /// <summary>The page's finder: a find was clicked, or stepped onto - the page's to act on.</summary>
    public event Action<IFound>? Picked;

    /// <summary>The Search page's: what a row says of its item under the properties asked for. None for the box.</summary>
    private readonly SearchResultsViewModel? _results;

    /// <param name="form">The Search page's conditions, for its finder; none for the window's search box.</param>
    /// <param name="results">With <paramref name="form"/>: the Search page's, for a row's numbers.</param>
    public QuickSearchViewModel(MainViewModel main, VaultService vaults, ISFXManager? sfxManager, VaultFilter? form = null,
                                SearchResultsViewModel? results = null)
    {
        _main = main;
        _vaults = vaults;
        _sfxManager = sfxManager;
        _results = results;
        _asked = form ?? new VaultFilter();
        IsPanel = form is not null;
        _mine = new InventorySearch(_asked, ItemIndex.OfOpenFiles);
        _theirs = new SavedSearches(_asked);

        if (IsPanel)
            _asked.Changed += Refresh;

        // A pause in the typing, then the search: each letter is not asked on its own. 90 ms, not
        // 180: a run kept the window a quarter of a second at 180, and 29 ms at 90.
        _soon = new DispatcherTimer(TimeSpan.FromMilliseconds(90), DispatcherPriority.Background, (_, _) =>
        {
            _soon!.Stop();
            _ = RunAsync();
        });
        _soon.Stop();
    }

    /// <summary>What is typed in the box. Emptied as its list is put away (<see cref="Close"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    private string _text = "";

    public bool HasText => Text.Trim().Length > 0;

    /// <summary>The list under the box: a group's heading, then its finds; a last line where it holds more.</summary>
    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>The list is in sight: the box is searching. Put away, the box is emptied with it.</summary>
    [ObservableProperty] private bool _isListOpen;

    /// <summary>
    /// Something was found, folded out of sight or not: every group folded, the list and its headings stay, or the
    /// heading that would unfold them went with the results and the search read as having found nothing.
    /// </summary>
    [ObservableProperty] private bool _hasFinds;

    /// <summary>There are rows in sight to mark and go to - and, on the Search page, to step through.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand))]
    private bool _canStep;

    /// <summary>Something was typed and nothing answers to it: the list says so.</summary>
    [ObservableProperty] private bool _foundNothing;

    /// <summary>The box is asked to take the pointer (Ctrl + F): the view's to do.</summary>
    public event Action? FocusRequested;

    /// <summary>A row was marked - by Up or Down, by F3 on the Search page, by a click: the list's to bring into sight.</summary>
    public event Action<IFound>? Marked;

    private List<IFound> Finds => [.. Rows.OfType<IFound>()];
    private IFound? _current;

    /// <summary>The groups' folds, by the name each is kept under.</summary>
    private const string MineFold = "Search_Character", VaultFold = "Search_Vault", OthersFold = "Search_Others", GrailFold = "Search_Grail",
                         SettingsFold = "Search_Settings", SectionsFold = "Search_Sections", FoldersFold = "Search_Folders";

    /// <summary>
    /// A run of a group's finds: a place's heading over them where they have one - a container, a folder, a
    /// character, a grail tab - and a last line where there are more than these: one that goes to them all
    /// (<see cref="FoundSeeAll"/>), or one that only says so (<see cref="FoundMore"/>).
    /// </summary>
    private sealed record Part(FoundPlace? Place, IReadOnlyList<IFound> Finds, object? Last = null);

    /// <summary>A group of the list: where its fold is kept, its name, how many it found, and what of them it lists.</summary>
    private sealed record Group(string Fold, string Name, int Found, IReadOnlyList<Part> Parts);

    /// <summary>What the last search found, group by group: the list is made of it again as a group is folded.</summary>
    private List<Group> _groups = [];

    /// <summary>A line over the finds, where the search was not asked of everything: the game is running.</summary>
    private string _note = "";

    /// <summary>
    /// The settings and the sections there are to find, read off the pages as they stand (the view's to give:
    /// they are its folds' headings and its Settings page's rows). None until the window is up.
    /// </summary>
    public Func<IReadOnlyList<SearchTarget>>? Targets { get; set; }

    /// <summary>A group's heading clicked: folded or unfolded, and kept so.</summary>
    [RelayCommand]
    private void Fold(FoundGroup? group)
    {
        if (group is null)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        _open[group.Fold] = !group.IsOpen;
        PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(group.Fold, !group.IsOpen);
        Show(_groups);
    }

    /// <summary>
    /// Whether each group is unfolded: as it was left in a session before (<see cref="FoldMemory"/>) until it is
    /// folded or unfolded in this one. Held here, as a fold is held by whatever shows it - the memory is only
    /// where it is read from and kept to.
    /// </summary>
    private readonly Dictionary<string, bool> _open = [];

    private bool IsOpen(string fold) =>
        _open.TryGetValue(fold, out bool open) ? open : _open[fold] = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(fold);

    /// <summary>Whether anything is asked: something typed in the box, or the page's conditions holding something.</summary>
    private bool Asks => IsPanel ? _asked.IsFiltering : HasText;

    /// <summary>The name asked for.</summary>
    private string Typed => (IsPanel ? _asked.Name : Text).Trim();

    /// <summary>The page's conditions changed, or the page was come to: its finds are found again, a moment after.</summary>
    public void Refresh()
    {
        _soon.Stop();
        if (!Asks)
        {
            _round++;
            Show([]);
            return;
        }

        _soon.Start();
    }

    /// <summary>
    /// What the empty box says: "Search" - and while a character is being opened, whose: "Opening
    /// Finder...", in the window's top row whatever page is up, from the moment before the load takes the window
    /// to the moment it is done. Home's own button says it too, where that is in sight.
    /// </summary>
    public string BoxHint => _main.Home is { IsLoading: true, OpeningName: { Length: > 0 } name } ? L($"Opening {name}...") : L("Search");

    /// <summary>The home page began or ended opening a character: the box's hint follows (told by the window's model, which has both).</summary>
    public void SayLoading() => OnPropertyChanged(nameof(BoxHint));

    partial void OnTextChanged(string value)
    {
        if (IsPanel)
            return;

        _asked.Name = value;
        _soon.Stop();

        if (!HasText)
        {
            _round++;
            Show([]);
            IsListOpen = false;
            return;
        }

        _soon.Start();
    }

    /// <summary>Ctrl + F from anywhere: the caret in the box.</summary>
    public void Focus() => FocusRequested?.Invoke();

    /// <summary>
    /// The box's search ended - a find gone to, Esc, the cross, a click elsewhere: its list put away and the
    /// box emptied, the mainstream behavior. Nothing of it is left
    /// to come back to; typing again is a new search.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        StopAsking();
        IsListOpen = false;
        Text = "";
        Ended?.Invoke();
    }

    /// <summary>The search ended (<see cref="Close"/>): the box lets the keyboard go, if it still has it.</summary>
    public event Action? Ended;

    // ---- the finds --------------------------------------------------------------------------------------

    /// <summary>
    /// The search run: the open character's finds and the vault's at once, the grail's after them, then the
    /// other characters' one save at a time, the window answering in between - each save is read once and kept
    /// until its file changes (<see cref="SavedItems"/>). A search typed over is dropped where it stands.
    /// </summary>
    private async System.Threading.Tasks.Task RunAsync()
    {
        int round = ++_round;
        if (!Asks)
            return;

        var session = _main.Session;
        var groups = new List<Group>();
        string typed = Typed;
        var rank = FindRank.For(typed);

        // While the game runs, no items, only the grail and the settings: the saves are the game's to write just then and nothing of a character's or
        // the vault's is read for a search - the list says so at its top. The sections are found too:
        // going to a heading reads nothing and moves only the view.
        bool game = _main.Sync?.IsGameRunning == true;
        _note = game ? L("Game running: no items searched") : "";

        try
        {
            if (!game && session.IsLoaded && _main.Inventory is { } inventory && inventory.FindsFor(_mine, _results) is { Count: > 0 } mine)
            {
                int found = mine.Sum(place => place.Found);
                groups.Add(IsPanel
                    ? new Group(MineFold, session.CharacterName, found, [.. mine.Select(place => new Part(place, place.Items))])
                    : new Group(MineFold, session.CharacterName, found, [Best(MineFold, typed, found, rank, MostMine,
                        mine.SelectMany(place => place.Items.Select(item =>
                            new FoundItem(item.Id, item.Tile, item.Detail, place.Name, inVault: false, item.Jump))))]));
            }

            if (!game && _main.Vault is { } vault)
            {
                if (IsPanel)
                {
                    // The Search page lists as many of the vault's as the list of finds always did.
                    var (inVault, found) = vault.FindsFor(_asked.Query(), SearchResultsViewModel.MostListed, _results);
                    if (found > 0)
                        groups.Add(new Group(VaultFold, L("Vault"), found, [.. inVault.Select(place => new Part(place, place.Items))]));
                }
                else
                {
                    var (best, found) = vault.BestFor(_asked.Query(), rank, MostVault);
                    if (found > 0)
                        groups.Add(new Group(VaultFold, L("Vault"), found, [new Part(null, best, SeeAllItems(VaultFold, typed, found, best.Count))]));
                }
            }

            // The Search page asks of items, the open character's and the vault's, and no more:
            // the grail, the settings, the sections and the
            // other characters are the window's box's.
            if (!IsPanel)
            {
                // The vault's folders by their names, under its items: places, not items -
                // found while the game runs too, as the sections are.
                if (_main.Vault is { } pages && FolderFinds(pages, typed, rank) is { } folders)
                    groups.Add(folders);

                if (GrailFinds(typed, rank) is { Count: > 0 } grail)
                    groups.Add(new Group(GrailFold, L("Compendium"), grail.Sum(part => part.Place?.Found ?? 0), grail));

                groups.AddRange(TargetFinds(typed, rank));
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }

        Show(groups);
        IsListOpen = true;
        if (game || IsPanel)
            return;

        // The other characters trail in - over the grail's, which stays last. Their group is put into the list
        // when all of them have answered, or every so often while they are still being read: the list was made
        // again at each character's arrival - twelve times with thirteen on disk, a third of a run, and for
        // that long it moved under the pointer (a trace). Each save is read once and its index
        // kept (SavedSearches), so but for the first search they all answer within the one pause.
        var others = new List<Part>();
        var sinceShown = System.Diagnostics.Stopwatch.StartNew();
        bool unshown = false;
        foreach (var card in _main.Home.Characters.ToList())
        {
            if (session.IsLoaded && string.Equals(card.Path, session.SavePath, StringComparison.OrdinalIgnoreCase))
                continue;

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (round != _round)
                return;

            var theirs = FindsOn(card.Path, card.Name, rank);
            if (theirs is null)
                continue;

            others.Add(new Part(theirs, theirs.Items));

            var group = new Group(OthersFold, L("Other characters"), others.Sum(part => part.Place?.Found ?? 0), [.. others]);
            int at = groups.FindIndex(each => each.Fold == OthersFold);
            if (at >= 0)
                groups[at] = group;
            else
                groups.Insert(groups.FindIndex(each => each.Fold is FoldersFold or GrailFold or SettingsFold or SectionsFold) is >= 0 and var before ? before : groups.Count, group);

            unshown = true;
            if (sinceShown.ElapsedMilliseconds >= OthersShownEvery)
            {
                Show(groups);
                unshown = false;
                sinceShown.Restart();
            }
        }

        if (unshown && round == _round)
            Show(groups);
    }

    /// <summary>While the other characters are still being read, their group is shown no more often than this, in milliseconds.</summary>
    private const int OthersShownEvery = 200;

    /// <summary>
    /// The other characters' saves read and their indexes written before anything is searched for - one
    /// character each time the window is idle, from start-up on (App): the first search of a session did all
    /// of it at once, most of a second with thirteen characters before its list answered (measured:
    /// the saves were 50 ms of it, the indexes 400). Not while the game runs - its saves are
    /// not read then - and given up as soon as something is typed: the search does what is left itself.
    /// </summary>
    public async System.Threading.Tasks.Task PrepareOthers()
    {
        if (IsPanel)
            return;

        try
        {
            foreach (var card in _main.Home.Characters.ToList())
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (HasText || _main.Sync?.IsGameRunning == true)
                    return;
                if (_main.Session.IsLoaded && string.Equals(card.Path, _main.Session.SavePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (SavedItems.Of(card.Path, Restore) is { } held)
                    {
                        // The index writes each item as its own save's layout has it.
                        SaveVerifier.Install(held.Format);
                        _theirs.Prepare(card.Path, held);
                    }
                }
                finally
                {
                    Restore();
                }
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }

        void Restore()
        {
            if (_main.Session.IsLoaded)
                _main.Session.UseMyLayout();
        }
    }

    /// <summary>The other characters' indexes, written how many times: what a test counts.</summary>
    public int OthersWritten => _theirs.Written;

    /// <summary>
    /// The few of a group's finds the box lists - the ones that answer what was typed best, in the order they
    /// were found in among equals - and, where there are more, the line that goes to them all.
    /// </summary>
    private Part Best(string fold, string typed, int found, Func<string, string?, int> rank, int most, IEnumerable<FoundItem> all)
    {
        var best = all.OrderBy(item => rank(item.Name, item.Tile.View.BaseName)).Take(most).ToList();
        return new Part(null, best, SeeAllItems(fold, typed, found, best.Count));
    }

    /// <summary>
    /// "See all 36 results", under a group of items that found more than it lists; null where it lists them all.
    /// The line says how many and not where to ("See all 36 in Search" read as a search
    /// within the search) - as a shop's or a mail program's short list ends; it stands under its own group.
    /// </summary>
    private FoundSeeAll? SeeAllItems(string fold, string typed, int found, int listed) =>
        found > listed ? new FoundSeeAll(("all", fold), SeeAllText(found), () => ToSearchPage(typed, caret: false)) : null;

    /// <summary>The line's words, the same under the items and under a grail tab: one phrase to translate.</summary>
    private static string SeeAllText(int found) => L($"See all {found} results");

    /// <summary>
    /// One other character's finds, read off its save - the few that answer best: each row asks before it
    /// loads the character. No line to the rest: they are another character's, and the Search page is the
    /// open one's.
    /// </summary>
    private FoundPlace? FindsOn(string path, string name, Func<string, string?, int> rank)
    {
        void Restore()
        {
            if (_main.Session.IsLoaded)
                _main.Session.UseMyLayout();
        }

        try
        {
            if (SavedItems.Of(path, Restore) is not { } held)
                return null;

            // The index writes each item as its own save's layout has it.
            SaveVerifier.Install(held.Format);
            var found = _theirs.Matching(path, held) ?? [];
            if (found.Count == 0)
                return null;

            var items = new List<FoundItem>();
            foreach (var item in held.All.Where(found.Contains).Take(MostWeighed)
                         .Select(item => (Item: item, View: ItemPresenter.Describe(item)))
                         .OrderBy(each => rank(each.View.Name, each.View.BaseName))
                         .Take(MostOther)
                         .Select(each => each.Item))
            {
                var tile = new ItemTileViewModel(item, 0);
                string detail = tile.View.BaseName is { Length: > 0 } named && !named.Equals(tile.Name, StringComparison.Ordinal) ? named : "";
                items.Add(new FoundItem(item, tile, detail, SavedItems.PlaceOf(held, item), inVault: false, () => { })
                {
                    OwnerPath = path,
                    Owner = name,
                    Accept = AcceptCommand,
                    Decline = DeclineCommand,
                });
            }

            return new FoundPlace(name, found.Count, items);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return null;
        }
        finally
        {
            Restore();
        }
    }

    // ---- the grail ----------------------------------------------------------------------------------------

    private sealed record GrailLine(string Kind, string Name, string Base, string Group, Item Item, string Text);

    private List<GrailLine>? _grail;
    private int _grailOf = -1;

    /// <summary>
    /// The grail's entries, each with what it says as an item (<see cref="ItemLines.TextOf(Item)"/>): the
    /// grail follows the item search, so an entry is found by its name, its base and
    /// its stats as an item is - and as the grail's own box finds it (<see cref="GrailCard.Answers"/>). Worded
    /// once for the tables in use.
    /// </summary>
    private List<GrailLine> Grail()
    {
        if (_grail is not null && _grailOf == InstalledTables.Generation)
            return _grail;

        var lines = new List<GrailLine>();

        void Add(string kind, string group, Item item)
        {
            try
            {
                var view = ItemPresenter.Describe(item);
                lines.Add(new GrailLine(kind, view.Name, view.BaseName ?? "", group, item, ItemLines.TextOf(item)));
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
            }
        }

        // One a name, as the Grail page's cards are.
        foreach (var card in GrailCatalog.UniqueCards())
            Add(GrailViewModel.Uniques, L("Uniques"), card.Rows[0].Item);
        foreach (var entry in GrailCatalog.Sets())
            Add(GrailViewModel.Sets, L("Sets"), entry.Item);

        _grailOf = InstalledTables.Generation;
        return _grail = lines;
    }

    /// <summary>
    /// The grail's finds, tab by tab - Uniques, Sets: the few of each that answer best, and under a
    /// tab that found more the line to that tab with what was typed in the grail's own box, which lists them all
    /// (the box may find on several tabs at once, so each tab has its own way in).
    /// </summary>
    private List<Part> GrailFinds(string typed, Func<string, string?, int> rank)
    {
        string? pattern = SearchPattern.Of(typed);
        var parts = new List<Part>();

        foreach (var tab in Grail().Where(line => line.Text.Contains(typed, StringComparison.OrdinalIgnoreCase)
                                                  || SearchPattern.Matches(pattern, line.Text))
                                   .GroupBy(line => line.Kind))
        {
            var items = new List<FoundItem>();
            foreach (var line in tab.OrderBy(line => rank(line.Name, line.Base)).Take(MostTab))
            {
                var entry = line;
                var tile = new ItemTileViewModel(entry.Item, 0);
                string detail = tile.View.BaseName is { Length: > 0 } named && !named.Equals(tile.Name, StringComparison.Ordinal) ? named : "";
                items.Add(new FoundItem(entry, tile, detail, "", inVault: false,
                                        () => _main.ShowGrailAt(entry.Kind, entry.Name)));
            }

            string kind = tab.Key;
            int count = tab.Count();
            parts.Add(new Part(new FoundPlace(tab.First().Group, count, items), items,
                               count > items.Count
                                   ? new FoundSeeAll(("all", GrailFold, kind), SeeAllText(count),
                                                     () => _main.ShowGrailAt(kind, typed))
                                   : null));
        }

        return parts;
    }

    // ---- settings and sections ----------------------------------------------------------------------------

    /// <summary>
    /// The settings and the sections what is typed is found in the name of - a phrase, or a pattern: the few
    /// of each that answer best. They are places, not items, and have no page that lists the rest: a last
    /// line only says how many more there are.
    /// </summary>
    private IEnumerable<Group> TargetFinds(string typed, Func<string, string?, int> rank)
    {
        if (typed.Length == 0 || (Targets ?? _main.QuickSearch?.Targets)?.Invoke() is not { Count: > 0 } all)
            yield break;

        string? pattern = SearchPattern.Of(typed);
        var found = all.Where(target => target.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)
                                        || SearchPattern.Matches(pattern, target.Name)).ToList();

        Group? Of(string fold, string name, bool settings)
        {
            var mine = found.Where(target => target.IsSetting == settings).ToList();
            if (mine.Count == 0)
                return null;

            var best = mine.OrderBy(target => rank(target.Name, null)).Take(MostTargets).Select(target => new FoundTarget(target)).ToList();
            int more = mine.Count - best.Count;
            return new Group(fold, name, mine.Count,
                             [new Part(null, best, more > 0 ? new FoundMore(Plural(more, "1 more", $"{more} more")) : null)]);
        }

        if (Of(SettingsFold, L("Settings"), settings: true) is { } settings)
            yield return settings;
        if (Of(SectionsFold, L("Sections"), settings: false) is { } sections)
            yield return sections;
    }

    /// <summary>
    /// The vault's folders whose names answer what was typed, as a group of the box's list: a row each - the
    /// folder's name, and the folder it lies in - that goes to it (MainViewModel.GoToVaultFolder). A few, the best
    /// first, as the settings and the sections are listed.
    /// </summary>
    private Group? FolderFinds(VP_ViewModel_Vault_L vault, string typed, Func<string, string?, int> rank)
    {
        if (typed.Length == 0)
            return null;

        string? pattern = SearchPattern.Of(typed);
        var found = vault.FoldersNamed(name => name.Contains(typed, StringComparison.OrdinalIgnoreCase) || SearchPattern.Matches(pattern, name));
        if (found.Count == 0)
            return null;

        var best = found.OrderBy(folder => rank(folder.Name, null)).Take(MostTargets)
            .Select(folder => new FoundTarget(new SearchTarget(false, folder.Name, folder.Where, () => _main.GoToVaultFolder(folder.Id))))
            .ToList();
        int more = found.Count - best.Count;
        return new Group(FoldersFold, L("Folders"), found.Count,
                         [new Part(null, best, more > 0 ? new FoundMore(Plural(more, "1 more", $"{more} more")) : null)]);
    }

    // ---- the list ---------------------------------------------------------------------------------------

    /// <summary>
    /// The finds put in the list: a group's heading, then - unless it is folded - its finds, under their
    /// places where they have them. The row marked is kept where it is still among them; in the box's list
    /// the first find is marked where none is, so Enter has somewhere to go.
    /// </summary>
    private void Show(List<Group> groups)
    {
        _groups = groups;
        object? at = _current?.Id;
        var rows = new List<object>();
        IFound? current = null;

        if (_note.Length > 0 && Asks)
            rows.Add(new FoundMore(_note));

        void Add(IFound row)
        {
            rows.Add(row);
            if (at is not null && Equals(row.Id, at))
                current = row;
        }

        foreach (var (fold, name, found, parts) in groups)
        {
            var group = new FoundGroup(fold, name, found, IsOpen(fold), FoldCommand);
            rows.Add(group);
            if (!group.IsOpen)
                continue;

            int listed = 0;
            foreach (var part in parts)
            {
                if (part.Place is { Name.Length: > 0 } place)
                    rows.Add(place);

                foreach (var find in part.Finds)
                {
                    listed++;
                    Add(find);
                }

                if (part.Last is IFound way)
                    Add(way);
                else if (part.Last is not null)
                    rows.Add(part.Last);
            }

            // The Search page lists its finds whole, up to what a list can hold.
            if (IsPanel && found > listed)
                rows.Add(new FoundMore(Plural(found - listed, "1 more", $"{found - listed} more")));
        }

        // The Search page's list is found again whenever a page reads its finds - a find gone to reads the
        // vault, every move the inventory. The same finds in the same places: it stays as it is, where it was
        // scrolled to, the row marked still marked.
        if (!(IsPanel && rows.Count == Rows.Count && rows.Zip(Rows).All(pair => Same(pair.First, pair.Second))))
        {
            Rows.Clear();
            foreach (object row in rows)
                Rows.Add(row);

            _current = current;
            if (current is not null)
                current.IsCurrent = true;
        }

        var finds = Finds;
        if (!IsPanel && _current is null && finds.Count > 0)
        {
            _current = finds[0];
            _current.IsCurrent = true;
        }

        // A folded group's finds are found all the same: its heading says how many.
        int count = finds.Count(find => find is not FoundSeeAll) + groups.Where(group => !IsOpen(group.Fold)).Sum(group => group.Found);
        bool any = finds.Count > 0 || groups.Any(group => group.Found > 0);
        CanStep = finds.Count > 0;
        HasFinds = any;
        FoundNothing = Asks && !any;
        FoundTitle = Asks ? Plural(count, "1 found", $"{count} found") : "";
    }

    /// <summary>Whether a row of the list as it was is the row it would be now: the same find, saying the same.</summary>
    private static bool Same(object one, object other) => (one, other) switch
    {
        (FoundGroup a, FoundGroup b) => a.Fold == b.Fold && a.Name == b.Name && a.Found == b.Found && a.IsOpen == b.IsOpen,
        (FoundPlace a, FoundPlace b) => a.Name == b.Name && a.Found == b.Found,
        (FoundItem a, FoundItem b) => Equals(a.Id, b.Id) && a.Name == b.Name && a.Detail == b.Detail && a.Where == b.Where,
        (FoundMore a, FoundMore b) => a.Text == b.Text,
        _ => false,
    };

    // ---- marking a find, and going to it ----------------------------------------------------------------

    /// <summary>A row clicked.</summary>
    [RelayCommand]
    private void Choose(IFound? row)
    {
        if (row is null)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        GoTo(row);
    }

    /// <summary>F3 on the Search page: the next of its finds gone to, round from the last to the first.</summary>
    [RelayCommand(CanExecute = nameof(CanStep))]
    private void Next()
    {
        if (Stepped(+1) is { } find)
            GoTo(find);
    }

    /// <summary>Shift + F3 on the Search page: the find before.</summary>
    [RelayCommand(CanExecute = nameof(CanStep))]
    private void Previous()
    {
        if (Stepped(-1) is { } find)
            GoTo(find);
    }

    /// <summary>The find <paramref name="by"/> after the one marked, round the list's ends; the first, or the last, where none is.</summary>
    private IFound? Stepped(int by)
    {
        var finds = Finds;
        if (finds.Count == 0)
            return null;

        int at = _current is null ? (by > 0 ? -1 : 0) : finds.IndexOf(_current);
        return finds[((at + by) % finds.Count + finds.Count) % finds.Count];
    }

    /// <summary>Down and Up in the box: the mark moved a row on or back, round the list's ends. Nothing is gone to.</summary>
    public void MoveMark(int by)
    {
        if (Stepped(by) is not { } find)
            return;

        StopAsking();
        Mark(find);
    }

    /// <summary>
    /// Enter in the box: the row marked is gone to - the first, where none was marked - and a row that asks
    /// whether to load its character is answered yes. Typed and entered before the list came, the search is
    /// run first: Enter does not wait on the pause after the last letter.
    /// </summary>
    public void GoMarked()
    {
        if (_soon.IsEnabled)
        {
            _soon.Stop();
            _ = RunAsync();
        }

        if ((_current ?? Finds.FirstOrDefault()) is not { } row)
            return;

        if (row is FoundItem { IsAsking: true } asking)
            AcceptCommand.Execute(asking);
        else
            ChooseCommand.Execute(row);
    }

    private void Mark(IFound found)
    {
        if (_current is not null)
            _current.IsCurrent = false;

        _current = found;
        found.IsCurrent = true;
        Marked?.Invoke(found);
    }

    /// <summary>
    /// A find gone to: its page shown, itself selected - and the box's search ended, its list put away and the
    /// box emptied. One on another character is not gone to: its row asks, the list in sight for it. On the
    /// Search page the find is handed to the page, which stays as it is.
    /// </summary>
    private void GoTo(IFound found)
    {
        StopAsking();
        Mark(found);

        if (found is FoundItem { OwnerPath: not null } elsewhere)
        {
            elsewhere.IsAsking = true;
            IsListOpen = true;
            return;
        }

        if (IsPanel)
        {
            Picked?.Invoke(found);
            return;
        }

        Close();
        JumpTo(found);
    }

    /// <summary>The find's page shown and the find brought into sight: an item selected, the grail at an entry, a setting's row, a section - or the page that lists a group whole.</summary>
    public void JumpTo(IFound found)
    {
        try
        {
            if (found is FoundTarget target)
                target.Jump();
            else if (found is FoundSeeAll all)
                all.Go();
            else if (found is FoundItem { Id: GrailLine } entry)
                entry.Jump();
            else if (found is FoundItem { InVault: true } stored && !_main.Session.IsLoaded)
                LoadForVault(stored);
            else if (found is FoundItem row)
                _main.GoToFind(row, toEdit: !IsPanel);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }
    }

    /// <summary>
    /// The character the Items page is opened with where none is open and something of the vault's is asked
    /// for: the highlighted character card in the selection list, or else the first character.
    /// <para>
    /// A vault is one mode's, softcore or hardcore, and loading a character of the other mode shows the other
    /// vault: so of the vault that was searched, the highlighted card where it is of that mode, else the
    /// first character that is. With none of that mode the rule is followed as given.
    /// </para>
    /// </summary>
    private CharacterCard? CardForVault()
    {
        var cards = _main.Home.Characters.ToList();
        bool hardcore = _vaults.IsHardcore;
        return _main.Home.Selected is { } picked && picked.Save.IsHardcore == hardcore ? picked
            : cards.FirstOrDefault(each => each.Save.IsHardcore == hardcore) ?? _main.Home.Selected ?? cards.FirstOrDefault();
    }

    /// <summary>
    /// A find of the vault's gone to with no character open: the Items page is a character's, so one is loaded
    /// for it (<see cref="CardForVault"/>) and the find gone to. Where the character loaded is of the other
    /// mode, the find is not in the vault then shown, and that is said.
    /// </summary>
    private void LoadForVault(FoundItem stored)
    {
        if (CardForVault() is not { } card)
        {
            _main.Session.Status = L("Require open character.");
            return;
        }

        bool hardcore = _vaults.IsHardcore;
        _main.Home.SwitchTo(card.Path, () =>
        {
            if (_vaults.IsHardcore != hardcore)
            {
                _main.Session.Status = L("Item not in this vault.");
                return;
            }

            _main.GoToFind(stored, toEdit: true);
        });
    }

    /// <summary>How many the Search page's conditions found: the line beside its title.</summary>
    [ObservableProperty] private string _foundTitle = "";

    private void StopAsking()
    {
        foreach (var row in Rows.OfType<FoundItem>())
            row.IsAsking = false;
    }

    /// <summary>The cross on a row that asks: the row as it was.</summary>
    [RelayCommand]
    private void Decline(FoundItem? row)
    {
        if (row is not null)
            row.IsAsking = false;
    }

    /// <summary>
    /// The tick on a row that asks: the box's search ended, its character loaded in place of the one open, and
    /// the item gone to - found among the loaded character's by its id and base.
    /// </summary>
    [RelayCommand]
    private void Accept(FoundItem? row)
    {
        if (row?.OwnerPath is not { } path || row.Id is not Item wanted)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        uint id = wanted.Id;
        string code = wanted.Code;
        bool compact = wanted.IsCompact;
        Close();

        _main.Home.SwitchTo(path, () =>
        {
            if (_main.Inventory?.FindOf(id, code, compact) is { } again)
                _main.GoToFind(again, toEdit: true);
        });
    }

    // ---- to the Search page -----------------------------------------------------------------------------

    /// <summary>
    /// Ctrl + Enter in the box, carrying over the text: the Search page, the caret in its keyword field. What is typed becomes its conditions,
    /// those set before replaced; with nothing typed the page is shown as it stands.
    /// </summary>
    [RelayCommand]
    private void ToList()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        string typed = Text.Trim();
        Close();
        ToSearchPage(typed, caret: true);
    }

    /// <summary>
    /// The Search page shown with <paramref name="typed"/> as its conditions - an item group's "See all results",
    /// or Ctrl + Enter, which puts the caret in its keyword field besides. With no character open and
    /// something typed, one is loaded for it as a find of the vault's has one loaded (<see cref="CardForVault"/>).
    /// </summary>
    private void ToSearchPage(string typed, bool caret)
    {
        void Ask(SearchWindowViewModel search)
        {
            if (caret)
                search.Open(typed);
            else
                search.SendText(typed);
        }

        if (_main.Session.IsLoaded || typed.Length == 0 || CardForVault() is not { } card)
        {
            _main.ShowSearch(Ask);
            return;
        }

        _main.Home.SwitchTo(card.Path, () => _main.ShowSearch(Ask));
    }
}
