using PD2SPCompanion.Core.Characters;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Gold between a purse and the vault (GoldViewModel's arrows), and the vault's own stepper: how much moves past each
/// limit and the words that say so, that the vault's side waits for the save and is then the same amount the purse lost,
/// and that no gold moves while the game runs. Asked as the arrows ask, and read back from the saved file and the vault.
/// That the arrows are drawn and read Shift is a window's to show.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultGoldTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private GoldViewModel Carried => program.Section().CarriedGold!;
    private GoldViewModel Shared => program.Section().SharedGold!;
    private VaultMoves Moves => program.Get<VaultMoves>();
    private VP_ViewModel_Vault_L VaultPage => program.Main.Vault!;
    private long InVaultFile => program.Get<VaultService>().Store!.GoldBalance();

    /// <summary>The carried gold as the save's file has it; the parser left as the open character's.</summary>
    private int CarriedInFile()
    {
        int gold = D2SLib.Core.ReadD2S(File.ReadAllBytes(program.SaveOf(Program.Open))).Attributes.Stats.GetValueOrDefault(AttributeEditor.Carried);
        Session.UseMyLayout();
        return gold;
    }

    private int SharedInFile() =>
        (int)SharedStash.Read(Path.Combine(program.Folder, "saves", SharedStashSession.SoftcoreFile)).Gold;

    /// <summary>The purse at an amount and the vault at another, nothing waiting for a save.</summary>
    private void Start(GoldViewModel purse, int inPurse, long inVault)
    {
        purse.Value = inPurse.ToString();
        Assert.Null(Moves.EditGold(inVault - Moves.GoldBalance));
        program.Saved();
        Assert.Equal(inVault, InVaultFile);
    }

    [CorpusFact]
    public void The_arrow_moves_the_amount_and_the_vault_has_it_only_once_saved()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var purse = Carried;
            Assert.True(purse.HasVault);
            Start(purse, 250_000, 40_000);
            purse.MoveAmount = "100000";

            purse.DepositCommand.Execute(null);
            Assert.Equal(150_000, purse.Held);
            Assert.Equal(140_000, purse.VaultBalance);
            Assert.Equal("140,000", purse.VaultAmount);
            Assert.Equal(40_000, InVaultFile);

            Assert.True(program.Saved(), "the move was not saved");
            Assert.Equal(140_000, InVaultFile);
            Assert.Equal(150_000, CarriedInFile());

            purse.MoveAmount = "30000";
            purse.WithdrawGoldCommand.Execute(null);
            Assert.Equal(180_000, purse.Held);
            Assert.Equal(110_000, purse.VaultBalance);
            Assert.Equal(140_000, InVaultFile);

            Assert.True(program.Saved(), "the move was not saved");
            Assert.Equal(110_000, InVaultFile);
            Assert.Equal(180_000, CarriedInFile());
        });
    }

    [CorpusFact]
    public void Past_each_limit_what_can_move_moves_and_the_words_say_which_stopped_it()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var purse = Carried;
            int most = purse.Maximum;

            // More than the purse holds.
            Start(purse, 50_000, 0);
            purse.MoveAmount = "80000";
            purse.DepositCommand.Execute(null);
            Assert.Equal("Purse held 50,000. Moved that.", Session.Status);
            Assert.Equal(0, purse.Held);
            Assert.Equal(50_000, purse.VaultBalance);
            Assert.False(purse.CanDeposit);

            // An empty purse: nothing moves.
            purse.DepositCommand.Execute(null);
            Assert.Equal("Purse is empty.", Session.Status);
            Assert.Equal(50_000, purse.VaultBalance);

            // More than the vault holds.
            purse.WithdrawGoldCommand.Execute(null);
            Assert.Equal("Vault held 50,000. Moved that.", Session.Status);
            Assert.Equal(50_000, purse.Held);
            Assert.Equal(0, purse.VaultBalance);
            Assert.False(purse.CanWithdrawGold);

            // An empty vault.
            purse.WithdrawGoldCommand.Execute(null);
            Assert.Equal("Vault is empty.", Session.Status);
            Assert.Equal(50_000, purse.Held);

            // More than the purse has room for.
            Start(purse, most - 10_000, 500_000);
            purse.MoveAmount = "80000";
            purse.WithdrawGoldCommand.Execute(null);
            Assert.Equal("Purse took 10,000, now full.", Session.Status);
            Assert.Equal(most, purse.Held);
            Assert.Equal(490_000, purse.VaultBalance);
            Assert.False(purse.CanWithdrawGold);

            // A full purse.
            purse.WithdrawGoldCommand.Execute(null);
            Assert.Equal("Purse is full.", Session.Status);
            Assert.Equal(490_000, purse.VaultBalance);

            // Nothing typed, or nought, or not a number.
            foreach (string typed in new[] { "0", "", "lots" })
            {
                Session.Status = "";
                purse.MoveAmount = typed;
                Assert.Equal(0, purse.Moved);
                purse.DepositCommand.Execute(null);
                Assert.Equal("Require amount.", Session.Status);
                Session.Status = "";
                purse.WithdrawGoldCommand.Execute(null);
                Assert.Equal("Require amount.", Session.Status);
            }

            Assert.Equal(most, purse.Held);
            Assert.Equal(490_000, purse.VaultBalance);
        });
    }

    [CorpusFact]
    public void Shift_on_the_arrows_moves_all_of_the_purse_or_fills_it()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var purse = Carried;
            int most = purse.Maximum;
            Start(purse, 70_000, 1_000);

            purse.DepositAll();
            Assert.Equal(0, purse.Held);
            Assert.Equal(71_000, purse.VaultBalance);

            purse.WithdrawFill();
            Assert.Equal(71_000, purse.Held);
            Assert.Equal(0, purse.VaultBalance);

            // Filled, where the vault holds more than there is room for: the rest stays.
            Start(purse, most - 5_000, 20_000);
            purse.WithdrawFill();
            Assert.Equal(most, purse.Held);
            Assert.Equal(15_000, purse.VaultBalance);

            Assert.True(program.Saved(), "the move was not saved");
            Assert.Equal(15_000, InVaultFile);
            Assert.Equal(most, CarriedInFile());
        });
    }

    [CorpusFact]
    public void The_shared_purse_moves_with_the_vault_as_the_carried_one_does()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var purse = Shared;
            Assert.True(purse.HasVault);
            Start(purse, 300_000, 0);
            purse.MoveAmount = "120000";

            purse.DepositCommand.Execute(null);
            Assert.Equal(180_000, purse.Held);
            Assert.True(program.Saved(), "the move was not saved");
            Assert.Equal(120_000, InVaultFile);
            Assert.Equal(180_000, SharedInFile());
        });
    }

    [CorpusFact]
    public void While_the_game_runs_no_gold_moves()
    {
        if (!program.Ready(changes: true))
            return;

        var purse = program.On(() =>
        {
            Start(Carried, 90_000, 60_000);
            return Carried;
        });

        File.WriteAllText(program.GameFlag, "running");
        try
        {
            Assert.True(program.Until(() => program.Main.Sync?.IsGameRunning == true, 10000), "the game was not seen");
            program.On(() =>
            {
                long vault = InVaultFile;
                long balance = purse.VaultBalance;

                Assert.False(purse.CanDeposit);
                Assert.False(purse.CanWithdrawGold);
                Assert.Equal("Require game closed to move gold.", purse.DepositHint);

                purse.MoveAmount = "1000";
                purse.DepositCommand.Execute(null);
                Assert.Equal("Require game closed to move gold.", Session.Status);
                purse.WithdrawGoldCommand.Execute(null);
                Assert.Equal("Require game closed to move gold.", Session.Status);

                Assert.Equal(balance, purse.VaultBalance);
                Assert.Equal(vault, InVaultFile);
            });
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => program.Main.Sync?.IsGameRunning != true, 10000);
        }
    }

    [CorpusFact]
    public void The_vault_pages_stepper_writes_the_vault_at_once_and_never_below_nothing()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            Start(Carried, Carried.Held, 0);
            int purse = Carried.Held;
            var page = VaultPage;

            Assert.Equal("0", page.GoldTyped);
            Assert.False(page.CanLowerGold);

            // Stepped: no save asked for, the file has it now.
            page.RaiseGoldCommand.Execute(null);
            Assert.Equal(VP_ViewModel_Vault_L.GoldStep, InVaultFile);
            Assert.Equal($"{VP_ViewModel_Vault_L.GoldStep}", page.GoldTyped);
            Assert.True(page.CanLowerGold);

            page.StepGold(up: true, all: false);
            Assert.Equal(VP_ViewModel_Vault_L.GoldStep + VP_ViewModel_Vault_L.GoldLeap, InVaultFile);

            page.LowerGoldCommand.Execute(null);
            Assert.Equal(VP_ViewModel_Vault_L.GoldLeap, InVaultFile);

            // Typed, and below nothing is nothing.
            page.GoldTyped = "75000";
            Assert.Equal(75_000, InVaultFile);
            page.GoldTyped = "not a number";
            Assert.Equal(75_000, InVaultFile);
            page.GoldTyped = "-5";
            Assert.Equal(0, InVaultFile);

            page.GoldTyped = "75000";
            page.StepGold(up: false, all: true);
            Assert.Equal(0, InVaultFile);
            page.LowerGoldCommand.Execute(null);
            Assert.Equal(0, InVaultFile);

            // Made or lost, not moved: no purse follows.
            Assert.Equal(purse, Carried.Held);
        });
    }
}
