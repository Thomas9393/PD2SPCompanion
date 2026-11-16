using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;

using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// What the keys do, a line a key - the key as a cap, what it does beside it, the caps in one column
/// (KeyHints.axaml). Shown by an item's tooltip at its foot and by every tooltip that lists keys.
/// </summary>
public partial class KeyHints : UserControl
{
    /// <summary>The lines, top to bottom.</summary>
    public static readonly StyledProperty<IReadOnlyList<TipHint>?> HintsProperty =
        AvaloniaProperty.Register<KeyHints, IReadOnlyList<TipHint>?>(nameof(Hints));

    public IReadOnlyList<TipHint>? Hints
    {
        get => GetValue(HintsProperty);
        set => SetValue(HintsProperty, value);
    }

    /// <summary>
    /// The lines side by side on one, a gap between them, where there is the width and not the height for a
    /// strip: under a page's notice (two columns instead of two rows).
    /// </summary>
    public static readonly StyledProperty<bool> AcrossProperty =
        AvaloniaProperty.Register<KeyHints, bool>(nameof(Across));

    public bool Across
    {
        get => GetValue(AcrossProperty);
        set => SetValue(AcrossProperty, value);
    }

    /// <summary>
    /// The least width of the keys' column: where several of these stand under one another - the groups of the
    /// sheet of keys - what each key does begins on one line down all of them, not on a line of each group's
    /// own (the rule for a strip, kept across strips). None by default: as wide as its keys.
    /// </summary>
    public static readonly StyledProperty<double> KeysWidthProperty =
        AvaloniaProperty.Register<KeyHints, double>(nameof(KeysWidth));

    public double KeysWidth
    {
        get => GetValue(KeysWidthProperty);
        set => SetValue(KeysWidthProperty, value);
    }

    public KeyHints()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HintsProperty)
            Lines.ItemsSource = Hints;

        if (change.Property == AcrossProperty)
        {
            Lines.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => Across
                ? new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 22 }
                : new StackPanel());
        }
    }
}
