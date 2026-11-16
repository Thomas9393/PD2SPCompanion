using Avalonia.Input;

using PD2SPCompanion.Shell.CustomControls.Base;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// A worth in HR is a number box of its own kind: the price table's and the payment's were
/// plain text boxes, which took any letter. It takes digits and one point or comma - "0,25" as sellers write it -
/// with no more places than it keeps; Up and Down step it by a tenth, PageUp and PageDown by one; and it keeps the
/// framework's inside, whose placeholder says "-" for no worth.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class DecimalBoxTests(Program program)
{
    private static string Type(NumericTextBox box, string typed)
    {
        box.CaretIndex = (box.Text ?? "").Length;
        box.SelectionStart = box.SelectionEnd = box.CaretIndex;
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = typed });
        return box.Text ?? "";
    }

    private static string Press(NumericTextBox box, Key key)
    {
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        return box.Text ?? "";
    }

    [CorpusFact]
    public void Takes_a_point_or_a_comma_once_and_no_letter()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var box = new NumericTextBox { Decimals = 3, Minimum = 0, Text = "" };
            Assert.Equal("0", Type(box, "0"));
            Assert.Equal("0,", Type(box, ","));
            Assert.Equal("0,", Type(box, "."));
            Assert.Equal("0,2", Type(box, "2"));
            Assert.Equal("0,2", Type(box, "x"));
            Assert.Equal("0,25", Type(box, "5"));
            Assert.Equal("0,255", Type(box, "5"));
            Assert.Equal("0,255", Type(box, "5"));
            Assert.Equal("0,255", Type(box, "-"));
        });
    }

    [CorpusFact]
    public void Steps_by_a_tenth_and_by_one_within_its_range()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var box = new NumericTextBox { Decimals = 3, Minimum = 0, Text = "0,25" };
            Assert.Equal("0.35", Press(box, Key.Up));
            Assert.Equal("1.35", Press(box, Key.PageUp));
            Assert.Equal("1.25", Press(box, Key.Down));
            Assert.Equal("0.25", Press(box, Key.PageDown));
            Assert.Equal("0", Press(box, Key.PageDown));
        });
    }

    [CorpusFact]
    public void Keeps_the_frameworks_inside_where_a_whole_number_box_has_the_slim_one()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.DoesNotContain(NumericTextBox.NumberClass, new NumericTextBox { Decimals = 3 }.Classes);
            Assert.Contains(NumericTextBox.NumberClass, new NumericTextBox().Classes);
        });
    }
}
