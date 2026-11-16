using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>One saved copy of a character, as the backup list offers it.</summary>
/// <param name="Taken">When it was taken - the save it was taken before, so its caption and its time agree.</param>
/// <param name="Caption">What it was taken before: "Before: Tome of Town Portal stored in the vault." Empty when unknown.</param>
public sealed partial class BackupOption(string path, DateTime taken, long bytes, string caption = "")
    : CommunityToolkit.Mvvm.ComponentModel.ObservableObject, IAsksTwice
{
    public string Path { get; } = path;

    public DateTime Taken { get; } = taken;

    public long Bytes { get; } = bytes;

    public string Caption { get; } = caption;

    /// <summary>Its bin pressed once: the next press deletes (AskTwice) - a backup is the last way back.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isArmed;

    public string When => $"{Taken:yyyy-MM-dd  HH:mm:ss}";

    public bool HasCaption => Caption.Length > 0;

    public string Size => Bytes < 1024 ? L($"{Bytes} bytes") : $"{Bytes / 1024.0:0.#} KB";
}

/// <summary>
/// The landing page: a carousel of the characters in the game's Save folder, and the
/// actions that apply to the selected one.
/// </summary>
public partial class VP_ViewModel_Default_L : ViewModelBase
{
    public CharacterSession Session { get; }

    /// <summary>
    /// The files saved together - this character and the shared stash. What Save and Undo
    /// act on, because an item moved between the two is written to both.
    /// </summary>
    public SaveSet Saves { get; }

    /// <summary>The shared stash, asked at restore time who else holds what it is about
    /// to put back.</summary>
    public SharedStashSession Stash { get; }

    // Null under the designer constructor, which gets no services; every use is guarded.
    private readonly ISFXManager _sfxManager = null!;
    private readonly ConfirmService _confirm;

    /// <summary>Whether a question is up over the page: the list's keys wait for it.</summary>
    public bool IsAsking => _confirm.IsAsking;

    /// <summary>Kept in step with the game - see GameSync. Null in the designer.</summary>
    private readonly GameSync? _sync;

    public ObservableCollection<CharacterCard> Characters { get; } = [];

    [ObservableProperty] private int _selectedIndex = -1;

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Default_L()
        : this(new CharacterSession(), new SaveSet(),
               new SharedStashSession(new CharacterSession(), new SaveSet()),
               null!, new ConfirmService(), null!, new PlayRules(new EditorSettings()), null, null, null, null)
    {
    }

    public VP_ViewModel_Default_L(
        CharacterSession session, SaveSet saves, SharedStashSession stash,
        ISFXManager sfxManager, ConfirmService confirm, VaultService vaults, PlayRules rules, GameSync? sync,
        SessionHistory? history, AutoSave? autoSave, RestoreReconcile? restore)
    {
        Session = session;
        _sync = sync;
        History = history;
        _autoSave = autoSave;
        _restore = restore;
        _health = new WhenShown(JudgeHealthSoon);

        // Undo and redo follow the history; the buttons ask it.
        if (history is not null)
        {
            history.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(UndoHint));
                OnPropertyChanged(nameof(RedoHint));
                UndoCommand.NotifyCanExecuteChanged();
                RedoCommand.NotifyCanExecuteChanged();
            };
        }

        // The game starting or closing turns Save, Restore and Rename off or on.
        if (sync is not null)
        {
            sync.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName is not (nameof(GameSync.IsGameRunning) or nameof(GameSync.IsWriteBlocked)))
                    return;

                OnPropertyChanged(nameof(GameBlocked));
                OnPropertyChanged(nameof(CanSaveNow));
                OnPropertyChanged(nameof(SaveHint));
                OnPropertyChanged(nameof(RenameHint));
                OnPropertyChanged(nameof(NameHint));
                OnPropertyChanged(nameof(CloneHint));
                OnPropertyChanged(nameof(DeleteHint));
                OnPropertyChanged(nameof(BackupsHint));
                OnPropertyChanged(nameof(ShowBackupsHint));
                OnPropertyChanged(nameof(CanShowBackups));
                OnPropertyChanged(nameof(ReviveHint));
                OnPropertyChanged(nameof(LoadHint));

                // The game closed on a character it had closed here: that one is chosen again, a
                // click from loading.
                if (!sync.IsGameRunning && !Session.IsLoaded && sync.ClosedPath is { } closed
                    && IndexOf(closed) is var at and >= 0)
                {
                    SelectedIndex = at;
                }
            };
        }

        _vaults = vaults;
        FollowSearch();
        Rules = rules;
        rules.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanRename));
            OnPropertyChanged(nameof(CanRevive));
        };
        session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.OpenCard))
                FollowOpenCard();

            if (changed.PropertyName == nameof(CharacterSession.HasAttempt))
            {
                OnPropertyChanged(nameof(CanRename));
                OnPropertyChanged(nameof(CanDelete));
            }

            if (changed.PropertyName == nameof(CharacterSession.IsLoaded))
            {
                OnPropertyChanged(nameof(CanRename));
                OnPropertyChanged(nameof(CanDelete));
                OnPropertyChanged(nameof(CanRevive));
                IsRenaming = false;
                IsCloning = false;
                _health.Ask();
            }
        };
        Saves = saves;

        // Read again after each save - what was written is what is judged.
        saves.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(SaveSet.HasChanges) && !saves.HasChanges)
                _health.Ask();
        };
        Stash = stash;

        // The stash opens after the character: judged again once its items are in.
        stash.Loaded += _health.Ask;
        _sfxManager = sfxManager;
        _confirm = confirm;

        // The character is one of the files saved together. The stash joins the same set
        // when it is constructed.
        Session.PartOf(saves);

        // The buttons watch the set, so the page follows it changing.
        Saves.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanSaveNow));
            OnPropertyChanged(nameof(SaveHint));
            OnPropertyChanged(nameof(UndoHint));
        };

        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CharacterSession.HasUnsavedChanges)
                                or nameof(CharacterSession.CanSave))
            {
                OnPropertyChanged(nameof(SaveHint));
                OnPropertyChanged(nameof(UndoHint));
            }

            if (e.PropertyName is nameof(CharacterSession.LoadProblem))
                OnPropertyChanged(nameof(LoadProblem));

            if (e.PropertyName is nameof(CharacterSession.IsLoaded)
                                or nameof(CharacterSession.HasAttempt)
                                or nameof(CharacterSession.CharacterPath))
            {
                // Another character: a card's list asked for before is let go of, or its copies stood for this one's.
                if (e.PropertyName == nameof(CharacterSession.CharacterPath))
                    _backupsOf = null;

                RefreshBackups();
                LoadCommand.NotifyCanExecuteChanged();

                OnPropertyChanged(nameof(StatLines));
                OnPropertyChanged(nameof(HasStats));

                // The same button, saying the other thing.
                OnPropertyChanged(nameof(IsReload));
                OnPropertyChanged(nameof(LoadLabel));
                OnPropertyChanged(nameof(LoadHint));
                OnPropertyChanged(nameof(LoadProblem));
            }
        };

        // Every save in the folder - the autosave of each move included - changed the lists, and
        // reading them is every character's file: done while this page is shown, or once when it is
        // shown next (WhenShown). Behind the Items page it was 14 ms of a move on a slower PC with
        // one character (measured), more with each character in the folder.
        _rescan = new WhenShown(Rescan);
        Session.SaveFolderChanged += _rescan.Ask;
        Refresh();
    }

    private readonly WhenShown _rescan;

    /// <summary>
    /// The health mark, drawn on this page only: judged again after every save - every move's autosave -
    /// over every item the character and the stash hold, 4 ms of a move behind the Items page (measured).
    /// While shown, or once when shown next.
    /// </summary>
    private readonly WhenShown _health;

    /// <summary>In front, or put away (the view, through PageDeck).</summary>
    public void Shown()
    {
        _rescan.Shown();
        _health.Shown();
    }

    public void Hidden()
    {
        _rescan.Hidden();
        _health.Hidden();
    }

    /// <summary>Both lists, after something in the save folder moved.</summary>
    private void Rescan()
    {
        Refresh();
        RefreshBackups();
    }

    public CharacterCard? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Characters.Count ? Characters[SelectedIndex] : null;

    /// <summary>
    /// The menu of the card selected - a right click selects it first: Open, as its button
    /// and a double click; Rename and Clone where the play mode edits characters; Delete as an icon at its head.
    /// The card's own buttons' commands, by their own guards: dimmed while the game runs, and saying so.
    /// </summary>
    public ActMenu MenuForSelected()
    {
        if (Selected is null || !IsBrowsable)
            return new ActMenu([], []);

        var icons = new List<MenuAct>();
        var rows = new List<MenuAct>
        {
            new(L("Open"), MenuGlyph.Open, () => LoadCommand.Execute(null)) { IsMain = true, Keys = L("Enter") },
        };

        if (CanRename)
        {
            rows.Add(new(L("Rename"), MenuGlyph.Pencil, () => ToggleRenameCommand.Execute(null))
            {
                RuleAbove = true, Keys = L("F2"), IsEnabled = !GameBlocked, Why = GameBlocked ? RenameHint : "",
            });
            rows.Add(new(L("Clone"), MenuGlyph.Duplicate, () => ToggleCloneCommand.Execute(null)) { IsEnabled = !GameBlocked, Why = GameBlocked ? CloneHint : "" });
        }

        // Its earlier copies, listed: the list a character's Backups button opens once it is open.
        var card = Selected;
        rows.Add(new(L("Backups"), MenuGlyph.Archive, () => ShowBackupsOf(card))
        {
            RuleAbove = true, IsEnabled = !GameBlocked, Why = GameBlocked ? BackupsHint : "",
        });

        if (CanDelete)
        {
            icons.Add(new(L("Delete"), MenuGlyph.Bin, () => DeleteCharacterCommand.Execute(null))
            {
                Keys = L("Del"), IsBin = true, IsEnabled = !GameBlocked, Why = GameBlocked ? DeleteHint : "",
            });
        }

        return new ActMenu(icons, rows);
    }

    public bool HasCharacters => Characters.Count > 0;

    /// <summary>
    /// False once a character has been chosen, open or not. A refusal keeps the page on
    /// that character so its backups are within reach.
    /// </summary>
    public bool IsBrowsable => !Session.HasAttempt;

    /// <summary>True when the chosen character would not open.</summary>
    public bool LoadFailed => Session.HasAttempt && !Session.IsLoaded;

    /// <summary>Whether the open character keeps PD2's statistics, and so has a panel of them.</summary>
    public bool HasStats => Session.IsLoaded && Session.Stats is not null;

    private readonly VaultService _vaults;

    /// <summary>
    /// The grail of this character's mode: what the game has discovered of each list, out of how many - the Grail
    /// page's own lists, counted by <see cref="GrailCatalog"/>, and its Discovery, read from the mode's shared stash.
    /// Account-wide, as the game keeps it, not this character's own finds: the editor keeps none.
    /// Read when the button is pressed, so a
    /// discovery since shows.
    /// </summary>
    public IReadOnlyList<StatLine> GrailLines
    {
        get
        {
            if (!Session.IsLoaded || Session.SaveFolder is not { Length: > 0 } folder)
                return [];

            var discovered = PD2SPCompanion.Core.Saves.SharedStash.DiscoveredIn(System.IO.Path.Combine(folder,
                Session.IsHardcore ? SharedStashSession.HardcoreFile : SharedStashSession.SoftcoreFile));

            static string Of(int found, int total) =>
                total == 0 ? "0 / 0" : $"{found} / {total}  ({100.0 * found / total:0.#}%)";

            // The game's own counts: one card a name, the specials not counted (GrailCatalog.UniqueCards) - its "9/427";
            // the set pieces each a card - its "2/128".
            var uniques = GrailCatalog.UniqueCards().Where(card => card.Counts).ToList();
            int found = uniques.Count(card => card.Rows.Any(entry => discovered.Uniques.Contains(entry.Row)));
            var sets = GrailCatalog.Sets();
            int setsFound = sets.Count(entry => discovered.Sets.Contains(entry.Row));

            return
            [
                new(L("Uniques"), Of(found, uniques.Count)),
                new(L("Sets"), Of(setsFound, sets.Count)),
                new(L("All"), Of(found + setsFound, uniques.Count + sets.Count)),
            ];
        }
    }

    /// <summary>The grail button, pressed: its panel reads the Discovery again.</summary>
    [RelayCommand]
    private void ShowGrail() => OnPropertyChanged(nameof(GrailLines));

    /// <summary>
    /// The statistics panel, in the groups PD2's commands print them in: the whole character,
    /// the act bosses, then the endgame.
    /// </summary>
    public IReadOnlyList<StatLine> StatLines => Session.Stats is not { } stats ? [] :
    [
        new(L("Time played"), stats.Played),
        new(L("Monster kills"), $"{stats.MonsterKills:N0}"),
        new(L("Deaths"), $"{stats.Deaths:N0}"),

        new(L("Act bosses"), "", IsHeading: true),
        new(L("Andariel"), $"{stats.Andariel:N0}"),
        new(L("Duriel"), $"{stats.Duriel:N0}"),
        new(L("Mephisto"), $"{stats.Mephisto:N0}"),
        new(L("Diablo"), $"{stats.Diablo:N0}"),
        new(L("Baal"), $"{stats.Baal:N0}"),

        new(L("Endgame"), "", IsHeading: true),
        new(L("Map bosses"), $"{stats.MapBosses:N0}"),
        new(L("Dungeon bosses"), $"{stats.DungeonBosses:N0}"),
        new(L("Uber Tristram"), $"{stats.UberTristram:N0}"),
        new(L("Uber Ancients"), $"{stats.UberAncients:N0}"),
        new(L("Solo Diablo Clone"), L($"{stats.DiabloClone:N0}  (tier 2: {stats.DiabloCloneTier2:N0})")),
        new(L("Solo Rathma"), L($"{stats.Rathma:N0}  (tier 2: {stats.RathmaTier2:N0})")),
        new(L("Player kills"), $"{stats.PlayerKills:N0}"),
    ];

    /// <summary>Older copies of the chosen character, newest first.</summary>
    public ObservableCollection<BackupOption> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    /// <summary>
    /// The backups picker is open, over the landing's right half (it was a fold
    /// in the card). Opened by the card's Backups button, and by itself after a failed load, when the
    /// backups are the way out.
    /// </summary>
    [ObservableProperty] private bool _showBackups;

    /// <summary>Kept a moment after it closes, so the picker fades rather than vanishes.</summary>
    [ObservableProperty] private bool _isBackupPickerPresent;

    partial void OnShowBackupsChanged(bool value) =>
        Presence.Keep(value, () => ShowBackups, on => IsBackupPickerPresent = on);

    /// <summary>The fade over: a card's list let go of by its closing is the open character's, or none, from here.</summary>
    partial void OnIsBackupPickerPresentChanged(bool value)
    {
        if (!value && _backupsLetGo)
        {
            _backupsLetGo = false;
            RefreshBackups();
        }
    }

    /// <summary>A card's list closed and not yet read again: its rows stay for the fade, as a picker's contents do.</summary>
    private bool _backupsLetGo;

    /// <summary>
    /// The save whose backups the list holds: a card's, asked for from its menu before the character is opened
    /// (after Explorer's "Restore previous versions" - a row that opens the list) - else
    /// the open character's. One opened from it is that character seen as it was, as from the open one's list.
    /// </summary>
    private string? _backupsOf;

    private string? BackupsPath => _backupsOf ?? Session.CharacterPath;

    /// <summary>
    /// The selected card's backups, listed: the card's own button, beside Rename and Clone. The menu's row
    /// was the only way to them, and a menu is never the only way.
    /// </summary>
    [RelayCommand]
    private void ShowSelectedBackups()
    {
        if (IsBrowsable && !GameBlocked && Selected is { } card)
            ShowBackupsOf(card);
    }

    /// <summary>
    /// The backups list's buttons, the card's and Settings': off while the game runs, as Rename, Clone and Delete are
    /// - the list restores and deletes the save's copies.
    /// </summary>
    public string BackupsHint => GameBlocked ? (_sync?.BlockedFor(L("open backups")) ?? GameSync.Blocked(L("open backups"))) : L("Backups");

    public string ShowBackupsHint => GameBlocked ? BackupsHint : L("Show backups");

    /// <summary>Settings' button: the open character has copies, and the game is closed.</summary>
    public bool CanShowBackups => HasBackups && !GameBlocked;

    /// <summary>A card's backups, listed: its button's and its menu's row.</summary>
    public void ShowBackupsOf(CharacterCard card)
    {
        _sfxManager?.Method_ButtonSFX_Press();
        _backupsOf = card.Save.Path;
        _backupsLetGo = false;
        RefreshBackups();
        ShowBackups = true;
    }

    [RelayCommand]
    private void CloseBackupPicker()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        ShowBackups = false;

        // A card's list is that card's only while it is open. Kept on, a later failed load offered its copies as the
        // way out for another character, and Settings' line counted them under another's name.
        if (_backupsOf is not null && !Session.IsViewingBackup)
        {
            _backupsOf = null;
            _backupsLetGo = true;
        }
    }

    /// <summary>Which copy is open, so the list can show it. -1 when the character is.</summary>
    [ObservableProperty] private int _openBackupIndex = -1;

    // ---- the health mark ----------------------

    /// <summary>
    /// What is wrong with the open character, for the mark on its card - empty when nothing is, and
    /// then no mark at all: subtle, there when something is wrong and otherwise not shown. Items the game could not hold, on the character and on the
    /// shared stash. A character that did not read back exactly never opens, so that cannot be one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHealthIssue))]
    private string _healthIssue = "";

    public bool HasHealthIssue => HealthIssue.Length > 0;

    /// <summary>Most names a line lists before "and n more".</summary>
    private const int NamesShown = 3;

    /// <summary>
    /// Judged once for a burst, after what set it off has drawn: a load asked twice - the
    /// character, then its stash - and checked every item both times inside the load's freeze,
    /// 70 ms of it (measured).
    /// </summary>
    private void JudgeHealthSoon()
    {
        if (_healthQueued)
            return;

        _healthQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _healthQueued = false;

            // The page put away since it was asked: owed, and judged when it is next shown.
            if (!_health.PutOff())
                JudgeHealth();
        }, DispatcherPriority.Background);
    }

    private bool _healthQueued;

    private void JudgeHealth()
    {
        if (!Session.IsLoaded || !StatRowViewModel.FlagIllegitimate)
        {
            HealthIssue = "";
            return;
        }

        static string Line(System.Collections.Generic.List<string> names, string where)
        {
            string listed = string.Join(", ", names.Take(NamesShown));
            int more = names.Count - NamesShown;
            string count = Plural(names.Count, "1 illegal item", $"{names.Count} illegal items");
            return more > 0 ? L($"{count} {where}: {listed} and {more} more.") : L($"{count} {where}: {listed}.");
        }

        static System.Collections.Generic.List<string> Impossible(System.Collections.Generic.IEnumerable<D2SLib.Model.Save.Item> items) =>
            [.. items.Where(item => !item.IsCompact && Legitimacy.AnyImpossible(item))
                     .Select(item => PD2SPCompanion.Core.Items.ItemPresenter.Describe(item).Name)];

        var lines = new System.Collections.Generic.List<string>();
        var own = Impossible(Session.HeldItems);
        if (own.Count > 0)
            lines.Add(Line(own, L("on character")));
        var shared = Impossible(Stash.HeldItems);
        if (shared.Count > 0)
            lines.Add(Line(shared, L("in shared stash")));

        HealthIssue = string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The game is running, so nothing may write its files: Save, Restore and Rename are off and
    /// red - see GameSync.
    /// </summary>
    public bool GameBlocked => _sync?.IsWriteBlocked ?? false;

    /// <summary>Something to save, and the game closed.</summary>
    public bool CanSaveNow => Saves.CanSave && !GameBlocked;

    public string RenameHint => GameBlocked ? (_sync?.BlockedFor(L("rename")) ?? GameSync.Blocked(L("rename")))
        : ShortcutText.With(L("Rename"), L("F2"));   // its key, as the menu's row says it

    /// <summary>
    /// Restore's tooltip: the button stands only while a backup is on screen (ShowsRestore) - the autosave took
    /// the Save it was, and with it "Save current character" and "No changes".
    /// </summary>
    public string SaveHint => GameBlocked
        ? (_sync?.BlockedFor(L("restore")) ?? GameSync.Blocked(L("restore")))
        : L("Restore this backup") + Environment.NewLine + L("Current character is backed up first");

    // ---- saving by itself, and the session's history ----

    /// <summary>What the autosave saved this session, for undo, redo and the list.</summary>
    public SessionHistory? History { get; }

    private readonly AutoSave? _autoSave;
    private readonly RestoreReconcile? _restore;

    /// <summary>A character being edited: its history in reach. Not a backup on screen, which restores.</summary>
    public bool IsEditing => Session.IsLoaded && !Session.IsViewingBackup;

    /// <summary>A backup on screen: Restore is what saving it means, and the one save left.</summary>
    public bool ShowsRestore => Session.IsViewingBackup;

    public string UndoHint => History?.UndoHint ?? L("Nothing to undo");

    public string RedoHint => History?.RedoHint ?? L("Nothing to redo");

    private bool CanUndo() => History?.CanUndo == true;

    private bool CanRedo() => History?.CanRedo == true;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        // What is waiting to save is a state of its own first, as for undo: it ends the redo, and is never walked over.
        _autoSave?.Flush();
        if (History?.Redo() is { } failed)
            Session.Status = failed;
        else
            Session.Status = L("Redone.");
    }

    /// <summary>Back or forward to one state in the list: the first is the character as opened.</summary>
    [RelayCommand]
    private void GoTo(HistoryRow? row)
    {
        if (row is null || History is null)
            return;

        _sfxManager?.Method_ButtonSFX_Press();
        _autoSave?.Flush();
        if (History.GoTo(row.Index) is { } failed)
            Session.Status = failed;
        else
            Session.Status = row.Index == 0 ? L("Back to as opened.") : L($"Back to {row.Label}");
    }

    /// <summary>
    /// What it does: closes the character - what is waiting is saved first (the autosave), and only what
    /// cannot be saved is asked about. It said "Close without saving" from before the autosave.
    /// </summary>
    public string CloseHint => CanLeave ? L("Close character") : L("Close character") + Environment.NewLine + L("No character open.");

    /// <summary>The backup on screen, as its row in the list reads; null while none is.</summary>
    public BackupOption? OpenedBackup =>
        Session.IsViewingBackup && OpenBackupIndex >= 0 && OpenBackupIndex < Backups.Count ? Backups[OpenBackupIndex] : null;

    public bool HasOpenedBackup => OpenedBackup is not null;

    /// <summary>
    /// Leaving is available while a copy is open even if the character itself will not
    /// load, since that is the case you most need a way out of.
    /// </summary>
    public bool CanLeave => Session.IsLoaded || Session.IsViewingBackup || LoadFailed;

    [RelayCommand]
    private void ToggleBackups()
    {
        if (!ShowBackups && GameBlocked)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        // As the folder now holds them: every save takes a copy and thins the older ones, and the list was read
        // when this page was last shown - which, opened from Settings, may be many saves ago. A row for a copy
        // since thinned away opened nothing ("Could not find file"). The open character's: a card's,
        // listed from its menu before, is let go of.
        if (!ShowBackups)
        {
            _backupsOf = null;
            _backupsLetGo = false;
            RefreshBackups();
        }

        ShowBackups = !ShowBackups;
    }

    /// <summary>The open character's backups counted again, for the page that names them (Settings, as it is shown).</summary>
    public void LookAtBackups() => RefreshBackups();

    private void RefreshBackups()
    {
        Backups.Clear();

        if (BackupsPath is { } path)
        {
            // First, the file as the companion last wrote it - where the file is no longer that. It is the
            // one copy that holds what the last save brought; every backup is from before a save.
            if (SaveWriter.AsWritten(path) is { } last)
                Backups.Add(new BackupOption(last.FullName, last.LastWriteTime, last.Length, L("As last saved here.")));

            foreach (var file in SaveWriter.BackupsOf(path))
                Backups.Add(new BackupOption(file.FullName, SaveWriter.TakenAt(file), file.Length, SaveWriter.CaptionOf(file)));
        }

        // The one that is open, so the list marks it.
        OpenBackupIndex = Session.OpenedFrom is { } from
            ? Backups.Select((b, i) => (b, i))
                     .FirstOrDefault(e => string.Equals(e.b.Path, from, StringComparison.OrdinalIgnoreCase), (null!, -1)).i
            : -1;

        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(CanShowBackups));
        OnPropertyChanged(nameof(BackupsLine));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ShowsRestore));
        OnPropertyChanged(nameof(LoadFailed));
        OnPropertyChanged(nameof(IsBrowsable));
        // The list back, or stepped aside: its cards are asked again, or their marks let go.
        FindSoon();
        OnPropertyChanged(nameof(SaveHint));
        OnPropertyChanged(nameof(CanLeave));
        OnPropertyChanged(nameof(CloseHint));
        OnPropertyChanged(nameof(OpenedBackup));
        OnPropertyChanged(nameof(HasOpenedBackup));

        // A backup on screen is itself the change a restore writes; the set does not notice
        // one being opened or left, so Restore is told here.
        Saves?.SomethingChanged();
    }

    /// <summary>
    /// Deletes one earlier copy. The retention limit does this in bulk after a save; this is
    /// for throwing away a single one deliberately.
    /// </summary>
    [RelayCommand]
    private void DeleteBackup(BackupOption? backup)
    {
        if (backup is null || AskTwice.First(backup, Backups))
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        try
        {
            File.Delete(backup.Path);
            SaveWriter.ForgetCaption(new FileInfo(backup.Path));
            Session.Status = L($"Deleted backup from {backup.When}.");
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            Session.Status = L($"Not deleted: {ex.Message}");
        }

        RefreshBackups();
    }

    [RelayCommand]
    private void OpenBackup(BackupOption? backup)
    {
        if (backup is null || BackupsPath is not { } path)
            return;

        // A list left open as the game started: nothing is opened beside it.
        if (_sync?.RefusedWhileGameRuns(L("open backups")) == true)
            return;

        _sfxManager?.Method_ButtonSFX_Press();

        void Open()
        {
            Session.LoadBackup(backup.Path, path);
            RefreshBackups();

            // Out of the way once the backup is on the card; left open when it
            // would not load, as after any failed load, so another can be picked.
            ShowBackups = LoadFailed && HasBackups;

            // On screen: to the card that shows it, with its two answers - the list is opened from Settings.
            if (Session.IsViewingBackup)
                BackupShown?.Invoke();
        }

        // As closing does: what is waiting to save is saved first, and what could not be - a backup on screen being
        // edited, a refusal - is asked about. Dropped without a word until now.
        _autoSave?.Flush();
        if (_sync?.HasEdits ?? Session.HasUnsavedChanges)
        {
            _confirm.Ask(
                L("Unsaved changes"),
                L($"{Session.CharacterName} has unsaved changes. Close discards them."),
                L("CLOSE ANYWAY"),
                Open);
            return;
        }

        Open();
    }

    /// <summary>A backup has been put on screen: the page that shows it is this one (MainViewModel goes to it).</summary>
    public event Action? BackupShown;

    /// <summary>Whose backups the list holds, and how many: Settings' line beside the button that opens it.</summary>
    public string BackupsLine => !Session.IsLoaded && !Session.IsViewingBackup
        ? L("No character open")
        : Backups.Count == 0
            ? L($"{Session.CharacterName}: none")
            : Plural(Backups.Count, $"{Session.CharacterName}: 1 backup", $"{Session.CharacterName}: {Backups.Count} backups");

    /// <summary>
    /// How many characters sit side by side. Two is worth having, but not at the cost of
    /// cutting every entry's text in half, so a narrow page falls back to one.
    /// </summary>
    [ObservableProperty] private int _columns = 2;

    /// <summary>Below this, two columns leave too little for a figure and its details.</summary>
    private const double TwoColumnWidth = 560;

    public void SetViewportWidth(double width)
    {
        if (width > 0)
            Columns = width < TwoColumnWidth ? 1 : 2;
    }

    /// <summary>
    /// How many characters the list holds, at the right of the page's title while the list shows
    /// (it was a line of the page's own under the list, beside the strip's).
    /// </summary>
    public string CharacterCount => Plural(Characters.Count, "1 character", $"{Characters.Count} characters");

    partial void OnSelectedIndexChanged(int value)
    {
        // With a character open, the highlight is what is open - not a separate pointer at
        // something that might be opened. Anything that moves it gets put back, which is
        // the guarantee the view's own locking is only there to make pleasant.
        if (Session.IsLoaded && Selected?.Path != Session.SavePath)
        {
            int open = IndexOf(Session.SavePath);
            if (open >= 0 && open != value)
            {
                SelectedIndex = open;
                return;
            }
        }

        // Not while the list is read again: emptied and filled, it passes through no selection, and the
        // large view went blank for a frame each time a save was written. Refresh
        // says it once, at the end, with the card it settled on.
        if (!_refreshing)
        {
            OnPropertyChanged(nameof(Selected));
            _ = AnimateSelectedAsync();
        }
        OnPropertyChanged(nameof(CanRevive));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(CanDelete));
        LoadCommand.NotifyCanExecuteChanged();

        // Another card chosen: a name half typed for the last one is put away, as Explorer does. Not
        // while the list is read again - Refresh decides then, by whether the card is still the one.
        if (_refreshing)
            return;

        IsRenaming = false;
        IsCloning = false;
    }

    private bool _refreshing;

    private int IndexOf(string? path) =>
        path is null
            ? -1
            : Characters.Select((card, index) => (card, index))
                        .FirstOrDefault(entry => entry.card.Path == path, (null!, -1)).index;

    // ---- rename ------------------------------------------------------------------------------

    public PlayRules Rules { get; }

    /// <summary>
    /// The pencil on the selected card in the list: Casual and Free, not Legit,
    /// by the same setting that opens the character page's editing. Rename, clone and delete are
    /// file operations on a character in the list, as Explorer's are on a file - not on an open one.
    /// Nothing is open while the list shows, so nothing unsaved is in the way.
    /// </summary>
    public bool CanRename => Rules.EditsCharacter && IsBrowsable && Selected is not null;

    /// <summary>
    /// The name picker is open: rename or clone asking for the name, over the left page (it was a box in place of the card's name).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameTitle))]
    private bool _isRenaming;

    /// <summary>Kept a moment after it closes, so the picker fades rather than vanishes.</summary>
    [ObservableProperty] private bool _isNamePickerPresent;

    partial void OnIsRenamingChanged(bool value) =>
        Presence.Keep(value, () => IsRenaming, on => IsNamePickerPresent = on);

    /// <summary>The picker's title: what is done, and to whom.</summary>
    public string NameTitle => $"{(IsCloning ? "Clone" : "Rename")} {Selected?.Name}";

    /// <summary>The answer's word, as the delete question's is DELETE.</summary>
    public string NameConfirmLabel => IsCloning ? L("CLONE") : L("RENAME");

    [ObservableProperty] private string _newName = "";

    /// <summary>The name box names a copy rather than renaming (clone follows rename).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHint))]
    [NotifyPropertyChangedFor(nameof(NamePlaceholder))]
    [NotifyPropertyChangedFor(nameof(NameTitle))]
    [NotifyPropertyChangedFor(nameof(NameConfirmLabel))]
    private bool _isCloning;

    /// <summary>The tick's words: what the box does.</summary>
    public string NameHint => GameBlocked
        ? (_sync?.BlockedFor(IsCloning ? L("clone") : L("rename")) ?? GameSync.Blocked(IsCloning ? L("clone") : L("rename")))
        : IsCloning ? L("Clone") : L("Rename");

    public string CloneHint => GameBlocked ? (_sync?.BlockedFor(L("clone")) ?? GameSync.Blocked(L("clone"))) : L("Clone");

    /// <summary>What the empty name box asks for: a new name, or the copy's.</summary>
    public string NamePlaceholder => IsCloning ? L("Copy's name") : L("New name");

    [RelayCommand]
    private void ToggleRename()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        bool open = IsRenaming && !IsCloning;
        IsCloning = false;
        NewName = open ? "" : Selected?.Name ?? "";
        IsRenaming = !open;
    }

    [RelayCommand]
    private void ToggleClone()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        bool open = IsRenaming && IsCloning;
        IsCloning = !open;
        NewName = "";
        IsRenaming = !open;
    }

    /// <summary>The tick in the name box: a rename, or a clone.</summary>
    [RelayCommand]
    private void ConfirmName()
    {
        if (IsCloning)
            Clone();
        else
            Rename();
    }

    /// <summary>
    /// A copy of the selected character under the name typed - its files, the name inside the save
    /// set, its items' ids kept (copies, as the duplicate badge says). Casual and Free, as rename;
    /// refused while the game runs or the patch guard holds writes.
    /// </summary>
    private void Clone()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        if (!CanRename || Selected is not { } card)
            return;

        if (_sync?.RefusedWhileRunning(L("clone")) == true)
            return;

        string path = card.Path;
        string original = card.Name;
        string name = NewName.Trim();
        try
        {
            CharacterRename.Clone(path, name);
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            Session.Status = ex.Message;
            return;
        }

        IsRenaming = false;
        IsCloning = false;
        NewName = "";
        Refresh();
        Session.Status = L($"Cloned {original} as {name}.");
    }

    /// <summary>Delete: any play mode, as the game deletes characters too - the selected one in the list, only with the game closed.</summary>
    public bool CanDelete => IsBrowsable && Selected is not null;

    public string DeleteHint => GameBlocked ? (_sync?.BlockedFor(L("delete")) ?? GameSync.Blocked(L("delete")))
        : ShortcutText.With(L("Delete"), L("Del"));  // the warning is the confirm's, which asks next

    /// <summary>
    /// Deletes the selected character, asked first: its files and its backups, nothing kept
    /// (CharacterRename.Delete). The vault keeps what it holds under the name.
    /// </summary>
    [RelayCommand]
    private void DeleteCharacter()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        if (!CanDelete || Selected is not { } card)
            return;

        if (_sync?.RefusedWhileRunning(L("delete")) == true)
            return;

        string path = card.Path;
        string name = card.Name;
        _confirm.Ask(
            L("Delete character"),
            L($"Delete {name} and its backups?") + Environment.NewLine + L("Warning: Cannot be undone!"),
            L("DELETE"),
            deletes: true,
            onConfirm: () =>
            {
                if (_sync?.RefusedWhileRunning(L("delete")) == true)
                    return;

                try
                {
                    CharacterRename.Delete(path);
                    Session.Status = L($"Deleted {name}.");
                }
                catch (Exception ex)
                {
                    Log.Caught(ex);
                    Session.Status = L($"Not deleted: {ex.Message}");
                }
                Refresh();
            });
    }

    /// <summary>Revive: a dead hardcore character, in Casual and Free - not Legit.</summary>
    public bool CanRevive => Rules.EditsCharacter && Session.IsLoaded && !Session.IsViewingBackup && Selected?.IsDead == true;

    public string ReviveHint => GameBlocked ? (_sync?.BlockedFor(L("revive")) ?? GameSync.Blocked(L("revive"))) : L("Revive");

    /// <summary>Takes a hardcore death back: the dead flag cleared and written, the character opened again.</summary>
    [RelayCommand]
    private void Revive()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        if (!CanRevive || Session.SavePath is not { } path)
            return;

        _autoSave?.Flush();
        if (Saves.HasChanges)
        {
            Session.Status = L("Require saved character.");
            return;
        }

        if (_sync?.RefusedWhileRunning(L("revive")) == true)
            return;

        string name = Session.CharacterName;
        try
        {
            if (!CharacterRename.Revive(path))
                return;
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            Session.Status = L($"Not revived: {ex.Message}");
            return;
        }

        _sync?.Opened();
        Session.Load(path);
        Refresh();
        OnPropertyChanged(nameof(CanRevive));
        Session.Status = L($"Revived {name}.");
    }

    /// <summary>The name picker put away, nothing renamed: Cancel, a click beside it, or Escape.</summary>
    [RelayCommand]
    private void CancelRename()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        IsRenaming = false;
        NewName = "";
    }

    /// <summary>
    /// Renames the selected character - its save, the files beside it, its backups and what the
    /// vault keeps under its name - and keeps it selected under the new one. See
    /// <see cref="CharacterRename"/>. Only from the list, where nothing is open and nothing unsaved,
    /// and only with the game closed, which would write the old name back when it saves.
    /// </summary>
    private void Rename()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (!CanRename || Selected is not { } card)
            return;

        if (_sync?.RefusedWhileRunning(L("rename")) == true)
            return;

        string path = card.Path;
        string oldName = Path.GetFileNameWithoutExtension(path);
        string newName = NewName.Trim();

        string renamed;
        try
        {
            renamed = CharacterRename.Rename(path, newName);
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            Session.Status = ex.Message;
            return;
        }

        int records = 0;
        try
        {
            records = _vaults?.Store?.RenameCharacter(oldName, newName) ?? 0;

            // And the records of every other vault of the season. One update per file, no blob touched.
            records += _vaults?.RenameInOtherVaults(oldName, newName) ?? 0;
        }
        catch (Exception)
        {
            // The files are renamed; the finds and "By" stay under the old name, which is only
            // a label.
        }

        // Its pages' locks (the cube, the stash) are kept by its name too.
        Rules.Settings.RenamePageLocks(oldName, newName);

        IsRenaming = false;
        NewName = "";
        Refresh();
        if (IndexOf(renamed) is var at and >= 0)
            SelectedIndex = at;
        _vaults?.SayChanged();

        Session.Status = L($"Renamed {oldName} to {newName}.");
    }

    /// <summary>Which reading of the list the figures are being drawn for; a newer one stops an older.</summary>
    private int _figuresRound;

    /// <summary>
    /// Each card's character in its gear, the selected one first,
    /// one character at a time with the page answering in between - each save is read here, on the UI
    /// thread, where the parser's one layout lives, and the open character's layout put back after.
    /// A card keeps its class picture until its own figure is ready, and for good where there is none.
    /// </summary>
    private async System.Threading.Tasks.Task DrawFiguresAsync()
    {
        int round = ++_figuresRound;
        var cards = Characters.OrderBy(card => card == Selected ? 0 : 1).ToList();
        MirrorOpenFigure();
        foreach (var card in cards.Where(card => card.AwaitsFigure && !IsOpen(card)))
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (round != _figuresRound)
                return;
            card.Figure = await PD2SPCompanion.Shell.Imaging.FigureBitmaps.OfFileAsync(
                card.Path, () => SavedItems.Of(card.Path, () => { if (Session.IsLoaded) Session.UseMyLayout(); })?.Look);
            card.AwaitsFigure = false;
            if (card == Selected)
                await AnimateSelectedAsync();
        }
        if (round == _figuresRound)
            await AnimateSelectedAsync();
    }

    /// <summary>The session's card whose figure the open character's card here shows.</summary>
    private CharacterCard? _followed;

    private bool IsOpen(CharacterCard card) =>
        Session.IsLoaded && Session.OpenCard is not null
        && string.Equals(card.Path, Session.SavePath, StringComparison.OrdinalIgnoreCase);

    private void FollowOpenCard()
    {
        if (_followed is not null)
            _followed.PropertyChanged -= OnOpenFigureChanged;
        _followed = Session.OpenCard;
        if (_followed is not null)
            _followed.PropertyChanged += OnOpenFigureChanged;
        MirrorOpenFigure();
    }

    private void OnOpenFigureChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CharacterCard.Figure) or nameof(CharacterCard.AwaitsFigure))
            MirrorOpenFigure();
    }

    /// <summary>
    /// The open character's card shows the Character page's figure - drawn from the save in memory, so a
    /// gear change shows here at once. Drawn from the file, it showed the old gear until the save was
    /// written, then the new: a gearless figure flicking to the equipped one.
    /// </summary>
    private void MirrorOpenFigure()
    {
        if (_followed is not { } open || !Session.IsLoaded)
            return;
        if (Characters.FirstOrDefault(IsOpen) is not { } mine)
            return;
        if (open.Figure is { } figure)
            mine.Figure = figure;
        mine.AwaitsFigure = open.Figure is null && open.AwaitsFigure;
    }

    /// <summary>The card whose figure plays: the selected one, its animation made when it was selected.</summary>
    private CharacterCard? _playing;

    /// <summary>
    /// The selected card's figure made whole - the others show their first frame, which is all of them that
    /// is kept (FigureBitmaps). The card selected before goes back to its still, its frames let go. The
    /// still stays on screen meanwhile; the animation takes it over without a fade.
    /// </summary>
    private async System.Threading.Tasks.Task AnimateSelectedAsync()
    {
        var card = Selected;
        if (ReferenceEquals(card, _playing))
            return;
        // The open character's card plays the session's figure (MirrorOpenFigure), already whole.
        if (card is not null && IsOpen(card))
        {
            _playing = card;
            return;
        }
        if (_playing is { } before && PD2SPCompanion.Shell.Imaging.FigureBitmaps.Drawn(before.Path) is { } still)
            before.Figure = still;
        _playing = null;
        if (card is null || card.Figure is null)
            return;

        _playing = card;
        var whole = await PD2SPCompanion.Shell.Imaging.FigureBitmaps.AnimatedAsync(card.Path);
        if (whole is not null && ReferenceEquals(_playing, card))
            card.Figure = whole;
    }

    private void Refresh()
    {
        // Keep the card the user was looking at, since this also runs unprompted when the
        // game writes a save.
        string? wasShowing = Selected?.Path;

        _refreshing = true;
        try
        {
            Characters.Clear();
            // A figure already drawn shows at once; any other card waits for its own (DrawFiguresAsync).
            foreach (var save in SaveSummary.FromFolder(Session.SaveFolder))
            {
                var card = new CharacterCard(save);
                card.Figure = IsOpen(card) ? Session.OpenCard?.Figure : PD2SPCompanion.Shell.Imaging.FigureBitmaps.Drawn(save.Path);
                card.AwaitsFigure = card.Figure is null;
                Characters.Add(card);
            }

            int restored = wasShowing is null
                ? -1
                : Characters.Select((card, index) => (card, index))
                            .FirstOrDefault(entry => entry.card.Path == wasShowing, (null!, -1)).index;

            SelectedIndex = restored >= 0 ? restored : (Characters.Count > 0 ? 0 : -1);
        }
        finally
        {
            _refreshing = false;
        }
        OnPropertyChanged(nameof(Selected));
        _ = AnimateSelectedAsync();

        // The same card back: a rename or clone being typed for it stays open - the list is read again
        // whenever a save is written, the game's included, and that took the half-typed name with it.
        // Another card, or none: put away, as a choice would.
        if (Selected?.Path != wasShowing)
        {
            IsRenaming = false;
            IsCloning = false;
        }

        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(CountLine));
        _ = DrawFiguresAsync();
        // New cards: what the standing search finds on each, from the saves already read where they have not changed.
        _ = FindAsync();

        // Empty because there is nowhere to look yet is not the same as empty because
        // there is nothing there, and only the second is worth saying. On a first run this
        // would otherwise name a folder that is the empty string.
        if (Characters.Count == 0 && Session.IsConfigured)
            Session.Status = L($"No characters in {Session.SaveFolder}.");
    }

    private bool CanLoad() => Session.IsLoaded || Selected is not null;

    /// <summary>
    /// Opens the selected character, or reads the open one off disk again.
    /// <para>
    /// The second is what makes testing quick: play the character, let the game write it,
    /// and one press brings those changes back here - rather than closing, finding the card
    /// again and opening it.
    /// </para>
    /// </summary>
    public bool IsReload => Session.IsLoaded;

    /// <summary>
    /// The button under the opened card: RELOAD for one open, TRY AGAIN for one that did not open - "OPEN" there read as
    /// though it could.
    /// </summary>
    public string LoadLabel => IsLoading ? L("OPENING...") : IsReload ? L("RELOAD") : LoadFailed ? L("TRY AGAIN") : L("OPEN");

    /// <summary>Why the character on the card did not open; empty otherwise.</summary>
    public string LoadProblem => LoadFailed ? Session.LoadProblem : "";

    /// <summary>The card's own button.</summary>
    public string OpenLabel => IsLoading ? L("OPENING...") : L("OPEN");

    /// <summary>
    /// A character being opened: its pages are laid out with it (Opened), which takes a moment, said on the
    /// button that was pressed rather than left to look like a hang.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadLabel), nameof(OpenLabel))]
    private bool _isLoading;

    /// <summary>
    /// Whose file is being opened, while one is (<see cref="IsLoading"/>): the window's search box says it too
    /// (QuickSearchViewModel.BoxHint) - a character opened from a find of the box has its button out of sight,
    /// and the window stood still for the best part of a second with nothing said where the eye was.
    /// </summary>
    public string OpeningName { get; private set; } = "";

    private void SayOpening(string path) => OpeningName = System.IO.Path.GetFileNameWithoutExtension(path);

    /// <summary>A character opened: the pages it is mostly seen on are laid out now (MainView).</summary>
    public event Action? Opened;

    public string LoadHint => IsReload
        ? L("Reload current character")
        : _sync?.IsGameRunning == true ? GameSync.Blocked(L("edit"))
        : LoadFailed ? L("Open character again")
        : L("Open selected character");

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void Load()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        // A reload is a read, but it drops anything typed here that the file does not have - the
        // same loss closing asks about, so it goes the same way: what is waiting to save is saved
        // first (the autosave), and only what could not be saved is asked about. It asked about edits
        // the autosave was about to write (wording from before the autosave).
        if (IsReload)
            _autoSave?.Flush();

        if (IsReload && Saves.HasChanges)
        {
            _confirm.Ask(
                L("Unsaved changes"),
                L($"{Session.CharacterName} has unsaved changes. Reload discards them."),
                L("RELOAD ANYWAY"),
                Reopen);
            return;
        }

        Reopen();
    }

    /// <summary>
    /// Another character opened in place of the one open - a find of the window's search that lies on it,
    /// its row's tick pressed (<see cref="QuickSearchViewModel"/>). What waits to be saved is saved first, and
    /// what could not be is asked about, as closing asks; <paramref name="then"/> runs once it is loaded.
    /// </summary>
    public void SwitchTo(string path, Action? then = null)
    {
        if (_sync?.RefusedWhileGameRuns("edit") == true)
            return;

        if (Session.IsLoaded)
        {
            _autoSave?.Flush();

            if (_sync?.HasEdits ?? Session.HasUnsavedChanges)
            {
                _confirm.Ask(
                    L("Unsaved changes"),
                    L($"{Session.CharacterName} has unsaved changes. Close discards them."),
                    L("CLOSE ANYWAY"),
                    () => Swap(path, then));
                return;
            }
        }

        Swap(path, then);
    }

    private void Swap(string path, Action? then)
    {
        if (Session.IsLoaded)
        {
            Session.Close();
            ShowBackups = false;
        }

        SayOpening(path);
        IsLoading = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _sync?.Opened();
                Session.Load(path);

                // The card follows, read or not: it was left on the character before, and the home page then showed
                // that one's name, figure and Revive over what the buttons act on. After the load, for the guard
                // that holds the highlight on what is open (OnSelectedIndexChanged). A save the list has not read yet -
                // it is read while this page is shown - has it read now.
                if (IndexOf(path) < 0)
                    Refresh();

                if (IndexOf(path) is var at and >= 0)
                    SelectedIndex = at;

                ShowBackups = LoadFailed && HasBackups;

                if (Session.IsLoaded)
                {
                    Opened?.Invoke();
                    then?.Invoke();
                }
            }
            finally
            {
                IsLoading = false;
            }
        }, DispatcherPriority.Background);
    }

    private void Reopen()
    {
        // The open character when there is one, so a reload does not quietly become a load
        // of whatever card the list happens to be showing.
        string? path = Session.IsLoaded ? Session.SavePath : Selected?.Path;
        if (path is null)
            return;

        // Nothing is edited while the game runs: it would close it again.
        if (_sync?.RefusedWhileGameRuns("edit") == true)
            return;

        // "Loading..." drawn first, then the open: posted below the render, so a frame shows it before the
        // UI thread is taken up.
        SayOpening(path);
        IsLoading = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _sync?.Opened();
                Session.Load(path);

                // Decided once the load has finished, not while it is running: during a load the
                // session has already recorded the attempt but not yet the success, so judging it
                // any earlier leaves the panel open after every load.
                ShowBackups = LoadFailed && HasBackups;

                if (Session.IsLoaded)
                    Opened?.Invoke();
            }
            finally
            {
                IsLoading = false;
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Drops every edit since the character was opened. Nothing on disk is touched, so
    /// this is not asked about the way closing is - and it cannot be reached at all
    /// unless something has actually changed.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        // What is waiting to save is a state of its own first, so undo takes back the last thing done.
        _autoSave?.Flush();
        if (History?.Undo() is { } failed)
            Session.Status = failed;
        else
            Session.Status = L("Undone.");
    }

    /// <summary>Writes the open character back. The session backs the original up first.</summary>
    [RelayCommand]
    private void Save()
    {
        _sfxManager?.Method_ButtonSFX_Press();


        // A restore takes the character back; the vault and the shared stash, which other
        // characters use too, stay as they are - only this character's own moves since are put
        // right, by adding what would be lost (RestoreReconcile). Said before it is done.
        if (Session.IsViewingBackup)
        {
            var plan = _restore?.PlanFor();
            var said = new List<string> { L("Current character is backed up first.") };
            if (plan is not null)
                said.AddRange(plan.Lines(done: false));

            // Only when the live stash will not read does the backup's copy go back with it -
            // and then an item it holds again, which another character took out, is in both.
            if (Stash.RestoresWithCharacter)
            {
                said.Add(L("Shared stash unreadable: backup's copy restored too."));
                if (Stash.OthersHoldingTheseItems() is { Count: > 0 } also)
                {
                    string holders = Joined(also);
                    said.Add(L($"Warning: {holders} hold items from it. Restoring duplicates them!"));
                }
            }

            // Replacing what the game saved since is what a restore is, and it was just asked.
            _confirm.Ask(L("Restore backup"), string.Join(Environment.NewLine, said), L("RESTORE"), () =>
            {
                bool Restore()
                {
                    // Into the vault first: what goes back is a gain, written before the file.
                    var refused = plan is not null && _restore is not null ? _restore.Apply(plan) : [];
                    if (!Write())
                        return false;

                    var lines = (plan?.Lines(done: true) ?? []).Concat(refused).ToList();
                    if (lines.Count > 0)
                        Session.Status = string.Join(" ", lines);
                    return true;
                }

                if (_sync is not null)
                    _sync.Save(Restore, restoring: true);
                else
                    Restore();
            });
            return;
        }

        // Asks which to keep where the game saved since - see GameSync.
        if (_sync is not null)
            _sync.Save(Write);
        else
            Write();
    }

    /// <summary>Writes every file in the set; says whether it did.</summary>
    private bool Write()
    {
        bool restoring = Session.IsViewingBackup;
        string? from = restoring ? Session.OpenedFrom : null;
        var trouble = Saves.Commit(out var report);
        if (trouble is not null)
            Session.Status = trouble;

        // The character as it was before the restore, said so.
        if (trouble is null && from is not null && report.Backup is { } backup)
            SaveWriter.Caption(backup, L($"Before restoring the copy from {SaveWriter.TakenAt(new FileInfo(from)):yyyy-MM-dd HH:mm}."));

        // Restored, the character on disk is the backup's: its history starts from there.
        if (trouble is null && restoring)
            History?.Restart();

        // The write just left a new copy beside the character, and the retention limit
        // may have taken older ones away. Commit changes no property this page watches,
        // so the list has to be told.
        RefreshBackups();
        return trouble is null;
    }

    /// <summary>
    /// Closes the character and returns to the list - with a backup on screen too: the card's
    /// corner and the top bar close the whole thing. Touches no file.
    /// </summary>
    [RelayCommand]
    private void Close() => Leaving(toList: true);

    /// <summary>Puts the backup on screen away and goes back to the character itself.</summary>
    [RelayCommand]
    private void CloseBackup()
    {
        if (Session.IsViewingBackup)
            Leaving(toList: false);
    }

    private void Leaving(bool toList)
    {
        _sfxManager?.Method_ButtonSFX_Press();

        // What is waiting to save is saved first: closing loses nothing (the autosave). What
        // could not be saved - a backup on screen being edited, a refusal - is asked about.
        _autoSave?.Flush();

        // Going back to the character from an earlier copy throws away any edits made to
        // that copy, so it is asked about too. Staged stash and vault moves count as well.
        if (_sync?.HasEdits ?? Session.HasUnsavedChanges)
        {
            _confirm.Ask(
                L("Unsaved changes"),
                L($"{Session.CharacterName} has unsaved changes. Close discards them."),
                L("CLOSE ANYWAY"),
                () => Leave(toList));
            return;
        }

        Leave(toList);
    }

    /// <summary>Closing itself, once there is nothing left to ask about.</summary>
    private void Leave(bool toList)
    {
        // Back to the character reads it, which the game running refuses (as every open): to the list instead.
        if (!toList && Session.IsViewingBackup && Session.CharacterPath is { } path && _sync?.RefusedWhileGameRuns(L("open")) != true)
        {
            Session.Load(path);
            RefreshBackups();
            return;
        }

        Session.Close();
        ShowBackups = false;
        Refresh();
    }
}

/// <summary>One line of the statistics panel, or a group heading when it has no value.</summary>
public sealed record StatLine(string Label, string Value, bool IsHeading = false);
