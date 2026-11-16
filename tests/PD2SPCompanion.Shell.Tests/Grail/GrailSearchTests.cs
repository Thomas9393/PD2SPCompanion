using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Grail;

/// <summary>
/// The grail's own box and its tabs:
/// the box finds an entry by its name, its base and its stats -
/// the text the window's search box reads - or by a pattern; what is typed stands across the tabs. Each tab
/// says two things, each in its own place: how many of its entries the game has discovered, always, and how many it
/// holds of what is typed, while the box holds anything - a tab with none steps back. The recipes
/// are found with the search's Family box; the grail has no Runewords tab, no ethereal lists and no whose finds.
/// <para>
/// What the grail's view model decides. Where the numbers are drawn on a tab is a window's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class GrailSearchTests(Program program)
{
    /// <summary>A line many uniques and some set pieces have, and no entry's name or base.</summary>
    private const string Stat = "cannot be frozen";

    private static int Number(string said) => int.TryParse(said, out int number) ? number : -1;

    private static string[] Matches(GrailViewModel grail) => [.. grail.Tabs.Select(tab => tab.Matches)];

    [CorpusFact]
    public void The_box_finds_an_entry_by_a_stats_line_as_well_as_by_its_name_and_base()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                grail.ShowTabCommand.Execute(GrailViewModel.Uniques);
                int all = grail.Cards.Count;

                grail.Search = Stat;
                Assert.True(grail.Cards.Count > 0, "no unique was found by its stat");
                Assert.True(grail.Cards.Count < all);
                Assert.All(grail.Cards, card =>
                {
                    Assert.DoesNotContain("frozen", card.Name, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("frozen", card.Subtitle, StringComparison.OrdinalIgnoreCase);
                    Assert.True(card.Answers(Stat, null));
                });

                // By name, as before; and by base.
                grail.Search = "death cleaver";
                Assert.Contains(grail.Cards, card => card.Name == "Death Cleaver");
                string based = grail.Cards.First(card => card.Name == "Death Cleaver").Subtitle;
                grail.Search = based;
                Assert.Contains(grail.Cards, card => card.Name == "Death Cleaver");

                // A pattern: either line.
                grail.Search = Stat;
                int frozen = grail.Cards.Count;
                grail.Search = "cannot be frozen|slain monsters rest in peace";
                Assert.True(grail.Cards.Count > frozen, "the pattern found no more than its first half");
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });
    }

    [CorpusFact]
    public void There_are_two_tabs_and_the_one_shown_is_marked()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                Assert.Equal([GrailViewModel.Uniques, GrailViewModel.Sets], grail.Tabs.Select(tab => tab.Key));
                Assert.Equal(["UNIQUES", "SETS"], grail.Tabs.Select(tab => tab.Label));
                Assert.Equal("Grail_Tab_Sets", grail.TabOf(GrailViewModel.Sets).TabId);

                grail.ShowTabCommand.Execute(GrailViewModel.Sets);
                Assert.Equal([false, true], grail.Tabs.Select(tab => tab.IsCurrent));

                // However the tab is come to: an entry gone to from the window's search box sets it directly.
                grail.ShowEntry(GrailViewModel.Uniques, "Death Cleaver");
                Assert.Equal([true, false], grail.Tabs.Select(tab => tab.IsCurrent));
                Assert.Equal("Death Cleaver", Assert.Single(grail.Cards).Name);
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });
    }

    /// <summary>
    /// A find gone to shows its card whatever the list was left on: Show and the Sets' tier let go of, as the type is.
    /// Left on Owned, a card no vault holds was an empty list with its name in the box.
    /// </summary>
    [CorpusFact]
    public void A_find_gone_to_shows_its_card_whatever_Show_and_the_tier_were_left_on()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                // A piece of a set of one tier, gone to with the list on another tier and on what it does not count.
                grail.ShowTabCommand.Execute(GrailViewModel.Sets);
                var piece = grail.Cards.First(card => card.Tier is { } tier && tier != SetTier.Class && !card.IsOwned);
                grail.ShowOnlyCommand.Execute(GrailShow.Owned);
                grail.ShowTierCommand.Execute(GrailShow.Class);

                grail.ShowEntry(GrailViewModel.Sets, piece.Name);

                Assert.Contains(grail.Cards, card => card.Name == piece.Name);
                Assert.Equal(GrailShow.All, grail.Showing.Key);
                Assert.Equal(GrailShow.All, grail.Tier.Key);
            }
            finally
            {
                grail.ShowOnlyCommand.Execute(GrailShow.All);
                grail.ShowTierCommand.Execute(GrailShow.All);
                GrailHands.Rest(grail);
            }
        });
    }

    [CorpusFact]
    public void Each_tab_says_how_many_of_its_entries_are_discovered_always_and_nothing_typed_or_chosen_changes_it()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                grail.ShowTabCommand.Execute(GrailViewModel.Uniques);
                var uniques = grail.TabOf(GrailViewModel.Uniques);

                // "9 / 427": the cards the game discovered - a card any of whose rows it has - of the cards that count,
                // as the list shows them, nothing narrowing it; the specials are shown and not counted. Discovered is
                // the shared stash's to say (the pad's, Season 13's, keeps none).
                var discovered = SharedStash.DiscoveredUniquesIn(Path.Combine(program.Folder, "saves", "pd2_shared.stash"));
                Assert.All(grail.Cards, card => Assert.Equal(card.Rows.Any(discovered.Contains), card.IsFound));
                var counted = grail.Cards.Where(card => card.Counts).ToList();
                int found = counted.Count(card => card.IsFound), all = counted.Count;
                Assert.Equal($"{found} / {all}", uniques.Progress);
                Assert.Equal((double)found / all, uniques.Share, 6);
                Assert.All(grail.Tabs, tab =>
                {
                    Assert.Matches(@"^\d+ / \d+( · \d+ owned)?$", tab.Progress);
                    Assert.InRange(tab.Share, 0, 1);
                });

                // The title row says nothing while nothing narrows the list: the tab says it.
                Assert.Equal("", grail.CountText);

                // Typed, a type chosen, another tab shown: the tabs' found lines stay.
                string[] before = [.. grail.Tabs.Select(tab => tab.Progress)];
                grail.Search = Stat;
                Assert.Equal(before, grail.Tabs.Select(tab => tab.Progress));
                Assert.Equal($"{grail.Cards.Count} shown", grail.CountText);

                grail.Search = "";
                grail.Quick = grail.QuickChoices.First(choice => !choice.IsAny);
                Assert.Equal(before, grail.Tabs.Select(tab => tab.Progress));
                Assert.Equal($"{grail.Cards.Count} shown", grail.CountText);

                grail.ShowTabCommand.Execute(GrailViewModel.Sets);
                Assert.Equal(before, grail.Tabs.Select(tab => tab.Progress));
                Assert.Equal("", grail.CountText);
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });
    }

    [CorpusFact]
    public void Each_tab_says_how_many_it_holds_of_what_is_typed_and_a_tab_with_none_steps_back()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grail = GrailHands.OnRight(program);
            try
            {
                grail.ShowTabCommand.Execute(GrailViewModel.Uniques);
                var uniques = grail.TabOf(GrailViewModel.Uniques);
                var sets = grail.TabOf(GrailViewModel.Sets);

                // Nothing typed: no numbers, nothing stepped back.
                Assert.Equal(["", ""], Matches(grail));
                Assert.All(grail.Tabs, tab => Assert.False(tab.HasMatches || tab.IsEmpty));

                var said = new List<string?>();
                void Heard(object? sender, System.ComponentModel.PropertyChangedEventArgs changed) => said.Add(changed.PropertyName);
                uniques.PropertyChanged += Heard;
                try
                {
                    grail.Search = Stat;
                }
                finally
                {
                    uniques.PropertyChanged -= Heard;
                }

                Assert.Contains(nameof(GrailTab.Matches), said);
                Assert.Contains(nameof(GrailTab.HasMatches), said);

                // The tab in sight says what its list shows; the others what theirs would.
                Assert.Equal(grail.Cards.Count.ToString(), uniques.Matches);
                Assert.True(uniques.HasMatches);
                Assert.False(uniques.IsEmpty);
                int inSets = Number(sets.Matches);
                Assert.True(inSets >= 0, $"sets {sets.Matches}");

                // What is typed stands across the tabs, and each list is what its tab said.
                grail.ShowTabCommand.Execute(GrailViewModel.Sets);
                Assert.Equal(Stat, grail.Search);
                Assert.Equal(inSets, grail.Cards.Count);
                Assert.Equal(inSets == 0, sets.IsEmpty);

                // A name one tab has: the other holds none, and says so.
                grail.Search = "death cleaver";
                Assert.Equal(["1", "0"], Matches(grail));
                Assert.Equal([false, true], grail.Tabs.Select(tab => tab.IsEmpty));

                // The box emptied: the numbers go.
                grail.Search = "";
                Assert.Equal(["", ""], Matches(grail));
                Assert.All(grail.Tabs, tab => Assert.False(tab.HasMatches || tab.IsEmpty));
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });
    }

    [CorpusFact]
    public void The_Extra_pages_grail_which_is_only_browsed_finds_and_counts_the_same()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var grail = GrailHands.OfExtra(program);
            try
            {
                grail.Load();
                grail.ShowTabCommand.Execute(GrailViewModel.Uniques);
                Assert.Equal(2, grail.Tabs.Count);
                Assert.Matches(@"^\d+ / \d+$", grail.TabOf(GrailViewModel.Uniques).Progress);

                grail.Search = Stat;
                Assert.True(grail.Cards.Count > 0);
                Assert.Equal(grail.Cards.Count.ToString(), grail.TabOf(GrailViewModel.Uniques).Matches);
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });
    }

    [CorpusFact]
    public void The_window_search_boxs_grail_count_for_a_tab_is_the_grails_own()
    {
        if (!program.Ready())
            return;

        var bar = program.Main.QuickSearch!;
        program.On(() => bar.Text = Stat);
        Assert.True(program.Until(() => bar.Rows.OfType<FoundGroup>().Any(group => group.Name == "Compendium")), "the box found nothing in the grail");

        program.On(() =>
        {
            // The tabs the box lists, each with its count, in the list's order.
            var rows = bar.Rows.ToList();
            int at = rows.FindIndex(row => row is FoundGroup group && group.Name == "Compendium");
            var tabs = rows.Skip(at + 1).TakeWhile(row => row is not FoundGroup).OfType<FoundPlace>().ToDictionary(tab => tab.Name, tab => tab.Found);
            bar.Close();

            var grail = GrailHands.OnRight(program);
            try
            {
                grail.Search = Stat;
                foreach (var tab in grail.Tabs)
                    Assert.Equal(tabs.GetValueOrDefault(tab.Key), Number(tab.Matches));
            }
            finally
            {
                GrailHands.Rest(grail);
            }
        });
    }
}
