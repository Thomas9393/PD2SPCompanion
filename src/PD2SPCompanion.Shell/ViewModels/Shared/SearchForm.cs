using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Core.Items;

using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;


using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The vault's search form, for any page that searches for items: the pickers and the property rows
/// over a <see cref="VaultFilter"/>, and the property picker that adds a row. The vault's right page
/// has one over the vault's own filter; the trading page's sale has one over a filter of its own,
/// filled from the item for sale, without the dates and folders a listing has none of.
/// </summary>
public sealed partial class SearchForm : ObservableObject
{
    private readonly ISFXManager? _sfxManager;

    public SearchForm(VaultFilter filter, ISFXManager? sfxManager, bool inVault)
    {
        Filter = filter;
        _sfxManager = sfxManager;
        InVault = inVault;
    }

    public VaultFilter Filter { get; }

    /// <summary>The vault's: where to look. A listing has no folder.</summary>
    public bool InVault { get; }

    /// <summary>Lets go of a duplicate badge's search: every item again.</summary>
    [RelayCommand]
    private void ClearSameId()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Filter.ClearSameId();
    }

    /// <summary>The vault's name box looks at the base and the stats too; the trade site's only at the name.</summary>
    public string NameCaption => L("Name, base or stat");

    // ---- the property picker --------------------------------------------------------------------

    /// <summary>
    /// The property picker (PropertyPicker.axaml): the trade site's options, some thousand lines -
    /// every skill is two of them - narrowed by every word typed, in any order. A pick adds its row.
    /// </summary>
    public PropertyPickerViewModel Properties => _properties ??= new(
        search =>
        {
            string[] words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 0
                ? PropertyIndex.Options
                : [.. PropertyIndex.Options.Where(option =>
                    words.All(word => option.Label.Contains(word, StringComparison.OrdinalIgnoreCase)))];
        },
        choice => Filter.AddProperty((PropertyOption)choice),
        _sfxManager);

    private PropertyPickerViewModel? _properties;

    [RelayCommand]
    private void OpenPropertyPicker() => Properties.Open();

    [RelayCommand]
    private void RemoveProperty(PropertyFilterRow? row)
    {
        if (row is null)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        Filter.RemoveProperty(row);
    }
}
