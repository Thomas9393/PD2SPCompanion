using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Char_L : UserControl, IShownPage
{
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        OnHidden();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>In front (PageDeck), or shown the first time: the cards catch up with the session.</summary>
    public void OnShown() => (DataContext as VP_ViewModel_Char_L)?.Shown();

    public void OnHidden() => (DataContext as VP_ViewModel_Char_L)?.Hidden();

    public VP_View_Char_L()
    {
        InitializeComponent();

        this.OnModifiedPress(Spend);
        this.OnModifiedPress(WaypointsReach);
    }

    /// <summary>
    /// The waypoints' Grant and Remove reach further with a modifier held: Ctrl every act of the difficulty,
    /// Shift every difficulty (Shift beats Ctrl). Moved here with the card from the Extra page, where it
    /// stayed behind when the waypoints went to the Character page's panel and so never ran.
    /// </summary>
    private static bool WaypointsReach(Button button, KeyModifiers modifiers)
    {
        if (button.DataContext is not WaypointsViewModel waypoints)
            return false;

        int scope = modifiers.HasFlag(KeyModifiers.Shift) ? WaypointsViewModel.ScopeEverything
                    : modifiers.HasFlag(KeyModifiers.Control) ? WaypointsViewModel.ScopeDifficulty
                    : WaypointsViewModel.ScopeAct;
        if (scope == WaypointsViewModel.ScopeAct)
            return false;   // A plain click is the button's own command.

        if (button.Classes.Contains("enable"))
            waypoints.EnableAll(scope);
        else if (button.Classes.Contains("disable"))
            waypoints.RemoveAll(scope);
        else
            return false;

        return true;
    }

    // The map seed's box applies on Enter or on leaving it; Enter in the name box is Keep's key binding.
    private void SeedBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && (sender as Control)?.DataContext is MapSeedViewModel seed)
        {
            seed.CommitSeed();
            e.Handled = true;
        }
    }

    private void SeedBox_LostFocus(object? sender, RoutedEventArgs e) =>
        ((sender as Control)?.DataContext as MapSeedViewModel)?.CommitSeed();

    /// <summary>
    /// The level's steppers, five or twenty levels at a time. The attributes' steppers, their points to spend and
    /// Refit stand on the right page, and are read there (VP_View_Char_R).
    /// </summary>
    private static bool Spend(Button button, KeyModifiers modifiers)
    {
        int points = Points(modifiers);
        if (points == 1)
            return false;   // Nothing this page reads: leave it to the button command.

        if (!button.Classes.Contains("spend"))
            return false;

        // Both keys: every point, every level - as far as the stepper goes (the level and the
        // points to spend took them as Shift's twenty, a stat's and a skill's as all).
        if (Every(modifiers))
            points = AttributeRowViewModel.EveryPoint;

        // The level's steppers: five or twenty levels at a time, as far as the points allow.
        if (button.DataContext is VP_ViewModel_Char_L levels && button.Tag is "level up" or "level down")
        {
            levels.StepLevel(button.Tag as string == "level up" ? points : -points);
            return true;
        }

        return false;
    }

    // The level and experience boxes apply what was typed on Enter, or on leaving the box -
    // not on every digit, which would pass through level 3 on the way to 30.
    private void LevelBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && DataContext is VP_ViewModel_Char_L page)
        {
            page.CommitLevel();
            e.Handled = true;
        }
    }

    private void LevelBox_LostFocus(object? sender, RoutedEventArgs e) =>
        (DataContext as VP_ViewModel_Char_L)?.CommitLevel();

    private void ExperienceBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && DataContext is VP_ViewModel_Char_L page)
        {
            page.CommitExperience();
            e.Handled = true;
        }
    }

    private void ExperienceBox_LostFocus(object? sender, RoutedEventArgs e) =>
        (DataContext as VP_ViewModel_Char_L)?.CommitExperience();

    /// <summary>Ctrl and Shift together: every point, where a stepper has an "all" (a stat, a skill).</summary>
    internal static bool Every(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Shift) && modifiers.HasFlag(KeyModifiers.Control);

    /// <summary>Shift beats Control; both held is every point (<see cref="Every"/>), worked out by the caller.</summary>
    private static int Points(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Shift) ? AttributeRowViewModel.ManyMorePoints
        : modifiers.HasFlag(KeyModifiers.Control) ? AttributeRowViewModel.ManyPoints
        : 1;
}
