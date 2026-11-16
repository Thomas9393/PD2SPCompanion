using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Edit;

/// <summary>
/// The affix picker lists its choices as the PD2 wiki does, where a +10 fcr and a +20 fcr
/// do not lie across the long list: by the
/// table's group - the stat, every tier of it together - then in the table's order, which rises with the level.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class AffixOrderTests(Program program)
{
    private VP_ViewModel_Inv_R Edit => program.EditPage();

    [CorpusFact]
    public void Every_group_stands_together_and_its_tiers_rise()
    {
        if (!program.Ready())
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            program.Select("Steel Ring of Skill", "Personal Stash");
            var affixes = Edit.Affixes;
            affixes.OpenAffixPickerCommand.Execute(null);
            try
            {
                foreach (string side in new[] { "prefix", "suffix" })
                {
                    affixes.AimAffixCommand.Execute($"0:{side}");
                    var rows = affixes.AffixChoices.Select(row => row.Option).ToList();
                    Assert.True(rows.Count > 20, $"{side}: {rows.Count} choices");

                    // Each group in one run, never met again after another.
                    var seen = new HashSet<string>();
                    string? last = null;
                    foreach (var option in rows)
                    {
                        if (option.Group != last)
                        {
                            Assert.True(seen.Add(option.Group), $"{side}: group {option.Group} comes twice ({option.Name})");
                            last = option.Group;
                        }
                    }

                    // And within one, in the table's order.
                    foreach (var group in rows.GroupBy(option => option.Group))
                        Assert.Equal(group.Select(option => option.Id).Order(), group.Select(option => option.Id));
                }
            }
            finally
            {
                affixes.CloseAffixPickerCommand.Execute(null);
                program.Deselect();
            }
        });
    }

    /// <summary>
    /// A new list is every row built again by the view, so the list is the same one while its rows are the same
    /// - and stands as it was once the picker is shut, when it was built again behind the fading panel and on
    /// every item selected after.
    /// </summary>
    [CorpusFact]
    public void The_list_is_the_same_one_while_its_rows_are_and_stands_while_the_picker_is_shut()
    {
        if (!program.Ready())
            return;

        using var free = program.InMode("Free");
        program.On(() =>
        {
            program.Panes("Personal Stash");
            program.Select("Steel Ring of Skill", "Personal Stash");
            var affixes = Edit.Affixes;
            affixes.OpenAffixPickerCommand.Execute(null);
            try
            {
                var prefixes = affixes.AffixChoices;
                Assert.NotEmpty(prefixes);

                // Asked again, told again: the same list.
                affixes.Notify();
                Assert.Same(prefixes, affixes.AffixChoices);

                // Another side is another list, and back again its rows are as they were.
                affixes.AimAffixCommand.Execute("0:suffix");
                var suffixes = affixes.AffixChoices;
                Assert.NotSame(prefixes, suffixes);
                Assert.NotEqual(prefixes.Select(row => row.Option.Id), suffixes.Select(row => row.Option.Id));

                // Shut: the list last shown, whatever is told.
                affixes.CloseAffixPickerCommand.Execute(null);
                Assert.Same(suffixes, affixes.AffixChoices);
                affixes.Notify();
                Assert.Same(suffixes, affixes.AffixChoices);

                // Opened again it is the first slot's prefixes, as every open is.
                affixes.OpenAffixPickerCommand.Execute(null);
                Assert.Equal(prefixes.Select(row => row.Option.Id), affixes.AffixChoices.Select(row => row.Option.Id));
            }
            finally
            {
                affixes.CloseAffixPickerCommand.Execute(null);
                program.Deselect();
            }
        });
    }
}
