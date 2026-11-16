using System;
using System.Collections.Generic;
using System.Linq;

using PD2SPCompanion.Core;
using PD2SPCompanion.Core.Diagnostics;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Services.Vault;

/// <summary>
/// A new season's vault filled from older ones: the
/// notice the first time a season with older vaults to migrate is opened, and the migration itself for
/// the Settings page's panel. What is migrated and how each item is judged is <see cref="SeasonMigration"/>'s;
/// this is when to ask, which seasons, and the vault closed around the run.
/// <para>
/// More than one older season can wait - a player who skipped the migration a season, or played two
/// and never migrated. Each goes straight into this season, oldest first, its items merged into the
/// same collections and folders.
/// </para>
/// </summary>
public sealed class SeasonMigrationService
{
    private readonly CharacterSession _session;
    private readonly SharedStashSession _stash;
    private readonly VaultService _vaults;
    private readonly EditorSettings _settings;
    private readonly ConfirmService _confirm;
    private readonly PatchGuardService _guard;

    public SeasonMigrationService(CharacterSession session, SharedStashSession stash, VaultService vaults, EditorSettings settings, ConfirmService confirm,
                                  PatchGuardService guard)
    {
        _session = session;
        _stash = stash;
        _vaults = vaults;
        _settings = settings;
        _confirm = confirm;
        _guard = guard;
        guard.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(PatchGuardService.IsHeld))
                HoldChanged?.Invoke();
        };
    }

    /// <summary>The patch guard took or lifted its hold: the panel's MIGRATE follows.</summary>
    public event Action? HoldChanged;

    /// <summary>
    /// Why a character's migration is refused now, or null: it writes saves and the shared stash, which the patch guard
    /// is holding. Accept or REPAIR lift a hold over files; stale tables want a restart.
    /// </summary>
    public string? CharactersHeld => _guard.IsHeld ? _guard.HeldLine : null;

    /// <summary>One vault of an older season, read or written: what came across, or why it could not.</summary>
    public sealed record Result(string Season, bool Hardcore, string Vault, SeasonMigration.Report? Report, string? Refused);

    /// <summary>The migration panel is wanted: the notice's MIGRATE. The main window shows Settings, the page opens it.</summary>
    public event Action? Wanted;

    /// <summary>A migration was written: the Settings row and the vault pages follow.</summary>
    public event Action? Migrated;

    /// <summary>The installed season's name, or null while the tables are not read or do not say.</summary>
    public string? To =>
        Safely(_session.EnsureGame) is not null && _session.Season is { } season && PD2SPCompanion.Core.Collection.Vault.TryInstalledSeason(out _, out _)
            ? SeasonSnapshot.NameFor(season)
            : null;

    /// <summary>Every older season with a vault, migrated or not, oldest first.</summary>
    public IReadOnlyList<string> Older() => To is { } to ? Safely(() => SeasonMigration.Older(to)) ?? [] : [];

    /// <summary>The older seasons still to migrate, oldest first.</summary>
    public IReadOnlyList<string> Pending() => To is { } to ? Safely(() => SeasonMigration.Pending(to)) ?? [] : [];

    /// <summary>"S12", "S12 and S13", "S11, S12 and S13".</summary>
    public static string Named(IReadOnlyList<string> seasons) => Joined(seasons);

    /// <summary>
    /// The notice, once a season: asked when the window is up, the first time a season is opened with older
    /// vaults to migrate. MIGRATE and START NEW both answer it; the Settings row stays for later.
    /// </summary>
    public void CheckAtStart()
    {
        if (!_session.IsConfigured || To is not { } to || Pending() is not { Count: > 0 } pending
            || string.Equals(_settings.MigrationNoticeSeason, to, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string named = Named(pending);
        _confirm.Ask(L("New season"),
                     Plural(pending.Count, $"{to} found in game folder. Migrate {named} vault?",
                            $"{to} found in game folder. Migrate {named} vaults?"),
                     L("MIGRATE"),
                     () =>
                     {
                         _settings.MigrationNoticeSeason = to;
                         Wanted?.Invoke();
                     },
                     // Not migrating is starting anew: the season's own vault is there,
                     // empty, and what was left can be migrated from Settings whenever.
                     cancelLabel: L("START NEW"),
                     onCancel: () => _settings.MigrationNoticeSeason = to);
    }

    /// <summary>
    /// A dry run of each vault still to migrate: what would come across, or why it cannot. Reads
    /// the old seasons' tables, so the caller's tables are swapped and put back meanwhile.
    /// </summary>
    public IReadOnlyList<Result> Preview() => Each(dryRun: true);

    /// <summary>
    /// Migrates the vaults chosen of those still to migrate, oldest season first - each into a vault of its
    /// own name - the open vault closed meanwhile so nothing holds its files open with an undo log the
    /// migration would not be in. One left out stays waiting. Refused with pending vault moves: their rows
    /// are what a save writes, and a migration does not wait for it. The first migrated of the open mode
    /// is the vault open after: a player who migrated wants to see what came across.
    /// </summary>
    public IReadOnlyList<Result> Migrate(IReadOnlyCollection<(string Season, bool Hardcore, string Vault)> chosen)
    {
        // A hold means the tables cannot be trusted, and a vault is written by them.
        if (CharactersHeld is { } held)
            return [new Result("", _vaults.IsHardcore, "", null, held)];

        if (_vaults.HasPendingMoves?.Invoke() == true)
            return [new Result("", _vaults.IsHardcore, "", null, L("Require save or undo."))];

        _vaults.Close();
        List<Result> results = [];
        try
        {
            results = Each(dryRun: false, chosen.Contains);
            return results;
        }
        finally
        {
            _vaults.Reopened(results.FirstOrDefault(result => result.Report is not null && result.Hardcore == _vaults.IsHardcore)?.Vault);
            Migrated?.Invoke();
        }
    }

    // ---- characters -------------------------------------------------------------------------------------------------

    /// <summary>One character of the save folder brought into this season in place: what it would be, or why not.</summary>
    public sealed record CharacterResult(string Path, string Name, bool Hardcore, CharacterMigration.Report? Report, string? Refused);

    /// <summary>
    /// The characters of the save folder a character's migration has something to do for: gold this season would drop,
    /// or the season before's base life and mana (<see cref="CharacterMigration.Waits"/>). Read without opening any of them.
    /// </summary>
    public IReadOnlyList<string> WaitingCharacters()
    {
        if (To is null || _session.SaveFolder is not { Length: > 0 } folder || !System.IO.Directory.Exists(folder))
            return [];

        var waiting = new List<string>();
        try
        {
            foreach (string file in System.IO.Directory.EnumerateFiles(folder, "*.d2s").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var (save, _) = PD2SPCompanion.Core.Saves.SaveVerifier.Load(file);
                    if (CharacterMigration.Waits(save, PD2SPCompanion.Core.Season.Current?.Game))
                        waiting.Add(file);
                }
                catch (Exception problem)
                {
                    // One that does not read is not offered: the patch guard and the health mark say why.
                    Log.Caught(problem);
                }
            }
        }
        finally
        {
            // The open character's layout back: each read above set the parser's for its own file.
            if (_session.IsLoaded)
                _session.UseMyLayout();
        }

        return waiting;
    }

    /// <summary>A dry run of each character waiting: what would move, or why it cannot.</summary>
    public IReadOnlyList<CharacterResult> PreviewCharacters() => EachCharacter(WaitingCharacters(), dryRun: true);

    /// <summary>
    /// Brings the characters chosen into this season, each with its mode's shared stash. Refused while the open
    /// character or shared stash has changes not saved: the run writes both files, and they would be written over. The
    /// open ones are read again after, if the run wrote them.
    /// </summary>
    public IReadOnlyList<CharacterResult> MigrateCharacters(IReadOnlyCollection<string> chosen)
    {
        if (CharactersHeld is { } held)
            return [.. chosen.Select(path => new CharacterResult(path, System.IO.Path.GetFileNameWithoutExtension(path), false, null, held))];

        if (_session.HasUnsavedChanges || _stash.HasUnsavedChanges)
            return [new CharacterResult("", "", false, null, L("Require save or undo."))];

        var results = EachCharacter(chosen, dryRun: false);

        if (_session.SavePath is { } open && results.Any(result => result.Report is not null
                                                                   && string.Equals(result.Path, open, StringComparison.OrdinalIgnoreCase)))
        {
            _session.Load(open);
        }
        else if (_stash.IsLoaded && results.Any(result => result.Report is { GoldMoved: > 0 }))
        {
            _stash.Load();
        }

        Migrated?.Invoke();
        return results;
    }

    /// <summary>
    /// Every character asked, in one run (<see cref="CharacterMigration.RunAll"/>): the old season's tables opened once for
    /// all of them - one at a time, six cards held the panel's opening some four seconds.
    /// </summary>
    private List<CharacterResult> EachCharacter(IEnumerable<string> paths, bool dryRun)
    {
        var results = new List<CharacterResult>();
        if (_session.Season is not { } season)
            return results;

        try
        {
            // Each with its mode's shared stash: hardcore's own.
            var asked = new List<(string Path, bool Hardcore, CharacterMigration.Request Request)>();
            foreach (string path in paths)
            {
                bool hardcore = PD2SPCompanion.Core.Saves.SaveSummary.TryRead(path)?.IsHardcore ?? false;
                string stash = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "",
                                                      hardcore ? SharedStashSession.HardcoreFile : SharedStashSession.SoftcoreFile);
                asked.Add((path, hardcore, new CharacterMigration.Request(path, stash, dryRun)));
            }

            var done = CharacterMigration.RunAll(season, [.. asked.Select(each => each.Request)]);
            for (int at = 0; at < asked.Count; at++)
            {
                var (path, hardcore, _) = asked[at];
                results.Add(new CharacterResult(path, done[at].Report?.Name ?? System.IO.Path.GetFileNameWithoutExtension(path),
                                                hardcore, done[at].Report, done[at].Refused));
            }
        }
        catch (InvalidOperationException refused)
        {
            results.AddRange(paths.Select(path => new CharacterResult(path, System.IO.Path.GetFileNameWithoutExtension(path), false, null, refused.Message)));
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            results.AddRange(paths.Select(path => new CharacterResult(path, System.IO.Path.GetFileNameWithoutExtension(path), false, null,
                                                                      L($"Not migrated: {problem.Message}"))));
        }
        finally
        {
            if (_session.IsLoaded)
                _session.UseMyLayout();
        }

        return results;
    }

    private List<Result> Each(bool dryRun, Func<(string Season, bool Hardcore, string Vault), bool>? only = null)
    {
        var results = new List<Result>();
        if (_session.Season is not { } season || To is not { } to)
            return results;

        foreach (string from in Pending())
        {
            foreach (var (hardcore, vault) in SeasonMigration.PendingVaults(from, to))
            {
                if (only?.Invoke((from, hardcore, vault)) == false)
                    continue;

                try
                {
                    // A preview is redone each time the panel opens: no report file for it.
                    var report = SeasonMigration.Run(season, new SeasonMigration.Request(from, hardcore, dryRun, StatIdBits: VaultService.StatIdBits,
                                                                                 KeepReport: !dryRun, Vault: vault));
                    results.Add(new Result(from, hardcore, vault, report, null));
                }
                catch (InvalidOperationException refused)
                {
                    results.Add(new Result(from, hardcore, vault, null, refused.Message));
                }
                catch (Exception problem)
                {
                    Log.Caught(problem);
                    results.Add(new Result(from, hardcore, vault, null, L($"Not migrated: {problem.Message}")));
                }
            }
        }

        return results;
    }

    private static T? Safely<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return default;
        }
    }
}
