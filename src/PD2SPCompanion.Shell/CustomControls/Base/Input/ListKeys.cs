using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// Rows by the keys where a list is no picker's - the pickers' own keys (<see cref="PickerKeys"/>)
/// said again for the lists outside them:
/// <list type="bullet">
/// <item>a menu - Sort, the folder menu, the tools the line had no room for, History: as it opens the row in force is
/// focused, else the first; Up and Down walk its rows, Enter picks the one reached (the button's own Enter), Esc
/// closes it;</item>
/// <item>a search box with its finds under it - the Forge's, the Search page's keyword: Down or Enter steps into the
/// finds, Up and Down walk them, Up from the first goes back to the box.</item>
/// </list>
/// A menu's rows are any of its <c>Button.pickone</c> or <c>Button.historyrow</c>; a box's finds a list that says so
/// (<see cref="IsListProperty"/>), or the one a box names (<see cref="IntoProperty"/>). Before, only the pickers and
/// the top bar's search answered an arrow key.
/// </summary>
public static class ListKeys
{
    /// <summary>On a list of rows in a page - the Forge's finds: Up and Down walk it once a row has the focus.</summary>
    public static readonly AttachedProperty<bool> IsListProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsList", typeof(ListKeys));

    public static bool GetIsList(Control control) => control.GetValue(IsListProperty);

    public static void SetIsList(Control control, bool value) => control.SetValue(IsListProperty, value);

    /// <summary>
    /// On a text box: the list its Down and Enter step into, where it is not the list beside it in its section - the
    /// Search page's keyword box, whose finds are in the section under its own.
    /// </summary>
    public static readonly AttachedProperty<Control?> IntoProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Control?>("Into", typeof(ListKeys));

    public static Control? GetInto(TextBox box) => box.GetValue(IntoProperty);

    public static void SetInto(TextBox box, Control? value) => box.SetValue(IntoProperty, value);

    private static bool _registered;

    /// <summary>The windows already hooked: attached twice, one press would walk two rows.</summary>
    private static readonly HashSet<TopLevel> Hooked = [];

    /// <summary>The menus' contents already hooked: a popup keeps its content from one opening to the next.</summary>
    private static readonly ConditionalWeakTable<Control, object> Menus = [];

    /// <summary>The box the keys stepped out of into a list: Up from the list's first row goes back to it.</summary>
    private static WeakReference<TextBox>? _from;

    /// <summary>Hooks the window, and every menu as it opens.</summary>
    public static void Attach(TopLevel window)
    {
        if (!_registered)
        {
            _registered = true;
            Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, change) =>
            {
                if (change.NewValue is true)
                    Opened(popup);
            });
        }

        if (Hooked.Add(window))
            window.AddHandler(InputElement.KeyDownEvent, (_, e) => OnWindowKey(window, e), RoutingStrategies.Tunnel);
    }

    // ---- menus --------------------------------------------------------------------------------------------------

    /// <summary>
    /// A popup opened: if it is a menu - rows in it - its keys are hooked. Opened by a key, the row in force takes the
    /// focus, else the first; opened by a click, the menu's frame holds the keyboard and the first Down lights that
    /// row, as Windows' menus do.
    /// </summary>
    private static void Opened(Popup popup)
    {
        var top = TopLevel.GetTopLevel(popup);
        bool byKey = top?.FocusManager?.GetFocusedElement() is Control opener && opener.Classes.Contains(":focus-visible");

        Dispatcher.UIThread.Post(() =>
        {
            if (!popup.IsOpen || popup.Child is not Control content)
                return;

            var rows = MenuRows(content);
            if (rows.Count == 0)
                return;

            if (!Menus.TryGetValue(content, out _))
            {
                Menus.Add(content, new object());
                content.AddHandler(InputElement.KeyDownEvent, (_, e) => OnMenuKey(content, e), RoutingStrategies.Tunnel);
            }

            if (byKey)
            {
                Light(Held(rows) ?? rows[0]);
            }
            else
            {
                content.Focusable = true;
                content.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The row in force: a sort's mode, the history's step now.</summary>
    private static Button? Held(List<Button> rows) =>
        rows.FirstOrDefault(each => each.Classes.Contains("held") || each.Classes.Contains("current"));

    /// <summary>A menu's rows, in the order they are drawn: those in sight and live.</summary>
    private static List<Button> MenuRows(Visual content) =>
        [.. content.GetVisualDescendants().OfType<Button>()
            .Where(button => (button.Classes.Contains("pickone") || button.Classes.Contains("historyrow"))
                             && button.IsEffectivelyVisible && button.IsEffectivelyEnabled)];

    private static void OnMenuKey(Control content, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Up or Key.Down or Key.Home or Key.End))
            return;

        var rows = MenuRows(content);
        if (rows.Count == 0)
            return;

        var focused = TopLevel.GetTopLevel(content)?.FocusManager?.GetFocusedElement() as Button;
        int at = focused is null ? -1 : rows.IndexOf(focused);

        var next = e.Key switch
        {
            Key.Home => rows[0],
            Key.End => rows[^1],
            // No row yet - the menu opened by a click: the first key lights the row in force, as Windows' does.
            _ when at < 0 => Held(rows) ?? (e.Key == Key.Down ? rows[0] : rows[^1]),
            Key.Down => rows[(at + 1) % rows.Count],
            _ => rows[(at - 1 + rows.Count) % rows.Count],
        };
        Move(next, e);
    }

    // ---- a search box and its finds ---------------------------------------------------------------------------------

    private static void OnWindowKey(TopLevel window, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Up or Key.Down or Key.Enter or Key.Return)
            || ItemPanels.TopVeil(window) is not null)
        {
            return;
        }

        switch (window.FocusManager?.GetFocusedElement())
        {
            // From the box: Down or Enter into its finds.
            case TextBox box when e.Key is Key.Down or Key.Enter or Key.Return && Finds(box) is { Count: > 0 } finds:
                _from = new(box);
                Move(finds.FirstOrDefault(row => row.Classes.Contains("held")) ?? finds[0], e);
                break;

            // Among the finds: the next, the one before, and from the first back to the box.
            case Button row when e.Key is Key.Up or Key.Down && row.Classes.Contains("pickone")
                                 && row.GetVisualAncestors().OfType<Control>().FirstOrDefault(GetIsList) is { } list:
                var rows = Rows(list);
                int at = rows.IndexOf(row);
                if (at < 0)
                    return;

                if (e.Key == Key.Down)
                    Move(rows[System.Math.Min(at + 1, rows.Count - 1)], e);
                else if (at > 0)
                    Move(rows[at - 1], e);
                else if (_from is not null && _from.TryGetTarget(out var back) && back.IsEffectivelyVisible)
                {
                    back.Focus(NavigationMethod.Directional);
                    e.Handled = true;
                }

                break;
        }
    }

    /// <summary>
    /// The finds a box steps into: the list it names, else a list in its own section - for a search box only, as a
    /// name's or a number's box has no finds under it.
    /// </summary>
    private static List<Button>? Finds(TextBox box)
    {
        if (GetInto(box) is { } into)
            return into.IsEffectivelyVisible ? Rows(into) : null;

        if (box is not SearchBox || box is NumericTextBox || PickerKeys.GetOwnsEnter(box))
            return null;

        var section = box.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(each => each is Border border && border.Classes.Contains("section") || each is UserControl);
        return section?.GetVisualDescendants().OfType<Control>().FirstOrDefault(GetIsList) is { } list ? Rows(list) : null;
    }

    /// <summary>A list's rows, in the order they are drawn: its pick buttons in sight.</summary>
    private static List<Button> Rows(Visual list) =>
        [.. list.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("pickone") && button.IsEffectivelyVisible && button.IsEffectivelyEnabled)];

    private static void Move(Button row, KeyEventArgs e)
    {
        Light(row);
        e.Handled = true;
    }

    private static void Light(Button row)
    {
        row.Focus(NavigationMethod.Directional);
        row.BringIntoView();
    }
}
