using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A whole vault deleted from Settings asks its question, as a character's deletion does (a
/// thing is the bin clicked twice; a whole file, or many at once, is asked about). It was the bin clicked twice.
/// Answered no, the vault is there still; yes, it is gone.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultDeleteAsksTests(Program program)
{
    private VaultManagerViewModel Manager => program.Get<VP_ViewModel_Settings_R>().Vaults!;

    private ConfirmService Confirm => program.Get<ConfirmService>();

    [CorpusFact]
    public void Deleting_a_vault_asks_no_keeps_it_yes_deletes_it()
    {
        if (!program.Ready(changes: true))
            return;

        const string name = "Asked about";
        try
        {
            program.On(() =>
            {
                Manager.StartNewCommand.Execute(null);
                Manager.NewName = name;
                Manager.CommitNew();
                var row = Manager.Rows.Single(each => each.Name == name);
                Assert.False(row.IsOpen, "a new vault is not the open one");

                Manager.DeleteVaultCommand.Execute(row);
                Assert.True(Confirm.IsAsking, "the bin asks its question at once");
                Assert.Equal("Delete vault", Confirm.Title);
                Assert.Equal("DELETE", Confirm.ConfirmLabel);

                Confirm.RejectCommand.Execute(null);
                Assert.Contains(Manager.Rows, each => each.Name == name);

                Manager.DeleteVaultCommand.Execute(Manager.Rows.Single(each => each.Name == name));
                Confirm.AcceptCommand.Execute(null);
                Assert.DoesNotContain(Manager.Rows, each => each.Name == name);
                Assert.Equal($"Deleted {name}. Copy in vault backups.", program.Main.Session.Status);
            });
        }
        finally
        {
            program.On(() =>
            {
                if (Confirm.IsAsking)
                    Confirm.RejectCommand.Execute(null);
            });
        }
    }
}
