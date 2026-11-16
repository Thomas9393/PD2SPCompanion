using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>One line of the history list: what an action was, and whether it is where the files are now.</summary>
public sealed partial class HistoryRow(int index, string label, DateTime at) : ObservableObject
{
    public int Index { get; } = index;
    public string Label { get; } = label;
    public string When { get; } = at.ToString("HH:mm:ss");

    /// <summary>The state the files are in now.</summary>
    [ObservableProperty] private bool _isCurrent;

    /// <summary>Undone - redo brings it back, the next action drops it.</summary>
    [ObservableProperty] private bool _isUndone;
}

/// <summary>
/// The open character's history, for this session only: every save the autosave makes is a state, and undo,
/// redo and revert go between them - revert all the way back to the one breakpoint, the character
/// as it was opened.
/// <para>
/// A state is the files' own bytes, not an inverse for every command. The editor changes its
/// model in hundreds of places, and one way back - putting the bytes back and saving - cannot miss
/// one. Only the vault is not a file of the set: what each save put in or took out of it is kept
/// whole (<see cref="SaveReport"/>) and done the other way round.
/// </para>
/// <para>
/// Going back writes as a save does: rows returned to the vault before the files, rows taken out
/// of it after - so a failure part-way leaves a copy in both, never an item in neither.
/// </para>
/// </summary>
public sealed partial class SessionHistory : ObservableObject
{
    /// <summary>
    /// How many states are kept; past it the oldest go, the earliest kept becoming the breakpoint. The
    /// Settings page's "Undo steps" (EditorSettings.UndoSteps, handed over by Apply); lowered, the
    /// surplus goes at the next step.
    /// </summary>
    public static int MostKept { get; set; } = EditorSettings.DefaultUndoSteps;

    private sealed class Entry
    {
        public required string Label { get; init; }
        public required DateTime At { get; init; }
        public byte[]? Character { get; init; }
        public byte[]? Stash { get; init; }

        /// <summary>The compendium's file as this state had it (null bytes: no file yet), or null where none was open.</summary>
        public Kept? Compendium { get; init; }

        /// <summary>The rows this state's save put into the vault, and where each is now.</summary>
        public List<(VaultRow Row, long Key)> Added { get; } = [];

        /// <summary>The rows it took out, and - while undone, put back - where each is now.</summary>
        public List<(VaultRow Row, long Key)> Removed { get; } = [];

        public IReadOnlyList<GoldMove> Gold { get; init; } = [];

        /// <summary>The trades this state's save wrote into the ledger: out of it again while undone.</summary>
        public IReadOnlyList<TradeRecord> Trades { get; init; } = [];

        /// <summary>
        /// A step the vault took by itself - a folder, a card moved or deleted - rather than a save:
        /// the ranges of its logs that take it back, or, while undone, that redo it. Null for a save.
        /// </summary>
        public List<(VaultUndo Log, long From, long To)>? Vault { get; init; }

        /// <summary>
        /// The item this step's changes were made to, where it is nothing but changes to one item kept at
        /// once (CharacterSession.TakeEditStreak): the next step of the same item is added to this one.
        /// </summary>
        public string? EditOf { get; init; }
    }

    /// <summary>A file's bytes as a state kept them - null bytes for a file not made yet.</summary>
    private sealed record Kept(byte[]? Bytes);

    /// <summary>The compendium as it stands on disk now, for a state; null where none is open.</summary>
    private Kept? CompendiumNow => _compendium is { IsLoaded: true } open ? new Kept(open.SavedBytes) : null;

    /// <summary>Each vault log's changes up to here belong to a step already, or to none.</summary>
    private readonly Dictionary<VaultUndo, long> _consumed = [];

    /// <summary>The strip's message when the last step was made: one posted since names the next.</summary>
    private object? _seenMessage;

    /// <summary>Set by the autosave while it writes: the vault's writes in a save are the save's.</summary>
    public bool Saving { get; set; }

    private bool _vaultStepPending;

    private readonly CharacterSession _session;
    private readonly SharedStashSession _stash;
    private readonly SaveSet _saves;
    private readonly VaultService _vaults;
    private readonly VaultMoves _moves;
    private readonly CompendiumSession? _compendium;
    private readonly List<Entry> _entries = [];
    private int _at = -1;

    /// <summary>
    /// What the saves since the last step did. A save with no step of its own - the Save button's, one cut short - is part
    /// of the next, which undoes the files past it too. Let go of where a character opens: a trade written with no step to
    /// own it stays in the ledger, and the next character's first step is not its owner.
    /// </summary>
    private readonly List<SaveReport> _sinceStep = [];

    public SessionHistory(CharacterSession session, SharedStashSession stash, SaveSet saves, VaultService vaults, VaultMoves moves,
                          CompendiumSession? compendium = null)
    {
        _compendium = compendium;
        _session = session;
        _stash = stash;
        _saves = saves;
        _vaults = vaults;
        _moves = moves;
        saves.Written += _sinceStep.Add;

        // A session is one character, opened and closed. A backup on screen has no history: saving
        // it is a restore, asked about apart.
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName != nameof(CharacterSession.IsLoaded))
                return;

            if (session.IsLoaded && !session.IsViewingBackup)
                Begin();
            else
                Clear();
        };

        // The vault's own steps (one history, whatever keeps the data). A write
        // outside a save or an undo becomes a step once the action that made it is over - posted, so
        // the several rows one action writes are one step.
        vaults.Written += () =>
        {
            if (_at < 0 || IsMoving || Saving || _vaultStepPending)
                return;

            _vaultStepPending = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(RecordVault, Avalonia.Threading.DispatcherPriority.Background);
        };

        // Bookkeeping written quietly is no step, nor part of the next.
        vaults.WrittenQuietly += () =>
        {
            if (!IsMoving && !Saving && !_vaultStepPending)
                Consume();
        };

        // Another collection or mode: the steps' logs went with it, so the history starts again.
        vaults.UndoLost += () =>
        {
            _consumed.Clear();
            if (_at >= 0)
                Begin();
        };
    }

    private IEnumerable<VaultUndo> Logs() =>
        new[] { _vaults.StoreUndo }.OfType<VaultUndo>();

    /// <summary>Every log's changes so far are accounted for: a save's, an undo's, or the start.</summary>
    private void Consume()
    {
        foreach (var log in Logs())
            _consumed[log] = log.Mark;
        _seenMessage = _session.Messages.Latest;
    }

    /// <summary>
    /// A vault step's name: what the strip said, when it said something for this action - else what
    /// the step did, read off its logs ("Folder made", "2 items deleted from vault").
    /// </summary>
    private string VaultLabel(IReadOnlyList<(VaultUndo Log, long From, long To)> ranges)
    {
        if (_session.Messages.Latest is { } latest && !ReferenceEquals(latest, _seenMessage))
            return latest.Text;

        var did = ranges.SelectMany(range => range.Log.Summary(range.From, range.To)).ToList();
        int Count(string table, string done) => did.Where(each => each.Table == table && each.Done == done).Sum(each => each.Count);
        static string Items(int n) => Plural(n, "1 item", $"{n} items");

        if (Count("item", "added") is > 0 and var added)
            return L($"{Items(added)} added to vault");
        if (Count("item", "deleted") is > 0 and var deleted)
            return L($"{Items(deleted)} deleted from vault");
        if (Count("folder", "added") > 0)
            return L("Folder made");
        if (Count("folder", "deleted") > 0)
            return L("Folder deleted");
        if (Count("folder", "changed") > 0)
            return L("Folder changed");
        if (Count("item", "changed") is > 0 and var changed)
            return L($"{Items(changed)} changed in vault");
        return L("Vault changed");
    }

    /// <summary>
    /// The vault changed by itself since the last step: that change becomes a step, named by what the
    /// strip last said. The files are as the step before left them.
    /// </summary>
    private void RecordVault()
    {
        _vaultStepPending = false;
        if (_at < 0 || IsMoving || Saving)
            return;

        var ranges = Logs()
            .Select(log => (Log: log, From: _consumed.GetValueOrDefault(log), To: log.Mark))
            .Where(range => range.To > range.From)
            .ToList();
        if (ranges.Count == 0)
            return;

        if (_at < _entries.Count - 1)
            _entries.RemoveRange(_at + 1, _entries.Count - _at - 1);

        // More changes to the vault item the last step changed: that step, its logs reaching further (as a
        // save's above). The logs' ranges follow one another, so the step's begins where it began.
        string? editOf = _session.TakeEditStreak();
        if (editOf is not null && _at > 0 && _entries[_at] is { Vault: { } earlier } last && last.EditOf == editOf)
        {
            var reach = ranges
                .Select(range => (range.Log, From: earlier.Where(each => each.Log == range.Log).Select(each => each.From).DefaultIfEmpty(range.From).Min(), range.To))
                .Concat(earlier.Where(each => ranges.All(range => range.Log != each.Log)))
                .ToList();

            _entries[_at] = new Entry
            {
                Label = last.Label,
                At = DateTime.Now,
                Character = last.Character,
                Stash = last.Stash,
                Compendium = last.Compendium,
                Vault = reach,
                EditOf = editOf,
            };
            Consume();
            Say();
            return;
        }

        var before = _entries[_at];
        _entries.Add(new Entry
        {
            Label = VaultLabel(ranges),
            At = DateTime.Now,
            Character = before.Character,
            Stash = before.Stash,
            Compendium = before.Compendium,
            Vault = ranges,
            EditOf = editOf,
        });
        _at = _entries.Count - 1;
        Consume();
        Trim();
        Say();
    }

    /// <summary>The oldest go first, past the most kept; what their logs held goes with them.</summary>
    private void Trim()
    {
        while (_entries.Count > MostKept)
        {
            _entries.RemoveAt(0);
            _at--;
        }

        foreach (var log in Logs())
        {
            long oldest = _entries.SelectMany(entry => entry.Vault ?? []).Where(range => range.Log == log)
                                  .Select(range => range.From).DefaultIfEmpty(_consumed.GetValueOrDefault(log)).Min();
            log.Forget(oldest);
        }
    }

    public ObservableCollection<HistoryRow> Rows { get; } = [];

    public bool CanUndo => _at > 0 && !IsMoving;

    public bool CanRedo => _at >= 0 && _at < _entries.Count - 1 && !IsMoving;

    /// <summary>Whether an undo or redo is writing: its save is not a new state.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>What undo takes back, and its key under it (no tooltip named a key).</summary>
    public string UndoHint => ViewModels.ShortcutText.With(CanUndo ? L($"Undo {_entries[_at].Label}") : L("Nothing to undo"), L("Ctrl + Z"));

    public string RedoHint => ViewModels.ShortcutText.With(CanRedo ? L($"Redo {_entries[_at + 1].Label}") : L("Nothing to redo"), L("Ctrl + Y"));

    /// <summary>The history started again from the files as they stand: a character opened, or a backup restored.</summary>
    public void Restart() => Begin();

    private void Begin()
    {
        // The compendium opens with the character too: read before the first state is taken, whichever heard first.
        if (_compendium is { IsLoaded: false } && _session.IsLoaded)
            _compendium.Load();

        _entries.Clear();
        _sinceStep.Clear();
        Consume();
        _entries.Add(new Entry
        {
            Label = L($"Opened {_session.CharacterName}"),
            At = DateTime.Now,
            Character = _session.SavedBytes,
            Stash = _stash.SavedBytes,
            Compendium = CompendiumNow,
        });
        _at = 0;
        Say();
    }

    private void Clear()
    {
        _entries.Clear();
        _at = -1;
        Consume();
        Say();
    }

    /// <summary>
    /// A save has just been made: the state it left, named by what was done. A state undone is
    /// dropped by a new one, as an editor's redo is.
    /// </summary>
    public void Record(string label)
    {
        if (IsMoving || _at < 0)
            return;

        var since = _sinceStep.ToList();
        _sinceStep.Clear();
        bool vaultMoved = since.Any(save => save.Added.Count > 0 || save.Removed.Count > 0 || save.Gold.Count > 0);
        string? editOf = _session.TakeEditStreak();
        List<TradeRecord> trades = [.. since.SelectMany(save => save.Trades)];

        if (_at < _entries.Count - 1)
            _entries.RemoveRange(_at + 1, _entries.Count - _at - 1);

        // More changes to the item the last step changed, and nothing else: that step, as it now stands
        // (slow edits to one item are one history step, not one a number). Undo then
        // goes to before the first of them. A step is never the first state, which is the breakpoint.
        if (editOf is not null && !vaultMoved && trades.Count == 0 && _at > 0 && _entries[_at] is { Vault: null } last && last.EditOf == editOf
            && last.Added.Count == 0 && last.Removed.Count == 0 && last.Gold.Count == 0 && last.Trades.Count == 0)
        {
            _entries[_at] = new Entry
            {
                Label = last.Label,
                At = DateTime.Now,
                Character = _session.SavedBytes,
                Stash = _stash.SavedBytes,
            Compendium = CompendiumNow,
                EditOf = editOf,
            };
            Consume();
            Say();
            return;
        }

        var entry = new Entry
        {
            Label = label,
            At = DateTime.Now,
            Character = _session.SavedBytes,
            Stash = _stash.SavedBytes,
            Compendium = CompendiumNow,
            Gold = [.. since.SelectMany(save => save.Gold)],
            Trades = trades,
            EditOf = !vaultMoved && trades.Count == 0 ? editOf : null,
        };
        entry.Added.AddRange(since.SelectMany(save => save.Added).Select(row => (row, row.Key)));
        entry.Removed.AddRange(since.SelectMany(save => save.Removed).Select(row => (row, row.Key)));

        _entries.Add(entry);
        _at = _entries.Count - 1;

        // The vault rows this save wrote are its own, taken back by its rows above, not its log.
        Consume();

        // The oldest go first. What was the next state becomes the breakpoint: its vault change
        // is done and cannot be undone past.
        Trim();

        Say();
    }

    public string? Undo() => CanUndo ? GoTo(_at - 1) : null;

    public string? Redo() => CanRedo ? GoTo(_at + 1) : null;

    /// <summary>
    /// To the state at <paramref name="target"/>, a step at a time: a save's step puts its files
    /// back and saves them, a vault step replays its logs. Null when done, else why not - the state
    /// left at the last step reached.
    /// </summary>
    public string? GoTo(int target)
    {
        if (target < 0 || target >= _entries.Count || target == _at || IsMoving)
            return null;

        while (_at != target)
        {
            int next = target < _at ? _at - 1 : _at + 1;
            var step = target < _at ? _entries[_at] : _entries[next];
            if ((step.Vault is not null ? StepVault(next, step) : StepFiles(next)) is { } failed)
                return failed;
        }

        return null;
    }

    /// <summary>
    /// A vault step, taken back or done again: its logs replayed, newest first, and the ranges that
    /// replay logged kept in their place - an undo's redo, a redo's undo.
    /// </summary>
    private string? StepVault(int next, Entry step)
    {
        bool back = next < _at;
        IsMoving = true;
        try
        {
            var ranges = step.Vault!;
            var order = back ? Enumerable.Range(0, ranges.Count).Reverse() : Enumerable.Range(0, ranges.Count);
            foreach (int n in order)
            {
                var (log, from, to) = ranges[n];
                var replay = log.Apply(from, to);
                ranges[n] = (log, replay.From, replay.To);
            }

            _at = next;
            _vaults.SayChanged();
            return null;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return back ? L($"Not undone: {problem.Message}") : L($"Not redone: {problem.Message}");
        }
        finally
        {
            Consume();
            IsMoving = false;
            Say();
        }
    }

    /// <summary>
    /// The files put in the state at <paramref name="target"/>, and saved. Null when done, else why
    /// not - the state left where it was, or, a write failing part-way, as the save says.
    /// </summary>
    private string? StepFiles(int target)
    {
        // Asked before anything moves, the vault's rows too: the game running or the patch guard's hold. The save
        // set asks again before it writes (an undo wrote past both).
        if (_saves.Refused(target < _at ? L("undo") : L("redo")) is { } refused)
            return refused;

        if (_vaults.Store is not { } store)
            return L("Not undone: vault not open.");

        // What the vault must get back and lose on the way, and the gold to move.
        var putBack = new List<(Entry Entry, List<(VaultRow Row, long Key)> List, int Index)>();
        var takeOut = new List<VaultRow>();
        var gold = new List<GoldMove>();

        // The ledger's trades with them: a trade undone is taken out of it, one done again written again.
        var forget = new List<TradeRecord>();
        var redone = new List<TradeRecord>();

        if (target < _at)
        {
            for (int step = _at; step > target; step--)
            {
                var entry = _entries[step];
                takeOut.AddRange(entry.Added.Select(each => each.Row with { Key = each.Key }));
                for (int n = 0; n < entry.Removed.Count; n++)
                    putBack.Add((entry, entry.Removed, n));
                gold.AddRange(entry.Gold.Select(move => move with { Amount = -move.Amount }));
                forget.AddRange(entry.Trades);
            }
        }
        else
        {
            for (int step = _at + 1; step <= target; step++)
            {
                var entry = _entries[step];
                takeOut.AddRange(entry.Removed.Select(each => each.Row with { Key = each.Key }));
                for (int n = 0; n < entry.Added.Count; n++)
                    putBack.Add((entry, entry.Added, n));
                gold.AddRange(entry.Gold);
                redone.AddRange(entry.Trades);
            }
        }

        var state = _entries[target];

        // Written down first, as a save's moves are: a going back cut short is put right as one.
        if (_moves.JournalHistory([.. putBack.Select(each => each.List[each.Index].Row)], takeOut, gold,
                                  state.Character, state.Stash) is { } unjournaled)
            return unjournaled;

        IsMoving = true;
        string? outcome = null;
        bool finished = false;
        try
        {
            outcome = Walk();
            finished = true;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            outcome = L($"Not undone: {problem.Message}");
        }
        finally
        {
            // A walk cut short is put right at once, and what that found said with it.
            if (_moves.CloseHistoryJournal(finished && outcome is null) is { } found)
                outcome = outcome is null ? found : $"{outcome} {found}";

            // What the walk wrote to the vault is the walk's, not a step.
            Consume();
            IsMoving = false;
            Say();
        }

        return outcome;

        string? Walk()
        {
            // The item on the Edit page, to find again: the files put back make every item afresh.
            var shown = _session.SelectedItem?.Item;
            bool shownInStash = shown is not null && _stash.Holds(shown);

            // 1. Into the vault first: gains before the files, as every save does - the gold
            // going in too.
            foreach (var (_, list, index) in putBack)
            {
                var (row, _) = list[index];
                if (store.PutBack(row, out string? refused) is not { } key)
                    return L($"Not undone: {row.Name} {refused}.");
                list[index] = (row, key);
            }

            if (gold.Where(move => move.Amount > 0).ToList() is { Count: > 0 } goldIn)
                _vaults.Store?.RecordGold(goldIn);

            // 2. The files, as they were in that state. One that stands so already is left alone:
            // a file put back is read again and every item, tile and editor of it made afresh, and most steps
            // changed one file - an item edited took the shared stash's nine pages with it, a stash move the
            // whole character (a trace: 12 and 52 of an undo's 107 ms).
            if (state.Character is { } character && !_session.StandsAs(character) && _session.RestoreBytes(character) is { } notBack)
                return notBack;
            if (state.Stash is { } stash && _stash.IsLoaded && !_stash.StandsAs(stash) && _stash.RestoreBytes(stash) is { } stashNotBack)
                return stashNotBack;
            if (state.Compendium is { } kept && _compendium is { IsLoaded: true } compendium && !compendium.StandsAs(kept.Bytes)
                && compendium.RestoreBytes(kept.Bytes) is { } compendiumNotBack)
            {
                return compendiumNotBack;
            }

            if (_saves.Commit(out var report) is { } trouble)
                return trouble;

            // The copy it took, said as what it was taken before.
            if (report.Backup is { } backup)
            {
                string caption = target == _at - 1 ? L($"Before undo: {_entries[_at].Label}")
                    : target == _at + 1 ? L($"Before redo: {_entries[target].Label}")
                    : target < _at ? L($"Before going back to: {state.Label}")
                    : L($"Before going forward to: {state.Label}");
                SaveWriter.Caption(backup, caption);
            }

            // The files are at the target from here on, whatever fails after.
            _at = target;

            // Whatever the moves between noted is part of this, not a new state.
            _sinceStep.Clear();

            // The ledger follows the files: what the steps gone back over traded is not a trade any more.
            store.ForgetTrades(forget);
            store.RecordTrades(redone);

            // 3. Out of the vault last: losses after the files, the gold coming out too.
            if (takeOut.Count > 0)
                store.Remove(takeOut.Select(row => row.Key));

            if (gold.Where(move => move.Amount < 0).ToList() is { Count: > 0 } goldOut)
                _vaults.Store?.RecordGold(goldOut);

            // Said only where the vault moved: its pages, the grail and the vaults' list each read it again (an undo of a
            // move in the files, 3.5 of its 30 ms, measured).
            if (putBack.Count > 0 || takeOut.Count > 0 || gold.Count > 0 || forget.Count > 0 || redone.Count > 0)
                _vaults.SayChanged();

            // The Edit page stays on its item, as it then is (the undo is that page's
            // way back, and one that shut the item was a way out).
            // A stash put back holds new items, and nothing else lets go of the old tile: edited, it would mark no
            // file and be lost on screen.
            if (shown is not null && shownInStash && !_stash.Holds(shown))
                _session.SelectedItem = null;

            if (shown is not null && _session.SelectedItem is null
                && _session.TileLike(shown, shownInStash ? _stash.Pages.SelectMany(page => page.Items) : null) is { } again)
            {
                _session.SelectedItem = again;
            }

            return null;
        }
    }

    private void Say()
    {
        Rows.Clear();
        for (int index = _entries.Count - 1; index >= 0; index--)
        {
            var entry = _entries[index];
            Rows.Add(new HistoryRow(index, entry.Label, entry.At) { IsCurrent = index == _at, IsUndone = index > _at });
        }

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoHint));
        OnPropertyChanged(nameof(RedoHint));
    }
}
