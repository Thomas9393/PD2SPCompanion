using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The vault and the inventory alike: the inventory's title row says
/// what the character holds and its gold, as the vault's does; Copy takes the inventory's picks; the
/// inventory's bin, armed for two, asks again once Ctrl + A has widened what it would delete; the vault's bin
/// says what it asks and what it did; and a vault card being carried, put back, is not withdrawn by the
/// release that follows.
/// <para>
/// What the two pages decide, asked of them as their views ask. That Esc and a right click reach a drag of the
/// mouse while it is under way is the UI cases' to show (units/vault-inventory-parity.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultInventoryParityTests(Program program)
{
    private const string RingA = "Steel Ring of Skill", RingB = "Felicitous Ring of Regrowth", Stash = "Personal Stash";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private ContainerSectionViewModel Section => VaultHands.Section(program);

    /// <summary>How many items of that name the character's containers and the shared stash hold, shown or not.</summary>
    private int Everywhere(string name) =>
        Section.Pages!.Where(page => page.Grid is not null).Sum(page => page.Grid!.Items.Count(tile => tile.Name == name));

    /// <summary>The two rings of the character's stash picked: one clicked, the other Ctrl + clicked.</summary>
    private void PickBothRings()
    {
        Section.SelectedPage = VaultHands.Choice(program, Stash);
        Page.ClickTileCommand.Execute(VaultHands.Tile(program, RingA, Stash));
        VaultHands.CtrlClick(Page, VaultHands.Tile(program, RingB, Stash));

        Assert.True(Page.IsPicking);
        Assert.Equal(2, Page.PickedCount);
    }

    /// <summary>The pane the tests turn to the character's stash, put back to the inventory as Ready leaves the page.</summary>
    private void PaneBack()
    {
        Page.ClearSelection();
        Page.DisarmDelete();
        Section.SelectedPage = VaultHands.Choice(program, "Inventory");
    }

    [CorpusFact]
    public void The_inventorys_title_row_says_what_the_character_holds_and_its_gold()
    {
        if (!program.Ready())
            return;

        program.On(() => Assert.Matches(@"^\d+ items · [\d,]+ gold$", Page.FactsText));
    }

    /// <summary>
    /// A pane turned to another page lets go of the picks on the page no pane shows any more, as the belt put away
    /// does: Delete, Store and Copy acted on them out of sight.
    /// </summary>
    [CorpusFact]
    public void Picks_on_a_page_no_pane_shows_are_let_go_of()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                PickBothRings();
                Assert.Equal(2, Page.PickedCount);
                Section.SelectedPage = VaultHands.Choice(program, "Cube");
            });

            Assert.True(program.Until(() => Page.PickedCount == 0 && !Page.IsPicking), "the rings out of sight are picked still");
        }
        finally
        {
            program.On(PaneBack);
        }
    }

    /// <summary>And how many are picked, as the vault's line says it.</summary>
    [CorpusFact]
    public void The_inventorys_title_row_says_how_many_are_picked()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            try
            {
                PickBothRings();
                Assert.EndsWith(" · 2 selected", Page.FactsText);

                Page.ClearSelection();
                Assert.DoesNotContain("selected", Page.FactsText);
            }
            finally
            {
                PaneBack();
            }
        });
    }

    [CorpusFact]
    public void Copy_and_paste_take_the_inventorys_picks_one_more_of_each()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            try
            {
                int a = Everywhere(RingA), b = Everywhere(RingB);
                PickBothRings();

                Assert.True(Page.CanCut);
                Page.Copy();
                Assert.EndsWith("2 items copied", Page.PasteHint);

                Page.PasteCommand.Execute(null);

                Assert.Equal("Copied 2 items.", Session.Status);
                Assert.Equal(a + 1, Everywhere(RingA));
                Assert.Equal(b + 1, Everywhere(RingB));
            }
            finally
            {
                PaneBack();
            }
        });
    }

    [CorpusFact]
    public void The_bin_armed_for_two_asks_again_once_Ctrl_A_has_widened_the_picks_and_nothing_is_deleted()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            try
            {
                PickBothRings();
                int held = Everywhere(RingA) + Everywhere(RingB), all = Session.ItemCount;

                Page.DeleteItemCommand.Execute(null);
                Assert.Equal("Delete 2 selected items? Click red tick again to confirm.", Session.Status);
                Assert.True(Page.DeleteArmed);
                Assert.StartsWith("Confirm deletion", Page.DeleteHint);

                // Ctrl + A over the section: every item the pane shows.
                Page.SelectAll(Section);
                Assert.True(Page.PickedCount > 2, "Ctrl + A widened the picks");
                Assert.False(Page.DeleteArmed, "the bin is no longer armed");
                Assert.DoesNotContain("Confirm deletion", Page.DeleteHint);

                Page.DeleteItemCommand.Execute(null);
                Assert.Equal($"Delete {Page.PickedCount} selected items? Click red tick again to confirm.", Session.Status);
                Assert.Equal(held, Everywhere(RingA) + Everywhere(RingB));
                Assert.Equal(all, Session.ItemCount);
            }
            finally
            {
                PaneBack();
            }
        });
    }

    [CorpusFact]
    public void The_vaults_bin_says_what_it_asks_and_what_it_deleted_on_the_strip_under_its_page()
    {
        if (!program.Ready(changes: true))
            return;

        var right = program.On(() => VaultHands.OnRight(program));
        program.On(() => VaultHands.GoTo(right, "Small"));
        Assert.True(program.Until(() => VaultHands.CardOrNone(right, "Small Charm of Fortune") is not null), "Small was not opened");

        program.On(() =>
        {
            int stored = VaultHands.Held(program);
            right.ClickTileCommand.Execute(VaultHands.Card(right, "Small Charm of Fortune"));
            Assert.Equal("Delete selected item (Del)", right.DeleteHint);

            right.DeleteCommand.Execute(null);
            Assert.Equal("Delete Small Charm of Fortune? Click red tick again to confirm.", Session.RightMessages.Text);
            Assert.StartsWith("Confirm deletion", right.DeleteHint);
            Assert.Equal(stored, VaultHands.Held(program));

            right.DeleteCommand.Execute(null);
            Assert.Equal("Deleted Small Charm of Fortune.", Session.RightMessages.Text);
            Assert.Equal(stored - 1, VaultHands.Held(program));
            Assert.Null(VaultHands.CardOrNone(right, "Small Charm of Fortune"));
        });
    }

    [CorpusFact]
    public void A_carried_card_put_back_is_not_withdrawn_by_the_release_that_follows()
    {
        if (!program.Ready(changes: true))
            return;

        var right = program.On(() => VaultHands.OnRight(program));
        Assert.True(program.Until(() => VaultHands.CardOrNone(right, "Tome of Town Portal") is not null), "the vault's cards were not listed");

        program.On(() =>
        {
            try
            {
                Section.SecondShown = true;
                Section.SecondPage = VaultHands.Choice(program, "Shared Page 8");
                var card = VaultHands.Card(right, "Tome of Town Portal");
                uint id = card.Item.Id;
                int stored = VaultHands.Held(program);

                Assert.True(right.BeginCarry(card));
                Assert.True(right.IsCarrying);
                Assert.True(Page.IsTakingDrops, "the inventory's cells take the card while it is carried");

                // Esc, and a right click while the left button is down: both end the carry (the view's two ways in).
                right.EndCarry();
                Assert.False(right.IsCarrying);
                Assert.False(Page.IsTakingDrops);

                // The button let go of over a cell of the shared page, as the drag would have been.
                Page.DropFromVault(VaultHands.FreeCell(program, "Shared Page 8", 1, 2));

                Assert.False(VaultHands.InSharedStash(program, id), "nothing was withdrawn");
                Assert.False(program.Get<VaultMoves>().HasWithdrawals, "nothing waits to be written");
                Assert.Equal(stored, VaultHands.Held(program));
                Assert.NotNull(VaultHands.CardOrNone(right, "Tome of Town Portal"));
            }
            finally
            {
                Section.SecondShown = false;
            }
        });
    }
}
