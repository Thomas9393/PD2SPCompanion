using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using System.IO;


using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One runeword in the wizard's list. The table keeps some runewords as several rows - Spirit in
/// a sword and in a shield, with different properties - and the list shows each once, with
/// every base it can be made in.
/// </summary>
/// <param name="Bases">
/// What it is made in: "Sword, Axe" - and with the bases counted, how many of each the vault's Bases folder
/// holds: "Sword (0), Axe (2)".
/// </param>
/// <param name="Count">How many can be made, runes and bases together; null where the bases are not counted.</param>
public sealed record RunewordRow(
    string Name, string Runes, string Bases, IReadOnlyList<string> Categories,
    int HighestRune, bool CanMake, string Missing, IReadOnlyList<Runewords.Word> Words, int? Count = null)
{
    /// <summary>The name, with how many can be made after it where the bases are counted: "Infinity (2)".</summary>
    public string Title => Count is { } made ? L($"{Name} ({made})") : Name;

    /// <summary>What cannot be made yet is shown, but dimmed.</summary>
    public double Opacity => CanMake ? 1.0 : 0.4;

    public bool HasMissing => Missing.Length > 0;

    private IReadOnlyList<ItemFact>? _bonus;

    /// <summary>
    /// What the runeword grants, for the row's tooltip, shown as an item's tooltip is but for the runeword's bonus only:
    /// the lines an item's tooltip would print, as ranges. A word the
    /// table keeps several times with different properties - Spirit in a sword and in a shield - says each
    /// under the bases it is made in, a rule between. Worked out when first asked: a tooltip is.
    /// </summary>
    public IReadOnlyList<ItemFact> Bonus => _bonus ??= Said();

    private IReadOnlyList<ItemFact> Said()
    {
        var kinds = Words
            .Select(word => (
                Bases: string.Join(", ", word.Types.Select(ItemTypes.ShownNameOf).Distinct()),
                Lines: new RunewordChoiceRow(word, false).Lines))
            .GroupBy(kind => string.Join("\n", kind.Lines))
            .Select(same => (Bases: string.Join(", ", same.Select(kind => kind.Bases).Distinct()), same.First().Lines))
            .ToList();

        var said = new List<ItemFact>();
        foreach (var (bases, lines) in kinds)
        {
            if (said.Count > 0)
                said.Add(new ItemFact("", Tone: ItemFact.Rule));
            if (kinds.Count > 1)
                said.Add(new ItemFact(bases));

            said.AddRange(lines.Select(line => new ItemFact(line, Tone: ItemFact.Magic)));
        }

        return said;
    }
}

/// <summary>
/// The wizard list's filter: one of the wiki's categories, or all of them, and whether to show
/// only what the runes on hand can make. Opened from the same menu button the sort orders use.
/// </summary>
public sealed class RunewordFilter : MenuChoice
{
    private static readonly string Everything = L("All runewords");
    private static readonly string ShowAll = L("Show all"), CanMakeOnly = L("Only what I can make");

    public RunewordFilter()
    {
        Options =
        [
            new SortOption(Everything, isDirection: false, descending: false, startsGroup: false),
            .. Runewords.Categories.Select(category =>
                new SortOption(category, isDirection: false, descending: false, startsGroup: false)),
            // The second group reuses the option's direction flag to mean "this is the
            // show-what line", which is all a second group needs.
            new SortOption(ShowAll, isDirection: true, descending: false, startsGroup: true),
            new SortOption(CanMakeOnly, isDirection: true, descending: true, startsGroup: false),
        ];

        MarkCurrent();
    }

    public override IReadOnlyList<SortOption> Options { get; }

    /// <summary>The category shown, or null for every one.</summary>
    public string? Category { get; private set; }

    public bool OnlyMakeable { get; private set; }

    public override string Hint =>
        L("Filter") + Environment.NewLine + Shown(Category is { } category ? L(category) : Everything);

    private string Shown(string category) => OnlyMakeable ? L($"{category}, only what I can make") : category;

    public event Action? Changed;

    public override void Pick(SortOption option)
    {
        if (option.IsDirection)
            OnlyMakeable = option.Descending;
        else
            Category = option.Label == Everything ? null : option.Label;

        MarkCurrent();
        Changed?.Invoke();
    }

    private void MarkCurrent()
    {
        foreach (var option in Options)
        {
            option.IsCurrent = option.IsDirection
                ? option.Descending == OnlyMakeable
                : option.Label == (Category ?? Everything);
        }

        OnPropertyChanged(nameof(Hint));
    }
}

/// <summary>
/// The rune wizard on the Extra page: the runes the shared stash's materials tab holds, and what
/// they can make.
/// <para>
/// Counted where a rune can be spent from: the character's containers (inventory, cube, its own
/// stash), the shared stash's pages and its materials tab. A rune in a socket is already spent,
/// and is never counted - it sits inside its item, not in a container.
/// </para>
/// </summary>
public sealed partial class RuneWizardViewModel : ObservableObject
{
    private readonly CharacterSession _session;
    private readonly SharedStashSession _stash;
    private readonly EditorSettings? _settings;
    private readonly VaultService? _vaults;

    public RuneWizardViewModel(CharacterSession session, SharedStashSession stash, EditorSettings? settings = null,
                               VaultService? vaults = null)
    {
        _session = session;
        _stash = stash;
        _settings = settings;
        _vaults = vaults;
        Filter.Changed += ShowRows;
    }

    /// <summary>
    /// Whether a runeword's bases are counted with its runes (a toggle; the crafting wizard showed the gap in this
    /// one, which counted none): the white bases in the vault's Bases folder with as many empty sockets as the word has runes.
    /// Counted, a row says how many of each base there are and, after its name, how many can be made; a word
    /// is makeable with its runes and a base. Not counted, by its runes alone, as it was. Kept in the settings.
    /// </summary>
    public bool CountsBases
    {
        get => _settings?.RuneWizardCountsBases ?? false;
        set
        {
            if (_settings is null || _settings.RuneWizardCountsBases == value)
                return;

            _settings.RuneWizardCountsBases = value;
            OnPropertyChanged();
            Refresh();
        }
    }

    [RelayCommand]
    private void ToggleBases() => CountsBases = !CountsBases;

    /// <summary>
    /// The toggle's tooltip: what it does, then - as the crafting wizard's mark says of its own folder - where
    /// the bases are counted from and the way they get there. Sorted storing sends white
    /// items to that folder.
    /// </summary>
    public string BasesTip =>
        L("Count bases")
        + Environment.NewLine + L($"Bases counted from vault folder {_folders}")
        + Environment.NewLine + L("Fast inventory storing with Ctrl puts bases there");

    /// <summary>The folders the bases are counted from, by the names they wear now: said by the toggle.</summary>
    private string _folders = SmartSort.BasesFolder;

    /// <summary>Folded away, and kept so, as the crafting wizard under it is.</summary>
    [ObservableProperty] private bool _isOpen = FoldMemory.IsOpen(nameof(RuneWizardViewModel) + nameof(IsOpen));

    partial void OnIsOpenChanged(bool value) => FoldMemory.Keep(nameof(RuneWizardViewModel) + nameof(IsOpen), value);

    [RelayCommand]
    private void ToggleOpen() => IsOpen = !IsOpen;

    /// <summary>The runes, laid out as the materials tab lays them out, counts only.</summary>
    public MaterialsViewModel Runes { get; } = new();

    public RunewordFilter Filter { get; } = new();

    public ObservableCollection<RunewordRow> Rows { get; } = [];

    private List<RunewordRow> _all = [];

    [ObservableProperty] private string _summary = "";

    /// <summary>Counts everything again. Called whenever the character or the stash changes.</summary>
    public void Refresh()
    {
        var read = MaterialsTab();
        var tab = read?.Count;
        string era = read?.Era ?? "";

        var stock = tab is null ? new int[34] : Runewords.Stock([], tab);

        // The same runes as last time, the same answer: the page is visited far more often than the
        // runes change, and rebuilding every row on each visit was most of its 30 ms and a list
        // redrawn in front of the player.
        // And the same bases: the rows of the vault's folder as they stand, read as items only when they change.
        // Where the sorter delivers white bases, by its own rules and its own links: a folder renamed or moved
        // is still the one: the wizards and the sorter looked at different things.
        var roots = SmartSort.BasesRoots(SmartSort.Current());
        string folders = string.Join(", ", roots.Select(root => _vaults?.Store?.DeliveryName(root) ?? root).Distinct());
        if (folders != _folders)
        {
            _folders = folders;
            OnPropertyChanged(nameof(BasesTip));
        }

        var bases = CountsBases && _vaults?.Store is { } store
            ? roots.SelectMany(store.RowsUnder).DistinctBy(row => row.Key).ToList()
            : null;
        string kept = bases is null ? "-" : string.Join(',', bases.Select(row => $"{row.Key}:{row.Blob.Length}:{row.OriginId}"));

        string held = $"{PD2SPCompanion.Core.GameFiles.InstalledTables.Generation}|{tab is null}|{era}|{string.Join(',', stock)}|{kept}";
        if (held == _held && _all.Count > 0)
            return;
        _held = held;

        // Which words each base can be made into: as many runes as it has sockets, all of them empty, a type
        // the word takes and none it excludes - the runeword builder's own rule. A made runeword is no base.
        var fits = bases?
            .Select(row => ItemBlob.Restore(row.Blob, VaultService.StatIdBits))
            .Where(item => !item.IsRuneword)
            .Select(item => (Lineage: ItemTypes.LineageOfBase(item.Code), Words: RunewordBuilder.WordsFor(item)))
            .Where(fit => fit.Words.Count > 0)
            .ToList();

        IReadOnlyList<string> Where(int rune) => stock[rune] > 0 ? [L($"Materials tab: {stock[rune]}")] : [];

        if (tab is null)
            Runes.Clear();
        else
            Runes.Refresh(slot => stock[slot], era, onlyRunes: true, notesOf: Where);

        _all = Runewords.All
            .GroupBy(word => (word.Name, Runes: string.Join(",", word.Runes)))
            .Select(rows => Row(rows.ToList(), stock, fits))
            // Lowest first: what the runes on hand are likeliest to make comes first.
            .OrderBy(row => row.HighestRune)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ShowRows();
    }

    /// <summary>
    /// The shared stash's materials tab, and only it:
    /// the open stash's while a character is loaded, else the file as it is on disk - so the wizards answer
    /// with no character open, the game running. A slot's count, and the era of the stash's items; null with
    /// no stash to read.
    /// </summary>
    public (Func<int, int> Count, string Era)? MaterialsTab()
    {
        if (_stash is { IsLoaded: true })
            return (_stash.MaterialCount, _stash.ItemEra ?? "");

        return Snapshot() is { } onDisk ? (onDisk.MaterialCount, "") : null;
    }

    /// <summary>The runes the rows were last worked out from: what <see cref="Refresh"/> compares.</summary>
    private string _held = "";

    /// <summary>The shared stash file as last read, and when it was written then.</summary>
    private (string Path, DateTime At, SharedStash Stash)? _snapshot;

    /// <summary>
    /// The shared stash on disk - the mode's the vault is set to, with no character to say - read
    /// again only when it has changed. Null when there is none, or it will not read.
    /// </summary>
    private SharedStash? Snapshot()
    {
        try
        {
            string folder = _session.SaveFolder;
            if (folder.Length == 0 || _session.EnsureGame() is null)
                return null;

            string path = Path.Combine(folder, _settings?.VaultHardcore == true
                ? SharedStashSession.HardcoreFile
                : SharedStashSession.SoftcoreFile);
            if (!File.Exists(path))
                return null;

            var at = File.GetLastWriteTimeUtc(path);
            if (_snapshot is { } known && known.Path == path && known.At == at)
                return known.Stash;

            var read = SharedStash.Read(path);
            _snapshot = (path, at, read);
            return read;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static RunewordRow Row(
        IReadOnlyList<Runewords.Word> rows, int[] stock,
        IReadOnlyList<(IReadOnlyList<string> Lineage, IReadOnlyList<Runewords.Word> Words)>? fits)
    {
        var word = rows[0];

        // Named as a player names them, and each once: the claws are two types of the table's.
        var kinds = rows.SelectMany(each => each.Types)
            .GroupBy(ItemTypes.ShownNameOf)
            .Select(same => (Name: same.Key, Types: same.Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();

        // The bases this word can be made in, where they are counted, and of them how many each kind.
        var own = fits?.Where(fit => fit.Words.Any(rows.Contains)).ToList();
        int? made = own is null ? null : Math.Min(Runewords.Sets(word, stock), own.Count);

        var missing = word.Runes
            .GroupBy(Runewords.RuneNumber)
            .Where(need => stock[need.Key] < need.Count())
            .Select(need => Runewords.RuneName(need.First()))
            .ToList();

        return new RunewordRow(
            Runewords.DisplayName(word),
            Runewords.Recipe(word),
            string.Join(", ", kinds.Select(kind => own is null
                ? kind.Name
                : L($"{kind.Name} ({own.Count(fit => kind.Types.Any(type => fit.Lineage.Contains(type, StringComparer.OrdinalIgnoreCase)))})"))),
            [.. Runewords.Categories.Where(category => rows.Any(each => Runewords.CategoriesOf(each).Contains(category)))],
            word.Runes.Max(Runewords.RuneNumber),
            made is { } count ? count > 0 : Runewords.CanMake(word, stock),
            missing.Count == 0 ? "" : "needs " + string.Join(", ", missing),
            rows,
            made);
    }

    private void ShowRows()
    {
        Rows.Clear();

        foreach (var row in _all.Where(row =>
                     (Filter.Category is null || row.Categories.Contains(Filter.Category))
                     && (!Filter.OnlyMakeable || row.CanMake)))
        {
            Rows.Add(row);
        }

        int makeable = _all.Count(row => row.CanMake);
        Summary = L($"{makeable} of {_all.Count} can be made");
    }
}
