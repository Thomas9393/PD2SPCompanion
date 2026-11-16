using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Generic;

using System;
using System.Collections.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A property picker over a page: a search box, the list it narrows, and a pick that adds and closes.
/// One for the search panel's properties (<see cref="SearchForm"/>, the trade site's composites) and
/// one for the Edit page's (the save's own properties) - the list and what a pick does are the
/// owner's, everything else is this (the two pickers were twins in markup).
/// The view is PropertyPicker.axaml.
/// </summary>
public sealed partial class PropertyPickerViewModel(
    Func<string, IReadOnlyList<IPropertyChoice>> narrow,
    Action<IPropertyChoice> pick,
    ISFXManager? sfxManager) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Choices))]
    [NotifyPropertyChangedFor(nameof(NoChoices))]
    private string _search = "";

    /// <summary>The list as the search narrows it - the owning page's rule.</summary>
    public IReadOnlyList<IPropertyChoice> Choices => narrow(Search);

    public bool NoChoices => Choices.Count == 0;

    [ObservableProperty] private bool _isPresent;

    [ObservableProperty] private bool _isOpen;

    partial void OnIsOpenChanged(bool value) =>
        Presence.Keep(value, () => IsOpen, on => IsPresent = on);

    /// <summary>
    /// Opens on an empty search. The list is asked again: an empty search changing to empty says
    /// nothing, and a list read before the tables were loaded would otherwise stay the one shown.
    /// </summary>
    public void Open()
    {
        sfxManager?.Method_ButtonSFX_Press();
        Search = "";
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(NoChoices));
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>A pick is added at once and the picker closes: nothing is provisional.</summary>
    [RelayCommand]
    private void Pick(IPropertyChoice? choice)
    {
        if (choice is null)
            return;

        sfxManager?.Method_ButtonSFX_Press();
        pick(choice);
        IsOpen = false;
    }
}
