using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One preset in a <see cref="IPresetSection"/>'s list: a name, what it holds in a line, whether it is the current one.</summary>
public abstract partial class PresetRowViewModel(string name, string summary) : ObservableObject, IAsksTwice
{
    public string Name { get; } = name;

    /// <summary>What it holds, in a line.</summary>
    public string Summary { get; } = summary;

    /// <summary>Whether this is the preset things are now.</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>Whether it can be deleted: a built-in one cannot.</summary>
    public virtual bool CanForget => true;

    /// <summary>Its bin pressed once: the next press deletes (AskTwice; BinButton draws the tick).</summary>
    [ObservableProperty] private bool _isArmed;
}

/// <summary>
/// A panel's presets, in the panel, with no second panel to open: what <c>Views/Shared/PresetSection.axaml</c> shows - a folding section with the current preset's
/// name on its header, what things are now, a name box whose Keep keeps them, renames the preset they are or updates
/// the one of that name, and the list, a click on one being it at once. The search panel and the drop panel each are one.
/// </summary>
public interface IPresetSection
{
    /// <summary>The preset things are now, or what they are when none: "No preset", "Vanilla".</summary>
    string CurrentPresetName { get; }

    /// <summary>What things are now, in a line - shown where <see cref="HasSummary"/>.</summary>
    string Summary { get; }

    bool HasSummary { get; }

    /// <summary>The name box.</summary>
    string NameText { get; set; }

    /// <summary>What Keep will do: keep, rename or update.</summary>
    string KeepHint { get; }

    IRelayCommand KeepCommand { get; }

    /// <summary>The presets, <see cref="PresetRowViewModel"/> each.</summary>
    IEnumerable PresetRows { get; }

    IRelayCommand<PresetRowViewModel?> ChooseCommand { get; }

    IRelayCommand<PresetRowViewModel?> ForgetCommand { get; }

    /// <summary>
    /// The order the list is in, where it can be chosen: the section then has a sort menu on its header. Null
    /// where it cannot - the search's presets, a handful listed by name (they have no sort menu).
    /// </summary>
    SortChoice? Order { get; }

    /// <summary>Whether the section is open; it folds as the Edit page's sections do.</summary>
    bool PresetsOpen { get; }

    IRelayCommand TogglePresetsCommand { get; }
}
