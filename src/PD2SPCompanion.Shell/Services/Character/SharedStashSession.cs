using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// PD2's shared stash, open alongside whatever character is open - or none.
/// <para>
/// It is a separate file that belongs to no character, which is why it is a session of its
/// own rather than another container on <see cref="CharacterSession"/>. Its page is kept in
/// each item's <see cref="Item.Location"/> field rather than in <c>Page</c>; see
/// <see cref="SharedStash"/>, which has the measurement.
/// </para>
/// <para>
/// The <b>selection is the character session's</b>, deliberately. The detail panel edits
/// whatever is chosen, wherever it lives, so pointing this page's tiles at the same property
/// gives the shared stash the whole of that panel without a second copy of it.
/// </para>
/// </summary>
public sealed partial class SharedStashSession : ObservableObject, ISaveParticipant, IItemHolder
{
    /// <summary>The softcore shared stash, as the game names it.</summary>
    public const string SoftcoreFile = "pd2_shared.stash";

    /// <summary>The hardcore one, which is a different file entirely.</summary>
    public const string HardcoreFile = "pd2_hc_shared.stash";

    /// <summary>
    /// Which of the two this character shares with.
    /// <para>
    /// <b>The modes do not share a stash.</b> The game keeps them apart and will not carry
    /// an item between them, so an editor that showed one stash to both would happily move
    /// a softcore item into a hardcore character - which is not something the game can undo
    /// or excuse.
    /// </para>
    /// <para>
    /// Found by making a hardcore character and watching a second file appear beside the
    /// first, rather than assumed either way.
    /// </para>
    /// </summary>
    public string FileName => _character.IsHardcore ? HardcoreFile : SoftcoreFile;

    private readonly CharacterSession _character;

    private SharedStash? _stash;

    /// <summary>
    /// The file as it stood when it was read - when it was last written, and how big.
    /// <para>
    /// The game re-reads this file every time it is entered and never merges, so a stash
    /// that has moved on since we read it is not ours to write over. The one case that
    /// matters is playing while the editor is open: the game writes the stash, our staged
    /// moves are rightly kept, and saving them would silently undo whatever was just done
    /// in game.
    /// </para>
    /// </summary>
    private (DateTime Written, long Length)? _asRead;

    /// <summary>
    /// The backup moment the stash on screen was read from, or null for the live file. A stash
    /// read from a backup is a restore waiting for Save - the confirm says the shared stash goes
    /// back with the character - so it counts as a change. Were it not, a
    /// restore would put the character back and leave the stash as it was.
    /// </summary>
    private string? _readMoment;

    /// <summary>
    /// Whether the live stash file is no longer the one read - the game has saved it since.
    /// Always the live file, even with a backup on screen: that is the file a save replaces.
    /// See <see cref="GameSync"/>.
    /// </summary>
    public bool ChangedOnDisk
    {
        get
        {
            if (Path is not { Length: > 0 } where || _asRead is not { } read || !File.Exists(where))
                return false;

            var now = new FileInfo(where);
            return now.LastWriteTimeUtc != read.Written || now.Length != read.Length;
        }
    }

    /// <summary>Takes the live file as it stands now as the one the next save replaces. See <see cref="CharacterSession.AcceptDisk"/>.</summary>
    public void AcceptDisk()
    {
        if (Path is { Length: > 0 } where && File.Exists(where))
        {
            var now = new FileInfo(where);
            _asRead = (now.LastWriteTimeUtc, now.Length);
        }
    }

    /// <summary>Raised whenever the pages have been rebuilt.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when the file has been read - opened with a character, or read again after the game
    /// wrote it - and not when the pages are rebuilt by an edit.
    /// </summary>
    public event Action? Loaded;

    private SaveSet? _saveSet;

    /// <summary>
    /// Joins the set of files saved together. The stash is never written on its own: doing
    /// so while a character had moved an item into it would leave the item in both files.
    /// </summary>
    public void PartOf(SaveSet set)
    {
        _saveSet = set;
        set.Takes(this);
    }

    public SharedStashSession(CharacterSession character, SaveSet saves)
    {
        _character = character;
        _character.SharedTiles = () => Pages.SelectMany(page => page.Items);

        // The second of the two files saved together. Joined here rather than by whoever
        // happens to use this, so there is no way to have a stash that is not in the set.
        PartOf(saves);


        // The stash is only ever reached through a character - the same way the game does
        // it, where you walk up to a stash in town. Tying the two together is what makes
        // one Save able to write both files without either being left behind.
        _character.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName != nameof(CharacterSession.IsLoaded))
                return;

            if (_character.IsLoaded)
                Load();
            else
                Unload();
        };

        // The file lives beside the characters, so it is found again whenever that moves -
        // but never over work that has not been saved yet (see Load), and only when there is
        // something to find: no stash yet, or a file that is not what this last read or wrote.
        // Every write in the folder lands here, this editor's own autosave of the stash
        // included; reading the file again for it put new items under the pages, and the item
        // panel went on editing the old ones - "Edited", and nothing saved.
        _character.SaveFolderChanged += () =>
        {
            if (_stash is null || ChangedOnDisk || !string.Equals(_loadedPath, Path, StringComparison.OrdinalIgnoreCase))
                Load();
        };
    }

    /// <summary>
    /// Puts the stash away with the character it was reached through. Anything staged goes
    /// with it: the character it belonged to is gone, and there is nothing left to save it
    /// alongside.
    /// </summary>
    public void Unload()
    {
        _stash = null;
        _readMoment = null;
        Pages.Clear();
        HasUnsavedChanges = false;
        Status = "";

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(CanSave));
        Changed?.Invoke();
    }

    /// <summary>The nine pages, in the order the game's tabs show them.</summary>
    public ObservableCollection<GridViewModel> Pages { get; } = [];

    /// <summary>
    /// What the last action on the shared stash came to - said on the message strip, as the
    /// character's is. Posted on every assignment; see <see cref="CharacterSession.Status"/>.
    /// </summary>
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            _character.Messages.Post(value);
        }
    }

    private string _status = "";

    [ObservableProperty] private bool _hasUnsavedChanges;

    partial void OnHasUnsavedChangesChanged(bool value)
    {
        // The set is what the Save button watches, so it is told whenever this turns over.
        _saveSet?.SomethingChanged();
    }

    /// <summary>What this is, when something has to say which file went wrong.</summary>
    string ISaveParticipant.What => L("The shared stash");

    /// <summary>Whether anything here is waiting to be written.</summary>
    bool ISaveParticipant.HasChanges => _stash is not null && (HasUnsavedChanges || _readMoment is not null || LeavingNow().Count > 0);

    /// <summary>
    /// Whether an item goes with the next save - the vault's moves, as on the character's side:
    /// see <see cref="CharacterSession.Leaves"/>.
    /// </summary>
    public Func<Item, bool>? Leaves { get; set; }

    private List<Item> LeavingNow() =>
        Leaves is { } leaves && _stash?.Items.Items is { } items ? items.Where(leaves).ToList() : [];

    /// <summary>What the last prepare left out, which the write then lets go.</summary>
    private List<Item> _preparedWithout = [];

    /// <summary>
    /// The stash is read and written with no skill bytes and its own stat width, which is
    /// not how a character is read. Said rather than assumed - the parser keeps one layout
    /// at a time and whichever file was touched last set it.
    /// </summary>
    public void UseMyLayout() =>
        D2SLib.Globals.ResetForFile(pd2Character: false, statIdBits: 10);

    /// <summary>Where the file is, or "" when the game folder is not set.</summary>
    public string Path =>
        _character.SaveFolder is { Length: > 0 } folder
            ? System.IO.Path.Combine(folder, FileName)
            : "";

    public bool IsLoaded => _stash is not null;

    public bool CanSave => _stash is not null && HasUnsavedChanges;

    /// <summary>How many items it holds, across every page.</summary>
    public int Count => Pages.Sum(page => page.Count);

    /// <summary>The live file the stash was last loaded for - a folder moved elsewhere is a new one.</summary>
    private string? _loadedPath;

    /// <summary>
    /// Reads the file, or reports why it could not be. A missing file is not an error: a
    /// player who has never opened the shared stash has none, and the page should say so
    /// rather than look broken.
    /// </summary>
    public void Load()
    {
        // The save folder is watched, and the game rewrites this file whenever it is played,
        // so a reload can arrive at any moment. Staged moves are not thrown away by one:
        // they belong to the character now, and only its Save or its Undo decides them.
        // Said by GameSync, and only when this file really changed - any write in the folder
        // lands here, the character's own save included. Staged on another backup, or on the
        // live file with a backup now open, they belonged to what is no longer on screen.
        if (HasUnsavedChanges && _readMoment == _character.ViewingMoment)
            return;

        // A stash item chosen in the panel belongs to the file about to be dropped: let it go, or the
        // panel goes on editing an item nothing holds any more.
        if (_character.SelectedItem is { } chosen && _stash?.Items.Items?.Any(item => ReferenceEquals(item, chosen.Item)) == true)
            _character.SelectedItem = null;

        Pages.Clear();
        _stash = null;
        _readMoment = null;
        HasUnsavedChanges = false;

        string path = Path;
        _loadedPath = path;

        // A character is open by the time this runs, so the folder is settled and the
        // tables are loaded. Both are kept as guards rather than assumptions, but neither
        // is something the reader can act on, so neither says so.
        if (path.Length == 0)
        {
            Status = "";
            Rebuild();
            return;
        }

        // Before the read, not after. An item's properties are read against the installed
        // ItemStatCost, so without the tables the reader walks the wrong number of bits and
        // the round-trip check fails on a file that is perfectly good - which is exactly
        // what it did, reporting that a stash this editor had written itself and verified
        // byte-identical "cannot be rewritten safely".
        //
        // Belt and braces since the stash became character-bound: opening a character loads
        // the tables already. Kept because it costs nothing and removes an ordering
        // assumption that would otherwise be invisible.
        if (_character.EnsureGame() is null)
        {
            Status = "";
            Rebuild();
            return;
        }

        // An earlier copy of the character does not bring back the stash saved with it: the
        // stash is every character's, and rolling it back would lose what the others put in
        // it since. Only a stash that will not read is taken from the
        // backup - the one restore of it there is, and asked about with the character's.
        if (_character.ViewingMoment is { Length: > 0 } moment && !ReadsWell(path))
        {
            // The stash as the companion last wrote it, where that reads: newer than the copy saved with
            // the character's backup, so less of the others' work in it is lost.
            string last = SaveWriter.AsWrittenPath(path, AsWrittenFolder(path));
            string paired = System.IO.Path.Combine(
                SaveWriter.BackupFolderFor(_character.SavePath ?? path),
                $"{FileName}.{moment}.bak");

            if (File.Exists(last) && ReadsWell(last))
            {
                path = last;
                Status = L("Shared stash unreadable. Backup's copy shown.");
            }
            else if (File.Exists(paired))
            {
                path = paired;
                Status = L("Shared stash unreadable. Backup's copy shown.");
            }
        }

        if (!File.Exists(path))
        {
            Status = L($"{FileName} not found.");
            Rebuild();
            return;
        }

        try
        {
            _stash = SharedStash.Read(path);
            _lastSeasonOwn = LastSeasonGuard.Own(_stash.LastSeason, _stash.Items.Items ?? []);
            _savedBytes = File.ReadAllBytes(path);
            HasUnsavedChanges = false;

            // The live file's stamp even when a backup was read: that is the file a save
            // replaces, and what the game may write to meanwhile.
            var file = new FileInfo(File.Exists(Path) ? Path : path);
            _asRead = (file.LastWriteTimeUtc, file.Length);
            _readMoment = string.Equals(path, Path, StringComparison.OrdinalIgnoreCase) ? null : _character.ViewingMoment;

            Status = "";
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            _stash = null;
            Status = ex.Message;

            // As read, for all that it would not read: not "changed in game" at every autosave.
            if (Path is { Length: > 0 } unread && File.Exists(unread))
            {
                var file = new FileInfo(unread);
                _asRead = (file.LastWriteTimeUtc, file.Length);
            }
        }

        Rebuild();

        if (_stash is not null)
            Loaded?.Invoke();
    }

    /// <summary>
    /// Where the stash's rolling copy lies: the root of the backups, not a character's folder - the stash is
    /// every character's, and its copy as last written is one whichever of them saved it.
    /// </summary>
    private static string AsWrittenFolder(string path) =>
        SaveWriter.BackupRoot(System.IO.Path.GetDirectoryName(path) ?? ".");

    /// <summary>Whether the file there reads as a shared stash.</summary>
    private static bool ReadsWell(string path)
    {
        try
        {
            _ = SharedStash.Read(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether a restore of the character writes this stash too: only one taken from the backup.</summary>
    public bool RestoresWithCharacter => _stash is not null && _readMoment is not null;

    /// <summary>
    /// The stash as it stands on disk: the bytes it was read from, or last written - what the
    /// session's history keeps of each state. Null
    /// with no stash read.
    /// </summary>
    public byte[]? SavedBytes => _stash is null ? null : _savedBytes;

    /// <summary>The bytes a save has prepared and not yet written, or null.</summary>
    public byte[]? PreparedBytes => _prepared;

    private byte[]? _savedBytes;

    /// <summary>Whether the stash stands as these bytes have it: its file is these, and nothing waits to be written.</summary>
    public bool StandsAs(byte[] bytes) =>
        _stash is not null && _savedBytes is not null && !((ISaveParticipant)this).HasChanges && bytes.AsSpan().SequenceEqual(_savedBytes);

    /// <summary>
    /// Puts an earlier state of the stash back, from its bytes - an undo or redo in the session's
    /// history. Unsaved until the next save writes it. Null when done, else why not.
    /// </summary>
    public string? RestoreBytes(byte[] bytes)
    {
        try
        {
            _stash = SharedStash.Read(bytes, FileName);

            // Undo made every item afresh: the ones a last-season stash may take back are these now.
            _lastSeasonOwn = LastSeasonGuard.Own(_stash.LastSeason, _stash.Items.Items ?? []);
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            return L($"Shared stash not put back: {ex.Message}");
        }

        HasUnsavedChanges = _savedBytes is null || !bytes.AsSpan().SequenceEqual(_savedBytes);
        OnPropertyChanged(nameof(CanSave));
        _saveSet?.SomethingChanged();
        Rebuild();
        Changed?.Invoke();
        return null;
    }

    /// <summary>The page grids kept across rebuilds, so their controls are too (GridViewModel).</summary>
    private readonly GridCache _grids = new();

    /// <summary>
    /// Lays the items out on the nine pages. The pages are always all nine, whether or not
    /// anything is on them - an empty page is a page, the same way an empty cube is a cube.
    /// </summary>
    private void Rebuild()
    {
        Pages.Clear();

        // A load or an undo brings the tab's counts with it, not only a move.
        MaterialsChanged?.Invoke();

        // The stash grid is the same whatever class is open, and this page opens with no
        // character at all, so the class here is only what the lookup needs to be given.
        var grid = _character.EnsureGame() is { } game
            ? InventoryLayout.Load(game, 0, expansion: true).Stash
            : InventoryLayout.Fallback.Stash;

        var held = (_stash?.Items.Items ?? [])
            .Select((item, index) => (Item: item, Index: index))
            .ToList();

        for (int page = SharedStash.FirstPage; page <= SharedStash.LastPage; page++)
        {
            var view = _grids.For(NameOfPage(page), (byte)page, grid.Columns, grid.Rows, shared: true);

            var shown = new List<ItemTileViewModel>();
            foreach (var (item, index) in held.Where(e => SharedStash.PageOf(e.Item) == page))
            {
                // An item on the cursor has left its cell as far as the eye is concerned,
                // the same as one lifted out of the character's own containers. It is still
                // in the file and still says where it was, so nothing has been edited yet.
                if (_character.IsHeld(item))
                    continue;

                shown.Add(new ItemTileViewModel(item, index, item.X, item.Y));
            }
            view.Fill(shown);

            Pages.Add(view);
        }

        // Every tile is new: the item the Edit page shows, where it lies here, is shown by its new tile - as the
        // character's rebuild finds its own - not by one no page draws any more.
        if (_character.SelectedItem is { } chosen
            && Pages.SelectMany(page => page.Items).FirstOrDefault(tile => ReferenceEquals(tile.Item, chosen.Item)) is { } again
            && !ReferenceEquals(again, chosen))
        {
            _character.SelectedItem = again;
        }

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(CanSave));
        Changed?.Invoke();
    }

    /// <summary>
    /// What a page is called. The game's own tabs read <c>P I II III IV V VI VII VIII IX</c>
    /// in Roman, but a picker naming them plainly is easier to read than nine numerals, and
    /// the P is the character's personal stash, which lives in the character's file and not
    /// in this one.
    /// </summary>
    private static string NameOfPage(int page) => L($"Page {page}");

    /// <summary>
    /// Other characters holding an item that is also in this stash, by name.
    /// <para>
    /// Restoring puts a character and its stash back together, which is right for the pair
    /// - but an item that passed <b>through</b> the stash links two characters that were
    /// never saved together. Put the stash back to before an item was taken out, while the
    /// character that took it still has it, and the item exists twice.
    /// </para>
    /// <para>
    /// The game never rewinds anything, so it has no answer to this and neither do we. It
    /// can at least be seen coming and named.
    /// </para>
    /// <para>
    /// The sweep is the character session's - see <see cref="CharacterSession.HeldByOthers"/>.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> OthersHoldingTheseItems()
    {
        if (_stash?.Items.Items is not { Count: > 0 } held)
            return [];

        var mine = held.Where(item => item.Id != 0).Select(item => item.Id).ToHashSet();

        return _character.HeldByOthers(mine).Values.Distinct().ToList();
    }

    /// <summary>Whether this very item is on one of the stash's pages.</summary>
    public bool Holds(Item item) =>
        (_stash?.Items.Items ?? []).Any(each => ReferenceEquals(each, item));

    /// <summary>Every item on the stash's pages, and what sits in their sockets.</summary>
    public IEnumerable<Item> HeldItems =>
        (_stash?.Items.Items ?? []).WithSockets();

    /// <summary>The items the stash held when read, while it is still in last season's encoding (<see cref="LastSeasonGuard"/>).</summary>
    private IReadOnlySet<Item>? _lastSeasonOwn;

    /// <summary>Whether the shared stash is still in last season's encoding, as the game wrote it before the new season.</summary>
    public bool IsLastSeason => _stash?.LastSeason == true;

    /// <summary>
    /// The item era this file is written in, or null when it holds nothing to tell by.
    /// A stash holds one era just as a character does; see
    /// <see cref="CharacterSession.ItemEra"/>.
    /// </summary>
    public string? ItemEra =>
        (_stash?.Items.Items ?? [])
            .Select(item => item.Version)
            .FirstOrDefault(version => version is { Length: > 0 });

    /// <summary>
    /// Why an item from elsewhere cannot come onto these pages, or null - what <see cref="Adopt"/> asks first,
    /// asked of a whole batch before any of it lands (<see cref="RefusesAny"/>).
    /// </summary>
    public string? Refuses(Item item) => Refuses(item, ItemEra);

    private string? Refuses(Item item, string? era)
    {
        if (_stash is null)
            return L("No shared stash open.");

        if (era is not null && item.Version is { Length: > 0 } arriving && !PD2SPCompanion.Core.Saves.ItemSeason.Mix(era, arriving))
            return L($"Require same version: item v{arriving}, stash v{era}.");

        return LastSeasonGuard.Refuses(_lastSeasonOwn, item, L("Shared stash"));
    }

    /// <summary>
    /// What <see cref="Refuses(Item)"/> says of the first that would be turned away, were these to arrive one after another:
    /// a file holding nothing is given its era by the first to land, and may then refuse the next. Null where all may come.
    /// </summary>
    public string? RefusesAny(IEnumerable<Item> items)
    {
        string? era = ItemEra;
        foreach (var item in items)
        {
            if (Refuses(item, era) is { } refused)
                return refused;

            era ??= item.Version is { Length: > 0 } stamp ? stamp : null;
        }

        return null;
    }

    /// <summary>
    /// A detached copy of an item of these pages, written and read back in the stash's own layout - not the
    /// character's, which another season's or the old format's stat ids would change (the
    /// inventory's multi-duplicate copied stash picks with the character's). Null where it cannot be.
    /// </summary>
    public Item? CopyOf(Item item)
    {
        try
        {
            UseMyLayout();
            return PD2SPCompanion.Core.Saves.ItemBlob.Copy(item, D2SLib.Globals.stat_id_bits);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Takes an item in from somewhere else and puts it on a page. The caller has found the
    /// room already; this only does the paperwork.
    /// </summary>
    /// <param name="keepId">
    /// Never a new identifier, even where one here has it - the vault's withdraw, as the
    /// character's <see cref="CharacterSession.Adopt"/>: a shared id is how a copy shows.
    /// </param>
    public string? Adopt(Item item, int page, int x, int y, bool rebuild = true, bool keepId = false)
    {
        if (Refuses(item) is { } refused)
            return Status = refused;

        // Open: no stash is the first thing Refuses says.
        var stash = _stash!;

        // Kept unless it clashes, the same as the character's side: a moved item is the
        // same item, and an id that survives the move is what makes it traceable.
        if (!keepId && stash.Items.Items!.Any(each => each.Id == item.Id))
            item.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);

        SharedStash.Place(item, page, x, y);

        stash.Items.Items!.Add(item);

        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(CanSave));
        if (rebuild)
            Rebuild();

        return null;
    }

    /// <summary>
    /// Takes in items dropped from the vault on one of these pages (a card
    /// dragged back to the shared stash was refused), as the character's
    /// <see cref="CharacterSession.WelcomeAt"/>: one item where it was dropped; several as they lay about
    /// the one held where they fit so, else packed onto that page from its first free cells - all of them or
    /// none. Identifiers kept.
    /// </summary>
    /// <returns>As the character's: nothing placed, and every item in NoRoom, when they do not all fit.</returns>
    public (int Placed, IReadOnlyList<Item> NoRoom, int RefusedCount, string? Refused) WelcomeAt(
        IReadOnlyList<Item> items, int page, int x, int y, IReadOnlyList<(int Dx, int Dy)>? shape = null)
    {
        if (_stash?.Items.Items is not { } held)
            return (0, items, 0, L("No shared stash open."));

        var grid = _character.EnsureGame() is { } game
            ? InventoryLayout.Load(game, 0, expansion: true).Stash
            : InventoryLayout.Fallback.Stash;
        var contents = held.Where(item => SharedStash.PageOf(item) == page).ToList();
        var spots = new List<(Item Item, int X, int Y)>();

        if (items.Count == 1)
        {
            if (!ItemMover.CanPlace(items[0], contents, grid, x, y).Allowed)
                return (0, items, 0, null);

            spots.Add((items[0], x, y));
        }
        else if (shape is not null && ItemMover.ShapedSpots(items, shape, contents, grid, x, y) is { } shaped)
        {
            // Several, as they lay about the one held - as the character's WelcomeAt.
            spots.AddRange(shaped);
        }
        else
        {
            foreach (var item in items)
            {
                if (ItemMover.FirstFreeCell(item, contents, grid) is not { } cell)
                    return (0, items, 0, null);

                // Not the stash's yet: where it will go is only its own until it is taken in.
                SharedStash.Place(item, page, cell.X, cell.Y);
                contents.Add(item);
                spots.Add((item, cell.X, cell.Y));
            }
        }

        int placed = 0, refusedCount = 0;
        string? refused = null;

        foreach (var (item, atX, atY) in spots)
        {
            if (Adopt(item, page, atX, atY, rebuild: false, keepId: true) is { } why)
            {
                refused ??= why;
                refusedCount++;
                continue;
            }

            placed++;
        }

        Rebuild();
        return (placed, [], refusedCount, refused);
    }

    /// <summary>Where an item of these pages lies now, for <see cref="PutBack"/>; null where it is none of theirs.</summary>
    public ItemOrigin? OriginOf(Item item) =>
        _stash?.Items is { } list && Holds(item) ? ItemOrigin.Of(item, list) : null;

    /// <summary>
    /// An item let go of and refused elsewhere, back on its own page and cell, as <see cref="CharacterSession.PutBack"/>:
    /// nothing asked of it, and to the first room of these pages only where something moved in the meantime has taken its
    /// cell. No rebuild.
    /// </summary>
    public void PutBack(Item item, ItemOrigin origin)
    {
        origin.Restore(item);

        if (SharedStash.PageOf(item) is { } page && origin.List.Items is { } held)
        {
            var grid = _character.EnsureGame() is { } game
                ? InventoryLayout.Load(game, 0, expansion: true).Stash
                : InventoryLayout.Fallback.Stash;
            IEnumerable<Item> On(int each) => held.Where(other => !ReferenceEquals(other, item) && SharedStash.PageOf(other) == each);

            if (!ItemMover.CanPlace(item, On(page), grid, origin.X, origin.Y).Allowed)
            {
                foreach (int other in Enumerable.Range(SharedStash.FirstPage, SharedStash.LastPage - SharedStash.FirstPage + 1))
                {
                    if (ItemMover.FirstFreeCell(item, On(other), grid) is { } cell)
                    {
                        SharedStash.Place(item, other, cell.X, cell.Y);
                        break;
                    }
                }
            }
        }

        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// Lets an item go to somewhere else. It is taken out of this file and handed back for
    /// the caller to put down; nothing is destroyed.
    /// </summary>
    public Item? Release(Item item, bool rebuild = true)
    {
        if (_stash?.Items.Items is not { } held || !held.Remove(item))
            return null;

        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(CanSave));
        if (rebuild)
            Rebuild();

        return item;
    }

    /// <summary>Lays the pages out again, for a caller that moved a batch quietly.</summary>
    public void RefreshPages() => Rebuild();

    /// <summary>
    /// Draws again every tile on the pages that stands for this item, after it was written to in
    /// place. The character redraws its own grids' tiles and the one the selection holds (its
    /// SelectedItemChanged) - a stash tile made by a rebuild since the item was chosen is neither,
    /// and went on showing the old count until something rebuilt the page.
    /// </summary>
    public void Redraw(Item item)
    {
        foreach (var tile in Pages.SelectMany(page => page.Items))
        {
            if (ReferenceEquals(tile.Item, item))
                tile.Rebuilt();
        }
    }

    /// <summary>
    /// Re-checks whether the stash differs from its file, after an item on its pages was edited
    /// in place - the item panel's KEEP. The character's own MarkEdited compares only the
    /// character, so an edit to a stash item showed on its tile and was never saved: no autosave,
    /// no history, nothing in game. Compared by bytes, as the character is.
    /// </summary>
    public void MarkEdited()
    {
        if (_stash is null)
            return;

        try
        {
            HasUnsavedChanges = _savedBytes is null || !_stash.Write().AsSpan().SequenceEqual(_savedBytes);
        }
        catch (Exception)
        {
            // Something will not serialize: saving is left available so the attempt says why.
            HasUnsavedChanges = true;
        }

        OnPropertyChanged(nameof(CanSave));
        _saveSet?.SomethingChanged();
    }

    /// <summary>
    /// Raised for an item the editor makes on these pages - a duplicate - as the character's own
    /// ItemMade is: the duplicate marks look again (DuplicateIndex).
    /// </summary>
    public event Action<Item>? ItemMade;

    /// <summary>An item on these pages made by the editor in place - a craft.</summary>
    public void Made(Item item) => ItemMade?.Invoke(item);

    /// <summary>
    /// A second of an item on a stash page (a copy of a staff on Shared Page 8
    /// went to the inventory): on the same page next to the original where there is room, else the
    /// first room on the pages after it, then before it - it stays in the shared stash. A deep copy
    /// with a <b>new identifier</b>, as the character's duplicate; selected in its place.
    /// </summary>
    /// <returns>Null when it landed, else what to say.</returns>
    public string? Duplicate(ItemTileViewModel tile)
    {
        if (_stash?.Items.Items is not { } held || !held.Any(each => ReferenceEquals(each, tile.Item))
            || SharedStash.PageOf(tile.Item) is not { } page)
            return Status = L("Not in the shared stash.");

        if (_character.Held is not null)
            return Status = L("Require held item placed.");

        if (CopyOf(tile.Item) is not { } copy)
            return Status = L($"{tile.Name} cannot be copied.");

        var grid = _character.EnsureGame() is { } game
            ? InventoryLayout.Load(game, 0, expansion: true).Stash
            : InventoryLayout.Fallback.Stash;
        IEnumerable<Item> On(int each) => held.Where(item => SharedStash.PageOf(item) == each);

        (int Page, (int X, int Y) Cell)? spot = ItemMover.FreeCellNear(copy, On(page), grid, tile.Item) is { } near
            ? (page, near)
            : null;
        var others = Enumerable.Range(page + 1, SharedStash.LastPage - page)
            .Concat(Enumerable.Range(SharedStash.FirstPage, page - SharedStash.FirstPage));
        foreach (int other in others)
        {
            if (spot is not null)
                break;
            if (ItemMover.FirstFreeCell(copy, On(other), grid) is { } cell)
                spot = (other, cell);
        }

        if (spot is not { } landing)
            return Status = L("No free space in the shared stash.");

        if (Receive(copy, landing.Page, landing.Cell.X, landing.Cell.Y) is { } refused)
            return refused;

        Status = landing.Page == page ? L($"Copied {tile.Name}.") : L($"Copied {tile.Name} to page {landing.Page}.");
        return null;
    }

    /// <summary>
    /// A copy the editor made, put on a page here and selected - a duplicate, from these pages or
    /// from the character (ItemLanding). A new identifier, as a new item; ItemMade raised. Null when it landed, else why not. Not selected with <paramref name="select"/> false: a socket's
    /// copy, whose item stays the one being edited.
    /// </summary>
    public string? Receive(Item copy, int page, int x, int y, bool select = true)
    {
        copy.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        if (Adopt(copy, page, x, y) is { } refused)
            return refused;

        ItemMade?.Invoke(copy);

        // Adopt rebuilt the pages; the item itself is how to find its new tile.
        if (select && Pages.SelectMany(each => each.Items).FirstOrDefault(each => ReferenceEquals(each.Item, copy)) is { } made)
            _character.SelectedItem = made;

        return null;
    }

    /// <summary>
    /// Copies the editor made, each put at its place here - all of them or none: every one asked first
    /// (<see cref="RefusesAny"/>), then one rebuild, and none selected, the caller's picks being the selection.
    /// The inventory's multi-duplicate, which landed them one by one: a refusal half way left the first ones
    /// in, each landing rebuilt the pages and selected its copy beside the picks.
    /// </summary>
    public string? ReceiveAll(IReadOnlyList<(Item Copy, int Page, int X, int Y)> landing)
    {
        if (landing.Count == 0)
            return null;

        if (RefusesAny(landing.Select(each => each.Copy)) is { } no)
            return Status = no;

        foreach (var (copy, page, x, y) in landing)
        {
            copy.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);

            // RefusesAny has just let it through, and a copy turned away would lose no item.
            Adopt(copy, page, x, y, rebuild: false);
            ItemMade?.Invoke(copy);
        }

        Rebuild();
        return null;
    }

    /// <summary>How many of a material the tab holds. Nought with no stash open.</summary>
    public int MaterialCount(int slot) => _stash?.MaterialCount(slot) ?? 0;

    /// <summary>The code a material comes out of the tab as (<see cref="SharedStash.MaterialCode"/>); the stack with no stash open.</summary>
    public string MaterialCode(int slot) => _stash?.MaterialCode(slot) ?? Materials.CodeAt(slot);

    /// <summary>Whether the open stash's tab keeps what Season 14 added (<see cref="Materials.Season14Codes"/>) - its file is version 3.</summary>
    public bool KeepsSeason14 => _stash?.Holds(Materials.Count + 1) == true;

    /// <summary>The slot an item goes into in the open stash's tab, and how many it counts for; Season 13's 98 with no stash open.</summary>
    public (int Slot, int Quantity)? Takes(Item item) => _stash is { } stash ? stash.Takes(item) : Materials.Takes(item);

    /// <summary>The shared stash's gold; none with no stash open.</summary>
    public int Gold => (int)(_stash?.Gold ?? 0);

    /// <summary>The most the open shared stash holds: its season's.</summary>
    public int MostGold => _stash?.MostGold ?? SharedStash.GoldCap;

    /// <summary>
    /// Sets the shared gold, within its season's most (<see cref="SharedStash.MostGold"/>) - an edit like any other,
    /// written with the stash at the next save.
    /// </summary>
    public void SetGold(int gold)
    {
        if (_stash is null)
            return;

        _stash.SetGold(Math.Clamp(gold, 0, _stash.MostGold));
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(Gold));
    }

    /// <summary>Raised when a count in the materials tab moves, so the tab can be drawn again.</summary>
    public event Action? MaterialsChanged;

    /// <summary>
    /// Puts <paramref name="quantity"/> of a material into the tab. In memory until saved, and
    /// saved with the character, like any move between the two files.
    /// </summary>
    public string? AddMaterial(int slot, int quantity)
    {
        if (_stash is null)
            return Status = L("No shared stash open.");

        int now = _stash.MaterialCount(slot);
        if (now + quantity > Materials.FieldMaximum)
            return Status = L($"Materials tab max: {Materials.FieldMaximum:N0} {Materials.NameOf(slot)}.");

        _stash.SetMaterialCount(slot, now + quantity);
        MaterialsMoved();
        return null;
    }

    /// <summary>Sets how many of a material the tab holds, as typed in the item panel.</summary>
    public string? SetMaterial(int slot, int count)
    {
        if (_stash is null)
            return Status = L("No shared stash open.");

        if (count < 0 || count > Materials.FieldMaximum)
            return Status = L($"Materials tab holds 0 to {Materials.FieldMaximum:N0} {Materials.NameOf(slot)}.");

        _stash.SetMaterialCount(slot, count);
        MaterialsMoved();
        return null;
    }

    /// <summary>
    /// Empties the materials tab - every count to nought, as one staged change: in memory until
    /// saved, and Undo puts it back. Returns how many were in it.
    /// </summary>
    public int ClearMaterials()
    {
        if (_stash is null)
            return 0;

        int held = 0;
        for (int slot = 1; slot <= Materials.Every; slot++)
        {
            held += _stash.MaterialCount(slot);
            if (_stash.MaterialCount(slot) > 0)
                _stash.SetMaterialCount(slot, 0);
        }

        if (held > 0)
            MaterialsMoved();
        return held;
    }

    /// <summary>Takes <paramref name="quantity"/> of a material out of the tab.</summary>
    public string? TakeMaterial(int slot, int quantity)
    {
        if (_stash is null)
            return Status = L("No shared stash open.");

        int now = _stash.MaterialCount(slot);
        if (quantity > now)
            return Status = L($"Materials tab has only {now} {Materials.NameOf(slot)}.");

        _stash.SetMaterialCount(slot, now - quantity);
        MaterialsMoved();
        return null;
    }

    private void MaterialsMoved()
    {
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(CanSave));
        MaterialsChanged?.Invoke();
    }

    /// <summary>
    /// The bytes this stash would be written as, checked to read back, without touching the
    /// file.
    /// <para>
    /// A stash that will not read back is the most likely refusal in the whole save, so it
    /// is found here - while the character on disk is still untouched. That is the entire
    /// reason preparing and writing are two steps.
    /// </para>
    /// </summary>
    private byte[]? _prepared;

    string? ISaveParticipant.Prepare()
    {
        if (_stash is null)
            return L("No shared stash open.");

        // Refused rather than merged, the way the game would have it: it re-reads this
        // file on entering and never reconciles two versions of it. Only ever reached
        // when the stash has changes of its own, so editing a character alone is
        // unaffected.
        // GameSync asks which to keep before a save gets here; this is the backstop.
        if (ChangedOnDisk)
            return L("Shared stash changed in game. Require reopen.");

        // Left out for the writing and put straight back, as the character does.
        var items = _stash.Items.Items!;
        var leaving = LeavingNow();
        var kept = items.ToList();

        try
        {
            if (leaving.Count > 0)
                items.RemoveAll(item => leaving.Contains(item));

            byte[] bytes;
            try
            {
                bytes = _stash.Write();
            }
            finally
            {
                if (leaving.Count > 0)
                {
                    items.Clear();
                    items.AddRange(kept);
                    _stash.Items.Count = (ushort)items.Count;
                }
            }

            // Never hand over something that will not read back.
            string staging = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"{FileName}.{Guid.NewGuid():N}");

            File.WriteAllBytes(staging, bytes);
            try
            {
                _ = SharedStash.Read(staging);
            }
            finally
            {
                File.Delete(staging);
            }

            _prepared = bytes;
            _preparedWithout = leaving;
            return null;
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            _prepared = null;
            _preparedWithout = [];
            Status = ex.Message;
            return L($"Shared stash not saved: {ex.Message}");
        }
    }

    SaveHoldings? ISaveParticipant.Holdings() =>
        _prepared is null ? null : new SaveHoldings(HeldIn(_savedBytes), HeldIn(_prepared));

    private IReadOnlyDictionary<string, long> HeldIn(byte[]? bytes) =>
        SaveHoldings.Counted(bytes, read =>
        {
            var stash = SharedStash.Read(read, FileName);
            return SaveHoldings.Count(stash.Items.Items, stash.Gold,
                Enumerable.Range(1, Materials.Every).Select(slot => (slot, stash.MaterialCount(slot))));
        });

    string? ISaveParticipant.Write(string moment, SaveReport report)
    {
        if (_prepared is null)
            return L("Shared stash not prepared.");

        string path = Path;

        try
        {
            // Beside the character's own copy, under the same moment, so the two are one
            // state of everything rather than two that happen to be close in time. A
            // restore that took the character back without this would put back an item
            // that is still sitting in the stash.
            // Nothing to copy where the file has gone: the save puts it back.
            string? backup = File.Exists(path) ? SaveWriter.Backup(path, moment, SaveWriter.BackupFolderFor(_character.SavePath ?? path)) : null;
            SaveWriter.WriteSafely(path, _prepared);

            // And the stash as it was just written, one copy for the file whichever character saved it.
            SaveWriter.KeepAsWritten(path, _prepared, AsWrittenFolder(path));
            SaveWriter.Prune(path, SaveWriter.BackupsToKeep, SaveWriter.BackupFolderFor(_character.SavePath ?? path));
            _savedBytes = _prepared;

            var file = new FileInfo(path);
            _asRead = (file.LastWriteTimeUtc, file.Length);

            // A restore is done once written; the live file is what is on screen now.
            _readMoment = null;
            _prepared = null;
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(CanSave));

            // Written without them, so they go now.
            if (_preparedWithout.Count > 0 && _stash?.Items.Items is { } items)
            {
                items.RemoveAll(item => _preparedWithout.Contains(item));
                _stash.Items.Count = (ushort)items.Count;
                _preparedWithout = [];
                Rebuild();
            }

            // To the log, not the strip, as the character's own save: written with every autosave, it took the
            // place of what had just been done - "Deleted Mage Plate." gone the moment it was said.
            Log.Info("stash", $"Saved {Count} items. Backup: {System.IO.Path.GetFileName(backup)}.");
            return null;
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            Status = ex.Message;
            return L($"Shared stash not saved: {ex.Message}");
        }
    }

    /// <summary>
    /// Throws away what is staged and reads the file again, which is what an undo means for
    /// this.
    /// </summary>
    void ISaveParticipant.Revert()
    {
        // A backup's stash on screen is not undone - it is what that backup holds, and it
        // goes when the backup is closed.
        if (!HasUnsavedChanges)
            return;

        _prepared = null;
        HasUnsavedChanges = false;
        Load();
    }
}
