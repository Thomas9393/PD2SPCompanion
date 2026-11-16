using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// The place a tile's tooltip opens from: one of two the layer moves under the pointer, over the tile there.
/// A control, since a tooltip is a control's - placed against it, opened and shut by the framework as the
/// pointer comes and goes - and its DataContext is the tile, so whoever walks up from what was pressed finds
/// the item as it found a tile's button. It paints nothing and takes the pointer over its whole face.
/// </summary>
public sealed class TileAnchor : Control, ICustomHitTest
{
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);
}

/// <summary>
/// Every item of a grid, drawn by one control: the whole grid drawn as one, the
/// level of optimization of an in-game inventory like PoE's.
/// A grid's cells were made one control first (<see cref="CellLayer"/>); a tile's inside the day
/// this was written (TileFace, which this takes the place of). Here there is no control an item at all: the
/// layer knows the tiles and where each lies, paints them in turn - the frame, the rarity's wash, the reds, the
/// art, the ring, the gems, a stack's count - and finds the one under the pointer by arithmetic.
/// <para>
/// What a button gave each tile is the layer's to give:
/// </para>
/// <list type="bullet">
/// <item>The click: a press and a release of the left button on one tile is <see cref="ClickTileCommand"/>, as a
/// button's click was - and not where a handler before it took the press or the release (a drag, a modified
/// click: DragPick's and the page's).</item>
/// <item>The tooltip: one of two <see cref="TileAnchor"/>s is laid over the tile under the pointer, and the
/// tooltip is its - two, turn about, so that going from one tile to the next is a leaving and a coming to the
/// framework's tooltip service, as it was between two buttons.</item>
/// <item>What a page asks of a pointer event: <see cref="Under"/>, the tile there.</item>
/// <item>What a rectangle, an arrow key or a scroll asks: <see cref="Tiles"/> and <see cref="RectOf"/>.</item>
/// <item>UI Automation: a peer a tile, with the name and the place a tile's button had (TileLayerPeer).</item>
/// </list>
/// <para>
/// Still controls, on the few tiles that show one: the vault's arrows, the alert and the duplicate badge
/// (<see cref="ExtrasTemplate"/>), laid over their tile.
/// </para>
/// </summary>
public sealed class TileLayer : Control, ICustomHitTest
{
    public static readonly StyledProperty<IEnumerable<ItemTileViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<TileLayer, IEnumerable<ItemTileViewModel>?>(nameof(Items));

    /// <summary>The tiles not selected or picked step back (ContainerGrid.IsDimmed).</summary>
    public static readonly StyledProperty<bool> IsDimmedProperty =
        AvaloniaProperty.Register<TileLayer, bool>(nameof(IsDimmed));

    public static readonly StyledProperty<double> SocketOpacityProperty =
        AvaloniaProperty.Register<TileLayer, double>(nameof(SocketOpacity), 1.0);

    public static readonly StyledProperty<ICommand?> ClickTileCommandProperty =
        AvaloniaProperty.Register<TileLayer, ICommand?>(nameof(ClickTileCommand));

    /// <summary>What is built over a tile with an arrow, an alert or a duplicate badge.</summary>
    public static readonly StyledProperty<IDataTemplate?> ExtrasTemplateProperty =
        AvaloniaProperty.Register<TileLayer, IDataTemplate?>(nameof(ExtrasTemplate));

    // ---- the colours, a style's (ItemTile.axaml)

    public static readonly StyledProperty<IBrush?> TileBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(TileBrush));
    public static readonly StyledProperty<IBrush?> HoverBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(HoverBrush));
    public static readonly StyledProperty<IBrush?> FrameBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(FrameBrush));
    public static readonly StyledProperty<IBrush?> HoverFrameBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(HoverFrameBrush));
    public static readonly StyledProperty<CornerRadius> TileCornerProperty = AvaloniaProperty.Register<TileLayer, CornerRadius>(nameof(TileCorner));
    public static readonly StyledProperty<IBrush?> RingBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(RingBrush));
    public static readonly StyledProperty<IBrush?> TargetBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(TargetBrush));
    public static readonly StyledProperty<IBrush?> IdleBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(IdleBrush));
    public static readonly StyledProperty<IBrush?> UnusableBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(UnusableBrush));
    public static readonly StyledProperty<IBrush?> HoleBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(HoleBrush));
    public static readonly StyledProperty<IBrush?> HoleEdgeBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(HoleEdgeBrush));
    public static readonly StyledProperty<FontFamily?> CountFontProperty = AvaloniaProperty.Register<TileLayer, FontFamily?>(nameof(CountFont));
    public static readonly StyledProperty<double> CountSizeProperty = AvaloniaProperty.Register<TileLayer, double>(nameof(CountSize), 13);
    public static readonly StyledProperty<IBrush?> CountBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(CountBrush));
    public static readonly StyledProperty<IBrush?> CappedBrushProperty = AvaloniaProperty.Register<TileLayer, IBrush?>(nameof(CappedBrush));

    public IEnumerable<ItemTileViewModel>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public bool IsDimmed { get => GetValue(IsDimmedProperty); set => SetValue(IsDimmedProperty, value); }
    public double SocketOpacity { get => GetValue(SocketOpacityProperty); set => SetValue(SocketOpacityProperty, value); }
    public ICommand? ClickTileCommand { get => GetValue(ClickTileCommandProperty); set => SetValue(ClickTileCommandProperty, value); }
    public IDataTemplate? ExtrasTemplate { get => GetValue(ExtrasTemplateProperty); set => SetValue(ExtrasTemplateProperty, value); }
    public IBrush? TileBrush { get => GetValue(TileBrushProperty); set => SetValue(TileBrushProperty, value); }
    public IBrush? HoverBrush { get => GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }
    public IBrush? FrameBrush { get => GetValue(FrameBrushProperty); set => SetValue(FrameBrushProperty, value); }
    public IBrush? HoverFrameBrush { get => GetValue(HoverFrameBrushProperty); set => SetValue(HoverFrameBrushProperty, value); }
    public CornerRadius TileCorner { get => GetValue(TileCornerProperty); set => SetValue(TileCornerProperty, value); }
    public IBrush? RingBrush { get => GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }

    /// <summary>The frame of a tile that what is carried would go into (ItemTileViewModel.IsSocketTarget).</summary>
    public IBrush? TargetBrush { get => GetValue(TargetBrushProperty); set => SetValue(TargetBrushProperty, value); }
    public IBrush? IdleBrush { get => GetValue(IdleBrushProperty); set => SetValue(IdleBrushProperty, value); }
    public IBrush? UnusableBrush { get => GetValue(UnusableBrushProperty); set => SetValue(UnusableBrushProperty, value); }
    public IBrush? HoleBrush { get => GetValue(HoleBrushProperty); set => SetValue(HoleBrushProperty, value); }
    public IBrush? HoleEdgeBrush { get => GetValue(HoleEdgeBrushProperty); set => SetValue(HoleEdgeBrushProperty, value); }
    public FontFamily? CountFont { get => GetValue(CountFontProperty); set => SetValue(CountFontProperty, value); }
    public double CountSize { get => GetValue(CountSizeProperty); set => SetValue(CountSizeProperty, value); }
    public IBrush? CountBrush { get => GetValue(CountBrushProperty); set => SetValue(CountBrushProperty, value); }
    public IBrush? CappedBrush { get => GetValue(CappedBrushProperty); set => SetValue(CappedBrushProperty, value); }

    static TileLayer()
    {
        AffectsRender<TileLayer>(
            IsDimmedProperty, SocketOpacityProperty, TileBrushProperty, HoverBrushProperty, FrameBrushProperty,
            HoverFrameBrushProperty, TileCornerProperty, RingBrushProperty, TargetBrushProperty, IdleBrushProperty, UnusableBrushProperty,
            HoleBrushProperty, HoleEdgeBrushProperty, CountFontProperty, CountSizeProperty, CountBrushProperty,
            CappedBrushProperty);
        ItemsProperty.Changed.AddClassHandler<TileLayer>((layer, _) => layer.Watch(layer.IsAttachedToVisualTree() ? layer.Items : null));
        ExtrasTemplateProperty.Changed.AddClassHandler<TileLayer>((layer, _) => layer.SyncExtras());
        IsDimmedProperty.Changed.AddClassHandler<TileLayer>((layer, _) => layer.DimExtras());
        FocusableProperty.OverrideDefaultValue<TileLayer>(false);
    }

    public TileLayer()
    {
        for (int i = 0; i < _anchors.Length; i++)
        {
            var anchor = new TileAnchor { IsVisible = false, Cursor = new Cursor(StandardCursorType.Hand) };
            anchor.Classes.Add("itemtip");
            ToolTip.SetShowOnDisabled(anchor, true);
            _anchors[i] = anchor;
            LogicalChildren.Add(anchor);
            VisualChildren.Add(anchor);
        }

        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Bubble);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerExitedEvent, OnExited, RoutingStrategies.Direct, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => _pressed = null, RoutingStrategies.Direct | RoutingStrategies.Bubble);
    }

    // ---- the tiles ------------------------------------------------------------------------------------------

    private List<ItemTileViewModel> _tiles = [];
    private INotifyCollectionChanged? _watched;

    /// <summary>The tiles drawn, in the order they are drawn: a later one over an earlier.</summary>
    public IReadOnlyList<ItemTileViewModel> Tiles => _tiles;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(Items);
        PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.Changed += OnClipboardChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Let go: the tiles and their list outlive the grid that drew them, and would keep this alive - as the
        // clipboard, which is the window's, would.
        PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.Changed -= OnClipboardChanged;
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Something cut, pasted or let go: the tiles that are cut are drawn faint, the rest as they were.</summary>
    private void OnClipboardChanged()
    {
        InvalidateVisual();
        DimExtras();
    }

    private void Watch(IEnumerable<ItemTileViewModel>? items)
    {
        if (_watched is not null)
            _watched.CollectionChanged -= OnItemsChanged;

        _watched = items as INotifyCollectionChanged;
        if (_watched is not null)
            _watched.CollectionChanged += OnItemsChanged;

        Take(items);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Take(sender as IEnumerable<ItemTileViewModel>);

    /// <summary>The list as it stands now: the tiles gone are let go of, the new listened to.</summary>
    private void Take(IEnumerable<ItemTileViewModel>? items)
    {
        var now = items?.ToList() ?? [];
        var kept = new HashSet<ItemTileViewModel>(now, ReferenceEqualityComparer.Instance);
        var had = new HashSet<ItemTileViewModel>(_tiles, ReferenceEqualityComparer.Instance);

        foreach (var gone in _tiles)
        {
            if (!kept.Contains(gone))
                gone.PropertyChanged -= OnTileChanged;
        }

        foreach (var come in now)
        {
            if (!had.Contains(come))
                come.PropertyChanged += OnTileChanged;
        }

        _tiles = now;
        if (_hover is not null && !kept.Contains(_hover))
            Hover(null);
        if (_pressed is not null && !kept.Contains(_pressed))
            _pressed = null;

        SyncExtras();
        InvalidateArrange();
        InvalidateVisual();
        TilesChanged?.Invoke();
    }

    /// <summary>The tiles drawn have changed: for the automation peer, which keeps one of its own a tile.</summary>
    internal event Action? TilesChanged;

    private void OnTileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ItemTileViewModel tile)
            SyncExtras(tile);

        // A tile moved under what lies over it.
        if (e.PropertyName is null or "" or nameof(ItemTileViewModel.Left) or nameof(ItemTileViewModel.Top))
            InvalidateArrange();

        InvalidateVisual();
    }

    // ---- where a tile is --------------------------------------------------------------------------------

    /// <summary>A tile's whole place, in the layer's own coordinates: what its button's bounds were.</summary>
    public static Rect PlaceOf(ItemTileViewModel tile) => new(tile.Left, tile.Top, tile.Width, tile.Height);

    /// <summary>Where a tile of this layer is drawn, or null for one that is not its own.</summary>
    public Rect? RectOf(ItemTileViewModel tile) => _tiles.Contains(tile) ? PlaceOf(tile) : null;

    /// <summary>The tile at a point of the layer: the topmost there, or null over none.</summary>
    public ItemTileViewModel? TileAt(Point point)
    {
        for (int i = _tiles.Count - 1; i >= 0; i--)
        {
            if (PlaceOf(_tiles[i]).Contains(point))
                return _tiles[i];
        }

        return null;
    }

    /// <summary>Only over a tile: the cells between them are the grid's, as they were between buttons.</summary>
    public bool HitTest(Point point) => TileAt(point) is not null;

    /// <summary>The layer a pointer event came through, or null: what was pressed is the layer or lies in it.</summary>
    public static TileLayer? Of(object? source) =>
        source as TileLayer ?? (source as Visual)?.FindAncestorOfType<TileLayer>();

    /// <summary>
    /// The grid's tile under the pointer of an event, or null where it is over none - what a page asks, where
    /// it walked up from what was pressed to a tile's button.
    /// </summary>
    public static ItemTileViewModel? Under(PointerEventArgs e) =>
        Of(e.Source) is { } layer ? layer.TileAt(e.GetPosition(layer)) : null;

    /// <summary>The layer that draws a tile, among what is drawn under <paramref name="root"/>, and where.</summary>
    public static (TileLayer Layer, Rect Place)? Find(Visual root, ItemTileViewModel tile)
    {
        foreach (var layer in root.GetVisualDescendants().OfType<TileLayer>())
        {
            if (layer.IsEffectivelyVisible && layer.RectOf(tile) is { } place)
                return (layer, place);
        }

        return null;
    }

    // ---- the pointer: the tile under it, the click, the tooltip's anchor -------------------------------------

    private ItemTileViewModel? _hover, _pressed;
    private readonly TileAnchor[] _anchors = new TileAnchor[2];
    private int _anchorNow;

    private void OnMoved(object? sender, PointerEventArgs e) => Hover(TileAt(e.GetPosition(this)));

    private void OnExited(object? sender, PointerEventArgs e)
    {
        // Onto one of the anchors is not away: they lie in the layer.
        if (TileAt(e.GetPosition(this)) is null)
            Hover(null);
    }

    /// <summary>One hand for every tile and anchor: a new one was made at every tile the pointer crossed.</summary>
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    /// <summary>
    /// A pen of a brush and a width, made once: every tile's edge, ring and socket made its own at each paint, and a
    /// layer is painted whole when the pointer goes from one tile to the next.
    /// </summary>
    private Pen PenOf(IBrush brush, double thickness)
    {
        if (!_pens.TryGetValue((brush, thickness), out var pen))
            _pens[(brush, thickness)] = pen = new Pen(brush, thickness);
        return pen;
    }

    private readonly Dictionary<(IBrush, double), Pen> _pens = [];

    /// <summary>A stack's count laid out once a text and brush: shaped anew at every paint before.</summary>
    private FormattedText CountText(string said, IBrush brush)
    {
        var key = (said, brush, CountFont, CountSize);
        if (_counts.TryGetValue(key, out var text))
            return text;

        if (_counts.Count > 256)
            _counts.Clear();
        return _counts[key] = new FormattedText(
            said, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(CountFont ?? FontFamily.Default), CountSize, brush);
    }

    private readonly Dictionary<(string, IBrush, FontFamily?, double), FormattedText> _counts = [];

    private void Hover(ItemTileViewModel? tile)
    {
        if (ReferenceEquals(tile, _hover))
            return;

        _hover = tile;
        Cursor = tile is null ? null : HandCursor;

        // The other anchor comes over the new tile and this one goes: a leaving and a coming for the tooltip.
        _anchors[_anchorNow].IsVisible = false;
        if (tile is not null)
        {
            _anchorNow = 1 - _anchorNow;
            var anchor = _anchors[_anchorNow];
            anchor.DataContext = tile;
            ToolTip.SetTip(anchor, tile);
            anchor.IsVisible = true;
        }

        InvalidateArrange();
        InvalidateVisual();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsOnExtra(e.Source) ? TileAt(e.GetPosition(this)) : null;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _pressed;
        _pressed = null;
        if (pressed is null || e.InitialPressMouseButton != MouseButton.Left || !ReferenceEquals(TileAt(e.GetPosition(this)), pressed))
            return;

        if (ClickTileCommand is { } command && command.CanExecute(pressed))
        {
            command.Execute(pressed);
            e.Handled = true;
        }
    }

    /// <summary>A press on a control over a tile - the duplicate badge - is that control's.</summary>
    private bool IsOnExtra(object? source)
    {
        for (var at = source as Visual; at is not null && !ReferenceEquals(at, this); at = at.GetVisualParent())
        {
            if (at is Button)
                return true;
        }

        return false;
    }

    /// <summary>A click without the pointer, as a key makes one: the tile's own, where the layer draws it.</summary>
    public bool Click(ItemTileViewModel tile)
    {
        if (!_tiles.Contains(tile) || ClickTileCommand is not { } command || !command.CanExecute(tile))
            return false;

        command.Execute(tile);
        return true;
    }

    // ---- the few controls over tiles ---------------------------------------------------------------------

    private readonly Dictionary<ItemTileViewModel, Control> _extras = new(ReferenceEqualityComparer.Instance);

    private static bool WantsExtras(ItemTileViewModel tile) =>
        tile.IsLeaving || tile.IsArriving || tile.IsImpossible || tile.IsDuplicate;

    private void SyncExtras()
    {
        var kept = new HashSet<ItemTileViewModel>(_tiles, ReferenceEqualityComparer.Instance);
        foreach (var gone in _extras.Keys.Where(tile => !kept.Contains(tile)).ToList())
            DropExtras(gone);

        foreach (var tile in _tiles)
            SyncExtras(tile);
    }

    private void SyncExtras(ItemTileViewModel tile)
    {
        bool wanted = ExtrasTemplate is not null && WantsExtras(tile);
        if (!wanted)
        {
            DropExtras(tile);
            return;
        }

        if (!_extras.TryGetValue(tile, out var extra))
        {
            if (ExtrasTemplate!.Build(tile) is not { } built)
                return;

            built.DataContext = tile;
            _extras[tile] = extra = built;
            LogicalChildren.Add(built);

            // Under the anchors, which are the last two.
            VisualChildren.Insert(Math.Max(0, VisualChildren.Count - _anchors.Length), built);
            InvalidateMeasure();
        }

        extra.Opacity = OpacityOf(tile);
    }

    private void DropExtras(ItemTileViewModel tile)
    {
        if (!_extras.Remove(tile, out var extra))
            return;

        VisualChildren.Remove(extra);
        LogicalChildren.Remove(extra);
    }

    private void DimExtras()
    {
        foreach (var (tile, extra) in _extras)
            extra.Opacity = OpacityOf(tile);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var (tile, extra) in _extras)
            extra.Measure(FaceOf(tile).Size);

        foreach (var anchor in _anchors)
            anchor.Measure(availableSize);

        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (tile, extra) in _extras)
            extra.Arrange(FaceOf(tile));

        foreach (var anchor in _anchors)
        {
            if (anchor is { IsVisible: true, DataContext: ItemTileViewModel tile })
                anchor.Arrange(PlaceOf(tile));
        }

        return finalSize;
    }

    // ---- painting ------------------------------------------------------------------------------------------

    /// <summary>
    /// Dimmed, the tiles not selected or picked step back, and a search's misses further still; what is
    /// selected is never dimmed (ItemTile.axaml's rules for the button a tile was).
    /// </summary>
    private double OpacityOf(ItemTileViewModel tile) =>
        // Cut (ItemClipboard): faint until it is pasted or let go, selected or not - as Explorer's cut file is.
        PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.IsCut(tile) ? 0.35
        : tile.IsMarked ? 1
        : tile.IsSearchMiss ? 0.25
        : IsDimmed ? 0.45
        : 1;

    /// <summary>
    /// The room inside a tile's frame, and in it the face: as large as the art - or as the item, where it has
    /// sockets - and no larger than the room, in its middle. What the button's content was, sized by what stood
    /// in it.
    /// </summary>
    private static Rect FaceOf(ItemTileViewModel tile)
    {
        var room = PlaceOf(tile).Deflate(1);
        var art = tile.Sprite?.Size ?? default;
        double width = Math.Min(room.Width, tile.HasSockets ? Math.Max(art.Width, tile.Width) : art.Width);
        double height = Math.Min(room.Height, tile.HasSockets ? Math.Max(art.Height, tile.Height) : art.Height);
        return room.CenterRect(new Rect(0, 0, Math.Max(0, width), Math.Max(0, height)));
    }

    public override void Render(DrawingContext context)
    {
        foreach (var tile in _tiles)
        {
            double opacity = OpacityOf(tile);
            if (opacity < 1)
            {
                using (context.PushOpacity(opacity))
                    Paint(context, tile);
            }
            else
            {
                Paint(context, tile);
            }
        }
    }

    private void Paint(DrawingContext context, ItemTileViewModel tile)
    {
        var place = PlaceOf(tile);
        bool hovered = ReferenceEquals(tile, _hover);

        // The frame a tile's button drew: a faint fill, lighter under the pointer, and a thin edge.
        // One rounded rectangle, filled and edged, its edge's middle half a unit in: as the button's presenter
        // drew it. (The fill inside the edge and the edge a ring round it, and a corner half a unit tighter,
        // were tried against the old tiles: 13,401 and 2,729 pixels off; this, 849 corner pixels by 2 in 255.)
        double corner = TileCorner.TopLeft;
        context.DrawRectangle(
            hovered ? HoverBrush ?? TileBrush : TileBrush,
            (hovered ? HoverFrameBrush ?? FrameBrush : FrameBrush) is { } edge ? PenOf(edge, 1) : null,
            place.Deflate(0.5), corner, corner);

        var face = FaceOf(tile);
        if (face.Width <= 0 || face.Height <= 0)
            return;

        // The quality, as a wash behind the art: the way the game colours a name.
        using (context.PushOpacity(tile.QualityWash))
            context.FillRectangle(tile.WashBrush, face);

        if (tile.IsIdleCharm && IdleBrush is { } idle)
            context.FillRectangle(idle, face);
        if (tile.IsUnusable && UnusableBrush is { } unusable)
            context.FillRectangle(unusable, face);

        // The art unscaled and in the middle, cut at the face's edge where it is larger - as an Image with no
        // stretch draws it.
        if (tile.Sprite is { } art)
        {
            var size = art.Size;
            var to = face.CenterRect(new Rect(size)).Intersect(face);
            var from = new Rect(size).CenterRect(new Rect(to.Size));
            using (context.PushOpacity(tile.SpriteOpacity))
                context.DrawImage(art, from, to);
        }

        if (tile.IsMarked && RingBrush is { } ring)
            context.DrawRectangle(null, PenOf(ring, 2), face.Deflate(1));

        if (tile.IsSocketTarget && TargetBrush is { } target)
            context.DrawRectangle(null, PenOf(target, 2), face.Deflate(1));

        if (tile.HasSockets)
            PaintSockets(context, tile, face);

        if (tile.HasQuantity)
            PaintCount(context, tile, face);
    }

    /// <summary>
    /// The sockets over the art, each where the game puts it on a box the item's own size, that box in the
    /// face's middle: a dark hole for an empty one, the gem's or the rune's own art for a filled one (SocketRow).
    /// </summary>
    private void PaintSockets(DrawingContext context, ItemTileViewModel tile, Rect face)
    {
        var box = face.CenterRect(new Rect(0, 0, tile.Width, tile.Height));
        using var faded = context.PushOpacity(SocketOpacity);

        foreach (var socket in tile.Sockets)
        {
            var at = new Rect(box.X + socket.Left, box.Y + socket.Top, socket.Size, socket.Size);
            if (socket.IsFilled && socket.Sprite is { } gem)
            {
                var size = gem.Size;
                if (size.Width <= 0 || size.Height <= 0)
                    continue;

                double scale = Math.Min(at.Width / size.Width, at.Height / size.Height);
                context.DrawImage(gem, new Rect(size), at.CenterRect(new Rect(0, 0, size.Width * scale, size.Height * scale)));
            }
            else if (socket.IsEmpty)
            {
                double radius = (socket.HoleSize - 1) / 2;
                context.DrawEllipse(HoleBrush, HoleEdgeBrush is { } edge ? PenOf(edge, 1) : null, at.Center, radius, radius);
            }
        }
    }

    /// <summary>A stack's count at the bottom left of the face, where the game puts it (StackCount.axaml).</summary>
    private void PaintCount(DrawingContext context, ItemTileViewModel tile, Rect face)
    {
        if ((tile.IsQuantityCapped ? CappedBrush ?? CountBrush : CountBrush) is not { } brush || tile.QuantityText is not { Length: > 0 } said)
            return;

        var text = CountText(said, brush);
        context.DrawText(text, new Point(face.X + 2, Math.Round(face.Bottom - 1 - text.Height)));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TileLayerPeer(this);
}
