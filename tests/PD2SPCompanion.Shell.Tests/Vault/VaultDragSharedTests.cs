using Avalonia.Input;

using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A vault card carried onto a shared stash page is withdrawn there, and carried back it is stored again (an
/// item stored from the shared stash by drag could not be dragged back). The vault on the
/// right page; on the left, the second pane on Shared Page 8.
/// <para>
/// What the two pages decide once the view has said where the drag was let go of. That a drag of the mouse
/// reaches them, to a cell of the second pane and back, is the UI case's to show (units/vault-drag-shared.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultDragSharedTests(Program program)
{
    private const string Tome = "Tome of Town Portal", Shared = "Shared Page 8";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    [CorpusFact]
    public void A_card_let_go_of_on_a_shared_stash_page_is_withdrawn_there_its_id_kept_and_carried_back_it_is_stored_again()
    {
        if (!program.Ready(changes: true))
            return;

        var right = program.On(() => VaultHands.OnRight(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(right, Tome) is not null), "the tome's card was not listed");

        try
        {
            uint id = 0;
            int stored = 0;
            program.On(() =>
            {
                var section = VaultHands.Section(program);
                section.SecondShown = true;
                section.SecondPage = VaultHands.Choice(program, Shared);

                var card = VaultHands.Card(right, Tome);
                id = card.Item.Id;
                stored = VaultHands.Held(program);
                Assert.False(VaultHands.InSharedStash(program, id), "start: the tome is not in the shared stash");

                // vault -> shared stash
                var cell = VaultHands.FreeCell(program, Shared, 1, 2);
                Assert.True(right.BeginCarry(card));
                Page.DropFromVault(cell);

                Assert.Equal("1 item moved to shared stash. 1 already held.", right.Notice);
                var landed = VaultHands.Tile(program, Tome, Shared);
                Assert.Equal(id, landed.Item.Id);
                Assert.Equal((cell.Column, cell.Row), (landed.CellX, landed.CellY));
            });
            Assert.True(program.Until(() => VaultHands.Held(program) == stored - 1), "the row did not leave the vault");

            // shared stash -> vault
            program.On(() =>
            {
                Assert.True(Page.BeginCarry(VaultHands.Tile(program, Tome, Shared)));
                Assert.True(Session.IsHolding);
                right.DropFromInventory(null);

                Assert.Equal($"{Tome} stored in vault.", Session.Status);
            });
            Assert.True(program.Until(() => VaultHands.Held(program) == stored), "the tome was not stored back");
            program.On(() =>
            {
                Assert.False(VaultHands.InSharedStash(program, id), "the tome has left the shared stash");
                Assert.NotNull(VaultHands.CardOrNone(right, Tome));
            });
        }
        finally
        {
            program.On(() => VaultHands.Section(program).SecondShown = false);
        }
    }
}
