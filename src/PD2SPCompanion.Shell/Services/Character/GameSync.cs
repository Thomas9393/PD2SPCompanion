using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// Keeps what is open here in step with the game, which writes the same files.
/// <list type="bullet">
/// <item>The game saving the open character or the shared stash is noticed when it happens -
/// on every change in the save folder and whenever the window is activated - not at Save. With
/// nothing unsaved, what the game saved is opened; with edits, the strip warns once.</item>
/// <item><b>Nothing is written to the game's files while the game runs</b> - no save, no
/// restore, no rename, and no vault move, buy or sell, which would write them at the next save.
/// The game keeps the character in memory and writes it back on leaving a game, and any
/// character leaving rewrites the shared stash; nothing outside can tell a character in a game
/// from one at character select (measured). Deliberate, over an earlier "save
/// anyway at character select". Editing and staging go on, and the vault's own work - folders,
/// its gold, storing a copy - writes only the vault, which the game never touches.</item>
/// <item>Saving over what the game saved is the player's choice - keep the game's, or keep
/// theirs with the game's version in the backups - where it used to be a refusal after the
/// editing was done.</item>
/// </list>
/// </summary>
public sealed partial class GameSync : ObservableObject
{
    /// <summary>
    /// What a button turned off by a running game says, on hover and at a click: its own act, not
    /// "save" on every one - Rename saying "close it to save" read as a different button's words.
    /// </summary>
    /// <param name="act">Already in the language in use: L("rename").</param>
    public static string Blocked(string act) => L($"Require game closed to {act}.");

    /// <summary>
    /// What a button turned off says: the patch guard's hold first,
    /// else the running game's words.
    /// </summary>
    public string BlockedFor(string act) =>
        _guard?.IsHeld == true ? _guard.HeldLine : Blocked(act);

    /// <summary>
    /// Whether writes to the game's files are refused now, as last looked: the game running, or the
    /// patch guard holding them. What the buttons turn red on.
    /// </summary>
    public bool IsWriteBlocked => IsGameRunning || _guard?.IsHeld == true;

    private readonly PatchGuardService? _guard;
    private readonly CharacterSession _session;
    private readonly SharedStashSession _stash;
    private readonly SaveSet _saves;
    private readonly ConfirmService _confirm;

    /// <summary>What the strip was last warned about, so one change is said once.</summary>
    private string? _warned;

    /// <summary>
    /// Whether the game is running, as last looked - every two seconds and on every
    /// <see cref="Check"/>. What the buttons turn red on; Save asks the process again itself.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWriteBlocked))]
    private bool _isGameRunning;

    private readonly DispatcherTimer? _poll;

    /// <summary>The compendium of the open character's mode, a third file the game writes beside the two.</summary>
    private readonly CompendiumSession? _compendium;

    public GameSync(CharacterSession session, SharedStashSession stash, SaveSet saves, ConfirmService confirm,
                    PatchGuardService? guard = null, CompendiumSession? compendium = null)
    {
        _guard = guard;
        _compendium = compendium;
        if (guard is not null)
        {
            guard.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName == nameof(PatchGuardService.IsHeld))
                    OnPropertyChanged(nameof(IsWriteBlocked));
            };
        }

        _session = session;
        _stash = stash;
        _saves = saves;
        _confirm = confirm;

        // Every write of the set asks here first, the history's too.
        saves.Refuse = RefuseWrite;

        // After the stash's own handler, which re-reads a clean stash first.
        session.SaveFolderChanged += CheckFolder;

        // The game closing writes nothing to the save folder from character select, so it is
        // looked for rather than waited on: a process lookup every two seconds is nothing.
        try
        {
            _poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => LookAside());
            _poll.Start();
        }
        catch (Exception)
        {
            // No dispatcher (a test harness): Check still looks.
        }
    }

    /// <summary>
    /// Looks at the process and says it on the strip when it changes: once on the game starting
    /// - seen at the latest on switching back to the editor - and once on it closing.
    /// </summary>
    private void Look() => Seen(GameRunning);

    /// <summary>
    /// The timer's look, with the process lookup off the UI thread: it lists every process on the
    /// machine, some 20 ms, and every two seconds that was a stall (measured). What it
    /// finds is said back on the UI thread, as Look says it.
    /// </summary>
    private void LookAside()
    {
        if (_looking)
            return;

        _looking = true;
        string root = _session.GameRoot;
        System.Threading.Tasks.Task.Run(() => IsRunningFrom(root)).ContinueWith(looked =>
            Dispatcher.UIThread.Post(() =>
            {
                _looking = false;
                if (looked.IsCompletedSuccessfully)
                    Seen(looked.Result);
            }));
    }

    private bool _looking;

    private void Seen(bool running)
    {
        if (running == IsGameRunning)
            return;

        IsGameRunning = running;

        if (running)
        {
            if (_session.IsLoaded)
                CloseForGame();
            else
                _session.Status = L("Require game closed to edit character.");
            return;
        }

        _session.Status = ClosedName is { } name
            ? L($"Game closed. Load {name} to continue.")
            : L("Game closed.");
    }

    /// <summary>
    /// The character the game's starting closed, and its name - offered again once it closes. Null
    /// when nothing was closed for it.
    /// </summary>
    public string? ClosedPath { get; private set; }

    public string? ClosedName { get; private set; }

    /// <summary>Raised just before a character is closed for the game, with its path.</summary>
    public event Action<string>? ClosingForGame;

    /// <summary>
    /// The game has started: the character goes - nothing may be edited that the game is about to
    /// hold. What is unsaved is written first: the game reads no save while it starts, and only at
    /// its character screen. A write that fails keeps the character open, saying so: losing the
    /// edits would be worse than the rule. So too a backup on screen with edits: writing it would be
    /// a restore nobody asked for, and closing it would lose them.
    /// </summary>
    private void CloseForGame()
    {
        string name = _session.CharacterName;

        if (HasEdits && _session.IsViewingBackup)
        {
            _session.Status = L($"Game started. {name} backup not restored. Kept open; close game to restore.");
            return;
        }

        if (HasEdits)
        {
            // Only while the game has just started: that it cannot have reached its character screen is what makes
            // this write safe. Seen late - the editor asleep or busy - the edits stay unsaved and the character open,
            // as when the write fails. Nor over what the game wrote since it was read:
            // Prepare refuses that (it was accepted here, and written over).
            string? trouble = JustStarted() ? null : L("Game seen too late.");
            if (trouble is null)
            {
                _closingForGame = true;
                try
                {
                    trouble = _saves.Commit();
                }
                finally
                {
                    _closingForGame = false;
                }
            }

            if (trouble is not null)
            {
                _session.Status = L($"Game started. {name} not saved: {trouble} Kept open; close game to save.");
                return;
            }
        }

        if (_session.SavePath is { } path)
        {
            ClosedPath = path;
            ClosedName = name;
            ClosingForGame?.Invoke(path);
        }

        _saves.Revert();
        _session.Close();
        _session.Status = L($"Game started. {name} closed; reload after game closes.");
    }

    /// <summary>The write <see cref="CloseForGame"/> makes itself, the game just started: let past the game's check, not the hold's.</summary>
    private bool _closingForGame;

    /// <summary>
    /// How long after the game's start its write may still be made. The game reads no save before its character
    /// screen, a menu and a click away; the editor looks every two seconds.
    /// </summary>
    private static readonly TimeSpan JustNow = TimeSpan.FromSeconds(5);

    /// <summary>Every game running started within <see cref="JustNow"/>; a start that cannot be read is not.</summary>
    private bool JustStarted()
    {
        var starts = GameProcess.Starts(_session.GameRoot, when: true);
        return starts.Count > 0 && starts.All(start => start is { } at && DateTime.UtcNow - at < JustNow);
    }

    /// <summary>
    /// What the save set asks before it writes (<see cref="SaveSet.Refuse"/>): the patch guard's hold, then the game -
    /// asked of the process now, unless <see cref="Save"/> has just asked it in the same breath (the autosave's
    /// look: one listing a save).
    /// </summary>
    private string? RefuseWrite(string act)
    {
        if (_guard?.IsHeld == true)
            return BlockedFor(act);

        if (_closingForGame || _lookedNow)
            return null;

        return RunningNow() ? Blocked(act) : null;
    }

    /// <summary>Set by <see cref="Save"/> around a write it has just looked for the game before: no second look.</summary>
    private bool _lookedNow;

    /// <summary>A write <see cref="Save"/> has looked for the game before, a moment ago and on this same call.</summary>
    private void Looked(Func<bool> write)
    {
        _lookedNow = true;
        try
        {
            write();
        }
        finally
        {
            _lookedNow = false;
        }
    }

    /// <summary>A character is being opened: what the game closed is no longer waiting.</summary>
    public void Opened()
    {
        ClosedPath = null;
        ClosedName = null;
    }

    /// <summary>
    /// Whether anything edited here would be lost by leaving - edits, staged moves, purchases. A
    /// backup merely open is not: nothing was done to it.
    /// </summary>
    public bool HasEdits =>
        _session.HasUnsavedChanges || _stash.HasUnsavedChanges || (_saves.HasChanges && !_session.IsViewingBackup);

    /// <summary>Whether the game is running now, asked of the process. See <see cref="IsRunningFrom"/>.</summary>
    public bool GameRunning => IsRunningFrom(_session.GameRoot);

    /// <summary>Whether a game from this folder is running: <see cref="GameProcess.IsRunningFrom"/>.</summary>
    public static bool IsRunningFrom(string gameRoot) => GameProcess.IsRunningFrom(gameRoot);

    /// <summary>
    /// Looks at the game and the files. Called when the window is activated - the player coming back
    /// from the game, whose click must not edit before the game is seen - and on every change in the
    /// save folder (<see cref="CheckFolder"/>).
    /// </summary>
    public void Check() => Check(lookNow: true);

    /// <summary>
    /// A change in the save folder - this editor's own autosave of every move among them. The game is
    /// looked for now only where the files we hold are not what we wrote: listing every process was
    /// 10 ms of each move on the UI thread (measured). Otherwise it is looked for off the UI
    /// thread, as the two-second poll looks.
    /// </summary>
    private void CheckFolder() => Check(lookNow: false);

    private void Check(bool lookNow)
    {
        string? changed = _session.IsLoaded ? Changed() : null;
        if (lookNow || changed is not null)
            Look();
        else
            LookAside();

        // A PD2 patched under the running editor leaves its tables stale (PatchGuardService).
        _guard?.CheckAgain();

        if (!_session.IsLoaded)
        {
            _warned = null;
            return;
        }

        // Looked at again: the game seen starting closes the character, and its files with it.
        changed = Changed();
        if (changed is null)
        {
            _warned = null;
            return;
        }

        // Nothing here to lose: take what the game saved. A backup on screen is left alone -
        // it is not the file, and the player opened it on purpose.
        if (!_saves.HasChanges)
        {
            if (_session.ChangedOnDisk && _session.CharacterPath is { } path)
            {
                _session.Load(path);
            }
            else
            {
                if (_stash.ChangedOnDisk)
                    _stash.Load();
                if (_compendium?.ChangedOnDisk == true)
                    _compendium.Load();
            }

            _session.Status = L($"{changed} reloaded. Saved in game.");
            _warned = null;
            return;
        }

        if (_warned == changed)
            return;

        _warned = changed;
        _session.Status = L($"{changed} changed in game. Save asks which to keep.");
    }

    /// <summary>
    /// Saves by way of <paramref name="write"/>, which says whether it wrote - refused while the
    /// game runs, and asking which to keep where the game saved these files since.
    /// </summary>
    /// <param name="restoring">
    /// An open backup being put back: replacing what the game saved since is what a restore is,
    /// and the restore question has already said so.
    /// </param>
    /// <param name="looked">
    /// The caller asked the process itself a moment ago (<see cref="GameRunning"/>) and stopped if it runs - the
    /// autosave: listing every process is 4 ms on the UI thread, and a save listed them twice.
    /// The patch guard's hold is still asked.
    /// </param>
    public void Save(Func<bool> write, bool restoring = false, bool looked = false)
    {
        // Asked of the process now, not of the last look: the game may have started a second ago.
        string act = restoring ? L("restore") : L("save");
        if (looked ? RefusedWhileHeld(act) : RefusedWhileRunning(act))
            return;

        if (restoring)
        {
            AcceptDisk();
            Looked(write);
            return;
        }

        // The answer to "Changed in game" below writes later, and looks again (the save set's gate).
        if (Changed() is not { } changed)
        {
            Looked(write);
            return;
        }

        _confirm.Ask(
            L("Changed in game"),
            L($"{changed} changed in game since it was opened."),
            L("KEEP GAME'S"),
            KeepGames,
            L("KEEP MINE"),
            () => KeepMine(write),
            confirmHint: L("Drop the changes here and open what the game saved."),
            alternativeHint: L("Save the changes here. The game's version goes to backups."));
    }

    /// <summary>
    /// Says so and returns true while the game is running - and nothing else. For opening a
    /// character, which reads: a patch guard's hold still lets one be opened and looked at.
    /// </summary>
    public bool RefusedWhileGameRuns(string act)
    {
        if (!RunningNow())
            return false;

        _session.Status = Blocked(act);
        return true;
    }

    /// <summary>
    /// Says so and returns true while the game is running, or while the patch guard holds writes.
    /// For every act that writes the save.
    /// </summary>
    public bool RefusedWhileRunning(string act)
    {
        if (RefusedWhileHeld(act))
            return true;

        if (!RunningNow())
            return false;

        _session.Status = Blocked(act);
        return true;
    }

    /// <summary>A new PD2 the saves have not been checked against: nothing is written with it. Says so.</summary>
    private bool RefusedWhileHeld(string act)
    {
        if (_guard?.IsHeld != true)
            return false;

        _session.Status = BlockedFor(act);
        return true;
    }

    /// <summary>Whether the game runs, asked of the process now; kept as <see cref="IsGameRunning"/>.</summary>
    private bool RunningNow()
    {
        bool running = GameRunning;

        // Seen, not only set: Seen acts on a change alone, so a game first noticed here - a Save refused as it
        // started - left the character open beside it for good. Posted: the act
        // that asked finishes first, refused.
        if (running && !IsGameRunning)
            Dispatcher.UIThread.Post(() => Seen(GameRunning));
        else
            IsGameRunning = running;

        return running;
    }

    /// <summary>
    /// Restoring an open backup replaces the files by design - the restore question has already
    /// said what goes - so what the game saved since is taken as what is being replaced.
    /// </summary>
    public void AcceptDisk()
    {
        _session.AcceptDisk();
        _stash.AcceptDisk();
        _compendium?.AcceptDisk();
    }

    private void KeepGames()
    {
        _saves.Revert();
        if (_session.CharacterPath is { } path)
            _session.Load(path);

        _warned = null;
        _session.Status = L("Opened what the game saved. Changes dropped.");
    }

    private void KeepMine(Func<bool> write)
    {
        AcceptDisk();
        _warned = null;

        if (write())
            _session.Status = L("Saved. The game's version is in backups.");
    }

    /// <summary>What the game has saved since it was read here, worded, or null for nothing.</summary>
    private string? Changed()
    {
        bool character = _session.ChangedOnDisk, stash = _stash.ChangedOnDisk;
        string? files = (character, stash) switch
        {
            (true, true) => L($"{_session.CharacterName} and the shared stash"),
            (true, false) => _session.CharacterName,
            (false, true) => L("Shared stash"),
            _ => null,
        };

        // The compendium beside them: its own name where it is the one, added where another is too.
        if (_compendium?.ChangedOnDisk != true)
            return files;
        return files is null ? L("Compendium") : L($"{files} and the compendium");
    }
}
