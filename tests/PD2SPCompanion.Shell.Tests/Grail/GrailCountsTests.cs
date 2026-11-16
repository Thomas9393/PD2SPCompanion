using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Grail;

/// <summary>
/// The grail's copies held: a unique in the shared stash is counted on its card as the
/// character's; stored into the vault, the card's counts follow - the store counts where it is going - and
/// Undo puts both back. (A runeword has no card, and is not counted.)
/// <para>
/// What the grail counts, read off its cards. That a card says its counts in a line - "Character 2   Vault 0"
/// - and a recipe's card has none, is the UI case's to show (units/grail-counts.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GrailCountsTests(Program program)
{
    private const string Piece = "Death Cleaver", Shared = "Shared Page 2";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    [CorpusFact]
    public void A_unique_in_the_shared_stash_counts_as_the_characters_stored_the_counts_follow_and_Undo_puts_both_back()
    {
        if (!program.Ready(changes: true))
            return;

        var grail = program.On(() => GrailHands.OnRight(program));
        try
        {
            GrailCard card = null!;
            int character = 0, vault = 0;
            program.On(() =>
            {
                card = GrailHands.Card(grail, GrailViewModel.Uniques, Piece);
                (character, vault) = (card.HeldOnCharacter, card.HeldInVault);

                Assert.True(card.ShowsHeld);
                Assert.True(character >= 1, "the shared stash's copy counts as the character's");

                // Stored: off the character's side, into the vault.
                VaultHands.Section(program).SelectedPage = VaultHands.Choice(program, Shared);
                Page.ClickTileCommand.Execute(VaultHands.Tile(program, Piece, Shared));
                Assert.True(Page.CanStore);
                Page.StoreToVaultCommand.Execute(null);
                Assert.Equal($"{Piece} stored in vault.", Session.Status);
            });
            Assert.True(program.Until(() => card.HeldOnCharacter == character - 1 && card.HeldInVault == vault + 1),
                        $"the counts did not follow the store ({card.HeldOnCharacter}, {card.HeldInVault})");

            Assert.True(program.Until(() => Main.Home.UndoCommand.CanExecute(null)), "nothing to undo");
            program.On(() => Main.Home.UndoCommand.Execute(null));
            Assert.True(program.Until(() => card.HeldOnCharacter == character && card.HeldInVault == vault),
                        $"undo did not put the counts back ({card.HeldOnCharacter}, {card.HeldInVault})");
        }
        finally
        {
            program.On(() =>
            {
                GrailHands.Rest(grail);
                Page.ClearSelection();
                VaultHands.Section(program).SelectedPage = VaultHands.Choice(program, "Inventory");
            });
        }
    }
}
