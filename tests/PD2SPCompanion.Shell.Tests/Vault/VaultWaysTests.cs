using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The ways about the vault:
/// the path over the page, each step a way to it and the chevron after a step the folders it
/// holds; Back, Forward and Up over where the page has been; the tree's rows and the folders' tiles on the
/// sheet, gone to and dropped onto; and the two buttons that put the tree and the map away.
/// <para>
/// What the page decides, asked of it as the view asks (GoToPlace, GoToCrumb, ListUnder, the commands,
/// BeginCarry and DropCarriedOnFolder) and read back from the page and the vault. That a click reaches a
/// row, a step or a tile, that Alt and an arrow reach the page, that a drag is let go of on the folder under
/// the pointer and how wide the tree's edge can be dragged are the UI cases' to show (units/vault-ways.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultWaysTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    private string Shown => VaultBook.Shown(Page);

    private void GoTo(string folder) => VaultBook.GoTo(Page, folder);

    private FolderCrumb Crumb(string name) => Page.Crumbs.First(crumb => crumb.Name == name);

    /// <summary>The folders the folder shown holds, as their tiles on its sheet name them.</summary>
    private List<string> Tiles => [.. Page.SheetFolders.Select(tile => tile.Name)];

    public void Dispose() => _book.PutAway();

    [CorpusFact]
    public void The_page_opens_at_the_vaults_top_with_nowhere_to_go_back_forward_or_up()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            Assert.Equal("Vault", Shown);
            Assert.False(Page.CanGoBack);
            Assert.False(Page.CanGoForward);
            Assert.False(Page.CanGoUp);
        });
    }

    [CorpusFact]
    public void A_row_of_the_tree_shows_its_folder_and_from_there_Back_and_Up_lead_somewhere_Forward_nowhere()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            GoTo("Small");

            Assert.Equal(@"Vault\Charms\Small", Shown);
            Assert.Equal(["Vault", "Charms", "Small"], Page.Crumbs.Select(crumb => crumb.Name));
            Assert.True(Page.CanGoBack);
            Assert.True(Page.CanGoUp);
            Assert.False(Page.CanGoForward);
        });
    }

    [CorpusFact]
    public void A_step_of_the_path_is_gone_to_its_folders_are_tiles_of_its_sheet_and_a_tile_is_a_way_in()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            GoTo("Small");
            Page.GoToCrumb(Crumb("Charms"));

            Assert.Equal(@"Vault\Charms", Shown);
            Assert.Equal(["Grand", "Small"], Tiles);

            Page.OpenFolderTile(Page.SheetFolders.First(tile => tile.Name == "Grand"));
            Assert.Equal(@"Vault\Charms\Grand", Shown);

            // A folder holding no folders has no tiles.
            Assert.Empty(Tiles);
        });
    }

    [CorpusFact]
    public void Back_and_Forward_go_over_where_the_page_has_been_and_Up_to_the_folder_above()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            GoTo("Small");
            Page.GoToCrumb(Crumb("Charms"));
            GoTo("Grand");

            Page.GoBackCommand.Execute(null);
            Assert.Equal(@"Vault\Charms", Shown);

            Page.GoBackCommand.Execute(null);
            Assert.Equal(@"Vault\Charms\Small", Shown);

            Page.GoForwardCommand.Execute(null);
            Assert.Equal(@"Vault\Charms", Shown);

            Page.GoUpCommand.Execute(null);
            Assert.Equal("Vault", Shown);
            Assert.False(Page.CanGoUp);
        });
    }

    [CorpusFact]
    public void A_folder_gone_to_after_going_back_leaves_no_way_forward()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            GoTo("Charms");
            Page.GoUpCommand.Execute(null);

            Page.GoBackCommand.Execute(null);
            Assert.Equal(@"Vault\Charms", Shown);
            Assert.True(Page.CanGoForward);

            GoTo("Jewels");
            Assert.False(Page.CanGoForward);
        });
    }

    [CorpusFact]
    public void The_chevron_after_a_step_lists_that_steps_folders_and_one_picked_is_gone_to_the_list_shut()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            GoTo("Jewels");
            Page.ListUnder(Crumb("Vault"));

            Assert.True(Page.IsMenuOpen);
            var listed = Page.MenuPlaces.Select(place => place.Name).ToList();
            Assert.Contains("Charms", listed);
            Assert.Contains("Unique Weapons", listed);

            // Jewels' own folder is not the top's.
            Assert.DoesNotContain("Magic", listed);

            Page.GoToPlace(Page.MenuPlaces.First(place => place.Name == "Charms"));
            Assert.Equal(@"Vault\Charms", Shown);
            Assert.False(Page.IsMenuOpen);
        });
    }

    [CorpusFact]
    public void With_the_tree_put_away_the_list_at_the_paths_end_goes_anywhere()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            GoTo("Jewels");
            Assert.True(Page.ShowsTreeNow);

            Page.ToggleTreeCommand.Execute(null);
            Assert.False(Page.ShowsTreeNow);

            Page.ListEverything();
            Assert.True(Page.IsMenuOpen);
            Page.GoToPlace(Page.MenuPlaces.First(place => place.Name == "Magic"));
            Assert.Equal(@"Vault\Jewels\Magic", Shown);

            Page.ToggleTreeCommand.Execute(null);
            Assert.True(Page.ShowsTreeNow);
        });
    }

    [CorpusFact]
    public void The_map_is_put_away_and_brought_back_by_its_button()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            GoTo("Magic");
            Page.GoUpCommand.Execute(null);
            Assert.Equal(@"Vault\Jewels", Shown);
            Assert.Equal(["Magic"], Tiles);

            Assert.True(Page.ShowsMapNow);
            Page.ToggleMinimapCommand.Execute(null);
            Assert.False(Page.ShowsMapNow);
            Page.ToggleMinimapCommand.Execute(null);
            Assert.True(Page.ShowsMapNow);
        });
    }

    [CorpusFact]
    public void A_card_let_go_of_on_a_row_of_the_tree_moves_there_and_the_page_stays_where_it_was()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            GoTo("Small");
            Assert.Equal("2 items", Page.PagesText);
            Assert.Equal("1", VaultBook.Row(Page, "Grand").ItemsText);

            // The drag as the view hands it over: the tile taken hold of, then the folder under the pointer.
            Assert.True(Page.BeginCarry(VaultBook.Card(Page, "Small Charm of Fortune")));
            Page.DropCarriedOnFolder(VaultBook.Row(Page, "Grand"));

            Assert.False(Page.IsCarrying);
            Assert.Equal("Moved to Grand.", Page.Notice);
            Assert.Equal("1 item", Page.PagesText);
            Assert.Equal("2", VaultBook.Row(Page, "Grand").ItemsText);
            Assert.Equal(@"Vault\Charms\Small", Shown);
            Assert.Equal(2, _book.Holds("Grand"));
        });
    }

    [CorpusFact]
    public void A_card_let_go_of_on_a_folders_tile_moves_into_that_folder()
    {
        if (!program.Ready(changes: true))
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            Assert.Equal("1 item", Page.PagesText);
            Assert.Equal("1", VaultBook.Row(Page, "Jewels").ItemsText);

            Assert.True(Page.BeginCarry(VaultBook.Card(Page, "Tome of Town Portal")));
            Page.DropCarriedOnFolder(Page.SheetFolders.First(tile => tile.Name == "Jewels").Place);

            Assert.Equal("0 items", Page.PagesText);
            Assert.Equal("2", VaultBook.Row(Page, "Jewels").ItemsText);
            Assert.Equal("Vault", Shown);
            Assert.Equal(2, _book.Holds("Jewels"));
        });
    }
}
