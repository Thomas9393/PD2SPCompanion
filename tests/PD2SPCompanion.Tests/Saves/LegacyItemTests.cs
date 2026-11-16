using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// Items of the season before kept as that season made them (<see cref="ItemSeason"/>): no migration for
/// an existing item, marked legacy, its legality no longer checked. Told by the stamp
/// alone - S14 writes 105, S13 wrote 103 - so these hold under either season's tables.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class LegacyItemTests(Game game)
{
    private static T WithStamps<T>(Func<T> run)
    {
        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps("105", "103");
        try { return run(); }
        finally { SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp); }
    }

    [CorpusFact]
    public void A_legacy_tooltip_prints_this_seasons_green_ranges_as_the_game_does()
    {
        if (!game.Available || Game.Tables is not null || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // LegacyTest's Saracen's Chance, Season 13's stamp, read in Season 14 (screenshot):
        // "+44% Enhanced Damage [30 - 50]", "All Resistances +25 [15 - 25]".
        var lines = WithStamps(() =>
        {
            var amulet = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "LegacyTestCopies.d2s")).Save.PlayerItemList.Items
                .First(item => item.Code.Trim() == "amu" && item.Quality == ItemQuality.Unique);
            Assert.Equal("103", amulet.Version);
            return PD2SPCompanion.Core.Items.ItemLines.Of(amulet, ItemPresenter.Describe(amulet), out _, out _);
        });
        Assert.Contains(lines, line => line.Text.Contains("Enhanced Damage", StringComparison.Ordinal) && line.Range.Contains("30") && line.Range.Contains("50"));
        Assert.Contains(lines, line => line.Text.Contains("All Resistances", StringComparison.Ordinal) && line.Range.Contains("15") && line.Range.Contains("25"));
    }

    /// <summary>An ethereal short bow - a base the game never makes ethereal, so an impossible item - at a stamp.</summary>
    private static Item EtherealBow(string stamp)
    {
        var item = PD2SPCompanion.Tests.Items.AffixSweepTests.Fresh("sbw");
        item.IsEthereal = true;
        item.Version = stamp;
        return item;
    }

    [CorpusFact]
    public void The_season_befores_stamp_is_legacy_and_this_seasons_and_an_older_one_are_not()
    {
        if (!game.Available)
            return;

        WithStamps(() =>
        {
            Assert.True(ItemSeason.IsLegacy(EtherealBow("103")));
            Assert.False(ItemSeason.IsLegacy(EtherealBow("105")));
            Assert.False(ItemSeason.IsLegacy(EtherealBow("101")));
            Assert.StartsWith("Legacy", ItemSeason.Mark(EtherealBow("103")));
            Assert.Null(ItemSeason.Mark(EtherealBow("105")));
            return 0;
        });

        // Neither season's stamp known: nothing is legacy.
        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps(null, null);
        try { Assert.False(ItemSeason.IsLegacy(EtherealBow("103"))); }
        finally { SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp); }
    }

    [CorpusFact]
    public void A_legacy_item_is_not_checked_and_the_same_item_of_this_season_is()
    {
        if (!game.Available)
            return;

        WithStamps(() =>
        {
            Assert.Empty(Legitimacy.Check(EtherealBow("103")));
            Assert.Contains(Legitimacy.Check(EtherealBow("105")), finding => finding.Certainty == Certainty.Impossible);

            // The rules themselves still judge one: what tells a file's two readings apart (SeasonReading).
            Assert.NotEmpty(Legitimacy.Judge(EtherealBow("103")));
            return 0;
        });
    }

    [CorpusFact]
    public void This_seasons_and_the_season_befores_items_mix_in_one_file_and_no_older_era_does()
    {
        WithStamps(() =>
        {
            Assert.True(ItemSeason.Mix("105", "103"));
            Assert.True(ItemSeason.Mix("103", "105"));
            Assert.True(ItemSeason.Mix("101", "101"));
            Assert.False(ItemSeason.Mix("105", "101"));
            Assert.False(ItemSeason.Mix("101", "103"));
            Assert.True(ItemSeason.Mix(null, "101"));
            return 0;
        });
    }

    [CorpusFact]
    public void A_new_item_takes_this_seasons_stamp_wherever_the_file_may_hold_it()
    {
        WithStamps(() =>
        {
            // An S13 character takes a new item at 105, beside its own; a pre-S10 one keeps its era alone.
            Assert.Equal("105", ItemSeason.ForNew("103"));
            Assert.Equal("105", ItemSeason.ForNew("105"));
            Assert.Equal("101", ItemSeason.ForNew("101"));
            return 0;
        });
    }

    [CorpusFact]
    public void An_edit_makes_a_legacy_item_this_seasons_and_checked()
    {
        if (!game.Available)
            return;

        WithStamps(() =>
        {
            var bow = EtherealBow("103");
            Assert.True(ItemSeason.MakeCurrent(bow));
            Assert.Equal("105", bow.Version);
            Assert.NotEmpty(Legitimacy.Check(bow));

            // Nothing to do for one of this season.
            Assert.False(ItemSeason.MakeCurrent(bow));
            return 0;
        });
    }

    [CorpusFact]
    public void An_item_the_repair_restamped_stays_legacy_by_the_record_until_it_is_edited()
    {
        if (!game.Available)
            return;

        WithStamps(() =>
        {
            // Written again by the encoding repair out of a stamp no column reads, at this season's stamp.
            var bow = EtherealBow("105");
            bow.Id = 0x5EA5_0013;
            Assert.False(ItemSeason.IsLegacy(bow));
            try
            {
                LegacyRecord.Add([(bow, "S12")]);
                Assert.True(ItemSeason.IsLegacy(bow));
                Assert.Equal("S12", ItemSeason.SeasonOf(bow));
                Assert.Equal("Legacy · S12", ItemSeason.Mark(bow));
                Assert.Empty(Legitimacy.Check(bow));

                // Edited: this season's, off the record.
                Assert.True(ItemSeason.MakeCurrent(bow));
                Assert.False(ItemSeason.IsLegacy(bow));
                Assert.NotEmpty(Legitimacy.Check(bow));
            }
            finally
            {
                LegacyRecord.Forget(bow);
            }
            return 0;
        });
    }

    /// <summary>The record is the only copy of its marks: one that will not read goes aside, never written over.</summary>
    [CorpusFact]
    public void A_legacy_record_that_will_not_read_is_kept_aside()
    {
        if (!game.Available)
            return;

        string? dataBefore = Environment.GetEnvironmentVariable("PD2SPC_DATA");
        string data = Path.Combine(Path.GetTempPath(), "pd2spc-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("PD2SPC_DATA", data);
        try
        {
            string record = Path.Combine(data, "legacy-items.json");
            Directory.CreateDirectory(Path.GetDirectoryName(record)!);
            File.WriteAllText(record, "[{ cut short");

            WithStamps(() =>
            {
                var bow = EtherealBow("105");
                Assert.False(ItemSeason.IsLegacy(bow));
                Assert.Equal("[{ cut short", File.ReadAllText(record + ".bad"));

                LegacyRecord.Add([(bow, "S12")]);
                Assert.Equal("[{ cut short", File.ReadAllText(record + ".bad"));
                Assert.Contains("S12", File.ReadAllText(record), StringComparison.Ordinal);
                LegacyRecord.Forget(bow);
                return 0;
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("PD2SPC_DATA", dataBefore);
            try { Directory.Delete(data, recursive: true); } catch (IOException) { }
        }
    }

    [CorpusFact]
    public void A_vault_migration_carries_a_legacy_item_as_it_is()
    {
        if (!game.Available)
            return;

        WithStamps(() =>
        {
            var bow = EtherealBow("103");
            bow.ItemLevel = 33;
            var (outcome, why) = SeasonMigration.Carry(bow, null, findingsBefore: 0, stamp: "105", statIdBits: D2SLib.Globals.stat_id_bits);

            Assert.Equal(SeasonMigration.Outcome.Clean, outcome);
            Assert.Empty(why);
            Assert.Equal("103", bow.Version);
            Assert.True(bow.IsEthereal);
            return 0;
        });
    }
}
