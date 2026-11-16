using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>An item's sockets, holes and contents. See the XAML.</summary>
public partial class SocketRow : UserControl
{
    /// <summary>One row however the item is shaped, rather than the item's own shape.</summary>
    public static readonly StyledProperty<bool> IsOneRowProperty =
        AvaloniaProperty.Register<SocketRow, bool>(nameof(IsOneRow));

    /// <summary>How large the sockets are drawn, where 1 is the grid's own cell size.</summary>
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<SocketRow, double>(nameof(Scale), 1.0);

    public bool IsOneRow
    {
        get => GetValue(IsOneRowProperty);
        set => SetValue(IsOneRowProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public SocketRow()
    {
        InitializeComponent();
        Scaler.LayoutTransform = new ScaleTransform(Scale, Scale);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Set on the transform rather than bound: a transform is not in the visual tree, so a
        // binding inside it has nothing to find its way up from.
        if (change.Property == ScaleProperty && Scaler is not null)
            Scaler.LayoutTransform = new ScaleTransform(Scale, Scale);
    }
}
