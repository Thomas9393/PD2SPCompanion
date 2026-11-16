using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// The materials tab: blocks of <see cref="ContainerGrid"/> placed as the game places them. The
/// four properties are passed straight through to every block, so a click on the tab reaches
/// the same commands a click on any stash grid does.
/// </summary>
public partial class MaterialsTab : UserControl
{
    public static readonly StyledProperty<ICommand?> ClickTileCommandProperty =
        AvaloniaProperty.Register<MaterialsTab, ICommand?>(nameof(ClickTileCommand));

    public static readonly StyledProperty<ICommand?> PlaceAtCommandProperty =
        AvaloniaProperty.Register<MaterialsTab, ICommand?>(nameof(PlaceAtCommand));

    public static readonly StyledProperty<bool> IsHoldingProperty =
        AvaloniaProperty.Register<MaterialsTab, bool>(nameof(IsHolding));

    public static readonly StyledProperty<double> SocketOpacityProperty =
        AvaloniaProperty.Register<MaterialsTab, double>(nameof(SocketOpacity));

    public ICommand? ClickTileCommand
    {
        get => GetValue(ClickTileCommandProperty);
        set => SetValue(ClickTileCommandProperty, value);
    }

    public ICommand? PlaceAtCommand
    {
        get => GetValue(PlaceAtCommandProperty);
        set => SetValue(PlaceAtCommandProperty, value);
    }

    public bool IsHolding
    {
        get => GetValue(IsHoldingProperty);
        set => SetValue(IsHoldingProperty, value);
    }

    public double SocketOpacity
    {
        get => GetValue(SocketOpacityProperty);
        set => SetValue(SocketOpacityProperty, value);
    }

    public MaterialsTab() => InitializeComponent();
}
