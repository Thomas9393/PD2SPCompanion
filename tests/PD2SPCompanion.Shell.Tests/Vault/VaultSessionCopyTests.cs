using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Edit;

using Store = PD2SPCompanion.Core.Collection.Vault;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The vault's copy a session, one restore per session:
/// closed after a session that changed it, the
/// vault is copied as it was left; a session that changed nothing adds no copy; opened, it is copied only
/// where its file is newer than its newest copy. Its copies are held to a number of their own.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultSessionCopyTests(Program program)
{
    private VaultService Vaults => program.Get<VaultService>();

    private static int Copies() => Store.Copies().Count(copy => copy.Why == "session");

    [CorpusFact]
    public void Closed_after_a_change_the_vault_is_copied_and_a_session_that_changed_nothing_adds_none()
    {
        // The pad's vault is closed and opened again, and a folder made in it: put back after.
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            Assert.NotNull(Vaults.Store);

            // Whatever the opening found is settled first: closed and opened once, nothing changed between.
            Vaults.Close();
            Assert.NotNull(Vaults.Store);
            int before = Copies();

            // A session that changes nothing: no copy as it is closed, none as it is opened again.
            Vaults.Close();
            Assert.Equal(before, Copies());
            Assert.NotNull(Vaults.Store);
            Assert.Equal(before, Copies());

            // A session that changed it: one copy as it is closed, and the opening after adds none - the file
            // is no newer than that copy.
            Vaults.Store!.CreateFolder(null, "Session copy test");
            Vaults.Close();
            Assert.Equal(before + 1, Copies());
            Assert.NotNull(Vaults.Store);
            Assert.Equal(before + 1, Copies());

            // The copy is the vault as it was left: the folder is in it.
            string newest = Store.Copies().First(copy => copy.Why == "session").Path;
            using (var copy = Store.Open(newest, Vaults.Season ?? "S13", VaultService.StatIdBits))
                Assert.Contains(copy.Folders(), folder => folder.Name == "Session copy test");
        });
    }

    [CorpusFact]
    public void The_vaults_copies_have_a_number_of_their_own()
    {
        if (!program.Ready())
            return;

        var settings = program.Get<PD2SPCompanion.Shell.Services.Settings.EditorSettings>();
        program.On(() =>
        {
            int characters = settings.BackupsToKeep, vault = settings.VaultBackupsToKeep;
            try
            {
                settings.VaultBackupsToKeep = 5;
                Assert.Equal(5, Store.CopiesToKeep);
                Assert.Equal(characters, PD2SPCompanion.Core.Saves.SaveWriter.BackupsToKeep);

                settings.BackupsToKeep = 15;
                Assert.Equal(5, Store.CopiesToKeep);
            }
            finally
            {
                settings.BackupsToKeep = characters;
                settings.VaultBackupsToKeep = vault;
            }
        });
    }
}
