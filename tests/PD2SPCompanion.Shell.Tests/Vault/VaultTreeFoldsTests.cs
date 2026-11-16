using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Vault;

/// <summary>
/// The folder tree's folds: the tree opens on the vault's own folders,
/// every one of them folded - folded is how a folder is made; a folder's chevron brings its folders into the
/// tree and puts them away again, the move picker shows the same fold, and each of the two vault pages has a tree
/// of its own. The tree is unfolded by hand alone: going to a folder inside a folded one leaves it folded, and
/// lights the folded folder in its place.
/// <para>
/// What a page's tree decides while the program runs - and a fold is no more than that: the
/// program opened again, the tree is folded (deliberately not kept across sessions). That
/// is the UI case's to show, with a second start (units/vault-tree-folds.ps1).
/// </para>
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class VaultTreeFoldsTests(Program program)
{
    /// <summary>The vault on both pages of the book, each listed.</summary>
    private (VP_ViewModel_Vault_L Left, VP_ViewModel_Vault_L Right) Both()
    {
        var pages = program.On(() => (VaultHands.OnLeft(program), VaultHands.OnRight(program)));
        Assert.True(program.Until(() => pages.Item1.Places.Count > 1 && pages.Item2.Places.Count > 1), "the trees were not listed");
        return pages;
    }

    [CorpusFact]
    public void The_tree_opens_folded_and_a_chevron_brings_a_folders_folders_in_and_puts_them_away_and_the_move_picker_follows()
    {
        if (!program.Ready())
            return;

        var (left, _) = Both();
        program.On(() =>
        {
            try
            {
                VaultHands.FoldAll(left);
                Assert.True(VaultHands.Place(left, "Charms").CanFold);
                Assert.False(VaultHands.Place(left, "Charms").IsOpen);
                Assert.True(VaultHands.Place(left, "Charms").IsShown, "the vault's own folders are the tree");
                Assert.True(VaultHands.Place(left, "Jewels").IsShown);
                Assert.False(VaultHands.Place(left, "Small").IsShown, "as it opens: Small is folded away under Charms");

                left.FoldPlaceCommand.Execute(VaultHands.Place(left, "Charms"));
                Assert.True(VaultHands.Place(left, "Charms").IsOpen);
                Assert.True(VaultHands.Place(left, "Small").IsShown, "Charms unfolded: Small is in the tree");
                Assert.False(VaultHands.Place(left, "Magic").IsShown, "and Jewels as it was");

                // One fold for the tree and the picker.
                left.ClickTileCommand.Execute(VaultHands.Card(left, "Tome of Town Portal"));
                left.OpenMoveCommand.Execute(null);
                Assert.True(left.Targets.First(target => target.Name == "Small").IsShown);
                Assert.False(left.Targets.First(target => target.Name == "Magic").IsShown);
                left.ClosePickerCommand.Execute(null);

                left.FoldPlaceCommand.Execute(VaultHands.Place(left, "Charms"));
                Assert.False(VaultHands.Place(left, "Small").IsShown, "folded again: Small is out of the tree");
            }
            finally
            {
                VaultHands.FoldAll(left);
                VaultHands.Rest(program);
            }
        });
    }

    [CorpusFact]
    public void Each_vault_page_has_a_tree_of_its_own_a_fold_on_the_left_is_not_the_rights()
    {
        if (!program.Ready())
            return;

        var (left, right) = Both();
        program.On(() =>
        {
            try
            {
                VaultHands.FoldAll(left);
                VaultHands.FoldAll(right);

                left.FoldPlaceCommand.Execute(VaultHands.Place(left, "Charms"));
                Assert.True(VaultHands.Place(left, "Small").IsShown);
                Assert.False(VaultHands.Place(right, "Charms").IsOpen);
                Assert.False(VaultHands.Place(right, "Small").IsShown, "Small is not in the right page's tree");

                // And the other way: unfolded and folded on the right, the left stays unfolded.
                right.FoldPlaceCommand.Execute(VaultHands.Place(right, "Charms"));
                right.FoldPlaceCommand.Execute(VaultHands.Place(right, "Charms"));
                Assert.False(VaultHands.Place(right, "Small").IsShown);
                Assert.True(VaultHands.Place(left, "Small").IsShown, "and still in the left page's");
            }
            finally
            {
                VaultHands.FoldAll(left);
                VaultHands.FoldAll(right);
                VaultHands.Rest(program);
            }
        });
    }

    /// <summary>
    /// Many people prefer to only glance over first layer of the tree, and use the folder icon in the main pane to
    /// navigate: the tree was unfolded to the folder shown, and kept so, and the chevron of a
    /// folder the one shown lay under did nothing.
    /// </summary>
    [CorpusFact]
    public void A_folder_gone_to_inside_a_folded_one_leaves_it_folded_and_the_folded_one_is_lit_in_its_place()
    {
        if (!program.Ready())
            return;

        var (left, _) = Both();
        program.On(() =>
        {
            try
            {
                VaultHands.FoldAll(left);
                VaultHands.GoTo(left, "Small");

                var charms = VaultHands.Place(left, "Charms");
                var small = VaultHands.Place(left, "Small");
                Assert.False(charms.IsOpen, "the tree is not unfolded to the folder shown");
                Assert.False(small.IsShown);
                Assert.True(small.IsHere);
                Assert.True(charms.HoldsHere);
                Assert.True(charms.IsTreeHere, "the folded folder it lies in is lit in its place");
                Assert.False(VaultHands.Place(left, "Jewels").IsTreeHere);

                // Its chevron works as any other's: unfolded, the folder shown is lit itself.
                Assert.True(charms.CanFold);
                left.FoldPlaceCommand.Execute(charms);
                charms = VaultHands.Place(left, "Charms");
                small = VaultHands.Place(left, "Small");
                Assert.True(small.IsShown);
                Assert.True(small.IsTreeHere);
                Assert.False(charms.IsTreeHere);

                // And folded again over the folder shown, which stays the folder shown.
                left.FoldPlaceCommand.Execute(charms);
                Assert.False(VaultHands.Place(left, "Small").IsShown);
                Assert.True(VaultHands.Place(left, "Charms").IsTreeHere);
                Assert.Equal(small.Id, left.Here);
            }
            finally
            {
                VaultHands.FoldAll(left);
                VaultHands.Rest(program);
            }
        });
    }

    /// <summary>The small line under a folder's name in the tree: its items and its folders, what it has none of left unsaid.</summary>
    [CorpusFact]
    public void A_rows_line_says_how_many_items_and_how_many_folders_and_Empty_for_neither()
    {
        if (!program.Ready())
            return;

        var (left, _) = Both();
        program.On(() =>
        {
            try
            {
                Assert.Equal("2 items", VaultHands.Place(left, "Small").ItemsLine);
                Assert.Equal("1 item", VaultHands.Place(left, "Grand").ItemsLine);
                Assert.Equal("2 folders", VaultHands.Place(left, "Charms").ItemsLine);
                Assert.Equal("1 item \u00B7 1 folder", VaultHands.Place(left, "Jewels").ItemsLine);
                Assert.Equal("Empty", VaultHands.Place(left, "New folder").ItemsLine);
            }
            finally
            {
                VaultHands.Rest(program);
            }
        });
    }
}
