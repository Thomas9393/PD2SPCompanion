using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The vault's Move to folder picker is a tree that folds: a folder holding folders has
/// a chevron, its folders go away under it and come back, and the picker opens as it was left while the
/// program runs. Nothing is moved.
/// <para>
/// What the picker lists and keeps, asked of the page as the view asks. That the chevron is there to click is
/// the UI case's to show (units/vault-move-tree.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultMoveTreeTests(Program program)
{
    [CorpusFact]
    public void A_folder_holding_folders_folds_them_away_in_the_picker_and_the_picker_opens_again_as_it_was_left()
    {
        if (!program.Ready())
            return;

        var left = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(left, "Tome of Town Portal") is not null), "the vault's cards were not listed");

        program.On(() =>
        {
            MoveTarget Target(string name) => left.Targets.First(target => target.Name == name);
            try
            {
                VaultHands.FoldAll(left);
                left.ClickTileCommand.Execute(VaultHands.Card(left, "Tome of Town Portal"));
                Assert.True(left.CanMove);
                left.OpenMoveCommand.Execute(null);

                Assert.True(left.IsPickerOpen);
                Assert.Equal("Move Tome of Town Portal", left.MoveTitle);
                // Folded is how a folder is made, in the picker as in the tree: one fold for both.
                Assert.True(Target("Charms").HoldsFolders, "Charms has its chevron");
                Assert.False(Target("Charms").IsOpen);
                Assert.False(Target("Small").IsShown, "opened: Charms' folder Small is folded away under it");
                Assert.True(Target("Jewels").IsShown, "the vault's own folders are listed");

                left.FoldTargetCommand.Execute(Target("Charms"));
                Assert.True(Target("Small").IsShown, "unfolded: Small is listed");
                Assert.False(Target("Magic").IsShown, "the folders beside Charms stay as they were");

                left.ClosePickerCommand.Execute(null);
                Assert.False(left.IsPickerOpen);
                left.OpenMoveCommand.Execute(null);
                Assert.True(Target("Charms").IsOpen);
                Assert.True(Target("Small").IsShown, "closed and opened again: Charms is still unfolded");

                left.FoldTargetCommand.Execute(Target("Charms"));
                Assert.False(Target("Small").IsShown, "folded: Small is gone from the list");
            }
            finally
            {
                VaultHands.FoldAll(left);
                VaultHands.Rest(program);
            }
        });
    }
}
