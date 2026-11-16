using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Inv_L : UserControl, PD2SPCompanion.Shell.CustomControls.Base.IShownPage
{
    /// <summary>
    /// Store's modified clicks: Ctrl stores sorted, Shift as a crafting base. Any other mix is
    /// left to the plain click.
    /// </summary>
    private static bool StoreSorted(Button button, KeyModifiers modifiers)
    {
        if (button.Tag is not "store" || button.DataContext is not VP_ViewModel_Inv_L page)
            return false;

        if (modifiers == KeyModifiers.Control)
            page.Store(VP_ViewModel_Inv_L.StoreAs.Sorted);
        else if (modifiers == KeyModifiers.Shift)
            page.Store(VP_ViewModel_Inv_L.StoreAs.CraftingBase);
        else
            return false;

        return true;
    }

    /// <summary>The mercenary's level steppers: Ctrl 5, Shift 20, both to the end - the character's level's keys.</summary>
    private bool StepMercLevel(Button button, KeyModifiers modifiers)
    {
        if (button.Tag is not ("merc:up" or "merc:down") || DataContext is not VP_ViewModel_Inv_L page)
            return false;

        bool shift = modifiers.HasFlag(KeyModifiers.Shift), ctrl = modifiers.HasFlag(KeyModifiers.Control);
        int levels = shift && ctrl ? 1000 : shift ? 20 : ctrl ? 5 : 0;
        if (levels == 0)
            return false;

        page.StepMercLevel(button.Tag as string == "merc:up" ? levels : -levels);
        return true;
    }

    /// <summary>
    /// Store all's modified clicks, as Store's: Ctrl sorted, Shift crafting bases only. Any other mix is left
    /// to the plain click, which stores everything as it is.
    /// </summary>
    private bool StoreAllTiers(Button button, KeyModifiers modifiers)
    {
        // The press lands on the ask-twice control's own button; the tag is the control's.
        if (button.FindAncestorOfType<PD2SPCompanion.Shell.CustomControls.Base.BinButton>() is not { Tag: "storeall" }
            || DataContext is not VP_ViewModel_Inv_L page)
        {
            return false;
        }

        if (modifiers == KeyModifiers.Control)
            page.StoreAllPress(VP_ViewModel_Inv_L.StoreAs.Sorted);
        else if (modifiers == KeyModifiers.Shift)
            page.StoreAllPress(VP_ViewModel_Inv_L.StoreAs.CraftingBase);
        else
            return false;

        return true;
    }

    /// <summary>Gold by the handful with control, or the whole purse with shift.</summary>
    private static bool SpendGold(Button button, KeyModifiers modifiers)
    {
        bool wholePurse = modifiers.HasFlag(KeyModifiers.Shift);
        bool leap = modifiers.HasFlag(KeyModifiers.Control);

        if (!wholePurse && !leap)
            return false;

        // The arrows between the carried gold and the shared stash's: shift moves all of it, as the vault's.
        if (button.DataContext is GoldTransferViewModel between && button.Tag is "down" or "up")
        {
            if (!wholePurse)
                return false;

            if (button.Tag is "down")
                between.MoveAllDown();
            else
                between.MoveAllUp();

            return true;
        }

        if (button.DataContext is not GoldViewModel purse || button.Tag is not string edge)
            return false;

        // The vault's arrows: shift moves all of it - the purse in, or the purse filled. Control
        // means nothing there: the amount is typed in the box between them, so the plain click
        // (the button's command) moves that.
        if (edge is "deposit" or "withdraw")
        {
            if (!wholePurse)
                return false;

            if (edge == "deposit")
                purse.DepositAll();
            else
                purse.WithdrawFill();

            return true;
        }

        if (edge != "fill" && edge != "empty")
            return false;

        bool up = edge == "fill";

        // Shift beats control, so holding both fills or empties.
        if (wholePurse)
        {
            if (up)
                purse.Fill();
            else
                purse.Empty();
        }
        else
        {
            purse.LeapBy(up);
        }

        return true;
    }

    /// <summary>
    /// What a worn slot's markup names as its tooltip: nothing to show, only that there is one - which keeps the
    /// tooltip service listening. The tooltip itself is made as it opens (the static constructor).
    /// <para>
    /// <b>Not the slot's item</b>: a binding goes on telling after a
    /// tooltip made in code has taken its place, so an item changed under an open tooltip - a rune dropped into
    /// the helmet the pointer was on - was handed to the tooltip as it was, and the tooltip showed the view
    /// model's type name (seen as a test dragged a rune onto a Veil of Steel). This never changes.
    /// </para>
    /// </summary>
    public static object WornTip { get; } = new Unsaid();

    /// <summary>Says nothing, should it ever be shown.</summary>
    private sealed class Unsaid
    {
        public override string ToString() => "";
    }

    /// <summary>
    /// A worn slot's tooltip made as it opens, afresh each time - as a grid's tiles have theirs
    /// (ContainerGrid, where the why of "afresh" is said). The control that shows it - the item's lines, the
    /// strip of keys, the comparison - was built for every slot of the character, the mercenary and the corpse
    /// at every character opened: 15 ms of an open, 50 on one whose pages had all been visited (a
    /// trace). Its item is the slot's, followed while it is up: what the slot holds is what it shows.
    /// </summary>
    static VP_View_Inv_L()
    {
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Button>((slot, opening) =>
        {
            // The slot's own tile only - the one that names this tip, or holds the card made of it at an earlier
            // opening: the weapon slot's I and II tabs share its data, and showed the item worn there in place of
            // their own words.
            if (slot.DataContext is not EquipSlotViewModel worn
                || ToolTip.GetTip(slot) is var named && !ReferenceEquals(named, WornTip) && named is not PD2SPCompanion.Shell.CustomControls.Base.ItemActionTip
                || slot.FindAncestorOfType<VP_View_Inv_L>() is null)
                return;

            if (worn.Item is not { } tile)
            {
                opening.Cancel = true;
                return;
            }

            var tip = new PD2SPCompanion.Shell.CustomControls.Base.ItemActionTip
            {
                Tile = tile, Origin = PD2SPCompanion.Shell.Services.Generic.TipActions.Worn,
            };
            tip.Bind(PD2SPCompanion.Shell.CustomControls.Base.ItemActionTip.TileProperty,
                new Avalonia.Data.Binding(nameof(EquipSlotViewModel.Item)) { Source = worn });
            ToolTip.SetTip(slot, tip);
        });
    }

    public VP_View_Inv_L()
    {
        InitializeComponent();

        this.OnModifiedPress(SpendGold);
        this.OnModifiedPress(StoreSorted);
        this.OnModifiedPress(StoreAllTiers);
        this.OnModifiedPress(StepMercLevel);

        // Tunnelling, so the right-click is seen before any button underneath can mark it
        // handled - the same reason the character page catches its modifier clicks here.
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, Item_Context);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);

        // Explorer's selection by mouse, a section at a time: a rectangle starts anywhere in one
        // but its header, and never reaches past it. The containers'
        // section is made by a template, and takes its own as it arrives - see Section_Attached.
        DragPick.AttachSection(EquippedSection, () => DataContext as IDragPicker);
        DragPick.AttachSection(MercSection, () => DataContext as IDragPicker);

        // A drag that carries something ends where the button comes up (DragPick began it).
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Carrying something out of the page leaves it nowhere to be put down - unless the vault
        // list is on the other page, where a drop stores it.
        // Putting it back costs nothing - the item never moved - so it is the right thing to do
        // rather than leaving a hold nobody can finish.
        AddHandler(PointerExitedEvent, OnLeft, RoutingStrategies.Tunnel);

        // A find gone to is brought into sight: once what the going changed is laid out - its container may
        // only now be the pane's, its section only now unfolded.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not VP_ViewModel_Inv_L page || ReferenceEquals(page, _findsOf))
                return;

            _findsOf = page;
            page.TextCopied = ToClipboard;
            page.WentToFound += tile => Avalonia.Threading.Dispatcher.UIThread.Post(() => BringIntoSight(tile, 3), Avalonia.Threading.DispatcherPriority.Loaded);
        };
    }

    private VP_ViewModel_Inv_L? _findsOf;

    /// <summary>
    /// An item's tile scrolled into the page's sight: in a grid, worn, or the mercenary's. Asked again a few
    /// times where the tile is not drawn yet.
    /// </summary>
    private void BringIntoSight(ItemTileViewModel tile, int tries)
    {
        // In a grid, where the layer that draws it says where (TileLayer); worn, its slot's button.
        var drawn = this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
            button.Classes.Contains("tile") && button.IsEffectivelyVisible
            && ReferenceEquals((button.DataContext as EquipSlotViewModel)?.Item, tile));

        if (TileLayer.Find(this, tile) is { } lying)
            lying.Layer.BringIntoView(lying.Place);
        else if (drawn is not null)
            drawn.BringIntoView();
        else if (tries > 0)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => BringIntoSight(tile, tries - 1), Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnLeft(object? sender, PointerEventArgs e)
    {
        if (!ItemPanels.OtherShown(this))
        {
            PutItBack();
            return;
        }

        // On its way to the other page: nothing here is under it any more.
        if (DataContext is VP_ViewModel_Inv_L page)
        {
            page.HoverSlot(null);
            page.HoverOver(null);
        }
    }

    /// <summary>
    /// A drag's release that nothing took - over the spine, the sidebar, a page that takes no
    /// items: what was carried goes back. Last of all, on the window, so a page that did take it
    /// (the vault list, storing it) has already ended the hold.
    /// </summary>
    private void OnWindowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is VP_ViewModel_Inv_L { IsDragCarrying: true } page && page.Session.IsHolding
            && e.InitialPressMouseButton == MouseButton.Left)
        {
            page.CancelHold();
        }

        // A drag out of the materials tab that nothing took: nothing moved.
        if (DataContext is VP_ViewModel_Inv_L { IsTabCarrying: true } carrying)
            carrying.EndTabCarry();
    }

    /// <summary>The sections that have their rectangle, so one arriving again is not given a second.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, DragPick> _sectionPicks = new();

    /// <summary>The list of a skill button on the Advanced Stats page that is open, if one is.</summary>
    private Flyout? _skillList;

    private void SkillList_Opened(object? sender, System.EventArgs e) => _skillList = sender as Flyout;

    /// <summary>A skill button's list about to open: made now, from the sheet it shows (CharacterSheetViewModel.PrepareChoices).</summary>
    private void SkillList_Opening(object? sender, System.EventArgs e)
    {
        if (sender is Flyout { Target: Button { DataContext: CharacterSheetViewModel sheet, Tag: string hand } })
            sheet.PrepareChoices(right: hand == "right");
    }

    /// <summary>
    /// A skill picked from a skill button's list: the list closes. The pick makes the sheet - and the button the list
    /// hangs from - again, and a list left open took the next click only to close. After the click, not in it: closed
    /// in it, the row left the tree before its button ran its command, and the pick was lost.
    /// </summary>
    private void SkillList_Picked(object? sender, RoutedEventArgs e)
    {
        var open = _skillList;
        _skillList = null;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => open?.Hide());
    }

    /// <summary>A containers section, made by the template, has arrived: its rectangle, once.</summary>
    private void Section_Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control section && !_sectionPicks.TryGetValue(section, out _))
            _sectionPicks.Add(section, DragPick.AttachSection(section, () => DataContext as IDragPicker));
    }

    /// <summary>
    /// The section under the pointer, for Ctrl+A: what is worn, the mercenary's, or a containers
    /// section - or null over none of them.
    /// </summary>
    private Control? SectionUnderPointer()
    {
        IEnumerable<Control> sections = new[] { EquippedSection, MercSection }
            .Concat(_sectionPicks.Select(pair => pair.Key));

        foreach (var section in sections)
        {
            if (!section.IsEffectivelyVisible || section.TranslatePoint(default, this) is not { } at)
                continue;

            if (new Rect(at, section.Bounds.Size).Contains(_pointerAt))
                return section;
        }

        return null;
    }

    /// <summary>Where the pointer was last over this page, for Ctrl+A.</summary>
    private Point _pointerAt;

    /// <summary>
    /// The release that ends a carrying drag: down on the cell under the pointer - Shift packs a
    /// group into any free cells - or back where it came from.
    /// </summary>
    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Inv_L page || e.InitialPressMouseButton != MouseButton.Left)
            return;

        // One of a material carried out of the tab: a stack of one where it is let go, or nothing.
        if (page.IsTabCarrying)
        {
            // Over an item - in a grid, where its tile hides the cell, or worn: a gem or a rune into its socket.
            var onItem = SlotUnder(e.Source)?.Slot.Item ?? TileUnder(e);
            page.DropFromTab(CellUnder(e), e.KeyModifiers.HasFlag(KeyModifiers.Shift), onItem);
            e.Handled = true;
            return;
        }

        // Cards carried over from the vault list on the other page, let go on a cell: withdrawn
        // there. Anywhere else, the vault ends
        // the carry itself.
        if (page.IsVaultCarrying)
        {
            if (CellUnder(e) is { } landing)
            {
                page.DropFromVault(landing);
                e.Handled = true;
            }
            // On a worn slot, the character's or the mercenary's: withdrawn and put on.
            else if (SlotUnder(e.Source) is { } worn)
            {
                page.DropFromVaultOnSlot(worn.Slot, worn.Mercenary);
                e.Handled = true;
            }
            // On an item: the cell is taken. The vault ends the carry; nothing moved.
            else if (TileUnder(e) is not null
                     || e.Source is Visual on && (on as Button ?? on.FindAncestorOfType<Button>()) is { } tile
                     && tile.Classes.Contains("tile"))
            {
                page.Session.Status = L("No free space.");
            }

            return;
        }

        if (!page.IsDragCarrying)
            return;

        // On a count in the materials tab: into the tab. Its tiles cover its cells, so no cell is found.
        if (CellUnder(e) is null && TileLayer.Under(e) is { } tabTile && page.IsTabTile(tabTile))
        {
            page.DropCarriedOnTab();
            e.Handled = true;
            return;
        }

        // On a row of the pages' list: to that page, in its first free room.
        if (CellUnder(e) is null && PageRowUnder(e.Source) is { } row)
        {
            page.DropCarriedOnPage(row);
            e.Handled = true;
            return;
        }

        // On a worn slot: put on - or, a potion on the belt worn, into the belt by the game's own
        // choice of column.
        if (CellUnder(e) is null && SlotUnder(e.Source) is { } slot)
            page.DropOnSlot(slot.Slot, slot.Mercenary);
        else
            page.DropCarried(CellUnder(e), e.KeyModifiers.HasFlag(KeyModifiers.Shift), TileUnder(e));

        e.Handled = true;
    }

    /// <summary>
    /// The grid's tile under the pointer, or null - a worn slot's is asked for first (<see cref="SlotUnder"/>).
    /// Asked of the layer that draws the grid's items, by where the pointer is (TileLayer.Under): no item is a
    /// control to be walked up to.
    /// </summary>
    private static ItemTileViewModel? TileUnder(PointerEventArgs e)
    {
        if (TileLayer.Of(e.Source) is not null)
            return TileLayer.Under(e);

        for (var at = e.Source as Visual; at is not null; at = at.GetVisualParent())
        {
            if (at is Control { DataContext: ItemTileViewModel tile })
                return tile;
            if (at is Control { DataContext: EquipSlotViewModel or VP_ViewModel_Inv_L })
                return null;
        }

        return null;
    }

    /// <summary>
    /// The Iron Golem's item under the pointer, or null. Its box is no worn slot (<see cref="SlotUnder"/>): nothing
    /// is put on it or dropped on it.
    /// </summary>
    private ItemTileViewModel? GolemItemUnder(object? source)
    {
        // (What lies on the corpse is drawn in a grid, and found as any grid's item is: TileUnder.)
        for (var at = source as Visual; at is not null && at != GolemSlots; at = at.GetVisualParent())
        {
            if (at is Control { DataContext: EquipSlotViewModel { IsFilled: true, Item: { } item } } && GolemSlots.IsVisualAncestorOf(at))
                return item;
        }

        return null;
    }

    /// <summary>The worn slot under the pointer - the character's or the mercenary's - or null.</summary>
    private (EquipSlotViewModel Slot, bool Mercenary)? SlotUnder(object? source)
    {
        for (var at = source as Visual; at is not null; at = at.GetVisualParent())
        {
            if (at is Control { DataContext: EquipSlotViewModel slot })
            {
                if (EquippedSlots.IsVisualAncestorOf(at))
                    return (slot, false);
                if (MercSlots.IsVisualAncestorOf(at))
                    return (slot, true);
            }

            if (at == EquippedSlots || at == MercSlots)
                break;
        }

        return null;
    }

    /// <summary>
    /// Leaving the page entirely, by the sidebar. The containers are only drawn here, so a
    /// hold that outlived the page would be invisible - an item missing from its cell with
    /// nothing on the cursor to explain it, and no way to finish the move without coming
    /// back and guessing.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        OnHidden();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Put away behind another page (PageDeck): as leaving the window was.</summary>
    public void OnHidden()
    {
        (DataContext as VP_ViewModel_Inv_L)?.SheetHidden();
        PutItBack();
        ItemPanels.Unregister(this);

        if (_keys is not null)
        {
            _keys.RemoveHandler(KeyDownEvent, OnKey);
            _keys.RemoveHandler(PointerReleasedEvent, OnWindowReleased);
            _keys = null;
        }
    }

    private TopLevel? _keys;

    /// <summary>
    /// The page's keys are listened for on the window rather than on the page. A key handler on
    /// a control only fires while focus is inside it, and the page has nothing focused until
    /// something is clicked - so the shortcut would not work until it had been used.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        OnShown();
    }

    /// <summary>In front again (PageDeck), or attached the first time: the keys are listened for, and what the sheets owe is done.</summary>
    public void OnShown()
    {
        (DataContext as VP_ViewModel_Inv_L)?.SheetShown();

        if (_keys is not null || TopLevel.GetTopLevel(this) is not { } window)
            return;

        // Before the handler below: the press that gives this page the keys is seen first.
        ItemPanels.Register(this);
        _keys = window;
        _keys.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _keys.AddHandler(PointerReleasedEvent, OnWindowReleased, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// X fades the sockets. Explorer's keys: Ctrl+A selects all of the section under the
    /// pointer - or, over none, the section of what is selected; Esc puts back what is carried,
    /// else clears the selection; Delete is the bin, asking twice as it does. None of them while
    /// a box or a picker has the keys - on either page, since this listens on the window - and
    /// none while the vault list beside it was pressed in last (<see cref="ItemPanels"/>).
    /// </summary>
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Inv_L page || !IsEffectivelyVisible || !ItemPanels.OwnsKeys(this))
            return;

        if (ItemPanels.IsBoxOrPicker(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual))
        {
            return;
        }

        // Nor while a picker or a question covers a page, wherever the focus was left.
        if (e.Key is Key.X or Key.A or Key.C or Key.V or Key.G or Key.R or Key.Escape or Key.Delete or Key.Left or Key.Right or Key.Up or Key.Down
            && ItemPanels.IsVeiled(TopLevel.GetTopLevel(this)))
            return;

        switch (e.Key, e.KeyModifiers)
        {
            case (Key.X, KeyModifiers.None):
                page.ToggleSockets();
                break;

            // The compendium's key: what is selected here, each that can go stored. The Grail
            // page, pressed in last, takes the keys and G there is its card's (GrailPanel).
            case (Key.G, KeyModifiers.None):
                page.StoreInCompendium();
                break;

            // The game's own key for its Deposit materials: every material on the
            // container pages into the materials tab, as the tools' button. Ctrl + R is the Forge's roll.
            case (Key.R, KeyModifiers.None):
                if (page.CanStoreInMaterials)
                    page.StoreInMaterialsSelected();
                break;

            case (Key.A, KeyModifiers.Control):
                if (SectionUnderPointer() is { } section)
                {
                    if (section == EquippedSection)
                        page.SelectAllEquipped();
                    else if (section == MercSection)
                        page.SelectAllMerc();
                    else if (section.DataContext is ContainerSectionViewModel containers)
                        page.SelectAll(containers);
                }
                else
                {
                    page.SelectAllAroundSelection();
                }
                break;

            // What is carried first; then a cut, let go of where it lies (ItemClipboard); then the selection.
            // A socket carried out of the Edit page's row is carried too: its section calls it off by Esc, but
            // heard here first - the two listen on the window, in the order they came to it - Esc let the
            // selection go under the carry, and the Edit page was empty (the UI cases).
            case (Key.Escape, KeyModifiers.None):
                if (PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.IsCarrying)
                    PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.Cancel?.Invoke();
                else if (page.Session.IsHolding)
                    page.CancelHold();
                // A jewel's layer over the Edit page goes first: back to the item (MainView.OnEscape), which this let go
                // of, the Edit page left empty.
                else if ((Avalonia.Application.Current as App)?.Services.GetService(typeof(MainViewModel)) is MainViewModel { EditLayerInSight: not null })
                    return;
                else if (PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.Kind == PD2SPCompanion.Shell.Services.Generic.ClipKind.Cut)
                    PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.Clear();
                else
                    page.ClearSelection();
                break;

            case (Key.X, KeyModifiers.Control):
                if (!page.CanCut)
                    return;
                page.CutCommand.Execute(null);
                break;

            case (Key.Delete, KeyModifiers.None):
                // A socket of the Edit page's row selected: what is in it, not the item it is in (SocketCarry).
                if (PD2SPCompanion.Shell.Services.Generic.SocketCarry.Shared.DeleteSelection?.Invoke() == true)
                    break;
                if (!page.CanDelete)
                    return;
                page.DeleteItemCommand.Execute(null);
                break;

            // The game's key over an item: what is selected, as the text the game copies (CopyText).
            case (Key.C, KeyModifiers.Control):
                page.CopySelectedCommand.Execute(null);
                break;

            // Paste: what the clipboard holds, here. A copy pasted is the duplicate:
            // there is no Duplicate key of its own.
            // By the key it lands on the cell the pointer is over, as the menu's Paste lands on the cell it was
            // opened on; over no grid, in the
            // first pane's first room.
            // Over the Edit page's row of sockets it is that section's: into the item's sockets.
            case (Key.V, KeyModifiers.Control):
                if (PD2SPCompanion.Shell.Services.Generic.ItemClipboard.Shared.PasteUnderPointer())
                    break;

                // Over a worn slot: onto it, as a drop there.
                if (SlotUnder(this.InputHitTest(_pointerAt)) is { } onSlot)
                {
                    page.PasteOnSlot(onSlot.Slot, onSlot.Mercenary);
                    break;
                }

                var (pane, under) = PaneUnderPointer(page);
                page.PasteOn(pane, under);
                break;

            case (Key.Left or Key.Right or Key.Up or Key.Down, KeyModifiers.None):
                if (!Arrow(page, e.Key))
                    return;
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// An arrow moves the one selected to the nearest item that way in its own grid or among the worn slots - a
    /// plain click on it, so nothing is lifted.
    /// Not while carrying or while several are picked. False where there is no selection to move from.
    /// </summary>
    private bool Arrow(VP_ViewModel_Inv_L page, Key arrow)
    {
        if (page.Session.IsHolding || page.IsPicking || page.Session.SelectedItem is not { } chosen)
            return false;

        // Every item in sight, by where its middle is on the page: a grid's from the layer that draws them, a
        // worn one by its slot's button. Within what it is in: its container's grid, the character's worn
        // slots, the mercenary's.
        Visual? Area(Visual of) =>
            of.FindAncestorOfType<ContainerGrid>() as Visual
            ?? (EquippedSection.IsVisualAncestorOf(of) ? EquippedSection
                : MercSection.IsVisualAncestorOf(of) ? MercSection : null);

        var spots = new List<(ItemTileViewModel Tile, Point Middle, Visual? Area, TileLayer? Layer, Button? Button)>();
        foreach (var layer in this.GetVisualDescendants().OfType<TileLayer>().Where(each => each.IsEffectivelyVisible))
        {
            var area = Area(layer);
            foreach (var tile in layer.Tiles)
            {
                if (layer.TranslatePoint(TileLayer.PlaceOf(tile).Center, this) is { } middle)
                    spots.Add((tile, middle, area, layer, null));
            }
        }

        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            if (button.Classes.Contains("tile") && button.IsEffectivelyVisible && (button.DataContext as EquipSlotViewModel)?.Item is { } worn
                && button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), this) is { } middle)
            {
                spots.Add((worn, middle, Area(button), null, button));
            }
        }

        int at = spots.FindIndex(spot => ReferenceEquals(spot.Tile, chosen));
        if (at < 0)
            return false;

        var from = spots[at];
        var among = spots.Where(spot => ReferenceEquals(spot.Area, from.Area) && !ReferenceEquals(spot.Tile, chosen)).ToList();
        if (ItemPanels.Nearest(from.Middle, among.Select(spot => spot.Middle).ToList(), arrow) is { } best)
        {
            var next = among[best];
            if (next.Layer is { } drawnBy)
            {
                drawnBy.Click(next.Tile);
                drawnBy.BringIntoView(TileLayer.PlaceOf(next.Tile));
            }
            else if (next.Button is { } slot)
            {
                NavigationKeys.Press(slot);
                slot.BringIntoView();
            }
        }

        return true;
    }

    private void PutItBack()
    {
        if (DataContext is VP_ViewModel_Inv_L page && page.Session.IsHolding)
            page.CancelHold();
        if (DataContext is VP_ViewModel_Inv_L carrying)
            carrying.EndTabCarry();
    }

    /// <summary>The page of the pane the pointer is over - or was last, on this page - and the cell of its grid there; nulls over neither.</summary>
    private (StashPageChoice? Page, GridCell? Cell) PaneUnderPointer(VP_ViewModel_Inv_L page)
    {
        foreach (var grid in this.GetVisualDescendants().OfType<ContainerGrid>())
        {
            // The belt is no page: its cell alone, which the paste takes into the belt.
            bool belt = grid.Source is not null && ReferenceEquals(grid.Source, page.Session.BeltGrid);
            if (grid.IsEffectivelyVisible && grid.TranslatePoint(default, this) is { } at
                && new Rect(at, grid.Bounds.Size).Contains(_pointerAt) && ItemPanels.InSight(grid, _pointerAt - at)
                && (page.PageShowing(grid.Source) is { } || belt))
            {
                var shown = belt ? null : page.PageShowing(grid.Source);
                var cell = grid.GetVisualDescendants().OfType<CellLayer>().FirstOrDefault() is { } layer
                           && this.TranslatePoint(_pointerAt, layer) is { } within
                    ? layer.CellAt(within)
                    : null;
                return (shown, cell);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// The game's text of a copy, on the system's clipboard - to paste in the Forge, as Ctrl + C always did here.
    /// The page's own command copies (CopySelectedCommand: the button, the key and the menu's row are one); the
    /// window's clipboard is the view's to reach.
    /// </summary>
    private void ToClipboard(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            _ = Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
    }

    /// <summary>
    /// The arrow joined to Fast inventory storing: its ways, listed under the split button - from the button's
    /// left edge, the two being one thing (it hung from the arrow, half the arrow's width in). A place of no width: the host's
    /// word for "from the control's own left edge" (ActMenuHost.Open).
    /// </summary>
    private void StoreAllMore_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is VP_ViewModel_Inv_L page)
        {
            PD2SPCompanion.Shell.Services.Generic.ActMenuService.Shared.Show(
                page.StoreAllMenu(), StoreAllButton, new Rect(0, 0, 0, StoreAllButton.Bounds.Height));
        }
    }

    /// <summary>
    /// The cell of a grid the pointer is over, with nothing carried: asked of the grids' cell layers by where they
    /// lie, as a layer takes the pointer itself only while something is held over it (CellLayer.HitTest) - and
    /// only of the grid the press landed in, as one scrolled out of sight lies under the pointer too.
    /// </summary>
    private GridCell? RoomUnder(PointerEventArgs e)
    {
        if (e.Source is not Visual pressed || (pressed as ContainerGrid ?? pressed.FindAncestorOfType<ContainerGrid>()) is not { } grid)
            return null;

        return grid.GetVisualDescendants().OfType<CellLayer>().FirstOrDefault() is { } layer ? layer.CellAt(e.GetPosition(layer)) : null;
    }

    /// <summary>What the right button was pressed on - an item's tile, a page's row, a pane's empty cell: what its release opens the menu of.</summary>
    private object? _context;

    /// <summary>
    /// The menu asked for: by the right button let go - of what it was pressed on - or by
    /// the Menu key or Shift + F10, of what is selected, beside it.
    /// </summary>
    private void Item_Context(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Inv_L { Session.IsHolding: false } page)
            return;

        var menus = PD2SPCompanion.Shell.Services.Generic.ActMenuService.Shared;
        if (e.TryGetPosition(this, out _))
        {
            var on = _context;
            _context = null;
            switch (on)
            {
                case ItemTileViewModel tile:
                    menus.Show(page.MenuFor(tile));
                    e.Handled = true;
                    break;
                case StashPageChoice row:
                    menus.Show(page.MenuFor(row));
                    e.Handled = true;
                    break;
                case GridCell room:
                    menus.Show(page.MenuFor(room));
                    e.Handled = true;
                    break;
                case System.ValueTuple<EquipSlotViewModel, bool> bare:
                    menus.Show(page.MenuFor(bare.Item1, bare.Item2));
                    e.Handled = true;
                    break;
            }

            return;
        }

        // By the keys: the item selected, or the first of several, where it is drawn - no item has the keyboard.
        if (ItemPanels.IsBoxOrPicker(e.Source as Visual) || page.MenuTile() is not { } chosen)
            return;

        if (TileLayer.Find(this, chosen) is { } drawn)
            menus.Show(page.MenuFor(chosen), drawn.Layer, drawn.Place);
        else
            menus.Show(page.MenuFor(chosen), this, new Rect(Bounds.Width / 3, Bounds.Height / 3, 1, 1));
        e.Handled = true;
    }

    /// <summary>
    /// Right-clicking anywhere puts down whatever is being carried, and so does clicking
    /// somewhere that is not anything - the page's own background. Clicking a control that
    /// does something else, a fold header or a weapon tab, leaves the hold alone: reaching
    /// the stash means opening it first, and that should not cost you what you are
    /// carrying.
    /// <para>
    /// With nothing carried, that same click on nothing clears the selection, one item or
    /// several - Explorer's click on empty space. Not with Ctrl or Shift, which begin a
    /// rectangle that adds to it, and not on a box or a picker, whose click is theirs.
    /// </para>
    /// </summary>
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Inv_L page)
            return;

        // The keys back from a picker or a box, pressed anywhere else: a tile takes no focus of its own.
        ItemPanels.TakeKeysBack(this, e);

        // The bin takes its own arming back on a press elsewhere - see BinButton.

        var properties = e.GetCurrentPoint(this).Properties;

        if (properties.IsRightButtonPressed)
        {
            if (!page.Session.IsHolding)
            {
                // What the press is on is what the menu will be for, once the button is let go (Item_Context): a
                // worn item or one in a grid - selected by the press, as Explorer's right click selects - or a row
                // of the pages' list. Whatever keys are held: the chords that once acted here (put on the
                // mercenary, store, send across) are rows of the menu.
                _context = null;
                if (SlotUnder(e.Source) is { Slot: { IsFilled: true, Item: { } wornItem } })
                {
                    page.SelectForMenu(wornItem);
                    _context = wornItem;
                    e.Handled = true;
                }
                // A bare worn slot: its own menu - paste there.
                else if (SlotUnder(e.Source) is { Slot.IsFilled: false } bareSlot)
                {
                    _context = (bareSlot.Slot, bareSlot.Mercenary);
                }
                // The Iron Golem's item, beside the mercenary's gear: its own menu.
                else if (GolemItemUnder(e.Source) is { } golemItem)
                {
                    page.SelectForMenu(golemItem);
                    _context = golemItem;
                    e.Handled = true;
                }
                else if (PageRowUnder(e.Source) is { } row)
                {
                    _context = row;
                    e.Handled = true;
                }
                else if (TileUnder(e) is { } tile)
                {
                    page.SelectForMenu(tile);
                    _context = tile;
                    e.Handled = true;
                }

                // On a pane's empty room: the room's own menu - paste there, select all.
                else if (RoomUnder(e) is { } room)
                {
                    _context = room;
                }

                return;
            }

            page.CancelHold();
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed)
            return;

        // Ctrl + click on a row of the pages' list: the second pane shows that page, or is shut on the one it
        // shows (by a right click until the right click became the menu's). Before the
        // row's own click, which is the first pane's.
        if (e.KeyModifiers == KeyModifiers.Control && !page.Session.IsHolding && PageRowUnder(e.Source) is { } second)
        {
            page.ShowSecond(second);
            e.Handled = true;
            return;
        }

        // A click that landed on no control at all: nothing was aimed at. A grid's item is no control and was
        // aimed at all the same (TileLayer): taken for the page's own ground, a press on one of several picked
        // let the picks go before the drag that was to carry them.
        if (e.Source is not Visual visual
            || (visual as Button ?? visual.FindAncestorOfType<Button>()) is not null
            || TileLayer.Under(e) is not null)
        {
            return;
        }

        // Except a scroll bar, whose track and thumb are not buttons. Dragging the page
        // into view is not a click on the background, and losing the selection to it would
        // punish the one gesture you have to make to see the rest of the list. Nor a box or
        // a picker: choosing a stash page is not letting go of what is selected.
        if ((visual as ScrollBar ?? visual.FindAncestorOfType<ScrollBar>()) is not null
            || ItemPanels.IsBoxOrPicker(visual))
        {
            return;
        }

        if (page.Session.IsHolding)
        {
            page.CancelHold();
            e.Handled = true;
        }
        else if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == 0
                 && (page.Session.SelectedItem is not null || page.IsPicking))
        {
            page.ClearSelection();
            e.Handled = true;
        }
    }

    /// <summary>Lights what is under a carried item. The ghost itself is the window's (MainView).</summary>
    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Inv_L page)
            return;

        _pointerAt = e.GetPosition(this);

        // The right button while a drag carries: put back, as on a vault card and a socket. The left is down for the
        // whole drag, so the right one comes as a move whose update is its press, not as a press of its own (OnPressed).
        if (e.GetCurrentPoint(null).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed && page.PutBackCarried())
        {
            _context = null;
            e.Handled = true;
            return;
        }

        // A card carried from the vault: the cells it would land on light green or red, or a worn slot under it.
        if (page.IsVaultCarrying)
        {
            var landing = CellUnder(e);
            var over = landing is null ? SlotUnder(e.Source) : null;
            page.HoverSlot(over?.Slot, over?.Mercenary ?? false);
            page.HoverArriving(landing);
            return;
        }

        // A count carried out of the materials tab: the item it would be socketed into lights - worn, or in a grid.
        if (page.IsTabCarrying)
        {
            // Nothing is held, so a tile still takes the pointer: its own first cell stands for it.
            var wornUnder = SlotUnder(e.Source);
            var lit = wornUnder is null ? CellUnder(e) ?? (TileUnder(e) is { } over ? page.CellOf(over) : null) : null;
            page.HoverSlot(wornUnder?.Slot, wornUnder?.Mercenary ?? false);
            page.HoverOver(lit);
            return;
        }

        if (!page.Session.IsHolding)
        {
            _pageLists = null;
            return;
        }

        // A cell, or a worn slot: whichever is under the pointer lights up. An item's tile takes the pointer
        // and has no cell to say - but one a carried gem would be socketed into, or a carried stack would join,
        // lights by its own first cell.
        var cell = CellUnder(e);
        var under = cell is null ? SlotUnder(e.Source) : null;
        if (cell is null && under is null && TileUnder(e) is { } host && (page.WouldSocket(host) || page.WouldStack(host)))
            cell = page.CellOf(host);
        page.HoverSlot(under?.Slot, under?.Mercenary ?? false);
        page.HoverOver(cell);
        page.HoverPage(cell is null && under is null ? PageRowUnder(e.Source) : null);
        ScrollPagesUnder(e);
    }

    /// <summary>The lists of pages, found once while something is carried (<see cref="ScrollPagesUnder"/>).</summary>
    private List<ScrollViewer>? _pageLists;

    /// <summary>How near the list's top or foot the pointer scrolls it under what is carried, and how far a move scrolls.</summary>
    private const double PagesEdge = 22, PagesStep = 14;

    /// <summary>
    /// Something carried near the top or the foot of the list of pages: the list scrolls under it, so a page
    /// whose row is out of sight can still be let go on.
    /// </summary>
    private void ScrollPagesUnder(PointerEventArgs e)
    {
        // Found once a carry: every move walked the whole page for them, 4 ms of each drag. Let go of when nothing is held.
        _pageLists ??= [.. this.GetVisualDescendants().OfType<ScrollViewer>().Where(list => list.Classes.Contains("pagelist"))];

        foreach (var list in _pageLists)
        {
            if (!list.IsEffectivelyVisible || list.Extent.Height <= list.Viewport.Height)
                continue;

            var at = e.GetPosition(list);
            if (at.X < 0 || at.X > list.Bounds.Width || at.Y < -PagesEdge || at.Y > list.Bounds.Height + PagesEdge)
                continue;

            double by = at.Y < PagesEdge ? -PagesStep : at.Y > list.Bounds.Height - PagesEdge ? PagesStep : 0;
            if (by != 0)
                list.Offset = list.Offset.WithY(System.Math.Clamp(list.Offset.Y + by, 0, list.Extent.Height - list.Viewport.Height));
        }
    }

    /// <summary>The page whose row of the list the pointer is over, or null.</summary>
    private static StashPageChoice? PageRowUnder(object? source)
    {
        for (var at = source as Visual; at is not null; at = at.GetVisualParent())
        {
            if (at is Button { DataContext: StashPageChoice { IsSeparator: false } page } row && row.Classes.Contains("pagerow"))
                return page;
        }

        return null;
    }

    /// <summary>The gold's panel is a popup, out of this page's tree: it takes ctrl and shift on its buttons itself.</summary>
    private void GoldPanel_Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { Tag: null } panel)
        {
            panel.Tag = "keys";
            panel.OnModifiedPress(SpendGold);
        }
    }

    /// <summary>
    /// The grid cell the pointer is over, or null. The element the event came from says which
    /// grid - its cell layer, which takes the pointer only while something is held, so it cannot
    /// disagree with whatever lies over it - and the pointer's place in it says which cell.
    /// <para>
    /// Where the event names no grid, the layers are asked by position. The renderer hit-tests the
    /// frame it last drew, and a release just after a pick-up came from the page's scroll presenter
    /// or the lifted tile rather than from the grid under it - the drop found no cell and the item
    /// was put back (the put-back race, 7 of 10 quick drops). The grids' own layout is current, so
    /// their place answers. Only a live tile of another item is a taken cell, as it always was.
    /// </para>
    /// </summary>
    private GridCell? CellUnder(PointerEventArgs e)
    {
        if (e.Source is not Visual visual)
            return null;

        if ((visual as CellLayer ?? visual.FindAncestorOfType<CellLayer>()) is { } layer)
            return layer.CellAt(e.GetPosition(layer));

        if (TileLayer.Under(e) is { } tile
            && (DataContext is not VP_ViewModel_Inv_L page || !page.Session.IsHeld(tile.Item)))
        {
            return null;
        }

        foreach (var under in this.GetVisualDescendants().OfType<CellLayer>())
        {
            // In sight too: the page's header lies over a grid scrolled under it, and a drop on the header is no
            // drop on rows the player cannot see. The race above comes from inside the scroller, which this keeps.
            var at = e.GetPosition(under);
            if (under.IsEffectivelyVisible && ItemPanels.InSight(under, at) && under.CellAt(at) is { } cell)
                return cell;
        }

        return null;
    }

    /// <summary>
    /// What two stash grids and the controls between them need to sit abreast: two ten-wide
    /// grids with their frames, and a column of buttons.
    /// </summary>
    private const double StashesSideBySideWidth = 800;

    /// <summary>
    /// The stash panes are laid out by a WrapPanel, so the window decides whether they end
    /// up across or stacked. Only the view knows which happened, so it tells the page -
    /// purely so the transfer arrows point the way the panes actually lie.
    /// </summary>
    private void OnSectionsResized(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is VP_ViewModel_Inv_L page)
            page.StashesSideBySide = e.NewSize.Width >= StashesSideBySideWidth;
    }
}
