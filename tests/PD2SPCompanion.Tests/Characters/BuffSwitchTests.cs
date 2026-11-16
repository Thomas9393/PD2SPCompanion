using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// The switches of the sheet's buff rows: what an item casts by chance on any trigger,
/// on the character and the mercenary, and what a mercenary casts on itself.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class BuffSwitchTests(Game game)
{
    private static D2S Save() =>
        SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-20", "Whirlwind-mercenary.d2s")).Save;

    private static void Casts(Item item, string stat, string skill)
    {
        int id = SkillRows.Named(skill)!.Id;
        ItemStats.Home(item).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf(stat), Stat = stat, Param = id, SkillId = id, SkillLevel = 15, Value = 10 });
    }

    [CorpusTheory]
    [InlineData("item_skillongethit")]
    [InlineData("item_skillonhit")]
    [InlineData("item_skillonattack")]
    [InlineData("item_skillonkill")]
    [InlineData("item_skilloncast")]
    public void A_lingering_skill_cast_by_chance_is_a_switch_that_begins_off(string stat)
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Save();
        Casts(CharacterSheet.For(save, data).Gear[0], stat, "Fade");

        var off = CharacterSheet.For(save, data);
        var fade = off.Buffs.Single(buff => buff.Kind == BuffKind.ChanceToCast && buff.Skill.Name == "Fade");
        Assert.True(fade.Toggles);
        Assert.False(fade.On);

        var on = CharacterSheet.For(save, data, new CharacterSheet.Options(Pressed: new Dictionary<string, bool> { [fade.Key] = true }));
        Assert.True(on.Buffs.Single(buff => buff.Key == fade.Key).Counted);
        Assert.True(on.Totals.Of("fireresist") > off.Totals.Of("fireresist"));
    }

    [CorpusFact]
    public void Bone_armor_lent_to_a_character_is_a_switch_and_cyclone_armor_which_shows_nowhere_is_not()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Save();
        var item = CharacterSheet.For(save, data).Gear[0];
        Casts(item, "item_skillongethit", "Bone Armor");
        Casts(item, "item_skillongethit", "Cyclone Armor");

        var buffs = CharacterSheet.For(save, data).Buffs;
        Assert.Contains(buffs, buff => buff.Skill.Name == "Bone Armor" && buff.Toggles);
        Assert.DoesNotContain(buffs, buff => buff.Skill.Name == "Cyclone Armor");
    }

    private static D2S Read(string name) =>
        SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-11-01", name)).Save;

    /// <summary>MercWar's screenshots: in town, Life 306 -> 656 and Mana 211 -> 386 on the character.</summary>
    [CorpusFact]
    public void A_mercenarys_battle_orders_reaches_the_character_in_town_as_read_in_game()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Read("MercWarRead.d2s");
        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false));
        var orders = sheet.Buffs.Single(buff => buff.Skill.Name == "Battle Orders");

        Assert.True(orders is { Toggles: true, On: true, Counted: true, Level: 21, OnMercenary: true });
        var given = sheet.GivenBy(orders).ToDictionary(line => line.Stat, line => line.Value / 256);
        Assert.Equal((350, 175), (given["maxhp"], given["maxmana"]));

        // One switch on both sheets, and its row on the mercenary's once: off there is off here.
        var mercenary = CharacterSheet.ForMercenary(save, data)!;
        var own = mercenary.Buffs.Single(buff => buff.Skill.Name == "Battle Orders");
        Assert.Equal(orders.Key, own.Key);
        var off = new CharacterSheet.Options(OutOfTown: false, Pressed: new Dictionary<string, bool> { [own.Key] = false });
        Assert.False(CharacterSheet.For(save, data, off).Buffs.Single(buff => buff.Key == own.Key).On);
    }

    /// <summary>MercDark's screenshots: Defense 4704 on a Base Def of 1120, +320%; the page's own line unmoved.</summary>
    [CorpusFact]
    public void A_mercenarys_bone_armor_counts_at_level_32_as_read_in_game_and_stays_on_its_own_sheet()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Read("MercDarkRead.d2s");
        var sheet = CharacterSheet.ForMercenary(save, data)!;
        var armor = sheet.Buffs.Single(buff => buff.Skill.Name == "Bone Armor");

        Assert.Equal(32, armor.BaseLevel);
        Assert.Equal(320, sheet.GivenBy(armor).Single(line => line.Stat == "item_armor_percent").Value);
        Assert.Equal(320, sheet.Totals.Of("item_armor_percent"));
        Assert.Equal(1090, sheet.Totals.Of("armorclass"));

        Assert.DoesNotContain(CharacterSheet.For(save, data).Buffs, buff => buff.Skill.Name == "Bone Armor");
    }

    /// <summary>
    /// MercSkills' screenshots: a Cold Iron Wolf of level 98, 196% cold mastery bare, 205% with +3 to All Skills:
    /// the table's level stops at 32 and gear adds on top, lvl 35 on blvl 32.
    /// </summary>
    [CorpusFact]
    public void All_skills_on_a_mercenarys_gear_lift_its_mastery_past_the_stop_as_read_in_game()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var sheet = CharacterSheet.ForMercenary(Read("MercSkillsRead.d2s"), data)!;
        Assert.Equal(205, sheet.Totals.Of("passive_cold_mastery"));
        Assert.Equal(1120, sheet.Totals.Of("armorclass"));
        Assert.Equal(17, sheet.Totals.Of("item_fastergethitrate"));
    }

    /// <summary>
    /// HustleTest's screenshots: Quickness kept cast counts at hard level 0 on either wearer, its formula's 15, not the 39
    /// its item level of 6 would give. Attack Rate 100 -> 115 on the mercenary, 115 on the character; Run/Walk 0 on both.
    /// </summary>
    [CorpusFact]
    public void Hustle_keeps_quickness_at_hard_level_0_on_a_mercenary_and_a_character_as_read_in_game()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Read("HustleTestRead.d2s");
        var mercenary = CharacterSheet.ForMercenary(save, data, new CharacterSheet.Options(OutOfTown: false))!;
        Assert.True(mercenary.Buffs.Single(buff => buff.Kind == BuffKind.WornSkill) is { Toggles: false, Counted: true, BaseLevel: 0, OnMercenary: true });
        Assert.Equal(15, mercenary.Totals.Of("attackrate"));
        Assert.Equal(0, mercenary.Totals.Of("item_fastermovevelocity"));
        Assert.Equal(20, mercenary.Totals.Of("item_fasterattackrate"));

        var hustle = save.MercenaryItemList!.ItemList!.Items.Single(item => item.Code.Trim() == "ltp");
        save.PlayerItemList!.Items.Add(hustle);
        var character = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false));
        Assert.Equal(15, character.Totals.Of("attackrate"));
        Assert.Equal(0, character.Totals.Of("item_fastermovevelocity"));
    }

    /// <summary>A percent of an item's own defense a level is paid on that item's base, as its enhanced defense is.</summary>
    [CorpusFact]
    public void Enhanced_defense_a_level_raises_the_items_own_defense_on_the_sheet()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Read("HustleTestRead.d2s");
        int before = CharacterSheet.ForMercenary(save, data)!.Totals.Of("armorclass");
        var armor = save.MercenaryItemList!.ItemList!.Items.Single(item => item.Code.Trim() == "ltp");
        const string Stat = "item_armorpercent_perlevel";
        ItemStats.Home(armor).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf(Stat), Stat = Stat, Value = 8 });

        // One percent a level, at the mercenary's 98.
        Assert.Equal(before + armor.Armor * 98 / 100, CharacterSheet.ForMercenary(save, data)!.Totals.Of("armorclass"));
    }

    [CorpusFact]
    public void A_skill_cast_by_chance_that_gives_the_caster_nothing_is_not_a_switch()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Save();
        Casts(CharacterSheet.For(save, data).Gear[0], "item_skillonhit", "Fire Ball");

        Assert.DoesNotContain(CharacterSheet.For(save, data).Buffs, buff => buff.Skill.Name == "Fire Ball");
    }

    [CorpusFact]
    public void What_the_mercenarys_gear_casts_by_chance_is_its_own_switch_on_its_own_sheet()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Save();
        Casts(CharacterSheet.ForMercenary(save, data)!.Gear[0], "item_skillonhit", "Fade");

        var off = CharacterSheet.ForMercenary(save, data)!;
        var fade = off.Buffs.Single(buff => buff.Skill.Name == "Fade");
        Assert.True(fade is { Toggles: true, On: false, OnMercenary: true });
        // Not the character's: the switch is apart from one of its own, and nothing of it reaches the character's row.
        Assert.DoesNotContain(CharacterSheet.For(save, data).Buffs, buff => buff.Skill.Name == "Fade");

        var pressed = new CharacterSheet.Options(Pressed: new Dictionary<string, bool> { [fade.Key] = true });
        Assert.True(CharacterSheet.ForMercenary(save, data, pressed)!.Totals.Of("fireresist") > off.Totals.Of("fireresist"));
    }

    [CorpusTheory]
    [InlineData("Bone Armor")]
    [InlineData("Battle Orders")]
    public void What_a_mercenary_casts_on_itself_is_a_switch_that_begins_on(string skill)
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var save = Save();
        int type = Enumerable.Range(0, 400).First(id =>
            Hirelings.SkillsOf(id, 98, save.Status.IsExpansion).Any(each => SkillRows.Named(each.Skill)?.Name == skill));
        save.Mercenary!.TypeId = (ushort)type;

        var on = CharacterSheet.ForMercenary(save, data)!;
        var buff = on.Buffs.Single(each => each.OnMercenary && each.Skill.Name == skill);
        Assert.True(buff is { Toggles: true, On: true, Kind: BuffKind.Cast });

        var off = CharacterSheet.ForMercenary(save, data, new CharacterSheet.Options(Pressed: new Dictionary<string, bool> { [buff.Key] = false }))!;
        Assert.False(off.Buffs.Single(each => each.Key == buff.Key).On);

        string stat = buff.Skill.Auras[0].Stat;
        Assert.NotEqual(off.Totals.Of(stat), on.Totals.Of(stat));

        var none = CharacterSheet.ForMercenary(save, data, new CharacterSheet.Options(NothingCast: true))!;
        Assert.False(none.Buffs.Single(each => each.Key == buff.Key).On);
    }
}
