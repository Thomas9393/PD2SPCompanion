using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One of the Custom panel's presets: Legit, Casual or Free, or a custom mode kept under a name.</summary>
public sealed class PolicyPresetRow(string name, string summary, PlayPolicy policy, PolicyPreset? preset, bool isMode)
    : PresetRowViewModel(name, summary)
{
    public PlayPolicy Policy { get; } = policy;

    /// <summary>The kept custom mode; null for the three built in.</summary>
    public PolicyPreset? Preset { get; } = preset;

    /// <summary>The built-in ones are not deleted, nor the one that is the mode.</summary>
    public override bool CanForget => Preset is not null && !isMode;
}

/// <summary>
/// The play mode in Settings: Legit, Casual and Free as buttons, the one lit being the mode, and
/// Custom opening a panel of every setting. The panel starts from the current mode and its header
/// names the preset its rows started from and how many differ; confirming rows that are a preset's
/// makes that preset the mode.
/// <para>
/// <b>Custom modes are kept under names</b>, with the tools the search's and the drop
/// rates' presets have (<see cref="IPresetSection"/>): the panel's Presets section lists Legit, Casual and
/// Free and every kept one, a click setting the rows to it; its name box keeps the rows under a name,
/// renames the kept mode they are, or updates the one of that name. SAVE makes the rows the mode: the kept
/// one they match, else under the name in the box, else the custom mode that is the mode now, edited.
/// </para>
/// </summary>
public sealed partial class PlayModeViewModel : ObservableObject, IPresetSection
{
    private readonly EditorSettings _settings;

    public PlayModeViewModel(EditorSettings settings)
    {
        _settings = settings;
        _draft = settings.Policy;
        Rows = MakeRows();
        Sections = MakeSections(Rows);
        Order = new SortChoice(settings, "PolicyPresets", N("Name"), N("Added"));
        Order.Changed += Reload;

        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EditorSettings.PlayMode) or nameof(EditorSettings.Policy) or nameof(EditorSettings.ModeLabel))
                OnModeChanged();
        };

        // A custom mode a vault brought in, or one chosen from a vault's question: the list follows.
        settings.ModeChosen += Reload;
        settings.CustomPresets.CollectionChanged += (_, _) => Reload();
    }

    public bool IsLegit => _settings.PlayMode == PlayPolicy.LegitName;
    public bool IsCasual => _settings.PlayMode == PlayPolicy.CasualName;
    public bool IsFree => _settings.PlayMode == PlayPolicy.FreeName;
    public bool IsCustom => _settings.PlayMode == PlayPolicy.CustomName;

    /// <summary>The Custom button: the custom mode that is the mode by its name - "CUSTOM - SSF RULES" - else the word.</summary>
    public string CustomButton => IsCustom ? _settings.ModeLabel.ToUpperInvariant() : L("CUSTOM");

    private void OnModeChanged()
    {
        OnPropertyChanged(nameof(IsLegit));
        OnPropertyChanged(nameof(IsCasual));
        OnPropertyChanged(nameof(IsFree));
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(CustomButton));
    }

    /// <summary>Makes a preset the mode, by name.</summary>
    [RelayCommand]
    private void PickMode(string name)
    {
        if (PlayPolicy.Preset(name) is { } preset)
            _settings.ChoosePolicy(preset);
    }

    // ---- the Custom panel ---------------------------------------------------------------

    private PlayPolicy _draft;

    /// <summary>Every setting, under its heading, in the order the design lists them.</summary>
    public IReadOnlyList<PolicyRow> Rows { get; }

    /// <summary>The same rows, one card per heading: the flat list was a mess.</summary>
    public IReadOnlyList<PolicySection> Sections { get; }

    // ---- the base: the preset the rows are measured against ------------------------------------------
    // The preset they started from: the mode itself as the panel opens, then whichever preset is clicked
    // in the list - Legit, Casual, Free, or a kept custom mode. It was a "Based on" box of the three until
    // the presets came, which made it a second way to the same.

    /// <summary>Legit, Casual and Free: the presets built in, and what a custom mode is described as being near.</summary>
    public IReadOnlyList<string> BaseNames { get; } = [PlayPolicy.LegitName, PlayPolicy.CasualName, PlayPolicy.FreeName];

    private PlayPolicy _basis = PlayPolicy.Casual;

    private string _basisName = PlayPolicy.CasualName;

    private PlayPolicy Basis => _basis;

    /// <summary>How many rows differ from the base.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangesText))]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyCanExecuteChangedFor(nameof(ResetAllCommand))]
    private int _changeCount;

    /// <summary>The base and how far the rows are from it: "Free", "Casual, 3 changes".</summary>
    public string ChangesText =>
        ChangeCount == 0 ? _basisName : Plural(ChangeCount, $"{_basisName}, 1 change", $"{_basisName}, {ChangeCount} changes");

    public bool HasChanges => ChangeCount > 0;

    /// <summary>The rows set to a preset, which is what they are measured against from then on.</summary>
    private void StartFrom(PlayPolicy policy, string name)
    {
        _basis = policy;
        _basisName = name;
        _draft = Basis;
        foreach (var row in Rows)
            row.Read(_draft);
        Compare();
    }

    /// <summary>Every row back to the base.</summary>
    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void ResetAll() => StartFrom(_basis, _basisName);

    /// <summary>One row back to the base.</summary>
    internal void Reset(PolicyRow row)
    {
        _draft = row.Copied(Basis, _draft);
        row.Read(_draft);
        Compare();
    }

    /// <summary>Each row told whether it differs from the base, and the count.</summary>
    private void Compare()
    {
        foreach (var row in Rows)
            row.Compare(Basis, _draft, _basisName);
        ChangeCount = Rows.Count(row => row.IsChanged);
        OnPropertyChanged(nameof(ChangesText));
        Follow();
    }

    /// <summary>The preset with the fewest rows different from these settings.</summary>
    private int Nearest(PlayPolicy policy) =>
        Enumerable.Range(0, BaseNames.Count)
                  .MinBy(i => Rows.Count(row => row.Differs(PlayPolicy.Preset(BaseNames[i])!, policy)));

    [ObservableProperty] private bool _isPanelPresent;

    [ObservableProperty] private bool _isPanelOpen;

    partial void OnIsPanelOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPanelOpen, on => IsPanelPresent = on);

    /// <summary>Opens on the current mode's settings, measured against the mode itself: nothing changed yet.</summary>
    [RelayCommand]
    private void OpenCustom()
    {
        StartFrom(_settings.Policy, IsCustom ? _settings.CustomName : _settings.PlayMode);
        NameText = IsCustom ? _settings.CustomName : "";
        Reload();
        IsPanelOpen = true;
    }

    [RelayCommand]
    private void CloseCustom() => IsPanelOpen = false;

    /// <summary>
    /// The rows made the mode. A custom one is the kept mode it matches; else it is kept under the name in
    /// the box; else - no name given - it is the custom mode that is the mode now, its rules edited.
    /// </summary>
    [RelayCommand]
    private void ConfirmCustom()
    {
        string typed = NameText.Trim();
        _settings.ChoosePolicy(_draft, Matching()?.Name ?? (typed.Length > 0 && !Reserved(typed) ? typed : null));
        IsPanelOpen = false;
    }

    // ---- the presets (IPresetSection) ---------------------------------------------------------

    /// <summary>Legit, Casual and Free, then the kept custom modes.</summary>
    public System.Collections.ObjectModel.ObservableCollection<PolicyPresetRow> Saved { get; } = [];

    public System.Collections.IEnumerable PresetRows => Saved;

    public SortChoice Order { get; }

    /// <summary>The kept custom mode the rows are, if any is.</summary>
    private PolicyPreset? Matching() => _settings.CustomPresets.FirstOrDefault(kept => kept.Policy == _draft);

    /// <summary>The kept custom mode the name box names, if any.</summary>
    private PolicyPreset? Named() => _settings.Custom(NameText);

    /// <summary>A name a kept mode cannot have: the three presets', and the word for any of them.</summary>
    private static bool Reserved(string name) =>
        new[] { PlayPolicy.LegitName, PlayPolicy.CasualName, PlayPolicy.FreeName, PlayPolicy.CustomName }
            .Contains(name, StringComparer.OrdinalIgnoreCase);

    public string CurrentPresetName =>
        PlayPolicy.PresetNameOf(_draft) ?? Matching()?.Name ?? L("No preset");

    /// <summary>The header already says the base and how many rows differ from it.</summary>
    public string Summary => "";

    public bool HasSummary => false;

    /// <summary>The name box: the custom mode the rows are, until something else is typed.</summary>
    [ObservableProperty] private string _nameText = "";

    partial void OnNameTextChanged(string value)
    {
        KeepCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(KeepHint));
    }

    /// <summary>Keep: a name that is free to use, rows that are no preset's, and not already kept under that name.</summary>
    public bool CanKeep =>
        NameText.Trim() is { Length: > 0 } name && !Reserved(name) && PlayPolicy.PresetNameOf(_draft) is null
        && Matching()?.Name != name && !RenameTaken();

    /// <summary>A rename onto the name of another kept mode would leave two of one name: not offered.</summary>
    private bool RenameTaken() =>
        Matching() is { } kept && Named() is { } other && !ReferenceEquals(kept, other);

    /// <summary>What Keep will do: rename the kept mode the rows are, update the one of that name, or keep a new one.</summary>
    public string KeepHint =>
        RenameTaken() ? L("Name taken")
        : Matching() is not null ? L("Rename preset")
        : Named() is not null ? L("Update preset")
        : L("Keep preset");

    /// <summary>
    /// The name box written to the list: the kept mode the rows already are is renamed; a kept mode of that
    /// name takes these rows - and where it is the mode, the mode takes them with it; else they are kept under it.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanKeep))]
    private void Keep()
    {
        string name = NameText.Trim();
        if (!CanKeep)
            return;

        if (Matching() is { } kept)
        {
            _settings.RenameCustom(kept, name);
        }
        else if (Named() is { } same && IsCustom && same.Name.Equals(_settings.CustomName, StringComparison.OrdinalIgnoreCase))
        {
            _settings.ChoosePolicy(_draft, same.Name);
        }
        else
        {
            _settings.KeepCustom(name, _draft);
            _settings.Save();
        }

        Reload();
    }

    /// <summary>A preset clicked: the rows become it, and the name box takes its name where it is a kept one.</summary>
    [RelayCommand]
    private void Choose(PresetRowViewModel? chosen)
    {
        if (chosen is not PolicyPresetRow row)
            return;

        NameText = row.Preset?.Name ?? "";
        StartFrom(row.Policy, row.Name);
    }

    [RelayCommand]
    private void Forget(PresetRowViewModel? chosen)
    {
        if (chosen is not PolicyPresetRow { Preset: { } preset, CanForget: true } || AskTwice.First(chosen, PresetRows))
            return;

        _settings.ForgetCustom(preset);
        Reload();
    }

    /// <summary>The presets, in the panel; folded until opened, the rows being what the panel is for.</summary>
    [ObservableProperty] private bool _presetsOpen = FoldMemory.IsOpen(nameof(PlayModeViewModel) + nameof(PresetsOpen), initially: false);

    partial void OnPresetsOpenChanged(bool value) => FoldMemory.Keep(nameof(PlayModeViewModel) + nameof(PresetsOpen), value);

    [RelayCommand]
    private void TogglePresets() => PresetsOpen = !PresetsOpen;

    private void Reload()
    {
        Saved.Clear();
        // The three built in, each with what it is in a line - the first-run question's words for them.
        foreach (string name in BaseNames)
        {
            string what = name switch
            {
                PlayPolicy.LegitName => L("Vault, compendium and trade. No editing."),
                PlayPolicy.FreeName => L("Unrestricted editing."),
                _ => L("Game-legal editing."),
            };
            Saved.Add(new PolicyPresetRow(name, what, PlayPolicy.Preset(name)!, null, isMode: false));
        }

        var ordered = Order.By switch
        {
            "Added" => _settings.CustomPresets.OrderBy(each => each.Created),
            _ => _settings.CustomPresets
                .OrderBy(each => each.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(each => each.Created),
        };

        foreach (var preset in Order.Arrange(ordered))
        {
            bool isMode = IsCustom && preset.Name.Equals(_settings.CustomName, StringComparison.OrdinalIgnoreCase);
            Saved.Add(new PolicyPresetRow(preset.Name, Describe(preset.Policy), preset.Policy, preset, isMode));
        }

        Follow();
    }

    /// <summary>A custom mode in a line: the preset it is nearest, and how many rows differ from that.</summary>
    private string Describe(PlayPolicy policy)
    {
        string near = BaseNames[Nearest(policy)];
        int changes = Rows.Count(row => row.Differs(PlayPolicy.Preset(near)!, policy));
        return Plural(changes, $"{near}, 1 change", $"{near}, {changes} changes");
    }

    /// <summary>The presets' part of the panel after the rows changed: which preset they are, and what Keep would do.</summary>
    private void Follow()
    {
        foreach (var row in Saved)
            row.IsCurrent = row.Policy == _draft;

        OnPropertyChanged(nameof(CurrentPresetName));
        OnPropertyChanged(nameof(KeepHint));
        KeepCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A row was changed: the draft takes it, and the header follows.</summary>
    internal void Changed(Func<PlayPolicy, PlayPolicy> change)
    {
        _draft = change(_draft);
        Compare();
    }

    private static IReadOnlyList<PolicySection> MakeSections(IReadOnlyList<PolicyRow> rows)
    {
        var sections = new List<PolicySection>();
        foreach (var row in rows)
        {
            if (row.IsHeading)
                sections.Add(new PolicySection(row.Title, new List<PolicyRow>()));
            else
                ((List<PolicyRow>)sections[^1].Rows).Add(row);
        }
        return sections;
    }

    private IReadOnlyList<PolicyRow> MakeRows()
    {
        PolicyRow Levels(string title, string locked, string legal, string any,
                         Func<PlayPolicy, Reach> get, Func<PlayPolicy, Reach, PlayPolicy> set) =>
            PolicyRow.Choice(this, title, [locked, legal, any],
                             policy => (int)get(policy), (policy, index) => set(policy, (Reach)index));

        PolicyRow Switch(string title, Func<PlayPolicy, bool> get, Func<PlayPolicy, bool, PlayPolicy> set) =>
            PolicyRow.Switch(this, title, get, set);

        // Short, in the project's wording style (checked on the final result); what a
        // label cannot say in a few words is its tooltip.
        return
        [
            PolicyRow.Heading(L("Character")),
            Levels(L("Stats and level"), L("Locked"), L("Editable"), L("Unrestricted"),
                   p => p.Character, (p, v) => p with { Character = v })
                .Tipped(L("Stats, skills, level and experience")),
            Switch(L("Gold amounts"), p => p.TypeGold, (p, v) => p with { TypeGold = v }).Tipped(L("Type gold amounts")),
            Switch(L("Waypoints"), p => p.Waypoints, (p, v) => p with { Waypoints = v }),
            Switch(L("Map seed"), p => p.MapSeed, (p, v) => p with { MapSeed = v }),

            PolicyRow.Heading(L("Items")),
            Levels(L("Properties"), L("Locked"), L("Within ranges"), L("Any"),
                   p => p.Properties, (p, v) => p with { Properties = v })
                .Tipped(L("Any: any value, any property")),
            // No preset has it: turned on, the mode is Custom.
            Switch(L("Raw property list"), p => p.RawProperties, (p, v) => p with { RawProperties = v })
                .Tipped(L("Add property lists every stat by id and engine name")),
            Levels(L("Item details"), L("Locked"), L("Within limits"), L("Any"),
                   p => p.Details, (p, v) => p with { Details = v })
                .Tipped(L("Defense, durability, quantity, charges, graphic")),
            Switch(L("Item level"), p => p.ItemLevel, (p, v) => p with { ItemLevel = v }).Tipped("1 to 99"),
            Switch(L("Ethereal"), p => p.Ethereal, (p, v) => p with { Ethereal = v }),
            // A switch: the game holds an item to its base's maximum whatever is
            // written, so there is no past it to offer.
            Switch(L("Sockets"), p => p.Sockets != Reach.Locked, (p, v) => p with { Sockets = v ? Reach.Legal : Reach.Locked })
                .Tipped(L("Up to the base's maximum")),
            Switch(L("Affixes"), p => p.Affixes, (p, v) => p with { Affixes = v }).Tipped(L("Choose, roll, reroll, rename a rare")),
            Switch(L("Corruption"), p => p.Corruption, (p, v) => p with { Corruption = v }).Tipped(L("Corrupt and uncorrupt")),
            Switch(L("Crafting"), p => p.Craft, (p, v) => p with { Craft = v }),
            Switch(L("Runewords"), p => p.Runewords, (p, v) => p with { Runewords = v }).Tipped(L("Build runewords")),
            Switch(L("Duplicating"), p => p.Duplicate, (p, v) => p with { Duplicate = v }),
            Switch(L("Forge"), p => p.Forge, (p, v) => p with { Forge = v }).Tipped(L("Make any item from nothing")),
            Switch(L("Equip past requirements"), p => p.ForceEquip, (p, v) => p with { ForceEquip = v }),
            Switch(L("Material counts"), p => p.TypeMaterials, (p, v) => p with { TypeMaterials = v }).Tipped(L("Type materials tab counts")),

            PolicyRow.Heading(L("Vault")),
            Switch(L("Duplicate marks"), p => p.DetectDuplicates, (p, v) => p with { DetectDuplicates = v }).Tipped(L("Mark duplicate items")),

            PolicyRow.Heading(L("Trading")),
            Switch(L("Repeat buys"), p => p.RepeatBuys, (p, v) => p with { RepeatBuys = v }).Tipped(L("Buy the same item twice without a warning")),
        ];
    }
}

/// <summary>
/// One line of the Custom panel: a heading, a choice of levels, or a switch - each reading its
/// setting from a policy and writing it back into the panel's draft.
/// </summary>
public sealed partial class PolicyRow : ObservableObject
{
    private readonly PlayModeViewModel? _mode;
    private readonly Func<PlayPolicy, int>? _get;
    private readonly Func<PlayPolicy, int, PlayPolicy>? _set;
    private bool _reading;

    private PolicyRow(PlayModeViewModel? mode, string title, IReadOnlyList<string> options,
                      Func<PlayPolicy, int>? get, Func<PlayPolicy, int, PlayPolicy>? set, bool isSwitch)
    {
        _mode = mode;
        Title = title;
        Options = options;
        _get = get;
        _set = set;
        IsSwitch = isSwitch;
    }

    public static PolicyRow Heading(string title) => new(null, title, [], null, null, isSwitch: false);

    public static PolicyRow Choice(PlayModeViewModel mode, string title, IReadOnlyList<string> options,
                                   Func<PlayPolicy, int> get, Func<PlayPolicy, int, PlayPolicy> set) =>
        new(mode, title, options, get, set, isSwitch: false);

    public static PolicyRow Switch(PlayModeViewModel mode, string title,
                                   Func<PlayPolicy, bool> get, Func<PlayPolicy, bool, PlayPolicy> set) =>
        new(mode, title, [], policy => get(policy) ? 1 : 0, (policy, on) => set(policy, on == 1), isSwitch: true);

    public string Title { get; }

    public IReadOnlyList<string> Options { get; }

    /// <summary>What the short label cannot say; null for none.</summary>
    public string? Tip { get; private set; }

    public PolicyRow Tipped(string tip)
    {
        Tip = tip;
        return this;
    }

    public bool IsHeading => _get is null;

    public bool IsSwitch { get; }

    public bool IsChoice => !IsHeading && !IsSwitch;

    /// <summary>A choice's option, by its place in <see cref="Options"/>.</summary>
    [ObservableProperty] private int _selected;

    /// <summary>A switch's state.</summary>
    [ObservableProperty] private bool _isOn;

    /// <summary>Differs from the base: marked, and its reset shown.</summary>
    [ObservableProperty] private bool _isChanged;

    /// <summary>On the reset: what the base has here.</summary>
    [ObservableProperty] private string _resetTip = "";

    internal bool Differs(PlayPolicy basis, PlayPolicy draft) => _get is not null && _get(basis) != _get(draft);

    internal void Compare(PlayPolicy basis, PlayPolicy draft, string baseName)
    {
        IsChanged = Differs(basis, draft);
        if (_get is null)
            return;
        int value = _get(basis);
        string shown = IsSwitch ? (value == 1 ? L("On") : L("Off")) : (value >= 0 && value < Options.Count ? Options[value] : "");
        ResetTip = L($"Back to {baseName}: {shown}");
    }

    /// <summary>The draft with this row's setting taken from the base.</summary>
    internal PlayPolicy Copied(PlayPolicy basis, PlayPolicy draft) =>
        _get is null || _set is null ? draft : _set(draft, _get(basis));

    [RelayCommand]
    private void Reset() => _mode?.Reset(this);

    partial void OnSelectedChanged(int value) => Write(value);

    partial void OnIsOnChanged(bool value) => Write(value ? 1 : 0);

    private void Write(int value)
    {
        if (!_reading && _set is not null && value >= 0)
            _mode?.Changed(policy => _set(policy, value));
    }

    /// <summary>Shows a policy's setting, without taking it for one the player changed.</summary>
    internal void Read(PlayPolicy policy)
    {
        if (_get is null)
            return;

        _reading = true;
        int value = _get(policy);
        Selected = value;
        IsOn = value == 1;
        _reading = false;
    }
}

/// <summary>One card of the Custom panel: a heading and its rows.</summary>
public sealed record PolicySection(string Title, IReadOnlyList<PolicyRow> Rows);
