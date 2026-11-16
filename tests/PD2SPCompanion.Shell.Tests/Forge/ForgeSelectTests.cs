using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// New is not select: the Forge and Edit are one item panel in two states and look
/// alike, so the Forge never stands beside a selection. Taking the Forge clears the left page's selection.
/// With the Forge up, an item selected on the left - one, or several picked, a vault card too - takes the
/// right page to Edit for it, and the Forge keeps what it holds, its bookmark saying "Holding ..." until it is
/// gone back to. What FORGE makes lands unselected, so the page stays. What a drag selected as it carried is
/// let go of, and the Forge stays: moving an item is not asking to see it. Only the Forge: Trade stays beside
/// a selection, and a vault folder walked into is no item.
/// <para>
/// What the main view model decides when the view tells it a press was let go of (SettleForge), asked of it
/// as the view asks. That a click, a Ctrl + click and a drag of the mouse reach that asking - after the
/// selection was made, and saying whether something was carried - is the UI cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ForgeSelectTests(Program program)
{
    private MainViewModel Main => program.Main;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;

    private ItemTileViewModel InGrid(string name, string container = "Inventory") =>
        Page.Sections.First(section => section.Pages is not null)
            .Pages!.First(page => page.Label == container).Grid!.Items.First(tile => tile.Name == name);

    /// <summary>
    /// Two Ctrl + clicks, as the drag rectangle's side of the page takes them: on two rings of the personal
    /// stash, which no lock keeps - the inventory's lock, on in a new install, keeps its charms and tools out
    /// of a pick.
    /// </summary>
    private void PickTwo()
    {
        Assert.True(Page.BeginDragPick(keepSelected: true));
        Page.SetPicked(InGrid("Steel Ring of Skill", "Personal Stash"), true);
        Page.SetPicked(InGrid("Entropy Gyre", "Personal Stash"), true);
        Page.EndPick();
        Assert.Equal(2, Page.PickedCount);
    }

    /// <summary>A plain click on an item of the inventory, and the press let go of with nothing carried.</summary>
    private void Click(string name)
    {
        Page.ClickTileCommand.Execute(InGrid(name));
        Main.SettleForge(carried: false);
    }

    [CorpusFact]
    public void Taking_the_Forge_clears_the_left_pages_selection()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Page.ClickTileCommand.Execute(InGrid("Annihilus"));
            Assert.Equal("Annihilus", Main.Session.SelectedItem?.Name);
        });

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            Assert.True(Main.IsRightForge);
            Assert.Null(Main.Session.SelectedItem);

            // Edit, gone back to, shows no Annihilus.
            Main.ShowRightCommand.Execute(MainViewModel.EditMark);
            Assert.False(bench.Right.Editor.HasItem);
        });
    }

    [CorpusFact]
    public void Several_picked_are_let_go_of_too_when_the_Forge_is_taken()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            PickTwo();
            Assert.True(Page.HasItemSelection);
        });

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            Assert.False(Page.IsPicking);
            Assert.Equal(0, Page.PickedCount);
            Assert.Null(Main.Session.SelectedItem);
        });
    }

    [CorpusFact]
    public void What_FORGE_makes_lands_unselected_the_Forge_stays_and_its_bookmark_says_only_Forge()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war trav", "War Traveler");
            bench.Forge.ForgeCommand.Execute(null);
            Assert.Equal("War Traveler forged to inventory.", bench.Session.Messages.Text);

            // The press on FORGE let go of.
            Main.SettleForge(carried: false);

            Assert.Null(Main.Session.SelectedItem);
            Assert.False(Page.HasItemSelection);
            Assert.True(Main.IsRightForge);
            Assert.Equal("War Traveler", bench.Forge.Draft?.Name);
            Assert.False(Main.ForgeWaits);
            Assert.Equal("Forge", Main.ForgeBookmarkHint);
        });
    }

    [CorpusFact]
    public void What_a_drag_selected_as_it_carried_is_let_go_of_and_the_Forge_stays()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war trav", "War Traveler");

            // A drag selects what it carries, as Explorer's does; the view says a carry was under way at the release.
            Page.ClickTileCommand.Execute(InGrid("Annihilus"));
            Main.SettleForge(carried: true);

            Assert.True(Main.IsRightForge);
            Assert.Null(Main.Session.SelectedItem);
            Assert.False(Main.ForgeWaits);

            // Edit, gone to by hand, shows nothing of it.
            Main.ShowRightCommand.Execute(MainViewModel.EditMark);
            Assert.False(bench.Right.Editor.HasItem);
        });
    }

    [CorpusFact]
    public void An_item_clicked_with_the_Forge_up_takes_the_right_page_to_Edit_and_the_Forge_keeps_what_it_holds()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war trav", "War Traveler");
            Click("Annihilus");

            Assert.True(Main.IsRightEdit);
            Assert.True(bench.Right.IsEdit);
            Assert.Equal("Annihilus", bench.Right.Editor.Summary?.Name);

            // The Forge's bookmark is marked, and says what it holds.
            Assert.True(Main.ForgeWaits);
            Assert.Equal("Forge" + Environment.NewLine + "Holding War Traveler", Main.ForgeBookmarkHint);

            // Gone back to: it still holds the boots, the mark is gone - and so is the selection it stood beside.
            Main.ShowRightCommand.Execute(MainViewModel.ForgeMark);
            Assert.True(Main.IsRightForge);
            Assert.Equal("War Traveler", bench.Forge.Draft?.Name);
            Assert.False(Main.ForgeWaits);
            Assert.Equal("Forge", Main.ForgeBookmarkHint);
            Assert.Null(Main.Session.SelectedItem);
        });
    }

    [CorpusFact]
    public void A_Forge_holding_nothing_is_left_for_Edit_unmarked()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            Assert.False(bench.Forge.HasDraft);
            Click("Annihilus");

            Assert.True(Main.IsRightEdit);
            Assert.False(Main.ForgeWaits);
            Assert.Equal("Forge", Main.ForgeBookmarkHint);
        });
    }

    [CorpusFact]
    public void Several_picked_with_the_Forge_up_leave_it_too_and_Edit_says_how_many()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war trav", "War Traveler");

            PickTwo();
            Main.SettleForge(carried: false);

            Assert.True(Main.IsRightEdit);
            Assert.True(bench.Right.Editor.IsPicking);
            Assert.Equal("2 selected", bench.Right.Editor.PickedText);
            Assert.True(Main.ForgeWaits);
        });
    }

    [CorpusFact]
    public void Only_the_Forge_Trade_stays_beside_a_selection()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.TradeMark);
            Assert.True(Main.IsRightTrade);

            Click("Annihilus");

            Assert.True(Main.IsRightTrade);
            Assert.Equal("Annihilus", Main.Session.SelectedItem?.Name);
        });
    }

    [CorpusFact]
    public void Beside_the_vault_a_card_clicked_leaves_the_Forge_for_Edit_and_a_folder_walked_into_does_not()
    {
        if (!program.Ready())
            return;

        using var bench = new ForgeBench(program);
        var vault = Main.Vault!;
        try
        {
            program.On(() =>
            {
                bench.Find("war trav", "War Traveler");
                Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
                vault.Show();
                Assert.True(Main.IsRightForge, "the Forge stays as the vault is taken on the left");
            });
            Assert.True(program.Until(() => Cards(vault).Count > 0), "the vault's cards were not listed");

            program.On(() =>
            {
                // A folder is no item: walked into, the Forge stays - it is where FORGE will put things.
                var folder = vault.Places.First(place => place.Id is not null);
                vault.GoToPlace(folder);
                Main.SettleForge(carried: false);
                Assert.True(Main.IsRightForge);
                Assert.Equal(folder.Id, vault.Here);

                vault.GoTo(null);
            });
            Assert.True(program.Until(() => Cards(vault).Any(card => card.Name == "Tome of Town Portal")), "the tome's card was not listed");

            program.On(() =>
            {
                vault.ClickTileCommand.Execute(Cards(vault).First(card => card.Name == "Tome of Town Portal"));
                Main.SettleForge(carried: false);

                Assert.True(Main.IsRightEdit);
                Assert.Equal("Tome of Town Portal", bench.Right.Editor.Summary?.Name);
                Assert.True(Main.ForgeWaits);
                Assert.Equal("Forge" + Environment.NewLine + "Holding War Traveler", Main.ForgeBookmarkHint);
            });
        }
        finally
        {
            program.On(() =>
            {
                vault.ClearSelection();
                vault.GoTo(null);
            });
        }
    }

    /// <summary>A vault page's cards, its sheet read as the window reads it (RightClickTests.CardsOf).</summary>
    private static IReadOnlyList<ItemTileViewModel> Cards(VP_ViewModel_Vault_L vault)
    {
        foreach (var row in vault.PageRows.ToList())
            _ = row.Pages;

        return vault.Cards;
    }
}
