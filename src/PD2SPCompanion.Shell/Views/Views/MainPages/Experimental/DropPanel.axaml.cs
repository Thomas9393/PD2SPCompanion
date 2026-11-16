using Avalonia.Controls;
using Avalonia.Input;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Views;

public partial class DropPanel : UserControl
{
    public DropPanel()
    {
        InitializeComponent();

        this.OnModifiedPress(StepRate);
    }

    /// <summary>
    /// A multiple's stepper with a modifier, as every stepper takes them (the key sheet says
    /// so): Ctrl ten times the multiple or a tenth, Shift the end of the scale. A row's, or
    /// its group's for every row of it. A plain click is the button's command, one step of the scale.
    /// </summary>
    private static bool StepRate(Button button, KeyModifiers modifiers)
    {
        if (button.Tag is not ("rate up" or "rate down"))
            return false;

        bool up = button.Tag as string == "rate up";
        bool shift = modifiers.HasFlag(KeyModifiers.Shift), ctrl = modifiers.HasFlag(KeyModifiers.Control);
        if (!shift && !ctrl)
            return false;

        switch (button.DataContext)
        {
            case DropRowViewModel row when shift: row.Jump(up); break;
            case DropRowViewModel row: row.Leap(up); break;
            case DropGroupViewModel group when shift: group.Jump(up); break;
            case DropGroupViewModel group: group.Leap(up); break;
            default: return false;
        }

        return true;
    }
}
