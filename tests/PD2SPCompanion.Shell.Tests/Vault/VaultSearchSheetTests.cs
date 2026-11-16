using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// A search on the vault's sheet:
/// the folder's sheet stays, its items on it, and the row says how many of the
/// folder were found; what was found is listed on the Search page; the tree goes on counting items, not finds; a
/// find of the vault's gone to shows its folder and selects the item, the Search page staying; and the search
/// ended, the row counts items alone.
/// <para>
/// What the vault page and the Search page decide, asked as the Search page's rows and its brush ask. That a
/// click reaches a row of it, and the folder's map - drawn, pressed to scroll the sheet - are the UI cases' to
/// show (units/vault-search-sheet.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultSearchSheetTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;
    private SearchWindowViewModel Panel => _book.Main.SearchWindow!;

    private const string Fortune = "Small Charm of Fortune";

    /// <summary>The panel's rows that are items of that name.</summary>
    private List<FoundItem> Found(string name) => [.. Panel.Finds.Rows.OfType<FoundItem>().Where(row => row.Name == name)];

    /// <summary>What is typed becomes the item search's, on its page beside the vault, and the vault page has read its folder again under it.</summary>
    private void Search(string words)
    {
        program.On(() => _book.Main.ShowSearch(search => search.SendText(words)));
        _book.Until(() => Page.PagesText.Contains("found"), $"the page did not count the finds of '{words}'");
    }

    public void Dispose() => _book.PutAway();

    [CorpusFact]
    public void A_search_leaves_the_folders_sheet_its_items_on_it_and_the_row_says_what_of_the_folder_was_found()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();

        // At the top, for something only the folders below hold.
        Search("charm");
        _book.Until(() => Found(Fortune).Count > 0, "the panel did not list the finds");

        program.On(() =>
        {
            Assert.True(Page.ShowsPages);
            Assert.Equal("1 item · 0 found", Page.PagesText);

            // The folder's own item is on the sheet still, stepped back: it is no find.
            var tome = VaultBook.Card(Page, "Tome of Town Portal");
            Assert.True(tome.IsSearchMiss);

            Assert.True(Panel.IsShown);
            Assert.Matches(@"^\d+ found$", Panel.Found);
        });
    }

    [CorpusFact]
    public void The_trees_rows_go_on_counting_items_not_finds_and_a_folder_holding_finds_counts_them_on_its_row()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        Search("charm");

        program.On(() =>
        {
            // Jewels and Magic hold one each, though the search finds one between them.
            Assert.Equal("2", VaultBook.Row(Page, "Small").ItemsText);
            Assert.Equal("1", VaultBook.Row(Page, "Grand").ItemsText);
            Assert.Equal("1", VaultBook.Row(Page, "Jewels").ItemsText);
            Assert.Equal("1", VaultBook.Row(Page, "Magic").ItemsText);

            VaultBook.GoTo(Page, "Small");
            Assert.Equal("2 items · 2 found", Page.PagesText);
            Assert.All(VaultBook.CardsOf(Page), card => Assert.False(card.IsSearchMiss));
        });
    }

    [CorpusFact]
    public void The_search_ended_the_row_counts_items_alone()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() => VaultBook.GoTo(Page, "Small"));
        Search("charm");

        program.On(() =>
        {
            Assert.Equal("2 items · 2 found", Page.PagesText);
            Panel.End();
            Assert.False(Panel.Asks);
        });

        _book.Until(() => Page.PagesText == "2 items", "the search did not end on the page");
        program.On(() => Assert.False(Page.IsSearching));
    }

    [CorpusFact]
    public void A_find_of_the_vaults_gone_to_shows_its_folder_and_selects_the_item_and_the_Search_page_stays()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        Search("fortune");

        // The character holds one of that name too, listed before the vault's.
        _book.Until(() => Found(Fortune).Any(row => row.InVault), "the vault's find was not listed");
        program.On(() =>
        {
            Assert.True(Found(Fortune).Count >= 2, "the character's and the vault's");
            Assert.Equal("Vault", VaultBook.Shown(Page));

            // A row of the panel clicked.
            Panel.Finds.ChooseCommand.Execute(Found(Fortune).Last(row => row.InVault));

            Assert.Equal(@"Vault\Charms\Small", VaultBook.Shown(Page));
            Assert.Equal("2 items · 1 found", Page.PagesText);
            Assert.Equal(Fortune, Page.Selected?.Name);

            Assert.True(Panel.IsShown);
        });
    }
}
