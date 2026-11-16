using System.Collections.Generic;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// One row of a vault folder's view: a stretch of its sheet, as a grid kept while the row is handed another -
/// a reload hands every row its stretch read again, and a grid told its new one builds its tiles and nothing
/// else. Made and kept by <see cref="PageRowsHost"/>.
/// </summary>
public sealed class PageRowView : StackPanel
{
    /// <summary>The pages of the row, left to right.</summary>
    public static readonly StyledProperty<IReadOnlyList<VaultPageViewModel>?> PagesProperty =
        AvaloniaProperty.Register<PageRowView, IReadOnlyList<VaultPageViewModel>?>(nameof(Pages));

    /// <summary>Called with the tile that was clicked, on any of the row's pages.</summary>
    public static readonly StyledProperty<ICommand?> ClickTileCommandProperty =
        AvaloniaProperty.Register<PageRowView, ICommand?>(nameof(ClickTileCommand));

    public IReadOnlyList<VaultPageViewModel>? Pages
    {
        get => GetValue(PagesProperty);
        set => SetValue(PagesProperty, value);
    }

    public ICommand? ClickTileCommand
    {
        get => GetValue(ClickTileCommandProperty);
        set => SetValue(ClickTileCommandProperty, value);
    }

    /// <summary>The tiles' tooltips held back, on every page of the row (<see cref="ContainerGrid.TipsOff"/>).</summary>
    public static readonly StyledProperty<bool> TipsOffProperty =
        AvaloniaProperty.Register<PageRowView, bool>(nameof(TipsOff));

    public bool TipsOff
    {
        get => GetValue(TipsOffProperty);
        set => SetValue(TipsOffProperty, value);
    }

    /// <summary>The tiles not selected or picked step back, on every page of the row (<see cref="ContainerGrid.IsDimmed"/>).</summary>
    public static readonly StyledProperty<bool> IsDimmedProperty =
        AvaloniaProperty.Register<PageRowView, bool>(nameof(IsDimmed));

    public bool IsDimmed
    {
        get => GetValue(IsDimmedProperty);
        set => SetValue(IsDimmedProperty, value);
    }

    /// <summary>How plainly the sockets over an item's art are drawn, on every page of the row (<see cref="ContainerGrid.SocketOpacity"/>).</summary>
    public static readonly StyledProperty<double> SocketOpacityProperty =
        AvaloniaProperty.Register<PageRowView, double>(nameof(SocketOpacity), 1.0);

    public double SocketOpacity
    {
        get => GetValue(SocketOpacityProperty);
        set => SetValue(SocketOpacityProperty, value);
    }

    public PageRowView()
    {
        Orientation = Orientation.Horizontal;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PagesProperty)
            Lay();
        else if (change.Property == ClickTileCommandProperty || change.Property == TipsOffProperty || change.Property == SocketOpacityProperty
                 || change.Property == IsDimmedProperty)
        {
            foreach (var child in Children)
            {
                if (child is ContainerGrid grid)
                {
                    grid.ClickTileCommand = ClickTileCommand;
                    grid.TipsOff = TipsOff;
                    grid.SocketOpacity = SocketOpacity;
                    grid.IsDimmed = IsDimmed;
                }
            }
        }
    }

    /// <summary>A grid a page: those there are told their pages, more are made where the row has grown, the rest go.</summary>
    private void Lay()
    {
        var pages = Pages ?? [];

        while (Children.Count > pages.Count)
            Children.RemoveAt(Children.Count - 1);

        for (int i = 0; i < pages.Count; i++)
        {
            if (i == Children.Count)
                Children.Add(new ContainerGrid
                {
                    IsBand = true,

                    // Not cut at its own edge, as every other grid is: an item whose top is on this stretch's
                    // last row reaches down over the next, and was drawn to the join and no further (probed:
                    // cut with the stretch below not built at all). A templated control clips
                    // unless told not to: this grid, and the list of tiles inside it (ContainerGrid.axaml).
                    ClipToBounds = false,
                    ClickTileCommand = ClickTileCommand,
                    TipsOff = TipsOff,
                    SocketOpacity = SocketOpacity,
                    IsDimmed = IsDimmed,
                });

            // The page is the grid's context too: the view finds the page under the pointer by it.
            var grid = (ContainerGrid)Children[i];
            grid.DataContext = pages[i];
            grid.Source = pages[i].Grid;
        }
    }
}
