using System.Linq;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Generic;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    #region Fields and Members
    private readonly Dictionary<string, ViewModelBase> _dict_ViewPages;

    // Null under the designer constructor, which gets no services; every use is guarded.
    private readonly ISFXManager _sfxManager = null!;

    /// <summary>Exposed so the sidebar can disable pages that need an open character.</summary>
    public CharacterSession Session { get; }

    private readonly VP_ViewModel_Experimental_L? _extra;

    /// <summary>
    /// Whether the Experimental sidebar button wears its red dot: something from the page is installed
    /// in the game folder (density, packs, the cow portal, drops) and the page's online notice has not
    /// been dismissed. Dismissing the notice takes the dot with it, for good.
    /// </summary>
    public bool ExperimentalMarked => _extra?.ShowsOnlineNotice == true;

    public string ExperimentalTip => ExperimentalMarked
        ? ShortcutText.With(L("Experimental"), L("Ctrl + 5")) + Environment.NewLine + VP_ViewModel_Experimental_L.OnlineNotice
        : ShortcutText.With(L("Experimental"), L("Ctrl + 5"));

    /// <summary>
    /// Whether the top bar's character strip is out. It follows <see cref="CharacterSession.IsLoaded"/>
    /// but lets a reload through: a load clears that first, on purpose, and sets it again when done -
    /// and the strip, following it directly, slid out and back in on every reload, since a transition
    /// starts from the value it was given, not from what is on the screen. So it goes only if the
    /// character is still gone once the load has run: a close, or a load that failed.
    /// </summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isCharacterShown;


    /// <summary>
    /// The landing page, whose reload, save, undo and close the top bar offers from every page -
    /// the same commands, labels and conditions, so the two never disagree.
    /// </summary>
    public VP_ViewModel_Default_L Home { get; }

    /// <summary>The overlay question, asked from here and from the landing page.</summary>
    public ConfirmService Confirm { get; }

    /// <summary>The release notes' popup, over everything as the confirm question is.</summary>
    public PD2SPCompanion.Shell.Services.Settings.WhatsNewService? WhatsNew { get; }

    /// <summary>The sheet of keys and gestures, over the whole window (F1; Settings' About card).</summary>
    public PD2SPCompanion.Shell.Services.Settings.KeySheetService Keys => PD2SPCompanion.Shell.Services.Settings.KeySheetService.Shared;

    /// <summary>Saves the character by itself - flushed before quitting. Null in the designer.</summary>
    public AutoSave? AutoSave { get; }

    /// <summary>Kept in step with the game - see GameSync. Null in the designer.</summary>
    public GameSync? Sync { get; }

    /// <summary>
    /// The first run's question - where the game is. Over everything until answered, and
    /// on a machine that has already answered it, never seen.
    /// </summary>
    public GameFolderSetup Setup { get; }
    #endregion

    /// <summary>The inventory page, whose carried items the window draws over both pages (the ghost).</summary>
    public VP_ViewModel_Inv_L? Inventory { get; }

    /// <summary>The vault list, whose carried cards the window draws the same way.</summary>
    public VP_ViewModel_Vault_L? Vault { get; }

    /// <summary>
    /// The right page's vault page: the one beside the inventory, and the second of the two where the vault is
    /// on both pages (VP_ViewModel_Vault_L.Twin.cs).
    /// </summary>
    public VP_ViewModel_Vault_L? RightVault { get; }

    /// <summary>Cards of the vault are being dragged, from either vault page: the window draws them under the pointer.</summary>
    public bool IsVaultCarrying => Vault?.IsCarrying == true || RightVault?.IsCarrying == true;

    public IReadOnlyList<PD2SPCompanion.Shell.ViewModels.GhostPart> VaultGhostParts =>
        Vault?.IsCarrying == true ? Vault.GhostParts : RightVault?.IsCarrying == true ? RightVault.GhostParts : [];

    /// <summary>The vault's folders carried with them, or alone: tiles of a folder's sheet (VP_ViewModel_Vault_L.GhostFolders).</summary>
    public IReadOnlyList<PD2SPCompanion.Shell.ViewModels.FolderGhost> VaultGhostFolders =>
        Vault?.IsCarrying == true ? Vault.GhostFolders : RightVault?.IsCarrying == true ? RightVault.GhostFolders : [];

    /// <summary>A gem dragged out of the Edit page's row of sockets: drawn under the pointer as the rest that is carried is.</summary>
    public PD2SPCompanion.Shell.Services.Generic.SocketCarry SocketCarry => PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared;

    /// <summary>Where the ghost of what is carried is drawn, in the window's coordinates.</summary>
    [ObservableProperty] private double _ghostLeft;
    [ObservableProperty] private double _ghostTop;

    #region MVVM Properties
    [ObservableProperty]
    private ViewModelBase _currentPage_L = null!;
    [ObservableProperty]
    private ViewModelBase _currentPage_R = null!;

    //UI Info
    [ObservableProperty]
    private bool _button_Char_isChecked;
    [ObservableProperty]
    private bool _button_Inventory_isChecked;
    [ObservableProperty]
    private bool _button_Extra_isChecked;
    [ObservableProperty]
    private bool _button_Experimental_isChecked;
    [ObservableProperty]
    private bool _button_Settings_isChecked;
    #endregion

    #region Constructor
    /// <summary>
    /// This is used for designer, and will require service through service locator pattern.
    /// </summary>
    public MainViewModel()
    {
        _dict_ViewPages = new Dictionary<string, ViewModelBase>
        {
            { "Default_L"   , Home = new VP_ViewModel_Default_L() },
            { "Default_R"   , new VP_ViewModel_Default_R() },

            { "Char_L"      , new VP_ViewModel_Char_L() },
            { "Char_R"      , new VP_ViewModel_Char_R() },

            { "Inventory_L" , new VP_ViewModel_Inv_L() },

            { "Vault_L"     , new VP_ViewModel_Vault_L() },
            { "Vault_Right" , new VP_ViewModel_Vault_L { PageName = VP_ViewModel_Vault_L.RightPage } },
            { "Vault_R"     , new VP_ViewModel_Vault_R() },

            { "Extra_L"      , new VP_ViewModel_Extra_L() },
            { "Trade"       , new VP_ViewModel_Trade() },
            { "Extra_R"      , new VP_ViewModel_Extra_R() },

            { "Experimental_L"     , new VP_ViewModel_Experimental_L() },
            { "Experimental_R"     , new VP_ViewModel_Experimental_R() },

            { "Settings_L"  , new VP_ViewModel_Settings_L() },
            { "Settings_R"  , new VP_ViewModel_Settings_R() },
        };

        Session = new CharacterSession();
        Confirm = new ConfirmService();
        Setup = new GameFolderSetup(new EditorSettings(), Session);

        CurrentPage_L       = _dict_ViewPages["Default_L"];
        CurrentPage_R       = _dict_ViewPages["Default_R"];
    }

    private readonly PatchGuardService? _guard;
    private readonly PD2SPCompanion.Shell.Services.Vault.SeasonMigrationService? _migration;
    private readonly PD2SPCompanion.Shell.Services.Settings.EditorSettings? _settings;

    /// <summary>
    /// What the start asks once a game folder is known, each posted in turn: the saves checked against the installed PD2
    /// before anything writes them, then a new season with older vaults
    /// not migrated, asked once. A failure is said on the strip: in a
    /// posted call, it would end the program.
    /// </summary>
    public void PostStartChecks()
    {
        void Asked(Action check) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                check();
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                Session.Messages.Post(problem.Message);
            }
        }, Avalonia.Threading.DispatcherPriority.Background);

        if (_guard is { } guard)
            Asked(guard.CheckAtStart);
        if (_migration is { } migration)
            Asked(migration.CheckAtStart);

        // A settings file that would not read: said once, where its copy is.
        if (_settings?.TakeUnreadableCopy() is { } copy)
            Asked(() => Session.Messages.Post(L($"Settings not read. Copy kept: {copy}")));
    }

    /// <summary>
    /// Main Constructor
    /// </summary>
    public MainViewModel(
        ISFXManager                 sfxManager,
        CharacterSession            session,
        // Made before the pages, which mark their tiles through it.
        PD2SPCompanion.Shell.Services.Vault.DuplicateIndex duplicates,
        PD2SPCompanion.Shell.Services.Vault.VaultService vaults,
        ConfirmService              confirm,
        GameFolderSetup             setup,
        GameSync                    sync,
        AutoSave                    autoSave,
        PD2SPCompanion.Shell.Services.Vault.SeasonMigrationService migration,
        PD2SPCompanion.Shell.Services.Settings.WhatsNewService whatsNew,
        PatchGuardService guard,
        PD2SPCompanion.Shell.Services.Settings.EditorSettings settings,
        //ViewPages
        VP_ViewModel_Default_L      vp_VM_Default_L,
        VP_ViewModel_Default_R      vp_VM_Default_R,
        VP_ViewModel_Char_L         vp_VM_Char_L,
        VP_ViewModel_Char_R         vp_VM_Char_R,
        VP_ViewModel_Inv_L          vp_VM_Inv_L,
        VP_ViewModel_Vault_L        vp_VM_Vault_L,
        [Microsoft.Extensions.DependencyInjection.FromKeyedServices(PD2SPCompanion.Shell.Helper.ViewLocator.MainViewPage_ViewLocator.RightSide)]
        VP_ViewModel_Vault_L        vp_VM_Vault_Right,
        VP_ViewModel_Vault_R        vp_VM_Vault_R,
        VP_ViewModel_Extra_L         vp_VM_Extra_L,
        VP_ViewModel_Trade          vp_VM_Trade,
        VP_ViewModel_Extra_R         vp_VM_Extra_R,
        VP_ViewModel_Experimental_L        vp_VM_Experimental_L,
        VP_ViewModel_Experimental_R        vp_VM_Experimental_R,
        VP_ViewModel_Settings_L     vp_VM_Settings_L,
        VP_ViewModel_Settings_R     vp_VM_Settings_R)
    {
        _sfxManager = sfxManager;
        _extra = vp_VM_Experimental_L;

        // The Experimental page's files in the game folder, marked on its sidebar button until the
        // page's online notice is dismissed (ShowsOnlineNotice follows IsInstalled and the dismissal).
        vp_VM_Experimental_L.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(VP_ViewModel_Experimental_L.ShowsOnlineNotice))
            {
                OnPropertyChanged(nameof(ExperimentalMarked));
                OnPropertyChanged(nameof(ExperimentalTip));
            }
        };
        Session = session;
        Confirm = confirm;
        Setup = setup;
        _guard = guard;
        _migration = migration;
        _settings = settings;

        // A first run, or a folder that has since gone: the start's checks found no folder to look at, and nothing else
        // asks them. Writes held from now until they have run.
        setup.Settled += () =>
        {
            guard.HoldUntilChecked();
            PostStartChecks();
        };

        // A duplicate badge clicked: the pages that hold its copies, side by side.
        duplicates.CopiesShown += ShowCopies;

        // The new-season notice's MIGRATE: Settings, where the page opens its migration panel.
        migration.Wanted += ShowSettings;

        WhatsNew = whatsNew;

        // The Forge's bookmark comes and goes with the play mode; gone while its page is up, Edit takes
        // the page back.
        vp_VM_Vault_R.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName != nameof(VP_ViewModel_Vault_R.CanForge))
                return;

            OnPropertyChanged(nameof(CanForge));
            if (!CanForge && IsRightForge)
            {
                RightBookmark = EditMark;
                if (Button_Inventory_isChecked)
                    ShowItems();
            }
        };

        QuickSearch = new QuickSearchViewModel(this, vaults, sfxManager);
        // The item search: the form the pages share, over the one search, and what it finds - on the right
        // page, under its Search bookmark. The window's box is a search apart: quick,
        // and gone with its list.
        SearchWindow = new SearchWindowViewModel(this, vaults,
            vp_VM_Inv_L.Search ?? new SearchPanelViewModel(sfxManager, vaults, new PD2SPCompanion.Shell.Services.Settings.EditorSettings()),
            sfxManager);
        vp_VM_Vault_R.Search = SearchWindow;
        SearchWindow.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(SearchWindowViewModel.Keeps) or nameof(SearchWindowViewModel.KeptSummary))
            {
                OnPropertyChanged(nameof(SearchKeeps));
                OnPropertyChanged(nameof(SearchBookmarkHint));
                OnPropertyChanged(nameof(SearchBookmarkTip));
            }
        };

        // A find of the search's list gone to: its page put in sight first.
        Results = vp_VM_Inv_L.Search?.Results;
        if (Results is not null)
        {
            // The panel is up on every page: a tool pressed from another page has the Items page
            // shown first, and with no character open - no Items page - its tools are dim. (A find gone to
            // shows its own page: GoToFind.)
            Results.SelectFoundRequested += () =>
            {
                ShowItemsPage();
                SelectFound();
            };
            Results.MoveFoundRequested += () =>
            {
                ShowItemsPage();
                MoveFound();
            };
        }
        Sync = sync;
        AutoSave = autoSave;

        // The Trade bookmark's Sell tab sells the one item selected on the left page: the inventory's, told
        // here, or beside the vault the card selected there (VP_ViewModel_Trade.WithVault).
        vp_VM_Inv_L.SellChoiceChanged += vp_VM_Trade.Follow;
        _trade = vp_VM_Trade;

        // The inventory is the left page's, so the vault beside it is the right page's vault page: what the two
        // pass between them is wired to that one. Dragged from the inventory onto it: stored.
        vp_VM_Vault_Right.InventoryDropped += vp_VM_Inv_L.StoreCarried;
        Inventory = vp_VM_Inv_L;

        // The right click's two questions the pages cannot answer alone: whether the vault is beside the
        // inventory (Shift + right click moves across only then), and which worn slot takes an item of the
        // vault's. The left vault page is never beside it.
        vp_VM_Inv_L.VaultIsBeside = () => IsRightVault;
        vp_VM_Vault_L.IsOnRight = () => false;
        vp_VM_Vault_Right.IsOnRight = () => IsRightVault && IsLeftInventory;
        vp_VM_Vault_Right.SlotAt = vp_VM_Inv_L.SlotAt;
        vp_VM_Vault_L.EquipChoice = vp_VM_Vault_Right.EquipChoice = vp_VM_Inv_L.EquipChoice;

        // The clipboard across the pages: a cut of the vault's pasted on the inventory is withdrawn, a cut of the
        // character's pasted on a vault page is stored into the folder pasted in.
        vp_VM_Inv_L.WithdrawPasted = vp_VM_Vault_Right.WithdrawKeys;
        vp_VM_Inv_L.WearPasted = vp_VM_Vault_Right.WearKeys;
        vp_VM_Vault_L.StorePasted = vp_VM_Vault_Right.StorePasted = vp_VM_Inv_L.StorePasted;
        vp_VM_Vault_Right.StoreDisplaced = vp_VM_Inv_L.StoreInto;

        // The two vault pages, each told of the other; whether the vault is on both, and the left page's
        // button that puts it there; and whose folder takes what is stored from elsewhere (_arrivals).
        vp_VM_Vault_L.Twin = vp_VM_Vault_Right;
        vp_VM_Vault_Right.Twin = vp_VM_Vault_L;
        vp_VM_Vault_L.Compares = vp_VM_Vault_Right.Compares = () => IsComparing;
        vp_VM_Vault_L.InSight = () => Button_Inventory_isChecked && IsLeftVault;
        vp_VM_Vault_Right.InSight = () => Button_Inventory_isChecked && IsRightVault;
        vp_VM_Vault_L.CompareRequested = ToggleCompare;
        vp_VM_Vault_L.TakesArrivals = () => _arrivals == VP_ViewModel_Vault_L.LeftPage;
        vp_VM_Vault_Right.TakesArrivals = () => _arrivals == VP_ViewModel_Vault_L.RightPage;
        _vaults = vaults;

        // What an item's tooltip says can be done with it, and what it is compared with (TipFor); the
        // comparison on or off as the settings left it, and kept there.
        var kept = vp_VM_Inv_L.Rules.Settings;
        PD2SPCompanion.Shell.Services.Generic.TipActions.Compares = kept.CompareOnHover;
        PD2SPCompanion.Shell.Services.Generic.TipActions.Keep = on => kept.CompareOnHover = on;
        PD2SPCompanion.Shell.Services.Generic.TipActions.Provider = TipFor;

        // Dragged from the vault onto a cell of the inventory: withdrawn there.
        vp_VM_Inv_L.VaultCarrying = () => vp_VM_Vault_Right.IsCarrying;
        vp_VM_Inv_L.VaultDropRequested += vp_VM_Vault_Right.WithdrawCarried;

        // Dragged from the vault onto a worn slot: withdrawn and put on.
        vp_VM_Inv_L.VaultCarriedItem = () => vp_VM_Vault_Right.CarriedItem;
        vp_VM_Inv_L.VaultCarriedLead = () => vp_VM_Vault_Right.CarriedLead;
        vp_VM_Inv_L.VaultSlotDropRequested += vp_VM_Vault_Right.WearCarried;
        vp_VM_Vault_Right.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName != nameof(VP_ViewModel_Vault_L.IsCarrying))
                return;

            vp_VM_Inv_L.VaultCarryChanged();
            OnPropertyChanged(nameof(IsVaultCarrying));
            OnPropertyChanged(nameof(VaultGhostParts));
            OnPropertyChanged(nameof(VaultGhostFolders));
        };
        vp_VM_Vault_L.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName != nameof(VP_ViewModel_Vault_L.IsCarrying))
                return;

            OnPropertyChanged(nameof(IsVaultCarrying));
            OnPropertyChanged(nameof(VaultGhostParts));
            OnPropertyChanged(nameof(VaultGhostFolders));
        };
        Vault = vp_VM_Vault_L;
        RightVault = vp_VM_Vault_Right;

        IsCharacterShown = session.IsLoaded;
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName != nameof(CharacterSession.IsLoaded))
                return;

            OnPropertyChanged(nameof(CanRightVault));
            OnPropertyChanged(nameof(CharacterPageTip));
            OnPropertyChanged(nameof(ItemsPageTip));
            OnPropertyChanged(nameof(InventoryBookmarkTip));
            OnPropertyChanged(nameof(VaultBookmarkTip));
            OnPropertyChanged(nameof(RightVaultBookmarkTip));

            if (session.IsLoaded)
                IsCharacterShown = true;
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () =>
                    {
                        IsCharacterShown = session.IsLoaded;
                        if (!session.IsLoaded)
                            CharacterGone();
                    },
                    Avalonia.Threading.DispatcherPriority.Background);
        };

        // The game closing a character: where the player was, to put them back there when that
        // character is loaded again.
        if (sync is not null)
        {
            sync.ClosingForGame += path => _resume = (path,
                Button_Char_isChecked ? "Char" : Button_Inventory_isChecked ? "Inventory" : Button_Extra_isChecked ? "Extra" : null,
                LeftBookmark, RightBookmark);
        }

        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded) && session.IsLoaded
                && _resume is { } resume && (resume.Path.Length == 0 || resume.Path == session.SavePath))
            {
                _resume = null;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Resume(resume), Avalonia.Threading.DispatcherPriority.Background);
            }
        };

        //ViewPages
        _dict_ViewPages = new Dictionary<string, ViewModelBase>
        {
            { "Default_L"   , Home = FollowedByTheBox(vp_VM_Default_L) },
            { "Default_R"   , vp_VM_Default_R },

            { "Char_L"      , vp_VM_Char_L },
            { "Char_R"      , vp_VM_Char_R },

            { "Inventory_L" , vp_VM_Inv_L },

            { "Vault_L"     , vp_VM_Vault_L },
            { "Vault_Right" , vp_VM_Vault_Right },
            { "Vault_R"     , vp_VM_Vault_R },

            { "Extra_L"      , vp_VM_Extra_L },
            { "Trade"       , vp_VM_Trade },
            { "Extra_R"      , vp_VM_Extra_R },

            { "Experimental_L"     , vp_VM_Experimental_L },
            { "Experimental_R"     , vp_VM_Experimental_R },

            { "Settings_L"  , vp_VM_Settings_L },
            { "Settings_R"  , vp_VM_Settings_R },
        };

        CurrentPage_L       = _dict_ViewPages["Default_L"];
        CurrentPage_R       = _dict_ViewPages["Default_R"];

        // A backup put on screen - from Settings, where the list is - is shown on the card that restores it.
        Home.BackupShown += ShowHome;
    }
    #endregion

    #region Commands
    /// <summary>
    /// The sidebar logo returns to the landing page.
    /// <para>
    /// The toggles are cleared through their bound properties rather than by name, which is
    /// why those bindings are TwoWay: OneWayToSource lets the view push a change up but
    /// gives no way to push one back down, so the buttons would stay lit after going home.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void GoHome()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        ShowHome();
    }

    /// <summary>The landing page shown, no side bar button lit: the logo's press, and a backup put on screen from Settings.</summary>
    private void ShowHome()
    {
        Button_Char_isChecked      = false;
        Button_Inventory_isChecked = false;
        Button_Extra_isChecked      = false;
        Button_Experimental_isChecked     = false;
        Button_Settings_isChecked  = false;

        CurrentPage_L = _dict_ViewPages["Default_L"];
        CurrentPage_R = _dict_ViewPages["Default_R"];
    }

    /// <summary>
    /// Goes to a page as its side bar button would, the button lit and the others not. "Inventory"
    /// is the Items page with the inventory on the left.
    /// </summary>
    private void ShowPage(string page)
    {
        Button_Char_isChecked      = page == "Char";
        Button_Inventory_isChecked = page == "Inventory";
        Button_Extra_isChecked      = page == "Extra";
        Button_Experimental_isChecked     = false;
        Button_Settings_isChecked  = false;

        switch (page)
        {
            case "Char": SwitchChar(); break;
            case "Inventory":
                LeftBookmark = InventoryMark;
                SwitchInventory();
                break;
            case "Extra": SwitchExtra(); break;
            case "Experimental":
                Button_Experimental_isChecked = true;
                SwitchExperimental();
                break;
            case "Settings":
                ShowSettings();
                break;
        }
    }

    /// <summary>Goes to Settings as its side bar button would, the button lit and the others not.</summary>
    private void ShowSettings()
    {
        if (Button_Settings_isChecked)
            return;

        Button_Char_isChecked      = false;
        Button_Inventory_isChecked = false;
        Button_Extra_isChecked      = false;
        Button_Experimental_isChecked     = false;
        Button_Settings_isChecked  = true;
        SwitchSettings();
    }

    /// <summary>Where the player was when the game closed their character - see <see cref="Resume"/>.</summary>
    private (string Path, string? Page, string Left, string Right)? _resume;

    /// <summary>The character the game closed, loaded again: back on the page and bookmarks it was left on.</summary>
    private void Resume((string Path, string? Page, string Left, string Right) resume)
    {
        // The page first: showing Items puts the inventory on the left, which the bookmarks then undo.
        if (resume.Page is { } page)
            ShowPage(page);

        LeftBookmark = resume.Left;

        // What the play mode allows now: it may have changed while the game had the character.
        RightBookmark = resume.Right == ForgeMark && !CanForge || resume.Right == VaultMark && !CanRightVault
            ? EditMark
            : resume.Right;
        if (Button_Inventory_isChecked)
            ShowItems();
    }

    // ---- the Items page's bookmarks ----------------------

    /// <summary>The bookmarks' names, as the commands are handed them from the view.</summary>
    public const string InventoryMark = "Inventory", VaultMark = "Vault", EditMark = "Edit", GrailMark = "Grail", TradeMark = "Trade",
                        ForgeMark = "Forge", SearchMark = "Search";

    /// <summary>
    /// What the Items page's left page shows: the inventory or the vault list. Kept while other
    /// pages are shown, so the page comes back as it was left.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLeftInventory))]
    [NotifyPropertyChangedFor(nameof(IsLeftVault))]
    [NotifyPropertyChangedFor(nameof(IsComparing))]
    private string _leftBookmark = InventoryMark;

    /// <summary>
    /// What an item's tooltip says of the item where it is, and what it would be compared with
    /// (TipActions, ItemActionTip). The right click's chords - RMB,
    /// Alt + RMB, Ctrl + RMB, Shift + RMB, Ctrl + Shift + RMB, each with what it would do here - are rows of
    /// the item's menu, which says itself what it does: the strip names the menu, and the comparison's keys.
    /// <para>
    /// <b>The comparison</b> (Tab) shows what is worn where the item would go - both pieces where a weapon held
    /// in both hands takes two off, or "Empty" - and the keys alone change it: Alt the next slot that takes it,
    /// Ctrl the mercenary and back.
    /// </para>
    /// </summary>
    private PD2SPCompanion.Shell.Services.Generic.TipInfo? TipFor(ItemTileViewModel tile, string origin)
    {
        if (!Session.IsLoaded || !Button_Inventory_isChecked || Inventory is not { } inventory)
            return null;

        var hints = new List<PD2SPCompanion.Shell.Services.Generic.TipHint>();
        void Say(string keys, string does) => hints.Add(new(keys, does));

        // A count in the materials tab has a menu only while a pane lies across for it to go to.
        if (tile.TabSlot is not null && !inventory.HasPaneAcross(tile))
            return null;

        Say(L("RMB"), L("Menu"));

        // Worn, a count of the tab, or a vault card with no inventory beside it to put it on: nothing to compare.
        bool card = origin == PD2SPCompanion.Shell.Services.Generic.TipActions.Vault;
        if (origin == PD2SPCompanion.Shell.Services.Generic.TipActions.Worn || tile.TabSlot is not null
            || card && !(IsRightVault && IsLeftInventory))
        {
            return new(hints, false, [], "", "");
        }

        var mine = inventory.SlotsFor(tile.Item, mercenary: false).Slots;
        var hired = inventory.SlotsFor(tile.Item, mercenary: true).Slots;
        bool offers = mine.Count > 0 || hired.Count > 0;
        bool open = offers && PD2SPCompanion.Shell.Services.Generic.TipActions.Compares;

        // What putting it on that slot takes off, as tiles of the page's own.
        IReadOnlyList<ItemTileViewModel> Off(EquipSlotViewModel slot, bool mercenary)
        {
            var worn = Session.Equipped.Concat(Session.MercEquipped).Select(each => each.Item).OfType<ItemTileViewModel>().ToList();
            return [.. Session.PushedOffBy(tile.Item, slot.Slot.Location, mercenary)
                .Select(item => worn.FirstOrDefault(each => ReferenceEquals(each.Item, item))).OfType<ItemTileViewModel>()];
        }

        IReadOnlyList<ItemTileViewModel> compared = [];
        string caption = "";
        if (open)
        {
            // Whose, as asked - and the other's where the one asked for cannot wear it.
            bool mercenary = PD2SPCompanion.Shell.Services.Generic.TipActions.Mercenary ? hired.Count > 0 : mine.Count == 0;
            var slots = mercenary ? hired : mine;
            int index = PD2SPCompanion.Shell.Services.Generic.TipActions.Slot % slots.Count;
            PD2SPCompanion.Shell.Services.Generic.TipActions.Settle(mercenary, index);

            var target = slots[index];
            if (slots.Count > 1)
                Say(L("Alt"), L("Next slot"));

            if (mine.Count > 0 && hired.Count > 0)
                Say(L("Ctrl"), mercenary ? L("Character") : L("Mercenary"));

            compared = Off(target, mercenary);
            caption = mercenary ? L($"Mercenary - {target.Label}") : L($"Equipped - {target.Label}");
        }

        if (offers)
            Say(L("Tab"), open ? L("Hide comparison") : L("Compare"));

        return new(hints, offers, compared, caption, L("Empty"));
    }

    /// <summary>The Trade page, told which page is beside it: it trades with that one.</summary>
    private VP_ViewModel_Trade? _trade;

    partial void OnLeftBookmarkChanged(string value)
    {
        if (_trade is { } trade)
            trade.WithVault = IsLeftVault;
    }

    /// <summary>What the right page shows: Utilities (search, edit, grail) or the vault list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRightEdit))]
    [NotifyPropertyChangedFor(nameof(IsRightForge))]
    [NotifyPropertyChangedFor(nameof(IsRightGrail))]
    [NotifyPropertyChangedFor(nameof(IsRightVault))]
    [NotifyPropertyChangedFor(nameof(IsRightTrade))]
    [NotifyPropertyChangedFor(nameof(IsRightSearch))]
    [NotifyPropertyChangedFor(nameof(IsComparing))]
    private string _rightBookmark = EditMark;

    /// <summary>
    /// The Search bookmark taken or left, however - its bookmark, the window's search box, a duplicate badge, a
    /// character closed: the item search asks while it is the right page and no longer (SearchWindowViewModel).
    /// </summary>
    partial void OnRightBookmarkChanged(string? oldValue, string newValue)
    {
        if (oldValue == SearchMark && newValue != SearchMark)
            SearchWindow?.Leave();
        else if (newValue == SearchMark && oldValue != SearchMark)
            SearchWindow?.Enter();
    }

    // ---- new is not select ----------------------------------------------
    //
    // The Forge and Edit are one item panel in two states - an item being made, an item selected - and they
    // look alike, so the Forge shown beside a selected item read as that item being edited. The rule:
    // new is not edit or select. The Forge never stands beside a selection:
    //   - taking the Forge clears the left page's selection;
    //   - selecting on the left while the Forge is up takes the right page to Edit - one item or several
    //     picked - and the Forge keeps what it holds, its bookmark marked until it is gone back to.
    // Only the Forge: Trade, the Grail and the vault are tools of their own and stay where they are (Trade's
    // Sell uses the selection). A vault folder is no item, and is walked into with the Forge up to choose where
    // FORGE puts things. What FORGE makes lands unselected, so forging several never leaves the page.

    /// <summary>The Forge left by a selection, still holding its item: its bookmark is marked, and says what it holds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForgeBookmarkHint))]
    [NotifyPropertyChangedFor(nameof(ForgeBookmarkTip))]
    private bool _forgeWaits;

    private ForgeViewModel? Forge => (_dict_ViewPages["Vault_R"] as VP_ViewModel_Vault_R)?.Forge;

    /// <summary>
    /// The Edit page in sight with a jewel's layer standing on it, the vault's as well as the Items page's: what Esc goes
    /// back from before anything else lets go of the item under it. Null where there is none.
    /// </summary>
    public VP_ViewModel_Inv_R? EditLayerInSight =>
        Button_Inventory_isChecked && IsRightEdit && _dict_ViewPages["Vault_R"] is VP_ViewModel_Vault_R { Editor: { Nested: not null } panel }
            ? panel
            : null;

    public string ForgeBookmarkHint => ForgeWaits && Forge?.Draft is { } held
        ? L("Forge") + Environment.NewLine + L($"Holding {held.Name}")
        : L("Forge");

    partial void OnRightBookmarkChanged(string value)
    {
        if (value != ForgeMark)
            return;

        // However the Forge was come to - its bookmark, Ctrl+Tab, a character resumed.
        ForgeWaits = false;
        ClearLeftSelection();
    }

    private bool LeftHasSelection => IsLeftVault ? Vault?.HasActing == true : Inventory?.HasItemSelection == true;

    /// <summary>Whether the left page is carrying something - an item dragged, cards, a count out of the materials tab.</summary>
    public bool LeftIsCarrying => Inventory?.IsTakingDrops == true || IsVaultCarrying;

    private void ClearLeftSelection()
    {
        if (IsLeftVault)
            Vault?.ClearSelection();
        else
            Inventory?.ClearSelection();
    }

    /// <summary>
    /// A press let go of, or a key, with the Forge up (the view, once the release has been dealt with): what it
    /// selected on the left takes the right page to Edit. <b>A drag selects what it carries</b>, as Explorer's
    /// does - and moving a forged item to its place is not asking to see it - so what a carry selected is let go
    /// of instead, and the Forge stays (<paramref name="carried"/>: whether one was under way at the release).
    /// </summary>
    public void SettleForge(bool carried)
    {
        if (!IsRightForge || !Button_Inventory_isChecked || !Session.IsLoaded || !LeftHasSelection)
            return;

        if (carried)
        {
            ClearLeftSelection();
            return;
        }

        bool holds = Forge?.HasDraft == true;
        RightBookmark = EditMark;
        ForgeWaits = holds;
        ShowItems();
    }

    // ---- a search's finds, on whichever page (how would a user know there is still a search result in the left
    // vault page when seeing only the inventory? - a green bookmark then, and
    // one list of them all) ---------------------------------------------------------------------------

    /// <summary>What the one search found, listed in a panel over the pages (<see cref="SearchResultsViewModel"/>).</summary>
    public SearchResultsViewModel? Results { get; }

    /// <summary>The window's own search, in its top row (<see cref="QuickSearchViewModel"/>).</summary>
    public QuickSearchViewModel? QuickSearch { get; }

    /// <summary>
    /// A find gone to from whatever page is up: the Items page shown, the find's page put in sight on it, its
    /// container or folder opened and the item selected.
    /// <para>
    /// <paramref name="toEdit"/>, for a find of the window's search box (search is exclusive: a jump from the
    /// search bar's list temporarily lifts the search bookmark and the grail search): the right page becomes Edit where it was Search, the Grail or the Forge. The
    /// Search page and a grail card each dim what their own search leaves out, and the item gone to may be
    /// one of those; leaving their page lifts that (the Search page keeps its conditions, its bookmark
    /// marked), and the item is shown on Edit as any selected item is. Beside the vault or Trade the right
    /// page stays: neither dims anything, and both work on what is selected.
    /// </para>
    /// </summary>
    public void GoToFind(FoundItem found, bool toEdit = false)
    {
        if (!Session.IsLoaded)
        {
            Session.Status = L("Require open character.");
            return;
        }

        ShowItemsPage();
        if (toEdit && (IsRightSearch || IsRightGrail || IsRightForge))
        {
            bool holds = IsRightForge && Forge?.HasDraft == true;
            RightBookmark = EditMark;
            ForgeWaits = holds;
            ShowItems();
        }

        ShowPageOf(found);
        found.Jump();
    }

    /// <summary>
    /// A folder of the vault gone to from whatever page is up - a find of the window's search box by its name:
    /// the Items page shown, the vault on its left page where neither page shows it, and the
    /// folder opened on the vault page in sight.
    /// </summary>
    public void GoToVaultFolder(long folder)
    {
        if (!Session.IsLoaded)
        {
            Session.Status = L("Require open character.");
            return;
        }

        ShowItemsPage();
        if (!IsLeftVault && !IsRightVault)
            GoLeft(VaultMark);

        (IsLeftVault ? Vault : RightVault)?.GoTo(folder);
    }

    /// <summary>The Search page was left with conditions set: its bookmark is marked, and says what it keeps.</summary>
    public bool SearchKeeps => SearchWindow?.Keeps == true;

    public string SearchBookmarkHint => SearchKeeps && SearchWindow is { } search
        ? L("Search") + Environment.NewLine + search.KeptSummary
        : L("Search");

    /// <summary>The home page, once it is there: the window's search box says whose file it is opening (QuickSearchViewModel.BoxHint).</summary>
    private VP_ViewModel_Default_L FollowedByTheBox(VP_ViewModel_Default_L home)
    {
        home.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(home.IsLoading))
                QuickSearch?.SayLoading();
        };
        return home;
    }

    /// <summary>The Items page shown, as its side bar button would - left as it is where it is up already.</summary>
    public void ShowItemsPage()
    {
        if (Session.IsLoaded && !Button_Inventory_isChecked)
            ShowPage("Inventory");
    }

    /// <summary>The item search, on the right page under its Search bookmark (<see cref="SearchWindowViewModel"/>).</summary>
    public SearchWindowViewModel? SearchWindow { get; }

    /// <summary>
    /// The item search's page shown from anywhere - an item group's "See all results" in the window's search box,
    /// Ctrl + Enter there - and then asked something: the Items page, its right page on the Search bookmark.
    /// A character's page, so with none open it says so.
    /// </summary>
    public void ShowSearch(Action<SearchWindowViewModel>? then = null)
    {
        if (SearchWindow is not { } search)
            return;

        if (!Session.IsLoaded)
        {
            Session.Status = L("Require open character.");
            return;
        }

        ShowItemsPage();
        if (!IsRightSearch)
        {
            RightBookmark = SearchMark;
            ShowItems();
        }

        then?.Invoke(search);
    }

    /// <summary>The name a page of the window's is kept under ("Settings_L", "Inventory_R"...), or null for none of them.</summary>
    public string? PageKeyOf(object page) =>
        _dict_ViewPages.FirstOrDefault(each => ReferenceEquals(each.Value, page)).Key;

    /// <summary>
    /// A page shown by the name it is kept under - a setting or a section of the window's search gone to: its
    /// side bar page, and on the Items page its bookmark. False for a page that cannot be shown just now (a
    /// character's, with none open).
    /// </summary>
    public bool ShowPageByKey(string key)
    {
        string page = key.Split('_')[0];
        if (page is "Char" or "Inventory" && !Session.IsLoaded)
            return false;

        switch (page)
        {
            case "Char":
                if (!Button_Char_isChecked)
                    ShowPage("Char");
                return true;
            case "Extra":
                if (!Button_Extra_isChecked)
                    ShowPage("Extra");
                return true;
            case "Settings":
                ShowSettings();
                return true;
            case "Experimental":
                if (!Button_Experimental_isChecked)
                {
                    Button_Char_isChecked = Button_Inventory_isChecked = Button_Extra_isChecked = Button_Settings_isChecked = false;
                    Button_Experimental_isChecked = true;
                    SwitchExperimental();
                }
                return true;
            case "Inventory":
                ShowItemsPage();
                if (key.EndsWith("_L", StringComparison.Ordinal))
                    GoLeft(InventoryMark);
                else
                    GoRight(EditMark);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The pages and the Items page's bookmarks, as sections the window's search finds by name.</summary>
    public IReadOnlyList<SearchTarget> PageTargets()
    {
        var targets = new List<SearchTarget>
        {
            new(false, L("Extra"), L("Page"), () => ShowPageByKey("Extra_L")),
            new(false, L("Settings"), L("Page"), () => ShowPageByKey("Settings_L")),
        };

        if (Session.IsLoaded)
        {
            targets.Add(new(false, L("Character"), L("Page"), () => ShowPageByKey("Char_L")));
            targets.Add(new(false, L("Items"), L("Page"), () => ShowPageByKey("Inventory_L")));
            targets.Add(new(false, L("Inventory"), L("Items"), () => ShowPageByKey("Inventory_L")));
            targets.Add(new(false, L("Edit"), L("Items"), () => ShowPageByKey("Inventory_R")));
            targets.Add(new(false, L("Vault"), L("Items"), () => { ShowItemsPage(); GoRight(VaultMark); }));
            targets.Add(new(false, L("Search"), L("Items"), () => ShowSearch()));
            targets.Add(new(false, L("Compendium"), L("Items"), () => { ShowItemsPage(); GoRight(GrailMark); }));
            targets.Add(new(false, L("Trade"), L("Items"), () => { ShowItemsPage(); GoRight(TradeMark); }));
            targets.Add(new(false, L("Forge"), L("Items"), () => { ShowItemsPage(); GoRight(ForgeMark); }));
        }

        return targets;
    }

    /// <summary>
    /// The grail shown at one entry - a find of the window's search: on the Items page's Grail bookmark with a
    /// character open, on the Extra page's grail with none; its tab, and the entry's name in the grail's own
    /// box, which narrows the cards to it.
    /// </summary>
    public void ShowGrailAt(string tab, string name)
    {
        GrailViewModel? grail;
        if (Session.IsLoaded)
        {
            ShowItemsPage();
            GoRight(GrailMark);
            grail = (_dict_ViewPages["Vault_R"] as VP_ViewModel_Vault_R)?.Grail;
        }
        else
        {
            ShowPage("Extra");
            grail = (_dict_ViewPages["Extra_R"] as VP_ViewModel_Extra_R)?.Grail;
        }

        grail?.ShowEntry(tab, name);
    }

    /// <summary>
    /// The page a find is on, shown where neither side shows it - on the left, the right page left as it is:
    /// the vault for a find of the vault's, the inventory for the character's.
    /// </summary>
    private void ShowPageOf(FoundItem found)
    {
        if (found.InVault ? IsLeftVault || IsRightVault : IsLeftInventory)
            return;

        GoLeft(found.InVault ? VaultMark : InventoryMark);
    }

    /// <summary>
    /// The search list's Select found: the finds that lie in what the pages in sight show become the
    /// selection, each page's its own. Said where none does - the finds are then in containers or folders
    /// not shown, and the list's rows go to them.
    /// </summary>
    private void SelectFound()
    {
        int selected = 0;
        if (IsLeftInventory && Inventory is { } inventory)
            selected += inventory.SelectFound();
        if (IsLeftVault && Vault is { } left)
            selected += left.SelectFound();
        if (IsRightVault && RightVault is { } right)
            selected += right.SelectFound();

        if (selected == 0)
            Session.Status = L("No find in what is shown.");
    }

    /// <summary>The search list's Move found to folder: asked of the vault page, in sight on the left, that the search is with.</summary>
    private void MoveFound()
    {
        var page = IsLeftVault && Vault is { IsSearchingHere: true } left ? left : null;

        if (page is null)
            Session.Status = L("Require vault page open.");
        else
            page.OpenMoveForFound();
    }

    public bool IsLeftInventory => LeftBookmark == InventoryMark;
    public bool IsLeftVault => LeftBookmark == VaultMark;
    public bool IsRightEdit => RightBookmark == EditMark;
    public bool IsRightGrail => RightBookmark == GrailMark;
    public bool IsRightForge => RightBookmark == ForgeMark;

    /// <summary>The Forge's bookmark is there where the play mode has it (Free's; PlayRules.Forges).</summary>
    public bool CanForge => (_dict_ViewPages["Vault_R"] as VP_ViewModel_Vault_R)?.CanForge == true;
    public bool IsRightVault => RightBookmark == VaultMark;
    public bool IsRightTrade => RightBookmark == TradeMark;

    /// <summary>The item search is the right page: the pages mark what it finds.</summary>
    public bool IsRightSearch => RightBookmark == SearchMark;

    /// <summary>
    /// The vault on the right page: beside the inventory, or beside the vault itself - the two compared (lifting an
    /// earlier rule that the vault was shown in one place only). It needs a
    /// character.
    /// </summary>
    public bool CanRightVault => Session.IsLoaded;

    /// <summary>
    /// The side bar's and the bookmarks' tooltips, with why where they are dimmed (a dimmed
    /// control says why): no character open.
    /// </summary>
    private string NeedsCharacter(string tip) => Session.IsLoaded ? tip : tip + Environment.NewLine + L("Require open character.");

    public string CharacterPageTip => NeedsCharacter(L("Character (Ctrl + 2)"));

    public string ItemsPageTip => NeedsCharacter(L("Items (Ctrl + 3)"));

    // A bookmark's tooltip names its key, as the key sheet does: the left page's two have a
    // key each, the right page's are walked by Ctrl + Tab - a line under the name, drawn as key caps (KeyedTip).
    public string InventoryBookmarkTip => NeedsCharacter(ShortcutText.With(L("Inventory"), L("Ctrl + PageUp")));

    public string VaultBookmarkTip => NeedsCharacter(ShortcutText.With(L("Vault"), L("Ctrl + PageDown")));

    private static string Walked(string tip) => tip + Environment.NewLine + L("Ctrl + Tab: Next bookmark");

    public string EditBookmarkTip => Walked(L("Edit"));

    public string RightVaultBookmarkTip => Walked(NeedsCharacter(L("Vault")));

    public string SearchBookmarkTip => Walked(SearchBookmarkHint);

    public string ForgeBookmarkTip => Walked(ForgeBookmarkHint);

    public string TradeBookmarkTip => Walked(L("Trade"));

    public string GrailBookmarkTip => Walked(L("Compendium"));

    /// <summary>The vault is on both pages: two vault pages over the one vault, side by side.</summary>
    public bool IsComparing => IsLeftVault && IsRightVault;

    /// <summary>What the right page showed before the vault was put there to compare: where Compare, off, puts it back.</summary>
    private string _beforeCompare = EditMark;

    /// <summary>
    /// The left vault page's Compare button: the vault on the right page as well, or off it again. The right
    /// Vault bookmark does the same by hand, and the button is lit either way.
    /// </summary>
    private void ToggleCompare()
    {
        if (!IsLeftVault || !Session.IsLoaded)
            return;

        if (IsRightVault)
        {
            RightBookmark = _beforeCompare == ForgeMark && !CanForge ? EditMark : _beforeCompare;
        }
        else
        {
            _beforeCompare = RightBookmark;
            RightBookmark = VaultMark;
        }

        ShowItems();
    }

    private readonly PD2SPCompanion.Shell.Services.Vault.VaultService? _vaults;

    /// <summary>
    /// The vault page whose folder takes what is stored from elsewhere - the inventory's Store, the Forge, a
    /// purchase: the left page's while it shows the vault (the left is the first of the
    /// two), the right page's while it alone does, and with neither in sight the one that was last - what was
    /// stored beside a folder goes on landing there while the right page is turned to Edit for a moment.
    /// </summary>
    private string _arrivals = VP_ViewModel_Vault_L.LeftPage;

    /// <summary>
    /// A character is needed for Character and Items: with the last one closed, those pages give way to
    /// home, and the book starts again on the inventory and Edit.
    /// </summary>
    private void CharacterGone()
    {
        LeftBookmark = InventoryMark;
        RightBookmark = EditMark;
        ForgeWaits = false;

        // Extra stays: it works with no character, the map seed greyed.
        if (Button_Char_isChecked || Button_Inventory_isChecked)
        {
            Button_Char_isChecked = false;
            Button_Inventory_isChecked = false;
            CurrentPage_L = _dict_ViewPages["Default_L"];
            CurrentPage_R = _dict_ViewPages["Default_R"];
        }

        if (_dict_ViewPages["Vault_R"] is VP_ViewModel_Vault_R right)
            right.Show(null);
    }

    /// <summary>
    /// The left page's bookmark. The vault taken here while the right page shows it is the vault on both
    /// pages. Trading
    /// stays: it works with whichever page is beside it.
    /// </summary>
    [RelayCommand]
    private void ShowLeft(string which)
    {
        if (GoLeft(which))
            _sfxManager?.Method_ButtonSFX_Press();
    }

    /// <summary>The left page's bookmark taken without a sound (<see cref="SwitchChar"/>); false where it was up already.</summary>
    private bool GoLeft(string which)
    {
        if (which == LeftBookmark || !Session.IsLoaded)
            return false;

        LeftBookmark = which;

        // The page now beside the Forge may hold a selection from before: the Forge stands beside none.
        if (IsRightForge)
            ClearLeftSelection();

        ShowItems();
        return true;
    }

    /// <summary>
    /// The right page's bookmark. Trade leaves the left
    /// page as it is, with no bouncing back to the inventory: what is bought and sold is the left
    /// page's, either one.
    /// </summary>
    [RelayCommand]
    private void ShowRight(string which)
    {
        if (GoRight(which))
            _sfxManager?.Method_ButtonSFX_Press();
    }

    /// <summary>The right page's bookmark taken without a sound (<see cref="SwitchChar"/>); false where it was up already or may not be.</summary>
    private bool GoRight(string which)
    {
        if (which == RightBookmark || (which == VaultMark && !CanRightVault) || (which == ForgeMark && !CanForge)
            || !Session.IsLoaded)
            return false;

        RightBookmark = which;
        ShowItems();
        return true;
    }

    /// <summary>
    /// Puts the Items book where a badge's copies can be seen. The badge's search is the item search's: its
    /// page is the right page - the copies listed there, each gone to by its row, and the search ended there -
    /// and the left page shows where copies are: the inventory for the character's and the shared stash's,
    /// the vault where they are all the vault's. A left page already showing some stays.
    /// </summary>
    private void ShowCopies(bool onCharacter, bool inVault)
    {
        if (!Session.IsLoaded || !Button_Inventory_isChecked)
            return;

        bool leftShowsSome = IsLeftInventory ? onCharacter : inVault;
        if (!leftShowsSome && (onCharacter || inVault))
            LeftBookmark = onCharacter ? InventoryMark : VaultMark;

        RightBookmark = SearchMark;
        ShowItems();
    }

    /// <summary>
    /// The pages a character is mostly seen on, each side's as the sidebar would show them now: the
    /// Character page, and the Items page with whichever right page it opens with. Laid out on opening a
    /// character (MainView.PrepareCharacterPages).
    /// </summary>
    public object[] CharacterPagesLeft =>
        [_dict_ViewPages["Char_L"], _dict_ViewPages[IsLeftVault ? "Vault_L" : "Inventory_L"]];

    public object[] CharacterPagesRight =>
        [_dict_ViewPages["Char_R"], _dict_ViewPages[IsRightVault ? "Vault_Right" : IsRightTrade ? "Trade" : "Vault_R"]];

    /// <summary>
    /// The Character page's cards caught up with the character just opened, before they are laid out
    /// ahead (MainView.PrepareCharacterPages): hidden, they follow the session only when shown.
    /// </summary>
    public void SettleCharacterPages()
    {
        (_dict_ViewPages["Char_L"] as VP_ViewModel_Char_L)?.Settle();
        (_dict_ViewPages["Char_R"] as VP_ViewModel_Char_R)?.Settle();
    }

    /// <summary>
    /// The pages left after the character's - Extra, Settings, Experimental - as left and right pairs, laid
    /// out ahead one at a time while the editor is idle after it starts (MainView.PrepareOtherPages): their
    /// first visits were 30-80 ms of building, against Items' and Character's few, and felt slower
    /// (they need no character, so not after an open). Idle, so no wait grows.
    /// </summary>
    public IReadOnlyList<(object Left, object Right)> OtherPages =>
    [
        (_dict_ViewPages["Extra_L"], _dict_ViewPages["Extra_R"]),
        (_dict_ViewPages["Settings_L"], _dict_ViewPages["Settings_R"]),
        (_dict_ViewPages["Experimental_L"], _dict_ViewPages["Experimental_R"]),
    ];

    /// <summary>
    /// Every page, as left and right pairs, laid out behind the splash (MainView.PrepareAllPages):
    /// the character's own first - their fixed parts, which were two thirds of the first open of
    /// a session (630 - 680 ms where every later open is 225) - then the vault on both pages, Trade, and the
    /// pages that need no character. A null is no page on that side.
    /// </summary>
    public IReadOnlyList<(object? Left, object? Right)> PagesAhead =>
    [
        (_dict_ViewPages["Char_L"], _dict_ViewPages["Char_R"]),
        (_dict_ViewPages["Inventory_L"], _dict_ViewPages["Vault_R"]),
        (_dict_ViewPages["Vault_L"], _dict_ViewPages["Vault_Right"]),
        (null, _dict_ViewPages["Trade"]),
        .. OtherPages.Select(pair => ((object?)pair.Left, (object?)pair.Right)),
    ];

    /// <summary>The open character's search index, written ahead of its first search (VP_ViewModel_Inv_L.PrepareSearch).</summary>
    public void PrepareSearch() => Inventory?.PrepareSearch();

    /// <summary>What a page works out the first time it is shown, worked out before the pages are laid out ahead: the Forge's lists.</summary>
    public void WarmPages() => Forge?.Warm();

    /// <summary>A page of <see cref="OtherPages"/> given what it will show, before it is laid out.</summary>
    public void SettleOtherPage(object page) => (page as VP_ViewModel_Extra_R)?.Settle();

    /// <summary>
    /// The two vault pages told how the book now stands, before either is shown: one in sight for the first
    /// time opens where the other is; and the folder that takes what is stored from elsewhere is said again.
    /// </summary>
    private void SettleVaultPages()
    {
        if (Vault is not { } left || RightVault is not { } right)
            return;

        if (IsRightVault)
            right.OpenWhereTwinIs();
        if (IsLeftVault)
            left.OpenWhereTwinIs();

        if (IsLeftVault)
            _arrivals = VP_ViewModel_Vault_L.LeftPage;
        else if (IsRightVault)
            _arrivals = VP_ViewModel_Vault_L.RightPage;

        left.BookChanged();
        right.BookChanged();
    }

    /// <summary>
    /// The Items page as its bookmarks say. Edit follows the left page: the character's item
    /// panel beside the inventory, the vault's beside the vault. Edit and Grail share one page.
    /// </summary>
    private void ShowItems()
    {
        SettleVaultPages();

        CurrentPage_L = _dict_ViewPages[IsLeftVault ? "Vault_L" : "Inventory_L"];
        CurrentPage_R = _dict_ViewPages[IsRightVault ? "Vault_Right" : IsRightTrade ? "Trade" : "Vault_R"];

        if (_dict_ViewPages["Vault_R"] is VP_ViewModel_Vault_R right)
        {
            right.EditsInventory = IsLeftInventory;
            right.Show(IsRightEdit ? "Edit" : IsRightGrail ? "Grail" : IsRightForge ? "Forge" : IsRightSearch ? "Search" : null);
        }
    }

    #region Button Press - SFX & Page Switch
    [RelayCommand]
    private void Button_Char_Press()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SwitchChar();
    }

    /// <summary>
    /// The Character page shown or left as its side bar button now stands, without a sound: the press's (above) or the program's own - a page
    /// gone to by the program makes no click, only a press does (a click was heard twice or three
    /// times as a character came back from the game).
    /// </summary>
    private void SwitchChar()
    {
        //Page Switch
        if (Button_Char_isChecked)//The check is after the UI change. So when the code hits here, the UI element is already set to IsChecked = true.
        {
            CurrentPage_L = _dict_ViewPages["Char_L"];
            CurrentPage_R = _dict_ViewPages["Char_R"];
        }
        else
        {
            CurrentPage_L = _dict_ViewPages["Default_L"];
            CurrentPage_R = _dict_ViewPages["Default_R"];
        }
    }

    [RelayCommand]
    private void Button_Inventory_Press()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SwitchInventory();
    }

    /// <summary>
    /// The Items page shown or left as its side bar button now stands, without a sound: the press's (above) or the program's own - a page
    /// gone to by the program makes no click, only a press does (a click was heard twice or three
    /// times as a character came back from the game).
    /// </summary>
    private void SwitchInventory()
    {
        //Page Switch
        if (Button_Inventory_isChecked)//The check is after the UI change. So when the code hits here, the UI element is already set to IsChecked = true.
        {
            ShowItems();
        }
        else
        {
            CurrentPage_L = _dict_ViewPages["Default_L"];
            CurrentPage_R = _dict_ViewPages["Default_R"];
        }
    }

    [RelayCommand]
    private void Button_Extra_Press()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SwitchExtra();
    }

    /// <summary>
    /// The Extra page shown or left as its side bar button now stands, without a sound: the press's (above) or the program's own - a page
    /// gone to by the program makes no click, only a press does (a click was heard twice or three
    /// times as a character came back from the game).
    /// </summary>
    private void SwitchExtra()
    {
        //Page Switch
        if (Button_Extra_isChecked)//The check is after the UI change. So when the code hits here, the UI element is already set to IsChecked = true.
        {
            CurrentPage_L = _dict_ViewPages["Extra_L"];
            CurrentPage_R = _dict_ViewPages["Extra_R"];

            // The grail's Discovery and the stash's runes read afresh, as the page is opened.
            if (_dict_ViewPages["Extra_R"] is VP_ViewModel_Extra_R extra)
                extra.Shown();
        }
        else
        {
            CurrentPage_L = _dict_ViewPages["Default_L"];
            CurrentPage_R = _dict_ViewPages["Default_R"];
        }
    }

    /// <summary>
    /// The extras page. Unlike the others it needs no character: what is on it is done to
    /// the game rather than to a save, which is why it sits outside the ones that grey out
    /// when nothing is open.
    /// </summary>
    [RelayCommand]
    private void Button_Experimental_Press()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SwitchExperimental();
    }

    /// <summary>
    /// The Experimental page shown or left as its side bar button now stands, without a sound: the press's (above) or the program's own - a page
    /// gone to by the program makes no click, only a press does (a click was heard twice or three
    /// times as a character came back from the game).
    /// </summary>
    private void SwitchExperimental()
    {
        if (Button_Experimental_isChecked)
        {
            CurrentPage_L = _dict_ViewPages["Experimental_L"];
            CurrentPage_R = _dict_ViewPages["Experimental_R"];
        }
        else
        {
            CurrentPage_L = _dict_ViewPages["Default_L"];
            CurrentPage_R = _dict_ViewPages["Default_R"];
        }
    }

    [RelayCommand]
    private void Button_Settings_Press()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SwitchSettings();
    }

    /// <summary>
    /// The Settings page shown or left as its side bar button now stands, without a sound: the press's (above) or the program's own - a page
    /// gone to by the program makes no click, only a press does (a click was heard twice or three
    /// times as a character came back from the game).
    /// </summary>
    private void SwitchSettings()
    {
        //Page Switch
        if (Button_Settings_isChecked)//The check is after the UI change. So when the code hits here, the UI element is already set to IsChecked = true.
        {
            CurrentPage_L = _dict_ViewPages["Settings_L"];
            CurrentPage_R = _dict_ViewPages["Settings_R"];
            (CurrentPage_L as VP_ViewModel_Settings_L)?.Shown();
        }
        else
        {
            CurrentPage_L = _dict_ViewPages["Default_L"];
            CurrentPage_R = _dict_ViewPages["Default_R"];
        }
    }
    #endregion
    #endregion
}