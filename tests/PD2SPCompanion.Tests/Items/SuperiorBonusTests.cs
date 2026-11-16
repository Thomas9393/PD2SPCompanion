using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A superior item's bonus as one row of qualityitems.txt: which rows a
/// base can carry, the row read back off an item, and the stored defence an Enhanced Defense brings.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SuperiorBonusTests(Game game)
{
    private const string Stamp = "103";

    private static string Words(QualityItems.Bonus bonus) => string.Join(" + ", bonus.Mods.Select(mod => mod.Code));

    [CorpusFact]
    public void A_base_is_offered_the_rows_of_its_kind()
    {
        if (!game.Available)
            return;

        var pike = ItemForge.BaseAs("pik", ItemQuality.Superior, Stamp).Item;
        var weapon = QualityItems.RowsFor(pike).Select(Words).ToList();
        Assert.Equal(["att%", "dmg%", "att% + dmg%", "dur%", "att% + dur%", "dmg% + dur%"], weapon);

        var plate = ItemForge.BaseAs("uar", ItemQuality.Superior, Stamp).Item;
        var armour = QualityItems.RowsFor(plate).Select(Words).ToList();
        Assert.Equal(["ac%", "dur%", "ac% + dur%"], armour);

        // Made with no bonus: the choice is the page's.
        Assert.Null(QualityItems.RowOn(pike));
        Assert.Null(QualityItems.RowOn(plate));
    }

    [CorpusFact]
    public void A_runeword_made_in_game_on_a_forged_superior_base_is_the_forges_own()
    {
        if (!game.Available)
            return;

        // A Thresher made superior in the Forge (Enhanced Damage, from its Superior line),
        // the runes of Pride put in it in game - the imperfect rolled Pride is made in game with the superior base
        // crafted in the Forge. Beside it the Forge's own Pride on the same base, perfect.
        string path = Path.Combine(Game.Corpus, "saves", "pride-on-forged-superior-base-s13.d2s");
        if (!File.Exists(path))
            return;

        var items = SaveVerifier.Load(path).Save.PlayerItemList?.Items ?? [];
        var games = items.Single(item => item.Id == 3799066733);
        Assert.Equal(ItemQuality.Superior, games.Quality);
        Assert.True(games.IsRuneword);
        Assert.Equal("dmg%", Words(QualityItems.RowOn(games)!));
        Assert.Empty(Legitimacy.Check(games));

        // The Forge's: Pride on a Thresher, the base made superior with the same row - as the Superior line does it.
        var word = Runewords.All.Single(each => each.Name == "Pride");
        var mine = ItemForge.Runeword(word, games.Code.Trim(), Stamp).Item;
        // The game's was made on a base the Forge forged before it put a base's one automod on: no
        // splash. Taken off here, so the two are the same base.
        AffixRanges.PutAutomagic(mine, 0);
        mine.Quality = ItemQuality.Superior;
        QualityItems.Put(mine, QualityItems.RowsFor(mine).Single(bonus => Words(bonus) == "dmg%"));
        ItemStats.InIdOrder(mine);
        Assert.Empty(Legitimacy.Check(mine));

        // What the game rolled and where it put the item are its own; everything else is the Forge's.
        mine.Id = games.Id;
        mine.X = games.X;
        mine.Y = games.Y;
        mine.Page = games.Page;
        foreach (var stat in ItemStats.Every(mine))
        {
            var rolled = ItemStats.Every(games).Single(each => each.Stat == stat.Stat && each.Param == stat.Param && each.SkillId == stat.SkillId);
            stat.Value = rolled.Value;
        }

        // One difference, and not the item's: a rune the game has just put in a socket is marked new (flag 13),
        // and the game takes the mark off again - five minutes and an Insight later the same Pride's runes were
        // not new, the Insight's were. The Forge's runes are as they settle: not new.
        Assert.All(games.SocketedItems, rune => Assert.True(rune.IsNew));
        Assert.All(items.Single(item => item.Id == 3167996672).SocketedItems, rune => Assert.False(rune.IsNew));
        foreach (var rune in mine.SocketedItems)
            rune.IsNew = true;

        Assert.Equal(Item.Write(games, 0x60), Item.Write(mine, 0x60));

        string later = Path.Combine(Game.Corpus, "saves", "pride-and-insight-five-minutes-later-s13.d2s");
        if (!File.Exists(later))
            return;

        var then = SaveVerifier.Load(later).Save.PlayerItemList?.Items ?? [];
        Assert.All(then.Single(item => item.Id == games.Id).SocketedItems, rune => Assert.False(rune.IsNew));
        Assert.All(then.Single(item => item.Id == 2645719264).SocketedItems, rune => Assert.True(rune.IsNew));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Two_chances_to_cast_of_one_stat_are_two_lines()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A Hitpower helm crafted in game: "12% chance to cast level 4 Charged Bolt when
        // struck" and "5% ... Frost Nova when struck", one stat twice with no parameter - the skill is kept beside
        // it. Taken for one line, the item panel wrote the second's 5% over the first and called the craft unrolled.
        string path = Path.Combine(Game.Corpus, "saves", "hitpower-craft-two-chances-when-struck-s13.d2s");
        if (!File.Exists(path))
            return;

        var craft = (SaveVerifier.Load(path).Save.PlayerItemList?.Items ?? []).Single(item => item.Id == 2297056411);
        var struck = ItemStats.Every(craft).Where(stat => stat.Stat == "item_skillongethit").ToList();
        Assert.Equal(2, struck.Count);
        Assert.True(ItemStats.SameLine(struck[0], struck[0]));
        Assert.False(ItemStats.SameLine(struck[0], struck[1]));
        Assert.Equal([12, 5], struck.Select(stat => stat.Value));
        Assert.Empty(Legitimacy.Check(craft));
    }

    [CorpusFact]
    public void A_skill_trees_line_is_one_line_written_here_or_read_from_a_file()
    {
        // Every writer here makes a tree's line with its parameter alone; a read splits the same parameter into
        // the tree and the class besides. Compared field by field the two were two lines, and the item panel's
        // copy of a rare just made on the Forge - read back from its bytes - wore the tree twice: "+2 to
        // Masteries (Barbarian Only) is outside 1" on an item nobody had touched (the Forge's roll).
        var written = new ItemStat { Id = 188, Stat = "item_addskill_tab", Param = 33, Value = 1 };
        var read = new ItemStat { Id = 188, Stat = "item_addskill_tab", Param = 33, SkillTab = 1, CharacterClass = 4, Value = 1 };
        var split = new ItemStat { Id = 188, Stat = "item_addskill_tab", SkillTab = 1, CharacterClass = 4, Value = 1 };
        Assert.True(ItemStats.SameLine(written, read));
        Assert.True(ItemStats.SameLine(read, written));
        Assert.True(ItemStats.SameLine(written, split));

        // Another tree of the same class is another line, however each is held.
        var other = new ItemStat { Id = 188, Stat = "item_addskill_tab", Param = 34, SkillTab = 2, CharacterClass = 4, Value = 1 };
        Assert.False(ItemStats.SameLine(written, other));
        Assert.False(ItemStats.SameLine(split, other));
        Assert.False(ItemStats.SameLine(read, new ItemStat { Id = 188, Stat = "item_addskill_tab", Param = 34, Value = 1 }));
    }

    [CorpusFact]
    public void A_row_put_on_is_the_row_read_back_and_writes_and_reads()
    {
        if (!game.Available)
            return;

        foreach (string code in new[] { "pik", "uar", "xtb", "hbw", "uit" })
        {
            var item = ItemForge.BaseAs(code, ItemQuality.Superior, Stamp).Item;
            foreach (var bonus in QualityItems.RowsFor(item))
            {
                QualityItems.Put(item, bonus);
                Assert.Equal(bonus.Row, QualityItems.RowOn(item)?.Row);

                // One row at a time: the last one's stats are gone.
                int carried = item.StatLists[0].Stats.Count(stat => stat.Stat is "item_armor_percent" or "item_tohit_percent"
                    or "item_maxdurability_percent" or "item_maxdamage_percent" or "item_mindamage_percent");
                int asked = bonus.Mods.Sum(mod => mod.Code == "dmg%" ? 2 : 1);
                Assert.Equal(asked, carried);

                byte[] bytes = Item.Write(item, 0x60);
                Assert.True(bytes.AsSpan().SequenceEqual(Item.Write(Item.Read(bytes, 0x60), 0x60)), $"{code} {Words(bonus)}: read back another item");
            }

            QualityItems.Put(item, null);
            Assert.Null(QualityItems.RowOn(item));
        }
    }

    [CorpusFact]
    public void Enhanced_defense_stores_the_bases_top_defence_plus_one()
    {
        if (!game.Available)
            return;

        var plate = ItemForge.BaseAs("uar", ItemQuality.Superior, Stamp).Item;
        int top = ItemBases.Of("uar")!.MaximumDefense!.Value;
        Assert.Equal(top, plate.Armor);

        var enhanced = QualityItems.RowsFor(plate).First(bonus => Words(bonus) == "ac%");
        QualityItems.Put(plate, enhanced);
        Assert.Equal(top + 1, plate.Armor);
        Assert.Equal(20, ItemStats.Find(plate, "item_armor_percent")?.Value);

        // Taken off, the defence stays: the base was settled when the item was made.
        QualityItems.Put(plate, QualityItems.RowsFor(plate).First(bonus => Words(bonus) == "dur%"));
        Assert.Equal(top + 1, plate.Armor);
        Assert.Null(ItemStats.Find(plate, "item_armor_percent"));
        Assert.Equal(25, ItemStats.Find(plate, "item_maxdurability_percent")?.Value);
    }
}
