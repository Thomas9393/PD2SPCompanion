using Avalonia;
using Avalonia.Controls;

using PD2SPCompanion.Shell.CustomControls.Base;

namespace PD2SPCompanion.Shell.Helper;

/// <summary>
/// A tooltip whose text lists what keys do is drawn as an item's tooltip draws its strip - the key as a cap,
/// what it does beside it (<see cref="KeyedTip"/>: the item tooltip's key caps for all
/// other controls that have a key hint). Set on every tooltip by the app's ToolTip style.
/// <para>
/// As it comes up, a tooltip showing a text with such a line in it shows a <see cref="KeyedTip"/> of that text
/// instead. Only a text: a tooltip that shows a control - an item's, a worn slot's, a vault card's - or plain
/// words is left exactly as it is. The control the tooltip belongs to keeps its text, which is what UI
/// Automation reads of it.
/// </para>
/// <para>
/// <b>Not a content template on the tooltip.</b> That was the first way (a template answering only for such a
/// text), and with any template set a tooltip whose content is a control came up empty: the worn items' and
/// the vault cards' tooltips were blank for the half hour it stood (tip-actions and tip-room failed on it the same minute).
/// </para>
/// </summary>
public static class KeyedTips
{
    /// <summary>Set by the app's ToolTip style: every tooltip draws its keys as caps.</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ToolTip, bool>("Enabled", typeof(KeyedTips));

    public static bool GetEnabled(ToolTip tip) => tip.GetValue(EnabledProperty);
    public static void SetEnabled(ToolTip tip, bool value) => tip.SetValue(EnabledProperty, value);

    static KeyedTips()
    {
        // The style sets Enabled again each time a tooltip opens: one subscription a tooltip (ClickThroughTips).
        EnabledProperty.Changed.AddClassHandler<ToolTip>((tip, change) =>
        {
            if (change.NewValue is true)
            {
                tip.AttachedToVisualTree -= Draw;
                tip.AttachedToVisualTree += Draw;
            }
        });
    }

    private static void Draw(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ToolTip { Content: string text } tip && KeyedTip.HasKeys(text))
            tip.Content = new KeyedTip { Text = text };
    }
}
