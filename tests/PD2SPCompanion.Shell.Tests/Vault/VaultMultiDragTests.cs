using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// Several vault cards picked and carried onto the inventory all come out,
/// and a plain click on one of several picked selects it
/// alone. The vault on the right page, in Charms' folder Small.
/// <para>
/// What the page decides: what a carry begun on a picked card takes with it, and what a click on one makes of
/// the picks. That a press and a drag of the mouse are told apart on a picked card is the UI cases' to show
/// (units/vault-multi-drag.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultMultiDragTests(Program program)
{
    private const string Fortune = "Small Charm of Fortune", Garnet = "Garnet Small Charm of Life";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;

    /// <summary>The vault on the right page, in Small, its two charms both picked.</summary>
    private VP_ViewModel_Vault_L WithBothPicked()
    {
        var right = program.On(() =>
        {
            var vault = VaultHands.OnRight(program);
            VaultHands.GoTo(vault, "Small");
            return vault;
        });
        Assert.True(program.Until(() => VaultHands.CardOrNone(right, Fortune) is not null && VaultHands.CardOrNone(right, Garnet) is not null), "Small was not opened");

        program.On(() =>
        {
            VaultHands.CtrlClick(right, VaultHands.Card(right, Fortune));
            VaultHands.CtrlClick(right, VaultHands.Card(right, Garnet));

            Assert.True(right.IsPicking);
            Assert.Equal(2, right.PickedCount);
            Assert.Equal("2 items · 2 selected", right.PagesText);
        });
        return right;
    }

    [CorpusFact]
    public void A_plain_click_on_one_of_several_picked_selects_it_alone()
    {
        if (!program.Ready())
            return;

        var right = WithBothPicked();
        program.On(() =>
        {
            try
            {
                // A press on a picked card let go of without a drag: the tile's click, which selects it alone.
                right.ClickTileCommand.Execute(VaultHands.Card(right, Fortune));

                Assert.False(right.IsPicking);
                Assert.Equal(Fortune, right.Selected?.Name);
                Assert.Equal("2 items", right.PagesText);
            }
            finally
            {
                VaultHands.Rest(program);
            }
        });
    }

    [CorpusFact]
    public void Two_picked_and_carried_by_one_onto_the_inventory_both_come_out()
    {
        if (!program.Ready(changes: true))
            return;

        var right = WithBothPicked();
        int stored = 0;
        program.On(() =>
        {
            VaultHands.Section(program).SelectedPage = VaultHands.Choice(program, "Inventory");
            stored = VaultHands.Held(program);
            int held = Session.ItemCount;

            Assert.True(right.BeginCarry(VaultHands.Card(right, Fortune)));
            Assert.Equal(2, right.GhostParts.Count);
            Main.Inventory!.DropFromVault(VaultHands.FreeCell(program, "Inventory"));

            Assert.StartsWith("2 items moved to character.", right.Notice);
            Assert.Equal(held + 2, Session.ItemCount);
        });

        Assert.True(program.Until(() => VaultHands.Held(program) == stored - 2), "the two rows did not leave the vault");
    }
}
