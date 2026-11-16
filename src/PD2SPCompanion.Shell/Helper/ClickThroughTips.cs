using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace PD2SPCompanion.Shell.Helper;

/// <summary>
/// Tooltips the pointer passes through, as the game's do: a click on an item a tooltip covers reaches the
/// item, and moving onto it shows that item's own tooltip.
/// <para>
/// An item's tooltip opens at once just under the item (ToolTip.axaml, ItemTipPlacement) - where the
/// pointer is going next on its way down a grid - and a tooltip is a window of its own: it took the click
/// meant for the item beneath, which then did not select (found measuring UI lag: a click on
/// Annihilus with Stone Barb's or a Garnet charm's tooltip over it). Earlier only a carried item's drop
/// was protected, by turning the tooltips off while carrying (ContainerGrid).
/// </para>
/// <para>
/// Windows: the tooltip's window answers its hit test with HTTRANSPARENT, so Windows asks the window
/// beneath - the editor's, on the same thread - instead. Elsewhere nothing changes.
/// </para>
/// </summary>
public static class ClickThroughTips
{
    /// <summary>Set by the app's ToolTip style: every tooltip passes the pointer on.</summary>
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ToolTip, bool>("Enabled", typeof(ClickThroughTips));

    public static bool GetEnabled(ToolTip tip) => tip.GetValue(EnabledProperty);
    public static void SetEnabled(ToolTip tip, bool value) => tip.SetValue(EnabledProperty, value);

    private const uint WmNcHitTest = 0x0084;
    private static readonly IntPtr HtTransparent = new(-1);
    private static readonly UIntPtr SubclassId = new(0x50443245);   // "PD2E"

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>Held for the life of the app: the native side calls it for every subclassed window.</summary>
    private static readonly SubclassProc Proc = (hwnd, message, wParam, lParam, id, data) =>
        message == WmNcHitTest ? HtTransparent : DefSubclassProc(hwnd, message, wParam, lParam);

    static ClickThroughTips()
    {
        // The style sets Enabled again each time a tooltip opens: one subscription per tooltip, not one
        // more per opening - they piled up on the long-lived controls' tooltips (found in a
        // long session's heap: 8 more every cycle of pages).
        EnabledProperty.Changed.AddClassHandler<ToolTip>((tip, change) =>
        {
            if (change.NewValue is true && OperatingSystem.IsWindows())
            {
                tip.AttachedToVisualTree -= PassThrough;
                tip.AttachedToVisualTree += PassThrough;
            }
        });
    }

    /// <summary>
    /// The tooltip's window passes the pointer on. Asked on every opening: SetWindowSubclass with the
    /// same procedure and id again only updates it, and Windows reuses a closed window's handle, so a
    /// list of the windows already done would have skipped a new one that happened to get an old number.
    /// </summary>
    private static void PassThrough(object? sender, VisualTreeAttachmentEventArgs e)
    {
        try
        {
            if (sender is not ToolTip tip
                || TopLevel.GetTopLevel(tip)?.TryGetPlatformHandle() is not { Handle: var hwnd } || hwnd == IntPtr.Zero)
                return;
            SetWindowSubclass(hwnd, Proc, SubclassId, UIntPtr.Zero);
        }
        catch (Exception problem)
        {
            // A tooltip that keeps its clicks is the old behaviour, not a failure worth more than a line.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
    }
}
