using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using PD2SPCompanion.Shell.CustomControls.Base;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Inv_R : UserControl, PD2SPCompanion.Shell.CustomControls.Base.IShownPage
{
    public VP_View_Inv_R()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => Follow();
        Follow();

        DataContextChanged += (_, _) => FollowNested();
        FollowNested();

        // Tunnelling, from the page rather than the buttons: a Button marks PointerPressed
        // handled in its own class handler, so a handler on the button never sees the
        // modifier. See ModifierClick, and VP_View_Char_L which does the same for its
        // attribute steppers.
        this.OnModifiedPress(Jump);

        SummaryGrip.PointerPressed += Grip_Pressed;
        SummaryGrip.PointerMoved += Grip_Moved;
        SummaryGrip.PointerReleased += Grip_Released;
        SizeChanged += (_, _) => ApplySummaryHeight();
        PropertyChanged += (_, changed) =>
        {
            if (changed.Property == ActionsProperty)
                ApplySummaryHeight();
        };
        ApplySummaryHeight();
    }

    // ---- the item holder's height ----------------------------------------------------------

    /// <summary>
    /// Its name among the sizes kept (FoldMemory.Size): the holder's share of the page's height, in thousandths -
    /// one for the Edit page and the Forge alike. None, 0: the holder's own height. A share, not a height: kept in pixels
    /// until then, a holder drawn out to its most in a
    /// small window was a small part of a large one.
    /// </summary>
    private const string SummaryShare = "ItemSummary.Share";

    /// <summary>The height in pixels the grip added, as an older setting kept it: read once, turned into a share, and forgotten.</summary>
    private const string SummarySizeOnce = "ItemSummary.Extra";

    /// <summary>The most of the page the holder takes: 45%.</summary>
    private const int MostShare = 450;

    /// <summary>The holder's first row as it is made: the art's 140; and the summary running through the foot too, 178.</summary>
    private const double SummaryRow = 140, SummaryTall = 178;

    /// <summary>The rest of the holder: its padding, frame and foot - what the 45% has to leave room for.</summary>
    private const double HolderBesides = 62;

    /// <summary>The holder's share of the page's height, in thousandths; 0, its own height.</summary>
    private int _summaryShare = PD2SPCompanion.Shell.Services.Settings.FoldMemory.Size(SummaryShare);

    /// <summary>What the grip adds while it is held, in pixels: the share is worked out as it is let go of.</summary>
    private int _summaryExtra;

    private double? _gripFrom;
    private int _gripExtraFrom;

    /// <summary>The most the grip adds: the holder at 45% of the page.</summary>
    private int MostSummaryExtra => Bounds.Height > 0
        ? (int)Math.Max(0, Bounds.Height * MostShare / 1000.0 - HolderBesides - SummaryRow)
        : int.MaxValue;

    /// <summary>What a share adds to the holder's own height, on the page as tall as it is now.</summary>
    private int ExtraOf(int share) => share <= 0 || Bounds.Height <= 0
        ? 0
        : Math.Clamp((int)Math.Round(Bounds.Height * share / 1000.0 - HolderBesides - SummaryRow), 0, MostSummaryExtra);

    /// <summary>The share a height added is of the page: none for none.</summary>
    private int ShareOf(int extra) => extra <= 0 || Bounds.Height <= 0
        ? 0
        : Math.Clamp((int)Math.Round((SummaryRow + extra + HolderBesides) * 1000.0 / Bounds.Height), 1, MostShare);

    /// <summary>
    /// The holder's rows at the share kept - or, the grip held, at what it has been dragged to - held to the 45%.
    /// The art's room is the row's whole height, so the item and its sockets stand in its middle (its room stayed 140 at
    /// the row's top as the row was drawn out).
    /// </summary>
    private void ApplySummaryHeight()
    {
        // Kept in pixels by a version before: a share of the page as tall as it now is, once.
        if (_summaryShare == 0 && Bounds.Height > 0
            && PD2SPCompanion.Shell.Services.Settings.FoldMemory.Size(SummarySizeOnce) is > 0 and int once)
        {
            _summaryShare = ShareOf(Math.Min(once, MostSummaryExtra));
            PD2SPCompanion.Shell.Services.Settings.FoldMemory.KeepSize(SummarySizeOnce, 0);
            PD2SPCompanion.Shell.Services.Settings.FoldMemory.KeepSize(SummaryShare, _summaryShare);
        }

        int extra = _gripFrom is null ? ExtraOf(_summaryShare) : Math.Clamp(_summaryExtra, 0, MostSummaryExtra);
        HolderRows.RowDefinitions[0].Height = new GridLength(SummaryRow + extra);
        SummaryRoom.Height = Actions is null ? SummaryTall + extra : double.NaN;
        SummaryView.ArtHeight = SummaryRow + extra;
    }

    private void SetSummaryExtra(int extra, bool keep)
    {
        _summaryExtra = Math.Clamp(extra, 0, MostSummaryExtra);
        if (keep)
        {
            _summaryShare = ShareOf(_summaryExtra);
            PD2SPCompanion.Shell.Services.Settings.FoldMemory.KeepSize(SummaryShare, _summaryShare);
        }

        ApplySummaryHeight();
    }

    private void Grip_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SummaryGrip).Properties.IsLeftButtonPressed)
            return;

        // The second click of a double-click: the holder's own height.
        if (e.ClickCount > 1)
        {
            _gripFrom = null;
            SummaryGrip.Classes.Set("held", false);
            SetSummaryExtra(0, keep: true);
            e.Handled = true;
            return;
        }

        _summaryExtra = _gripExtraFrom = ExtraOf(_summaryShare);
        _gripFrom = e.GetPosition(this).Y;
        SummaryGrip.Classes.Set("held", true);
        e.Pointer.Capture(SummaryGrip);
        e.Handled = true;
    }

    private void Grip_Moved(object? sender, PointerEventArgs e)
    {
        if (_gripFrom is { } from)
            SetSummaryExtra(_gripExtraFrom + (int)Math.Round(e.GetPosition(this).Y - from), keep: false);
    }

    private void Grip_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_gripFrom is null)
            return;

        _gripFrom = null;
        SummaryGrip.Classes.Set("held", false);
        e.Pointer.Capture(null);
        SetSummaryExtra(_summaryExtra, keep: true);
    }

    private VP_ViewModel_Inv_R? _nestedOf;
    private VP_View_Inv_R? _nestedView;

    /// <summary>
    /// The jewel's layer (VP_ViewModel_Inv_R.Nested): this same view again, over the panel the view model opens
    /// for a jewel of its item's sockets - made here the first time one is opened, since markup cannot hold a
    /// control inside itself, and kept for the next.
    /// </summary>
    private void FollowNested()
    {
        if (_nestedOf is not null)
            _nestedOf.PropertyChanged -= OnNestedChanged;

        _nestedOf = DataContext as VP_ViewModel_Inv_R;
        if (_nestedOf is not null)
            _nestedOf.PropertyChanged += OnNestedChanged;

        ShowNested();
    }

    private void OnNestedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VP_ViewModel_Inv_R.Nested))
            ShowNested();
    }

    private void ShowNested()
    {
        if (_nestedOf?.Nested is not { } layer)
        {
            NestedHost.Content = null;
            return;
        }

        _nestedView ??= new VP_View_Inv_R();
        _nestedView.DataContext = layer;
        NestedHost.Content = _nestedView;
    }

    /// <summary>
    /// What a page of its own puts in the corner of the item's holder:
    /// the Forge's FORGE and RESET.
    /// </summary>
    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<VP_View_Inv_R, object?>(nameof(Actions));

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>What the holder says with no item in it, on a page that shows it empty.</summary>
    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<VP_View_Inv_R, string>(nameof(EmptyText), "");

    public string EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>What leads the sections, scrolling with them: the Forge's search.</summary>
    public static readonly StyledProperty<object?> LeadProperty =
        AvaloniaProperty.Register<VP_View_Inv_R, object?>(nameof(Lead));

    public object? Lead
    {
        get => GetValue(LeadProperty);
        set => SetValue(LeadProperty, value);
    }

    /// <summary>
    /// A page of its own over the panel, which makes its item rather than being handed one - the Forge: the
    /// holder and the lead stand with nothing in them yet, and the item, being in no file, shows no id.
    /// </summary>
    public static readonly StyledProperty<bool> IsBareProperty =
        AvaloniaProperty.Register<VP_View_Inv_R, bool>(nameof(IsBare));

    public bool IsBare
    {
        get => GetValue(IsBareProperty);
        set => SetValue(IsBareProperty, value);
    }

    /// <summary>
    /// Shift-clicking a - or a + sends its field straight to that end. A property's value also
    /// takes ctrl: ctrl to the ends of its affix range, shift to the ends
    /// of the field - the latter only while properties are edited freely; otherwise a
    /// shift-click is a plain step.
    /// <para>
    /// Which field is read off the button's Tag rather than from its command: the command
    /// knows how to take one step and nothing about where the field stops, while the
    /// button sits beside the box and knows exactly which one it belongs to.
    /// </para>
    /// </summary>
    private bool Jump(Button button, KeyModifiers modifiers)
    {
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = modifiers.HasFlag(KeyModifiers.Control);
        if (!shift && !ctrl)
            return false;

        if (button.Tag is not string tag || tag.Split(':') is not [var field, var way])
            return false;

        bool up = way == "up";

        // A property's value, its skill level and charges, and the Basic section's boxes know what ctrl
        // means; the rest jump on shift alone.
        if (!shift && field is not ("value" or "skill" or "charges") && button.DataContext is not BasicEditor)
            return false;

        switch (field)
        {
            // As every stepper: Shift the end - the affix range's, the end the game could
            // roll; Ctrl a larger step; both, in free editing, the field's own end. It was Ctrl the affix end and
            // Shift the field's, the other way round from every other stepper.
            case "value" when button.DataContext is StatRowViewModel row:
                if (shift && ctrl && StatRowViewModel.FreeEditing)
                    row.JumpValue(up);
                else if (shift)
                    row.JumpToRoll(up);
                else
                    row.Leap(up);
                return true;

            case "skill" when button.DataContext is StatRowViewModel row:
                if (shift)
                    row.JumpSkillLevel(up);
                else
                    row.LeapSkillLevel(up);
                return true;

            case "charges" when button.DataContext is StatRowViewModel row:
                if (shift)
                    row.JumpMaxCharges(up);
                else
                    row.LeapMaxCharges(up);
                return true;

            // Shift on the Forge's orb: the fuller roll - ethereal and the affixes with the rest.
            case "item" when way == "roll" && shift && button.DataContext is VP_ViewModel_Inv_R { RollsAsDrop: true } rolled:
                rolled.RollAsNewDrop();
                return true;

            // Shift on the die: one to the most the base takes.
            case "sockets" when way == "roll" && button.DataContext is SocketsEditor sockets:
                sockets.RollWhole();
                return true;

            case "sockets" when button.DataContext is SocketsEditor sockets:
                sockets.Jump(up);
                return true;

            default:
                // The Basic section's boxes: their buttons sit in its own control.
                if (button.DataContext is not BasicEditor basic)
                    return false;

                // Shift to the end - the base's, where it has one; ctrl ten at a time (the Basic
                // boxes took no ctrl); both the field's end, as a property's.
                if (shift)
                    basic.Jump(field, up, full: ctrl);
                else
                    basic.Leap(field, up);
                return true;
        }
    }

    private VP_ViewModel_Inv_R? _watched;

    /// <summary>
    /// Listens to whichever panel is in front, and only that one. The one let go of throws away
    /// anything typed and not kept, as leaving the page does (<see cref="OnDetachedFromVisualTree"/>):
    /// the Utilities page's Edit swaps the inventory's panel for the vault's as the left page
    /// changes, and comes back to it later.
    /// </summary>
    private void Follow()
    {
        if (_watched is not null)
        {
            _watched.ShowHeldIn -= BringHeldIntoView;
            _watched.ShowStatRow -= BringStatRowIntoView;
            if (!ReferenceEquals(_watched, DataContext))
                _watched.DiscardStats();
        }

        _watched = DataContext as VP_ViewModel_Inv_R;

        if (_watched is not null)
        {
            _watched.ShowHeldIn += BringHeldIntoView;
            _watched.ShowStatRow += BringStatRowIntoView;
        }
    }

    /// <summary>
    /// Brings a property's row into sight: one just picked, in the place the game's order gave it. By its
    /// place in the list, as the rows out of sight are not built; posted, as the rows are built again by
    /// the change that asked.
    /// </summary>
    private void BringStatRowIntoView(int index)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.GetLogicalDescendants().OfType<ItemsControl>().FirstOrDefault(each => each.Name == "StatRows") is { } rows
                && index < rows.ItemCount)
            {
                rows.ScrollIntoView(index);
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Brings the row a list is already sitting on into sight - the affix in the slot being
    /// filled, the halves a rare name is built from, the corruption an item carries.
    /// <para>
    /// Found by walking the list rather than by index, because one of them is not a flat
    /// list at all: the corruptions are grouped by rarity, so the row wanted lives inside a
    /// nested list of its own.
    /// </para>
    /// <para>
    /// Posted rather than done now: the rows are rebuilt by the same change that asked for
    /// this, and they do not exist until the layout pass that follows.
    /// </para>
    /// </summary>
    private void BringHeldIntoView(string listName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // By name across the editors' own controls, whose names are not in this one's scope.
            if (this.GetLogicalDescendants().OfType<ItemsControl>().FirstOrDefault(each => each.Name == listName) is not { } list)
                return;

            // A flat list by the row's place: one that makes only the rows in sight (the affixes') has no
            // control for a row out of it.
            int at = 0;
            foreach (object? row in list.Items)
            {
                if (row is IPickRow { IsHeld: true })
                {
                    list.ScrollIntoView(at);
                    return;
                }

                at++;
            }

            list.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(row => row.DataContext is IPickRow { IsHeld: true })
                ?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Leaving the page throws away anything typed and not kept.
    /// <para>
    /// Changing item already rebuilds these rows from whatever is now selected, so that
    /// case looks after itself. This is the other way out: leaving by the sidebar and
    /// coming back would otherwise show edits that were never written, against an item
    /// that does not have them.
    /// </para>
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        OnHidden();
        if (_keys is not null)
        {
            _keys.RemoveHandler(KeyDownEvent, OnRerollKey);
            _keys.RemoveHandler(KeyUpEvent, OnRerollKeyUp);
            _keys = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private TopLevel? _keys;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // On the window, as the Items page listens: the page's own controls seldom have the focus.
        if (_keys is null && TopLevel.GetTopLevel(this) is { } window)
        {
            _keys = window;
            _keys.AddHandler(KeyDownEvent, OnRerollKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _keys.AddHandler(KeyUpEvent, OnRerollKeyUp, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    /// <summary>The key is down and has rolled: a key held rolls once, not as fast as the keyboard repeats.</summary>
    private bool _rolledByKey;

    /// <summary>
    /// With Shift too it is the Forge's fuller roll - ethereal and the affixes with the rest - and no key on
    /// an item that is someone's (VP_ViewModel_Inv_R.RerollByKey).
    /// Ctrl + R is the Demonic Cube's reroll of the item on this page. First built as a bare R, and changed the
    /// same evening: a bare letter is one stray keypress from an item changed ("simply press R is also unsafe"),
    /// and over the vault's list a letter jumps to a name - R could not be the cube there. With Ctrl it is no
    /// letter anywhere, so it is the same key on every page that shows an item to edit - the Items page, the
    /// vault, the Forge. Held to where the cube's button can be pressed:
    /// <list type="bullet">
    /// <item>the page in sight, and the innermost of it - a jewel's layer open over the item takes the key;</item>
    /// <item>not while a picker or a question covers the page, nor from a box of words (a search, a name): the
    /// keys there are the text's. From a number box it rolls - that is where the focus is left after a number
    /// is typed into a row;</item>
    /// <item>only where the cube's button is - the play mode edits affixes and the item has something to roll
    /// (VP_ViewModel_Inv_R.RerollByKey);</item>
    /// <item>once a press: a key held does not chew through rolls nobody saw.</item>
    /// </list>
    /// What it rolled is on the strip, and Ctrl + Z takes it back, as the button's.
    /// </summary>
    private void OnRerollKey(object? sender, KeyEventArgs e)
    {
        bool full = e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);
        if (e.Key != Key.R || (e.KeyModifiers != KeyModifiers.Control && !full))
            return;

        if (!IsEffectivelyVisible || NestedHost.Content is not null || DataContext is not VP_ViewModel_Inv_R page)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (ItemPanels.IsVeiled(top) || top?.FocusManager?.GetFocusedElement() is TextBox and not NumericTextBox)
            return;

        if (_rolledByKey)
        {
            e.Handled = true;
            return;
        }

        if (page.RerollByKey(full))
        {
            _rolledByKey = true;
            e.Handled = true;
        }
    }

    private void OnRerollKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.R)
            _rolledByKey = false;
    }

    /// <summary>Put away behind another page (PageDeck): leaving it, as it ever was.</summary>
    public void OnHidden()
    {
        if (DataContext is VP_ViewModel_Inv_R panel)
            panel.DiscardStats();
    }

    public void OnShown()
    {
    }
}
