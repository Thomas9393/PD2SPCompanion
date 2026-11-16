using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// An item sent to the other pane (by its menu's "To
/// other pane"): between the inventory's two panes one item, back again, and every pick where
/// it is one of several - "Require pane II open." with one pane; between the two vault pages into the
/// folder the other shows - "Already in this folder." where both show the same, "Require vault on both pages."
/// with one. And the row of an item's menu, which is there only while there is somewhere across.
/// <para>
/// What the pages decide, asked of them as the menu's row asks (SendAcross) and read back from the panes and
/// the vault's folders. That the menu opens on a vault card with the vault on both pages and where Swap stands
/// are the UI cases' (units/send-across.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SendAcrossTests(Program program)
{
    private const string Ring = "Steel Ring of Skill", Charm = "Small Charm of Fortune";

    private const string ToOtherPane = "To other pane", ToOtherPage = "To other page";

    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    /// <summary>The row of an item's menu that sends it across, as it is worded - or null where the menu has none.</summary>
    private string? Across(ItemTileViewModel tile) => Page.MenuFor(tile).Find(ToOtherPane)?.Label;

    private static ItemTileViewModel Card(VP_ViewModel_Vault_L vault, string name) =>
        InventoryPad.CardsOf(vault).First(card => card.Name == name);

    private static bool HasCard(VP_ViewModel_Vault_L vault, string name) =>
        InventoryPad.CardsOf(vault).Any(card => card.Name == name);

    /// <summary>A vault page on a folder, its cards listed (Show: what the view calls as the page comes into sight).</summary>
    private void Open(VP_ViewModel_Vault_L vault, long? folder, string expecting)
    {
        program.On(() =>
        {
            vault.Show();
            vault.GoTo(folder);
        });
        Assert.True(program.Until(() => HasCard(vault, expecting)), $"{expecting} was not listed on the {vault.PageName} vault page");
    }

    [CorpusFact]
    public void With_one_pane_there_is_nowhere_to_send_it_and_the_tooltip_does_not_offer_it()
    {
        if (!program.Ready())
            return;

        try
        {
            program.On(() =>
            {
                program.Panes("Personal Stash");
                var ring = program.Tile(Ring, "Personal Stash");

                Assert.Null(Across(ring));
                Assert.False(Page.HasPaneAcross(ring));

                Page.SendAcross(ring);
                Assert.Equal("Require pane II open.", Session.Status);
                Assert.True(program.Holds("Personal Stash", Ring));
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void With_two_panes_the_tooltip_names_the_key_and_the_click_sends_the_item_across_and_back()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash", "Shared Page 5");
            Assert.Equal(ToOtherPane, Across(program.Tile(Ring, "Personal Stash")));

            Page.SendAcross(program.Tile(Ring, "Personal Stash"));
            Assert.Equal("Moved 1 item to Shared Page 5.", Session.Status);
            Assert.True(program.Holds("Shared Page 5", Ring));
            Assert.False(program.Holds("Personal Stash", Ring));

            // The same click in the second pane sends it back.
            Page.SendAcross(program.Tile(Ring, "Shared Page 5"));
            Assert.Equal("Moved 1 item to Personal Stash.", Session.Status);
            Assert.True(program.Holds("Personal Stash", Ring));
            Assert.False(program.Holds("Shared Page 5", Ring));
        });
    }

    /// <summary>Three of the personal stash picked, as a rectangle drawn over them picks them (DragPick).</summary>
    private static readonly string[] Picks = [Ring, "Felicitous Ring of Regrowth", "Silver Jewel of Malice"];

    private void PickThree()
    {
        Assert.True(Page.BeginDragPick(keepSelected: false));
        foreach (string name in Picks)
            Page.SetPicked(program.Tile(name, "Personal Stash"), true);
        Page.EndPick();
        Assert.Equal(3, Page.PickedCount);
    }

    [CorpusFact]
    public void A_click_on_one_of_several_picked_sends_them_all()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash", "Shared Page 5");
            PickThree();
            Assert.Equal("To other pane (3)", Across(program.Tile(Ring, "Personal Stash")));

            Page.SendAcross(program.Tile(Ring, "Personal Stash"));
            Assert.Equal("Moved 3 items to Shared Page 5.", Session.Status);
            Assert.All(Picks, name => Assert.True(program.Holds("Shared Page 5", name), $"{name} was not sent"));
            Assert.All(Picks, name => Assert.False(program.Holds("Personal Stash", name), $"{name} stayed"));

            // Picked still, on the shared page: a pick is the item, whichever file holds it.
            Assert.True(Page.IsPicking);
            Assert.Equal(3, Page.PickedCount);
            Assert.All(Picks, name => Assert.True(program.Tile(name, "Shared Page 5").IsPicked, $"{name} is not lit"));
        });
    }

    /// <summary>
    /// The materials tab across: it takes and gives one at a time, so the line counts the one clicked, picked among
    /// several or not (it said "(3)" and one went).
    /// </summary>
    [CorpusFact]
    public void With_the_materials_tab_across_the_line_counts_the_one_it_sends()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash", "Materials");
            PickThree();
            Assert.Equal(ToOtherPane, Across(program.Tile(Ring, "Personal Stash")));
        });
    }

    [CorpusFact]
    public void Several_sent_between_the_characters_own_containers_stay_picked()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash", "Inventory");
            PickThree();

            Page.SendAcross(program.Tile(Ring, "Personal Stash"));
            Assert.Equal("Moved 3 items to Inventory.", Session.Status);
            Assert.All(Picks, name => Assert.True(program.Holds("Inventory", name), $"{name} was not sent"));

            // A pick is the item, which is the same item on the other side.
            Assert.True(Page.IsPicking);
            Assert.Equal(3, Page.PickedCount);
        });
    }

    [CorpusFact]
    public void A_vault_card_is_sent_only_while_the_vault_is_on_both_pages()
    {
        if (!program.Ready())
            return;

        var left = Main.Vault!;
        long? was = program.On(() => left.Here);
        try
        {
            program.On(() =>
            {
                Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
                Main.ShowRightCommand.Execute(MainViewModel.GrailMark);
            });
            Open(left, program.On(() => program.Folder("Small")), Charm);

            program.On(() =>
            {
                Assert.False(left.IsComparing);
                // Dimmed with why, not left out: the menu keeps its rows whatever the other page shows.
                var row = left.MenuFor(Card(left, Charm)).Row(ToOtherPage);
                Assert.False(row.IsEnabled);
                Assert.Equal("Require vault on both pages.", row.Why);

                left.SendAcross(Card(left, Charm));
                Assert.Equal("Require vault on both pages.", left.Notice);
                Assert.True(HasCard(left, Charm));
            });
        }
        finally
        {
            program.On(() => left.GoTo(was));
        }
    }

    [CorpusFact]
    public void With_the_vault_on_both_pages_a_card_goes_into_the_folder_the_other_page_shows_and_back()
    {
        if (!program.Ready(changes: true))
            return;

        var left = Main.Vault!;
        var right = Main.RightVault!;
        var (leftWas, rightWas) = program.On(() => (left.Here, right.Here));
        try
        {
            long small = program.On(() => program.Folder("Small")), grand = program.On(() => program.Folder("Grand"));
            program.On(() =>
            {
                Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
                left.ToggleCompareCommand.Execute(null);
                Assert.True(left.IsComparing, "Compare did not put the vault on the right page");
            });
            Open(left, small, Charm);
            Open(right, small, Charm);

            program.On(() =>
            {
                left.SendAcross(Card(left, Charm));
                Assert.Equal("Already in this folder.", left.Notice);
            });

            // A folder of its own on the right page: the card's tooltip offers it, and the click sends it.
            program.On(() => right.GoTo(grand));
            Assert.True(program.Until(() => right.PagesText == "1 item"), "the right page is not on Grand");
            program.On(() =>
            {
                Assert.True(left.MenuFor(Card(left, Charm)).Row(ToOtherPage).IsEnabled);
                Assert.Equal("2 items", left.PagesText);

                left.SendAcross(Card(left, Charm));
                Assert.Equal("Moved to Grand.", left.Notice);
            });
            Assert.True(program.Until(() => HasCard(right, Charm)), "the charm is not on the right page's folder");
            program.On(() =>
            {
                Assert.Equal("1 item", left.PagesText);
                Assert.Equal("2 items", right.PagesText);
                Assert.False(HasCard(left, Charm));

                right.SendAcross(Card(right, Charm));
                Assert.Equal("Moved to Small.", right.Notice);
            });
            Assert.True(program.Until(() => HasCard(left, Charm)), "the charm is not back on the left page's folder");
            program.On(() =>
            {
                Assert.Equal("2 items", left.PagesText);
                Assert.Equal("1 item", right.PagesText);
            });
        }
        finally
        {
            program.On(() =>
            {
                left.GoTo(leftWas);
                right.GoTo(rightWas);
            });
        }
    }
}
