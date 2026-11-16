using D2SLib;
using D2SLib.Model.Save;
using D2SLib.Model.TXT;
using Wrote = PD2SPCompanion.Core.Items.AffixRanges.Wrote;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The ways of calling two lines one line that really differ, each under its own name (ItemStats), and the two columns
/// a stat's field is read from (StatFields). Pinned so that merging any two of them has to be a decision: each answer
/// here is what a caller relies on today, not what the game is known to do.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class StatLineMatchersTests(Game game)
{
    private const string Cast = "item_skilloncast", Charged = "item_charged_skill", Now = "105", Then = "103";

    private static ItemStat Line(string stat = Cast, int value = 10, int? skill = null, int? level = null, int? param = null, int? charges = null) =>
        new() { Stat = stat, Value = value, SkillId = skill, SkillLevel = level, Param = param, MaxCharges = charges };

    [Fact]
    public void A_tree_read_from_a_file_is_one_line_to_SameLine_and_two_to_SameFields()
    {
        var made = Line("item_addskill_tab", param: (1 << 3) | 2);
        var read = new ItemStat { Stat = "item_addskill_tab", Value = 10, CharacterClass = 1, SkillTab = 2 };

        Assert.True(ItemStats.SameLine(made, read));
        Assert.False(ItemStats.SameFields(made, read));
    }

    [Fact]
    public void A_null_parameter_and_a_nought_are_one_line_to_SameLine_and_two_to_SameFields()
    {
        Assert.True(ItemStats.SameLine(Line(param: 0), Line()));
        Assert.False(ItemStats.SameFields(Line(param: 0), Line()));
    }

    [Fact]
    public void Only_SameFieldsAndCharges_tells_two_charge_counts_apart()
    {
        var one = Line(Charged, skill: 54, level: 3, charges: 20);
        var other = Line(Charged, skill: 54, level: 3, charges: 40);

        Assert.True(ItemStats.SameLine(one, other));
        Assert.True(ItemStats.SameFields(one, other));
        Assert.False(ItemStats.SameFieldsAndCharges(one, other));
        Assert.True(ItemStats.SameFieldsAndCharges(one, Line(Charged, value: 1, skill: 54, level: 3, charges: 20)));
    }

    [Theory]
    //     what is written                 LandsOn SameSkillLine SameSkill
    [InlineData(54, 4, true, true, true)]     // its skill and level
    [InlineData(54, 3, false, false, true)]   // its skill at another level: only SameSkill calls it this line
    [InlineData(54, null, true, false, true)] // its skill, no level: a blank asks nothing, but for SameSkillLine it is a blank
    [InlineData(59, 4, false, false, false)]  // another skill
    [InlineData(null, null, true, false, true)] // the plain stat
    public void The_three_matchers_of_a_written_skill_differ(int? skill, int? level, bool lands, bool sameSkillLine, bool sameSkill)
    {
        var line = Line(skill: 54, level: 4);
        var wrote = new Wrote(Cast, 5, skill, level);

        Assert.Equal(lands, ItemStats.LandsOn(line, wrote));
        Assert.Equal(sameSkillLine, ItemStats.SameSkillLine(line, wrote));
        Assert.Equal(sameSkill, ItemStats.SameSkill(line, wrote));
    }

    [Theory]
    //     the line's, the written        LandsOn ParamAgrees
    [InlineData(null, null, true, true)]
    [InlineData(3, null, true, true)]     // a value with no parameter asks nothing of the line
    [InlineData(3, 3, true, true)]
    [InlineData(3, 5, false, false)]
    [InlineData(null, 0, true, true)]     // a line with none is nought to LandsOn
    [InlineData(null, 5, false, true)]    // and nothing to ask of to ParamAgrees
    public void A_parameter_the_line_lacks_differs_between_LandsOn_and_ParamAgrees(int? held, int? written, bool lands, bool agrees)
    {
        var line = Line("item_singleskill", param: held);
        var wrote = new Wrote("item_singleskill", 1, Param: written);

        Assert.Equal(lands, ItemStats.LandsOn(line, wrote));
        Assert.Equal(agrees, ItemStats.ParamAgrees(line, wrote));
    }

    [Fact]
    public void What_each_matcher_leaves_out()
    {
        // The parameter, for the two made for packed lines.
        var held = Line(param: 3);
        Assert.True(ItemStats.SameSkillLine(held, new Wrote(Cast, 1, Param: 5)));
        Assert.True(ItemStats.SameSkill(held, new Wrote(Cast, 1, Param: 5)));

        // The skill, for the automagic's.
        Assert.True(ItemStats.ParamAgrees(Line(skill: 54), new Wrote(Cast, 1, SkillId: 59)));

        // The name, for none of them, and its case for all.
        foreach (var match in new Func<ItemStat, Wrote, bool>[] { ItemStats.LandsOn, ItemStats.SameSkillLine, ItemStats.SameSkill, ItemStats.ParamAgrees })
        {
            Assert.False(match(Line("strength"), new Wrote("dexterity", 1)));
            Assert.True(match(Line("Strength"), new Wrote("strength", 1)));
        }
    }

    [GameFact]
    public void The_search_key_says_the_level_and_the_range_key_does_not()
    {
        var cast = new Wrote(Cast, 5, 54, 4);
        Assert.Equal($"{Cast}#54", AffixRanges.KeyOf(cast));
        Assert.Equal($"{Cast}#54@4", AffixSearch.KeyOf(cast));

        // But for charges, which the search matches by the skill alone.
        var charged = new Wrote(Charged, 20, 54, 3, 20);
        Assert.Equal($"{Charged}#54", AffixRanges.KeyOf(charged));
        Assert.Equal($"{Charged}#54", AffixSearch.KeyOf(charged));

        // A plain stat has no subject to say.
        Assert.Equal("strength", AffixRanges.KeyOf(new Wrote("strength", 5)));

        // What a property writes and the line made of it answer to one key.
        foreach (var wrote in new[] { cast, charged, new Wrote("strength", 5), new Wrote("item_singleskill", 1, Param: 54), new Wrote(Cast, 5, 0, 1) })
        {
            Assert.Equal(AffixRanges.KeyOf(wrote), AffixRanges.KeyOf(ItemStats.LineFrom(wrote)));
            Assert.Equal(AffixSearch.KeyOf(wrote), AffixSearch.KeyOf(ItemStats.LineFrom(wrote)));
        }
    }

    [GameFact]
    public void A_line_from_what_is_written_holds_every_field_and_no_parameter_it_was_not_given()
    {
        var charged = ItemStats.LineFrom(new Wrote(Charged, 20, 54, 3, 40));
        Assert.Equal(StatCatalogue.IdOf(Charged), charged.Id);
        Assert.Equal((Charged, 20, 54, 3, 40), (charged.Stat, charged.Value, charged.SkillId, charged.SkillLevel, charged.MaxCharges));
        Assert.Null(charged.Param);

        Assert.Equal(54, ItemStats.LineFrom(new Wrote("item_singleskill", 1, Param: 54)).Param);
    }

    /// <summary>
    /// The open question, kept as it stands until a specimen in game settles it: a packed value landing on a line of its
    /// skill and level overwrites it through WriteValues, and adds to it through AddToSkillLine.
    /// </summary>
    [GameFact]
    public void A_packed_value_on_its_own_line_overwrites_through_WriteValues_and_adds_through_AddToSkillLine()
    {
        var written = ItemForge.PlainBase("7o7", 0, Now).Item;
        ItemStats.Home(written).Stats.Add(ItemStats.LineFrom(new Wrote(Cast, 10, 54, 4)));
        ItemStats.WriteValues(written, [new Wrote(Cast, 5, 54, 4)]);
        Assert.Equal(5, ItemStats.Every(written).Single(stat => stat.Stat == Cast).Value);

        var crafted = ItemForge.PlainBase("7o7", 0, Now).Item;
        ItemStats.Home(crafted).Stats.Add(ItemStats.LineFrom(new Wrote(Cast, 10, 54, 4)));
        ItemStats.AddToSkillLine(ItemStats.Home(crafted), new Wrote(Cast, 5, 54, 4));
        Assert.Equal(15, ItemStats.Every(crafted).Single(stat => stat.Stat == Cast).Value);

        // Another level is a line of its own, to both.
        ItemStats.WriteValues(written, [new Wrote(Cast, 7, 54, 3)]);
        Assert.Equal(2, ItemStats.Every(written).Count(stat => stat.Stat == Cast));
        ItemStats.AddToSkillLine(ItemStats.Home(crafted), new Wrote(Cast, 7, 54, 3));
        Assert.Equal(2, ItemStats.Every(crafted).Count(stat => stat.Stat == Cast));
    }

    [GameFact]
    public void A_plain_value_adds_through_WriteValues_held_at_its_field()
    {
        var item = ItemForge.PlainBase("7o7", 0, Now).Item;
        ItemStats.Home(item).Stats.Add(ItemStats.LineFrom(new Wrote("strength", 3)));
        ItemStats.WriteValues(item, [new Wrote("strength", 4)]);
        Assert.Equal(7, ItemStats.Every(item).Single(stat => stat.Stat == "strength").Value);

        int top = StatFields.CeilingFor(item, "strength");
        ItemStats.WriteValues(item, [new Wrote("strength", top)]);
        Assert.Equal(top, ItemStats.Every(item).Single(stat => stat.Stat == "strength").Value);
    }

    /// <summary>A stat the previous season's columns give another field than this season's.</summary>
    private static string MovedStat()
    {
        foreach (var row in D2SLib.Core.TXT.ItemStatCostTXT.Rows)
        {
            if (row["Stat"].Value?.Trim() is not { Length: > 0 } name)
                continue;

            (int, int) Field(bool previous)
            {
                using (Globals.Use(new ParserLayout(false, 10, previous)))
                    return (StatEncoding.SaveBits(row), StatEncoding.SaveAdd(row));
            }

            if (Field(false) != Field(true) && Field(false).Item1 > 0 && Field(true).Item1 > 0)
                return name;
        }

        throw new InvalidOperationException("The tables carry a previous season, and no stat's field moved in it.");
    }

    /// <summary>
    /// The two columns stay apart: FieldOf is this season's whatever the parser's switch says, FieldAsSwitched and all
    /// that follows it (ItemForge's clamp, WriteValues' ceiling) read what the switch reads.
    /// </summary>
    [GameFact]
    public void The_installed_columns_and_the_switched_ones_part_only_with_the_switch_on()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason)
            return;

        string moved = MovedStat();

        StatFields.StatField? installed, before;
        using (Globals.Use(new ParserLayout(false, 10, false)))
        {
            installed = StatFields.FieldOf(moved);
            before = StatFields.FieldAsSwitched(moved);
            Assert.Equal(installed, before);
        }

        using (Globals.Use(new ParserLayout(false, 10, true)))
        {
            Assert.Equal(installed, StatFields.FieldOf(moved));
            Assert.NotEqual(installed, StatFields.FieldAsSwitched(moved));

            Assert.Equal(installed!.Value.Max, StatFields.CeilingOf(moved));
            Assert.Equal((installed.Value.Min, installed.Value.Max), StatFields.RangeOf(moved));
        }
    }

    [GameFact]
    public void An_items_field_goes_by_its_own_stamp_and_a_stamp_of_neither_season_by_the_switch()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason)
            return;

        string moved = MovedStat();
        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        try
        {
            SeasonReading.SetStamps(Now, Then);

            StatFields.StatField? installed, previous;
            using (Globals.Use(new ParserLayout(false, 10, false)))
                installed = StatFields.FieldAsSwitched(moved);
            using (Globals.Use(new ParserLayout(false, 10, true)))
                previous = StatFields.FieldAsSwitched(moved);
            Assert.NotEqual(installed, previous);

            foreach (bool on in new[] { false, true })
            {
                using (Globals.Use(new ParserLayout(false, 10, on)))
                {
                    Assert.Equal(installed, StatFields.FieldOf(new Item { Version = Now }, moved));
                    Assert.Equal(previous, StatFields.FieldOf(new Item { Version = Then }, moved));
                    Assert.Equal(on ? previous : installed, StatFields.FieldOf(new Item { Version = "101" }, moved));
                    Assert.Equal(previous!.Value.Max, StatFields.CeilingFor(new Item { Version = Then }, moved));

                    // And the scope is over when it answers.
                    Assert.Equal(on, Globals.previous_season_stats);
                }
            }
        }
        finally
        {
            SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp);
        }
    }

    /// <summary>A sum is held at the field the item is written in: its own stamp's, whichever file was read last.</summary>
    [GameFact]
    public void A_value_written_onto_an_item_is_held_at_its_own_stamps_field()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason)
            return;

        string moved = MovedStat();
        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        try
        {
            SeasonReading.SetStamps(Now, Then);

            foreach (bool on in new[] { false, true })
            {
                using (Globals.Use(new ParserLayout(false, 10, on)))
                {
                    foreach (string stamp in new[] { Now, Then })
                    {
                        var item = new Item { Version = stamp };
                        var line = ItemStats.WriteValues(item, [new AffixRanges.Wrote(moved, 1 << 30)]).Stats.Single(stat => stat.Stat == moved);
                        Assert.Equal(StatFields.CeilingFor(item, moved), line.Value);
                    }
                }
            }
        }
        finally
        {
            SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp);
        }
    }

    [GameFact]
    public void No_field_says_nothing_and_holds_nothing_back()
    {
        const string none = "no such stat";

        Assert.Null(StatFields.FieldOf(none));
        Assert.Null(StatFields.FieldAsSwitched(none));
        Assert.Null(StatFields.CeilingOf(none));
        Assert.Null(StatFields.RangeOf(none));
        Assert.Equal(int.MaxValue, StatFields.CeilingFor(new Item { Version = Now }, none));
        Assert.Equal(123456, StatFields.Held(none, 123456));
    }

    [GameFact]
    public void A_field_holds_a_number_at_both_its_ends()
    {
        var field = StatFields.FieldOf("strength")!.Value;

        Assert.Equal(-field.Add, field.Min);
        Assert.Equal((1 << field.Bits) - 1 - field.Add, field.Max);
        Assert.Equal(field.Max, StatFields.Held("strength", int.MaxValue));
        Assert.Equal(field.Min, StatFields.Held("strength", int.MinValue));
        Assert.Equal(1, StatFields.Held("strength", 1));
    }
}
