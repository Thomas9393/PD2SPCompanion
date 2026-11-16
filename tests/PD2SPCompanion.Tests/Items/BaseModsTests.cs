using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// What a base brings of its own, chosen on the item panel as a superior bonus is: the
/// automagic row its <c>auto prefix</c> group offers, and the staff mods a class base rolls. The Forge made
/// neither - a Maiden Javelin came without its javelin skills, a wand without its Necromancer skills.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class BaseModsTests(Game game)
{
    /// <summary>The loaded season's: forged as 103 under S14's tables, an item takes S13's field widths.</summary>
    private static string Stamp => Game.Stamp;

    [CorpusFact]
    public void An_automagic_goes_on_changes_and_comes_off_and_the_check_stays_silent()
    {
        if (!game.Available)
            return;

        var javelin = ItemForge.BaseAs("am5", ItemQuality.Normal, Stamp, new Random(3)).Item;
        javelin.ItemLevel = 99;
        var rows = AffixRanges.AutomagicRowsFor(javelin);
        Assert.True(rows.Count > 1, $"a Maiden Javelin's group offers several rows ({rows.Count})");

        foreach (var row in rows.Take(2))
        {
            AffixRanges.PutAutomagic(javelin, row.Id);
            Assert.True(javelin.IsAutoAffix);
            Assert.Equal(row.Id, javelin.AutoAffixId);
            Assert.Equal(row.Id, AffixRanges.AutomagicFor(javelin));
            Assert.Empty(Legitimacy.Judge(javelin));
        }

        AffixRanges.PutAutomagic(javelin, 0);
        Assert.False(javelin.IsAutoAffix);
        Assert.Null(AffixRanges.AutomagicFor(javelin));
        Assert.Empty(ItemStats.Every(javelin));

        // Never on a unique.
        var unique = ItemForge.UniqueAt(ItemForge.EnabledUniques().First(), Stamp).Item;
        Assert.Empty(AffixRanges.AutomagicRowsFor(unique));
    }

    [CorpusFact]
    public void An_automod_is_drawn_by_frequency_among_the_rows_the_item_level_reaches()
    {
        if (!game.Available)
            return;

        // A necromancer head (a Succubus Skull): its poison rows, at item level 40 and at 99.
        foreach (int level in new[] { 40, 99 })
        {
            var head = ItemForge.BaseAs("nee", ItemQuality.Magic, "103", new Random(1)).Item;
            head.ItemLevel = (byte)level;
            var reached = AffixRanges.AutomagicRowsFor(head).Where(row => row.Reaches(level)).ToList();
            Assert.True(reached.Count > 1);

            var random = new Random(7);
            var drawn = Enumerable.Range(0, 4000).Select(_ => AffixRanges.RollAutomagic(head, random)!.Id).ToList();
            Assert.All(drawn, id => Assert.Contains(reached, row => row.Id == id));

            int total = reached.Sum(row => Math.Max(0, row.Frequency));
            foreach (var row in reached)
            {
                double expected = total == 0 ? 1.0 / reached.Count : Math.Max(0, row.Frequency) / (double)total;
                double seen = drawn.Count(id => id == row.Id) / (double)drawn.Count;
                Assert.InRange(seen, expected - 0.04, expected + 0.04);
            }
        }
    }

    [CorpusFact]
    public void A_poison_automod_is_said_as_one_line_over_its_length()
    {
        if (!game.Available)
            return;

        // of Virulence, a necromancer head's (a Succubus Skull): stored per frame in 256ths.
        var lines = Crafts.Ranges(new AffixRanges.CraftRecipe("",
        [
            new AffixRanges.CraftGrant("pois-min", 1012, 1012),
            new AffixRanges.CraftGrant("pois-max", 1270, 1270),
            new AffixRanges.CraftGrant("pois-len", 50, 50),
        ]));

        var line = Assert.Single(lines);
        Assert.Contains("198-248", line);
        Assert.DoesNotContain("poisonlength", line);
    }

    [CorpusFact]
    public void A_base_with_one_automod_is_forged_with_it()
    {
        if (!game.Available)
            return;

        // A mace's splash, a War Staff's cast rate and splash: their group's one row, on as the game puts it.
        foreach (string code in new[] { "mac", "wst" })
        {
            var item = ItemForge.PlainBase(code, 0, Stamp).Item;
            Assert.Single(AffixRanges.AutomagicRowsFor(item));
            Assert.True(item.IsAutoAffix, $"{code}: no automod");
            Assert.Equal(item.AutoAffixId, AffixRanges.AutomagicFor(item));
            Assert.Contains(ItemStats.Every(item), stat => stat.Stat == "item_splashonhit");
            Assert.Empty(Legitimacy.Judge(item));
        }

        // A class javelin's group has several: the choice is the line's, nothing is put on.
        Assert.False(ItemForge.PlainBase("am5", 0, Stamp).Item.IsAutoAffix);
    }

    [CorpusFact]
    public void Staff_mods_are_rolled_from_the_class_and_the_check_stays_silent()
    {
        if (!game.Available)
            return;

        var wand = ItemForge.BaseAs("wnd", ItemQuality.Magic, Stamp, new Random(3)).Item;
        wand.ItemLevel = 60;
        Assert.Equal("nec", StaffMods.ClassOf(wand));
        Assert.NotEmpty(StaffMods.Offered(wand));

        var counts = new HashSet<int>();
        for (int seed = 0; seed < 60; seed++)
        {
            var mods = StaffMods.Roll(wand, new Random(seed));
            counts.Add(mods.Count);
            StaffMods.Put(wand, mods);
            Assert.Equal(mods.OrderBy(mod => mod.Skill), StaffMods.On(wand).OrderBy(mod => mod.Skill));
            Assert.Empty(Legitimacy.Judge(wand));
        }
        Assert.Equal([0, 1, 2, 3], counts.Order());

        StaffMods.Put(wand, []);
        Assert.Empty(StaffMods.On(wand));
    }

    [CorpusFact]
    public void A_forged_magic_or_rare_item_is_rolled_as_a_drop_at_the_top_of_every_range()
    {
        if (!game.Available)
            return;

        for (int seed = 0; seed < 20; seed++)
        {
            // A rare at item level 99: six affixes (PD2's floor at 85), every number its sources' best.
            var ring = ItemForge.BaseAs("rin", ItemQuality.Rare, Stamp, new Random(seed)).Item;
            Assert.Equal(6, ring.MagicPrefixIds!.Count(id => id > 0) + ring.MagicSuffixIds!.Count(id => id > 0));
            var ranges = AffixRanges.OwnOf(ring);
            Assert.All(ring.StatLists[0].Stats.Where(stat => ranges.ContainsKey(AffixRanges.KeyOf(stat))
                                                             && StatShapes.Of(stat.Stat!) is StatShape.Value),
                stat => Assert.Equal(ranges[AffixRanges.KeyOf(stat)].Max, stat.Value));
            Assert.Empty(Legitimacy.Judge(ring));

            // A magic wand at 99: a prefix and a suffix (weapons from 65), and its staffmods at +3.
            var wand = ItemForge.BaseAs("wnd", ItemQuality.Magic, Stamp, new Random(seed)).Item;
            Assert.Equal(2, wand.MagicPrefixIds!.Count(id => id > 0) + wand.MagicSuffixIds!.Count(id => id > 0));
            Assert.All(StaffMods.On(wand), mod => Assert.Equal(3, mod.Levels));
            Assert.Empty(Legitimacy.Judge(wand));

            // A magic class javelin: an automod from its group.
            var javelin = ItemForge.BaseAs("am5", ItemQuality.Magic, Stamp, new Random(seed)).Item;
            Assert.True(javelin.IsAutoAffix);
            Assert.Empty(Legitimacy.Judge(javelin));
        }
    }

    /// <summary>
    /// A runeword whose own line is a single skill - Dominion's +2-3 to Golem Mastery, on a Grim Wand - and a staff mod of the
    /// same skill: two lines, the base's on its own list, the runeword's on its own, neither written into the other.
    /// Merged as a corruption is merged, the staff mod never reached the base and wrote its level over the runeword's.
    /// </summary>
    [CorpusFact]
    public void A_staff_mod_of_the_skill_a_runeword_gives_goes_on_the_base_and_leaves_the_runewords_line()
    {
        if (!game.Available)
            return;

        var dominion = ItemForge.Runeword(Runewords.All.First(word => word.Name == "Dominion"), "gwn", Stamp).Item;
        int golem = SkillNames.IdOf("Golem Mastery")!.Value;
        int boneArmor = SkillNames.IdOf("Bone Armor")!.Value;
        var words = dominion.StatLists.Skip(1).SelectMany(list => list.Stats)
            .Single(stat => stat.Stat == StaffMods.Stat && (stat.SkillId ?? stat.Param) == golem);
        int wordsLevels = words.Value;

        StaffMods.Put(dominion, [new StaffMods.Mod(golem, 1), new StaffMods.Mod(boneArmor, 3)]);
        Assert.Equal([new StaffMods.Mod(golem, 1), new StaffMods.Mod(boneArmor, 3)], StaffMods.On(dominion).OrderBy(mod => mod.Skill == boneArmor));
        Assert.Equal(wordsLevels, words.Value);
        Assert.Empty(Legitimacy.Judge(dominion));

        // Taken off again: the base's go, the runeword's stays.
        StaffMods.Put(dominion, []);
        Assert.Empty(StaffMods.On(dominion));
        Assert.Contains(dominion.StatLists.Skip(1).SelectMany(list => list.Stats), stat => ReferenceEquals(stat, words));
        Assert.Equal(wordsLevels, words.Value);
    }

    [CorpusFact]
    public void A_dagger_draws_its_staffmods_from_its_own_list_as_the_game_made_them()
    {
        if (!game.Available)
            return;

        // The list: thirteen skills, Poison Strike among them, the Assassin's traps and shadow skills not.
        var dagger = ItemForge.BaseAs("dgr", ItemQuality.Magic, Stamp, new Random(3)).Item;
        var listed = StaffMods.SkillsFor(dagger);
        Assert.NotNull(listed);
        Assert.Equal(13, listed!.Count);
        Assert.Equal(13, StaffMods.Offered(dagger).Count);
        Assert.Contains(StaffMods.Offered(dagger), skill => skill.Name == "Poison Strike");
        Assert.DoesNotContain(StaffMods.Offered(dagger), skill => skill.Name is "Fire Blast" or "Shadow Warrior" or "Burst of Speed");
        Assert.All(StaffMods.On(dagger), mod => Assert.Contains(mod.Skill, listed));
        Assert.Empty(Legitimacy.Judge(dagger));

        // A wand keeps its class's: no list of its own.
        Assert.Null(StaffMods.SkillsFor(ItemForge.BaseAs("wnd", ItemQuality.Magic, Stamp, new Random(3)).Item));

        // The 32 the game made from the test recipe (SkeleStrafe): every staffmod in the list.
        string path = Path.Combine(Game.Corpus, "saves", "thirty-two-daggers-from-the-cube-s13.d2s");
        if (!File.Exists(path))
            return;

        var made = (SaveVerifier.Load(path).Save.PlayerItemList?.Items ?? []).Where(item => item.Code.Trim() == "dgr").ToList();
        Assert.Equal(32, made.Count);
        var seen = made.SelectMany(StaffMods.On).Select(mod => mod.Skill).ToHashSet();
        Assert.Equal(13, seen.Count);
        Assert.Subset(listed.ToHashSet(), seen);
    }

    [CorpusFact]
    public void Every_forged_magic_rare_and_craft_is_silent_and_reads_back_whole()
    {
        if (!game.Available)
            return;

        // Written alone, each item takes its stamp's widths only while the reader knows the season's stamps.
        Game.UseStamps();

        // The kinds the Forge rolls: maps and dungeons, jewelry, class bases, armour, a craft. A map's
        // Monster Rarity came out double, a dungeon's monster life past its field (56 written, -8 read) - until the
        // fresh set went on as the Perfect Skull's does and the top was held to the field.
        var kinds = new (string Code, ItemQuality Quality)[]
        {
            ("t11", ItemQuality.Rare), ("t31", ItemQuality.Magic), ("t42", ItemQuality.Rare), ("t43", ItemQuality.Rare),
            ("rin", ItemQuality.Rare), ("amu", ItemQuality.Magic), ("jew", ItemQuality.Rare), ("cm3", ItemQuality.Magic),
            ("wnd", ItemQuality.Rare), ("dgr", ItemQuality.Magic), ("am5", ItemQuality.Rare), ("wst", ItemQuality.Rare),
            ("ci3", ItemQuality.Rare), ("uar", ItemQuality.Rare), ("rin", ItemQuality.Craft), ("xtg", ItemQuality.Craft),
        };

        var wrong = new List<string>();
        foreach (var (code, quality) in kinds)
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var item = ItemForge.BaseAs(code, quality, Stamp, new Random(seed)).Item;
                if (Legitimacy.Judge(item) is { Count: > 0 } found)
                    wrong.Add($"{code} {quality} seed {seed}: {string.Join(" | ", found.Select(each => each.Said))}");

                var back = Item.Read(Item.Write(item, 0x60), 0x60);
                string Stats(Item each) => string.Join(",", ItemStats.Every(each).Select(stat => $"{stat.Stat}={stat.Value}"));
                if (Stats(back) != Stats(item))
                    wrong.Add($"{code} {quality} seed {seed}: reads back {Stats(back)} for {Stats(item)}");
            }
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count}: " + string.Join(Environment.NewLine, wrong.Take(12)));
    }
}
