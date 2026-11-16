using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Imaging;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Core.Graphics;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A row in a picker that can be the one already in use. Shared so the view can find and
/// scroll to it without knowing which panel it is looking at.
/// </summary>
public interface IPickRow
{
    bool IsHeld { get; }
}

/// <summary>
/// One side of one affix slot, as the panel draws it: what is in it, and whether the list
/// below is currently aimed at it.
/// </summary>
public sealed record AffixSlotRow(
    int Slot, string PrefixName, string SuffixName,
    bool PrefixAimed, bool SuffixAimed);

/// <summary>
/// One affix in the list, and whether it is the one already in the slot being filled -
/// which is worth marking, since picking it again is a reroll rather than a change.
/// </summary>
public sealed record AffixChoiceRow(
    MagicAffixes.AffixOption Option, bool IsHeld, bool IsAboveLevel = false,
    int NeedsLevel = 0, bool IsTaken = false, string Telling = "") : IPickRow
{
    public string Name => Option.Name;

    /// <summary>
    /// What tells this row from another of the list that reads the same - a Paladin shield fits three Monk's, by
    /// <c>ashd</c>, <c>shld</c> and the armour rows, so many with the same stats: the affix level it rolls at,
    /// the level it asks of the wearer, rare only. Empty for a row read once.
    /// As the automagic picker's rows read by what tells them apart.
    /// </summary>
    public bool HasTelling => Telling.Length > 0;

    /// <summary>
    /// What item level this one asks for, said only where the item is short of it - and it
    /// is the item level, not the affix's own level, which on most bases is higher. Or that
    /// another slot holds its group, which no level answers.
    /// </summary>
    public string LevelNote => IsTaken ? L("in another slot") : IsAboveLevel ? L($"item level {NeedsLevel}") : "";
    public string Readable => Option.Readable;
    public bool NeedsParam => Option.NeedsParam;

    /// <summary>
    /// Pickable: not one asking for a parameter, nor one whose group another slot holds - shown greyed, with why
    /// rather than hidden.
    /// </summary>
    public bool CanPick => !Option.NeedsParam && !IsTaken;
}

/// <summary>
/// One half of a rare name in the list, and whether it is the half already chosen.
/// </summary>
public sealed record RareHalfRow(RareHalf Half, bool IsHeld) : IPickRow
{
    public string Name => Half.Name;
}

/// <summary>
/// The right inventory page: what the selected item is, what is socketed into it, and its
/// editable stats.
/// </summary>
public partial class VP_ViewModel_Inv_R : ViewModelBase, IItemPanel
{
    public IItemEditHost Session { get; }

    /// <summary>What every editor has staged and KEEP has not yet written.</summary>
    public ItemDraft Draft { get; } = new();

    public BasicEditor Basic { get; }

    public RareNameEditor RareName { get; }

    public AffixEditor Affixes { get; }

    public SocketsEditor Sockets { get; }

    public CorruptionEditor Corruption { get; }

    public DesecrationEditor Desecration { get; }

    public CraftingEditor Crafting { get; }

    /// <summary>The superior bonus: its line under Affixes and its picker (SuperiorEditor).</summary>
    public SuperiorEditor Superior { get; }

    /// <summary>The base's automagic row, chosen as a superior bonus is (AutomagicEditor).</summary>
    public AutomagicEditor Automagic { get; }

    /// <summary>A class base's staff mods, chosen as a superior bonus is (StaffModsEditor).</summary>
    public StaffModsEditor StaffSkills { get; }

    /// <summary>What the cube does to a map with one item: its button among the tools and its picker (MapRecipeEditor).</summary>
    public MapRecipeEditor MapRecipe { get; }

    public RunewordEditor Runeword { get; }

    /// <summary>
    /// What the play mode shows in this panel - Legit shows the item and changes nothing.
    /// </summary>
    public PlayRules Rules { get; }

    /// <summary>Several items are selected beside the panel, which edits one item at a time.</summary>
    public bool IsPicking => Session.IsPicking;

    /// <summary>What the panel says in place of an item while several are selected: how many.</summary>
    public string PickedText => L($"{Session.PickedCount} selected");

    ItemTileViewModel? IItemPanel.Selected => Session.SelectedItem;
    Item IItemPanel.Staged(Item item, bool withPicks) => Staged(item, withPicks);
    Item IItemPanel.StagedToChange(Item item, bool withPicks) => StagedAnew(item, withPicks);
    Item? IItemPanel.Pending() => Pending();
    void IItemPanel.StageStatsFrom(Item shadow, ItemTileViewModel tile) => StageStatsFrom(shadow, tile);
    void IItemPanel.Changed() => NotifyChanges();
    void IItemPanel.Press() => _sfxManager?.Method_ButtonSFX_Press();
    void IItemPanel.Reveal(params string[] lists) => Reveal(lists);
    int? IItemPanel.CharacterLevel => (Session as CharacterSession)?.Leveling?.Level;
    Services.Settings.PlayRules IItemPanel.Rules => Rules;
    bool IItemPanel.SocketsAreChanged => Sockets.AreChanged;
    bool IItemPanel.IsTabTile => IsTabTile;
    int IItemPanel.MaximumSockets(ItemTileViewModel tile) => Session.MaximumSockets(tile);
    bool IItemPanel.CanEmptySockets => Session.CanEmptySockets;
    string? IItemPanel.Eject(ItemTileViewModel tile, Item gem) => Eject(tile, gem);
    bool IItemPanel.CopiesSocketed => _landing is not null && Session is CharacterSession && Rules.Duplicates;
    string? IItemPanel.EjectTo(ItemTileViewModel tile, Item gem, GridCell cell) => EjectTo(tile, gem, cell);
    void IItemPanel.ShowStagedRuneword(ItemTileViewModel tile) => ShowStagedRuneword(tile);
    void IItemPanel.NameChanged() => Affixes.Build();
    IReadOnlyList<StatRowViewModel> IItemPanel.StatRows => Stats;
    void IItemPanel.AffixesChanged(bool rebuild)
    {
        if (rebuild)
            Affixes.BuildPairs();
        Affixes.Notify();
        Basic.AffixesChanged();
    }

    // ---- a jewel in a socket, in a layer of its own --------------------------
    //
    // A socketable that is a jewel has a button in the socket section that opens a second layer of edit, the jewel
    // in situ. The same keep and undo at that level, and a header to show the exit back to the parent panel.
    //
    // The layer is this same panel again, over a host of its own (SocketedEditHost) whose item is the jewel:
    // every section works on the jewel as it does on any item, and what is written is the jewel
    // inside the item that holds it - so the file that changed is this panel's item's, marked as this panel
    // marks its own. One layer panel, made the first time a jewel is opened and handed the next jewel after.
    // It shuts when this panel's item is no longer the one holding the jewel: another item chosen, the item
    // gone, an undo that put the file's items back as they were.

    /// <summary>The jewel's panel while one is open, over this one; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NestedBack))]
    private VP_ViewModel_Inv_R? _nested;

    private VP_ViewModel_Inv_R? _layer;
    private SocketedEditHost? _layerHost;
    private Item? _nestedIn, _nestedJewel;

    /// <summary>The way back, named by where it leads.</summary>
    public string NestedBack => Session.SelectedItem is { } tile ? L($"Back to {tile.Name}") : L("Back");

    /// <summary>Not where the page makes its own item - the Forge: no jewel is opened there.</summary>
    public bool OpensSocketed => !Session.ChoosesItsItem;

    /// <summary>Opens a jewel of the selected item's sockets in the layer. With changes waiting on this item, they are settled first.</summary>
    public void OpenSocketed(Item jewel)
    {
        if (!OpensSocketed || Session.SelectedItem is not { } tile || !tile.Item.SocketedItems.Contains(jewel))
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        if (HasChanges)
        {
            // What waits, said as the page says it: a gem with no room to come out to, a value out of range.
            Say(ChangeNote is { Length: > 0 } waiting ? waiting : L("Finish the open change first."));
            return;
        }

        if (_layer is null || _layerHost is null)
        {
            _layerHost = new SocketedEditHost(Session);
            _layer = new VP_ViewModel_Inv_R(_layerHost, _sfxManager!, null, Rules);
        }

        var holder = tile.Item;
        _layerHost.Marked = () =>
        {
            Session.MarkEdited();
            if (_stash?.Holds(holder) == true)
                _stash.MarkEdited();
        };
        _layerHost.Redrawn = () =>
        {
            Session.SelectedItemChanged();
            _stash?.Redraw(holder);
        };

        (_nestedIn, _nestedJewel) = (holder, jewel);
        _layerHost.SelectedItem = new ItemTileViewModel(jewel, 0);
        Nested = _layer;
    }

    /// <summary>Back to this panel's own item: the layer shut, what it had staged dropped, the sockets read again.</summary>
    [RelayCommand]
    private void CloseNested()
    {
        if (Nested is null)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        ShutNested();
        Refresh();
    }

    private void ShutNested()
    {
        Nested = null;
        (_nestedIn, _nestedJewel) = (null, null);
        if (_layerHost is not null)
            _layerHost.SelectedItem = null;
    }

    int IItemPanel.StagedSockets => Sockets.Staged;
    void IItemPanel.StageSocketsFrom(Item shadow) => Sockets.StageFrom(shadow);

    void IItemPanel.StageDefense(int defense)
    {
        if (Basic.HasDefense)
            Basic.Defense = defense.ToString();
    }

    void IItemPanel.StageDurability(int durability)
    {
        if (Basic.HasDurability)
            Basic.Durability = durability.ToString();
    }

    void IItemPanel.ShowBasicsOf(Item shadow)
    {
        Basic.ItemLevel = shadow.ItemLevel.ToString();
        RareName.ShowFrom(shadow);
    }

    private readonly ISFXManager _sfxManager = null!;

    /// <summary>What is in the selected item's sockets, empty ones included.</summary>

    /// <summary>
    /// The selected item's properties, as rows that hold their edits back.
    /// <para>
    /// Nothing typed here reaches the item until it is saved. Looking at an item should
    /// not be able to change it by accident, and half-finished numbers are exactly what
    /// gets left behind when attention moves elsewhere - so moving elsewhere throws them
    /// away rather than quietly keeping them.
    /// </para>
    /// </summary>
    public ObservableCollection<StatRowViewModel> Stats { get; } = [];

    /// <summary>
    /// What the legitimacy check makes of the item as the page holds it.
    /// <para>
    /// Asked of the staged item rather than the stored one, so it answers for what KEEP
    /// would write. Rebuilt rather than computed on demand: the check copies the item and
    /// walks every affix and property, which is not something to do inside a property
    /// getter the view reads whenever anything changes.
    /// </para>
    /// </summary>
    public ObservableCollection<FindingRow> Findings { get; } = [];

    public bool HasFindings => !IsTabTile && Findings.Count > 0;

    /// <summary>
    /// Whether the item looked at is a slot of the shared stash's materials tab. Its tile stands
    /// for a count, not an item: the count is all there is to edit, so every other part of the
    /// panel is closed, and applying writes the count into the tab.
    /// </summary>
    private bool IsTabTile => Session.SelectedItem?.TabSlot is not null;

    private readonly SharedStashSession? _stash;

    /// <summary>
    /// Whether anything found is something the game could not hold, which is the one case
    /// worth colouring the heading for.
    /// </summary>
    public bool AnyImpossible => Findings.Any(row => row.IsImpossible);

    public string FindingsHeader => L("Warning");

    /// <summary>
    /// Open to begin with, and folded away by anyone who would rather not read it - the
    /// same as every other section on this page. It is a warning, not a modal.
    /// </summary>
    [ObservableProperty] private bool _findingsOpen = PD2SPCompanion.Shell.Services.Settings.FoldMemory.IsOpen(nameof(FindingsOpen));

    partial void OnFindingsOpenChanged(bool value) => PD2SPCompanion.Shell.Services.Settings.FoldMemory.Keep(nameof(FindingsOpen), value);

    [RelayCommand]
    private void ToggleFindings()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        FindingsOpen = !FindingsOpen;
    }

    private void BuildFindings()
    {
        Findings.Clear();

        // Off entirely when the user has said they would rather edit without being told.
        if (!StatRowViewModel.FlagIllegitimate || Session.SelectedItem is not { } tile)
        {
            OnPropertyChanged(nameof(HasFindings));
            OnPropertyChanged(nameof(AnyImpossible));
            OnPropertyChanged(nameof(FindingsHeader));
            return;
        }

        var looking = Pending() ?? tile.Item;

        foreach (var finding in Legitimacy.Check(looking))
            Findings.Add(new FindingRow(finding));

        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(AnyImpossible));
        OnPropertyChanged(nameof(FindingsHeader));
    }

    /// <summary>
    /// Properties picked from the list and not yet kept. They are not on the item - nothing
    /// is, until KEEP - so they live here and are attached by <c>ApplyStats</c>. Cleared by
    /// <c>BuildStats</c>, which is what undo, discard and moving to another item all call.
    /// </summary>
    private readonly List<ItemStat> _added = [];

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Inv_R() : this(new CharacterSession(), null!, null!, new PlayRules(new EditorSettings())) { }

    public VP_ViewModel_Inv_R(CharacterSession session, ISFXManager sfxManager, SharedStashSession stash, PlayRules rules,
                              ItemLanding? landing = null)
        : this((IItemEditHost)session, sfxManager, stash, rules)
    {
        _landing = landing;

        // "No free space" is asked again when room may have come or gone: a pane opened on a page with
        // room, a move. Only while gems are staged to leave, the one thing it answers for.
        if (landing is not null)
        {
            landing.RoomChanged += () =>
            {
                if (!Sockets.AreChanged)
                    return;

                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(ChangeNote));

                // Kept at once, the gem held back for want of room goes as soon as there is some: nothing
                // else would ask again (no KEEP to press). After the move that made the room is over.
                if (KeepsAtOnce && CanApply)
                    Dispatcher.UIThread.Post(() => KeepAtOnce());
            };
        }
    }

    /// <summary>Where a gem turned out of its socket lands, on the character's page - see ItemLanding. Null on the vault's page.</summary>
    private readonly ItemLanding? _landing;

    /// <summary>
    /// Turns a gem out of the selected item, landing it around that item, not on the first page - beside it, else in a pane's open
    /// container, else the first room. On the vault's page, as the host does it.
    /// </summary>
    private string? Eject(ItemTileViewModel tile, D2SLib.Model.Save.Item gem)
    {
        if (_landing is null || Session is not CharacterSession character)
            return Session.Unsocket(tile, gem);

        if (_landing.Find(gem, tile.Item) is not { } spot)
        {
            Say(L("No free space."));
            return L("No free space.");
        }

        return spot.Shared && _stash is { } stash
            ? character.Unsocket(tile, gem, null, each => stash.Adopt(each, spot.Page, spot.X, spot.Y))
            : character.Unsocket(tile, gem, (spot.Page, (spot.X, spot.Y)), null);
    }

    /// <summary>
    /// A gem out of the selected item's socket onto a cell of the character's or of a shared page - one the asker
    /// found free (a drag let go of there, a paste made there). Refused by the file that takes it, it goes where
    /// it would land turned out (<see cref="Eject"/>).
    /// </summary>
    private string? EjectTo(ItemTileViewModel tile, D2SLib.Model.Save.Item gem, GridCell cell)
    {
        if (Session is not CharacterSession character)
            return Eject(tile, gem);

        string? refused = cell.Shared
            ? _stash is { } stash ? character.Unsocket(tile, gem, null, each => stash.Adopt(each, cell.Page, cell.Column, cell.Row)) : L("No free space.")
            : character.Unsocket(tile, gem, (cell.Page, (cell.Column, cell.Row)), null);
        return refused is null ? null : Eject(tile, gem);
    }

    /// <summary>
    /// The same panel over another host - the vault's Edit page. A panel of its own, so it keeps
    /// a selection of its own. There is no shared stash behind it: a vault item is never a
    /// materials-tab slot.
    /// </summary>
    public VP_ViewModel_Inv_R(IItemEditHost session, ISFXManager sfxManager, SharedStashSession? stash, PlayRules rules)
    {
        Session = session;
        Basic = new BasicEditor(this);
        RareName = new RareNameEditor(this);
        Affixes = new AffixEditor(this);
        Sockets = new SocketsEditor(this);

        // A gem cut in its socket is turned out by its paste (ItemClipboard), wherever that is made: this panel's
        // to do where it is the character's - the one page gems come out on - and still shows the item.
        if (session is CharacterSession character && stash is not null)
        {
            PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.EjectSocketed = (host, gems, at) =>
                Session.SelectedItem is { } shown && ReferenceEquals(shown.Item, host) && Sockets.EjectAll(gems, at)
                    ? [.. gems.Select(character.TurnedOut)]
                    : null;
        }
        Corruption = new CorruptionEditor(this);
        Desecration = new DesecrationEditor(this);
        Crafting = new CraftingEditor(this);
        Superior = new SuperiorEditor(this);
        Automagic = new AutomagicEditor(this);
        StaffSkills = new StaffModsEditor(this);
        MapRecipe = new MapRecipeEditor(this);
        Runeword = new RunewordEditor(this);
        _stash = stash;
        _sfxManager = sfxManager;
        Rules = rules;
        rules.PropertyChanged += (_, _) =>
        {
            // The Basic fields answer to their own setting - "Defense, durability, quantity,
            // charges, picture" - not to the properties'. They shared the lock's one flag.
            Basic.Notify();
            Runeword.Notify();
            // A socket's Duplicate is the play mode's to offer.
            Sockets.Notify();
            FollowPlayMode();
        };

        Session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.SelectedItem))
            {
                // The jewel's layer stands only over the item that holds the jewel.
                if (Nested is not null
                    && !(Session.SelectedItem is { } still && ReferenceEquals(still.Item, _nestedIn)
                         && _nestedJewel is not null && still.Item.SocketedItems.Contains(_nestedJewel)))
                {
                    ShutNested();
                }

                OnPropertyChanged(nameof(NestedBack));
                Refresh();
            }

            if (changed.PropertyName == nameof(IItemEditHost.IsPicking))
                OnPropertyChanged(nameof(IsPicking));

            if (changed.PropertyName == nameof(IItemEditHost.PickedCount))
                OnPropertyChanged(nameof(PickedText));
        };

        // What a picker staged is kept when it closes, where the host keeps at once.
        if (session.KeepsAtOnce)
        {
            PropertyChanged += KeepWhenClosed;
            Affixes.PropertyChanged += KeepWhenClosed;
            Corruption.PropertyChanged += KeepWhenClosed;
            Desecration.PropertyChanged += KeepWhenClosed;
            Crafting.PropertyChanged += KeepWhenClosed;
            Superior.PropertyChanged += KeepWhenClosed;
            Automagic.PropertyChanged += KeepWhenClosed;
            StaffSkills.PropertyChanged += KeepWhenClosed;
            MapRecipe.PropertyChanged += KeepWhenClosed;
            Runeword.PropertyChanged += KeepWhenClosed;
            RareName.PropertyChanged += KeepWhenClosed;
            StatPicker.PropertyChanged += KeepWhenClosed;
        }

        Refresh();
    }

    /// <summary>
    /// The row whose skill list is showing, or null.
    /// <para>
    /// The list belongs to the page rather than to the row that asked for it. Anchoring it
    /// to a control means placing a popup against that control, and placing a popup is the
    /// part that does not work - it sizes and positions itself from numbers that are not
    /// settled when it opens. Over the page there is nothing to anchor to and nothing to
    /// get wrong.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPickerOpen))]
    private StatRowViewModel? _openPicker;

    public bool IsPickerOpen => OpenPicker is not null;

    /// <summary>
    /// What the panel shows. The same row as <see cref="OpenPicker"/> while it is open, and
    /// still that row for a moment after it closes: the panel fades out, and a panel whose
    /// contents vanish on the first frame of a fade looks like a fault rather than a close.
    /// </summary>
    [ObservableProperty]
    private StatRowViewModel? _pickerContent;

    partial void OnOpenPickerChanged(StatRowViewModel? value)
    {
        // Presence as well as content: see the picker presence region.
        Presence.Keep(value is not null, () => IsPickerOpen, on => IsPickerPresent = on, VeilFade);

        if (value is not null)
        {
            PickerContent = value;
            return;
        }

        var going = PickerContent;
        DispatcherTimer.RunOnce(
            () =>
            {
                // Unless it was opened again in the meantime, on this row or another.
                if (OpenPicker is null && ReferenceEquals(PickerContent, going))
                    PickerContent = null;
            },
            TimeSpan.FromMilliseconds(220));
    }

    [RelayCommand]
    private void ClosePicker()
    {
        if (OpenPicker is { } row)
            row.PickerOpen = false;
    }

    #region Picker presence

    /// <summary>
    /// How long a picker's veil takes to fade. Matches the transition in the view; a
    /// picker taken out of the layout before its fade ends would vanish rather than fade.
    /// </summary>
    internal static readonly TimeSpan VeilFade = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// Whether each picker is <b>present in the layout</b>, as against open.
    /// <para>
    /// A veil is hidden by opacity so it can fade, and something at zero opacity is still
    /// measured and arranged on every layout pass. The property picker holds all 503
    /// properties, so a closed picker was costing 3021 controls of layout on every switch
    /// to this page - enough to eat a 300ms page transition whole.
    /// </para>
    /// <para>
    /// So presence is kept apart from openness: it goes true the moment a picker opens and
    /// false only once the fade is over, which takes the rows out of the layout without
    /// taking the fade away. The same two-property shape the first-run overlay uses.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _isPickerPresent;


    #endregion

    /// <summary>
    /// Whether all that is staged is a corruption, and the numbers it writes: the game's cube leaves a legacy item as the
    /// season before stamped it - five Saracen's Chance of Season 13 cubed in Season 14 stayed 103, <c>corruptor</c> 1
    /// (LegacyTest); only a brick, a new item, came out 105. A corruption that bores sockets counts as an
    /// edit: not seen.
    /// </summary>
    private bool OnlyACorruption(ItemTileViewModel tile)
    {
        if (Draft.Corruption is not > 0 || Corruption.CleanStaged || HasChangesBeside)
            return false;

        var written = Corruptions.GrantsFor(tile.Item, Draft.Corruption.Value)
            .SelectMany(grant => AffixRanges.StatsWritten(grant.Code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Stats.Where(row => row.IsChanged).All(row => written.Contains(row.Name))
            && _added.All(stat => written.Contains(stat.Stat ?? ""));
    }

    public bool HasChanges =>
        HasChangesBeside || _added.Count > 0 || Corruption.IsStaged || Stats.Any(row => row.IsChanged);

    /// <summary>What is staged but a corruption and the rows it may have written.</summary>
    private bool HasChangesBeside =>
        Basic.QuantityIsChanged || Basic.ItemLevelIsChanged || Basic.EtherealIsChanged || Sockets.AreChanged
        || Basic.PersonalNameIsChanged
        || RareName.IsChanged || Draft.CraftFrom is not null
        || Basic.DefenseIsChanged || Basic.DurabilityIsChanged || Basic.MaxDurabilityIsChanged
        || Basic.GraphicIsChanged
        || Affixes.AnyStaged
        || Desecration.IsStaged
        || Draft.Code is not null
        || Draft.Runeword is not null
        || Draft.Quality is not null
        || Draft.AutoAffix is not null;

    /// <summary>A value that will not parse. Nothing is written while one is outstanding.</summary>
    public bool HasBadValue => Stats.Any(row => !row.IsValid) || Basic.PersonalNameIsBad;

    /// <summary>
    /// Nothing is written while a gem has nowhere to go.
    /// <para>
    /// Keeping only the part that fits would leave the item half edited and the page saying
    /// one thing while the file says another. Refusing the lot is the answer that loses
    /// nothing: make room, or take the ejection back.
    /// </para>
    /// </summary>
    public bool CanApply => HasChanges && !HasBadValue && Homeless.Count == 0;

    /// <summary>
    /// The gems that are staged to leave and would have nowhere to go - asked of the
    /// session, which reckons them all at once rather than one at a time, since turning
    /// three out into two free cells fits two of them and not the third.
    /// </summary>
    private IReadOnlyList<Item> Homeless
    {
        get
        {
            if (Session.SelectedItem is not { } tile || !Sockets.AreChanged)
                return [];

            var leaving = Sockets.Leaving(tile.Item);

            // By the rule they land by (ItemLanding) on the character's page; the vault says none.
            return _landing is not null && Session is CharacterSession
                ? _landing.WithoutRoom(leaving, tile.Item)
                : Session.WithoutRoom(leaving);
        }
    }

    /// <summary>
    /// Says what the panel's last action came to, on the right page's strip: the item panel's
    /// actions stay on the right page. What reaches
    /// another page - a gem turned out to the inventory, KEEP's edit - the host says on the left
    /// strip. It used to be a line under the item's name, cleared with the panel's rows.
    /// </summary>
    public void Say(string text) => Session.RightMessages.Post(text);

    /// <summary>
    /// The line under the name: only a state of the item in front of it - a warning that holds
    /// KEEP back. What an action came to goes to the strip (<see cref="Say"/>). No reminder of
    /// unsaved edits - it was obsolete, and its line appearing shifted the panel.
    /// </summary>
    public string ChangeNote => Homeless.Count > 0
        ? !Session.CanEmptySockets
            ? L("Socketed items do not come out here.")
            : Homeless.Count == 1
            ? L("No free space for it. Make space, or put it back.")
            : L($"No free space for {Homeless.Count} of them. Make space, or put them back.")
        : Basic.PersonalNameProblem is { } name
        ? name
        : HasBadValue
        ? L("Value out of range.")
        : "";

    /// <summary>
    /// What follows a write of the item - KEEP's, or a change kept at once: said, the host told, the files
    /// that hold the item marked.
    /// </summary>
    private void Wrote(ItemTileViewModel tile, bool crafted)
    {
        // Said on the strip before anything is marked: autosave names the history step and the
        // backup after the strip's message, and may start as soon as a change is flagged. Unsaid,
        // KEEP was recorded under whatever the last action had said. Kept at
        // once it is said when the run of changes begins, not at every number of it: every
        // assignment is a message, and the strip would fill with one line.
        string label = L($"Edited {tile.Name}.");
        if (!KeepsAtOnce || Session.Status != label)
            Session.Status = label;

        // Whose changes these are, for the history: a run of them is one step.
        if (KeepsAtOnce)
            Session.EditStreak(tile);

        Session.MarkEdited();

        // An item on the shared stash's pages is that file's: marked there too, or the edit shows
        // on its tile and is never saved.
        if (_stash?.Holds(tile.Item) == true)
            _stash.MarkEdited();

        // A craft is the editor's making: an item made, as its copy was (ItemMade - the duplicate marks look again).
        if (crafted)
        {
            if (_stash?.Holds(tile.Item) == true)
                _stash.Made(tile.Item);
            else if (Session is CharacterSession character)
                character.Made(tile.Item);
        }
    }

    /// <summary>
    /// Everything staged written into the item, in the order that matters. Answers whether a craft
    /// was made in place. KEEP's writing, apart from what follows it - the strip, the host, the
    /// rebuild - so a host that keeps at once (<see cref="KeepAtOnce"/>) writes the same way.
    /// </summary>
    private bool Write(ItemTileViewModel tile)
    {
        // An edit makes a legacy item this season's, checked from now on: kept as the season
        // before made it only while nothing of it is changed. First, so what the edit writes takes this season's
        // fields. A corruption alone is the game's own and leaves it as it was, Season 13's one bit and all (OnlyACorruption).
        if (!OnlyACorruption(tile))
            PD2SPCompanion.Core.Saves.ItemSeason.MakeCurrent(tile.Item);

        foreach (var row in Stats)
            row.Apply();

        // The affixes the picker settled on, written with everything else rather than when
        // the panel closed.
        Affixes.Commit(tile.Item);

        Corruption.Commit(tile.Item);

        Desecration.Commit(tile.Item);

        // A craft made in place: what it changed besides the properties, which the rows write below.
        bool crafted = Crafting.Commit(tile.Item);

        // The base before the defence and durability below, which were staged against it.
        if (Draft.Code is { } upgraded)
            tile.Item.Code = upgraded.PadRight(4, ' ');

        // A normal item made superior, or a Forge runeword's base made normal again: the quality is the
        // whole of it, the bonus being rows.
        if (Draft.Quality is { } quality)
        {
            tile.Item.Quality = quality;
            Draft.Quality = null;
        }

        // The automagic row chosen on its line: its id, the properties being rows.
        if (Draft.AutoAffix is { } automagic)
        {
            tile.Item.IsAutoAffix = automagic > 0;
            tile.Item.AutoAffixId = (ushort)automagic;
            Draft.AutoAffix = null;
        }

        RareName.Commit(tile.Item);

        Draft.Code = null;

        // Anything picked from the list goes on now, with whatever was typed into it
        // already written by its row's Apply above.
        if (_added.Count > 0)
        {
            var list = tile.Item.StatLists.FirstOrDefault();
            if (list is null)
            {
                list = new ItemStatList();
                tile.Item.StatLists.Add(list);
            }

            list.Stats.AddRange(_added);
            _added.Clear();
        }

        // Removals last: taking a stat out while still walking the list is the sort of
        // thing that works until the day it does not.
        foreach (var row in Stats.Where(row => row.IsDeleted).ToList())
            ItemStats.Remove(tile.Item, row.Stat);

        // The Basic boxes after every property is on or off: a durability is held to the maximum the
        // item's own bonuses give it, and a superior bonus picked is among the properties just added.
        Basic.Commit(tile.Item);

        Sockets.Commit(tile);

        // The runeword staged, after the sockets: the same list the pending rows have just
        // written their values into goes onto the item.
        Runeword.Commit(tile.Item);

        // The elemental skill words after their grants: an element chosen, a number moved, a row struck
        // (ElementSkills). Not in the raw list, which writes what it is given.
        if (!Rules.ListsRawProperties)
            ElementSkills.Settle(tile.Item);

        // And the lists as the game writes them, whatever order the rows were added in.
        ItemStats.InIdOrder(tile.Item);

        return crafted;
    }

    // ---- keeping at once (the Forge) ---------------------------------------------------------

    /// <summary>
    /// Whether the host keeps every change as it is made (<see cref="IItemEditHost.KeepsAtOnce"/>): the
    /// Forge, whose item is in no file. Nothing waits on KEEP there, so nothing reads as pending.
    /// </summary>
    public bool KeepsAtOnce => Session.KeepsAtOnce;

    /// <summary>Whether the buttons that make the item another kind of item are offered: not where the host's page chose the kind.</summary>
    public bool Transforms => !Session.ChoosesItsItem;

    private bool _keeping;

    /// <summary>A picker over the panel: what is chosen in one is staged until it closes.</summary>
    private bool AnyPickerOpen =>
        OpenPicker is not null || StatPicker.IsOpen || RareName.IsPickerOpen || Affixes.IsAffixPickerOpen
        || Corruption.IsPickerOpen || Desecration.IsPickerOpen || Crafting.IsPickerOpen || Runeword.IsPickerOpen
        || Superior.IsPickerOpen || Automagic.IsPickerOpen || StaffSkills.IsPickerOpen || MapRecipe.IsPickerOpen;

    /// <summary>
    /// What is staged written to the item now, for a host that keeps at once, and the page told of the item
    /// as it then is. Asked at the head of every refresh of the page, and when a picker closes; answers
    /// whether it kept anything - the refresh that asked has then been done.
    /// <para>
    /// <b>Not while a picker is open</b> - a rebuild closes them all, and several properties are added from
    /// one opening - and <b>not while a value will not parse or is out of range</b>: the item stays at the
    /// last number that held, and the row says why.
    /// </para>
    /// <para>
    /// <b>A number typed or stepped is written without rebuilding the rows</b>: they are told they hold
    /// what the item holds (<see cref="StatRowViewModel.Kept"/>). A rebuild puts new rows under the
    /// pointer and the caret, and a box being typed in would lose both between two digits. Everything
    /// else - a property added or removed, affixes, a corruption, sockets, a runeword, an upgrade,
    /// ethereal - changes what rows there are, and is rebuilt as KEEP rebuilds.
    /// </para>
    /// </summary>
    private bool KeepAtOnce()
    {
        if (!KeepsAtOnce || _keeping || _building || !HasChanges || AnyPickerOpen || !CanApply)
            return false;
        if (Session.SelectedItem is not { } tile)
            return false;

        // A count of the materials tab: the count is the whole of it, and it is the shared stash's (as KEEP's).
        if (tile.TabSlot is { } tabSlot)
        {
            if (!Basic.QuantityIsChanged || _stash is null)
                return false;

            _keeping = true;
            try
            {
                _stash.SetMaterial(tabSlot, Math.Clamp(Basic.QuantityTyped, Basic.QuantityMinimum, Basic.QuantityMaximum));
            }
            finally
            {
                _keeping = false;
            }

            return true;
        }

        _keeping = true;
        try
        {
            bool rebuild = _added.Count > 0 || Stats.Any(row => row.IsDeleted)
                           || Affixes.AnyStaged || Corruption.IsStaged || Desecration.IsStaged
                           || Draft.Code is not null || Draft.Runeword is not null || Draft.CraftFrom is not null
                           || Draft.Quality is not null || Draft.AutoAffix is not null || Sockets.AreChanged || RareName.IsChanged || Basic.EtherealIsChanged;

            // A property just picked from the list, to bring into sight once its row is built again - in the
            // place the game's order gives it, which is seldom the end of the list it was picked onto. Not
            // what a corruption, a bonus or a reroll adds: the page stays where that was done.
            var picked = _picked is not null && _added.Contains(_picked) ? _picked : null;
            _picked = null;

            bool crafted = Write(tile);
            Wrote(tile, crafted);

            if (!rebuild)
            {
                foreach (var row in Stats)
                    row.Kept();

                // Something the rows and the boxes do not let go of by themselves: back to what the item says.
                rebuild = HasChanges;
            }

            // Before the page is told: what is drawn off the item - the holder - is drawn again first.
            // The stash's tiles too: the character's redraw does not reach them.
            Session.SelectedItemChanged();
            _stash?.Redraw(tile.Item);

            // A rebuild ends on a refresh of its own; without one, the refresh is this.
            if (rebuild)
            {
                BuildStats();
                Sockets.Build();

                if (picked is not null && Stats.ToList().FindIndex(row => ReferenceEquals(row.Stat, picked)) is >= 0 and var at)
                    ShowStatRow?.Invoke(at);
            }
            else
            {
                NotifyChanges();
            }
        }
        finally
        {
            _keeping = false;
        }

        return true;
    }

    /// <summary>A picker closed on a host that keeps at once: what it staged is kept now.</summary>
    private void KeepWhenClosed(object? sender, System.ComponentModel.PropertyChangedEventArgs changed)
    {
        if (changed.PropertyName is nameof(IsPickerOpen) or nameof(AffixEditor.IsAffixPickerOpen) or nameof(PropertyPickerViewModel.IsOpen))
            Dispatcher.UIThread.Post(() => KeepAtOnce());
    }

    /// <summary>
    /// Called when the item or the page is about to change. Anything unsaved goes: it was
    /// never in the item, so there is nothing to undo, only something to stop showing.
    /// </summary>
    public void DiscardStats()
    {
        if (HasChanges)
            BuildStats();
    }

    /// <summary>
    /// Builds the rows with the rows' own change notices held: marking a row's group raised thirteen
    /// notices a row, and each ran the whole page's refresh - the legitimacy check and every editor
    /// copying the item - which was 500-600 ms of a click on an item (measured). The build
    /// ends with one refresh of its own.
    /// </summary>
    private void BuildStats()
    {
        _building = true;
        try
        {
            BuildRows();
        }
        finally
        {
            _building = false;
        }
    }

    private bool _building;

    /// <summary>
    /// A row changed: the page's refresh runs once for the burst, at the end of what is being done
    /// now, rather than once per notice - typing one digit raises several.
    /// </summary>
    private void RowChanged()
    {
        if (_building || _refreshQueued)
            return;

        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            NotifyChanges();
        });
    }

    private bool _refreshQueued;

    private void BuildRows()
    {
        // Every panel that opens over this page belongs to the item that was showing when
        // it opened. A rebuild means that item has changed or gone, so they go with it -
        // a picker left standing would write to something nobody is looking at.
        OpenPicker = null;
        RareName.IsPickerOpen = false;
        StatPicker.IsOpen = false;
        Corruption.Reset();
        Desecration.Reset();
        Affixes.Reset();
        Crafting.Reset();
        Superior.Reset();
        Automagic.Reset();
        StaffSkills.Reset();
        MapRecipe.Reset();
        Runeword.Reset();

        // The slots the affix panel had opened or marked belong to that item too.

        // The rows about to be built read the play mode as it is - see FollowPlayMode.
        _followed = (Rules.EditsPropertiesFreely, Rules.Policy.Details);

        // What the slots show is read from the staged ids, so they are rebuilt where the
        // ids are dropped - UNDO cleared them and left the button still naming them.
        Draft.Clear();

        // What links the rows and what their dice draw, asked again of the item as it is now: every change is kept
        // into the same item, and asked once per item a corruption gained or changed while it was shown left the
        // link button as the item was when it was picked - missing on a new all res, still there on one taken
        // away. Within one build each is asked once.
        _fellows = null;
        _drawn = null;

        // Sockets are staged too, and this is the one place every discard goes through -
        // UNDO and leaving the page both rebuild from here. Left out, a staged socket
        // count survived its own UNDO, went on reading as a change, and would have been
        // written by the next KEEP.
        Sockets.Reset();

        Stats.Clear();

        // A rebuild is the page going back to what the item says, so anything picked and
        // not kept goes with it.
        _added.Clear();

        // The fields, not the properties, from here to the graphic, on purpose (MVVMTK0034): this is
        // the reset to what the item says, in an order that matters, and the change handlers would
        // run NotifyChanges and the affix limits against the item that has just gone. Each value is
        // announced by hand right after it is set. Anywhere else, set the property.
#pragma warning disable MVVMTK0034
        // Set before the rows, since its own change notification runs through NotifyChanges
        // and would otherwise read the stack against the item that has just gone.
        RareName.ShowStored();

        // The bounds before the numbers, and this order is the whole of it.
        //
        // Every one of these boxes refuses a value outside the Minimum and Maximum it is
        // bound to, and refusing means writing the clamped number back. Sent the new item's
        // durability while the box still held the old item's ceiling, a spear of 56 landed
        // on a body armour's 32 and wrote 32 back - and the page then showed 32 of 32,
        // marked pending, on an item nobody had touched.
        Basic.ShowStored();
#pragma warning restore MVVMTK0034

        if (Session.SelectedItem is { } tile)
        {
            // Asked apart, because the file keeps them apart: what the item rolled for
            // itself, and what a runeword or a set added. Added together, a staff's own
            // +2 Fire Bolt and a runeword's +3 made a range of 4-6 that either line could
            // sit anywhere inside - so a staffmod of +6 passed for a number the game rolls.
            var rolls = AffixRanges.OwnOf(tile.Item);
            var granted = AffixRanges.AddedTo(tile.Item);

            // The game's bracket for the same item, worked out once for the whole list.
            var shown = AffixRanges.DisplayedFor(tile.Item);

            int poisonLength = tile.Item.StatLists
                .SelectMany(list => list.Stats)
                .FirstOrDefault(stat => stat.Stat == "poisonlength")?.Value ?? 0;

            // The two the corruption writes for its own bookkeeping are not properties:
            // one names which corruption it was and the other is the flag that paints it
            // red. Neither is anything to read or to set, and the page says both in its
            // own words. Left on the item, simply not listed.
            // Which list a property is in is the game's own answer to where it came from:
            // an item's own properties are in the first and a runeword's in the one after.
            // What is in the item's own list is the item's; what is in the lists after it was
            // added - by a runeword, or by the rest of a set. The game keeps them apart and
            // this is that division, drawn.
            var word = Runewords.On(tile.Item);
            bool isSet = tile.Item.Quality is ItemQuality.Set;
            bool started = false;

            // An elemental skill bonus is one row, "+1 to Magic Skills" with its element, as the game shows one
            // line: the words are a second stat that follows it (ElementSkills), settled on KEEP. The raw list
            // shows every stat as the file has it, and settles nothing.
            bool raw = Rules.ListsRawProperties;

            OnePass(() =>
            {
            for (int which = 0; which < tile.Item.StatLists.Count; which++)
            {
                var list = tile.Item.StatLists[which];

                foreach (var stat in list.Stats
                             .Where(stat => stat.Stat is not (Corruptions.IdStat or Corruptions.FlagStat
                                                              or Desecrations.ValueStat or Desecrations.FlagStat))
                             .Where(stat => raw || !ElementSkills.IsLabel(stat.Stat)))
                {
                    bool added = which > 0 && (word is not null || isSet);

                    var row = Row(
                        tile, stat, added: false, poisonLength: poisonLength,
                        rolls: added ? granted : rolls,
                        alongside: list.Stats,
                        painted: shown);

                    bool fromSet = added && word is null;

                    row.MarkGroup(
                        runeword: added && word is not null,
                        set: fromSet,
                        heading: added && !started
                            ? (word is null ? null : Runewords.GameName(word)) ?? SetNames.Of(tile.Item) ?? L("Set bonus")
                            : "",
                        pieces: fromSet
                            ? AffixRanges.SetPiecesFor(tile.Item, which, stat.Stat ?? "") ?? 0
                            : 0);

                    started |= added;

                    Stats.Add(row);
                }
            }
            });
        }

        // The whole affix side, not only the pairs: the rare name row is built here
        // too, and UNDO left the button showing the name it had just put back. Last,
        // because what it reads - the item level above all - is reset above.
        Affixes.Build();
        Affixes.Notify();

        NotifyChanges();
    }

    /// <summary>
    /// A row struck or restored: a run's other entries go, or come back, with it - the file cannot hold a run in
    /// part (ItemStats). Within the list the row is in, and not while the page itself sets the rows.
    /// <para>
    /// Enhanced Damage's other half and a flat damage's copies, which move with its number, stay: full control
    /// is better than enforcement on our side, so each row is its own,
    /// and its group takes its number when asked (<see cref="SyncFellows"/>).
    /// </para>
    /// </summary>
    private void GoTogether(StatRowViewModel struck)
    {
        if (_building || _settingRows || struck.Stat.Stat is null)
            return;

        if (struck.Stat.Id is not { } id || !ItemStats.IsInRun(id) || ItemStats.RunContaining(id) is not { } run)
            return;

        _settingRows = true;
        try
        {
            foreach (var row in Stats)
            {
                if (ReferenceEquals(row, struck) || row.IsDeleted == struck.IsDeleted
                    || row.IsRuneword != struck.IsRuneword || row.IsSetBonus != struck.IsSetBonus)
                    continue;

                if (row.Stat.Id is { } other && run.Contains(other))
                    row.IsDeleted = struck.IsDeleted;
            }
        }
        finally
        {
            _settingRows = false;
        }

        OnPropertyChanged(nameof(StatsHeader));
    }

    /// <summary>
    /// A row's group takes its number, asked for by the row's link button or Alt + Enter in its box
    /// (StatRowViewModel.AskFellows): the button propagates the current box's value to the other synced boxes.
    /// Nothing moves another by itself:
    /// every row is edited on its own, on all occasions.
    /// <para>
    /// Its group is either kind of rows written together: those <b>never apart</b> (AffixRanges.NeverApart) -
    /// Enhanced Damage's maximum and minimum, a flat damage's one-hand, two-hand and thrown numbers, a map's paired
    /// lines - which followed each other; and those <b>one property rolled together</b>
    /// (AffixRanges.RolledTogether) - all four resistances, the four attributes - which moved by as much. Both
    /// were taken out after use: a corruption's resistance on top of all four is common, and an Enhanced Damage
    /// whose halves an affix or corruption taken off had left apart could not be kept again.
    /// </para>
    /// <para>
    /// Whatever else feeds the rows: asked, the number wanted is the one given. Within the row's own list - a
    /// runeword's Enhanced Damage is not the base's - each held to its own limits.
    /// </para>
    /// </summary>
    private void SyncFellows(StatRowViewModel asked)
    {
        if (Session.SelectedItem is not { } tile || asked.IsDeleted || asked.Number is not { } number
            || asked.Stat.Stat is not { Length: > 0 } name)
            return;

        var alike = AffixRanges.NeverApart().FirstOrDefault(group => group.Contains(name));
        string key = AffixRanges.KeyOf(asked.Stat);
        var rolled = AffixRanges.RolledTogether(Staged(tile.Item)).Where(group => group.Contains(key)).ToList();
        if (alike is null && rolled.Count == 0)
            return;

        int set = 0;
        foreach (var row in Stats)
        {
            if (ReferenceEquals(row, asked) || row.IsDeleted || !row.CanEdit
                || row.IsRuneword != asked.IsRuneword || row.IsSetBonus != asked.IsSetBonus)
                continue;

            if ((alike is not null && alike.Contains(row.Stat.Stat ?? ""))
                || rolled.Any(group => group.Contains(AffixRanges.KeyOf(row.Stat))))
            {
                row.MoveTo(number);
                set++;
            }
        }

        if (set > 0)
            Say(Plural(set, $"{asked.Name}: {number} on {set} more row.", $"{asked.Name}: {number} on {set} more rows."));
    }

    /// <summary>
    /// The stats and keys of the item that are written with others - never apart (by name) or rolled together by one
    /// property (by key) - for the rows to show their link button: asked once per build of the rows (BuildRows
    /// forgets it), since a change kept into the item can add or take a source.
    /// </summary>
    private IReadOnlySet<string> Fellows(Item item)
    {
        if (_fellows is { } kept && ReferenceEquals(kept.Item, item))
            return kept.Keys;

        var keys = new HashSet<string>(AffixRanges.RolledTogether(item).SelectMany(group => group), StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(AffixRanges.NeverApart().SelectMany(group => group));
        _fellows = (item, keys);
        return keys;
    }

    private (Item Item, IReadOnlySet<string> Keys)? _fellows;

    /// <summary>The page is setting the rows' numbers itself; no row moves another meanwhile.</summary>
    private bool _settingRows;

    /// <summary>
    /// What the item's sources draw at random - a class, a skill (AffixRanges.DrawnFor) - asked
    /// once per build of the rows rather than once per row (BuildRows forgets it).
    /// </summary>
    private AffixRanges.DrawnSubjects Drawn(Item item)
    {
        if (_drawn is { } kept && ReferenceEquals(kept.Item, item))
            return kept.Subjects;

        var drawn = AffixRanges.DrawnFor(item);
        _drawn = (item, drawn);
        return drawn;
    }

    private (Item Item, AffixRanges.DrawnSubjects Subjects)? _drawn;

    /// <summary>
    /// One property's row, wired to the page. Built here rather than inline so a property
    /// picked from the list arrives the same way one read from the item does.
    /// </summary>
    private StatRowViewModel Row(
        ItemTileViewModel tile,
        ItemStat stat,
        bool added,
        int? poisonLength = null,
        IReadOnlyDictionary<string, RollRange>? rolls = null,
        IEnumerable<ItemStat>? alongside = null,
        IReadOnlyDictionary<string, RollRange>? painted = null)
    {
        // A property picked by hand goes to the item's own list, and answers to its sources alone.
        rolls ??= AffixRanges.OwnOf(tile.Item);

        // By the skill it names as well as by its own name, where it names one: three
        // +skill lines on a runeword staff are three properties, not one counted thrice.
        if (!rolls.TryGetValue(AffixRanges.KeyOf(stat), out var roll))
            rolls.TryGetValue(stat.Stat ?? "", out roll);

        // The game's own bracket beside it, which is wider and is not a limit. It costs a
        // sweep of both affix tables, so a caller listing an item's properties works it out
        // once and hands it down rather than letting every row ask again.
        painted ??= AffixRanges.DisplayedFor(tile.Item);

        if (!painted.TryGetValue(AffixRanges.KeyOf(stat), out var bracket))
            painted.TryGetValue(stat.Stat ?? "", out bracket);

        // Poison damage is stored per frame and means nothing without the length stored
        // beside it, which is a different stat in the same run.
        poisonLength ??= tile.Item.StatLists
            .SelectMany(list => list.Stats)
            .FirstOrDefault(each => each.Stat == "poisonlength")?.Value ?? 0;

        // What the item's other lines for this same property hold, since the game adds
        // them and the range is the sum of what granted them.
        // Its fellows are the ones granted by the same source, which is to say the ones in
        // the same property list. A runeword's Fire Bolt is not another line of the staff's.
        string key = AffixRanges.KeyOf(stat);

        int shared = (alongside ?? ItemStats.Every(tile.Item))
            .Where(each => !ReferenceEquals(each, stat) && AffixRanges.KeyOf(each) == key)
            .Sum(each => each.Value);

        var row = new StatRowViewModel(
            stat, roll, staged: true,
            staffClass: StaffMods.ClassOf(tile.Item),
            staffSkills: StaffMods.SkillsFor(tile.Item),
            itemLevel: tile.Item.ItemLevel,
            poisonLength: poisonLength.Value,
            alongside: shared,
            shown: bracket,
            classDrawn: Drawn(tile.Item).Classes.Contains(stat.Stat ?? ""),
            skillDrawn: Drawn(tile.Item).Skills.GetValueOrDefault(stat.Stat ?? ""))
        {
            IsAdded = added,
            DrawnTogether = stat.Stat is "item_singleskill" or "item_skilloncast" && AffixRanges.RandomSkillCastsOn(tile.Item).Any()
                ? () => DrawSkillCast(tile.Item, new Random())
                : null,
        };

        // Asked of the page rather than the file. A corruption confirmed but not yet kept
        // is still what wrote the property, and reading the item left its new properties
        // looking ordinary until KEEP.
        var (corrupted, desecrated) = CorruptedStats;
        row.MarkCorrupted(corrupted.Contains(stat.Stat ?? ""), desecrated.Contains(stat.Stat ?? ""));

        row.HasFellows = Fellows(tile.Item).Contains(key) || Fellows(tile.Item).Contains(stat.Stat ?? "");
        row.FellowsAsked += SyncFellows;

        row.PropertyChanged += (sender, changed) =>
        {
            // Struck or restored: the rows written with it follow.
            if (changed.PropertyName == nameof(StatRowViewModel.IsDeleted) && sender is StatRowViewModel struck)
                GoTogether(struck);

            // One list at a time, and the page holds whose it is.
            if (changed.PropertyName == nameof(StatRowViewModel.PickerOpen)
                && sender is StatRowViewModel asked)
            {
                if (asked.PickerOpen)
                {
                    foreach (var other in Stats)
                    {
                        if (!ReferenceEquals(other, asked))
                            other.PickerOpen = false;
                    }

                    OpenPicker = asked;
                }
                else if (ReferenceEquals(OpenPicker, asked))
                {
                    OpenPicker = null;
                }
            }

            RowChanged();
        };

        return row;
    }

    private void NotifyChanges()
    {
        // A host that keeps at once: written first, and the page told once, of the item as it now is.
        if (KeepAtOnce())
            return;

        bool outer = !_notifying;
        _notifying = true;
        try
        {
            OnePass(TellChanges);
        }
        finally
        {
            if (outer)
                _notifying = false;
        }
    }

    private void TellChanges()
    {
        BuildFindings();

        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasBadValue));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ChangeNote));
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(CanAddStat));
        OnPropertyChanged(nameof(CanCarryStats));
        OnPropertyChanged(nameof(NoStats));
        OnPropertyChanged(nameof(NoStatsNote));
        OnPropertyChanged(nameof(IsSocketFiller));
        Corruption.Notify();
        Affixes.Notify();
        Desecration.Notify();
        OnPropertyChanged(nameof(CanReroll));
        OnPropertyChanged(nameof(ShowsCube));
        OnPropertyChanged(nameof(CanRollNow));
        OnPropertyChanged(nameof(ShowsOrb));
        OnPropertyChanged(nameof(RerollHint));
        OnPropertyChanged(nameof(CanUpgradeBase));
        OnPropertyChanged(nameof(UpgradeHint));
        OnPropertyChanged(nameof(WeaponEffect));
        OnPropertyChanged(nameof(ArmorEffect));
        OnPropertyChanged(nameof(ShieldEffect));
        OnPropertyChanged(nameof(HasWeaponEffect));
        OnPropertyChanged(nameof(HasArmorEffect));
        OnPropertyChanged(nameof(HasShieldEffect));
        OnPropertyChanged(nameof(StatsHeader));
        Basic.Notify();
        Crafting.Notify();
        Superior.Notify();
        Automagic.Notify();
        StaffSkills.Notify();
        MapRecipe.Notify();
        Runeword.Notify();

        ShowSummary();
    }

    /// <summary>
    /// What the boxes will and will not take. Raised on its own, and before any value is
    /// put into them, because a box holds its bounds harder than its content: told a number
    /// outside them it keeps the bound instead and writes that back as if it were typed.
    /// </summary>
    [RelayCommand]
    private void ToggleStats()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        StatsOpen = !StatsOpen;
    }

    private void Refresh()
    {
        BuildStats();
        Sockets.Build();
        Affixes.Build();

        OnPropertyChanged(nameof(HasItem));
        Sockets.Notify();
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(CanAddStat));
        OnPropertyChanged(nameof(CanCarryStats));
        OnPropertyChanged(nameof(NoStats));
        OnPropertyChanged(nameof(NoStatsNote));
        OnPropertyChanged(nameof(StatsHeader));
    }

    /// <summary>The lists a picker can be asked to scroll, by the name the view gives them.</summary>
    public const string AffixList = "AffixList";

    public const string CorruptionList = "CorruptionList";

    public const string DesecrationList = "DesecrationList";

    public const string CraftList = "CraftList";

    public const string RareFirstList = "RareFirstList";

    public const string RareSecondList = "RareSecondList";

    /// <summary>
    /// Whether this item may be edited outside the rules: properties added, properties
    /// taken off, numbers set past what their source could roll. Only Free mode says yes.
    /// </summary>
    public bool IsFreeEditing => StatRowViewModel.FreeEditing;

    /// <summary>
    /// What this panel's rows were last told of the play mode. Their answers read statics the settings have already
    /// written by the time anyone hears of the change (EditorSettings.Apply), and there is a panel each for the
    /// character, the vault, the Forge and a jewel's layer, so a question of the statics finds nothing to tell.
    /// </summary>
    private (bool Free, Reach Details) _followed;

    /// <summary>
    /// Free editing is the play mode's to decide - Free edits freely, every other mode holds an
    /// item to its sources. There is no lock that lets Free mode close itself again:
    /// the tiered policy already says it, once, for every page.
    /// Told again when the mode changes, so an item already open follows it.
    /// </summary>
    private void FollowPlayMode()
    {
        var now = (Rules.EditsPropertiesFreely, Rules.Policy.Details);
        if (_followed == now)
            return;

        _followed = now;

        OnPropertyChanged(nameof(IsFreeEditing));
        OnPropertyChanged(nameof(CanAddStat));
        OnPropertyChanged(nameof(NoStatsNote));

        // Every row answers the question differently now, and the rows are what hold
        // the limits - so they are all told, rather than rebuilt and losing edits.
        foreach (var row in Stats)
            row.LimitsChanged();

        NotifyChanges();
    }

    /// <summary>
    /// Asks the view to bring a property's row into sight, by its place in <see cref="Stats"/>: one just
    /// picked, which is the item's at once and whose number is the next thing set.
    /// </summary>
    public event Action<int>? ShowStatRow;

    /// <summary>The property last picked from the property picker and not yet kept (AddStat).</summary>
    private ItemStat? _picked;

    /// <summary>
    /// Asks the view to bring the marked row of a named list into sight. The panel knows
    /// which row that is - it is the one it marks - but only the view can scroll to it,
    /// and only after the rows it just rebuilt have been laid out.
    /// </summary>
    public event Action<string>? ShowHeldIn;

    private void Reveal(params string[] lists)
    {
        foreach (string list in lists)
            ShowHeldIn?.Invoke(list);
    }

    /// <summary>
    /// A copy of the item wearing the staged picks, for asking questions of. The real item
    /// is left alone until the panel is confirmed.
    /// </summary>
    /// <param name="withPicks">
    /// Whether the picks the panel is holding count. False when the answer is about to be
    /// handed to <see cref="MagicAffixes.Apply"/>, which applies them itself and needs the
    /// item as it was to know what the change is taking away - given the picks twice, it
    /// read the new affix as the old one and took nothing off.
    /// </param>
    private Item Staged(Item item, bool withPicks = true)
    {
        if (_onePass == 0)
            return StagedAnew(item, withPicks);

        if (!_stagedOnce.TryGetValue((item, withPicks), out var once))
            _stagedOnce[(item, withPicks)] = once = StagedAnew(item, withPicks);
        return once;
    }

    /// <summary>
    /// One pass of the page's questions - the rows built, or the page told what changed: nothing changes while it
    /// runs, so the staged copy and the pending item are made once and handed to every question of it. Each question
    /// made its own - write and read the item - some thirty for a select or a digit typed. Only for questions: what writes to a copy (Pending, the commands) makes its own.
    /// </summary>
    private void OnePass(Action pass)
    {
        _onePass++;
        try
        {
            pass();
        }
        finally
        {
            if (--_onePass == 0)
            {
                _stagedOnce.Clear();
                _pendingOnce.Clear();
            }
        }
    }

    private int _onePass;
    private readonly Dictionary<(Item, bool), Item> _stagedOnce = new(new StagedKey());
    private readonly Dictionary<bool, Item?> _pendingOnce = [];

    /// <summary>The item by reference, as the staged copy is of that very item.</summary>
    private sealed class StagedKey : IEqualityComparer<(Item, bool)>
    {
        public bool Equals((Item, bool) a, (Item, bool) b) => ReferenceEquals(a.Item1, b.Item1) && a.Item2 == b.Item2;
        public int GetHashCode((Item, bool) key) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Item1), key.Item2);
    }

    private Item StagedAnew(Item item, bool withPicks)
    {
        // No copy, no staging: the item itself is handed back untouched rather than
        // written to. A list one pick out of date beats an item changed behind the panel.
        if (Session.Copy(item) is not { } shadow)
            return item;

        // Typed rather than stored: raising the level and looking at the list without
        // keeping anything first is exactly how someone finds out what a level buys them.
        if (Basic.ItemLevelTyped is > 0 and <= byte.MaxValue)
            shadow.ItemLevel = (byte)Basic.ItemLevelTyped;

        shadow.TotalNumberOfSockets = (byte)Sockets.Staged;
        shadow.IsSocketed = Sockets.Staged > 0;

        if (Draft.Code is { } code)
            shadow.Code = code.PadRight(4, ' ');

        if (Draft.Quality is { } quality)
            shadow.Quality = quality;

        if (Draft.AutoAffix is { } automagic)
        {
            shadow.IsAutoAffix = automagic > 0;
            shadow.AutoAffixId = (ushort)automagic;
        }

        if (Draft.Corruption is { } corruption)
            CorruptionEditor.Write(shadow, corruption);

        if (Draft.Desecration is { } desecration)
            DesecrationEditor.Write(shadow, desecration);

        Runeword.Wear(shadow);

        // The mod is not in the file, so a copy does not carry it: told again, from what was
        // staged, or from what the item itself was told.
        Desecrations.Assume(shadow, Draft.Desecration is null ? Desecrations.Assumed(item) : Draft.DesecrationMod);

        // And a crafted item's recipe, where a reroll pinned it - see AffixRanges.PinRecipe.
        AffixRanges.CarryPinnedRecipe(item, shadow);

        foreach (var ((slot, prefix), id) in Draft.AffixIds)
        {
            var settled = prefix ? shadow.MagicPrefixIds : shadow.MagicSuffixIds;
            if (settled is not null && slot >= 0 && slot < settled.Length)
                settled[slot] = (ushort)id;
        }

        if (withPicks)
            Affixes.WearPicks(shadow);

        return shadow;
    }

    /// <summary>
    /// The item as the page has it: a detached copy wearing the affixes settled on and the
    /// property values typed into the rows. What KEEP would write, in other words, and the
    /// only honest thing to work the next change out from.
    /// </summary>
    /// <param name="whole">
    /// With what a row stages besides its number - a skill, a class, a tree - worn as well: for the item's
    /// summary (<see cref="Preview"/>). Without, as every question asked of the pending item has had it.
    /// </param>
    private Item? Pending(bool whole = false)
    {
        if (_onePass == 0 || !_notifying)
            return PendingAnew(whole);

        if (!_pendingOnce.TryGetValue(whole, out var once))
            _pendingOnce[whole] = once = PendingAnew(whole);
        return once;
    }

    /// <summary>Whether the pass is the page told what changed - the rows stand - and not the rows being built.</summary>
    private bool _notifying;

    private Item? PendingAnew(bool whole)
    {
        if (Session.SelectedItem is not { } tile)
            return null;

        var shadow = StagedAnew(tile.Item, withPicks: false);

        // Staged hands back the item itself when it cannot copy it, and writing to that
        // would be the very thing all of this is arranged to avoid.
        if (ReferenceEquals(shadow, tile.Item))
            return null;

        if (shadow.StatLists.FirstOrDefault() is not { } list)
            return shadow;

        var claimed = new HashSet<ItemStat>(ReferenceEqualityComparer.Instance);
        foreach (var row in Stats)
        {
            if (row.Stat.Stat is not { Length: > 0 } name)
                continue;

            // Into the list the row is drawn from (BuildStats): the item's own properties are
            // its first list, a runeword's the one after. By name alone, a runeword's +33% Faster
            // Cast Rate was written over a staff's own +50% and the pending item read as
            // unrollable (found building Memory on a Stalagmite). A set's bonuses are
            // read-only and left as the copy has them.
            var target = row.IsRuneword
                ? shadow.StatLists.Count > 1 ? shadow.StatLists[^1] : null
                : row.IsSetBonus ? null : list;

            // The word being replaced or cleaned: the shadow wears the change already, and its
            // struck rows would otherwise be taken off the new word's list.
            if (target is null || (row.IsRuneword && !row.IsAdded && Draft.Runeword is not null))
                continue;

            // The same line, not only the same stat: a runeword's two "+2 to one skill" lines are
            // one stat twice, told apart by the skill, and a Hitpower helm's two chances to cast when
            // struck by a skill kept beside the parameter (ItemStats.SameLine). Each line is one row's.
            var held = target.Stats.FirstOrDefault(each => !claimed.Contains(each) && ItemStats.SameLine(each, row.Stat));
            if (held is not null)
                claimed.Add(held);

            if (row.IsDeleted)
            {
                if (held is not null)
                    target.Stats.Remove(held);

                continue;
            }

            if (row.Number is not { } typed)
                continue;

            if (held is null)
            {
                // A line staged and not on the item yet - an affix's just picked, a craft's: the whole of it, not its
                // number alone. With the name and the number only, "of Amplify Damage"'s chance to cast came into the
                // next change's copy as skill 0 at level 1, was staged again as a line of its own beside its row, and
                // so once more at every change while nothing was kept - five "12% Chance to cast level 1 (none) on
                // striking" on a Glyph Scratch.
                held = new ItemStat
                {
                    Id = row.Stat.Id,
                    Stat = name,
                    Param = row.Stat.Param,
                    SkillId = row.Stat.SkillId,
                    SkillLevel = row.Stat.SkillLevel,
                    MaxCharges = row.Stat.MaxCharges,
                    SkillTab = row.Stat.SkillTab,
                    CharacterClass = row.Stat.CharacterClass,
                    Value = typed,
                };
                target.Stats.Add(held);
            }
            else
            {
                held.Value = typed;
            }

            if (whole)
                row.Wear(held);
        }

        // As KEEP will write it: the elemental skill words after their grants (see Write).
        if (!Rules.ListsRawProperties)
            ElementSkills.Settle(shadow);

        return shadow;
    }

    // ---- the item's summary ---------------------------------------------------------------------

    /// <summary>
    /// The item as KEEP would leave it, on a copy: <see cref="Pending"/>, and what that leaves to KEEP -
    /// a craft made in place, the Basic section's boxes, the rare name, gems on their way out. What the
    /// summary over the page is drawn from while anything is staged.
    /// </summary>
    private Item? Preview()
    {
        if (Session.SelectedItem is not { } tile || Pending(whole: true) is not { } shadow)
            return null;

        Crafting.Wear(shadow);
        Basic.Commit(shadow);
        RareName.Commit(shadow);
        Sockets.Wear(shadow, tile.Item);
        return shadow;
    }

    /// <summary>
    /// The item summed up as the page has it (a dynamic summary, not what was
    /// kept): the selected item's own tile while nothing is staged, else a tile over <see cref="Preview"/>,
    /// made again at every change. On a host that keeps at once too: what waits there is a picker's change,
    /// written when it closes, and the pickers stay open after a pick - the holder
    /// drawn from the written item showed nothing of it until then.
    /// </summary>
    [ObservableProperty] private ItemTileViewModel? _summary;

    private void ShowSummary()
    {
        var tile = Session.SelectedItem;
        if (tile is null || tile.TabSlot is not null || !HasChanges)
        {
            Summary = tile;
            return;
        }

        try
        {
            Summary = Preview() is { } staged ? new ItemTileViewModel(staged, 0) : tile;
        }
        catch (Exception problem)
        {
            // A summary that cannot be drawn is the item as it is: the page still edits it.
            Log.Caught(problem);
            Summary = tile;
        }
    }

    public bool HasItem => Session.SelectedItem is not null;
}
