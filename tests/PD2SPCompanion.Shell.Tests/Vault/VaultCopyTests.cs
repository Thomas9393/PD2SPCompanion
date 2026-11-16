using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A second of a vault card (a copy pasted where it lies): a card selected on
/// the vault page, copied and pasted - the vault holds one row more, the second under an id of its own, in the
/// same folder. In Legit, which makes no copies, there is no Copy.
/// <para>
/// What the page decides. That the Copy button is not drawn in Legit, and that a click on it and on Paste
/// reaches the page in Free, is the UI cases' to show (units/clipboard.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultCopyTests(Program program)
{
    private const string Tome = "Tome of Town Portal";

    private PD2SPCompanion.Core.Collection.Vault Store => program.Get<VaultService>().Store!;

    /// <summary>The ids of the vault's tomes, as its rows keep them.</summary>
    private List<uint?> TomeIds(VP_ViewModel_Vault_L vault) =>
        [.. VaultHands.CardsOf(vault).Where(card => card.Name == Tome).Select(card => card.Row.OriginId)];

    private VP_ViewModel_Vault_L WithTheTomeSelected()
    {
        var left = program.On(() => VaultHands.OnLeft(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(left, Tome) is not null), "the tome's card was not listed");
        program.On(() => left.ClickTileCommand.Execute(VaultHands.Card(left, Tome)));
        return left;
    }

    [CorpusFact]
    public void A_card_copied_and_pasted_is_one_row_more_in_its_folder_under_an_id_of_its_own()
    {
        if (!program.Ready(changes: true))
            return;

        var settings = program.On(() => program.Get<EditorSettings>());
        string mode = program.On(() => settings.PlayMode);
        try
        {
            program.On(() => settings.PlayMode = PlayPolicy.FreeName);
            var left = WithTheTomeSelected();

            program.On(() =>
            {
                int stored = Store.Count(), atTop = Store.Count(null);
                Assert.Single(TomeIds(left));
                Assert.True(left.Rules.Duplicates);
                Assert.Equal("Copy (Ctrl + C)", left.CopyHint);

                // Copied and pasted where it lies: a second beside the first, as Duplicate made it.
                left.Copy();
                Assert.Equal("Paste (Ctrl + V)" + Environment.NewLine + "1 item copied", left.PasteHint);
                left.PasteCommand.Execute(null);

                Assert.Equal($"Copied {Tome}.", program.Main.Session.Status);
                Assert.Equal(stored + 1, Store.Count());
                Assert.Equal(atTop + 1, Store.Count(null));

                var ids = TomeIds(left);
                Assert.Equal(2, ids.Count);
                Assert.All(ids, id => Assert.NotNull(id));
                Assert.NotEqual(ids[0], ids[1]);
            });
        }
        finally
        {
            program.On(() => settings.PlayMode = mode);
        }
    }

    [CorpusFact]
    public void In_Legit_there_is_no_Copy_and_asked_all_the_same_nothing_is_made()
    {
        if (!program.Ready(changes: true))
            return;

        var settings = program.On(() => program.Get<EditorSettings>());
        string mode = program.On(() => settings.PlayMode);
        try
        {
            program.On(() => settings.PlayMode = PlayPolicy.LegitName);
            var left = WithTheTomeSelected();

            program.On(() =>
            {
                int stored = Store.Count();

                // What the button's being there is bound to.
                Assert.False(left.Rules.Duplicates);

                left.Copy();
                Assert.True(PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.IsEmpty);
                left.PasteCommand.Execute(null);
                Assert.Equal(stored, Store.Count());
                Assert.Single(TomeIds(left));
            });
        }
        finally
        {
            program.On(() =>
            {
                settings.PlayMode = mode;
                VaultHands.Rest(program);
            });
        }
    }
}
