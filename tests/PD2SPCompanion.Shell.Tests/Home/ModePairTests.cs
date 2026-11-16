using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Home;

/// <summary>
/// Softcore and hardcore are a pair of buttons wherever they are chosen (the UI language's rule C):
/// Settings' Vault card, whose pair opens the other mode's vault and says so - live only with no
/// character open, whose mode the vault follows - and the character creation's Mode.
/// <para>
/// What the pair's commands do. That the two are drawn side by side, softcore first and of one width in both
/// places, is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ModePairTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VaultManagerViewModel Vaults => program.Get<VP_ViewModel_Settings_R>().Vaults!;

    [CorpusFact]
    public void With_no_character_open_Settings_pair_opens_the_other_modes_vault_and_back()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            program.On(() => Main.Home.CloseCommand.Execute(null));
            Assert.True(program.Until(() => !Session.IsLoaded));

            program.On(() =>
            {
                Assert.True(Vaults.CanChangeMode);
                Assert.Null(Vaults.ModeHint);
                Assert.False(Vaults.IsHardcore);

                // Extra's pair is told too: its page is kept, and lit Softcore over Hardcore's numbers.
                var extra = program.Get<VP_ViewModel_Extra_R>();
                var told = new List<string?>();
                void Told(object? _, System.ComponentModel.PropertyChangedEventArgs changed) => told.Add(changed.PropertyName);
                extra.PropertyChanged += Told;

                Vaults.ChooseModeCommand.Execute("1");
                extra.PropertyChanged -= Told;
                Assert.Contains(nameof(VP_ViewModel_Extra_R.IsHardcore), told);
                Assert.True(extra.IsHardcore);
                Assert.Equal("Opened hardcore vault.", Session.Status);
                Assert.True(Vaults.IsHardcore);
                Assert.True(program.Get<VaultService>().IsHardcore);

                Vaults.ChooseModeCommand.Execute("0");
                Assert.Equal("Opened softcore vault.", Session.Status);
                Assert.False(Vaults.IsHardcore);
                Assert.False(program.Get<VaultService>().IsHardcore);
            });
        }
        finally
        {
            // The hardcore vault opened once: the settings kept its name, and the mode it was left in.
            program.On(() =>
            {
                program.Get<VaultService>().UseMode(false);
                program.Get<EditorSettings>().VaultNameHardcore = "";
            });
        }
    }

    [CorpusFact]
    public void With_a_character_open_the_pair_is_off_and_says_whose_mode_the_vault_follows()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.False(Vaults.CanChangeMode);
            Assert.Equal("Follows open character.", Vaults.ModeHint);

            string said = Session.Status;
            Vaults.ChooseModeCommand.Execute("1");
            Assert.False(Vaults.IsHardcore);
            Assert.Equal(said, Session.Status);
        });
    }

    [CorpusFact]
    public void Character_creations_pair_chooses_the_mode()
    {
        if (!program.Ready())
            return;

        var creation = program.Get<VP_ViewModel_Experimental_R>().Creation;
        try
        {
            program.On(() =>
            {
                Assert.False(creation.IsHardcore);
                creation.ChooseModeCommand.Execute("1");
                Assert.True(creation.IsHardcore);
                creation.ChooseModeCommand.Execute("0");
                Assert.False(creation.IsHardcore);
            });
        }
        finally
        {
            program.On(() => creation.ModeIndex = 0);
        }
    }

    [CorpusFact]
    public void The_vaults_backups_are_listed_one_is_brought_back_by_the_import_and_a_bin_deletes_one_on_its_second_press()
    {
        if (!program.Ready(changes: true))
            return;

        var service = program.Get<PD2SPCompanion.Shell.Services.Vault.VaultService>();
        string? made = null;
        try
        {
            program.On(() =>
            {
                // A copy of the open vault, as one is taken before it is cleaned.
                string name = service.VaultName;
                made = service.Store!.BackUp("cleared");

                Vaults.OpenCopiesCommand.Execute(null);
                Assert.True(Vaults.IsCopiesOpen);
                var row = Vaults.Copies.First(copy => copy.Copy.Path == made);
                Assert.Equal(name, row.Name);
                Assert.StartsWith("Before cleaning · ", row.Summary);

                // Brought back: the import's panel, for a vault of the copy's own name - nothing written yet.
                Vaults.BringBackCommand.Execute(row);
                Assert.False(Vaults.IsCopiesOpen);
                Assert.True(Vaults.IsImportOpen);
                Assert.Equal($"Import {name}", Vaults.ImportTitle);

                // Back, a layer at a time: from the import to the backups' list, from there to the vaults' list.
                Assert.True(Vaults.ImportIsOfCopy);
                Vaults.BackToCopiesCommand.Execute(null);
                Assert.False(Vaults.IsImportOpen);
                Assert.True(Vaults.IsCopiesOpen);
                Vaults.BackToVaultsCommand.Execute(null);
                Assert.False(Vaults.IsCopiesOpen);
                Assert.True(Vaults.IsPickerOpen);
                Vaults.ClosePickerCommand.Execute(null);
                Assert.Equal(name, service.VaultName);

                // Its bin: armed by the first press, the copy gone at the second.
                Vaults.OpenCopiesCommand.Execute(null);
                row = Vaults.Copies.First(copy => copy.Copy.Path == made);
                Vaults.DeleteCopyCommand.Execute(row);
                Assert.True(row.IsArmed);
                Assert.True(File.Exists(made));

                Vaults.DeleteCopyCommand.Execute(row);
                Assert.False(File.Exists(made));
                Assert.DoesNotContain(Vaults.Copies, copy => copy.Copy.Path == made);
                Vaults.CloseCopiesCommand.Execute(null);
            });
        }
        finally
        {
            program.On(() =>
            {
                if (Vaults.IsImportOpen)
                    Vaults.CloseImportCommand.Execute(null);
                Vaults.CloseCopiesCommand.Execute(null);
            });
            if (made is not null && File.Exists(made))
                File.Delete(made);
        }
    }
}
