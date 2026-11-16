using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// What the Items page's drag and its keys ask of the page. The right button comes as a move
/// during a drag, Esc is the window's, and a release lands on what lies under the pointer: that those reach this is a
/// window's to show. Asked here is what each then does: what is carried goes back, the jewel's layer is found on the page
/// in sight, a grid under the header counts as out of sight, and a rectangle remembers each item as it first saw it.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class CarryAndClipTests(Program program)
{
    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    [CorpusFact]
    public void What_a_drag_carries_is_put_back_with_nothing_moved()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            Assert.False(Page.PutBackCarried());

            var tile = program.Tile("Witchwild String", "Personal Stash");
            var item = tile.Item;
            var at = (item.X, item.Y);
            Assert.True(Page.BeginCarry(tile));
            Assert.True(Page.Session.IsHolding);

            Assert.True(Page.PutBackCarried());
            Assert.False(Page.Session.IsHolding);
            Assert.Equal(at, (item.X, item.Y));
        });
    }

    [CorpusFact]
    public void A_jewels_layer_is_found_on_the_Edit_page_in_sight_and_not_behind_another_bookmark()
    {
        if (!program.Ready())
            return;

        var main = program.Main;
        program.On(() =>
        {
            var edit = program.EditPage();
            Assert.Null(main.EditLayerInSight);

            program.Select("Stone Barb");
            edit.Sockets.OpenJewelCommand.Execute(edit.Sockets.Rows.First(row => row.IsJewel));
            try
            {
                Assert.Same(edit, main.EditLayerInSight);

                // Another bookmark on the right page: nothing of it is in sight, and Esc is the left page's again.
                main.ShowRightCommand.Execute(MainViewModel.SearchMark);
                Assert.Null(main.EditLayerInSight);
                main.ShowRightCommand.Execute(MainViewModel.EditMark);
                Assert.Same(edit, main.EditLayerInSight);
            }
            finally
            {
                edit.CloseNestedCommand.Execute(null);
                program.Deselect();
            }

            Assert.Null(main.EditLayerInSight);
        });
    }

    [CorpusFact]
    public void A_point_of_a_control_scrolled_under_the_header_is_out_of_sight_and_one_in_the_scroller_is_not()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grid = new Border { Height = 400, Width = 200 };
            var scroller = new ScrollViewer { Height = 100, Content = grid, VerticalAlignment = VerticalAlignment.Top };
            var header = new Border { Height = 50 };
            var page = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(header, Dock.Top);
            page.Children.Add(header);
            page.Children.Add(scroller);

            var window = new Avalonia.Controls.Window { Width = 300, Height = 300, Content = page };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                scroller.Offset = new Vector(0, 100);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                // A hundred down the grid is the scroller's top; ninety is under the header above it.
                Assert.True(ItemPanels.InSight(grid, new Point(10, 110)));
                Assert.False(ItemPanels.InSight(grid, new Point(10, 90)));

                // Past the scroller's foot.
                Assert.False(ItemPanels.InSight(grid, new Point(10, 250)));

                // In no scroller at all: nothing clips it.
                Assert.True(ItemPanels.InSight(header, new Point(10, 10)));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [CorpusFact]
    public void A_rectangle_remembers_an_item_as_it_first_saw_it()
    {
        var before = new PickedBefore();
        object picked = new(), plain = new(), builtLater = new();

        // The drag's start, over what is built.
        Assert.True(before.Seen(picked, pickedNow: true));
        Assert.False(before.Seen(plain, pickedNow: false));

        // A row built later, by the scroll at the edge, holding a pick: picked before, whatever the rectangle does after.
        Assert.True(before.Seen(builtLater, pickedNow: true));
        Assert.True(before.Seen(builtLater, pickedNow: false));

        // The rectangle has since touched these: what they were before it stands.
        Assert.False(before.Seen(plain, pickedNow: true));
        Assert.True(before.Seen(picked, pickedNow: false));

        before.Clear();
        Assert.False(before.Seen(picked, pickedNow: false));
    }
}
