using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;
using PD2SPCompanion.Shell.Views;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The item holder's grip on the Edit page and the Forge, for greater height (at most 45% of it in the first version):
/// dragged down, the
/// item's lines have more room, never past 45% of the page; double-clicked, the holder is its own height again.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SummaryGripTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    private static void Pump()
    {
        for (int i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }
    }

    [CorpusFact]
    public void The_grip_gives_the_lines_room_up_to_45_percent_and_a_double_click_puts_it_back()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            program.Panes("Personal Stash");
            program.Select("Demon Limb", "Personal Stash");
            var page = new VP_View_Inv_R { DataContext = Edit };
            var window = new Avalonia.Controls.Window { Width = 700, Height = 1000, Background = Brushes.Black, Content = page };
            window.Show();
            Pump();
            try
            {
                var holder = page.GetVisualDescendants().OfType<Border>().First(each => each.Name == "Holder");
                var grip = page.GetVisualDescendants().OfType<Border>().First(each => each.Name == "SummaryGrip");
                var lines = page.GetVisualDescendants().OfType<ItemFactLines>().First();
                var viewport = lines.FindAncestorOfType<ScrollViewer>()!;
                double was = viewport.Bounds.Height, holderWas = holder.Bounds.Height;

                var at = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
                void Drag(double by)
                {
                    window.MouseDown(at, MouseButton.Left);
                    window.MouseMove(at + new Point(0, by), RawInputModifiers.LeftMouseButton);
                    window.MouseUp(at + new Point(0, by), MouseButton.Left);
                    Pump();
                }

                Drag(120);
                Assert.Equal(was + 120, viewport.Bounds.Height, 1);

                // Never past 45% of the page, however far it is dragged.
                at = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
                Drag(900);
                Assert.True(holder.Bounds.Height <= page.Bounds.Height * 0.45 + 1, $"the holder is {holder.Bounds.Height} of {page.Bounds.Height}");
                Assert.True(holder.Bounds.Height > holderWas + 120);

                // Double-clicked: its own height.
                at = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
                Pump();
                Assert.Equal(holderWas, holder.Bounds.Height, 1);
            }
            finally
            {
                window.Close();
                program.Deselect();
            }
        });
    }
}
