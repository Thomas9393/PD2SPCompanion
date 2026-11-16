using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// What a save did comes back from it (<see cref="SaveReport"/>): the copy it took of the character, for its asker to
/// caption, and what it did to the vault, for the session's history. Saves of the real files, on the pad.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SaveReportTests(Program program)
{
    private const string Stash = "Personal Stash", Ring = "Steel Ring of Skill";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private VP_ViewModel_Vault_L Vault => Main.RightVault!;
    private SaveSet Saves => program.Get<SaveSet>();

    [CorpusFact]
    public void The_copy_a_save_took_is_on_its_report_and_on_no_later_ones()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes(Stash);
            program.GivePlain("hp1", 0);

            Assert.Null(Saves.Commit(out var first));
            Assert.NotNull(first.Backup);
            Assert.True(File.Exists(first.Backup), "the report names a copy that is not there");

            // Nothing more to write: no copy, and none left over from the save before.
            Assert.Null(Saves.Commit(out var second));
            Assert.Null(second.Backup);
        });
    }

    /// <summary>
    /// The Save button's save has no step of its own. Undoing the next step takes the files back past it, so what it did to
    /// the vault goes back as well: otherwise the item is on the character and its row still in the vault.
    /// </summary>
    [CorpusFact]
    public void A_save_with_no_step_of_its_own_goes_back_with_the_next_step()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes(Stash);
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            Vault.Show();
            int stored = Inventory.InventoryPad.InVault(program), held = Session.ItemCount;

            Assert.True(Page.BeginCarry(program.Tile(Ring, Stash)));
            Vault.DropFromInventory(null);
            Assert.Null(Saves.Commit(out var report));

            Assert.Single(report.Added);
            Assert.Equal(stored + 1, Inventory.InventoryPad.InVault(program));
            Assert.Equal(held - 1, Session.ItemCount);

            var history = program.Get<SessionHistory>();
            history.Record("Next step");
            Assert.Null(history.Undo());

            Assert.Equal(stored, Inventory.InventoryPad.InVault(program));
            Assert.Equal(held, Session.ItemCount);
        });
    }
}
