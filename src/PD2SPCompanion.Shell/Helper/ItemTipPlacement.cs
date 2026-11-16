using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;

namespace PD2SPCompanion.Shell.Helper;

/// <summary>
/// Where an item's tooltip opens, as the game opens it: under the item, a
/// gap below it; above it, the same gap, where the screen has no room below. Used by every host of
/// an item's tooltip through <c>ToolTip.axaml</c>'s <c>itemtip</c> style.
/// <para>
/// Placement "Bottom" with an offset flipped too, but kept the offset's sign: a flipped tooltip
/// came down by the gap and covered the item's top edge (measured: 7px over a mercenary's circlet).
/// So the side is chosen here, and the gap goes the right way on either.
/// </para>
/// <para>
/// <b>Where it fits neither under the item nor over it</b> - a wall of lines - it opens beside the item, its
/// top at the item's, and slides up as far as the screen needs: there it has the screen's whole height (unlike
/// Diablo 4's, which always opens beside: below for every item that
/// fits, the game's own way, and beside only for the one that does not). To the item's right; to its left
/// where the screen ends. Until then it opened where it was "most at home" and slid over the item. An item
/// taller still than the screen is cut to it and scrolls (<see cref="Most"/>; ItemActionTip, the [ and ] keys).
/// </para>
/// </summary>
public static class ItemTipPlacement
{
    /// <summary>Between the item and its tooltip, either side.</summary>
    public const double Gap = 6;

    public static CustomPopupPlacementCallback Callback { get; } = Place;

    /// <summary>Kept clear of the screen's edges by a tooltip cut to the screen's height.</summary>
    private const double Edge = 8;

    /// <summary>
    /// For the tests alone (PD2SPC_TIP_ROOM, "side,most" in the page's own units): the room under and over an
    /// item never counted as more than <c>side</c>, and the screen's height as <c>most</c> - a small screen
    /// pretended on a large one, so the placement beside an item and the scrollbar can be driven.
    /// </summary>
    private static readonly (double Side, double Most)? Pretended = ReadPretended();

    private static (double, double)? ReadPretended()
    {
        string[] parts = (System.Environment.GetEnvironmentVariable("PD2SPC_TIP_ROOM") ?? "").Split(',');
        return parts.Length == 2
               && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double side)
               && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double most)
            ? (side, most)
            : null;
    }

    /// <summary>
    /// The most height an item's tooltip may take on the screen <paramref name="window"/> is on: the working
    /// area's, less a margin either end. What is taller is cut to it and scrolls.
    /// </summary>
    public static double Most(TopLevel? window)
    {
        // A screen's height is in the window's units; the tooltip is laid out in the layout's own, drawn at
        // the window's scale (Services.Settings.UiScale) - so the screen holds that much more of it, or less.
        double scale = PD2SPCompanion.Shell.Services.Settings.UiScale.Current;

        if (Pretended is { } pretended)
            return pretended.Most / scale;

        return window is WindowBase { Screens: { } screens } based && screens.ScreenFromWindow(based) is { } screen
            ? Math.Max(120, screen.WorkingArea.Height / screen.Scaling / scale - 2 * Edge)
            : double.PositiveInfinity;
    }

    private static void Place(CustomPopupPlacement placement)
    {
        bool below = true, beside = false;

        // The room under the item and over it, on the screen it is on, against the tooltip's height.
        // Measured from the item itself: the anchor rectangle is not in its coordinates, and read as
        // if it were it counted the item's place twice (a charm mid-grid opened above).
        var target = placement.Target;
        if (TopLevel.GetTopLevel(target)?.Screens?.ScreenFromVisual(target) is { } screen)
        {
            var top = target.PointToScreen(default);
            var bottom = target.PointToScreen(new Point(0, target.Bounds.Height));
            double scale = screen.Scaling;
            double roomBelow = (screen.WorkingArea.Bottom - bottom.Y) / scale;
            double roomAbove = (top.Y - screen.WorkingArea.Y) / scale;
            if (Pretended is { } pretended)
                (roomBelow, roomAbove) = (Math.Min(roomBelow, pretended.Side), Math.Min(roomAbove, pretended.Side));

            double needed = placement.PopupSize.Height + Gap;

            // Below when it fits; above when only that fits; beside the item when neither does.
            below = roomBelow >= needed;
            beside = !below && roomAbove < needed;
        }

        if (beside)
        {
            // The tooltip's top at the item's, to its right: slid up as the screen needs, to the left where it ends.
            placement.Anchor = PopupAnchor.TopRight;
            placement.Gravity = PopupGravity.BottomRight;
            placement.Offset = new Point(Gap, 0);
            placement.ConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipX | PopupPositionerConstraintAdjustment.SlideY;
            return;
        }

        placement.Anchor = below ? PopupAnchor.Bottom : PopupAnchor.Top;
        placement.Gravity = below ? PopupGravity.Bottom : PopupGravity.Top;
        placement.Offset = new Point(0, below ? Gap : -Gap);
        // The side is chosen; the rest is kept on the screen by sliding, never flipping again.
        placement.ConstraintAdjustment = PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.SlideY;
    }
}
