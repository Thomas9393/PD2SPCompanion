using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Season;

/// <summary>
/// The season migration as the program offers it:
/// with an older season's vault beside this season's, the notice asks once - MIGRATE opens Settings and the
/// migration panel with its dry run, START NEW answers it too - and the panel's MIGRATE writes it, says what
/// was migrated on the strip, leaves the Settings row saying Migrated, and opens the migrated vault. With two
/// older seasons never migrated the notice and the panel name both, each has its card, and the one whose
/// tables are in no snapshot is refused with the reason and stays waiting.
/// <para>
/// What the migration itself reads, fits and writes is the Core's tests'
/// (tests/PD2SPCompanion.Tests/Collection/SeasonMigrationTests): here, what the program does with it. The
/// older vaults are made as the command line tool's <c>pastvault</c> makes them (<see cref="PastVault"/>);
/// the notice is asked for as the start asks for it once the window is up (CheckAtStart). That the start
/// does ask, and that the notice and the panel are drawn and take their clicks, are the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SeasonMigrationTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private EditorSettings Settings => program.Get<EditorSettings>();
    private SeasonMigrationService Service => program.Get<SeasonMigrationService>();
    private SeasonMigrationViewModel Migration => program.Get<VP_ViewModel_Settings_R>().Migration!;

    private string Vaults => Path.Combine(program.Folder, "vtsmart");

    /// <summary>
    /// The installed season's item stamp, as opening one of its characters tells it. Ready() opens the corpus's
    /// character, but under a season's tables laid over the archives (PD2SPC_TABLES: the corpus is S13's, S14 is
    /// installed) no snapshot is kept to write it in (SeasonSnapshot.EnsureFor), and the migration
    /// refused with "item version unknown" - both tests failed on their dry run. Answers what to put back.
    /// </summary>
    private (string? Current, string? Previous) KnowTheStamp()
    {
        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        program.On(() =>
        {
            if (SeasonReading.CurrentStamp is null && Session.ItemEra is { Length: > 0 } era)
                SeasonReading.SetStamps(era, SeasonReading.PreviousStamp);
        });
        return kept;
    }

    /// <summary>The notice answered, the panel shut and the season's answer forgotten: as the fixture's settings began.</summary>
    private void Leave()
    {
        program.Until(() => !Migration.IsBusy, 60000);
        program.On(() =>
        {
            if (Main.Confirm.IsAsking)
                Main.Confirm.RejectCommand.Execute(null);
            Migration.IsPanelOpen = false;
            Settings.MigrationNoticeSeason = "";
        });
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void One_older_vault_the_notice_asks_once_MIGRATE_opens_the_panel_and_the_panels_MIGRATE_writes_it()
    {
        if (!program.Ready(changes: true) || !PastVault.Available)
            return;

        var stamps = KnowTheStamp();
        try
        {
            program.On(() =>
            {
                Assert.Equal("", Settings.MigrationNoticeSeason);
                Assert.Equal("S14", Service.To);

                // Nothing older: nothing asked, and no row.
                Service.CheckAtStart();
                Assert.False(Main.Confirm.IsAsking);

                Assert.Equal(40, PastVault.S13(program));

                Service.CheckAtStart();
                Assert.True(Main.Confirm.IsAsking);
                Assert.Equal("S14 found in game folder. Migrate S13 vault?", Main.Confirm.Message);
                Assert.Equal("MIGRATE", Main.Confirm.ConfirmLabel);
                Assert.Equal("START NEW", Main.Confirm.CancelLabel);

                // START NEW answers it for the season: not asked again, the panel not opened.
                Main.Confirm.RejectCommand.Execute(null);
                Assert.Equal("S14", Settings.MigrationNoticeSeason);
                Assert.False(Migration.IsPanelOpen);
                Service.CheckAtStart();
                Assert.False(Main.Confirm.IsAsking);

                // Asked afresh, and MIGRATE: Settings, with the migration panel and its dry run.
                Settings.MigrationNoticeSeason = "";
                Service.CheckAtStart();
                Main.Confirm.AcceptCommand.Execute(null);

                Assert.Equal("S14", Settings.MigrationNoticeSeason);
                Assert.IsType<VP_ViewModel_Settings_L>(Main.CurrentPage_L);
                Assert.True(Migration.IsPanelOpen);
                Assert.Equal("Migrate S13 to S14", Migration.PanelTitle);
                Assert.Equal("Migrate from S13", Migration.RowTitle);
                Assert.Equal("Not migrated yet", Migration.RowSummary);
            });

            Assert.True(program.Until(() => !Migration.IsBusy && Migration.Modes.Count == 1, 60000), "the dry run did not come");
            program.On(() =>
            {
                var card = Migration.Modes[0];
                Assert.Equal(PastVault.S13Name, card.Title);
                Assert.Equal("S13 Softcore", card.From);
                Assert.StartsWith("40 items:", card.Lines[0]);
                Assert.Contains("Gold: 1,000", card.Lines);
                Assert.Equal("Values kept as they are: no copy of S13's tables", card.Note);
                Assert.True(card.IsChosen);
                Assert.True(Migration.CanMigrate);

                // Nothing written by a dry run.
                Assert.False(File.Exists(Path.Combine(Vaults, "S14", "Softcore", PastVault.S13Name + ".pd2lib")));

                Migration.MigrateCommand.Execute(null);
            });

            Assert.True(program.Until(() => !Migration.IsBusy && !Migration.IsPanelOpen, 60000), "the migration did not end");
            program.On(() =>
            {
                Assert.StartsWith("Migrated 40 items from S13.", Session.Status);
                Assert.Equal("Migrated", Migration.RowSummary);
                Assert.Equal("Migrate from S13", Migration.RowTitle);
                Assert.False(Migration.HasPending);

                // Into a vault of its own name, beside the season's own, and that one is open after.
                Assert.True(File.Exists(Path.Combine(Vaults, "S14", "Softcore", PastVault.S13Name + ".pd2lib")), "S13 Second is not a vault of S14's");
                Assert.Equal(PastVault.S13Name, program.Get<VaultService>().VaultName);
                Assert.Equal(40, program.Get<VaultService>().Store!.Count());
                Assert.Equal("S14", Settings.MigrationNoticeSeason);

                // And the notice has nothing left to ask.
                Settings.MigrationNoticeSeason = "";
                Service.CheckAtStart();
                Assert.False(Main.Confirm.IsAsking);
            });
        }
        finally
        {
            Leave();
            SeasonReading.SetStamps(stamps.Current, stamps.Previous);
        }
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Two_older_seasons_both_are_named_each_has_its_card_and_the_one_with_no_snapshot_stays_waiting()
    {
        if (!program.Ready(changes: true) || !PastVault.Available)
            return;

        var stamps = KnowTheStamp();
        try
        {
            program.On(() =>
            {
                Assert.Equal(20, PastVault.S7(program));
                Assert.Equal(40, PastVault.S13(program));

                Service.CheckAtStart();
                Assert.Equal("S14 found in game folder. Migrate S7 and S13 vaults?", Main.Confirm.Message);
                Main.Confirm.AcceptCommand.Execute(null);
                Assert.Equal("Migrate S7 and S13 to S14", Migration.PanelTitle);
            });

            Assert.True(program.Until(() => !Migration.IsBusy && Migration.Modes.Count == 2, 60000), "the dry run did not come");
            program.On(() =>
            {
                var s7 = Migration.Modes.Single(card => card.From == "S7 Softcore");
                Assert.True(s7.IsRefused);
                Assert.StartsWith("No snapshot of S7", s7.Refused);
                Assert.False(s7.CanChoose);
                Assert.False(s7.IsChosen);

                var s13 = Migration.Modes.Single(card => card.From == "S13 Softcore");
                Assert.False(s13.IsRefused);
                Assert.StartsWith("40 items:", s13.Lines[0]);
                Assert.True(Migration.CanMigrate);

                Migration.MigrateCommand.Execute(null);
            });

            Assert.True(program.Until(() => !Migration.IsBusy && !Migration.IsPanelOpen, 60000), "the migration did not end");
            program.On(() =>
            {
                Assert.StartsWith("Migrated 40 items from S13.", Session.Status);
                Assert.EndsWith("S7 not migrated.", Session.Status);

                // S7 still waits: the row names it.
                Assert.Equal("Migrate from S7", Migration.RowTitle);
                Assert.Equal("Not migrated yet", Migration.RowSummary);
                Assert.Equal(["S7"], Migration.Pending);
            });
        }
        finally
        {
            Leave();
            SeasonReading.SetStamps(stamps.Current, stamps.Previous);
        }
    }
}
