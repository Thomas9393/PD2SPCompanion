using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Search;

/// <summary>
/// Every group of the Search page folded: what was found is still found. The results and their headings used to go with
/// the rows in sight, the heading that would unfold them among them, and the fold is kept across sessions, so every search
/// after read as finding nothing. F3 has nothing to step to meanwhile.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SearchFoldTests(Program program)
{
    private QuickSearchViewModel Bar => program.Main.QuickSearch!;
    private SearchWindowViewModel Search => program.Main.SearchWindow!;

    private List<FoundGroup> Groups() => [.. Search.Finds.Rows.OfType<FoundGroup>()];

    [CorpusFact]
    public void Every_group_folded_keeps_the_results_their_headings_and_how_many_were_found()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Bar.Text = "fortune";
            Bar.ToListCommand.Execute(null);
        });
        Assert.True(program.Until(() => Search.HasResults), "nothing listed");

        var opened = new List<string>();
        try
        {
            program.On(() =>
            {
                int headed = Groups().Sum(group => group.Found);
                Assert.True(headed > 0);

                foreach (string name in Groups().Select(group => group.Name).ToList())
                {
                    var group = Groups().First(each => each.Name == name);
                    if (group.IsOpen)
                    {
                        Search.Finds.FoldCommand.Execute(group);
                        opened.Add(name);
                    }
                }

                Assert.All(Groups(), group => Assert.False(group.IsOpen));
                Assert.Empty(Search.Finds.Rows.OfType<FoundItem>());

                Assert.True(Search.HasResults);
                Assert.True(Search.Finds.HasFinds);
                Assert.False(Search.Finds.FoundNothing);
                Assert.Equal(headed, Groups().Sum(group => group.Found));
                Assert.StartsWith(headed.ToString(), Search.Finds.FoundTitle);

                // Nothing in sight to step to, and no fault in asking.
                Assert.False(Search.Finds.NextCommand.CanExecute(null));
                Assert.False(Search.Finds.PreviousCommand.CanExecute(null));
            });
        }
        finally
        {
            program.On(() =>
            {
                foreach (string name in opened)
                    Search.Finds.FoldCommand.Execute(Groups().First(each => each.Name == name));

                Assert.True(Search.Finds.Rows.OfType<FoundItem>().Any());
                Assert.True(Search.Finds.NextCommand.CanExecute(null));

                Bar.Text = "";
                program.Main.ShowRightCommand.Execute(MainViewModel.EditMark);
            });
        }
    }

    [CorpusFact]
    public void A_word_nothing_answers_to_is_still_nothing_found()
    {
        if (!program.Ready())
            return;

        program.On(() => Bar.Text = "zzzqqq");
        Assert.True(program.Until(() => Bar.FoundNothing), "the box does not say nothing was found");
        program.On(() =>
        {
            Assert.False(Bar.HasFinds);
            Assert.False(Bar.CanStep);
            Bar.Text = "";
        });
    }

    /// <summary>A preset is its family too: two that differ only there are two searches, so Keep names a new one.</summary>
    [CorpusFact]
    public void Two_presets_of_one_rarity_and_another_family_ask_for_different_items()
    {
        var blood = new SearchPreset { Name = "Blood", CraftFamily = "Blood" };
        var caster = new SearchPreset { Name = "Caster", CraftFamily = "Caster" };
        var again = new SearchPreset { Name = "Again", CraftFamily = " blood " };

        Assert.False(blood.AsksTheSameAs(caster));
        Assert.True(blood.AsksTheSameAs(again));
        Assert.False(blood.AsksTheSameAs(new SearchPreset()));
    }
}
