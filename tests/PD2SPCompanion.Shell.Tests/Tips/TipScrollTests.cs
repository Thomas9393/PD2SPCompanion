using PD2SPCompanion.Shell.Services.Generic;

namespace PD2SPCompanion.Shell.Tests.Tips;

/// <summary>
/// The keys of a tooltip cut to the screen (TipActions.Scroll): [ and ] are a tooltip's
/// only while one that scrolls is up - a step up, a step down - and whatever they were everywhere else.
/// <para>
/// Only who has the keys is decided without a window. Where a tooltip opens - under the item, beside it where it
/// fits neither under nor over, cut to the screen where even that is not enough - that the bar shows, that the
/// strip then lists "[ ] - Scroll", and how far a press moves it, are the tooltip's own to lay out: the UI cases
/// of tip-room show them.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class TipScrollTests(Program program)
{
    [CorpusFact]
    public void The_bracket_keys_are_a_tooltips_only_while_one_that_scrolls_is_up()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            object tip = new(), other = new();
            var steps = new List<int>();
            void Stepped(int direction) => steps.Add(direction);

            TipActions.Scrolled += Stepped;
            try
            {
                // None up: the key is whatever it was.
                Assert.False(TipActions.Scroll(1));

                // Up, and whole: still not its key.
                TipActions.Opened(tip, null);
                Assert.False(TipActions.Scroll(1));

                // One that is not up says it scrolls: not heard.
                TipActions.Scrolls(other, true);
                Assert.False(TipActions.Scroll(1));
                Assert.Empty(steps);

                TipActions.Scrolls(tip, true);
                Assert.True(TipActions.Scroll(1));
                Assert.True(TipActions.Scroll(1));
                Assert.True(TipActions.Scroll(-1));
                Assert.Equal([1, 1, -1], steps);

                // Whole again - the comparison shut, say - and the keys are given back.
                TipActions.Scrolls(tip, false);
                Assert.False(TipActions.Scroll(1));

                // Gone while it scrolled: given back too.
                TipActions.Scrolls(tip, true);
                TipActions.Closed(tip);
                Assert.False(TipActions.Scroll(-1));
                Assert.Equal(3, steps.Count);
            }
            finally
            {
                TipActions.Scrolled -= Stepped;
                TipActions.Closed(tip);
            }
        });
    }
}
