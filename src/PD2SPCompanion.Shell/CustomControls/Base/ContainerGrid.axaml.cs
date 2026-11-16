using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// One container drawn the way the game draws it: ruled cells, the items laid over them,
/// their sockets and stack counts, and the cells you click to put something down.
/// <para>
/// It was the inventory page's, written inline there, and the shared stash page began life
/// with a second copy of the same thing under different names. <b>The controls were
/// identical and only the view model differed</b>, which is what this control is for - the
/// page hands it a <see cref="Source"/> and the commands to call, and nothing else about it
/// is a page's business.
/// </para>
/// <para>
/// The copy had already started to drift before it was a day old: it had no place targets,
/// no charm rule, no socket overlay and a poorer tooltip. That is the cost this removes.
/// </para>
/// </summary>
public partial class ContainerGrid : UserControl
{
    /// <summary>The container to draw, or null to draw nothing.</summary>
    public static readonly StyledProperty<GridViewModel?> SourceProperty =
        AvaloniaProperty.Register<ContainerGrid, GridViewModel?>(nameof(Source));

    /// <summary>Called with the tile that was clicked.</summary>
    public static readonly StyledProperty<ICommand?> ClickTileCommandProperty =
        AvaloniaProperty.Register<ContainerGrid, ICommand?>(nameof(ClickTileCommand));

    /// <summary>
    /// Called with the cell that was clicked while something is held. A page that does not
    /// let items be put down leaves this null, and no cell offers itself.
    /// </summary>
    public static readonly StyledProperty<ICommand?> PlaceAtCommandProperty =
        AvaloniaProperty.Register<ContainerGrid, ICommand?>(nameof(PlaceAtCommand));

    /// <summary>
    /// Whether what is not selected or picked steps back, so what is stands out.
    /// The page says so for its section; the rule is the grid's own, since a page's style does not reach
    /// these tiles - the section-wide dimming had stopped working when the grids became this control.
    /// </summary>
    public static readonly StyledProperty<bool> IsDimmedProperty =
        AvaloniaProperty.Register<ContainerGrid, bool>(nameof(IsDimmed));

    public bool IsDimmed
    {
        get => GetValue(IsDimmedProperty);
        set => SetValue(IsDimmedProperty, value);
    }

    /// <summary>Whether something is on the cursor, which is what makes the cells clickable.</summary>
    public static readonly StyledProperty<bool> IsHoldingProperty =
        AvaloniaProperty.Register<ContainerGrid, bool>(nameof(IsHolding));

    /// <summary>
    /// How plainly to draw what is in the sockets. The inventory page fades them with X so
    /// the item underneath can be seen, so the page owns the number rather than the control.
    /// </summary>
    public static readonly StyledProperty<double> SocketOpacityProperty =
        AvaloniaProperty.Register<ContainerGrid, double>(nameof(SocketOpacity), 1.0);

    /// <summary>A stretch of a sheet that runs on above and below: framed at its sides only.</summary>
    public static readonly StyledProperty<bool> IsBandProperty =
        AvaloniaProperty.Register<ContainerGrid, bool>(nameof(IsBand));

    public bool IsBand
    {
        get => GetValue(IsBandProperty);
        set => SetValue(IsBandProperty, value);
    }

    /// <summary>
    /// Joined to the block above it, as the materials tab's fourth rune row is to the other three:
    /// no top border, no top padding and square top corners, so its cells follow the block above's
    /// and cover its bottom frame - one box, as the game draws them.
    /// </summary>
    public static readonly StyledProperty<bool> IsJoinedAboveProperty =
        AvaloniaProperty.Register<ContainerGrid, bool>(nameof(IsJoinedAbove));

    public bool IsJoinedAbove
    {
        get => GetValue(IsJoinedAboveProperty);
        set => SetValue(IsJoinedAboveProperty, value);
    }

    public GridViewModel? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ICommand? ClickTileCommand
    {
        get => GetValue(ClickTileCommandProperty);
        set => SetValue(ClickTileCommandProperty, value);
    }

    public ICommand? PlaceAtCommand
    {
        get => GetValue(PlaceAtCommandProperty);
        set => SetValue(PlaceAtCommandProperty, value);
    }

    public bool IsHolding
    {
        get => GetValue(IsHoldingProperty);
        set => SetValue(IsHoldingProperty, value);
    }

    public double SocketOpacity
    {
        get => GetValue(SocketOpacityProperty);
        set => SetValue(SocketOpacityProperty, value);
    }

    /// <summary>
    /// The tiles' tooltips are held back: the grid is being scrolled. A wheel turned with the pointer resting
    /// on a page puts another item under it at every notch, and the tooltip of each was opened and closed in
    /// turn - a window made and thrown away a notch, four fifths of what the window was busy with while a
    /// long folder scrolled (traced: a significant lag when scrolling).
    /// </summary>
    public static readonly StyledProperty<bool> TipsOffProperty =
        AvaloniaProperty.Register<ContainerGrid, bool>(nameof(TipsOff));

    public bool TipsOff
    {
        get => GetValue(TipsOffProperty);
        set => SetValue(TipsOffProperty, value);
    }

    /// <summary>Whether a tile shows its tooltip: not while something is on the cursor, nor while tips are held back.</summary>
    public static readonly StyledProperty<bool> ShowsTipsProperty =
        AvaloniaProperty.Register<ContainerGrid, bool>(nameof(ShowsTips), true);

    public bool ShowsTips
    {
        get => GetValue(ShowsTipsProperty);
        private set => SetValue(ShowsTipsProperty, value);
    }

    public ContainerGrid()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsHoldingProperty || change.Property == TipsOffProperty)
            ShowsTips = !IsHolding && !TipsOff;

        // Held back means gone, at once: a tooltip left up over pages that have begun to move says an item
        // that is no longer under it: once scrolling starts, the tooltip disappears immediately.
        // The one tooltip that is open, where it is this grid's: every tile of every built page was looked
        // through for it at each scroll's start, 25 ms of a long folder's first notch.
        if (change.Property == TipsOffProperty && TipsOff
            && _tipOf?.TryGetTarget(out var tile) == true && ToolTip.GetIsOpen(tile)
            && ReferenceEquals(tile.FindAncestorOfType<ContainerGrid>(), this))
        {
            ToolTip.SetIsOpen(tile, false);
        }
    }

    /// <summary>The tile's anchor whose tooltip opened last, in any grid: the one to shut when the pages begin to move.</summary>
    private static System.WeakReference<TileAnchor>? _tipOf;

    /// <summary>
    /// A tile's tooltip - its anchor's, the one of two the layer lays over the tile under the pointer
    /// (TileLayer) - opens only while its grid shows tips (<see cref="ShowsTips"/>) - asked here as it
    /// opens, by class, where it was a binding on every tile: a scroll's start and end told every tile of
    /// every built page, one at a time.
    /// <para>
    /// <b>And it is made here, afresh at each opening</b>. The tile's <c>ToolTip.Tip</c> in the markup is
    /// the tile's model, which only keeps the tooltip service listening; drawn through a data template, as it
    /// was, the framework kept one ToolTip a tile and showed it again under a new popup each time - and from
    /// its second showing on nothing under the window could be read by UI Automation (E_FAIL: the tooltip's
    /// peer had lost its parent), a screen reader's loss and a test's. A control given as the tip is shown in
    /// a ToolTip of its own, so a new one each time is a first showing each time - as cheap as before, the
    /// template having built a new <see cref="ItemActionTip"/> at each opening too.
    /// </para>
    /// </summary>
    static ContainerGrid()
    {
        ToolTip.ToolTipOpeningEvent.AddClassHandler<TileAnchor>((tile, opening) =>
        {
            if (tile.FindAncestorOfType<ContainerGrid>() is not { } grid)
                return;

            if (!grid.ShowsTips || tile.DataContext is not ItemTileViewModel model)
            {
                opening.Cancel = true;
                return;
            }

            _tipOf = new System.WeakReference<TileAnchor>(tile);
            ToolTip.SetTip(tile, new ItemActionTip { Tile = model, Origin = PD2SPCompanion.Shell.Services.Generic.TipActions.Grid });
        });
    }
}
