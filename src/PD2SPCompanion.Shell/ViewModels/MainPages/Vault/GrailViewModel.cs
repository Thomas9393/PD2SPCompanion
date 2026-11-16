using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using D2SLib.Model.Save;

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
/// One entry on the grail: a unique or a set piece, made from its table row as the game would make it, and shown as
/// a vault card is - the art, then its name, what it is made of, its properties with the range each rolls in, and
/// whether the game has discovered it.
/// </summary>
public sealed partial class GrailCard : ObservableObject
{
    private readonly Item _item;
    private ItemTileViewModel? _tile;
    private IReadOnlyList<ItemFact>? _stats;

    /// <param name="rows">Every row of the card's name - Arm of King Leoric's two (GrailCatalog.UniqueCard); its own alone where null.</param>
    /// <param name="counts">Whether it counts towards Discovery: not the specials the Ubers and dungeon bosses drop.</param>
    public GrailCard(Item item, string kind, int row, int tableOrder, IReadOnlyList<int>? rows = null, bool counts = true)
    {
        _item = item;
        Kind = kind;
        Row = row;
        Rows = rows ?? [row];
        Counts = counts;
        TableOrder = tableOrder;

        var view = ItemPresenter.Describe(item);
        Name = view.Name;
        Subtitle = view.BaseName;
        TypeRank = ItemOrder.TypeRank(item);
        SetName = SetNames.Of(item) ?? "";
        Tier = SetName.Length > 0 ? SetNames.TierOf(SetName) : null;
    }

    /// <summary>A set piece's tier on the Sets tab - Common, Uncommon or Class (SetNames.TierOf); null for a unique.</summary>
    public SetTier? Tier { get; }

    // A craft recipe had a card of its own, on a Crafting tab, until the craft family became an option in the search page: the tab was a
    // way to pick crafted items out of the vault, which the search's Family box does now, and a chart of the
    // recipes, which is to be the crafting wizard's on the Extra page. The recipes themselves are
    // GrailCatalog.CraftEntries still, kept for that. Runewords have no tab and uniques and sets no ethereal list:
    // the game's Discovery keeps neither.

    /// <summary>"unique" or "set" - with <see cref="Row"/>, which entry it is.</summary>
    public string Kind { get; }

    /// <summary>
    /// The row's number: the FileIndex of a unique or set piece, as an item of it carries - what
    /// tells the eight Rainbow Facets apart, and the bit the game's Discovery keeps it by.
    /// </summary>
    public int Row { get; }

    /// <summary>Every row of its name, its own first: one card a name, as the game's tab has it.</summary>
    public IReadOnlyList<int> Rows { get; }

    /// <summary>
    /// Whether it counts towards Discovery. The tab shows the specials - Itherael's Path, Overlord's Helm, The Third Eye
    /// and the rest - noting they do not (seen on a screenshot); a card says so, its name unmasked.
    /// </summary>
    public bool Counts { get; }

    /// <summary>Where the row sits among its tab's entries - roughly the order PD2 added them.</summary>
    public int TableOrder { get; }

    /// <summary>The card's ethereal mark: the few rows that make their item ethereal always.</summary>
    public bool ShowsEthereal => _item.IsEthereal;

    public string Name { get; }

    /// <summary>The base.</summary>
    public string Subtitle { get; }

    private string? _text;

    /// <summary>
    /// Whether the card answers what is typed in the grail's box - a phrase, or a pattern
    /// (<paramref name="pattern"/>, <see cref="SearchPattern"/>): by its name,
    /// by its base, and by what it says as an item - its stats, a line each, the text
    /// the window's search box and the vault's search read (<see cref="ItemLines.TextOf(Item)"/>). The item's text is worded the first time it is
    /// asked for.
    /// </summary>
    public bool Answers(string typed, string? pattern)
    {
        bool Has(string text) =>
            text.Contains(typed, StringComparison.OrdinalIgnoreCase) || SearchPattern.Matches(pattern, text);

        if (Has(Name) || Has(Subtitle))
            return true;

        if (_text is null)
        {
            try
            {
                _text = ItemLines.TextOf(_item);
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                _text = "";
            }
        }

        return Has(_text);
    }

    public int TypeRank { get; }

    /// <summary>The set a set piece belongs to; empty for anything else.</summary>
    public string SetName { get; }

    /// <summary>
    /// The tile the card is drawn from, made the first time the card is drawn. It reads its sprite
    /// as it is made, and there are some six hundred entries; the list only draws those on screen.
    /// </summary>
    private ItemTileViewModel Tile => _tile ??= new ItemTileViewModel(Deposited ?? _item, 0);

    public Bitmap? Sprite => Tile.Sprite;

    /// <summary>The item the card is drawn from: the one deposited where there is one, else the row's.</summary>
    public Item Picture => Deposited ?? _item;

    public double SpriteWidth => Sprite?.PixelSize.Width ?? 0;

    public double SpriteHeight => Sprite?.PixelSize.Height ?? 0;

    /// <summary>Ghosted when ethereal, as the item panel draws one - and faint while <see cref="IsShaded"/>.</summary>
    public double SpriteOpacity => Tile.SpriteOpacity * (IsShaded ? NotStored : 1);

    /// <summary>How much of the picture shows on a card discovered and not stored: on the card's dark ground, as dark as the game's.</summary>
    private const double NotStored = 0.3;

    /// <summary>
    /// The picture faint, as the game's Compendium draws a card not stored (screenshots) - on a
    /// card discovered and not stored only, the card itself lit (three states - undiscovered as it was, the
    /// card faded; discovered and not stored, the card lit and the item sprite rather transparent;
    /// stored, whole). The Extra page's grail has no compendium: its pictures are whole. A black layer masked by the
    /// picture was tried first and drew nothing: a brush is not in the tree, and its binding found no card.
    /// </summary>
    public bool IsShaded => !Browsing && IsFound && !IsOwned;

    /// <summary>Its name's colour: its item's own.</summary>
    public IBrush RarityBrush => Tile.RarityBrush;

    /// <summary>
    /// The entry's own properties, each with the range it rolls in - the lines the game prints in blue; the deposited
    /// item's own rolls where one lies in the compendium. The base's lines are left out.
    /// </summary>
    public IReadOnlyList<ItemFact> Stats => _stats ??= [.. Tile.Summed.Where(line => line.IsMagic)];

    /// <summary>The properties are shown: discovered, or a copy deposited - an undiscovered name and base only otherwise.</summary>
    public bool ShowsProperties => IsFound || IsOwned;

    // ---- discovered -----------------------------------------------------------------------------

    /// <summary>
    /// Discovered by the game - its Discovery Progress, account-wide, read from the shared stash: the record is not
    /// ours to keep, we only read from it. The editor keeps no finds of its own.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClickable))]
    [NotifyPropertyChangedFor(nameof(TakesPointer))]
    [NotifyPropertyChangedFor(nameof(ShowsProperties))]
    [NotifyPropertyChangedFor(nameof(IsShaded))]
    [NotifyPropertyChangedFor(nameof(SpriteOpacity))]
    private bool _isFound;

    /// <summary>
    /// The copy deposited in the game's Compendium - the stash's Uniques and Sets tabs, one copy kept of each, to store
    /// or retrieve - or null. The card is drawn from it and says its rolls.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOwned))]
    [NotifyPropertyChangedFor(nameof(ShowsProperties))]
    [NotifyPropertyChangedFor(nameof(Picture))]
    [NotifyPropertyChangedFor(nameof(CanStore))]
    [NotifyPropertyChangedFor(nameof(CanTakeOut))]
    [NotifyPropertyChangedFor(nameof(IsClickable))]
    [NotifyPropertyChangedFor(nameof(TakesPointer))]
    [NotifyPropertyChangedFor(nameof(IsShaded))]
    private Item? _deposited;

    partial void OnDepositedChanged(Item? oldValue, Item? newValue)
    {
        if (ReferenceEquals(oldValue, newValue))
            return;

        // Drawn and worded again from the item now shown.
        _tile = null;
        _stats = null;
        _text = null;
        OnPropertyChanged(nameof(Sprite));
        OnPropertyChanged(nameof(SpriteWidth));
        OnPropertyChanged(nameof(SpriteHeight));
        OnPropertyChanged(nameof(SpriteOpacity));
        OnPropertyChanged(nameof(RarityBrush));
        OnPropertyChanged(nameof(Stats));
    }

    /// <summary>A copy lies in the compendium (<see cref="Deposited"/>).</summary>
    public bool IsOwned => Deposited is not null;

    /// <summary>Store is offered: none deposited yet, and a copy held here - on the character or in the shared stash.</summary>
    public bool CanStore => !Browsing && !IsOwned && HeldOnCharacter > 0;

    /// <summary>Take out is offered: one deposited.</summary>
    public bool CanTakeOut => !Browsing && IsOwned;

    /// <summary>
    /// Copies held now, on the Items page's grail (to see what is collected and
    /// which extras are spare): everything the character and the shared stash hold, and the open
    /// collection. Ethereal copies count with the plain ones. Items in sockets are not copies held spare,
    /// so neither side counts them.
    /// </summary>
    [ObservableProperty]
    private bool _showsHeld;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalHeld))]
    [NotifyPropertyChangedFor(nameof(IsClickable))]
    [NotifyPropertyChangedFor(nameof(TakesPointer))]
    [NotifyPropertyChangedFor(nameof(CanStore))]
    private int _heldOnCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalHeld))]
    [NotifyPropertyChangedFor(nameof(IsClickable))]
    [NotifyPropertyChangedFor(nameof(TakesPointer))]
    private int _heldInVault;

    /// <summary>
    /// Copies on the other characters of the same mode, read from their saves without opening them
    /// (SavedItems): a count, never an act - to take one, that character is opened.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalHeld))]
    private int _heldOnOthers;

    /// <summary>Every count together: what the Count sort orders by.</summary>
    public int TotalHeld => HeldOnCharacter + HeldInVault + HeldOnOthers;

    /// <summary>
    /// Whether the pointer reaches the card: one discovered, or with a copy here or in the vault, to be chosen - choosing
    /// searches the vault for its copies. One with neither is unseen by the pointer - it has no Forge button to make the
    /// item from its row (the Forge page makes the same items).
    /// </summary>
    public bool IsClickable => !Browsing && (IsFound || IsOwned || HeldOnCharacter + HeldInVault > 0);

    /// <summary>
    /// Whether the pointer reaches the card at all. Browsed, every card does - its properties scroll
    /// under the wheel - though a click chooses nothing and nothing lights.
    /// </summary>
    public bool TakesPointer => Browsing || IsClickable;

    /// <summary>
    /// Shown on the Extra page's grail, which is only browsed: nothing on
    /// the card takes the pointer - no search.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClickable))]
    [NotifyPropertyChangedFor(nameof(TakesPointer))]
    [NotifyPropertyChangedFor(nameof(CanStore))]
    [NotifyPropertyChangedFor(nameof(CanTakeOut))]
    [NotifyPropertyChangedFor(nameof(IsShaded))]
    [NotifyPropertyChangedFor(nameof(SpriteOpacity))]
    private bool _browsing;
}

/// <summary>
/// One of the game's five ways to show its Uniques and Sets tabs (the DLL's CompendiumFilterShow*), as a choice over the
/// grail's list (the game's tabs, our cards) - or one of the Sets tab's tiers, All / Common /
/// Uncommon / Class, as the game's icons over it. <paramref name="idOfRow"/>: the
/// start of its id, which row of choices it is in.
/// </summary>
public sealed partial class GrailShow(string key, string label, string idOfRow = "Grail_Show_") : ObservableObject
{
    /// <summary>The choices' keys - ids, not words: what each says is its label.</summary>
    public const string All = "all", Discovered = "discovered", Undiscovered = "undiscovered", Owned = "owned", Missing = "missing";

    /// <summary>The tiers' keys (SetNames.TierOf), All the same as above.</summary>
    public const string Common = "common", Uncommon = "uncommon", Class = "class";

    public string Key { get; } = key;

    public string Label => L(label);

    public string Id => idOfRow + Key;

    [ObservableProperty] private bool _isCurrent;

    /// <summary>Whether a card is one this choice shows: Owned in the compendium, Missing not; a tier its set pieces.</summary>
    public bool Shows(GrailCard card) => Key switch
    {
        Discovered => card.IsFound,
        Undiscovered => !card.IsFound,
        Owned => card.IsOwned,
        Missing => !card.IsOwned,
        Common or Uncommon or Class => card.Tier is { } tier && Is(tier),
        _ => true,
    };

    /// <summary>Whether a set of this name is in this tier - for the Sets tab's drop-down, which lists the tier's sets.</summary>
    public bool ShowsSet(string set) => Key is not (Common or Uncommon or Class) || Is(SetNames.TierOf(set));

    private bool Is(SetTier tier) => Key == tier switch { SetTier.Common => Common, SetTier.Uncommon => Uncommon, _ => Class };
}

/// <summary>
/// One of the grail's tabs, as the row over its list shows it: its name on the first line, with - while the
/// grail's box holds anything - how many of its entries answer what is typed; under it how many of its entries
/// are discovered, "43 / 452", after the grail's own mark, and along its foot that share as a line.
/// </summary>
public sealed partial class GrailTab(string key, string label) : ObservableObject
{
    /// <summary>"Uniques" or "Sets": what <see cref="GrailViewModel.Tab"/> is.</summary>
    public string Key { get; } = key;

    public string Label => L(label);

    /// <summary>The names its button, its match count and its found line are known by to a screen reader and a test.</summary>
    public string TabId => "Grail_Tab_" + Key;

    public string MatchesId => "Grail_Found_" + Key;

    public string ProgressId => "Grail_Progress_" + Key;

    [ObservableProperty] private bool _isCurrent;

    /// <summary>How many of its entries are discovered, of how many: "43 / 452".</summary>
    [ObservableProperty] private string _progress = "";

    /// <summary>That, as a share of one: how far along its foot the line is drawn.</summary>
    [ObservableProperty] private double _share;

    /// <summary>How many of its entries answer what is typed in the grail's box; empty while the box is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatches))]
    private string _matches = "";

    public bool HasMatches => Matches.Length > 0;

    /// <summary>The box holds something and this tab holds none of it: it steps back.</summary>
    [ObservableProperty] private bool _isEmpty;
}

/// <summary>
/// The vault's Grail page: every unique and set piece the season has, discovered or not, under the tabs PD2's own
/// stash has - Uniques, Sets - in a row over the list (<see cref="Tabs"/>). Discovered is the game's word
/// (<see cref="GrailCard.IsFound"/>): read from the shared stash of the mode, never written.
/// <para>
/// Over the list: a quick filter - the item type for uniques, the set for set pieces - a search, and the vault's
/// sort. Selecting a card searches the vault for it by name.
/// </para>
/// </summary>
public sealed partial class GrailViewModel : ObservableObject
{
    public const string Uniques = "Uniques", Sets = "Sets";


    private readonly VaultService _vaults;

    private List<GrailCard>? _uniques, _sets;
    private IReadOnlyList<FilterChoice> _setChoices = [VaultFilter.Any];

    private readonly CharacterSession _session;
    private readonly SharedStashSession? _stash;
    private readonly VaultMoves? _moves;

    /// <summary>The open character's mode's compendium: what Store puts in and Take out takes from. Null on the Extra page's grail.</summary>
    private readonly CompendiumSession? _compendium;

    /// <summary>What Store and Take out do (<see cref="CompendiumMoves"/>), an item's menu and its key the same. Null on the Extra page's grail.</summary>
    private readonly CompendiumMoves? _deposits;

    public GrailViewModel(VaultService vaults, EditorSettings settings, CharacterSession session,
                          bool browseOnly = false, SharedStashSession? stash = null, VaultMoves? moves = null,
                          CompendiumSession? compendium = null, CompendiumMoves? deposits = null)
    {
        BrowseOnly = browseOnly;
        _vaults = vaults;
        _session = session;
        _stash = stash;
        _moves = moves;
        _compendium = compendium;
        _deposits = deposits;
        _held = new WhenShown(ApplyHeld);
        _discovered = new WhenShown(DiscoveredAgain);

        // The copies held, counted again after anything that moves, deletes or makes an item - on the
        // character, in the shared stash, in the vault, or staged between them - and after an undo.
        if (!browseOnly)
        {
            session.Changed += CountHeldSoon;
            vaults.Changed += CountHeldSoon;
            vaults.Switched += CountHeldSoon;
            vaults.Written += CountHeldSoon;
            if (stash is not null)
                stash.Changed += CountHeldSoon;
            if (moves is not null)
                moves.Changed += CountHeldSoon;

            // The other characters' copies: read again when a save in the folder is written, made or deleted.
            session.SaveFolderChanged += () => _ = CountOthersAsync();
        }

        // What the game has discovered: read again whenever the save folder changes - the game writes the shared stash
        // as it plays - and when another mode's vault is opened. The Items page's grail only while it is in front
        // (Shown); the Extra page's is told nothing of its showing, and reads at once - a header, no items.
        void Again()
        {
            if (browseOnly)
                DiscoveredAgain();
            else
                _discovered.Ask();
        }

        session.SaveFolderChanged += Again;
        vaults.Switched += Again;

        // What lies in the compendium: as it stands here, a store not yet saved included.
        if (compendium is not null)
            compendium.Changed += Again;

        // A card chosen is a search, and the search belongs to the session it was made in: let go
        // of when a character is opened or closed.
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded))
            {
                Release();
                Again();
            }
        };

        // "Found" puts the discovered first, the rest after, each in table order. No Rarity: each tab is one rarity.
        // Count - the copies held, character and vault together - only where the counts are shown;
        // the Extra page's grail is browsed and counts nothing. Each page keeps
        // its own order: shared, Count chosen on the Items page read as nothing on the Extra page's and
        // fell back to Type there.
        _order = browseOnly
            ? new SortChoice(settings, "GrailBrowse", N("Type"), N("Found"), N("Name"))
            : new SortChoice(settings, "Grail", N("Type"), N("Found"), N("Name"), N("Count"));
        _order.Changed += Show;

        // The card's search ended elsewhere - cleared on the vault page, or changed into another: the card
        // is let go of too, so it does not stay lit over a search it no longer is.
        if (!browseOnly)
            vaults.Filter.Changed += LetGoIfSearchEnded;
    }

    private void DiscoveredAgain()
    {
        if (_uniques is null)
            return;

        ApplyDiscovered();
        Show();
    }

    /// <summary>
    /// The copies held, and what is discovered, worked out only while the grail is in front - asked behind another
    /// bookmark, once when it is next shown. Once opened it counted after every move on the character, every store and
    /// every change of the vault: a query and some 1,500 cards told four times over, for nobody.
    /// </summary>
    private readonly WhenShown _held, _discovered;

    /// <summary>The grail in front (VP_ViewModel_Vault_R.Show): what it owes, done.</summary>
    public void Shown()
    {
        _held.Shown();
        _discovered.Shown();
    }

    public void Hidden()
    {
        _held.Hidden();
        _discovered.Hidden();
    }

    private readonly SortChoice _order;

    /// <summary>The order the tab shown is in, and what its sort menu offers.</summary>
    public SortChoice Order => _order;

    /// <summary>The cards the list shows: the tab's, narrowed and ordered.</summary>
    public ObservableCollection<GrailCard> Cards { get; } = [];

    // ---- tabs ---------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUniques))]
    [NotifyPropertyChangedFor(nameof(IsSets))]
    [NotifyPropertyChangedFor(nameof(QuickChoices))]
    [NotifyPropertyChangedFor(nameof(QuickHint))]
    private string _tab = Uniques;

    public bool IsUniques => Tab == Uniques;
    public bool IsSets => Tab == Sets;

    partial void OnTabChanged(string value)
    {
        foreach (var tab in Tabs)
            tab.IsCurrent = tab.Key == value;
    }

    /// <summary>
    /// The tabs, as the row over the list shows them (they stood in a
    /// column at the list's left, which took its width for the page's whole height): each its name, how many
    /// of its entries are discovered, and - while the box holds anything - how many it holds of what is typed.
    /// </summary>
    public IReadOnlyList<GrailTab> Tabs { get; } =
    [
        new(Uniques, N("UNIQUES")) { IsCurrent = true },
        new(Sets, N("SETS")),
    ];

    [RelayCommand]
    private void ShowTab(string tab)
    {
        if (Tab == tab)
            return;

        // Back to Any before the drop-down's list is swapped: Any is on every list, so the box
        // keeps it, where a choice it no longer lists would come back as nothing.
        Quick = VaultFilter.Any;
        Selected = null;
        Tab = tab;
        Show();
    }

    /// <summary>
    /// The grail at one entry (the window's search, a grail find gone to), or at all a tab holds of what was
    /// typed there (a grail tab's "See all results"): the tab, and the name, or what was typed, in the box, which
    /// narrows the cards to it. The tab's type is let go of: the cards shown are then the ones the search box
    /// counted. Nothing is selected: selecting a card searches the vault for its copies, which is another act.
    /// </summary>
    public void ShowEntry(string tab, string name)
    {
        Load();
        Selected = null;
        Quick = VaultFilter.Any;
        Tab = tab;

        // The list's Show and the Sets' tier too: left on Owned or a tier, a find gone to showed an empty list.
        foreach (var show in Shows)
            show.IsCurrent = show.Key == GrailShow.All;
        foreach (var tier in Tiers)
            tier.IsCurrent = tier.Key == GrailShow.All;
        OnPropertyChanged(nameof(QuickChoices));

        if (Search == name)
            Show();
        else
            Search = name;
    }

    // ---- building the lists -------------------------------------------------------------------

    /// <summary>
    /// Made the first time the page is opened: they need the tables, and there is no hurry
    /// before. What is discovered is read again every time, as the game may have written it since.
    /// </summary>
    public void Load()
    {
        if (_uniques is null)
        {
            // What is on the grail is GrailCatalog's to say.
            // The game's Uniques tab: one card a name, the specials marked (GrailCatalog.UniqueCards).
            _uniques = [.. GrailCatalog.UniqueCards().Select(card => new GrailCard(
                card.Rows[0].Item, card.Rows[0].Kind, card.Rows[0].Row, card.Rows[0].Order,
                [.. card.Rows.Select(entry => entry.Row)], card.Counts))];
            _sets = CardsOf(GrailCatalog.Sets());

            _setChoices =
            [
                VaultFilter.Any,
                .. _sets.Select(card => card.SetName).Where(name => name.Length > 0).Distinct()
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .Select(name => new FilterChoice(name, Code: name)),
            ];
            OnPropertyChanged(nameof(QuickChoices));
        }

        ApplyDiscovered();
        ApplyHeld();
        Show();
    }

    /// <summary>A card per entry.</summary>
    private static List<GrailCard> CardsOf(IEnumerable<GrailCatalog.Entry> entries) =>
        [.. entries.Select(entry => new GrailCard(entry.Item, entry.Kind, entry.Row, entry.Order))];

    private IReadOnlyList<GrailCard> TabCards => CardsOn(Tab);

    private IReadOnlyList<GrailCard> CardsOn(string tab) => (tab switch
    {
        Uniques => _uniques,
        Sets => _sets,
        _ => null,
    }) ?? [];

    // ---- discovered ---------------------------------------------------------------------------------

    /// <summary>
    /// The shared stash whose Discovery the grail reads: the open character's mode's, or with none the open vault's -
    /// softcore and hardcore each keep their own.
    /// </summary>
    private string DiscoveryFile =>
        _session.SaveFolder is { Length: > 0 } folder
            ? System.IO.Path.Combine(folder, (_session.IsLoaded ? _session.IsHardcore : _vaults.IsHardcore)
                ? SharedStashSession.HardcoreFile : SharedStashSession.SoftcoreFile)
            : "";

    /// <summary>
    /// Each card told whether the game has discovered it: uniques and set pieces by their rows' bits, one read of the
    /// file (SharedStash.Discovered).
    /// </summary>
    private void ApplyDiscovered()
    {
        var discovered = DiscoveryFile is { Length: > 0 } path ? SharedStash.DiscoveredIn(path) : SharedStash.Discovery.None;

        foreach (var card in _uniques ?? [])
            card.IsFound = card.Rows.Any(discovered.Uniques.Contains);
        foreach (var card in _sets ?? [])
            card.IsFound = card.Rows.Any(discovered.Sets.Contains);

        var owned = Owned();
        foreach (var card in (_uniques ?? []).Concat(_sets ?? []))
            card.Deposited = card.Rows.Select(row => owned.GetValueOrDefault((card.Kind, row))).FirstOrDefault(item => item is not null);
    }

    /// <summary>
    /// The copy the mode's Compendium holds of each entry, by row: the open session's, a store not yet saved included,
    /// or with no character the file's. None where it has no file yet or the file does not read (said in the log,
    /// never on the page: the grail is the same without it). Where a row is held twice the first counts.
    /// </summary>
    private Dictionary<(string, int), Item> Owned()
    {
        var owned = new Dictionary<(string, int), Item>();
        if (_compendium is { IsLoaded: true } open && _session.IsLoaded)
        {
            foreach (var item in open.Items)
            {
                if (GrailCatalog.EntryOf(item) is { } entry)
                    owned.TryAdd(entry, item);
            }

            return owned;
        }

        if (_session.SaveFolder is not { Length: > 0 } folder)
            return owned;

        try
        {
            bool hardcore = _session.IsLoaded ? _session.IsHardcore : _vaults.IsHardcore;
            foreach (var item in Compendium.In(folder, hardcore)?.Items ?? [])
            {
                if (GrailCatalog.EntryOf(item) is { } entry)
                    owned.TryAdd(entry, item);
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }
        finally
        {
            // Reading the file set the parser for it: back to the open character's layout.
            if (_session.IsLoaded)
                _session.UseMyLayout();
        }

        return owned;
    }

    // ---- copies held -----------------------------------------------------------------------------

    private bool _countPosted;

    /// <summary>
    /// Counted once the burst of changes an action raises has passed: a move fires the character's,
    /// the stash's and the moves' notices together, and one count after them is enough.
    /// </summary>
    private void CountHeldSoon()
    {
        if (_countPosted || _uniques is null)
            return;

        _countPosted = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _countPosted = false;
            _held.Ask();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>A card's count: its row's copies, ethereal or not (<see cref="GrailCatalog.EntryOf"/>).</summary>
    private static int Of(IReadOnlyDictionary<(string, int), int> counts, GrailCard card) =>
        card.Rows.Sum(row => counts.GetValueOrDefault((card.Kind, row)));

    /// <summary>
    /// Each unique and set card given its copies held: on the character (with the shared stash), and in
    /// the open collection. A store or a withdrawal staged under the Move rule counts where it is going -
    /// the item marked to go is the vault's, the row marked to come out the character's - so the counts
    /// do not wait for the save.
    /// </summary>
    private void ApplyHeld()
    {
        if (BrowseOnly || _uniques is null)
            return;

        var staged = _moves?.Storing.ToList() ?? [];
        var leaving = new HashSet<Item>(staged, ReferenceEqualityComparer.Instance);

        var onCharacter = new Dictionary<(string, int), int>();
        foreach (var item in TopLevel(_session.HeldItems.Concat(_stash?.HeldItems ?? [])))
        {
            if (!leaving.Contains(item))
                Add(onCharacter, item);
        }

        var inVault = new Dictionary<(string, int), int>();
        try
        {
            if (_vaults.Store is { } store)
            {
                foreach (var (key, count) in store.GrailCounts(_moves?.Withdrawing ?? []))
                    inVault[key] = count;
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }

        foreach (var item in staged)
            Add(inVault, item);

        foreach (var card in (_uniques ?? []).Concat(_sets ?? []))
        {
            card.HeldOnCharacter = Of(onCharacter, card);
            card.HeldInVault = Of(inVault, card);
            card.HeldOnOthers = Of(_onOthers, card);
            card.ShowsHeld = true;
        }

        // Ordered by the counts: what changed them moves the cards.
        if (Order.By == "Count")
            Show();

        // Another character open than the others were counted beside: counted again, it now among them.
        if (_othersBeside != _session.SavePath)
            _ = CountOthersAsync();
    }

    private static void Add(Dictionary<(string, int), int> counts, Item item)
    {
        if (item.Quality is not (ItemQuality.Unique or ItemQuality.Set) || GrailCatalog.EntryOf(item) is not { } entry)
            return;

        counts[entry] = counts.GetValueOrDefault(entry) + 1;
    }

    /// <summary>The copies on the other characters, by card; and which open character they were counted beside.</summary>
    private Dictionary<(string, int), int> _onOthers = [];
    private string? _othersBeside;

    /// <summary>Which counting of the others is under way; a newer one stops an older.</summary>
    private int _othersRound;

    /// <summary>Each save's own count, kept while its items are the ones read (<see cref="SavedItems"/> keeps them until the file changes).</summary>
    private readonly Dictionary<string, (SavedItems.Held Held, Dictionary<(string, int), int> Counts)> _perSave =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The other characters' copies, counted: every save of the folder but the open one, of the open
    /// character's mode - softcore and hardcore share nothing in the game, and the vault is a mode's too. One
    /// save at a time, the page answering in between; a save not changed since it was read costs a look at
    /// its file. Read only.
    /// </summary>
    private async System.Threading.Tasks.Task CountOthersAsync()
    {
        if (BrowseOnly || _uniques is null || !_session.IsLoaded)
            return;

        int round = ++_othersRound;
        string? mine = _session.SavePath;
        bool hardcore = _session.IsHardcore;
        var counts = new Dictionary<(string, int), int>();
        try
        {
            foreach (var save in SaveSummary.FromFolder(_session.SaveFolder))
            {
                if (string.Equals(save.Path, mine, StringComparison.OrdinalIgnoreCase) || save.IsHardcore != hardcore)
                    continue;

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                if (round != _othersRound)
                    return;

                if (SavedItems.Of(save.Path, () => { if (_session.IsLoaded) _session.UseMyLayout(); }) is not { } held)
                    continue;

                if (!_perSave.TryGetValue(save.Path, out var kept) || !ReferenceEquals(kept.Held, held))
                {
                    var own = new Dictionary<(string, int), int>();
                    foreach (var item in held.All)
                        Add(own, item);
                    _perSave[save.Path] = kept = (held, own);
                }

                foreach (var (key, count) in kept.Counts)
                    counts[key] = counts.GetValueOrDefault(key) + count;
            }
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }

        _onOthers = counts;
        _othersBeside = mine;
        foreach (var card in (_uniques ?? []).Concat(_sets ?? []))
            card.HeldOnOthers = Of(counts, card);

        if (Order.By == "Count")
            Show();
    }

    /// <summary>The items themselves, not what sits in their sockets (HeldItems lists both).</summary>
    private static IEnumerable<Item> TopLevel(IEnumerable<Item> held)
    {
        var all = held.ToList();
        var socketed = new HashSet<Item>(all.SelectMany(item => item.SocketedItems), ReferenceEqualityComparer.Instance);
        return all.Where(item => !socketed.Contains(item));
    }

    // ---- the quick filter, the search, the order ------------------------------------------------

    /// <summary>What the drop-down offers: for uniques the item types, as the vault search has them; for set pieces every set by name.</summary>
    public IReadOnlyList<FilterChoice> QuickChoices =>
        Tab == Sets ? [.. _setChoices.Where(choice => choice.IsAny || choice.Code is not { } set || Tier.ShowsSet(set))] : TypesHeld();

    /// <summary>
    /// The vault search's item types, less those with nothing in the list shown - runes, gems
    /// and the like, which no unique is.
    /// </summary>
    private IReadOnlyList<FilterChoice> TypesHeld()
    {
        var cards = TabCards;
        return [.. VaultFilter.TypeSpanChoices.Where(choice =>
            choice.IsAny || cards.Any(card => card.TypeRank >= choice.From && card.TypeRank <= choice.To))];
    }

    public string QuickHint => Tab == Sets ? L("Set") : L("Type");

    [ObservableProperty] private FilterChoice? _quick = VaultFilter.Any;

    partial void OnQuickChanged(FilterChoice? value) => Show();

    /// <summary>
    /// What is typed in the grail's box: part of a name, of a base or of a stat's line, ignoring case; or a pattern
    /// (<see cref="GrailCard.Answers"/>). It stands across the tabs.
    /// </summary>
    [ObservableProperty] private string _search = "";

    partial void OnSearchChanged(string value) => Show();

    // ---- what each tab says -------------------------------------------------------
    // Two numbers, each with its own place on the tab, so neither takes the other's:
    //   discovered / total - "43 / 452", always: how far that part of the grail is. Not narrowed by anything
    //     chosen or typed.
    //   how many it holds of what is typed, while the box holds anything - the box stands across the tabs
    //     and the list shows one of them, so what is typed may be on a tab not in sight (the window's search
    //     box finds on both at once). A tab with none steps back.

    /// <summary>The tab of that name.</summary>
    public GrailTab TabOf(string key) => Tabs.First(tab => tab.Key == key);

    /// <summary>
    /// The tabs' numbers, as things stand: how many of each are discovered, and - for what is typed - how many
    /// each holds, as its list would show it, come to from another tab.
    /// </summary>
    private void CountTabs(string typed, string? pattern)
    {
        foreach (var tab in Tabs)
        {
            // Discovered of those that count, as the game's "9/427": the specials are shown and not counted.
            var cards = CardsOn(tab.Key);
            var counted = cards.Where(card => card.Counts).ToList();
            int found = counted.Count(card => card.IsFound), owned = cards.Count(card => card.IsOwned);
            tab.Progress = owned > 0 ? L($"{found} / {counted.Count} · {owned} owned") : $"{found} / {counted.Count}";
            tab.Share = counted.Count > 0 ? (double)found / counted.Count : 0;

            int? matches = typed.Length > 0
                ? cards.Count(card => Showing.Shows(card) && (tab.Key != Sets || Tier.Shows(card)) && card.Answers(typed, pattern))
                : null;
            tab.Matches = matches?.ToString() ?? "";
            tab.IsEmpty = matches == 0;
        }
    }

    /// <summary>
    /// Beside the page's title, while something narrows the list - the box, the type: how many cards are shown.
    /// Nothing otherwise: how many are discovered is on the tab itself (it stood here, "43 / 452 found", until the
    /// tabs said it).
    /// </summary>
    public string CountText => Cards.Count == TabCards.Count ? "" : L($"{Cards.Count} shown");

    // ---- the game's five ways to show -----------------------------------------------------

    /// <summary>All, Discovered, Undiscovered, Owned, Missing - the game's own, over the list.</summary>
    public IReadOnlyList<GrailShow> Shows { get; } =
    [
        new(GrailShow.All, N("All")) { IsCurrent = true },
        new(GrailShow.Discovered, N("Discovered")),
        new(GrailShow.Undiscovered, N("Undiscovered")),
        new(GrailShow.Owned, N("Owned")),
        new(GrailShow.Missing, N("Missing")),
    ];

    /// <summary>The one chosen: it narrows the list, and the tabs' counts of what is typed.</summary>
    public GrailShow Showing => Shows.FirstOrDefault(show => show.IsCurrent) ?? Shows[0];

    [RelayCommand]
    private void ShowOnly(string key)
    {
        foreach (var show in Shows)
            show.IsCurrent = show.Key == key;
        Selected = null;
        Show();
    }

    /// <summary>
    /// The Sets tab's tiers, as the game's icons over its tab name them (the wiki's
    /// All Set Items / Common / Uncommon / Class-Focused): the set pieces of one, its sets alone in the drop-down.
    /// </summary>
    public IReadOnlyList<GrailShow> Tiers { get; } =
    [
        new(GrailShow.All, N("All"), TierIds) { IsCurrent = true },
        new(GrailShow.Common, N("Common"), TierIds),
        new(GrailShow.Uncommon, N("Uncommon"), TierIds),
        new(GrailShow.Class, N("Class"), TierIds),
    ];

    private const string TierIds = "Grail_Tier_";

    public GrailShow Tier => Tiers.FirstOrDefault(tier => tier.IsCurrent) ?? Tiers[0];

    [RelayCommand]
    private void ShowTier(string key)
    {
        foreach (var tier in Tiers)
            tier.IsCurrent = tier.Key == key;
        Selected = null;

        // The drop-down lists the tier's sets: a set of another tier chosen in it goes back to any.
        OnPropertyChanged(nameof(QuickChoices));
        if (Tab == Sets && Quick is { IsAny: false } quick && !QuickChoices.Contains(quick))
            Quick = VaultFilter.Any;
        Show();
    }

    // ---- store and take out -------------------------

    /// <summary>
    /// The copy Store takes: the item selected, where it is this entry's; else the first of the character's carried and
    /// stored items, then the shared stash's. Not one worn, the mercenary's or on a corpse - those come off by hand -
    /// nor one in a socket. Which of the two holds it says the second.
    /// </summary>
    private (Item Item, bool Shared)? CopyOf(GrailCard card)
    {
        bool Is(Item item) => GrailCatalog.EntryOf(item) is { } entry && entry.Kind == card.Kind && card.Rows.Contains(entry.Row);

        var mine = TopLevel(_session.HeldItems)
            .Where(item => item.Mode == ItemMode.Stored && !_session.IsMercenaryItem(item) && !_session.IsCorpseItem(item) && Is(item))
            .ToList();
        var shared = TopLevel(_stash?.HeldItems ?? []).Where(Is).ToList();

        if (_session.SelectedItem?.Item is { } chosen)
        {
            if (mine.Contains(chosen))
                return (chosen, false);
            if (shared.Contains(chosen))
                return (chosen, true);
        }

        return mine.Count > 0 ? (mine[0], false) : shared.Count > 0 ? (shared[0], true) : null;
    }

    /// <summary>
    /// A copy of the card's entry into the compendium, as the game's Deposit (<see cref="CompendiumMoves.Store"/>): the
    /// copy <see cref="CopyOf"/> finds, one kept a card.
    /// </summary>
    [RelayCommand]
    private void Store(GrailCard? card)
    {
        if (card is null || BrowseOnly || _deposits is null)
            return;

        if (card.IsOwned)
        {
            _session.Status = L($"{card.Name} already in compendium.");
            return;
        }

        if (CopyOf(card) is not var (item, _))
        {
            _session.Status = L($"No {card.Name} carried or stored here.");
            return;
        }

        _deposits.Store([item]);
    }

    /// <summary>The card's deposited copy taken out, as the game's Withdraw (<see cref="CompendiumMoves.TakeOut"/>).</summary>
    [RelayCommand]
    private void TakeOut(GrailCard? card)
    {
        if (card?.Deposited is { } item && !BrowseOnly)
            _deposits?.TakeOut(item);
    }

    /// <summary>The card's key, G, over the Grail page: its item taken out where one is in, else a copy stored.</summary>
    public void Transfer(GrailCard card)
    {
        if (card.IsOwned)
            TakeOut(card);
        else
            Store(card);
    }

    /// <summary>
    /// A card's menu: Store or Take out, as the card's arrow - no
    /// key on the row, as no worded row of an item's menu has one. None on the Extra page's grail, which is only browsed.
    /// </summary>
    public ActMenu MenuFor(GrailCard card)
    {
        if (BrowseOnly || _deposits is null)
            return new ActMenu([], []);

        var row = card.IsOwned
            ? new MenuAct(L("Take out of compendium"), MenuGlyph.ArrowUp, () => TakeOut(card)) { IsMain = true }
            : new MenuAct(L("Store in compendium"), MenuGlyph.ArrowDown, () => Store(card))
            {
                IsMain = true,
                IsEnabled = card.CanStore,
                Why = card.CanStore ? "" : L($"No {card.Name} carried or stored here."),
            };
        return new ActMenu([], [row]);
    }

    // ---- dragged off its card ---------------------------

    /// <summary>The card whose item is being dragged off it, while it is; nothing has left the compendium yet.</summary>
    public GrailCard? Carried { get; private set; }

    /// <summary>
    /// A drag has left a card with its item in the compendium: the item carried, drawn under the pointer as a gem out
    /// of the Sockets row is (SocketCarry, not from a row) - the Items page lights where it would land. False where
    /// there is nothing to carry, or it cannot come out now (said on the strip).
    /// </summary>
    public bool BeginCarry(GrailCard card)
    {
        if (BrowseOnly || _deposits is null || card.Deposited is not { } item || Carried is not null)
            return false;
        if (_deposits.Blocked(L("take out")) is { } why)
        {
            if (why.Length > 0)
                _session.Status = why;
            return false;
        }

        Carried = card;
        PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.Begin([item], EndCarry, fromRow: false);
        return true;
    }

    /// <summary>The drag over, nothing moved: let go of where it lands nowhere, Esc, the right button, off the book.</summary>
    public void EndCarry()
    {
        Carried = null;
        PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.End();
    }

    /// <summary>
    /// Let go of on a cell of the Items page: out of the compendium, there or in the free room nearest it, as the take
    /// out lands it - <paramref name="land"/> sets that cell (VP_ViewModel_Inv_L.LandFromRow) around the take out.
    /// </summary>
    public void DropCarried(Action<Action> land)
    {
        if (Carried is not { } card)
            return;

        EndCarry();
        land(() => TakeOut(card));
    }

    private void Show()
    {
        IEnumerable<GrailCard> shown = TabCards;
        shown = shown.Where(Showing.Shows);
        if (Tab == Sets)
            shown = shown.Where(Tier.Shows);

        // Null too is Any: the drop-down hands that back while its list is being swapped.
        if (Quick is { IsAny: false } quick)
        {
            shown = quick.Code is not { } code
                ? shown.Where(card => card.TypeRank >= quick.From && card.TypeRank <= quick.To)
                : shown.Where(card => card.SetName.Equals(code, StringComparison.OrdinalIgnoreCase));
        }

        string typed = (Search ?? "").Trim();
        string? pattern = SearchPattern.Of(typed);
        if (typed.Length > 0)
            shown = shown.Where(card => card.Answers(typed, pattern));

        CountTabs(typed, pattern);

        var ordered = Order.By switch
        {
            "Found" => shown.OrderBy(card => card.IsFound ? 0 : 1).ThenBy(card => card.TableOrder),
            "Name" => shown.OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase).ThenBy(card => card.TableOrder),
            "Count" => shown.OrderBy(card => card.TotalHeld).ThenBy(card => card.TableOrder),
            _ => shown.OrderBy(card => card.TypeRank)
                      .ThenBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(card => card.TableOrder),
        };

        // The list is rebuilt, and the list box lets go of its selection as it empties. The card
        // chosen is chosen again if it is still shown, and its search kept; gone, it is let go.
        var kept = Selected;
        _rebuilding = true;

        Cards.Clear();
        foreach (var card in Order.Arrange(ordered))
        {
            card.Browsing = BrowseOnly;
            Cards.Add(card);
        }

        Selected = kept is not null && kept.IsClickable && Cards.Contains(kept) ? kept : null;
        _rebuilding = false;

        if (kept is not null && Selected is null)
            Release();

        OnPropertyChanged(nameof(CountText));
    }

    // ---- choosing a card ----------------------------------------------------------------------

    /// <summary>
    /// The card selected - selecting one is what searches for it, and clicking it again, which
    /// lets it go, ends the search. Only a clickable card can be (<see cref="GrailCard.IsClickable"/>).
    /// </summary>
    [ObservableProperty] private GrailCard? _selected;

    private bool _rebuilding;

    /// <summary>The name this page searched the vault for, while that search stands.</summary>
    private string? _searched;

    /// <summary>
    /// The Extra page's grail:
    /// browsed only - a card chosen searches nothing; that is the Items page's Grail.
    /// </summary>
    public bool BrowseOnly { get; }

    partial void OnSelectedChanged(GrailCard? value)
    {
        if (_rebuilding)
            return;

        if (BrowseOnly && value is not null)
        {
            Selected = null;
            return;
        }

        // A card with nothing to search for is not chosen. With no character, none is: choosing searches, and
        // nothing searched is shown.
        if (value is { IsClickable: false } || (value is not null && !_session.IsLoaded))
        {
            Selected = null;
            return;
        }

        if (value is null)
            Release();
        else
            Find(value);
    }

    /// <summary>
    /// A card chosen: the vault on the left shows the copies of it, by a name search over every
    /// folder. The rest of the search is let go of, so nothing else hides them.
    /// </summary>
    private void Find(GrailCard card)
    {
        _finding = true;
        try
        {
            SearchFor(card);
        }
        finally
        {
            _finding = false;
        }
    }

    /// <summary>Setting the card's search: the filter's changes on the way are this page's own.</summary>
    private bool _finding;

    private void SearchFor(GrailCard card)
    {
        _vaults.Filter.Clear();
        _vaults.Filter.Name = card.Name;
        _searched = card.Name;
    }

    /// <summary>
    /// The vault's search changed: if it is no longer the chosen card's - cleared, or another typed in
    /// its place - the card is let go of. The search itself is left as it is: it is the user's now.
    /// </summary>
    private void LetGoIfSearchEnded()
    {
        if (_finding || _rebuilding || Selected is null || _searched is not { } name)
            return;

        if (_vaults.Filter.Name == name)
            return;

        _searched = null;
        Selected = null;
    }

    /// <summary>
    /// The card let go - clicked again, or the page left: its search ends. Only if the search is
    /// still this page's: one typed on the search page since is the user's, and stays.
    /// </summary>
    public void Release()
    {
        if (!_rebuilding && Selected is not null)
        {
            Selected = null;
            return;
        }

        if (_searched is { } name && _vaults.Filter.Name == name)
            _vaults.Filter.Clear();

        _searched = null;
    }
}
