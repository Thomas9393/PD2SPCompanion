using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A tooltip that says what its control cuts short, and only then (a short name needs none): a long item name in a row of
/// the search's list, a deep folder's path over the vault page,
/// a folder's name the tree's edge cuts across.
/// <code>&lt;Button base:TrimmedTip.Tip="{Binding Name}"&gt; ... &lt;TextBlock TextTrimming="CharacterEllipsis"/&gt;</code>
/// It is the control's tooltip in every other way - placed by <c>ToolTip.Placement</c> - but it opens only while
/// a text inside the control is drawn with an ellipsis.
/// </summary>
public sealed class TrimmedTip : AvaloniaObject
{
    /// <summary>The whole of what the control says; shown while the control cuts any of its text short.</summary>
    public static readonly AttachedProperty<string?> TipProperty =
        AvaloniaProperty.RegisterAttached<TrimmedTip, Control, string?>("Tip");

    /// <summary>
    /// What the control's tooltip says whether or not anything is cut - a key's line, say - with
    /// <see cref="TipProperty"/> over it while something is: a row of the Items page's list of pages says what
    /// the right button does, and its page's whole name once the list is too narrow for it.
    /// </summary>
    public static readonly AttachedProperty<string?> AlwaysProperty =
        AvaloniaProperty.RegisterAttached<TrimmedTip, Control, string?>("Always");

    static TrimmedTip()
    {
        TipProperty.Changed.AddClassHandler<Control>((control, change) => ToolTip.SetTip(control, Said(control, cut: true)));
        AlwaysProperty.Changed.AddClassHandler<Control>((control, change) => ToolTip.SetTip(control, Said(control, cut: true)));

        // Raised on the control alone, and going nowhere from it: listened for by class.
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>((control, opening) =>
        {
            if (!control.IsSet(TipProperty))
                return;

            bool cut = Trims(control);
            if (GetAlways(control) is { Length: > 0 })
                ToolTip.SetTip(control, Said(control, cut));
            else if (!cut)
                opening.Cancel = true;
        });
    }

    /// <summary>The tooltip's words: what is cut, over what is always said.</summary>
    private static string? Said(Control control, bool cut) =>
        (GetTip(control), GetAlways(control)) switch
        {
            (var tip, null or "") => tip,
            ({ Length: > 0 } tip, var always) when cut => tip + System.Environment.NewLine + always,
            (_, var always) => always,
        };

    public static string? GetAlways(Control control) => control.GetValue(AlwaysProperty);

    public static void SetAlways(Control control, string? value) => control.SetValue(AlwaysProperty, value);

    public static string? GetTip(Control control) => control.GetValue(TipProperty);

    public static void SetTip(Control control, string? value) => control.SetValue(TipProperty, value);

    /// <summary>
    /// Whether the control cuts anything short: a text drawn with an ellipsis, a path with steps left out
    /// (<see cref="CrumbPanel"/>), or a text the edge of the scroll it is in cuts across.
    /// </summary>
    private static bool Trims(Control control) =>
        control.GetSelfAndVisualDescendants().Any(each => each switch
        {
            CrumbPanel { IsShortened: true } => true,
            TextBlock { IsEffectivelyVisible: true } text =>
                text.TextLayout.TextLines.Any(line => line.HasCollapsed) || IsCutByScroll(text),
            _ => false,
        });

    /// <summary>A text some of which is out of sight to the left or the right of the scroll it is in.</summary>
    private static bool IsCutByScroll(TextBlock text)
    {
        if (text.FindAncestorOfType<ScrollViewer>() is not { } scroll || text.TranslatePoint(default, scroll) is not { } at)
            return false;

        return at.X < -0.5 || at.X + text.Bounds.Width > scroll.Viewport.Width + 0.5;
    }
}
