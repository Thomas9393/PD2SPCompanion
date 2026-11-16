using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A group of the window's search list - the open character, the vault, the other characters, the grail - over
/// its places: its name, how many it found, and whether it is unfolded. Clicked, it folds
/// (<see cref="QuickSearchViewModel"/>).
/// </summary>
public sealed record FoundGroup(string Fold, string Name, int Found, bool IsOpen, System.Windows.Input.ICommand Toggle)
{
    public string CountText => Found.ToString();
}

/// <summary>A row of a search's list that can be gone to and stepped onto: an item, or a place in the editor.</summary>
public interface IFound
{
    /// <summary>What it is across two runs of a search, so the one gone to stays marked.</summary>
    object Id { get; }

    /// <summary>The find last gone to: marked in the list.</summary>
    bool IsCurrent { get; set; }
}

/// <summary>
/// A place in the editor the window's search can go to: a setting, by its name on the Settings page, or a
/// section - a page, a bookmark, a fold's heading - by its own: a mixed search. Read off the pages themselves, as they are built.
/// </summary>
/// <param name="IsSetting">A setting; else a section.</param>
/// <param name="Name">What the page calls it.</param>
/// <param name="Where">The page it is on.</param>
/// <param name="Go">Shows its page and brings it into sight, unfolded.</param>
public sealed record SearchTarget(bool IsSetting, string Name, string Where, Action Go);

/// <summary>A setting or a section found, as a row of the search's list: its name, the page it is on, the way to it.</summary>
public sealed partial class FoundTarget(SearchTarget target) : ObservableObject, IFound
{
    public object Id { get; } = (target.IsSetting, target.Where, target.Name);

    public string Name => target.Name;

    public string Where => target.Where;

    public Action Jump => target.Go;

    [ObservableProperty] private bool _isCurrent;
}

/// <summary>A place holding finds - a container of the character's, a folder of the vault's - over its finds in the list.</summary>
public sealed record FoundPlace(string Name, int Found, IReadOnlyList<FoundItem> Items)
{
    public string CountText => Found.ToString();
}

/// <summary>
/// One find in the search's list: the item as its tile - its art, its name in its rarity's color, its tooltip -
/// what tells it from another of its name, where in its place it lies, and the way to it.
/// <paramref name="id"/> is what the find is across reloads: the vault's key, or the character's item itself.
/// </summary>
public sealed partial class FoundItem(object id, ItemTileViewModel tile, string detail, string where, bool inVault, Action jump)
    : ObservableObject, IFound
{
    public object Id { get; } = id;

    public ItemTileViewModel Tile { get; } = tile;

    public string Name => Tile.Name;

    /// <summary>The base it is built on, and its numbers under the properties the search asks for.</summary>
    public string Detail { get; } = detail;

    public bool HasDetail => Detail.Length > 0;

    /// <summary>
    /// Where it lies, at the right end of the row's second line: a place in its container, or - in the
    /// window's search box, whose short list has no headings over its finds - the container or the vault
    /// folder itself. Empty where a heading over the row says it all.
    /// </summary>
    public string Where { get; } = where;

    /// <summary>The row has a second line: something tells it apart, or it says where it lies.</summary>
    public bool HasSecondLine => Detail.Length > 0 || Where.Length > 0;

    /// <summary>
    /// Where it lies, in full, where the row says less: a vault find's whole path, "Vault\Crafting Bases\Weapons",
    /// under a heading that already says "Vault". Null where <see cref="Where"/> is the whole of it.
    /// </summary>
    public string? FullWhere { get; init; }

    /// <summary>
    /// The row in full, for its tooltip while it cuts its name or its place short: the name, and under it the place, whole.
    /// </summary>
    public string Tip => (FullWhere ?? Where) is { Length: > 0 } place ? Name + Environment.NewLine + place : Name;

    public bool InVault { get; } = inVault;

    /// <summary>Shows its place, brings it into sight and selects it.</summary>
    public Action Jump { get; } = jump;

    /// <summary>
    /// Its right-click menu on the Search page, made as it is asked for by the page the find lies on: "Go to its
    /// place", then what can be done to the item from here. Null for a find with none - one on another character.
    /// </summary>
    public Func<ActMenu>? Menu { get; init; }

    /// <summary>The find last gone to: marked in the list.</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>
    /// The save it lies in, where that is not the character open - a find of the window's search on another
    /// character. It cannot be gone to without loading that character, so its row asks first
    /// (<see cref="IsAsking"/>); null for a find that can be gone to at once.
    /// </summary>
    public string? OwnerPath { get; init; }

    /// <summary>The other character's name, for the row's question.</summary>
    public string Owner { get; init; } = "";

    /// <summary>
    /// Its row is greyed and asks, over itself, whether to load the character it lies on - a tick and a cross
    /// (no popup, no button on every row: the item card greys out with a new layer on
    /// top saying this is on another character, want to switch?).
    /// </summary>
    [ObservableProperty] private bool _isAsking;

    /// <summary>The row's question while it asks.</summary>
    public string AskText => L($"Open {Owner}?");

    /// <summary>The tick and the cross of a row that asks: the list's own, handed to the row it made.</summary>
    public System.Windows.Input.ICommand? Accept { get; init; }

    public System.Windows.Input.ICommand? Decline { get; init; }
}

/// <summary>The list's last line, where a search finds more than the list holds.</summary>
public sealed record FoundMore(string Text);

/// <summary>
/// The last line of a group of the window's search box that found more than it lists, and the way to them all
/// (the search bookmark with the same query, the same for grails): "See all 36 results" - under the items to the Search page, under a grail tab to that tab. A row as a find is - marked
/// by the arrows, gone to by Enter or a click.
/// </summary>
public sealed partial class FoundSeeAll(object id, string text, Action go) : ObservableObject, IFound
{
    public object Id { get; } = id;

    public string Text { get; } = text;

    /// <summary>Shows the page that lists them all, asked what the box was.</summary>
    public Action Go { get; } = go;

    [ObservableProperty] private bool _isCurrent;
}

/// <summary>
/// What the pages know of the standing search's finds, and the two tools that act on them together - for the
/// Search page (<see cref="SearchWindowViewModel"/>): how many the character holds and how many the vault,
/// that they changed, and what a row says of its item.
/// <para>
/// It was the list itself (one floating panel, a row a find under its place). Since the
/// Search page lists what its own finder finds (<see cref="QuickSearchViewModel.IsPanel"/>), the pages went on
/// making that list beside it - the vault's part of it 500 item cards - for no view to show:
/// it was taken out. The pages count; the finder lists, once.
/// </para>
/// </summary>
public sealed partial class SearchResultsViewModel : ObservableObject
{
    private readonly VaultFilter _filter;
    private readonly ISFXManager? _sfxManager;

    public SearchResultsViewModel(VaultFilter filter, ISFXManager? sfxManager)
    {
        _filter = filter;
        _sfxManager = sfxManager;
    }

    /// <summary>The most finds of the vault's the list holds; past it, a line says how many more there are.</summary>
    public const int MostListed = 500;

    private int _characterFound, _vaultFound;

    /// <summary>How many of the character's items the search finds, from the inventory page: every container's, what is worn, the mercenary's.</summary>
    public void SetCharacter(int found)
    {
        _characterFound = found;
        Said();
    }

    /// <summary>How many of the vault's, from the vault page: every folder's.</summary>
    public void SetVault(int found)
    {
        _vaultFound = found;
        Said();
    }

    /// <summary>
    /// A page read its finds again - the search changed, an item moved, another character was opened, a find
    /// was gone to: the Search page finds again. Said every time; a list that comes out the same is left as it
    /// lies, where it was scrolled to (the finder's own check).
    /// </summary>
    private void Said()
    {
        Found = _characterFound + _vaultFound;
        OnPropertyChanged(nameof(HasVaultFinds));
        FindsChanged?.Invoke();
    }

    /// <summary>How many the search finds, the character's and the vault's together.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFinds))]
    [NotifyPropertyChangedFor(nameof(CanSelectFound))]
    private int _found;

    /// <summary>Whether Select found has anything to select.</summary>
    public bool CanSelectFound => HasFinds;

    // ---- what a row says of its item ------------------------------------------------------------------

    /// <summary>The properties the search asks for, each with the name the form gives it.</summary>
    public IReadOnlyList<(string Label, PropertyWant Want)> Wants =>
        [.. _filter.Properties.Select(row => (row.Option.Label, row.Want()))];

    /// <summary>
    /// A row's second line: the base an item's own name hides, then its number under each property asked
    /// for - what tells five charms of one name apart.
    /// </summary>
    public string Detail(ItemTileViewModel tile, IReadOnlyList<double?>? values)
    {
        var parts = new List<string>();
        if (tile.View.BaseName is { Length: > 0 } named && !named.Equals(tile.Name, StringComparison.Ordinal))
            parts.Add(named);

        var wants = Wants;
        for (int i = 0; values is not null && i < wants.Count && i < values.Count; i++)
        {
            if (values[i] is { } value)
                parts.Add($"{wants[i].Label} {(value % 1 == 0 ? ((long)value).ToString() : value.ToString("0.#"))}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>A page read its finds again: the search's page finds again.</summary>
    public event Action? FindsChanged;

    // ---- acting on the finds -----------------
    // The list went to one find at a time. Two acts on them all: the finds that lie in what the pages show
    // become the selection, to carry, store or delete as any selection; and the vault's finds, wherever they
    // lie, go to a folder chosen from the list the vault's Move button opens. The pages do both - the list
    // only asks (the main view model joins them: it knows which pages are in sight).

    /// <summary>Whether there is anything to select.</summary>
    public bool HasFinds => Found > 0;

    /// <summary>Whether the vault holds any of the finds: Move found to folder is theirs.</summary>
    public bool HasVaultFinds => _vaultFound > 0;

    /// <summary>The finds in sight are to be selected.</summary>
    public event Action? SelectFoundRequested;

    /// <summary>The vault's finds are to be moved to a folder.</summary>
    public event Action? MoveFoundRequested;

    [RelayCommand]
    private void SelectFound()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        SelectFoundRequested?.Invoke();
    }

    [RelayCommand]
    private void MoveFound()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        MoveFoundRequested?.Invoke();
    }
}
