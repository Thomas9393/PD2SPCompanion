using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;

using PD2SPCompanion.Shell.CustomControls.Base;

namespace PD2SPCompanion.Shell.Tests.Character;

/// <summary>
/// An attribute's figure (AttributeFigure: rows of two kinds - with a part
/// from the gear and without - each as wide as the longest figure of its kind and no wider, shorter points
/// against the longest's right end, the gear's signs in a line: "option A, + line up"). The widths; that it
/// reads as one text is what UI Automation is told.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class AttributeFigureTests(Program program)
{
    private static AttributeFigure Figure(string points, string sign = "", string number = "",
                                          string[]? pointsOfAll = null, string[]? gearOfAll = null) =>
        new()
        {
            Points = points, GearSign = sign, GearNumber = number, PointsOfAll = pointsOfAll, GearOfAll = gearOfAll,
            FontSize = 20, FontWeight = FontWeight.Bold,
        };

    /// <summary>Figures laid out, each as wide as it asks to be.</summary>
    private static Avalonia.Controls.Window Shown(params AttributeFigure[] figures)
    {
        var stack = new StackPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        foreach (var figure in figures)
        {
            figure.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            stack.Children.Add(figure);
        }

        var window = new Avalonia.Controls.Window { Width = 400, Height = 300, Background = Brushes.Black, Content = stack };
        window.Show();
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        }

        return window;
    }

    [CorpusFact]
    public void A_figure_is_as_wide_as_the_longest_of_its_kind_and_no_wider()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            // Rows of two digits: two digits wide, each of them.
            string[] twos = ["30", "20"];
            AttributeFigure thirty = Figure("30", pointsOfAll: twos), twenty = Figure("20", pointsOfAll: twos);

            // One row at 120: every row of the kind is three digits wide.
            string[] mixed = ["30", "120"];
            AttributeFigure short3 = Figure("30", pointsOfAll: mixed), long3 = Figure("120", pointsOfAll: mixed);
            AttributeFigure alone = Figure("120");

            // With the gear's part: the longest points, the sign, the longest part - on each row of the kind.
            string[] points = ["125", "10"], gear = ["54", "6"];
            AttributeFigure big = Figure("125", "+", "54", points, gear), small = Figure("10", "+", "6", points, gear);
            AttributeFigure tight = Figure("10", "+", "6");

            var window = Shown(thirty, twenty, short3, long3, alone, big, small, tight);
            try
            {
                Assert.True(thirty.Bounds.Width > 0);
                Assert.Equal(thirty.Bounds.Width, twenty.Bounds.Width);

                Assert.Equal(alone.Bounds.Width, long3.Bounds.Width);
                Assert.Equal(long3.Bounds.Width, short3.Bounds.Width);
                Assert.True(long3.Bounds.Width > thirty.Bounds.Width, "three digits took no more room than two");

                Assert.Equal(big.Bounds.Width, small.Bounds.Width);
                Assert.True(big.Bounds.Width > long3.Bounds.Width);

                // No wider than what the rows show: the same row by itself is narrower.
                Assert.True(tight.Bounds.Width < small.Bounds.Width);

                // One line high, with the gear's part or without.
                Assert.Equal(thirty.Bounds.Height, big.Bounds.Height);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [CorpusFact]
    public void It_reads_as_one_text()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            AttributeFigure plain = Figure("30"), with = Figure("25", "+", "54");
            var window = Shown(plain, with);
            try
            {
                Assert.Equal("30", ControlAutomationPeer.CreatePeerForElement(plain).GetName());
                Assert.Equal("25 +54", ControlAutomationPeer.CreatePeerForElement(with).GetName());
                Assert.Equal(AutomationControlType.Text, ControlAutomationPeer.CreatePeerForElement(with).GetAutomationControlType());
            }
            finally
            {
                window.Close();
            }
        });
    }
}
