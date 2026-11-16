using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Threading;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>One item of a group on the cursor besides the one the drag started on: which file it came from, and its cells from that one.</summary>
public sealed record CarriedItem(ItemTileViewModel Tile, bool Shared, int Dx, int Dy);

/// <summary>
/// The loaded character, shared by every page.
/// <para>
/// The pages are separate view models resolved from the container, so the save cannot live
/// in any one of them. This owns it: it opens the game data, loads and commits the file,
/// builds the item grids, and tracks which item is selected. Pages subscribe to
/// <see cref="Changed"/> and take the slice they show.
/// </para>
/// <para>
/// There is no interface over it because nothing swaps the implementation. The SFX manager
/// has one because it is the piece that fails on a machine with no audio device.
/// </para>
/// </summary>
public sealed partial class CharacterSession : ObservableObject, ISaveParticipant, IItemEditHost, IItemHolder
{
    private GameData? _game;

    /// <summary>
    /// The layout this character was read at, so it is read and written at the same one
    /// afterwards.
    /// <para>
    /// The parser is set by a global, and <b>every file that is read sets it</b>. The shared
    /// stash reads at its own widths with no skill bytes, so once it has been touched,
    /// anything that reads or writes this character at the settings left behind gets
    /// nonsense - which is exactly what Undo did, reporting "invalid item data" for a
    /// character it had read perfectly well a moment before.
    /// </para>
    /// </summary>
    private SaveFormat _format = SaveFormat.Pd2;

    /// <summary>
    /// While the character is still in last season's encoding, the items it held when read - which may
    /// leave and come back; anything else is refused (<see cref="LastSeasonGuard"/>). Null in this season's.
    /// </summary>
    private IReadOnlySet<Item>? _lastSeasonOwn;

    /// <summary>Whether the open character is still in last season's encoding, as the game wrote it before the new season.</summary>
    public bool IsLastSeason => _format == SaveFormat.Pd2LastSeason;

    /// <summary>
    /// Puts the parser back into this character's layout. Said before reading or writing it,
    /// rather than assumed.
    /// </summary>
    public void UseMyLayout() => SaveVerifier.Install(_format);

    partial void OnHasUnsavedChangesChanged(bool value)
    {
        // The set is what the buttons watch, so it is told whenever this changes.
        _saveSet?.SomethingChanged();
    }

    private void UseOwnLayout() => UseMyLayout();
    private D2S? _save;

    /// <summary>Raised after a load or a commit, once every grid has been rebuilt.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised by every edit of the character, after it is marked (<see cref="MarkEdited"/>): a value on the Edit page,
    /// a move, a socket. What reads the whole character - Advanced Stats - asks again by it.
    /// </summary>
    public event Action? Edited;

    [ObservableProperty] private string? _savePath;
    [ObservableProperty] private string _characterName = L("No character");
    [ObservableProperty] private string _characterDetail = "";
    /// <summary>
    /// What the last action came to - said on the message strip under the left page, from every
    /// page (see <see cref="MessageLog"/>). Every assignment is a message, the same text again
    /// included, which is why this is not a generated property: that skips an equal value.
    /// </summary>
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            Messages.Post(value);
        }
    }

    private string _status = L("No character open.");

    /// <summary>The message strip's log - one for the app, held here because every page holds this.</summary>
    public MessageLog Messages { get; } = new("strip");

    /// <summary>
    /// The right page's strip: what an action that stays on the right page came to - the item
    /// panel's edits, the waypoints, a vault item refused. <see cref="Messages"/> keeps what reaches
    /// another page.
    /// </summary>
    public MessageLog RightMessages { get; } = new("right strip");
    /// <summary>
    /// The open character as it stood when it was opened, serialized. What is in memory is
    /// compared against this to decide whether anything actually changed - so undoing an
    /// edit by hand puts the character back to clean, rather than leaving it dirty because
    /// something was touched.
    /// </summary>
    private byte[] _baseline = [];

    /// <summary>
    /// The character file as it stood when it was opened - its write time and length - so a
    /// save can tell the game has rewritten it since. The shared stash keeps the same stamp
    /// for the same reason (see <see cref="SharedStashSession"/>); the character had none,
    /// so a save after playing wrote the older character over the game's progress without a
    /// word. Taken off the character file even when a backup is what was opened, because that
    /// is the file a save replaces.
    /// </summary>
    private (DateTime Written, long Length)? _asRead;

    /// <summary>
    /// Whether the character file is no longer the one opened - the game has saved it since.
    /// A missing file is not a change: nothing was played over it, and a save puts it back.
    /// See <see cref="GameSync"/>.
    /// </summary>
    public bool ChangedOnDisk
    {
        get
        {
            if (SavePath is not { } path || _asRead is not { } read || !File.Exists(path))
                return false;

            var now = new FileInfo(path);
            return now.LastWriteTimeUtc != read.Written || now.Length != read.Length;
        }
    }

    /// <summary>
    /// Takes the file as it stands now as the one the next save replaces - the player chose to
    /// keep their edits over what the game saved. The game's version still goes to the backups,
    /// as every replaced file does.
    /// </summary>
    public void AcceptDisk()
    {
        if (SavePath is { } path && File.Exists(path))
        {
            var now = new FileInfo(path);
            _asRead = (now.LastWriteTimeUtc, now.Length);
        }
    }

    /// <summary>True when what is in memory no longer matches what was opened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _hasUnsavedChanges;

    /// <summary>The set this character is saved as part of. See <see cref="SaveSet"/>.</summary>
    private SaveSet? _saveSet;

    /// <summary>
    /// Joins the set of files saved together. The character no longer decides when anything
    /// is written, including itself.
    /// </summary>
    public void PartOf(SaveSet set)
    {
        _saveSet = set;
        set.Takes(this);
    }

    /// <summary>What this is, when something has to say which file went wrong.</summary>
    string ISaveParticipant.What => L("The character");

    /// <summary>
    /// Whether this character's own bytes differ from its file, or it holds an item that leaves
    /// with the next save.
    /// </summary>
    bool ISaveParticipant.HasChanges =>
        _save is not null && (HasUnsavedChanges || IsViewingBackup || LeavingNow().Count > 0);

    /// <summary>
    /// Whether an item goes with the next save: moved into the vault, which keeps the item here,
    /// marked, until then - see <c>VaultMoves</c>, which sets this. The item is left out of what
    /// is written and let go once the file is.
    /// </summary>
    public Func<Item, bool>? Leaves { get; set; }

    /// <summary>The items here that leave with the next save, in the order they are held - the mercenary's too.</summary>
    private List<Item> LeavingNow() =>
        Leaves is { } leaves ? OwnLists().SelectMany(list => list.Items).Where(leaves).ToList() : [];

    /// <summary>
    /// The item lists this character owns: its own - worn, carried, stored, belted - and its
    /// mercenary's, which the save keeps apart.
    /// </summary>
    private IEnumerable<ItemList> OwnLists()
    {
        if (_save?.PlayerItemList is { } own)
            yield return own;
        if (_save?.MercenaryItemList?.ItemList is { } hired)
            yield return hired;
    }

    /// <summary>The list holding this very item, or null.</summary>
    private ItemList? ListHolding(Item item) =>
        OwnLists().FirstOrDefault(list => list.Items.Any(each => ReferenceEquals(each, item)))
        ?? CorpseHolding(item);

    /// <summary>What the last prepare left out, which the write then lets go.</summary>
    private List<Item> _preparedWithout = [];

    /// <summary>
    /// Whether there is anything to save, here or in anything saved alongside.
    /// <para>
    /// Kept for the pages that bind to the session rather than to the set. A move into the
    /// shared stash changes no byte of this character, so asking only about its own bytes
    /// leaves the one button that can write that move greyed out.
    /// </para>
    /// </summary>
    public bool CanSave => _saveSet?.CanSave ?? (_save is not null && HasUnsavedChanges);

    /// <summary>
    /// What stands for "nothing of the open files has changed since" (<see cref="SaveSet.Version"/>) - null with
    /// no set to ask, and then nothing worked out from the items is kept.
    /// </summary>
    public object? ItemsStamp => _saveSet?.Version;

    /// <summary>
    /// Re-checks whether the character differs from what was opened. Called by whatever
    /// just edited it; comparing the bytes means no edit path has to be trusted to report
    /// itself correctly.
    /// </summary>
    public void MarkEdited()
    {
        if (_save is null)
        {
            HasUnsavedChanges = false;
            return;
        }

        // What was taken off the corpse is the character's (CharacterSession.Corpse.cs).
        SettleCorpses();

        // Nothing floats in a belt: a potion taken out lets the ones above it fall, as in play.
        // Here, where every edit passes and no load does - an opened file is left as it was.
        if (_save.PlayerItemList is { } carried)
            Belts.Settle(carried.Items.Where(item => item.Mode == ItemMode.Belt));

        // Nor does an Iron Golem outlast its skill: the item that gave it taken off, the golem goes.
        SettleGolem();

        // Changed at once; compared with the file a moment later, once for a burst (CompareSoon) - an edit put back as
        // it was reads as saved again. Compared here, every edit wrote the whole character to bytes: 4 ms of each move,
        // and the autosave writes it again. Something in the character that will
        // not serialize leaves it changed, so saving says why rather than the button going dead.
        HasUnsavedChanges = true;
        CompareSoon();
        _saveSet?.SomethingChanged();

        // The figure wears what the character can use, in its look: a worn piece edited in place - its base upgraded,
        // its requirements raised - rebuilds no grid, which was the one asking. Nothing is drawn where nothing it
        // depends on changed (0.4-0.7 ms to find that out, ShowFigureSoon).
        ShowFigureSoon();
        Edited?.Invoke();
    }

    private void CompareSoon()
    {
        if (_comparePosted)
            return;

        _comparePosted = true;
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                _comparePosted = false;
                CompareWithFile();
            }, DispatcherPriority.Background);
        }
        catch (Exception)
        {
            // No dispatcher (a test harness): compared now.
            _comparePosted = false;
            CompareWithFile();
        }
    }

    private bool _comparePosted;

    /// <summary>Unchanged after all where the character writes as its file: an edit and its reverse.</summary>
    private void CompareWithFile()
    {
        if (_save is null || !HasUnsavedChanges)
            return;

        try
        {
            if (!D2S.Write(_save).AsSpan().SequenceEqual(_baseline))
                return;
        }
        catch (Exception)
        {
            return;
        }

        HasUnsavedChanges = false;
        _saveSet?.SomethingChanged();
    }

    /// <summary>True once a character is open. The nav buttons key off this: there is
    /// nothing to show on the character, inventory or stash pages until one is.</summary>
    [ObservableProperty] private bool _isLoaded;

    /// <summary>
    /// What PD2's own <c>.kills</c>, <c>.deaths</c> and <c>.played</c> print for the open
    /// character, read from its save. Null with no character open, or a save that keeps none.
    /// </summary>
    public PlayerStats? Stats => _save is null ? null : PlayerStats.FromTrailer(_save.Trailer);

    /// <summary>
    /// True once a character has been chosen, whether or not it opened.
    /// <para>
    /// A failed load is exactly when the backups matter, so the page stays on that
    /// character rather than dropping back to the list as though nothing happened.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _hasAttempt;

    /// <summary>
    /// Why the last character chosen did not open, said on its card under "Character not opened."
    /// - empty while one opens or did.
    /// </summary>
    [ObservableProperty] private string _loadProblem = "";

    /// <summary>The character file being worked on, which is not where the data came from
    /// when a backup was opened.</summary>
    [ObservableProperty] private string? _characterPath;

    /// <summary>Where the open data came from. Differs from the character when a backup
    /// is being looked at.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewingBackup))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string? _openedFrom;

    /// <summary>
    /// True while an earlier copy is open. Saving then replaces the character with it,
    /// which is a different act from saving edits, so the page says so.
    /// </summary>
    public bool IsViewingBackup =>
        OpenedFrom is { Length: > 0 } && !string.Equals(OpenedFrom, CharacterPath, StringComparison.OrdinalIgnoreCase);
    [ObservableProperty] private ItemTileViewModel? _selectedItem;

    /// <summary>Set by the inventory page while several items are selected - see IItemEditHost.</summary>
    [ObservableProperty] private bool _isPicking;

    /// <summary>How many are picked, for the item panel to say in place of an item. The inventory page keeps it.</summary>
    [ObservableProperty] private int _pickedCount;

    /// <summary>The open character as the pages show it, so each need not rebuild it.</summary>
    [ObservableProperty] private CharacterCard? _openCard;

    /// <summary>
    /// Attribute spending for the open character, or null when none is open. Null also when
    /// the class table could not be read, since without a floor a respec has no rules.
    /// </summary>
    [ObservableProperty] private AttributeEditor? _attributes;

    /// <summary>
    /// The open character's skill trees, or null when none is open. Read-only apart from a
    /// full respec - see <see cref="SkillEditor"/> for why.
    /// </summary>
    [ObservableProperty] private SkillEditor? _skills;

    /// <summary>
    /// The open character's level and experience, or null when none is open - see
    /// <see cref="LevelEditor"/>.
    /// </summary>
    [ObservableProperty] private LevelEditor? _leveling;
    public ObservableCollection<string> SelectedFacts { get; } = [];

    /// <summary>
    /// The character's map seed - the <c>.d2s</c> header's Map ID, four bytes at 0xAB.
    /// <para>
    /// Zero when nothing is loaded. Setting it marks the save edited like any other change;
    /// nothing is written until the person saves.
    /// </para>
    /// <para>
    /// <b>What this is known to be, and what it is not:</b> the field is the seed the game
    /// generates a character's world from - the value the old <c>-seed</c> command line passed,
    /// and the one MapEasy exists to set. What has <b>not</b> been verified here is whether
    /// changing it alone is enough in PD2: the character also keeps <c>.map</c> and
    /// <c>.ma0</c>-<c>.ma3</c> files beside the save, and if those cache generated maps they
    /// may need clearing too.
    /// </para>
    /// </summary>
    public uint MapSeed
    {
        get => _save?.MapId ?? 0;
        set
        {
            if (_save is null || _save.MapId == value)
                return;

            _save.MapId = value;
            OnPropertyChanged();
            MarkEdited();
        }
    }

    /// <summary>The open character's waypoints, by difficulty. Empty when none is open.</summary>
    public IReadOnlyList<DifficultyWaypoints> Waypoints { get; private set; } = [];

    /// <summary>The character's quests, all three difficulties - null with none open. See QuestProgress.</summary>
    public QuestsSection? Quests => _save?.Quests;

    /// <summary>
    /// Completes a quest on the open character (QuestProgress.Complete): its word, and what it gives
    /// for good added to the attributes. Null when nothing changed. The caller announces the edit.
    /// </summary>
    public QuestProgress.Completed? CompleteQuest(int difficulty, QuestProgress.Info quest) =>
        _save is null ? null : QuestProgress.Complete(_save, difficulty, quest);

    /// <summary>Takes back a completion of this session: null when done, otherwise why not.</summary>
    public string? RevertQuest(QuestProgress.Completion done) =>
        _save is null ? L("No character open.") : QuestProgress.Revert(_save, done);

    /// <summary>Whether the character is of the expansion, which has a fifth act.</summary>
    public bool IsExpansion => _save?.Status.IsExpansion ?? false;

    /// <summary>Every change of the item panel's is the character's at once; the autosave writes it (IItemEditHost.KeepsAtOnce).</summary>
    bool IItemEditHost.KeepsAtOnce => true;

    void IItemEditHost.EditStreak(ItemTileViewModel tile) => SetEditStreak($"item:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(tile.Item)}");

    private string? _editStreak;

    /// <summary>
    /// Names the item a change kept at once was made to - the character's by its id, the vault's by its key
    /// (VaultEditHost) - for the history's next step (<see cref="TakeEditStreak"/>).
    /// </summary>
    public void SetEditStreak(string item) => _editStreak = item;

    /// <summary>
    /// The item the changes since the last step were made to, and forgotten: a step made of them is that
    /// item's, and the next of the same item is added to it (SessionHistory). Null where the step is of
    /// anything else - a move, a deletion, a page of quests.
    /// </summary>
    public string? TakeEditStreak()
    {
        string? item = _editStreak;
        _editStreak = null;
        return item;
    }

    /// <summary>The copy lands in a container, selected.</summary>

    /// <summary>Who the mercenary is, or <see cref="MercenaryProfile.None"/>.</summary>
    [ObservableProperty] private MercenaryProfile _mercenary = MercenaryProfile.None;

    /// <summary>Grid sizes for the loaded character, read from the installed inventory.txt.</summary>
    private InventoryLayout _layout = InventoryLayout.Fallback;

    /// <summary>
    /// Where the game is. Empty until the first run has settled it, and every path this
    /// class builds hangs off it.
    /// </summary>
    public string GameRoot { get; private set; } = "";

    /// <summary>
    /// Whether the open character is hardcore. The two modes keep <b>separate shared
    /// stashes</b>, and the game will not carry an item between them.
    /// </summary>
    public bool IsHardcore => _save?.Status.IsHardcore ?? false;

    /// <summary>Whether the folder is settled and really holds the game.</summary>
    public bool IsConfigured => GameLocation.Holds(GameRoot);

    /// <summary>
    /// The installed game's tables, opened on first use and kept.
    /// <para>
    /// Opening a character does this as part of loading, but <b>not everything that needs
    /// the tables needs a character</b> - the shared stash is a file of its own and can be
    /// opened with none - so the two are separated here rather than leaving the tables
    /// closed until somebody happens to load a save.
    /// </para>
    /// <para>
    /// Null when no game folder is settled, which is the one case where there is nothing to
    /// open and the caller has to cope.
    /// </para>
    /// </summary>
    public GameData? EnsureGame()
    {
        if (_game is not null)
            return _game;

        if (!IsConfigured)
            return null;

        // Begun at the program's first lines (GameAhead): taken here, waited for where it is not done, and only
        // now said - on this thread, to the same listeners at the same moment as an open made here says it.
        // What that open threw is thrown here, to whoever asked - and the next asking opens it anew, below.
        if (GameAhead.Take(GameRoot) is { } ahead)
        {
            Season = ahead;
            _game = Season.Game;
            PD2SPCompanion.Core.GameFiles.InstalledTables.Announce();
            return _game;
        }

        // Opened once for the run: nothing else in the app installs tables, so they stay as loaded.
        Season = PD2SPCompanion.Core.Season.Open(GameRoot);
        _game = Season.Game;

        // Kept outside the game the first time a season (or a build of it) is seen: a patch rewrites the
        // archives, and the season before it is gone with them.
        // Right after the install, so what it keeps is exactly what the tables were read from.
        try
        {
            PD2SPCompanion.Core.SeasonSnapshot.EnsureFor(Season);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }

        return _game;
    }

    /// <summary>
    /// <see cref="EnsureGame"/> for a caller with nothing round it: a failed open logged and said on the strip, and null
    /// (a damaged archive, a patch half done; in a command or a posted call it ended the program). Null with <see cref="IsConfigured"/> false is no game folder, and nothing said.
    /// </summary>
    public GameData? TryGame()
    {
        try
        {
            return EnsureGame();
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Messages.Post(L($"PD2 tables not read: {problem.Message}"));
            return null;
        }
    }

    /// <summary>The season the tables were loaded from - what the patch guard compares against. Null until opened.</summary>
    public PD2SPCompanion.Core.Season? Season { get; private set; }

    /// <summary>
    /// Where the characters are. Empty rather than relative while no game folder is
    /// settled - Path.Combine("", "Save") is "Save", which is a real path relative to
    /// wherever the editor happens to have been started from.
    /// <para>
    /// <c>PD2SPC_SAVES</c> points it at a copy, as <c>PD2SPC_VAULTS</c> points the vaults: how a test
    /// saves, and undoes, without writing a player's own characters.
    /// </para>
    /// </summary>
    public string SaveFolder =>
        Environment.GetEnvironmentVariable("PD2SPC_SAVES") is { Length: > 0 } elsewhere ? elsewhere
        : GameRoot.Length > 0 ? Path.Combine(GameRoot, "Save") : "";

    /// <summary>
    /// Raised, on the UI thread, when anything in the save folder is created, changed,
    /// renamed or deleted - characters and the copies saving leaves beside them.
    /// <para>
    /// It lives here rather than on a page because more than one page is built from that
    /// folder: the landing page lists characters and their earlier copies, and the
    /// settings page counts the copies. A second watcher on each would be two things to
    /// keep in step.
    /// </para>
    /// </summary>
    public event Action? SaveFolderChanged;

    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>Used by the designer, which gets no services.</summary>
    public CharacterSession() : this(null) { }

    public CharacterSession(EditorSettings? settings) =>
        UseGameFolder(settings?.GameFolder ?? "");

    /// <summary>
    /// Points the session at a game folder.
    /// <para>
    /// Called once at startup from the stored setting, and again when the first run settles
    /// one. Nothing is read out of the archives until a character is opened, so this is
    /// safe up to that point and the settings page asks for a restart after it - by then
    /// the tables, strings and sprites are loaded and static, and swapping the folder under
    /// them would leave half the app reading one game and half another.
    /// </para>
    /// </summary>
    public void UseGameFolder(string folder)
    {
        GameRoot = GameLocation.Tidy(folder);

        _watcher?.Dispose();
        _watcher = null;

        if (!IsConfigured)
            return;

        // Before the watcher, so the moves do not read as a folder full of activity.
        int moved = SaveWriter.MigrateLooseBackups(SaveFolder);
        if (moved > 0)
        {
            Status = Plural(moved, $"Moved {moved} old backup to {SaveWriter.BackupFolderName}.",
                       $"Moved {moved} old backups to {SaveWriter.BackupFolderName}.") + " " + Status;
        }

        WatchSaveFolder();
        SaveFolderChanged?.Invoke();
    }

    /// <summary>
    /// Picks up characters created or played while the editor is open, so the lists do not
    /// go stale behind a running game.
    /// </summary>
    private void WatchSaveFolder()
    {
        try
        {
            if (!Directory.Exists(SaveFolder))
                return;

            _watcher = new FileSystemWatcher(SaveFolder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,

                // Backups sit in a folder per character rather than beside them.
                IncludeSubdirectories = true,
            };

            // A single Filter takes one pattern, and every kind matters here - the shared stash
            // too, which the game can write without its character (an outside change
            // to it alone went unheard). Whoever listens decides whether a file really changed.
            _watcher.Filters.Add("*.d2s");
            _watcher.Filters.Add("*.bak");
            _watcher.Filters.Add("*.stash");

            // The game rewrites a save in several bursts, so settle before re-reading, and
            // come back on the UI thread since this fires on a pool thread.
            void Settle(object? _, FileSystemEventArgs __) =>
                _debounce?.Change(TimeSpan.FromMilliseconds(600), Timeout.InfiniteTimeSpan);

            _debounce = new Timer(_ => Dispatcher.UIThread.Post(() => SaveFolderChanged?.Invoke()));

            _watcher.Created += Settle;
            _watcher.Changed += Settle;
            _watcher.Deleted += Settle;
            _watcher.Renamed += (_, _) => Settle(null, null!);

            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // Watching is a convenience; the pages can still be refreshed by hand.
        }
    }

    public void Load(string path)
    {
        // What a crash left beside the saves before swapping it in - never a save of its own.
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
            SaveWriter.ClearPending(folder);

        ViewingMoment = null;
        LoadFrom(path, path);
    }

    /// <summary>
    /// The moment an earlier copy was taken, while one is open, and null while the
    /// character itself is. The stash reads its own copy of the same moment, so the two are
    /// put back as the pair they were saved as.
    /// </summary>
    public string? ViewingMoment { get; private set; }

    /// <summary>
    /// Opens an older copy of a character, while still pointing at the character itself.
    /// Saving then restores it, backing up what is there first.
    /// </summary>
    public void LoadBackup(string backupPath, string characterPath)
    {
        // "MigrationTest.d2s.20260924-154528.bak", and sometimes "-2" where two copies
        // landed in the same second.
        string name = Path.GetFileNameWithoutExtension(backupPath);
        int at = name.LastIndexOf('.');
        ViewingMoment = at >= 0 ? name[(at + 1)..] : null;

        // The rolling copy of the file as last written has no moment in its name: its own time is it.
        if (backupPath.EndsWith(SaveWriter.AsWrittenSuffix, StringComparison.OrdinalIgnoreCase))
            ViewingMoment = File.GetLastWriteTime(backupPath).ToString("yyyyMMdd-HHmmss");

        LoadFrom(backupPath, characterPath);
    }

    private void LoadFrom(string dataPath, string characterPath)
    {
        try
        {
            // Cleared up front so a failed load cannot leave the previous character's
            // pages reachable. The held item goes with it: it belongs to the save about to
            // be replaced, and an item from a character that is no longer open would be
            // put down into one that never had it.
            IsLoaded = false;
            Held = null;

            // Set before anything can fail, so a refusal still shows which character it was
            // about and what can be opened instead.
            HasAttempt = true;
            LoadProblem = "";
            CharacterPath = characterPath;
            OpenedFrom = dataPath;
            var summary = SaveSummary.TryRead(characterPath) ?? SaveSummary.TryRead(dataPath);
            // Waiting for its figure from the start, so the class picture does not show first (ShowFigureSoon).
            OpenCard = summary is null ? null : new CharacterCard(summary) { AwaitsFigure = true };

            // The tables, once for the run (EnsureGame). They were installed again on every load,
            // which read every table out of the archives each time a character was opened.
            var game = EnsureGame() ?? throw new InvalidOperationException(L("No game folder."));

            var (save, result) = SaveVerifier.Load(dataPath);
            _save = save;
            _format = result.Format;
            _lastSeasonOwn = LastSeasonGuard.OwnOf(_format == SaveFormat.Pd2LastSeason, save);

            // A character of the installed season tells the stamp its items carry, which no table does. Not a stamp of
            // the season before: its characters are played on in this one, legacy items and all (ItemSeason), and the
            // first opened in a new season may well be one.
            if (result.Format == SaveFormat.Pd2 && ItemEra is { } era && era != PD2SPCompanion.Core.Saves.SeasonReading.PreviousStamp
                && Season is { } installed)
            {
                try
                {
                    PD2SPCompanion.Core.SeasonSnapshot.NoteItemVersion(PD2SPCompanion.Core.SeasonSnapshot.NameFor(installed), era);
                }
                catch (Exception problem)
                {
                    Log.Caught(problem);
                }
            }

            // A backup from before a rename carries the old name inside, and the game names a
            // character by what is inside: restored as it is, the next save in game would come
            // back under the old name. The file's name is the character's.
            if (!string.Equals(dataPath, characterPath, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(characterPath) is { Length: > 0 } fileName
                && save.Name != fileName)
            {
                save.Name = fileName;
            }

            // Saving writes to the character, even when the data came from a backup.
            SavePath = characterPath;

            var onDisk = new FileInfo(characterPath);
            _asRead = onDisk.Exists ? (onDisk.LastWriteTimeUtc, onDisk.Length) : null;

            _layout = InventoryLayout.Load(game, save.ClassId, save.Status.IsExpansion);
            var classStats = ClassStats.For(game, save.ClassId);
            Attributes = new AttributeEditor(save, classStats);
            Skills = new SkillEditor(save, SkillTree.For(game, save.ClassId));
            LookAtGolem();
            Leveling = new LevelEditor(save, classStats, ExperienceTable.For(game, classStats.Name));
            Waypoints = WaypointProgress.Read(save);

            CharacterName = save.Name;
            string mode = save.Status.IsHardcore ? L("hardcore") : L("softcore");
            CharacterDetail = save.Status is { IsDead: true, IsHardcore: true }
                ? L($"level {save.Level}, {mode}, dead")
                : L($"level {save.Level}, {mode}");
            // The format and the exact round trip are the log's: a character that opens
            // always round-trips - one that does not is refused, and says so - so the strip only says it opened.
            Log.Info("load", $"{save.Name}: {result.Format}, {result.ItemCount} items, round-trip exact");
            Status = dataPath == characterPath
                ? L($"Opened {save.Name}.")
                : L($"Opened backup from {SaveWriter.TakenAt(new FileInfo(dataPath)):yyyy-MM-dd HH:mm} ({result.ItemCount} items).");

            // The baseline for every later comparison. Serialized rather than read off
            // disk, so it is the same shape as what a save would write.
            _baseline = D2S.Write(save);
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(CanSave));

            IsLoaded = true;

            Rebuild();
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            Status = ex.Message;
            LoadProblem = ex.Message;
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(CanSave));
            IsLoaded = false;
            // The refused character's card stays, with its class picture: no figure is coming.
            if (OpenCard is { } refused)
                refused.AwaitsFigure = false;
        }
    }

    /// <summary>
    /// Announces that the open character's attributes have been spent or refunded. The
    /// editor writes straight into the save, so nothing else would notice.
    /// </summary>
    public event Action? AttributesChanged;

    public void RaiseAttributesChanged()
    {
        MarkEdited();
        AttributesChanged?.Invoke();

        // The figure wears what the character can use, and strength and dexterity decide that: asked again
        // here. It was asked only when the grids were rebuilt, and points moved rebuild no grid - Refit made
        // the gear usable, the tiles lost their red, and the figure stood as it was. Nothing is drawn where what it depends on is as it was.
        ShowFigureSoon();
    }

    /// <summary>
    /// Puts the character back to how it was opened, dropping every edit since.
    /// <para>
    /// Rebuilt from the baseline bytes rather than re-read from disk: those bytes are what
    /// every change has been measured against, so this lands exactly where the comparison
    /// says clean - and it cannot be caught out by the file having moved underneath.
    /// </para>
    /// </summary>
    void ISaveParticipant.Revert()
    {
        if (RebuildFrom(_baseline) is null)
        {
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(CanSave));
            Status = L("All changes reverted.");
        }
    }

    /// <summary>
    /// The character as it stands on disk: the bytes it was opened with, or last written. What
    /// the session's history keeps of each state.
    /// </summary>
    public byte[] SavedBytes => _baseline;

    /// <summary>The bytes a save has prepared and not yet written, or null: what the move journal counts as "after".</summary>
    public byte[]? PreparedBytes => _prepared;

    /// <summary>
    /// Whether the character stands as these bytes have it: its file is these, and nothing waits to be written.
    /// What the session's history asks before it puts a state back (<see cref="RestoreBytes"/>).
    /// </summary>
    public bool StandsAs(byte[] bytes) =>
        _save is not null && !((ISaveParticipant)this).HasChanges && bytes.AsSpan().SequenceEqual(_baseline);

    /// <summary>
    /// Puts an earlier state of the character back, from its bytes - an undo or redo in the
    /// session's history. Unsaved until the next save writes it. Null when done, else why not.
    /// </summary>
    public string? RestoreBytes(byte[] bytes)
    {
        if (RebuildFrom(bytes) is { } failed)
            return failed;

        MarkEdited();
        return null;
    }

    /// <summary>The character rebuilt from these bytes, every editor with it; null when done.</summary>
    private string? RebuildFrom(byte[] bytes)
    {
        if (_save is null || _game is null || bytes.Length == 0)
            return L("No character open.");

        try
        {
            UseOwnLayout();

            var restored = D2SLib.Core.ReadD2S(bytes);
            KeepUnchanged(restored);

            // Undo builds the items it changed afresh, so anything on the cursor now belongs to a
            // save that no longer exists. Putting it down would write to an object nothing
            // reads, and the item would simply disappear.
            Held = null;

            _save = restored;

            // Undo made items afresh: the ones a last-season file may take back are these now.
            _lastSeasonOwn = LastSeasonGuard.OwnOf(IsLastSeason, restored);

            // The editors hold the save they were built on, so they are rebuilt too.
            var classStats = ClassStats.For(_game, restored.ClassId);
            Attributes = new AttributeEditor(restored, classStats);
            Skills = new SkillEditor(restored, SkillTree.For(_game, restored.ClassId));
            LookAtGolem();
            Leveling = new LevelEditor(restored, classStats, ExperienceTable.For(_game, classStats.Name));
            Waypoints = WaypointProgress.Read(restored);

            Rebuild();
            AttributesChanged?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            Status = L($"Not undone: {ex.Message}");
            return Status;
        }
    }

    /// <summary>
    /// The items an undo or redo finds as they stand, kept (undo by changed items): an item of
    /// <paramref name="restored"/> whose bytes - its place and its sockets with it - are an item's here now is that
    /// item, not one made afresh from the bytes. Its tile is kept with it (<see cref="TileFor"/>) and the selection
    /// stays on it; only what the step changed is described and drawn again. Every item used to come back new, and
    /// an undo of one move drew the whole character again (10 of its 30 ms, measured).
    /// <para>
    /// Only an item in this very list is kept, so nothing from before the step comes back into the save; what is on
    /// the cursor or leaves with the next save (<see cref="Leaves"/>) is spoken for and never kept.
    /// </para>
    /// </summary>
    private void KeepUnchanged(D2S restored)
    {
        if (_save is null)
            return;

        Keep(_save.PlayerItemList, restored.PlayerItemList, restored.Header.Version);
        Keep(_save.MercenaryItemList?.ItemList, restored.MercenaryItemList?.ItemList, restored.Header.Version);

        void Keep(ItemList? now, ItemList? then, uint version)
        {
            if (now is null || then is null || now.Items.Count == 0)
                return;

            var mine = new Dictionary<string, Queue<Item>>();
            foreach (var item in now.Items)
            {
                if (IsHeld(item) || Leaves?.Invoke(item) == true)
                    continue;

                string key = Convert.ToBase64String(Item.Write(item, _save.Header.Version));
                if (!mine.TryGetValue(key, out var same))
                    mine[key] = same = new Queue<Item>();
                same.Enqueue(item);
            }

            for (int index = 0; index < then.Items.Count; index++)
            {
                if (mine.TryGetValue(Convert.ToBase64String(Item.Write(then.Items[index], version)), out var same) && same.Count > 0)
                    then.Items[index] = same.Dequeue();
            }
        }
    }

    /// <summary>
    /// The bytes this character would be written as, checked to read back. Nothing is
    /// touched. See <see cref="ISaveParticipant"/> for why this is separate from writing.
    /// </summary>
    private byte[]? _prepared;

    string? ISaveParticipant.Prepare()
    {
        if (_save is null || SavePath is null)
            return L("No character open.");

        // Refused rather than merged, as the shared stash is: the game rewrote the file while
        // it was open here, and writing now would undo whatever was played. Undo does not
        // help - it goes back to what was opened, not to what is on disk.
        // GameSync asks which to keep before a save gets here; this is the backstop.
        if (ChangedOnDisk)
            return L($"{CharacterName} changed in game. Require reopen.");

        // No corpse is written with nothing on it (CharacterSession.Corpse.cs).
        SettleCorpses();

        // An Iron Golem that lost its skill and could not be dismissed: the game would refuse the file.
        if (GolemAwaitsRoom && GolemHasNoRoom() is { } golem)
            return golem;

        // What leaves with this save is taken out for the writing and put straight back, so a
        // refusal anywhere in the set leaves the character exactly as it was.
        // Both lists: the mercenary's kit can be stored too.
        var leaving = LeavingNow();

        // Nor a golem whose skill leaves with it: the store refuses that first; this is the backstop.
        if (GolemLeavesWith(leaving))
            return GolemKeepsItem;
        var kept = OwnLists().Select(list => (List: list, Items: list.Items.ToList())).ToList();

        try
        {
            if (leaving.Count > 0)
            {
                foreach (var (list, _) in kept)
                {
                    list.Items.RemoveAll(item => leaving.Contains(item));
                    list.Count = (ushort)list.Items.Count;
                }
            }

            _prepared = SaveWriter.Prepared(_save);
            _preparedWithout = leaving;
            return null;
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            _prepared = null;
            _preparedWithout = [];
            return L($"Not saved: {ex.Message}");
        }
        finally
        {
            if (leaving.Count > 0)
            {
                foreach (var (list, items) in kept)
                {
                    list.Items.Clear();
                    list.Items.AddRange(items);
                    list.Count = (ushort)list.Items.Count;
                }
            }
        }
    }

    SaveHoldings? ISaveParticipant.Holdings() =>
        _prepared is null ? null : new SaveHoldings(HeldIn(_baseline), HeldIn(_prepared));

    private static IReadOnlyDictionary<string, long> HeldIn(byte[] bytes) =>
        SaveHoldings.Counted(bytes, read =>
        {
            var save = D2SLib.Core.ReadD2S(read);
            var stats = save.Attributes.Stats;
            return SaveHoldings.Count(
                ItemsHeldIn(save),
                (long)stats.GetValueOrDefault(AttributeEditor.Carried) + stats.GetValueOrDefault(AttributeEditor.Stashed));
        });

    /// <summary>All a character's file holds, for telling what it gains from what it loses: the golem's item is in it as a corpse's is.</summary>
    public static IEnumerable<Item> ItemsHeldIn(D2S save) =>
        SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses | ItemScope.Golem);

    string? ISaveParticipant.Write(string moment, SaveReport report)
    {
        if (_save is null || SavePath is null || _prepared is null)
            return L("Character not prepared.");

        // For tests: while the file PD2SPC_FAIL_SAVE names exists, the character's write fails
        // after the vault's gains are written - a save cut short, as the move journal meets it.
        if (Environment.GetEnvironmentVariable("PD2SPC_FAIL_SAVE") is { Length: > 0 } fail && File.Exists(fail))
            return L("Not saved: test failure.");

        try
        {
            bool restoring = IsViewingBackup;
            // Nothing to copy where the file has gone: the save puts it back.
            string? backup = File.Exists(SavePath) ? SaveWriter.Backup(SavePath, moment, null) : null;
            report.Backup = backup;
            SaveWriter.WriteSafely(SavePath, _prepared);
            SaveWriter.KeepAsWritten(SavePath, _prepared);
            SaveWriter.Prune(SavePath, SaveWriter.BackupsToKeep);

            // Written without them, so they go now: what is in memory is what is on disk.
            bool removed = _preparedWithout.Count > 0;
            if (removed)
            {
                foreach (var list in OwnLists())
                {
                    list.Items.RemoveAll(item => _preparedWithout.Contains(item));
                    list.Count = (ushort)list.Items.Count;
                }

                if (SelectedItem is { } chosen && _preparedWithout.Contains(chosen.Item))
                    SelectedItem = null;
            }
            _preparedWithout = [];

            // What was open is now the character itself.
            OpenedFrom = SavePath;
            ViewingMoment = null;

            // Written, so this is the new mark to compare against.
            _baseline = _prepared;

            var written = new FileInfo(SavePath);
            _asRead = (written.LastWriteTimeUtc, written.Length);
            _prepared = null;
            HasUnsavedChanges = false;
            OnPropertyChanged(nameof(CanSave));

            // A save is the autosave's now, and says nothing: the strip keeps what was done. A
            // restore is asked for, and says where the character as it was went.
            if (restoring)
                Status = backup is null ? L("Restored.") : L($"Restored. Backup: {Path.GetFileName(backup)}.");

            // Laid out again only where the pages changed: items written out, or a restore become the
            // character. An ordinary save left every item as it was, and the rebuild - every tile and
            // every page listening - was about 10 ms of each move's autosave (measured).
            // The shared stash's save already did the same.
            if (removed || restoring)
                Rebuild();
            return null;
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            return L($"Not saved: {ex.Message}");
        }
    }

    /// <summary>Puts everything back to the state before any character was opened.</summary>
    public void Close()
    {
        _save = null;
        _layout = InventoryLayout.Fallback;

        SavePath = null;
        CharacterPath = null;
        OpenedFrom = null;
        HasAttempt = false;
        CharacterName = L("No character");
        CharacterDetail = "";
        Status = L("No character open.");
        _baseline = [];
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(CanSave));
        IsLoaded = false;

        Held = null;
        SelectedItem = null;   // also clears the selected facts
        AllGrids.Clear();
        _grids.Clear();
        Equipped.Clear();
        MercEquipped.Clear();
        Mercenary = MercenaryProfile.None;
        Attributes = null;
        Skills = null;
        Leveling = null;
        Waypoints = [];
        OpenCard = null;

        Changed?.Invoke();
    }


}
