using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using PD2SPCompanion.Shell.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.Controls;

/// <summary>
/// Draws the arrows between a skill and the ones it depends on, the way the game's own tree
/// does.
/// <para>
/// The plates are positioned by layout - rows size to their tallest name - so nothing here
/// computes where they are. It reads back where they actually landed and routes between
/// them, which means the arrows keep up with a window resize or a longer name without
/// knowing anything about either.
/// </para>
/// </summary>
public sealed class SkillLinks : Control
{
    /// <summary>The grid of plates to draw between.</summary>
    public static readonly StyledProperty<ItemsControl?> SourceProperty =
        AvaloniaProperty.Register<SkillLinks, ItemsControl?>(nameof(Source));

    public ItemsControl? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    static SkillLinks()
    {
        AffectsRender<SkillLinks>(SourceProperty);
    }

    public SkillLinks()
    {
        // The plates move whenever anything is laid out again, and this has no way to know
        // that from its own properties.
        LayoutUpdated += (_, _) => InvalidateVisual();
        IsHitTestVisible = false;
    }

    /// <summary>How far above the skill it feeds the sideways run sits.</summary>
    private const double Jog = 14;

    /// <summary>The least a line drops before it is allowed to turn.</summary>
    private const double MinimumDrop = 6;

    private const double HeadLength = 7;
    private const double HeadWidth = 5;

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source)
            return;

        // Where each skill's plate ended up, by skill id.
        var placed = new Dictionary<int, Rect>();
        var cells = new Dictionary<int, SkillCellViewModel>();

        foreach (var container in source.GetRealizedContainers())
        {
            if (container.DataContext is not SkillCellViewModel cell)
                continue;

            // The plate, not its container. A container fills its whole grid cell, and
            // neighbouring cells touch - so measuring those made every plate look as though
            // it began exactly where the one above ended, and links between adjacent rows
            // were thrown away as impossible.
            var plate = Plate(container);
            if (plate is null)
                continue;

            var origin = plate.TranslatePoint(default, this);
            if (origin is null)
                continue;

            placed[cell.Skill.Id] = new Rect(origin.Value, plate.Bounds.Size);
            cells[cell.Skill.Id] = cell;
        }

        foreach (var (id, target) in placed)
        {
            if (!cells.TryGetValue(id, out var cell))
                continue;

            foreach (int required in cell.Skill.Requires)
            {
                if (!placed.TryGetValue(required, out var from))
                    continue;

                // A line reads as live once the skill it comes from has been taken.
                bool satisfied = cells.TryGetValue(required, out var source2) && source2.HasPoints;
                Draw(context, from, target, satisfied);
            }
        }
    }

    /// <summary>
    /// Routes from the bottom of one plate to the top of another, square cornered: down,
    /// across, then down again. Straight through when they share a column.
    /// <para>
    /// The sideways run sits just above the skill being fed rather than just below the one
    /// it leaves. Several of these cross columns - the Amazon's Lightning Bolt is fed by
    /// Poison Javelin and Power Strike from two different ones - and turning early sends the
    /// line across whatever sits in the row between. The gap above the target is empty by
    /// construction, so that is where it turns.
    /// </para>
    /// </summary>
    private static void Draw(DrawingContext context, Rect from, Rect to, bool satisfied)
    {
        var pen = new Pen(
            satisfied ? Brushes.Goldenrod : new SolidColorBrush(Color.Parse("#6B5C38")),
            satisfied ? 2 : 1.4,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);

        double startX = from.Center.X;
        double startY = from.Bottom;
        double endX = to.Center.X;

        // The dependent skill is above its prerequisite; nothing sensible to draw.
        if (to.Top <= from.Bottom)
            return;

        // Stop short of the plate so the head is not swallowed by its border - but never
        // above where the line starts, which would turn the arrow back on itself.
        double endY = Math.Max(startY, to.Top - HeadLength);

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(startX, startY), isFilled: false);

            if (Math.Abs(startX - endX) < 1)
            {
                path.LineTo(new Point(endX, endY));
            }
            else
            {
                double elbow = Math.Max(startY + MinimumDrop, endY - Jog);
                path.LineTo(new Point(startX, elbow));
                path.LineTo(new Point(endX, elbow));
                path.LineTo(new Point(endX, endY));
            }

            path.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
        DrawHead(context, new Point(endX, endY), pen.Brush);
    }

    /// <summary>The bordered plate inside a container, which is what the eye sees.</summary>
    private static Visual? Plate(Visual container) =>
        container as Border is { } self && self.Classes.Contains("skill")
            ? self
            : container.GetVisualDescendants()
                       .OfType<Border>()
                       .FirstOrDefault(border => border.Classes.Contains("skill"));

    /// <summary>A solid head at the end, pointing into the skill it feeds.</summary>
    private static void DrawHead(DrawingContext context, Point tip, IBrush? brush)
    {
        var head = new StreamGeometry();
        using (var path = head.Open())
        {
            path.BeginFigure(new Point(tip.X, tip.Y + HeadLength), isFilled: true);
            path.LineTo(new Point(tip.X - HeadWidth, tip.Y));
            path.LineTo(new Point(tip.X + HeadWidth, tip.Y));
            path.EndFigure(true);
        }

        context.DrawGeometry(brush, null, head);
    }
}
