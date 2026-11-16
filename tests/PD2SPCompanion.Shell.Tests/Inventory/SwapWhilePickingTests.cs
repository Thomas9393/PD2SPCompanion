using PD2SPCompanion.Shell.Tests.Vault;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Swap trades the two panes' pages while items are picked, and they stay picked: it went
/// dead while picking, for the arrow buttons that moved the picks across, which are gone. The picks are kept by
/// item, so they go with their items to the other page.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class SwapWhilePickingTests(Program program)
{
    [CorpusFact]
    public void Two_picked_cross_with_their_page_and_stay_picked()
    {
        if (!program.Ready(changes: true))
            return;

        program.On(() =>
        {
            var page = program.Page();
            var section = program.Section();
            program.Panes("Personal Stash", "Shared Page 8");
            var tiles = program.Grid("Personal Stash").Items.Take(2).ToList();
            Assert.True(tiles.Count == 2, "the personal stash holds two items to pick");
            var items = tiles.Select(tile => tile.Item).ToList();

            VaultHands.CtrlClick(page, tiles[0]);
            VaultHands.CtrlClick(page, tiles[1]);
            Assert.True(page.IsPicking);
            Assert.Equal(2, page.PickedCount);
            Assert.True(section.CanSwap, section.SwapHint);

            page.SwapPanesCommand.Execute(section);

            var moved = program.Grid("Shared Page 8").Items.Where(tile => items.Contains(tile.Item)).ToList();
            Assert.Equal(2, moved.Count);
            Assert.True(page.IsPicking);
            Assert.Equal(2, page.PickedCount);
            Assert.All(moved, tile => Assert.True(tile.IsPicked, $"{tile.Name} is picked still"));
        });
    }
}
