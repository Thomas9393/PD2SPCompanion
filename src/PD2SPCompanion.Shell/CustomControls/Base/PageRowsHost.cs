using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// The rows of a vault folder's pages, down a scroll: as tall as all of them, with only the rows in sight
/// and near it built - and <b>a row once built is kept</b>.
/// <para>
/// A list that draws only the rows in sight throws a row's hundred-odd tiles away as it leaves and builds
/// them again when it comes back, and building them is what a long folder's scrolling stuttered on once the
/// tooltips were out of it (measured with tests/perf/vault-scroll.ps1). So the rows are cached: those in sight are
/// built, their neighbours behind them, and the furthest
/// are let go only past <see cref="Kept"/> - what bounds the memory of a folder of thousands.
/// </para>
/// <para>
/// <b>Nothing is built while the pages move fast</b> (what a long list is
/// scrolled by where it is done well): a thumb dragged from end to end, a run of page jumps. A row not built
/// is drawn as a sketch of itself - its pages' frames, and a block of its rarity's color where each item
/// lies (<see cref="PageSketch"/>, from the marks the folder's map draws) - which costs nothing to scroll;
/// the tiles are built when the motion slows, a row a turn and below the wheel in the dispatcher's order.
/// </para>
/// </summary>
public sealed class PageRowsHost : Panel
{
    /// <summary>The rows, top to bottom. Told of its changes where it tells them (an observable collection's).</summary>
    public static readonly StyledProperty<IReadOnlyList<VaultPageRow>?> RowsProperty =
        AvaloniaProperty.Register<PageRowsHost, IReadOnlyList<VaultPageRow>?>(nameof(Rows));

    /// <summary>A row's height: the same for every row, so the whole is known before any is built.</summary>
    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<PageRowsHost, double>(nameof(RowHeight), 100);

    /// <summary>Called with the tile that was clicked, on any page.</summary>
    public static readonly StyledProperty<ICommand?> ClickTileCommandProperty =
        AvaloniaProperty.Register<PageRowsHost, ICommand?>(nameof(ClickTileCommand));

    /// <summary>The tiles' tooltips held back on every row, while the rows are being scrolled (<see cref="ContainerGrid.TipsOff"/>).</summary>
    public static readonly StyledProperty<bool> TipsOffProperty =
        AvaloniaProperty.Register<PageRowsHost, bool>(nameof(TipsOff));

    /// <summary>
    /// The tiles not selected or picked step back, on every row (<see cref="ContainerGrid.IsDimmed"/>): the vault's
    /// sheet dims them as the Items page does (selecting in the vault dimmed nothing: the rows
    /// were never told).
    /// </summary>
    public static readonly StyledProperty<bool> IsDimmedProperty =
        AvaloniaProperty.Register<PageRowsHost, bool>(nameof(IsDimmed));

    public bool IsDimmed
    {
        get => GetValue(IsDimmedProperty);
        set => SetValue(IsDimmedProperty, value);
    }

    /// <summary>How plainly the sockets over an item's art are drawn, on every row (<see cref="ContainerGrid.SocketOpacity"/>): the vault's page fades them as the Items page does.</summary>
    public static readonly StyledProperty<double> SocketOpacityProperty =
        AvaloniaProperty.Register<PageRowsHost, double>(nameof(SocketOpacity), 1.0);

    public double SocketOpacity
    {
        get => GetValue(SocketOpacityProperty);
        set => SetValue(SocketOpacityProperty, value);
    }

    /// <summary>The sheet's width in columns: what a row not built is sketched at.</summary>
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<PageRowsHost, int>(nameof(Columns), VaultLayout.DefaultColumns);

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>Every item of the folder as a mark: what a row not built is sketched from.</summary>
    public static readonly StyledProperty<IReadOnlyList<VaultMark>?> MarksProperty =
        AvaloniaProperty.Register<PageRowsHost, IReadOnlyList<VaultMark>?>(nameof(Marks));

    /// <summary>A search's finds in the folder, or null while none stands: sketched in the search's green, the rest faint.</summary>
    public static readonly StyledProperty<IReadOnlySet<long>?> HitsProperty =
        AvaloniaProperty.Register<PageRowsHost, IReadOnlySet<long>?>(nameof(Hits));

    /// <summary>
    /// What stands over the sheet's cells and is scrolled with them: the folder's own folders, as tiles (FolderTile).
    /// Laid from the sheet's first cell - its own top left is that cell's - and as wide
    /// as the cells are, over every row.
    /// </summary>
    public static readonly StyledProperty<Control?> OverProperty =
        AvaloniaProperty.Register<PageRowsHost, Control?>(nameof(Over));

    public Control? Over
    {
        get => GetValue(OverProperty);
        set => SetValue(OverProperty, value);
    }

    public IReadOnlyList<VaultPageRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public ICommand? ClickTileCommand
    {
        get => GetValue(ClickTileCommandProperty);
        set => SetValue(ClickTileCommandProperty, value);
    }

    public bool TipsOff
    {
        get => GetValue(TipsOffProperty);
        set => SetValue(TipsOffProperty, value);
    }

    public IReadOnlyList<VaultMark>? Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    public IReadOnlySet<long>? Hits
    {
        get => GetValue(HitsProperty);
        set => SetValue(HitsProperty, value);
    }

    /// <summary>How many rows beyond those in sight are built ahead, each way.</summary>
    private const int Ahead = 2;

    /// <summary>The most rows kept built: past it, the furthest from what is in sight goes.</summary>
    private const int Kept = 14;

    /// <summary>
    /// Moving faster than this, in units a millisecond, nothing is built: a row and more every 280 ms - a
    /// thumb dragged, page jumps one after another, a wheel spun. A wheel turned a notch at a time is under
    /// it. (A row every 150 ms was tried first: page jumps 150 ms apart sat on the line, built half the time.)
    /// </summary>
    private double Fast => RowHeight / 280;

    /// <summary>How long the pages must have moved slowly, or not at all, before rows are built after moving fast.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(110);

    /// <summary>The rows built, by their place in the folder.</summary>
    private readonly Dictionary<int, PageRowView> _built = [];

    /// <summary>The sketch of every row not built, behind them all.</summary>
    private readonly PageSketch _sketch = new();

    /// <summary>
    /// The sheet's frame across its top and across its foot. A stretch is framed at its sides only, so that
    /// the next begins where it ends - which left the sheet with no top to its frame, and the first row of
    /// cells looking cut off however far up it was scrolled. As a grid's frame is: a
    /// line, and two of black inside it (Styles/CommonControls/ItemTile.axaml, Border.gridframe).
    /// </summary>
    private readonly Border _top = Cap(new Thickness(1, 1, 1, 0), new CornerRadius(2, 2, 0, 0));

    private readonly Border _foot = Cap(new Thickness(1, 0, 1, 1), new CornerRadius(0, 0, 2, 2));

    /// <summary>How tall a cap is: the frame's line and its padding.</summary>
    private const double CapHeight = 3;

    private static Border Cap(Thickness lines, CornerRadius corners)
    {
        var cap = new Border
        {
            Background = Brushes.Black,
            BorderThickness = lines,
            CornerRadius = corners,
            IsHitTestVisible = false,
        };

        cap.Bind(Border.BorderBrushProperty, cap.GetResourceObservable("[Brush] Frame - Golden Dim"));
        return cap;
    }

    private Rect _inSight;

    /// <summary>Where the top of what is in sight was at the last move, and when.</summary>
    private double _lastTop = double.NaN;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private long _lastAt;

    private DispatcherTimer? _settled;

    /// <summary>A built row's width: the width of the sketch behind them all.</summary>
    private double _rowWidth;

    public PageRowsHost()
    {
        Children.Add(_sketch);

        // After the sketch, and after every row: a row is put in before them (Build).
        Children.Add(_top);
        Children.Add(_foot);

        EffectiveViewportChanged += (_, e) =>
        {
            _inSight = e.EffectiveViewport;
            Moved();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged was)
                was.CollectionChanged -= WhenRowsChanged;
            if (change.NewValue is INotifyCollectionChanged now)
                now.CollectionChanged += WhenRowsChanged;

            Reset();
        }
        else if (change.Property == RowHeightProperty)
        {
            InvalidateMeasure();
        }
        else if (change.Property == OverProperty)
        {
            // Last of the children: over the sketch, every row and the frame's two caps.
            if (change.OldValue is Control was)
                Children.Remove(was);
            if (change.NewValue is Control now)
                Children.Add(now);

            InvalidateMeasure();
        }
        else if (change.Property == ClickTileCommandProperty)
        {
            foreach (var row in _built.Values)
                row.ClickTileCommand = ClickTileCommand;
        }
        else if (change.Property == TipsOffProperty)
        {
            foreach (var row in _built.Values)
                row.TipsOff = TipsOff;
        }
        else if (change.Property == SocketOpacityProperty)
        {
            foreach (var row in _built.Values)
                row.SocketOpacity = SocketOpacity;
        }
        else if (change.Property == IsDimmedProperty)
        {
            foreach (var row in _built.Values)
                row.IsDimmed = IsDimmed;
        }
        else if (change.Property == MarksProperty || change.Property == HitsProperty)
        {
            Sketch();
        }
        else if (change.Property == ColumnsProperty)
        {
            InvalidateMeasure();
            Sketch();
        }
    }

    private bool _resetAsked;

    /// <summary>The rows were read again, or are others now: once for however many of them changed.</summary>
    private void WhenRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_resetAsked)
            return;

        _resetAsked = true;
        Dispatcher.UIThread.Post(() =>
        {
            _resetAsked = false;
            Reset();
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// The rows are other rows: those in sight are handed their new pages - their grids kept - and the rest
    /// let go, to be built again behind the scroll.
    /// </summary>
    private void Reset()
    {
        var rows = Rows ?? [];
        var (first, last) = InSight(rows.Count);

        foreach (int index in _built.Keys.ToList())
        {
            if (index >= first && index <= last && index < rows.Count)
                _built[index].Pages = rows[index].Pages;
            else
                Drop(index);
        }

        InvalidateMeasure();
        Sketch();
        AskFill();
    }

    /// <summary>The first and the last row any of which is in sight; none in sight is the first.</summary>
    private (int First, int Last) InSight(int count)
    {
        if (count == 0 || RowHeight <= 0)
            return (0, -1);

        int first = Math.Clamp((int)Math.Floor(_inSight.Top / RowHeight), 0, count - 1);
        int last = Math.Clamp((int)Math.Floor((_inSight.Bottom - 0.5) / RowHeight), first, count - 1);
        return (first, last);
    }

    /// <summary>The sketch told what to draw: the folder's marks, which rows are built, what is in sight.</summary>
    private void Sketch()
    {
        var rows = Rows ?? [];
        var (first, last) = InSight(rows.Count);
        _sketch.Show(rows, Marks, Hits, Columns, RowHeight, first, last, _built.Keys);
    }

    /// <summary>
    /// What is in sight has moved. Slowly - a wheel's notch, a jump on its own - the rows wanted are asked for
    /// at once, to be built behind the scroll. Fast, nothing is asked until the pages have slowed: the sketch
    /// is what is scrolled over, and the tiles are built for where the motion ends, not for what it passed.
    /// </summary>
    private void Moved()
    {
        long now = _clock.ElapsedMilliseconds;
        double top = _inSight.Top;
        bool fast = !double.IsNaN(_lastTop) && Math.Abs(top - _lastTop) / Math.Max(1, now - _lastAt) > Fast;

        (_lastTop, _lastAt) = (top, now);
        Sketch();

        // Moving: nothing is let go until the pages have been still again.
        if (_quiet?.IsEnabled == true)
            AskLetGo();

        if (!fast && _settled?.IsEnabled != true)
        {
            AskFill();
            return;
        }

        _settled ??= new DispatcherTimer(Settle, DispatcherPriority.Background, (_, _) =>
        {
            _settled?.Stop();
            AskFill();
        });

        _settled.Stop();
        _settled.Start();
    }

    private bool _fillAsked;

    /// <summary>Asks for the rows wanted to be built - not now: behind the scroll, a row a turn (<see cref="Fill"/>).</summary>
    private void AskFill()
    {
        if (_fillAsked || (Rows?.Count ?? 0) == 0)
            return;

        _fillAsked = true;
        Dispatcher.UIThread.Post(Fill, DispatcherPriority.Background);
    }

    /// <summary>
    /// One row a turn: the first in sight not yet built; then, with all in sight built, the nearest
    /// neighbour, <see cref="Ahead"/> each way; then, with nothing left to build, the furthest are let go past
    /// what is kept - later, once the pages are still. Not while the pages move fast: the turn after they slow.
    /// </summary>
    private void Fill()
    {
        _fillAsked = false;

        if (_settled?.IsEnabled == true)
            return;

        var rows = Rows ?? [];
        var (first, last) = InSight(rows.Count);
        if (last < first)
            return;

        int? next = null;
        for (int index = first; index <= last && next is null; index++)
        {
            if (!_built.ContainsKey(index))
                next = index;
        }

        for (int step = 1; step <= Ahead && next is null; step++)
        {
            foreach (int index in new[] { last + step, first - step })
            {
                if (index >= 0 && index < rows.Count && !_built.ContainsKey(index))
                {
                    next = index;
                    break;
                }
            }
        }

        if (next is { } build)
        {
            Build(rows, build);
            AskFill();
            return;
        }

        // Nothing left to build: the rows furthest from what is in sight go, past what is kept - once the
        // pages have been still a while, and one at a time. Letting a row go takes its tiles out of the tree,
        // 30 ms of it: done here it was a third of what a page's jump waited for, and done as soon as the
        // window was idle it fell between two jumps of a run and the row was built again on the way back
        // (traced, twice).
        if (_built.Count > Kept && _quiet?.IsEnabled != true)
            AskLetGo();
    }

    /// <summary>How long the pages must have been still before a row past what is kept is let go; and how long between two.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(1500), Between = TimeSpan.FromMilliseconds(120);

    private DispatcherTimer? _quiet;

    private void AskLetGo()
    {
        _quiet ??= new DispatcherTimer(Quiet, DispatcherPriority.ApplicationIdle, (_, _) => LetGo());
        _quiet.Stop();
        _quiet.Interval = Quiet;
        _quiet.Start();
    }

    private void LetGo()
    {
        _quiet?.Stop();
        if (_built.Count <= Kept)
            return;

        var (first, last) = InSight(Rows?.Count ?? 0);
        double middle = (first + last) / 2.0;
        int furthest = _built.Keys.OrderByDescending(index => Math.Abs(index - middle)).First();

        // Never one in sight or just beside it, however many there are.
        if (furthest >= first - Ahead && furthest <= last + Ahead)
            return;

        Drop(furthest);

        if (_built.Count > Kept && _quiet is not null)
        {
            _quiet.Interval = Between;
            _quiet.Start();
        }
    }

    private void Build(IReadOnlyList<VaultPageRow> rows, int index)
    {
        if (_built.ContainsKey(index))
            return;

        var row = new PageRowView
        {
            Background = Brushes.Black,
            Height = RowHeight,
            ClickTileCommand = ClickTileCommand,
            TipsOff = TipsOff,
            SocketOpacity = SocketOpacity,
            IsDimmed = IsDimmed,
            Pages = rows[index].Pages,
        };

        // Over the rows below it: an item whose top is on this stretch reaches down into the next, and is
        // drawn - and pressed - there, over that stretch's cells. By its place among the children, which
        // are drawn in their order: the sketch first, then the rows from the last to the first.
        Children.Insert(1 + _built.Keys.Count(built => built > index), row);
        _built[index] = row;
        InvalidateMeasure();
        Sketch();
    }

    private void Drop(int index)
    {
        if (_built.Remove(index, out var row))
        {
            Children.Remove(row);
            Sketch();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double widest = 0;
        foreach (var row in _built.Values)
        {
            row.Measure(new Size(double.PositiveInfinity, RowHeight));
            widest = Math.Max(widest, row.DesiredSize.Width);
        }

        // Before any row is built, the width the sketch works out for the sheet.
        _rowWidth = widest > 0 ? widest : _sketch.RowWidth(Rows, Columns);
        _sketch.Measure(availableSize);
        _top.Measure(new Size(_rowWidth, CapHeight));
        _foot.Measure(new Size(_rowWidth, CapHeight));
        Over?.Measure(new Size(Columns * ItemTileViewModel.CellSize, (Rows?.Count ?? 0) * RowHeight));

        return new Size(_rowWidth, (Rows?.Count ?? 0) * RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double ground = Math.Min(_rowWidth, finalSize.Width);
        double left = Math.Max(0, (finalSize.Width - ground) / 2), tall = (Rows?.Count ?? 0) * RowHeight;
        _sketch.Arrange(new Rect(left, 0, ground, tall));

        // The frame's top over the first stretch and its foot under the last, in the room the sheet's margin
        // leaves: out of the rows' own height, which is the sheet's and nothing else.
        _top.IsVisible = _foot.IsVisible = tall > 0;
        _top.Arrange(new Rect(left, -CapHeight, ground, CapHeight));
        _foot.Arrange(new Rect(left, tall, ground, CapHeight));

        // Each row at its place down the folder, in the middle of the width there is.
        foreach (var (index, row) in _built)
        {
            double width = row.DesiredSize.Width;
            row.Arrange(new Rect(Math.Max(0, (finalSize.Width - width) / 2), index * RowHeight, width, RowHeight));
        }

        // What stands over the cells, from where a built row's cells begin - asked of the row, whose frame and
        // padding are rounded to whole pixels by the layout (three units are 3.75 pixels at 125%): worked out
        // here it could lie a pixel off the cells. Before any row is built, where the sketch draws them.
        if (Over is { } over)
        {
            double cells = left + PageSketch.Inset;
            foreach (var row in _built.Values)
            {
                if (row.GetVisualDescendants().OfType<TileLayer>().FirstOrDefault() is { } layer
                    && layer.TranslatePoint(default, this) is { } at)
                {
                    cells = at.X;
                    break;
                }
            }

            over.Arrange(new Rect(cells, 0, Columns * ItemTileViewModel.CellSize, tall));
        }

        return finalSize;
    }
}

/// <summary>
/// A stretch of the sheet not built, drawn: its black, the sheet's two sides, and each item a block of its
/// rarity's color where it lies - with a search standing its finds stay and the rest are faint, as the folder's
/// map draws them. One picture behind all the rows; only the rows in sight and not built are drawn into it.
/// </summary>
public sealed class PageSketch : Control
{
    private const double Cell = ItemTileViewModel.CellSize;

    /// <summary>The sheet's frame and padding at each side of its cells, as a grid draws them: one of border and two of padding.</summary>
    public const double Inset = 3;

    private IReadOnlyList<VaultPageRow> _rows = [];
    private IReadOnlyList<VaultMark>? _marks;
    private Dictionary<int, List<VaultMark>> _byPage = [];
    private IReadOnlySet<long>? _hits;
    private HashSet<int> _built = [];
    private double _rowHeight;
    private int _first, _last = -1, _columns = VaultLayout.DefaultColumns;

    public PageSketch()
    {
        IsHitTestVisible = false;
    }

    /// <summary>A row's width by the sheet's columns, where no row is built to measure.</summary>
    public double RowWidth(IReadOnlyList<VaultPageRow>? rows, int columns) => rows is { Count: > 0 } ? columns * Cell + 2 * Inset : 0;

    public void Show(IReadOnlyList<VaultPageRow> rows, IReadOnlyList<VaultMark>? marks, IReadOnlySet<long>? hits,
                     int columns, double rowHeight, int first, int last, IEnumerable<int> built)
    {
        // Asked at every step of a scroll: where nothing it draws has moved - the same stretches in sight, the same
        // built - it is left as it is drawn, not drawn again with a set made anew.
        if (ReferenceEquals(marks, _marks) && ReferenceEquals(rows, _rows) && ReferenceEquals(hits, _hits)
            && columns == _columns && rowHeight == _rowHeight && first == _first && last == _last && SameBuilt(built)
            && SameInSight(rows))
        {
            return;
        }

        if (!ReferenceEquals(marks, _marks))
        {
            _marks = marks;
            _byPage = [];
            foreach (var mark in marks ?? [])
            {
                if (!_byPage.TryGetValue(mark.Page, out var onPage))
                    _byPage[mark.Page] = onPage = [];

                onPage.Add(mark);
            }
        }

        (_rows, _hits, _columns, _rowHeight, _first, _last) = (rows, hits, columns, rowHeight, first, last);
        _built = [.. built];
        _inSightRows = [.. rows.Skip(Math.Max(0, first)).Take(Math.Max(0, Math.Min(last, rows.Count - 1) - Math.Max(0, first) + 1))];
        InvalidateVisual();
    }

    /// <summary>The stretches in sight are the very rows drawn: the list is changed in place, a row for a row.</summary>
    private bool SameInSight(IReadOnlyList<VaultPageRow> rows)
    {
        for (int index = Math.Max(0, _first); index <= _last && index < rows.Count; index++)
        {
            int at = index - Math.Max(0, _first);
            if (at >= _inSightRows.Length || !ReferenceEquals(rows[index], _inSightRows[at]))
                return false;
        }

        return true;
    }

    private VaultPageRow[] _inSightRows = [];

    private bool SameBuilt(IEnumerable<int> built)
    {
        int count = 0;
        foreach (int index in built)
        {
            if (!_built.Contains(index))
                return false;
            count++;
        }

        return count == _built.Count;
    }

    public override void Render(DrawingContext context)
    {
        if (_rowHeight <= 0)
            return;

        var frame = new Pen(Frame() ?? Brushes.DimGray, 1);
        double wide = _columns * Cell + 2 * Inset;

        for (int index = Math.Max(0, _first); index <= _last && index < _rows.Count; index++)
        {
            if (_built.Contains(index))
                continue;

            double top = index * _rowHeight;
            var stretch = new Rect(0, top, wide, _rowHeight);
            context.FillRectangle(Brushes.Black, stretch);
            context.DrawLine(frame, new Point(0.5, top), new Point(0.5, top + _rowHeight));
            context.DrawLine(frame, new Point(wide - 0.5, top), new Point(wide - 0.5, top + _rowHeight));

            // Its own items, and those of the stretch above that reach down into it.
            foreach (int from in new[] { _rows[index].First - 1, _rows[index].First })
            {
                if (!_byPage.TryGetValue(from, out var marks))
                    continue;

                foreach (var mark in marks)
                {
                    var block = new Rect(Inset + mark.X * Cell, mark.Row * Cell, mark.Width * Cell, mark.Height * Cell)
                        .Intersect(stretch).Deflate(2);
                    if (block.Width <= 0 || block.Height <= 0)
                        continue;

                    bool found = _hits?.Contains(mark.Key) == true;
                    using (context.PushOpacity(_hits is null ? 0.42 : found ? 0.8 : 0.14))
                        context.FillRectangle(Color(mark.Quality), block);
                }
            }
        }
    }

    private readonly Dictionary<int, IBrush> _colors = [];

    private IBrush Color(int quality)
    {
        if (!_colors.TryGetValue(quality, out var brush))
            _colors[quality] = brush = RarityPalette.OfQuality(quality);

        return brush;
    }

    private IBrush? _frame;

    /// <summary>The pages' frame color, asked for once.</summary>
    private IBrush? Frame() =>
        _frame ??= this.TryFindResource("[Brush] Frame - Golden Dim", ActualThemeVariant, out object? found) ? found as IBrush : null;
}
