using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// Settings. For now: how many backups of a character to keep.
/// </summary>
public partial class VP_ViewModel_Settings_L : ViewModelBase
{
    public CharacterSession Session { get; }
    public EditorSettings Settings { get; }

    private readonly ISFXManager _sfxManager = null!;
    private readonly ConfirmService _confirm;

    /// <summary>
    /// Where the game is, which everything the editor reads comes out of. Shown rather
    /// than typed into: a path half-edited in a box is a path that names nothing, and this
    /// one is worth changing only deliberately.
    /// </summary>
    public string GameFolder =>
        Settings.GameFolder.Length > 0 ? Settings.GameFolder : L("Not chosen");

    /// <summary>
    /// The patch guard: whether writes are held over a new PD2, which files did not read back, and
    /// the Accept that lifts it.
    /// </summary>
    public PatchGuardService Guard { get; }

    /// <summary>
    /// What the folder yields, said as a fact rather than as a reassurance: the mod
    /// version the archives there belong to.
    /// </summary>
    public string GameFolderVersion =>
        Settings.HasGameFolder && ModVersion.Of(Settings.GameFolder).Name is { Length: > 0 } named
            ? L($"Project Diablo 2 {named}{SeasonOfFolder}")
            : "";

    /// <summary>
    /// " · S13": the season the open tables say, beside the mod version (the game's
    /// season belongs with the game, not a second line in About). Only for the folder the tables were
    /// read from - a folder just chosen is read at the next start.
    /// </summary>
    private string SeasonOfFolder =>
        Session.Season is { } season
        && string.Equals(Path.GetFullPath(season.Root).TrimEnd('\\'), Path.GetFullPath(Settings.GameFolder).TrimEnd('\\'),
                         StringComparison.OrdinalIgnoreCase)
            ? $" · {PD2SPCompanion.Core.SeasonSnapshot.NameFor(season)}"
            : "";

    public bool HasGameFolderVersion => GameFolderVersion.Length > 0;

    /// <summary>
    /// Set once the folder has been changed, because the change does not take until the
    /// editor is started again - by the time this page can be reached the tables, strings
    /// and sprites are loaded from the old one.
    /// </summary>
    [ObservableProperty] private string _gameFolderNote = "";

    public bool HasGameFolderNote => GameFolderNote.Length > 0;

    partial void OnGameFolderNoteChanged(string value) =>
        OnPropertyChanged(nameof(HasGameFolderNote));

    [RelayCommand]
    private async Task ChangeGameFolder()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        if (await FolderPicker.Pick(L("Select Project Diablo II game folder")) is not { Length: > 0 } chosen)
            return;

        // ProjectD2 picked: the Diablo II folder around it.
        string picked = GameLocation.Resolve(chosen);

        if (GameLocation.Problem(picked) is { } wrong)
        {
            GameFolderNote = wrong;
            return;
        }

        if (string.Equals(picked, Settings.GameFolder, StringComparison.OrdinalIgnoreCase))
        {
            GameFolderNote = "";
            return;
        }

        Settings.GameFolder = picked;

        OnPropertyChanged(nameof(GameFolder));
        OnPropertyChanged(nameof(GameFolderVersion));
        OnPropertyChanged(nameof(HasGameFolderVersion));

        GameFolderNote = L("Require restart.");
    }

    /// <summary>
    /// The home page's model, whose list of the open character's backups Storage opens over this page (the
    /// backups out of the main flow, into Settings). One list, the home page's own - it
    /// still opens there by itself when a character will not load.
    /// </summary>
    public VP_ViewModel_Default_L? Home { get; }

    /// <summary>Used by the designer, which gets no services.</summary>
    public VP_ViewModel_Settings_L()
        : this(new CharacterSession(), EditorSettings.Load(), null!, new ConfirmService()) { }

    public VP_ViewModel_Settings_L(
        CharacterSession session, EditorSettings settings, ISFXManager sfxManager,
        ConfirmService confirm, PatchGuardService? guard = null, WhatsNewService? whatsNew = null,
        VP_ViewModel_Default_L? home = null, PD2SPCompanion.Shell.Services.Vault.VaultService? vaults = null)
    {
        _whatsNew = whatsNew;
        _vaults = vaults;
        Home = home;

        Guard = guard ?? new PatchGuardService(session, settings);
        Session = session;
        Settings = settings;
        _sfxManager = sfxManager;
        _confirm = confirm;

        // Lowering the limit does not delete anything there and then. Stepping from ten
        // down to three would otherwise delete on the way past each number, and
        // overshooting would cost copies that stepping back up could not bring back.
        // What is over the limit goes at the next start instead.
        Settings.PropertyChanged += (_, _) =>
        {
            RefreshCounts();
        };

        // Every edit and every save in the folder changes the counts, and they are read from disk: done
        // while the page is shown, or once when it is shown next (WhenShown).
        _counts = new WhenShown(RefreshCountsInBackground, afreshOnShow: true);
        Session.Changed += _counts.Ask;

        // Backups are deleted from the landing page as well as from here, so the count
        // follows the folder rather than only this page's own button.
        Session.SaveFolderChanged += _counts.Ask;

        // On a first run there is no save folder yet, so the bulk trim waits for one to
        // be settled rather than running against nothing. It still runs once and once
        // only: the watcher raises this event on every change in the folder, and trimming
        // on each of those is not what "at the next start" means.
        Session.SaveFolderChanged += ApplyLimit;

        // Whatever the last run left over the limit goes now. This is the only place
        // the limit is applied in bulk, which is why lowering it says "next time".
        ApplyLimit();

        RefreshCounts();
    }

    private readonly WhenShown _counts;

    /// <summary>
    /// How many backups are over the limit and waiting to go. Saving prunes the character
    /// it wrote, so this is what a lowered limit has left behind.
    /// </summary>
    [ObservableProperty] private int _overLimit;

    /// <summary>
    /// Said as the number rises - a limit lowered - and once: it was said at every change of any setting while
    /// any stood over, Animations, the UI scale and the play mode too.
    /// </summary>
    partial void OnOverLimitChanged(int oldValue, int newValue)
    {
        if (newValue > oldValue)
        {
            Session.Status =
                Plural(newValue, $"{newValue} backup over limit, deleted on next start.",
                       $"{newValue} backups over limit, deleted on next start.");
        }
    }

    /// <summary>
    /// Deletes whatever is over the limit. Run at startup only: the limit is changed a
    /// step at a time, and acting on each step would delete copies that stepping back
    /// could not restore.
    /// </summary>
    private bool _limitApplied;

    private void ApplyLimit()
    {
        // Nothing to trim in a folder that is not settled yet, and Directory.GetFiles on
        // an empty path throws rather than finding none.
        if (_limitApplied || !Session.IsConfigured)
            return;

        _limitApplied = true;

        int removed = 0;
        try
        {
            foreach (string save in Directory.GetFiles(Session.SaveFolder, "*.d2s"))
                removed += SaveWriter.Prune(save, Settings.BackupsToKeep);

            // The vaults' own copies too, held to their own count (Vault.PruneBackups) - and those of vaults
            // that are no more, which no count reaches, past their month (Vault.PruneOrphans).
            removed += PD2SPCompanion.Core.Collection.Vault.PruneBackups(Settings.VaultBackupsToKeep);
            removed += PD2SPCompanion.Core.Collection.Vault.PruneOrphans();
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            Session.Status = L($"Backup limit not applied: {ex.Message}");
            return;
        }

        if (removed > 0)
        {
            Session.Status =
                Plural(removed, $"Deleted {removed} old backup.", $"Deleted {removed} old backups.");
        }
    }

    /// <summary>Backups currently on disk, across every character.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupSummary))]
    [NotifyPropertyChangedFor(nameof(HasBackups))]
    private int _backupCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupSummary))]
    private string _backupSize = "";

    /// <summary>The row's second line, as the Vault card's are: "52 files · 258.1 KB".</summary>
    public string BackupSummary => BackupCount == 0 ? L("None")
        : Plural(BackupCount, $"1 file · {BackupSize}", $"{BackupCount} files · {BackupSize}");

    public bool HasBackups => BackupCount > 0;

    /// <summary>What the current setting means, in a sentence.</summary>
    public string Policy =>
        (Settings.BackupsToKeep == 0
            ? L("Keeps the last 15 minutes only")
            : L($"Keeps the last 15 minutes, then {Settings.BackupsToKeep} older per character")
              + Environment.NewLine + L("Older ones thinned: one per 10 minutes, hour, day"))
          + (OverLimit > 0
              ? Environment.NewLine + L($"Warning: {OverLimit} over limit, deleted on next start!")
              : "");

    public IReadOnlyList<string> BackupChoiceNames => EditorSettings.BackupChoiceNames;

    /// <summary>The languages offered: English, then each pack found in the Languages folder, by name.</summary>
    public IReadOnlyList<(string Code, string Name)> Languages { get; } = PD2SPCompanion.Core.Localization.Language.Available();

    /// <summary>Whether there is a choice to make: a pack beside English. Without one the section is hidden.</summary>
    public bool OffersLanguages => Languages.Count > 1;

    public IReadOnlyList<string> LanguageNames => [.. Languages.Select(each => each.Name)];

    public int LanguageIndex
    {
        get
        {
            int at = Languages.ToList().FindIndex(each => each.Code.Equals(Settings.Language, StringComparison.OrdinalIgnoreCase));
            return Math.Max(0, at);
        }
        set
        {
            if (value < 0 || value >= Languages.Count || Languages[value].Code.Equals(Settings.Language, StringComparison.OrdinalIgnoreCase))
                return;
            Settings.Language = Languages[value].Code;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageNote));
            OnPropertyChanged(nameof(HasLanguageNote));
        }
    }

    /// <summary>A language chosen that is not the one in use: it waits for the next start.</summary>
    public string LanguageNote =>
        Settings.Language.Equals(PD2SPCompanion.Core.Localization.Language.Code, StringComparison.OrdinalIgnoreCase)
            ? ""
            : L("Require restart.");

    public bool HasLanguageNote => LanguageNote.Length > 0;

    /// <summary>The vault's own drop-down, the same choices: how many of its older copies are kept.</summary>
    public int VaultBackupIndex
    {
        get => Math.Max(0, EditorSettings.BackupChoices.ToList().IndexOf(Settings.VaultBackupsToKeep));
        set
        {
            if (value < 0 || value >= EditorSettings.BackupChoices.Count || EditorSettings.BackupChoices[value] == Settings.VaultBackupsToKeep)
                return;
            Settings.VaultBackupsToKeep = EditorSettings.BackupChoices[value];
            OnPropertyChanged();
            OnPropertyChanged(nameof(VaultPolicy));
            OnPropertyChanged(nameof(VaultBackupSummary));
        }
    }

    /// <summary>What the vault's setting means, in a sentence: its row's tooltip.</summary>
    public string VaultPolicy =>
        L("One copy for each session that changed the vault") + Environment.NewLine
        + (Settings.VaultBackupsToKeep == 0
            ? L("Keeps the last 15 minutes only")
            : L($"Keeps the last 15 minutes, then {Settings.VaultBackupsToKeep} older"));

    /// <summary>The vault's copies on disk, under its row: "12 files · 5.2 MB" - what the number costs.</summary>
    public string VaultBackupSummary
    {
        get
        {
            try
            {
                var copies = PD2SPCompanion.Core.Collection.Vault.Copies();
                return copies.Count == 0 ? L("None")
                    : Plural(copies.Count, $"1 file · {SizeText(copies.Sum(copy => copy.Bytes))}",
                             $"{copies.Count} files · {SizeText(copies.Sum(copy => copy.Bytes))}");
            }
            catch (Exception problem)
            {
                Log.Caught(problem);
                return "";
            }
        }
    }

    /// <summary>
    /// The drop-down's choice: Never, 5, 10, 15, 20 (it was a stepper). Lowering it
    /// deletes nothing now: what is over goes at the next start, as before.
    /// </summary>
    public int BackupIndex
    {
        get => Math.Max(0, EditorSettings.BackupChoices.ToList().IndexOf(Settings.BackupsToKeep));
        set
        {
            if (value < 0 || value >= EditorSettings.BackupChoices.Count || EditorSettings.BackupChoices[value] == Settings.BackupsToKeep)
                return;
            Settings.BackupsToKeep = EditorSettings.BackupChoices[value];
            OnPropertyChanged();
            Notify();
        }
    }

    /// <summary>
    /// Throws away every backup of every character, for a clean start. The limit above
    /// trims the oldest after a save; this empties the lot now.
    /// <para>
    /// Asked about first, because unlike everything else on this page it cannot be undone.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void DeleteAll()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        string[] files;
        try
        {
            string root = SaveWriter.BackupRoot(Session.SaveFolder);
            files = Directory.Exists(root)
                ? Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories)
                : [];
        }
        catch (Exception ex)
        {
            Log.Caught(ex);
            Session.Status = L($"Backups not read: {ex.Message}");
            return;
        }

        if (files.Length == 0)
        {
            Session.Status = L("No backups.");
            return;
        }

        _confirm.Ask(
            L("Delete all backups"),
            Plural(files.Length, $"{files.Length} backup will be deleted. Characters are not affected.",
                   $"{files.Length} backups will be deleted. Characters are not affected.")
            + Environment.NewLine + L("Warning: Cannot be undone!"),
            L("DELETE ALL"),
            () => Delete(files), deletes: true);
    }

    private void Delete(string[] files)
    {
        int removed = 0;
        foreach (string file in files)
        {
            try
            {
                File.Delete(file);
                SaveWriter.ForgetCaption(new FileInfo(file));
                removed++;
            }
            catch (Exception)
            {
                // Open elsewhere, or read-only. Left be rather than failing the rest.
            }
        }

        // The folder a character's copies lived in has nothing left to say.
        try
        {
            foreach (string folder in Directory.GetDirectories(SaveWriter.BackupRoot(Session.SaveFolder)))
            {
                if (Directory.GetFileSystemEntries(folder).Length == 0)
                    Directory.Delete(folder);
            }
        }
        catch (Exception)
        {
            // Tidying the empty folders is not worth reporting a failure over.
        }

        Session.Status = removed == files.Length
            ? Plural(removed, $"Deleted {removed} backup.", $"Deleted {removed} backups.")
            : L($"Deleted {removed} of {files.Length} backups.");

        RefreshCounts();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Policy));
        OnPropertyChanged(nameof(OverLimit));

        RefreshCounts();
    }

    /// <summary>
    /// The trade site's listings kept on disk, and their size. Counted when the page is shown as
    /// well, since buying on the trading page adds to it and tells nothing here.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CacheSummary))]
    [NotifyPropertyChangedFor(nameof(HasCache))]
    private int _cacheCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CacheSummary))]
    private string _cacheSize = "";

    public string CacheSummary => CacheCount == 0 ? L("None")
        : Plural(CacheCount, $"1 listing · {CacheSize}", $"{CacheCount} listings · {CacheSize}");

    public bool HasCache => CacheCount > 0;

    /// <summary>The Settings page coming into view: whatever changed on disk since, counted again.</summary>
    public void Shown()
    {
        _counts.Shown();

        // The open character's backups as they now are: its line under Storage, and the list behind it.
        Home?.LookAtBackups();
    }

    /// <summary>Put away (the view, through PageDeck): changes are counted on the next visit.</summary>
    public void Hidden() => _counts.Hidden();

    /// <summary>
    /// Empties the trade cache. Asked about first: a listing sold or expired since cannot be
    /// fetched again, and the site keeps no archive for a player who is not logged in.
    /// </summary>
    [RelayCommand]
    private void ClearCache()
    {
        _sfxManager?.Method_ButtonSFX_Press();

        int count = TradeListings.CacheSize().Count;
        if (count == 0)
        {
            Session.Status = L("No cached listings.");
            return;
        }

        _confirm.Ask(
            L("Clear trade cache"),
            Plural(count, $"{count} cached listing will be deleted.", $"{count} cached listings will be deleted.")
            + Environment.NewLine + L("Warning: Sold listings cannot be reopened!"),
            L("CLEAR"),
            () =>
            {
                int removed = TradeListings.ClearCache();
                Session.Status = removed == count
                    ? Plural(removed, $"Cleared {removed} cached listing.", $"Cleared {removed} cached listings.")
                    : L($"Cleared {removed} of {count} cached listings.");
                RefreshCounts();
            });
    }

    internal static string SizeText(long bytes) => bytes switch
    {
        < 1024 => L($"{bytes} bytes"),
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };

    /// <summary>
    /// What the disk holds for the page's counts: the trade cache and this editor's backups. A size is null
    /// where it could not be read (or, for backups, with no save folder): shown blank.
    /// </summary>
    private sealed record Counts(int CacheCount, long? CacheBytes, int Backups, long? BackupBytes, int OverLimit, long? DataBytes = null);

    /// <summary>Everything in the data folder, in bytes; null where it could not be read.</summary>
    private static long? DataBytesOnDisk()
    {
        try
        {
            var root = new DirectoryInfo(PD2SPCompanion.Core.Storage.EditorData.Root);
            return root.Exists ? root.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length) : 0;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Counted from the disk alone, so any thread may do it: the page's visits count in the background
    /// (<see cref="RefreshCountsInBackground"/>), a page that waited on the disk every visit.
    /// </summary>
    private static Counts CountOnDisk(bool configured, string saveFolder, int keep)
    {
        int cacheCount = 0;
        long? cacheBytes = null;
        try
        {
            (cacheCount, long bytes) = TradeListings.CacheSize();
            cacheBytes = bytes;
        }
        catch (Exception)
        {
            cacheCount = 0;
        }

        long? data = DataBytesOnDisk();
        if (!configured)
            return new Counts(cacheCount, cacheBytes, 0, null, 0, data);

        try
        {
            // Only what this editor wrote: a folder per character under our own root.
            // The save folder itself belongs to the game and to whatever else is
            // installed beside it.
            string root = SaveWriter.BackupRoot(saveFolder);

            string[] files = Directory.Exists(root)
                ? Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories)
                : [];

            // Counted per character, the way the limit is applied: three each is not
            // over a limit of three, however many characters there are.
            int overLimit = Directory.GetFiles(saveFolder, "*.d2s")
                                     .Sum(save => SaveWriter.Surplus(save, keep));

            return new Counts(cacheCount, cacheBytes, files.Length, files.Sum(f => new FileInfo(f).Length), overLimit, data);
        }
        catch (Exception)
        {
            return new Counts(cacheCount, cacheBytes, 0, null, 0, data);
        }
    }

    private void ShowCounts(Counts counts)
    {
        CacheCount = counts.CacheCount;
        CacheSize = counts.CacheBytes is long cache ? SizeText(cache) : "";
        BackupCount = counts.Backups;
        BackupSize = counts.BackupBytes is long backups ? SizeText(backups) : "";
        OverLimit = counts.OverLimit;
        DataSize = counts.DataBytes is long data ? SizeText(data) : "";
        OnPropertyChanged(nameof(Policy));
        OnPropertyChanged(nameof(VaultBackupSummary));
    }

    /// <summary>
    /// How much the data folder holds, beside its path: "28.4 MB" - what is kept is held to counts and ages, and this
    /// is where it is seen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataFolderText))]
    private string _dataSize = "";

    /// <summary>The data folder's row, under its name: where it is, and how much it holds.</summary>
    public string DataFolderText => DataSize.Length > 0 ? $"{DataFolder} · {DataSize}" : DataFolder;

    /// <summary>The counts now: for an action that reads them straight after (a new limit).</summary>
    private void RefreshCounts() =>
        ShowCounts(CountOnDisk(Session.IsConfigured, Session.SaveFolder, Settings.BackupsToKeep));

    private int _countsAsked;

    /// <summary>
    /// The counts on a visit: read off the UI thread and shown when read, the latest asking only - the
    /// page opens at once on the counts it last had.
    /// </summary>
    private void RefreshCountsInBackground()
    {
        int asked = ++_countsAsked;
        bool configured = Session.IsConfigured;
        string folder = Session.SaveFolder;
        int keep = Settings.BackupsToKeep;

        Task.Run(() => CountOnDisk(configured, folder, keep)).ContinueWith(
            done =>
            {
                if (done.IsCompletedSuccessfully && asked == _countsAsked)
                    ShowCounts(done.Result);
            },
            TaskScheduler.FromCurrentSynchronizationContext());

        Task.Run(SmartSort.Current).ContinueWith(
            done =>
            {
                if (done.IsCompletedSuccessfully && asked == _countsAsked)
                    SortingRulesText = RulesText(done.Result);
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ---- Sorting rules: the file sorted storing reads --------------------------

    /// <summary>
    /// The sorting rules' row, under its name: the file, how many rules it holds, and how many lines were skipped -
    /// what an edit made in another program came to, read again when the page is shown.
    /// </summary>
    [ObservableProperty]
    private string _sortingRulesText = SmartSort.FileName;

    private static string RulesText(SmartSort.Rules rules)
    {
        string count = Plural(rules.All.Count, $"{SmartSort.FileName} · 1 rule", $"{SmartSort.FileName} · {rules.All.Count} rules");
        return rules.Problems.Count == 0
            ? count
            : Plural(rules.Problems.Count, $"{count}, 1 line skipped", $"{count}, {rules.Problems.Count} lines skipped");
    }

    /// <summary>The rules file, opened in the program Windows opens a .filter file with - written first if there is none.</summary>
    [RelayCommand]
    private void OpenSortingRules()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        try
        {
            SortingRulesText = RulesText(SmartSort.Current());
            using var editor = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(SmartSort.FilePath) { UseShellExecute = true });
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            Session.Status = L($"File not opened: {problem.Message}");
        }
    }

    /// <summary>The rules file shown in its folder, selected - beside it the default to compare with.</summary>
    [RelayCommand]
    private void ShowSortingRules()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        try
        {
            SortingRulesText = RulesText(SmartSort.Current());
            using var explorer = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{SmartSort.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            Session.Status = L($"Folder not opened: {problem.Message}");
        }
    }

    // ---- About: the build, the data folder, the release notes ----------------

    public string AppName => PD2SPCompanion.Core.AppVersion.AppName;

    /// <summary>"1.0.0", or "1.0.0-dev" for a build that is not a release.</summary>
    public string VersionLabel => PD2SPCompanion.Core.AppVersion.Label;

    /// <summary>"Built 2026-10-01 from 994e076".</summary>
    public string VersionBuilt => PD2SPCompanion.Core.AppVersion.Built;

    /// <summary>Where the editor keeps its settings, vaults, logs and backups.</summary>
    public string DataFolder => PD2SPCompanion.Core.Storage.EditorData.Root;

    /// <summary>
    /// The titles' font: "Cinzel", the editor's own, or the one read from the Fonts folder as the editor started
    /// (Services/Settings/TitleFont) - so a player who put a file there sees whether it was taken.
    /// </summary>
    public string TitleFontText =>
        PD2SPCompanion.Shell.Services.Settings.TitleFont.FromFolder
            ? L($"{PD2SPCompanion.Shell.Services.Settings.TitleFont.Family} · from the Fonts folder")
            : PD2SPCompanion.Shell.Services.Settings.TitleFont.Family;

    [RelayCommand]
    private void OpenFontsFolder()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        try
        {
            string folder = PD2SPCompanion.Shell.Services.Settings.TitleFont.Folder;
            System.IO.Directory.CreateDirectory(folder);
            using var explorer = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            Session.Status = L($"Folder not opened: {problem.Message}");
        }
    }

    /// <summary>
    /// One line for a bug report: the editor's build, the game's version and season, and Windows - what
    /// "which version?" would otherwise take a question and an answer to learn.
    /// </summary>
    public string BugReportLine =>
        string.Join(" · ", new[]
        {
            $"{AppName} {VersionLabel}",
            VersionBuilt,
            GameFolderVersion,
            Environment.OSVersion.VersionString,
        }.Where(part => part.Length > 0));

    [RelayCommand]
    private async Task CopyVersion()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        try
        {
            if (Avalonia.Controls.TopLevel.GetTopLevel(FolderPicker.Window())?.Clipboard is { } clipboard)
            {
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, BugReportLine);
                Session.Status = L("Version copied.");
            }
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            Session.Status = L("Not copied: clipboard busy.");
        }
    }

    // ---- a report of a problem (PD2SPCompanion.Core.Diagnostics.ProblemReport) ------------

    private readonly PD2SPCompanion.Shell.Services.Vault.VaultService? _vaults;

    /// <summary>
    /// Whether the report takes the open character's files and a copy of the vault with it. Off as the program
    /// starts, each time: they are the player's own, and go in only where the player says so.
    /// </summary>
    [ObservableProperty] private bool _reportWithSaves;

    /// <summary>The report's row, under its name: what goes in.</summary>
    public string ReportText => ReportWithSaves
        ? L("Logs, settings, open character and vault, in one zip")
        : L("Logs and settings, in one zip");

    partial void OnReportWithSavesChanged(bool value) => OnPropertyChanged(nameof(ReportText));

    /// <summary>
    /// The report written, and its path: the logs, the settings, a page of what is running - and, where asked,
    /// the open character's files and a copy of the vault. Nothing is sent: the player attaches the file.
    /// </summary>
    public string MakeReport()
    {
        var files = new List<(string Source, string Entry)>(PD2SPCompanion.Core.Diagnostics.ProblemReport.Logs(PD2SPCompanion.Core.Storage.EditorData.Logs))
        {
            (PD2SPCompanion.Core.Storage.EditorData.SettingsFile, "settings.json"),
        };

        string? vaultCopy = null;
        try
        {
            if (ReportWithSaves)
            {
                // The character's own files - its save and what the game keeps beside it under its name - and
                // the two shared stashes; none of the backups.
                if (Session.IsLoaded && Session.SavePath is { Length: > 0 } save && Path.GetDirectoryName(save) is { } folder)
                {
                    string stem = Path.GetFileNameWithoutExtension(save);
                    files.AddRange(Directory.GetFiles(folder, stem + ".*")
                        .Where(file => !file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                        .Select(file => (file, "Saves/" + Path.GetFileName(file))));
                    files.AddRange(new[] { SharedStashSession.SoftcoreFile, SharedStashSession.HardcoreFile }
                        .Select(name => (Path.Combine(folder, name), "Saves/" + name)));
                }

                // The vault as it stands, through its own copy: its file alone may be half of it.
                if (_vaults?.Store is { } store)
                {
                    vaultCopy = Path.Combine(Path.GetTempPath(), $"pd2spc-report-{Guid.NewGuid():N}.pd2lib");
                    store.CopyTo(vaultCopy);
                    files.Add((vaultCopy, "Vault/" + Path.GetFileName(store.FilePath)));
                }
            }

            return PD2SPCompanion.Core.Diagnostics.ProblemReport.Write(
                PD2SPCompanion.Core.Storage.EditorData.Reports, ReportAbout(), files, DateTime.Now);
        }
        finally
        {
            if (vaultCopy is not null)
            {
                try
                {
                    File.Delete(vaultCopy);
                }
                catch (Exception)
                {
                    // A temporary file left behind is Windows' to clear.
                }
            }
        }
    }

    /// <summary>The report's first page: what is running, on what, set how - and what the data folder holds, part by part.</summary>
    private string ReportAbout()
    {
        string scale = Settings.UiScale == 0 ? "Automatic" : Settings.UiScale + "%";
        double inForce = PD2SPCompanion.Shell.Services.Settings.UiScale.Current;
        var lines = new List<string>
        {
            $"{AppName} {VersionLabel}",
            VersionBuilt,
            $"Game: {(GameFolderVersion.Length > 0 ? GameFolderVersion : "not read")}",
            $"Windows: {Environment.OSVersion.VersionString}",
            $".NET: {Environment.Version}",
            $"Play mode: {Settings.PlayMode}",
            $"UI scale: {scale}, in force {inForce:0.###}",
            $"Language: {Settings.Language}",
            $"Character open: {(Session.IsLoaded ? "yes" : "no")}",
            $"Report made: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            "",
            $"Data folder: {DataFolder}",
        };

        try
        {
            var root = new DirectoryInfo(DataFolder);
            foreach (var part in root.GetDirectories().OrderBy(each => each.Name, StringComparer.OrdinalIgnoreCase))
            {
                var held = part.GetFiles("*", SearchOption.AllDirectories);
                lines.Add($"  {part.Name}: {held.Length} files, {held.Sum(file => file.Length) / 1024} KB");
            }
        }
        catch (Exception problem)
        {
            lines.Add($"  not read: {problem.Message}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Report a problem: the report made, and shown in its folder - to be attached where the problem is reported.</summary>
    [RelayCommand]
    private void ReportProblem()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        try
        {
            string path = MakeReport();
            Session.Status = L($"Report made: {Path.GetFileName(path)}.");
            RefreshCountsInBackground();

            // Shown in Explorer, the file selected.
            using var explorer = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            Session.Status = L($"Report not made: {problem.Message}");
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        try
        {
            using var explorer = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(DataFolder) { UseShellExecute = true });
        }
        catch (Exception problem)
        {
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            Session.Status = L($"Folder not opened: {problem.Message}");
        }
    }

    /// <summary>The release notes, in the popup over the whole window.</summary>
    private readonly WhatsNewService? _whatsNew;

    [RelayCommand]
    private void OpenWhatsNew()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        _whatsNew?.Show();
    }

    /// <summary>The sheet of keys and gestures, over the whole window (KeySheetService).</summary>
    [RelayCommand]
    private void OpenKeys()
    {
        _sfxManager?.Method_ButtonSFX_Press();
        KeySheetService.Shared.Show();
    }

    public string KeysHint => ShortcutText.With(L("Show keys and gestures"), L("F1"));
}
