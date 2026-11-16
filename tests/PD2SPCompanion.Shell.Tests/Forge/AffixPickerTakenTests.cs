using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// The affix picker keeps its search across slots: a user may not know which one the stat is in, and would type it
/// again and again. A pick on one stat still blocks the other slots from it, but notifies: the group unavailable
/// because another slot has picked one is greyed, not hidden.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class AffixPickerTakenTests(Program program)
{
    [CorpusFact]
    public void Another_slots_group_is_greyed_with_why_and_the_search_stays_and_points_to_the_other_side()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("war sword", "War Sword");
            bench.Rarity("Rare");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

        program.On(() =>
        {
            AffixEditor Affixes() => bench.Panel.Affixes;

            // A rare with two prefixes at least, so one slot's group stands in the other's list.
            for (int made = 0; made < 60 && Affixes().StagedPairs.Count(pair => pair.PrefixName.Length > 0) < 2; made++)
            {
                if (!bench.Forge.ResetCommand.CanExecute(null))
                    bench.Panel.RerollItemCommand.Execute(null);
                bench.Forge.ResetCommand.Execute(null);
            }

            var affixes = Affixes();
            affixes.OpenAffixPickerCommand.Execute(null);
            try
            {
                var pairs = affixes.StagedPairs.Where(pair => pair.PrefixName.Length > 0).ToList();
                Assert.True(pairs.Count >= 2);

                // Aimed at the second prefix: the first one's group is listed, greyed, saying why.
                affixes.AimAffixCommand.Execute($"{pairs[1].Slot}:prefix");
                string firstName = pairs[0].PrefixName;
                var taken = affixes.AffixChoices.Where(row => row.IsTaken).ToList();
                Assert.Contains(taken, row => row.Name == firstName);
                Assert.All(taken, row => Assert.False(row.CanPick));
                Assert.All(taken, row => Assert.Equal("in another slot", row.LevelNote));

                // A suffix's name typed on the prefix side: nothing here, and it says the suffixes have it.
                string suffix = affixes.StagedPairs.First(pair => pair.SuffixName.Length > 0).SuffixName;
                affixes.AffixSearch = suffix;
                Assert.True(affixes.NoAffixChoices);
                Assert.Equal("No prefix matches. Suffixes do.", affixes.NoChoicesNote);

                // To a suffix slot: the search stays, and finds it.
                affixes.AimAffixCommand.Execute("0:suffix");
                Assert.Equal(suffix, affixes.AffixSearch);
                Assert.Contains(affixes.AffixChoices, row => row.Name == suffix);
            }
            finally
            {
                affixes.CloseAffixPickerCommand.Execute(null);
            }
        });
    }
}
