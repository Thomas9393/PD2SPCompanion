using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;
using PD2SPCompanion.Shell.Views;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The real Items page beside the real Vault page in a window on no screen, over the program's own view models, as the
/// book shows them with the vault on the right: for what only the views decide, a press, a drag and where its release
/// lands. A pointer here takes the path a mouse takes, through the window's hit test and the views' handlers.
/// <para>
/// What the window itself adds is not here: the sidebar, the ghost under the pointer, its Esc. Everything is asked on the
/// program's thread, inside <c>program.On</c>; made inside one, and disposed of inside one.
/// </para>
/// </summary>
internal sealed class PageBench : IDisposable
{
    private readonly Program _program;

    /// <summary>The window both pages are in.</summary>
    public Avalonia.Controls.Window Window { get; }

    /// <summary>The Items page, on the left.</summary>
    public VP_View_Inv_L Items { get; }

    /// <summary>The Vault page, on the right.</summary>
    public VP_View_Vault_L Vault { get; }

    /// <param name="program">The program; its Items page has its panes set by the test, before or after.</param>
    public PageBench(Program program)
    {
        _program = program;
        var main = program.Main;
        main.ShowRightCommand.Execute(MainViewModel.VaultMark);
        main.RightVault!.Show();

        Items = new VP_View_Inv_L { DataContext = main.Inventory };
        Vault = new VP_View_Vault_L { DataContext = main.RightVault };

        var book = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(Vault, 1);
        book.Children.Add(Items);
        book.Children.Add(Vault);

        Window = new Avalonia.Controls.Window { Width = 1800, Height = 1000, Background = Brushes.Black, Content = book };
        Window.Show();
        Pump(30);
    }

    /// <summary>What the program posted to itself done, and a frame drawn: hit tests go by the last frame.</summary>
    public static void Pump(int rounds = 6)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }
    }

    /// <summary>The middle of a tile drawn on either page, in the window. Fails where it is not drawn.</summary>
    public Point At(ItemTileViewModel tile)
    {
        var drawn = TileLayer.Find(Window, tile) ?? throw new InvalidOperationException($"{tile.Name} is not drawn.");
        return drawn.Layer.TranslatePoint(drawn.Place.Center, Window)!.Value;
    }

    /// <summary>The middle of a cell of a container the Items page's panes show, in the window.</summary>
    public Point AtCell(string container, int column, int row)
    {
        var grid = _program.Choice(container).Grid!;
        var layer = Items.GetVisualDescendants().OfType<CellLayer>()
            .FirstOrDefault(each => each.IsEffectivelyVisible && each.CellAt(default)?.Page == grid.Page && each.CellAt(default)?.Shared == grid.Shared)
            ?? throw new InvalidOperationException($"{container} is not shown.");
        double side = ItemTileViewModel.CellSize;
        return layer.TranslatePoint(new Point((column + 0.5) * side, (row + 0.5) * side), Window)!.Value;
    }

    /// <summary>The middle of the Vault page's sheet, in the window: a drop there goes into the folder it shows.</summary>
    public Point AtSheet()
    {
        var sheet = Vault.GetVisualDescendants().OfType<ScrollViewer>().First(each => each.Name == "PagesScroll");
        return sheet.TranslatePoint(new Point(sheet.Bounds.Width / 2, sheet.Bounds.Height / 2), Window)!.Value;
    }

    /// <summary>A press where the pointer has first moved to, as a hand does.</summary>
    public void Press(Point at)
    {
        Window.MouseMove(at);
        Pump(2);
        Window.MouseDown(at, MouseButton.Left);
        Pump(2);
    }

    /// <summary>The held button moved to a place, by steps.</summary>
    public void MoveTo(Point from, Point to, int steps = 8)
    {
        for (int step = 1; step <= steps; step++)
        {
            var at = from + (to - from) * ((double)step / steps);
            Window.MouseMove(at, RawInputModifiers.LeftMouseButton);
            Pump(1);
        }

        Pump(2);
    }

    /// <summary>The button let go of here.</summary>
    public void Release(Point at)
    {
        Window.MouseUp(at, MouseButton.Left);
        Pump();
    }

    /// <summary>A whole drag: pressed, carried and let go of.</summary>
    public void Drag(Point from, Point to)
    {
        Press(from);
        MoveTo(from, to);
        Release(to);
    }

    public void Dispose()
    {
        Window.Close();
        Pump(2);
    }
}
