using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>Small, clear findings, each pinned once it was right.</summary>
[Collection(nameof(GameTables))]
public sealed class ReviewSmallFixesTests(Game game)
{
    private const string Stamp = "105";

    /// <summary>
    /// A bonus repeated across tiers says its own tier. Aldur's Gauntlet has one attribute on 2, 3 and 4 pieces;
    /// asked by the stat alone, every row said the first.
    /// </summary>
    [GameFact]
    public void A_set_bonus_repeated_across_tiers_says_each_tiers_own_pieces()
    {
        if (!game.Available)
            return;

        var row = ItemForge.SetItems().Select(each => ItemForge.SetItemAt(each, Stamp).Item).FirstOrDefault(piece =>
            piece.StatLists.Skip(1).SelectMany(list => list.Stats).GroupBy(stat => stat.Stat).Any(same => same.Count() > 1));
        Assert.NotNull(row);

        int tier = 0, list = 0;
        var pieces = new List<int?>();
        string repeated = row.StatLists.Skip(1).SelectMany(each => each.Stats).GroupBy(stat => stat.Stat).First(same => same.Count() > 1).Key!;
        for (tier = 1; tier <= 5; tier++)
        {
            if ((row.SetItemMask & (1 << (tier - 1))) == 0)
                continue;

            list++;
            if (row.StatLists[list].Stats.Any(stat => stat.Stat == repeated))
            {
                Assert.Equal(tier + 1, AffixRanges.SetPiecesFor(row, list, repeated));
                pieces.Add(tier + 1);
            }
        }

        Assert.True(pieces.Count > 1);
        Assert.Equal(pieces.Count, pieces.Distinct().Count());
    }

    /// <summary>The green lists are settled as the item's own: nothing out of id order, on any set piece.</summary>
    [GameFact]
    public void A_forged_set_pieces_green_lists_are_in_id_order()
    {
        if (!game.Available)
            return;

        foreach (int row in ItemForge.SetItems())
        {
            var piece = ItemForge.SetItemAt(row, Stamp).Item;
            foreach (var list in piece.StatLists)
            {
                var ids = list.Stats.Select(stat => (int)stat.Id).ToList();
                Assert.Equal(ids.Order().ToList(), ids);
            }
        }
    }

    /// <summary>What a pin answers is not kept for the same bytes without the pin.</summary>
    [GameFact]
    public void A_verdict_under_a_pin_is_not_remembered_by_the_bytes()
    {
        if (!game.Available)
            return;

        // Something to find - a base the game never makes ethereal - so a verdict is a list of its own and can be told
        // from a remembered one.
        var item = AffixSweepTests.Fresh("sbw");
        item.IsEthereal = true;
        item.Version = Stamp;
        var plain = ItemBlob.Copy(item, D2SLib.Globals.stat_id_bits);

        Desecrations.Assume(item, Corruptions.Available(AffixSweepTests.Fresh("sbw"))[0]);
        Assert.True(Desecrations.IsPinned(item));

        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps("105", "103");
        try
        {
            Assert.NotEmpty(Legitimacy.Check(item));
            Assert.NotSame(Legitimacy.Check(item), Legitimacy.Check(item));

            // The copy with no pin is remembered as any item is.
            Assert.False(Desecrations.IsPinned(plain));
            Assert.Same(Legitimacy.Check(plain), Legitimacy.Check(plain));
        }
        finally
        {
            SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp);
        }
    }

    /// <summary>A Season 14 rune stacks in its own code; a stack of thirty counted as one would lose twenty-nine.</summary>
    [GameFact]
    public void A_plain_rune_stack_counts_its_quantity()
    {
        if (!game.Available || !Stacks.IsStackable("r08"))
            return;

        var stack = ItemForge.PlainBase("r08", 0, Stamp).Item;
        stack.Quantity = 30;
        Assert.Equal(Stacks.Shown(30), Runewords.Stock([stack])[8]);
        Assert.True(Runewords.Stock([stack])[8] > 1);
    }

    /// <summary>The bit after durability is kept, so a file holding it set goes on reading byte for byte.</summary>
    [GameTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_spare_bit_after_durability_reads_back(bool bit)
    {
        if (!game.Available)
            return;

        var armor = ItemForge.PlainBase("uar", 0, Stamp).Item;
        Assert.True(armor.MaxDurability > 0);
        armor.DurabilitySpareBit = bit;

        Assert.Equal(bit, ItemBlob.Copy(armor, D2SLib.Globals.stat_id_bits).DurabilitySpareBit);
    }

    /// <summary>The captions follow the backups a rename moved; a name that only starts alike keeps its own.</summary>
    [GameFact]
    public void A_rename_moves_the_captions_with_the_backups()
    {
        string folder = Path.Combine(Path.GetTempPath(), "pd2spc-captions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllLines(Path.Combine(folder, SaveWriter.CaptionsFile),
            [
                "Old.d2s.20261101.bak\tBefore: a tome stored.",
                "Older.d2s.20261101.bak\tSomeone else's.",
            ]);

            SaveWriter.RenameCaptions(folder, "Old.d2s.", "New.d2s.");

            var captions = SaveWriter.Captions(folder);
            Assert.Equal("Before: a tome stored.", captions["New.d2s.20261101.bak"]);
            Assert.Equal("Someone else's.", captions["Older.d2s.20261101.bak"]);
            Assert.False(captions.ContainsKey("Old.d2s.20261101.bak"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
