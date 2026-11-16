using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// The Items page's right page for its Edit and Grail bookmarks: the bookmark says which it shows (<see cref="Show"/>).
/// Edit is the item panel over what the left page shows (<see cref="Editor"/>): the character's
/// own panel beside the inventory, the vault's beside the vault. Grail lists every unique, set
/// piece and runeword, and a card chosen searches for it. The search itself is a panel over the
/// Inventory and Vault pages (<see cref="SearchPanelViewModel"/>).
/// </summary>
public partial class VP_ViewModel_Vault_R : ViewModelBase
{
    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Vault_R()
        : this(null!, new VaultService(new CharacterSession(), new EditorSettings()), new EditorSettings(), new CharacterSession(),
               new PlayRules(new EditorSettings()), new VP_ViewModel_Inv_R(), null, null, null!) { }

    public VP_ViewModel_Vault_R(ISFXManager sfxManager, VaultService vaults, EditorSettings settings,
                                CharacterSession session, PlayRules rules, VP_ViewModel_Inv_R inventoryEditor,
                                SharedStashSession? stash, VaultMoves? moves, ItemLanding landing,
                                CompendiumSession? compendium = null, CompendiumMoves? deposits = null)
    {
        _vaults = vaults;
        Settings = settings;

        // The inventory's own item panel, over a host of the vault's - its own instance, so its
        // selection is the vault's and never the character's. Beside the inventory, the
        // character's panel instead.
        _vaultEditor = new VP_ViewModel_Inv_R(new VaultEditHost(vaults, session), sfxManager, stash: null, rules);
        _inventoryEditor = inventoryEditor;

        Grail = new GrailViewModel(vaults, settings, session, stash: stash, moves: moves,
                                   compendium: compendium, deposits: deposits);
        Forge = new ForgeViewModel(vaults, session, sfxManager, rules, landing);

        Session = session;

        _rules = rules;
        rules.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanForge));
    }

    private readonly VaultService _vaults;

    private readonly VP_ViewModel_Inv_R _vaultEditor, _inventoryEditor;

    public CharacterSession Session { get; }

    /// <summary>
    /// The Edit page: the item panel over what the left page shows - the item the inventory has
    /// selected, or the one card the vault list has.
    /// </summary>
    public VP_ViewModel_Inv_R Editor => EditsInventory ? _inventoryEditor : _vaultEditor;

    /// <summary>Whether the left page is the inventory, so Edit edits the character's items.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Editor))]
    private bool _editsInventory;

    partial void OnEditsInventoryChanged(bool value)
    {
        // What the Forge keeps goes to the left page: the inventory, or the vault's open folder.
        Forge.KeepsToVault = !value;
    }

    /// <summary>The Grail page: every unique and set piece, found or not.</summary>
    public GrailViewModel Grail { get; }

    /// <summary>The Forge page: an item made from nothing, kept to the left page.</summary>
    public ForgeViewModel Forge { get; }

    private readonly PlayRules _rules;

    /// <summary>
    /// What found is, for the mark beside the Grail page's title: what the game has discovered, read from the
    /// shared stash (<see cref="GrailViewModel"/>). Sets wait for their bits to be known.
    /// </summary>
    public string GrailTip => L("Found: discovered in game") + "\n" + L("Sets: not read yet");

    public EditorSettings Settings { get; }

    /// <summary>
    /// What the right page shows: "Edit", "Grail", "Forge" or "Search". Kept as the last one shown while another
    /// bookmark's page is up, so the page does not change under a fade.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEdit))]
    [NotifyPropertyChangedFor(nameof(IsGrail))]
    [NotifyPropertyChangedFor(nameof(IsForge))]
    [NotifyPropertyChangedFor(nameof(IsSearch))]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _page = "Edit";

    private bool _shown;

    public bool IsEdit => Page == "Edit";

    /// <summary>How several items are picked, under the Edit page's notices (PickMenu.Drags).</summary>
    public string SelectKeys => PickMenu.Drags;
    public bool IsGrail => Page == "Grail";

    /// <summary>The Forge: an item made from nothing, and kept to the left page.</summary>
    public bool IsForge => Page == "Forge";

    /// <summary>The item search: its conditions and what they find (SearchWindowViewModel).</summary>
    public bool IsSearch => Page == "Search";

    /// <summary>The Search page. The main view model's, which makes it: it goes to the finds' pages.</summary>
    [ObservableProperty] private SearchWindowViewModel? _search;

    /// <summary>Whether the play mode has the Forge: its bookmark is there only then.</summary>
    public bool CanForge => _rules.Forges;

    /// <summary>The page's heading.</summary>
    public string Title => L(Page);

    /// <summary>
    /// The bookmark taken: Edit, Grail or Forge shown, or null for another bookmark's page. Leaving the
    /// grail lets go of the card chosen there, and of the search it made.
    /// </summary>
    public void Show(string? page)
    {
        if (_shown && IsGrail && page != "Grail")
            Grail.Release();

        _shown = page is not null;
        if (page is not null)
            Page = page;

        // Made the first time it is shown: it needs the tables, and there is no hurry before.
        if (_shown && IsGrail)
            Grail.Load();
        if (_shown && IsForge)
            Forge.Load();

        // What it owes, counted as it comes in front; behind another bookmark it counts nothing.
        if (_shown && IsGrail)
            Grail.Shown();
        else
            Grail.Hidden();
    }
}
