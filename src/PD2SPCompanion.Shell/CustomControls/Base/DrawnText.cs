using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// What a control that draws its own words (<see cref="ItemFactLines"/>, <see cref="StatRowHead"/>) needs to
/// put them where a text block would have: the layout gives a control a whole number of device pixels, and a
/// line drawn at a fraction of one is a blurred line.
/// </summary>
internal static class DrawnText
{
    private static double ScaleOf(Visual visual) => TopLevel.GetTopLevel(visual)?.RenderScaling ?? 1;

    /// <summary>A size as the layout gives a control one: each side up to the next device pixel.</summary>
    public static Size Whole(Visual visual, double width, double height)
    {
        double scale = ScaleOf(visual);
        return new Size(Math.Ceiling(width * scale - 1e-6) / scale, Math.Ceiling(height * scale - 1e-6) / scale);
    }

    /// <summary>A place as the layout gives a control one: at the nearest device pixel.</summary>
    public static double Snapped(Visual visual, double value)
    {
        double scale = ScaleOf(visual);
        return Math.Round(value * scale) / scale;
    }
}

/// <summary>
/// One text of a control that draws its words itself, as its text block was to UI Automation. A peer of that
/// control - many of them of the one control - as a tile's is of its layer's (TilePeer): only a control's peer
/// can say where it is on the screen. The window tests read a tooltip and a property's row by their texts.
/// </summary>
internal sealed class DrawnTextPeer(ControlAutomationPeer drawn, string text) : ControlAutomationPeer(drawn.Owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string? GetAutomationIdCore() => null;

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => [];

    protected override string GetClassNameCore() => nameof(TextBlock);

    protected override AutomationPeer? GetLabeledByCore() => null;

    protected override string? GetNameCore() => text;

    protected override string? GetHelpTextCore() => null;

    protected override AutomationPeer? GetParentCore() => drawn;

    protected override bool HasKeyboardFocusCore() => false;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override void SetFocusCore()
    {
    }

    protected override bool TrySetParent(AutomationPeer? parent) => false;
}
