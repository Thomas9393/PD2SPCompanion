using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Search;

/// <summary>
/// The window's search box (quick and
/// short-lived): what it lists and in what order, that each group lists a few - the best first - and ends in
/// the way to them all, that a find gone to puts the list away and empties the box, the mark Down and Up move
/// and Enter goes to, the row that asks before another character is loaded, a group folded, and what it reads
/// while the game runs. The view models' own deciding, proven here without a window; that the list drops under
/// the box and a click or a key reaches a row is the UI cases' to show.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SearchBoxTests(Program program)
{
    private MainViewModel Main => program.Main;
    private QuickSearchViewModel Bar => Main.QuickSearch!;
    private SearchWindowViewModel Search => Main.SearchWindow!;
    private VaultFilter Filter => program.Get<VaultService>().Filter;

    private bool Ready() => program.Ready();

    /// <summary>Typed, and waited for: the list as it stands once a find of that name is in it.</summary>
    private void Ask(string typed, Func<bool> answered)
    {
        program.On(() => Bar.Text = typed);
        Assert.True(program.Until(answered), $"'{typed}' was not answered");
    }

    private bool Lists(string name, bool elsewhere = false) =>
        Bar.Rows.OfType<FoundItem>().Any(row => row.Name == name && (row.OwnerPath is not null) == elsewhere);

    private List<string> Groups() => [.. Bar.Rows.OfType<FoundGroup>().Select(group => group.Name)];

    private FoundItem Mine(string name) => Bar.Rows.OfType<FoundItem>().First(row => row.Name == name && row.OwnerPath is null);

    /// <summary>The grail's row of that name - not a copy the character or the vault holds.</summary>
    private FoundItem OfGrail(string name) => Under("Compendium").OfType<FoundItem>().First(row => row.Name == name);

    private bool GrailLists(string name) => Under("Compendium").OfType<FoundItem>().Any(row => row.Name == name);

    private FoundItem Theirs(string name) => Bar.Rows.OfType<FoundItem>().First(row => row.Name == name && row.OwnerPath is not null);

    private int Listed() => Bar.Rows.OfType<IFound>().Count();

    /// <summary>The rows under a group's heading, down to the next heading.</summary>
    private List<object> Under(string group)
    {
        var rows = Bar.Rows.ToList();
        int at = rows.FindIndex(row => row is FoundGroup heading && heading.Name == group);
        return at < 0 ? [] : [.. rows.Skip(at + 1).TakeWhile(row => row is not FoundGroup)];
    }

    [CorpusFact]
    public void Nothing_typed_no_list_and_nothing_to_go_to()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            Assert.False(Bar.IsListOpen);
            Assert.False(Bar.HasFinds);
            Assert.Empty(Bar.Rows);

            // Enter and the arrows with nothing listed do nothing.
            Bar.MoveMark(+1);
            Bar.GoMarked();
            Assert.False(Bar.IsListOpen);
        });
    }

    [CorpusFact]
    public void The_open_characters_finds_come_first_then_the_other_characters_then_the_grails()
    {
        if (!Ready())
            return;

        Ask("stone", () => Lists("Stone Barb") && Lists("Stone Barb", elsewhere: true));

        program.On(() =>
        {
            var groups = Groups();
            Assert.Equal(Program.Open, groups[0]);
            Assert.Contains("Other characters", groups);
            Assert.Contains("Compendium", groups);
            Assert.True(groups.IndexOf("Other characters") < groups.IndexOf("Compendium"), string.Join(", ", groups));

            Assert.True(Bar.IsListOpen);
            Assert.True(Bar.HasFinds);
        });
    }

    [CorpusFact]
    public void Each_group_lists_a_few_and_ends_in_the_way_to_them_all()
    {
        if (!Ready())
            return;

        program.On(() => Bar.Targets = Main.PageTargets);

        // A letter everything has: every group finds more than it lists.
        Ask("a", () => Groups().Contains("Other characters") && Groups().Contains("Compendium"));

        program.On(() =>
        {
            // The open character: ten, no place headings over them - each row says its container - and the line.
            var mine = Under(Program.Open);
            var own = Bar.Rows.OfType<FoundGroup>().First(group => group.Name == Program.Open);
            Assert.True(own.Found > QuickSearchViewModel.MostMine, $"the pad's character holds only {own.Found} finds of 'a'");
            Assert.Equal(QuickSearchViewModel.MostMine, mine.OfType<FoundItem>().Count());
            Assert.DoesNotContain(mine, row => row is FoundPlace);
            Assert.All(mine.OfType<FoundItem>(), row => Assert.NotEqual("", row.Where));
            Assert.Equal($"See all {own.Found} results", Assert.IsType<FoundSeeAll>(mine[^1]).Text);

            // The vault: no more than ten; the line where it holds more.
            if (Bar.Rows.OfType<FoundGroup>().FirstOrDefault(group => group.Name == "Vault") is { } vault)
            {
                var stored = Under("Vault");
                Assert.True(stored.OfType<FoundItem>().Count() <= QuickSearchViewModel.MostVault);
                Assert.All(stored.OfType<FoundItem>(), row => Assert.True(row.InVault));

                // Each says its folder, under the vault's own name: no step before the first.
                Assert.All(stored.OfType<FoundItem>(), row => Assert.False(row.Where.StartsWith('\\') || row.Where.StartsWith("Vault", StringComparison.Ordinal), row.Where));

                // And its tooltip the whole of it: the name, and the path as the vault page writes it.
                Assert.All(stored.OfType<FoundItem>(), row =>
                    Assert.Equal(row.Name + Environment.NewLine + (row.Where.Length > 0 ? @"Vault\" + row.Where : "Vault"), row.Tip));
                if (vault.Found > QuickSearchViewModel.MostVault)
                    Assert.Equal($"See all {vault.Found} results", Assert.IsType<FoundSeeAll>(stored[^1]).Text);
            }

            // The other characters: three each under the character's name, and no line - they are not the open one's.
            var others = Under("Other characters");
            Assert.Contains(others, row => row is FoundPlace place && place.Name == Program.Other);
            Assert.True(others.OfType<FoundItem>().Count(row => row.Owner == Program.Other) <= QuickSearchViewModel.MostOther);
            Assert.DoesNotContain(others, row => row is FoundSeeAll);

            // The grail: four a tab, and under each tab that holds more the line to that tab.
            var grail = Under("Compendium");
            var tabs = grail.OfType<FoundPlace>().ToList();
            Assert.Equal(["Uniques", "Sets"], tabs.Select(tab => tab.Name));
            foreach (var tab in tabs)
            {
                int at = grail.IndexOf(tab);
                var rows = grail.Skip(at + 1).TakeWhile(row => row is not FoundPlace).ToList();
                Assert.Equal(Math.Min(tab.Found, QuickSearchViewModel.MostTab), rows.OfType<FoundItem>().Count());
                Assert.True(tab.Found > QuickSearchViewModel.MostTab, $"{tab.Name} holds only {tab.Found} of 'a'");
                Assert.Equal($"See all {tab.Found} results", Assert.IsType<FoundSeeAll>(rows[^1]).Text);
            }

            // Settings and sections: five each, and a line that only says how many more.
            foreach (string places in new[] { "Settings", "Sections" })
            {
                if (Bar.Rows.OfType<FoundGroup>().FirstOrDefault(group => group.Name == places) is not { } group)
                    continue;

                var rows = Under(places);
                Assert.Equal(Math.Min(group.Found, QuickSearchViewModel.MostTargets), rows.OfType<FoundTarget>().Count());
                if (group.Found > QuickSearchViewModel.MostTargets)
                    Assert.Equal($"{group.Found - QuickSearchViewModel.MostTargets} more", Assert.IsType<FoundMore>(rows[^1]).Text);
            }
        });
    }

    [CorpusFact]
    public void Within_a_group_a_name_that_starts_with_it_comes_before_a_name_that_has_it_before_a_base_before_a_stat()
    {
        if (!Ready())
            return;

        Ask("s", () => Groups().Contains("Other characters") && Groups().Contains("Compendium"));

        program.On(() =>
        {
            var rank = FindRank.For("s");
            int Of(FoundItem row) => rank(row.Name, row.Tile.View.BaseName);

            void InOrder(string what, IEnumerable<FoundItem> rows)
            {
                var ranks = rows.Select(Of).ToList();
                Assert.True(ranks.Count > 0, $"{what}: nothing listed");
                Assert.True(ranks.SequenceEqual(ranks.OrderBy(each => each)), $"{what}: {string.Join(", ", ranks)}");
            }

            InOrder("the character's", Under(Program.Open).OfType<FoundItem>());
            if (Groups().Contains("Vault"))
                InOrder("the vault's", Under("Vault").OfType<FoundItem>());
            InOrder("the other character's", Under("Other characters").OfType<FoundItem>().Where(row => row.Owner == Program.Other));

            var grail = Under("Compendium");
            foreach (var tab in grail.OfType<FoundPlace>())
                InOrder("the grail's " + tab.Name, grail.Skip(grail.IndexOf(tab) + 1).TakeWhile(row => row is not FoundPlace).OfType<FoundItem>());

            // The pad's character holds things whose name starts with S: the first row is one of them.
            Assert.Equal(FindRank.Starts, Of(Under(Program.Open).OfType<FoundItem>().First()));
        });
    }

    [CorpusFact]
    public void The_rank_of_a_find_is_its_names_start_its_name_its_base_then_anything_else()
    {
        var rank = FindRank.For("stone");
        Assert.Equal(FindRank.Starts, rank("Stone Barb", "Ring"));
        Assert.Equal(FindRank.Starts, rank("STONERAVEN", null));
        Assert.Equal(FindRank.Named, rank("The Stone of Jordan", "Ring"));
        Assert.Equal(FindRank.Based, rank("Raven Frost", "Stone Ring"));
        Assert.Equal(FindRank.Other, rank("Raven Frost", "Ring"));
        Assert.Equal(FindRank.Other, rank("Raven Frost", null));

        // A pattern is a name that has it, never one that starts with it.
        var either = FindRank.For("barb|frost");
        Assert.Equal(FindRank.Named, either("Stone Barb", "Ring"));
        Assert.Equal(FindRank.Based, either("Nagelring", "Frost Ring"));
        Assert.Equal(FindRank.Other, either("Nagelring", "Ring"));
    }

    [CorpusFact]
    public void A_rows_second_line_is_its_base_and_its_place_and_its_tooltip_says_both_in_full()
    {
        if (!Ready())
            return;

        Ask("stone barb", () => Lists("Stone Barb"));

        program.On(() =>
        {
            var row = Mine("Stone Barb");
            Assert.NotEqual("", row.Where);
            Assert.True(row.HasSecondLine);
            Assert.Equal("Stone Barb" + Environment.NewLine + row.Where, row.Tip);

            // No base to say and no place: one line, and the tooltip the name alone.
            var bare = new FoundItem(row.Id, row.Tile, "", "", inVault: false, () => { });
            Assert.False(bare.HasSecondLine);
            Assert.Equal("Stone Barb", bare.Tip);
            Assert.True(new FoundItem(row.Id, row.Tile, "Matriarchal Spear", "", inVault: false, () => { }).HasSecondLine);
        });
    }

    [CorpusFact]
    public void A_find_gone_to_puts_the_list_away_and_empties_the_box()
    {
        if (!Ready())
            return;

        Ask("stone barb", () => Lists("Stone Barb"));

        program.On(() =>
        {
            Main.ShowPageByKey("Settings_L");
            Bar.ChooseCommand.Execute(Mine("Stone Barb"));

            Assert.False(Bar.IsListOpen);
            Assert.Equal("", Bar.Text);
            Assert.Empty(Bar.Rows);
            Assert.False(Bar.HasFinds);
            Assert.True(Main.Button_Inventory_isChecked, "the Items page is shown for the find");
            Assert.Equal("Stone Barb", Main.Session.SelectedItem?.Name);
        });
    }

    [CorpusFact]
    public void The_first_find_is_marked_Down_and_Up_move_the_mark_round_the_list_and_Enter_goes_to_the_row_marked()
    {
        if (!Ready())
            return;

        Ask("stone", () => Lists("Stone Barb") && Lists("Stone Barb", elsewhere: true) && Groups().Contains("Compendium"));

        program.On(() =>
        {
            var finds = Bar.Rows.OfType<IFound>().ToList();
            Assert.True(finds.Count > 2);
            IFound Marked() => Assert.Single(Bar.Rows.OfType<IFound>(), row => row.IsCurrent);

            // The first, before a key: Enter has somewhere to go.
            Assert.Same(finds[0], Marked());

            var heard = new List<IFound>();
            void Heard(IFound row) => heard.Add(row);
            Bar.Marked += Heard;
            try
            {
                Bar.MoveMark(+1);
                Assert.Same(finds[1], Marked());

                // Round the ends: up from the first is the last, down from the last the first.
                Bar.MoveMark(-1);
                Bar.MoveMark(-1);
                Assert.Same(finds[^1], Marked());
                Bar.MoveMark(+1);
                Assert.Same(finds[0], Marked());

                // Each move is said, for the list to keep the row in sight; nothing was gone to.
                Assert.Equal(4, heard.Count);
                Assert.True(Bar.IsListOpen);
                Assert.Equal("stone", Bar.Text);
            }
            finally
            {
                Bar.Marked -= Heard;
            }

            // Enter: the row marked is gone to.
            var first = Assert.IsType<FoundItem>(finds[0]);
            Assert.Null(first.OwnerPath);
            Bar.GoMarked();
            Assert.False(Bar.IsListOpen);
            Assert.Equal("", Bar.Text);
            Assert.Equal(first.Name, Main.Session.SelectedItem?.Name);
        });
    }

    [CorpusFact]
    public void Enter_before_the_list_has_come_searches_first_and_goes_to_the_best_find()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            // Typed and entered at once: the pause after the last letter is not waited for.
            Bar.Text = "stone barb";
            Assert.False(Bar.IsListOpen);

            Bar.GoMarked();

            Assert.Equal("", Bar.Text);
            Assert.Equal("Stone Barb", Main.Session.SelectedItem?.Name);
        });
    }

    [CorpusFact]
    public void A_find_on_another_character_asks_on_its_own_row_and_CANCEL_puts_the_row_back()
    {
        if (!Ready())
            return;

        Ask("stone barb", () => Lists("Stone Barb", elsewhere: true));

        program.On(() =>
        {
            var row = Theirs("Stone Barb");
            Bar.ChooseCommand.Execute(row);

            Assert.True(row.IsAsking);
            Assert.Equal($"Open {Program.Other}?", row.AskText);
            Assert.True(Bar.IsListOpen, "the list stays for the question");
            Assert.Equal("stone barb", Bar.Text);
            Assert.Equal(Program.Open, Main.Session.CharacterName);

            row.Decline!.Execute(row);
            Assert.False(row.IsAsking);
            Assert.Equal(Program.Open, Main.Session.CharacterName);

            // A row that asks and the mark moved off it: it asks no more.
            Bar.ChooseCommand.Execute(row);
            Assert.True(row.IsAsking);
            Bar.MoveMark(-1);
            Assert.False(row.IsAsking);
        });
    }

    [CorpusFact]
    public void LOAD_loads_the_other_character_and_goes_to_the_find_there_and_the_box_is_emptied()
    {
        if (!Ready())
            return;

        Ask("stone barb", () => Lists("Stone Barb", elsewhere: true));

        program.On(() =>
        {
            var row = Theirs("Stone Barb");
            Assert.Equal("Search", Bar.BoxHint);
            Bar.ChooseCommand.Execute(row);
            row.Accept!.Execute(row);

            Assert.Equal("", Bar.Text);
            Assert.False(Bar.IsListOpen);

            // The empty box says whose file is being opened, before the load takes the window.
            Assert.Equal($"Opening {Program.Other}...", Bar.BoxHint);
        });

        Assert.True(program.Until(() => Main.Session.IsLoaded && Main.Session.CharacterName == Program.Other
                                        && Main.Session.SelectedItem?.Name == "Stone Barb", 15000), "the other character was not loaded and gone to");
        program.On(() =>
        {
            Assert.Equal("Search", Bar.BoxHint);
            Assert.True(Main.Button_Inventory_isChecked);
            Assert.True(Main.IsLeftInventory);
            Assert.Equal("", Bar.Text);
            Assert.False(Bar.IsListOpen);
        });
    }

    [CorpusFact]
    public void Enter_on_a_row_that_asks_answers_yes()
    {
        if (!Ready())
            return;

        Ask("stone barb", () => Lists("Stone Barb", elsewhere: true));

        program.On(() =>
        {
            // Down to the other character's row, Enter: it asks. Enter again: yes.
            var row = Theirs("Stone Barb");
            while (!row.IsCurrent)
                Bar.MoveMark(+1);

            Bar.GoMarked();
            Assert.True(row.IsAsking);
            Assert.Equal(Program.Open, Main.Session.CharacterName);

            Bar.GoMarked();
        });

        Assert.True(program.Until(() => Main.Session.IsLoaded && Main.Session.CharacterName == Program.Other
                                        && Main.Session.SelectedItem?.Name == "Stone Barb", 15000), "Enter did not load the other character");
    }

    [CorpusFact]
    public void From_one_character_to_the_other_and_back_again_by_their_finds()
    {
        if (!Ready())
            return;

        // Each time: the other's find asked about, LOAD, and that character is the open one.
        foreach (string to in new[] { Program.Other, Program.Open, Program.Other })
        {
            Ask("annihilus", () => Lists("Annihilus", elsewhere: true));
            program.On(() =>
            {
                var row = Theirs("Annihilus");
                Assert.Equal(to, row.Owner);
                Bar.ChooseCommand.Execute(row);
                row.Accept!.Execute(row);
            });

            Assert.True(program.Until(() => Main.Session.IsLoaded && Main.Session.CharacterName == to
                                            && Main.Session.SelectedItem?.Name == "Annihilus", 15000), $"{to} was not loaded and gone to");
        }
    }

    [CorpusFact]
    public void With_no_character_open_the_others_finds_are_listed_and_LOAD_loads_one()
    {
        if (!Ready())
            return;

        program.On(() => Main.Home.CloseCommand.Execute(null));
        Assert.True(program.Until(() => !Main.Session.IsLoaded));

        Ask("stone", () => Lists("Stone Barb", elsewhere: true) && Groups().Contains("Compendium"));
        program.On(() =>
        {
            var groups = Groups();
            Assert.True(groups.IndexOf("Other characters") >= 0 && groups.IndexOf("Other characters") < groups.IndexOf("Compendium"), string.Join(", ", groups));
            Assert.DoesNotContain(Bar.Rows.OfType<FoundItem>(), row => row.OwnerPath is null && !row.InVault && row.Name == "Stone Barb");

            var row = Theirs("Stone Barb");
            Bar.ChooseCommand.Execute(row);
            Assert.True(row.IsAsking);
            Assert.False(Main.Session.IsLoaded, "nothing is loaded until LOAD");

            row.Accept!.Execute(row);
        });

        Assert.True(program.Until(() => Main.Session.IsLoaded && Main.Session.SelectedItem?.Name == "Stone Barb", 15000), "no character was loaded and gone to");
        program.On(() =>
        {
            Assert.True(Main.Button_Inventory_isChecked, "the Items page is shown for the find");
            Assert.Equal("", Bar.Text);
            Assert.False(Bar.IsListOpen);
        });
    }

    [CorpusFact]
    public void A_group_folded_is_out_of_the_list_and_keeps_its_count()
    {
        if (!Ready())
            return;

        Ask("stone", () => Lists("Stone Barb") && Groups().Contains("Compendium"));

        program.On(() =>
        {
            int all = Listed();
            int under = Under("Compendium").OfType<IFound>().Count();
            var grail = Bar.Rows.OfType<FoundGroup>().First(group => group.Name == "Compendium");
            Assert.True(grail.IsOpen);
            Assert.True(under > 0);

            grail.Toggle.Execute(grail);
            var folded = Bar.Rows.OfType<FoundGroup>().First(group => group.Name == "Compendium");
            Assert.False(folded.IsOpen);
            Assert.Equal(all - under, Listed());
            Assert.Empty(Under("Compendium"));

            // Its heading still says how many it found.
            Assert.Equal(grail.Found, folded.Found);

            folded.Toggle.Execute(folded);
            Assert.Equal(all, Listed());
        });
    }

    [CorpusFact]
    public void A_grail_find_shows_the_grail_at_it()
    {
        if (!Ready())
            return;

        Ask("death cleaver", () => GrailLists("Death Cleaver"));

        program.On(() =>
        {
            Bar.ChooseCommand.Execute(OfGrail("Death Cleaver"));

            Assert.True(Main.Button_Inventory_isChecked);
            Assert.True(Main.IsRightGrail);
            Assert.False(Bar.IsListOpen);
            Assert.Equal("", Bar.Text);
        });
    }

    [CorpusFact]
    public void With_no_character_open_a_grail_find_shows_the_Extra_pages_grail_which_is_the_fallback()
    {
        if (!Ready())
            return;

        // With a character open the Items page's Grail bookmark is the one shown (the test above); the Extra
        // page's grail, which is only browsed, is where a find goes when there is no character to show it for
        // (the Extra page's grail is a fallback).
        program.On(() => Main.Home.CloseCommand.Execute(null));
        Assert.True(program.Until(() => !Main.Session.IsLoaded));

        Ask("death cleaver", () => GrailLists("Death Cleaver"));
        program.On(() =>
        {
            Bar.ChooseCommand.Execute(OfGrail("Death Cleaver"));

            Assert.False(Main.Session.IsLoaded, "no character is loaded for a grail find");
            Assert.True(Main.Button_Extra_isChecked);
            var grail = program.Get<VP_ViewModel_Extra_R>().Grail;
            Assert.Equal(GrailViewModel.Uniques, grail.Tab);
            Assert.Equal("Death Cleaver", grail.Search);
            Assert.Contains(grail.Cards, card => card.Name == "Death Cleaver");
            grail.Search = "";
        });
    }

    [CorpusFact]
    public void A_grail_tabs_line_shows_that_tab_with_what_was_typed_in_the_grails_own_box_and_all_of_them_listed()
    {
        if (!Ready())
            return;

        Ask("stone", () => Under("Compendium").OfType<FoundSeeAll>().Any());

        var grail = program.Get<VP_ViewModel_Vault_R>().Grail;
        program.On(() =>
        {
            var rows = Under("Compendium");
            var line = rows.OfType<FoundSeeAll>().First();
            var tab = rows.Take(rows.IndexOf(line)).OfType<FoundPlace>().Last();
            Assert.Equal($"See all {tab.Found} results", line.Text);

            // Something narrowing that tab from before: let go of, so the cards are the ones counted.
            grail.Load();
            grail.Quick = grail.QuickChoices.First(choice => !choice.IsAny);

            Bar.ChooseCommand.Execute(line);

            Assert.True(Main.IsRightGrail);
            Assert.Equal(tab.Name, grail.Tab);
            Assert.Equal("stone", grail.Search);
            Assert.True(grail.Quick?.IsAny);
            Assert.Equal(tab.Found, grail.Cards.Count);
            Assert.Equal("", Bar.Text);
            Assert.False(Bar.IsListOpen);

            grail.Search = "";
        });
    }

    [CorpusFact]
    public void An_item_groups_line_shows_the_Search_page_with_what_was_typed_as_its_only_condition()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            // Conditions from before: replaced whole, and kept as the previous search.
            Main.ShowSearch(search => search.Open("fortune"));
            Filter.SocketsMin = "2";
            Main.ShowRightCommand.Execute(MainViewModel.EditMark);
        });

        Ask("a", () => Under(Program.Open).OfType<FoundSeeAll>().Any());
        int found = 0;
        program.On(() =>
        {
            found = Bar.Rows.OfType<FoundGroup>().First(group => group.Name == Program.Open).Found;
            Bar.ChooseCommand.Execute(Under(Program.Open).OfType<FoundSeeAll>().First());

            Assert.True(Main.Button_Inventory_isChecked);
            Assert.True(Main.IsRightSearch);
            Assert.Equal("a", Filter.Name);
            Assert.Equal("", Filter.SocketsMin);
            Assert.Equal("", Bar.Text);
            Assert.False(Bar.IsListOpen);

            var previous = Assert.IsType<PreviousSearchRow>(Search.Panel.PresetRows.Cast<object>().First());
            Assert.Equal("fortune", previous.Preset.ItemName);
        });

        // The page lists them all: the group that listed ten says the number the line did.
        Assert.True(program.Until(() => Search.Finds.Rows.OfType<FoundGroup>().FirstOrDefault(group => group.Name == Program.Open)?.Found == found),
                    "the Search page does not list the character's finds");
        program.On(() => Assert.True(Search.Finds.Rows.OfType<FoundItem>().Count(row => !row.InVault) > QuickSearchViewModel.MostMine));
    }

    [CorpusFact]
    public void Ctrl_Enter_shows_the_Search_page_with_the_caret_asked_into_its_keyword_field()
    {
        if (!Ready())
            return;

        program.On(() =>
        {
            int asked = 0;
            void Wanted() => asked++;
            Search.KeywordWanted += Wanted;
            try
            {
                // Something typed: it becomes the page's only condition.
                Main.ShowSearch(search => search.Open("fortune"));
                Filter.SocketsMin = "2";
                Main.ShowRightCommand.Execute(MainViewModel.EditMark);
                Search.ShowsConditions = false;
                asked = 0;

                Bar.Text = "shael";
                Bar.ToListCommand.Execute(null);

                Assert.True(Main.IsRightSearch);
                Assert.True(Search.ShowsConditions);
                Assert.Equal("shael", Filter.Name);
                Assert.Equal("", Filter.SocketsMin);
                Assert.Equal("", Bar.Text);
                Assert.False(Bar.IsListOpen);
                Assert.Equal(1, asked);

                // Nothing typed: the page as it stands, the caret in its keyword field all the same.
                Filter.SocketsMin = "3";
                Main.ShowRightCommand.Execute(MainViewModel.EditMark);
                Bar.ToListCommand.Execute(null);

                Assert.True(Main.IsRightSearch);
                Assert.Equal("shael", Filter.Name);
                Assert.Equal("3", Filter.SocketsMin);
                Assert.Equal(2, asked);
            }
            finally
            {
                Search.KeywordWanted -= Wanted;
            }
        });
    }

    [CorpusFact]
    public void An_item_gone_to_from_the_box_puts_Edit_on_the_right_where_the_Search_page_was_and_the_search_is_kept()
    {
        if (!Ready())
            return;

        program.On(() => Main.ShowSearch(search => search.Open("fortune")));
        Assert.True(program.Until(() => Main.Inventory!.IsSearching), "the inventory does not mark the finds");

        Ask("stone barb", () => Lists("Stone Barb"));
        program.On(() =>
        {
            Assert.False(Main.SearchKeeps);
            Bar.ChooseCommand.Execute(Mine("Stone Barb"));

            // Edit, the item on it; nothing dimmed, since the search left with its page...
            Assert.True(Main.IsRightEdit);
            Assert.Equal("Stone Barb", Main.Session.SelectedItem?.Name);
            Assert.False(Filter.IsFiltering);
            Assert.False(Main.Inventory!.IsSearching);

            // ...which keeps what it asked, and its bookmark says so.
            Assert.True(Main.SearchKeeps);
            Assert.Contains("fortune", Main.SearchBookmarkHint);

            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
            Assert.Equal("fortune", Filter.Name);
            Assert.False(Main.SearchKeeps);
            Assert.Equal("Search", Main.SearchBookmarkHint);
        });
    }

    [CorpusFact]
    public void An_item_gone_to_from_the_box_puts_Edit_where_the_Grail_was_and_leaves_Trade_where_it_is()
    {
        if (!Ready())
            return;

        program.On(() => Main.ShowRightCommand.Execute(MainViewModel.GrailMark));
        Ask("stone barb", () => Lists("Stone Barb"));
        program.On(() =>
        {
            Assert.True(Main.IsRightGrail, $"the right page is {Main.RightBookmark}, loaded {Main.Session.IsLoaded}");
            Bar.ChooseCommand.Execute(Mine("Stone Barb"));
            Assert.True(Main.IsRightEdit);
            Assert.Equal("Stone Barb", Main.Session.SelectedItem?.Name);

            Main.ShowRightCommand.Execute(MainViewModel.TradeMark);
        });

        Ask("stone barb", () => Lists("Stone Barb"));
        program.On(() =>
        {
            Assert.True(Main.IsRightTrade);
            Bar.ChooseCommand.Execute(Mine("Stone Barb"));

            // Trade works on what is selected, and dims nothing: it stays.
            Assert.True(Main.IsRightTrade);
            Assert.Equal("Stone Barb", Main.Session.SelectedItem?.Name);
        });
    }

    [CorpusFact]
    public void A_page_is_found_by_its_name_under_Sections_and_gone_to()
    {
        if (!Ready())
            return;

        // The sections and settings are the window's to give (its folds' headings, its Settings page's rows);
        // the pages themselves are the main view model's.
        program.On(() => Bar.Targets = Main.PageTargets);
        Ask("settings", () => Bar.Rows.OfType<FoundTarget>().Any(target => target.Name == "Settings"));

        program.On(() =>
        {
            Assert.Contains("Sections", Groups());

            Bar.ChooseCommand.Execute(Bar.Rows.OfType<FoundTarget>().First(target => target.Name == "Settings"));
            Assert.True(Main.Button_Settings_isChecked);
            Assert.False(Bar.IsListOpen);
            Assert.Equal("", Bar.Text);
        });
    }

    /// <summary>
    /// The vault's folders are found by their names:
    /// a group of their own under the vault's items, a row a folder - its name, and the folder it lies in - that
    /// shows the vault and goes to it.
    /// </summary>
    [CorpusFact]
    public void A_vault_folder_is_found_by_its_name_under_Folders_and_gone_to()
    {
        if (!Ready())
            return;

        Ask("grand", () => Bar.Rows.OfType<FoundTarget>().Any(target => target.Name == "Grand"));

        try
        {
            program.On(() =>
            {
                Assert.Contains("Folders", Groups());
                var grand = Assert.Single(Under("Folders").OfType<FoundTarget>());
                Assert.Equal(("Grand", @"Vault\Charms"), (grand.Name, grand.Where));

                Bar.ChooseCommand.Execute(grand);
                Assert.False(Bar.IsListOpen);
                Assert.Equal("", Bar.Text);
                Assert.True(Main.IsLeftVault, "the vault is shown for the folder gone to");
                Assert.Equal(@"Vault\Charms\Grand", Main.Vault!.Place?.Path);
            });

            // A word no folder is named by lists none of them.
            Ask("stone", () => Lists("Stone Barb"));
            program.On(() => Assert.DoesNotContain("Folders", Groups()));
        }
        finally
        {
            program.On(() =>
            {
                Bar.Close();
                Vault.VaultHands.Rest(program);
                Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark);
            });
        }
    }

    [CorpusFact]
    public void What_nothing_answers_to_is_said_and_there_is_nothing_to_go_to()
    {
        if (!Ready())
            return;

        Ask("zzzqqq", () => Bar.FoundNothing);

        program.On(() =>
        {
            Assert.True(Bar.IsListOpen);
            Assert.Empty(Bar.Rows);
            Assert.False(Bar.HasFinds);

            Bar.GoMarked();
            Assert.Equal("zzzqqq", Bar.Text);
        });
    }

    [CorpusFact]
    public void Closed_by_Esc_or_a_click_elsewhere_the_list_is_put_away_and_the_box_emptied()
    {
        if (!Ready())
            return;

        Ask("stone barb", () => Lists("Stone Barb"));

        program.On(() =>
        {
            Bar.CloseCommand.Execute(null);

            Assert.Equal("", Bar.Text);
            Assert.False(Bar.IsListOpen);
            Assert.Empty(Bar.Rows);
            Assert.False(Bar.HasFinds);

            // Closed while the list was still on its way: it does not come after all.
            Bar.Text = "stone barb";
            Bar.Close();
        });

        Assert.False(program.Until(() => Bar.IsListOpen, 600), "the list of a search that was closed came all the same");
        program.On(() => Assert.Equal("", Bar.Text));
    }

    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void While_the_game_runs_no_items_are_searched_the_grail_and_the_sections_are()
    {
        if (!Ready())
            return;

        program.On(() => Bar.Targets = Main.PageTargets);
        File.WriteAllText(program.GameFlag, "running");
        try
        {
            // Seen within the two seconds between looks; the character is closed for the game.
            Assert.True(program.Until(() => Main.Sync?.IsGameRunning == true && !Main.Session.IsLoaded, 10000), "the game was not seen");

            Ask("stone", () => Groups().Contains("Compendium"));
            program.On(() =>
            {
                Assert.Equal("Game running: no items searched", Assert.IsType<FoundMore>(Bar.Rows[0]).Text);
                Assert.DoesNotContain("Other characters", Groups());
                Assert.DoesNotContain("Vault", Groups());
                Assert.DoesNotContain(Bar.Rows.OfType<FoundItem>(), row => row.Name == "Stone Barb");
            });

            Ask("settings", () => Bar.Rows.OfType<FoundTarget>().Any(target => target.Name == "Settings"));
            program.On(() => Assert.Contains("Sections", Groups()));
        }
        finally
        {
            File.Delete(program.GameFlag);
            program.Until(() => Main.Sync?.IsGameRunning != true, 10000);
        }
    }
}
