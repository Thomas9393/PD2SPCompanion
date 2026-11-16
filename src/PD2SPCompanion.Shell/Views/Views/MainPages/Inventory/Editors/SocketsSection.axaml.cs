using System;
using System.Linq;

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
/// The item panel's socket section; its view model is <see cref="ViewModels.SocketsEditor"/>. What only a window
/// knows is here: a socket dragged out of the row and where it is let go of; the section lit
/// while a gem, a rune or a jewel the Items page carries is over it; and a socket's right-click menu.
/// </summary>
public partial class SocketsSection : UserControl
{
    private TopLevel? _window;

    /// <summary>A filled socket the left button went down on, and where: a drag of it begins once the pointer has left that place.</summary>
    private (SocketViewModel Socket, Point At)? _pressed;

    /// <summary>The socket the right button went down on: what its release opens the menu of.</summary>
    private SocketViewModel? _context;

    private const double DragStart = ItemPanels.DragStart;

    public SocketsSection()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, Strip_Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(ContextRequestedEvent, Socket_Context);

        // Ctrl and Shift on a socket, and the rectangle drawn from the section's empty room: the hand that
        // selects on the Items page and in the vault.
        PD2SPCompanion.Shell.CustomControls.Base.DragPick.AttachSection(this, () => Editor);

        // A caption longer than its room scrolls in it; another socket's, or another item's, begins at its top - the
        // offset of the one before cut the next one's name.
        CaptionNameText.PropertyChanged += Caption_Changed;
        CaptionEffectText.PropertyChanged += Caption_Changed;
    }

    private void Caption_Changed(object? sender, AvaloniaPropertyChangedEventArgs changed)
    {
        if (changed.Property == TextBlock.TextProperty)
            CaptionScroll.Offset = default;
    }

    /// <summary>The left button's second press of a double click went down on a socket: its click opens a jewel.</summary>
    private bool _doubled;

    /// <summary>
    /// A socket clicked: selected alone - the one selected alone, clicked again, stays so (one is always
    /// selected). The second click of
    /// a double click selects it and opens a jewel to be edited, where it is one and jewels open here ("Double
    /// click (LMB) on the socket item enter edit mode (if available)"). Ctrl and Shift never reach here: the
    /// selection's own hand takes those presses.
    /// </summary>
    private void Socket_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SocketViewModel socket } button || Editor is not { } editor)
            return;

        // The click makes it the captioned one, whose tooltip the caption stands in for: an open one closed now.
        ToolTip.SetIsOpen(button, false);

        if (_doubled)
        {
            _doubled = false;
            editor.SelectOnly(socket);
            if (editor.CanEditSelected && editor.OpensJewels)
                editor.OpenJewelCommand.Execute(socket);
            return;
        }

        editor.SelectCommand.Execute(socket);
    }

    private SocketsEditor? Editor => DataContext as SocketsEditor;

    /// <summary>The Items page's model: it knows what is carried, and which cells lie bare.</summary>
    private VP_ViewModel_Inv_L? Inventory => (this.FindAncestorOfType<MainView>()?.DataContext as MainViewModel)?.Inventory;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ItemClipboard.Shared.Changed += Clipboard_Changed;
        ItemClipboard.Shared.AddPasteTaker(TakePaste);
        SocketCarry.Shared.PropertyChanged += Carry_Changed;

        if ((_window = TopLevel.GetTopLevel(this)) is null)
            return;

        _window.AddHandler(PointerMovedEvent, Window_Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Before anything under the pointer and before the Items page's own last word on a carry, which puts
        // back what is let go of off its page: the release over this page never comes through that page's view.
        _window.AddHandler(PointerReleasedEvent, Window_Released, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ItemClipboard.Shared.Changed -= Clipboard_Changed;
        ItemClipboard.Shared.RemovePasteTaker(TakePaste);
        SocketCarry.Shared.PropertyChanged -= Carry_Changed;
        _pointerOver = false;
        Editor?.EndCarry(null, cancel: true);
        if (Editor is { } editor)
            editor.IsDropTarget = false;

        _window?.RemoveHandler(PointerMovedEvent, Window_Moved);
        _window?.RemoveHandler(PointerReleasedEvent, Window_Released);
        _window?.RemoveHandler(KeyDownEvent, Window_KeyDown);
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Clipboard_Changed() => Editor?.SayCut();

    /// <summary>The pointer is over the section: Ctrl + V is then a paste into the item's sockets, where what is held goes into one.</summary>
    private bool _pointerOver;

    /// <summary>Ctrl + V, asked of the section before the Items page pastes on a page (ItemClipboard.PasteUnderPointer).</summary>
    private bool TakePaste()
    {
        if (!_pointerOver || !IsEffectivelyVisible || Editor is not { CanPasteHere: true } editor)
            return false;

        editor.PasteHereCommand.Execute(null);
        return true;
    }

    /// <summary>The socket of the row a pointer event came from, or null.</summary>
    private static SocketViewModel? SocketOf(object? source)
    {
        for (var at = source as Visual; at is not null; at = at.GetVisualParent())
        {
            if (at is Button { DataContext: SocketViewModel socket } cell && cell.Classes.Contains("socketcell"))
                return socket;
        }

        return null;
    }

    private bool Over(PointerEventArgs e) => IsEffectivelyVisible && new Rect(Bounds.Size).Contains(e.GetPosition(this));

    /// <summary>
    /// A press on a socket: with the left button a drag may begin from it; with the right it is selected, and its
    /// release opens its menu. The cell's own click selects it.
    /// </summary>
    private void Strip_Pressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        _pressed = null;
        _doubled = false;

        if (SocketOf(e.Source) is not { } socket || Editor is not { } editor)
            return;

        if (point.Properties.IsRightButtonPressed)
        {
            // Selected by the press, as any right click selects - but for one of several selected, whose menu
            // is theirs.
            if (!(socket.IsSelected && editor.SelectedSockets.Count > 1))
                editor.SelectOnly(socket);
            _context = socket;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            _doubled = e.ClickCount == 2;

            // A drag may begin from it - not with Ctrl or Shift held, which are the selection's.
            if (socket.IsFilled && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == KeyModifiers.None)
                _pressed = (socket, point.Position);
        }
    }

    /// <summary>
    /// The pointer moved, anywhere in the window. A socket pressed and now dragged is carried - the right button
    /// pressed meanwhile calls that off, as it puts back whatever else is carried. And while the Items page
    /// carries something that would go into this section's item, the section is lit under it.
    /// </summary>
    private void Window_Moved(object? sender, PointerEventArgs e)
    {
        _pointerOver = Over(e);
        if (Editor is not { } editor)
            return;

        if (editor.IsCarrying)
        {
            if (e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
            {
                editor.EndCarry(null, cancel: true);
                e.Handled = true;
                return;
            }

            // The item it would go into, lit green under the pointer as for any carried gem: worn, or in a grid.
            if (Inventory is { } page)
            {
                var slot = SlotAt(e);
                page.HoverSlot(slot);
                if (slot is null)
                    page.HoverOver(Over(e) ? null : CellAt(e));
            }

            return;
        }

        if (_pressed is { } pressed)
        {
            var now = e.GetCurrentPoint(this);
            if (!now.Properties.IsLeftButtonPressed)
            {
                _pressed = null;
            }
            else if (Math.Abs(now.Position.X - pressed.At.X) > DragStart || Math.Abs(now.Position.Y - pressed.At.Y) > DragStart)
            {
                _pressed = null;
                if (editor.BeginCarry(pressed.Socket))
                    e.Pointer.Capture(this);
            }

            return;
        }

        bool lit = Over(e) && Inventory?.WouldSocketEdited(editor.Host) == true;
        if (editor.IsDropTarget != lit)
            editor.IsDropTarget = lit;
    }

    /// <summary>
    /// The button let go of. What the Items page carries, over this section: into the item's next socket, as let
    /// go of on the item itself - or refused in its words, and put back. A socket carried out of the row: on a
    /// bare cell of a grid, there; back over the section, nowhere; anywhere else, where what arrives lands.
    /// </summary>
    private void Window_Released(object? sender, PointerReleasedEventArgs e)
    {
        _pressed = null;
        if (Editor is not { } editor)
            return;

        if (editor.IsDropTarget)
            editor.IsDropTarget = false;

        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (!editor.IsCarrying)
        {
            if (Over(e) && Inventory is { } items && (items.IsDragCarrying || items.IsTabCarrying) && items.DropOnEdited(editor.Host))
                e.Handled = true;

            return;
        }

        e.Pointer.Capture(null);
        bool back = Over(e);
        GridCell? at = back ? null : CellAt(e);

        // On another item with an empty socket: into it, as many as it has room for.
        if (!back && Inventory is { } page && (SlotAt(e)?.Item ?? page.HostOver(at)) is { } host && page.TakesFromRow(host, editor.Host)
            && editor.EndCarryInto(gems => page.SocketFromRow(host, gems, editor.EjectAll)))
        {
            e.Handled = true;
            return;
        }

        // On a cell: there, and the rest about it, as the footprint showed (LandFromRow).
        if (!back && Inventory is { } landing)
            landing.LandFromRow(at, () => editor.EndCarry(at is not null && landing.IsFreeRoom(at) ? at : null, cancel: false));
        else
            editor.EndCarry(null, cancel: back);
        e.Handled = true;
    }

    /// <summary>A carry out of the row over, however - let go of, Esc, the right button, off the book: whatever it lit goes out.</summary>
    private void Carry_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs changed)
    {
        if (changed.PropertyName != nameof(SocketCarry.IsCarrying) || SocketCarry.Shared.IsCarrying || Inventory is not { } page)
            return;

        page.HoverSlot(null);
        page.HoverOver(null);
    }

    /// <summary>The worn slot under the pointer, wherever in the window - the character's or the mercenary's; null else.</summary>
    private EquipSlotViewModel? SlotAt(PointerEventArgs e)
    {
        if (_window is null || _window.InputHitTest(e.GetPosition(_window)) is not Visual hit)
            return null;

        for (var at = hit; at is not null; at = at.GetVisualParent())
        {
            if (at is Control { DataContext: EquipSlotViewModel slot })
                return slot;
        }

        return null;
    }

    /// <summary>The cell of a grid under the pointer, wherever in the window (ItemPanels.CellAt).</summary>
    private GridCell? CellAt(PointerEventArgs e) => ItemPanels.CellAt(_window, e);

    /// <summary>Esc calls a carry off.</summary>
    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Editor is { IsCarrying: true } editor)
        {
            editor.EndCarry(null, cancel: true);
            e.Handled = true;
        }
    }

    /// <summary>
    /// A socket's menu: by the right button let go of on it, where the pointer is; by the Menu key or Shift + F10
    /// with a socket of the row having the keyboard, beside it.
    /// </summary>
    private void Socket_Context(object? sender, ContextRequestedEventArgs e)
    {
        if (Editor is not { } editor)
            return;

        bool byPointer = e.TryGetPosition(this, out Point _);
        var socket = byPointer ? _context : SocketOf(e.Source);
        _context = null;
        if (socket is null)
            return;

        if (!byPointer)
            editor.SelectCommand.Execute(socket);

        ActMenuService.Shared.Show(editor.MenuFor(socket), byPointer ? null : e.Source as Control);
        e.Handled = true;
    }
}
