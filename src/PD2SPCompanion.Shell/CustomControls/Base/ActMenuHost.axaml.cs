using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// The window's one right-click menu, drawn (ActMenuHost.axaml) and opened and closed for
/// <see cref="ActMenuService"/>: where the pointer is, or beside the control the Menu key was pressed on. Closed by
/// a pick, Esc, any press outside it - which goes on to what it was made on, as Windows' does - the wheel, and the
/// window losing the keyboard.
/// </summary>
public partial class ActMenuHost : UserControl
{
    private TopLevel? _window;

    /// <summary>What had the keyboard as the menu opened, and whether by the keys (its outline drawn): given it back so.</summary>
    private IInputElement? _before;

    private bool _byKey;

    public ActMenuHost()
    {
        InitializeComponent();
        DataContext = ActMenuService.Shared;

        // Esc, with the keyboard in the menu - where a menu's rows take it as it opens (ListKeys).
        Frame.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        // In the list a row opened: Left and Esc shut the list and give the keyboard back to its row.
        SubFrame.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Left)
            {
                CloseSub(byKey: true);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        // Right on a row that opens a list: the list, its first line lit.
        Frame.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Right && _window?.FocusManager?.GetFocusedElement() is Button { DataContext: MenuAct { HasMore: true, IsEnabled: true } } row
                && Frame.IsVisualAncestorOf(row))
            {
                OpenSub(row, byKey: true);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        Menu.Closed += (_, _) =>
        {
            ActMenuService.Shared.IsOpen = false;
            Sub.IsOpen = false;

            // The keyboard back where it was, if the menu had it: a row of the tree goes on answering the arrows.
            var had = _before;
            _before = null;
            if (had is Visual { IsEffectivelyVisible: true }
                && _window?.FocusManager?.GetFocusedElement() is Visual now && Inside(now))
            {
                had.Focus(_byKey ? NavigationMethod.Directional : NavigationMethod.Pointer);
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActMenuService.Shared.Asked += Open;
        ActMenuService.Shared.Shut += Close;

        if ((_window = TopLevel.GetTopLevel(this)) is null)
            return;

        _window.AddHandler(PointerPressedEvent, Window_Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(PointerWheelChangedEvent, Window_Wheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (_window is Window window)
            window.Deactivated += Window_Deactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActMenuService.Shared.Asked -= Open;
        ActMenuService.Shared.Shut -= Close;
        Close();

        _window?.RemoveHandler(PointerPressedEvent, Window_Pressed);
        _window?.RemoveHandler(PointerWheelChangedEvent, Window_Wheel);
        _window?.RemoveHandler(KeyDownEvent, Window_KeyDown);
        if (_window is Window window)
            window.Deactivated -= Window_Deactivated;
        _window = null;

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The room the menu's frame keeps about itself for its shadow (its Margin): what an offset takes back to stand the frame on a line.</summary>
    private const double Shadow = 12;

    /// <summary>
    /// Opens the menu: beside a control - under it, a little in from its left, the row the Menu key was pressed
    /// on - or with none where the pointer is. A place of no width says "from the control's own left edge": a
    /// button's list of its ways hangs under the button as a drop-down does (the Items page's split button).
    /// </summary>
    private void Open(object? beside, Rect? at)
    {
        if (beside is Control { IsEffectivelyVisible: true } anchor)
        {
            bool flush = at is { Width: 0 };
            Menu.PlacementTarget = anchor;
            Menu.PlacementRect = flush ? null : at;
            Menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            Menu.HorizontalOffset = flush ? -Shadow : Math.Min(24, (at?.Width ?? anchor.Bounds.Width) / 2);
            Menu.VerticalOffset = -Shadow;
        }
        else
        {
            Menu.PlacementTarget = this;
            Menu.PlacementRect = null;
            Menu.Placement = PlacementMode.Pointer;
            Menu.HorizontalOffset = -10;
            Menu.VerticalOffset = -10;
        }

        _before = _window?.FocusManager?.GetFocusedElement();
        _byKey = _before is StyledElement lit && lit.Classes.Contains(":focus-visible");

        Menu.IsOpen = true;
        ActMenuService.Shared.IsOpen = true;
    }

    private void Close()
    {
        Sub.IsOpen = false;
        if (Menu.IsOpen)
            Menu.IsOpen = false;
    }

    /// <summary>Whether something is the menu's: in it, or in the list a row of it opened.</summary>
    private bool Inside(Visual at) =>
        ReferenceEquals(at, Frame) || Frame.IsVisualAncestorOf(at) || ReferenceEquals(at, SubFrame) || SubFrame.IsVisualAncestorOf(at);

    /// <summary>The row whose list is open.</summary>
    private Button? _subOf;

    /// <summary>
    /// A row's list, beside the row. By the keys, its line in force is lit - else its first; by the pointer the
    /// keyboard stays where it was, and the first Down lights it (ListKeys, as for any menu).
    /// </summary>
    private void OpenSub(Button row, bool byKey)
    {
        if (row.DataContext is not MenuAct { More: { Count: > 0 } more })
            return;

        if (Sub.IsOpen && ReferenceEquals(_subOf, row))
            return;

        Sub.IsOpen = false;
        _subOf = row;
        SubRows.ItemsSource = more;
        Sub.PlacementTarget = row;
        Sub.IsOpen = true;

        if (byKey)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var lines = SubFrame.GetVisualDescendants().OfType<Button>().Where(each => each.IsEffectivelyEnabled).ToList();
                (lines.FirstOrDefault(each => each.DataContext is MenuAct { IsOn: true }) ?? lines.FirstOrDefault())?.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Loaded);
        }
    }

    private void CloseSub(bool byKey = false)
    {
        if (!Sub.IsOpen)
            return;

        Sub.IsOpen = false;
        var row = _subOf;
        _subOf = null;
        if (byKey)
            row?.Focus(NavigationMethod.Directional);
    }

    /// <summary>The pointer over a row: its list opens, where it has one; over any other row of the menu, the open list shuts.</summary>
    private void Row_Entered(object? sender, PointerEventArgs e)
    {
        if (sender is not Button row || !Frame.IsVisualAncestorOf(row))
            return;

        if (row.DataContext is MenuAct { HasMore: true, IsEnabled: true })
            OpenSub(row, byKey: false);
        else
            CloseSub();
    }

    private void Act_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: MenuAct act } clicked)
            return;

        // A row that opens a list does that, and the menu stays.
        if (act.HasMore)
        {
            if (clicked is Button row)
                OpenSub(row, byKey: row.Classes.Contains(":focus-visible"));
            return;
        }

        // One that asks first: its question, in the menu, which stays; the next press is the answer.
        if (act is { Asks: true, IsAsking: false, IsEnabled: true })
        {
            act.IsAsking = true;
            return;
        }

        ActMenuService.Shared.Pick(act, run => Dispatcher.UIThread.Post(run));
    }

    /// <summary>A press in the window - the menu is a window of its own, so anywhere outside it: it closes, and the press goes on.</summary>
    private void Window_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (Menu.IsOpen && !(e.Source is Visual pressed && Inside(pressed)))
            Close();
    }

    private void Window_Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (Menu.IsOpen && !(e.Source is Visual turned && Inside(turned)))
            Close();
    }

    /// <summary>Esc while the keyboard is not in the menu - it opened under the pointer, and no key has lit a row yet.</summary>
    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Menu.IsOpen && e.Key == Key.Escape && !e.Handled)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e) => Close();
}
