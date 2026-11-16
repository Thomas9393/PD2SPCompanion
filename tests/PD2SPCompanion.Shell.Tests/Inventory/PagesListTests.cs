using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// The Items page's list of pages: the
/// containers section's pages stand in a list beside the grids, where a drop box over each pane named them. A
/// click shows a page in the first pane; Ctrl + click in the second, opening it - and on the page the second
/// shows, shuts it; with two panes up the rows and the panes' lines say I and II. An item carried and let go on a
/// row goes to that page's first free room. A page has a short name for a list too narrow for its whole one.
/// The shared stash's gold is the section's, in its header, with the carried gold above it in the panel behind.
/// <para>
/// What the page decides, asked of it as the view asks. That the rows are drawn beside the grids, that a right
/// click reaches a row, that the list gives way to the grid on a narrow page and says its names short then, and
/// that the gold's button opens its panel, are the UI cases' (units/pages-list.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class PagesListTests(Program program)
{
    private MainViewModel Main => program.Main;
    private CharacterSession Session => Main.Session;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private ContainerSectionViewModel Section => program.Section();

    private StashPageChoice Row(string name) => program.Choice(name);

    private IEnumerable<StashPageChoice> Rows => Section.Pages!.Where(page => !page.IsSeparator);

    [CorpusFact]
    public void A_click_shows_a_page_in_the_first_pane_and_Ctrl_click_or_its_menu_in_the_second_which_it_also_shuts()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            try
            {
                Section.SecondShown = false;

                // A click: the first pane. One pane up, no row wears a number.
                Page.ShowPageCommand.Execute(Row("Personal Stash"));
                Assert.Same(Row("Personal Stash"), Section.SelectedPage);
                Assert.True(Row("Personal Stash").IsShown);
                Assert.All(Rows, row => Assert.Equal("", row.PaneMark));
                Assert.Equal("Personal Stash", Section.FirstTitle);
                Assert.Single(Rows, row => row.IsShown);

                // Ctrl + click, or the row's menu: the second pane, opened on that page; the two rows and the two lines say which.
                Page.ShowSecond(Row("Shared Page 2"));
                Assert.True(Section.SecondShown);
                Assert.Same(Row("Shared Page 2"), Section.SecondPage);
                Assert.Equal("I", Row("Personal Stash").PaneMark);
                Assert.Equal("II", Row("Shared Page 2").PaneMark);
                Assert.Equal("", Row("Cube").PaneMark);
                Assert.Equal("I · Personal Stash", Section.FirstTitle);
                Assert.Equal("II · Shared Page 2", Section.SecondTitle);

                // What Ctrl + click does is a row's tooltip: opens, or - on the second pane's own - shuts. And the
                // row's menu says the same, with the pane a plain click shows it in and its lock.
                Assert.Equal("Ctrl + LMB: Close pane II", Row("Shared Page 2").RowHint);
                Assert.Equal("Ctrl + LMB: Show in pane II", Row("Cube").RowHint);
                Assert.Equal("Show in pane I; Close pane II; Lock", Page.MenuFor(Row("Shared Page 2")).Words());
                Assert.Equal("Show in pane I; Show in pane II; Lock", Page.MenuFor(Row("Cube")).Words());
                Assert.Equal("Ctrl + LMB", Page.MenuFor(Row("Cube")).Row("Show in pane II").Keys);

                // Another page's row of the menu picked: the second pane shows that one, and stays open.
                Page.MenuFor(Row("Cube")).Pick("Show in pane II");
                Assert.True(Section.SecondShown);
                Assert.Same(Row("Cube"), Section.SecondPage);
                Assert.Equal("", Row("Shared Page 2").PaneMark);

                // The first pane's page sent to the second: the two trade, so no page is shown twice.
                Page.ShowSecond(Row("Personal Stash"));
                Assert.Same(Row("Personal Stash"), Section.SecondPage);
                Assert.Same(Row("Cube"), Section.SelectedPage);

                // And the second pane's own page: shut, the numbers gone.
                Page.MenuFor(Row("Personal Stash")).Pick("Close pane II");
                Assert.False(Section.SecondShown);
                Assert.All(Rows, row => Assert.Equal("", row.PaneMark));
                Assert.Equal("Cube", Section.FirstTitle);
            }
            finally
            {
                Section.SecondShown = false;
                Section.SelectedPage = Row("Inventory");
            }
        });
    }

    [CorpusFact]
    public void Each_page_has_a_short_name_for_a_list_too_narrow_for_its_whole_one()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.Equal(
                ["Inv", "Cube", "PStash", "SP 1", "SP 2", "SP 3", "SP 4", "SP 5", "SP 6", "SP 7", "SP 8", "SP 9", "Mat"],
                Rows.Select(row => row.Short));

            // The whole name is the row's own: what its tooltip says, and what a pane's line shows.
            Assert.Equal("Shared Page 4", Rows.Single(row => row.Short == "SP 4").Label);
            Assert.Equal("Personal Stash", Rows.Single(row => row.Short == "PStash").Label);
        });
    }

    [CorpusFact]
    public void The_shared_stashs_gold_is_the_sections_with_the_carried_gold_beside_it()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.True(Section.HasSharedGold);
            var gold = Section.SharedGold!;
            Assert.Equal($"{program.Get<SharedStashSession>().Gold:N0} gold", gold.Words);
            Assert.Same(gold, Row("Shared Page 1").Purse);
            Assert.Same(gold, Row("Materials").Purse);

            // The carried gold goes with the inventory, above the shared stash's in the panel; the
            // character's own stash keeps none in Season 14.
            Assert.Same(Section.CarriedGold, Row("Inventory").Purse);
            Assert.NotNull(Section.GoldBetween);
            Assert.Null(Row("Personal Stash").Purse);
            Assert.Null(Row("Cube").Purse);
        });
    }

    [CorpusFact]
    public void The_arrows_move_the_panels_one_amount_between_the_carried_gold_and_the_shared_stashs()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var carried = Section.CarriedGold!;
            var shared = Section.SharedGold!;
            var between = Section.GoldBetween!;
            var kept = (Carried: carried.Value, Shared: shared.Value, Amount: shared.MoveAmount);
            try
            {
                carried.Value = "250000";
                shared.Value = "1000000";
                shared.MoveAmount = "100000";

                // The amount, down and back up; both purses' files follow.
                between.MoveDownCommand.Execute(null);
                Assert.Equal(150_000, carried.Held);
                Assert.Equal(1_100_000, shared.Held);
                Assert.Equal(1_100_000, program.Get<SharedStashSession>().Gold);
                between.MoveUpCommand.Execute(null);
                Assert.Equal(250_000, carried.Held);

                // More than the purse holds: what it holds. Shift: all of it, or the purse filled.
                shared.MoveAmount = "400000";
                between.MoveDownCommand.Execute(null);
                Assert.Equal(0, carried.Held);
                Assert.Equal(1_250_000, shared.Held);
                Assert.False(between.CanMoveDown);
                between.MoveAllUp();
                Assert.Equal(1_250_000, carried.Held);
                Assert.Equal(0, shared.Held);
                between.MoveAllDown();
                Assert.Equal(0, carried.Held);
                Assert.Equal(1_250_000, shared.Held);
            }
            finally
            {
                carried.Value = kept.Carried;
                shared.Value = kept.Shared;
                shared.MoveAmount = kept.Amount;
            }
        });
    }

    [CorpusFact]
    public void An_item_carried_and_let_go_on_a_row_goes_to_that_pages_first_free_room()
    {
        if (!program.Ready(changes: true))
            return;

        const string Shown = "Shared Page 5", Ring = "Steel Ring of Skill";
        program.On(() =>
        {
            Section.SecondShown = false;
            Page.ShowPageCommand.Execute(Row("Personal Stash"));
            Assert.Empty(program.Grid(Shown).Items);
            var ring = program.Tile(Ring, "Personal Stash");
            var item = ring.Item;
            var cell = ((int)item.X, (int)item.Y);
            int held = program.Grid("Personal Stash").Items.Count;

            // On the row of the page it lies on: put back, where it lay.
            Assert.True(Page.BeginCarry(ring));
            Page.HoverPage(Row("Personal Stash"));
            Assert.True(Row("Personal Stash").IsDropTarget);
            Page.DropCarriedOnPage(Row("Personal Stash"));
            Assert.False(Session.IsHolding);
            Assert.False(Row("Personal Stash").IsDropTarget);
            Assert.Equal(cell, ((int)item.X, (int)item.Y));
            Assert.Equal(held, program.Grid("Personal Stash").Items.Count);

            // On another page's row - one no pane shows: there, in its first free room.
            Assert.True(Page.BeginCarry(program.Tile(Ring, "Personal Stash")));
            Page.DropCarriedOnPage(Row(Shown));
            Assert.False(Session.IsHolding);
            var landed = Assert.Single(program.Grid(Shown).Items);
            Assert.Equal(Ring, landed.Name);
            Assert.Equal((0, 0), ((int)landed.Item.X, (int)landed.Item.Y));
            Assert.Equal(held - 1, program.Grid("Personal Stash").Items.Count);
            Assert.Same(Row("Personal Stash"), Section.SelectedPage);
        });
    }

    /// <summary>
    /// The list gives way to the grids (SidePanel): beside the main in the panel's middle with its own width
    /// where there is room, narrower where there is not and never under its least - and it says when it is
    /// squeezed, which is when the rows show their short names.
    /// </summary>
    [CorpusFact]
    public void The_list_stands_by_the_grid_and_gives_way_to_it()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            // A side that asks for 170 and can be given less, as a list of names cut short can.
            var side = new Avalonia.Controls.Border
            {
                Height = 300,
                ClipToBounds = true,
                Child = new Avalonia.Controls.Border { Width = 170 },
            };
            var main = new Avalonia.Controls.Border { Width = 285, Height = 400 };
            var panel = new SidePanel { Gap = 10, SideMinWidth = 100, SideTop = 32, Children = { side, main } };

            void Lay(double width)
            {
                panel.Measure(new Avalonia.Size(width, 1000));
                panel.Arrange(new Avalonia.Rect(0, 0, width, panel.DesiredSize.Height));
            }

            // Room for both: the two together in the middle, the list level with the grid under the pane's line.
            Lay(665);
            Assert.False(panel.IsSqueezed);
            Assert.Equal(new Avalonia.Rect(100, 32, 170, 300), side.Bounds);
            Assert.Equal(280, main.Bounds.X);
            Assert.Equal(400, panel.DesiredSize.Height);

            // Less than both ask: the grid whole, the list what is left.
            Lay(425);
            Assert.True(panel.IsSqueezed);
            Assert.Equal(130, side.Bounds.Width);
            Assert.Equal(285, main.Bounds.Width);
            Assert.Equal(0, side.Bounds.X);

            // And never under its least.
            Lay(300);
            Assert.True(panel.IsSqueezed);
            Assert.Equal(100, side.Bounds.Width);

            // With no list, the main has the whole panel - and stands in its middle, a grid of a width of its own.
            side.IsVisible = false;
            Lay(425);
            Assert.False(panel.IsSqueezed);
            Assert.Equal(285, panel.DesiredSize.Width);
            Assert.Equal(70, main.Bounds.X);
        });
    }
}
