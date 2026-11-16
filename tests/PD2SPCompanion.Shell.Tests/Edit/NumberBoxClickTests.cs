using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

using PD2SPCompanion.Shell.CustomControls.Base;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A click into a number box selects its number whole, so what is typed next replaces it, and a selection is
/// drawn in the app's gold with dark letters, not the framework's blue - the box was clunky
/// to use.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class NumberBoxClickTests(Program program)
{
    [CorpusFact]
    public void A_click_into_a_number_box_selects_the_number_and_a_second_click_places_the_caret()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var box = new NumericTextBox { Text = "120", Width = 120, Height = 30 };
            var other = new NumericTextBox { Text = "7", Width = 120, Height = 30 };
            var window = new Avalonia.Controls.Window
            {
                Width = 400, Height = 200,
                Content = new Avalonia.Controls.StackPanel { Children = { other, box } },
            };
            window.Show();
            try
            {
                other.Focus();
                Dispatcher.UIThread.RunJobs();

                var at = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;
                void Click()
                {
                    window.MouseDown(at, MouseButton.Left);
                    window.MouseUp(at, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                }

                Click();
                Assert.True(box.IsFocused);
                Assert.Equal((0, 3), (Math.Min(box.SelectionStart, box.SelectionEnd), Math.Max(box.SelectionStart, box.SelectionEnd)));

                // Typed over: the number replaced.
                window.KeyTextInput("9");
                Assert.Equal("9", box.Text);

                // Already being typed in: a click places the caret.
                box.Text = "120";
                box.SelectionStart = box.SelectionEnd = 0;
                Click();
                Assert.Equal(box.SelectionStart, box.SelectionEnd);

                var gold = Assert.IsAssignableFrom<ISolidColorBrush>(box.SelectionBrush);
                Assert.Equal(Color.Parse("#A59263"), gold.Color);
                var dark = Assert.IsAssignableFrom<ISolidColorBrush>(box.SelectionForegroundBrush);
                Assert.Equal(Color.Parse("#16110A"), dark.Color);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
