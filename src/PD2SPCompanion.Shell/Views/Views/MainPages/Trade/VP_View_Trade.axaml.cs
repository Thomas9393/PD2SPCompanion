using Avalonia;
using Avalonia.Controls;
using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Trade : UserControl, IShownPage
{
    public VP_View_Trade()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        OnShown();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        OnHidden();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>In front (PageDeck), or attached the first time: the Sell tab catches up with the inventory.</summary>
    public void OnShown() => (DataContext as VP_ViewModel_Trade)?.Shown();

    public void OnHidden() => (DataContext as VP_ViewModel_Trade)?.Hidden();
}
