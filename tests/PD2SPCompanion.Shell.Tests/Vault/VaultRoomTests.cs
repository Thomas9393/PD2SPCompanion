using PD2SPCompanion.Shell.Services.Settings;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The room a vault page gives its parts, and the grips.
/// Whether a page is too narrow, and where a grip has been
/// dragged to, are the view's to work out; what the page makes of them is proven here: told it has no room it
/// puts the map and the tree away and says why on their buttons, the toggles kept; the tree keeps between its
/// least and its most width and, dragged shut, is its edge alone; and it comes back by its button at the width
/// it had. (A folder's folders are tiles of the sheet - VaultFolderTilesTests.)
/// <para>
/// Everything measured - the tools on one line with a "more" button, ten columns of the sheet kept in sight,
/// how far a grip goes and when the tree shuts under it - is the UI cases' to show (units/vault-narrow.ps1,
/// units/vault-grips.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultRoomTests(Program program) : IDisposable
{
    private readonly VaultBook _book = new(program);

    private VP_ViewModel_Vault_L Page => _book.Left;

    public void Dispose() => _book.PutAway();

    [CorpusFact]
    public void Told_it_has_no_room_the_page_puts_the_map_and_the_tree_away_and_their_buttons_say_why_the_toggles_kept()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            Assert.True(Page.ShowsMapNow);
            Assert.True(Page.ShowsTreeNow);
            Assert.Equal("Minimap", Page.MapHint);
            Assert.Equal("Folder tree", Page.TreeHint);

            Page.RoomHidesMap = true;
            Page.RoomHidesTree = true;

            Assert.False(Page.ShowsMapNow);
            Assert.False(Page.ShowsTreeNow);
            Assert.Equal("Minimap" + Environment.NewLine + "Hidden: page too narrow", Page.MapHint);
            Assert.Equal("Folder tree" + Environment.NewLine + "Hidden: page too narrow", Page.TreeHint);

            // What was asked for is kept: it all comes back with the room.
            Assert.True(Page.IsMinimap);
            Assert.True(Page.ShowsTree);

            Page.RoomHidesMap = false;
            Page.RoomHidesTree = false;
            Assert.True(Page.ShowsMapNow);
            Assert.True(Page.ShowsTreeNow);
            Assert.Equal("Minimap", Page.MapHint);
        });
    }

    [CorpusFact]
    public void The_tree_keeps_between_its_least_and_its_most_width_and_dragged_shut_is_its_edge_alone()
    {
        if (!program.Ready())
            return;

        _book.OpenLeft();
        program.On(() =>
        {
            Assert.Equal(VaultLook.DefaultTreeWidth, Page.TreeWidth);

            Page.TreeWidth = 10;
            Assert.Equal(VaultLook.NarrowestTree, Page.TreeWidth);
            Page.TreeWidth = 5000;
            Assert.Equal(VaultLook.WidestTree, Page.TreeWidth);

            Page.TreeWidth = 250;
            Assert.Equal(250, Page.TreePaneWidth);

            // Held past half its least width: shut under the pointer, the tree still asked for.
            Page.TreeDraggedShut = true;
            Assert.Equal(VaultLook.EdgeWidth, Page.TreePaneWidth);
            Assert.True(Page.ShowsTree);

            // Let go of there, as the view lets go: put away - and back by its button as wide as it was.
            Page.TreeDraggedShut = false;
            Page.ShowsTree = false;
            Assert.False(Page.ShowsTreeNow);

            Page.ToggleTreeCommand.Execute(null);
            Assert.True(Page.ShowsTreeNow);
            Assert.Equal(250, Page.TreePaneWidth);
        });
    }
}
