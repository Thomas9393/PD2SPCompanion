using D2SLib;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The paths of <see cref="Migration.Fit"/> that change a value: an item holding what the installed season's fields
/// cannot hold is brought into range and reported, never truncated by the writer; and what
/// <see cref="SeasonMigration.Carry"/> makes of the report. The items are built, and each field looked up in the installed
/// table, so these hold under any season's tables.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class MigrationFitTests(Game game)
{
    private static Item Plain() => ItemForge.PlainBase("7o7", 0, Game.Stamp).Item;

    private static ItemStat Give(Item item, string name, int value)
    {
        var row = D2SLib.Core.TXT.ItemStatCostTXT[name];
        var stat = new ItemStat { Stat = name, Id = (ushort)row["ID"].ToInt32(), Value = value };
        ItemStats.Home(item).Stats.Add(stat);
        return stat;
    }

    /// <summary>
    /// The first plain stat in the table that passes: an item property, no parameter, written in the item's lists.
    /// A stat of the stat lists only: the runs and encoded kinds are written otherwise.
    /// </summary>
    private static (string Name, int Bits, int Add) Field(Func<int, int, bool> wanted)
    {
        foreach (var row in D2SLib.Core.TXT.ItemStatCostTXT.Rows)
        {
            if (row["Stat"].Value?.Trim() is not { Length: > 0 } name
                || row["Save Param Bits"].ToInt32() > 0
                || row["Encode"].ToInt32() != 0
                || !int.TryParse(row["ID"].Value?.Trim(), out int id) || id is >= 48 and <= 59)
                continue;

            int bits = row["Save Bits"].ToInt32(), add = row["Save Add"].ToInt32();
            if (bits > 0 && wanted(bits, add))
                return (name, bits, add);
        }

        throw new InvalidOperationException("No such field in the installed table.");
    }

    private static int Ceiling(int bits, int add) => (1 << bits) - 1 - add;

    /// <summary>The item through the writer and back, at the installed season's widths: what the save would hold.</summary>
    private static Item WrittenAndRead(Item item) => ItemBlob.Restore(Item.Write(item, 0x60), 10);

    [GameFact]
    public void A_value_over_its_field_comes_down_to_the_top_of_it_and_is_said()
    {
        if (!game.Available)
            return;

        var (name, bits, add) = Field((bits, _) => bits is > 1 and < 20);
        int ceiling = Ceiling(bits, add), was = ceiling + 5;
        var item = Plain();
        var stat = Give(item, name, was);

        var change = Assert.Single(Migration.Fit(item));

        Assert.Equal(new Migration.Change(name, was, ceiling, $"{name} was {was}; this season holds {-add} to {ceiling}."), change);
        Assert.Equal(ceiling, stat.Value);
        Assert.Empty(Migration.Fit(item));
    }

    [GameFact]
    public void A_value_under_its_field_comes_up_to_the_bottom_of_it_and_is_said()
    {
        if (!game.Available)
            return;

        var (name, bits, add) = Field((bits, add) => bits is > 1 and < 20 && add > 0);
        int was = -add - 3;
        var item = Plain();
        var stat = Give(item, name, was);

        var change = Assert.Single(Migration.Fit(item));

        Assert.Equal(was, change.Was);
        Assert.Equal(-add, change.Now);
        Assert.Contains($"this season holds {-add} to {Ceiling(bits, add)}", change.Why);
        Assert.Equal(-add, stat.Value);
    }

    [GameFact]
    public void A_value_at_either_end_of_its_field_is_left_alone()
    {
        if (!game.Available)
            return;

        var (name, bits, add) = Field((bits, _) => bits is > 1 and < 20);
        var item = Plain();
        var top = Give(item, name, Ceiling(bits, add));

        Assert.Empty(Migration.Fit(item));
        Assert.Equal(Ceiling(bits, add), top.Value);

        top.Value = -add;
        Assert.Empty(Migration.Fit(item));
        Assert.Equal(-add, top.Value);
    }

    /// <summary>The old <c>corruptor</c>: a roll of 1001 and up in a field narrowed to a single bit, a flag now.</summary>
    [GameFact]
    public void A_number_in_a_field_narrowed_to_one_bit_becomes_the_flag()
    {
        if (!game.Available)
            return;

        var (name, _, add) = Field((bits, add) => bits == 1 && add == 0);
        var item = Plain();
        var stat = Give(item, name, 1476);

        var change = Assert.Single(Migration.Fit(item));

        Assert.Equal((name, 1476, 1), (change.Stat, change.Was, change.Now));
        Assert.Contains("single flag", change.Why);
        Assert.Equal(1, stat.Value);
        Assert.Equal(0, add);
    }

    [GameFact]
    public void A_one_bit_flag_that_fits_is_left_alone()
    {
        if (!game.Available)
            return;

        var (name, _, _) = Field((bits, add) => bits == 1 && add == 0);

        foreach (int value in new[] { 0, 1 })
        {
            var item = Plain();
            var stat = Give(item, name, value);

            Assert.Empty(Migration.Fit(item));
            Assert.Equal(value, stat.Value);
        }
    }

    [GameFact]
    public void A_parameter_on_a_stat_with_no_field_for_one_is_wiped_and_said()
    {
        if (!game.Available)
            return;

        var (name, bits, add) = Field((bits, _) => bits is > 1 and < 20);
        int value = Math.Max(1, -add + 1);
        var item = Plain();
        var stat = Give(item, name, value);
        stat.Param = 9;
        stat.SkillId = 54;
        stat.SkillLevel = 3;
        stat.SkillTab = 2;
        stat.CharacterClass = 1;
        stat.MaxCharges = 7;

        var change = Assert.Single(Migration.Fit(item));

        Assert.Equal(name, change.Stat);
        Assert.Equal(9, change.Was);
        Assert.Equal(0, change.Now);
        Assert.Contains("cannot be carried across", change.Why);

        // Every field named, so a person reading the report can see what went.
        Assert.Contains($"skill {SkillNames.NameOf(54)}", change.Why);
        Assert.Contains("level 3", change.Why);
        Assert.Contains("tab 2", change.Why);
        Assert.Contains("class 1", change.Why);
        Assert.Contains("7 charges", change.Why);
        Assert.Contains("parameter 9", change.Why);

        Assert.Null(stat.Param);
        Assert.Null(stat.SkillId);
        Assert.Null(stat.SkillLevel);
        Assert.Null(stat.SkillTab);
        Assert.Null(stat.CharacterClass);
        Assert.Null(stat.MaxCharges);
        Assert.Equal(value, stat.Value);
    }

    [GameFact]
    public void An_empty_parameter_on_a_stat_with_no_field_goes_without_a_word()
    {
        if (!game.Available)
            return;

        var (name, bits, add) = Field((bits, _) => bits is > 1 and < 20);
        var item = Plain();
        var stat = Give(item, name, Math.Max(1, -add + 1));
        stat.Param = 0;
        stat.SkillId = 0;

        Assert.Empty(Migration.Fit(item));
        Assert.Null(stat.Param);
        Assert.Null(stat.SkillId);
    }

    [GameFact]
    public void A_parameter_the_field_can_hold_is_kept()
    {
        if (!game.Available)
            return;

        var row = D2SLib.Core.TXT.ItemStatCostTXT["item_singleskill"];
        Assert.True(row["Save Param Bits"].ToInt32() > 0);

        var item = Plain();
        var stat = Give(item, "item_singleskill", 2);
        stat.Param = 54;

        Assert.Empty(Migration.Fit(item));
        Assert.Equal(54, stat.Param);
    }

    [GameFact]
    public void A_stat_the_season_lacks_is_reported_and_left_as_it_was()
    {
        if (!game.Available)
            return;

        var item = Plain();
        var stat = new ItemStat { Stat = "no_such_property", Id = 1023, Value = 12, Param = 3 };
        ItemStats.Home(item).Stats.Add(stat);

        var change = Assert.Single(Migration.Fit(item));

        Assert.Equal(new Migration.Change("no_such_property", 12, 12, "no_such_property is not a property this season has."), change);
        Assert.Equal(12, stat.Value);
        Assert.Equal(3, stat.Param);
        Assert.Contains(ItemStats.Every(item), each => each.Stat == "no_such_property");
    }

    [GameFact]
    public void A_stamp_asked_for_is_said_and_set_and_a_stamp_already_held_is_not()
    {
        if (!game.Available)
            return;

        var item = Plain();
        item.Version = "101";

        var change = Assert.Single(Migration.Fit(item, stampAs: Game.Stamp));

        Assert.Equal("version", change.Stat);
        Assert.Contains($"v101 as v{Game.Stamp}", change.Why);
        Assert.Equal(Game.Stamp, item.Version);

        Assert.Empty(Migration.Fit(item, stampAs: Game.Stamp));
        Assert.Empty(Migration.Fit(item));
        Assert.Equal(Game.Stamp, item.Version);
    }

    /// <summary>The point of it all: unfitted, the writer cuts 1476 down to what the bits leave. Fitted, what is written is what was said.</summary>
    [GameFact]
    public void What_is_fitted_is_what_the_file_then_holds()
    {
        if (!game.Available)
            return;

        var (name, bits, add) = Field((bits, _) => bits is > 1 and < 16);
        int ceiling = Ceiling(bits, add);
        var (flag, _, _) = Field((bits, add) => bits == 1 && add == 0);

        var item = Plain();
        item.Id = 0x1234;
        Give(item, name, ceiling + 1 + (1 << bits));
        Give(item, flag, 1476);
        ItemStats.InIdOrder(item);

        Migration.Fit(item);

        using var layout = Globals.Use(new ParserLayout(false, 10, false));
        var read = WrittenAndRead(item);
        Assert.Equal(ceiling, ItemStats.Every(read).Single(stat => stat.Stat == name).Value);
        Assert.Equal(1, ItemStats.Every(read).Single(stat => stat.Stat == flag).Value);
    }

    // ---- Carry's reading of the report ------------------------------------------------------------------------------

    [GameFact]
    public void An_item_that_needs_nothing_is_clean()
    {
        if (!game.Available)
            return;

        var (outcome, why) = SeasonMigration.Carry(Plain(), null, -1, Game.Stamp, 10);

        Assert.Equal(SeasonMigration.Outcome.Clean, outcome);
        Assert.Empty(why);
    }

    [GameFact]
    public void A_stamp_alone_is_no_reason_to_look()
    {
        if (!game.Available)
            return;

        var item = Plain();
        item.Version = "101";

        var (outcome, why) = SeasonMigration.Carry(item, null, -1, Game.Stamp, 10);

        Assert.Equal(SeasonMigration.Outcome.Clean, outcome);
        Assert.Empty(why);
        Assert.Equal(Game.Stamp, item.Version);
    }

    [GameFact]
    public void A_value_cut_down_sends_the_item_to_review_with_the_reason()
    {
        if (!game.Available)
            return;

        var (name, bits, add) = Field((bits, _) => bits is > 1 and < 20);
        var item = Plain();
        var stat = Give(item, name, Ceiling(bits, add) + 5);
        ItemStats.InIdOrder(item);

        var (outcome, why) = SeasonMigration.Carry(item, null, -1, Game.Stamp, 10);

        Assert.Equal(SeasonMigration.Outcome.Review, outcome);
        Assert.Contains(why, line => line.Contains("this season holds"));
        Assert.Equal(Ceiling(bits, add), stat.Value);
    }

    [GameFact]
    public void A_parameter_lost_sends_the_item_to_review_with_the_reason()
    {
        if (!game.Available)
            return;

        var (name, bits, add) = Field((bits, _) => bits is > 1 and < 20);
        var item = Plain();
        var stat = Give(item, name, Math.Max(1, -add + 1));
        stat.SkillId = 54;
        ItemStats.InIdOrder(item);

        var (outcome, why) = SeasonMigration.Carry(item, null, -1, Game.Stamp, 10);

        Assert.Equal(SeasonMigration.Outcome.Review, outcome);
        Assert.Contains(why, line => line.Contains("cannot be carried"));
    }

    /// <summary>A flag set where a number stood keeps the meaning: said, but nothing to check.</summary>
    [GameFact]
    public void A_number_become_a_flag_is_adjusted_not_lost()
    {
        if (!game.Available)
            return;

        var (flag, _, _) = Field((bits, add) => bits == 1 && add == 0);
        var item = Plain();
        Give(item, flag, 1476);
        ItemStats.InIdOrder(item);

        var (outcome, why) = SeasonMigration.Carry(item, null, 0, Game.Stamp, 10);

        Assert.Contains(why, line => line.Contains("single flag"));
        Assert.DoesNotContain(why, line => line.Contains("this season holds"));
        Assert.NotEqual(SeasonMigration.Outcome.Failed, outcome);
    }

    /// <summary>
    /// The writer has no row to write it by, and threw: out of Carry and out of the run, so that no item of the vault
    /// came across. That one item stays in the old vault, said.
    /// </summary>
    [GameFact]
    public void A_stat_the_season_lacks_fails_that_item_and_does_not_stop_the_migration()
    {
        if (!game.Available)
            return;

        var item = Plain();
        ItemStats.Home(item).Stats.Add(new ItemStat { Stat = "no_such_property", Id = 1023, Value = 12 });

        var (outcome, why) = SeasonMigration.Carry(item, null, -1, Game.Stamp, 10);

        Assert.Equal(SeasonMigration.Outcome.Failed, outcome);
        Assert.Contains("would not write", Assert.Single(why));
    }

    [GameFact]
    public void A_legacy_item_is_carried_as_it_is()
    {
        if (!game.Available || Game.PreviousStamp is not { } previous)
            return;

        var item = ItemForge.PlainBase("7o7", 0, previous).Item;
        var (name, bits, add) = Field((bits, _) => bits is > 1 and < 20);
        var stat = Give(item, name, Math.Max(1, -add + 1));
        ItemStats.InIdOrder(item);
        int was = stat.Value;

        var (outcome, why) = SeasonMigration.Carry(item, null, -1, Game.Stamp, 10);

        Assert.Equal(SeasonMigration.Outcome.Clean, outcome);
        Assert.Empty(why);
        Assert.Equal(previous, item.Version);
        Assert.Equal(was, stat.Value);
    }

    // ---- a legacy item edited ----------------------------------------------------------------------------------------

    /// <summary>What an edit does to a legacy item: stamped as this season, and every property fitted, the changes not said.</summary>
    [GameFact]
    public void A_legacy_item_made_current_takes_the_stamp_and_its_fields()
    {
        if (!game.Available || Game.PreviousStamp is not { } previous)
            return;

        var (name, bits, add) = Field((bits, _) => bits is > 1 and < 20);
        var item = ItemForge.PlainBase("7o7", 0, previous).Item;
        var stat = Give(item, name, Ceiling(bits, add) + 5);

        Assert.True(ItemSeason.IsLegacy(item));
        Assert.True(ItemSeason.MakeCurrent(item));

        Assert.Equal(Game.Stamp, item.Version);
        Assert.Equal(Ceiling(bits, add), stat.Value);
        Assert.False(ItemSeason.IsLegacy(item));
        Assert.False(ItemSeason.MakeCurrent(item));
    }
}
