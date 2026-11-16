using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A find of the vault's gone to from the home page, no character open, once blocked: it
/// loads the highlighted character card in the selection
/// list, or falls back to the first character: the vault's finds are listed with the rest; one chosen, a character
/// is loaded for it - the Items page is a character's - and the vault shown at the find, the item selected.
/// <para>
/// What the search box and the pages decide. That the row is there to click on the home page, and the vault
/// then drawn with the item in sight, is the UI case's to show (units/quick-vault-home.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class QuickVaultHomeTests(Program program)
{
    private MainViewModel Main => program.Main;
    private QuickSearchViewModel Bar => Main.QuickSearch!;

    [CorpusFact]
    public void With_no_character_open_a_vault_find_chosen_loads_a_character_and_shows_the_vault_at_the_find()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() => Main.Home.CloseCommand.Execute(null));
            Assert.True(program.Until(() => !Main.Session.IsLoaded), "the character was not closed");

            program.On(() => Bar.Text = "charm");
            Assert.True(program.Until(() => Bar.Rows.OfType<FoundItem>().Any(row => row.InVault), 8000), "no find of the vault's was listed");

            string name = program.On(() =>
            {
                Assert.Contains("Vault", Bar.Rows.OfType<FoundGroup>().Select(group => group.Name));

                var row = Bar.Rows.OfType<FoundItem>().First(found => found.InVault);
                Bar.ChooseCommand.Execute(row);
                return row.Name;
            });

            Assert.True(program.Until(() => Main.Session.IsLoaded && Main.IsLeftVault && Main.Vault!.Selected?.Name == name, 20000),
                        "no character was loaded and the find gone to");
            program.On(() =>
            {
                Assert.True(Main.Button_Inventory_isChecked, "the Items page is shown for the find");
                Assert.Equal("", Bar.Text);
                Assert.False(Bar.IsListOpen);
            });
        }
        finally
        {
            program.On(() =>
            {
                Bar.Text = "";
                if (Main.Session.IsLoaded)
                    VaultHands.Rest(program);
            });
        }
    }
}
