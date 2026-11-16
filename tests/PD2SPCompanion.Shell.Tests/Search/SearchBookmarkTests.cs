using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Search;

/// <summary>
/// The item search on the Items page's right page, under its Search bookmark.
/// It lists the open character's finds and the
/// vault's and nothing else; the left page marks them only while the bookmark is the right page, and what was
/// asked is asked again when the bookmark is come back to; a find clicked shows its place on the left page,
/// selected, and is the item on the Edit page when Edit is taken. The window's search box is a search apart,
/// quick and gone with its list, and a way to this page - an item group's "See all results", and Ctrl + Enter
/// (SearchBoxTests). Since the fifth design: its bookmark is marked while it keeps conditions it was left
/// with, the conditions last replaced whole are the presets' first row ("Previous search"), and F3 steps
/// through its results.
/// <para>
/// What the view models decide. Where the bookmark is drawn, and the conditions having the page while there
/// is nothing to list, are a window's to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SearchBookmarkTests(Program program)
{
    private MainViewModel Main => program.Main;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private QuickSearchViewModel Bar => Main.QuickSearch!;
    private SearchWindowViewModel Search => Main.SearchWindow!;
    private VaultFilter Filter => program.Get<VaultService>().Filter;

    private List<string> Groups() => [.. Search.Finds.Rows.OfType<FoundGroup>().Select(group => group.Name)];

    /// <summary>The Search page asked for a word, as Ctrl + Enter in the window's search box asks it: the page up and its finds listed.</summary>
    private void SearchFor(string word)
    {
        program.On(() =>
        {
            Bar.Text = word;
            Bar.ToListCommand.Execute(null);
        });
        Assert.True(program.Until(() => Search.HasResults), $"nothing listed for '{word}'");
    }

    private List<PresetRowViewModel> PresetRows() => [.. Search.Panel.PresetRows.Cast<PresetRowViewModel>()];

    [CorpusFact]
    public void The_bookmark_taken_with_nothing_asked_is_the_page_with_nothing_to_list()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Assert.False(Search.IsShown);

            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);

            Assert.True(Main.IsRightSearch);
            Assert.True(Search.IsShown);
            Assert.True(program.Get<VP_ViewModel_Vault_R>().IsSearch);
            Assert.Equal("Search", program.Get<VP_ViewModel_Vault_R>().Title);
            Assert.False(Search.Asks);
            Assert.False(Search.HasResults);
            Assert.Equal("", Search.Found);
            Assert.False(Page.IsSearching);
        });
    }

    [CorpusFact]
    public void Asked_from_the_box_the_page_is_shown_and_it_lists_the_characters_finds_and_the_vaults_and_no_more()
    {
        if (!program.Ready())
            return;

        SearchFor("fortune");
        program.On(() =>
        {
            Assert.True(Main.IsRightSearch);
            Assert.True(Search.ShowsConditions);
            Assert.Equal("fortune", Filter.Name);
            Assert.Equal("", Bar.Text);

            // The grail holds Gheed's Fortune, and the window's box lists it: this search does not.
            Assert.Equal([Program.Open, "Vault"], Groups());
            Assert.Matches(@"^\d+ found$", Search.Found);
        });
    }

    [CorpusFact]
    public void A_word_nothing_has_finds_nothing_and_says_so_beside_the_title()
    {
        if (!program.Ready())
            return;

        program.On(() => Main.ShowSearch(search => search.Open("zzqqxxyy")));
        Assert.True(program.Until(() => Search.Found == "0 found"), "said: " + program.On(() => Search.Found));
        program.On(() =>
        {
            Assert.True(Search.Asks);
            Assert.False(Search.HasResults);
        });
    }

    [CorpusFact]
    public void The_left_page_marks_the_finds_only_while_the_bookmark_is_the_right_page_and_again_when_it_is_come_back_to()
    {
        if (!program.Ready())
            return;

        SearchFor("fortune");
        Assert.True(program.Until(() => Page.IsSearching), "the inventory does not mark the finds");

        program.On(() => Main.ShowRightCommand.Execute(MainViewModel.EditMark));
        program.On(() =>
        {
            Assert.False(Search.IsShown);
            Assert.False(Filter.IsFiltering);
            Assert.False(Page.IsSearching);
        });

        program.On(() => Main.ShowRightCommand.Execute(MainViewModel.SearchMark));
        Assert.True(program.Until(() => Page.IsSearching && Search.HasResults), "the search was not asked again");
        program.On(() =>
        {
            Assert.Equal("fortune", Filter.Name);
            Assert.Equal([Program.Open, "Vault"], Groups());
        });
    }

    [CorpusFact]
    public void A_find_of_the_vaults_clicked_shows_the_vault_on_the_left_and_is_the_item_on_the_Edit_page_when_Edit_is_taken()
    {
        if (!program.Ready())
            return;

        SearchFor("fortune");
        string name = "";
        program.On(() =>
        {
            var find = Search.Finds.Rows.OfType<FoundItem>().First(row => row.InVault);
            name = find.Name;
            Search.Finds.ChooseCommand.Execute(find);

            Assert.True(Main.IsLeftVault);
            Assert.True(Main.IsRightSearch, "the page stays");
        });
        Assert.True(program.Until(() => program.Get<VaultService>().Chosen is not null), "the find was not selected in the vault");

        program.On(() => Main.ShowRightCommand.Execute(MainViewModel.EditMark));
        Assert.True(program.Until(() => program.VaultEditPage().Session.SelectedItem?.Name == name),
            "the find is not on the Edit page: " + program.On(() => program.VaultEditPage().Session.SelectedItem?.Name ?? "nothing"));
        program.On(() => Assert.False(Filter.IsFiltering));
    }

    [CorpusFact]
    public void A_find_of_the_characters_clicked_is_selected_and_asked_into_sight()
    {
        if (!program.Ready())
            return;

        SearchFor("fortune");
        program.On(() =>
        {
            ItemTileViewModel? asked = null;
            void Heard(ItemTileViewModel tile) => asked = tile;
            Page.WentToFound += Heard;
            try
            {
                var find = Search.Finds.Rows.OfType<FoundItem>().First(row => !row.InVault);
                Search.Finds.ChooseCommand.Execute(find);

                // Selected, and the view asked to scroll to it: a find low on the page was selected out of sight.
                Assert.True(Main.IsLeftInventory);
                Assert.NotNull(Main.Session.SelectedItem);
                Assert.Equal(find.Name, Main.Session.SelectedItem!.Name);
                Assert.Same(Main.Session.SelectedItem, asked);
                Assert.True(Main.IsRightSearch, "the page stays");
            }
            finally
            {
                Page.WentToFound -= Heard;
            }
        });
    }

    [CorpusFact]
    public void The_brush_empties_the_conditions_and_the_page_stays_with_nothing_to_list()
    {
        if (!program.Ready())
            return;

        SearchFor("fortune");
        program.On(() => Search.Panel.ClearSearchCommand.Execute(null));
        Assert.True(program.Until(() => !Search.HasResults), "the finds are still listed");
        program.On(() =>
        {
            Assert.False(Search.Asks);
            Assert.Equal("", Search.Found);
            Assert.True(Main.IsRightSearch);
            Assert.True(Search.ShowsConditions);
            Assert.False(Page.IsSearching);
        });

        // Emptied, nothing is kept for the next time either.
        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.EditMark);
            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
            Assert.False(Search.Asks);
        });
    }

    [CorpusFact]
    public void The_conditions_fold_by_their_heading_and_Ctrl_Enter_in_the_box_unfolds_them()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
            bool was = Search.ShowsConditions;

            Search.ToggleConditionsCommand.Execute(null);
            Assert.Equal(!was, Search.ShowsConditions);

            Search.ShowsConditions = false;
            Bar.ToListCommand.Execute(null);
            Assert.True(Search.ShowsConditions);
        });
    }

    [CorpusFact]
    public void The_results_fold_by_their_heading_and_what_was_found_stays_found()
    {
        if (!program.Ready())
            return;

        SearchFor("fortune");
        program.On(() =>
        {
            bool was = Search.ShowsResults;
            try
            {
                Search.ShowsResults = true;
                Search.ToggleResultsCommand.Execute(null);

                // Folded, not ended: the finds are counted and the left page marks them still.
                Assert.False(Search.ShowsResults);
                Assert.True(Search.HasResults);
                Assert.Matches(@"^\d+ found$", Search.Found);
                Assert.True(Page.IsSearching);

                Search.ToggleResultsCommand.Execute(null);
                Assert.True(Search.ShowsResults);
            }
            finally
            {
                Search.ShowsResults = was;
            }
        });
    }

    [CorpusFact]
    public void The_box_searches_beside_the_item_search_and_closed_it_leaves_the_page_as_it_was()
    {
        if (!program.Ready())
            return;

        SearchFor("fortune");
        program.On(() => Bar.Text = "stone");
        Assert.True(program.Until(() => Bar.Rows.OfType<FoundGroup>().Any(group => group.Name == "Compendium")), "the box found nothing for 'stone'");

        program.On(() =>
        {
            // Two searches, apart: the page's is as it was while the box lists its own finds - the grail's too.
            Assert.Equal("fortune", Filter.Name);
            Assert.True(Main.IsRightSearch);
            Assert.Contains(Bar.Rows.OfType<FoundGroup>(), group => group.Name == "Compendium");

            Bar.Close();
            Assert.Equal("fortune", Filter.Name);
            Assert.True(Main.IsRightSearch);
            Assert.True(Search.HasResults);
        });
    }

    [CorpusFact]
    public void With_no_character_open_Ctrl_Enter_with_nothing_typed_says_one_is_needed()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            program.Get<PD2SPCompanion.Shell.Services.Character.AutoSave>().Flush();
            Main.Session.Close();

            Bar.ToListCommand.Execute(null);

            Assert.Equal("Require open character.", Main.Session.Status);
            Assert.False(Main.IsRightSearch);
            Assert.False(Search.IsShown);
        });
    }

    [CorpusFact]
    public void With_no_character_open_a_word_sent_from_the_box_loads_one_and_the_page_asks_for_it()
    {
        if (!program.Ready())
            return;

        program.On(() => Main.Home.CloseCommand.Execute(null));
        Assert.True(program.Until(() => !Main.Session.IsLoaded), "the character was not closed");

        program.On(() =>
        {
            Bar.Text = "fortune";
            Bar.ToListCommand.Execute(null);
        });

        Assert.True(program.Until(() => Main.Session.IsLoaded && Main.IsRightSearch && Search.HasResults, 20000),
                    "no character was loaded for the Search page");
        program.On(() =>
        {
            Assert.True(Main.Button_Inventory_isChecked);
            Assert.Equal("fortune", Filter.Name);
            Assert.Equal("", Bar.Text);
        });
    }

    [CorpusFact]
    public void The_page_says_what_is_asked_in_a_line_and_a_row_says_its_number_under_a_property_asked_for()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
            Assert.Equal("", Search.Panel.Asked);

            // The line on the Conditions heading - in sight while the conditions are folded.
            Filter.Name = "charm";
            Assert.Contains("charm", Search.Panel.Asked);

            // The presets' own section no longer repeats it.
            Assert.False(Search.Panel.HasSummary);
        });
        Assert.True(program.Until(() => Search.HasResults), "nothing listed for 'charm'");

        program.On(() =>
        {
            // A property asked for: each row says its item's number under it - what tells charms of one name
            // apart. (The Search page's own list had lost this to a second, plainer list; one list now.)
            var picker = Search.Panel.Form.Properties;
            picker.Search = "to life";
            picker.PickCommand.Execute(picker.Choices.First(choice => choice.ToString() == "+X to Life"));
            Assert.Single(Filter.Properties);
        });
        Assert.True(program.Until(() => Search.HasResults && Search.Finds.Rows.OfType<FoundItem>().Any(row => System.Text.RegularExpressions.Regex.IsMatch(row.Detail, @" \d+$")), 6000),
                    "no row says its number under the property asked for: " + program.On(() => string.Join(" | ", Search.Finds.Rows.OfType<FoundItem>().Take(3).Select(row => row.Detail))
                        + " // asked: " + Filter.Describe() + " // " + Search.Found + " // choices: " + string.Join(", ", Search.Panel.Form.Properties.Choices.Take(6).Select(choice => choice.ToString()))));
        program.On(() =>
        {
            // Every find has the property, so every row says a number after its base.
            string label = Filter.Properties[0].Option.Label;
            Assert.All(Search.Finds.Rows.OfType<FoundItem>(), row => Assert.Contains(label, row.Detail));
        });
    }

    // ---- the bookmark's mark --------------------------------------------------------------------------

    [CorpusFact]
    public void Left_with_conditions_the_bookmark_is_marked_and_says_what_it_keeps_until_it_is_come_back_to()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var said = new List<string?>();
            void Heard(object? sender, System.ComponentModel.PropertyChangedEventArgs changed) => said.Add(changed.PropertyName);
            Main.PropertyChanged += Heard;
            try
            {
                // Left with nothing asked: nothing is kept, and nothing marked.
                Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
                Main.ShowRightCommand.Execute(MainViewModel.EditMark);
                Assert.False(Main.SearchKeeps);
                Assert.Equal("Search", Main.SearchBookmarkHint);

                Main.ShowSearch(search => search.Open("fortune"));
                Filter.SocketsMin = "2";
                Assert.False(Main.SearchKeeps, "the page is up: its bookmark is the current one");
                said.Clear();

                Main.ShowRightCommand.Execute(MainViewModel.GrailMark);
                Assert.True(Main.SearchKeeps);
                Assert.Contains(nameof(MainViewModel.SearchKeeps), said);
                Assert.Contains(nameof(MainViewModel.SearchBookmarkHint), said);
                Assert.StartsWith("Search" + Environment.NewLine, Main.SearchBookmarkHint);
                Assert.Contains("fortune", Main.SearchBookmarkHint);

                // Kept through other bookmarks, until the page is come back to.
                Main.ShowRightCommand.Execute(MainViewModel.EditMark);
                Assert.True(Main.SearchKeeps);

                Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
                Assert.False(Main.SearchKeeps);
                Assert.Equal("fortune", Filter.Name);
                Assert.Equal("2", Filter.SocketsMin);
            }
            finally
            {
                Main.PropertyChanged -= Heard;
            }
        });
    }

    // ---- the previous search --------------------------------------------------------------------------

    [CorpusFact]
    public void Conditions_replaced_whole_are_the_presets_first_row_and_clicked_they_change_places_with_what_stands()
    {
        if (!program.Ready())
            return;

        var settings = program.Get<EditorSettings>();
        program.On(() =>
        {
            int kept = settings.SearchPresets.Count;
            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
            Assert.DoesNotContain(PresetRows(), row => row is PreviousSearchRow);

            // Conditions set by hand, field by field, replace nothing: no previous search.
            Filter.Name = "fortune";
            Filter.SocketsMin = "2";
            Assert.DoesNotContain(PresetRows(), row => row is PreviousSearchRow);

            // A word sent from the window's search box replaces them whole.
            Search.SendText("shael");
            var previous = Assert.IsType<PreviousSearchRow>(PresetRows()[0]);
            Assert.Equal("Previous search", previous.Name);
            Assert.Equal(Filter.Describe(previous.Preset), previous.Summary);
            Assert.Equal("fortune", previous.Preset.ItemName);
            Assert.False(previous.CanForget);
            Assert.Single(PresetRows(), row => row is PreviousSearchRow);

            // Clicked: it is the search, and what stood is the previous one.
            Search.Panel.ChooseCommand.Execute(previous);
            Assert.Equal("fortune", Filter.Name);
            Assert.Equal("2", Filter.SocketsMin);
            Assert.Equal("shael", Assert.IsType<PreviousSearchRow>(PresetRows()[0]).Preset.ItemName);

            // And again: back the other way.
            Search.Panel.ChooseCommand.Execute(PresetRows()[0]);
            Assert.Equal("shael", Filter.Name);
            Assert.Equal("", Filter.SocketsMin);
            Assert.Equal("fortune", Assert.IsType<PreviousSearchRow>(PresetRows()[0]).Preset.ItemName);

            // One slot, this session's: nothing of it is written with the kept presets.
            Assert.Equal(kept, settings.SearchPresets.Count);
            Assert.Equal(kept, Search.Panel.Saved.Count);
        });
    }

    [CorpusFact]
    public void The_brush_keeps_what_it_emptied_as_the_previous_search_and_clicked_with_nothing_standing_the_slot_is_empty_again()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Main.ShowSearch(search => search.Open("fortune"));
            Search.Panel.ClearSearchCommand.Execute(null);

            Assert.False(Search.Asks);
            var previous = Assert.IsType<PreviousSearchRow>(PresetRows()[0]);
            Assert.Equal("fortune", previous.Preset.ItemName);

            // The brush again, on nothing: the slot keeps what it held.
            Search.Panel.ClearSearchCommand.Execute(null);
            Assert.Equal("fortune", Assert.IsType<PreviousSearchRow>(PresetRows()[0]).Preset.ItemName);

            // Clicked with nothing standing: it is the search, and there is nothing to keep in its place.
            Search.Panel.ChooseCommand.Execute(PresetRows()[0]);
            Assert.Equal("fortune", Filter.Name);
            Assert.DoesNotContain(PresetRows(), row => row is PreviousSearchRow);
        });
    }

    [CorpusFact]
    public void A_kept_preset_picked_keeps_what_it_replaced_and_the_same_search_sent_again_keeps_nothing()
    {
        if (!program.Ready())
            return;

        const string kept = "Unit test fortunes";
        var settings = program.Get<EditorSettings>();
        try
        {
            program.On(() =>
            {
                Main.ShowSearch(search => search.Open("fortune"));
                Search.Panel.NameText = kept;
                Search.Panel.KeepCommand.Execute(null);
                var row = Search.Panel.Saved.First(each => each.Name == kept);

                // The same search sent again replaces nothing.
                Search.SendText("fortune");
                Assert.DoesNotContain(PresetRows(), each => each is PreviousSearchRow);

                Search.SendText("shael");
                Search.Panel.ForgetPrevious();

                // The preset picked over "shael": that is the previous search, and the kept presets follow it.
                Search.Panel.ChooseCommand.Execute(row);
                Assert.Equal("fortune", Filter.Name);
                Assert.Equal("shael", Assert.IsType<PreviousSearchRow>(PresetRows()[0]).Preset.ItemName);
                Assert.Contains(PresetRows().Skip(1), each => each is SearchPresetRow { Name: kept });
            });
        }
        finally
        {
            program.On(() =>
            {
                // By the bin, as a player forgets one: its first press asks, its second deletes.
                for (int press = 0; press < 2 && Search.Panel.Saved.FirstOrDefault(row => row.Name == kept) is { } mine; press++)
                    Search.Panel.ForgetCommand.Execute(mine);

                Assert.DoesNotContain(settings.SearchPresets, preset => preset.Name == kept);
                Search.Panel.NameText = "";
                Search.End();
            });
        }
    }

    [CorpusFact]
    public void A_duplicate_badges_search_over_kept_conditions_keeps_them_as_the_previous_search()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Main.ShowSearch(search => search.Open("fortune"));
            Main.ShowRightCommand.Execute(MainViewModel.EditMark);
            Assert.True(Main.SearchKeeps);

            // As a badge clicked asks: the standing search is its copies', and the Search page is brought up.
            Filter.Clear();
            Filter.SameId = 0x12345678;
            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);

            Assert.True(Filter.HasSameId, "the badge's search is the search");
            Assert.False(Main.SearchKeeps);
            Assert.Equal("fortune", Assert.IsType<PreviousSearchRow>(PresetRows()[0]).Preset.ItemName);

            // Left, a badge's search is not kept: a preset cannot hold it.
            Main.ShowRightCommand.Execute(MainViewModel.EditMark);
            Assert.False(Main.SearchKeeps);
            Assert.False(Filter.IsFiltering);
        });
    }

    // ---- F3 -------------------------------------------------------------------------------------------

    [CorpusFact]
    public void F3_goes_to_the_next_result_and_Shift_F3_to_the_one_before_round_the_lists_ends()
    {
        if (!program.Ready())
            return;

        SearchFor("fortune");
        program.On(() =>
        {
            var finds = Search.Finds.Rows.OfType<FoundItem>().ToList();
            Assert.True(finds.Count > 1, "one find of 'fortune' is no list to step through");
            Assert.DoesNotContain(finds, row => row.IsCurrent);

            var heard = new List<IFound>();
            void Heard(IFound row) => heard.Add(row);
            Search.Finds.Marked += Heard;
            try
            {
                Search.Step(+1);
                Assert.True(finds[0].IsCurrent);
                Assert.True(Main.IsRightSearch, "the page stays");

                Search.Step(+1);
                Assert.True(finds[1].IsCurrent);
                Assert.False(finds[0].IsCurrent);

                // Back past the first: the last.
                Search.Step(-1);
                Search.Step(-1);
                Assert.True(finds[^1].IsCurrent);

                // On past the last: the first.
                Search.Step(+1);
                Assert.True(finds[0].IsCurrent);

                // Each step is said, for the list to keep the row in sight.
                Assert.Equal(5, heard.Count);
                Assert.Equal("fortune", Filter.Name);
            }
            finally
            {
                Search.Finds.Marked -= Heard;
            }
        });
    }

    [CorpusFact]
    public void F3_with_nothing_found_does_nothing()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            Main.ShowRightCommand.Execute(MainViewModel.SearchMark);
            Search.Step(+1);
            Search.Step(-1);
            Assert.True(Main.IsRightSearch);
            Assert.False(Search.HasResults);
        });
    }
}
