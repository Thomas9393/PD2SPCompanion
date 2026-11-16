using PD2SPCompanion.Shell.Services.Generic;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The Extra page's right half: the grail, browsed, and under the
/// left page's buttons the rune wizard's panel - neither needs a character. The map seed moved to
/// the Character page.
/// </summary>
public sealed partial class VP_ViewModel_Extra_R : ViewModelBase
{
    public CharacterSession Session { get; }

    /// <summary>The rune wizard, beneath the mode switch.</summary>
    public RuneWizardViewModel Wizard { get; }

    /// <summary>The crafting wizard, under the rune wizard: counted when it is, from the same materials tab.</summary>
    public CraftWizardViewModel Crafting { get; }

    /// <summary>The grail, browsed only: the page under the panels.</summary>
    public GrailViewModel Grail { get; }

    private readonly PD2SPCompanion.Shell.Services.Vault.VaultService _vaults;

    /// <summary>
    /// Softcore or hardcore, with no character to say: the grail's Discovery and the shared stash the wizard reads
    /// are each mode's own. A character loaded decides by its own mode.
    /// </summary>
    public bool IsHardcore => _vaults.IsHardcore;

    /// <summary>
    /// The switch is always shown, so the page looks the same either way: with
    /// no character it chooses; with one it is locked to that character's mode.
    /// </summary>
    public bool ChoosesMode => !Session.IsLoaded;

    /// <summary>Why the pair is dimmed, on each of its buttons: Settings' words for its own.</summary>
    public string? ModeHint => Session.IsLoaded ? L("Follows open character.") : null;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ShowMode(string mode)
    {
        _vaults.UseMode(mode == "Hardcore");
        OnPropertyChanged(nameof(IsHardcore));
        Grail.Load();
        Wizard.Refresh();
        Crafting.Refresh();
    }

    public EditorSettings Settings { get; }

    /// <summary>The play mode.</summary>
    public PlayRules Rules { get; }

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Extra_R() : this(new CharacterSession(), new EditorSettings(), null!, null!, new PlayRules(new EditorSettings()),
                                        new PD2SPCompanion.Shell.Services.Vault.VaultService(new CharacterSession(), new EditorSettings())) { }

    public VP_ViewModel_Extra_R(CharacterSession session, EditorSettings settings, SharedStashSession stash,
                               ISFXManager sfxManager, PlayRules rules, PD2SPCompanion.Shell.Services.Vault.VaultService vaults)
    {
        Session = session;
        Settings = settings;
        Rules = rules;
        _vaults = vaults;
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded))
            {
                OnPropertyChanged(nameof(ChoosesMode));
                OnPropertyChanged(nameof(IsHardcore));
                OnPropertyChanged(nameof(ModeHint));
            }
        };

        // The shared stash's materials tab: the open stash's while a character is loaded, else the
        // file as it is on disk - so it follows both.
        // Counted while the page is shown; hidden, on the next visit (WhenShown).
        Wizard = new RuneWizardViewModel(session, stash, settings, vaults);
        Crafting = new CraftWizardViewModel(Wizard, vaults);
        _wizard = new WhenShown(() => { Wizard.Refresh(); Crafting.Refresh(); }, afreshOnShow: true);

        // The crafting wizard's bases are the vault's: a write to it, or another vault opened, counts again.
        vaults.Written += _wizard.Ask;
        vaults.Switched += _wizard.Ask;

        // The other mode chosen elsewhere - Settings' Vault card - lights the pair here too: this page is kept.
        vaults.Switched += () => OnPropertyChanged(nameof(IsHardcore));
        session.Changed += _wizard.Ask;
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded))
                _wizard.Ask();
        };
        if (stash is not null)
        {
            stash.Changed += _wizard.Ask;
            stash.MaterialsChanged += _wizard.Ask;
        }

        // With no character, the stash on disk: read again whenever the save folder changes -
        // the game saving it while it runs.
        session.SaveFolderChanged += () =>
        {
            if (!session.IsLoaded)
                _wizard.Ask();
        };

        Grail = new GrailViewModel(vaults, settings, session, browseOnly: true);
    }

    /// <summary>The page opened: the grail's Discovery read again, and the stash's runes.</summary>
    public void Shown()
    {
        // With no character opened yet, the tables are not loaded: both need them. A failed open has said why.
        if (Session.TryGame() is null && Session.IsConfigured)
            return;

        Grail.Load();
        _wizard.Shown();
    }

    /// <summary>Put away (the view, through PageDeck): the runes are counted on the next visit.</summary>
    public void Hidden() => _wizard.Hidden();

    /// <summary>
    /// What the page will show, worked out while it is still hidden - before it is laid out ahead of its
    /// first visit (MainView.PrepareOtherPages), so that layout is of the rows it will have.
    /// </summary>
    public void Settle()
    {
        // No game folder yet - a first run, before its setup: nothing to work out, the grail's lists are made
        // on its first visit after. Made now they failed on the missing tables, a warning in every first run's log.
        if (Session.EnsureGame() is null)
            return;
        Grail.Load();
        _wizard.Settle();
    }

    private readonly WhenShown _wizard;

}
