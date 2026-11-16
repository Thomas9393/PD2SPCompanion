using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The settings page's right panel: the vault, and how the editor edits. The game folder and the
/// backups are the left panel's.
/// </summary>
public partial class VP_ViewModel_Settings_R : ViewModelBase
{
    public CharacterSession Session { get; }

    /// <summary>What the Editing section's switches are bound to.</summary>
    public EditorSettings Settings { get; }

    private readonly ISFXManager _sfxManager = null!;

    /// <summary>The play mode: Legit, Casual, Free or Custom - first on this page.</summary>
    public PlayModeViewModel PlayMode { get; }

    /// <summary>The vault's mode, and its way out to a file and in from one - whole, or a folder of it.</summary>
    public VaultManagerViewModel? Vaults { get; }

    /// <summary>The vault card's "Migrate from" row and its panel: a new season's vault filled from older ones.</summary>
    public SeasonMigrationViewModel? Migration { get; }

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Settings_R()
        : this(new CharacterSession(), EditorSettings.Load(), null!, null!, null, null) { }

    public VP_ViewModel_Settings_R(
        CharacterSession session, EditorSettings settings, ISFXManager sfxManager, VaultService vaults,
        ConfirmService? confirm, SeasonMigrationService? migration)
    {
        Session = session;
        Settings = settings;
        _sfxManager = sfxManager;
        _vaults = vaults;
        PlayMode = new PlayModeViewModel(settings);
        Vaults = vaults is null ? null : new VaultManagerViewModel(session, vaults, sfxManager, confirm);
        Migration = vaults is null || migration is null ? null : new SeasonMigrationViewModel(session, migration, vaults, sfxManager);

        // Stored into from the inventory while this page is not showing, the counts follow: the list hears the vault
        // itself (VaultManagerViewModel). Heard here too, every vault change read every vault's file twice.
    }

    /// <summary>
    /// The price table: each currency with what the player says it is worth in HR, saved as typed
    /// (moved here from the trading page, the same panel behind the same $).
    /// Made the first time it is asked for with the game's tables read - a row draws its currency,
    /// and the page is built while the app starts, before they are.
    /// </summary>
    public IReadOnlyList<TradePriceRow> PriceRows
    {
        get
        {
            if (_priceRows is null && Settings is { } settings)
            {
                try
                {
                    _priceRows = [.. VP_ViewModel_Trade.Currencies.Select(code => new TradePriceRow(code, settings))];
                }
                catch (Exception)
                {
                    // Not yet: asked again the next time the page shows.
                    return [];
                }
            }

            return _priceRows ?? [];
        }
    }

    private IReadOnlyList<TradePriceRow>? _priceRows;

    /// <summary>The Trade section's line: how many currencies have a price.</summary>
    public string PriceSummary
    {
        get
        {
            int set = VP_ViewModel_Trade.Currencies.Count(code => Settings?.TradePrices.ContainsKey(code) == true);
            return L($"{set} of {VP_ViewModel_Trade.Currencies.Length} priced");
        }
    }

    [ObservableProperty] private bool _isPriceTablePresent;

    [ObservableProperty] private bool _isPriceTableOpen;

    partial void OnIsPriceTableOpenChanged(bool value)
    {
        Presence.Keep(value, () => IsPriceTableOpen, on => IsPriceTablePresent = on);

        // The rows are asked for when the panel first shows, the tables read by then.
        if (value)
            OnPropertyChanged(nameof(PriceRows));
    }

    [RelayCommand]
    private void OpenPriceTable()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        IsPriceTableOpen = true;
    }

    [RelayCommand]
    private void ClosePriceTable()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        IsPriceTableOpen = false;
        OnPropertyChanged(nameof(PriceSummary));
    }

    /// <summary>The vault, shared with the vault page and the inventory's store button.</summary>
    private readonly VaultService _vaults;

    /// <summary>
    /// Reads the vault's counts. Called when the page is shown, not when it is built: the vault
    /// is named for the installed season, which is only known once the tables are read.
    /// </summary>
    public void ShowVault() => Vaults?.Refresh();

}
