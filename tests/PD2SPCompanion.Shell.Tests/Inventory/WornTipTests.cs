using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.ViewModels;
using PD2SPCompanion.Shell.Views;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// An equipped slot's tooltip is made as it opens (VP_View_Inv_L). With the item named as the
/// slot's tip in its markup, an item changed under an open tooltip (a rune dropped into
/// the helmet the pointer was on) was handed to the tooltip as it was: the view model's type name showed
/// at the pointer. What a tooltip shows is the item's own tooltip, before the change and after.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class WornTipTests(Program program)
{
    private CharacterSession Session => program.Main.Session;

    private static void Pump()
    {
        for (int i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }
    }

    [CorpusFact]
    public void An_item_changed_under_its_open_tooltip_is_shown_as_an_item_never_as_its_model()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var page = new VP_View_Inv_L { DataContext = program.Get<VP_ViewModel_Inv_L>() };
            var window = new Avalonia.Controls.Window { Width = 900, Height = 1000, Background = Brushes.Black, Content = page };
            window.Show();
            Pump();

            var filled = Session.Equipped.Where(slot => slot.Item is not null && !slot.IsSwap).ToList();
            Assert.True(filled.Count >= 2, "two worn items are needed");
            var worn = filled[0];
            var first = worn.Item!;
            var other = filled[1].Item!;
            try
            {
                var button = page.GetVisualDescendants().OfType<Button>()
                    .First(each => ReferenceEquals(each.DataContext, worn) && each.Classes.Contains("itemtip"));

                ToolTip.SetIsOpen(button, true);
                Pump();
                Assert.True(ToolTip.GetIsOpen(button), "the tooltip did not open");
                var shown = Assert.IsType<ItemActionTip>(ToolTip.GetTip(button));
                Assert.Same(first, shown.Tile);

                // The slot's item is another, the tooltip still up - as when a rune lands in what the pointer is on.
                worn.Item = other;
                Pump();
                shown = Assert.IsType<ItemActionTip>(ToolTip.GetTip(button));
                Assert.Same(other, shown.Tile);

                // Shut and opened again it is made afresh, of the item the slot has now.
                ToolTip.SetIsOpen(button, false);
                Pump();
                ToolTip.SetIsOpen(button, true);
                Pump();
                var again = Assert.IsType<ItemActionTip>(ToolTip.GetTip(button));
                Assert.NotSame(shown, again);
                Assert.Same(other, again.Tile);
                ToolTip.SetIsOpen(button, false);
            }
            finally
            {
                worn.Item = first;
                window.Close();
                Pump();
            }
        });
    }
}
