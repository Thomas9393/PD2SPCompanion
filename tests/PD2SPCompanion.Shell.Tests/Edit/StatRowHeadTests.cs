using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.CustomControls.Base;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A property row's head drawn by one control (StatRowHead).
/// It was laid over the row's own controls in the
/// window, picture by picture, the day it was written; what stays to be held here is what a picture does not
/// say: UI Automation is told the texts the text blocks told it - the window tests find a row by its name and
/// read its line - the head is no control but itself, it is laid out again when its row changes, and it lets
/// its row go when it leaves the window.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class StatRowHeadTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    private static void Pump(int rounds = 4)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }
    }

    private static Avalonia.Controls.Window Shown(Control content, double width)
    {
        var window = new Avalonia.Controls.Window
        {
            Width = 500, Height = 300, Background = Brushes.Black,
            Content = new Border
            {
                Width = width, Child = content,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            },
        };
        window.Show();
        Pump();
        return window;
    }

    private static List<string> Told(StatRowHead head) =>
        [.. ControlAutomationPeer.CreatePeerForElement(head).GetChildren().Select(peer => peer.GetName())];

    [CorpusFact]
    public void The_head_tells_the_texts_its_text_blocks_told_and_is_one_control()
    {
        if (!program.Ready())
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            program.Select("Steel Ring of Skill", "Personal Stash");
            try
            {
                var row = Edit.Stats.First(each => each.HasRoll && !each.IsQuestionable);
                var head = new StatRowHead { Row = row, EditsProperties = true };
                var window = Shown(head, 400);
                try
                {
                    var told = Told(head);
                    Assert.Equal(row.HasInGame ? row.InGame : row.Name, told[0]);
                    Assert.Equal(row.Detail, told[1]);
                    Assert.Contains(row.RollText, told);
                    Assert.Empty(head.GetVisualDescendants());
                    Assert.True(head.Bounds.Height > 20 && head.Bounds.Width > 20, $"the head took {head.Bounds.Size}");

                    // Where the play mode edits no properties, what a roll would be is not said.
                    head.EditsProperties = false;
                    Pump();
                    Assert.DoesNotContain(row.RollText, Told(head));
                }
                finally
                {
                    window.Close();
                }
            }
            finally
            {
                program.Deselect();
            }
        });
    }

    [CorpusFact]
    public void The_head_is_laid_out_again_when_its_row_changes_and_lets_the_row_go_when_it_leaves()
    {
        if (!program.Ready())
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            program.Select("Steel Ring of Skill", "Personal Stash");
            try
            {
                var row = Edit.Stats.First(each => each.CanDelete);
                var head = new StatRowHead { Row = row, EditsProperties = true };
                var window = Shown(head, 400);
                try
                {
                    Assert.True(head.IsMeasureValid);

                    // Taken off: every text is struck through, so the head is laid out again.
                    row.DeleteCommand.Execute(null);
                    Assert.True(row.IsDeleted);
                    Assert.False(head.IsMeasureValid);
                    Pump();
                    Assert.True(head.IsMeasureValid);

                    row.RestoreCommand.Execute(null);
                    Assert.False(head.IsMeasureValid);
                    Pump();
                }
                finally
                {
                    window.Close();
                    Pump();
                }

                // Out of the window it listens to no row: a row's change does not reach it.
                head.Measure(new Size(400, double.PositiveInfinity));
                Assert.True(head.IsMeasureValid);
                row.DeleteCommand.Execute(null);
                Assert.True(head.IsMeasureValid);
                row.RestoreCommand.Execute(null);
            }
            finally
            {
                program.Deselect();
            }
        });
    }
}
