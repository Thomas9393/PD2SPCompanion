using D2SLib.Model.Save;

using PD2SPCompanion.Core.Saves;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Services.Vault;
using PD2SPCompanion.Shell.Tests.Home;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Search;

/// <summary>
/// The search's index is kept while its items are as they were:
/// a character's items were written into it at every run of a search,
/// and once for each character on disk - most of a run. Kept, it must still be the items as they are: written
/// again when the stamp moves or the items are others, and an item edited in place found as it now is.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SearchIndexKeptTests(Program program)
{
    private CharacterSession Session => program.Main.Session;
    private QuickSearchViewModel Bar => program.Main.QuickSearch!;
    private BasicEditor Basic => program.Get<VP_ViewModel_Inv_R>().Basic;
    private ItemTileViewModel Amulet => Session.Equipped.First(slot => slot.Slot.Location == ItemLocation.Neck && !slot.IsSwap).Item!;

    private void Ask(string typed, Func<bool> answered)
    {
        program.On(() => Bar.Text = typed);
        Assert.True(program.Until(answered), $"'{typed}' was not answered");
    }

    private bool HasMine() => Bar.Rows.OfType<FoundItem>().Any(row => row.OwnerPath is null && !row.InVault);

    [CorpusFact]
    public void The_index_is_written_once_while_its_items_stand_and_again_when_the_stamp_or_the_items_change()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var filter = new VaultFilter { Name = "charm" };
            using var search = new InventorySearch(filter);
            var items = Session.AllGrids.SelectMany(grid => grid.Items).Select(tile => tile.Item).ToList();
            var stamp = new object();

            var first = search.Matching(items, stamp)!;
            Assert.NotEmpty(first);
            Assert.Equal(1, search.Written);

            // The same items under the same stamp: only the question is asked, and it answers the same.
            Assert.True(first.SetEquals(search.Matching(items, stamp)!));
            Assert.Equal(1, search.Written);

            // Another question of the same items: still only asked.
            filter.Name = "grand";
            var grand = search.Matching(items, stamp)!;
            Assert.True(grand.Count < first.Count && grand.IsSubsetOf(first));
            Assert.Equal(1, search.Written);

            // The stamp moved: written again.
            var later = new object();
            search.Matching(items, later);
            Assert.Equal(2, search.Written);

            // The stamp standing, the items others: written again - a stamp is not trusted over what is handed in.
            search.Matching(items.Skip(1), later);
            Assert.Equal(3, search.Written);

            // No stamp: written at every run, as it always was.
            search.Matching(items);
            search.Matching(items);
            Assert.Equal(5, search.Written);
        });
    }

    /// <summary>
    /// The open character's items are asked by the window's box, the Search page and the Items page's marking:
    /// each wrote an index of its own, the same items three times after every change. One is shared now
    /// (<see cref="ItemIndex.OfOpenFiles"/>), so the second asker only asks.
    /// </summary>
    [CorpusFact]
    public void Two_searches_of_one_shared_index_write_it_once_and_each_gets_its_own_answer()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            using var index = new ItemIndex();
            var charms = new VaultFilter { Name = "charm" };
            var grands = new VaultFilter { Name = "grand" };
            using var first = new InventorySearch(charms, index);
            using var second = new InventorySearch(grands, index);
            var items = Session.AllGrids.SelectMany(grid => grid.Items).Select(tile => tile.Item).ToList();
            var stamp = new object();

            var charm = first.Matching(items, stamp)!;
            var grand = second.Matching(items, stamp)!;
            Assert.Equal(1, index.Written);
            Assert.NotEmpty(grand);
            Assert.True(grand.Count < charm.Count && grand.IsSubsetOf(charm));

            // Turn about, and neither writes.
            Assert.True(charm.SetEquals(first.Matching(items, stamp)!));
            Assert.True(grand.SetEquals(second.Matching(items, stamp)!));
            Assert.Equal(1, index.Written);

            // A search let go does not take the shared index with it.
            first.Dispose();
            Assert.True(grand.SetEquals(second.Matching(items, stamp)!));
        });
    }

    [CorpusFact]
    public void A_save_on_disk_has_an_index_of_its_own_written_once_until_its_file_changes()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            string path = program.SaveOf(Program.Open);
            var held = SavedItems.Of(path, Session.UseMyLayout)!;
            var filter = new VaultFilter { Name = "charm" };
            using var saved = new SavedSearches(filter);
            try
            {
                SaveVerifier.Install(held.Format);
                var first = saved.Matching(path, held)!;
                Assert.NotEmpty(first);
                Assert.Equal(1, saved.Written);

                filter.Name = "grand";
                Assert.NotEmpty(saved.Matching(path, held)!);
                Assert.Equal(1, saved.Written);

                // Another save: its own index, the first one's standing.
                saved.Matching(path + ".other", held);
                Assert.Equal(2, saved.Written);
                saved.Matching(path, held);
                Assert.Equal(2, saved.Written);
            }
            finally
            {
                Session.UseMyLayout();
            }
        });
    }

    [CorpusFact]
    public void An_item_edited_in_place_is_found_by_the_next_search_as_it_now_is()
    {
        if (!program.Ready(changes: true))
            return;

        using var mode = Made.Mode(program, "Casual");

        // Nobody's yet.
        Ask("zedd", () => Bar.IsListOpen);
        program.On(() => Assert.False(HasMine()));

        // A name put on the amulet - the item itself changed, the same item in the same place.
        program.On(() =>
        {
            Session.SelectedItem = Amulet;
            Basic.PersonalName = "Zedd";
            Assert.True(Amulet.Item.IsPersonalized);
        });
        Ask("zedd's", () => Bar.IsListOpen && HasMine());

        // And taken off again: not found.
        program.On(() => Basic.PersonalName = "");
        Ask("zedd", () => Bar.IsListOpen && !HasMine());

        program.On(() => Bar.Close());
    }

    [CorpusFact]
    public void The_other_characters_indexes_are_written_ahead_and_a_search_then_writes_none()
    {
        if (!program.Ready())
            return;

        // As start-up asks it, at idle: every other save read and its index written.
        var ahead = program.On(() => Bar.PrepareOthers());
        Assert.True(program.Until(() => ahead.IsCompleted, 15000), "the other characters' indexes were not written ahead");

        int written = program.On(() => Bar.OthersWritten);
        Assert.True(written > 0);

        // A search finds what the other character holds, and writes no index for it.
        Ask("stone barb", () => Bar.Rows.OfType<FoundItem>().Any(row => row.Name == "Stone Barb" && row.OwnerPath is not null));
        program.On(() =>
        {
            Assert.Equal(written, Bar.OthersWritten);
            Bar.Close();
        });
    }
}
