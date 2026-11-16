using Avalonia.Controls;

namespace PD2SPCompanion.Shell.Views;

public partial class PresetSection : UserControl
{
    public PresetSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// How tall the list of kept presets is: fixed, with its own scroll, so nothing moves as presets are kept.
    /// 200 where the section has a page's height to stand in; less where it shares a small panel - the standing
    /// search's conditions, where 200 of the column's 500 left the form itself a strip.
    /// </summary>
    public static readonly Avalonia.StyledProperty<double> ListHeightProperty =
        Avalonia.AvaloniaProperty.Register<PresetSection, double>(nameof(ListHeight), 200);

    public double ListHeight
    {
        get => GetValue(ListHeightProperty);
        set => SetValue(ListHeightProperty, value);
    }
}
