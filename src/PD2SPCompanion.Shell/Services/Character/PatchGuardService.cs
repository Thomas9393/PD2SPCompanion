using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// Holds writes to the game's files when the installed PD2 is not the build the saves were last
/// checked against, until they are; of the utmost importance. <see cref="GameSync"/> asks it before every write, as it asks
/// whether the game runs.
/// <list type="bullet">
/// <item>At start: the same build as the settings' <see cref="EditorSettings.VerifiedBuild"/> needs
/// nothing. A new one, or the first run, has every character and shared stash read back in memory;
/// all exact makes it the known good build, anything not holds writes, with the files named and an
/// Accept for after the new-season checklist.</item>
/// <item>On every window activation: a build different from the one the tables were loaded from
/// means they are stale - writes held until the editor restarts, with no Accept and no Repair.</item>
/// <item>A folder settled while running - a first run, or a folder that has since gone: writes held from that moment
/// until this check has run (<see cref="HoldUntilChecked"/>).</item>
/// </list>
/// </summary>
public sealed partial class PatchGuardService : ObservableObject
{
    private readonly CharacterSession _session;
    private readonly EditorSettings _settings;

    /// <summary>The build the tables in memory came from; empty until the first check.</summary>
    private string _loadedBuild = "";

    /// <summary>
    /// The build the saves were last checked against - kept with what was checked. The player's
    /// saves: in the settings. A test's copies (<c>PD2SPC_SAVES</c>): in a file beside them, so a test
    /// never tells the player's settings a build is good on the strength of its own copies (a
    /// probe run stored the real build there).
    /// </summary>
    private string KnownGood
    {
        get
        {
            if (TestFile() is not { } file)
                return _settings.VerifiedBuild;
            try { return File.Exists(file) ? File.ReadAllText(file).Trim() : ""; }
            catch (Exception) { return ""; }
        }
        set
        {
            if (TestFile() is not { } file)
            {
                _settings.VerifiedBuild = value;
                return;
            }
            try { File.WriteAllText(file, value); }
            catch (Exception) { }
        }
    }

    /// <summary>Where a test's known good build is kept: beside its save folder. Null for the player's own.</summary>
    private string? TestFile() =>
        Environment.GetEnvironmentVariable("PD2SPC_SAVES") is { Length: > 0 } saves
            ? Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(saves)) ?? saves, "pd2spc-verified-build.txt")
            : null;

    private readonly SharedStashSession? _stash;
    private readonly PD2SPCompanion.Shell.Services.Vault.VaultService? _vaults;

    public PatchGuardService(CharacterSession session, EditorSettings settings, SharedStashSession? stash = null,
                             PD2SPCompanion.Shell.Services.Vault.VaultService? vaults = null)
    {
        _session = session;
        _settings = settings;
        _stash = stash;
        _vaults = vaults;
    }

    /// <summary>Whether writes to the game's files are held.</summary>
    [ObservableProperty] private bool _isHeld;

    /// <summary>Why, in a line: what the strip and a refused button say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeldLine))]
    private string _reason = "";

    /// <summary>Held over files that did not read back - which Accept may lift. Not over stale tables.</summary>
    [ObservableProperty] private bool _canAccept;

    /// <summary>The files that did not read back, and why - those the repair cannot reach.</summary>
    public ObservableCollection<PatchGuard.Checked> Failed { get; } = [];

    /// <summary>
    /// The files an earlier build of PD2 wrote, in a way this one reads no more - characters, shared stashes, vaults
    /// (<see cref="EncodingRepair.Survey"/>): what REPAIR writes again.
    /// </summary>
    public ObservableCollection<EncodingRepair.Finding> Repairable { get; } = [];

    /// <summary>Held over files the repair can write again.</summary>
    [ObservableProperty] private bool _canRepair;

    /// <summary>What a refused write says of the hold.</summary>
    public string HeldLine => L($"{Reason} Writes held.");

    /// <summary>Held only until <see cref="CheckAtStart"/> has run: a game folder just settled.</summary>
    private bool _untilChecked;

    /// <summary>
    /// A game folder was settled while the program runs: nothing writes until the check it brings has run, a moment after.
    /// </summary>
    public void HoldUntilChecked()
    {
        _untilChecked = true;
        Reason = L("Reading saves.");
        IsHeld = true;
    }

    /// <summary>
    /// The check at start, once a game folder is known: the build's fingerprint against the one
    /// last checked, and the saves read back where they differ - and each file looked at under the
    /// encodings kept of earlier builds and seasons, for those that one of them wrote.
    /// </summary>
    public void CheckAtStart()
    {
        if (!_session.IsConfigured)
            return;

        try
        {
            if (_session.EnsureGame() is null)
                return;
        }
        catch (Exception problem)
        {
            // Tables that will not open cannot be checked against, and nothing can be read with them: said, and held
            // until a restart looks again. Out of here it ended the program.
            Log.Caught(problem);
            _untilChecked = false;
            Failed.Clear();
            Repairable.Clear();
            CanAccept = CanRepair = false;
            Reason = L("PD2 tables not read. Require restart.");
            IsHeld = true;
            _session.Status = HeldLine;
            return;
        }

        if (_untilChecked)
        {
            _untilChecked = false;
            Reason = "";
            IsHeld = false;
        }

        // The build the tables in memory were read from, as the season recorded it when it opened them.
        string build = _session.Season?.Build ?? PatchGuard.Fingerprint(_session.GameRoot);
        _loadedBuild = build;

        // An unknown build claims nothing either way.
        string known = KnownGood;
        if (build.Length == 0 || build == known)
            return;

        bool first = known.Length == 0;
        var results = PatchGuard.Verify(_session.SaveFolder);
        var repairable = Survey(anyUnread: results.Any(each => !each.Good));

        var bad = results.Where(each => !each.Good && !repairable.Any(found => found.Name.Equals(each.File, StringComparison.OrdinalIgnoreCase))).ToList();
        if (bad.Count == 0 && repairable.Count == 0)
        {
            KnownGood = build;
            if (!first)
                _session.Status = L($"PD2 changed: {Counted(results)} read back exactly.");
            return;
        }

        Hold(results.Count, bad, repairable);
    }

    /// <summary>
    /// The files a kept encoding wrote, as the survey finds them. Asked only where one can have: a build of this season
    /// kept whose tables differ - a file may read under both - or, with a file that did not read back
    /// (<paramref name="anyUnread"/>), an earlier season's. Otherwise nothing is read: a new build is most often the
    /// same encoding, and reading every file and the vault again held the window for nothing.
    /// </summary>
    private System.Collections.Generic.List<EncodingRepair.Finding> Survey(bool anyUnread)
    {
        if (_session.Season is not { } season)
            return [];

        try
        {
            var earlier = EncodingRepair.Candidates(season).Where(each => each.SameSeason || anyUnread).ToList();
            if (earlier.Count == 0)
                return [];

            var files = EncodingRepair.FilesOf(_session.SaveFolder, PD2SPCompanion.Core.SeasonSnapshot.NameFor(season));
            return [.. EncodingRepair.Survey(season, files, earlier)
                .Where(found => found.Verdict == EncodingRepair.Verdict.Repairable)];
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
            return [];
        }
        finally
        {
            if (_session.IsLoaded)
                _session.UseMyLayout();
        }
    }

    /// <summary>Writes held: what does not read back, and what the repair can write again.</summary>
    private void Hold(int checkedCount, System.Collections.Generic.IReadOnlyList<PatchGuard.Checked> bad,
                      System.Collections.Generic.IReadOnlyList<EncodingRepair.Finding> repairable)
    {
        Failed.Clear();
        foreach (var each in bad)
            Failed.Add(each);
        Repairable.Clear();
        foreach (var each in repairable)
            Repairable.Add(each);

        // Files a repair can mend are mended, not accepted over: that would lift the hold with them unread.
        CanAccept = bad.Count > 0 && Repairable.Count == 0;
        CanRepair = repairable.Count > 0;
        Reason = repairable.Count > 0
            ? Plural(repairable.Count, "PD2 changed how items are stored: 1 file needs repair.",
                     $"PD2 changed how items are stored: {repairable.Count} files need repair.")
            : L($"PD2 changed: {bad.Count} of {checkedCount} files do not read back.");
        IsHeld = true;
        _session.Status = HeldLine;
    }

    /// <summary>
    /// The files an earlier encoding wrote, written again in this one (<see cref="EncodingRepair.Repair"/>): every
    /// value kept, a backup of each first. Refused while the game runs or the open files have changes not saved -
    /// they are among those written. The open character, shared stash and vault are read again after, and the
    /// saves checked again: all read back, and this build is the known good one.
    /// <para>
    /// <b>A file tried and not mended is no more the repair's</b>: it goes to
    /// <see cref="Failed"/> with why, and Accept takes the hold over it, the file named. Left as repairable, every press
    /// failed the same way and no button lifted the hold. Refused on tables a patch has made stale.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void Repair()
    {
        // The build looked at again first: a patch since the window last came back would be written by the old tables.
        CheckAgain();
        if (!CanRepair || _session.Season is not { } season)
            return;
        if (_session.HasUnsavedChanges || _stash?.HasUnsavedChanges == true)
        {
            _session.Status = L("Require save or undo.");
            return;
        }

        var tried = Repairable.ToList();
        System.Collections.Generic.IReadOnlyList<EncodingRepair.Repaired> done;
        try
        {
            done = EncodingRepair.Repair(season, [.. Repairable]);
        }
        catch (InvalidOperationException notRun)
        {
            _session.Status = notRun.Message;
            return;
        }
        finally
        {
            if (_session.IsLoaded)
                _session.UseMyLayout();
        }

        if (_session.SavePath is { } open)
            _session.Load(open);
        if (_stash?.IsLoaded == true)
            _stash.Load();
        _vaults?.Reopened();

        var results = PatchGuard.Verify(_session.SaveFolder);
        var found = Survey(anyUnread: results.Any(each => !each.Good));

        // Tried and still the earlier encoding's, or refused whatever it reads as now: why, by file.
        var refused = done.Where(each => each.Refused is not null).ToList();
        bool WasTried(EncodingRepair.Finding finding) =>
            tried.Any(each => each.Path.Equals(finding.Path, StringComparison.OrdinalIgnoreCase));
        var stuck = refused.Select(each => new PatchGuard.Checked(Path.GetFileName(each.Path), each.Refused))
            .Concat(found.Where(WasTried)
                .Where(each => !refused.Any(gone => gone.Path.Equals(each.Path, StringComparison.OrdinalIgnoreCase)))
                .Select(each => new PatchGuard.Checked(each.Name, L("still not read after repair"))))
            .ToList();

        var repairable = found.Where(each => !WasTried(each)).ToList();
        bool Named(string file) =>
            repairable.Any(each => each.Name.Equals(file, StringComparison.OrdinalIgnoreCase))
            || stuck.Any(each => each.File.Equals(file, StringComparison.OrdinalIgnoreCase));
        var bad = results.Where(each => !each.Good && !Named(each.File)).Concat(stuck).ToList();
        int fixedCount = done.Count(each => each.Refused is null);
        if (bad.Count == 0 && repairable.Count == 0)
        {
            KnownGood = _loadedBuild;
            Failed.Clear();
            Repairable.Clear();
            CanAccept = CanRepair = false;
            Reason = "";
            IsHeld = false;
            _session.Status = Plural(fixedCount, "Repaired 1 file. Writes allowed.", $"Repaired {fixedCount} files. Writes allowed.");
            return;
        }

        // A vault is no file the read-back counts: refused, it counts all the same.
        Hold(results.Select(each => each.File).Concat(stuck.Select(each => each.File)).Distinct(StringComparer.OrdinalIgnoreCase).Count(), bad, repairable);
        if (refused.Count > 0)
        {
            _session.Status = string.Join(" ", refused.Select(each => L($"{Path.GetFileName(each.Path)} not repaired: {each.Refused}")).Append(HeldLine));
        }
    }

    /// <summary>
    /// The build looked at again - on every window activation. Changed since the tables were
    /// loaded, they are stale: writes held until a restart reads the new ones.
    /// </summary>
    public void CheckAgain()
    {
        if (_loadedBuild.Length == 0 || (IsHeld && !CanAccept && !CanRepair))
            return;

        string now = PatchGuard.Fingerprint(_session.GameRoot);
        if (now.Length == 0 || now == _loadedBuild)
            return;

        CanAccept = CanRepair = false;
        Failed.Clear();
        Repairable.Clear();
        Reason = L("PD2 changed while open. Require restart.");
        IsHeld = true;
        _session.Status = HeldLine;
    }

    /// <summary>
    /// Writes allowed with this build, after the new-season checklist: it becomes the known good
    /// one. Only over files that did not read back, never over stale tables.
    /// </summary>
    [RelayCommand]
    private void Accept()
    {
        CheckAgain();
        if (!CanAccept)
            return;

        KnownGood = _loadedBuild;
        Failed.Clear();
        CanAccept = false;
        Reason = "";
        IsHeld = false;
        _session.Status = L("PD2 version accepted. Writes allowed.");
    }

    /// <summary>"3 characters and the shared stash", from what was checked.</summary>
    private static string Counted(System.Collections.Generic.IReadOnlyList<PatchGuard.Checked> results)
    {
        int characters = results.Count(each => each.File.EndsWith(".d2s", StringComparison.OrdinalIgnoreCase));
        int stashes = results.Count - characters;
        string who = Plural(characters, "1 character", $"{characters} characters");
        return stashes switch
        {
            0 => who,
            1 => L($"{who} and the shared stash"),
            _ => L($"{who} and {stashes} shared stashes"),
        };
    }
}
