using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using CommunityToolkit.Mvvm.Input;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;
using PD2SPCompanion.Shell.Views;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Every item of a grid drawn by one control (TileLayer).
/// No item is a control: the layer paints them, finds the one
/// under the pointer by arithmetic, and gives what a button gave each - the click, the tooltip's anchor, the
/// peer UI Automation finds an item by. The few controls left lie over the tiles that show a mark.
/// <para>
/// With a pointer the headless window is given, as a window is given a mouse. That the layer paints what the
/// buttons painted was compared pixel for pixel against them when it was made (corners within 2 in 255); what
/// only a real window shows - a tooltip opening, a drag across pages, UI Automation from outside - is the UI
/// cases'.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class TileLayerTests(Program program)
{
    private static void Pump(int rounds = 6)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }
    }

    /// <summary>Tiles of the character's own items laid down a sheet, new ones: nothing of the page's is touched.</summary>
    private List<ItemTileViewModel> Tiles(Func<ItemTileViewModel, bool> of, int count)
    {
        var items = new[] { "Inventory", "Personal Stash" }
            .SelectMany(name => program.Grid(name).Items).Where(of).Select(tile => tile.Item).ToList();
        Assert.NotEmpty(items);
        return [.. Enumerable.Range(0, count).Select(i => new ItemTileViewModel(items[i % items.Count], i, i % 5 * 2, i / 5 * 4))];
    }

    private static bool Plain(ItemTileViewModel tile) =>
        !tile.HasSockets && !tile.HasQuantity && tile.View.GridWidth == 1;

    /// <summary>
    /// A grid of tiles in a window of its own, laid out: the window, its layer, and what was clicked. The grid
    /// is no taller than the window: it lies in the window's middle, and of a taller one the first rows were
    /// above the window's top - out of sight, and under no pointer.
    /// </summary>
    private static (Avalonia.Controls.Window Window, TileLayer Layer, List<ItemTileViewModel> Clicked) Shown(IReadOnlyList<ItemTileViewModel> tiles)
    {
        var clicked = new List<ItemTileViewModel>();
        var grid = new GridViewModel("Sheet", 9, 10, 20);
        grid.Fill(tiles);
        var window = new Avalonia.Controls.Window
        {
            Width = 400, Height = 600, Background = Brushes.Black,
            Content = new ContainerGrid { Source = grid, ClickTileCommand = new RelayCommand<ItemTileViewModel>(tile => clicked.Add(tile!)) },
        };
        window.Show();
        Pump();
        return (window, window.GetVisualDescendants().OfType<TileLayer>().Single(), clicked);
    }

    private static Point Middle(Avalonia.Controls.Window window, TileLayer layer, ItemTileViewModel tile) =>
        layer.TranslatePoint(TileLayer.PlaceOf(tile).Center, window)!.Value;

    [CorpusFact]
    public void A_grids_items_are_one_control_whatever_their_number()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var (few, fewLayer, _) = Shown(Tiles(_ => true, 3));
            int with3 = few.GetVisualDescendants().Count();
            few.Close();

            var (many, layer, _) = Shown(Tiles(_ => true, 60));
            try
            {
                Assert.Equal(60, layer.Tiles.Count);
                Assert.Equal(with3, many.GetVisualDescendants().Count());

                // Under the layer: the two anchors a tooltip opens from, and nothing an item.
                Assert.All(layer.GetVisualChildren(), child => Assert.IsType<TileAnchor>(child));
                Assert.Equal(2, layer.GetVisualChildren().Count());
            }
            finally
            {
                many.Close();
            }
        });
    }

    [CorpusFact]
    public void The_item_at_a_point_is_found_by_where_the_tiles_lie()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var tiles = Tiles(Plain, 6);
            var (window, layer, _) = Shown(tiles);
            try
            {
                // Laid at columns 0, 2, 4, 6, 8 of the first row, and column 0 four rows down.
                Assert.Same(tiles[0], layer.TileAt(new Point(14, 14)));
                Assert.Same(tiles[1], layer.TileAt(new Point(2 * 28 + 1, 27)));
                Assert.Same(tiles[5], layer.TileAt(new Point(14, 4 * 28 + 14)));

                // Between two tiles and under the last: no item, and the layer does not take the pointer there.
                Assert.Null(layer.TileAt(new Point(28 + 14, 14)));
                Assert.False(layer.HitTest(new Point(28 + 14, 14)));
                Assert.True(layer.HitTest(new Point(14, 14)));

                Assert.Equal(new Rect(2 * 28, 0, 28, 28), layer.RectOf(tiles[1]));
                Assert.Null(layer.RectOf(Tiles(Plain, 1)[0]));
                Assert.Equal((layer, new Rect(0, 4 * 28, 28, 28)), TileLayer.Find(window, tiles[5]));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [CorpusFact]
    public void A_press_and_a_release_on_one_item_click_it_and_the_pointer_over_it_brings_its_anchor()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var tiles = Tiles(Plain, 6);
            var (window, layer, clicked) = Shown(tiles);
            try
            {
                Point first = Middle(window, layer, tiles[0]), second = Middle(window, layer, tiles[1]);
                var anchors = layer.GetVisualChildren().OfType<TileAnchor>().ToList();

                // Over the first: one anchor over it, the item its DataContext - what a tooltip opens from, and
                // what walking up from what was pressed finds.
                window.MouseMove(first);
                Pump();
                var over = Assert.Single(anchors, anchor => anchor.IsVisible);
                Assert.Same(tiles[0], over.DataContext);
                Assert.Equal(TileLayer.PlaceOf(tiles[0]), over.Bounds);

                // On to the second: the other anchor, so that it is a leaving and a coming.
                window.MouseMove(second);
                Pump();
                var next = Assert.Single(anchors, anchor => anchor.IsVisible);
                Assert.NotSame(over, next);
                Assert.Same(tiles[1], next.DataContext);

                // A click.
                window.MouseDown(second, MouseButton.Left);
                window.MouseUp(second, MouseButton.Left);
                Pump();
                Assert.Equal([tiles[1]], clicked);

                // Pressed on one and let go on another: no click of either.
                window.MouseDown(second, MouseButton.Left);
                window.MouseMove(first, RawInputModifiers.LeftMouseButton);
                window.MouseUp(first, MouseButton.Left);
                Pump();
                Assert.Equal([tiles[1]], clicked);

                // The right button is no click.
                window.MouseDown(first, MouseButton.Right);
                window.MouseUp(first, MouseButton.Right);
                Pump();
                Assert.Equal([tiles[1]], clicked);

                // Off the tiles: no anchor.
                window.MouseMove(new Point(first.X + 28, first.Y));
                Pump();
                Assert.DoesNotContain(anchors, anchor => anchor.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [CorpusFact]
    public void A_mark_that_is_a_control_lies_over_its_tile_only_while_the_tile_shows_it()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var tiles = Tiles(Plain, 4);
            var (window, layer, _) = Shown(tiles);
            try
            {
                int bare = layer.GetVisualChildren().Count();

                // An arrow: a control over that tile, inside its frame.
                tiles[2].IsLeaving = true;
                Pump();
                var extra = Assert.Single(layer.GetVisualChildren(), child => child is not TileAnchor);
                Assert.Same(tiles[2], ((Avalonia.Controls.Control)extra).DataContext);
                Assert.True(TileLayer.PlaceOf(tiles[2]).Contains(extra.Bounds));

                // A second mark on the same tile: still the one control.
                tiles[2].DuplicatePlaces = ["Inventory", "Vault"];
                Pump();
                Assert.Same(extra, Assert.Single(layer.GetVisualChildren(), child => child is not TileAnchor));

                // The last mark gone: taken away.
                tiles[2].IsLeaving = false;
                tiles[2].DuplicatePlaces = null;
                Pump();
                Assert.Equal(bare, layer.GetVisualChildren().Count());

                // What is only painted - the ring, the reds - adds no control.
                tiles[1].IsPicked = true;
                tiles[1].IdleCharm = PD2SPCompanion.Core.Characters.CharmIdle.OutsideArea;
                Pump();
                Assert.Equal(bare, layer.GetVisualChildren().Count());
            }
            finally
            {
                window.Close();
            }

            // Out of the window the layer no longer listens: a mark set then builds nothing on it.
            tiles[0].IsLeaving = true;
            Pump();
            Assert.All(layer.GetVisualChildren(), child => Assert.IsType<TileAnchor>(child));
            Assert.Empty(layer.Tiles);
        });
    }

    [CorpusFact]
    public void UI_Automation_is_told_of_each_item_as_it_was_of_a_tiles_button()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var tiles = Tiles(Plain, 3);
            var stack = Tiles(tile => tile.HasQuantity, 1);
            stack[0].MoveTo(99, 9, 0);
            var (window, layer, clicked) = Shown([.. tiles, .. stack]);
            try
            {
                var peers = ControlAutomationPeer.CreatePeerForElement(layer).GetChildren();
                Assert.Equal(4, peers.Count);

                var second = peers[1];
                Assert.Equal("Tile", second.GetAutomationId());
                Assert.Equal(tiles[1].Name, second.GetName());
                Assert.Equal(AutomationControlType.Button, second.GetAutomationControlType());
                Assert.False(second.IsOffscreen());

                // Where it is drawn, in the window's own coordinates.
                var at = layer.TranslatePoint(TileLayer.PlaceOf(tiles[1]).TopLeft, window)!.Value;
                Assert.Equal(new Rect(at, new Size(28, 28)), second.GetBoundingRectangle());

                // Invoke is the item's click.
                Assert.IsAssignableFrom<IInvokeProvider>(second).Invoke();
                Assert.Equal([tiles[1]], clicked);

                // A stack's count is a text under its item; a plain item holds none.
                Assert.Empty(second.GetChildren());
                var count = Assert.Single(peers[3].GetChildren());
                Assert.Equal(AutomationControlType.Text, count.GetAutomationControlType());
                Assert.Equal(stack[0].QuantityText, count.GetName());
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Found by the window cases the day the layer was made: a press on an item was taken for a press on the
    /// page's own ground - it had asked whether what was pressed was a button - and let the picks go before the
    /// drag that was to carry them.
    /// </summary>
    [CorpusFact]
    public void A_drag_off_one_of_several_picked_carries_them_all()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var page = program.Main.Inventory!;
            program.Panes("Personal Stash");
            var window = new Avalonia.Controls.Window { Width = 900, Height = 1000, Background = Brushes.Black, Content = new VP_View_Inv_L { DataContext = page } };
            window.Show();
            Pump(30);
            try
            {
                var first = program.Tile("Steel Ring of Skill", "Personal Stash");
                var second = program.Tile("Felicitous Ring of Regrowth", "Personal Stash");
                Point At(ItemTileViewModel tile)
                {
                    var (layer, place) = TileLayer.Find(window, tile)!.Value;
                    return layer.TranslatePoint(place.Center, window)!.Value;
                }

                Point a = At(first), b = At(second);

                // A click selects; Ctrl + click on another picks the two.
                window.MouseMove(a);
                window.MouseDown(a, MouseButton.Left);
                window.MouseUp(a, MouseButton.Left);
                Pump();
                Assert.Same(first, page.Session.SelectedItem);

                window.MouseMove(b, RawInputModifiers.Control);
                window.MouseDown(b, MouseButton.Left, RawInputModifiers.Control);
                window.MouseUp(b, MouseButton.Left, RawInputModifiers.Control);
                Pump();
                Assert.True(page.IsPicking);
                Assert.True(first.IsPicked && second.IsPicked);

                // Pressed on one of them: the picks stay. Dragged: both are carried.
                window.MouseMove(a);
                window.MouseDown(a, MouseButton.Left);
                Pump(2);
                Assert.True(first.IsPicked && second.IsPicked, "a press on a picked item let the picks go");

                for (int step = 1; step <= 6; step++)
                    window.MouseMove(new Point(a.X + step * 6, a.Y + step * 3), RawInputModifiers.LeftMouseButton);
                Pump();
                Assert.True(page.Session.IsHolding);
                Assert.Equal(1, page.Session.HeldGroup.Count);
            }
            finally
            {
                if (page.Session.IsHolding)
                    page.CancelHold();
                page.ClearSelection();
                window.Close();
                program.Section().SelectedPage = program.Choice("Inventory");
            }
        });
    }
}
