using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// The open vault, for every page that uses it: the vault page reads and edits it, the inventory
/// page stores into it. A vault is one file (<see cref="VaultLibrary"/>): the items and their folders,
/// and the player's records - the trade ledger and the vault's gold.
/// <see cref="Store"/> is that file, open. <b>A mode has several, one open at a time</b>:
/// this is also where they are listed, switched between, made, copied, renamed, deleted,
/// written out and brought in - for the settings page, the only place that does it.
/// <para>
/// Moved here from the vault page when the inventory needed it too - a second page opening its
/// own connection to the same file would be two writers nobody coordinates.
/// </para>
/// <para>
/// <b>The mode follows the character</b>: loading a hardcore one opens the hardcore vault. With none
/// loaded, the last one stays.
/// </para>
/// </summary>
public sealed class VaultService : IDisposable
{
    private readonly CharacterSession _session;
    private readonly EditorSettings _settings;

    public VaultService(CharacterSession session, EditorSettings settings)
    {
        _session = session;
        _settings = settings;

        // A character's mode is its vault's. Staged moves have gone by now: loading drops every
        // edit (VaultMoves), so nothing is left pointing into the vault being left.
        _session.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(CharacterSession.IsLoaded) && _session.IsLoaded)
                UseMode(_session.IsHardcore);
        };
    }

    /// <summary>
    /// Where a vault lives and which season it belongs to are the store's own business - see
    /// <see cref="VaultLibrary"/> and <see cref="PD2SPCompanion.Core.Collection.Vault.InstalledSeason"/>. The
    /// CLI opens the same files, so neither may be worked out twice.
    /// </summary>
    private PD2SPCompanion.Core.Collection.Vault? _vault;

    /// <summary>The stat-id width the vault's items are written at.</summary>
    public const int StatIdBits = 10;

    /// <summary>The season the open vault belongs to, once it is open.</summary>
    public string? Season { get; private set; }

    /// <summary>Whether the open vault is hardcore's.</summary>
    public bool IsHardcore => _settings.VaultHardcore;

    /// <summary>The open vault's name, once one is open.</summary>
    public string VaultName { get; private set; } = "";

    /// <summary>
    /// The vault the mode opens on: the one it was left on, where that is still there; else its first by
    /// name (<see cref="VaultLibrary.MainName"/>), which is made where the mode has none - or, where that
    /// one was renamed, the first of those it has.
    /// </summary>
    private string NameToOpen(string season)
    {
        string kept = IsHardcore ? _settings.VaultNameHardcore : _settings.VaultNameSoftcore;
        var vaults = VaultLibrary.Vaults(season, IsHardcore);
        if (vaults.FirstOrDefault(each => string.Equals(each.Name, kept, StringComparison.OrdinalIgnoreCase)) is { } last)
            return last.Name;

        string main = VaultLibrary.MainName(season);
        return vaults.Count == 0 || vaults.Any(each => string.Equals(each.Name, main, StringComparison.OrdinalIgnoreCase))
            ? main
            : vaults[0].Name;
    }

    private void Remember(string name)
    {
        if (IsHardcore)
            _settings.VaultNameHardcore = name;
        else
            _settings.VaultNameSoftcore = name;
    }

    /// <summary>Raised when another mode's vault is opened, or this one replaced - pages read again.</summary>
    public event Action? Switched;

    /// <summary>
    /// Whether anything waits for a save that points into the open vault - a stored item or a
    /// withdrawn row. Told by VaultMoves, which is built after this and knows.
    /// </summary>
    public Func<bool>? HasPendingMoves { get; set; }

    /// <summary>
    /// What the vault is being searched for: edited on the vault's right panel, read by its list.
    /// Kept here, beside the vault itself, so both pages hold the same one.
    /// </summary>
    public VaultFilter Filter { get; } = new();

    /// <summary>
    /// The one card the vault page has selected, for the Edit page to edit - or null, with none
    /// selected or several picked. Only the vault page sets it.
    /// </summary>
    public PD2SPCompanion.Shell.ViewModels.ItemTileViewModel? Chosen { get; private set; }

    public event Action? ChosenChanged;

    /// <summary>
    /// How many the left vault page has selected together, for the Edit page beside it to say in place of an
    /// item - it edits one at a time. Nought with one selected or none.
    /// </summary>
    public int Picked { get; private set; }

    public event Action? PickedChanged;

    public void SetPicked(int count)
    {
        if (Picked == count)
            return;

        Picked = count;
        PickedChanged?.Invoke();
    }

    public void Choose(PD2SPCompanion.Shell.ViewModels.ItemTileViewModel? card)
    {
        if (ReferenceEquals(Chosen, card))
            return;

        Chosen = card;
        ChosenChanged?.Invoke();
    }

    /// <summary>
    /// A vault's file has just been opened - this mode's for the first time, or another after a switch. Raised
    /// from inside the read that opened it: what answers it does its work after, not within.
    /// </summary>
    public event Action? VaultOpened;

    /// <summary>Why the vault could not be opened, when it could not.</summary>
    public string Trouble { get; private set; } = "";

    /// <summary>
    /// The open vault - the items and the player's records - opened on first use. Kept open: a vault is
    /// read on every sort and every refresh, and SQLite in WAL mode is happier held than reopened.
    /// </summary>
    public PD2SPCompanion.Core.Collection.Vault? Store => Open() ? _vault : null;

    /// <summary>The collections could not be folded in this session: said once, not tried at every read.</summary>
    private bool _mergeFailed;

    /// <summary>Opens the mode's vault if it is not open; false, with <see cref="Trouble"/> said, if it cannot be.</summary>
    private bool Open()
    {
        if (_vault is not null)
            return true;

        if (_mergeFailed)
            return false;

        try
        {
            // The season is read off the installed tables, and those load on first use of the
            // game data - so ask for it rather than hoping something else has. This is what lets
            // the vault be browsed without opening a character first.
            bool tables = _session.EnsureGame() is not null;

            // No season, no vault: opening one under a made-up name is how an empty second file once
            // appeared beside the real S13 one. Said as it is - no game folder, or tables that do not
            // say which season they are, where the vault waits for the editor to be updated.
            if (!PD2SPCompanion.Core.Collection.Vault.TryInstalledSeason(out string season, out _))
            {
                Trouble = tables
                    ? L("Season unknown. Vault closed; require update.")
                    : L("No game folder. Vault closed.");
                return false;
            }

            // The one file of before collections goes to the mode its characters played - once; and
            // collections from the first layout, one folder for both modes, go each into its own.
            // Both are then folded in, below.
            if (VaultLibrary.NeedsSplit(season))
            {
                bool hardcore = LegacyIsHardcore(season);
                VaultLibrary.Split(season, hardcore, StatIdBits);
                _settings.VaultHardcore = hardcore;
            }

            VaultLibrary.Rehome(season);

            // And each mode's one vault of before, to its place as the mode's first by name.
            VaultLibrary.Rehouse(season);

            Season = season;

            // The mode's collections, folded into its first vault - once. On a copy
            // that takes the vault's place only when all were read; the files go to Backups.
            if (VaultLibrary.NeedsMerge(season, IsHardcore))
            {
                try
                {
                    var merged = VaultLibrary.MergeCollections(season, IsHardcore, StatIdBits);
                    Log.Info("vault", $"Collections folded into the {(IsHardcore ? "hardcore" : "softcore")} vault: {merged.Collections} collections, "
                                      + $"{merged.Items} items, {merged.Failed} not read; before: {merged.Backup}; the files: {merged.Retired}");
                    _session.Status = merged.Failed == 0
                        ? L($"Collections merged into vault: {merged.Items} items.")
                        : L($"Collections merged into vault: {merged.Items} items, {merged.Failed} not read. Old files in vault backups.");
                }
                catch (Exception problem)
                {
                    Log.Caught(problem);
                    Trouble = L($"Collections not merged: {problem.Message}");
                    _mergeFailed = true;
                    return false;
                }
            }

            string name = NameToOpen(season);
            bool opened = _vault is null;
            _vault ??= VaultLibrary.OpenVault(season, IsHardcore, name, StatIdBits);
            VaultName = name;
            Remember(name);
            Trouble = "";

            // A copy where the file is newer than its newest one: the vault as this session found it.
            if (opened)
                _vault.CopyAsFound();

            // A save cut short since the vault last opened: put right, and said.
            RecoverMoves();

            // The opening's own upkeep is done: what changes from here is the session's, and has the
            // vault copied as it is closed (Vault.CopyAsLeft).
            _vault.SessionBegins();

            // Its own changes logged for undo and redo, the vault's part of the session's history.
            _vault.StartUndo(() => Written?.Invoke());
            VaultOpened?.Invoke();
            return true;
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            Trouble = L($"Vault not opened: {problem.Message}");
            Close();
            return false;
        }
    }

    /// <summary>
    /// Which mode the one-file vault belonged to: hardcore only if the characters its records name,
    /// among those still in the save folder, are all hardcore. Softcore otherwise - including when
    /// none is left to ask.
    /// </summary>
    private bool LegacyIsHardcore(string season)
    {
        try
        {
            using var legacy = PD2SPCompanion.Core.Collection.Vault.Open(PD2SPCompanion.Core.Collection.Vault.PathFor(season), season, StatIdBits);
            // Its trades: the one record that names characters since format 20, which opening it has brought it to
            // (its finds named them too, before).
            var names = legacy.Trades().Select(trade => trade.Character)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var modes = names
                .Select(name => Path.Combine(_session.SaveFolder, name + ".d2s"))
                .Where(File.Exists)
                .Select(path =>
                {
                    // The status byte: 0x24 in the header, hardcore its third bit.
                    using var file = File.OpenRead(path);
                    file.Seek(0x24, SeekOrigin.Begin);
                    return (file.ReadByte() & 0x04) != 0;
                })
                .ToList();

            return modes.Count > 0 && modes.All(hardcore => hardcore);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens a mode's vault - a character of that mode was loaded. Nothing happens when it is
    /// already the open one.
    /// </summary>
    public void UseMode(bool hardcore)
    {
        if (hardcore == IsHardcore && _vault is not null)
            return;

        Close();
        _settings.VaultHardcore = hardcore;
        Reopened();
    }

    // ---- the mode's vaults (the settings page) -------------------------------------------------

    /// <summary>The list of vaults changed - one made, renamed, deleted or brought in - with the open one as it was.</summary>
    public event Action? Listed;

    /// <summary>Something the list says of a vault changed - the play mode it keeps - and nothing it holds.</summary>
    public void SayListed() => Listed?.Invoke();

    /// <summary>The open mode's vaults, by name; none where the vault cannot be opened.</summary>
    public IReadOnlyList<VaultInfo> Vaults() =>
        Open() && Season is { } season ? VaultLibrary.Vaults(season, IsHardcore) : [];

    /// <summary>Why a vault's name will not do, or null.</summary>
    public string? RefuseName(string name, string? except = null) =>
        Open() && Season is { } season ? VaultLibrary.RefuseName(season, IsHardcore, name, except) : Trouble;

    private string Opened() => Open() && Season is { } season ? season : throw new InvalidOperationException(Trouble);

    private bool IsOpen(string name) => string.Equals(name, VaultName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Closes the open vault and opens another of the mode. The caller has seen to moves waiting for a save.</summary>
    public void SwitchTo(string name)
    {
        Opened();
        if (IsOpen(name))
            return;

        Close();
        Remember(name);
        Reopened();
    }

    /// <summary>Makes a vault of the mode: empty, or a copy of another - everything in it. Not opened.</summary>
    public void Create(string name, string? copyOf)
    {
        VaultLibrary.CreateVault(Opened(), IsHardcore, name, copyOf, StatIdBits);
        Listed?.Invoke();
    }

    /// <summary>Renames a vault; the open one is closed for it and opened again under its new name.</summary>
    public void Rename(string name, string newName)
    {
        string season = Opened();
        bool open = IsOpen(name);
        if (open)
            Close();

        try
        {
            VaultLibrary.RenameVault(season, IsHardcore, name, newName);
            if (open)
                Remember(newName.Trim());
        }
        finally
        {
            if (open)
                Reopened();
        }

        Listed?.Invoke();
    }

    /// <summary>Deletes a vault that is not the open one, a copy of it kept in Backups. Returns where the copy went.</summary>
    public string Delete(string name)
    {
        string season = Opened();
        if (IsOpen(name))
            throw new InvalidOperationException(L("Vault open. Open another first."));

        string backup = VaultLibrary.DeleteVault(season, IsHardcore, name, StatIdBits);
        Listed?.Invoke();
        return backup;
    }

    /// <summary>Writes a copy of a vault - items, folders and records - to <paramref name="target"/>.</summary>
    public void ExportVault(string name, string target)
    {
        string season = Opened();
        if (IsOpen(name) && Store is { } open)
            open.CopyTo(target);
        else
            VaultLibrary.CopyForReading(VaultLibrary.VaultPath(season, IsHardcore, name), target);
    }

    /// <summary>A file chosen to import, staged to be read: the caller disposes it.</summary>
    public VaultLibrary.Incoming Stage(string path, string? name = null) => VaultLibrary.Stage(path, Opened(), StatIdBits, name);

    /// <summary>Adds a staged file to the mode's vaults and opens it: the one open before stays in the list. Returns its name.</summary>
    public string AddAsVault(VaultLibrary.Incoming incoming)
    {
        string name = VaultLibrary.AddAsVault(incoming, Opened(), IsHardcore, StatIdBits);
        SwitchTo(name);
        Listed?.Invoke();
        return name;
    }

    /// <summary>
    /// Brings a staged file's items into the open vault as a new top folder of the file's name, the folders
    /// ticked (<paramref name="takes"/>) or all. Not a step to undo: the folder is there to be deleted.
    /// Returns the folder's name and how many items came in and did not.
    /// </summary>
    public (string Folder, int Added, int Failed) AddAsFolder(VaultLibrary.Incoming incoming, Func<long?, bool>? takes)
    {
        if (Store is not { } store)
            throw new InvalidOperationException(Trouble);

        var result = WriteQuietly(() => VaultLibrary.AddAsFolder(store, incoming, takes));
        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// A character renamed, in the records that name it in every other vault of the season. The open
    /// vault's records are renamed apart, by the caller; no item names who stored it. Returns the rows changed.
    /// </summary>
    public int RenameInOtherVaults(string oldName, string newName)
    {
        if (!Open() || Season is not { } season)
            return 0;

        return VaultLibrary.RenameCharacter(season, oldName, newName, StatIdBits, open: _vault?.FilePath);
    }

    /// <summary>The folder exports go to by default - where the file pickers open.</summary>
    public string? ExportsFolder => Open() && Season is { } season ? VaultLibrary.ExportsFolder(season) : null;

    /// <summary>
    /// Checks the moves a save left part-way (<see cref="MoveRecovery"/>): what is in neither
    /// place goes back to the vault, a copy in both is said and left. Every line goes to the
    /// strip - the one duplicate always said, whatever the play rules -
    /// unless the caller says it itself (<paramref name="say"/> false), and returned.
    /// </summary>
    public IReadOnlyList<string> RecoverMoves(bool say = true)
    {
        var found = new List<string>();
        if (_vault is not { } vault)
            return found;

        try
        {
            found.AddRange(MoveRecovery.Recover(vault));
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            found.Add(L($"Save cut short, not checked: {problem.Message}"));
        }
        finally
        {
            // Reading a character to count its items installs that file's layout.
            if (_session.IsLoaded)
                _session.UseMyLayout();
        }

        if (say)
        {
            foreach (string line in found)
                _session.Status = line;
        }

        return found;
    }

    /// <summary>Closes the vault's file, so it can be replaced on disk.</summary>
    public void Close()
    {
        bool was = _vault is not null;

        // A copy where the session changed it: the vault as this session leaves it (Vault.CopyAsLeft).
        _vault?.CopyAsLeft();
        _vault?.Dispose();
        _vault = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (was)
            UndoLost?.Invoke();
    }

    /// <summary>
    /// The vault changed something itself - a folder, a card - outside a save: the session's
    /// history makes it a step.
    /// </summary>
    public event Action? Written;

    /// <summary>The log a history step points into is gone: another mode's vault, or closed.</summary>
    public event Action? UndoLost;

    /// <summary>A quiet write is done: what it logged belongs to no step (see <see cref="WriteQuietly"/>).</summary>
    public event Action? WrittenQuietly;

    /// <summary>
    /// Writes that are bookkeeping, not an action to undo - a vault cleaned from Settings: written, but
    /// no history step made of them, and none of the next one either.
    /// </summary>
    public T WriteQuietly<T>(Func<T> write)
    {
        var log = StoreUndo;
        bool was = log?.Quiet ?? false;
        if (log is not null)
            log.Quiet = true;
        try
        {
            return write();
        }
        finally
        {
            if (log is not null)
                log.Quiet = was;
            WrittenQuietly?.Invoke();
        }
    }

    /// <summary>The open vault's undo log, without opening anything.</summary>
    public PD2SPCompanion.Core.Collection.VaultUndo? StoreUndo => _vault?.Undo;

    /// <summary>
    /// Opens the mode's vault again after a switch or a migration - the one named, where one is - and tells
    /// the pages.
    /// </summary>
    public void Reopened(string? name = null)
    {
        if (name is { Length: > 0 })
            Remember(name);

        CurrentFolder = null;
        UndoLost?.Invoke();
        Open();
        Switched?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>
    /// The folder the vault page is showing. Something stored from elsewhere lands there, the
    /// way a file saved into an open Explorer window does. Null is the top level.
    /// </summary>
    public long? CurrentFolder { get; set; }

    /// <summary>
    /// Raised when something outside the vault page has changed what it holds - the inventory
    /// storing an item, the settings page emptying it.
    /// </summary>
    public event Action? Changed;

    public void SayChanged() => Changed?.Invoke();

    /// <summary>
    /// The vault's gold changed and nothing else (VaultMoves.EditGold): what lists the vaults with their gold follows
    /// it. The pages show the gold through the moves' own change; a press of the gold stepper raised
    /// <see cref="Changed"/> and both vault pages read every card again, the grail and the vaults' list with them.
    /// </summary>
    public event Action? GoldChanged;

    public void SayGoldChanged() => GoldChanged?.Invoke();

    /// <summary>
    /// One item's row was written again in place - the Edit page's change, kept at once: a vault page that
    /// holds its card reads that row again, and nothing else of the page is made anew (VaultEditHost).
    /// </summary>
    public event Action<long>? Rewritten;

    public void SayRewritten(long key) => Rewritten?.Invoke(key);

    public void Dispose() => Close();
}
