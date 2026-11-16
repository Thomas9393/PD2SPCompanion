using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A folder's sheet in small, beside it where the scroll bar stood: every item a block of its rarity's colour
/// where it lies, and over them a window on what the sheet shows now - pressed or dragged, the sheet goes there
/// (a scroll bar with a small preview window, as in VS Code).
/// A folder longer than the map is tall slides under it as the sheet scrolls, as the editor's does. While a
/// search stands, what it found stays and the rest is faint. One frame round the whole, not a frame of its
/// own for each page.
/// <para>
/// Drawn, not built of controls: a folder is thousands of items and this is one picture of them.
/// </para>
/// </summary>
public sealed class PagesMinimap : Control
{
    /// <summary>One cell of the sheet, on the map: three, and less for a sheet too wide for that.</summary>
    private double Cell => CellFor(_columns);

    private static double CellFor(int columns) => columns <= 30 ? 3 : 90.0 / columns;

    /// <summary>How wide the map of a sheet of so many columns is: what a page asks before it has room for one.</summary>
    public static double WidthFor(int columns) => Math.Max(1, columns) * CellFor(Math.Max(1, columns)) + 2 * Gap;

    /// <summary>Round the map.</summary>
    private const double Gap = 2;

    private IReadOnlyList<VaultMark> _marks = [];
    private IReadOnlySet<long>? _hits;
    private int _columns = VaultLayout.DefaultColumns, _pages = 1;
    private double _offset, _viewport = 1, _extent = 1;

    /// <summary>The pages went to this far down their scroll: asked for by a press or a drag on the map.</summary>
    public event Action<double>? Sought;

    /// <summary>The map was taken hold of, or let go: while it is held the view says which pages are in sight.</summary>
    public event Action<bool>? Held;

    /// <summary>The middle of the window on the map, down from the map's top: where the view puts what it says.</summary>
    public double WindowMiddle
    {
        get
        {
            var (top, tall) = Window();
            return top + tall / 2;
        }
    }

    public PagesMinimap()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);

        // Three layers: the ground (this control's own), the marks, the window. The marks are drawn again only when
        // the folder or the search changes, and slide under a transform as the pages scroll; only the window is drawn
        // at each step. Drawn whole at each step of a scroll, a long folder's every mark was recorded again for every
        // notch of the wheel - 2,822 on a season's vault.
        _marksLayer = new Layer(DrawMarks) { RenderTransform = _slide };
        _windowLayer = new Layer(DrawWindow);
        VisualChildren.Add(_marksLayer);
        VisualChildren.Add(_windowLayer);
    }

    private readonly Layer _marksLayer, _windowLayer;
    private readonly TranslateTransform _slide = new();

    /// <summary>A drawn layer of the map, over the whole of it, that the pointer passes through to the map.</summary>
    private sealed class Layer(Action<DrawingContext> draw) : Control
    {
        public Layer() : this(_ => { }) { }

        public override void Render(DrawingContext context) => draw(context);

        protected override void OnInitialized()
        {
            base.OnInitialized();
            IsHitTestVisible = false;
        }
    }

    /// <summary>
    /// The map's corners (2px rounded): the map cut to them, and its window
    /// rounded only where it meets the map's top or foot - square elsewhere.
    /// </summary>
    private const double Corner = 2;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var whole = new Rect(finalSize);
        Clip = new RectangleGeometry(whole, Corner, Corner);
        _marksLayer.Arrange(whole);
        _windowLayer.Arrange(whole);
        Slide();
        return finalSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _marksLayer.Measure(availableSize);
        _windowLayer.Measure(availableSize);
        return base.MeasureOverride(availableSize);
    }

    /// <summary>The marks slid up under the frame as far as the pages have scrolled.</summary>
    private void Slide() => _slide.Y = -Laid().Slid;

    /// <summary>What to draw: the folder's items, the sheet's width in columns and how many stretches of it there are, and a search's finds.</summary>
    public void Show(IReadOnlyList<VaultMark> marks, int columns, int pages, IReadOnlySet<long>? hits)
    {
        _marks = marks;
        _columns = Math.Max(1, columns);
        _pages = Math.Max(1, pages);
        _hits = hits;

        Width = WidthFor(_columns);
        _marksLayer.InvalidateVisual();
        _windowLayer.InvalidateVisual();
        Slide();
    }

    /// <summary>Where the pages are scrolled to: how far down, how much is in sight, how long the whole is.</summary>
    public void Scrolled(double offset, double viewport, double extent)
    {
        (_offset, _viewport, _extent) = (offset, Math.Max(1, viewport), Math.Max(1, extent));
        Slide();
        _windowLayer.InvalidateVisual();
    }

    /// <summary>The whole map's height, and how far it has slid up under its frame: none while it fits.</summary>
    private (double Height, double Slid) Laid()
    {
        double height = _pages * Vault.PageRows * Cell + 2 * Gap;
        double room = Bounds.Height;
        if (height <= room || _extent <= _viewport)
            return (height, 0);

        return (height, Math.Clamp(_offset / (_extent - _viewport), 0, 1) * (height - room));
    }

    public override void Render(DrawingContext context) =>
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));

    /// <summary>The frame and every mark, at the map's own top: <see cref="_slide"/> puts them where the scroll has them.</summary>
    private void DrawMarks(DrawingContext context)
    {
        var frame = new Pen(Brush("[Brush] Frame - Golden Dim") ?? Brushes.DimGray, 1);
        double cell = Cell;
        context.DrawRectangle(null, frame, new Rect(Gap, Gap, _columns * cell, _pages * Vault.PageRows * cell).Inflate(-0.5));

        foreach (var mark in _marks)
        {
            var block = new Rect(Gap + mark.X * cell, Gap + mark.Row * cell, mark.Width * cell, mark.Height * cell).Deflate(0.5);

            // Each in its rarity's color; with a search standing the finds stay and the rest step back, as on
            // the sheet - no green.
            bool found = _hits?.Contains(mark.Key) == true;
            var brush = Colour(mark.Quality);

            using (context.PushOpacity(_hits is null ? 0.85 : found ? 1 : 0.22))
                context.FillRectangle(brush, block);
        }
    }

    /// <summary>The window on what the pages show.</summary>
    private void DrawWindow(DrawingContext context)
    {
        var (top, tall) = Window();
        var window = new Rect(0.5, top + 0.5, Math.Max(0, Bounds.Width - 1), tall - 1);

        // Its line follows the map's corner where it meets the top or the foot; the line's middle is half a unit in.
        double atTop = top <= 0.5 ? Corner - 0.5 : 0;
        double atFoot = top + tall >= Bounds.Height - 0.5 ? Corner - 0.5 : 0;
        var shape = new RoundedRect(window, new CornerRadius(atTop, atTop, atFoot, atFoot));

        using (context.PushOpacity(0.14))
            context.DrawRectangle(Brushes.White, null, shape);
        _windowPen ??= new Pen(Brush("[Brush] Button - Primary Hover Edge") ?? Brushes.Goldenrod, 1);
        context.DrawRectangle(null, _windowPen, shape);
    }

    private Pen? _windowPen;

    /// <summary>A quality's colour, asked of the palette once: a long folder's map draws thousands of marks a frame.</summary>
    private IBrush Colour(int quality)
    {
        if (!_colours.TryGetValue(quality, out var brush))
            _colours[quality] = brush = RarityPalette.OfQuality(quality);

        return brush;
    }

    private readonly Dictionary<int, IBrush> _colours = [];

    private readonly Dictionary<string, IBrush?> _brushes = [];

    private IBrush? Brush(string key)
    {
        if (!_brushes.TryGetValue(key, out var brush))
            _brushes[key] = brush = this.TryFindResource(key, ActualThemeVariant, out object? found) ? found as IBrush : null;

        return brush;
    }

    /// <summary>In the automation tree, as a control of its own: a drawn control is in it only if it says so.</summary>
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(PagesMinimap owner) : Avalonia.Automation.Peers.ControlAutomationPeer(owner)
    {
        protected override Avalonia.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            Avalonia.Automation.Peers.AutomationControlType.Custom;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }

    // ---- going there: a press, and a drag while it is held -------------------------------------------
    // As the editor's map does it: a press outside the window brings the pages there, the window's middle
    // under the pointer; held and dragged - from there, or from a press on the window itself - the window
    // goes with the pointer, and the map's whole height is the folder's whole length, so its foot is the
    // folder's end however far the map itself has to slide to show it.

    /// <summary>Where the drag took hold: the pointer then, and how far down the pages were.</summary>
    private (double Y, double Offset)? _held;

    /// <summary>The window on the map: its top and its height, as drawn.</summary>
    private (double Top, double Tall) Window()
    {
        var (height, slid) = Laid();
        return (_offset / _extent * height - slid, Math.Max(6, _viewport / _extent * height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        double y = e.GetPosition(this).Y;
        var (top, tall) = Window();
        double from = _offset;

        if (y < top || y > top + tall)
        {
            var (height, slid) = Laid();
            from = Math.Clamp((y + slid) / height * _extent - _viewport / 2, 0, Math.Max(0, _extent - _viewport));
            Sought?.Invoke(from);
        }

        _held = (y, from);
        e.Pointer.Capture(this);
        e.Handled = true;
        Held?.Invoke(true);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_held is not { } held)
            return;

        // The window's travel is the map's height in sight less the window's own; the pages' is their length
        // less what is in sight.
        var (height, _) = Laid();
        double most = Math.Max(0, _extent - _viewport);
        double travel = Math.Max(1, Math.Min(height, Bounds.Height) - Window().Tall);
        Sought?.Invoke(Math.Clamp(held.Offset + (e.GetPosition(this).Y - held.Y) * most / travel, 0, most));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _held = null;
        e.Pointer.Capture(null);
        Held?.Invoke(false);
    }
}
