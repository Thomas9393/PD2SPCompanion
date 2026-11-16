using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A line of tools that stays one line (after Explorer's command bar and VS Code's menu
/// bar): where the page is too narrow for all of them, the least needed
/// are left off the line and a "more" button stands at its end to list them - the line never goes on to a
/// second. The panel only decides which are left off and where the rest lie; the page builds the list from
/// <see cref="Overflowed"/>.
/// <code>
/// &lt;base:OverflowRow Spacing="6"&gt;
///     &lt;Button .../&gt;                                    never left off
///     &lt;Border Classes="toolrule" base:OverflowRow.IsRule="True"/&gt;
///     &lt;Button base:OverflowRow.Leaves="1" .../&gt;         the first to go
///     &lt;Button base:OverflowRow.Leaves="2" .../&gt;         then this
///     &lt;Button base:OverflowRow.IsFar="True" .../&gt;       at the line's far end while everything fits
///     &lt;Button base:OverflowRow.IsMore="True" .../&gt;      shown only while something is left off
/// &lt;/base:OverflowRow&gt;
/// </code>
/// The tools marked <c>IsFar</c> - the last of the line, with the rule before them - stand at its right end
/// while every tool has its room (as Explorer keeps Details and VS Code its layout
/// buttons: what is done at the left, what is shown at the right); the rule goes then, the gap parting them.
/// Once anything is left off the line they close up with the rest, as a line with no far end.
/// A rule is drawn only between tools that are on the line: none at its start or its end, and one for two that
/// would meet. A tool left off is laid out of the row altogether, where nothing draws and nothing is pressed;
/// it is still there, so its command, its tooltip and its key work as before.
/// </summary>
public sealed class OverflowRow : Panel
{
    /// <summary>The order a tool leaves the line in, from 1; 0 - not set - never leaves.</summary>
    public static readonly AttachedProperty<int> LeavesProperty =
        AvaloniaProperty.RegisterAttached<OverflowRow, Control, int>("Leaves");

    /// <summary>A rule between groups of tools: drawn only with a tool on the line at either side.</summary>
    public static readonly AttachedProperty<bool> IsRuleProperty =
        AvaloniaProperty.RegisterAttached<OverflowRow, Control, bool>("IsRule");

    /// <summary>
    /// A tool of the line's far end - and the rule before the first of them: at the right while everything
    /// fits. The far tools are the last of the line.
    /// </summary>
    public static readonly AttachedProperty<bool> IsFarProperty =
        AvaloniaProperty.RegisterAttached<OverflowRow, Control, bool>("IsFar");

    /// <summary>The button that lists what is left off: on the line, at its end, only while something is.</summary>
    public static readonly AttachedProperty<bool> IsMoreProperty =
        AvaloniaProperty.RegisterAttached<OverflowRow, Control, bool>("IsMore");

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<OverflowRow, double>(nameof(Spacing), 6);

    static OverflowRow()
    {
        AffectsMeasure<OverflowRow>(SpacingProperty);
        LeavesProperty.Changed.AddClassHandler<Control>((control, _) => (control.Parent as OverflowRow)?.InvalidateMeasure());
    }

    public OverflowRow()
    {
        ClipToBounds = true;
    }

    public static int GetLeaves(Control control) => control.GetValue(LeavesProperty);
    public static void SetLeaves(Control control, int value) => control.SetValue(LeavesProperty, value);

    public static bool GetIsRule(Control control) => control.GetValue(IsRuleProperty);
    public static void SetIsRule(Control control, bool value) => control.SetValue(IsRuleProperty, value);

    public static bool GetIsFar(Control control) => control.GetValue(IsFarProperty);
    public static void SetIsFar(Control control, bool value) => control.SetValue(IsFarProperty, value);

    public static bool GetIsMore(Control control) => control.GetValue(IsMoreProperty);
    public static void SetIsMore(Control control, bool value) => control.SetValue(IsMoreProperty, value);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>What is on the line, in its order, rules among them; decided by the measure.</summary>
    private List<Control> _shown = [];

    /// <summary>
    /// The tools left off the line, in the line's order, with a null between two that a rule stood between: what
    /// the "more" button lists. Empty while everything fits.
    /// </summary>
    public IReadOnlyList<Control?> Overflowed { get; private set; } = [];

    /// <summary>Something has been left off the line, or put back on it.</summary>
    public event Action? OverflowChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        var tools = new List<Control>();
        Control? more = null;
        double tall = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (!child.IsVisible)
                continue;

            if (GetIsMore(child))
                more = child;
            else
                tools.Add(child);

            tall = Math.Max(tall, child.DesiredSize.Height);
        }

        var left = new HashSet<Control>();
        var shown = Lay(tools, left);

        if (!double.IsInfinity(availableSize.Width) && Wide(shown) > availableSize.Width)
        {
            // The "more" button has its room from here on; then one tool at a time, the first to go first.
            double room = availableSize.Width - (more is null ? 0 : more.DesiredSize.Width + Spacing);
            foreach (var tool in tools.Where(tool => GetLeaves(tool) > 0 && !GetIsRule(tool)).OrderBy(GetLeaves))
            {
                left.Add(tool);
                shown = Lay(tools, left);
                if (Wide(shown) <= room)
                    break;
            }

            if (more is not null && left.Count > 0)
                shown.Add(more);
        }

        // What is listed: the tools left off, a gap where a rule lay between two of them.
        var listed = new List<Control?>();
        bool gap = false;
        foreach (var tool in tools)
        {
            if (GetIsRule(tool))
            {
                gap = listed.Count > 0;
                continue;
            }

            if (!left.Contains(tool))
                continue;

            if (gap)
                listed.Add(null);

            gap = false;
            listed.Add(tool);
        }

        bool changed = listed.Count != Overflowed.Count || !listed.SequenceEqual(Overflowed);
        _shown = shown;
        Overflowed = listed;
        if (changed)
            OverflowChanged?.Invoke();

        return new Size(Math.Min(Wide(shown), double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width), tall);
    }

    /// <summary>The tools on the line without those left off, and the rules that still part two of them.</summary>
    private static List<Control> Lay(List<Control> tools, HashSet<Control> left)
    {
        var line = new List<Control>();
        Control? rule = null;

        foreach (var tool in tools)
        {
            if (GetIsRule(tool))
            {
                // A rule waits for a tool after it, and is one rule however many stood together.
                if (line.Count > 0)
                    rule = tool;

                continue;
            }

            if (left.Contains(tool))
                continue;

            if (rule is not null)
            {
                line.Add(rule);
                rule = null;
            }

            line.Add(tool);
        }

        return line;
    }

    private double Wide(List<Control> line) =>
        line.Sum(each => each.DesiredSize.Width) + Math.Max(0, line.Count - 1) * Spacing;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var on = new HashSet<Control>(_shown);
        double x = 0;

        // The far tools at the right end, while nothing is left off and there is room to part them from the
        // rest: the room is the gap, and the rule that led them is not drawn across it.
        double gap = Overflowed.Count == 0 ? finalSize.Width - Wide(_shown) : 0;
        int far = gap > 0 ? _shown.FindIndex(GetIsFar) : -1;

        for (int i = 0; i < _shown.Count; i++)
        {
            var child = _shown[i];

            if (i == far)
            {
                if (GetIsRule(child) && gap > 2 * Spacing)
                {
                    on.Remove(child);
                    x += gap + child.DesiredSize.Width + Spacing;
                    continue;
                }

                x += gap;
            }

            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }

        // Left off: out of the row altogether.
        foreach (var child in Children)
        {
            if (!on.Contains(child))
                child.Arrange(new Rect(-100000, 0, child.DesiredSize.Width, finalSize.Height));
        }

        return finalSize;
    }
}
