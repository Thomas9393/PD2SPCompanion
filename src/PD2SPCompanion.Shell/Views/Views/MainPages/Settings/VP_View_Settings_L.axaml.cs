using Avalonia;
using Avalonia.Controls;
using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Settings_L : UserControl, IShownPage
{
    public VP_View_Settings_L()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        OnHidden();
        base.OnDetachedFromVisualTree(e);
    }

    public void OnShown()
    {
    }

    /// <summary>Put away (PageDeck): the backups are counted on the next visit. Shown comes from the page's opening (MainViewModel).</summary>
    public void OnHidden() => (DataContext as VP_ViewModel_Settings_L)?.Hidden();
}
