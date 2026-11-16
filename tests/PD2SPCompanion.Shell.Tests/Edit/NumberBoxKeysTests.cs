using Avalonia.Input;

using PD2SPCompanion.Shell.CustomControls.Base;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The whole-number box under the keys: Up and Down on a range whose minimum is over its maximum (a vault item with more
/// filled sockets than its base takes, until the box took in what the item holds) ended the program, and a minus over the
/// number a click or Tab selects was refused, so "-25" typed over it went in as 25.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class NumberBoxKeysTests(Program program)
{
    private static string Press(NumericTextBox box, Key key)
    {
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        return box.Text ?? "";
    }

    private static string Type(NumericTextBox box, string typed)
    {
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = typed });
        return box.Text ?? "";
    }

    [CorpusFact]
    public void A_range_upside_down_is_no_crash_on_either_kind_of_box()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var whole = new NumericTextBox { Minimum = 5, Maximum = 3, Text = "3" };
            Assert.Equal("3", Press(whole, Key.Up));
            Assert.Equal("3", Press(whole, Key.PageDown));

            var fraction = new NumericTextBox { Decimals = 3, Minimum = 5, Maximum = 3, Text = "3" };
            Assert.Equal("3", Press(fraction, Key.Up));
        });
    }

    [CorpusFact]
    public void A_minus_takes_the_place_of_the_number_selected()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var box = new NumericTextBox { Minimum = -100, Maximum = 100, Text = "10" };
            box.SelectAll();
            Assert.Equal("-", Type(box, "-"));
            Assert.Equal("-2", Type(box, "2"));
            Assert.Equal("-25", Type(box, "5"));

            // A number that is negative already, selected whole.
            box.SelectAll();
            Assert.Equal("-", Type(box, "-"));
        });
    }

    [CorpusFact]
    public void A_minus_stays_out_where_it_would_not_stand_first_or_a_second_would_follow()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var box = new NumericTextBox { Minimum = -100, Maximum = 100, Text = "-5" };

            // In front of a minus.
            box.SelectionStart = box.SelectionEnd = 0;
            box.CaretIndex = 0;
            Assert.Equal("-5", Type(box, "-"));

            // After a digit.
            box.Text = "15";
            box.CaretIndex = 2;
            box.SelectionStart = box.SelectionEnd = 2;
            Assert.Equal("15", Type(box, "-"));
        });
    }

    [CorpusFact]
    public void A_range_that_stops_at_nought_takes_no_minus_with_the_number_selected()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var box = new NumericTextBox { Minimum = 0, Maximum = 100, Text = "10" };
            box.SelectAll();
            Assert.Equal("10", Type(box, "-"));

            // Unbounded below - Free's field that has no row - takes one.
            var free = new NumericTextBox { Text = "10" };
            free.SelectAll();
            Assert.Equal("-", Type(free, "-"));
        });
    }
}
