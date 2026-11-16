using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

/// <summary>
/// A tool a narrow page's line had no room for, as the list under its "more" button shows it: the tool's own
/// glyph and what its tooltip says, ticked where it is a toggle that is on, faint where it is dead. A null
/// tool is the rule between two groups.
/// </summary>
public sealed record ToolEntry(Avalonia.Media.Geometry? Glyph, string Text, bool IsEnabled, bool IsOn, Control? Tool)
{
    public bool IsRule => Tool is null;
}

public partial class VP_View_Vault_L : UserControl, PD2SPCompanion.Shell.CustomControls.Base.IShownPage
{
    public VP_View_Vault_L()
    {
        InitializeComponent();

        // Before the scroll's own: the wheel over the folder's sheet moves by cells and by stretches (Pages_Wheel).
        PagesScroll.AddHandler(PointerWheelChangedEvent, Pages_Wheel, RoutingStrategies.Tunnel);

        // The middle button held drags the sheet about, both ways (Pan_*).
        PagesScroll.AddHandler(PointerPressedEvent, Pan_Pressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PagesScroll.AddHandler(PointerMovedEvent, Pan_Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PagesScroll.AddHandler(PointerReleasedEvent, Pan_Released, RoutingStrategies.Tunnel, handledEventsToo: true);

        // The pages pick as the inventory's grids do: Ctrl and Shift clicks, the rectangle, a drag that carries.
        DragPick.AttachSection(PagesScroll, () => DataContext as IDragPicker);

        // The pointer on the sheet holds its layout: what leaves the folder leaves its place empty until the
        // pointer goes (VP_ViewModel_Vault_L.HoldSheet).
        PagesScroll.PointerEntered += (_, _) => (DataContext as VP_ViewModel_Vault_L)?.HoldSheet(true);
        PagesScroll.PointerExited += (_, _) => (DataContext as VP_ViewModel_Vault_L)?.HoldSheet(false);

        // The page's room, worked out again as it changes: the tree half of it at the most, and on a page
        // too narrow the map and the tree giving way to the sheet (Fit).
        SizeChanged += (_, _) => Fit();

        // The tools left off a narrow line are listed under the button at its end. After the layout that decided it.
        Tools.OverflowChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Tools.Overflowed.Count == 0)
                MoreMenu.IsOpen = false;
        });

        MoreMenu.Closed += (_, _) => _moreShutAt = Environment.TickCount64;

        // A right click, or the Menu key, on a folder or an item: its menu (Menu_Context).
        AddHandler(ContextRequestedEvent, Menu_Context);

        // Ctrl and Shift on the gold panel's steppers, through the tunnel the inventory's and the character's
        // use - see ModifierClick. The panel is a popup, out of the page's tree: it hears its own presses.
        GoldPanel.OnModifiedPress(StepGold);

        // Something carried over from the inventory on the other page, let go of here: stored into the
        // folder shown. Tunnelled and handled-too, so what is
        // under the pointer cannot take the release first.
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Vault_L model || e.InitialPressMouseButton != MouseButton.Left)
            return;

        // The vault's own items, carried - from this page or from the other, the vault on both: onto a folder -
        // a row of the tree, a folder's tile on the sheet - to move them there; onto the sheet, into the folder it shows;
        // onto the path for the picker; or nowhere.
        if (model.IsCarriedOver)
        {
            if (FolderAt(e) is { } into)
                model.DropCarriedOnFolder(into);
            else if (OverSheet(e) && model.SocketCarried(CardBeneath(e)))
            {
                // A gem, a rune or a jewel on a card with an empty socket: into it (VP_ViewModel_Vault_L.Socketing).
            }
            else if (OverSheet(e))
                model.DropCarriedOnSheet();
            else if (PathBar.IsEffectivelyVisible && new Rect(PathBar.Bounds.Size).Contains(e.GetPosition(PathBar)))
                model.MoveCarriedByPicker();
            else
                model.EndCarryOver();

            e.Handled = true;
            return;
        }

        if (!model.Session.IsHolding)
            return;

        if (FolderAt(e) is { } folder)
            model.DropFromInventoryOn(folder);
        else
            model.DropFromInventory(null);
        e.Handled = true;
    }

    /// <summary>
    /// A drag of the vault's items let go of where nothing took it - the spine, a page that takes no
    /// items: the carry ends. Last of all, on the window, so the inventory's cells (a withdraw there) and
    /// this page's own have already had it.
    /// </summary>
    private void Window_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is VP_ViewModel_Vault_L { IsCarrying: true } model)
            model.EndCarry();
    }

    private TopLevel? _window;

    /// <summary>
    /// Explorer's ways about the folders, anywhere in the window while the vault is shown: the
    /// mouse's two side buttons, Alt+Left and Backspace back, Alt+Right forward, Alt+Up one folder up. On
    /// the window rather than this page, so a side button works over the item on the right page too.
    /// Tunnelled and handled-too, so a list that takes the press first does not swallow it.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        OnShown();
    }

    /// <summary>In front again (PageDeck), or attached the first time: the window's keys and back button.</summary>
    public void OnShown()
    {
        if (_window is not null || TopLevel.GetTopLevel(this) is not { } window)
            return;

        // Before the handlers below: the press that gives this list the keys is seen first.
        ItemPanels.Register(this);
        _window = window;
        _window.AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _window.AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        _window.AddHandler(PointerReleasedEvent, Window_Released, RoutingStrategies.Bubble, handledEventsToo: true);
        _window.AddHandler(PointerMovedEvent, Window_Moved, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        OnHidden();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Put away behind another page (PageDeck): as leaving the window was.</summary>
    public void OnHidden()
    {
        _window?.RemoveHandler(PointerPressedEvent, Window_PointerPressed);
        _window?.RemoveHandler(KeyDownEvent, Window_KeyDown);
        _window?.RemoveHandler(PointerReleasedEvent, Window_Released);
        _window?.RemoveHandler(PointerMovedEvent, Window_Moved);
        _window = null;
        ItemPanels.Unregister(this);
    }

    /// <summary>One of the ways about the folders.</summary>
    private enum Way { Back, Forward, Up }

    /// <summary>
    /// Back, forward or up one folder, where the vault is in sight, has the keys (<see cref="ItemPanels"/>)
    /// and there is such a way - not behind a picker or a question.
    /// </summary>
    private bool Go(Way way)
    {
        if (!IsEffectivelyVisible || !ItemPanels.OwnsKeys(this) || DataContext is not VP_ViewModel_Vault_L model
            || ItemPanels.IsVeiled(TopLevel.GetTopLevel(this)))
            return false;

        var (can, command) = way switch
        {
            Way.Back => (model.CanGoBack, model.GoBackCommand),
            Way.Forward => (model.CanGoForward, model.GoForwardCommand),
            _ => (model.CanGoUp, model.GoUpCommand),
        };

        if (!can)
            return false;

        command.Execute(null);
        return true;
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // The keys back from the search box or a picker, a card pressed: Delete is the card's then (ItemPanels).
        if (IsEffectivelyVisible)
            ItemPanels.TakeKeysBack(this, e);

        // And back from a row of the tree, the sheet pressed: the arrows are the items' again. A row clicked keeps
        // the keyboard for the tree's own arrows (TreeKey), a tile takes none - so the arrows went on moving the
        // tree's mark after a click on the sheet (found by a window case).
        if (IsEffectivelyVisible && e.Source is Visual struck
            && (ReferenceEquals(struck, PagesScroll) || PagesScroll.IsVisualAncestorOf(struck))
            && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Button { DataContext: FolderPlace } row
            && row.Classes.Contains("treerow") && this.IsVisualAncestorOf(row))
        {
            Focusable = true;
            Focus(NavigationMethod.Pointer);
        }

        // A side button: this page's where the keys are its - the page last pressed in, which a side button
        // pressed over a page makes it (ItemPanels) - so with the vault on both pages each answers for itself.
        var pressed = e.GetCurrentPoint(null).Properties.PointerUpdateKind;
        if (pressed == PointerUpdateKind.XButton1Pressed && Go(Way.Back) || pressed == PointerUpdateKind.XButton2Pressed && Go(Way.Forward))
            e.Handled = true;

        // A right press on a card: it is selected, and what the menu will be for once the button is let go
        // (Menu_Context) - whatever keys are held: the chords that once acted here are rows of the menu.
        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            _context = null;
            if (IsEffectivelyVisible && DataContext is VP_ViewModel_Vault_L { IsCarrying: false } model && CardUnder(e) is { } card)
            {
                model.SelectForMenu(card);
                _context = card;
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// The game's text of a copy, on the system's clipboard - to paste in the Forge, as Ctrl + C always did here.
    /// The page's own command copies (CopySelectedCommand: the button, the key, the menu's row and the row of the
    /// more-list are one); the window's clipboard is the view's to reach.
    /// </summary>
    private void ToClipboard(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            _ = Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
    }

    /// <summary>The card the right button was pressed on: what its release opens the menu of.</summary>
    private ItemTileViewModel? _context;

    /// <summary>The item card under the pointer on this list, or null - a folder's is none.</summary>
    private ItemTileViewModel? CardUnder(PointerEventArgs e)
    {
        // On a page's sheet the items are one layer's, asked by where the pointer is (TileLayer).
        if (TileLayer.Under(e) is { } drawn && DataContext is VP_ViewModel_Vault_L sheet)
            return sheet.CardOf(drawn);

        for (var at = e.Source as Visual; at is not null && !ReferenceEquals(at, this); at = at.GetVisualParent())
        {
            // A page's tile is the item.
            if (at is Control { DataContext: ItemTileViewModel tile } && DataContext is VP_ViewModel_Vault_L model)
                return model.CardOf(tile);
        }

        return null;
    }

    /// <summary>
    /// The card of the sheet beneath the pointer while something is carried: asked of the sheet's layers by where
    /// they lie - the press that began the carry has the pointer, so what an event names as its source (CardUnder)
    /// is where the carry began, not where it is.
    /// </summary>
    private ItemTileViewModel? CardBeneath(PointerEventArgs e)
    {
        if (!OverSheet(e))
            return null;

        foreach (var layer in PagesScroll.GetVisualDescendants().OfType<TileLayer>())
        {
            if (layer.IsEffectivelyVisible && layer.TileAt(e.GetPosition(layer)) is { } card)
                return card;
        }

        return null;
    }

    /// <summary>Whether the pointer is over the folder's sheet.</summary>
    private bool OverSheet(PointerEventArgs e) =>
        PagesScroll.IsEffectivelyVisible && new Rect(PagesScroll.Bounds.Size).Contains(e.GetPosition(PagesScroll));

    /// <summary>
    /// While something is carried over the page - the vault's own cards, or what the inventory holds: the folder
    /// it would go into is lit, a row of the tree or a folder's tile, or the sheet itself for the folder shown.
    /// (A card has no cell to land on, lit under it, and the sheet does not scroll at its edges to reach
    /// one out of sight.)
    /// </summary>
    private void CarryOverPages(PointerEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Vault_L model)
            return;

        if (!model.IsCarriedOver && !(model.Session.IsHolding && IsEffectivelyVisible))
            return;

        var folder = FolderAt(e);
        CarryOverFolder(model, folder);
        bool overSheet = folder is null && OverSheet(e);
        model.HoverSheet(overSheet);
        model.HoverCard(folder is null && model.IsCarriedOver ? CardBeneath(e) : null);

        // The cells under it lit (VP_ViewModel_Vault_L.HoverFootprint).
        if (overSheet && PageCellAt(e) is { } at)
            model.HoverFootprint(at.Page, at.Column, at.Row);
        else
            model.HoverFootprint(null, 0, 0);
    }

    /// <summary>The page and the cell of it under the pointer, or null where there is none.</summary>
    private (VaultPageViewModel Page, int Column, int Row)? PageCellAt(PointerEventArgs e)
    {
        foreach (var cells in PagesScroll.GetVisualDescendants().OfType<CellLayer>())
        {
            if (!cells.IsEffectivelyVisible || cells.FindAncestorOfType<ContainerGrid>() is not { DataContext: VaultPageViewModel page })
                continue;

            var at = e.GetPosition(cells);
            if (at.X < 0 || at.Y < 0)
                continue;

            // The stretch whose own rows the pointer is over: an item reaching down from the one above is drawn
            // here too, and the cell is still this stretch's.
            int column = (int)(at.X / ItemTileViewModel.CellSize), row = (int)(at.Y / ItemTileViewModel.CellSize);
            if (column < page.Grid.Columns && row < PD2SPCompanion.Core.Collection.Vault.PageRows)
                return (page, column, row);
        }

        return null;
    }

    /// <summary>
    /// A right-click while cards are dragged puts them back, as a right-click does on the inventory.
    /// The left button is down for the whole drag, so the right one comes as a move whose update is its press,
    /// not as a press of its own.
    /// </summary>
    private void Window_Moved(object? sender, PointerEventArgs e)
    {
        CarryOverPages(e);

        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed
            && DataContext is VP_ViewModel_Vault_L { IsCarrying: true } model)
        {
            model.EndCarry();
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        // An open list - of folders, of the tools a narrow line left off - is shut by Esc, before the selection
        // is let go of by it.
        if (e.Key == Key.Escape && MoreMenu.IsOpen)
        {
            MoreMenu.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && DataContext is VP_ViewModel_Vault_L { IsMenuOpen: true } listing)
        {
            listing.IsMenuOpen = false;
            e.Handled = true;
            return;
        }

        Way? way = (e.Key, e.KeyModifiers) switch
        {
            (Key.Left, KeyModifiers.Alt) => Way.Back,
            (Key.Right, KeyModifiers.Alt) => Way.Forward,
            (Key.Up, KeyModifiers.Alt) => Way.Up,
            // Backspace only outside a box, where it would be taking a letter back.
            (Key.Back, KeyModifiers.None) when TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox => Way.Back,
            _ => null,
        };

        if (way is { } going && Go(going))
        {
            e.Handled = true;
            return;
        }

        if (TreeKey(e))
        {
            e.Handled = true;
            return;
        }

        // (A letter typed does not go to the item of that name: the player sees
        // the sprites directly, not names, and has a better search - and the letters are free for keys of the page's own,
        // X among them.)
        if (SelectionKey(e))
            e.Handled = true;
    }

    /// <summary>
    /// Explorer's keys over the list: Ctrl+A selects every card on the page, Esc
    /// clears the selection, Delete is the bin - asking twice, as it does. Not while a box or a
    /// picker has the keys, and only while the vault is the page in sight.
    /// </summary>
    private bool SelectionKey(KeyEventArgs e)
    {
        if (!IsEffectivelyVisible || !ItemPanels.OwnsKeys(this) || DataContext is not VP_ViewModel_Vault_L model)
            return false;

        if (ItemPanels.IsBoxOrPicker(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual))
        {
            return false;
        }

        // Nor while a picker or a question covers a page, wherever the focus was left.
        if (e.Key is Key.A or Key.C or Key.V or Key.X or Key.Escape or Key.Delete or Key.Enter or Key.Return or Key.F2
                or Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End
            && ItemPanels.IsVeiled(TopLevel.GetTopLevel(this)))
            return false;

        switch (e.Key, e.KeyModifiers)
        {
            case (Key.A, KeyModifiers.Control):
                model.SelectAll();
                return true;

            // The sockets over the items shown or faded, as on the Items page - the same key, the one state.
            case (Key.X, KeyModifiers.None):
                model.ToggleSockets();
                return true;

            // A carried card first, as the inventory's Esc puts back what it carries; then the selection.
            // A carried card first; then a cut, let go of where it lies (ItemClipboard); then the selection.
            case (Key.Escape, KeyModifiers.None):
                if (model.IsCarrying)
                    model.EndCarry();
                // A jewel's layer over the Edit page goes first: back to the item (MainView.OnEscape).
                else if ((Avalonia.Application.Current as App)?.Services.GetService(typeof(MainViewModel)) is MainViewModel { EditLayerInSight: not null })
                    return false;
                else if (PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.Kind == PD2SPCompanion.Shell.Services.Generic.ClipKind.Cut)
                    PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.Clear();
                else
                    model.ClearSelection();
                return true;

            case (Key.X, KeyModifiers.Control) when model.CanCut:
                model.CutCommand.Execute(null);
                return true;

            case (Key.Delete, KeyModifiers.None) when model.CanDeleteSelection:
                model.DeleteCommand.Execute(null);
                return true;

            // A folder's tile selected (just like Windows Explorer): Enter goes into it, F2
            // names it. Enter is a button's own where one has the keyboard - a row of the tree, a tool.
            case (Key.Enter or Key.Return, KeyModifiers.None) when model.SelectedFolder is { } open
                    && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Button:
                model.OpenFolderTile(open);
                return true;

            // Explorer's F2 names what is selected: a folder's tile. With none selected it does nothing - not
            // the folder shown, where the path stands.
            case (Key.F2, KeyModifiers.None) when model.SelectedFolder is not null:
                model.RenameSelectedFolder();
                return true;

            // The game's key over an item: what is selected, as the text the game copies (CopyText).
            case (Key.C, KeyModifiers.Control):
                model.CopySelectedCommand.Execute(null);
                return true;

            // Paste: what the clipboard holds, into the folder shown. A copy pasted is the
            // duplicate: there is no Duplicate key of its own.
            case (Key.V, KeyModifiers.Control):
                model.PasteCommand.Execute(null);
                return true;

            // The arrows step the selection to the nearest item that way, and the sheet follows it.
            case (Key.Left or Key.Right or Key.Up or Key.Down, KeyModifiers.None) when model.ShowsPages:
                model.StepSelection(e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0);
                return true;

            case (Key.PageUp or Key.PageDown, KeyModifiers.None) when model.ShowsPages:
                return ScrollPages(PageStep.Page, e.Key == Key.PageUp ? -1 : 1);

            case (Key.Home or Key.End, KeyModifiers.None) when model.ShowsPages:
                return ScrollPages(PageStep.End, e.Key == Key.Home ? -1 : 1);

            default:
                return false;
        }
    }

    /// <summary>The gold stepper: control a handful (the purses' pacing); shift on - all of it. + has no end for shift.</summary>
    private bool StepGold(Button button, KeyModifiers modifiers)
    {
        if (DataContext is not VP_ViewModel_Vault_L model || button.Tag is not string tag
            || tag.Split(':') is not ["gold", var way])
        {
            return false;
        }

        bool up = way == "up", shift = modifiers.HasFlag(KeyModifiers.Shift), ctrl = modifiers.HasFlag(KeyModifiers.Control);
        if (!ctrl && !(shift && !up))
            return false;

        model.StepGold(up, all: shift);
        return true;
    }

    /// <summary>
    /// The name box takes the focus and its whole text as it appears - once it is laid out, never in the attach
    /// itself. Focused while it was being attached, the box could not be read by UI Automation for as long as
    /// it was shown that first time: every walk of the window failed under it (E_FAIL at the box's presenter,
    /// no exception thrown; measured with tests/ui lookups and PD2SPC_FIRST_CHANCE), which a screen
    /// reader meets too - and which had let two tests pass without seeing anything.
    /// </summary>
    private void Rename_Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            // Once it is laid out, not in the attach itself: a box that comes with a new folder (the path's)
            // is built before its place on the row is shown, where it takes no focus, and the typing went
            // nowhere.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (box.IsEffectivelyVisible && !box.IsFocused)
                {
                    box.Focus();
                    box.SelectAll();
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void Rename_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: FolderRename rename }
            || DataContext is not VP_ViewModel_Vault_L model)
        {
            return;
        }

        if (e.Key is Key.Enter or Key.Return)
            model.CommitRename(rename);
        else if (e.Key == Key.Escape)
            model.CancelRename(rename);
        else
            return;

        e.Handled = true;
    }

    /// <summary>Leaving the box keeps the name, as Explorer does.</summary>
    private void Rename_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: FolderRename rename }
            && DataContext is VP_ViewModel_Vault_L model)
        {
            model.CommitRename(rename);
        }
    }

    /// <summary>
    /// Reads the vault when the page appears.
    /// <para>
    /// Not in the view model's constructor: it is a singleton built during start-up, before a
    /// game folder is open, and the vault's season comes from tables that are loaded lazily.
    /// By the time this page is on screen they are there.
    /// </para>
    /// </summary>
    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is not VP_ViewModel_Vault_L model)
            return;

        // Once. OnLoaded runs again every time the page is shown, and a second subscription
        // would scroll twice and leave a dangling handler behind it.
        if (!_listening)
        {
            model.ScrolledHome += ScrollHome;
            model.PageSought += ScrollToPage;
            model.PlaceSought += ScrollToPlace;
            model.TextCopied = ToClipboard;
            model.MapChanged += ShowMap;
            model.HereShown += ShowHereInTree;

            // What the page's room depends on besides its size: what it was asked to show, and how wide.
            model.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName is nameof(VP_ViewModel_Vault_L.IsMinimap) or nameof(VP_ViewModel_Vault_L.ShowsTree)
                    or nameof(VP_ViewModel_Vault_L.TreeWidth) or nameof(VP_ViewModel_Vault_L.SheetColumns)
                    or nameof(VP_ViewModel_Vault_L.ShowsPages) or nameof(VP_ViewModel_Vault_L.TreeDraggedShut))
                {
                    Fit();
                }

                // The map come or gone: the sheet has other room beside it.
                if (changed.PropertyName is nameof(VP_ViewModel_Vault_L.ShowsMapNow) or nameof(VP_ViewModel_Vault_L.ShowsPages))
                    Avalonia.Threading.Dispatcher.UIThread.Post(SizeSheet, Avalonia.Threading.DispatcherPriority.Loaded);
            };
            Fit();
            Minimap.Sought += to => PagesScroll.Offset = new Vector(PagesScroll.Offset.X, to);
            Minimap.Held += held =>
            {
                ScrubLabel.IsVisible = held;
                ShowMap(redraw: false);
            };
            ShowMap();
            _listening = true;
        }

        // Posted, not called. Reading the vault and building a page of cards is a few
        // hundred milliseconds of synchronous work, and done here it runs *inside* the page
        // transition - so the animation had no frames to draw and the page appeared to jump.
        // At Background priority the transition renders first and the list fills behind it.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            model.Show, Avalonia.Threading.DispatcherPriority.Background);
    }

    private bool _listening;

    /// <summary>The room the folder's sheet has: how many stretches of it are in sight.</summary>
    private void Pages_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is VP_ViewModel_Vault_L model)
            model.SetPagesViewport(e.NewSize.Height);
    }

    private void SheetArea_SizeChanged(object? sender, SizeChangedEventArgs e) => SizeSheet();

    /// <summary>What the sheet's own margins take across (PageRows' 6 each side).</summary>
    private const double AroundSheet = 12;

    /// <summary>What the plain scroll bar takes beside the sheet, where the map does not stand in its place.</summary>
    private const double ScrollBarRoom = 18;

    /// <summary>
    /// The sheet's columns from the room it has: as
    /// many as fit beside the map - which is wider for a wider sheet, so the two are sized together - in fours
    /// (a folder's tile is four wide, and a row of them fills the sheet), never fewer than twelve. The item at the top of what was in sight is in sight after: laid out at another
    /// width, a folder's rows are other rows.
    /// </summary>
    private void SizeSheet()
    {
        if (DataContext is not VP_ViewModel_Vault_L model || SheetArea.Bounds.Width <= 0)
            return;

        double room = SheetArea.Bounds.Width, cell = ItemTileViewModel.CellSize;
        int columns = PD2SPCompanion.Core.Collection.VaultLayout.LeastColumns;
        for (int trying = PD2SPCompanion.Core.Collection.VaultLayout.Columns(room, cell); trying > columns;
             trying = PD2SPCompanion.Core.Collection.VaultLayout.Fit(trying - 1))
        {
            double beside = model.ShowsMapNow
                ? PagesMinimap.WidthFor(trying) + MapFrame.Margin.Left + MapFrame.Margin.Right
                  + MapFrame.BorderThickness.Left + MapFrame.BorderThickness.Right
                : ScrollBarRoom;
            if (trying * cell + AroundSheet + beside <= room)
            {
                columns = trying;
                break;
            }
        }

        if (columns == model.SheetColumns)
            return;

        long? top = model.KeyNearRow((int)(PagesScroll.Offset.Y / cell));
        bool atTop = PagesScroll.Offset.Y < 1;
        model.SetSheetColumns(columns);

        if (!atTop && top is { } key)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (model.RowOf(key) is { } row)
                    PagesScroll.Offset = new Vector(PagesScroll.Offset.X, row * cell);
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// The scroll over the folder's sheet: the wheel moves by a row of cells;
    /// with Ctrl, or PageUp and PageDown, by a stretch of fifteen rows; Home and End go to the top and to the
    /// end. With Shift the wheel goes sideways, as everywhere (it was top and end while a folder was pages,
    /// which had no sideways).
    /// </summary>
    private enum PageStep { Cell, Page, End }

    private bool ScrollPages(PageStep by, double direction)
    {
        if (!PagesScroll.IsEffectivelyVisible)
            return false;

        double row = VP_ViewModel_Vault_L.PageRowHeight;
        double most = Math.Max(0, PagesScroll.Extent.Height - PagesScroll.Viewport.Height);
        double at = PagesScroll.Offset.Y;

        double to = by switch
        {
            PageStep.End => direction < 0 ? 0 : most,
            // To the row of pages before or after the one at the top, whole: from between two, up is the one in sight.
            PageStep.Page => direction < 0 ? (Math.Ceiling(at / row - 0.001) - 1) * row : (Math.Floor(at / row + 0.001) + 1) * row,
            // A row of cells a notch: a wheel turned three notches at once says so in one event.
            _ => at + direction * ItemTileViewModel.CellSize,
        };

        PagesScroll.Offset = new Vector(PagesScroll.Offset.X, Math.Clamp(to, 0, most));
        return true;
    }

    /// <summary>The sheet moved sideways by so much, within what there is of it. False where it does not go sideways.</summary>
    private bool ScrollAcross(double by)
    {
        double most = Math.Max(0, PagesScroll.Extent.Width - PagesScroll.Viewport.Width);
        if (!PagesScroll.IsEffectivelyVisible || most <= 0)
            return false;

        PagesScroll.Offset = new Vector(Math.Clamp(PagesScroll.Offset.X + by, 0, most), PagesScroll.Offset.Y);
        return true;
    }

    /// <summary>
    /// An item's place brought into sight, where it is not wholly there: its stretch at the top of the sheet's
    /// room, and its column within it across.
    /// </summary>
    private void ScrollToPage(int page, int column)
    {
        double row = VP_ViewModel_Vault_L.PageRowHeight;
        double top = page * row;
        double at = PagesScroll.Offset.Y, most = Math.Max(0, PagesScroll.Extent.Height - PagesScroll.Viewport.Height);

        double down = top < at || top + row > at + PagesScroll.Viewport.Height ? Math.Clamp(top, 0, most) : at;

        // Across: two cells of room either side of where the item begins, where the sheet goes sideways at all.
        double left = column * ItemTileViewModel.CellSize, across = PagesScroll.Offset.X;
        double mostAcross = Math.Max(0, PagesScroll.Extent.Width - PagesScroll.Viewport.Width);
        if (left < across)
            across = left - 2 * ItemTileViewModel.CellSize;
        else if (left + 4 * ItemTileViewModel.CellSize > across + PagesScroll.Viewport.Width)
            across = left + 4 * ItemTileViewModel.CellSize - PagesScroll.Viewport.Width;

        PagesScroll.Offset = new Vector(Math.Clamp(across, 0, mostAcross), down);
    }

    /// <summary>
    /// An item stepped to by an arrow key brought into sight: the sheet moves no further than shows it whole, a
    /// cell of room about it - so a walk along a row does not move the sheet at all, and one down it moves a row
    /// at a time.
    /// </summary>
    private void ScrollToPlace(int row, int column, int wide, int high)
    {
        double cell = ItemTileViewModel.CellSize;
        double top = PageRows.Margin.Top + row * cell, left = PageRows.Margin.Left + column * cell;
        double bottom = top + high * cell, right = left + wide * cell;

        double down = PagesScroll.Offset.Y, across = PagesScroll.Offset.X;
        if (top - cell < down)
            down = top - cell;
        else if (bottom + cell > down + PagesScroll.Viewport.Height)
            down = bottom + cell - PagesScroll.Viewport.Height;

        if (left - cell < across)
            across = left - cell;
        else if (right + cell > across + PagesScroll.Viewport.Width)
            across = right + cell - PagesScroll.Viewport.Width;

        double mostDown = Math.Max(0, PagesScroll.Extent.Height - PagesScroll.Viewport.Height);
        double mostAcross = Math.Max(0, PagesScroll.Extent.Width - PagesScroll.Viewport.Width);
        PagesScroll.Offset = new Vector(Math.Clamp(across, 0, mostAcross), Math.Clamp(down, 0, mostDown));
    }

    // ---- the pan: the middle button held ----------------------------------------------------------------
    // The sheet dragged about under the pointer, both ways: the way across a sheet wider than its room without the bar at
    // its foot.

    /// <summary>Where the pan took hold: the pointer then, and how far the sheet was scrolled.</summary>
    private (Point At, Vector Offset)? _pan;

    private void Pan_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(PagesScroll).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed
            || DataContext is VP_ViewModel_Vault_L { IsCarrying: true })
        {
            return;
        }

        _pan = (e.GetPosition(PagesScroll), PagesScroll.Offset);
        e.Pointer.Capture(PagesScroll);
        PagesScroll.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void Pan_Moved(object? sender, PointerEventArgs e)
    {
        if (_pan is not { } pan)
            return;

        if (!e.GetCurrentPoint(PagesScroll).Properties.IsMiddleButtonPressed)
        {
            EndPan(e.Pointer);
            return;
        }

        var moved = e.GetPosition(PagesScroll) - pan.At;
        double mostAcross = Math.Max(0, PagesScroll.Extent.Width - PagesScroll.Viewport.Width);
        double mostDown = Math.Max(0, PagesScroll.Extent.Height - PagesScroll.Viewport.Height);
        PagesScroll.Offset = new Vector(Math.Clamp(pan.Offset.X - moved.X, 0, mostAcross), Math.Clamp(pan.Offset.Y - moved.Y, 0, mostDown));
        e.Handled = true;
    }

    private void Pan_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_pan is not null && e.InitialPressMouseButton == MouseButton.Middle)
        {
            EndPan(e.Pointer);
            e.Handled = true;
        }
    }

    private void EndPan(IPointer pointer)
    {
        _pan = null;
        pointer.Capture(null);
        PagesScroll.Cursor = null;
    }

    // ---- the folder's map ----------------------------------------------------------------------------
    // It says where a search's finds are, out of sight too - arrows to them were tried beside it and taken out
    // the same day (too redundant).

    private void Pages_Scrolled(object? sender, ScrollChangedEventArgs e)
    {
        ShowMap(redraw: false);

        if (e.OffsetDelta.Y != 0)
            QuietTips();
    }

    /// <summary>
    /// The items' tooltips held back while the pages move, and for a moment after: under a pointer at rest
    /// each notch of the wheel is another item, and a tooltip opened and closed for each is what the scroll
    /// stuttered on (ContainerGrid.TipsOff).
    /// </summary>
    private void QuietTips()
    {
        PageRows.TipsOff = true;

        _tipsBack ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250), Avalonia.Threading.DispatcherPriority.Background,
            (_, _) =>
            {
                _tipsBack?.Stop();
                PageRows.TipsOff = false;
            });

        _tipsBack.Stop();
        _tipsBack.Start();
    }

    private Avalonia.Threading.DispatcherTimer? _tipsBack;

    /// <summary>The map as the folder, the search and the scroll now stand.</summary>
    private void ShowMap() => ShowMap(redraw: true);

    private void ShowMap(bool redraw)
    {
        if (DataContext is not VP_ViewModel_Vault_L model)
            return;

        if (redraw && model.ShowsMapNow)
            Minimap.Show(model.MapMarks, model.SheetColumns, model.PagesShown, model.HitKeys);

        Minimap.Scrolled(PagesScroll.Offset.Y, PagesScroll.Viewport.Height, PagesScroll.Extent.Height);

        // Beside the map's window while it is held: which rows are in sight.
        if (ScrubLabel.IsVisible)
        {
            ScrubText.Text = model.PagesInSight(PagesScroll.Offset.Y, PagesScroll.Viewport.Height);
            double top = MapFrame.Bounds.Top + MapFrame.BorderThickness.Top + Minimap.WindowMiddle - 14;
            ScrubLabel.Margin = new Thickness(0, Math.Clamp(top, 4, Math.Max(4, PagesScroll.Bounds.Height - 32)), 8, 0);
        }
    }

    private void Pages_Wheel(object? sender, PointerWheelEventArgs e)
    {
        // Shift turns the wheel sideways: some mice say so themselves, in the event's X.
        double turned = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (turned == 0)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.Y == 0)
        {
            // A sheet that does not go sideways lets the wheel be: nothing to do, and nothing taken.
            if (ScrollAcross(-turned * ItemTileViewModel.CellSize))
                e.Handled = true;
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                e.Handled = true;

            return;
        }

        var by = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? PageStep.Page : PageStep.Cell;
        if (ScrollPages(by, by == PageStep.Cell ? -turned : turned > 0 ? -1 : 1))
            e.Handled = true;
    }

    // ---- the path, the tree and the folders' tiles -----

    /// <summary>A step of the path: gone to.</summary>
    private void Crumb_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FolderCrumb crumb } && DataContext is VP_ViewModel_Vault_L model)
            model.GoToCrumb(crumb);
    }

    /// <summary>The chevron after a step: the folders that step holds, listed under it.</summary>
    private void CrumbList_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderCrumb crumb } chevron || DataContext is not VP_ViewModel_Vault_L model)
            return;

        FolderMenu.PlacementTarget = chevron;
        model.ListUnder(crumb);
    }

    /// <summary>The chevron at the path's end: every folder, listed under the bar.</summary>
    private void PathEnd_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Vault_L model)
            return;

        FolderMenu.PlacementTarget = PathBar;
        model.ListEverything();
    }

    /// <summary>A folder's row or tile: gone to.</summary>
    private void Place_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderPlace place } row || DataContext is not VP_ViewModel_Vault_L model)
            return;

        // A double click on a row of the tree folds its folder, or unfolds it, as Explorer's does: the second click of
        // two on the one row, the first having gone to it. Told by the clicks
        // themselves - a Button's own DoubleTapped never reaches a handler (PD2UI001).
        long now = Environment.TickCount64;
        bool second = ReferenceEquals(_clicked, place) && now - _clickedAt <= DoubleClick;
        (_clicked, _clickedAt) = (second ? null : place, now);

        if (second && place.HoldsFolders && row.Classes.Contains("treerow"))
            model.FoldPlaceCommand.Execute(place);
        else
            model.GoToPlace(place);
    }

    /// <summary>
    /// A folder's tile on the sheet: a click selects it, as a click on an item does, and a second within
    /// <see cref="DoubleClick"/> goes into it - Explorer's. Told by the clicks themselves,
    /// as a row of the tree's are.
    /// </summary>
    private void FolderTile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderTile tile } || DataContext is not VP_ViewModel_Vault_L model)
            return;

        long now = Environment.TickCount64;
        bool second = ReferenceEquals(_tileClicked, tile.Place) && now - _tileClickedAt <= DoubleClick;
        (_tileClicked, _tileClickedAt) = (second ? null : tile.Place, now);

        if (second)
            model.OpenFolderTile(tile);
        else
            model.ClickFolderTileCommand.Execute(tile);
    }

    /// <summary>The folder whose tile was clicked last, and when. By its folder: a reload between two clicks makes the tile anew.</summary>
    private FolderPlace? _tileClicked;
    private long _tileClickedAt;

    /// <summary>The row of the tree clicked last, and when: a click on it again within <see cref="DoubleClick"/> is a double click.</summary>
    private FolderPlace? _clicked;
    private long _clickedAt;

    /// <summary>Windows' own time for a double click, at its default: half a second.</summary>
    private const int DoubleClick = 500;

    /// <summary>
    /// The arrows on a row of the tree that has the keyboard (they only move the selected
    /// state; Enter is the row's own, as a button's). Up and Down walk the
    /// rows in sight, quick access's and then the tree's, and go nowhere: the row reached is lit, the folder shown
    /// the folder shown. Right unfolds the row's folder - or, unfolded already, steps to the first folder in it;
    /// Left folds it - or, folded or holding none, steps to the folder it is in. As Explorer's tree. On a row of
    /// quick access, which is no tree, Left and Right do nothing. Asked before the sheet's own arrows, which step
    /// the selection among the items wherever the keyboard is.
    /// </summary>
    private bool TreeKey(KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)
            || !IsEffectivelyVisible || DataContext is not VP_ViewModel_Vault_L model
            || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Button { DataContext: FolderPlace place } row
            || !row.Classes.Contains("treerow") || !this.IsVisualAncestorOf(row))
        {
            return false;
        }

        if (e.Key is Key.Up or Key.Down)
        {
            var rows = new Control[] { QuickRows, TreeRows }
                .SelectMany(list => list.GetVisualDescendants().OfType<Button>())
                .Where(each => each.Classes.Contains("treerow") && each.IsEffectivelyVisible).ToList();
            int at = rows.IndexOf(row);
            int to = Math.Clamp(at + (e.Key == Key.Down ? 1 : -1), 0, rows.Count - 1);
            if (at >= 0 && to != at)
            {
                rows[to].Focus(NavigationMethod.Directional);
                rows[to].BringIntoView();
            }

            return true;
        }

        if (!TreeRows.IsVisualAncestorOf(row))
            return true;

        void Reach(FolderPlace? other)
        {
            if (other is not null && TreeRows.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(each => ReferenceEquals(each.DataContext, other) && each.Classes.Contains("treerow")) is { } there)
            {
                there.Focus(NavigationMethod.Directional);
                there.BringIntoView();
            }
        }

        if (e.Key == Key.Right)
        {
            if (place.HoldsFolders && !place.IsOpen)
                model.FoldPlaceCommand.Execute(place);
            else if (place.HoldsFolders)
                Reach(model.Places.FirstOrDefault(each => each.Id is not null && each.Parent == place.Id));
        }
        else if (place.HoldsFolders && place.IsOpen)
        {
            model.FoldPlaceCommand.Execute(place);
        }
        else if (place.Id is not null)
        {
            Reach(model.Places.FirstOrDefault(each => each.Id == place.Parent));
        }

        return true;
    }

    /// <summary>A row's chevron: its folders put away or brought back, and the row itself not gone to.</summary>
    private void Fold_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FolderPlace place } && DataContext is VP_ViewModel_Vault_L model)
            model.FoldPlaceCommand.Execute(place);

        e.Handled = true;
    }

    /// <summary>
    /// A menu asked for (the page's MenuFor): a folder's, by a right click on its row of the
    /// tree or of quick access, its tile on the sheet or its step of the path - where the pointer is - or by the
    /// Menu key or Shift + F10 on a row that has the keyboard, beside the row; an item's, by a right click on it,
    /// or by the keys for what is selected, where it is drawn. Avalonia tells pointer and keys apart: only a
    /// pointer's asking has a place.
    /// </summary>
    private void Menu_Context(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Vault_L { IsCarrying: false } model)
            return;

        var menus = PD2SPCompanion.Shell.Services.Generic.ActMenuService.Shared;
        bool byPointer = e.TryGetPosition(this, out _);

        // The card the right button was pressed on.
        if (byPointer && _context is { } pressed)
        {
            _context = null;
            if (model.CardOf(pressed) is { } card)
            {
                menus.Show(model.MenuFor(card));
                e.Handled = true;
            }

            return;
        }

        for (var at = e.Source as Visual; at is not null && !ReferenceEquals(at, this); at = at.GetVisualParent())
        {
            long? folder;
            bool ofTile = false;
            if (at is Button { DataContext: FolderPlace place } row && row.Classes.Contains("folderrow"))
                folder = place.Id;
            else if (at is Button { DataContext: FolderTile tile } onSheet && onSheet.Classes.Contains("sheetfolder"))
            {
                // A tile of the sheet is selected by the right press, as an item's is - or stays selected with the
                // others: its menu is then the selection's (MenuFor).
                model.SelectForMenu(tile);
                folder = tile.Place.Id;
                ofTile = true;
            }
            else if (at is Button { DataContext: FolderCrumb crumb } step && step.Classes.Contains("step") && !step.Classes.Contains("list"))
                folder = crumb.Id;
            else
                continue;

            // A row of a list of folders is in a popup of its own, which a second menu over it would not outlive.
            if (!this.IsVisualAncestorOf(at))
                return;

            menus.Show(model.MenuFor(folder, ofTile), byPointer ? null : at);
            e.Handled = true;
            return;
        }

        // The sheet's empty room, under the pointer: no card was pressed, and no folder is there.
        if (byPointer && PagesScroll.IsEffectivelyVisible && e.TryGetPosition(PagesScroll, out var on)
            && new Rect(PagesScroll.Bounds.Size).Contains(on)
            && e.Source is Visual struck && (ReferenceEquals(struck, PagesScroll) || PagesScroll.IsVisualAncestorOf(struck)))
        {
            menus.Show(model.MenuForSpace());
            e.Handled = true;
            return;
        }

        // By the keys, no row of the tree having the keyboard: what is selected, where it is drawn.
        if (!byPointer && IsEffectivelyVisible && !ItemPanels.IsBoxOrPicker(e.Source as Visual)
            && ItemPanels.OwnsKeys(this) && model.MenuCard() is { } chosen
            && TileLayer.Find(this, chosen) is { } drawn)
        {
            menus.Show(model.MenuFor(chosen), drawn.Layer, drawn.Place);
            e.Handled = true;
        }
    }

    /// <summary>
    /// The folder under the pointer, where it is over a row of the tree or a folder's tile on the sheet - what a
    /// drag is let go of on. By where the rows lie, not by what the pointer is over: whatever is carried is under it.
    /// </summary>
    private FolderPlace? FolderAt(PointerEventArgs e)
    {
        if (OverSheet(e))
        {
            foreach (var tile in FolderTiles.GetVisualDescendants().OfType<Button>())
            {
                if (tile is { DataContext: FolderTile folder, IsEffectivelyVisible: true } && tile.Classes.Contains("sheetfolder")
                    && new Rect(tile.Bounds.Size).Contains(e.GetPosition(tile)))
                {
                    return folder.Place;
                }
            }
        }

        foreach (var (list, within) in new (Control, Control)[] { (QuickRows, QuickScroll), (TreeRows, TreeScroll) })
        {
            if (!within.IsEffectivelyVisible || !new Rect(within.Bounds.Size).Contains(e.GetPosition(within)))
                continue;

            foreach (var row in list.GetVisualDescendants().OfType<Button>())
            {
                if (row is { DataContext: FolderPlace place, IsEffectivelyVisible: true } && row.Classes.Contains("pickone")
                    && row.Classes.Contains("folderrow")
                    && new Rect(row.Bounds.Size).Contains(e.GetPosition(row)))
                {
                    return place;
                }
            }
        }

        return null;
    }

    /// <summary>The folder a carry is held over, and since when: held there a while, a folded row of the tree opens.</summary>
    private FolderPlace? _over;

    private Avalonia.Threading.DispatcherTimer? _unfold;

    private void CarryOverFolder(VP_ViewModel_Vault_L model, FolderPlace? place)
    {
        model.HoverFolder(place);

        if (ReferenceEquals(place, _over))
            return;

        _over = place;
        _unfold?.Stop();

        if (place is not { HoldsFolders: true, IsOpen: false })
            return;

        _unfold ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _unfold.Tick -= Unfold_Tick;
        _unfold.Tick += Unfold_Tick;
        _unfold.Start();
    }

    private void Unfold_Tick(object? sender, EventArgs e)
    {
        _unfold?.Stop();

        if (_over is { } place && DataContext is VP_ViewModel_Vault_L model && (model.IsCarriedOver || model.Session.IsHolding))
            model.Unfold(place);
    }

    /// <summary>
    /// The row of the folder shown brought into sight in the tree: down to it, and across to its name where the
    /// name is not wholly there - a deep folder is read by its name, not by the steps in before it. Once the rows are
    /// laid out: called as the folder changes, before they are.
    /// </summary>
    private void ShowHereInTree() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!TreeScroll.IsEffectivelyVisible)
                return;

            // The tree's own row - at its last level, the row of the folder the one shown lies under
            // (FolderPlace.IsTreeHere). Down to it; the tree has no way sideways.
            var row = TreeRows.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(each => each is { DataContext: FolderPlace { IsTreeHere: true }, IsEffectivelyVisible: true } && each.Classes.Contains("folderrow"));
            if (row is null || row.TranslatePoint(default, TreeRows) is not { } top)
                return;

            double down = TreeScroll.Offset.Y;
            double mostDown = Math.Max(0, TreeScroll.Extent.Height - TreeScroll.Viewport.Height);
            double above = top.Y + TreeRows.Margin.Top, below = above + row.Bounds.Height;
            if (above < down)
                down = above;
            else if (below > down + TreeScroll.Viewport.Height)
                down = below - TreeScroll.Viewport.Height;

            TreeScroll.Offset = new Vector(0, Math.Clamp(down, 0, mostDown));
        }, Avalonia.Threading.DispatcherPriority.Loaded);

    // ---- the page's room ----

    /// <summary>The page's own margin at each side (the root grid's).</summary>
    private const double PageMargin = 16;

    /// <summary>The columns of the sheet a page keeps in sight before anything else: the fewest a sheet has.</summary>
    private const int ColumnsKept = PD2SPCompanion.Core.Collection.VaultLayout.LeastColumns;

    /// <summary>
    /// What the page has room for. Everything it was asked to show, where twelve columns of the sheet still fit
    /// beside the tree and the map; where they do not, the map gives way first - the plain scroll bar takes its
    /// place; then the tree is squeezed to what is left, and put away where even
    /// its least would not fit. The kept toggles are not touched, so it all comes back with the room.
    /// </summary>
    private void Fit()
    {
        if (DataContext is not VP_ViewModel_Vault_L model || Bounds.Width <= 0)
            return;

        // The sheet's frame, its margin in its section and the section's own frame, about its cells.
        const double About = 24;

        double room = Bounds.Width - 2 * PageMargin;
        double sheet = Math.Min(ColumnsKept, model.SheetColumns) * ItemTileViewModel.CellSize + About;
        double tree = !model.ShowsTree || !model.ShowsPages ? 0
            : model.TreeDraggedShut ? PD2SPCompanion.Shell.Services.Settings.VaultLook.EdgeWidth
            : Math.Min(model.TreeWidth, TreeRoom);
        double map = model.IsMinimap
            ? PagesMinimap.WidthFor(model.SheetColumns) + MapFrame.Margin.Left + MapFrame.Margin.Right
              + MapFrame.BorderThickness.Left + MapFrame.BorderThickness.Right
            : 0;

        // Where the map is not, the plain bar is: a sheet of twelve columns is to fit beside it, with no way sideways
        // (uncounted, it cost the tenth column its last pixels and brought a bar across).
        bool narrow = room - tree - (model.IsMinimap ? map : ScrollBarRoom) < sheet;
        double left = room - sheet - ScrollBarRoom;

        TreePane.MaxWidth = narrow ? Math.Max(0, Math.Min(TreeRoom, left)) : TreeRoom;
        model.RoomHidesMap = narrow && model.IsMinimap;
        model.RoomHidesTree = narrow && tree > 0 && left < PD2SPCompanion.Shell.Services.Settings.VaultLook.NarrowestTree;
    }

    // ---- the tools a narrow line left off (OverflowRow) ---------------------------------------------------------

    /// <summary>The "more" button: what the line had no room for, listed under it as the line has them now.</summary>
    private void More_Click(object? sender, RoutedEventArgs e)
    {
        // The button pressed while its list is open: the press has shut the list already, as a press anywhere
        // outside it does - and the button is not to open it again for that.
        if (Environment.TickCount64 - _moreShutAt < 250)
            return;

        MoreRows.ItemsSource = Tools.Overflowed.Select(tool => tool is null ? new ToolEntry(null, "", false, false, null) : EntryOf(tool)).ToList();
        MoreMenu.IsOpen = true;
    }

    /// <summary>When the list of tools was last shut.</summary>
    private long _moreShutAt;

    /// <summary>A tool as a row of the list: its glyph, the first line of its tooltip, whether it is on, whether it is dead.</summary>
    private static ToolEntry EntryOf(Control tool)
    {
        object? tip = tool is BinButton bin ? bin.Tip : ToolTip.GetTip(tool);
        string text = (tip as string ?? "").Split('\r', '\n')[0].Trim();
        object? drawn = (tool as ContentControl)?.Content;
        var glyph = (drawn as Avalonia.Controls.Shapes.Path
                     ?? (drawn as Panel)?.Children.OfType<Avalonia.Controls.Shapes.Path>().FirstOrDefault(path => path.IsVisible))?.Data;
        return new ToolEntry(glyph, text, tool.IsEnabled, tool.Classes.Contains("on"), tool);
    }

    /// <summary>A row of the list: the tool's own command, as a press on the tool.</summary>
    private void MoreRow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ToolEntry { Tool: Button tool } })
            return;

        MoreMenu.IsOpen = false;

        // Once the list has shut: shutting gives the focus back to where it was, and a name box
        // opened before that lost it at once - and a box that loses the focus keeps what it holds and
        // closes (measured: Rename folder from the list opened no box that could be seen).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (tool.Command is { } command && command.CanExecute(tool.CommandParameter))
                command.Execute(tool.CommandParameter);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }


    // ---- the tree's edge, dragged ---------------------------------------------------------------------------

    /// <summary>
    /// The most the tree may take of the page: half of it, its edge with it - and never nothing. Not a third:
    /// deep folders' names want more.
    /// </summary>
    private double TreeRoom => Math.Max(60, Bounds.Width / 2);

    /// <summary>Where the edge was taken hold of, and how wide the tree was then.</summary>
    private (double At, double Width)? _edgeHeld;

    private void TreeEdge_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Vault_L model || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _edgeHeld = (e.GetPosition(this).X, model.TreeWidth);
        TreeEdge.Classes.Add("held");
        e.Pointer.Capture(TreeEdge);
        e.Handled = true;
    }

    private void TreeEdge_Moved(object? sender, PointerEventArgs e)
    {
        if (_edgeHeld is not { } held || DataContext is not VP_ViewModel_Vault_L model)
            return;

        // Past half its least width the tree shuts under the pointer, the edge still held: back if the pointer
        // comes back, put away if it is let go of there.
        double wanted = held.Width + e.GetPosition(this).X - held.At;
        model.TreeDraggedShut = wanted < PD2SPCompanion.Shell.Services.Settings.VaultLook.NarrowestTree / 2;

        // Never so wide that the sheet has no room: half of the page is the tree's at the most.
        if (!model.TreeDraggedShut)
            model.TreeWidth = Math.Min(TreeRoom, wanted);

        e.Handled = true;
    }

    private void TreeEdge_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_edgeHeld is not { } held)
            return;

        _edgeHeld = null;
        TreeEdge.Classes.Remove("held");
        e.Pointer.Capture(null);

        if (DataContext is VP_ViewModel_Vault_L model)
        {
            // Let go of while shut: the tree is put away, as its button puts it away - and comes back by the
            // button as wide as it was before this drag, never at nothing.
            if (model.TreeDraggedShut)
            {
                model.TreeWidth = held.Width;
                model.TreeDraggedShut = false;
                model.ShowsTree = false;
            }

            model.KeepTreeWidth();
        }

        e.Handled = true;
    }

    /// <summary>Another folder's sheet, or a folder sorted, begins at its top left.</summary>
    private void ScrollHome() => PagesScroll.Offset = new Avalonia.Vector(0, 0);
}
