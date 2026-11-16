using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.ViewModels;

using System.Linq;

namespace PD2SPCompanion.Shell.Views;

public partial class VP_View_Extra_L : UserControl
{
    public VP_View_Extra_L()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// A press anywhere on the page but on a recipe of the crafting wizard's chart, or on the strip that tells
    /// of the recipe picked, lets that recipe go. Looked at on the way down: nothing is taken from what was pressed.
    /// </summary>
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not VP_ViewModel_Extra_L { Tools.Crafting: { HasSelected: true } crafting }
            || e.Source is not Visual pressed)
        {
            return;
        }

        bool onRecipe = pressed.GetSelfAndVisualAncestors()
            .Any(each => each is Button { Classes: var classes } && classes.Contains("craftcell") || ReferenceEquals(each, CraftStrip));
        if (!onRecipe)
            crafting.Release();
    }
}
