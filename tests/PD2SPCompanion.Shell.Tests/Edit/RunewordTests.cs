using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The runeword builder on the Edit page (RunewordEditor):
/// a white Crystal Sword with four sockets in the vault is made Spirit on the vault's Edit page - the Zod
/// button's picker, the word picked - and the vault holds Spirit, a runeword, where the white sword was.
/// <para>
/// What the builder decides, asked through its commands and read back from the vault's own row. Nothing of it
/// is the window's alone.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class RunewordTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.VaultEditPage();
    private RunewordEditor Runeword => Edit.Runeword;

    [CorpusFact]
    public void A_word_picked_for_a_white_base_in_the_vault_makes_it_the_runeword_there()
    {
        if (!program.Ready(changes: true))
            return;

        using var free = program.InMode("Free");
        program.OnTheVaultsEditPage("crs", 4, "Crystal Sword");

        program.On(() =>
        {
            Assert.True(Runeword.CanBuild);
            Assert.False(Runeword.IsNow);

            Runeword.OpenPickerCommand.Execute(null);
            Assert.True(Runeword.IsPickerOpen);
            Assert.Equal("Crystal Sword", Runeword.Base);

            Runeword.PickCommand.Execute(Runeword.Choices.First(row => row.Name == "Spirit"));
            // Open still, as every picker is after a pick; written when it closes.
            Assert.True(Runeword.IsPickerOpen);
            Runeword.ClosePickerCommand.Execute(null);
            // Named as it was: the word is the item's name from here on.
            Assert.Equal("Crystal Sword: becomes Spirit.", program.Main.Session.RightMessages.Text);
        });

        Assert.True(program.Until(() => program.Stored().Item.IsRuneword), "the vault's row is no runeword");
        program.Settled(Edit);
        program.On(() =>
        {
            var (row, item) = program.Stored();
            Assert.Equal("Spirit", row.Name);
            Assert.Equal(4, item.Filled());
            Assert.Equal("Spirit", Edit.Summary?.Name);
            Assert.True(Runeword.IsNow);
            Assert.Contains(Edit.Stats, each => each.IsRuneword);
        });

        // The vault read again: no white Crystal Sword is left in it.
        Assert.True(program.Until(() => program.Main.Vault!.Cards().Any(card => card.Name == "Spirit")), "no card is named Spirit");
        program.On(() => Assert.DoesNotContain(program.Main.Vault!.Cards(), card => card.Name == "Crystal Sword"));
    }
}
