using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// The steps of a path in a row - "Vault > Charms > Skillers" - kept to the room they have by their front, as
/// Explorer's address bar keeps its own: the steps there is no room for go from the
/// start, whole, and an ellipsis stands where they were, so the last step - where one is - is always there.
/// <see cref="IsShortened"/> says steps were left out, for a tooltip that gives the whole path
/// (<see cref="TrimmedTip"/>).
/// </summary>
public sealed class CrumbPanel : Panel
{
    /// <summary>The room the ellipsis takes at the row's start.</summary>
    private const double Mark = 18;

    /// <summary>The ellipsis: drawn by the panel, and no step of the path.</summary>
    private readonly TextBlock _mark = new()
    {
        Text = "…",
        IsVisible = false,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The first step that is shown.</summary>
    private int _from;

    public CrumbPanel()
    {
        ClipToBounds = true;
        VisualChildren.Add(_mark);

        // The panel's own, so no style of the page reaches it: drawn black on the dark bar, it was not to be seen
        // (on a long path). The steps' size, a quieter colour than theirs.
        _mark.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("[Text] Muted"));
        _mark.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("[Type] Body"));
    }

    /// <summary>Steps were left out of what is shown, or the last cut short.</summary>
    public bool IsShortened => _from > 0 || _lastCut;

    /// <summary>The last step alone is wider than the bar: measured to the room left, its name gives way.</summary>
    private bool _lastCut;

    protected override Size MeasureOverride(Size availableSize)
    {
        double total = 0, tall = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            total += child.DesiredSize.Width;
            tall = Math.Max(tall, child.DesiredSize.Height);
        }

        _from = 0;
        if (!double.IsInfinity(availableSize.Width))
        {
            double shown = total;
            while (_from < Children.Count - 1 && shown + (_from > 0 ? Mark : 0) > availableSize.Width)
                shown -= Children[_from++].DesiredSize.Width;

            total = Math.Min(availableSize.Width, shown + (_from > 0 ? Mark : 0));
        }

        // The last step, alone and still too wide: given only the room there is, its name gives way and its
        // chevron stays (the step's template trims its name).
        _lastCut = false;
        if (!double.IsInfinity(availableSize.Width) && Children.Count > 0)
        {
            var last = Children[^1];
            double room = Math.Max(0, availableSize.Width - (_from > 0 ? Mark : 0));
            if (_from == Children.Count - 1 && last.DesiredSize.Width > room)
            {
                last.Measure(new Size(room, availableSize.Height));
                _lastCut = true;
            }
        }

        _mark.IsVisible = _from > 0;
        _mark.Measure(new Size(Mark, availableSize.Height));

        return new Size(total, tall);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _mark.Arrange(new Rect(0, 0, Mark, finalSize.Height));

        double x = _from > 0 ? Mark : 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];

            // Left out: out of the row altogether, where nothing draws and nothing is pressed.
            if (i < _from)
            {
                child.Arrange(new Rect(-100000, 0, child.DesiredSize.Width, finalSize.Height));
                continue;
            }

            // The last has what is left where even it is too long: its own text gives way then.
            double wide = i == Children.Count - 1 ? Math.Min(child.DesiredSize.Width, Math.Max(0, finalSize.Width - x)) : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, wide, finalSize.Height));
            x += wide;
        }

        return finalSize;
    }
}
