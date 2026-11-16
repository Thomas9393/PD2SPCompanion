using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The vault's bin deletes on its second press once the vault has been on both pages (where nothing
/// in the vault could be deleted): the vault shown on the right, then on the left, a card selected, the
/// bin pressed and pressed again - the row is gone from the vault; then the same from the right page.
/// <para>
/// What the two pages decide: each bin its own arming, each acting on its own page's selection, the other
/// page reading the vault again. The fault this was written for lay in the window - a press reaching the
/// view kept behind - which is the UI case's to show (units/vault-delete.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultDeleteTests(Program program)
{
    private MainViewModel Main => program.Main;

    [CorpusFact]
    public void With_the_vault_once_on_both_pages_each_pages_bin_deletes_its_own_card_on_the_second_press()
    {
        if (!program.Ready(changes: true))
            return;

        // The vault on the right first, then on the left: both pages listed from then on.
        var right = program.On(() => VaultHands.OnRight(program));
        var left = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(left, "Tome of Town Portal") is not null && VaultHands.CardsOf(right).Count > 0), "the vault's cards were not listed");

        program.On(() =>
        {
            Assert.True(Main.IsComparing);
            Assert.True(Main.CanRightVault, "the right Vault bookmark is there while the left page shows the vault");

            // The right page turned to the grail: the left is the one vault page.
            Main.ShowRightCommand.Execute(MainViewModel.GrailMark);
            Assert.False(Main.IsComparing);

            int stored = VaultHands.Held(program);
            left.ClickTileCommand.Execute(VaultHands.Card(left, "Tome of Town Portal"));
            Assert.True(left.CanDeleteSelection);

            left.DeleteCommand.Execute(null);
            Assert.True(left.DeleteArmed);
            Assert.False(right.DeleteArmed, "the other page's bin is not armed by it");
            Assert.Equal(stored, VaultHands.Held(program));

            left.DeleteCommand.Execute(null);
            Assert.False(left.DeleteArmed);
            Assert.Equal(stored - 1, VaultHands.Held(program));
            Assert.Null(VaultHands.CardOrNone(left, "Tome of Town Portal"));
            Assert.Null(VaultHands.CardOrNone(right, "Tome of Town Portal"));

            // And from the right page, the left one now the page kept behind.
            Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark);
            Main.ShowRightCommand.Execute(MainViewModel.VaultMark);
            VaultHands.GoTo(right, "Small");
            right.ClickTileCommand.Execute(VaultHands.Card(right, "Small Charm of Fortune"));

            right.DeleteCommand.Execute(null);
            Assert.True(right.DeleteArmed);
            Assert.Equal(stored - 1, VaultHands.Held(program));

            right.DeleteCommand.Execute(null);
            Assert.Equal(stored - 2, VaultHands.Held(program));
            Assert.Null(VaultHands.CardOrNone(right, "Small Charm of Fortune"));
        });
    }

    [CorpusFact]
    public void A_bin_armed_is_disarmed_by_another_selection_and_then_only_asks_again()
    {
        if (!program.Ready(changes: true))
            return;

        var left = program.On(() =>
        {
            var vault = VaultHands.OnLeft(program);
            VaultHands.GoTo(vault, "Small");
            return vault;
        });
        Assert.True(program.Until(() => VaultHands.CardOrNone(left, "Small Charm of Fortune") is not null), "Small was not opened");

        program.On(() =>
        {
            int stored = VaultHands.Held(program);
            left.ClickTileCommand.Execute(VaultHands.Card(left, "Small Charm of Fortune"));
            left.DeleteCommand.Execute(null);
            Assert.True(left.DeleteArmed);

            // What the bin was armed for has changed: the arming goes.
            left.ClickTileCommand.Execute(VaultHands.Card(left, "Garnet Small Charm of Life"));
            Assert.False(left.DeleteArmed);

            left.DeleteCommand.Execute(null);
            Assert.Equal("Delete Garnet Small Charm of Life? Click red tick again to confirm.", Main.Session.Status);
            Assert.Equal(stored, VaultHands.Held(program));
        });
    }
}
