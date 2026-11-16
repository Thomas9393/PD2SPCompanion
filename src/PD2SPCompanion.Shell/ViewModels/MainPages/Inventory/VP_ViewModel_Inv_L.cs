using System;
using Avalonia.Threading;
using D2SLib.Model.Save;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One choice in a pane's picker: a container of the character's - inventory, cube, own stash -
/// one page of the shared stash, its materials tab, or the rule that divides the two files.
/// </summary>
/// <param name="Label">What the picker calls it.</param>
/// <param name="Grid">The container, or null for the rule.</param>
/// <param name="IsCharacter">
/// Whether it is in the character's file - the inventory, the cube or the character's own stash -
/// rather than the shared stash's. Which file lets an item go and takes it in.
/// </param>
/// <param name="IsSeparator">
/// Whether this is the rule rather than a choice. A ComboBox has no separator of its own, so
/// it is an entry that draws as a line - and the <b>view</b> refuses the click, because a
/// correction raised inside the binding's write to the source is dropped as that write's
/// echo. See pitfalls/avalonia-input-and-styling.md.
/// </param>
/// <param name="IsMaterials">
/// Whether this is the shared stash's materials tab, which is counts rather than a grid - the
/// pane shows the tab in place of its grid while it is chosen.
/// </param>
/// <param name="Title">What the section's header calls it: "Shared Stash" for every shared page.</param>
/// <param name="Sheet">The materials tab's page it shows - Season 13's one, or one of Season 14's tabs.</param>
/// <param name="Purse">The gold that goes with it, or null: the cube keeps none.</param>
/// <summary>One piece of the ghost under the pointer: an item, where it sits from the one held.</summary>
public sealed record GhostPart(ItemTileViewModel Tile, double Left, double Top)
{
    /// <summary>
    /// How far each next piece of a group with no shape of its own is drawn down and right of the one before: the
    /// inventory's from several places, the vault's cards, the sockets' fillers - one look for every carry.
    /// </summary>
    public const double Fan = 8;
}

public sealed partial class StashPageChoice(
    string Label, GridViewModel? Grid, bool IsCharacter = false, bool IsSeparator = false,
    bool IsMaterials = false, string Title = "", GoldViewModel? Purse = null, string? LockKey = null,
    string? Short = null, MaterialSheet? Sheet = null) : ObservableObject
{
    /// <summary>The materials tab's page it shows, where it is one (<see cref="IsMaterials"/>).</summary>
    public MaterialSheet? Sheet { get; } = Sheet;

    public string Label { get; } = Label;

    /// <summary>
    /// Its name where the list of pages is squeezed ("SP 1/SP 2/Mat/Cube/Inv/PStash"):
    /// the whole name is shown wherever there is room for it, and is the row's tooltip where there is not.
    /// </summary>
    public string Short { get; } = Short ?? Label;
    public GridViewModel? Grid { get; } = Grid;
    public bool IsCharacter { get; } = IsCharacter;
    public bool IsSeparator { get; } = IsSeparator;
    public bool IsMaterials { get; } = IsMaterials;
    public string Title { get; } = Title;
    public GoldViewModel? Purse { get; } = Purse;

    /// <summary>The character's inventory - where the inventory lock applies.</summary>
    public bool IsInventory => IsCharacter && Title == "Inventory";

    /// <summary>
    /// A page that can be laid out again - Sort and Close gaps, stash only: the character's
    /// stash and each shared page. Not the inventory, whose places are the player's own; not the cube; not
    /// the materials tab, which has no cells.
    /// </summary>
    public bool CanArrange => Grid is not null && !IsMaterials && !IsSeparator && (!IsCharacter || Title == "Personal Stash");

    /// <summary>
    /// What the page's lock is remembered by (<c>EditorSettings.LockedPages</c>), or null for a page with
    /// no lock of its own: the inventory, whose lock is the charm area's, and the materials tab, which
    /// Store all never takes from.
    /// </summary>
    public string? LockKey { get; } = LockKey;

    public bool CanLock => LockKey is not null;

    /// <summary>
    /// Locked, the whole page stays where it is: out of Store all, Select all and
    /// the rectangle, and nothing on it is moved out - the inventory lock's rules, for a page. Things can
    /// still be put on it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockHint))]
    [NotifyPropertyChangedFor(nameof(ArrangeBlocked))]
    [NotifyPropertyChangedFor(nameof(CloseGapsHint))]
    private bool _isLocked;

    /// <summary>
    /// Why the page cannot be laid out again just now, or null: locked. Its Sort and Close gaps are dimmed for
    /// it and say so, as Swap does, rather than stand lit and be refused on the strip.
    /// </summary>
    public string? ArrangeBlocked => IsLocked ? L("Page locked.") : null;

    /// <summary>Close gaps' tooltip: its name, and why it is dimmed where it is.</summary>
    public string CloseGapsHint => IsLocked ? L("Close gaps") + Environment.NewLine + L("Page locked.") : L("Close gaps");

    public string LockHint => IsLocked
        ? L("Page lock on") + Environment.NewLine + L("Items cannot be picked; Store all skips it")
        : L("Page lock off");

    // ---- its row in the list of pages ------------------------------------------------
    // The pages stand in a list beside the panes, always in sight, where a drop box a pane named them: a row a
    // page, its lock at the row's end. A click shows the page in the first pane, a right click in the second -
    // and on the page the second shows, shuts the second. Marked I and II while two panes are up.

    /// <summary>The first pane shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaneMark))]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    private bool _isFirst;

    /// <summary>The second pane shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaneMark))]
    [NotifyPropertyChangedFor(nameof(IsShown))]
    [NotifyPropertyChangedFor(nameof(RowHint))]
    private bool _isSecond;

    /// <summary>Two panes are up: only then does a row say which of them shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaneMark))]
    private bool _twoPanes;

    /// <summary>What is carried would go to this page: its row is framed.</summary>
    [ObservableProperty] private bool _isDropTarget;

    /// <summary>A pane shows it: its row is the one held.</summary>
    public bool IsShown => IsFirst || IsSecond;

    /// <summary>Which pane, as the weapon tabs are numbered - while there are two to tell apart.</summary>
    public string PaneMark => !TwoPanes ? "" : IsFirst ? PaneOne : IsSecond ? PaneTwo : "";

    public const string PaneOne = "I", PaneTwo = "II";

    /// <summary>The row's tooltip: what the right button does - a key's line, drawn as a cap.</summary>
    public string RowHint => IsSecond ? L("Ctrl + LMB: Close pane II") : L("Ctrl + LMB: Show in pane II");
}

/// <summary>
/// One grid container on the page, as a section that folds away.
/// <para>
/// Most containers are one grid. The shared stash is nine, so a section may be given a set
/// of <see cref="Pages"/> and a picker to choose between them, and the grid it shows follows
/// that choice.
/// </para>
/// </summary>
public sealed partial class ContainerSectionViewModel : ObservableObject
{
    public ContainerSectionViewModel(
        string name,
        GridViewModel grid,
        IReadOnlyList<StashPageChoice>? pages = null,
        string? key = null)
    {
        Key = key ?? name;
        _name = L(name);
        _grid = grid;
        Pages = pages;
        _selectedPage = pages?.FirstOrDefault(page => !page.IsSeparator);

        // Something other than the first, so the two panes are not the same thing
        // twice the moment the section opens.
        _secondPage = pages?.FirstOrDefault(
            page => !page.IsSeparator && page != _selectedPage) ?? _selectedPage;

        _lastFirst = _selectedPage;
        _lastSecond = _secondPage;
        MarkPanes();
    }

    /// <summary>Each page told which pane shows it, for its row in the list.</summary>
    private void MarkPanes()
    {
        foreach (var page in Pages ?? [])
        {
            page.IsFirst = ReferenceEquals(page, SelectedPage);
            page.IsSecond = SecondShown && ReferenceEquals(page, SecondPage);
            page.TwoPanes = SecondShown;
        }

        OnPropertyChanged(nameof(FirstTitle));
        OnPropertyChanged(nameof(SecondTitle));
    }

    /// <summary>
    /// The shared stash's gold, said in the section's header and set behind the button beside it, as the vault's gold
    /// button is. Behind it, the carried gold above and the vault's below: Season
    /// 14 keeps no gold in a character's stash, so that purse is not shown.
    /// </summary>
    public GoldViewModel? SharedGold =>
        (Pages ?? []).Select(page => page.Purse).FirstOrDefault(purse => purse?.Purse == PD2SPCompanion.Shell.Services.Vault.VaultMoves.SharedPurse);

    public bool HasSharedGold => SharedGold is not null;

    /// <summary>The open character's carried gold, the panel's top row.</summary>
    public GoldViewModel? CarriedGold =>
        (Pages ?? []).Select(page => page.Purse).FirstOrDefault(purse => purse?.Purse == PD2SPCompanion.Core.Collection.MoveRecovery.CarriedPurse);

    /// <summary>The arrows between the carried gold and the shared stash's; null without either.</summary>
    public GoldTransferViewModel? GoldBetween { get; init; }

    /// <summary>Whether the panel has an arrow, and so its amount row.</summary>
    public bool MovesGold => GoldBetween is not null || SharedGold?.HasVault == true;

    /// <summary>The play mode's rules, for the gold's panel: whether the amount is typed. Given by the page.</summary>
    public PlayRules? Rules { get; init; }

    /// <summary>What a pane's line says it shows: the page, with the pane's number before it while there are two.</summary>
    public string FirstTitle => Titled(SelectedPage);

    public string SecondTitle => Titled(SecondPage);

    private static string Titled(StashPageChoice? page) =>
        page is null or { IsSeparator: true } ? "" : page.PaneMark.Length > 0 ? $"{page.PaneMark} · {page.Label}" : page.Label;

    /// <summary>
    /// The page each pane last showed - a page, never the rule between the character's and the shared ones. A
    /// picker stepped by the keys or the wheel passes over the rule on its way (the view steps it on), so the
    /// value a pane held just before its new page can be the rule; and the page landed on being the other pane's,
    /// the two trade - which handed the other pane the rule, selected and showing nothing. What is traded is the page shown before the rule.
    /// </summary>
    private StashPageChoice? _lastFirst, _lastSecond;

    /// <summary>
    /// What this section is, as against what it is currently showing. The stash renames
    /// itself with the picker, so its heading is no use for finding it again after a
    /// rebuild - and every rebuild makes new sections.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// What the section calls itself. Fixed for most containers, and for the stash it
    /// follows the picker: the character's own stash and the shared one are different
    /// things, and the header is what says which you are looking at.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Count))]
    [NotifyPropertyChangedFor(nameof(Header))]
    [NotifyPropertyChangedFor(nameof(ControlsWidth))]
    [NotifyPropertyChangedFor(nameof(CanSwap))]
    [NotifyPropertyChangedFor(nameof(SwapHint))]
    private GridViewModel _grid;

    /// <summary>
    /// Whether the two panes ended up beside each other. The page works it out from the
    /// width it was given and tells each section, because the control group has to be sized
    /// differently in the two cases.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControlsWidth))]
    private bool _sideBySide;

    /// <summary>
    /// How wide the control group is: auto between two panes, and <b>a grid's width</b> when
    /// it has wrapped onto a line of its own.
    /// <para>
    /// A WrapPanel lays a line out left to right and gives each child the width it asks for,
    /// so a child can only centre itself inside its own slot - which for four small buttons
    /// is four small buttons, hard against the left. Matching the grid's width makes the
    /// slot the line, and centring then means what it looks like it should mean.
    /// </para>
    /// </summary>
    public double ControlsWidth => SideBySide ? double.NaN : Grid.Width;

    public int Count => Grid.Count;

    /// <summary>What the picker offers, or null for a container that is a single grid.</summary>
    public IReadOnlyList<StashPageChoice>? Pages { get; }

    public bool HasPages => Pages is { Count: > 0 };


    /// <summary>Whether to draw the picker: only where there is one, and only while open.</summary>
    public bool ShowPicker => HasPages && IsOpen;

    /// <summary>
    /// Whether the second stash is being shown. Folded away it takes its grid and its picker
    /// with it, and the buttons that move things between the two have nothing to work on, so
    /// they go dead rather than disappearing - the row keeps its shape either way.
    /// <para>
    /// Folded away to begin with. Most of the time one stash is what you want to look at,
    /// and a second grid doubles the height of the section before anybody has asked for it.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSecond))]
    [NotifyPropertyChangedFor(nameof(CanSwap))]
    [NotifyPropertyChangedFor(nameof(SwapHint))]
    private bool _secondShown;

    public bool ShowSecond => ShowPicker && SecondShown;

    /// <summary>Whether the first pane is showing the materials tab.</summary>
    public bool ShowsMaterials => SelectedPage?.IsMaterials == true;

    /// <summary>Whether the second pane is.</summary>
    public bool SecondShowsMaterials => SecondPage?.IsMaterials == true;

    /// <summary>The first pane's grid, hidden while it shows the materials tab.</summary>
    public bool ShowsGrid => !ShowsMaterials;

    public bool SecondShowsGrid => !SecondShowsMaterials;

    /// <summary>Swap's tooltip, with why where it is dimmed (the first reason found).</summary>
    public string SwapHint
    {
        get
        {
            if (CanSwap)
                return L("Swap");

            string why = !SecondShown ? L("Require pane II open.")
                : SelectedPage is { IsLocked: true } || SecondPage is { IsLocked: true } ? L("A page is locked.")
                : SelectedPage is { Grid: { } a } && SecondPage is { Grid: { } b } && (a.Columns != b.Columns || a.Rows != b.Rows)
                    ? L("Pages of different sizes.")
                : ReferenceEquals(SelectedPage, SecondPage) ? L("The same page twice.")
                : L("Both pages empty.");
            return L("Swap") + Environment.NewLine + why;
        }
    }

    /// <summary>
    /// Whether the two panes can trade contents.
    /// <para>
    /// <b>Stash to stash only</b>, and only between grids of the same shape. Both panes come
    /// from this section's picker, which offers the personal stash and the nine shared pages
    /// and nothing else - so a swap can never reach the inventory, the cube, the belt or
    /// anything worn. The shapes are checked as well rather than assumed: they are the same
    /// 10x15 for an expansion character, and a classic one's 6x4 stash is not, and a swap
    /// between two different layouts is not a swap.
    /// </para>
    /// <para>
    /// Beyond that, two different panes with something in at least one of them - swapping
    /// two empty pages is a no-op with a sound.
    /// </para>
    /// </summary>
    public bool CanSwap => SecondShown
                           && HasPages
                           && SelectedPage is { IsSeparator: false, Grid: not null, IsLocked: false } first
                           && SecondPage is { IsSeparator: false, Grid: not null, IsLocked: false } second
                           && !ReferenceEquals(first, second)
                           && first.Grid.Columns == second.Grid.Columns
                           && first.Grid.Rows == second.Grid.Rows
                           && (first.Grid.Items.Count > 0 || second.Grid.Items.Count > 0);


    /// <summary>
    /// Whether the page has several selected (or a rectangle is being drawn). The page tells each section.
    /// Swap works on through it, the picks kept - they are kept by item: it went dead
    /// while picking, for the arrow buttons that moved the picks across, which are gone.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwap))]
    [NotifyPropertyChangedFor(nameof(SwapHint))]
    [NotifyPropertyChangedFor(nameof(HasPicks))]
    private bool _isPicking;

    /// <summary>
    /// Something selected or picked among what this section shows. The page dims every container
    /// on any section's marks (PageHasPicks), not this section's alone. A
    /// single selection too, not several picks only.
    /// </summary>
    public bool HasPicks => (ShowsGrid && Grid.Items.Any(tile => tile.IsMarked))
                            || (ShowSecond && SecondShowsGrid && SecondGrid?.Items.Any(tile => tile.IsMarked) == true);

    /// <summary>The page's picks have changed; this section looks at its own again.</summary>
    public void SayPicks()
    {
        OnPropertyChanged(nameof(HasPicks));
    }

    /// <summary>
    /// Select all, none or the other way round, over what this section is showing. Given by the
    /// page after the section is already on screen, so it says when it arrives.
    /// </summary>
    [ObservableProperty] private PickMenu? _picks;

    /// <summary>Tells the swap button to look at its panes again.</summary>
    public void SaySomethingMoved()
    {
        OnPropertyChanged(nameof(CanSwap));
        OnPropertyChanged(nameof(SwapHint));
    }

    /// <summary>
    /// The same section kept through a rebuild (the page's Rebuild): its grids are the same objects
    /// with other tiles in them, so what is counted off them is told again.
    /// </summary>
    public void ContentsChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(ControlsWidth));
        SaySomethingMoved();
    }

    /// <summary>Which page is being shown. Choosing one renames the section and swaps the grid.</summary>
    [ObservableProperty] private StashPageChoice? _selectedPage;

    partial void OnSelectedPageChanged(StashPageChoice? oldValue, StashPageChoice? newValue)
    {
        SayPanesChanged();

        if (newValue is null or { IsSeparator: true })
            return;

        var before = _lastFirst;
        _lastFirst = newValue;

        if (newValue.IsMaterials)
            Name = L("Materials");
        else if (newValue.Grid is { } grid)
        {
            Grid = grid;
            Name = L(newValue.Title);
        }
        else
            return;

        // Showing the same page twice is never what was meant: the page the other pane shows, asked of
        // this one, trades places with it.
        if (!_swapping && ReferenceEquals(newValue, SecondPage))
            Swap(() => SecondPage = before);
    }

    partial void OnSecondPageChanged(StashPageChoice? oldValue, StashPageChoice? newValue)
    {
        SayPanesChanged();

        if (newValue is null or { IsSeparator: true } or { Grid: null, IsMaterials: false })
            return;

        var before = _lastSecond;
        _lastSecond = newValue;

        if (!_swapping && ReferenceEquals(newValue, SelectedPage))
            Swap(() => SelectedPage = before);
    }

    /// <summary>What follows from either pane changing what it shows.</summary>
    private void SayPanesChanged()
    {
        MarkPanes();
        OnPropertyChanged(nameof(ShowsMaterials));
        OnPropertyChanged(nameof(SecondShowsMaterials));
        OnPropertyChanged(nameof(ShowsGrid));
        OnPropertyChanged(nameof(SecondShowsGrid));
        OnPropertyChanged(nameof(HasPicks));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(CanSwap));
        OnPropertyChanged(nameof(SwapHint));
    }

    partial void OnSecondShownChanged(bool value) => MarkPanes();

    partial void OnIsOpenChanged(bool value) =>
        PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(ContainerSectionViewModel) + nameof(IsOpen), value);

    /// <summary>Keeps the swap below from setting off its own mirror image.</summary>
    private bool _swapping;

    private void Swap(System.Action swap)
    {
        _swapping = true;
        try
        {
            swap();
        }
        finally
        {
            _swapping = false;
        }
    }

    /// <summary>
    /// The second container shown under the controls, so things can be moved between two
    /// stashes without changing what is on screen between picking one up and putting it
    /// down.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecondGrid))]
    [NotifyPropertyChangedFor(nameof(CanSwap))]
    [NotifyPropertyChangedFor(nameof(SwapHint))]
    private StashPageChoice? _secondPage;

    public GridViewModel? SecondGrid => SecondPage?.Grid;

    /// <summary>Every purse the section has - for telling them the vault changed.</summary>
    public IEnumerable<GoldViewModel> Purses =>
        (Pages ?? []).Select(page => page.Purse).OfType<GoldViewModel>().Distinct();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPicker))]
    [NotifyPropertyChangedFor(nameof(ShowSecond))]
    [NotifyPropertyChangedFor(nameof(ShowsPicks))]
    private bool _isOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(ContainerSectionViewModel) + nameof(IsOpen));

    /// <summary>
    /// Whether to show the picks menu (and Store all beside it): on a section that is open. Not
    /// only while picking - there is no picking mode to start it from.
    /// </summary>
    public bool ShowsPicks => IsOpen;

    public string Header =>
        ShowsMaterials ? Name : Count > 0 ? $"{Name}  ({Count})" : L($"{Name}  (empty)");
}

/// <summary>
/// The left inventory page: what the character is wearing, and the containers it carries.
/// <para>
/// The belt and the shared stash are deliberately absent. The belt is a row of potion
/// slots rather than a grid, and the shared stash is a separate file that no character
/// owns - both want handling of their own rather than being squeezed in here.
/// </para>
/// </summary>
public partial class VP_ViewModel_Inv_L : ViewModelBase, IDragPicker
{
    public CharacterSession Session { get; }

    /// <summary>The shared stash, which belongs to no character and is shown without one.</summary>
    public SharedStashSession Stash { get; }

    /// <summary>The vault, shared with the vault page - see <see cref="StoreToVault"/>.</summary>
    private readonly VaultService _vaults;

    /// <summary>What goes into and comes out of the vault with the next save, under the Move rule.</summary>
    private readonly VaultMoves _moves;

    private readonly ISFXManager _sfxManager = null!;

    /// <summary>The equipment panel, laid out by slot.</summary>
    public ObservableCollection<EquipSlotViewModel> Equipped { get; } = [];

    /// <summary>The mercenary's equipment panel, laid out the same way.</summary>
    public ObservableCollection<EquipSlotViewModel> MercEquipped { get; } = [];

    /// <summary>The grid containers: one folding section, every container in it (see <see cref="ContainersSection"/>).</summary>
    public ObservableCollection<ContainerSectionViewModel> Sections { get; } = [];

    /// <summary>What the play mode shows here: duplicating, and typing a purse's gold.</summary>
    public PlayRules Rules { get; }

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Inv_L()
        : this(new CharacterSession(), new SharedStashSession(new CharacterSession(), new SaveSet()), null!, null!,
               new PlayRules(new EditorSettings()), null!,
               new ItemLanding(new CharacterSession(), new SharedStashSession(new CharacterSession(), new SaveSet())), null)
    {
    }

    public VP_ViewModel_Inv_L(
        CharacterSession session, SharedStashSession stash, ISFXManager sfxManager, VaultService vaults,
        PlayRules rules, VaultMoves moves, ItemLanding landing, GameSync? sync, SearchPanelViewModel? search = null,
        CompendiumMoves? deposits = null)
    {
        Rules = rules;
        _landing = landing;
        _deposits = deposits;
        if (deposits is not null)
            deposits.Changed += SayCompendium;
        landing.OpenContainers = OpenContainers;
        Search = search;
        _moves = moves;
        _sync = sync;

        // A running game turns a moving Store off: it takes the item off the character at the next
        // save. A copy into the vault writes only the vault, so it goes on - see GameSync.
        if (sync is not null)
        {
            sync.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName is not (nameof(GameSync.IsGameRunning) or nameof(GameSync.IsWriteBlocked)))
                    return;

                OnPropertyChanged(nameof(StoreBlocked));
                OnPropertyChanged(nameof(CanStore));
                OnPropertyChanged(nameof(StoreHint));
                OnPropertyChanged(nameof(StoreAllHint));
                SayCompendium();
                SayVaultGold();
            };
        }

        // The arrows follow what is staged, and the store button's wording follows the mode.
        if (moves is not null)
            moves.Changed += () => { ShowMoves(); SayPicksChanged(); SayVaultGold(); };
        rules.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StoreHint));
            OnPropertyChanged(nameof(StoreAllHint));
            OnPropertyChanged(nameof(StoreBlocked));
            OnPropertyChanged(nameof(CanStore));
        };
        Session = session;
        Stash = stash;
        _sfxManager = sfxManager;
        _vaults = vaults;

        // Another vault - a mode switched, one imported or restored - is another vault's gold.
        if (vaults is not null)
        {
            vaults.Switched += SayVaultGold;

            // And what the clipboard holds of the vault's, by numbers that name other items there. Said, where it held any.
            vaults.Switched += () =>
            {
                if (PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.DropVault())
                    Session.Status = L("Clipboard cleared.");
            };
        }

        // The stash reads itself when a character is opened, and is rebuilt on its own
        // account. Nothing here reads it: a read raises Changed, and a rebuild that starts
        // another one built the page twice.
        Stash.Changed += Refresh;

        // The tab is counts in the stash's header, not items, so it has its own signal.
        Stash.MaterialsChanged += RefreshMaterials;

        Session.Changed += Refresh;
        if (DuplicateIndex.Current is { } duplicates)
            duplicates.Changed += JudgeAll;

        // The vault's search reaches this page's items too.
        if (vaults is not null)
        {
            _search = new InventorySearch(vaults.Filter, ItemIndex.OfOpenFiles);
            vaults.Filter.Changed += WhenSearchChanged;
        }

        // Strength or dexterity moved on the character page: what it can use, judged again - and the sheet.
        Session.AttributesChanged += JudgeAll;
        Session.AttributesChanged += RefreshSheet;
        Session.Edited += SheetAfterEdit;

        // And an edit of an item: what it asks and what it gives move with it - a worn base upgraded past the
        // character's strength stayed usable, as the sheet stayed behind an edit.
        // Once for a burst, and only while the page is seen (JudgeAll).
        Session.Edited += JudgeAll;

        // The title row's count and gold (FactsText): told again when the items or the gold change.
        Session.Changed += () => OnPropertyChanged(nameof(FactsText));
        Session.AttributesChanged += () => OnPropertyChanged(nameof(FactsText));

        // What the buttons over the panel may do depends on what is chosen, and the choice
        // is the session's rather than this page's.
        Session.PropertyChanged += (_, changed) =>
        {
            // The ghost follows what is held - one item, or a group - and the grids take its drop.
            if (changed.PropertyName == nameof(CharacterSession.Held))
            {
                OnPropertyChanged(nameof(GhostParts));
                OnPropertyChanged(nameof(IsTakingDrops));
            }

            if (changed.PropertyName == nameof(CharacterSession.SelectedItem))
            {
                // A selection dims the rest of its section, as picks do.
                foreach (var section in Sections)
                    section.SayPicks();
                OnPropertyChanged(nameof(EquipmentHasPicks));
                OnPropertyChanged(nameof(MercHasPicks));
                OnPropertyChanged(nameof(PageHasPicks));
                DisarmDelete();
                OnPropertyChanged(nameof(CanDelete));
                OnPropertyChanged(nameof(CanStore));
                OnPropertyChanged(nameof(StoreHint));
                SayEquipTools();
                SaySellChoice();
                SayPanesMoved();
            }

            // Picking belongs to the character it was started on. Every load and every close
            // passes through "not loaded", so that is where it ends - left on, the panel went on
            // saying how many were selected over a closed character.
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded) && !Session.IsLoaded)
            {
                IsPicking = false;

                // And what was cut or copied of it: its items are gone with it. Said, where it held any.
                if (PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.DropItems())
                    Session.Status = L("Clipboard cleared.");
            }
        };

        // Something cut, copied, pasted or let go: Paste says what it holds.
        PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.Changed += SayClipboard;

        // The sockets' look and the socket guard are every page's (PlayRules): changed on the vault's page, they
        // are changed here.
        Rules.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null or "" or nameof(PlayRules.SocketsFaded) or nameof(PlayRules.SocketOpacity))
            {
                OnPropertyChanged(nameof(SocketsFaded));
                OnPropertyChanged(nameof(SocketOpacity));
                OnPropertyChanged(nameof(SocketHint));
            }

            if (e.PropertyName is null or "" or nameof(PlayRules.SocketGuard))
            {
                OnPropertyChanged(nameof(SocketGuard));
                OnPropertyChanged(nameof(SocketGuardHint));
            }

            // The play mode changed: the mercenary's pencil and level answer to it, and this page is kept while Settings
            // changes it. Read as a rebuild reads it, so what its boxes answer is not taken for an edit. Only on the empty
            // name the mode raises: an X press, which names the sockets' look, reads nothing of the mercenary.
            if (e.PropertyName is null or "")
                SayMerc();
        };

        // What is held, pasted into an item's sockets: this page's to do (VP_ViewModel_Inv_L.Socketing).
        PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.FitsSockets = FitsSockets;
        PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.PasteIntoSockets = PasteIntoSockets;

        Refresh();
    }

    /// <summary>
    /// Tells each section that what its panes hold may have changed: a move empties one and fills the
    /// other while both grids stay the same objects, so nothing else would say the swap had changed.
    /// </summary>
    private void SayPanesMoved()
    {
        foreach (var section in Sections)
            section.SaySomethingMoved();
    }

    /// <summary>
    /// What the containers section offers: the character's containers, the shared pages, the tab.
    /// Null without a container. <paramref name="personal"/> is the character's stash, if any.
    /// </summary>
    private List<StashPageChoice>? ContainerChoices(out StashPageChoice? personal)
    {
        var choices = new List<StashPageChoice>();

        // The carried gold goes with the inventory, shown in the gold panel above the shared stash's.
        var carried = Session.Attributes is { } attributes
            ? new GoldViewModel(Session, attributes, AttributeEditor.Carried, L("Carried gold"), _moves, _sync)
            : null;
        if (Session.GridsNamed("Inventory").FirstOrDefault() is { } inventory)
            choices.Add(new StashPageChoice(L("Inventory"), inventory, IsCharacter: true, Title: "Inventory", Short: L("Inv"), Purse: carried));
        if (Session.GridsNamed("Cube").FirstOrDefault() is { } cube)
            choices.Add(Locked(new StashPageChoice(L("Cube"), cube, IsCharacter: true, Title: "Cube", LockKey: CharacterLock("Cube"))));

        personal = Session.GridsNamed("Stash").FirstOrDefault() is { } stash
            ? Locked(new StashPageChoice(L("Personal Stash"), stash, IsCharacter: true, Title: "Personal Stash",
                                         LockKey: CharacterLock("Stash"), Short: L("PStash")))
            : null;
        if (personal is not null)
            choices.Add(personal);

        if (choices.Count == 0)
            return null;

        // The shared stash's purse: its gold is the shared file's, shown with a shared page or
        // the materials tab.
        var shared = Stash.IsLoaded
            ? new GoldViewModel(
                L("Shared gold"), VaultMoves.SharedPurse, () => Stash.Gold, Stash.SetGold,
                Stash.MostGold, _moves, _sync, message => Session.Status = message)
            : null;
        // The page's title row says the shared gold.
        if (shared is not null)
            shared.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName == nameof(GoldViewModel.Value))
                    OnPropertyChanged(nameof(FactsText));
            };

        if (Stash.Pages.Count > 0)
            choices.Add(new StashPageChoice("", null, IsSeparator: true));

        int number = 0;
        foreach (var page in Stash.Pages)
        {
            number++;
            choices.Add(Locked(new StashPageChoice(L($"Shared {page.Name}"), page, Title: "Shared Stash", Purse: shared,
                                                   LockKey: $"shared:{Stash.FileName}:{page.Name}", Short: L($"SP {number}"))));
        }

        // The tab lives in the same file as the shared pages, so it is offered with them: Season 13's as one page,
        // Season 14's as the game's own tabs, one row each.
        if (Stash.IsLoaded)
        {
            foreach (var sheet in Materials.SheetsFor(Stash.KeepsSeason14))
                choices.Add(new StashPageChoice(sheet.Name, null, IsMaterials: true, Title: "Materials", Purse: shared, Short: sheet.Short, Sheet: sheet));
        }

        return choices;
    }

    /// <summary>
    /// A character's own page's lock key: by the character, so each keeps its own. Null without a
    /// character name, and the page then has no lock.
    /// </summary>
    private string? CharacterLock(string page) =>
        Session.CharacterName is { Length: > 0 } name ? $"character:{name}:{page}" : null;

    /// <summary>The page with its remembered lock.</summary>
    private StashPageChoice Locked(StashPageChoice page)
    {
        if (page.LockKey is { } key)
            page.IsLocked = Rules.Settings.IsPageLocked(key);
        return page;
    }

    /// <summary>
    /// Whether a kept section offers the same: the same pages, by name, over the same grid objects
    /// (GridCache keeps them) - an ordinary move changes what is in them and nothing else.
    /// </summary>
    private static bool SameChoices(IReadOnlyList<StashPageChoice>? kept, IReadOnlyList<StashPageChoice> fresh) =>
        kept is not null && kept.Count == fresh.Count
        && kept.Zip(fresh).All(pair => pair.First.Label == pair.Second.Label
                                       && ReferenceEquals(pair.First.Grid, pair.Second.Grid)
                                       && pair.First.IsSeparator == pair.Second.IsSeparator
                                       && pair.First.IsMaterials == pair.Second.IsMaterials
                                       && ReferenceEquals(pair.First.Sheet, pair.Second.Sheet)
                                       && (pair.First.Purse is null) == (pair.Second.Purse is null));

    /// <summary>
    /// The one section every container is in: inventory, cube and stash merged, as apart they were clunky to use.
    /// Each pane picks from all of them: the
    /// character's inventory, cube and own stash, then the shared stash's nine pages and its
    /// materials tab - so anything moves between any two with the buttons between the panes.
    /// Null without an inventory, which is no character at all.
    /// </summary>
    private ContainerSectionViewModel? ContainersSection(List<StashPageChoice> choices, StashPageChoice? personal)
    {
        var first = choices[0];

        var purses = choices.Select(page => page.Purse).OfType<GoldViewModel>().Distinct().ToList();
        var carried = purses.FirstOrDefault(purse => purse.Purse == MoveRecovery.CarriedPurse);
        var shared = purses.FirstOrDefault(purse => purse.Purse == VaultMoves.SharedPurse);

        var section = new ContainerSectionViewModel(first.Title, first.Grid!, choices, key: "Containers")
        {
            Rules = Rules,
            GoldBetween = carried is not null && shared is not null
                ? new GoldTransferViewModel(carried, shared, _sync, message => Session.Status = message)
                : null,
        };

        // The inventory beside the character's stash, to begin with: the move made most.
        if (personal is not null && !ReferenceEquals(personal, first))
            section.SecondPage = personal;

        // The materials tab's clear button shows only while the tab does.
        section.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(ContainerSectionViewModel.ShowsMaterials) or nameof(ContainerSectionViewModel.SecondShowsMaterials))
            {
                OnPropertyChanged(nameof(MaterialsShown));
                OnPropertyChanged(nameof(CanClearMaterials));
                ClearMaterialsArmed = false;
            }

            // What the panes show is where a gem or a copy may land (OpenContainers) - and what may stay picked.
            if (changed.PropertyName is nameof(ContainerSectionViewModel.SelectedPage)
                or nameof(ContainerSectionViewModel.SecondPage) or nameof(ContainerSectionViewModel.ShowSecond))
            {
                _landing.SayRoomChanged();
                SayArrangeBlocked();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => DropUnseenPicks(section));
            }

            // A pane showing other items is another set of marks, or none.
            if (changed.PropertyName is nameof(ContainerSectionViewModel.HasPicks))
                OnPropertyChanged(nameof(PageHasPicks));
        };

        return section;
    }

    /// <summary>
    /// Whether the two stashes ended up beside each other rather than one above the other.
    /// <para>
    /// The panes flow, so the window decides - only the view knows what it was given, and it
    /// says so. This is here purely so Swap's arrows point the way the panes actually lie.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool _stashesSideBySide;

    partial void OnStashesSideBySideChanged(bool value)
    {
        foreach (var section in Sections)
            section.SideBySide = value;
    }

    /// <summary>The section a page is one of.</summary>
    private ContainerSectionViewModel? SectionOf(StashPageChoice? page) =>
        page is null or { IsSeparator: true } ? null : Sections.FirstOrDefault(section => section.Pages?.Contains(page) == true);

    /// <summary>A row of the pages' list clicked: the first pane shows that page.</summary>
    [RelayCommand]
    private void ShowPage(StashPageChoice? page)
    {
        if (SectionOf(page) is not { } section)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        section.SelectedPage = page;
    }

    /// <summary>
    /// A row clicked with Ctrl, or its menu's row: the second pane shows that page, opened where it was shut - and
    /// on the page the second pane shows already, the pane is shut.
    /// </summary>
    public void ShowSecond(StashPageChoice? page)
    {
        if (SectionOf(page) is not { } section)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        if (section.SecondShown && ReferenceEquals(section.SecondPage, page))
        {
            section.SecondShown = false;
            return;
        }

        section.SecondPage = page;
        section.SecondShown = true;
    }

    /// <summary>
    /// The pane across from the one that shows this item, where its section shows two; none where it
    /// shows one, or the item lies in neither (worn, on the mercenary).
    /// </summary>
    private (ContainerSectionViewModel Section, bool Downward)? AcrossFrom(ItemTileViewModel tile)
    {
        bool Shows(StashPageChoice? page) => page is { IsSeparator: false }
            && (page.IsMaterials ? (page.Sheet?.Panels ?? Materials.Panels).SelectMany(panel => panel.Grid.Items) : page.Grid?.Items ?? []).Contains(tile);

        foreach (var section in Sections)
        {
            if (!section.ShowSecond)
                continue;
            if (Shows(section.SelectedPage))
                return (section, true);
            if (Shows(section.SecondPage))
                return (section, false);
        }

        return null;
    }

    /// <summary>Whether <see cref="SendAcross"/> has somewhere to send this item: the tooltip's line is offered by it.</summary>
    public bool HasPaneAcross(ItemTileViewModel tile) => AcrossFrom(tile) is not null;

    /// <summary>
    /// How many <see cref="SendAcross"/> sends with this item, as its tooltip counts them: the picks on its side, or
    /// the one alone where either pane is the materials tab, which takes and gives one at a time (the line counted
    /// every pick, and only the one clicked went).
    /// </summary>
    public int SentAcrossWith(ItemTileViewModel tile) =>
        AcrossFrom(tile) is { Section: var section } && (section.SelectedPage?.IsMaterials == true || section.SecondPage?.IsMaterials == true)
            ? 1
            : MovedWith(tile);

    /// <summary>
    /// Ctrl + Shift + right click: the item to the pane across, and from there back - PD2's key for the cube, widened to "the other
    /// container". The click is the drag without the carrying: every pick on that side where the item
    /// clicked is one of several, landing in the first room of the page the other pane shows. It took the
    /// place of four arrow buttons between the panes.
    /// </summary>
    public void SendAcross(ItemTileViewModel tile)
    {
        if (Session.IsHolding)
            return;

        if (AcrossFrom(tile) is not { } across)
        {
            _sfxManager?.Method_ButtonSFX_Press();
            Session.Status = L("Require pane II open.");
            return;
        }

        Transfer(across.Section, across.Downward, tile);
    }

    /// <summary>
    /// Trades the contents of the two panes.
    /// <para>
    /// Not two moves. Moving everything one way and then back would need somewhere to put
    /// the first lot down, and there is nowhere - so both sides are emptied first and each
    /// is laid out in the other's grid.
    /// </para>
    /// <para>
    /// Only ever between two stashes of the same shape - see <see cref="CanSwap"/> - so
    /// where each item lands is simply where it already is: the cells do not change, only
    /// which page owns them. The swap cannot fail to fit, and a page that was ordered on
    /// purpose stays ordered.
    /// </para>
    /// <para>
    /// Both panes are empty in the middle of this, which is the one moment where a refusal
    /// would be expensive. A swap that cannot be finished is never begun; if a file refuses
    /// an item anyway - a wrong era, say - everything goes back exactly where it was.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void SwapPanes(ContainerSectionViewModel section)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        var first = section.SelectedPage;
        var second = section.SecondPage;

        if (first?.Grid is null || second?.Grid is null || ReferenceEquals(first, second))
        {
            Session.Status = L("Require two different containers.");
            return;
        }

        var here = first.Grid.Items.Select(tile => tile.Item).ToList();
        var there = second.Grid.Items.Select(tile => tile.Item).ToList();

        if (here.Count == 0 && there.Count == 0)
        {
            Session.Status = L("Both are empty.");
            return;
        }

        // Two stashes of the same shape, which the button will not light for anything else.
        // So nothing has to be arranged: each item keeps the cell it is in and only the page
        // it belongs to changes. It cannot fail to fit, because every item is going to a cell
        // that was free by definition - the one it is standing in.
        //
        // This is also what the eye expects. Laying both sides out afresh would shuffle a
        // page that was deliberately ordered, for no reason anyone asked for.
        if (first.Grid.Columns != second.Grid.Columns || first.Grid.Rows != second.Grid.Rows)
        {
            Session.Status = L($"{first.Label} and {second.Label} are different shapes.");
            return;
        }

        // Each as it stands now, so it can be put back if a file objects: both sides out first, in this order.
        var lifts = ItemLift.OfAll(here.Select(item => (Holder(first), item)).Concat(there.Select(item => (Holder(second), item))));
        string? refused = lifts.Count == here.Count + there.Count ? null : L("Item not moved.");

        // Each to the other pane, in the cell it stands in: a release leaves the item's cell as it was.
        for (int i = 0; i < lifts.Count && refused is null; i++)
        {
            var to = i < here.Count ? second : first;
            refused = lifts[i].Into(Holder(to), to.Grid!.Page, lifts[i].Item.X, lifts[i].Item.Y);
        }

        if (refused is not null)
        {
            // Back as it lay, nothing asked: the file that refused one may refuse it again, and it would be in no list.
            ItemLift.Back(lifts);
            Session.Status = L($"{refused} Nothing was swapped.");
        }
        else
        {
            ItemLift.Done(lifts);
            Session.Status = L($"Swapped {first.Label} and {second.Label}: {here.Count} one way, {there.Count} the other.");
        }

        Session.RefreshContainers();
        Stash.RefreshPages();
    }


    /// <summary>
    /// Moves the item clicked - or the picks it is one of - between the section's two panes.
    /// <para>
    /// The two may be in <b>different files</b> - the character's own stash and a page of
    /// the shared one - so each item is let go by whichever session holds it and taken in by
    /// the other. Both refuse an item whose version stamp is not theirs, because a save
    /// holds one era of items and the game refuses a mixture.
    /// </para>
    /// <para>
    /// Room is worked out against what is already there <b>plus what this move has put
    /// there</b>, since nothing is rebuilt until the end: asking the grid alone would lay
    /// every item of a batch on the same cell.
    /// </para>
    /// <para>
    /// An item that will not fit is <b>stepped over</b> rather than ending the move. Items
    /// come in different sizes, so a four-cell armour failing says nothing about the ring
    /// behind it - stopping there would leave gaps unfilled and look like a much smaller
    /// stash than there is. A refusal from the file itself does stop it, because that is
    /// the save disagreeing rather than the space running out.
    /// </para>
    /// </summary>
    private void Transfer(ContainerSectionViewModel section, bool downward, ItemTileViewModel clicked)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        var from = downward ? section.SelectedPage : section.SecondPage;
        var to = downward ? section.SecondPage : section.SelectedPage;

        // One side is the materials tab: counts on that side, items on the other.
        if (from is not null && to is not null && (from.IsMaterials || to.IsMaterials))
        {
            if (from.IsMaterials)
                TransferOutOfTab(to, clicked);
            else
                TransferIntoTab(from, clicked);
            return;
        }

        if (from?.Grid is null || to?.Grid is null || ReferenceEquals(from, to))
        {
            Session.Status = L("Require two different containers.");
            return;
        }

        // On one of several picked, the picks on this side go. They stay
        // picked: a pick is the item, which is the same item on the other side.
        bool several = IsPicking && clicked.IsPicked;
        var moving = several
            ? from.Grid.Items.Select(tile => tile.Item).Where(_picked.Contains).ToList()
            : [clicked.Item];

        if (!several && IsKept(clicked))
        {
            Session.Status = KeptSay(clicked);
            return;
        }

        // The target page read once. Each item that lands blocks its own cells, so room is
        // worked out against what is already there plus what this move has put there -
        // asking the grid alone would lay every item of a batch on the same cell, and
        // reading it again per item was what made a large move crawl.
        var grid = new GridSize(to.Grid.Columns, to.Grid.Rows);
        var page = new ItemMover.Occupancy(grid, to.Grid.Items.Select(tile => tile.Item));

        int moved = 0;
        int noRoom = 0;
        string? refused = null;

        foreach (var item in moving)
        {
            if (ItemMover.FirstFreeCell(item, page) is not { } cell)
            {
                // This one is too big for what is left. The next one may not be.
                noRoom++;
                continue;
            }

            if (ItemLift.Of(Holder(from), item) is not { } lift)
            {
                refused ??= L("Item not moved.");
                break;
            }

            if (lift.Into(Holder(to), to.Grid!.Page, cell.X, cell.Y) is { } trouble)
            {
                // Put it back where it came from rather than losing it.
                lift.Back();
                refused ??= trouble;
                break;
            }

            lift.Done();

            var (width, height) = ItemMover.Footprint(item);
            page.Block(cell.X, cell.Y, width, height);
            moved++;
        }

        // One rebuild at the end rather than one per item.
        Session.RefreshContainers();
        Stash.RefreshPages();

        string left = noRoom == 0 ? "" : " " + L($"{noRoom} did not fit.");

        Session.Status = moved == 0
            ? refused ?? (noRoom > 0 ? L($"No free space in {to.Label}.") : L("Nothing moved."))
            : Plural(moved, $"Moved 1 item to {to.Label}.", $"Moved {moved} items to {to.Label}.") + left + (refused is null ? "" : $" {refused}");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEquippedPicks))]
    private bool _equipmentIsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(EquipmentIsOpen));

    partial void OnEquipmentIsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(EquipmentIsOpen), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMercPicks))]
    private bool _mercIsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(MercIsOpen));

    partial void OnMercIsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(MercIsOpen), value);

    /// <summary>The worn panels' pick menus: while the panel is open, as a container's is.</summary>
    public bool ShowsEquippedPicks => EquipmentIsOpen;

    public bool ShowsMercPicks => MercIsOpen;

    /// <summary>
    /// The Iron Golem's item beside the mercenary's gear: shown to begin with -
    /// it is an item the character holds - and put away with the golem's button in the section's header,
    /// which is there only while a golem stands.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GolemShown))]
    private bool _showGolem = true;

    public bool HasGolem => Session.HasGolem;

    public bool GolemShown => ShowGolem && HasGolem;

    /// <summary>
    /// The golem's one slot, drawn as the mercenary's are: a box the size of the item it was made of.
    /// The same slot object while the item is the same, so its box is not built again with every move.
    /// </summary>
    public IReadOnlyList<EquipSlotViewModel> GolemSlots
    {
        get
        {
            if (Session.GolemTile is not { } tile)
                return _golemSlots = [];
            if (_golemSlots is [{ Item: { } held }] && ReferenceEquals(held, tile))
                return _golemSlots;
            return _golemSlots =
                [new EquipSlotViewModel(new EquipSlot(tile.Item.Location, L("Golem"), tile.View.GridWidth, tile.View.GridHeight, 0, 0), tile)];
        }
    }

    private IReadOnlyList<EquipSlotViewModel> _golemSlots = [];

    // ---- the corpse (CharacterSession.Corpse.cs) -------------------------------------------
    // What the character wore as it died, where it left the game without taking its body up: shown over the
    // worn slots, which then stand empty, each item chosen and edited as any is - and one button takes all of it
    // back, as a click on the body does in game. Only while the save has a corpse.

    public bool HasCorpse => Session.HasCorpse;

    /// <summary>The corpse's items, a box each: a new list only where the items are other ones.</summary>
    public IReadOnlyList<EquipSlotViewModel> CorpseSlots =>
        _corpseSlots.SequenceEqual(Session.CorpseEquipped) ? _corpseSlots : _corpseSlots = [.. Session.CorpseEquipped];

    private IReadOnlyList<EquipSlotViewModel> _corpseSlots = [];

    /// <summary>Under the corpse's heading: how many lie on it.</summary>
    public string CorpseDetail => Plural(Session.CorpseCount, "1 item on the corpse", $"{Session.CorpseCount} items on the corpse");

    [RelayCommand]
    private void TakeFromCorpse()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        Session.TakeFromCorpse();
    }

    /// <summary>The item the golem was made of, by name: under its heading, where the mercenary's kind and level are.</summary>
    public string GolemDetail => Session.GolemTile?.Name ?? "";

    /// <summary>Hidden, the golem's item lets go of the selection: nothing unseen is edited.</summary>
    partial void OnShowGolemChanged(bool value)
    {
        if (!value && Session.SelectedItem is { } chosen && Session.IsGolemItem(chosen.Item))
            Session.SelectedItem = null;
    }

    [RelayCommand]
    private void ToggleGolem()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        ShowGolem = !ShowGolem;
    }

    /// <summary>The golem gone and its item back on the character. Its outcome is the strip's.</summary>
    [RelayCommand]
    private void DismissGolem()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        var item = Session.GolemTile?.Item;
        Session.DismissGolem();

        // Its item, given back on the character's side: on to the shared page a pane shows, as everything that
        // arrives lands (ItemLanding) - and the strip says where it is now.
        if (item is not null && _landing.Rehome(item) is { } moved)
            Session.Status = L($"Iron Golem dismissed: {ItemPresenter.Describe(item).Name} in shared page {moved.Page}.");
    }

    /// <summary>
    /// Which weapon set is in hand, 1 or 2 - the tab lit over the two weapon boxes. The game keeps both pairs on
    /// the character and swaps between them; the I and II tabs do the same here.
    /// <para>
    /// <b>The tabs are the game's</b>: the boxes always show the pair in hand - the main
    /// hand slots', whichever tab the save names for them (<see cref="CharacterSession.TabInHand"/>) - and the
    /// other tab swaps, as clicking it in game does (<see cref="CharacterSession.SwapWeapons"/>): an edit, which
    /// the figure and the requirements follow. Until then I was always the main slots and II the swap slots, a
    /// tab only chose which pair was looked at, and a character saved on its II tab showed under I what the game
    /// shows under II.
    /// </para>
    /// </summary>
    [ObservableProperty] private int _weaponSet = 1;

    /// <summary>The tab lit, brought to what the character says - opened, undone, saved by the game on its other tab.</summary>
    private void FollowTabInHand()
    {
        if (IsSwapping || WeaponSet == Session.TabInHand)
            return;

        WeaponSet = Session.TabInHand;
        OnPropertyChanged(nameof(IsSetI));
        OnPropertyChanged(nameof(IsSetII));
    }

    public bool IsSetI => WeaponSet == 1;
    public bool IsSetII => WeaponSet == 2;

    /// <summary>
    /// One command per tab rather than a parameter: XAML hands a literal over as a
    /// string, and a generated command taking an int refuses it.
    /// </summary>
    [RelayCommand]
    private void ShowSetI() => ShowSet(1);

    [RelayCommand]
    private void ShowSetII() => ShowSet(2);

    /// <summary>
    /// True while the weapon boxes are between sets, which is what fades them.
    /// <para>
    /// The swap has to be in two parts for that. The list is rebuilt on a set change, so
    /// the boxes on screen when it happens are thrown away: fading them out has to finish
    /// before the rebuild, and the ones that replace them are born already faded and come
    /// up once this goes back off.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _isSwapping;

    /// <summary>Long enough to read as a change, short enough not to be waited on.</summary>
    private static readonly TimeSpan SwapFade = TimeSpan.FromMilliseconds(110);

    private void ShowSet(int set)
    {
        if (WeaponSet == set || IsSwapping)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        if (Session.CannotSwapWeapons() is { } refused)
        {
            Session.Status = refused;
            return;
        }

        // The tabs answer at once; it is the boxes below them that take the moment - and the swap itself, made
        // once they have faded.
        WeaponSet = set;
        OnPropertyChanged(nameof(IsSetI));
        OnPropertyChanged(nameof(IsSetII));

        IsSwapping = true;
        DispatcherTimer.RunOnce(
            () =>
            {
                string? late = Session.SwapWeapons();
                IsSwapping = false;
                if (late is not null)
                    Session.Status = late;

                // Refused after all, or the character gone meanwhile: the tab back to what is in hand.
                FollowTabInHand();
                ShowEquipped();
            },
            SwapFade);
    }

    public int WornCount => Equipped.Count(slot => slot.IsFilled);

    /// <summary>
    /// The count when something is worn; nothing at all when nothing is - not "(nothing worn)", not "(0)": the empty
    /// slots say it. The mercenary's the same.
    /// </summary>
    public string EquipmentHeader =>
        WornCount > 0 ? L($"Equipped  ({WornCount})") : L("Equipped");

    public bool HasMerc => Session.Mercenary.IsHired;

    public int MercWornCount => MercEquipped.Count(slot => slot.IsFilled);

    public string MercHeader => !HasMerc
        ? L("Mercenary  (none hired)")
        : MercWornCount > 0 ? L($"Mercenary  ({MercWornCount})") : L("Mercenary");


    public bool HasCharacter => Session.IsLoaded;


    /// <summary>
    /// Said out loud when the character is carrying something on a page none of these
    /// containers draws, so a wrong page number shows up as a warning rather than as items
    /// quietly going missing.
    /// </summary>
    public bool HasUnknownPages => Session.UnknownPages.Count > 0;

    public string UnknownPagesWarning => !HasUnknownPages
        ? ""
        : L($"Carrying {UnknownPagesList} — unknown container. Not lost, not drawn here.");

    private string UnknownPagesList => string.Join(", ", Session.UnknownPages.Select(
        entry => Plural(entry.Count, $"{entry.Count} item on page {entry.Page}", $"{entry.Count} items on page {entry.Page}")));

    /// <summary>The warning's button: the stray items into the first container with room.</summary>
    [RelayCommand]
    private void MoveStrays() => Session.RehomeStrays();

    // No move lock. It kept a
    // click from lifting an item; a click now only selects, and moving takes a drag.

    /// <summary>
    /// Sockets drawn over an item hide the art underneath, which is the whole point until
    /// you want to see the item itself. Faded rather than hidden, so they are still there
    /// to be counted.
    /// <para>
    /// A mode, not a peek. It began as a look at one item and put itself back when the
    /// pointer moved off; now that a button holds it as plainly as the key does, answering
    /// to the pointer as well would mean a button that turns itself off.
    /// </para>
    /// </summary>
    public bool SocketsFaded
    {
        get => Rules.SocketsFaded;
        set => Rules.SocketsFaded = value;
    }

    /// <summary>One state with the vault's page (PlayRules.SocketsFaded): faded here is faded there.</summary>
    public double SocketOpacity => Rules.SocketOpacity;

    [RelayCommand]
    public void ToggleSockets()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SocketsFaded = !SocketsFaded;
    }

    /// <summary>Says which way the press goes, since the icon alone cannot.</summary>
    public string SocketHint => SocketWords.Look(SocketsFaded);


    /// <summary>
    /// The page's facts on its title row, at the right, as the vault's are: what the character holds, and the shared stash's gold - the character's
    /// carried and stashed without a shared stash. Empty with no character.
    /// </summary>
    public string FactsText
    {
        get
        {
            if (Session.Attributes is not { } attributes)
                return "";
            int count = Session.ItemCount;
            long gold = Stash.IsLoaded
                ? Stash.Gold
                : (long)attributes.Gold(AttributeEditor.Carried) + attributes.Gold(AttributeEditor.Stashed);
            // And how many are picked, as the vault's line says it: the page said it nowhere.
            return Plural(count, "1 item", $"{count} items") + " · " + L($"{gold:N0} gold")
                   + (PickedCount > 1 ? " · " + L($"{PickedCount} selected") : "");
        }
    }

    public string DeleteHint => DeleteArmed
        ? L("Confirm deletion") + Environment.NewLine + L("Click elsewhere to cancel")
        : ShortcutText.With(IsPicking ? L($"Delete {Selected(PickedCount)}") : L("Delete selected item"), L("Del"));

    /// <summary>"1 selected item", "3 selected items".</summary>
    private static string Selected(int count) =>
        Plural(count, "1 selected item", $"{count} selected items");

    /// <summary>
    /// Whether the delete button has been clicked once and is waiting to be meant.
    /// <para>
    /// A bin deletes a thing and always asks; a cross or a brush takes something off and never does.
    /// It acted at once for a day, by the rule that what undo brings back is not asked about - but that undo
    /// ends with the session, and an item gone by a stray click or key was then gone for good.
    /// The button turns into a red tick rather than opening a dialogue: a second click on
    /// the same spot confirms, and a click anywhere else is a refusal.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteHint))]
    private bool _deleteArmed;

    /// <summary>Takes back the arming, whatever else the click was for.</summary>
    public void DisarmDelete() => DeleteArmed = false;

    /// <summary>
    /// Whether the selected item can be thrown away - worn too, the character's or the
    /// mercenary's (reversing an earlier read-only rule: the save
    /// keeps base stats, not totals with gear, so nothing goes stale when an item leaves). A
    /// belt holding potions above its bottom row is refused - see CannotTakeOff.
    /// </summary>
    public bool CanDelete => IsPicking
        ? PickedCount > 0
        : Session.SelectedItem is { TabSlot: null };

    private readonly GameSync? _sync;

    /// <summary>
    /// Store would move items out of a game file while the game runs: off and red. See GameSync.
    /// </summary>
    public bool StoreBlocked => _sync?.IsWriteBlocked ?? false;

    /// <summary>Whether there is something to store: the item looked at, or the picks.</summary>
    public bool CanStore => !StoreBlocked && !StoreLocked && (IsPicking ? PickedCount > 0 : Session.SelectedItem is { TabSlot: null });

    /// <summary>
    /// The inventory lock holds all that Store would take: said on the button, dimmed. What is marked to go already
    /// can still be taken back.
    /// </summary>
    public bool StoreLocked
    {
        get
        {
            if (!InventoryLock)
                return false;

            List<ItemTileViewModel> going = IsPicking ? PickedTiles() : Session.SelectedItem is { TabSlot: null } tile ? [tile] : [];
            return going.Count > 0 && going.All(each => IsInInventory(each) && !_moves.IsLeaving(each.Item));
        }
    }

    public string StoreHint => StoreBlocked ? (_sync?.BlockedFor(L("store")) ?? GameSync.Blocked(L("store")))
        : StoreLocked ? L("Inventory locked.")
        : IsPicking
            ? (PickedTiles() is { Count: > 0 } picked && picked.All(tile => _moves.IsLeaving(tile.Item))
                ? L($"Keep {Selected(PickedCount)} out of vault")
                : L($"Store {Selected(PickedCount)} in vault"))
            : Session.SelectedItem is { } chosen && _moves.IsLeaving(chosen.Item)
                ? L("Keep selected item out of vault")
                : L("Store selected item in vault");

    /// <summary>The Store button's tooltip: what it does, then its modified clicks.</summary>
    public string StoreTip => StoreBlocked || StoreLocked
        ? StoreHint
        : StoreHint + Environment.NewLine + L("Ctrl + LMB: sorted") + Environment.NewLine + L("Shift + LMB: as crafting base");
}
