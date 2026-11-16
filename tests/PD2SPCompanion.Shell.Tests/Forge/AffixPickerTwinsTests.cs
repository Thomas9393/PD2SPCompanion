using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// The affix picker, on a Paladin shield's three Monk's and three Priest's, which look like one stat many times:
/// rows that read the same are different rows of the table - by <c>ashd</c>, <c>shld</c> and the
/// armour rows - each with its own levels, so each says what tells it apart. A row read once says nothing more.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class AffixPickerTwinsTests(Program program)
{
    [CorpusFact]
    public void Rows_that_read_the_same_say_what_tells_them_apart()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("vortex shield", "Vortex Shield");
            bench.Rarity("Rare");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

        program.On(() =>
        {
            var affixes = bench.Panel.Affixes;
            affixes.OpenAffixPickerCommand.Execute(null);
            try
            {
                affixes.AimAffixCommand.Execute("0:prefix");
                var rows = affixes.AffixChoices;

                var twins = rows.Where(row => row.Option.ClassSpecific == "pal").GroupBy(row => row.Readable)
                    .Where(same => same.Count() > 1).ToList();
                Assert.NotEmpty(twins);
                foreach (var same in twins)
                {
                    Assert.All(same, row => Assert.True(row.HasTelling));
                    Assert.Equal(same.Count(), same.Select(row => row.Telling).Distinct().Count());
                }

                Assert.All(rows.GroupBy(row => row.Readable).Where(same => same.Count() == 1).SelectMany(same => same),
                    row => Assert.False(row.HasTelling));
            }
            finally
            {
                affixes.CloseAffixPickerCommand.Execute(null);
            }
        });
    }
}
