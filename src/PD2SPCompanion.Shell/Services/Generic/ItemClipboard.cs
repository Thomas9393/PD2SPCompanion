using System;
using System.Collections.Generic;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Services.Generic;

/// <summary>What is held: nothing, things marked to be moved by the paste, or things a paste makes a second of.</summary>
public enum ClipKind { None, Cut, Copy }

/// <summary>
/// The clipboard of items and folders: Ctrl + C copies and Ctrl + V pastes, which most
/// users are accustomed to. Ctrl + X marks what is selected -
/// dimmed where it lies, and <b>nothing moves until the paste</b>, as Explorer's cut; Ctrl + C holds it to be
/// made a second of; Ctrl + V pastes where the pointer's page is: a cut is moved there, a copy duplicated
/// there - where the play mode duplicates. Esc lets a cut go. It took Duplicate's place, whose key Ctrl + V was.
/// <para>
/// It holds what the pages hand it and decides nothing: the character's items by the items themselves (a
/// rebuild makes new tiles of the same items), the vault's by their rows' keys, a folder by its id. The page
/// pasted on asks what is held and does the move with its own means - a drop, a store, a withdrawal.
/// One clipboard for the window, so a cut on one page pastes on the other.
/// </para>
/// </summary>
public sealed class ItemClipboard
{
    public static ItemClipboard Shared { get; } = new();

    private readonly HashSet<Item> _items = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<long> _cards = [];

    public ClipKind Kind { get; private set; }

    /// <summary>The character's items held - worn, in a grid, on a shared page.</summary>
    public IReadOnlyCollection<Item> Items => _items;

    /// <summary>The vault's items held, by their rows' keys.</summary>
    public IReadOnlyCollection<long> Cards => _cards;

    /// <summary>
    /// The vault folders cut, to be moved - with all in them - into the folder pasted in. Several, and with
    /// items of the vault's beside them (<see cref="Cards"/>), since a folder is a tile of its folder's sheet,
    /// selected among the items: what is selected there is cut together.
    /// </summary>
    public IReadOnlyCollection<long> Folders => _folders;

    private readonly HashSet<long> _folders = [];

    /// <summary>
    /// A gem, a rune or a jewel cut in its socket (the Edit page's Sockets section), and the item it is
    /// in: turned out by the paste, to where the paste is made. It is no item of a grid, so it is no
    /// <see cref="Items"/>' - and it comes out only by the section's own means (<see cref="EjectSocketed"/>),
    /// which undo a runeword it was a rune of.
    /// </summary>
    public Item? Socketed => _socketed.Count > 0 ? _socketed[0] : null;

    /// <summary>All of them, where several sockets were selected and cut together, in the row's order.</summary>
    public IReadOnlyList<Item> SocketedGems => _socketed;

    private readonly List<Item> _socketed = [];

    public Item? SocketHost { get; private set; }

    /// <summary>
    /// Turns the cut gems out of their item, the first onto a cell or - with none - where what arrives lands: the
    /// character's Edit page's to do, which says so here as it is made. Answers the items as they lie loose now - in
    /// Season 14 new ones (ItemForge.Loose), which a paste on the vault stores (a stored skull was left on the
    /// character) - or null where that page no longer shows the item.
    /// </summary>
    public Func<Item, IReadOnlyList<Item>, GridCell?, IReadOnlyList<Item>?>? EjectSocketed { get; set; }

    /// <summary>
    /// What is held, put into an item's sockets, straight to an empty
    /// socket: whether it would go in, and putting it in - the Items page's to say and do, which knows the
    /// grids a cut lies in and how one thing is socketed. Asked by the Edit page's Sockets section.
    /// </summary>
    public Func<ItemTileViewModel, bool>? FitsSockets { get; set; }

    public Func<ItemTileViewModel, bool>? PasteIntoSockets { get; set; }

    private readonly List<Func<bool>> _takers = [];

    /// <summary>
    /// A place that takes a paste itself while the pointer is over it - the Edit page's row of sockets: asked
    /// first by Ctrl + V, whichever page hears the key (two listeners on the window hear a key in the order they
    /// came to it, which is no order to lean on).
    /// </summary>
    public void AddPasteTaker(Func<bool> taker) => _takers.Add(taker);

    public void RemovePasteTaker(Func<bool> taker) => _takers.Remove(taker);

    /// <summary>True where a place under the pointer took the paste.</summary>
    public bool PasteUnderPointer() => Array.Exists(_takers.ToArray(), taker => taker());

    public bool IsEmpty => Kind == ClipKind.None;

    public int Count => _items.Count + _cards.Count + _folders.Count + _socketed.Count;

    /// <summary>Held, or let go: the tiles and rows that are cut draw themselves again.</summary>
    public event Action? Changed;

    public void HoldItems(ClipKind kind, IEnumerable<Item> items)
    {
        Reset();
        _items.UnionWith(items);
        Settle(kind);
    }

    public void HoldCards(ClipKind kind, IEnumerable<long> keys)
    {
        Reset();
        _cards.UnionWith(keys);
        Settle(kind);
    }

    /// <summary>A folder is cut only: a second of a folder and all in it is nothing the vault makes.</summary>
    public void HoldFolder(long folder) => HoldVault([], [folder]);

    /// <summary>The vault's items and folders cut together: what was selected on a folder's sheet.</summary>
    public void HoldVault(IEnumerable<long> keys, IEnumerable<long> folders)
    {
        Reset();
        _cards.UnionWith(keys);
        _folders.UnionWith(folders);
        Settle(ClipKind.Cut);
    }

    public void HoldSocketed(Item host, Item gem) => HoldSocketed(host, [gem]);

    public void HoldSocketed(Item host, IEnumerable<Item> gems)
    {
        Reset();
        _socketed.AddRange(gems);
        SocketHost = _socketed.Count > 0 ? host : null;
        Settle(ClipKind.Cut);
    }

    public void Clear()
    {
        if (IsEmpty)
            return;

        Reset();
        Changed?.Invoke();
    }

    /// <summary>The character's items let go, the vault's kept: the character was closed, and its items with it.</summary>
    public bool DropItems()
    {
        if (_items.Count == 0 && _socketed.Count == 0)
            return false;

        Clear();
        return true;
    }

    /// <summary>
    /// The vault's cards and folders let go, the character's items kept: another vault was opened, whose rows and folders
    /// are numbered from 1 again, so what is held by number would name other items there. Whether any were held.
    /// </summary>
    public bool DropVault()
    {
        if (_cards.Count == 0 && _folders.Count == 0)
            return false;

        Clear();
        return true;
    }

    private void Reset()
    {
        _items.Clear();
        _cards.Clear();
        _folders.Clear();
        _socketed.Clear();
        SocketHost = null;
        Kind = ClipKind.None;
    }

    private void Settle(ClipKind kind)
    {
        Kind = Count == 0 ? ClipKind.None : kind;
        Changed?.Invoke();
    }

    /// <summary>Whether a tile's item is cut: drawn dimmed where it lies, until it is pasted or let go.</summary>
    public bool IsCut(ItemTileViewModel tile) =>
        Kind == ClipKind.Cut && (tile.IsVaults ? _cards.Contains(tile.Key) : _items.Contains(tile.Item));

    public bool IsCut(long? folder) => Kind == ClipKind.Cut && folder is { } id && _folders.Contains(id);

    public bool IsCutSocketed(Item? gem) => Kind == ClipKind.Cut && gem is not null && _socketed.Exists(each => ReferenceEquals(each, gem));

    /// <summary>What is held, in a tooltip's words: "3 items cut", "1 item copied", "Folder cut". Empty with nothing.</summary>
    public string Says => Kind == ClipKind.None ? ""
        : _folders.Count == 1 && Count == 1 ? L("Folder cut")
        : _folders.Count > 0 && Count == _folders.Count ? L($"{Count} folders cut")
        : _folders.Count > 0 ? L($"{Count} cut")
        : Kind == ClipKind.Cut ? Plural(Count, "1 item cut", $"{Count} items cut")
        : Plural(Count, "1 item copied", $"{Count} items copied");
}
