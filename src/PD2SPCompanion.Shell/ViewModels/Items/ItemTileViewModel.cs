using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D2SLib.Model.Save;
using PD2SPCompanion.Shell.Imaging;
using PD2SPCompanion.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One line of an item's tooltip: what it says, the range it rolled in where the game would
/// print one, and which of the game's colours it is said in.
/// </summary>
public sealed record ItemFact(string Text, string Range = "", string Tone = ItemFact.Fact,
    bool Corrupted = false, bool Desecrated = false)
{
    /// <summary>
    /// The game's red <c>*</c> at the end of a line a corruption gave - the merged line, where its number was
    /// summed into one the item had - and a gold one on the line the desecration gave, behind the red where both
    /// wrote it. Empty on any other line.
    /// </summary>
    public string CorruptStar => Corrupted ? "*" : "";
    public string DesecrateStar => Desecrated ? "*" : "";

    /// <summary>A line as Core words it (<see cref="ItemLines"/>), for a view to draw.</summary>
    public static ItemFact From(ItemLine line) => new(line.Text, line.Range, line.Tone, line.Corrupted, line.Desecrated);

    // The tones are the lines' own: one set of names for the wording and for the styles that color it.
    public const string Name = ItemLine.Name;
    public const string Fact = ItemLine.Fact;
    public const string Magic = ItemLine.Magic;
    public const string Corrupt = ItemLine.Corrupt;
    public const string Desecrate = ItemLine.Desecrate;
    public const string Skin = ItemLine.Skin;
    public const string Warning = ItemLine.Warning;
    public const string Unmet = ItemLine.Unmet;
    public const string SetBonus = ItemLine.SetBonus;
    public const string Rule = ItemLine.Rule;
    public const string Runes = ItemLine.Runes;

    public bool HasRange => Range.Length > 0;
    public bool IsFact => Tone == Fact;
    public bool IsMagic => Tone == Magic;
    public bool IsCorrupt => Tone == Corrupt;
    public bool IsDesecrate => Tone == Desecrate;
    public bool IsSkin => Tone == Skin;
    public bool IsWarning => Tone == Warning;
    public bool IsUnmet => Tone == Unmet;
    public bool IsSetBonus => Tone == SetBonus;
    public bool IsRunes => Tone == Runes;
    public bool IsRule => Tone == Rule;
    public bool IsWords => Tone != Rule;
}

/// <summary>One item as it sits on a grid: a sprite at a cell position.</summary>
public sealed partial class ItemTileViewModel : ObservableObject
{
    /// <summary>Diablo II's inventory cell is 28 pixels square, which is also the sprites' scale.</summary>
    public const int CellSize = 28;

    public Item Item { get; }
    public int Index { get; private set; }
    public ItemView View { get; private set; }
    public Bitmap? Sprite { get; private set; }

    /// <summary>Cell position on the grid. Stored items use their own X/Y; equipped
    /// items have no meaningful grid coordinates, so the caller places them.</summary>
    public int CellX { get; private set; }
    public int CellY { get; private set; }

    /// <summary>
    /// The same item, somewhere else: a kept tile moved rather than a new one made (CharacterSession.TileFor).
    /// A moved item used to get a new tile, the selection followed it there, and the item panel built
    /// all its rows again - a select's work on every move of the item being looked at.
    /// </summary>
    public void MoveTo(int index, int cellX, int cellY)
    {
        Index = index;
        if (CellX == cellX && CellY == cellY)
            return;

        CellX = cellX;
        CellY = cellY;
        OnPropertyChanged(nameof(CellX));
        OnPropertyChanged(nameof(CellY));
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
    }

    public double Left => CellX * CellSize;
    public double Top => CellY * CellSize;
    public double Width => View.GridWidth * CellSize;
    public double Height => View.GridHeight * CellSize;

    public string Name => View.Name;

    /// <summary>The name as the tooltip heads it, the game's way: with the socket count after it, "Stone Barb [4]".</summary>
    public string Title => ItemLines.Title(Item, View);

    /// <summary>
    /// The base this is built on, in brackets, for an item whose own name hides it - a
    /// unique or a set item. Empty when the name already is the base's, so nothing is said
    /// twice. Follows the item's code, so upgrading the base says so.
    /// </summary>
    public string BaseNote =>
        View.BaseName.Length > 0 && !View.BaseName.Equals(View.Name, StringComparison.Ordinal)
            ? $"({View.BaseName})"
            : "";

    /// <summary>
    /// The sockets this item has, empty ones included, laid out over its art.
    /// <para>
    /// Where the game puts them is in no table, so <see cref="SocketLayout"/> writes them out
    /// from the game's own screenshots, each hole in its place by its number. A
    /// whole cell each, the size the game draws them at, since what goes in a socket is a
    /// one-cell item; at half that they read as dots rather than as gems.
    /// </para>
    /// <para>
    /// One base in PD2 is allowed more sockets than it has cells: every sorceress orb is
    /// two cells and takes a third socket at item level 40. There is nowhere to put it, so
    /// the holes are drawn smaller, on the item rather than over its neighbours.
    /// </para>
    /// </summary>
    public IReadOnlyList<SocketViewModel> Sockets { get; private set; } = [];

    public bool HasSockets => Sockets.Count > 0;

    /// <summary>Whether something is in a socket: the item can be read with its sockets apart (<see cref="PD2SPCompanion.Shell.Services.Generic.SocketView"/>).</summary>
    public bool HasFilledSockets => Item.SocketedItems.Count > 0;

    /// <summary>The key that reads the sockets apart, for a strip under the item where it stands outside a tooltip: the trade frame.</summary>
    public IReadOnlyList<PD2SPCompanion.Shell.Services.Generic.TipHint> SocketHints => PD2SPCompanion.Shell.Services.Generic.SocketView.Hints;

    /// <summary>Shift pressed or let go: the lines are the other reading's, and whatever shows them reads them again.</summary>
    public void SocketViewChanged() => OnPropertyChanged(nameof(Tooltip));

    /// <summary>
    /// A charm that does nothing where it is, and why: outside the charm section, or a second of a unique charm
    /// (CharmArea.IdleAt). Set by whoever builds the grid, since an item on its own has no idea which container it
    /// is in.
    /// </summary>
    public CharmIdle IdleCharm
    {
        get => _idleCharm;
        set => SetProperty(ref _idleCharm, value);
    }

    private CharmIdle _idleCharm;

    public bool IsIdleCharm => IdleCharm != CharmIdle.None;

    // ---- the tile's rarer parts, built only where one shows --------------------------------------
    //
    // Most tiles show none of them, and making every part for every tile - a red wash, a count,
    // two arrows, a badge, a row of holes - was a third of the Items page's first open (tests/perf:
    // 410 ms, 273 without them). Each is this tile where something in it shows, and null where
    // nothing does, so a ContentPresenter builds its parts only then.

    /// <summary>Why a charm does nothing, as its tooltip and the move's message say it.</summary>
    public static string IdleText(CharmIdle idle) =>
        idle == CharmIdle.AnotherCounts ? L("One already active: no effect") : L("Above charm line: no effect");

    /// <summary>The red under the art: an idle charm, or one the character cannot use.</summary>
    public ItemTileViewModel? RedWash => IsIdleCharm || IsUnusable ? this : null;

    /// <summary>What sits over the art: the count, the vault's arrows, the duplicate badge, the alert of an item the game could not hold.</summary>
    public ItemTileViewModel? Marks => HasQuantity || IsLeaving || IsArriving || IsDuplicate || IsImpossible ? this : null;

    /// <summary>The holes drawn over the art.</summary>
    public ItemTileViewModel? Holes => HasSockets ? this : null;

    private static readonly System.ComponentModel.PropertyChangedEventArgs
        RedWashChanged = new(nameof(RedWash)), MarksChanged = new(nameof(Marks)), HolesChanged = new(nameof(Holes));

    /// <summary>Whatever a part shows by changes whether it is there at all.</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(IdleCharm) or nameof(IsUnusable):
                base.OnPropertyChanged(RedWashChanged);
                break;
            case nameof(HasQuantity) or nameof(IsLeaving) or nameof(IsArriving) or nameof(IsDuplicate) or nameof(IsImpossible):
                base.OnPropertyChanged(MarksChanged);
                break;
            case nameof(HasSockets) or nameof(Sockets):
                base.OnPropertyChanged(HolesChanged);
                break;
        }
    }

    /// <summary>
    /// Kept for another rebuild (CharacterSession's tile reuse): what the pages set on a tile after a
    /// rebuild goes back to how a new tile starts, so they set it again exactly as before. Not the
    /// selection: the session's SelectedItem keeps that, and handing it the same tile changes nothing.
    /// </summary>
    public void ResetMarks()
    {
        IdleCharm = CharmIdle.None;
        IsPicked = false;
        IsLeaving = false;
        IsArriving = false;
        IsSearchMiss = false;
        DuplicatePlaces = null;
        IsUnusable = false;

        // Read when drawn, and the setting it follows may have moved since. Only then: a kept tile's item
        // is byte for byte what it was (CharacterSession.TileFor), and announced on every rebuild it
        // ran the legitimacy check again for every tile - with the notices, 7 ms of a move.
        if (_announcedFlag != StatRowViewModel.FlagIllegitimate)
        {
            _announcedFlag = StatRowViewModel.FlagIllegitimate;
            OnPropertyChanged(nameof(IsImpossible));
        }
    }

    /// <summary>The flag setting <see cref="IsImpossible"/> was last read under, by a rebuild's reset.</summary>
    private bool? _announcedFlag;

    // ---- a vault item ---------------------------------------------------------------------------------
    // A vault item is its tile. A card between the two (VaultCard, from when the vault was a list of
    // cards) held the row, and a second copy of the tile's own marks - ticked, leaving -
    // which the page had to keep in step, and did not always (a leaving item's arrow was the card's, and no
    // tile showed it). One model for an item, wherever it lies.

    private VaultRow? _row;

    /// <summary>
    /// A vault item's tile, read from its row. The blob is stored without the id, so two captures of one item
    /// hash alike; the id is kept beside it and put back here, so what the tile shows - and the Edit page's
    /// facts - is the item's own id rather than a blank.
    /// </summary>
    /// <param name="statIdBits">
    /// The stat-id width the vault wrote its blobs at. Passed in rather than assumed: a blob is not
    /// self-describing, and read at the wrong width an item decodes into nonsense or throws
    /// (<see cref="ItemBlob.Restore"/>).
    /// </param>
    public static ItemTileViewModel OfVault(VaultRow row, int index, int statIdBits)
    {
        var item = ItemBlob.Restore(row.Blob, statIdBits);
        if (row.OriginId is { } id)
            item.Id = id;

        return new ItemTileViewModel(item, index) { _row = row };
    }

    /// <summary>
    /// The row as the vault holds it: what is withdrawn, moved or copied off a vault item is the row's blob.
    /// Asked only of a vault's tile (<see cref="IsVaults"/>) - an item a character holds has none.
    /// </summary>
    public VaultRow Row => _row ?? throw new InvalidOperationException("Not a vault item.");

    /// <summary>The row's key: what the vault knows this item by.</summary>
    public long Key => Row.Key;

    /// <summary>The item was written again in place (the Edit page): its row as it now is. The tile is the one being edited, and stays.</summary>
    public void Reread(VaultRow row) => _row = row;

    /// <summary>
    /// When a vault item's stay in the vault began (VaultRow.Added): said in its tooltip, in a section of its
    /// own at the foot, over the keys: the last section before the control hint. Null for an item that is not the vault's.
    /// </summary>
    public DateTimeOffset? Added => _row?.Added;

    /// <summary>
    /// The tooltip's line of it: "Stored 2026-10-09", in the day of here. "Stored", as the button that puts an
    /// item in the vault says and the strip after it (it was "Added").
    /// </summary>
    public string StoredLine => Added is { } at ? L($"Stored {at.ToLocalTime():yyyy-MM-dd}") : "";

    /// <summary>
    /// A vault item's tile, on a page of its folder: its tooltip says what a click does to a vault item - put
    /// on from the vault, withdrawn - not what it does to one the character holds (TipActions.Vault).
    /// </summary>
    public bool IsVaults => _row is not null;

    /// <summary>Set while this tile is the one the detail page is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMarked))]
    private bool _isSelected;

    /// <summary>
    /// Ticked while the inventory page picks several at once. Set by the page, which keeps the
    /// picks by item: every rebuild makes new tiles.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMarked))]
    private bool _isPicked;

    /// <summary>
    /// Going into the vault with the next save: the up arrow. Set by the page, as picks are -
    /// every rebuild makes new tiles.
    /// </summary>
    [ObservableProperty]
    private bool _isLeaving;

    /// <summary>Come out of the vault, to leave it with the next save: the down arrow.</summary>
    [ObservableProperty]
    private bool _isArriving;

    /// <summary>
    /// A gem, a rune or a jewel carried over this card would go into its socket, let go of now: framed in the
    /// landing's green. The vault's cards, which have no cells to light under them (the Items page lights the
    /// item's cells). Set by the page.
    /// </summary>
    [ObservableProperty]
    private bool _isSocketTarget;

    /// <summary>
    /// Left out by the search standing - dimmed, so what it found stands out where it lies. Set by the page, as picks are.
    /// </summary>
    [ObservableProperty]
    private bool _isSearchMiss;

    /// <summary>Framed as the one being looked at, which is how a picked tile is drawn too.</summary>
    public bool IsMarked => IsSelected || IsPicked;

    /// <summary>
    /// Every place this item's id is, when it is held more than once - the duplicate badge
    /// (<see cref="PD2SPCompanion.Shell.Services.Vault.DuplicateIndex"/>). Null otherwise, and whenever the
    /// play rule is off. Set by the page, as the other marks are.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDuplicate))]
    [NotifyPropertyChangedFor(nameof(DuplicateHint))]
    private IReadOnlyList<string>? _duplicatePlaces;

    public bool IsDuplicate => DuplicatePlaces is { Count: > 1 };

    public string DuplicateHint => DuplicatePlaces is { Count: > 1 } places
        ? L($"Duplicate: {string.Join(", ", places)}") + Environment.NewLine + L("LMB: show every copy")
        : "";

    /// <summary>The badge clicked: the search shows this item's copies, wherever they are.</summary>
    [RelayCommand]
    private void ShowCopies() => PD2SPCompanion.Shell.Services.Vault.DuplicateIndex.Current?.ShowCopies(Item);

    /// <summary>
    /// This item's quality colour, so a grid reads at a glance the way the game's does.
    /// Set and unique keep their colours even when unidentified, since an editor shows
    /// what is in the file rather than what the player can see yet.
    /// </summary>
    public IBrush RarityBrush => RarityPalette.Of(Item);

    /// <summary>
    /// The same quality, in a colour meant to sit behind the art. On the materials tab, the
    /// game's own cells instead: navy under what the tab holds, dark grey under what it does not
    /// - (12, 12, 29) and (29, 29, 29), sampled off an in-game screenshot.
    /// </summary>
    public IBrush WashBrush => Tally is { } tally
        ? tally > 0 ? HeldCell : EmptyCell
        : RarityPalette.WashOf(Item);

    private static readonly IBrush HeldCell = new SolidColorBrush(Color.FromRgb(12, 12, 29));
    private static readonly IBrush EmptyCell = new SolidColorBrush(Color.FromRgb(29, 29, 29));

    /// <summary>
    /// How strongly the quality colour washes the cell behind the sprite. Enough to tell
    /// a magic item from a plain one at a glance, and not so much that it colours the art. The
    /// materials tab's cells are the colour itself.
    /// </summary>
    public double QualityWash => Tally is null ? 0.26 : 1.0;

    /// <summary>
    /// An ethereal item is see-through in game, so it is here too. The sprite is the same
    /// graphic either way, so the difference has to be drawn rather than read from the art.
    /// A slot the materials tab holds none of is a shadow: in game its art is ~43 in brightness
    /// against 113 for a held one, over the grey cell - 0.2 is that.
    /// </summary>
    public double SpriteOpacity => Tally == 0 ? 0.2 : Item.IsEthereal ? 0.55 : 1.0;

    /// <summary>
    /// A count that belongs to the place the tile is shown rather than to the item - the
    /// materials tab, where the tile stands for how many of a thing the tab holds. At nought
    /// the tile is drawn dim and shows no number, the way the game's tab does. Null for every
    /// ordinary tile, which counts by its own stack.
    /// </summary>
    public int? Tally { get; }

    /// <summary>The materials-tab slot this tile stands for, or null for an ordinary tile.</summary>
    public int? TabSlot { get; }

    private readonly IReadOnlyList<string> _notes;

    /// <summary>
    /// Re-reads what the tile draws from the item it stands for.
    /// <para>
    /// Everything here is computed straight off the item, which notifies nothing - so an
    /// edit written into the item leaves the tile drawing the old one. Whoever writes the
    /// item says when it has finished.
    /// </para>
    /// </summary>
    public void Rebuilt()
    {
        // Built from the item rather than bound to it, so it is built again here. Notifying
        // alone handed back the same list the constructor made, and an edit that was kept
        // left the item it replaced on screen - the tooltip saying what it used to hold, and
        // the holes over the art still drawn for sockets it no longer has.
        // The picture as well as the words. An item can change which of its base's graphics
        // it wears, and its name can change outright - a runeword undone is a Short Staff
        // again - and both are read off the item, so both are read again.
        View = ItemPresenter.Describe(Item);
        Sprite = SpriteBitmap.From(SpriteLoader.Current?.Load(View.SpriteName, View.SpriteTint));

        // A list of its own, not the old one emptied: what stays bound to the lines - the Forge's holder -
        // is handed the same list otherwise, and goes on drawing what it drew.
        _facts = [];
        _apart = null;
        _described = false;
        _requirementsAt = -1;
        FillSockets();

        OnPropertyChanged(nameof(View));
        OnPropertyChanged(nameof(Sprite));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasFilledSockets));

        OnPropertyChanged(nameof(Sockets));
        OnPropertyChanged(nameof(HasSockets));
        OnPropertyChanged(nameof(SpriteOpacity));
        OnPropertyChanged(nameof(RarityBrush));
        OnPropertyChanged(nameof(WashBrush));
        OnPropertyChanged(nameof(BaseNote));
        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(SummaryFacts));
        OnPropertyChanged(nameof(SummaryFactsWithId));
        OnPropertyChanged(nameof(HasQuantity));
        OnPropertyChanged(nameof(QuantityText));
        _impossible = null;
        _impossibilities = null;
        OnPropertyChanged(nameof(IsImpossible));
    }

    /// <summary>
    /// Whether this item carries something the game could not have held - an affix no
    /// recipe puts on this base, a slot the file cannot store, the same affix twice.
    /// <para>
    /// Only the impossible, not everything the check finds. A tile is a glance: a number
    /// outside its range or a property the editor cannot explain is worth reading about in
    /// the panel, and marking the whole item for it would put a mark on half a stash.
    /// </para>
    /// <para>
    /// Asked of the item as it stands rather than as the panel has it staged - the tile
    /// belongs to the grid, which shows what the character holds.
    /// </para>
    /// </summary>
    public bool IsImpossible =>
        StatRowViewModel.FlagIllegitimate && (_impossible ??= Legitimacy.AnyImpossible(Item));

    /// <summary>
    /// The check's answer for the item as it stands, asked once: every grid tile draws by it
    /// (not the worn slots alone), and the check is some ten microseconds an item - nothing for a page,
    /// worth not paying again at every look. Forgotten when the item is read again (<see cref="Rebuilt"/>).
    /// </summary>
    private bool? _impossible;

    /// <summary>
    /// How many are in the stack, drawn on the tile the way the game draws it.
    /// <para>
    /// Only where the number means something. PD2 gave throwing weapons and arrows/bolts
    /// unlimited quantity: those bases still store a count, and the game shows none, so
    /// neither does this. See <see cref="Stacks"/>.
    /// </para>
    /// </summary>
    public bool HasQuantity => Tally is { } tally ? tally > 0 : Stacks.IsCounted(Item.Code);

    public string QuantityText => IsQuantityCapped
        ? QuantityCap.ToString()
        : (Tally ?? Stacks.Shown(Item.Quantity)).ToString();

    /// <summary>
    /// The most a tile writes out. A cell holds three digits and no more - five came out cut to
    /// their first three, so 65,535 read as 655 - and a count past this is drawn as this, in
    /// green, with the true figure in the tooltip. Only the materials tab counts that
    /// high; an item's own stack stops at 511.
    /// </summary>
    public const int QuantityCap = 999;

    public bool IsQuantityCapped => (Tally ?? Item.Quantity) > QuantityCap;

    /// <summary>
    /// What the item is. Built once, except for the charm note, which depends on where
    /// the item ended up and so is only known after a grid has placed it.
    /// <para>
    /// Which reading of a socketed item it is follows Shift (<see cref="PD2SPCompanion.Shell.Services.Generic.SocketView"/>):
    /// the game's, its sockets summed in, or - held - its own lines and each socket's beneath. One list for
    /// the tooltip, the trade frame and the Edit page's summary, so the three read alike.
    /// </para>
    /// </summary>
    public IReadOnlyList<ItemFact> Tooltip
    {
        get
        {
            var lines = Facts;
            if (HasFilledSockets)
            {
                PD2SPCompanion.Shell.Services.Generic.SocketView.Watch(this);
                if (PD2SPCompanion.Shell.Services.Generic.SocketView.Apart)
                {
                    lines = _apart ??= [.. ItemLines.Of(Item, View, out _, out _, Tally, _notes, _classCode, _met,
                                                        socketsApart: true, _etherealsWorn, _holderLevel).Select(ItemFact.From)];
                }
            }

            if (IsIdleCharm)
                lines = [.. lines, new ItemFact(IdleText(IdleCharm), Tone: ItemFact.Warning)];

            // Why the tile wears its red mark, under a rule: the mark said nothing, and nor did the tooltip.
            // The impossible alone, as the mark is for them alone.
            return IsImpossible ? [.. lines, new ItemFact("", Tone: ItemFact.Rule), .. Impossibilities] : lines;
        }
    }

    /// <summary>What the check finds the game could not have held, a line each - worked out once, when first shown.</summary>
    private List<ItemFact> Impossibilities => _impossibilities ??=
    [
        .. Legitimacy.Check(Item)
            .Where(finding => finding.Certainty is Certainty.Impossible)
            .Select(finding => new ItemFact(finding.Said, Tone: ItemFact.Warning)),
    ];

    private List<ItemFact>? _impossibilities;

    /// <summary>The lines as the game reads them, whatever key is held: for what takes its words from them (a grail card).</summary>
    public IReadOnlyList<ItemFact> Summed => Facts;

    /// <summary>The lines with the sockets apart, written when first asked for; let go when the item or its verdict changes.</summary>
    private List<ItemFact>? _apart;

    /// <summary>The line under the name in the item's summary: its code and its rarity.</summary>
    public string SummaryFacts =>
        string.Join("  ·  ", PD2SPCompanion.Shell.Services.Character.ItemFacts.Brief(this, withId: false));

    /// <summary>The same with the item's id, for an item that is in a file.</summary>
    public string SummaryFactsWithId =>
        string.Join("  ·  ", PD2SPCompanion.Shell.Services.Character.ItemFacts.Brief(this, withId: true));

    /// <summary>
    /// The item's lines, written the first time they are asked for - by <see cref="ItemLines"/>, which words
    /// an item as the game's own tooltip would and is what the search reads too.
    /// </summary>
    private List<ItemFact> Facts
    {
        get
        {
            if (!_described)
            {
                _described = true;
                _facts = [.. ItemLines.Of(Item, View, out _requirementsAt, out _requirementsCount,
                                          Tally, _notes, _classCode, _met, etherealsWorn: _etherealsWorn, holderLevel: _holderLevel).Select(ItemFact.From)];
            }

            return _facts;
        }
    }

    private List<ItemFact> _facts = [];
    private bool _described;

    // ---- requirements ---------------------------------

    private int _requirementsAt = -1, _requirementsCount;
    private string? _classCode;
    private (bool Level, bool Strength, bool Dexterity)? _met;
    private int? _etherealsWorn, _holderLevel;

    /// <summary>
    /// The character cannot use it: a requirement not met, counting what the gear gives. The
    /// tile is tinted red, as the game draws an unusable item. False until judged.
    /// </summary>
    [ObservableProperty] private bool _isUnusable;

    /// <summary>
    /// The page's verdict for the character it belongs with: which requirements are met, and
    /// anything else that stops its use (<paramref name="unusable"/> - a bow worn without arrows).
    /// Re-tones the tooltip's requirement lines and the tile. Null lets go of a verdict - no
    /// character. <paramref name="etherealsWorn"/> is what its holder wears of ethereal items, which a per-ethereal
    /// line says the total of (Purgatory's), and <paramref name="holderLevel"/> its level, which a per-level line says the
    /// total at; a new count or level words the lines again.
    /// </summary>
    public void Judge(string? classCode, (bool Level, bool Strength, bool Dexterity)? met, string? unusable = null,
                      int? etherealsWorn = null, int? holderLevel = null)
    {
        bool recount = _described && Item.StatLists.SelectMany(list => list.Stats).Any(stat => stat.Stat is { } name
            && ((etherealsWorn != _etherealsWorn && Requirements.PaidByEthereals(name, stat.Value, 0) is not null)
                || (holderLevel != _holderLevel && StatText.PerLevelShift(name) > 0)));
        _classCode = classCode;
        _met = met;
        _etherealsWorn = etherealsWorn;
        _holderLevel = holderLevel;
        _apart = null;
        IsUnusable = unusable is not null
                     || (met is { } verdict && !(verdict.Level && verdict.Strength && verdict.Dexterity));

        if (recount)
        {
            _described = false;
            OnPropertyChanged(nameof(Tooltip));
            OnPropertyChanged(nameof(SummaryFacts));
            OnPropertyChanged(nameof(SummaryFactsWithId));
            return;
        }

        // Not yet written, the lines are toned by this verdict when they are.
        if (!_described || _requirementsAt < 0 || _requirementsAt + _requirementsCount > _facts.Count)
            return;

        _facts.RemoveRange(_requirementsAt, _requirementsCount);
        var lines = ItemLines.Requirements(Item, _classCode, _met).Select(ItemFact.From).ToList();
        _facts.InsertRange(_requirementsAt, lines);
        _requirementsCount = lines.Count;

        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(SummaryFacts));
        OnPropertyChanged(nameof(SummaryFactsWithId));
    }


    public ItemTileViewModel(Item item, int index, int? cellX = null, int? cellY = null,
                             int? tally = null, int? tabSlot = null, IReadOnlyList<string>? notes = null)
    {
        _notes = notes ?? [];
        TabSlot = tabSlot;
        // First, so the tooltip built below counts what the tab holds, not the item made to
        // stand for it - which has to hold at least one, and said "Quantity: 1" for an empty slot.
        Tally = tally;
        Item = item;
        Index = index;
        CellX = cellX ?? item.X;
        CellY = cellY ?? item.Y;
        View = ItemPresenter.Describe(item);
        Sprite = SpriteBitmap.From(SpriteLoader.Current?.Load(View.SpriteName, View.SpriteTint));

        // The words wait for whoever reads them (Facts): a load builds a tile for every item in
        // every stash page, and their tooltips were 74 ms of it (measured).
        FillSockets();
    }

    /// <summary>
    /// The holes drawn over the art, and how wide the block of them is.
    /// <para>
    /// Empty sockets count: an item with three sockets and one gem shows three, so what is
    /// missing is as visible as what is there.
    /// </para>
    /// </summary>
    private void FillSockets()
    {
        int total = Math.Max(Item.TotalNumberOfSockets, Item.SocketedItems.Count);
        var sockets = new List<SocketViewModel>();

        for (int i = 0; i < total; i++)
        {
            var gem = i < Item.SocketedItems.Count ? Item.SocketedItems[i] : null;
            sockets.Add(new SocketViewModel(gem, Item));
        }

        Sockets = sockets;

        if (total > 0)
        {
            // Where the game puts each hole, by its number - the gem's place in the list, which is
            // its x in the save (SocketLayout). Placed on a box the item's own size, which the
            // views centre over the art: an equipped slot is bigger than the item in it.
            var spots = SocketLayout.Of(View.GridWidth, View.GridHeight, total);

            // A whole cell each, as the game draws them - smaller only where holes would overlap,
            // a 1x2 orb's three, so they stay on the item rather than over its neighbours.
            double closest = double.MaxValue;
            for (int a = 0; a < spots.Count; a++)
            {
                for (int b = a + 1; b < spots.Count; b++)
                    closest = Math.Min(closest, Math.Max(Math.Abs(spots[a].X - spots[b].X), Math.Abs(spots[a].Y - spots[b].Y)));
            }
            double size = Math.Min(SocketViewModel.FullSize, closest * CellSize);

            for (int i = 0; i < total; i++)
            {
                sockets[i].Size = size;
                sockets[i].Left = spots[i].X * CellSize - size / 2;
                sockets[i].Top = spots[i].Y * CellSize - size / 2;
            }
        }
    }
}
