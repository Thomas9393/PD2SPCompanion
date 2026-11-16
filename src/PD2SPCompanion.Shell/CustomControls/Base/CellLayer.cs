using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A grid's cells, drawn by one control rather than a control per cell (the idea: bake
/// them into a big texture and map to coordinates). The ruled lines and the lit targets
/// under a carried item are painted here, and a cell is found from a point by arithmetic - so a
/// 10x10 stash page is one control, not four hundred.
/// <para>
/// Takes the pointer only while something is held, as the per-cell buttons it replaces were only
/// visible then: an empty cell clicked with nothing held is the page's to handle (a click on
/// nothing clears the selection).
/// </para>
/// </summary>
public sealed class CellLayer : Control, ICustomHitTest
{
    public static readonly StyledProperty<GridViewModel?> SourceProperty =
        AvaloniaProperty.Register<CellLayer, GridViewModel?>(nameof(Source));

    public static readonly StyledProperty<bool> IsHoldingProperty =
        AvaloniaProperty.Register<CellLayer, bool>(nameof(IsHolding));

    public static readonly StyledProperty<ICommand?> PlaceAtCommandProperty =
        AvaloniaProperty.Register<CellLayer, ICommand?>(nameof(PlaceAtCommand));

    // The colours the per-cell borders used (ContainerGrid.axaml's cell and target styles).
    private static readonly IPen Rule = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#2EA79066")), 1);
    private static readonly IBrush Refused = new ImmutableSolidColorBrush(Color.Parse("#3CFF3322"));
    private static readonly IBrush Allowed = new ImmutableSolidColorBrush(Color.Parse("#3C00FF66"));
    // Amber, not red: it would go there, it just would not do anything (an idle charm's place).
    private static readonly IBrush Idle = new ImmutableSolidColorBrush(Color.Parse("#46FFAA00"));

    static CellLayer()
    {
        AffectsRender<CellLayer>(SourceProperty);
        // Followed only while on screen: attaching follows the grid of the moment itself.
        SourceProperty.Changed.AddClassHandler<CellLayer>((layer, change) =>
        {
            if (layer._attached)
                layer.Follow(change.OldValue as GridViewModel, change.NewValue as GridViewModel);
            else
                layer.InvalidateMeasure();
        });
        IsHoldingProperty.Changed.AddClassHandler<CellLayer>((layer, _) =>
            layer.Cursor = layer.IsHolding ? new Cursor(StandardCursorType.Hand) : null);
    }

    public GridViewModel? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool IsHolding
    {
        get => GetValue(IsHoldingProperty);
        set => SetValue(IsHoldingProperty, value);
    }

    public ICommand? PlaceAtCommand
    {
        get => GetValue(PlaceAtCommandProperty);
        set => SetValue(PlaceAtCommandProperty, value);
    }

    /// <summary>The cell at a point in this control's own coordinates, or null past the grid.</summary>
    public GridCell? CellAt(Point point)
    {
        if (Source is not { } grid || point.X < 0 || point.Y < 0)
            return null;

        int column = (int)(point.X / ItemTileViewModel.CellSize);
        int row = (int)(point.Y / ItemTileViewModel.CellSize);

        return column < grid.Columns && row < grid.Rows ? grid.Cells[row * grid.Columns + column] : null;
    }

    /// <summary>Hit only while something is held, and only over a cell.</summary>
    public bool HitTest(Point point) => IsHolding && CellAt(point) is not null;

    protected override Size MeasureOverride(Size availableSize) =>
        Source is { } grid
            ? new Size(grid.Columns * ItemTileViewModel.CellSize, grid.Rows * ItemTileViewModel.CellSize)
            : default;

    public override void Render(DrawingContext context)
    {
        if (Source is not { } grid)
            return;

        double size = ItemTileViewModel.CellSize;

        foreach (var cell in grid.Cells)
        {
            // Inset half a pixel, as a one-pixel border inside its own box is drawn.
            var box = new Rect(cell.Left + 0.5, cell.Top + 0.5, size - 1, size - 1);

            if (cell.IsTarget)
                context.FillRectangle(!cell.IsAllowed ? Refused : cell.IsIdle ? Idle : Allowed, box);

            context.DrawRectangle(null, Rule, box);
        }
    }

    private GridCell? _pressed;

    private bool _attached;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsHolding || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _pressed = CellAt(e.GetPosition(this));
        e.Handled = _pressed is not null;
    }

    /// <summary>A click on a cell - down and up on the same one - puts the held item there.</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var pressed = _pressed;
        _pressed = null;

        if (pressed is null || e.InitialPressMouseButton != MouseButton.Left
            || !ReferenceEquals(CellAt(e.GetPosition(this)), pressed))
        {
            return;
        }

        if (PlaceAtCommand?.CanExecute(pressed) == true)
            PlaceAtCommand.Execute(pressed);

        e.Handled = true;
    }

    // ---- redrawn when a cell lights or goes dark -------------------------------------------------

    private void Follow(GridViewModel? old, GridViewModel? now)
    {
        if (old is not null)
        {
            foreach (var cell in old.Cells)
                cell.PropertyChanged -= OnCellChanged;
        }

        if (now is not null)
        {
            foreach (var cell in now.Cells)
                cell.PropertyChanged += OnCellChanged;
        }

        InvalidateMeasure();
    }

    private void OnCellChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // A grid outlives the page it was shown on; the handlers go with the control.
        Follow(Source, null);
        _attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Follow(null, Source);
    }
}
