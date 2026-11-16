using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.Services.Character;
using PD2SPCompanion.Shell.Tests.Edit;
using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.Tests.Inventory;

/// <summary>
/// Season 14's materials tab on the Items page (the game's tabs one under another on one page,
/// scrolling, at a stash page's size): with a real S14 shared stash in the pad, one Materials row draws every
/// part under its name with the counts the game showed (screenshots); a white map put into the
/// tab lands on its own count. That the rows are drawn and take a click is the UI cases'.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class Season14MaterialsTests(Program program)
{
    private static string Season14Stash =>
        Path.Combine(Program.Corpus, "specimens", "s14-launch-2026-10-23", "session2", SharedStashSession.SoftcoreFile);

    private VP_ViewModel_Inv_L Page => program.Main.Inventory!;

    /// <summary>The pad's character opened again over the real S14 shared stash.</summary>
    private void OpenOverTheSeason14Stash()
    {
        program.On(() => program.Main.Session.Close());
        File.Copy(Season14Stash, Path.Combine(program.Folder, "saves", SharedStashSession.SoftcoreFile), overwrite: true);
        program.On(() => program.Main.Home.SwitchTo(program.SaveOf(Program.Open)));
        Assert.True(program.Until(() => program.Main.Session.IsLoaded && Page.Stash.IsLoaded, 15000), "the character did not open again");
        program.On(() =>
        {
            program.Main.ShowPageByKey("Inventory_L");
            program.Main.ShowPageByKey("Inventory_R");
        });
    }

    private static int SlotOf(string code) => Enumerable.Range(1, Materials.Every).Single(slot => Materials.CodeAt(slot) == code);

    [CorpusFact]
    public void One_Materials_page_holds_every_tab_of_the_games_at_a_stash_pages_size()
    {
        if (!program.Ready(changes: true) || !File.Exists(Season14Stash))
            return;

        try
        {
            OpenOverTheSeason14Stash();
            program.On(() =>
            {
                var materials = Assert.Single(program.Section().Pages!, page => page.IsMaterials);
                Assert.Equal("Materials", materials.Label);

                program.Panes("Materials");
                var sheet = materials.Sheet!;
                var tiles = sheet.Panels.SelectMany(panel => panel.Grid.Items).ToList();

                Assert.Equal(["Gems", "Runes", "Misc", "Tier 1 Maps", "Tier 2 Maps", "Tier 3 Maps", "Ubers"],
                             sheet.Captions.Select(caption => caption.Text));
                Assert.Equal(9, tiles.Single(tile => tile.TabSlot == SlotOf("t24")).Tally);     // Tomb of Zoltun Kulle
                Assert.Equal(10, tiles.Single(tile => tile.TabSlot == SlotOf("t34")).Tally);    // Ancestral Trial
                Assert.Equal(2, tiles.Single(tile => tile.TabSlot == SlotOf("ivea")).Tally);    // the Assassin's ears, once
                Assert.Equal(69, tiles.Single(tile => tile.TabSlot == SlotOf("rrra")).Tally);   // Infused Horadrim Orb
                Assert.Equal(32, tiles.Single(tile => tile.TabSlot == SlotOf("ubaa")).Tally);   // Sigil of Madawc
                Assert.Contains(tiles, tile => tile.TabSlot == SlotOf("ubtm"));

                // A stash page's size, scrolling inside.
                var page = program.Main.Inventory!.Stash.Pages[0];
                Assert.Equal(page.Rows * ItemTileViewModel.CellSize + 6, sheet.ViewHeight);
                Assert.True(sheet.Panels.Max(panel => panel.Top) > sheet.ViewHeight, "the tab is shorter than a page: nothing to scroll");
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }

    /// <summary>
    /// A count of Season 14's own slots, past the 98 of Season 13's table, carried over an item: what the pointer's hover
    /// asks threw out of the pointer handler and closed the program. A gem goes in; a map,
    /// which no socket takes, is only not lit.
    /// </summary>
    [CorpusFact]
    public void A_count_of_Season_14s_own_slots_carried_over_an_item_is_asked_and_a_gem_goes_in()
    {
        if (!program.Ready(changes: true) || !File.Exists(Season14Stash))
            return;

        try
        {
            OpenOverTheSeason14Stash();
            program.On(() =>
            {
                var gem = Enumerable.Range(Materials.Count + 1, Materials.Every - Materials.Count)
                    .First(slot => Socketing.FillerCode(Materials.CodeAt(slot)) is not null);
                if (Page.Stash.MaterialCount(gem) == 0)
                    Assert.Null(Page.Stash.AddMaterial(gem, 1));
                int gems = Page.Stash.MaterialCount(gem);

                program.Panes("Inventory", "Materials");
                var host = program.GivePlain("cap", 2);
                var tiles = program.Choice("Materials").Sheet!.Panels.SelectMany(panel => panel.Grid.Items).ToList();

                // A map: nothing goes in, and nothing throws.
                Assert.True(Page.BeginCarry(tiles.Single(tile => tile.TabSlot == SlotOf("t24"))));
                Assert.False(Page.WouldSocket(program.TileOf(host)));
                Page.EndTabCarry();

                Assert.True(Page.BeginCarry(tiles.Single(tile => tile.TabSlot == gem)));
                Assert.True(Page.WouldSocket(program.TileOf(host)));
                Page.DropFromTab(null, full: false, onItem: program.TileOf(host));

                Assert.Single(host.SocketedItems);
                Assert.Equal(gems - 1, Page.Stash.MaterialCount(gem));
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }

    [CorpusFact]
    public void A_white_map_put_into_the_tab_lands_on_its_own_count()
    {
        if (!program.Ready(changes: true) || !File.Exists(Season14Stash))
            return;

        try
        {
            OpenOverTheSeason14Stash();
            program.On(() =>
            {
                var map = ItemForge.Material("t24", 2, Page.Stash.ItemEra ?? "103");
                var taken = Page.Stash.Takes(map);

                Assert.Equal((SlotOf("t24"), 2), taken);
                Assert.Null(Page.Stash.AddMaterial(taken!.Value.Slot, taken.Value.Quantity));
                Assert.Equal(11, Page.Stash.MaterialCount(SlotOf("t24")));

                // A magic map stays an item, as the game's own tab left one on the pages.
                map.Quality = D2SLib.Model.Save.ItemQuality.Magic;
                Assert.Null(Page.Stash.Takes(map));
            });
        }
        finally
        {
            program.PanesAsFound();
        }
    }
}
