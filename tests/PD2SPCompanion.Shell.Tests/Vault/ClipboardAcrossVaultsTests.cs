using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// What the clipboard holds of a vault's is held by numbers each vault counts from 1: opened another vault, they would
/// name other items and folders there. So another vault lets them go, and says so; the
/// character's items, held as themselves, stay held; and a character opened in the same vault's mode opens no other vault.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ClipboardAcrossVaultsTests(Program program)
{
    private static ItemClipboard Clip => ItemClipboard.Shared;

    private VaultService Vaults => program.Get<VaultService>();

    /// <summary>The hardcore vault opened for a look, the softcore one and the settings as they were after.</summary>
    private void InTheOtherVault(Action look)
    {
        try
        {
            Vaults.UseMode(true);
            look();
        }
        finally
        {
            Clip.Clear();
            Vaults.UseMode(false);
            program.Get<EditorSettings>().VaultNameHardcore = "";
        }
    }

    [CorpusFact]
    public void Cards_and_folders_held_go_when_another_vault_is_opened_and_it_is_said()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            Clip.HoldVault([1, 2, 3], [1]);
            Assert.Equal(ClipKind.Cut, Clip.Kind);

            InTheOtherVault(() =>
            {
                Assert.True(Clip.IsEmpty);
                Assert.Empty(Clip.Cards);
                Assert.Empty(Clip.Folders);
                Assert.Equal("Clipboard cleared.", program.Main.Session.Status);
            });
        });
    }

    [CorpusFact]
    public void A_copy_of_cards_goes_too()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            Clip.HoldCards(ClipKind.Copy, [1]);
            InTheOtherVault(() => Assert.True(Clip.IsEmpty));
        });
    }

    [CorpusFact]
    public void The_characters_items_stay_held()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var item = program.Main.Session.HeldItems.First();
            Clip.HoldItems(ClipKind.Copy, [item]);
            string said = program.Main.Session.Status;

            InTheOtherVault(() =>
            {
                Assert.Contains(item, Clip.Items);
                Assert.NotEqual("Clipboard cleared.", program.Main.Session.Status);
            });
        });
    }

    [CorpusFact]
    public void Nothing_is_let_go_where_the_vault_is_the_one_open_already()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            try
            {
                Clip.HoldCards(ClipKind.Cut, [1]);
                Vaults.UseMode(Vaults.IsHardcore);
                Assert.Contains(1L, Clip.Cards);
            }
            finally
            {
                Clip.Clear();
            }
        });
    }
}
