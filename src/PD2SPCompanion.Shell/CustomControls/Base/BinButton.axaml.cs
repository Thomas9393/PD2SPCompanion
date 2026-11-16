using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>The bin that asks twice. See the XAML.</summary>
public partial class BinButton : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<BinButton, ICommand?>(nameof(Command));

    /// <summary>What the command is handed: a list's row, where one command serves every row's bin.</summary>
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<BinButton, object?>(nameof(CommandParameter));

    /// <summary>
    /// Whether the first click has been made and the second is awaited. Two-way: the page's
    /// command sets it, and this control clears it when a press lands anywhere else.
    /// </summary>
    public static readonly StyledProperty<bool> IsArmedProperty =
        AvaloniaProperty.Register<BinButton, bool>(nameof(IsArmed), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// The small square a row's actions are (spend), rather than the tool buttons' - for a row of a
    /// card, as the Settings page's. The tool rows keep the large one.
    /// </summary>
    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<BinButton, bool>(nameof(Compact));

    public static readonly StyledProperty<object?> TipProperty =
        AvaloniaProperty.Register<BinButton, object?>(nameof(Tip));

    /// <summary>Green rather than red, under the pointer and while it asks: a button that asks twice and loses nothing.</summary>
    public static readonly StyledProperty<bool> PositiveProperty =
        AvaloniaProperty.Register<BinButton, bool>(nameof(Positive));

    /// <summary>
    /// The first half of a split button: its right corners square, for the arrow that stands hard against it and
    /// lists its other ways (the Items page's Fast inventory storing).
    /// </summary>
    public static readonly StyledProperty<bool> LeadsProperty =
        AvaloniaProperty.Register<BinButton, bool>(nameof(Leads));

    public bool Leads
    {
        get => GetValue(LeadsProperty);
        set => SetValue(LeadsProperty, value);
    }

    /// <summary>Drawn in place of the bin, where the button means something other than delete.</summary>
    public static readonly StyledProperty<object?> GlyphProperty =
        AvaloniaProperty.Register<BinButton, object?>(nameof(Glyph));

    /// <summary>What the button shows while not armed: the bin, or the glyph given instead.</summary>
    public static readonly StyledProperty<bool> ShowsBinProperty =
        AvaloniaProperty.Register<BinButton, bool>(nameof(ShowsBin), defaultValue: true);

    public static readonly StyledProperty<bool> ShowsGlyphProperty =
        AvaloniaProperty.Register<BinButton, bool>(nameof(ShowsGlyph));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool IsArmed
    {
        get => GetValue(IsArmedProperty);
        set => SetValue(IsArmedProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public object? Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    public bool Positive
    {
        get => GetValue(PositiveProperty);
        set => SetValue(PositiveProperty, value);
    }

    public object? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public bool ShowsBin => GetValue(ShowsBinProperty);

    public bool ShowsGlyph => GetValue(ShowsGlyphProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsArmedProperty || change.Property == GlyphProperty)
        {
            SetValue(ShowsBinProperty, !IsArmed && Glyph is null);
            SetValue(ShowsGlyphProperty, !IsArmed && Glyph is not null);
        }
    }

    public BinButton() => InitializeComponent();

    private TopLevel? _window;

    /// <summary>
    /// Listened for on the window, tunnelling, so a press is seen wherever it lands and before
    /// anything under it can mark it handled - another button beside this one included.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _window = TopLevel.GetTopLevel(this);
        _window?.AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _window?.RemoveHandler(PointerPressedEvent, OnAnyPress);
        _window = null;

        // A page left with its bin armed comes back to one that is not.
        if (IsArmed)
            SetCurrentValue(IsArmedProperty, false);

        base.OnDetachedFromVisualTree(e);
    }

    private void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (!IsArmed)
            return;

        // A bin on a page kept behind another (PageDeck) is still in the tree, and where two views show
        // one page model - the vault on the left and on the right - it is armed with its twin. Unseen,
        // it took the confirming click on the twin for a press elsewhere and disarmed both: the vault's
        // bin armed again and again and nothing was ever deleted.
        if (!IsEffectivelyVisible)
            return;

        if (e.Source is Visual pressed && (pressed == this || this.IsVisualAncestorOf(pressed)))
            return;

        SetCurrentValue(IsArmedProperty, false);
    }
}
