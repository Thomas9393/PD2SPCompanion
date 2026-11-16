using Xunit;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// The shared stash's layouts by version (<see cref="SharedStash.Layout"/>): Season 13's version 2 and Season 14's
/// version 3, each on a file the game wrote. The S14 file is a real one after the season's first session, copied;
/// its counts are the ones screenshots of the tabs show.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SharedStashLayoutTests(Game game)
{
    private static string Season14Stash =>
        Path.Combine(Game.Corpus, "specimens", "s14-launch-2026-10-23", "live", "pd2_shared.stash");

    private static string Season13Stash =>
        Path.Combine(Game.Corpus, "specimens", "s14-launch-2026-10-23", "before", "pd2_shared.stash");

    [CorpusFact]
    public void A_season_14_stash_reads_as_version_3_and_writes_back_exactly()
    {
        if (!game.Available || !File.Exists(Season14Stash))
            return;

        byte[] bytes = File.ReadAllBytes(Season14Stash);
        var stash = SharedStash.Read(bytes, "S14 stash");

        Assert.Same(SharedStash.Layout.Version3, stash.Shape);
        Assert.Equal(56, stash.Items.Items?.Count);
        Assert.Equal(bytes, stash.Write());
    }

    [CorpusFact]
    public void A_season_14_stash_says_the_uniques_the_game_discovered_by_their_row()
    {
        string Discovery(string state) => Path.Combine(Game.Corpus, "specimens", "s14-discovery", state, "pd2_shared.stash");
        if (!game.Available || !File.Exists(Discovery("after-flamebellow")))
            return;

        // Nothing, then Coif of Glory (row 73), then Flamebellow (row 353) - real finds.
        Assert.Empty(SharedStash.Read(Discovery("before")).DiscoveredUniques);
        Assert.Equal([73], SharedStash.Read(Discovery("after-coif")).DiscoveredUniques);
        Assert.Equal([73, 353], SharedStash.Read(Discovery("after-flamebellow")).DiscoveredUniques.Order());
        Assert.Equal([73, 353], SharedStash.DiscoveredUniquesIn(Discovery("after-flamebellow")).Order());
        Assert.Empty(SharedStash.DiscoveredUniquesIn(Path.Combine(Path.GetTempPath(), "no-such.stash")));

        // Season 13's file keeps none.
        var old = SharedStash.Read(Season13Stash);
        Assert.False(old.KeepsDiscovery);
        Assert.Empty(old.DiscoveredUniques);
    }

    [CorpusFact]
    public void A_season_14_stash_says_the_set_pieces_the_game_discovered_from_bit_2048_and_the_uniques_below_it()
    {
        string specimen = Path.Combine(Game.Corpus, "specimens", "s14-discovery", "two-sets", "pd2_shared.stash");
        if (!game.Available || !File.Exists(specimen))
            return;

        // A stash at the game's "1% (2/128)": Immortal King's Soul Cage (row 71) at bit 2119 and
        // Stone Crusher (row 75) at bit 2123 - and none of the two among the uniques.
        var discovered = SharedStash.DiscoveredIn(specimen);
        Assert.Equal([71, 75], discovered.Sets.Order());
        Assert.Equal(discovered.Sets.Order(), SharedStash.Read(specimen).Discovered.Sets.Order());
        Assert.All(discovered.Uniques, row => Assert.InRange(row, 0, SharedStash.SetsFrom - 1));
        Assert.Contains(73, discovered.Uniques);
        Assert.Empty(SharedStash.DiscoveredIn(Path.Combine(Path.GetTempPath(), "no-such.stash")).Sets);
    }

    [CorpusFact]
    public void A_season_14_stash_counts_where_the_game_shows_them()
    {
        if (!game.Available || !File.Exists(Season14Stash))
            return;

        var stash = SharedStash.Read(Season14Stash);

        Assert.Equal(1_266_036u, stash.Gold);
        Assert.Equal(66, stash.MaterialCount(Materials.SlotFor("r13s")!.Value));
        Assert.Equal(83, stash.MaterialCount(Materials.SlotFor("glrs")!.Value));
        Assert.Equal(5, stash.MaterialCount(Materials.SlotFor("lucd")!.Value));
        Assert.Equal(221, stash.MaterialCount(Materials.SlotFor("rvl")!.Value));
        Assert.Equal(157, stash.MaterialCount(Materials.SlotFor("jewf")!.Value));
        Assert.Equal(69, stash.MaterialCount(Materials.SlotFor("rrra")!.Value));
    }

    [CorpusFact]
    public void A_count_set_on_a_season_14_stash_lands_in_its_own_slot_only()
    {
        if (!game.Available || !File.Exists(Season14Stash))
            return;

        byte[] bytes = File.ReadAllBytes(Season14Stash);
        var stash = SharedStash.Read(bytes, "S14 stash");
        int slot = Materials.SlotFor("wss")!.Value;

        stash.SetMaterialCount(slot, 168);
        stash.SetGold(1_266_037);
        byte[] written = stash.Write();

        // The shard's u16 at 0xF6, the gold's lowest byte at 0x134, the checksum - and nothing else.
        var changed = Enumerable.Range(0, bytes.Length).Where(at => bytes[at] != written[at]).ToList();
        Assert.Equal([0xF6, 0x134], changed.Where(at => at is < 0x0C or > 0x0F));
        Assert.Equal(168, SharedStash.Read(written, "again").MaterialCount(slot));
    }

    [CorpusFact]
    public void Season_14s_tab_hands_out_the_plain_rune_and_gem_and_13s_the_stack()
    {
        if (!game.Available || !File.Exists(Season14Stash) || !File.Exists(Season13Stash))
            return;

        var now = SharedStash.Read(Season14Stash);
        var then = SharedStash.Read(Season13Stash);
        int nef = Materials.SlotFor("r04s")!.Value, topaz = Materials.SlotFor("gpys")!.Value, shard = Materials.SlotFor("wss")!.Value;

        Assert.Equal(["r04", "gpy", "wss"], new[] { now.MaterialCode(nef), now.MaterialCode(topaz), now.MaterialCode(shard) });
        Assert.Equal(["r04s", "gpys", "wss"], new[] { then.MaterialCode(nef), then.MaterialCode(topaz), then.MaterialCode(shard) });
    }

    [CorpusFact]
    public void A_rune_and_a_gem_made_for_season_14s_tab_are_the_games_own()
    {
        // NewS after a Nef Rune and a Perfect Topaz were taken out of the S14 tab.
        // Season 14's own tables only: under S13's a rune is compact, and a file holding S14's plain one does not read.
        string taken = Path.Combine(Game.Corpus, "specimens", "s14-launch-2026-10-23", "session2", "NewS.d2s");
        if (!game.Available || Game.Tables is not null || !File.Exists(taken))
            return;

        var (save, _) = SaveVerifier.Load(taken);
        int bits = D2SLib.Globals.stat_id_bits;

        byte[] Blob(D2SLib.Model.Save.Item item)
        {
            var copy = ItemBlob.Copy(item, bits);
            copy.IsNew = false;
            ItemBlob.Normalise(copy);
            Assert.True(ItemBlob.TryCapture(copy, bits, out var blob, out _, out _));
            return blob!;
        }

        foreach (string code in new[] { "r04", "gpy" })
        {
            var given = save.PlayerItemList.Items.Single(item => item.Code.Trim() == code);
            Assert.Equal(Blob(given), Blob(ItemForge.Material(code, 1, given.Version)));
        }
    }

    /// <summary>
    /// The numbered file read in game: every material count holds its file slot's number, every
    /// count of the second block 200 + its slot. Each pair below is a cell of the screenshots - what the item in
    /// it is, and the number it showed.
    /// </summary>
    [CorpusTheory]
    [InlineData("r04s", 4)]       // Runes: Nef
    [InlineData("glrs", 39)]      // Gems: Flawless Ruby
    [InlineData("crfv", 54)]      // Gems: Vampiric infusion
    [InlineData("lucd", 72)]      // Ubers: Flesh of Malic
    [InlineData("ubtm", 73)]      // Ubers: the pentagram, under the organs
    [InlineData("uba", 74)]       // Ubers: bottom row, first
    [InlineData("rtma", 76)]      // Ubers: the blue voidstone
    [InlineData("luca", 77)]
    [InlineData("rvs", 78)]       // Misc: the two potions
    [InlineData("rvl", 79)]
    [InlineData("imrn", 80)]      // Misc: the cube
    [InlineData("fed", 89)]       // Misc: the green essence
    [InlineData("std", 90)]       // Maps, lower panel: the standard
    [InlineData("rera", 103)]
    [InlineData("ivea", 104)]     // the ears
    [InlineData("ives", 110)]
    [InlineData("toa", 111)]      // Misc: the "T"
    [InlineData("llmr", 112)]     // Misc: the mirror
    [InlineData("lsvl", 113)]     // Misc: the vial
    [InlineData("t11", 201)]      // Maps tier 1, first
    [InlineData("t12", 209)]      // Maps tier 2, first
    [InlineData("t17", 217)]
    [InlineData("t2a", 229)]      // Maps tier 2, second row
    [InlineData("t13", 218)]      // Maps tier 3, first
    [InlineData("t3b", 226)]
    [InlineData("gcy", 261)]      // Gems: Chipped Topaz
    [InlineData("gcr", 266)]      // Gems: Chipped Ruby
    [InlineData("skf", 274)]      // Gems: Flawed Skull
    [InlineData("sku", 281)]      // Gems: Skull
    public void Each_slot_reads_the_number_the_game_showed_in_its_cell(string code, int shown)
    {
        string numbered = Path.Combine(Game.Corpus, "specimens", "s14-launch-2026-10-23", "numbered", "pd2_shared.stash");
        if (!game.Available || !File.Exists(numbered))
            return;

        var stash = SharedStash.Read(numbered);
        int slot = Enumerable.Range(1, Materials.Every).Single(each => Materials.CodeAt(each) == code);

        Assert.True(stash.Holds(slot));
        Assert.Equal(shown, stash.MaterialCount(slot));
    }

    [CorpusFact]
    public void A_season_13_stash_keeps_none_of_what_14_added()
    {
        if (!game.Available || !File.Exists(Season13Stash))
            return;

        var stash = SharedStash.Read(Season13Stash);
        int map = Materials.Count + Materials.Season14Codes.ToList().IndexOf("t11") + 1;

        Assert.False(stash.Holds(map));
        Assert.Equal(0, stash.MaterialCount(map));
        Assert.Throws<InvalidOperationException>(() => stash.SetMaterialCount(map, 1));
        Assert.True(stash.Holds(Materials.Count));
    }

    [CorpusFact]
    public void Each_seasons_stash_holds_its_own_most_gold()
    {
        if (!game.Available || !File.Exists(Season14Stash) || !File.Exists(Season13Stash))
            return;

        // "Increased shared gold stash cap to 25 million" - the S14 patch notes; 10 million in S13.
        var now = SharedStash.Read(Season14Stash);
        var then = SharedStash.Read(Season13Stash);

        now.SetGold(25_000_000);
        Assert.Equal(25_000_000u, SharedStash.Read(now.Write(), "again").Gold);
        Assert.Throws<ArgumentOutOfRangeException>(() => now.SetGold(25_000_001));
        Assert.Throws<ArgumentOutOfRangeException>(() => then.SetGold(10_000_001));
    }

    [CorpusFact]
    public void A_season_13_stash_still_reads_as_version_2()
    {
        if (!game.Available || !File.Exists(Season13Stash))
            return;

        byte[] bytes = File.ReadAllBytes(Season13Stash);
        var stash = SharedStash.Read(bytes, "S13 stash");

        Assert.Same(SharedStash.Layout.Version2, stash.Shape);
        Assert.Equal(100_000u, stash.Gold);
        Assert.Equal(bytes, stash.Write());
    }
}
