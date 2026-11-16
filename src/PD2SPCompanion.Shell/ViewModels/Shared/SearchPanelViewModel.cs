using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;


using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One kept search in the presets' list.</summary>
public sealed class SearchPresetRow(SearchPreset preset, string summary) : PresetRowViewModel(preset.Name, summary)
{
    public SearchPreset Preset { get; } = preset;
}

/// <summary>
/// The first row of the presets' list while there is one: the conditions that were last
/// replaced whole - by what was sent from the window's search box, by a preset, by a duplicate badge, by the
/// brush. Clicked, it and the conditions that stand change places. One slot, this session's: never written.
/// </summary>
public sealed class PreviousSearchRow(SearchPreset preset, string summary) : PresetRowViewModel(L("Previous search"), summary)
{
    public SearchPreset Preset { get; } = preset;

    public override bool CanForget => false;
}

/// <summary>
/// The one search, opened from the search button under the Inventory's and the Vault's headers:
/// an overlay on the page that
/// opened it, the same contents from either, since it is one <see cref="VaultFilter"/> both pages
/// obey. Which page has it open is the page's own; what it holds is this.
/// <para>
/// <b>Presets</b> are the map seed card's logic on searches, in the panel
/// (the shared preset section, as the drop panel's): a kept preset clicked is the search
/// at once - no chosen preset, no apply. The name box keeps the search as it stands, renames the preset it already
/// is, or updates the preset of that name. Keeping, renaming and forgetting are written to the settings as they
/// are made, as seeds are.
/// </para>
/// </summary>
public sealed partial class SearchPanelViewModel : ObservableObject, IPresetSection
{
    private readonly ISFXManager? _sfxManager;

    /// <summary>Used by the designer, which gets no services.</summary>
    public SearchPanelViewModel()
        : this(null, new VaultService(new Services.Character.CharacterSession(), new EditorSettings()), new EditorSettings()) { }

    public SearchPanelViewModel(ISFXManager? sfxManager, VaultService vaults, EditorSettings settings)
        : this(sfxManager, vaults, settings, vaults.Filter) { }

    /// <param name="filter">The search the form edits: the standing search.</param>
    public SearchPanelViewModel(ISFXManager? sfxManager, VaultService vaults, EditorSettings settings, VaultFilter filter)
    {
        _sfxManager = sfxManager;
        Filter = filter;
        Form = new SearchForm(filter, sfxManager, inVault: true);
        Results = new SearchResultsViewModel(filter, sfxManager);
        Settings = settings;

        Reload();

        // What the preset row says follows the search, whoever changed it.
        Filter.Changed += () =>
        {
            SayCurrent();
            MarkCurrentRow();
        };
    }

    /// <summary>What the panel edits, and the vault list and the inventory obey.</summary>
    public VaultFilter Filter { get; }

    /// <summary>The form over it, with its property picker.</summary>
    public SearchForm Form { get; }

    /// <summary>What it found, listed in a panel floating over the pages while the form is shut.</summary>
    public SearchResultsViewModel Results { get; }

    public EditorSettings Settings { get; }

    /// <summary>Every picker back to Any, the name box emptied.</summary>
    [RelayCommand]
    private void ClearSearch()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Replace(Filter.Clear);
    }

    // ---- the previous search ------------------------------------------------------------------
    // Conditions take a while to set and are replaced whole by one click - a preset, the brush, a word sent
    // from the window's search box, a duplicate badge. What was replaced is kept in one slot, shown as the
    // presets' first row, and clicked it changes places with what stands: an undo of one step, and a way to
    // go between two searches. (A de facto last preset - one slot, not a history.)

    private SearchPreset? _previous;

    /// <summary>Whether <paramref name="asked"/> asks for anything a preset can hold.</summary>
    private static bool AsksAnything(SearchPreset asked) => !asked.AsksTheSameAs(new SearchPreset());

    /// <summary>
    /// The conditions replaced whole by <paramref name="change"/>: what they were is kept as the previous
    /// search, where they asked anything and the change left something else.
    /// </summary>
    public void Replace(Action change)
    {
        var before = Filter.Snapshot();
        change();
        if (!before.AsksTheSameAs(Filter.Snapshot()))
            KeepPrevious(before);
    }

    /// <summary>Conditions that were replaced, kept as the previous search. Ones that ask nothing are not kept: the slot stays as it is.</summary>
    public void KeepPrevious(SearchPreset replaced)
    {
        if (!AsksAnything(replaced))
            return;

        _previous = replaced;
        ListRows();
    }

    /// <summary>Nothing kept as the previous search.</summary>
    public void ForgetPrevious()
    {
        if (_previous is null)
            return;

        _previous = null;
        ListRows();
    }

    /// <summary>The previous search's row clicked: it becomes the search, and what stood is the previous one - none, where nothing stood.</summary>
    private void SwapPrevious()
    {
        if (_previous is not { } back)
            return;

        var stood = Filter.Snapshot();
        _previous = AsksAnything(stood) ? stood : null;
        Filter.Apply(back);
        NameText = Matching()?.Name ?? "";
        ListRows();
    }

    // ---- presets ------------------------------------------------------------------------------

    public ObservableCollection<SearchPresetRow> Saved { get; } = [];

    /// <summary>None: the presets are listed by name.</summary>
    public SortChoice? Order => null;

    public bool HasSaved => Saved.Count > 0;

    /// <summary>The list as it is shown: the previous search, where there is one, then the kept presets.</summary>
    public System.Collections.IEnumerable PresetRows => _rows;

    private readonly ObservableCollection<PresetRowViewModel> _rows = [];

    private void ListRows()
    {
        _rows.Clear();
        if (_previous is { } before)
            _rows.Add(new PreviousSearchRow(before, Filter.Describe(before)));

        foreach (var row in Saved)
            _rows.Add(row);
    }

    /// <summary>The presets' section, open until folded - and as it was left, the next time the editor opens.</summary>
    [ObservableProperty] private bool _presetsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(SearchPanelViewModel) + nameof(PresetsOpen));

    partial void OnPresetsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(SearchPanelViewModel) + nameof(PresetsOpen), value);

    [RelayCommand]
    private void TogglePresets() => PresetsOpen = !PresetsOpen;

    /// <summary>The kept preset that asks what the search asks now, if any does.</summary>
    private SearchPreset? Matching()
    {
        var now = Filter.Snapshot();
        return Settings.SearchPresets.FirstOrDefault(kept => kept.AsksTheSameAs(now));
    }

    /// <summary>The search as it stands, for the preset row and the picker.</summary>
    public string CurrentPresetName => Matching()?.Name is { Length: > 0 } name ? name : L("No preset");

    public string CurrentSummary => Filter.Describe();

    /// <summary>
    /// What the search asks, in a line, for the Conditions heading - "fortune", Rare, 2 sockets: in sight while
    /// the conditions are folded, when nothing else on the page says what the results are of. Empty while
    /// nothing is asked: "Any item" on a heading over empty fields says nothing.
    /// </summary>
    public string Asked => Filter.IsFiltering ? Filter.Describe() : "";

    /// <summary>What the search asks: said on the Conditions heading (<see cref="Asked"/>), not under the presets' header.</summary>
    public string Summary => CurrentSummary;

    /// <summary>
    /// Never: the line stood under the presets' header, over the very fields it repeated, and was folded away
    /// with them. It is on the Conditions heading now, in sight while the conditions are folded - which is when
    /// nothing else on the page says what the results are of.
    /// </summary>
    public bool HasSummary => false;

    /// <summary>The name box: the preset the search is, until something else is typed.</summary>
    [ObservableProperty] private string _nameText = "";

    partial void OnNameTextChanged(string value)
    {
        KeepCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(KeepHint));
    }

    /// <summary>Keep: a name, and either no preset asks this yet or the one that does is named otherwise.</summary>
    public bool CanKeep => NameText.Trim() is { Length: > 0 } name && Matching()?.Name != name;

    /// <summary>The kept preset the name box names, if any.</summary>
    private SearchPreset? Named() =>
        Settings.SearchPresets.FirstOrDefault(kept => kept.Name.Equals(NameText.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>What Keep will do: rename the preset the search is, update the one of that name, or keep a new one.</summary>
    public string KeepHint =>
        Matching() is not null ? L("Rename preset")
        : Named() is not null ? L("Update preset")
        : L("Keep preset");

    private void Reload()
    {
        Saved.Clear();

        // By name; Created is the tie-break, so two presets of one name keep their order between reloads.
        foreach (var preset in Settings.SearchPresets
                     .OrderBy(each => each.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(each => each.Created))
        {
            Saved.Add(new SearchPresetRow(preset, Filter.Describe(preset)));
        }

        ListRows();
        MarkCurrentRow();
        SayCurrent();
        OnPropertyChanged(nameof(HasSaved));
    }

    /// <summary>The kept preset the search is, marked in the list.</summary>
    private void MarkCurrentRow()
    {
        var now = Filter.Snapshot();
        foreach (var row in Saved)
            row.IsCurrent = row.Preset.AsksTheSameAs(now);
    }

    private void SayCurrent()
    {
        OnPropertyChanged(nameof(CurrentPresetName));
        OnPropertyChanged(nameof(CurrentSummary));
        OnPropertyChanged(nameof(Asked));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(KeepHint));
        KeepCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Writes the name box to the list: the preset the search already is gets the name, a kept preset of that
    /// name takes the search as it stands (presets lacked tools such as an edit), and
    /// otherwise the search is kept under it. Written at once, as seeds are.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanKeep))]
    private void Keep()
    {
        string name = NameText.Trim();
        if (name.Length == 0)
            return;

        if (Matching() is { } kept)
        {
            kept.Name = name;
        }
        else if (Named() is { } same)
        {
            // In its place in the list, keeping when it was first kept.
            var made = Filter.Snapshot();
            made.Name = same.Name;
            made.Created = same.Created;
            Settings.SearchPresets[Settings.SearchPresets.IndexOf(same)] = made;
        }
        else
        {
            var made = Filter.Snapshot();
            made.Name = name;
            made.Created = DateTimeOffset.Now;
            Settings.SearchPresets.Add(made);
        }

        Settings.Save();
        Reload();
    }

    /// <summary>
    /// A kept preset clicked: the search becomes it, and the name box takes its name. The previous search's
    /// row clicked: it and the search change places.
    /// </summary>
    [RelayCommand]
    private void Choose(PresetRowViewModel? chosen)
    {
        if (chosen is PreviousSearchRow)
        {
            _sfxManager?.Method_ButtonSFX_Press();
            SwapPrevious();
            return;
        }

        if (chosen is not SearchPresetRow row)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        Replace(() => Filter.Apply(row.Preset));
        NameText = row.Preset.Name;
    }

    [RelayCommand]
    private void Forget(PresetRowViewModel? chosen)
    {
        if (chosen is not SearchPresetRow row || AskTwice.First(row, PresetRows))
            return;

        Settings.SearchPresets.Remove(row.Preset);
        Settings.Save();
        Reload();
    }
}
