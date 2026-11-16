using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// Clean the open vault: the Settings page's Clean row opens a check list - items and
/// folders, trade records, gold - with nothing ticked and CLEAN waiting for a tick; what is ticked is
/// deleted and the rest kept, a copy of the vault left in Backups first.
/// <para>
/// What the list decides, asked of it as the view's buttons ask and read back from the vault's file. That
/// the row's bin, the ticks and CLEAN are there to click is the UI case's to show (units/vault-clear.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultClearTests(Program program)
{
    private VaultManagerViewModel Manager => program.Get<VP_ViewModel_Settings_R>().Vaults!;

    private PD2SPCompanion.Core.Collection.Vault Store => program.Get<VaultService>().Store!;

    private static int Copies() => PD2SPCompanion.Core.Collection.Vault.Copies().Count(copy => copy.Why == "cleared");

    [CorpusFact]
    public void The_Clean_rows_bin_opens_the_check_list_named_for_the_vault_and_CLEAN_waits_for_a_tick()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            try
            {
                Manager.OpenClearCommand.Execute(null);

                Assert.True(Manager.IsClearOpen);
                Assert.Equal("Clean S14 Main", Manager.ClearTitle);
                Assert.False(Manager.ClearItems || Manager.ClearTrades || Manager.ClearGold, "nothing is ticked to begin with");
                Assert.False(Manager.ConfirmClearCommand.CanExecute(null), "nothing ticked: CLEAN waits");

                Manager.ClearTrades = true;
                Assert.True(Manager.ConfirmClearCommand.CanExecute(null), "ticked: CLEAN is there");

                Manager.ClearTrades = false;
                Assert.False(Manager.ConfirmClearCommand.CanExecute(null));
            }
            finally
            {
                Manager.CloseClearCommand.Execute(null);
            }
        });
    }

    [CorpusFact]
    public void What_is_ticked_is_deleted_the_rest_kept_and_a_copy_of_the_vault_left_in_Backups()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            // A trade of its own: a season's vault begins with none - PD2 clears trades at a new season, and the migration
            // carries none.
            Store.RecordTrades([new PD2SPCompanion.Core.Collection.TradeRecord(DateTimeOffset.Now, Program.Open, "bought", "listing", null, null, 4242,
                                                "Shako", "1 Ist", "1 Ist", "Legit")]);
            int items = Store.Count(), trades = Store.Trades().Count;
            Assert.True(items > 0 && trades > 0, $"the pad's vault holds items and trades ({items}, {trades})");
            int copies = Copies();

            Manager.OpenClearCommand.Execute(null);
            Manager.ClearTrades = true;
            Manager.ConfirmClearCommand.Execute(null);

            Assert.Equal("Cleaned. Copy in vault backups.", program.Main.Session.Status);
            Assert.False(Manager.IsClearOpen);
            Assert.Contains(" · 0 trades · ", Manager.Summary);

            Assert.Empty(Store.Trades());
            Assert.Equal(items, Store.Count());
            Assert.Equal(copies + 1, Copies());
        });
    }

    /// <summary>
    /// Cleaning a hardcore vault failed, "Not cleaned: SQLite Error 19:
    /// 'UNIQUE constraint failed: index 'folder_sibling''". A vault sorted storing has filled holds folders of
    /// one name under different parents, and the clean made them all top folders for a moment before deleting
    /// them. No test here had ticked the items.
    /// </summary>
    [CorpusFact]
    public void Items_and_folders_ticked_are_deleted_whatever_the_folders_are_named()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            // The pad's vault has Charms/Small; a second Small under another parent, as sorted storing makes them.
            Store.FolderFor(["Jewels", "Small"]);
            Store.FolderFor(["Rings", "Rare"]);
            Store.FolderFor(["Amulets", "Rare"]);
            Assert.True(Store.Count() > 0 && Store.Folders().GroupBy(folder => folder.Name).Any(same => same.Count() > 1));
            long gold = Store.GoldBalance();

            Manager.OpenClearCommand.Execute(null);
            Manager.ClearItems = true;
            Manager.ConfirmClearCommand.Execute(null);

            Assert.Equal("Cleaned. Copy in vault backups.", program.Main.Session.Status);
            Assert.False(Manager.IsClearOpen);
            Assert.Equal(0, Store.Count());
            Assert.Empty(Store.Folders());
            Assert.Equal(gold, Store.GoldBalance());
        });
    }
}
