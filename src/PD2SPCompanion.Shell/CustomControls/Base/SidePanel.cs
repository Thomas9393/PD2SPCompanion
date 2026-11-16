using System;

using Avalonia;
using Avalonia.Controls;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A list at the left of what it leads to, <b>the list giving way</b> (on a page of
/// ordinary width the Items page's list of pages pushed the grid off its edge: the grid comes first,
/// and the list can be squeezed). Two children: the side, then the main.
/// <para>
/// The two stand together in the panel's middle. The main is measured first, in all the width there is but
/// the least the side may have, and takes what it asks for. The side has what is left: its own width where there is room, less where there is not - never
/// under <see cref="SideMinWidth"/> - and cuts its texts short as it narrows (their tooltips say them whole,
/// TrimmedTip). A Grid cannot say this: an Auto column measures its content in endless width, so a WrapPanel
/// in it never wraps; a star column has no claim on the room its content needs.
/// </para>
/// </summary>
public sealed class SidePanel : Panel
{
    /// <summary>The room between the two.</summary>
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<SidePanel, double>(nameof(Gap), 12);

    /// <summary>The least the side is ever given, however little is left.</summary>
    public static readonly StyledProperty<double> SideMinWidthProperty =
        AvaloniaProperty.Register<SidePanel, double>(nameof(SideMinWidth), 96);

    /// <summary>
    /// The side has less than its own width: what it shows may say itself shorter (the Items page's list of
    /// pages: "SP 1" for "Shared Page 1"). Its own width is what it asks for in endless room, so what is drawn
    /// in its place must not change what is laid out - the short name beside the whole one, not instead of it.
    /// </summary>
    public static readonly DirectProperty<SidePanel, bool> IsSqueezedProperty =
        AvaloniaProperty.RegisterDirect<SidePanel, bool>(nameof(IsSqueezed), panel => panel.IsSqueezed);

    private bool _isSqueezed;

    public bool IsSqueezed
    {
        get => _isSqueezed;
        private set => SetAndRaise(IsSqueezedProperty, ref _isSqueezed, value);
    }

    /// <summary>
    /// How far under the panel's top the side begins: the Items page's list of pages starts level with the
    /// first pane's grid, which has a line of its own over it (level with that grid's top, not with
    /// the header's).
    /// </summary>
    public static readonly StyledProperty<double> SideTopProperty =
        AvaloniaProperty.Register<SidePanel, double>(nameof(SideTop));

    public double SideTop
    {
        get => GetValue(SideTopProperty);
        set => SetValue(SideTopProperty, value);
    }

    /// <summary>
    /// The side is held to the main's height, and never under this (the Items page's list of
    /// pages beside a small grid made the section as tall as the list, a lot of gap): it scrolls what it has no room for.
    /// None, 0: the
    /// side is as tall as it asks, as it was.
    /// </summary>
    public static readonly StyledProperty<double> SideLeastHeightProperty =
        AvaloniaProperty.Register<SidePanel, double>(nameof(SideLeastHeight));

    public double SideLeastHeight
    {
        get => GetValue(SideLeastHeightProperty);
        set => SetValue(SideLeastHeightProperty, value);
    }

    static SidePanel()
    {
        AffectsMeasure<SidePanel>(GapProperty, SideMinWidthProperty, SideTopProperty, SideLeastHeightProperty);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double SideMinWidth
    {
        get => GetValue(SideMinWidthProperty);
        set => SetValue(SideMinWidthProperty, value);
    }

    private double _side;

    private (Control? Side, Control? Main) Parts() =>
        Children.Count switch
        {
            0 => (null, null),
            1 => (null, Children[0]),
            _ => (Children[0].IsVisible ? Children[0] : null, Children[1]),
        };

    protected override Size MeasureOverride(Size availableSize)
    {
        var (side, main) = Parts();
        _side = 0;
        if (main is null)
            return default;

        if (side is null)
        {
            IsSqueezed = false;
            main.Measure(availableSize);
            return main.DesiredSize;
        }

        // The side's own width, were there room for it.
        side.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        double own = side.DesiredSize.Width, least = Math.Min(SideMinWidth, own);

        // The main first: everything but the least the side may have.
        main.Measure(new Size(Math.Max(0, availableSize.Width - least - Gap), availableSize.Height));

        _side = double.IsInfinity(availableSize.Width)
            ? own
            : Math.Clamp(availableSize.Width - Gap - main.DesiredSize.Width, least, own);
        // As tall as the main beside it where it is held to that, and never under its least.
        double tall = SideLeastHeight > 0
            ? Math.Max(SideLeastHeight, main.DesiredSize.Height - SideTop)
            : availableSize.Height;
        side.Measure(new Size(_side, tall));
        IsSqueezed = _side < own - 0.5;

        return new Size(_side + Gap + main.DesiredSize.Width, Math.Max(SideTop + side.DesiredSize.Height, main.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (side, main) = Parts();
        if (main is null)
            return finalSize;

        if (side is null)
        {
            main.Arrange(new Rect(finalSize));
            return finalSize;
        }

        // The two together in the panel's middle, the side hard by the main with the gap between them (it stood at the
        // panel's left edge,
        // a wide page's width from the grid it leads to).
        double both = _side + Gap + main.DesiredSize.Width;
        double from = Math.Max(0, (finalSize.Width - both) / 2);
        side.Arrange(new Rect(from, SideTop, _side, side.DesiredSize.Height));

        double left = from + _side + Gap;
        main.Arrange(new Rect(left, 0, Math.Max(0, Math.Min(main.DesiredSize.Width, finalSize.Width - left)), finalSize.Height));
        return finalSize;
    }
}
