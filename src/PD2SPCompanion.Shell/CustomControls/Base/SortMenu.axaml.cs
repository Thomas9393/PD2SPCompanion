using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

public partial class SortMenu : UserControl
{
    /// <summary>The button's icon. Sort's two arrows unless a filter or the like gives its own.</summary>
    public static readonly StyledProperty<Geometry> GlyphProperty =
        AvaloniaProperty.Register<SortMenu, Geometry>(nameof(Glyph),
            Geometry.Parse("M 3.5,11 L 3.5,1.5  M 1,4 L 3.5,1.5 L 6,4  M 8.5,1 L 8.5,10.5  M 6,8 L 8.5,10.5 L 11,8"));

    public Geometry Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>
    /// The page-level size - ActionButton.axaml's - for a toolbar at the top of a page, beside
    /// buttons of that size. The small one otherwise, which is what a picker's row of buttons is.
    /// </summary>
    public static readonly StyledProperty<bool> IsLargeProperty =
        AvaloniaProperty.Register<SortMenu, bool>(nameof(IsLarge));

    public bool IsLarge
    {
        get => GetValue(IsLargeProperty);
        set => SetValue(IsLargeProperty, value);
    }

    public SortMenu() => InitializeComponent();

    /// <summary>A line of the menu: apply it and close, as a menu does.</summary>
    private void Row_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SortOption option }
            || DataContext is not MenuChoice choice)
        {
            return;
        }

        choice.Pick(option);

        Opener.Flyout?.Hide();
    }
}
