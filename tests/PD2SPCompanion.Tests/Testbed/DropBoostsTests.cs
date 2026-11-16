using System.Text;
using PD2SPCompanion.Core.Testbed;

namespace PD2SPCompanion.Tests.Testbed;

/// <summary>
/// The drop panel's entries, odds and table (DropBoosts, DropOverride):
/// The special items by the row PD2's code reads, the high runes by their weight; S13's numbers.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class DropBoostsTests(Game game)
{
    private static Dictionary<string, string[]> Rows(ModdedFile file)
    {
        var lines = Encoding.UTF8.GetString(file.Bytes).Split('\n').Select(line => line.TrimEnd('\r').Split('\t')).ToList();
        return lines.Skip(1).Where(cells => cells.Length > 1).GroupBy(cells => cells[0]).ToDictionary(group => group.Key, group => group.First());
    }

    [GameFact]
    public void The_entries_are_SP_plus_special_items_and_Lem_to_Zod()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var book = DropBoosts.Book(data);

        Assert.Equal(
            ["lbox", "lpp", "rkey", "rid", "rtp", "llmr", "lsvl", "imrn", "rtmo", "lucb", "lucc", "lucd", "ubaa"],
            book.Entries.Where(entry => entry.Kind == DropKind.Special).Select(entry => entry.Key));
        Assert.Equal(["zone-wss", "zone-t1", "map-unique", "map-boss"], book.Entries.Where(entry => entry.Kind == DropKind.Zone).Select(entry => entry.Key));
        Assert.Equal(["wss", "pk1", "pk2", "pk3", "tes", "ceh", "bet", "fed", "crfb", "crfc", "crfs", "crfh", "crfv", "crfu", "crfp", "irma", "irra", "rrra", "urma", "scou", "scrb"],
            book.Entries.Where(entry => entry.Kind == DropKind.Material).Select(entry => entry.Key));
        Assert.Equal(["gem-flawless", "gem-perfect"], book.Entries.Where(entry => entry.Kind == DropKind.Gem).Select(entry => entry.Key));
        Assert.Equal(Enumerable.Range(1, 33).Select(rune => $"r{rune}"),
            book.Entries.Where(entry => entry.Kind == DropKind.Rune).Select(entry => entry.Key));

        // Every item-less row is accounted for.
        Assert.Empty(book.Unknown);
        Assert.Equal(["N", "NM", "H"], book.Entries.Single(entry => entry.Key == "lpp").Columns.Select(column => column.Label));
        Assert.Equal(["All"], book.Entries.Single(entry => entry.Key == "lucc").Columns.Select(column => column.Label));
        Assert.Equal(["T1", "T2", "T3"], book.Entries.Single(entry => entry.Key == "ubaa").Columns.Select(column => column.Label));
    }

    [GameFact]
    public void The_odds_are_the_tables_before_and_after()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var odds = DropBoosts.Book(data).Odds(new Dictionary<string, int> { ["lpp"] = 20, ["r33"] = 5 });

        // Puzzlepiece per kill: one in Unique times Set - 4050 / 2025 / 675 by 100 - Unique divided by 20.
        Assert.Equal([1 / 405000.0, 1 / 202500.0, 1 / 67500.0], odds["lpp"].Select(column => column.Before!.Value));
        Assert.Equal([1 / 20300.0, 1 / 10100.0, 1 / 3400.0], odds["lpp"].Select(column => column.After!.Value));

        // PD2's published rates, which the product rule matches: Lilith's Mirror in Hell 1 in 25 million, the
        // Jawbone 1 in 22,500 - counted on undead.
        Assert.Equal(1 / 25_000_000.0, odds["llmr"][2].Before!.Value, 15);
        Assert.Equal(1 / 22_500.0, odds["rtmo"][2].Before!.Value, 12);
        Assert.Equal("undead", odds["rtmo"][2].Source);

        // Puzzlebox does not drop in Normal: nothing to say there, before or after.
        Assert.Null(odds["lbox"][0].Before);

        // Zod from a Hell Act 5 champion or unique: a rune 28 of 264, Zod 1 in 5171 of the runes (the study's
        // number) - 1 in 48,755 - five times as often boosted; none in Normal or Nightmare, whose chains stop below it.
        var zod = odds["r33"];
        Assert.Null(zod[0].Before);
        Assert.Null(zod[1].Before);
        Assert.InRange(1 / zod[2].Before!.Value, 48700, 48800);
        Assert.InRange(zod[2].After!.Value / zod[2].Before!.Value, 4.99, 5.0);
    }

    [GameFact]
    public void The_shard_and_the_keys_and_what_boosting_costs()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var book = DropBoosts.Book(data);
        var boosts = new Dictionary<string, int> { ["wss"] = 5, ["pk1"] = 5 };
        var odds = book.Odds(boosts);

        // The shard from a Hell Act 5 champion or unique: 4 of 264, at 5x 20 of 280.
        Assert.Equal(264 / 4.0, 1 / odds["wss"][2].Before!.Value, 3);
        Assert.Equal(280 / 20.0, 1 / odds["wss"][2].After!.Value, 3);

        // The Key of Terror per Countess kill: five picks of 20 in 1017.
        var terror = Assert.Single(odds["pk1"]);
        Assert.Equal("Countess", terror.Source);
        Assert.Equal(5 * 20 / 1017.0, terror.Before!.Value, 6);

        // What it costs: the worst class gives up the most share - and nothing boosted costs nothing.
        Assert.Null(book.WorstCost(new Dictionary<string, int>()));
        Assert.True(book.WorstCost(new Dictionary<string, int> { ["wss"] = 20 })!.Kept < 0.85);
        Assert.Equal(1.0, book.OtherRunesKept(new Dictionary<string, int>()), 9);
        Assert.InRange(book.OtherRunesKept(new Dictionary<string, int> { ["r20"] = 20, ["r21"] = 20 }), 0, 0.95);

        // The runes' own cost only: a boosted shard takes from every rune alike and is said on its own header.
        Assert.Equal(1.0, book.OtherRunesKept(new Dictionary<string, int> { ["wss"] = 20 }), 9);

        // A gem tier from a Hell Act 5 champion or unique: Perfect 6 of 264, at 5x 30 of 288.
        var gems = book.Odds(new Dictionary<string, int> { ["gem-perfect"] = 5 });
        Assert.Equal(264 / 6.0, 1 / gems["gem-perfect"][2].Before!.Value, 3);
        Assert.Equal(288 / 30.0, 1 / gems["gem-perfect"][2].After!.Value, 3);
        Assert.Null(gems["gem-perfect"][0].Before);   // none from Normal's
        Assert.True(book.WorstCost(new Dictionary<string, int> { ["gem-perfect"] = 20 })!.Kept < 0.85);
    }

    [GameFact]
    public void Only_the_entries_boosted_change_in_the_table()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var before = Rows(DropOverride.Build(data, false, null).File);
        var (file, report) = DropOverride.Build(data, false, new Dictionary<string, int> { ["lpp"] = 20, ["lbox"] = 5, ["r33"] = 5 });
        var after = Rows(file);

        Assert.Equal(5, report.Specials);   // three Puzzlepiece rows, two Puzzlebox (its Normal is 0 and stays so)
        Assert.Equal(2, report.HighRunes);  // Zod in Runes 17 and Runes 17 equiv

        var changed = before.Keys.Where(name => !before[name].SequenceEqual(after[name])).Order().ToList();
        Assert.Equal(
            ["Larzuk Puzzlebox H", "Larzuk Puzzlebox NM", "Larzuk Puzzlepiece H", "Larzuk Puzzlepiece N", "Larzuk Puzzlepiece NM",
             "Runes 17", "Runes 17 equiv"],
            changed);
    }

    [GameFact]
    public void A_boost_past_the_first_column_takes_the_rest_from_the_second()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var book = DropBoosts.Book(data);

        var odds = book.Odds(new Dictionary<string, int> { ["llmr"] = 50, ["imrn"] = 1000, ["zone-wss"] = 8 });
        Assert.Equal(1 / 500_000.0, odds["llmr"][2].After!.Value, 15);
        Assert.Equal(1 / 1_000.0, odds["imrn"][2].After!.Value, 12);
        Assert.Equal(1 / 25.0, odds["zone-wss"][0].After!.Value, 9);
    }

    [GameFact]
    public void Essences_per_boss_kill_and_the_gem_switch()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var book = DropBoosts.Book(data);

        // Mephisto in Hell: seven picks, his essence 15 of the whole - none in Normal, where his class lists none.
        var odds = book.Odds(new Dictionary<string, int> { ["ceh"] = 3 });
        Assert.Null(odds["ceh"][0].Before);
        Assert.Equal("Mephisto", odds["ceh"][2].Source);
        Assert.True(odds["ceh"][2].After > odds["ceh"][2].Before * 2.5);

        // Chipped and flawed gems out: every other Good item comes more often, the table writes them at 0.
        var gemsOut = new Dictionary<string, int> { [DropBoosts.LowGemsOut] = 2 };
        var with = book.Odds(gemsOut);
        var without = book.Odds(new Dictionary<string, int>());
        Assert.True(with["gem-perfect"][2].After > without["gem-perfect"][2].After);
        var rows = Rows(DropOverride.Build(data, false, gemsOut).File);
        var hellGood = rows["Act 5 (H) Good"];
        int chipped = Array.IndexOf(hellGood, "Chipped Gem");
        Assert.Equal("0", hellGood[chipped + 1]);
    }

    [GameFact]
    public void A_boost_lowers_Unique_first_then_Set()
    {
        Assert.Equal((500, 1000), DropBoosts.Divided(25000, 1000, 50));   // the Mirror at 50x
        Assert.Equal((1, 1000), DropBoosts.Divided(1000, 1000, 1000));    // the Cube at 1000x
        Assert.Equal((1, 200), DropBoosts.Divided(1000, 1000, 5000));     // past Unique: Set takes the rest
        Assert.Equal((34, 100), DropBoosts.Divided(675, 100, 20));        // the Puzzlepiece at 20x
        Assert.Equal((250, (int?)null), DropBoosts.Divided(2000, null, 8)); // a blank Set stays blank
        Assert.Equal((0, (int?)100), DropBoosts.Divided(0, 100, 20));      // no drop stays no drop
    }

    [GameFact]
    public void An_install_from_before_the_panel_reads_as_the_same_choice()
    {
        var old = DropChoice.FromHighRunes(5);
        Assert.Equal(14, old.Count);
        Assert.All(old.Values, boost => Assert.Equal(5, boost));
        Assert.Empty(DropChoice.FromHighRunes(1));

        // The material box reached every material then; the panel does now.
        var both = DropChoice.FromOld(1, 3);
        Assert.Equal(DropBoosts.MaterialKeys.Order(), both.Keys.Order());
        Assert.All(both.Values, boost => Assert.Equal(3, boost));
    }
}
