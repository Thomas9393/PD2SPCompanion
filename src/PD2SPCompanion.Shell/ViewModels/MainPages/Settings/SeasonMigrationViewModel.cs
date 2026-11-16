using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Core;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Vault;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One vault of an older season in the migration panel - or one character of the save folder (<paramref name="character"/>,
/// its file) - what would come across, or why it cannot, and its tick: migrated with the rest, or left where it is
/// (migrate, or start anew).
/// </summary>
public sealed partial class MigrationModeRow(string season, bool hardcore, string vault, string from,
                                             IReadOnlyList<string> lines, string? note, string? refused, string? character = null) : ObservableObject
{
    /// <summary>The character's file, where the card is a character's; null for a vault.</summary>
    public string? Character { get; } = character;

    public string Season { get; } = season;

    public bool Hardcore { get; } = hardcore;

    /// <summary>The vault's name: the card's title.</summary>
    public string Title { get; } = vault;

    /// <summary>Where it is from: "S12 Softcore".</summary>
    public string From { get; } = from;

    public IReadOnlyList<string> Lines { get; } = lines;

    public string? Note { get; } = note;

    public string? Refused { get; } = refused;

    public bool HasNote => Note is not null;

    public bool IsRefused => Refused is not null;

    /// <summary>One that can be migrated has a tick; one refused has none to give.</summary>
    public bool CanChoose => Refused is null;

    /// <summary>Raised when the tick changes.</summary>
    public Action? Chosen { get; set; }

    [ObservableProperty] private bool _isChosen = refused is null;

    partial void OnIsChosenChanged(bool value) => Chosen?.Invoke();
}

/// <summary>
/// The Settings page's season migration: the vault card's
/// "Migrate from" row, and the panel it opens - a dry run of each older season's vaults still to
/// migrate, each with its tick, then MIGRATE for the ones ticked. The run is
/// <see cref="SeasonMigrationService"/>'s.
/// </summary>
public sealed partial class SeasonMigrationViewModel : ViewModelBase
{
    private readonly CharacterSession _session;
    private readonly SeasonMigrationService _migration;
    private readonly ISFXManager? _sfx;

    public SeasonMigrationViewModel(CharacterSession session, SeasonMigrationService migration, VaultService vaults, ISFXManager? sfx)
    {
        _session = session;
        _migration = migration;
        _sfx = sfx;

        vaults.Switched += Refresh;
        migration.Migrated += Refresh;
        migration.HoldChanged += () =>
        {
            Weigh();
            OnPropertyChanged(nameof(MigrateHint));
        };
        migration.Wanted += () =>
        {
            Refresh();
            Open();
        };
    }

    // ---- the row ------------------------------------------------------------------------------

    /// <summary>Whether any older season has a vault: the row shows only then.</summary>
    [ObservableProperty] private bool _hasSource;

    [ObservableProperty] private string _rowTitle = "";

    [ObservableProperty] private string _rowSummary = "";

    /// <summary>The seasons still to migrate, oldest first; empty once all are.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    [NotifyPropertyChangedFor(nameof(RowHint))]
    private IReadOnlyList<string> _pending = [];

    /// <summary>The characters of the save folder the season before left with its items or its gold.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    [NotifyPropertyChangedFor(nameof(RowHint))]
    private IReadOnlyList<string> _characters = [];

    public bool HasPending => Pending.Count > 0 || Characters.Count > 0;

    public string RowHint => HasPending ? L("Migrate") : L("Nothing to migrate");

    /// <summary>The row, as the vault opens or changes: which older seasons there are, and whether any wait.</summary>
    public void Refresh()
    {
        var older = _migration.Older();
        Pending = _migration.Pending();
        Characters = _migration.WaitingCharacters();
        HasSource = older.Count > 0 || Characters.Count > 0;

        // The seasons waiting, or once none does, the last one migrated; then the characters still waiting.
        string from = Pending.Count > 0 ? SeasonMigrationService.Named(Pending)
            : older.Count > 0 ? older[^1] : D2SLib.Model.TXT.StatEncoding.PreviousSeason;
        RowTitle = HasSource ? L($"Migrate from {from}") : "";
        RowSummary = Pending.Count > 0 ? L("Not migrated yet")
            : Characters.Count > 0 ? Plural(Characters.Count, "1 character waiting", $"{Characters.Count} characters waiting")
            : L("Migrated");
    }

    // ---- the panel ----------------------------------------------------------------------------

    [ObservableProperty] private bool _isPanelPresent;

    [ObservableProperty] private bool _isPanelOpen;

    partial void OnIsPanelOpenChanged(bool value) =>
        Presence.Keep(value, () => IsPanelOpen, on => IsPanelPresent = on);

    [ObservableProperty] private string _panelTitle = "";

    /// <summary>Reading or writing: the answers wait, and the line says which.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMigrate))]
    [NotifyPropertyChangedFor(nameof(MigrateHint))]
    private bool _isBusy;

    [ObservableProperty] private string _busyText = "";

    public ObservableCollection<MigrationModeRow> Modes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMigrate))]
    [NotifyPropertyChangedFor(nameof(MigrateHint))]
    private bool _anyToMigrate;

    public bool CanMigrate => AnyToMigrate && !IsBusy;

    /// <summary>The button's tooltip, with why where it is dimmed.</summary>
    public string MigrateHint => CanMigrate ? L("Migrate")
        : IsBusy ? L("Migrate") + Environment.NewLine + L("Migrating.")
        : _migration.CharactersHeld is { } held && Modes.Any(row => row.Character is not null && row.CanChoose && row.IsChosen) ? L("Migrate") + Environment.NewLine + held
        : L("Migrate") + Environment.NewLine + L("Nothing to migrate.");

    [RelayCommand]
    private void OpenPanel()
    {
        _sfx?.Method_ButtonSFX_Press();
        Open();
    }

    private void Open()
    {
        if (!HasPending)
            return;

        string seasons = Pending.Count > 0 ? SeasonMigrationService.Named(Pending) : D2SLib.Model.TXT.StatEncoding.PreviousSeason;
        PanelTitle = L($"Migrate {seasons} to {_migration.To}");
        Modes.Clear();
        AnyToMigrate = false;
        IsPanelOpen = true;
        Busy(L($"Reading {seasons}"), () => Show(_migration.Preview(), _migration.PreviewCharacters(), preview: true));
    }

    [RelayCommand]
    private void ClosePanel()
    {
        if (!IsBusy)
            IsPanelOpen = false;
    }

    [RelayCommand]
    private void Migrate()
    {
        if (!CanMigrate)
            return;

        _sfx?.Method_ButtonSFX_Press();
        string seasons = Pending.Count > 0 ? SeasonMigrationService.Named(Pending) : D2SLib.Model.TXT.StatEncoding.PreviousSeason;
        Busy(L($"Migrating {seasons}"), () =>
        {
            // The ticked, and with them the vaults that cannot be migrated: tried again, so the strip says which stayed.
            var vaults = Modes.Where(row => row.Character is null && (row.IsRefused || row.IsChosen)).ToList();
            var characters = Modes.Where(row => row.Character is not null && row.CanChoose && row.IsChosen).Select(row => row.Character!).ToList();
            var results = vaults.Count > 0 ? _migration.Migrate([.. vaults.Select(row => (row.Season, row.Hardcore, row.Title))]) : [];
            var moved = characters.Count > 0 ? _migration.MigrateCharacters(characters) : [];
            if (results.Any(result => result.Report is not null) || moved.Any(result => result.Report is not null))
            {
                IsPanelOpen = false;
                _session.Status = string.Join(" ", new[] { results.Count > 0 ? Outcome(results) : "", CharactersOutcome(moved) }
                                                       .Where(said => said.Length > 0));
            }
            else
            {
                Show(results, moved, preview: false);
            }
        });
    }

    /// <summary>
    /// Runs <paramref name="work"/> once the busy line has drawn: the migration swaps the tables every page
    /// reads, so it runs on this thread, not beside it.
    /// </summary>
    private void Busy(string text, Action work)
    {
        BusyText = text;
        IsBusy = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                work();
            }
            finally
            {
                IsBusy = false;
            }
        }, DispatcherPriority.Background);
    }

    private void Show(IReadOnlyList<SeasonMigrationService.Result> results, IReadOnlyList<SeasonMigrationService.CharacterResult> characters, bool preview)
    {
        Modes.Clear();

        void Add(MigrationModeRow row)
        {
            row.Chosen = Weigh;
            Modes.Add(row);
        }

        foreach (var result in results)
        {
            string from = $"{result.Season} {(result.Hardcore ? L("Hardcore") : L("Softcore"))}";
            if (result.Report is not { } report)
            {
                Add(new MigrationModeRow(result.Season, result.Hardcore, result.Vault, from, [], null, result.Refused));
                continue;
            }

            var lines = new List<string>
            {
                Summary(report),
                L($"Gold: {report.Gold:N0}"),
            };
            if (report.Count(SeasonMigration.Outcome.Failed) is > 0 and int failed)
                lines.Add(L($"{failed} unreadable, staying in {report.From}"));

            // How values come across, per season: one with a snapshot keeps their place in the range.
            string note = report.Rescaled
                ? L("Values keep their place in each range")
                : L($"Values kept as they are: no copy of {report.From}'s tables");
            Add(new MigrationModeRow(result.Season, result.Hardcore, result.Vault, from, lines, note, null));
        }

        // A character: its items carried as a vault's are, its gold, and its base life and mana where the class table moved.
        foreach (var each in characters.Where(each => each.Path.Length > 0 || each.Refused is not null))
        {
            string season = each.Report is { } done ? done.From : D2SLib.Model.TXT.StatEncoding.PreviousSeason;
            string kind = each.Hardcore ? L($"{season} Hardcore character") : L($"{season} Softcore character");
            if (each.Report is not { } report)
            {
                Add(new MigrationModeRow(season, each.Hardcore, each.Name, kind, [], null, each.Refused, each.Path));
                continue;
            }

            var lines = new List<string> { CharacterSummary(report) };
            lines.Add(report.GoldLeft > 0
                ? L($"Gold: {report.GoldMoved:N0} to the shared stash, {report.GoldLeft:N0} stays")
                : L($"Gold: {report.GoldMoved:N0} to the shared stash"));
            if (report.Now != report.Was)
                lines.Add(L($"Base life {report.Was.Life} -> {report.Now.Life}, mana {report.Was.Mana} -> {report.Now.Mana}"));
            Add(new MigrationModeRow(season, each.Hardcore, report.Name, kind, lines, L("Points stay where they are"), null, each.Path));
        }

        _previewed = preview;
        Weigh();
    }

    /// <summary>The cards are a dry run's, to migrate from: not a migration's own, which only says what went wrong.</summary>
    private bool _previewed;

    /// <summary>MIGRATE is there while a vault that can be migrated is ticked, or a character while the patch guard holds nothing.</summary>
    private void Weigh() =>
        AnyToMigrate = _previewed && Modes.Any(row => row.CanChoose && row.IsChosen && (row.Character is null || _migration.CharactersHeld is null));

    private static string Outcome(IReadOnlyList<SeasonMigrationService.Result> results)
    {
        var done = results.Where(result => result.Report is not null).ToList();
        var reports = done.Select(result => result.Report!).ToList();
        int items = reports.Sum(report => report.Items.Count - report.Count(SeasonMigration.Outcome.Failed));
        int check = reports.Sum(report => report.Count(SeasonMigration.Outcome.Review));
        string from = SeasonMigrationService.Named([.. done.Select(result => result.Season).Distinct()]);
        string said = Plural(items, $"Migrated 1 item from {from}.", $"Migrated {items} items from {from}.");
        if (check > 0)
            said += " " + L($"{check} to check, in \"{SeasonMigration.ReviewFolder}\".");
        // A season refused stays waiting: its row says so, and the panel says why.
        if (results.Where(result => result.Refused is not null).Select(result => result.Season).Distinct().ToList() is { Count: > 0 } refused)
            said += " " + L($"{SeasonMigrationService.Named(refused)} not migrated.");

        return said;
    }

    /// <summary>"Migrated 2 characters." - and those that were not, by name.</summary>
    private static string CharactersOutcome(IReadOnlyList<SeasonMigrationService.CharacterResult> results)
    {
        var done = results.Where(result => result.Report is not null).ToList();
        string said = done.Count == 0 ? "" : Plural(done.Count, $"Migrated {done[0].Name}.", $"Migrated {done.Count} characters.");
        foreach (var refused in results.Where(result => result.Refused is not null))
        {
            string why = refused.Name.Length > 0 ? L($"{refused.Name}: {refused.Refused}") : refused.Refused!;
            said = said.Length > 0 ? said + " " + why : why;
        }

        return said;
    }

    /// <summary>"80 items: 76 as they are, 4 adjusted, 0 to check" - a character's.</summary>
    private static string CharacterSummary(CharacterMigration.Report report)
    {
        int all = report.Items.Count, clean = report.Count(SeasonMigration.Outcome.Clean);
        int adjusted = report.Count(SeasonMigration.Outcome.Adjusted), review = report.Count(SeasonMigration.Outcome.Review);
        return Plural(all, $"1 item: {clean} as they are, {adjusted} adjusted, {review} to check",
                      $"{all} items: {clean} as they are, {adjusted} adjusted, {review} to check");
    }

    /// <summary>"12 items: 10 as they are, 1 adjusted, 1 to check".</summary>
    private static string Summary(SeasonMigration.Report report)
    {
        int all = report.Items.Count, clean = report.Count(SeasonMigration.Outcome.Clean);
        int adjusted = report.Count(SeasonMigration.Outcome.Adjusted), review = report.Count(SeasonMigration.Outcome.Review);
        return Plural(all, $"1 item: {clean} as they are, {adjusted} adjusted, {review} to check",
                      $"{all} items: {clean} as they are, {adjusted} adjusted, {review} to check");
    }
}
