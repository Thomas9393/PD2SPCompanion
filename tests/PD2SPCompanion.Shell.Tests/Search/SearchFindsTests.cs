using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Search;

/// <summary>
/// Acting on what a search found:
/// the search's list has two tools. Select found - the finds that lie in what the pages show become the
/// selection, the stash's three maps, then the two charms of a vault folder, and it says so where none does.
/// Move found to folder - every find of the vault's, whichever folder it lies in, moved by the list the vault's
/// Move button opens; it is there only while the vault holds a find. Ctrl + A while a search stands takes the
/// finds and not the rest. And the Edit page beside either left page says how many are selected.
/// <para>
/// What the pages decide, asked through the list's own commands and read back from the pages, the strips and
/// the vault. That a click reaches the two buttons, and Ctrl + A and Esc the page under the pointer, is the UI
/// cases' to show.
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SearchFindsTests(Program program)
{
    private MainViewModel Main => program.Main;
    private VP_ViewModel_Inv_L Page => Main.Inventory!;
    private VP_ViewModel_Vault_L Vault => Main.Vault!;
    private SearchWindowViewModel Panel => Main.SearchWindow!;
    private SearchResultsViewModel Results => Panel.Results;

    /// <summary>The item panel beside the left page: the character's beside the inventory, the vault's beside the vault.</summary>
    private VP_ViewModel_Inv_R Edit => program.Get<VP_ViewModel_Vault_R>().Editor;

    private ContainerSectionViewModel Section => Page.Sections.First(section => section.Pages is not null);

    private StashPageChoice Choice(string label) => Section.Pages!.First(page => page.Label == label);

    private List<string> Picked() => [.. Choice("Personal Stash").Grid!.Items.Where(tile => tile.IsPicked).Select(tile => tile.Name).Order()];

    private FolderPlace Folder(string name) => Vault.Places.First(place => place.Name == name);

    /// <summary>
    /// The inventory's first pane on the personal stash, and a search for maps standing: three of them lie
    /// there. Waited for until the page has marked them and the panel lists them - the search is taken up a
    /// moment after it stops changing.
    /// </summary>
    private void MapsInTheStash()
    {
        program.On(() =>
        {
            Section.SelectedPage = Choice("Personal Stash");
            Main.ShowSearch(search => search.Open("map"));
        });
        Assert.True(program.Until(() => Page.IsSearching
                                        && Choice("Personal Stash").Grid!.Items.Count(tile => !tile.IsSearchMiss) == 3
                                        && Panel.Finds.Rows.OfType<FoundPlace>().Any(place => place.Name == "Personal Stash")),
                    "the stash's three maps were not found");
    }

    /// <summary>The vault on the left page, at its top, and a search for charms standing: four of the vault's, in three folders.</summary>
    private void CharmsInTheVault()
    {
        program.On(() =>
        {
            Main.ShowLeftCommand.Execute(MainViewModel.VaultMark);
            Vault.Show();
            Main.ShowSearch(search => search.Open("charm"));
        });
        Assert.True(program.Until(() => Results.HasVaultFinds && Vault.IsSearchingHere && Vault.PagesText.Contains("0 found", StringComparison.Ordinal)),
                    "the vault's charms were not found");
    }

    /// <summary>As every test finds the program: no search, nothing selected, the vault at its top, the inventory in its pane on the left page.</summary>
    private void PutBack() => program.On(() =>
    {
        Panel.End();
        Vault.IsPickerOpen = false;
        Vault.ClearSelection();
        Vault.GoTo(null);
        Main.ShowLeftCommand.Execute(MainViewModel.InventoryMark);
        Page.ClearSelection();
        Section.SelectedPage = Choice("Inventory");
    });

    /// <summary>One lock, one rule: a locked folder's finds are not selected, and the page says why.</summary>
    [CorpusFact]
    public void Select_found_picks_nothing_in_a_locked_folder()
    {
        if (!program.Ready())
            return;

        try
        {
            CharmsInTheVault();
            program.On(() => Vault.GoToPlace(Folder("Small")));
            Assert.True(program.Until(() => Vault.PagesText.Contains("2 found", StringComparison.Ordinal)), "Small's two charms were not found there");

            program.On(() =>
            {
                Vault.ToggleFolderLockCommand.Execute(null);
                try
                {
                    Results.SelectFoundCommand.Execute(null);

                    Assert.Equal(0, Vault.PickedCount);
                    Assert.False(Vault.HasActing);
                    Assert.Equal("Folder locked.", Vault.Notice);
                }
                finally
                {
                    Vault.ToggleFolderLockCommand.Execute(null);
                    Vault.Notice = "";
                }
            });
        }
        finally
        {
            PutBack();
        }
    }

    [CorpusFact]
    public void Select_found_makes_the_finds_the_panes_show_the_selection_and_the_Edit_page_says_how_many()
    {
        if (!program.Ready())
            return;

        try
        {
            MapsInTheStash();
            program.On(() =>
            {
                Assert.Matches(@"^\d+ found$", Panel.Found);

                // The vault holds none of them: no Move found to folder.
                Assert.True(Results.CanSelectFound);
                Assert.False(Results.HasVaultFinds);

                Results.SelectFoundCommand.Execute(null);

                Assert.Equal(3, Page.PickedCount);
                Assert.Equal(["Ruins of Viz-Jun Map", "Shadows of Westmarch Map", "Torajan Jungle Map"], Picked());
                Assert.StartsWith("Store 3 selected items", Page.StoreHint);
                Assert.True(Edit.IsPicking);
                Assert.Equal("3 selected", Edit.PickedText);

                // Let go of, as Esc does.
                Page.ClearSelection();
                Assert.Equal(0, Page.PickedCount);
                Assert.False(Edit.IsPicking);
            });
        }
        finally
        {
            PutBack();
        }
    }

    [CorpusFact]
    public void Ctrl_A_while_a_search_stands_takes_the_finds_and_not_the_rest_of_the_page()
    {
        if (!program.Ready())
            return;

        try
        {
            MapsInTheStash();
            program.On(() =>
            {
                int all = Choice("Personal Stash").Grid!.Items.Count;
                Assert.True(all > 3, "the stash holds nothing but the maps: the check below proves nothing");

                Page.SelectAll(Section);
                Assert.Equal(3, Page.PickedCount);
                Assert.Equal(["Ruins of Viz-Jun Map", "Shadows of Westmarch Map", "Torajan Jungle Map"], Picked());

                // The search ended, all is everything again.
                Page.ClearSelection();
                Panel.End();
            });

            Assert.True(program.Until(() => !Page.IsSearching && !Choice("Personal Stash").Grid!.Items.Any(tile => tile.IsSearchMiss)), "the search did not let go of the page");
            program.On(() =>
            {
                Page.SelectAll(Section);
                Assert.Equal(Choice("Personal Stash").Grid!.Items.Count, Page.PickedCount);
            });
        }
        finally
        {
            PutBack();
        }
    }

    [CorpusFact]
    public void Where_no_find_lies_in_what_is_shown_Select_found_says_so()
    {
        if (!program.Ready())
            return;

        try
        {
            CharmsInTheVault();
            program.On(() =>
            {
                Assert.Matches(@"^\d+ found$", Panel.Found);

                // The vault holds some of the finds: Move found to folder is there.
                Assert.True(Results.HasVaultFinds);

                // At the vault's top, which holds none of them.
                Results.SelectFoundCommand.Execute(null);

                Assert.Equal("No find in what is shown.", Main.Session.Status);
                Assert.False(Vault.HasActing);
            });
        }
        finally
        {
            PutBack();
        }
    }

    [CorpusFact]
    public void Select_found_in_a_vault_folder_selects_its_finds_and_the_Edit_page_beside_the_vault_says_how_many()
    {
        if (!program.Ready())
            return;

        try
        {
            CharmsInTheVault();
            program.On(() => Vault.GoToPlace(Folder("Small")));
            Assert.True(program.Until(() => Vault.PagesText.Contains("2 found", StringComparison.Ordinal)), "Small's two charms were not found there");

            program.On(() =>
            {
                Results.SelectFoundCommand.Execute(null);

                Assert.Equal(2, Vault.PickedCount);
                Assert.Contains("2 selected", Vault.PagesText);

                // Several selected in the vault with the Edit page beside it: said, not refused.
                Assert.True(Edit.IsPicking);
                Assert.Equal("2 selected", Edit.PickedText);
            });
        }
        finally
        {
            PutBack();
        }
    }

    [CorpusFact]
    public void Move_found_to_folder_moves_every_find_of_the_vaults_whichever_folder_it_lies_in()
    {
        if (!program.Ready(changes: true))
            return;

        try
        {
            CharmsInTheVault();
            program.On(() => Vault.GoToPlace(Folder("Small")));
            Assert.True(program.Until(() => Vault.PagesText.Contains("2 found", StringComparison.Ordinal)), "Small's two charms were not found there");

            int before = 0;
            program.On(() =>
            {
                // Four charms, in three folders; the one to move them to holds nothing yet.
                Assert.Equal(0, Folder("New folder").Items);
                before = Folder("Grand").Items + Folder("Small").Items + Folder("Jewels").Items;

                Results.MoveFoundCommand.Execute(null);

                Assert.True(Vault.IsPickerOpen);
                Assert.Equal("Move 4 found items", Vault.MoveTitle);

                // The folder shown is listed too: they are not all in it.
                Assert.Contains(Vault.Targets, target => target.Name == "Small");

                Vault.MoveToCommand.Execute(Vault.Targets.First(target => target.Name == "New folder"));

                Assert.False(Vault.IsPickerOpen);
                Assert.Equal("4 items moved to New folder.", Vault.Notice);
            });

            Assert.True(program.Until(() => Folder("New folder").Items == 4), "New folder does not hold the four");
            program.On(() =>
            {
                Assert.Equal(before - 4, Folder("Grand").Items + Folder("Small").Items + Folder("Jewels").Items);

                // The search stands, its list with it.
                Assert.True(Panel.IsShown);
                Assert.Matches(@"^\d+ found$", Panel.Found);
                Assert.True(Results.HasVaultFinds);
            });
        }
        finally
        {
            PutBack();
        }
    }

    [CorpusFact]
    public void Move_found_asks_for_the_vault_page_while_none_is_in_sight()
    {
        if (!program.Ready())
            return;

        try
        {
            // The inventory on the left, the search on the right: the vault's charms are found all the same.
            program.On(() => Main.ShowSearch(search => search.Open("charm")));
            Assert.True(program.Until(() => Results.HasVaultFinds), "the vault's charms were not found");

            program.On(() =>
            {
                Results.MoveFoundCommand.Execute(null);

                Assert.Equal("Require vault page open.", Main.Session.Status);
                Assert.False(Vault.IsPickerOpen);
            });
        }
        finally
        {
            PutBack();
        }
    }
}
