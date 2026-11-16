using System;
using System.Collections.Generic;
using System.Linq;

using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Services.Character;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// The vault's side of a move, held until the character is saved: what is going into the vault
/// and which rows are coming out of it.
/// <para>
/// Under the Move rule the vault is storage, not a library, so storing takes the item away and
/// withdrawing takes the row away. Neither happens on the click: a stored item stays where it
/// was, marked, and a withdrawn one lands marked while its row stays - so Undo has everything to
/// put back and nothing to repair. Save writes it all, in the order that can only ever leave a
/// duplicate: the vault's gains before the character and stash, its losses after them.
/// </para>
/// <para>
/// Two save participants for that, one either side of the files - see
/// <see cref="ISaveParticipant.Order"/>. The files leave out what is going (see
/// <see cref="CharacterSession.Leaves"/>) and let it go once written.
/// </para>
/// </summary>
public sealed class VaultMoves : ISaveJournal
{
    private readonly CharacterSession _character;
    private readonly SharedStashSession _stash;
    private readonly VaultService _vaults;
    private readonly SaveSet _saves;

    /// <summary>What is going in: the item itself and the folder it goes to.</summary>
    private readonly Dictionary<Item, (long? Folder, IReadOnlyList<string>? SortTo)> _storing = new(ReferenceEqualityComparer.Instance);

    /// <summary>What is coming out: the row, and the item it landed as.</summary>
    private readonly Dictionary<long, Item> _withdrawing = [];

    /// <summary>
    /// What a trade brings in and takes out with the vault beside it (trading works with
    /// the vault, so a user does not feel bounced back to the inventory): an item bought, which was never the
    /// character's, and a row sold, whose item lands nowhere. Written with the save as the rest is - the item
    /// in before the files, the row out after them, so the materials tab that pays or is paid is never ahead
    /// of the vault - and into the session's history, so Undo takes a purchase out and puts a sale back.
    /// </summary>
    private readonly Dictionary<Item, long?> _bought = new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<long> _sold = [];

    /// <summary>
    /// Gold moved and not yet saved, by purse: into the vault positive, out negative. The purse
    /// has its new amount already, as an edit the file will write; the vault's side waits for the
    /// save, in the order that can only ever duplicate gold, never lose it - what goes in is
    /// written before the files, what comes out after them (the design note's "Vault gold").
    /// </summary>
    private readonly Dictionary<string, long> _gold = [];

    public VaultMoves(CharacterSession character, SharedStashSession stash, VaultService vaults, SaveSet saves)
    {
        _character = character;
        _stash = stash;
        _vaults = vaults;
        _saves = saves;

        _character.Leaves = IsLeaving;
        _stash.Leaves = IsLeaving;

        // The shared stash read again: gold moved into or out of it has gone with its edits.
        _stash.Loaded += () =>
        {
            if (_gold.Remove(SharedPurse))
                Say();
        };

        // Switching vault is refused while anything here points into the open one.
        _vaults.HasPendingMoves = () => HasPending;

        _gains = new Gains(this);
        _losses = new Losses(this);
        saves.Takes(_gains);
        saves.Takes(_losses);
        saves.Journal = this;

        // Opening, reloading or closing a character drops every edit, and these with them: the
        // items they point at belong to a save that is no longer open.
        _character.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded) && !_character.IsLoaded)
                Drop();
        };
    }

    private readonly Gains _gains;
    private readonly Losses _losses;

    /// <summary>Raised when anything is staged, written or dropped, so the marks can be drawn again.</summary>
    public event Action? Changed;

    /// <summary>
    /// An item marked to go into the vault, or one come out of it: an action of its own, which
    /// autosave writes at once rather than after its pause (<see cref="AutoSave"/>).
    /// </summary>
    public event Action? ItemsMoved;

    /// <summary>What goes into the vault with the next save - still in its place until then.</summary>
    public IEnumerable<Item> Storing => _storing.Keys;

    /// <summary>The vault rows that come out with the next save - their items already on the character.</summary>
    public IReadOnlyCollection<long> Withdrawing => _withdrawing.Keys;

    /// <summary>Whether this item goes into the vault with the next save: the up arrow.</summary>
    public bool IsLeaving(Item item) => _storing.ContainsKey(item);

    /// <summary>Whether this item came out of the vault and waits for a save: the down arrow.</summary>
    public bool IsArriving(Item item) => _withdrawing.Values.Any(each => ReferenceEquals(each, item));

    /// <summary>Whether this row leaves the vault with the next save - withdrawn, or sold: the card's up arrow.</summary>
    public bool IsWithdrawing(long key) => _withdrawing.ContainsKey(key) || _sold.Contains(key);

    /// <summary>Whether any row waits to leave the vault.</summary>
    public bool HasWithdrawals => _withdrawing.Count > 0 || _sold.Count > 0;

    /// <summary>
    /// Saves whatever is waiting now rather than half a second on (set by <see cref="AutoSave"/>),
    /// for a step that needs the moves already written - deleting a card, emptying the vault.
    /// </summary>
    public Func<bool>? SaveNow { get; set; }

    /// <summary>
    /// Whether anything waits for a save: a mark on an item no longer held waits for nothing, as for the save
    /// itself (<see cref="Gains.HasChanges"/>), and gold staged goes to whichever vault is open at the save.
    /// </summary>
    public bool HasPending => _storing.Keys.Any(item => WhereIs(item).Held) || _withdrawing.Count > 0 || _bought.Count > 0 || _sold.Count > 0 || _gold.Count > 0;

    /// <summary>The shared stash's purse, as the gold log names it.</summary>
    public const string SharedPurse = "shared";

    /// <summary>Gold moved and not yet saved: into the vault positive.</summary>
    public long PendingGold => _gold.Values.Sum();

    /// <summary>The vault's gold as it will be once saved.</summary>
    public long GoldBalance => (_vaults.Store?.GoldBalance() ?? 0) + PendingGold;

    /// <summary>The gold log's purse for gold set on the vault page, moved from no purse at all.</summary>
    public const string EditedPurse = "edited";

    /// <summary>
    /// Gold put into the vault or taken out of it on the vault page's stepper (a stepper only
    /// on the vault page; the inventory only transfers) - made or lost, not moved, so it is written to the vault at once
    /// and touches no game file: allowed while the game runs, as vault work is. Logged under no character, so the
    /// purse of a file's, so no save's journal counts it. Never below nothing. Null when written, else why not.
    /// </summary>
    public string? EditGold(long amount)
    {
        if (_vaults.Store is not { } store)
            return L("Vault not open.");

        amount = Math.Max(amount, -GoldBalance);
        if (amount == 0)
            return null;

        try
        {
            store.RecordGold([new GoldMove(EditedPurse, amount)]);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return L($"Vault gold not saved: {problem.Message}");
        }

        Changed?.Invoke();
        _vaults.SayGoldChanged();
        return null;
    }

    /// <summary>
    /// Stages gold moved between a purse and the vault: into the vault positive. The caller has
    /// changed the purse already.
    /// </summary>
    public void StageGold(string purse, long amount)
    {
        if (amount == 0)
            return;

        _gold[purse] = _gold.GetValueOrDefault(purse) + amount;
        if (_gold[purse] == 0)
            _gold.Remove(purse);

        Say();
    }

    /// <summary>The gold moves to write, one per purse, in or out.</summary>
    private List<GoldMove> GoldMoves(bool into) =>
        [.. _gold.Where(each => into ? each.Value > 0 : each.Value < 0)
                 .Select(each => new GoldMove(each.Key, each.Value))];

    /// <summary>Writes one direction's gold into the vault and lets it go from the staging.</summary>
    private string? WriteGold(bool into, SaveReport report)
    {
        var moves = GoldMoves(into);
        if (moves.Count == 0)
            return null;

        if (_vaults.Store is not { } store)
            return L("Vault gold not saved: vault not open.");

        try
        {
            store.RecordGold(moves);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return L($"Vault gold not saved: {problem.Message}");
        }

        foreach (var move in moves)
            _gold.Remove(move.Purse);

        report.Gold.AddRange(moves);
        return null;
    }

    /// <summary>
    /// Marks an item to go into the vault with the next save: into <paramref name="folder"/>, or
    /// where smart sort sent it (<paramref name="sortTo"/>) - a category whose folder is found or
    /// made at the save, so a store taken back leaves no empty folder behind.
    /// </summary>
    public void Store(Item item, long? folder, IReadOnlyList<string>? sortTo = null)
    {
        _storing[item] = (folder, sortTo);
        Say();
        Moved();
    }

    /// <summary>Takes a mark back: the item stays.</summary>
    public void Keep(Item item)
    {
        if (_storing.Remove(item))
            Say();
    }

    /// <summary>Records that a row came out as this item, to leave the vault with the next save.</summary>
    public void Withdrew(long key, Item item)
    {
        _withdrawing[key] = item;
        Say();
        Moved();
    }

    /// <summary>
    /// An item bought, to go into the vault with the next save: into <paramref name="folder"/>. It is held by
    /// nothing - not the character's, not the stash's - so nothing leaves a file for it.
    /// </summary>
    public void Bought(Item item, long? folder)
    {
        _bought[item] = folder;
        Say();
        Moved();
    }

    /// <summary>A row sold, to leave the vault with the next save: its item lands nowhere.</summary>
    public void Sold(long key)
    {
        _sold.Add(key);
        Say();
        Moved();
    }

    /// <summary>Forgets everything staged. The vault was never touched, so nothing needs undoing there.</summary>
    public void Drop()
    {
        if (!HasPending && _storing.Count == 0)
            return;

        _storing.Clear();
        _withdrawing.Clear();
        _bought.Clear();
        _sold.Clear();
        _gold.Clear();
        Say();
    }

    private void Say()
    {
        if (_together > 0)
        {
            _saidLater = true;
            return;
        }

        Changed?.Invoke();
        _saves.SomethingChanged();
    }

    private void Moved()
    {
        if (_together > 0)
        {
            _movedLater = true;
            return;
        }

        ItemsMoved?.Invoke();
    }

    private int _together;
    private bool _saidLater, _movedLater;

    /// <summary>
    /// Marks made together, told once when the scope ends: Store all marked each item on its own and every mark
    /// told both pages and the save set, items times tiles of work for one click.
    /// </summary>
    public IDisposable Together()
    {
        _together++;
        return new Ending(this);
    }

    private sealed class Ending(VaultMoves moves) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (_ended || --moves._together > 0)
            {
                _ended = true;
                return;
            }

            _ended = true;
            if (moves._saidLater)
            {
                moves._saidLater = false;
                moves.Say();
            }

            if (moves._movedLater)
            {
                moves._movedLater = false;
                moves.Moved();
            }
        }
    }

    // ---- the move journal ------------------------------------------------------------------------

    /// <summary>The batch the save in progress wrote down, or null; and the history walk's, apart - its own save runs inside it.</summary>
    private long? _batch, _historyBatch;

    /// <summary>
    /// Writes down what this save moves across files - items in and out of the vault, gold -
    /// before the first file is written. Each side
    /// counted before and after, so a save cut short can be told from one that finished.
    /// </summary>
    string? ISaveJournal.Open(IReadOnlyList<ISaveParticipant> acting)
    {
        _batch = null;
        bool gains = acting.Contains(_gains), losses = acting.Contains(_losses);
        if (!gains && !losses)
            return null;

        var intoVault = new List<(Item Item, VaultCapture Captured, bool InStash, long? Folder)>();
        if (gains)
            intoVault.AddRange(_gains.Prepared);

        var outOfVault = new List<(Item Item, bool InStash, VaultRow Row)>();
        if (losses && _vaults.Store is { } store)
        {
            foreach (var row in store.RowsOf(_losses.Prepared))
            {
                if (_withdrawing.TryGetValue(row.Key, out var item))
                    outOfVault.Add((item, WhereIs(item).InStash, row));
            }
        }

        var gold = _gold.Where(each => each.Value > 0 ? gains : losses)
                        .Select(each => (each.Key, each.Value))
                        .ToList();

        return Journal(intoVault, outOfVault, gold, _character.PreparedBytes, _stash.PreparedBytes, out _batch);
    }

    string? ISaveJournal.Close(bool completed) => CloseJournal(ref _batch, completed);

    /// <summary>Closes the history walk's batch, as <see cref="ISaveJournal.Close"/> a save's.</summary>
    public string? CloseHistoryJournal(bool completed) => CloseJournal(ref _historyBatch, completed);

    /// <summary>
    /// The same for the session's history going back or forward: the rows it puts back into
    /// the vault, those it takes out, the gold, and the files as they will be.
    /// </summary>
    public string? JournalHistory(IReadOnlyList<VaultRow> intoVault, IReadOnlyList<VaultRow> outOfVault,
                                  IReadOnlyList<GoldMove> gold, byte[]? characterAfter, byte[]? stashAfter)
    {
        _historyBatch = null;
        if (intoVault.Count == 0 && outOfVault.Count == 0 && gold.Count == 0)
            return null;

        // A row carries no item; the one it is, read from its blob, is what the files count.
        var into = new List<(Item Item, VaultCapture Captured, bool InStash, long? Folder)>();
        var outOf = new List<(Item Item, bool InStash, VaultRow Row)>();
        if (_vaults.Store is { } store)
        {
            foreach (var row in intoVault)
            {
                if (ItemOf(store, row) is { } item)
                    into.Add((item, new VaultCapture(item, row.Blob, [], row.OriginId), false, row.FolderId));
            }

            foreach (var row in outOfVault)
            {
                if (ItemOf(store, row) is { } item)
                    outOf.Add((item, false, row));
            }
        }

        // A purse's gold going in apart from its gold coming out: the two are written at different moments.
        var purses = gold.GroupBy(move => (move.Purse, In: move.Amount > 0))
                         .Select(group => (group.Key.Purse, group.Sum(move => move.Amount)))
                         .ToList();

        return Journal(into, outOf, purses, characterAfter, stashAfter, out _historyBatch);
    }

    /// <summary>
    /// Clears the batch once every file is written; one cut short is put right at once, and
    /// what that found is returned, for the message.
    /// </summary>
    private string? CloseJournal(ref long? open, bool completed)
    {
        if (open is not { } batch)
            return null;

        open = null;
        try
        {
            if (completed)
            {
                _vaults.Store?.ClearJournal(batch);
                return null;
            }

            var found = _vaults.RecoverMoves(say: false);
            _vaults.SayChanged();
            return found.Count > 0 ? string.Join(" ", found) : null;
        }
        catch (Exception problem)
        {
            // Left in the journal: the next time the vault opens, it is checked then.
            Log.Caught(problem);
            return null;
        }
    }

    private Item? ItemOf(PD2SPCompanion.Core.Collection.Vault store, VaultRow row)
    {
        try
        {
            var item = ItemBlob.Restore(row.Blob, store.StatIdBits);
            if (row.OriginId is { } id)
                item.Id = id;
            return item;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Log.Warning("vault", $"{row.Name}: not in this step's journal, its item did not read.");
            return null;
        }
        finally
        {
            _character.UseMyLayout();
        }
    }

    private string? Journal(
        IReadOnlyList<(Item Item, VaultCapture Captured, bool InStash, long? Folder)> intoVault,
        IReadOnlyList<(Item Item, bool InStash, VaultRow Row)> outOfVault,
        IReadOnlyList<(string Purse, long Amount)> gold,
        byte[]? characterAfter, byte[]? stashAfter, out long? batch)
    {
        batch = null;
        if (intoVault.Count == 0 && outOfVault.Count == 0 && gold.Count == 0)
            return null;

        if (_vaults.Store is not { } store)
            return null;

        try
        {
            // Each file before - as it is on disk - and after - as it will be written.
            var character = Sides(_character.SavedBytes, characterAfter, stash: false);
            (string Path, SaveCounts Before, SaveCounts After)? stash =
                _stash.IsLoaded && _stash.SavedBytes is { } stashBytes ? Sides(stashBytes, stashAfter, stash: true) : null;

            string name = _character.CharacterName;
            string collection = VaultLibrary.VaultName;
            var moves = new List<PendingMove>();

            foreach (var (item, captured, inStash, folder) in intoVault)
            {
                var (path, before, after) = FileFor(item, inStash, character, stash);
                uint? id = item.IsCompact ? null : item.Id;
                long held = store.CountHeld(captured.OriginId, captured.Blob);
                moves.Add(new PendingMove(MoveRecovery.Store, path, name, ItemPresenter.Describe(item).Name, id, item.Code,
                                          captured.Blob, collection, null, folder, "", 0,
                                          before.Count(id, item.Code), after.Count(id, item.Code), held, held + 1));
            }

            foreach (var (item, inStash, row) in outOfVault)
            {
                var (path, before, after) = FileFor(item, inStash, character, stash);
                uint? id = item.IsCompact ? null : item.Id;
                moves.Add(new PendingMove(MoveRecovery.Withdraw, path, name, row.Name, id, item.Code,
                                          row.Blob, collection, row.Key, row.FolderId, "", 0,
                                          before.Count(id, item.Code), after.Count(id, item.Code), 1, 0));
            }

            // The vault's gold is written twice at most: what goes in before the files, in one write, and
            // what comes out after them, in another. Each move is marked by the write that carries it.
            long written = store.GoldWrites();
            int goingIn = gold.Any(each => each.Amount > 0) ? 1 : 0;
            foreach (var (purse, amount) in gold)
            {
                var sides = purse == SharedPurse ? stash : character;
                if (sides is null)
                    continue;
                var (path, before, after) = sides.Value;

                long reached = written + (amount > 0 ? 1 : goingIn + 1);
                moves.Add(new PendingMove(MoveRecovery.Gold, path, name, "gold", null, "", null, collection, null, null,
                                          purse, amount, before.Purse(purse), after.Purse(purse), reached - 1, reached));
            }

            batch = store.JournalMoves(moves);
            return null;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return L($"Not saved: {problem.Message}");
        }
        finally
        {
            _character.UseMyLayout();
        }
    }

    /// <summary>A file counted before and after the save; after is before when the file is not written.</summary>
    private (string Path, SaveCounts Before, SaveCounts After) Sides(byte[] before, byte[]? after, bool stash)
    {
        // Counted once a byte array: "before" is the file as last saved - the same bytes save after save until it
        // is written again, and then the last save's "after". Each count is a
        // whole file read.
        SaveCounts Count(byte[] bytes)
        {
            if (_counted.TryGetValue(bytes, out var known))
                return known;

            SaveCounts counted;
            if (stash)
                counted = SaveCounts.Of(SharedStash.Read(bytes, _stash.FileName));
            else
            {
                _character.UseMyLayout();
                counted = SaveCounts.Of(D2SLib.Core.ReadD2S(bytes));
            }

            _counted.AddOrUpdate(bytes, counted);
            return counted;
        }

        var counted = Count(before);
        return (stash ? _stash.Path : _character.SavePath ?? "", counted, after is null ? counted : Count(after));
    }

    /// <summary>What each file's bytes held, counted: by the array, so a file written again is counted again.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], SaveCounts> _counted = new();

    /// <summary>
    /// The file an item is in, of the two: the one whose count of it changes, or else the one it
    /// is held by.
    /// </summary>
    private static (string Path, SaveCounts Before, SaveCounts After) FileFor(
        Item item, bool inStash,
        (string Path, SaveCounts Before, SaveCounts After) character,
        (string Path, SaveCounts Before, SaveCounts After)? stash)
    {
        uint? id = item.IsCompact ? null : item.Id;
        bool Changes((string Path, SaveCounts Before, SaveCounts After) file) =>
            file.Before.Count(id, item.Code) != file.After.Count(id, item.Code);

        if (stash is { } shared && (Changes(shared) || (inStash && !Changes(character))))
            return shared;
        return character;
    }

    /// <summary>Whether an item is still held, and by which: a stored item may have been moved or deleted since.</summary>
    private (bool Held, bool InStash) WhereIs(Item item) =>
        _character.Holds(item) ? (true, false)
        : _stash.Holds(item) ? (true, true)
        : (false, false);

    /// <summary>
    /// The items going in, written before any file. Captured - copied, normalised and checked to
    /// read back - in the prepare, so a refusal stops the whole save while nothing is touched.
    /// </summary>
    private sealed class Gains(VaultMoves moves) : ISaveParticipant
    {
        private List<(VaultCapture Item, long? Folder, IReadOnlyList<string>? SortTo)> _prepared = [];
        private List<(Item Item, VaultCapture Captured, bool InStash, long? Folder)> _journal = [];

        /// <summary>What the prepare captured, with the items themselves, for the journal.</summary>
        public IReadOnlyList<(Item Item, VaultCapture Captured, bool InStash, long? Folder)> Prepared => _journal;

        public string What => L("The vault");

        public int Order => -1;

        public bool HasChanges => moves._storing.Keys.Any(item => moves.WhereIs(item).Held)
                                  || moves._bought.Count > 0
                                  || moves._gold.Values.Any(amount => amount > 0);

        public void UseMyLayout() => moves._character.UseMyLayout();

        public string? Prepare()
        {
            _prepared = [];
            _journal = [];

            if (moves._vaults.Store is not { } store)
                return L($"Not saved: vault not open. {moves._vaults.Trouble}").Trim();

            try
            {
                foreach (var (item, (folder, sortTo)) in moves._storing)
                {
                    var (held, inStash) = moves.WhereIs(item);
                    if (!held)
                        continue;

                    // The layout the item was read with: a character's and a stash's differ.
                    if (inStash)
                        moves._stash.UseMyLayout();
                    else
                        moves._character.UseMyLayout();

                    if (store.Capture(item, out string? refused) is not { } captured)
                        return L($"Not saved: vault item {refused}.");

                    _prepared.Add((captured, folder, sortTo));
                    _journal.Add((item, captured, inStash, folder));
                }

                // What a trade bought: made for the character's save, and in no file - so not in the
                // journal, which counts an item on both sides of a move.
                moves._character.UseMyLayout();
                foreach (var (item, folder) in moves._bought)
                {
                    if (store.Capture(item, out string? refused) is not { } captured)
                        return L($"Not saved: vault item {refused}.");

                    _prepared.Add((captured, folder, null));
                }
            }
            finally
            {
                moves._character.UseMyLayout();
            }

            return null;
        }

        public string? Write(string moment, SaveReport report)
        {
            if (moves._vaults.Store is not { } store)
                return L("Vault not open.");

            try
            {
                // Sorted in by its kind's folder - unless that folder is locked: then where it was sent
                // unsorted, as a kind with no rule goes (Vault.IsLocked).
                var locked = store.LockedFolders();

                // A category's folder found once a store: each item asked again read every folder and link of
                // the vault - Store all over a full stash, hundreds of times.
                var sortedBy = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                long SortedInto(IReadOnlyList<string> path)
                {
                    string key = string.Join("|", path);
                    if (!sortedBy.TryGetValue(key, out long folder))
                        sortedBy[key] = folder = store.FolderFor(path);
                    return folder;
                }

                var into = _prepared.Select(each => each.SortTo is { } path && SortedInto(path) is var sorted && !locked.Contains(sorted)
                    ? sorted : each.Folder).ToList();
                var keys = store.AddCaptured([.. _prepared.Select((each, n) => (each.Item, into[n]))]);

                report.Added.AddRange(store.RowsOf(keys));
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                return L($"Vault not saved: {problem.Message}");
            }

            // In the vault now. The files let the items go as they are written; should one of
            // them fail, what is left is a copy in both - never an item in neither. Let go before the gold:
            // marks still standing after a gold failure stored every one again at the next save, unsaid.
            _prepared = [];
            _journal = [];
            moves._storing.Clear();
            moves._bought.Clear();

            // Gold going in, before the purses it left are written.
            string? goldRefused = moves.WriteGold(into: true, report);
            moves._vaults.SayChanged();
            moves.Changed?.Invoke();
            return goldRefused;
        }

        public void Revert() => moves.Drop();
    }

    /// <summary>
    /// The rows coming out, deleted after every file is written. Only rows whose item is still
    /// held: an arrival deleted before the save leaves its row where it was, so nothing a save
    /// does can remove the last copy of an item.
    /// </summary>
    private sealed class Losses(VaultMoves moves) : ISaveParticipant
    {
        private List<long> _prepared = [];

        /// <summary>The rows the prepare chose, for the journal.</summary>
        public IReadOnlyList<long> Prepared => _prepared;

        public string What => L("The vault");

        public int Order => 1;

        public bool HasChanges => moves._withdrawing.Count > 0 || moves._sold.Count > 0 || moves._gold.Values.Any(amount => amount < 0);

        public void UseMyLayout() => moves._character.UseMyLayout();

        public string? Prepare()
        {
            if (moves._vaults.Store is null)
                return L($"Not saved: vault not open. {moves._vaults.Trouble}").Trim();

            // And the rows sold, which land nowhere: the journal passes them by, having no item
            // on the other side to count (see ISaveJournal.Open).
            _prepared = moves._withdrawing
                .Where(each => moves.WhereIs(each.Value).Held)
                .Select(each => each.Key)
                .Concat(moves._sold)
                .Distinct()
                .ToList();
            return null;
        }

        public string? Write(string moment, SaveReport report)
        {
            if (moves._vaults.Store is not { } store)
                return L("Vault not open.");

            try
            {
                // Whole, before they go: an undo puts them back as they were.
                var leaving = store.RowsOf(_prepared);
                store.Remove(_prepared);
                report.Removed.AddRange(leaving);
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                return L($"Vault not saved: {problem.Message}");
            }

            // Gold coming out, now the purses holding it are written.
            if (moves.WriteGold(into: false, report) is { } goldRefused)
                return goldRefused;

            _prepared = [];
            moves._withdrawing.Clear();
            moves._sold.Clear();
            moves._vaults.SayChanged();
            moves.Changed?.Invoke();
            return null;
        }

        public void Revert() => moves.Drop();
    }
}
