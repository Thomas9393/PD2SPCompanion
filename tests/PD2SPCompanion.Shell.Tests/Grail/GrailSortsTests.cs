using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Grail;

/// <summary>
/// The two grails keep their own sort: Count chosen on the Items page's grail is not
/// carried to the Extra page's, which has no Count and fell back to Type; each keeps what it was given, under
/// a name of its own in the settings. All of it the grails' own deciding: nothing is left to a UI case.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GrailSortsTests(Program program)
{
    private static void Pick(SortChoice order, string mode) => order.Pick(order.Options.First(option => option.Label == mode));

    [CorpusFact]
    public void Count_chosen_on_the_Items_pages_grail_is_not_the_Extra_pages_and_each_keeps_what_it_was_given()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var items = GrailHands.OfItems(program);
            var extra = GrailHands.OfExtra(program);
            items.ShowTabCommand.Execute(GrailViewModel.Uniques);
            extra.ShowTabCommand.Execute(GrailViewModel.Uniques);

            var settings = program.Get<EditorSettings>();
            var was = (items.Order.By, items.Order.Descending, extra.Order.By, extra.Order.Descending);
            try
            {
                Pick(items.Order, "Count");
                Assert.Equal("Count", items.Order.By);

                // The Extra page's grail counts nothing, and has no Count to be given.
                Assert.DoesNotContain("Count", extra.Order.Modes);
                Assert.NotEqual("Count", extra.Order.By);
                Assert.DoesNotContain("Count", extra.Order.Hint);

                Pick(extra.Order, "Name");
                Assert.Equal("Name", extra.Order.By);
                Assert.StartsWith("Sort" + Environment.NewLine + "Name ", extra.Order.Hint);

                Assert.Equal("Count", items.Order.By);
                Assert.StartsWith("Sort" + Environment.NewLine + "Count ", items.Order.Hint);

                // Kept apart, for the next time the program is opened.
                Assert.Equal("Count", settings.SortFor("Grail").By);
                Assert.Equal("Name", settings.SortFor("GrailBrowse").By);
            }
            finally
            {
                (items.Order.By, items.Order.Descending) = (was.Item1, was.Item2);
                (extra.Order.By, extra.Order.Descending) = (was.Item3, was.Item4);
            }
        });
    }
}
