using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Char_R : UserControl, IShownPage
{
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        OnHidden();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>In front (PageDeck), or shown the first time: the cards catch up with the session.</summary>
    public void OnShown() => (DataContext as VP_ViewModel_Char_R)?.Shown();

    public void OnHidden() => (DataContext as VP_ViewModel_Char_R)?.Hidden();

    public VP_View_Char_R()
    {
        InitializeComponent();

        this.OnModifiedPress(Spend);
    }

    // The points-to-spend boxes apply what was typed on Enter or on leaving them, as the level's box -
    // the attributes' (the left page's model) and the skills' alike.
    private void UnspentBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            Commit(sender);
            e.Handled = true;
        }
    }

    private void UnspentBox_LostFocus(object? sender, RoutedEventArgs e) => Commit(sender);

    private static void Commit(object? sender)
    {
        switch ((sender as Control)?.DataContext)
        {
            case VP_ViewModel_Char_L attributes: attributes.CommitUnspent(); break;
            case VP_ViewModel_Char_R skills: skills.CommitUnspent(); break;
        }
    }

    /// <summary>
    /// Every stepper on this page takes Ctrl and Shift for 5 and 20 points: the attributes, both points
    /// to spend, and each skill (a skill's Shift was "all it takes"). Both together
    /// are every point, on a stat and a skill - PD2's own Ctrl + Shift.
    /// </summary>
    private bool Spend(Button button, KeyModifiers modifiers)
    {
        int points = modifiers.HasFlag(KeyModifiers.Shift) ? AttributeRowViewModel.ManyMorePoints
                     : modifiers.HasFlag(KeyModifiers.Control) ? AttributeRowViewModel.ManyPoints
                     : 1;

        if (points == 1)
            return false;

        int spent = VP_View_Char_L.Every(modifiers) ? AttributeRowViewModel.EveryPoint : points;

        // The attributes, shown here above the skills: their rows and their points to spend.
        if (button.DataContext is AttributeRowViewModel row && button.Tag is "invest" or "refund")
        {
            if (button.Tag as string == "invest")
                row.Invest(spent);
            else
                row.Refund(spent);
            return true;
        }

        if (button.DataContext is SkillCellViewModel cell && button.Tag is "skill in" or "skill out"
            && DataContext is VP_ViewModel_Char_R skills)
        {
            if (button.Tag as string == "skill in")
                skills.AddPoints(cell, spent);
            else
                skills.RemovePoints(cell, spent);
            return true;
        }

        if (button.DataContext is VP_ViewModel_Char_L stats && button.Tag is "add points" or "remove points")
        {
            stats.ChangeUnspent(button.Tag as string == "add points" ? spent : -spent);
            return true;
        }

        if (button.DataContext is not VP_ViewModel_Char_R page
            || button.Tag is not ("add points" or "remove points"))
        {
            return false;
        }

        page.ChangeUnspent(button.Tag as string == "add points" ? spent : -spent);
        return true;
    }
}
