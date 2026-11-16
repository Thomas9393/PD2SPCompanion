using D2SLib.Model.Save;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// The vault's search over the character's own items. The items are put into a vault of their own, in memory, through the vault's own
/// capture and projection, and the same query answers: one set of search rules for the inventory
/// and the vault, so the two can never disagree about what a search finds.
/// <para>
/// The vault's folders are the vault's alone, and are left out of the question asked here.
/// </para>
/// </summary>
public sealed class InventorySearch : IDisposable
{
    private readonly VaultFilter _filter;
    private readonly ItemIndex _index;
    private readonly bool _ownsIndex;

    /// <summary>
    /// <paramref name="shared"/>: an index others ask too - the open character's (<see cref="ItemIndex.OfOpenFiles"/>),
    /// which the window's box, the Search page and the Items page's own marking each wrote for themselves, the
    /// same items three times after every change. None, and the search keeps one of its own.
    /// </summary>
    public InventorySearch(VaultFilter filter, ItemIndex? shared = null)
    {
        _filter = filter;
        _index = shared ?? new ItemIndex();
        _ownsIndex = shared is null;
    }

    /// <summary>The standing search, less what only the vault's items can answer.</summary>
    private VaultQuery Query() => _filter.Query() with { Folders = null };

    /// <summary>Whether a search stands that the inventory can be asked.</summary>
    public bool IsActive => Query().IsFiltering;

    /// <summary>
    /// Which of <paramref name="items"/> the standing search keeps, by reference; null while
    /// nothing is searched for. An item that cannot be read into the index is kept out.
    /// <para>
    /// <b>The index is kept where the caller can say the items are as they were</b>: writing a
    /// character's ninety items into it was most of a search, and it was done again at every letter - and once
    /// for each character on disk (a trace on thirteen characters: 130 of a run's 160 ms). <paramref
    /// name="stamp"/> is what stands for "nothing in them has changed": a save set's count of changes for the
    /// open files (<c>CharacterSession.ItemsStamp</c>), a read save's own record for one on disk
    /// (<c>SavedItems.Held</c>). With the same stamp, the same items by reference and the same tables as the last
    /// time, only the question is asked. With none, the index is written anew, as it always was.
    /// </para>
    /// </summary>
    public HashSet<Item>? Matching(IEnumerable<Item> items, object? stamp = null)
    {
        var query = Query();
        if (!query.IsFiltering)
            return null;

        _index.Hold(items, stamp);
        return _index.Answering(query);
    }

    /// <summary>
    /// The index written for these items ahead of any search, where it does not stand already: a search then
    /// only asks. Nothing is asked here, so no search need stand.
    /// </summary>
    public void Prepare(IEnumerable<Item> items, object stamp) => _index.Hold(items, stamp);

    /// <summary>How many times the index was written: what a test counts to see it kept.</summary>
    public int Written => _index.Written;

    /// <summary>
    /// What each of these items holds under each property the search asks for, in the search's order - null
    /// where an item has none. Of the items the last <see cref="Matching"/> read; none where no property is asked.
    /// </summary>
    public IReadOnlyDictionary<Item, double?[]> ValuesOf(IEnumerable<Item> items) =>
        Query().Properties is { Count: > 0 } wants ? _index.ValuesOf(items, wants) : new Dictionary<Item, double?[]>(ReferenceEqualityComparer.Instance);

    public void Dispose()
    {
        if (_ownsIndex)
            _index.Dispose();
    }
}

/// <summary>
/// Items in a scratch vault of their own, in memory, to be asked as the vault is (<see cref="InventorySearch"/>):
/// written when the items change and kept while they stand. One for the open files is shared by every search of
/// them (<see cref="OfOpenFiles"/>).
/// </summary>
public sealed class ItemIndex : IDisposable
{
    /// <summary>
    /// The open character's and its shared stash's items: one index for the window's box, the Search page and
    /// the Items page's marking, which ask of the same items (VP_ViewModel_Inv_L.AllTiles) under the same
    /// stamp. Held for as long as the program runs.
    /// </summary>
    public static ItemIndex OfOpenFiles { get; } = new();

    private PD2SPCompanion.Core.Collection.Vault? _vault;

    /// <summary>What the index was last written from: the items asked (those that could not be read among them), their stamp, the tables.</summary>
    private readonly HashSet<Item> _asked = new(ReferenceEqualityComparer.Instance);
    private object? _stamp;
    private int _tables = -1;

    /// <summary>The index's keys and their items, both ways, as the last write left them.</summary>
    private readonly Dictionary<long, Item> _items = [];
    private readonly Dictionary<Item, long> _keys = new(ReferenceEqualityComparer.Instance);

    /// <summary>How many times the index was written.</summary>
    public int Written { get; private set; }

    /// <summary>The index made to hold these items: written, unless it holds these very ones unchanged.</summary>
    public void Hold(IEnumerable<Item> items, object? stamp)
    {
        // In memory: the season is only a label here, so an unnamed one does not stop a search.
        _vault ??= PD2SPCompanion.Core.Collection.Vault.OpenInMemory(
            PD2SPCompanion.Core.Collection.Vault.TryInstalledSeason(out string season, out _) ? season : "unknown", VaultService.StatIdBits);

        var asked = items.Distinct(ReferenceEqualityComparer.Instance).Cast<Item>().ToList();
        if (!Stands(asked, stamp))
            Write(asked, stamp);
    }

    /// <summary>Which of the items held a question keeps.</summary>
    public HashSet<Item> Answering(VaultQuery query)
    {
        var found = new HashSet<Item>(ReferenceEqualityComparer.Instance);
        if (_vault is null)
            return found;

        foreach (var row in _vault.Page(query, "Stored", descending: false, 0, int.MaxValue))
        {
            if (_items.TryGetValue(row.Key, out var item))
                found.Add(item);
        }

        return found;
    }

    /// <summary>What each of these items holds under each property asked for, of the items held.</summary>
    public IReadOnlyDictionary<Item, double?[]> ValuesOf(IEnumerable<Item> items, IReadOnlyList<PD2SPCompanion.Core.Collection.PropertyWant> wants)
    {
        var values = new Dictionary<Item, double?[]>(ReferenceEqualityComparer.Instance);
        if (_vault is null)
            return values;

        var asked = items.Where(_keys.ContainsKey).ToList();
        var byKey = _vault.ValuesOf([.. asked.Select(item => _keys[item])], wants);
        foreach (var item in asked)
            values[item] = byKey[_keys[item]];

        return values;
    }

    /// <summary>Whether the index holds these very items, unchanged: the stamp the same, the items the same ones in number and by reference, the tables the ones it was written under.</summary>
    private bool Stands(List<Item> asked, object? stamp) =>
        stamp is not null && Equals(stamp, _stamp)
        && _tables == PD2SPCompanion.Core.GameFiles.InstalledTables.Generation
        && asked.Count == _asked.Count
        && asked.All(_asked.Contains);

    /// <summary>The index emptied and written from these items.</summary>
    private void Write(List<Item> asked, object? stamp)
    {
        _vault!.DropItems();

        var captured = new List<(VaultCapture Item, long? Folder)>();
        var order = new List<Item>();
        foreach (var item in asked)
        {
            if (_vault.Capture(item, out _) is { } capture)
            {
                captured.Add((capture, null));
                order.Add(item);
            }
        }

        var keys = _vault.AddCaptured(captured);
        _items.Clear();
        _keys.Clear();
        for (int at = 0; at < keys.Count; at++)
        {
            _items[keys[at]] = order[at];
            _keys[order[at]] = keys[at];
        }

        _asked.Clear();
        _asked.UnionWith(asked);
        _stamp = stamp;
        _tables = PD2SPCompanion.Core.GameFiles.InstalledTables.Generation;
        Written++;
    }

    public void Dispose() => _vault?.Dispose();
}

/// <summary>
/// The characters on disk, an index each: a search across them asked one index of every character in
/// turn, writing each character's items into it again at every run. A save is read once and kept until its file
/// changes (<see cref="PD2SPCompanion.Shell.Services.Character.SavedItems"/>), and its index as long: the save's
/// own record is the stamp. One of a file that is gone stays until the program closes - a few hundred kilobytes.
/// </summary>
public sealed class SavedSearches(VaultFilter filter) : IDisposable
{
    private readonly Dictionary<string, InventorySearch> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a search stands that a character can be asked.</summary>
    public bool IsActive => _probe.IsActive;

    private readonly InventorySearch _probe = new(filter);

    /// <summary>Which of the items of the save at <paramref name="path"/> the standing search keeps; null while nothing is searched for.</summary>
    public HashSet<Item>? Matching(string path, PD2SPCompanion.Shell.Services.Character.SavedItems.Held held)
    {
        if (!_byPath.TryGetValue(path, out var search))
            _byPath[path] = search = new InventorySearch(filter);

        return search.Matching(held.All, held);
    }

    /// <summary>A save's index written ahead of any search (<see cref="InventorySearch.Prepare"/>).</summary>
    public void Prepare(string path, PD2SPCompanion.Shell.Services.Character.SavedItems.Held held)
    {
        if (!_byPath.TryGetValue(path, out var search))
            _byPath[path] = search = new InventorySearch(filter);

        search.Prepare(held.All, held);
    }

    /// <summary>How many times any of the indexes was written.</summary>
    public int Written => _byPath.Values.Sum(search => search.Written);

    public void Dispose()
    {
        _probe.Dispose();
        foreach (var search in _byPath.Values)
            search.Dispose();
    }
}
