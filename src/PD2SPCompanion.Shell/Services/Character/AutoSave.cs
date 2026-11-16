using System;
using Avalonia.Threading;
using PD2SPCompanion.Shell.Services.Vault;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// Saves the open character by itself, a moment after the last change. Everything saves this way - an item being edited too:
/// every change of the Edit page is the item's as it is made (IItemEditHost.KeepsAtOnce),
/// and a run of them to one item is one state (SessionHistory). A stepper held down or a name typed
/// saves once, when it stops.
/// <para>
/// Each save is a state in <see cref="SessionHistory"/>, named by what the message strip last
/// said. Not while a backup is on screen - saving it is a restore, asked about apart - nor while
/// the game runs, which closes the character anyway (<see cref="GameSync"/>).
/// </para>
/// </summary>
public sealed partial class AutoSave : ObservableObject
{
    /// <summary>How long after the last change the save is made.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);

    private readonly CharacterSession _session;
    private readonly SaveSet _saves;
    private readonly SessionHistory _history;
    private readonly GameSync? _sync;
    private readonly DispatcherTimer? _timer;

    public AutoSave(CharacterSession session, SaveSet saves, SessionHistory history, GameSync? sync, VaultMoves? moves = null)
    {
        if (moves is not null)
        {
            moves.SaveNow = Flush;
            moves.ItemsMoved += SaveBeforeDrawing;
        }

        _session = session;
        _saves = saves;
        _history = history;
        _sync = sync;

        try
        {
            _timer = new DispatcherTimer(Pause, DispatcherPriority.Background, (_, _) =>
            {
                _timer!.Stop();
                Flush();
            });
        }
        catch (Exception)
        {
            // No dispatcher (a test harness): Flush is still called by hand.
        }

        // Writes allowed again - the patch guard's hold lifted by Accept - with edits made while they
        // were not: those save now. The timer only starts when changes first appear, and these
        // appeared while held, so nothing else would ever save them (patch guard test).
        if (sync is not null)
        {
            sync.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName == nameof(GameSync.IsWriteBlocked) && !sync.IsWriteBlocked
                    && saves.HasChanges && !history.IsMoving)
                {
                    _timer?.Stop();
                    _timer?.Start();
                }
            };
        }

        saves.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(SaveSet.HasChanges) && saves.HasChanges && !history.IsMoving)
            {
                _timer?.Stop();
                _timer?.Start();
            }
        };
    }

    private bool _savePosted;

    /// <summary>
    /// An item into or out of the vault saves at once, so a withdrawal or a store
    /// with the vault is seamless. Staged moves show an arrow until the save, and
    /// the pause left the card and the item on screen for most of a second - measured 0.8-0.9 s
    /// from the drop to the files, 500 ms of it the pause. Posted at Normal priority, ahead of
    /// the next render, so the drop and the save reach the screen as one change; a batch (Store
    /// all, several cards) posts once.
    /// </summary>
    private void SaveBeforeDrawing()
    {
        if (_savePosted)
            return;

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                _savePosted = false;
                Flush();
            }, DispatcherPriority.Normal);
            _savePosted = true;
        }
        catch (Exception)
        {
            // No dispatcher (a test harness): the pause, or Flush by hand, saves it.
        }
    }

    /// <summary>
    /// Saves now whatever is waiting - before the character closes, the page changes or the app
    /// ends. True when nothing is left unsaved; false, with the strip saying why, when it is.
    /// </summary>
    public bool Flush()
    {
        _timer?.Stop();

        if (!_session.IsLoaded || _session.IsViewingBackup || !_saves.HasChanges || _history.IsMoving)
            return !_saves.HasChanges || _session.IsViewingBackup;

        // Said on the strip, once while it stands. It was kept in a State of the autosave's own, "what the top
        // bar says", which nothing showed: an edit waiting as the game started went unsaid.
        if (_sync?.GameRunning == true)
        {
            string held = L("Not saved: game running");
            if (_session.Status != held)
                _session.Status = held;
            return false;
        }

        // A PD2 the saves have not been checked against (PatchGuardService): the strip has said why.
        if (_sync?.IsWriteBlocked == true)
            return false;

        // Named by what was done, as the strip said it.
        string label = _session.Messages.Text is { Length: > 0 } said ? said : L("Edited");

        // Through GameSync, so a file something else changed since it was read asks which to
        // keep rather than being written over.
        bool saved = false;
        if (_sync is not null)
            _sync.Save(() => saved = Write(label), looked: true);
        else
            saved = Write(label);

        return saved;
    }

    private bool Write(string label)
    {
        // The vault's own writes in this save are the save's, not a vault step of their own.
        _history.Saving = true;
        string? trouble;
        SaveReport report;
        try
        {
            trouble = _saves.Commit(out report);
        }
        finally
        {
            _history.Saving = false;
        }

        if (trouble is not null)
        {
            _session.Status = trouble;
            return false;
        }

        // The copy this save took holds the character as it was just before it - said so, in the
        // words the strip used.
        if (report.Backup is { } backup)
            PD2SPCompanion.Core.Saves.SaveWriter.Caption(backup, L($"Before: {label}"));

        _history.Record(label);
        return true;
    }
}
