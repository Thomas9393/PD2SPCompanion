using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// PD2's Compendium of the open character's mode - the stash's Uniques and Sets tabs, one copy kept of an item - open
/// alongside it, as the shared stash is. The Grail page stores an item in it
/// and takes one out.
/// <para>
/// One of the files saved together (<see cref="SaveSet"/>): an item stored leaves the character or the shared stash in
/// the same save that writes it here, the file gaining it written first, so a save cut short leaves it in both and not in
/// neither. Only one save moving items both ways has no such order, and the copies beside the files are its way back. The
/// session's history keeps this file's bytes beside theirs, so Undo takes a store back whole. Written with a copy of what was
/// there in the backups, never while the game runs (SaveSet's gate), and refused where the game has written it since
/// it was read - its own deposits made while the editor was open.
/// </para>
/// </summary>
public sealed partial class CompendiumSession : ObservableObject, ISaveParticipant
{
    private readonly CharacterSession _character;
    private readonly SaveSet _saves;

    private Compendium? _compendium;

    /// <summary>The bytes it was read from or last written; null where the file is not there yet.</summary>
    private byte[]? _savedBytes;

    /// <summary>The live file's stamp when it was read or written: the game writing it after is seen.</summary>
    private (DateTime Written, long Length)? _asRead;

    private string? _loadedPath;

    public CompendiumSession(CharacterSession character, SaveSet saves)
    {
        _character = character;
        _saves = saves;
        saves.Takes(this);

        _character.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName != nameof(CharacterSession.IsLoaded))
                return;

            if (_character.IsLoaded)
                Load();
            else
                Unload();
        };

        // The game writes it beside the characters as it plays; read again only where it is not what this last read
        // or wrote, and never over a store not yet saved (Load).
        _character.SaveFolderChanged += () =>
        {
            if (_character.IsLoaded && (ChangedOnDisk || !string.Equals(_loadedPath, Path, StringComparison.OrdinalIgnoreCase)))
                Load();
        };
    }

    /// <summary>Raised when what it holds has changed: read, stored into, taken from, put back by an undo.</summary>
    public event Action? Changed;

    /// <summary>The file of the open character's mode, or "" without a save folder.</summary>
    public string Path =>
        _character.SaveFolder is { Length: > 0 } folder
            ? System.IO.Path.Combine(folder, Compendium.FileFor(_character.IsHardcore))
            : "";

    /// <summary>Whether a compendium is open - read, or begun empty where the mode has no file yet.</summary>
    public bool IsLoaded => _compendium is not null;

    /// <summary>Every item stored, in the order the file keeps them.</summary>
    public IReadOnlyList<Item> Items => _compendium?.Items ?? [];

    [ObservableProperty] private bool _hasUnsavedChanges;

    partial void OnHasUnsavedChangesChanged(bool value) => _saves.SomethingChanged();

    /// <summary>What it last could not do: the file would not read. Said where it is asked, not on the strip.</summary>
    public string Trouble { get; private set; } = "";

    /// <summary>
    /// Whether the live file is not the one read - the game has saved it since. See <see cref="GameSync"/>, which asks
    /// which to keep where both changed.
    /// </summary>
    public bool ChangedOnDisk
    {
        get
        {
            if (Path is not { Length: > 0 } where)
                return false;
            if (!File.Exists(where))
                return _asRead is not null;
            if (_asRead is not { } read)
                return true;

            var now = new FileInfo(where);
            return now.LastWriteTimeUtc != read.Written || now.Length != read.Length;
        }
    }

    /// <summary>Takes the live file as it stands now as the one the next save replaces.</summary>
    public void AcceptDisk() => Stamp();

    private void Stamp()
    {
        _asRead = Path is { Length: > 0 } where && File.Exists(where) ? (new FileInfo(where).LastWriteTimeUtc, new FileInfo(where).Length) : null;
    }

    /// <summary>
    /// Reads the mode's file, or begins an empty one where there is none. A store not yet saved is kept - it belongs to
    /// the character's next save or its undo, as the shared stash's staged moves do.
    /// </summary>
    public void Load()
    {
        if (HasUnsavedChanges)
            return;

        _loadedPath = Path;
        _compendium = null;
        _savedBytes = null;
        Trouble = "";

        if (Path is not { Length: > 0 } path || _character.EnsureGame() is null)
        {
            Changed?.Invoke();
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                byte[] bytes = File.ReadAllBytes(path);
                _compendium = Compendium.Read(bytes, Compendium.FileFor(_character.IsHardcore));
                _savedBytes = bytes;
            }
            else
            {
                _compendium = Compendium.Empty();
            }

            Stamp();
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            _compendium = null;
            Trouble = problem.Message;

            // As read, for all that it would not read: not "changed in game" at every autosave.
            Stamp();
        }
        finally
        {
            // Reading it set the parser for it: back to the character's layout.
            _character.UseMyLayout();
        }

        OnPropertyChanged(nameof(IsLoaded));
        Changed?.Invoke();
    }

    private void Unload()
    {
        _compendium = null;
        _savedBytes = null;
        _asRead = null;
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(IsLoaded));
        Changed?.Invoke();
    }

    /// <summary>
    /// Puts an item in, last, as the game appends one (<see cref="Compendium.Deposit"/>). The caller has taken it from
    /// where it lay. Written with the next save.
    /// </summary>
    public void Store(Item item)
    {
        if (_compendium is null)
            throw new InvalidOperationException(L("No compendium open."));

        _compendium.Deposit(item);
        HasUnsavedChanges = true;
        Changed?.Invoke();
    }

    /// <summary>Takes an item out for the caller to put down; false where it was not here.</summary>
    public bool TakeOut(Item item)
    {
        if (_compendium?.TakeOut(item) != true)
            return false;

        HasUnsavedChanges = true;
        Changed?.Invoke();
        return true;
    }

    // ---- the session's history -----------------------------------------------------------------------------------

    /// <summary>The file as it stands on disk, for the session's history: null where it is not there yet.</summary>
    public byte[]? SavedBytes => _savedBytes;

    /// <summary>Whether it stands as these bytes have it - its file these, nothing waiting.</summary>
    public bool StandsAs(byte[]? bytes) =>
        !HasUnsavedChanges && (bytes is null ? _savedBytes is null : _savedBytes is not null && bytes.AsSpan().SequenceEqual(_savedBytes));

    /// <summary>
    /// Puts an earlier state back, from its bytes - none for "no file yet", an empty one - an undo or redo. Unsaved
    /// until the next save writes it. Null when done, else why not.
    /// </summary>
    public string? RestoreBytes(byte[]? bytes)
    {
        try
        {
            _compendium = bytes is null ? Compendium.Empty() : Compendium.Read(bytes, Compendium.FileFor(_character.IsHardcore));
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return L($"Compendium not put back: {problem.Message}");
        }
        finally
        {
            _character.UseMyLayout();
        }

        HasUnsavedChanges = !StandsAsSaved(bytes);
        _saves.SomethingChanged();
        Changed?.Invoke();
        return null;
    }

    private bool StandsAsSaved(byte[]? bytes) =>
        bytes is null ? _savedBytes is null : _savedBytes is not null && bytes.AsSpan().SequenceEqual(_savedBytes);

    // ---- the save ---------------------------------------------------------------------------------------------------

    string ISaveParticipant.What => L("The compendium");

    bool ISaveParticipant.HasChanges => _compendium is not null && HasUnsavedChanges;

    void ISaveParticipant.UseMyLayout() => D2SLib.Globals.ResetForFile(pd2Character: false, statIdBits: 10);

    private byte[]? _prepared;

    string? ISaveParticipant.Prepare()
    {
        if (_compendium is null)
            return L("No compendium open.");

        // Refused rather than merged, as the shared stash is: the game re-reads the file and never reconciles two.
        if (ChangedOnDisk)
            return L("Compendium changed in game. Require reopen.");

        try
        {
            byte[] bytes = _compendium.Write();

            // Never hand over something that will not read back.
            _ = Compendium.Read(bytes, Compendium.FileFor(_character.IsHardcore));
            _prepared = bytes;
            return null;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            _prepared = null;
            return L($"Compendium not saved: {problem.Message}");
        }
    }

    SaveHoldings? ISaveParticipant.Holdings() =>
        _prepared is null ? null : new SaveHoldings(HeldIn(_savedBytes), HeldIn(_prepared));

    private IReadOnlyDictionary<string, long> HeldIn(byte[]? bytes) =>
        SaveHoldings.Counted(bytes, read => SaveHoldings.Count(Compendium.Read(read, Compendium.FileFor(_character.IsHardcore)).Items));

    string? ISaveParticipant.Write(string moment, SaveReport report)
    {
        if (_prepared is null)
            return L("Compendium not prepared.");

        string path = Path;
        try
        {
            // Beside the character's own copy, under the same moment: one state of everything (SaveWriter.Backup).
            string backup = File.Exists(path)
                ? SaveWriter.Backup(path, moment, SaveWriter.BackupFolderFor(_character.SavePath ?? path))
                : "";
            SaveWriter.WriteSafely(path, _prepared);
            SaveWriter.Prune(path, SaveWriter.BackupsToKeep, SaveWriter.BackupFolderFor(_character.SavePath ?? path));

            _savedBytes = _prepared;
            _prepared = null;
            Stamp();
            HasUnsavedChanges = false;

            Log.Info("compendium", $"Saved {Items.Count} items. Backup: {(backup.Length > 0 ? System.IO.Path.GetFileName(backup) : "none - a new file")}.");
            return null;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return L($"Compendium not saved: {problem.Message}");
        }
    }

    /// <summary>Throws away what is staged and reads the file again.</summary>
    void ISaveParticipant.Revert()
    {
        if (!HasUnsavedChanges)
            return;

        _prepared = null;
        HasUnsavedChanges = false;
        Load();
    }
}
