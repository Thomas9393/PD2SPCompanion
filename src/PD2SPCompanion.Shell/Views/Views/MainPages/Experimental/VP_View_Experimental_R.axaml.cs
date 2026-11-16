using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Experimental_R : UserControl
{
    public VP_View_Experimental_R()
    {
        InitializeComponent();

        this.OnModifiedPress(StepLevel);
    }

    /// <summary>
    /// Character Creation's level stepper with a modifier, as the Character page's: Ctrl five levels, Shift twenty,
    /// both straight to level 1 or 99. A plain click
    /// is the button's command, one level.
    /// </summary>
    private static bool StepLevel(Button button, KeyModifiers modifiers)
    {
        if (button.DataContext is not CharacterCreationViewModel creation || button.Tag is not ("level up" or "level down"))
            return false;

        bool up = button.Tag as string == "level up";
        bool ctrl = modifiers.HasFlag(KeyModifiers.Control), shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (ctrl && shift)
            creation.LevelToEnd(up);
        else if (shift)
            creation.StepLevel(up ? AttributeRowViewModel.ManyMorePoints : -AttributeRowViewModel.ManyMorePoints);
        else if (ctrl)
            creation.StepLevel(up ? AttributeRowViewModel.ManyPoints : -AttributeRowViewModel.ManyPoints);
        else
            return false;
        return true;
    }
}
