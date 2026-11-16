using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

/// <summary>
/// The grail's list. On the Items page it is the game's Compendium, and an item panel as the
/// inventory and the vault list are (<see cref="ItemPanels"/>): a press in it takes the keys - G then stores the card
/// chosen or takes it out - and what the Items page carries may be brought over it and let go of, stored as G stores
/// it. A card whose item is in the compendium is dragged off onto a cell of the Items page: taken out there.
/// </summary>
public partial class GrailPanel : UserControl
{
    public GrailPanel()
    {
        InitializeComponent();

        // A card's menu (the compendium's key, plus a right-click menu): Store or Take out.
        AddHandler(ContextRequestedEvent, Card_Context);

        // A press on a card whose item is in: a drag may begin from it. Handled-too: the list takes the press to choose it.
        AddHandler(PointerPressedEvent, Card_Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private GrailViewModel? Grail => DataContext as GrailViewModel;

    /// <summary>The Items page's model: what it carries, and where on its cells something lands.</summary>
    private VP_ViewModel_Inv_L? Inventory => (this.FindAncestorOfType<MainView>()?.DataContext as MainViewModel)?.Inventory;

    private TopLevel? _window;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if ((_window = TopLevel.GetTopLevel(this)) is null)
            return;

        // Before the handlers below: the press that gives this list the keys is seen first.
        ItemPanels.Register(this);
        SocketCarry.Shared.PropertyChanged += Carry_Changed;
        _window.AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        _window.AddHandler(PointerMovedEvent, Window_Moved, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Before anything under the pointer and before the Items page's own last word on a carry, which puts back what is
        // let go of off its page.
        _window.AddHandler(PointerReleasedEvent, Window_Released, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _pressed = null;
        if (Grail is { Carried: not null } grail)
            grail.EndCarry();

        SocketCarry.Shared.PropertyChanged -= Carry_Changed;
        _window?.RemoveHandler(KeyDownEvent, Window_KeyDown);
        _window?.RemoveHandler(PointerMovedEvent, Window_Moved);
        _window?.RemoveHandler(PointerReleasedEvent, Window_Released);
        _window = null;
        ItemPanels.Unregister(this);
        base.OnDetachedFromVisualTree(e);
    }

    private bool Over(PointerEventArgs e) => IsEffectivelyVisible && new Rect(Bounds.Size).Contains(e.GetPosition(this));

    /// <summary>
    /// A card's menu: by the right button let go of on it, where the pointer is; by the Menu key or Shift + F10 with a
    /// card having the keyboard, beside it. The card is not chosen for it - choosing one searches for its copies.
    /// </summary>
    private void Card_Context(object? sender, ContextRequestedEventArgs e)
    {
        if (Grail is not { } grail || (e.Source as Control)?.DataContext is not GrailCard card)
            return;

        var menu = grail.MenuFor(card);
        if (menu.Rows.Count == 0)
            return;

        bool byPointer = e.TryGetPosition(this, out Point _);
        ActMenuService.Shared.Show(menu, byPointer ? null : e.Source as Control);
        e.Handled = true;
    }

    // ---- G: the card chosen, its item in or out --------------------------------------------------

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Grail is not { } grail || !IsEffectivelyVisible)
            return;

        // Esc calls a drag off the card off.
        if (e.Key == Key.Escape && grail.Carried is not null)
        {
            grail.EndCarry();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.G || e.KeyModifiers != KeyModifiers.None || !ItemPanels.OwnsKeys(this) || ItemPanels.IsVeiled(_window)
            || ItemPanels.IsBoxOrPicker(_window?.FocusManager?.GetFocusedElement() as Visual) || grail.Selected is not { } card)
        {
            return;
        }

        grail.Transfer(card);
        e.Handled = true;
    }

    // ---- dragged off a card, and dropped on the list ---------------------------------------------------------------------

    /// <summary>The card a left press went down on, and where - a drag from it begins past ItemPanels.DragStart.</summary>
    private (GrailCard Card, Point At)? _pressed;

    private void Card_Pressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == KeyModifiers.None
            && (e.Source as Control)?.DataContext is GrailCard { IsOwned: true } card)
        {
            _pressed = (card, point.Position);
        }
    }

    /// <summary>
    /// The pointer moved, anywhere in the window. A card pressed and now dragged carries its item - the right button
    /// pressed meanwhile calls that off; while it is carried, the Items page lights where it would land.
    /// </summary>
    private void Window_Moved(object? sender, PointerEventArgs e)
    {
        if (Grail is not { } grail)
            return;

        if (grail.Carried is not null)
        {
            if (e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
            {
                grail.EndCarry();
                e.Handled = true;
                return;
            }

            Inventory?.HoverOver(Over(e) ? null : ItemPanels.CellAt(_window, e));
            return;
        }

        if (_pressed is not { } pressed)
            return;

        var now = e.GetCurrentPoint(this);
        if (!now.Properties.IsLeftButtonPressed)
        {
            _pressed = null;
        }
        else if (Math.Abs(now.Position.X - pressed.At.X) > ItemPanels.DragStart || Math.Abs(now.Position.Y - pressed.At.Y) > ItemPanels.DragStart)
        {
            _pressed = null;
            if (grail.BeginCarry(pressed.Card))
                e.Pointer.Capture(this);
        }
    }

    /// <summary>
    /// The button let go of. A card's item carried: on a cell of the Items page, out of the compendium there; anywhere
    /// else, nothing. What the Items page carries, let go of over the list: into the compendium.
    /// </summary>
    private void Window_Released(object? sender, PointerReleasedEventArgs e)
    {
        _pressed = null;
        if (Grail is not { BrowseOnly: false } grail || e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (grail.Carried is not null)
        {
            e.Pointer.Capture(null);
            if (!Over(e) && ItemPanels.CellAt(_window, e) is { } at && Inventory is { } page)
                grail.DropCarried(takeOut => page.LandFromRow(at, takeOut));
            else
                grail.EndCarry();

            e.Handled = true;
            return;
        }

        if (Over(e) && Inventory is { } items && items.Session.IsHolding && items.DropOnCompendium())
            e.Handled = true;
    }

    /// <summary>A carry off a card over, however - let go of, Esc on either page, the right button, off the book: what it lit goes out.</summary>
    private void Carry_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs changed)
    {
        if (changed.PropertyName == nameof(SocketCarry.IsCarrying) && !SocketCarry.Shared.IsCarrying)
            Inventory?.HoverOver(null);
    }
}
