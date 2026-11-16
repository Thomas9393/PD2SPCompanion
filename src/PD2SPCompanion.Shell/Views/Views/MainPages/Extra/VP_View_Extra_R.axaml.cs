using Avalonia;
using Avalonia.Controls;
using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Extra_R : UserControl, IShownPage
{
    public VP_View_Extra_R()
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

    /// <summary>Put away (PageDeck): the runes are counted on the next visit. Shown comes from the page's opening (MainViewModel), after the tables it needs.</summary>
    public void OnHidden() => (DataContext as VP_ViewModel_Extra_R)?.Hidden();
}
