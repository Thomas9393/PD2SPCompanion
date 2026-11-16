using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// Explorer's selection by mouse: on the vault's list, and on each section of the
/// inventory page.
/// <para>
/// A drag that starts on empty space draws the rectangle once it has moved a few pixels, so a
/// click still clicks. Every item it touches is picked; a plain drag replaces the picks and
/// Ctrl+drag flips what it covers. Held near the top or bottom edge, a list scrolls. A click on
/// empty space clears the selection. A plain drag that starts on an item carries it instead - see
/// <see cref="IDragPicker.BeginCarry"/>.
/// </para>
/// <para>
/// Explorer's clicks too: Ctrl+click flips one item; Shift+click picks from the anchor - the item
/// last clicked without Shift - to this one, in reading order within its own list, and lets the
/// rest go; Ctrl+Shift+click adds that run. A modified press is kept from the item (no selection)
/// and acted on at release, so it can still become a Ctrl+drag. What each of these means for the
/// page is the page's own (<see cref="IDragPicker"/>).
/// </para>
/// <para>
/// Listens on the way down (tunnel, handled too): a <see cref="Button"/> takes the press in its
/// class handler, which runs before anything attached to it - see ModifierClick. The press itself is never consumed, so selection,
/// tapping and clicking work as before; only once the pointer has moved far enough is it
/// captured, which also stops the click it would have been.
/// </para>
/// <para>
/// <b>A section of lists.</b> On the inventory page the host is a whole section, aside from its header: every items
/// control or grid in it marked <c>picklist</c> is picked over - worn
/// slots, the belt, both panes' grids - in the host's coordinates. On the vault the host is the
/// sheet's scroll, and the rectangle is measured on what it scrolls. A press in the part marked
/// <c>sectionhead</c> is the header's, never a drag's. (A host that was one list - the vault's list
/// of cards - went with that list.)
/// </para>
/// </summary>
public sealed class DragPick
{
    /// <summary>How far the pointer must move before a press becomes a drag.</summary>
    private const double Threshold = 6;

    /// <summary>How close to a list's top or bottom edge the pointer scrolls it, and how fast.</summary>
    private const double EdgeBand = 28, MostScrollPerTick = 24;

    /// <summary>The class that marks a section's pick lists, and the one that marks its header.</summary>
    public const string ListClass = "picklist", HeadClass = "sectionhead";

    /// <summary>
    /// The class of a button that is the item itself though it does not stand straight in its container: a
    /// folder's tile on the vault's sheet, beside which - in the one container - its name box stands while it is
    /// being named. A grid's tile says the same by "tile".
    /// </summary>
    public const string ItemClass = "pickitem";

    private readonly Control _host;
    private readonly Func<IDragPicker?> _picker;

    private IReadOnlyList<ItemsControl> _lists = []; // what this press picks over
    private IReadOnlyList<TileLayer> _layers = [];   // and the grids among it, whose items are no controls
    private Point? _pressedAt;           // in the reference's coordinates - see Reference
    private Point _pointer;              // the last pointer position, in the host's coordinates
    private bool _pressedOnEmpty;
    private object? _pressedItem;        // the item under a modified press, acted on at release
    private object? _carryable;          // the item under a plain press, which a drag may carry
    private KeyModifiers _modifiers;     // Ctrl and Shift, as they were at the press
    private object? _anchor;             // where a Shift+click's run starts
    private bool _dragging;
    private bool _flipping;              // Ctrl+drag: the rectangle flips what it covers
    private readonly PickedBefore _before = new(); // picked before the drag touched it
    private readonly Dictionary<Control, Rect> _hitBoxes = new(ReferenceEqualityComparer.Instance); // this drag's, see HitBox
    private Canvas? _overlay;
    private Border? _band;
    private DispatcherTimer? _scroller;

    private DragPick(Control host, Func<IDragPicker?> picker)
    {
        _host = host;
        _picker = picker;

        host.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => End(), RoutingStrategies.Direct | RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Drag picking over a section: every <c>picklist</c> inside <paramref name="section"/> that
    /// is showing, from anywhere in it but its <c>sectionhead</c>.
    /// </summary>
    /// <param name="picker">The page, read at each press since a DataContext can change.</param>
    public static DragPick AttachSection(Control section, Func<IDragPicker?> picker) =>
        new(section, picker);

    /// <summary>The lists a press here picks over: the section's that show.</summary>
    private IReadOnlyList<ItemsControl> ListsNow() =>
        [.. _host.GetVisualDescendants().OfType<ItemsControl>()
            .Where(list => list.Classes.Contains(ListClass) && list.IsEffectivelyVisible)];

    /// <summary>
    /// What the rectangle is measured in: a scroll's content, where the host is the scroll - the vault's
    /// sheet - so the rectangle's start stays on the item it began by as the sheet scrolls under it; or
    /// the section itself.
    /// </summary>
    private Visual? Reference => _host is ScrollViewer { Content: Visual content } ? content : _host;

    /// <summary>
    /// What scrolls while the rectangle is held at its edge (it never scrolled, but on a list
    /// of the vault's that is gone): the host where it is a scroll - the vault's sheet - or the one around the
    /// host, the Items page's.
    /// </summary>
    private ScrollViewer? Scroller => _host as ScrollViewer ?? _host.FindAncestorOfType<ScrollViewer>();

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedAt = null;
        _pressedItem = null;
        _carryable = null;

        var point = e.GetCurrentPoint(_host);
        if (!point.Properties.IsLeftButtonPressed || _picker() is not { } picker || Reference is not { } reference
            || InHeader(e.Source))
        {
            return;
        }

        _lists = ListsNow();
        _layers = LayersNow();
        if (StartsOnControl(e.Source))
            return;

        _pressedAt = e.GetPosition(reference);
        _pointer = point.Position;
        _modifiers = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift);

        var container = ContainerOf(e.Source)?.Container;
        object? item = LayerItem(e) ?? container?.DataContext;
        _pressedOnEmpty = item is null;
        if (item is null)
            return;

        if (picker.IsPicked(item) is null)
        {
            // A modified click on what takes no pick is the page's to answer, and kept from the
            // item - through to its own click it would replace the selection it meant to add to.
            if (_modifiers != KeyModifiers.None)
            {
                _pressedAt = null;
                e.Handled = true;
                picker.RefusedPick(item);
                return;
            }

            // A plain drag off it is still the page's to carry or refuse - what the inventory
            // lock keeps says so, rather than a rectangle starting from it.
            _carryable = item;
            return;
        }

        if (_modifiers == KeyModifiers.None)
        {
            // An ordinary click: the item's own (select it), and the next run's start. Kept too,
            // should the press become a drag that carries it. A tile clicks on its release, so a
            // drag can carry the picks before the click would make it the one selected.
            _anchor = item;
            _carryable = item;
            return;
        }

        // Ctrl or Shift on an item: picking comes on now, keeping what was selected - Explorer
        // adds to it. Refused (an item on the cursor), the press is left to the item.
        if (!picker.BeginDragPick(keepSelected: true))
        {
            _pressedAt = null;
            return;
        }

        // Kept from the item, so it does not select it alone. What it does waits for the
        // release - it may yet be the start of a Ctrl+drag.
        _pressedItem = item;
        e.Handled = true;
    }

    /// <summary>Whether a press landed in the section's header, which is never a drag's.</summary>
    private bool InHeader(object? source)
    {
        for (var at = source as Visual; at is not null && at != _host; at = at.GetVisualParent())
        {
            if (at is StyledElement element && element.Classes.Contains(HeadClass))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A press on something that acts or scrolls - a button, a box being typed in, a picker, a
    /// scrollbar - is that thing's, never the start of a drag. The one button that is not: an
    /// item that <i>is</i> a button, as a grid's tile is, which is the item itself.
    /// </summary>
    private bool StartsOnControl(object? source)
    {
        if (source is not Visual origin)
            return false;

        if ((origin as Button ?? origin.FindAncestorOfType<Button>()) is { } button)
        {
            // A worn item's tile sits inside its slot's frame rather than straight in the
            // container, and is the item all the same.
            if ((button.Classes.Contains("tile") || button.Classes.Contains(ItemClass)) && ContainerOf(button) is not null)
                return false;

            return !(button.GetVisualParent() is Control parent && _lists.Any(list => list.IndexFromContainer(parent) >= 0));
        }

        return origin is ScrollBar
               || origin.FindAncestorOfType<ScrollBar>() is not null
               || ItemPanels.IsBoxOrPicker(origin);
    }

    /// <summary>The grids a press here picks over: the section's that show and are marked as its lists are.</summary>
    private IReadOnlyList<TileLayer> LayersNow() =>
        [.. _host.GetVisualDescendants().OfType<TileLayer>()
            .Where(layer => layer.Classes.Contains(ListClass) && layer.IsEffectivelyVisible)];

    /// <summary>The grid's item under the pointer, where the press came through one of the grids picked over.</summary>
    private object? LayerItem(PointerEventArgs e) =>
        TileLayer.Of(e.Source) is { } layer && _layers.Contains(layer) ? layer.TileAt(e.GetPosition(layer)) : null;

    /// <summary>
    /// Every item picked over and where the rectangle must reach to take it, in the reference's coordinates: a
    /// list's by its container (<see cref="HitBox"/>), a grid's by where its layer draws it.
    /// </summary>
    private IEnumerable<(object Item, Rect Box)> Spots(Visual reference)
    {
        foreach (var container in Containers())
        {
            if (container.DataContext is { } item)
                yield return (item, HitBox(container, reference));
        }

        foreach (var layer in _layers)
        {
            if (layer.TranslatePoint(default, reference) is not { } origin)
                continue;

            foreach (var tile in layer.Tiles)
            {
                var place = TileLayer.PlaceOf(tile);
                yield return (tile, new Rect(origin + place.TopLeft, place.Size));
            }
        }
    }

    /// <summary>The item's container the press landed in, and its list - or null for empty space.</summary>
    private (Control Container, ItemsControl List)? ContainerOf(object? source)
    {
        for (var at = source as Visual; at is not null && at != _host; at = at.GetVisualParent())
        {
            if (at is not Control control)
                continue;

            foreach (var list in _lists)
            {
                if (list.IndexFromContainer(control) >= 0)
                    return (control, list);
            }
        }

        return null;
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (_pressedAt is not { } start || Reference is not { } reference)
            return;

        _pointer = e.GetPosition(_host);

        if (!_dragging)
        {
            var now = e.GetPosition(reference);
            if (Math.Abs(now.X - start.X) < Threshold && Math.Abs(now.Y - start.Y) < Threshold)
                return;

            _flipping = _modifiers.HasFlag(KeyModifiers.Control);
            _pressedItem = null;

            // A plain drag off an item moves it, as Explorer's does - the picks with it where it
            // is one. The page holds it from here and puts it down on the release; the press is
            // let go, so the item's own click never comes.
            if (_carryable is { } carried && _picker() is { } carrier && carrier.BeginCarry(carried))
            {
                _carryable = null;
                _pressedAt = null;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            _carryable = null;

            if (_picker() is not { } picker || !picker.BeginDragPick(keepSelected: _flipping))
            {
                _pressedAt = null;
                return;
            }

            if (!_flipping)
                picker.ClearPicks();

            _dragging = true;
            foreach (object item in Items().Concat(_layers.SelectMany(layer => layer.Tiles)))
                _before.Seen(item, picker.IsPicked(item) == true);

            e.Pointer.Capture(_host);
            ShowBand();
            _scroller = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) => ScrollAtEdge());
            _scroller.Start();
        }

        Update();
        e.Handled = true;
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            End();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        bool pressed = _pressedAt is not null;
        _pressedAt = null;

        if (!pressed || _picker() is not { } picker)
            return;

        if (_pressedItem is { } item)
        {
            _pressedItem = null;
            e.Handled = true;
            ModifiedClick(picker, item);
            picker.EndPick();
        }
        else if (_pressedOnEmpty && _modifiers == KeyModifiers.None)
        {
            picker.ClickedEmpty();
        }
    }

    /// <summary>Explorer's Ctrl+click, Shift+click and Ctrl+Shift+click on one item.</summary>
    private void ModifiedClick(IDragPicker picker, object item)
    {
        bool ctrl = _modifiers.HasFlag(KeyModifiers.Control), shift = _modifiers.HasFlag(KeyModifiers.Shift);

        if (!shift)
        {
            picker.SetPicked(item, picker.IsPicked(item) != true);
            _anchor = item;
            return;
        }

        // The run from the anchor, in reading order within the item's own list - or the one
        // item, with no anchor there (it was in another list, or nothing was clicked yet). The
        // anchor stays put, as Explorer's does, so a second Shift+click reshapes the same run.
        var order = (_anchor is null ? null : picker.ReadingOrder(_anchor, item)?.ToList()) ?? InReadingOrder(item);
        int to = order.IndexOf(item), from = _anchor is null ? -1 : order.IndexOf(_anchor);
        if (to < 0)
            return;

        if (from < 0)
        {
            from = to;
            _anchor = item;
        }

        if (!ctrl)
            picker.ClearPicks();

        for (int at = Math.Min(from, to); at <= Math.Max(from, to); at++)
            picker.SetPicked(order[at], true);
    }

    /// <summary>
    /// The pickable items of <paramref name="item"/>'s list as a reader meets them: row by row,
    /// left to right. A list's order and a grid's alike, where an item's place in its collection
    /// may be anything.
    /// </summary>
    private List<object> InReadingOrder(object item)
    {
        var picker = _picker();

        // A grid's: by where its layer draws each.
        if (item is PD2SPCompanion.Shell.ViewModels.ItemTileViewModel one && _layers.FirstOrDefault(each => each.Tiles.Contains(one)) is { } layer)
        {
            return [.. layer.Tiles
                .Where(each => picker?.IsPicked(each) is not null)
                .OrderBy(each => each.Top)
                .ThenBy(each => each.Left)];
        }

        var list = _lists.FirstOrDefault(each => each.GetRealizedContainers().Any(container => ReferenceEquals(container.DataContext, item)));
        if (list is null)
            return [];

        return [.. list.GetRealizedContainers()
            .Where(container => container.DataContext is { } each && picker?.IsPicked(each) is not null)
            .OrderBy(container => container.Bounds.Top)
            .ThenBy(container => container.Bounds.Left)
            .Select(container => container.DataContext!)];
    }

    private void End()
    {
        _pressedAt = null;
        if (!_dragging)
            return;

        _dragging = false;
        _scroller?.Stop();
        _scroller = null;
        _before.Clear();
        _hitBoxes.Clear();

        if (_overlay is not null)
        {
            AdornerLayer.SetAdorner(_host, null);
            _overlay = null;
            _band = null;
        }

        _picker()?.EndPick();
    }

    /// <summary>
    /// Where the rectangle must reach to take an item, in the reference's coordinates: its tile,
    /// where it draws one, else its container. A worn slot's container is wider than its box -
    /// the slots' side margins are the gaps between them - so a rectangle begun in a gap took the
    /// slot beside it. Measured once a drag; nothing moves within a section or
    /// a list's panel while it lasts.
    /// </summary>
    private Rect HitBox(Control container, Visual reference)
    {
        if (_hitBoxes.TryGetValue(container, out var box))
            return box;

        Visual measured = container.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Classes.Contains("tile"))
                          ?? (Visual)container;

        box = measured.TranslatePoint(default, reference) is { } at
            ? new Rect(at, measured.Bounds.Size)
            : default;

        return _hitBoxes[container] = box;
    }

    private IEnumerable<Control> Containers() => _lists.SelectMany(list => list.GetRealizedContainers());

    private IEnumerable<object> Items() => Containers().Select(container => container.DataContext).OfType<object>();

    /// <summary>
    /// Picks what the rectangle touches and lets go of the rest - or, on a Ctrl+drag, adds what it
    /// touches and leaves the rest as it was - and moves the drawn rectangle to match. Ctrl+drag
    /// flipped what it touched, taking the picked out, where "Ctrl + Drag: add area" said it adds; Ctrl + click is the
    /// one that takes one out.
    /// </summary>
    private void Update()
    {
        if (_pressedAt is not { } start || Reference is not { } reference || _picker() is not { } picker)
            return;

        var end = _host.TranslatePoint(_pointer, reference) ?? start;
        var area = new Rect(start, end).Normalize();

        foreach (var (item, box) in Spots(reference))
        {
            if (picker.IsPicked(item) is not { } picked)
                continue;

            bool inside = box.Intersects(area), was = _before.Seen(item, picked);
            bool wanted = _flipping ? was || inside : inside;
            if (wanted != picked)
                picker.SetPicked(item, wanted);
        }

        if (_overlay is not null && _band is not null
            && reference.TranslatePoint(area.TopLeft, _overlay) is { } topLeft)
        {
            Canvas.SetLeft(_band, topLeft.X);
            Canvas.SetTop(_band, topLeft.Y);
            _band.Width = area.Width;
            _band.Height = area.Height;
        }
    }

    /// <summary>
    /// The rectangle, on the host's adorner layer: drawn over it, sized to it and clipped to it,
    /// and never in the way of a click. In the colours a picked item is lit with: gold, as
    /// every selection (it was the red wash).
    /// </summary>
    private void ShowBand()
    {
        _band = new Border
        {
            Background = Resource("[Brush] Selection - Current") ?? new SolidColorBrush(Colors.Goldenrod, 0.15),
            BorderBrush = Resource("[UI] GradBrush - Frame - Golden#02") ?? Brushes.Goldenrod,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };

        _overlay = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
        _overlay.Children.Add(_band);
        AdornerLayer.SetAdorner(_host, _overlay);
    }

    private IBrush? Resource(string key) =>
        _host.TryFindResource(key, _host.ActualThemeVariant, out object? found) ? found as IBrush : null;

    /// <summary>
    /// Scrolls while the pointer is held near, or past, an edge of what scrolls: up and down, and along where it
    /// is wider than it shows (a vault thirty columns wide). The stretches it brings into sight are read into the
    /// rectangle's reach as they are drawn.
    /// </summary>
    private void ScrollAtEdge()
    {
        if (!_dragging || Scroller is not { } scroll || _host.TranslatePoint(_pointer, scroll) is not { } at)
            return;

        static double Step(double at, double size) =>
            at < EdgeBand ? -Speed(EdgeBand - at)
            : at > size - EdgeBand ? Speed(at - (size - EdgeBand))
            : 0;

        double down = Step(at.Y, scroll.Bounds.Height);
        double along = scroll.Extent.Width > scroll.Viewport.Width + 0.5 ? Step(at.X, scroll.Bounds.Width) : 0;
        if (down == 0 && along == 0)
            return;

        var to = new Vector(
            Math.Clamp(scroll.Offset.X + along, 0, Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width)),
            Math.Clamp(scroll.Offset.Y + down, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
        if (to == scroll.Offset)
            return;

        scroll.Offset = to;

        // What the scroll has brought into sight is drawn on the next layout: the rectangle reaches it then.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_dragging)
                return;

            _lists = ListsNow();
            _layers = LayersNow();
            _hitBoxes.Clear();
            Update();
        }, DispatcherPriority.Loaded);
    }

    // Faster the further in (or past) the band: a nudge at its edge, full speed well beyond it.
    private static double Speed(double depth) => Math.Min(MostScrollPerTick, 4 + depth / 2);
}
