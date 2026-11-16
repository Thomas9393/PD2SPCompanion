using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Settings_R : UserControl
{
    public VP_View_Settings_R()
    {
        InitializeComponent();
    }

    /// <summary>
    /// A name box takes the focus and its whole text the moment it appears. The boxes stay in the tree,
    /// hidden, until a name is asked for - a new vault's, a row's being renamed - so each is watched for
    /// being shown: attached, it is not yet there to take anything (the typing went to the row behind it).
    /// </summary>
    private void Box_Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        box.PropertyChanged -= Box_Shown;
        box.PropertyChanged += Box_Shown;
        TakeFocus(box);
    }

    private static void Box_Shown(object? sender, AvaloniaPropertyChangedEventArgs changed)
    {
        if (sender is TextBox box && changed.Property == IsVisibleProperty)
            TakeFocus(box);
    }

    /// <summary>Once it is laid out: shown this instant, it has no place on screen yet to be focused in.</summary>
    private static void TakeFocus(TextBox box) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (box.IsEffectivelyVisible && !box.IsFocused)
            {
                box.Focus();
                box.SelectAll();
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);

    /// <summary>Enter makes the vault, Escape lets it go - the vault's folder naming.</summary>
    private void NewName_KeyDown(object? sender, KeyEventArgs e)
    {
        if ((DataContext as VP_ViewModel_Settings_R)?.Vaults is not { } vaults)
            return;

        if (e.Key is Key.Enter or Key.Return)
            vaults.CommitNew();
        else if (e.Key == Key.Escape)
            vaults.CancelNew();
        else
            return;

        e.Handled = true;
    }

    private void Rename_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: VaultListRow row }
            || (DataContext as VP_ViewModel_Settings_R)?.Vaults is not { } vaults)
        {
            return;
        }

        if (e.Key is Key.Enter or Key.Return)
            vaults.CommitRename(row);
        else if (e.Key == Key.Escape)
            vaults.CancelRename(row);
        else
            return;

        e.Handled = true;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is VP_ViewModel_Settings_R model)
            Avalonia.Threading.Dispatcher.UIThread.Post(
                model.ShowVault, Avalonia.Threading.DispatcherPriority.Background);
    }
}
