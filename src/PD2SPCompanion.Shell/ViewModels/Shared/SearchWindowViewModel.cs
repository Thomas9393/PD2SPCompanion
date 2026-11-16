using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using System;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The item search, on the Items page's right page under its Search bookmark: conditions on top, what they find under
/// them. It takes the place of a page - as Edit, the Grail and the Forge do - where it was a panel over the
/// window, then a floating one, then a dock at the window's right that took width no page could spare.
/// <para>
/// <b>It searches items</b> - the open character's and the vault's - and nothing else: settings, sections, the
/// grail and the other characters are the window's own search box's, which is a search apart. That one is
/// quick and gone with its list; this one is the long list that stays, and the box
/// hands over to it - an item group's "See all results", Ctrl + Enter.
/// </para>
/// <para>
/// <b>The conditions</b> - the form: the text, type, rarity, the property rows, the presets
/// (<see cref="Panel"/>) - are the search: filling them is searching. <b>The results</b>
/// (<see cref="Finds"/>): a find clicked is gone to - the left page shows its place, the item selected - and
/// this page stays.
/// </para>
/// <para>
/// <b>The pages mark the finds only while this page is the right page</b>. Left for another bookmark, what it asks is put aside
/// (<see cref="Leave"/>) and nothing is marked - its bookmark says it keeps something (<see cref="Keeps"/>);
/// come back to, it asks again (<see cref="Enter"/>).
/// </para>
/// </summary>
public sealed partial class SearchWindowViewModel : ObservableObject
{
    private readonly VaultService _vaults;
    private readonly ISFXManager? _sfxManager;

    /// <param name="panel">The form over the standing search, with the finds' tools.</param>
    public SearchWindowViewModel(MainViewModel main, VaultService vaults, SearchPanelViewModel panel, ISFXManager? sfxManager)
    {
        _vaults = vaults;
        _sfxManager = sfxManager;
        Panel = panel;

        Finds = new QuickSearchViewModel(main, vaults, sfxManager, vaults.Filter, panel.Results);
        Finds.Picked += Pick;
        Finds.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(QuickSearchViewModel.FoundTitle))
                OnPropertyChanged(nameof(Found));
            if (changed.PropertyName == nameof(QuickSearchViewModel.HasFinds))
                OnPropertyChanged(nameof(HasResults));
        };

        vaults.Filter.Changed += Said;

        // A page read its finds again - an item moved, another character opened: found again.
        Results.FindsChanged += Finds.Refresh;
    }

    /// <summary>The form and its presets, over the standing search.</summary>
    public SearchPanelViewModel Panel { get; }

    /// <summary>The finds' tools - Select found, Move found to folder.</summary>
    public SearchResultsViewModel Results => Panel.Results;

    /// <summary>What the search finds, live: the rows of the results.</summary>
    public QuickSearchViewModel Finds { get; }

    private const string Fold = "Search_Conditions";

    /// <summary>The conditions are unfolded: folded, the results have the page.</summary>
    [ObservableProperty] private bool _showsConditions = FoldMemory.IsOpen(Fold);

    partial void OnShowsConditionsChanged(bool value) => FoldMemory.Keep(Fold, value);

    private const string ResultsFold = "Search_Results";

    /// <summary>The results are unfolded: folded, their heading alone stands under the conditions, which have the page.</summary>
    [ObservableProperty] private bool _showsResults = FoldMemory.IsOpen(ResultsFold);

    partial void OnShowsResultsChanged(bool value) => FoldMemory.Keep(ResultsFold, value);

    /// <summary>The results' heading: folded or unfolded.</summary>
    [RelayCommand]
    private void ToggleResults()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        ShowsResults = !ShowsResults;
    }

    /// <summary>
    /// There are finds to list: the page is then split, conditions over results. With none - nothing asked,
    /// or nothing found - the conditions have the whole page.
    /// </summary>
    public bool HasResults => Finds.HasFinds;

    /// <summary>Something is asked.</summary>
    public bool Asks => _vaults.Filter.IsFiltering;

    /// <summary>How many the search finds, beside the page's title: "33 found". Nothing while nothing is asked.</summary>
    public string Found => Asks ? Finds.FoundTitle : "";

    private void Said()
    {
        OnPropertyChanged(nameof(Asks));
        OnPropertyChanged(nameof(Found));
    }

    // ---- on the right page, and off it ---------------------------------------------------------------------

    /// <summary>The Search bookmark is the right page: the pages mark what it finds.</summary>
    public bool IsShown { get; private set; }

    /// <summary>What it asked when it was left, to ask again when it is come back to.</summary>
    private SearchPreset? Kept
    {
        get => _kept;
        set
        {
            if (ReferenceEquals(_kept, value))
                return;

            _kept = value;
            OnPropertyChanged(nameof(Keeps));
            OnPropertyChanged(nameof(KeptSummary));
        }
    }

    private SearchPreset? _kept;

    /// <summary>
    /// The page was left with conditions set, and keeps them for when it is come back to: its bookmark is
    /// marked, as the Forge's is while it holds an item.
    /// </summary>
    public bool Keeps => _kept is not null;

    /// <summary>What it keeps, in a line: the bookmark's second line.</summary>
    public string KeptSummary => _kept is { } kept ? _vaults.Filter.Describe(kept) : "";

    /// <summary>
    /// The Search bookmark taken: what it asked when it was left is asked again. A search that stands already -
    /// a duplicate badge's, which brings this page up for its copies - is the search, and what was kept is the
    /// previous search, to go back to by its row.
    /// </summary>
    public void Enter()
    {
        IsShown = true;
        var kept = Kept;
        Kept = null;

        if (kept is not null && !Asks)
        {
            _vaults.Filter.Apply(kept);
            return;
        }

        if (kept is not null)
            Panel.KeepPrevious(kept);

        Finds.Refresh();
    }

    /// <summary>
    /// Another bookmark taken: what is asked is kept here and the search itself emptied, so no page is marked
    /// by a search that is not in sight. What was selected stays selected - a find gone to is then the item on
    /// the Edit page. A duplicate badge's search is not a preset's to hold, and ends with the page.
    /// </summary>
    public void Leave()
    {
        IsShown = false;
        if (!Asks)
            return;

        var asked = _vaults.Filter.Snapshot();
        Kept = asked.AsksTheSameAs(new SearchPreset()) ? null : asked;
        _vaults.Filter.Empty();
    }

    // ---- asked from the window's search box ---------------------------------------------------------------

    /// <summary>The conditions' keyword field is asked to take the caret: the view's to do.</summary>
    public event Action? KeywordWanted;

    /// <summary>
    /// Ctrl + Enter in the window's search box: the conditions unfolded and the caret put in their keyword
    /// field. What was typed there becomes the search (<see cref="SendText"/>); with nothing typed the
    /// conditions stay as they stand.
    /// </summary>
    public void Open(string? text = null)
    {
        ShowsConditions = true;
        if (text?.Trim() is { Length: > 0 } typed)
            SendText(typed);

        KeywordWanted?.Invoke();
    }

    /// <summary>
    /// What was typed in the window's search box becomes the search and nothing else is asked - a group's
    /// "See all results" there, or Ctrl + Enter. The conditions it replaces are the previous search.
    /// </summary>
    public void SendText(string text) =>
        Panel.Replace(() =>
        {
            _vaults.Filter.Clear();
            _vaults.Filter.Name = text;
        });

    /// <summary>F3 and Shift + F3 while the page is up: the next of its finds gone to, or the one before, round the list's ends.</summary>
    public void Step(int by)
    {
        var step = by < 0 ? Finds.PreviousCommand : Finds.NextCommand;
        if (step.CanExecute(null))
            step.Execute(null);
    }

    /// <summary>The conditions' heading: folded or unfolded.</summary>
    [RelayCommand]
    private void ToggleConditions()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        ShowsConditions = !ShowsConditions;
    }

    /// <summary>The search ended: nothing asked, here or kept - nor kept as the previous search.</summary>
    public void End()
    {
        Kept = null;
        _vaults.Filter.Clear();
        Panel.ForgetPrevious();
    }

    /// <summary>A find clicked: gone to, the page left as it is.</summary>
    private void Pick(IFound found) => Finds.JumpTo(found);
}
