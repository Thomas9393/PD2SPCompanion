using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>Crafted, superior and packed lines found wrong, each pinned once it was right.</summary>
[Collection(nameof(GameTables))]
public sealed class ReviewCraftFixesTests(Game game)
{
    private const string Stamp = "105";

    /// <summary>A magic base with no affixes yet, as the Edit page holds one.</summary>
    private static Item Magic(string code, int seed)
    {
        var item = ItemForge.BaseAs(code, ItemQuality.Magic, Stamp, new Random(seed)).Item;
        item.ItemLevel = 90;
        return item;
    }

    private static int Sum(Item item, string stat) =>
        ItemStats.Every(item).Where(each => each.Stat == stat).Sum(each => each.Value);

    // ---- a craft made on the Edit page regenerates a class base's staff mods ------------

    [GameFact]
    public void A_class_base_crafted_in_place_comes_out_with_staff_mods_and_no_mark()
    {
        if (!game.Available)
            return;

        int withMods = 0;
        for (int seed = 0; seed < 40; seed++)
        {
            var staff = Magic("wst", seed);
            StaffMods.Put(staff, []);
            var recipe = AffixRanges.CraftFamilies(staff)[seed % AffixRanges.CraftFamilies(staff).Count];

            Crafts.CraftFrom(staff, recipe, new Random(seed));

            var mods = StaffMods.On(staff);
            Assert.InRange(mods.Count, 0, StaffMods.MostPerItem);
            Assert.All(mods, mod => Assert.InRange(mod.Levels, StaffMods.Levels.Min, StaffMods.Levels.Max));
            if (mods.Count > 0)
                withMods++;

            Assert.DoesNotContain(Legitimacy.Check(staff), finding => finding.Said.Contains("item_singleskill")
                                                                     || finding.Certainty == Certainty.Unaccounted);
        }

        // None at 10% a craft: forty in a row with none would mean the roll never ran.
        Assert.True(withMods > 25, $"{withMods} of 40 had staff mods");
    }

    [GameFact]
    public void The_forges_craft_rolls_its_staff_mods_once()
    {
        if (!game.Available)
            return;

        // As the cube draws them: the set CraftFrom rolled is the one that stays, at most three lines and each its own skill.
        for (int seed = 0; seed < 20; seed++)
        {
            var staff = ItemForge.BaseAs("wst", ItemQuality.Craft, Stamp, new Random(seed)).Item;
            var mods = StaffMods.On(staff);
            Assert.InRange(mods.Count, 0, StaffMods.MostPerItem);
            Assert.Equal(mods.Count, mods.Select(mod => mod.Skill).Distinct().Count());
        }
    }

    [GameFact]
    public void A_base_that_rolls_none_is_crafted_with_none()
    {
        if (!game.Available)
            return;

        var plate = Magic("uar", 3);
        Assert.False(StaffMods.Rolls(plate));

        Crafts.CraftFrom(plate, AffixRanges.CraftFamilies(plate)[0], new Random(3));

        Assert.Empty(StaffMods.On(plate));
    }

    /// <summary>The cube's new item keeps an ethereal ingredient's ethereal and no empty socket: both read in game.</summary>
    [GameFact]
    public void A_craft_keeps_ethereal_and_closes_empty_sockets()
    {
        if (!game.Available)
            return;

        var plate = Magic("uar", 3);
        plate.IsEthereal = true;
        plate.TotalNumberOfSockets = 1;
        plate.IsSocketed = true;

        Crafts.CraftFrom(plate, AffixRanges.CraftFamilies(plate)[0], new Random(3));

        Assert.True(plate.IsEthereal);
        Assert.Equal(0, plate.TotalNumberOfSockets);
        Assert.False(plate.IsSocketed);
    }

    // ---- a superior bonus changed keeps a corruption's share of the same stat ------------

    private static string Words(QualityItems.Bonus bonus) => string.Join("+", bonus.Mods.Select(mod => mod.Code));

    private static QualityItems.Bonus Row(Item item, string words) => QualityItems.RowsFor(item).First(bonus => Words(bonus) == words);

    /// <summary>A superior armor with the corruption that writes Enhanced Defense alone, no bonus yet: the corruption's number and its reach.</summary>
    private static (Item Plate, int Roll, int Least, int Most) CorruptedPlate(int seed)
    {
        // The cube gives a superior sockets alone (S14): a magic twin's row, as a file can hold it from the Edit page.
        var plate = ItemForge.BaseAs("uar", ItemQuality.Superior, Stamp, new Random(seed)).Item;
        var option = Corruptions.Available(Magic("uar", seed)).First(each => !each.GivesSockets && each.Grants.Count == 1
            && AffixRanges.StatsWritten(each.Grants[0].Code) is ["item_armor_percent"]);
        Corruptions.Apply(plate, option, new Random(seed));

        var grant = option.Grants[0];
        var reach = AffixRanges.RangeFor(grant.Code, "item_armor_percent", grant.Min, grant.Max, grant.Param);
        return (plate, Sum(plate, "item_armor_percent"), reach.Min, reach.Max);
    }

    [GameFact]
    public void A_corruptions_line_alone_is_no_superior_row_and_stays_when_there_is_none_to_take_off()
    {
        if (!game.Available)
            return;

        var (plate, roll, least, most) = CorruptedPlate(1);
        Assert.InRange(roll, least, most);
        Assert.Null(QualityItems.RowOn(plate));

        QualityItems.Put(plate, null);

        Assert.Equal(roll, Sum(plate, "item_armor_percent"));
        Assert.True(Corruptions.IsCorrupted(plate));
    }

    [GameFact]
    public void A_row_goes_on_the_corruptions_line_and_comes_off_it_again()
    {
        if (!game.Available)
            return;

        var (plate, _, _, most) = CorruptedPlate(2);

        // At the top of what the corruption reaches, so the bonus shows above it.
        ItemStats.Find(plate, "item_armor_percent")!.Value = most;

        QualityItems.Put(plate, Row(plate, "ac%"));
        int top = Row(plate, "ac%").Mods[0].Max;
        Assert.Equal(most + top, Sum(plate, "item_armor_percent"));
        Assert.Equal("ac%", Words(QualityItems.RowOn(plate)!));
        Assert.Single(plate.StatLists[0].Stats, each => each.Stat == "item_armor_percent");

        // Another row, again and again: the corruption's share is where it was.
        for (int pick = 0; pick < 3; pick++)
        {
            QualityItems.Put(plate, Row(plate, "dur%"));
            Assert.Equal(most, Sum(plate, "item_armor_percent"));
            Assert.Equal("dur%", Words(QualityItems.RowOn(plate)!));
            Assert.True(Sum(plate, AffixRanges.StatsWritten("dur%")[0]) > 0);

            QualityItems.Put(plate, Row(plate, "ac%"));
            Assert.Equal(most + top, Sum(plate, "item_armor_percent"));
        }

        QualityItems.Put(plate, null);
        Assert.Equal(most, Sum(plate, "item_armor_percent"));
        Assert.Equal(0, Sum(plate, AffixRanges.StatsWritten("dur%")[0]));
        Assert.True(Corruptions.IsCorrupted(plate));
    }

    [GameFact]
    public void A_superiors_bonus_changed_on_a_corrupted_armor_is_no_mark()
    {
        if (!game.Available)
            return;

        for (int seed = 0; seed < 8; seed++)
        {
            var (plate, _, _, _) = CorruptedPlate(seed);

            // The game's own: Durability alone at its top, a corruption's Enhanced Defense beside it, not the pair's 15.
            QualityItems.Put(plate, Row(plate, "dur%"));
            Assert.DoesNotContain(Legitimacy.Check(plate), finding => finding.Certainty == Certainty.Unrolled);

            QualityItems.Put(plate, Row(plate, "ac%"));
            Assert.DoesNotContain(Legitimacy.Check(plate), finding => finding.Certainty == Certainty.Unrolled);

            QualityItems.Put(plate, Row(plate, "ac%+dur%"));
            Assert.DoesNotContain(Legitimacy.Check(plate), finding => finding.Certainty == Certainty.Unrolled);
        }
    }

    [GameFact]
    public void An_uncorrupted_superior_is_written_as_it_was()
    {
        if (!game.Available)
            return;

        var plate = ItemForge.BaseAs("uar", ItemQuality.Superior, Stamp, new Random(1)).Item;
        QualityItems.Put(plate, Row(plate, "ac%"));
        Assert.Equal(Row(plate, "ac%").Mods[0].Max, Sum(plate, "item_armor_percent"));

        QualityItems.Put(plate, Row(plate, "dur%"));
        Assert.Equal(0, Sum(plate, "item_armor_percent"));
        Assert.Equal("dur%", Words(QualityItems.RowOn(plate)!));

        QualityItems.Put(plate, null);
        Assert.Null(QualityItems.RowOn(plate));
    }

    [GameFact]
    public void A_corruptions_enhanced_damage_stays_when_a_weapons_row_changes()
    {
        if (!game.Available)
            return;

        var pike = ItemForge.BaseAs("pik", ItemQuality.Superior, Stamp, new Random(4)).Item;
        var option = Corruptions.Available(Magic("pik", 4)).First(each => !each.GivesSockets && each.Grants.Count == 1
            && AffixRanges.StatsWritten(each.Grants[0].Code).Contains("item_maxdamage_percent"));
        Corruptions.Apply(pike, option, new Random(4));
        int most = option.Grants[0].Max;
        ItemStats.Find(pike, "item_maxdamage_percent")!.Value = most;
        ItemStats.Find(pike, "item_mindamage_percent")!.Value = most;
        Assert.Null(QualityItems.RowOn(pike));

        QualityItems.Put(pike, Row(pike, "dmg%"));
        int top = Row(pike, "dmg%").Mods[0].Max;
        Assert.Equal(most + top, Sum(pike, "item_maxdamage_percent"));
        Assert.Equal(most + top, Sum(pike, "item_mindamage_percent"));
        Assert.Equal("dmg%", Words(QualityItems.RowOn(pike)!));

        QualityItems.Put(pike, Row(pike, "att%"));
        Assert.Equal(most, Sum(pike, "item_maxdamage_percent"));
        Assert.Equal(most, Sum(pike, "item_mindamage_percent"));
        Assert.Equal("att%", Words(QualityItems.RowOn(pike)!));
    }

    // ---- two chance-to-cast lines of one skill are judged each on its own ----------------

    /// <summary>
    /// A crafted amulet of a recipe writing a chance to cast and an affix writing the same skill's at another level,
    /// each its own line in the given order. Found in the tables as the finding read them: Hitpower's Frost Nova, level 4,
    /// and "of Frost Shield"'s, level 3.
    /// </summary>
    private static (Item Amulet, ItemStat Craft, ItemStat Affix)? TwoChances(bool craftFirst)
    {
        var amulet = ItemForge.BaseAs("amu", ItemQuality.Rare, Stamp, new Random(5)).Item;
        amulet.ItemLevel = 90;
        Array.Clear(amulet.MagicPrefixIds);
        Array.Clear(amulet.MagicSuffixIds);
        amulet.Quality = ItemQuality.Craft;

        foreach (var recipe in AffixRanges.CraftFamilies(amulet))
        {
            var grant = recipe.Grants.FirstOrDefault(each => each.Code == "gethit-skill");
            if (grant is null)
                continue;

            for (int id = 1; id < 800; id++)
            {
                if (MagicAffixes.Find(id, prefix: false) is not { } suffix || !MagicAffixes.Names(suffix))
                    continue;
                var same = suffix.Grants.Where(each => each.Code == "gethit-skill" && each.Param == grant.Param && each.Max != grant.Max).ToList();
                if (same.Count == 0)
                    continue;

                var other = same[0];

                var list = ItemStats.Home(amulet);
                list.Stats.Clear();
                foreach (var each in recipe.Grants)
                    foreach (var wrote in AffixRanges.Written(each.Code, each.Min, each.Max, each.Max, each.Param))
                        if (each != grant)
                            list.Stats.Add(new ItemStat { Id = StatCatalogue.IdOf(wrote.Stat), Stat = wrote.Stat, Value = wrote.Value, Param = wrote.Param, SkillId = wrote.SkillId, SkillLevel = wrote.SkillLevel, MaxCharges = wrote.MaxCharges });

                amulet.MagicSuffixIds[0] = (ushort)id;

                ItemStat Line(AffixRanges.Wrote wrote) => new()
                {
                    Id = StatCatalogue.IdOf(wrote.Stat), Stat = wrote.Stat, Value = wrote.Value,
                    SkillId = wrote.SkillId, SkillLevel = wrote.SkillLevel, MaxCharges = wrote.MaxCharges,
                };

                var craft = Line(AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Min, grant.Param)[0]);
                var affix = Line(AffixRanges.Written(other.Code, other.Min, other.Max, other.Min, other.Param)[0]);
                list.Stats.AddRange(craftFirst ? [craft, affix] : [affix, craft]);
                return (amulet, craft, affix);
            }
        }

        return null;
    }

    private static List<Finding> ChanceFindings(Item item) =>
        [.. Legitimacy.Check(item).Where(finding => finding.Said.Contains("chance at level"))];

    [GameTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_craft_line_and_an_affix_line_of_one_skill_each_pass_in_either_order(bool craftFirst)
    {
        if (!game.Available)
            return;

        var made = TwoChances(craftFirst);
        Assert.NotNull(made);
        Assert.Empty(ChanceFindings(made.Value.Amulet));
    }

    [GameTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_line_of_a_skill_no_source_grants_at_that_level_is_flagged_whichever_stands_first(bool craftFirst)
    {
        if (!game.Available)
            return;

        var made = TwoChances(craftFirst);
        Assert.NotNull(made);

        // The second line was never looked at.
        made.Value.Affix.SkillLevel = 17;
        var finding = Assert.Single(ChanceFindings(made.Value.Amulet));
        Assert.Contains("at level 17", finding.Said);

        // And a line named for itself, with its own number, not the group's sum.
        Assert.Contains($"{made.Value.Affix.Value}% chance at level 17", finding.Said);
    }

    // ---- a craft's family changed keeps what a base upgrade put on a shared line --------

    /// <summary>A crafted War Staff of a recipe writing cast rate, cubed up to a Rune Staff: the recipe's share and the upgrade's on one line.</summary>
    private static (Item Staff, AffixRanges.CraftRecipe Caster) UpgradedCaster(int seed)
    {
        var staff = Magic("wst", seed);
        var caster = AffixRanges.CraftFamilies(staff).First(recipe => recipe.Grants.Any(grant => grant.Code.StartsWith("cast")));
        Crafts.CraftFrom(staff, caster, new Random(seed));

        int before = Sum(staff, "item_fastercastrate");
        BaseUpgrades.Upgrade(staff, new Random(seed));
        Assert.Equal("8ws", staff.Code.Trim());
        Assert.Equal(before + 20, Sum(staff, "item_fastercastrate"));
        return (staff, caster);
    }

    [GameFact]
    public void A_family_with_no_cast_rate_leaves_the_upgrades_twenty_with_the_automagic()
    {
        if (!game.Available)
            return;

        for (int seed = 0; seed < 25; seed++)
        {
            var (staff, caster) = UpgradedCaster(seed);
            var other = AffixRanges.CraftFamilies(staff).First(recipe => recipe.Family != caster.Family
                && !recipe.Grants.Any(grant => grant.Code.StartsWith("cast")));

            Crafts.Reforge(staff, Crafts.Draft(other, new Random(seed)), new Random(seed));

            // The upgrade's twenty is there whatever the automagic and the affixes rolled.
            Assert.True(Sum(staff, "item_fastercastrate") >= 20, $"seed {seed}: {Sum(staff, "item_fastercastrate")}");
            Assert.DoesNotContain(Legitimacy.Check(staff), finding => finding.Subject.Contains("Cast Rate"));
        }
    }

    [GameFact]
    public void A_family_with_cast_rate_of_its_own_adds_to_the_upgrades_twenty()
    {
        if (!game.Available)
            return;

        for (int seed = 0; seed < 10; seed++)
        {
            var (staff, caster) = UpgradedCaster(seed);
            var draft = Crafts.Draft(caster, new Random(seed + 1));
            int recipe = draft.Values.Where(each => each.Stat == "item_fastercastrate").Sum(each => each.Value);

            Crafts.Reforge(staff, draft, new Random(seed));

            Assert.True(Sum(staff, "item_fastercastrate") >= recipe + 20, $"seed {seed}: {Sum(staff, "item_fastercastrate")} of {recipe}");
            Assert.DoesNotContain(Legitimacy.Check(staff), finding => finding.Subject.Contains("Cast Rate"));
        }
    }

    [GameFact]
    public void A_line_that_does_not_hold_the_upgrade_is_not_given_it()
    {
        if (!game.Available)
            return;

        // A Rune Staff recording Staff Tier 1, its list only the automagic - as a craft leaves one just emptied: the
        // credit says twenty, the line holds none of it, and none is invented.
        var staff = Magic("wst", 7);
        var caster = AffixRanges.CraftFamilies(staff).First(recipe => recipe.Grants.Any(grant => grant.Code.StartsWith("cast")));
        Array.Clear(staff.MagicPrefixIds);
        Array.Clear(staff.MagicSuffixIds);
        staff.Quality = ItemQuality.Craft;
        var list = ItemStats.Home(staff);
        list.Stats.Clear();
        int automagic = AffixRanges.AutomagicOn(staff, new Random(7)).Where(each => each.Stat == "item_fastercastrate").Sum(each => each.Value);
        Corruptions.AddStat(list, "item_fastercastrate", automagic);
        staff.Code = "8ws ";
        Assert.Contains(BaseUpgrades.Grants(staff), grant => grant.Min == 20);

        var draft = Crafts.Draft(caster, new Random(7));
        int recipe = draft.Values.Where(each => each.Stat == "item_fastercastrate").Sum(each => each.Value);
        Crafts.Reforge(staff, draft, new Random(7));

        Assert.Equal(automagic + recipe, Sum(staff, "item_fastercastrate"));
    }
}
