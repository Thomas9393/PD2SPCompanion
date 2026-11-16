using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// Where each item id is, so an item held twice can be marked. Only under the play rule
/// <see cref="PlayRules.DetectsDuplicates"/> - on in Legit, off in Casual and Free - since the
/// odds of a duplicate are low and the marks would be noise to most.
/// <para>
/// Looked at: the open character and its mercenary, the shared stash, the vault - every
/// collection of the mode, and only under the Move rule, where a vault copy is an item rather
/// than a library entry - and the other characters of the same mode in the save folder. Other
/// files are read once and kept until they change. Compact items have no id, and are never marked.
/// </para>
/// <para>
/// <b>When it looks</b>: only once per session or per character loading. A copy can
/// only come into being at a few moments - a move takes an item from one place and puts it in another, and
/// makes none: when a character is loaded (its file, or another's, may have changed outside the companion - a
/// restore and a clone are loads too), when another file of the save folder is written, when another vault
/// is opened or one imported into, when the stash is read, and when an item is made on the character or the
/// stash (pasted from the game, it keeps its id). So it looks then (<see cref="LookAgain"/>), and - only
/// while something IS marked - again at every marking pass, so a copy dealt with loses its mark and the
/// places a badge names stay true. With nothing marked, which is the usual case, a marking pass reads nothing.
/// It was worked out again on every marking pass - the character, the stash, every id in the vault, the save
/// folder's listing, each time a page rebuilt its tiles - until then.
/// </para>
/// </summary>
public sealed class DuplicateIndex
{
    /// <summary>The one the tiles ask, as <c>SpriteLoader.Current</c> is: tiles are made in many places.</summary>
    public static DuplicateIndex? Current { get; private set; }

    private readonly CharacterSession _character;
    private readonly SharedStashSession _stash;
    private readonly VaultService _vaults;
    private readonly VaultMoves _moves;
    private readonly PlayRules _rules;

    /// <summary>Another file's ids and where each is, by path, with the write time they were read at.</summary>
    private readonly Dictionary<string, (DateTime At, List<(uint Id, string Place)> Held)> _files =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<uint, List<Place>> _places = [];

    /// <summary>
    /// Where a copy is, as said to the player, and what the pages need of it: on this character or its
    /// shared stash, in the vault. Told by flags, not by reading the words back - they are translated.
    /// </summary>
    private sealed record Place(string Shown, bool Here = false, bool InVault = false);

    public DuplicateIndex(CharacterSession character, SharedStashSession stash, VaultService vaults, VaultMoves moves,
                          PlayRules rules)
    {
        _character = character;
        _stash = stash;
        _vaults = vaults;
        _moves = moves;
        _rules = rules;
        Current = this;

        // The search's Duplicates only, and its ids.
        vaults.Filter.DuplicateIds = DuplicateIds;
        vaults.Filter.OffersDuplicates = rules.DetectsDuplicates;

        // The rule switched: every page marks its tiles again.
        rules.PropertyChanged += (_, _) =>
        {
            if (_on != rules.DetectsDuplicates)
            {
                _on = rules.DetectsDuplicates;
                vaults.Filter.OffersDuplicates = _on;
                _stale = true;
                Changed?.Invoke();
            }
        };
        _on = rules.DetectsDuplicates;

        // The moments a copy can come into being: looked for at the next marking pass after each.
        character.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded) && character.IsLoaded)
                LookAgain();
        };
        character.SaveFolderChanged += LookAgain;
        character.ItemMade += _ => LookAgain();
        stash.Loaded += LookAgain;
        stash.ItemMade += _ => LookAgain();
        vaults.Switched += LookAgain;
        vaults.VaultOpened += LookAgain;

        // The vault's ids kept until the vault changes: every save of the character echoes through the save
        // folder's watcher, and each look queried every id of the vault again - 2,822 rows on a season's vault -
        // though no save can change it.
        void VaultMoved() => _vaultIds = null;
        vaults.Changed += VaultMoved;
        vaults.Written += VaultMoved;
        vaults.WrittenQuietly += VaultMoved;
        vaults.UndoLost += VaultMoved;
        vaults.Switched += VaultMoved;
        vaults.VaultOpened += VaultMoved;
        moves.Changed += VaultMoved;
        moves.ItemsMoved += VaultMoved;
    }

    /// <summary>The open vault's item keys and ids as last read; null where the vault changed since.</summary>
    private IReadOnlyList<(long Key, uint Id)>? _vaultIds;

    /// <summary>Something happened that can make a copy: the files are read again at the next marking pass.</summary>
    public void LookAgain() => _stale = true;

    /// <summary>The files were not read since something that can make a copy.</summary>
    private bool _stale = true;

    /// <summary>Something is held twice: while it is, every marking pass looks again, so the marks follow what is done about it.</summary>
    private bool _any;

    /// <summary>How many times the files were read: what a test counts.</summary>
    public int Looks { get; private set; }

    /// <summary>The files read again, where a copy may have come into being or one is marked; left alone otherwise.</summary>
    private void Look()
    {
        if (!_stale && !_any)
            return;

        Rebuild();
        _stale = false;
        _any = _places.Values.Any(places => places.Count > 1);
        Looks++;
    }

    /// <summary>
    /// A duplicate badge clicked: the search shows that item's copies, in the vault and on the
    /// character, the rest of the search let go of so nothing else hides them - as a grail card does.
    /// </summary>
    public void ShowCopies(Item item)
    {
        if (item.IsCompact || item.Id == 0)
            return;

        var held = _places.TryGetValue(item.Id, out var all) && all.Count > 1 ? all : [];
        _vaults.Filter.Clear();
        _vaults.Filter.SameId = item.Id;

        // Another character's copy cannot be shown, only named - which the strip does for all.
        string places = string.Join(", ", held.Select(place => place.Shown));
        _character.Status = held.Count > 0 ? L($"Every copy shown: {places}.") : L("Every copy shown.");

        CopiesShown?.Invoke(held.Any(place => place.Here), held.Any(place => place.InVault));
    }

    private bool _on;

    /// <summary>
    /// A badge clicked: whether copies are on the character or its shared stash, and whether in
    /// the vault - so the window can put the pages that show them side by side,
    /// for a copy in the vault while the right page shows something else.
    /// </summary>
    public event Action<bool, bool>? CopiesShown;

    /// <summary>Raised when the marks should be drawn again though nothing on the pages changed.</summary>
    public event Action? Changed;

    /// <summary>Whether items are being marked at all.</summary>
    public bool IsOn => _rules.DetectsDuplicates && _character.IsLoaded;

    /// <summary>Marks each tile held more than once with the places its id is, and clears the rest.</summary>
    public void Mark(IEnumerable<ItemTileViewModel> tiles)
    {
        var list = tiles.ToList();
        if (!IsOn)
        {
            foreach (var tile in list)
                tile.DuplicatePlaces = null;
            return;
        }

        Look();
        foreach (var tile in list)
            tile.DuplicatePlaces = PlacesOf(tile.Item);
    }

    /// <summary>Every place this item's id is, when it is in more than one; null otherwise.</summary>
    public IReadOnlyList<string>? PlacesOf(Item item) =>
        !item.IsCompact && item.Id != 0 && _places.TryGetValue(item.Id, out var places) && places.Count > 1
            ? [.. places.Select(place => place.Shown)]
            : null;

    /// <summary>The ids held more than once, for the search's Duplicates only.</summary>
    public IReadOnlyCollection<uint> DuplicateIds()
    {
        if (!IsOn)
            return [];

        Look();
        return [.. _places.Where(each => each.Value.Count > 1).Select(each => each.Key)];
    }

    private void Rebuild()
    {
        var places = new Dictionary<uint, List<Place>>();
        void Add(uint id, Place place)
        {
            if (id == 0)
                return;
            if (!places.TryGetValue(id, out var list))
                places[id] = list = [];
            list.Add(place);
        }

        string me = _character.CharacterName;
        foreach (var item in _character.HeldItems.Where(item => !item.IsCompact))
        {
            string where = Container(item);
            Add(item.Id, new Place(L($"{me}'s {where}"), Here: true));
        }

        foreach (var item in _stash.HeldItems.Where(item => !item.IsCompact))
            Add(item.Id, new Place(SharedStash.PageOf(item) is { } page ? L($"shared stash page {page + 1}") : L("shared stash"),
                                   Here: true));

        // The vault's items, a row on its way out being the character's now.
        {
            if (_vaults.Store is { } open)
            {
                foreach (var (key, id) in _vaultIds ??= open.OriginIds())
                {
                    if (!_moves.IsWithdrawing(key))
                        Add(id, new Place(L("vault"), InVault: true));
                }
            }
        }

        // The other characters of the same mode.
        string folder = _character.SaveFolder;
        if (folder.Length > 0 && Directory.Exists(folder))
        {
            foreach (string path in Directory.GetFiles(folder, "*.d2s"))
            {
                if (string.Equals(path, _character.CharacterPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var (id, place) in Read(path, ReadCharacter))
                    Add(id, new Place(place));
            }
        }

        _places = places;
    }

    /// <summary>A file's ids, read again only when it has changed since.</summary>
    private List<(uint Id, string Place)> Read(string path, Func<string, List<(uint, string)>> read)
    {
        try
        {
            var at = File.GetLastWriteTimeUtc(path);
            // By the mode too: a character of the other mode reads as holding nothing, which is true of this mode alone.
            string key = $"{_character.IsHardcore}|{path}";
            if (_files.TryGetValue(key, out var known) && known.At == at)
                return known.Held;

            var held = read(path);
            _files[key] = (at, held);
            return held;
        }
        catch (Exception)
        {
            // A file that will not read says nothing either way: this is a warning, not a proof.
            return [];
        }
    }

    private List<(uint, string)> ReadCharacter(string path)
    {
        // The status byte: 0x24 in the header, hardcore its third bit. Another mode's items
        // cannot meet this one's.
        using (var file = File.OpenRead(path))
        {
            file.Seek(0x24, SeekOrigin.Begin);
            if (((file.ReadByte() & 0x04) != 0) != _character.IsHardcore)
                return [];
        }

        try
        {
            var (save, _) = SaveVerifier.Load(path);
            return [.. SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary | ItemScope.Sockets)
                .Where(item => !item.IsCompact && item.Id != 0)
                .Select(item => (item.Id, save.Name))];
        }
        finally
        {
            // Reading a character installs its layout.
            _character.UseMyLayout();
        }
    }

    /// <summary>Where on the character an item is, as the tooltip says it.</summary>
    private string Container(Item item)
    {
        if (_character.IsMercenaryItem(item))
            return L("mercenary");

        return item.Mode switch
        {
            ItemMode.Equipped => L("gear"),
            ItemMode.Belt => L("belt"),
            ItemMode.Socket => L("sockets"),
            _ => item.Page switch
            {
                InventoryLayout.CubePage => L("cube"),
                InventoryLayout.ClassicStashPage or InventoryLayout.ExpansionStashPage => L("stash"),
                _ => L("inventory"),
            },
        };
    }
}
