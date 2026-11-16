using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>What was found wrong in the item and save rules, each pinned once it was right.</summary>
[Collection(nameof(GameTables))]
public sealed class ReviewFixesTests(Game game)
{
    private const string Stamp = "105";

    [CorpusFact]
    public void The_seasons_stamps_ship_and_a_first_character_cannot_teach_otherwise()
    {
        Assert.Equal("103", SeasonSnapshot.KnownStamps["S13"]);
        Assert.Equal("105", SeasonSnapshot.KnownStamps["S14"]);

        // No snapshot needed, and none written: a stamp of a known season is not news.
        SeasonSnapshot.NoteItemVersion("S14", "103");
        Assert.Equal("105", SeasonSnapshot.KnownStamps["S14"]);
    }

    [CorpusTheory]
    [InlineData("pa1")] // a Paladin shield
    [InlineData("ne1")] // a Voodoo head
    public void A_gem_in_a_class_shield_reads_the_shield_column(string code)
    {
        if (!game.Available)
            return;

        var host = ItemForge.BaseAs(code, ItemQuality.Normal, Stamp, new Random(5)).Item;
        var plainShield = ItemForge.BaseAs("buc", ItemQuality.Normal, Stamp, new Random(5)).Item;

        Assert.Equal(2, ItemBases.Of(code)!.GemApply);
        Assert.Equal(SocketEffects.StatsIn("gpw", plainShield).Select(stat => (stat.Stat, stat.Value)),
                     SocketEffects.StatsIn("gpw", host).Select(stat => (stat.Stat, stat.Value)));
    }

    [CorpusFact]
    public void A_gem_in_a_helm_and_in_a_weapon_read_their_own_columns_still()
    {
        if (!game.Available)
            return;

        var helm = ItemForge.BaseAs("cap", ItemQuality.Normal, Stamp, new Random(5)).Item;
        var sword = ItemForge.BaseAs("ssd", ItemQuality.Normal, Stamp, new Random(5)).Item;
        var shield = ItemForge.BaseAs("buc", ItemQuality.Normal, Stamp, new Random(5)).Item;

        var inHelm = SocketEffects.StatsIn("gpw", helm).Select(stat => stat.Stat).ToList();
        var inSword = SocketEffects.StatsIn("gpw", sword).Select(stat => stat.Stat).ToList();
        var inShield = SocketEffects.StatsIn("gpw", shield).Select(stat => stat.Stat).ToList();
        Assert.NotEqual(inHelm, inShield);
        Assert.NotEqual(inSword, inShield);
    }

    [CorpusFact]
    public void A_corruption_on_a_set_piece_goes_on_its_own_list_never_a_green_bonus()
    {
        if (!game.Available)
            return;

        foreach (int row in ItemForge.SetItems())
        {
            var piece = ItemForge.SetItemAt(row, Stamp, RollAs.Maximum).Item;
            var green = piece.StatLists.Skip(1).SelectMany(list => list.Stats).Select(stat => stat.Stat).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var own = piece.StatLists[0].Stats.Select(stat => stat.Stat).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var option = Corruptions.Available(piece).FirstOrDefault(each => !each.GivesSockets && each.Grants.Count == 1
                && each.Grants[0].Param == 0 && AffixRanges.StatsWritten(each.Grants[0].Code) is { Count: 1 } written
                && green.Contains(written[0]) && !own.Contains(written[0]));
            if (option is null)
                continue;

            string stat = AffixRanges.StatsWritten(option.Grants[0].Code)[0];
            int before = piece.StatLists.Skip(1).SelectMany(list => list.Stats).Where(each => each.Stat == stat).Sum(each => each.Value);

            Corruptions.Apply(piece, option, new Random(3));

            Assert.Equal(before, piece.StatLists.Skip(1).SelectMany(list => list.Stats).Where(each => each.Stat == stat).Sum(each => each.Value));
            Assert.Contains(piece.StatLists[0].Stats, each => each.Stat == stat && each.Value > 0);
            return;
        }

        Assert.Fail("no set piece with a corruption that meets only its green list");
    }

    [CorpusTheory]
    [InlineData("The Iron Jang Bong", "item_fastercastrate", 40, 60)]
    [InlineData("Heart Carver", "item_deadlystrike", 0, 200)]
    public void The_cube_rolls_a_row_of_two_lines_of_one_stat_as_their_sum(string name, string stat, int least, int most)
    {
        if (!game.Available)
            return;

        var item = ItemForge.Unique(name, Stamp, RollAs.Maximum).Item;
        int Total() => ItemStats.Every(item).Where(line => line.Stat == stat).Sum(line => line.Value);
        Assert.True(item.StatLists[0].Stats.Count(line => line.Stat == stat) > 1, "no longer two lines");
        int forged = Total();
        Assert.InRange(forged, least, most);

        var random = new Random(7);
        for (int roll = 0; roll < 20; roll++)
        {
            DemonicCube.Reroll(item, random);
            Assert.InRange(Total(), least, Math.Min(most, forged));
        }
    }

    [CorpusFact]
    public void An_affix_of_another_slot_is_no_other_tier_of_this_item()
    {
        if (!game.Available)
            return;

        var helm = ItemForge.BaseAs("cap", ItemQuality.Magic, Stamp, new Random(5)).Item;
        var gloveOnly = Enumerable.Range(1, 2000).Select(id => MagicAffixes.Find(id, prefix: true)).OfType<MagicAffixes.AffixOption>()
            .First(option => option.On.Any() && option.On.All(type => type == "glov"));

        Assert.False(MagicAffixes.SameFamilyOtherTier(helm, gloveOnly));
    }

    /// <summary>LookTest's Javelin and Throwing Knife, screenshots: the throw line first.</summary>
    [CorpusTheory]
    [InlineData("jav", "Throw Damage: 6 to 10", "One-Hand Damage: 1 to 5")]
    [InlineData("tkf", "Throw Damage: 4 to 5", "One-Hand Damage: 2 to 3")]
    public void A_throwing_weapon_says_its_throw_damage_over_its_one_hand_damage(string code, string thrown, string held)
    {
        if (!game.Available)
            return;

        var item = ItemForge.PlainBase(code, 0, Stamp).Item;
        var lines = ItemLines.Of(item, ItemPresenter.Describe(item), out _, out _).Select(line => line.Text).ToList();

        Assert.Contains(thrown, lines);
        Assert.Equal(lines.IndexOf(held) - 1, lines.IndexOf(thrown));
    }

    /// <summary>Berserker's Hatchet's green bonus as the game reads it worn: "+50% Enhanced Damage" (LookTest).</summary>
    [CorpusFact]
    public void A_sets_green_enhanced_damage_is_one_line()
    {
        if (!game.Available)
            return;

        int row = ItemForge.SetItems().First(each => ItemForge.SetOf(each) == "Berserker's Garb" && ItemForge.SetItemAt(each, Stamp).Item.Code.Trim() == "2ax");
        var hatchet = ItemForge.SetItemAt(row, Stamp, RollAs.Maximum).Item;
        var lines = ItemLines.Of(hatchet, ItemPresenter.Describe(hatchet), out _, out _).Select(line => line.Text).ToList();

        Assert.Contains(lines, line => line.StartsWith("+50% Enhanced Damage (", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("Enhanced Maximum Damage (", StringComparison.Ordinal) && line.StartsWith("+50%", StringComparison.Ordinal));
    }

    [CorpusFact]
    public void A_number_under_its_fields_floor_is_held_there_not_wrapped()
    {
        if (!game.Available)
            return;

        var written = AffixRanges.Written("map-play-ac%", -75, -60, -70).Single(each => each.Stat == "map_play_ac%");
        Assert.Equal(StatFields.RangeOf("map_play_ac%")!.Value.Min, written.Value);
    }

    /// <summary>LookTest, LookAssn and LookPala's Boots and Buckler, screenshots.</summary>
    [CorpusTheory]
    [InlineData("bar", "lbt", null, "Durability: 12 of 12")]
    [InlineData("ass", "lbt", "Kick Damage: 3 to 8", "Defense: 3")]
    [InlineData("pal", "lbt", null, "Durability: 12 of 12")]
    [InlineData("bar", "buc", "Chance to Block: 25%", "Defense: 6")]
    [InlineData("ass", "buc", "Chance to Block: 25%", "Defense: 6")]
    [InlineData("pal", "buc", "Chance to Block: 30%", "Defense: 6")]
    public void Kick_smite_and_block_read_by_the_class_holding_them(string classCode, string code, string? expected, string above)
    {
        if (!game.Available)
            return;

        var item = ItemForge.PlainBase(code, 0, Stamp).Item;
        var lines = ItemLines.Of(item, ItemPresenter.Describe(item), out _, out _, classCode: classCode).Select(line => line.Text).ToList();

        if (expected is not null)
            Assert.Equal(lines.IndexOf(above) + 1, lines.IndexOf(expected));
        Assert.Equal(classCode == "ass" && code == "lbt", lines.Contains("Kick Damage: 3 to 8"));
        Assert.Equal(classCode == "pal" && code == "buc", lines.Contains("Smite Damage: 1 to 3"));
    }

    [CorpusFact]
    public void A_recipe_pinned_by_a_reroll_lets_go_when_the_family_changes()
    {
        if (!game.Available)
            return;

        var item = AffixSweepTests.Fresh("ame");
        item.Quality = ItemQuality.Craft;
        var families = AffixRanges.CraftFamilies(item);
        var hitpower = families.First(family => family.Family == "Hitpower");
        var blood = families.First(family => family.Family == "Blood");
        Crafts.Reforge(item, Crafts.Draft(hitpower, new Random(5)), new Random(5));
        Assert.True(AffixRanges.PinRecipe(item));

        Crafts.Reforge(item, Crafts.Draft(blood, new Random(5)), new Random(5));
        Assert.Equal("Hitpower", AffixRanges.CraftFamilyOf(item));

        AffixRanges.ForgetPinnedRecipe(item);
        Assert.Equal("Blood", AffixRanges.CraftFamilyOf(item));
    }

    [CorpusTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rename_lists_every_earlier_backup_under_the_new_name(bool withLastCopy)
    {
        string folder = Path.Combine(Path.GetTempPath(), "pd2spc-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string save = Path.Combine(folder, "OldName.d2s");
            File.Copy(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "HandsTestCubed.d2s"), save);
            SaveWriter.Backup(save);
            if (withLastCopy)
                File.WriteAllBytes(Path.Combine(SaveWriter.BackupFolderFor(save), "OldName.d2s" + SaveWriter.AsWrittenSuffix), [1]);

            string renamed = CharacterRename.Rename(save, "NewName");

            Assert.NotEmpty(SaveWriter.BackupsOf(renamed));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(3, 1, false)] // above the line
    [InlineData(3, 2, true)] // reaching in from above: in a fast dump's way, though no charm there would count
    [InlineData(4, 1, true)]
    public void An_item_touches_the_charm_area_if_any_of_it_lies_there(int y, int height, bool touches) =>
        Assert.Equal(touches, PD2SPCompanion.Core.Characters.CharmArea.Touches(new(10, 8), y, height));

    [Theory]
    [InlineData("103", true)]
    [InlineData("105", true)]
    [InlineData("50", true)]
    [InlineData("107", false)]
    public void A_known_seasons_stamp_or_an_older_one_is_never_a_later_seasons(string stamp, bool earlier) =>
        Assert.Equal(earlier, SeasonSnapshot.IsOfAnEarlierSeason(stamp));

    /// <summary>A corruption and an affix on one one-bit stat: 1 + 1 written would read back 0, and the line would be gone from the item.</summary>
    [GameFact]
    public void Two_grants_of_a_one_bit_stat_hold_at_one()
    {
        if (!game.Available)
            return;

        const string Stat = "item_cannotbefrozen";
        var suffix = Enumerable.Range(1, 1200).Select(id => (Id: id, Row: MagicAffixes.Find(id, prefix: false)))
            .First(each => each.Row?.Stats.Contains(Stat, StringComparer.OrdinalIgnoreCase) == true);

        foreach (string code in new[] { "amu", "rin", "cap", "buc", "qui", "lbl", "lgl", "lbt", "ssd" })
        {
            var item = ItemForge.BaseAs(code, ItemQuality.Magic, Stamp, new Random(5)).Item;
            item.ItemLevel = 99;
            Array.Clear(item.MagicPrefixIds!);
            Array.Clear(item.MagicSuffixIds!);

            var option = Corruptions.Available(item).FirstOrDefault(each => !each.GivesSockets
                && each.Grants.Any(grant => AffixRanges.StatsWritten(grant.Code).Contains(Stat, StringComparer.OrdinalIgnoreCase)));
            if (option is null)
                continue;

            Corruptions.Apply(item, option, new Random(3));
            MagicAffixes.Apply(item,
                new Dictionary<(int, bool), MagicAffixes.AffixPick> { [(0, false)] = new(suffix.Id, MagicAffixes.Roll(suffix.Row!, new Random(3))) },
                new Random(3));

            Assert.Equal(1, item.StatLists[0].Stats.Single(each => each.Stat == Stat).Value);
            return;
        }

        Assert.Fail("no base here takes a corruption that grants it");
    }
}
