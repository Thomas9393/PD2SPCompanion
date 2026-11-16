using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.Services.Generic;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// An item's tooltip where the item can be acted on: the item, a strip of what the keys do with it, and - the
/// comparison on - what is worn where a click would put it (ItemActionTip.axaml; <see cref="TipActions"/>).
/// Drawn afresh as the keys held change and as the comparison is turned on and off, while it is up.
/// </summary>
public partial class ItemActionTip : UserControl
{
    /// <summary>The item the tooltip is of.</summary>
    public static readonly StyledProperty<ItemTileViewModel?> TileProperty =
        AvaloniaProperty.Register<ItemActionTip, ItemTileViewModel?>(nameof(Tile));

    /// <summary>Where the item is, which says what can be done with it: <see cref="TipActions"/>'s origins.</summary>
    public static readonly StyledProperty<string> OriginProperty =
        AvaloniaProperty.Register<ItemActionTip, string>(nameof(Origin), TipActions.Grid);

    public ItemTileViewModel? Tile
    {
        get => GetValue(TileProperty);
        set => SetValue(TileProperty, value);
    }

    public string Origin
    {
        get => GetValue(OriginProperty);
        set => SetValue(OriginProperty, value);
    }

    public ItemActionTip()
    {
        InitializeComponent();
    }

    private bool _up;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Two boxes of its own: the tooltip around them gives up its frame (ToolTip.actions).
        if (this.FindAncestorOfType<ToolTip>() is { } tip && !tip.Classes.Contains("actions"))
            tip.Classes.Add("actions");

        // Never taller than the screen: what is, scrolls (the [ and ] keys).
        double most = PD2SPCompanion.Shell.Helper.ItemTipPlacement.Most(
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow);
        OwnBox.MaxHeight = most;
        Compared.MaxHeight = most;

        _up = true;
        TipActions.Opened(this, Tile);
        TipActions.Changed += Refresh;
        TipActions.Scrolled += Scroll;
        OwnBox.PropertyChanged += OnOwnBoxChanged;
        CaptionBox.PropertyChanged += OnCaptionChanged;
        Scroller.ScrollChanged += OnExtent;
        Scroller2.ScrollChanged += OnExtent;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _up = false;
        _compares = false;
        ComparedPopup.IsOpen = false;
        TipActions.Changed -= Refresh;
        TipActions.Scrolled -= Scroll;
        OwnBox.PropertyChanged -= OnOwnBoxChanged;
        CaptionBox.PropertyChanged -= OnCaptionChanged;
        Scroller.ScrollChanged -= OnExtent;
        Scroller2.ScrollChanged -= OnExtent;
        TipActions.Closed(this);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TileProperty && _up)
            TipActions.Opened(this, Tile);

        if (change.Property == TileProperty || change.Property == OriginProperty)
            Refresh();
    }

    private void Refresh()
    {
        Own.Content = Tile;
        StoredLine.Text = Tile?.StoredLine ?? "";
        StoredBlock.IsVisible = Tile?.Added is not null;

        // Asked only while it is up: a tooltip is built ahead of being shown.
        // A vault's tile is drawn by the same grid as the character's: what it is, it says itself.
        var info = _up && Tile is { } tile ? TipActions.Describe(tile, tile.IsVaults ? TipActions.Vault : Origin) : null;

        // The comparison open over an item that goes on someone: its own popup beside the item's box, which
        // stays as it is - the strip in it either way.
        bool compares = _up && TipActions.Compares && info is { Offers: true };
        TipActions.Offers(this, info is { Offers: true });

        // And the key that reads its sockets apart, where something is in one - wherever the item is.
        _hints = Tile is { HasFilledSockets: true } ? [.. info?.Hints ?? [], .. SocketView.Hints] : info?.Hints ?? [];
        ShowHints();

        Others.ItemsSource = compares ? info!.Compared : null;
        EmptyBox.IsVisible = compares && info!.Compared.Count == 0;
        EmptyText.Text = compares ? info!.Empty : "";
        Caption.Text = compares ? info!.Caption : "";
        ShowCompared(compares);
    }

    private bool _compares;

    /// <summary>
    /// The comparison's popup opened or shut. <b>Never opened before the item's own tooltip stands where it
    /// will</b>: it is set against the item's box, and asked for as the tooltip was only coming up - the
    /// comparison left open from the item before - it was placed against a box not yet laid out or put on the
    /// screen, and came down over the tooltip instead of beside it (opened by Tab over a tooltip already up, it stood
    /// right). So it waits for the
    /// tooltip's layout and placing, which a job behind them in the queue does.
    /// </summary>
    private void ShowCompared(bool compares)
    {
        _compares = compares;
        if (!compares)
        {
            ComparedPopup.IsOpen = false;
            return;
        }

        if (!ComparedPopup.IsOpen)
            PlaceCompared();
    }

    private void PlaceCompared() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_up || !_compares)
                return;

            LineUp();
            ComparedPopup.IsOpen = true;
        }, Avalonia.Threading.DispatcherPriority.Background);

    /// <summary>
    /// How tall the comparison's caption is, as last laid out - the same for every one, and wanted before the
    /// popup that holds it is open; a line of small text in its box until one has been measured.
    /// </summary>
    private static double _captionHeight = 21;

    /// <summary>
    /// The comparison's first line level with the item's: up by its caption's height, which then stands out over
    /// the item's box.
    /// </summary>
    private void LineUp() => ComparedPopup.VerticalOffset = -_captionHeight;

    /// <summary>The caption laid out: its height kept, and the comparison put level by it where that differs from what it was placed by.</summary>
    private void OnCaptionChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != BoundsProperty || e.NewValue is not Rect { Height: > 0 } now || Math.Abs(now.Height - _captionHeight) < 0.5)
            return;

        _captionHeight = now.Height;
        LineUp();
    }

    /// <summary>The item's box changed its size while the comparison is open - a line of the strip grew: placed against it again.</summary>
    private void OnOwnBoxChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != BoundsProperty || !ComparedPopup.IsOpen
            || e.OldValue is not Rect before || e.NewValue is not Rect now || before.Size == now.Size)
        {
            return;
        }

        ComparedPopup.IsOpen = false;
        PlaceCompared();
    }

    private System.Collections.Generic.IReadOnlyList<TipHint> _hints = [];
    private bool _scrolls;

    /// <summary>How far a press of [ or ] moves a tooltip that scrolls: three lines or so.</summary>
    private const double Step = 54;

    /// <summary>The strip's lines, and after them - only while something here scrolls - the keys that scroll it.</summary>
    private void ShowHints()
    {
        var lines = _scrolls ? [.. _hints, new TipHint(L("[ ]"), L("Scroll"))] : _hints;
        Hints.Hints = lines;
        Strip.IsVisible = lines.Count > 0;
    }

    /// <summary>What there is to scroll changed - the tooltip laid out, the comparison opened: whether the bar shows, and its keys with it.</summary>
    private void OnExtent(object? sender, ScrollChangedEventArgs e)
    {
        bool scrolls = Overflows(Scroller) || (ComparedPopup.IsOpen && Overflows(Scroller2));
        if (scrolls == _scrolls)
            return;

        _scrolls = scrolls;
        TipActions.Scrolls(this, scrolls);
        ShowHints();
    }

    private static bool Overflows(ScrollViewer scroller) => scroller.Extent.Height > scroller.Viewport.Height + 0.5;

    /// <summary>[ or ]: the item, and what it is compared with, a step up or down.</summary>
    private void Scroll(int direction)
    {
        foreach (var scroller in new[] { Scroller, Scroller2 })
        {
            double furthest = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
            scroller.Offset = new Vector(0, Math.Clamp(scroller.Offset.Y + direction * Step, 0, furthest));
        }
    }
}
