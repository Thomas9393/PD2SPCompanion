using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// The character sheet against the game's own Advanced Stats page:
/// three characters, their saves in TestCorpus <c>sheet</c>, and the
/// lines of the screenshots taken of them. LightFury is an Amazon of level 86 in Hell with her
/// whole gear - her javelins on the second weapon set, Call to Arms in hand as the save was taken; Fury a Druid
/// and Frenzy a Barbarian of level 85 with next to nothing on.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CharacterSheetTests(Game game)
{
    private static CharacterSheet Sheet(GameData data, string name, int set = WeaponSets.InHand,
                                        CharacterSheet.Options? options = null, Action<D2SLib.Model.Save.D2S>? change = null)
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", name + ".d2s"));
        change?.Invoke(save);
        return CharacterSheet.For(save, data, options ?? new CharacterSheet.Options(set));
    }

    private static Buff Named(CharacterSheet sheet, string name) => sheet.Buffs.Single(buff => buff.Name == name);

    [CorpusFact]
    public void An_Amazon_in_her_gear_reads_as_the_games_page_line_by_line()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var sheet = Sheet(data, "LightFury", options: new CharacterSheet.Options(WeaponSets.Swapped, OtherWeaponSet: false));
        int Of(string stat) => sheet.Totals.Of(stat);
        (int, int) Resist(string element) => (sheet.Resist(element, 2).Value.Value, sheet.Resist(element, 2).Maximum.Value);

        // "Fire Resist: 85 / 75", "Cold Resist: 149 / 75  Length: 0%", "Lightning Resist: 108 / 83",
        // "Poison Resist: 85 / 75  Length: 200%" - in Hell, three scrolls read.
        Assert.Equal(3, sheet.Scrolls);
        Assert.Equal((85, 75), Resist("fire"));
        Assert.Equal((149, 75), Resist("cold"));
        Assert.Equal((108, 83), Resist("lightning"));
        Assert.Equal((85, 75), Resist("poison"));
        Assert.Equal(0, sheet.ColdLength);
        Assert.Equal(200, sheet.PoisonLength(2));

        // "Damage Reduction: 24/30% 0/0%", "Attacker Takes Damage: 1427 151".
        Assert.Equal((24, 30), (Of("normal_damage_reduction"), Of("damageresist")));
        Assert.Equal((1427, 151), (Of("item_attackertakesdamage"), Of("item_attackertakeslightdamage")));

        // Lightning mastery 60%, lightning pierce 75%.
        Assert.Equal((60, 75), (Of("passive_ltng_mastery"), Of("passive_ltng_pierce")));

        // "Base AR: dex: 945", "Base Def: dex: 48 equip: 4117 total: 4165", "Base Damage: 1h: 155-280".
        Assert.Equal(945, sheet.AttackFromDexterity.Value);
        Assert.Equal((48, 4117), (sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((155, 280), (Of("mindamage"), Of("maxdamage")));

        // "Cast Rate: 30  Block Rate: 35", "Hit Recovery: 59  Run/Walk: 173", "Attack Rate: 110  IAS: 105".
        Assert.Equal((30, 35), (Of("item_fastercastrate"), Of("item_fasterblockrate")));
        Assert.Equal((59, 173), (Of("item_fastergethitrate"), Of("item_fastermovevelocity")));
        Assert.Equal((110, 105), (sheet.AttackRate.Value, Of("item_fasterattackrate")));

        // "Breakpoints (Lightning Fury)": FCR 22 reached, FHR 52 reached, "IAS (Frames): 0 / 6 / 16 / 30 / 52 / 89 (9)".
        Assert.Equal("Lightning Fury", sheet.RightSkill?.Name);
        Assert.Equal([0, 7, 14, 22, 32, 48, 68, 99, 152], sheet.CastRateRow!.Points.Select(point => point.Value));
        Assert.Equal(22, sheet.CastRateRow.Points[sheet.CastRateRow.Reached].Value);
        Assert.Equal([0, 6, 13, 20, 32, 52, 86, 174, 600], sheet.HitRecoveryRow!.Points.Select(point => point.Value));
        Assert.Equal(52, sheet.HitRecoveryRow.Points[sheet.HitRecoveryRow.Reached].Value);
        Assert.Equal([0, 6, 16, 30, 52, 89], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((89, 9), sheet.AttackRow.Points[sheet.AttackRow.Reached]);

        // "Deadly Strike: 0 / 75 / 150%", "Critical Strike: 56 / 75 / 235%", "Life Leech: 12", "Mana Leech: 4",
        // "Projectile Pierce: 100", "HP/MP per Kill: 4 / 6".
        Assert.Equal((75, 150), (sheet.DeadlyStrikeCap, sheet.DeadlyMultiplier.Value));
        Assert.Equal((56, 235), (sheet.CriticalStrike.Value, sheet.CriticalMultiplier.Value));
        Assert.Equal((12, 4), (Of("lifedrainmindam"), Of("manadrainmindam")));
        Assert.Equal(100, sheet.ProjectilePierce.Value);
        Assert.Equal((4, 6), (Of("item_healafterkill"), Of("item_manaafterkill")));

        // The lightning damage line, raised by the mastery: "17-2241".
        Assert.Equal((17, 2241), sheet.Added("lightmindam", "lightmaxdam", "passive_ltng_mastery"));
    }

    [CorpusFact]
    public void A_number_says_what_it_is_made_of()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var sheet = Sheet(data, "LightFury", options: new CharacterSheet.Options(WeaponSets.Swapped, OtherWeaponSet: false));

        // Hit recovery 59: the armor's 40 and the Dodge skill's 4 + its level.
        var recovery = sheet.Stat("item_fastergethitrate");
        Assert.Equal(recovery.Value, recovery.Parts.Sum(part => part.Value));
        Assert.Contains(recovery.Parts, part => part is { Source: "Dodge", Value: 19 });

        // A resistance names the scrolls and the difficulty among its parts, and they add up.
        var fire = sheet.Resist("fire", 2).Value;
        Assert.Contains(fire.Parts, part => part is { Source: "Scroll of Resistance", Value: 30 });
        Assert.Contains(fire.Parts, part => part is { Source: "Hell", Value: -100 });
        Assert.Equal(85, fire.Parts.Sum(part => part.Value));

        // Ten charms of one name are ten parts, not one.
        Assert.Equal(10, fire.Parts.Count(part => part.Source.StartsWith("Shimmering Small Charm", StringComparison.Ordinal)));
    }

    [CorpusFact]
    public void The_weapon_set_asked_for_is_the_one_counted()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);

        // In hand as the save was taken: Call to Arms and a Lidless Wall - another weapon, another shield,
        // another right-hand skill.
        var inHand = Sheet(data, "LightFury");
        Assert.Equal(100, inHand.AttackRate.Value);
        Assert.Equal(70, inHand.Totals.Of("item_fastercastrate"));
        Assert.Equal("Battle Command", inHand.RightSkill?.Name);

        // A skill an item lends has a level and no hard point.
        Assert.Contains(inHand.Skills, skill => skill is { Name: "Battle Command", BaseLevel: 0, Level: > 0 });
        Assert.DoesNotContain(Sheet(data, "LightFury", WeaponSets.Swapped).Skills, skill => skill.Name == "Battle Command");
    }

    [CorpusFact]
    public void A_bare_character_reads_its_class_and_its_scrolls()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);

        // Fury, a Druid with dexterity 20: "Base AR: dex: 70", "Base Def: dex: 5", every resist "30 / 75" in Normal.
        var fury = Sheet(data, "Fury");
        Assert.Equal((70, 5), (fury.AttackFromDexterity.Value, fury.DefenseFromDexterity.Value));
        Assert.Equal((30, 75), (fury.Resist("fire", 0).Value.Value, fury.Resist("fire", 0).Maximum.Value));
        Assert.Equal(-70, fury.Resist("fire", 2).Value.Value);

        // Frenzy, a Barbarian with two Twin Axes of 18-54 and speed 10: "Base AR: dex: 85", "Base Damage: 1h:
        // 18-54" - one axe's, not both.
        var frenzy = Sheet(data, "Frenzy");
        Assert.Equal(85, frenzy.AttackFromDexterity.Value);
        Assert.Equal((18, 54), (frenzy.Totals.Of("mindamage"), frenzy.Totals.Of("maxdamage")));
        Assert.Equal([0, 9, 20, 37, 63, 105, 200], frenzy.CastRateRow!.Points.Select(point => point.Value));
        Assert.Equal([0, 7, 15, 27, 48, 86, 200], frenzy.HitRecoveryRow!.Points.Select(point => point.Value));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_wolf_reads_as_the_games_page()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);

        // Fury's right hand holds Feral Rage, which only a wolf can use: the wolf is up, and Feral Rage with it.
        var wolf = Sheet(data, "Fury");
        Assert.Equal("wolf", wolf.Form?.Name);
        Assert.True(Named(wolf, "Werewolf").On);
        Assert.True(Named(wolf, "Feral Rage").On);
        Assert.False(Named(wolf, "Werebear").On);
        Assert.Equal("bear", Named(wolf, "Maul").Waits);

        // "Attack Rate: 120", "Damage Reduction: 1/0% 1/0%", and the wolf's own rows: "FCR: 6 / 14 / 26 / 40 /
        // 60 / 95 / 157", "FHR: 9 / 20 / 42 / 86 / 280"; the attack row the game's page does not show either.
        Assert.Equal(120, wolf.AttackRate.Value);
        Assert.Equal((1, 1), (wolf.Totals.Of("normal_damage_reduction"), wolf.Totals.Of("magic_damage_reduction")));
        Assert.Equal([0, 6, 14, 26, 40, 60, 95, 157], wolf.CastRateRow!.Points.Select(point => point.Value));
        Assert.Equal([0, 9, 20, 42, 86, 280], wolf.HitRecoveryRow!.Points.Select(point => point.Value));
        // The game's page shows none; the wiki's wereform tables are read (A_forms_attack_reads_as_PD2s_wereform_tables).
        Assert.NotNull(wolf.AttackRow);

        // Feral Rage at its full charge: "Life Leech: 13" at level 20 and 8 at level 10 - a percent a charge.
        Assert.Equal(13, wolf.Totals.Of("lifedrainmindam"));
        var ten = Sheet(data, "Fury", change: save => save.ClassSkills.Skills[SlotOf(data, 5, "Feral Rage")].Points = 10);
        Assert.Equal(8, ten.Totals.Of("lifedrainmindam"));

        // Pressed off, the wolf is a Druid again - and what only the wolf can use waits.
        var druid = Sheet(data, "Fury", options: new CharacterSheet.Options(Pressed: new Dictionary<string, bool> { [Named(wolf, "Werewolf").Key] = false }));
        Assert.Null(druid.Form);
        Assert.Equal(100, druid.AttackRate.Value);
        Assert.Equal(0, druid.Totals.Of("lifedrainmindam"));
        Assert.Equal([0, 4, 10, 19, 30, 46, 68, 99, 163], druid.CastRateRow!.Points.Select(point => point.Value));
    }

    [CorpusFact]
    public void A_bear_takes_the_wolfs_place()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var asWolf = Sheet(data, "Fury");

        // One form at a time: the bear pressed on, the wolf goes - "Attack Rate: 100", "Damage Reduction: 0/0%
        // 0/0%", "FCR: 7 / 15 / 26 / 40 / 63 / 99 / 163", "FHR: 5 / 10 / 16 / 24 / 37 / 54 / 86 / 152 / 360".
        var bear = Sheet(data, "Fury", options: new CharacterSheet.Options(Pressed: new Dictionary<string, bool> { [Named(asWolf, "Werebear").Key] = true }));
        Assert.Equal("bear", bear.Form?.Name);
        Assert.False(Named(bear, "Werewolf").On);
        Assert.True(Named(bear, "Maul").On);
        Assert.Equal("wolf", Named(bear, "Feral Rage").Waits);
        Assert.Equal(100, bear.AttackRate.Value);
        Assert.Equal(0, bear.Totals.Of("normal_damage_reduction"));
        Assert.Equal([0, 7, 15, 26, 40, 63, 99, 163], bear.CastRateRow!.Points.Select(point => point.Value));
        Assert.Equal([0, 5, 10, 16, 24, 37, 54, 86, 152, 360], bear.HitRecoveryRow!.Points.Select(point => point.Value));
    }

    [CorpusFact]
    public void Frenzy_at_its_full_reads_as_the_games_page()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);

        // "Attack Rate: 132" - 100, less the axe's 10, plus Frenzy 20's 42 - and "IAS (Frames): 0 (16) / 6 / 14 /
        // 24 / 39 / 60 / 95 / 163".
        var frenzy = Sheet(data, "Frenzy");
        Assert.True(Named(frenzy, "Frenzy").On);
        Assert.Equal(132, frenzy.AttackRate.Value);
        Assert.Equal([0, 6, 14, 24, 39, 60, 95, 163], frenzy.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((0, 16), frenzy.AttackRow.Points[frenzy.AttackRow.Reached]);

        // Pressed off: the axes alone.
        var calm = Sheet(data, "Frenzy", options: new CharacterSheet.Options(Pressed: new Dictionary<string, bool> { [Named(frenzy, "Frenzy").Key] = false }));
        Assert.Equal(90, calm.AttackRate.Value);
    }

    [CorpusFact]
    public void A_mercenarys_aura_reaches_the_character_out_of_town()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);

        // The save was taken with the mercenary dead: nothing of its is up - only the cries of the Call to Arms on
        // her other weapon set are in the row.
        Assert.All(Sheet(data, "LightFury", WeaponSets.Swapped).Buffs, buff => Assert.Contains("weapon set", buff.From));

        // Alive and beside her, out of town: "Damage Reduction: 33/39%" - its Defiance 18, raised to 19 by the Battle
        // Command of her Call to Arms (it gives 1; the party's skills reach the mercenary's aura - Whirlwind's guard),
        // gives lvl/2: 9 and 9% - and what it wears is up too. In town: "24/30%".
        static void Alive(D2SLib.Model.Save.D2S save) => save.Mercenary.IsDead = 0;
        var outside = Sheet(data, "LightFury", WeaponSets.Swapped, change: Alive);
        Assert.Equal((33, 39), (outside.Totals.Of("normal_damage_reduction"), outside.Totals.Of("damageresist")));
        Assert.Contains(outside.Buffs, buff => buff is { Kind: BuffKind.MercenaryAura, Name: "Defiance", Level: 19, BaseLevel: 18, Toggles: false, On: true });
        Assert.Contains(outside.Buffs, buff => buff is { Kind: BuffKind.ItemAura, Name: "Might", Level: 8, On: true });
        Assert.Contains(outside.Stat("damageresist").Parts, part => part.Value == 9 && part.Source.StartsWith("Defiance", StringComparison.Ordinal));

        var inTown = Sheet(data, "LightFury", options: new CharacterSheet.Options(WeaponSets.Swapped, OutOfTown: false), change: Alive);
        Assert.Equal((24, 30), (inTown.Totals.Of("normal_damage_reduction"), inTown.Totals.Of("damageresist")));
        Assert.All(inTown.Buffs, buff => Assert.Contains("weapon set", buff.From));
    }

    [CorpusFact]
    public void A_cry_that_raises_every_skill_is_cast_first()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);

        // Call to Arms in hand: Battle Command, lent, is up at its own level and gives 1 - a lent skill has no
        // hard point - and every other skill is a level higher for it, Battle Orders among them.
        var sheet = Sheet(data, "LightFury");
        var command = Named(sheet, "Battle Command");
        Assert.True(command is { On: true, BaseLevel: 0, Level: 17 });
        Assert.Equal(19, Named(sheet, "Battle Orders").Level);
        Assert.Equal(48, sheet.Skills.Single(skill => skill.Name == "Lightning Fury").Level);

        var without = Sheet(data, "LightFury", options: new CharacterSheet.Options(Pressed: new Dictionary<string, bool> { [command.Key] = false }));
        Assert.Equal(18, Named(without, "Battle Orders").Level);
        Assert.Equal(47, without.Skills.Single(skill => skill.Name == "Lightning Fury").Level);
    }

    [CorpusFact]
    public void Every_character_of_the_corpus_has_a_sheet()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var files = Directory.GetFiles(Path.Combine(Game.Corpus, "stress"), "*.d2s")
            .Concat(Directory.GetFiles(Path.Combine(Game.Corpus, "saves"), "*.d2s")).ToList();
        Assert.NotEmpty(files);

        int read = 0;
        foreach (string file in files)
        {
            D2SLib.Model.Save.D2S save;
            try
            {
                (save, _) = SaveVerifier.Load(file);
            }
            catch (Exception)
            {
                // A save of another season's tables is not this test's.
                continue;
            }

            read++;
            foreach (int set in new[] { WeaponSets.InHand, WeaponSets.Swapped })
            {
                foreach (bool near in new[] { true, false })
                {
                    var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(set, near));

                    // Every number's parts add up to it, and of each lot that excludes one another one at most is up -
                    // as many as it has room for: two spirits with Spiritkeeper (a level 99 Druid).
                    foreach (var figure in new[] { sheet.Strength, sheet.Dexterity, sheet.AttackRate, sheet.CriticalStrike, sheet.Resist("fire", 2).Value, sheet.Stat("armorclass") })
                        Assert.Equal(figure.Value, figure.Parts.Sum(part => part.Value));
                    foreach (var lot in sheet.Buffs.Where(buff => buff.Rivals.Length > 0).GroupBy(buff => buff.Rivals))
                        Assert.True(lot.Count(buff => buff.On) <= lot.Max(buff => buff.Room), $"{Path.GetFileName(file)}: {lot.Key}");
                    Assert.All(sheet.Buffs.Where(buff => !buff.Toggles), buff => Assert.True(buff.On));
                }
            }
        }

        Assert.True(read >= 30, $"{read} of {files.Count} read");
    }

    [CorpusFact]
    public void The_skill_lines_the_sheet_counts_are_all_read()
    {
        if (!game.Available)
            return;

        // A passive's lines and a buff's, each by its formula: what the reader does not know it leaves out, so
        // a season's new word would go missing without a sound. Known and left out: one monster's row the table
        // itself closes a bracket too many on.
        var context = new SkillCalc.Context(_ => (1, 1), 50, _ => 0);
        var unread = new List<string>();
        foreach (var row in SkillRows.All)
        {
            var lines = (row.PassiveState.Length > 0 ? row.Passives : [])
                .Concat(row.AuraState.Length > 0 && row.GivesTheCaster ? row.Auras : []);
            unread.AddRange(lines.Where(line => new SkillCalc(row, 20, 20, context).Evaluate(line.Calc) is null)
                                 .Select(line => $"{row.Name}: {line.Stat} = {line.Calc}"));
        }

        Assert.All(unread, line => Assert.True(line.StartsWith("Cyclone ArmorMon", StringComparison.Ordinal), line));
    }

    [CorpusFact]
    public void The_other_weapon_sets_cries_are_cast_and_kept()
    {
        if (!game.Available)
            return;

        // A level 97 Paladin fights with set I; his Call to Arms is on set II. Cast with it in hand and kept through the swap
        // back (an offhand CTA counts, on the buff list whenever it is equipped):
        // its Battle Orders and Battle Command are in the row, on.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "paladin-97-1--sheet-ingame-2026-10-13.d2s"));
        var sheet = CharacterSheet.For(save, data);
        var command = Named(sheet, "Battle Command");
        Assert.True(command is { On: true, Kind: BuffKind.Cast, From: "Call to Arms (weapon set II)" });
        Assert.True(Named(sheet, "Battle Orders").On);

        // Every cast that set gives higher (every weapon set's possible buff should
        // count, so long as it does not need that item kept on): Holy Shield cast with set II's
        // levels, Battle Command's among them.
        Assert.True(Named(sheet, "Holy Shield") is { Level: 36, From: "weapon set II", Toggles: true });

        var without = CharacterSheet.For(save, data, new CharacterSheet.Options(OtherWeaponSet: false));
        Assert.Equal(29, Named(without, "Holy Shield").Level);

        // Its cries reach the mercenary too.
        Assert.Contains(CharacterSheet.ForMercenary(save, data)!.Buffs, buff => buff.Name == "Battle Orders");
    }

    /// <summary>
    /// A cast buff the hands cannot cast is off, said why: Holy Sword only while holding a
    /// two-handed sword, no Holy Shield with no shield worn: the skill's itypea -
    /// Holy Shield a shield, Holy Sword a two-handed sword - asked of the set it is cast with.
    /// </summary>
    [CorpusFact]
    public void Holy_Shield_needs_a_shield_and_Holy_Sword_a_two_handed_sword_in_the_hands_that_cast_them()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        D2S Paladin() => SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "paladin-97-1--sheet-ingame-2026-10-13.d2s")).Save;
        static void TakeOff(D2S save, ItemLocation hand) =>
            save.PlayerItemList.Items.RemoveAll(item => item.Mode == ItemMode.Equipped && item.Location == hand);

        // Set I's Stormshield off: set II's Ephemeral casts it, and the swap back keeps it.
        var save = Paladin();
        TakeOff(save, ItemLocation.LeftHand);
        Assert.True(Named(CharacterSheet.For(save, data), "Holy Shield") is { On: true, Counted: true, Needs: null, From: "weapon set II" });

        // Not counting set II, or no shield in either: off, and why.
        var alone = CharacterSheet.For(save, data, new CharacterSheet.Options(OtherWeaponSet: false));
        Assert.True(Named(alone, "Holy Shield") is { On: false, Counted: false, Needs: "Shield" });
        TakeOff(save, ItemLocation.SwapLeft);
        Assert.True(Named(CharacterSheet.For(save, data), "Holy Shield") is { On: false, Needs: "Shield" });

        // Holy Sword, granted by a line on the shield, with Schaefer's Hammer in hand: a two-handed sword it needs.
        var sworded = Paladin();
        sworded.PlayerItemList.Items.Single(item => item.Mode == ItemMode.Equipped && item.Location == ItemLocation.LeftHand)
            .StatLists[0].Stats.Add(new ItemStat { Id = 107, Stat = "item_singleskill", Param = 116, Value = 3 });
        Assert.True(Named(CharacterSheet.For(sworded, data, new CharacterSheet.Options(OtherWeaponSet: false)), "Holy Sword")
            is { On: false, Needs: "Two-Handed Sword" });
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void An_Amazon_swings_a_polearm_on_her_staff_animation()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // LightFury with The Battlebranch, a polearm, in town, nothing cast (screenshot): her
        // own staff animation, AMA1STF 20 frames less 2 at the start - not her spear's, with which PD2's wiki groups
        // staves and polearms ("2H Melee (not sword)": 0 / 6 / 11 / 22 / 32 / 48 / 70 (11) / ...). Every line of the page.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-13", "LightFury-battlebranch.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 0 };
        var all = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false));
        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false, OtherWeaponSet: false,
            Pressed: all.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false)));
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Equal("stf", sheet.WeaponClass);
        Assert.Equal((55, 20, 53, 83, 55), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("cold", 2).Value.Value,
            sheet.Resist("lightning", 2).Value.Value, sheet.Resist("lightning", 2).Maximum.Value, sheet.Resist("poison", 2).Value.Value));
        Assert.Equal((725, 100, 37, 387), (sheet.AttackFromDexterity.Value, Of("tohit"), sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((41, 89), (Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((30, 17, 173, 90, 80), (Of("item_fastercastrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal([0, 2, 6, 11, 20, 29, 42, 58, 83, 120, 194], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((58, 13), sheet.AttackRow.Points[sheet.AttackRow.Reached]);
        Assert.Equal((52, 230, 96, 247), (sheet.CriticalStrike.Value, sheet.CriticalMultiplier.Value, sheet.ProjectilePierce.Value, sheet.OpenWoundsDamage.Value));
        Assert.Equal((19, 4, 4, 6), (Of("lifedrainmindam"), Of("manadrainmindam"), Of("item_healafterkill"), Of("item_manaafterkill")));
        Assert.Equal((5, 579), sheet.Added("lightmindam", "lightmaxdam", "passive_ltng_mastery"));
    }

    [CorpusFact]
    public void A_mercenary_reads_as_the_games_page()
    {
        if (!game.Available)
            return;

        // LightFury's Desert Guard, level 86, Defiance, with Infinity; out of town, nothing cast, Hell (
        // screenshots of the page and of the mercenary's own screen).
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-13", "LightFury-mercenary.d2s"));
        var pressed = CharacterSheet.For(save, data).Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false);
        var sheet = CharacterSheet.ForMercenary(save, data, new CharacterSheet.Options(Pressed: pressed))!;
        int Of(string stat) => sheet.Totals.Of(stat);

        // Its screen: Strength 253, Dexterity 172, resistances 80 (shown at its ceiling), 68, 68, 68.
        Assert.Equal((86, 253, 172), (sheet.Level, sheet.Strength.Value, sheet.Dexterity.Value));

        // "Fire Resist: 101 / 80", "Cold Resist: 68 / 75 Length: 0%", "Poison Resist: 68 / 75 Length: 200%",
        // "Curse Resist: 20 / 75 Length: 100%".
        Assert.Equal((101, 80), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((68, 75), (sheet.Resist("cold", 2).Value.Value, sheet.Resist("cold", 2).Maximum.Value));
        Assert.Equal(68, sheet.Resist("lightning", 2).Value.Value);
        Assert.Equal((68, 200), (sheet.Resist("poison", 2).Value.Value, sheet.PoisonLength(2)));
        Assert.Equal((20, 100), (sheet.CurseResist.Value, sheet.CurseLength));

        // "Damage Reduction: 9/38% 14/0%" - its own Defiance in it; "Elemental Pierce: ... 44% ...".
        Assert.Equal((9, 38, 14), (Of("normal_damage_reduction"), Of("damageresist"), Of("magic_damage_reduction")));
        Assert.Equal(44, Of("passive_ltng_pierce"));

        // "Base AR: dex: 830 equip: 0" - the player's class factor, and none of its own attack rating;
        // "Base Def: dex: 43 equip: 4885"; "Base Damage: 1h: 39-46 2h: 224-1223".
        Assert.Equal((830, 0), (sheet.AttackFromDexterity.Value, Of("tohit")));
        Assert.Equal((43, 4885), (sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((39, 46, 224, 1223), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));

        // "Hit Recovery: 20 Run/Walk: 70", "Attack Rate: 80 IAS: 125".
        Assert.Equal((20, 70, 80, 125), (Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value, Of("item_fasterattackrate")));

        // "Breakpoints (Jab):", "FCR: 0", "FHR: 5 / 9 / 14 / 20 / 30 / 42 / 60 / 86 / 142 / 280",
        // "IAS (Frames): 0 / 4 / 9 / 16 / 24 / 37 / 54 / 80 / 125 (9) / 208 / 456".
        Assert.Equal("Jab", sheet.AttackName);
        Assert.Null(sheet.CastRateRow);
        Assert.Equal([0, 5, 9, 14, 20, 30, 42, 60, 86, 142, 280], sheet.HitRecoveryRow!.Points.Select(point => point.Value));
        Assert.Equal([0, 4, 9, 16, 24, 37, 54, 80, 125, 208, 456], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((125, 9), sheet.AttackRow.Points[sheet.AttackRow.Reached]);

        // "Crushing Blow: 40", "Deadly Strike: 10", "Life Leech: 15 Mana Leech: 8", "fire 26-92 cold 30-60", "Magic Find: 30".
        Assert.Equal((40, 10, 15, 8, 30), (Of("item_crushingblow"), Of("item_deadlystrike"), Of("lifedrainmindam"), Of("manadrainmindam"), Of("item_magicbonus")));
        Assert.Equal((26, 92, 30, 60), (Of("firemindam"), Of("firemaxdam"), Of("coldmindam"), Of("coldmaxdam")));
    }

    [CorpusFact]
    public void In_a_form_a_skill_box_offers_what_the_form_may_use()
    {
        if (!game.Available)
            return;

        // A level 97 Druid as a bear (Maul on the right names the form): only what the skill table lets a bear use -
        // restrict 1, or 2 with the bear among its states (some skills are not available in
        // a form, like Teleport in werebear and werewolf).
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "druid-97-1--sheet-ingame-2026-10-13.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = (uint)SkillRows.Named("Maul")!.Id };
        var sheet = CharacterSheet.For(save, data);
        Assert.Equal("bear", sheet.Form?.Name);
        Assert.All(sheet.LeftChoices.Concat(sheet.RightChoices).Where(choice => choice.Id != sheet.RightSkill!.Id && choice.Id != sheet.LeftSkill?.Id),
            choice => Assert.True(SkillRows.Of(choice.Id) is { } row && (row.Restrict == 1 || (row.Restrict == 2 && row.States.Contains("bear"))),
                                  choice.Name));
        Assert.Contains(sheet.RightChoices, choice => choice.Name == "Hunger");
        Assert.DoesNotContain(sheet.RightChoices, choice => choice.Name == "Arctic Blast");

        // Out of the form, the list is whole again.
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 0 };
        var human = CharacterSheet.For(save, data, new CharacterSheet.Options(Pressed: new Dictionary<string, bool> { ["Cast:228"] = false }));
        Assert.Null(human.Form);
        Assert.Contains(human.RightChoices, choice => choice.Name == "Arctic Blast");
    }

    [CorpusFact]
    public void A_block_reads_as_PD2s_faster_block_rate_table()
    {
        if (!game.Available)
            return;

        // PD2's wiki, Breakpoints, "Faster Block Rate": every row, the FBR that buys
        // each frame - each class's block animation from 50, as hit recovery, from 100 with Holy Shield.
        using var data = GameData.Open(Game.Root, Game.Tables);
        int[] Points(AnimData.Animation? animation, Breakpoints.Rule rule) =>
            [.. Breakpoints.Of(animation!, rule, 600).Select(point => point.Value)];
        AnimData.Animation? Class(int id, string held) => AnimData.Of(id, "BL", held);

        Assert.Equal([0, 4, 6, 11, 15, 23, 29, 40, 56, 80, 120, 200, 480], Points(Class(0, "1HS"), Breakpoints.BlockRate));
        Assert.Equal([0, 13, 32, 86, 600], Points(Class(0, "1HT"), Breakpoints.BlockRate));
        Assert.Equal([0, 13, 32, 86, 600], Points(Class(6, "HTH"), Breakpoints.BlockRate));
        Assert.Equal([0, 9, 20, 42, 86, 280], Points(Class(4, "1HS"), Breakpoints.BlockRate));
        Assert.Equal([0, 6, 13, 20, 32, 52, 86, 174, 600], Points(Class(5, "1HS"), Breakpoints.BlockRate));
        Assert.Equal([0, 5, 10, 16, 27, 40, 65, 109, 223], Points(AnimData.Of("TG", "BL", "HTH"), Breakpoints.BlockRate));
        Assert.Equal([0, 7, 15, 27, 48, 86, 200], Points(AnimData.Of("40", "BL", "HTH"), Breakpoints.BlockRate));
        Assert.Equal([0, 6, 13, 20, 32, 52, 86, 174, 600], Points(Class(2, "1HS"), Breakpoints.BlockRate));
        Assert.Equal([0, 13, 32, 86, 600], Points(Class(3, "1HS"), Breakpoints.BlockRate));
        Assert.Equal([0, 86], Points(Class(3, "1HS"), Breakpoints.HolyShieldBlockRate));
        Assert.Equal([0, 7, 15, 27, 48, 86, 200], Points(Class(1, "1HS"), Breakpoints.BlockRate));

        // On a character: a level 97 Paladin with a shield, his Holy Shield up and pressed off.
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "paladin-97-1--sheet-ingame-2026-10-13.d2s"));
        Assert.Equal([0, 86], CharacterSheet.For(save, data).BlockRateRow!.Points.Select(point => point.Value));
        var plain = CharacterSheet.For(save, data, new CharacterSheet.Options(Pressed: new Dictionary<string, bool> { ["Cast:117"] = false }));
        Assert.Equal([0, 13, 32, 86, 600], plain.BlockRateRow!.Points.Select(point => point.Value));
    }

    [CorpusFact]
    public void A_forms_attack_reads_as_PD2s_wereform_tables()
    {
        if (!game.Available)
            return;

        // PD2's wiki, Breakpoints, "Wereform": the row at weapon threshold -9 with a
        // one-handed weapon, 10 with a spear, -18 with a staff, -4 with a crossbow - one row for all four, the frames
        // each Total Attack Speed reaches. The bear's attack (12 frames) and the wolf's Rabies (10): every cell.
        using var data = GameData.Open(Game.Root, Game.Tables);
        const int Druid = 5;
        (int Frames, int Speed)[] bear = [(29, -19), (28, -17), (27, -14), (26, -10), (25, -7), (24, -3), (23, 0), (22, 5), (21, 10), (20, 15),
                                          (19, 21), (18, 27), (17, 34), (16, 42), (15, 50), (14, 61), (13, 72)];
        (int Frames, int Speed)[] rabies = [(22, 0), (21, 2), (20, 7), (19, 12), (18, 18), (17, 25), (16, 32), (15, 40), (14, 49), (13, 60), (12, 72)];
        foreach (var (weaponClass, threshold) in new[] { ("1hs", -9), ("2ht", 10), ("stf", -18), ("xbw", -4) })
        {
            int bearSpeed = CharacterSheet.WereformSpeed("TG", Druid, weaponClass, threshold)!.Value;
            foreach (var (frames, speed) in bear)
                Assert.Equal(frames, Breakpoints.AttackFrames(12, bearSpeed, 0, 100 + speed));

            int wolfSpeed = CharacterSheet.WereformSpeed("40", Druid, weaponClass, threshold)!.Value;
            foreach (var (frames, speed) in rabies)
                Assert.Equal(frames, Breakpoints.AttackFrames(10, wolfSpeed, 0, 100 + speed));
        }

        // Another class in a form an item lends ("OSkill Wereform"): a Paladin's spear at -16 and a Sorceress's sword at
        // -20 (two start frames) - each the row of the Druid's one-handed weapon at -20 and -17.
        (int Frames, int Speed)[] first = [(33, -18), (32, -15), (31, -13), (30, -9), (29, -7), (28, -4), (27, 0), (26, 3), (25, 8), (24, 11),
                                           (23, 16), (22, 21), (21, 27), (20, 33), (19, 39), (18, 46), (17, 55), (16, 64), (15, 73)];
        (int Frames, int Speed)[] second = [(32, -18), (31, -17), (30, -13), (29, -11), (28, -8), (27, -5), (26, -1), (25, 3), (24, 7), (23, 11),
                                            (22, 16), (21, 21), (20, 27), (19, 33), (18, 40), (17, 48), (16, 57), (15, 66)];
        int paladin = CharacterSheet.WereformSpeed("TG", 3, "2ht", -16)!.Value;
        int sorceress = CharacterSheet.WereformSpeed("TG", 1, "1hs", -20)!.Value;
        foreach (var (frames, speed) in first)
            Assert.Equal(frames, Breakpoints.AttackFrames(12, paladin, 0, 100 + speed));
        foreach (var (frames, speed) in second)
            Assert.Equal(frames, Breakpoints.AttackFrames(12, sorceress, 0, 100 + speed));

        // The vampire's own animation at its own speed - VAA1HTH, 14 frames at 176: a level 98 Necromancer in Vampire Form,
        // attack rate 100, no attack speed: "IAS (Frames): 0 (20) / 4 / 9 / 16 / 24 / 37 / 52 / 75 / 109 / 168" (the
        // screenshot).
        var vampire = Breakpoints.AttackRow(14, 176, 0, 100, sequence: false);
        Assert.Equal([0, 4, 9, 16, 24, 37, 52, 75, 109, 168], vampire.Select(point => point.Value));
        Assert.Equal(20, vampire[0].Frames);

        // On a character: a level 97 Druid as a bear, Maul on the right - a row, where the game's page has none.
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "druid-97-1--sheet-ingame-2026-10-13.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = (uint)SkillRows.Named("Maul")!.Id };
        Assert.NotNull(CharacterSheet.For(save, data).AttackRow);
    }

    [CorpusFact]
    public void The_skill_boxes_offer_what_the_game_lists()
    {
        if (!game.Available)
            return;

        // A level 97 Paladin with no throwing weapon: Attack and Unsummon of the skills every class has, no
        // Throw (unavailable without throwables), no passive, and on the
        // left only what the skill table lets sit there.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "paladin-97-1--sheet-ingame-2026-10-13.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 106 };
        save.LeftSkill = new D2SLib.Model.Save.Skill { Id = 122 };
        var sheet = CharacterSheet.For(save, data);

        var right = sheet.RightChoices.Select(choice => choice.Id).ToList();
        Assert.Equal([0, 3], right.Take(2));
        Assert.DoesNotContain(2, right);
        Assert.Contains(106, right);
        Assert.Contains(122, right);
        Assert.DoesNotContain(right, id => SkillRows.Of(id)!.IsPassive);
        Assert.All(sheet.LeftChoices, choice => Assert.Equal("1", SkillRows.Of(choice.Id)!.Text("leftskill")));

        // What a box holds stays in its list, whatever it is: Kick, which the table keeps off the left.
        save.LeftSkill = new D2SLib.Model.Save.Skill { Id = 1 };
        Assert.Contains(CharacterSheet.For(save, data).LeftChoices, choice => choice.Id == 1);

        // LightFury's javelins, the weapons of her other set: Throw is offered with them in hand.
        var (amazon, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "LightFury.d2s"));
        var held = CharacterSheet.For(amazon, data, new CharacterSheet.Options(WeaponSet: WeaponSets.Swapped));
        Assert.Contains(held.RightChoices, choice => choice.Id == 2);
    }

    [CorpusFact]
    public void The_sheets_inputs_follow_what_they_read_and_nothing_else()
    {
        if (!game.Available)
            return;

        // The page leaves both sheets as they are while this number stands: a move in
        // the stash leaves it, and whatever a sheet reads changes it.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-13", "LightFury-mercenary.d2s"));
        int first = CharacterSheet.InputsOf(save);
        Assert.Equal(first, CharacterSheet.InputsOf(save));

        var items = save.PlayerItemList.Items;
        var stashed = items.First(item => item.Mode == D2SLib.Model.Save.ItemMode.Stored && item.Page != PD2SPCompanion.Core.Saves.InventoryLayout.InventoryPage);
        stashed.X = (byte)(stashed.X + 1);
        Assert.Equal(first, CharacterSheet.InputsOf(save));

        var worn = items.First(item => item.Mode == D2SLib.Model.Save.ItemMode.Equipped && item.StatLists.FirstOrDefault()?.Stats.Count > 0);
        var stat = worn.StatLists[0].Stats[0];
        stat.Value += 1;
        Assert.NotEqual(first, CharacterSheet.InputsOf(save));
        stat.Value -= 1;
        Assert.Equal(first, CharacterSheet.InputsOf(save));

        save.Attributes.Stats["strength"] += 1;
        Assert.NotEqual(first, CharacterSheet.InputsOf(save));
        save.Attributes.Stats["strength"] -= 1;

        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 0 };
        Assert.NotEqual(first, CharacterSheet.InputsOf(save));

        if (save.MercenaryItemList?.ItemList?.Items is [{ } hired, ..] && hired.StatLists.FirstOrDefault()?.Stats is [{ } mercStat, ..])
        {
            int before = CharacterSheet.InputsOf(save);
            mercStat.Value += 1;
            Assert.NotEqual(before, CharacterSheet.InputsOf(save));
        }
    }

    [CorpusFact]
    public void A_box_whose_skill_is_gone_holds_attack()
    {
        if (!game.Available)
            return;

        // An item that was the only source of a box's skill taken off, the box still showed it.
        // A level 97 Paladin with no Teleport (54) - no point, no item lends it, no charges: the box holds Attack, and its list
        // no longer offers Teleport. The save keeps its own, for the item put back on.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "paladin-97-1--sheet-ingame-2026-10-13.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 54 };
        save.LeftSkill = new D2SLib.Model.Save.Skill { Id = 54 };
        var sheet = CharacterSheet.For(save, data);

        Assert.Equal(0, sheet.RightSkill!.Id);
        Assert.Equal(0, sheet.LeftSkill!.Id);
        Assert.DoesNotContain(sheet.RightChoices, choice => choice.Id == 54);
        Assert.Equal(54u, save.RightSkill.Id);

        // One of his own, with points: kept.
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 106 };
        Assert.Equal(106, CharacterSheet.For(save, data).RightSkill!.Id);
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_Paladin_with_Zeal_reads_as_the_games_page()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Paladin of level 97 built from the corpus and loaded in game (five
        // screenshots). In town, nothing cast, Zeal on the right hand.
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(uint right, uint left, bool holyShield)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "paladin-97-1--sheet-ingame-2026-10-13.d2s"));
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = right };
            save.LeftSkill = new D2SLib.Model.Save.Skill { Id = left };
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false,
                Pressed: new Dictionary<string, bool> { ["Cast:117"] = holyShield }, OtherWeaponSet: false));
        }

        const uint Attack = 0, Might = 98, Zeal = 106, Fanaticism = 122;
        var sheet = With(Zeal, Attack, holyShield: false);
        int Of(string stat) => sheet.Totals.Of(stat);

        // The character screen: 253, 152, 351, 57.
        Assert.Equal((253, 152, 351, 57), (sheet.Strength.Value, sheet.Dexterity.Value, sheet.Vitality.Value, sheet.Energy.Value));

        // "Fire Resist: 84 / 77" ... "Curse Resist: 0 / 75 Length: 50%", "Absorption: ... 0/5% ...",
        // "Damage Reduction: 7/49%", "Attacker Takes Damage: 257 239".
        Assert.Equal((84, 77), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((139, 83), (sheet.Resist("lightning", 2).Value.Value, sheet.Resist("lightning", 2).Maximum.Value));
        Assert.Equal(50, sheet.CurseLength);
        Assert.Equal(5, Of("item_absorblight_percent"));
        Assert.Equal((7, 49), (Of("normal_damage_reduction"), Of("damageresist")));
        Assert.Equal((257, 239), (Of("item_attackertakesdamage"), Of("item_attackertakeslightdamage")));

        // "Base AR: dex: 745 equip: 1027", "Base Def: dex: 38 equip: 2885", "Base Damage: 1h: 319-620 2h: 9-242".
        Assert.Equal((745, 1027), (sheet.AttackFromDexterity.Value, Of("tohit")));
        Assert.Equal((38, 2885), (sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((319, 620, 9, 242), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));

        // "Cast Rate: 45  Block Rate: 35", "Hit Recovery: 93  Run/Walk: 67", "Attack Rate: 80  IAS: 60".
        Assert.Equal((45, 35, 93, 67), (Of("item_fastercastrate"), Of("item_fasterblockrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity")));
        Assert.Equal((80, 60), (sheet.AttackRate.Value, Of("item_fasterattackrate")));

        // "IAS (Frames): 0 / 5 / 9 / 10 / 16 / 24 / 37 / 52 / 54 (7|6|11) / 72 / 109 / 125 / 168 / 317 / 456".
        Assert.Equal([0, 5, 9, 10, 16, 24, 37, 52, 54, 72, 109, 125, 168, 317, 456], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal(54, sheet.AttackRow.Points[sheet.AttackRow.Reached].Value);
        Assert.Equal([7, 6, 11], sheet.AttackRow.Hits![sheet.AttackRow.Reached]);

        // "Crushing Blow: 15 / 100%", "Open Wounds: 35%", "Deadly Strike: 58 / 75 / 150%", "Life Leech: 23",
        // "HP/MP per Kill: 9 / 2", cold "10-25", lightning "77-625", "129-129 over 2.0s", "Magic Find: 15".
        Assert.Equal((15, 35, 58, 23), (Of("item_crushingblow"), Of("item_openwounds"), Of("item_deadlystrike"), Of("lifedrainmindam")));
        Assert.Equal((9, 2), (Of("item_healafterkill"), Of("item_manaafterkill")));
        Assert.Equal((10, 25), sheet.Added("coldmindam", "coldmaxdam", "passive_cold_mastery"));
        Assert.Equal((77, 625), sheet.Added("lightmindam", "lightmaxdam", "passive_ltng_mastery"));
        Assert.Equal((129, 129, 2.0), sheet.Poison);
        Assert.Equal(15, Of("item_magicbonus"));

        // Under the pointer on Defense: "Chance to Block: 54%" - the Stormshield's 22, a Paladin's 30 and the
        // shield's own 25 more, x (152 - 15) / (2 x 97) - and the dexterity that would make it 75.
        Assert.Equal((77, 54), (sheet.Blocking!.Value, sheet.Block));
        Assert.Equal(204, sheet.DexterityForMostBlock);

        // Fanaticism on the right hand, Holy Shield cast: "Attack Rate: 117", and no attack row for an aura;
        // "Chance to Block: 75%" with Holy Shield 29's 36.
        var aura = With(Fanaticism, Zeal, holyShield: true);
        Assert.Equal(117, aura.AttackRate.Value);
        Assert.Null(aura.AttackRow);
        Assert.Equal((113, 75), (aura.Blocking!.Value, aura.Block));

        // Fanaticism on the left, Zeal on the right: the left hand's aura is up where the right holds none -
        // "Attack Rate: 117", "IAS (Frames): 0 / 9 / 24 / 30 / 46 (6|5|8) / 86 / 113".
        var left = With(Zeal, Fanaticism, holyShield: true);
        Assert.Equal(117, left.AttackRate.Value);
        Assert.Equal([0, 9, 24, 30, 46, 86, 113], left.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal([6, 5, 8], left.AttackRow.Hits![left.AttackRow.Reached]);

        // An aura on each hand: only the right hand's is up.
        var both = With(Might, Fanaticism, holyShield: false);
        Assert.Equal(80, both.AttackRate.Value);
        Assert.Equal(["Might"], both.Buffs.Where(buff => buff is { Kind: BuffKind.OwnAura, On: true }).Select(buff => buff.Name));
    }

    [CorpusTheory]
    [InlineData(4, 5)]
    [InlineData(12, 12)]
    [InlineData(5, 5)]
    [InlineData(20, 23)]
    [InlineData(35, 54)]
    [InlineData(50, 98)]
    [InlineData(65, 155)]
    [InlineData(85, 243)]
    [InlineData(86, 247)]
    [InlineData(97, 295)]
    public void Open_wounds_for_a_level_are_the_games(int level, int perSecond) =>
        Assert.Equal(perSecond, CharacterSheet.OpenWoundsAt(level, season: 13));

    // Season 14: vanilla's start again (PsnStrike 386 with 120 from gear, CorpseExp, LightFury).
    [CorpusTheory]
    [InlineData(90, 266)]
    [InlineData(91, 271)]
    [InlineData(92, 275)]
    public void Open_wounds_for_a_level_are_the_games_in_season_14(int level, int perSecond) =>
        Assert.Equal(perSecond, CharacterSheet.OpenWoundsAt(level, season: 14));

    [CorpusFact]
    public void A_bare_handed_character_of_each_class_reads_as_the_games_page()
    {
        if (!game.Available)
            return;

        // Five bare characters made for the open wounds line, each of another class (screenshots,
        // in game): attack rating from dexterity, and the three rows with bare hands.
        using var data = GameData.Open(Game.Root, Game.Tables);
        void Reads(string name, int attack, int[] cast, int[] recovery, int[] attackRow, int frames)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-13", name + ".d2s"));
            var sheet = CharacterSheet.For(save, data);
            Assert.Equal(attack, sheet.AttackFromDexterity.Value);
            Assert.Equal(cast, sheet.CastRateRow!.Points.Skip(1).Select(point => point.Value));
            Assert.Equal(recovery, sheet.HitRecoveryRow!.Points.Skip(1).Select(point => point.Value));
            Assert.Equal(attackRow, sheet.AttackRow!.Points.Select(point => point.Value));
            Assert.Equal(frames, sheet.AttackRow.Points[0].Frames);
            Assert.Equal(-70, sheet.Resist("fire", sheet.Difficulty).Value.Value);
            Assert.Equal(CharacterSheet.OpenWoundsAt(save.Level), sheet.OpenWoundsDamage.Value);
        }

        Reads("OWfive", 95, [7, 14, 22, 32, 48, 68, 99, 152], [6, 13, 20, 32, 52, 86, 174, 600], [0, 11, 26, 48, 86, 180], 11);
        Reads("OWtwenty", 85, [9, 20, 37, 63, 105, 200], [7, 15, 27, 48, 86, 200], [0, 11, 26, 48, 86, 180], 11);
        Reads("OWthirtyfive", 85, [9, 18, 30, 48, 75, 125], [7, 15, 27, 48, 86, 200], [0, 9, 20, 37, 63, 105, 200], 13);
        Reads("OWsixtyfive", 80, [8, 16, 27, 42, 65, 102, 174], [7, 15, 27, 48, 86, 200], [0, 13, 29, 56, 113], 10);

        // And the two made to settle the open wounds rule, of the two classes left: a Sorceress - one start frame
        // bare-handed, as the Amazon - and a Necromancer.
        Reads("OWfour", 75, [9, 20, 37, 63, 105, 200], [5, 9, 14, 20, 30, 42, 60, 86, 142, 280], [0, 9, 19, 32, 54, 86, 152], 14);
        Reads("OWtwelve", 80, [9, 18, 30, 48, 75, 125], [5, 10, 16, 26, 39, 56, 86, 152, 377], [0, 9, 19, 32, 54, 86, 152], 14);

        // The Druid's hit recovery is where the sheet and the page part, knowingly: bare-handed, the page lists
        // the row of a one-handed swing ("3 / 7 / 13 / 19 / 29 / 42 / 63 / 99 / 174 / 456") - with nothing in
        // hand it takes an axe for granted - and the animation the character plays is the bare-handed one.
        Reads("OWfifty", 70, [4, 10, 19, 30, 46, 68, 99, 163], [5, 10, 16, 26, 39, 56, 86, 152, 377], [0, 9, 18, 30, 48, 75, 125], 15);
        Assert.Equal([0, 3, 7, 13, 19, 29, 42, 63, 99, 174, 456],
            Breakpoints.Of(AnimData.Of(5, "GH", "1hs")!, Breakpoints.HitRecovery).Select(point => point.Value));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Of_two_unlike_weapons_the_faster_is_the_one_counted()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Barbarian of level 99: a Ghost Glaive (speed 20) in the main slot and a Flying Knife
        // (speed 0, 10% increased attack speed) in the other. In town, nothing cast (three
        // screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(uint right)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "barbarian-99-2--sheet-ingame-2026-10-13.d2s"));
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = right };
            var bare = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false));
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false,
                Pressed: bare.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false)));
        }

        const uint Attack = 0, DoubleThrow = 140, Frenzy = 147;
        var sheet = With(DoubleThrow);
        int Of(string stat) => sheet.Totals.Of(stat);

        // The knife's: "Attack Rate: 100  IAS: 32", "Base AR: ... equip: 1004", "Base Damage: 1h: 280-683 2h:
        // 48-157", critical damage "215%", "366-366 over 6.0s" - and none of the glaive's: "Crushing Blow: 32",
        // "Attacker Takes Damage: 0 108".
        Assert.Equal("Blood Edge", ItemPresenter.Describe(sheet.Weapon!).Name);
        Assert.Equal((100, 32), (sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal((640, 1004), (sheet.AttackFromDexterity.Value, Of("tohit")));
        Assert.Equal((280, 683, 48, 157), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((32, 119), (Of("item_crushingblow"), sheet.CrushingBlowEfficiency.Value));
        Assert.Equal((0, 108), (Of("item_attackertakesdamage"), Of("item_attackertakeslightdamage")));
        Assert.Equal(215, sheet.CriticalMultiplier.Value);
        Assert.Equal((366, 366, 6.0), sheet.Poison);

        // What is no weapon's is as ever: "Damage Reduction: 6/10% 10/0%", "Hit Recovery: 24  Run/Walk: 73",
        // "Deadly Strike: 56", "Projectile Pierce: 48", "Magic Find: 431".
        Assert.Equal((6, 10, 10), (Of("normal_damage_reduction"), Of("damageresist"), Of("magic_damage_reduction")));
        Assert.Equal((24, 73), (Of("item_fastergethitrate"), Of("item_fastermovevelocity")));
        Assert.Equal((56, 48, 431), (Of("item_deadlystrike"), sheet.ProjectilePierce.Value, Of("item_magicbonus")));

        // "Open Wounds: 19%/+842": 304 for the level, 151 from his boots, and the Deep Wounds skill's 220 raised
        // 76% by its synergy.
        Assert.Equal((19, 842), (Of("item_openwounds"), sheet.OpenWoundsDamage.Value));
        Assert.Contains(sheet.OpenWoundsDamage.Parts, part => part is { Source: "Deep Wounds", Value: 387 });

        // A mastery's critical strike is for its own kind of attack: Throwing Mastery's 31 with a throw on the
        // right hand, nothing with a swing.
        Assert.Equal(31, sheet.CriticalStrike.Value);
        Assert.Equal(0, With(Attack).CriticalStrike.Value);
        Assert.Equal(0, With(Frenzy).CriticalStrike.Value);

        // "IAS (Frames): 0 / 2 / 6 / 13 / 19 / 29 (12) / 40 / 60 / 89 / 138 / 240 / 680" (Double Throw), "0 / 9 /
        // 18 / 30 (12) / 48 / 75 / 125" (Attack), "0 / 3 / 6 / 9 / 14 / 19 / 24 / 32 (17) / 40 / 54 / 70 / 92 /
        // 125 / 180 / 292 / 638" (Frenzy).
        void Row(CharacterSheet with, int[] row, (int, int) reached)
        {
            Assert.Equal(row, with.AttackRow!.Points.Select(point => point.Value));
            Assert.Equal(reached, with.AttackRow.Points[with.AttackRow.Reached]);
        }

        Row(sheet, [0, 2, 6, 13, 19, 29, 40, 60, 89, 138, 240, 680], (29, 12));
        Row(With(Attack), [0, 9, 18, 30, 48, 75, 125], (30, 12));
        Row(With(Frenzy), [0, 3, 6, 9, 14, 19, 24, 32, 40, 54, 70, 92, 125, 180, 292, 638], (32, 17));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void An_Amazon_with_a_bow_reads_as_the_games_page()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Amazon of level 99 with a Windforce; in town, nothing cast (two
        // screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(uint right)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "amazon-99-1--sheet-ingame-2026-10-13.d2s"));
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = right };
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false, OtherWeaponSet: false));
        }

        const uint MultipleShot = 12, Strafe = 26;
        var sheet = With(Strafe);
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Equal((82, 75), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((100, 80), (sheet.Resist("poison", 2).Value.Value, sheet.Resist("poison", 2).Maximum.Value));
        Assert.Equal((5, 4), (Of("item_absorbcold_percent"), Of("item_absorblight_percent")));
        Assert.Equal((7, 3, 10), (Of("normal_damage_reduction"), Of("damageresist"), Of("item_attackertakesdamage")));
        Assert.Equal(21, Of("passive_pois_pierce"));
        Assert.Equal((850, 600, 44, 2040), (sheet.AttackFromDexterity.Value, Of("tohit"), sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((56, 234, 141, 844), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((25, 57, 32), (Of("item_fastercastrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity")));
        Assert.Equal((90, 145), (sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal((25, 304), (Of("item_crushingblow"), sheet.OpenWoundsDamage.Value));
        Assert.Equal((64, 200, 98), (sheet.CriticalStrike.Value, sheet.CriticalMultiplier.Value, sheet.ProjectilePierce.Value));
        Assert.Equal((6, 7, 7, 0), (Of("lifedrainmindam"), Of("manadrainmindam"), Of("item_healafterkill"), Of("item_manaafterkill")));
        Assert.Equal((7, 15), sheet.Added("firemindam", "firemaxdam", "passive_fire_mastery"));
        Assert.Equal((540, 639, 2.0), sheet.Poison);
        Assert.Equal((146, 363), (Of("item_magicbonus"), Of("item_goldbonus")));

        // Strafe, arrow by arrow: "IAS (Frames): 0 / 3 / 11 / 26 / 42 / 46 / 80 / 120 (5|2|2|2|6) / 157" - five
        // arrows, put back half the way after each; a bow has no start frames.
        Assert.Equal([0, 3, 11, 26, 42, 46, 80, 120, 157], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal([5, 2, 2, 2, 6], sheet.AttackRow.Hits![sheet.AttackRow.Reached]);

        // Multiple Shot is the bow's plain row: "0 / 5 / 11 / 22 / 35 / 56 / 89 (9) / 147 / 292".
        var plain = With(MultipleShot);
        Assert.Equal([0, 5, 11, 22, 35, 56, 89, 147, 292], plain.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((89, 9), plain.AttackRow.Points[plain.AttackRow.Reached]);
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void An_Assassin_who_kicks_reads_as_the_games_page()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Assassin of level 98 with a one-handed weapon and a shield, Dragon Talon on the right
        // hand, in town (three screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(bool burstOfSpeed)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "assassin-98-1--sheet-ingame-2026-10-13.d2s"));
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = 255 };
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false,
                Pressed: new Dictionary<string, bool> { ["Cast:258"] = burstOfSpeed }, OtherWeaponSet: false));
        }

        var sheet = With(burstOfSpeed: false);
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Equal((80, 83), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((74, 75), (sheet.Resist("cold", 2).Value.Value, sheet.Resist("cold", 2).Maximum.Value));
        Assert.Equal(10, sheet.CurseResist.Value);
        Assert.Equal((13, 10), (Of("normal_damage_reduction"), Of("damageresist")));
        Assert.Equal((11, 28), (Of("passive_fire_mastery"), Of("passive_fire_pierce")));
        Assert.Equal((525, 305, 27, 4005), (sheet.AttackFromDexterity.Value, Of("tohit"), sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((165, 192, 3, 2), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((25, 49, 42), (Of("item_fastercastrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity")));
        Assert.Equal((110, 40), (sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal((85, 119, 10, 454), (Of("item_crushingblow"), sheet.CrushingBlowEfficiency.Value, Of("item_openwounds"), sheet.OpenWoundsDamage.Value));
        Assert.Equal((75, 75), (Math.Min(Of("item_deadlystrike"), sheet.DeadlyStrikeCap), sheet.DeadlyStrikeCap));
        Assert.Equal((160, 480), sheet.Added("magicmindam", "magicmaxdam", "passive_mag_mastery"));
        Assert.Equal((24, 61), sheet.Added("coldmindam", "coldmaxdam", "passive_cold_mastery"));
        Assert.Equal((106, 43, 111), (Of("item_normaldamage"), Of("item_magicbonus"), Of("item_goldbonus")));

        // "Block Rate: 5": Weapon Block's, a passive for claws, with no claw in hand.
        Assert.Equal(5, Of("item_fasterblockrate"));

        // "IAS (Frames): 0 / 10 / 26 / 30 (4|3|8) / 50 / 95" - three kicks, put back the whole way.
        Assert.Equal([0, 10, 26, 30, 50, 95], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal([4, 3, 8], sheet.AttackRow.Hits![sheet.AttackRow.Reached]);

        // Under the pointer on Defense: "Chance to Block: 34%".
        Assert.Equal(34, sheet.Block);

        // Burst of Speed cast: "Attack Rate: 163" - its hard points' 53 - and "IAS (Frames): 0 (4|3|6)".
        var fast = With(burstOfSpeed: true);
        Assert.Equal(163, fast.AttackRate.Value);
        Assert.Equal([0], fast.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal([4, 3, 6], fast.AttackRow.Hits![0]);
    }

    [CorpusFact]
    public void A_Sorceress_and_her_mercenarys_raised_aura_read_as_the_games_page()
    {
        if (!game.Available)
            return;

        // A ladder Sorceress of level 99 with an orb and a shield; her mercenary's Defiance is 18, and
        // what it wears adds 2 to all skills (three screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(bool outOfTown)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "sorceress-99-1--sheet-ingame-2026-10-13.d2s"));
            var all = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: outOfTown));
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: outOfTown,
                Pressed: all.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false)));
        }

        var sheet = With(outOfTown: false);
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Equal((160, 81), (sheet.Resist("cold", 2).Value.Value, sheet.Resist("cold", 2).Maximum.Value));
        Assert.Equal((45, 75), (sheet.Resist("poison", 2).Value.Value, sheet.Resist("poison", 2).Maximum.Value));
        Assert.Equal(75, sheet.CurseLength);
        Assert.Equal((9, 5), (Of("item_absorbcold"), Of("item_absorblight_percent")));
        Assert.Equal((0, 0, 4), (Of("normal_damage_reduction"), Of("damageresist"), Of("magic_damage_reduction")));

        // Cold Mastery, with the levels her orb and her shield add: "Elemental Mastery: 10% 210% 12%", cold
        // pierce "107%".
        Assert.Equal((10, 210, 12), (Of("passive_fire_mastery"), Of("passive_cold_mastery"), Of("passive_ltng_mastery")));
        Assert.Equal(107, Of("passive_cold_pierce"));
        Assert.Equal((850, 45, 1757), (sheet.AttackFromDexterity.Value, sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((127, 90, 30), (Of("item_fastercastrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity")));
        Assert.Equal((13, 10), (Of("item_healafterkill"), Of("item_manaafterkill")));
        Assert.Equal((1013, 1608), sheet.Added("coldmindam", "coldmaxdam", "passive_cold_mastery"));

        // A Sorceress's swing with a one-handed weapon, two start frames: "0 (20) / 2 / 6 / 11 / 20 / 29 / 42 /
        // 58 / 83 / 120 / 194"; and her rows.
        Assert.Equal([0, 2, 6, 11, 20, 29, 42, 58, 83, 120, 194], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal(20, sheet.AttackRow.Points[0].Frames);
        Assert.Equal(105, sheet.CastRateRow!.Points[sheet.CastRateRow.Reached].Value);
        Assert.Equal(86, sheet.HitRecoveryRow!.Points[sheet.HitRecoveryRow.Reached].Value);

        // Under the pointer on Defense: "Chance to Block: 55%".
        Assert.Equal(55, sheet.Block);

        // Out of town, the mercenary beside her: "Damage Reduction: 10/10%" - Defiance 20, the 18 of its kind
        // and the 2 its gear adds.
        var outside = With(outOfTown: true);
        Assert.Equal((10, 10), (outside.Totals.Of("normal_damage_reduction"), outside.Totals.Of("damageresist")));
        Assert.Contains(outside.Buffs, buff => buff is { Kind: BuffKind.MercenaryAura, Name: "Defiance", Level: 20, BaseLevel: 18 });
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_Paladins_own_aura_gives_him_its_own_lines()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Paladin of level 96 with a Lightsabre; in town, nothing cast (two
        // screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(uint right)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "paladin-96-1--sheet-ingame-2026-10-13.d2s"));
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = right };
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false,
                Pressed: new Dictionary<string, bool> { ["Cast:117"] = false }, OtherWeaponSet: false));
        }

        const uint HolyFire = 102, Vengeance = 111;
        var sheet = With(Vengeance);
        int Of(string stat) => sheet.Totals.Of(stat);

        // The resist auras' hard points raise the ceilings: "Fire Resist: 98 / 86", "Lightning Resist: 80 / 88".
        Assert.Equal((98, 86), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((80, 88), (sheet.Resist("lightning", 2).Value.Value, sheet.Resist("lightning", 2).Maximum.Value));
        Assert.Equal((65, 125), (sheet.Resist("poison", 2).Value.Value, sheet.PoisonLength(2)));
        Assert.Equal((10, 51, 82), (Of("item_absorblight_percent"), Of("damageresist"), Of("item_attackertakesdamage")));
        Assert.Equal((20, 62), (Of("passive_fire_mastery"), Of("passive_ltng_mastery")));
        Assert.Equal((14, 10, 73), (Of("passive_fire_pierce"), Of("passive_cold_pierce"), Of("passive_ltng_pierce")));
        Assert.Equal((955, 48, 1557), (sheet.AttackFromDexterity.Value, sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((143, 180), (Of("mindamage"), Of("maxdamage")));
        Assert.Equal((59, 33, 130, 57), (Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal([0, 8, 24, 54], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal(291, sheet.OpenWoundsDamage.Value);
        Assert.Equal((60, 120), sheet.Added("magicmindam", "magicmaxdam", "passive_mag_mastery"));
        Assert.Equal((42, 4004), sheet.Added("lightmindam", "lightmaxdam", "passive_ltng_mastery"));
        Assert.Equal((73, 143), (Of("item_magicbonus"), Of("item_goldbonus")));

        // The fire line: "284-555" with no aura, "555-897" with Holy Fire on the right hand - its 226 and 285
        // on his own attacks, the mastery's 20% over all of it.
        Assert.Equal((284, 555), sheet.Added("firemindam", "firemaxdam", "passive_fire_mastery"));
        var aura = With(HolyFire);
        Assert.Equal((555, 897), aura.Added("firemindam", "firemaxdam", "passive_fire_mastery"));
        Assert.Contains(aura.Stat("firemindam").Parts, part => part is { Source: "Holy Fire", Value: 226 });
        Assert.Null(aura.AttackRow);
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_Necromancer_in_a_whole_set_and_its_form_reads_as_the_games_page()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Necromancer of level 98 in Trang-Oul's whole set, on his second weapon set
        // (Death's Web and Trang-Oul's Wing); in town (two screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(bool vampire)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "necromancer-98-1--sheet-ingame-2026-10-13.d2s"));
            return CharacterSheet.For(save, data, new CharacterSheet.Options(WeaponSets.Swapped, OutOfTown: false,
                Pressed: new Dictionary<string, bool> { ["Cast:388"] = vampire }));
        }

        var sheet = With(vampire: false);
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Empty(sheet.LeftOut);
        Assert.Equal((76, 75), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((27, 107), (sheet.Resist("lightning", 2).Value.Value, sheet.Resist("poison", 2).Value.Value));
        Assert.Equal((0, 9, 5, 442), (Of("normal_damage_reduction"), Of("damageresist"), Of("magic_damage_reduction"), Of("item_attackertakesdamage")));
        Assert.Equal((109, 75), (Of("passive_pois_mastery"), Of("passive_pois_pierce")));
        Assert.Equal((990, 51, 2494), (sheet.AttackFromDexterity.Value, sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((22, 28), (Of("mindamage"), Of("maxdamage")));
        Assert.Equal((60, 89, 50), (Of("item_fastercastrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity")));
        Assert.Equal([0, 7, 15, 23, 35, 52, 78, 117, 194], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal(48, sheet.CastRateRow!.Points[sheet.CastRateRow.Reached].Value);
        Assert.Equal((300, 26), (sheet.OpenWoundsDamage.Value, Of("lifedrainmindam")));
        Assert.Equal((20, 5), (Of("item_healafterkill"), Of("item_manaafterkill")));
        Assert.Equal((9236, 9236, 22.0), sheet.Poison);

        // Vampire Form, which the whole set lends: "Cast Rate: 100", "Elemental Mastery: ... 129% 30%",
        // "HP/MP per Kill: 30 / 5", poison "10119-10119 over 22.0s" - the form's 20% poison mastery on it.
        var vampire = With(vampire: true);
        int Then(string stat) => vampire.Totals.Of(stat);
        Assert.Equal("monsterset", vampire.Form?.Name);
        Assert.Equal((100, 129, 30), (Then("item_fastercastrate"), Then("passive_pois_mastery"), Then("passive_mag_mastery")));
        Assert.Equal((30, 5), (Then("item_healafterkill"), Then("item_manaafterkill")));
        Assert.Equal((10119, 10119, 22.0), vampire.Poison);

        // Where the sheet and the page part, knowingly: the page's cast row in the form is the Necromancer's
        // own - its code turns to a form's rows for the wolf and the bear alone - and the sheet's is the
        // vampire's animation's, which is the wiki's "Necromancer (Vampire)" row.
        Assert.Equal([0, 6, 11, 18, 24, 35, 48, 65, 86, 120, 180], vampire.CastRateRow!.Points.Select(point => point.Value));

        // Its attack row is the page's own: the vampire's animation at its own speed, "IAS (Frames): 0 (20) / 4 / 9 /
        // 16 / 24 / 37 / 52 / 75 / 109 / 168" (screenshot).
        Assert.Equal([0, 4, 9, 16, 24, 37, 52, 75, 109, 168], vampire.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((0, 20), vampire.AttackRow.Points[vampire.AttackRow.Reached]);
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_Druid_with_a_spirit_and_a_wolf_reads_as_the_games_page_in_town()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Druid of level 95 with a Tomb Reaver; in town, Spirit of Barbs summoned, then a wolf
        // with Fury on the right hand (two screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(bool wolf)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "druid-95-1--sheet-ingame-2026-10-13.d2s"));
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = wolf ? 248u : 246u };
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false,
                Pressed: new Dictionary<string, bool> { ["Spirit:296"] = true, ["Cast:223"] = wolf, ["Cast:232"] = false }, OtherWeaponSet: false));
        }

        var sheet = With(wolf: false);
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Equal((135, 75), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((96, 80), (sheet.Resist("poison", 2).Value.Value, sheet.Resist("poison", 2).Maximum.Value));
        Assert.Equal(20, sheet.CurseResist.Value);
        Assert.Equal((5, 4), (Of("item_absorbcold_percent"), Of("item_absorblight")));
        Assert.Equal((0, 27, 6), (Of("normal_damage_reduction"), Of("damageresist"), Of("magic_damage_reduction")));
        Assert.Equal((485, 907, 25, 3028), (sheet.AttackFromDexterity.Value, Of("tohit"), sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((0, 19, 198, 928), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((49, 30, 90, 155), (Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal((35, 10, 483), (Of("item_crushingblow"), Of("item_openwounds"), sheet.OpenWoundsDamage.Value));
        Assert.Equal((2, 3, 10), (Of("lifedrainmindam"), Of("manadrainmindam"), Of("item_healafterkill")));
        Assert.Equal((91, 121), (Of("item_magicbonus"), Of("item_goldbonus")));

        // The spirit's aura does not reach her in town: "Attacker Takes Damage: 952" and "Deadly Strike: 18 /
        // 75" - her gear's alone.
        Assert.Equal((952, 18, 75), (Of("item_attackertakesdamage"), Of("item_deadlystrike"), sheet.DeadlyStrikeCap));
        Assert.DoesNotContain(sheet.Buffs, buff => buff.Kind == BuffKind.Spirit);

        // A wolf in full gear: "Attack Rate: 149", "Damage Reduction: 4/27% 10/0%", the wolf's rows.
        var wolf = With(wolf: true);
        Assert.Equal(149, wolf.AttackRate.Value);
        Assert.Equal((4, 27, 10), (wolf.Totals.Of("normal_damage_reduction"), wolf.Totals.Of("damageresist"), wolf.Totals.Of("magic_damage_reduction")));
        Assert.Equal([0, 6, 14, 26, 40, 60, 95, 157], wolf.CastRateRow!.Points.Select(point => point.Value));
        Assert.Equal(42, wolf.HitRecoveryRow!.Points[wolf.HitRecoveryRow.Reached].Value);
        // Fury on the right: a skill of several hits in a form, not counted (A_forms_attack_reads_as_PD2s_wereform_tables).
        Assert.Null(wolf.AttackRow);

        // Out of town the spirit's aura is hers: "Attacker Takes Damage: 1487", "Deadly Strike: 50 / 81" - 7 +
        // its level 25, and a ceiling a quarter of the level higher, the spirit's own hard points. And Fury on
        // the right hand of a Druid who is no wolf has its row strike by strike: "IAS (Frames): 0 / 2 / 6 / 11 /
        // 20 / 29 / 30 / 44 / 58 / 63 / 92 / 120 / 142 (7|6|9) / 250".
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "druid-95-1--sheet-ingame-2026-10-13.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 248 };
        var outside = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true,
            Pressed: new Dictionary<string, bool> { ["Spirit:296"] = true, ["Cast:223"] = false, ["Cast:232"] = false }, OtherWeaponSet: false));
        Assert.Equal((1487, 50, 81), (outside.Totals.Of("item_attackertakesdamage"), outside.Totals.Of("item_deadlystrike"), outside.DeadlyStrikeCap));
        Assert.Equal([0, 2, 6, 11, 20, 29, 30, 44, 58, 63, 92, 120, 142, 250], outside.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal([7, 6, 9], outside.AttackRow.Hits![outside.AttackRow.Reached]);
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_bear_in_full_gear_with_Hunger_reads_as_the_games_page()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Druid of level 97 with a Warlord's Trust: out of town, Heart of Wolverine
        // summoned, a bear, Hunger used on a corpse (screenshot).
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "druid-97-1--sheet-ingame-2026-10-13.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 242 };
        var all = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true));
        var pressed = all.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false);
        pressed["Spirit:297"] = pressed["Cast:228"] = pressed["Cast:242"] = true;
        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true, Pressed: pressed, OtherWeaponSet: false));
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Equal("bear", sheet.Form?.Name);
        Assert.Equal((151, 77), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((132, 86, 150), (sheet.Resist("poison", 2).Value.Value, sheet.Resist("poison", 2).Maximum.Value, sheet.PoisonLength(2)));
        Assert.Equal((5, 6, 6), (Of("item_absorbcold_percent"), Of("item_absorblight"), Of("item_absorblight_percent")));
        Assert.Equal((4, 42, 540), (Of("normal_damage_reduction"), Of("damageresist"), Of("item_attackertakesdamage")));
        Assert.Equal((255, 170, 14, 2042), (sheet.AttackFromDexterity.Value, Of("tohit"), sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((201, 1015), (Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((106, 33, 115, 100), (Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal([0, 7, 15, 26, 40, 63, 99, 163], sheet.CastRateRow!.Points.Select(point => point.Value));
        Assert.Equal(86, sheet.HitRecoveryRow!.Points[sheet.HitRecoveryRow.Reached].Value);
        Assert.NotNull(sheet.AttackRow);

        // Hunger's: "Open Wounds: 77%/+5380" - its deep wounds raised by its synergies - and "Life Leech: 16".
        Assert.Equal((77, 5380), (Of("item_openwounds"), sheet.OpenWoundsDamage.Value));
        Assert.Equal((16, 5), (Of("lifedrainmindam"), Of("manadrainmindam")));
        Assert.Equal((5, 2, 53, 158), (Of("item_healafterkill"), Of("item_manaafterkill"), Of("item_magicbonus"), Of("item_goldbonus")));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Of_two_unlike_claws_the_faster_is_the_one_counted()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Assassin of level 99 with a Jade Talon (speed -10) and Firelizard's Talons (-20);
        // Fists of Fire on the right hand, in town (two screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(bool burstOfSpeed)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "assassin-99-1--sheet-ingame-2026-10-13.d2s"));
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = 259 };
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false,
                Pressed: new Dictionary<string, bool> { ["Cast:258"] = burstOfSpeed }, OtherWeaponSet: false));
        }

        var sheet = With(burstOfSpeed: false);
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Equal((119, 81), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((84, 81, 50), (sheet.Resist("cold", 2).Value.Value, sheet.Resist("cold", 2).Maximum.Value, sheet.ColdLength));
        Assert.Equal((6, 5, 921), (Of("item_absorbfire"), Of("item_absorblight_percent"), Of("item_attackertakesdamage")));
        Assert.Equal((48, 60), (Of("passive_fire_mastery"), Of("passive_fire_pierce")));
        Assert.Equal((750, 442, 38, 3706), (sheet.AttackFromDexterity.Value, Of("tohit"), sheet.DefenseFromDexterity.Value, Of("armorclass")));

        // The faster claw's: "Base Damage: 1h: 106-259", "Attack Rate: 120  IAS: 15"; and "Block Rate: 138",
        // Weapon Block's with every item's.
        Assert.Equal("Firelizard's Talons", ItemPresenter.Describe(sheet.Weapon!).Name);
        Assert.Equal((106, 259), (Of("mindamage"), Of("maxdamage")));
        Assert.Equal((120, 15, 138), (sheet.AttackRate.Value, Of("item_fasterattackrate"), Of("item_fasterblockrate")));
        Assert.Equal((89, 91), (Of("item_fastergethitrate"), Of("item_fastermovevelocity")));

        // The claw's swing - 11 frames at a speed of 208: "IAS (Frames): 0 / 5 (10) / 19 / 42 / 86"; with
        // Burst of Speed "Attack Rate: 169" and "0 / 2 (7)".
        Assert.Equal([0, 5, 19, 42, 86], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((5, 10), sheet.AttackRow.Points[sheet.AttackRow.Reached]);
        var fast = With(burstOfSpeed: true);
        Assert.Equal(169, fast.AttackRate.Value);
        Assert.Equal([0, 2], fast.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((2, 7), fast.AttackRow.Points[fast.AttackRow.Reached]);

        Assert.Equal((20, 304, 6, 4), (Of("item_crushingblow"), sheet.OpenWoundsDamage.Value, Of("lifedrainmindam"), Of("manadrainmindam")));
        Assert.Equal((500, 1110), sheet.Added("firemindam", "firemaxdam", "passive_fire_mastery"));
        Assert.Equal((15, 76), (Of("item_magicbonus"), Of("item_goldbonus")));

        // "Critical Strike: 0 / 75 / 200%" - with Fists of Fire on the right hand and with Attack (a third
        // screenshot): Claw and Dagger Mastery's 25 is not on the page.
        Assert.Equal(0, sheet.CriticalStrike.Value);
        Assert.Equal(25, Of("passive_mastery_melee_crit"));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_Barbarian_with_an_aura_on_his_shield_reads_as_the_games_page_in_town()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Barbarian of level 99 with a Winged Axe and an Exile (Defiance 16); in town,
        // nothing cast (two screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "barbarian-99-4--sheet-ingame-2026-10-13.d2s"));
        var all = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false));
        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false,
            Pressed: all.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false)));
        int Of(string stat) => sheet.Totals.Of(stat);

        // The character screen: 146, 237, 391, 39 - and "Chance to Block: 75%" under the pointer on Defense.
        Assert.Equal((146, 237, 391, 39), (sheet.Strength.Value, sheet.Dexterity.Value, sheet.Vitality.Value, sheet.Energy.Value));
        Assert.Equal(75, sheet.Block);

        // His own shield's Defiance works in town: "Damage Reduction: 8/8% 24/0%".
        Assert.Equal((8, 8, 24), (Of("normal_damage_reduction"), Of("damageresist"), Of("magic_damage_reduction")));
        Assert.Contains(sheet.Buffs, buff => buff is { Kind: BuffKind.ItemAura, Name: "Defiance", Level: 16, On: true });

        Assert.Equal((91, 79), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value));
        Assert.Equal((112, 101, 25), (sheet.Resist("lightning", 2).Value.Value, sheet.Resist("poison", 2).Value.Value, sheet.CurseResist.Value));
        Assert.Equal((5, 35), (Of("item_absorblight_percent"), Of("item_attackertakesdamage")));
        Assert.Equal((1170, 783, 59, 3100), (sheet.AttackFromDexterity.Value, Of("tohit"), sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((154, 570, 56, 121), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((10, 50, 85, 50), (Of("item_fastercastrate"), Of("item_fasterblockrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity")));
        Assert.Equal((110, 70), (sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal([0, 6, 16, 30, 52, 89], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((11, 7, 100, 3), (Of("lifedrainmindam"), Of("manadrainmindam"), sheet.ProjectilePierce.Value, Of("item_manaafterkill")));
        Assert.Equal((10, 22), sheet.Added("firemindam", "firemaxdam", "passive_fire_mastery"));
        Assert.Equal((1, 53), sheet.Added("lightmindam", "lightmaxdam", "passive_ltng_mastery"));
        Assert.Equal((142, 112), (Of("item_magicbonus"), Of("item_goldbonus")));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_Sorceress_casting_Chain_Lightning_reads_as_the_games_page()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Sorceress of level 97 with Infinity; in town, nothing cast - Chain Lightning, Lightning
        // and Ice Blast on the right hand in turn (three screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(uint right)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "sorceress-97-1--sheet-ingame-2026-10-13.d2s"));
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = right };
            var all = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false));
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false,
                Pressed: all.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false)));
        }

        const uint IceBlast = 45, Lightning = 49, ChainLightning = 53;
        var sheet = With(ChainLightning);
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Equal((94, 91, 100), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("cold", 2).Value.Value, sheet.ColdLength));
        Assert.Equal((102, 82), (sheet.Resist("lightning", 2).Value.Value, sheet.Resist("lightning", 2).Maximum.Value));
        Assert.Equal(73, sheet.CurseLength);
        Assert.Equal((14, 10, 498, 80), (Of("passive_fire_mastery"), Of("passive_cold_mastery"), Of("passive_ltng_mastery"), Of("passive_ltng_pierce")));
        Assert.Equal((130, 9, 961), (sheet.AttackFromDexterity.Value, sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((338, 483), (Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((125, 60, 75, 90), (Of("item_fastercastrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value));
        Assert.Equal((40, 295, 5), (Of("item_crushingblow"), sheet.OpenWoundsDamage.Value, Of("item_manaafterkill")));
        Assert.Equal((35, 4215), sheet.Added("lightmindam", "lightmaxdam", "passive_ltng_mastery"));
        Assert.Equal((4, 6), sheet.Added("coldmindam", "coldmaxdam", "passive_cold_mastery"));
        Assert.Equal((3, 3, 2.0), sheet.Poison);

        // "FCR: 7 / 15 / 23 / 35 / 52 / 78 / 117 / 194" with 117 reached under Chain Lightning - and the plain
        // "9 / 20 / 37 / 63 / 105 / 200" under Lightning and Ice Blast: PD2's Lightning is no slow spell.
        Assert.Equal([0, 7, 15, 23, 35, 52, 78, 117, 194], sheet.CastRateRow!.Points.Select(point => point.Value));
        Assert.Equal(117, sheet.CastRateRow.Points[sheet.CastRateRow.Reached].Value);
        Assert.Equal(60, sheet.HitRecoveryRow!.Points[sheet.HitRecoveryRow.Reached].Value);
        foreach (uint plain in new[] { Lightning, IceBlast })
        {
            var row = With(plain).CastRateRow!;
            Assert.Equal([0, 9, 20, 37, 63, 105, 200], row.Points.Select(point => point.Value));
            Assert.Equal(105, row.Points[row.Reached].Value);
        }
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_Sorceress_casting_Frozen_Orb_reads_as_the_games_page()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Sorceress of level 96 with Death's Fathom and a Lidless Wall; in town, nothing
        // cast, Frozen Orb on the right hand (two screenshots).
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "sorceress-96-1--sheet-ingame-2026-10-13.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 64 };
        var all = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false));
        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false,
            Pressed: all.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false)));
        int Of(string stat) => sheet.Totals.Of(stat);

        Assert.Equal((95, 84, 81), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("cold", 2).Value.Value, sheet.Resist("cold", 2).Maximum.Value));
        Assert.Equal((77, 74), (sheet.Resist("lightning", 2).Value.Value, sheet.Resist("poison", 2).Value.Value));
        Assert.Equal(8, Of("item_absorbcold"));
        Assert.Equal((160, 84), (Of("passive_cold_mastery"), Of("passive_cold_pierce")));
        Assert.Equal((275, 16, 1769), (sheet.AttackFromDexterity.Value, sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((38, 53, 8, 0), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((120, 64, 40, 90), (Of("item_fastercastrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value));
        Assert.Equal(117, sheet.CastRateRow!.Points[sheet.CastRateRow.Reached].Value);
        Assert.Equal(60, sheet.HitRecoveryRow!.Points[sheet.HitRecoveryRow.Reached].Value);
        Assert.Equal((291, 16, 13), (sheet.OpenWoundsDamage.Value, Of("item_healafterkill"), Of("item_manaafterkill")));
        Assert.Equal((478, 769), sheet.Added("coldmindam", "coldmaxdam", "passive_cold_mastery"));
        Assert.Equal((1, 3), sheet.Added("lightmindam", "lightmaxdam", "passive_ltng_mastery"));
        Assert.Equal((109, 172), (Of("item_magicbonus"), Of("item_goldbonus")));

        // Under the pointer on Defense: "Chance to Block: 9%" - 36 x (51 - 15) / 192, the formula at its low end.
        Assert.Equal(9, sheet.Block);
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Of_two_weapons_alike_in_speed_the_other_slots_counts_and_a_mercenarys_aura_is_raised()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Barbarian of level 99 with two Winds - the main slot's of 263% enhanced damage,
        // the other's of 254% - and a mercenary with Blessed Aim 18 and 5 to all skills on its gear (
        // two screenshots, in town and out of it).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(bool outOfTown)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "barbarian-99-1--sheet-ingame-2026-10-13.d2s"));
            var all = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: outOfTown));
            return CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: outOfTown,
                Pressed: all.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false)));
        }

        var sheet = With(outOfTown: false);
        int Of(string stat) => sheet.Totals.Of(stat);

        // "Base Damage: 1h: 116-157" - the other slot's Wind; alike in speed, the main slot's is not the one.
        Assert.Equal(D2SLib.Model.Save.ItemLocation.LeftHand, sheet.Weapon!.Location);
        Assert.Equal((116, 157, 0, 5), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));
        Assert.Equal((1280, 204, 64, 919), (sheet.AttackFromDexterity.Value, Of("tohit"), sheet.DefenseFromDexterity.Value, Of("armorclass")));
        Assert.Equal((88, 128, 120, 92), (Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value, Of("item_fasterattackrate")));
        Assert.Equal([0, 5, 16, 34, 63], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal((83, 73, 53, 84), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("cold", 2).Value.Value, sheet.Resist("lightning", 2).Value.Value, sheet.Resist("poison", 2).Value.Value));
        Assert.Equal((1, 285), (Of("damageresist"), Of("item_attackertakesdamage")));
        Assert.Equal((15, 10, 446), (Of("item_crushingblow"), Of("item_openwounds"), sheet.OpenWoundsDamage.Value));
        Assert.Equal((215, 26, 11, 93), (sheet.CriticalMultiplier.Value, Of("lifedrainmindam"), Of("manadrainmindam"), sheet.ProjectilePierce.Value));
        Assert.Equal((13, 5), (Of("item_healafterkill"), Of("item_manaafterkill")));
        Assert.Equal((588, 588, 6.0), sheet.Poison);
        Assert.Equal(30, Of("item_deadlystrike"));

        // Out of town the mercenary's Blessed Aim - 18 and its gear's 5 - gives 5 + 23 / 2 = 16: "Deadly Strike:
        // 46"; at 18 it would read 44.
        var outside = With(outOfTown: true);
        Assert.Equal(46, outside.Totals.Of("item_deadlystrike"));
        Assert.Contains(outside.Buffs, buff => buff is { Kind: BuffKind.MercenaryAura, Name: "Blessed Aim", Level: 23 });
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Charge_has_a_row_of_its_own()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        // A ladder Paladin of level 96 with Doombringer, Charge on the right hand; in town and out of
        // it (his mercenary's Blessed Aim cannot raise a deadly strike at its ceiling) - two
        // screenshots.
        using var data = GameData.Open(Game.Root, Game.Tables);
        foreach (bool outOfTown in new[] { false, true })
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "paladin-96-2--sheet-ingame-2026-10-13.d2s"));
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = 107 };
            var all = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: outOfTown));
            var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: outOfTown,
                Pressed: all.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false)));
            int Of(string stat) => sheet.Totals.Of(stat);

            Assert.Equal((91, 85, 139, 83), (sheet.Resist("fire", 2).Value.Value, sheet.Resist("fire", 2).Maximum.Value,
                sheet.Resist("lightning", 2).Value.Value, sheet.Resist("lightning", 2).Maximum.Value));
            Assert.Equal((7, 3, 3, 118), (Of("damageresist"), Of("magic_damage_reduction"), Of("item_attackertakesdamage"), Of("item_attackertakeslightdamage")));
            Assert.Equal((450, 817, 23, 1721), (sheet.AttackFromDexterity.Value, Of("tohit"), sheet.DefenseFromDexterity.Value, Of("armorclass")));
            Assert.Equal((329, 633, 747, 887), (Of("mindamage"), Of("maxdamage"), Of("secondary_mindamage"), Of("secondary_maxdamage")));
            Assert.Equal((30, 40, 50, 110, 85), (Of("item_fasterblockrate"), Of("item_fastergethitrate"), Of("item_fastermovevelocity"), sheet.AttackRate.Value, Of("item_fasterattackrate")));
            Assert.Equal((15, 10, 431), (Of("item_crushingblow"), Of("item_openwounds"), sheet.OpenWoundsDamage.Value));
            Assert.Equal((75, 75), (Math.Min(Of("item_deadlystrike"), sheet.DeadlyStrikeCap), sheet.DeadlyStrikeCap));
            Assert.Equal((17, 2, 3, 210, 126), (Of("lifedrainmindam"), Of("manadrainmindam"), Of("item_manaafterkill"), Of("item_magicbonus"), Of("item_goldbonus")));

            // "IAS (Frames): 0 / 14 / 40 (9) / 95 / 250".
            Assert.Equal([0, 14, 40, 95, 250], sheet.AttackRow!.Points.Select(point => point.Value));
            Assert.Equal((40, 9), sheet.AttackRow.Points[sheet.AttackRow.Reached]);

            // Attack on the right: Doombringer, a two-handed sword in both hands, swung with its 2hs - the
            // Paladin's 18 frames, not the 15 of 1hs: "IAS (Frames): 0 / 4 / 13 / 23 / 39 / 60 (11) / 99" (
            // two screenshots, in town and out of it).
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = 0 };
            var swung = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: outOfTown,
                Pressed: all.Buffs.Where(buff => buff.Toggles).ToDictionary(buff => buff.Key, _ => false)));
            Assert.Equal("2hs", swung.WeaponClass);
            Assert.Equal([0, 4, 13, 23, 39, 60, 99], swung.AttackRow!.Points.Select(point => point.Value));
            Assert.Equal((60, 11), swung.AttackRow.Points[swung.AttackRow.Reached]);
        }
    }

    [CorpusFact]
    public void A_spell_cast_as_a_sequence_has_a_cast_row_of_its_own()
    {
        if (!game.Available)
            return;

        // The game's page lists "7 / 15 / 23 / 35 / 52 / 78 / 117 / 194" with Chain Lightning or Frozen Orb on
        // the right hand, and the Sorceress's plain row with anything else.
        // A level 99 Sorceress with no Chain Lightning nor Lightning: a point in each, or the box holds Attack (A_box_whose_skill_is_gone_holds_attack).
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet With(uint skill)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "sorceress-99-1--stress.d2s"));
            save.ClassSkills.Skills[SlotOf(data, 1, "Chain Lightning")].Points = 1;
            save.ClassSkills.Skills[SlotOf(data, 1, "Lightning")].Points = 1;
            save.RightSkill = new D2SLib.Model.Save.Skill { Id = skill };
            return CharacterSheet.For(save, data);
        }

        Assert.Equal([0, 7, 15, 23, 35, 52, 78, 117, 194], With(53).CastRateRow!.Points.Select(point => point.Value));
        Assert.Equal([0, 9, 20, 37, 63, 105, 200], With(49).CastRateRow!.Points.Select(point => point.Value));
        Assert.Equal([0, 7, 15, 23, 35, 52, 78, 117, 194], With(64).CastRateRow!.Points.Select(point => point.Value));
        Assert.Null(With(53).AttackRow);
        Assert.Equal([0, 9, 20, 37, 63, 105, 200], With(54).CastRateRow!.Points.Select(point => point.Value));
    }

    private static int SlotOf(GameData data, byte classId, string skill) =>
        SkillTree.For(data, classId).Pages.SelectMany(page => page.Skills).Single(entry => entry.Name == skill).Slot;

    [CorpusFact]
    public void A_formula_is_counted_in_the_games_whole_numbers()
    {
        if (!game.Available)
            return;

        var context = new SkillCalc.Context(_ => (0, 0), 85, _ => 0);
        int? Value(string skill, int level, int baseLevel, string formula) =>
            new SkillCalc(SkillRows.Named(skill)!, level, baseLevel, context).Evaluate(formula);

        // Werewolf 1 is +20 attack rate ("Attack Rate: 120"): 110 x 1 / 7 first, then x 70 / 100, then + 10.
        // The product taken first gives 21.
        Assert.Equal(20, Value("Wearwolf", 1, 1, "dm34"));

        // Frenzy 20 at its full: 42 ("Attack Rate: 132" on two axes of speed 10).
        Assert.Equal(42, Value("Frenzy", 20, 20, "dm56"));

        // Battle Command lent at level 18: "+1 to All Skills" - a lent skill has no hard point.
        Assert.Equal(1, Value("Battle Command", 18, 0, "1 + blvl / 10"));
        Assert.Equal(3, Value("Battle Command", 25, 20, "1 + blvl / 10"));

        // The words: min, a comparison, a choice, another skill's row - and null for what it does not know.
        Assert.Equal(40, Value("Frenzy", 20, 20, "min(40, 50)"));
        Assert.Equal(7, Value("Frenzy", 20, 20, "lvl > 10 ? 7 : 3"));
        Assert.Equal(85, Value("Frenzy", 20, 20, "ulvl"));
        Assert.Null(Value("Frenzy", 20, 20, "nonesuch + 1"));

        // A row's own calc by name, and another's through skill(): Feral Rage 10's calc2, lvl / 2 + 3 = 8, and its
        // life leech line, which reads it so - 1 + 9 x (min(1 x 10 / 2 + 4, 10) - 1) / (8 - 1) = 11.
        Assert.Equal(8, Value("Feral Rage", 10, 10, "clc2"));
        var feral = new SkillCalc.Context(name => name == "Feral Rage" ? (10, 10) : (0, 0), 85, _ => 0);
        var rage = SkillRows.Named("Feral Rage")!;
        string leech = rage.Auras.Single(line => line.Stat == "lifedrainmindam").Calc;
        Assert.Equal(11, new SkillCalc(rage, 10, 10, feral).Evaluate(leech));
    }

    [CorpusFact]
    public void An_attack_row_is_the_games_own()
    {
        // Frenzy at full stacks on two Twin Axes - attack rate 132, a sequence of 17 frames:
        // "IAS (Frames): 0 (16) / 6 / 14 / 24 / 39 / 60 / 95 / 163".
        var row = Breakpoints.AttackRow(frames: 17, animationSpeed: 256, start: 0, attackRate: 132, sequence: true);
        Assert.Equal([0, 6, 14, 24, 39, 60, 95, 163], row.Select(point => point.Value));
        Assert.Equal(16, row[0].Frames);

        // Jab with a two-handed thrust at attack rate 110, a sequence of 18 frames: "0 / 4 / 7 / 13 / 18 (18) / 24
        // / 35 / 46 / 63 / 83 / 117 / 168 / 280" (SpearZon).
        var jab = Breakpoints.AttackRow(frames: 18, animationSpeed: 256, start: 0, attackRate: 110, sequence: true);
        Assert.Equal([0, 4, 7, 13, 18, 24, 35, 46, 63, 83, 117, 168, 280], jab.Select(point => point.Value));
        Assert.Equal(18, jab[4].Frames);
    }

    [CorpusFact]
    public void A_skill_that_strikes_more_than_once_reads_strike_by_strike()
    {
        // Fend with the same spear - three strikes, put back 60% after each, the thrust's 18 frames acting on
        // the 11th, an Amazon's two start frames: "0 / 4 / 8 (9, 6, 10) / 22 / 23 / 42 / 60 / 75 / 142"
        // (SpearZon).
        var (points, hits) = Breakpoints.MultiHitRow(strikes: 3, actsOn: 11, frames: 18, animationSpeed: 256, start: 2, attackRate: 110, rollback: 60);
        Assert.Equal([0, 4, 8, 22, 23, 42, 60, 75, 142], points.Select(point => point.Value));
        Assert.Equal([9, 6, 10], hits[2]);

        if (!game.Available)
            return;

        // And through the sheet: the corpus's spear Amazon holds Fend in her right hand.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var sheet = CharacterSheet.For(save, data);
        Assert.Equal("Fend", sheet.RightSkill?.Name);
        Assert.Equal(110, sheet.AttackRate.Value);
        Assert.Equal([0, 4, 8, 22, 23, 42, 60, 75, 142], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal(3, sheet.AttackRow.Hits![0].Count);
    }

    [CorpusFact]
    public void Season_14_characters_read_as_the_games_page()
    {
        // Season 14 saves: read only with the season's own tables, as every check of them is (Game.Tables is S13
        // until the S14 corpus).
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // Read in game, from screenshots.
        using var data = GameData.Open(Game.Root, Game.Tables);
        D2SLib.Model.Save.D2S Load(string name)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-27", name + ".d2s"));
            return save;
        }

        // PsnStrike in town: Plague's Cleansing counts its curse line (48) and leaves poison's length at Hell's 200%.
        var psn = CharacterSheet.For(Load("PsnStrike"), data, new CharacterSheet.Options(OutOfTown: false));
        Assert.Equal((200, 48), (psn.PoisonLength(2), psn.CurseResist.Value));
        Assert.Equal(386, psn.OpenWoundsDamage.Value);

        // CorpseExp out of town, and its mercenary: open wounds as Season 14 has them, the Iron Golem's aura up.
        var corpse = Load("CorpseExp");
        var sheet = CharacterSheet.For(corpse, data);
        Assert.Equal(271, sheet.OpenWoundsDamage.Value);
        Assert.Contains(sheet.Buffs, buff => buff is { Kind: BuffKind.ItemAura, Name: "Fanaticism" } && buff.From.EndsWith("(Iron Golem)"));
        Assert.Equal(471, CharacterSheet.ForMercenary(corpse, data)!.OpenWoundsDamage.Value);
    }

    [CorpusFact]
    public void Season_14_mercenaries_of_acts_3_4_and_5_read_as_their_pages_in_town()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // Screenshots: three hired in Hell and nothing worn, each page read in its act's town.
        using var data = GameData.Open(Game.Root, Game.Tables);
        CharacterSheet Page(string name)
        {
            var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-28", name + ".d2s"));
            return CharacterSheet.ForMercenary(save, data, new CharacterSheet.Options(OutOfTown: false))!;
        }

        // The Barbarian, level 97: bare-handed it swings its monster's two-handed sword (monstats2 BaseW), 16 frames.
        var barbarian = Page("MercE");
        Assert.Equal(65, barbarian.Resist("fire", 2).Value.Value);
        Assert.Equal(["0 (15)", "9", "18", "30", "48", "75", "125"],
            barbarian.AttackRow!.Points.Select((point, at) => at == 0 ? $"{point.Value} ({point.Frames})" : $"{point.Value}"));
        Assert.Equal(297, barbarian.OpenWoundsDamage.Value);

        // The Ascendant, level 97: its own Sanctuary works on itself in town. Life per kill 74 at its mastery's 32 - the
        // page read 63 just after the hire, as every one did; 74 once loaded again (the Fire Iron Wolf).
        var ascendant = Page("MercD");
        var magic = ascendant.Added("magicmindam", "magicmaxdam", "passive_mag_mastery");
        Assert.Equal((459, 474), (magic.Min, magic.Max));
        Assert.Equal(74, ascendant.LifePerKill.Value);

        // The Iron Wolf, level 98: cold pierce 30, life per kill 74, and its cold mastery 196% - at its row's level 32,
        // where hireling.txt says 34. Another, level 96 (MercTest), reads the same 196.
        var wolf = Page("MercC");
        Assert.Equal(30, wolf.Stat("passive_cold_pierce").Value);
        Assert.Equal(74, wolf.LifePerKill.Value);
        Assert.Equal(301, wolf.OpenWoundsDamage.Value);
        Assert.Equal(196, wolf.Stat("passive_cold_mastery").Value);
        var younger = Page("MercTest");
        Assert.Equal(196, younger.Stat("passive_cold_mastery").Value);
        Assert.Equal(293, younger.OpenWoundsDamage.Value);

        // The Fire Iron Wolf, level 97 (MercTestFire): its mastery 298% at 32 too, and of Cleansing only the
        // aura's curse resist on itself - "Curse Resist: 25 / 75 Length: 100%", poison "Length: 200%" in Hell. Its
        // life per kill read 63 just hired and 74 after a save and a load: "HP/MP per Kill: 74 / 0".
        var fire = Page("MercTestFire");
        Assert.Equal(298, fire.Stat("passive_fire_mastery").Value);
        Assert.Equal(20, fire.Stat("passive_fire_pierce").Value);
        Assert.Equal(25, fire.CurseResist.Value);
        Assert.Equal(100, fire.CurseLength);
        Assert.Equal(200, fire.PoisonLength(2));
        Assert.Equal(297, fire.OpenWoundsDamage.Value);
        Assert.Equal(74, fire.LifePerKill.Value);

        // The Cold Rogue, level 98 (MercTestColdRogue, read after a load): none of Meditation's own mana per
        // kill (blvl / 2, 14) - "HP/MP per Kill: 0 / 0"; Dodge's hit recovery 9, pierce 66.
        var rogue = Page("MercTestColdRogue");
        Assert.Equal(0, rogue.Stat("item_manaafterkill").Value);
        Assert.Equal(9, rogue.Stat("item_fastergethitrate").Value);
        Assert.Equal(66, rogue.ProjectilePierce.Value);
        Assert.Equal(301, rogue.OpenWoundsDamage.Value);

        // The Defensive Act 2 guard, level 98, with an ethereal superior (20%) Pride War Pike (MercTestGuard,
        // read after a load): "Base Damage: 1h: 47-54 2h: 61-1422" - 278 x 120% + 278 x 392%, each rounded down, where
        // one sum gives 1423.
        var guard = Page("MercTestGuard");
        Assert.Equal((61, 1422), (guard.Totals.Of("secondary_mindamage"), guard.Totals.Of("secondary_maxdamage")));
        Assert.Equal((9, 9), (guard.Totals.Of("normal_damage_reduction"), guard.Totals.Of("damageresist")));
    }

    [CorpusFact]
    public void A_season_14_paladins_own_auras_give_their_passives_where_a_mercenarys_do_not()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // AuraTest, a level 99 Paladin with 20 hard points in Vigor, Cleansing, Meditation and Resist Fire, nothing worn,
        // Attack in both boxes, read in town after a load (screenshot): each passive on its line -
        // "Run/Walk: 20", curse "Length: 80%", poison "Length: 180%", "HP/MP per Kill: 0 / 10", "Fire Resist: -70 / 85".
        // A mercenary's same three are on no page (ForMercenary).
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-28", "AuraTest.d2s"));
        var paladin = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false));

        Assert.Equal(20, paladin.Stat("item_fastermovevelocity").Value);
        Assert.Equal(80, paladin.CurseLength);
        Assert.Equal(180, paladin.PoisonLength(2));
        Assert.Equal(10, paladin.Stat("item_manaafterkill").Value);
        Assert.Equal((-70, 85), (paladin.Resist("fire", 2).Value.Value, paladin.Resist("fire", 2).Maximum.Value));
        Assert.Equal(306, paladin.OpenWoundsDamage.Value);
    }

    [CorpusFact]
    public void A_buckler_blocks_with_the_class_and_the_gear_though_its_own_chance_is_none()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // AuraTest again, 20 dexterity, in Purgatory (20% Increased Chance of Blocking) and an ethereal Buckler, whose
        // armor.txt block is 0 (screenshot): "Chance to Block: 1%", "Base Def: dex: 5 equip: 1489
        // total: 1494" - 0 + 30 + 20 at (20 - 15) / (2 x 99).
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-29", "AuraTest.d2s"));
        var paladin = CharacterSheet.For(save, data);

        Assert.Equal((50, 1), (paladin.Blocking!.Value, paladin.Block));
        Assert.Equal((5, 1494), (paladin.DefenseFromDexterity.Value, paladin.Stat("armorclass").Value + paladin.DefenseFromDexterity.Value));
    }

    [CorpusFact]
    public void Two_worn_items_of_one_aura_are_one_aura_at_their_levels_sum()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // Might 5 on the Crystal Sword and 7 on Purgatory: one Might of 12, said as both (the D2 wiki's Item Skill
        // Bonuses - Dream's helm and shield are one Holy Shock 30).
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-29", "AuraTest.d2s"));
        const int might = 98;
        foreach (var (name, level) in new[] { ("Crystal Sword", 5), ("Purgatory", 7) })
        {
            var item = save.PlayerItemList!.Items.Single(each => ItemPresenter.Describe(each).Name == name);
            ItemStats.Home(item).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("item_aura"), Stat = "item_aura", SkillId = might, Value = level });
        }

        var sheet = CharacterSheet.For(save, data);
        var aura = Assert.Single(sheet.Buffs, buff => buff.Kind == BuffKind.ItemAura && buff.Skill.Id == might);
        Assert.Equal((12, true), (aura.Level, aura.Counted));
        Assert.Equal(["Crystal Sword", "Purgatory"], aura.From.Split(" + ").Order());
    }

    [CorpusFact]
    public void Two_worn_auras_of_one_skill_add_up_and_give_their_wearer_the_owners_lines()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // AuraStack, a level 30 Amazon, a Short Sword (2-7) and a Quilted Armor each with Fanaticism 5 (
        // screenshots): "Attack Damage 6-22", "Attack Rating 175" - one Fanaticism of 10: 2-7 x (100 + 15 + 18
        // + 185) / 100, the owner's ln56 alone; 95 x (100 + 85) / 100. With the sword alone: 4-17 and 152, at 5.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-29", "AuraStack.d2s"));
        var sheet = CharacterSheet.For(save, data);

        var aura = Assert.Single(sheet.Buffs, buff => buff.Kind == BuffKind.ItemAura);
        Assert.Equal(("Fanaticism", 10, true), (aura.Name, aura.Level, aura.Counted));
        Assert.Equal((185, 85, 30), (sheet.Totals.Of("damagepercent"), sheet.Totals.Of("item_tohit_percent"), sheet.Totals.Of("attackrate")));
    }

    [CorpusFact]
    public void A_spirit_summoned_off_charges_puts_out_its_aura_and_one_spirit_is_up_at_a_time()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // A level 90 Amazon with Wisp Projector's charges, Spirit of Barbs 2 summoned, no Battle Command
        // (screenshot): "Attacker Takes Damage: 538", "Deadly Strike: 54" - 23 and 7 + 2 the
        // spirit's; "Hit Recovery: 86", "Projectile Pierce: 99" - Dodge 17 and Pierce 13, no cry raising them.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "amazon-90-1--sheet-ingame-2026-10-30.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 12 };

        // Charges begin off, a spirit's too.
        var first = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true));
        Assert.Equal([false, false], first.Buffs.Where(buff => buff.Kind == BuffKind.Charges).Select(buff => buff.On));

        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true,
            Pressed: new Dictionary<string, bool> { ["Charges:296"] = true, ["Cast:155"] = false }));
        int Of(string stat) => sheet.Totals.Of(stat);
        Assert.Equal((538, 54, 86, 99), (Of("item_attackertakesdamage"), Of("item_deadlystrike"), Of("item_fastergethitrate"), sheet.ProjectilePierce.Value));

        // Both pressed: one spirit is up - two only with Spiritkeeper's extra.
        var both = new Dictionary<string, bool> { ["Charges:296"] = true, ["Charges:297"] = true };
        Assert.Single(CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true, Pressed: both)).Buffs, buff => buff.Kind == BuffKind.Charges && buff.On);

        var helm = save.PlayerItemList!.Items.Single(each => ItemPresenter.Describe(each).Name == "Giant Skull");
        ItemStats.Home(helm).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("extra_spirits"), Stat = "extra_spirits", Value = 1 });
        Assert.Equal(2, CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true, Pressed: both)).Buffs.Count(buff => buff.Kind == BuffKind.Charges && buff.On));
    }

    [CorpusFact]
    public void Jab_with_a_one_handed_thrust_reads_as_the_games_page()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // A level 90 Amazon on a Gargoyle's Bite at attack rate 110, IAS 90 (screenshot): "IAS (Frames):
        // 0 / 2 / 6 / 11 / 18 / 24 / 37 / 50 / 70 (12) / 99 / 147 / 250", "Attacker Takes Damage: 1506", no spirit up.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "amazon-90-2--sheet-ingame-2026-10-30.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = 10 };

        var sheet = CharacterSheet.For(save, data);
        Assert.Equal("Jab", sheet.RightSkill?.Name);
        Assert.Equal([0, 2, 6, 11, 18, 24, 37, 50, 70, 99, 147, 250], sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal(12, sheet.AttackRow.Points[8].Frames);
        Assert.Equal(1506, sheet.Totals.Of("item_attackertakesdamage"));
    }

    [CorpusTheory]
    // SeqBarb, a level 99 Barbarian with one point in each, read in town (screenshots).
    [InlineData("Berserk", "ssd", null, 100, 2, 7, 200, new[] { 0, 3, 6, 9, 14, 19, 24, 32, 40, 54, 70, 92, 125, 180, 292, 638 }, 24)]
    [InlineData("Berserk", "2hs", null, 100, 2, 9, 200, new[] { 0, 3, 6, 9, 14, 19, 24, 32, 40, 54, 70, 92, 125, 180, 292, 638 }, 24)]
    [InlineData("Frenzy", "ssd", null, 100, 2, 7, 200, new[] { 0, 3, 6, 9, 14, 19, 24, 32, 40, 54, 70, 92, 125, 180, 292, 638 }, 24)]
    [InlineData("Double Throw", "tax", "tax", 90, 4, 7, 200, new[] { 0, 2, 5, 8, 13, 18, 26, 34, 46, 60, 86, 125, 194, 360, 1680 }, 20)]
    [InlineData("Double Throw", "tax", "tkf", 100, 2, 3, 215, new[] { 0, 2, 6, 13, 19, 29, 40, 60, 89, 138, 240, 680 }, 17)]
    [InlineData("Leap", "ssd", null, 100, 2, 7, 200, new[] { 0, 3, 6, 11, 16, 23, 30, 40, 56, 75, 102, 152, 240, 507 }, 21)]
    [InlineData("Leap Attack", "ssd", null, 100, 2, 7, 200, new[] { 0, 3, 6, 11, 16, 23, 30, 40, 56, 75, 102, 152, 240, 507 }, 21)]
    [InlineData("Whirlwind", "ssd", null, 100, 2, 7, 200, new[] { 0 }, 6)]
    [InlineData("Whirlwind", "ssd", "ssd", 100, 2, 7, 200, new[] { 0 }, 7)]
    public void A_barbarians_sequences_read_as_the_games_page(string skill, string right, string? left, int attackRate, int least, int most, int critical, int[] row, int first)
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "SeqBarb.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = (uint)SkillRows.Named(skill)!.Id };

        var items = save.PlayerItemList!.Items;
        var weapons = items.Where(item => ItemBases.Of(item.Code)?.WeaponClass is { Length: > 0 }).ToList();
        foreach (var weapon in weapons.Where(each => each.Mode == ItemMode.Equipped))
            ItemMover.Place(weapon, InventoryLayout.ExpansionStashPage, 0, 0);
        var first1 = weapons.First(each => each.Code.Trim() == right);
        Equipping.Place(first1, ItemLocation.RightHand);
        if (left is not null)
            Equipping.Place(weapons.First(each => each != first1 && each.Code.Trim() == left), ItemLocation.LeftHand);

        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false, NothingCast: true));
        Assert.Equal(attackRate, sheet.AttackRate.Value);
        Assert.Equal((least, most), (sheet.Totals.Of("mindamage"), sheet.Totals.Of("maxdamage")));
        Assert.Equal(critical, sheet.CriticalMultiplier.Value);
        Assert.Equal(row, sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal(first, sheet.AttackRow.Points[0].Frames);
    }

    [CorpusTheory]
    // SeqAssn, a level 99 Assassin with one point in each, read in town (screenshots).
    [InlineData(260, "ktr", null, 110, new[] { 0, 2, 7, 15, 24, 40, 63, 99, 168, 395 }, 15)]
    [InlineData(260, "ktr", "ktr", 110, new[] { 0, 2, 6, 11, 18, 24, 37, 50, 70, 99, 147, 250 }, 20)]
    [InlineData(266, null, null, 100, null, 0)]
    [InlineData(251, null, null, 100, new[] { 0, 9, 18, 30, 48, 78, 125 }, 15)]
    [InlineData(276, null, null, 100, new[] { 0, 9, 18, 30, 48, 78, 125 }, 15)]
    public void An_assassins_skills_read_as_the_games_page(int skill, string? right, string? left, int attackRate, int[]? row, int first)
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "SeqAssn.d2s"));
        save.RightSkill = new D2SLib.Model.Save.Skill { Id = (uint)skill };

        var weapons = save.PlayerItemList!.Items.Where(item => ItemBases.Of(item.Code)?.WeaponClass is { Length: > 0 }).ToList();
        foreach (var weapon in weapons.Where(each => each.Mode == ItemMode.Equipped))
            ItemMover.Place(weapon, InventoryLayout.ExpansionStashPage, 0, 0);
        if (right is not null)
        {
            var inRight = weapons.First(each => each.Code.Trim() == right);
            Equipping.Place(inRight, ItemLocation.RightHand);
            if (left is not null)
                Equipping.Place(weapons.First(each => each != inRight && each.Code.Trim() == left), ItemLocation.LeftHand);
        }

        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false, NothingCast: true));
        Assert.Equal(attackRate, sheet.AttackRate.Value);
        if (row is null)
        {
            Assert.Null(sheet.AttackRow);
            return;
        }

        Assert.Equal(row, sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal(first, sheet.AttackRow.Points[0].Frames);
    }

    [CorpusTheory]
    // SeqPala on Smite with a Buckler and nothing else, SeqSorc on Inferno, SeqDruid on Arctic Blast, all bare-handed and
    // read in town (screenshots). The Druid's own hit recovery row is the knowing difference,
    // so it is not compared.
    [InlineData("SeqPala", new[] { 0, 11, 26, 48, 86, 180 }, 11, 6)]
    [InlineData("SeqSorc", null, 0, 0)]
    [InlineData("SeqDruid", new[] { 0, 5, 13, 24, 40, 72, 138, 377 }, 11, 0)]
    public void The_other_classes_sequences_read_as_the_games_page(string name, int[]? row, int first, int crushingBlow)
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", name + ".d2s"));

        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false, NothingCast: true));
        Assert.Equal(100, sheet.AttackRate.Value);
        Assert.Equal(crushingBlow, sheet.Totals.Of("item_crushingblow"));
        if (row is null)
        {
            Assert.Null(sheet.AttackRow);
            return;
        }

        Assert.Equal(row, sheet.AttackRow!.Points.Select(point => point.Value));
        Assert.Equal(first, sheet.AttackRow.Points[0].Frames);
    }

    [CorpusFact]
    public void A_corruptions_numbers_are_in_no_bracket()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // SeqBarb's four (screenshots): a Stone of Jordan's "+40 to Life*" bare beside "+40 to Mana
        // [20 - 40]"; a Stormshield's "24% Better Chance of Getting Magic Items*" bare beside "+30 to Strength [20 - 30]";
        // another's "Cold Resist +87%* [40 - 50]", its own range; a rare's "+30 to Life* [1 - 40]", what its base could roll.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "SeqBarbCorrupted.d2s"));
        var items = save.PlayerItemList!.Items;

        var ring = AffixRanges.DisplayedFor(items.Single(item => item.Code.Trim() == "rin" && item.Quality == ItemQuality.Unique));
        Assert.False(ring.ContainsKey("maxhp"));
        Assert.Equal(new RollRange(20, 40), ring["maxmana"]);

        var shield = AffixRanges.DisplayedFor(items.Single(item => item.Code.Trim() == "uit" && Corruptions.IdOn(item) == 7));
        Assert.False(shield.ContainsKey("item_magicbonus"));
        Assert.Equal(new RollRange(20, 30), shield["strength"]);

        var merged = AffixRanges.DisplayedFor(items.Single(item => item.Code.Trim() == "uit" && Corruptions.IdOn(item) == 37));
        Assert.Equal(new RollRange(40, 50), merged["coldresist"]);

        var rare = AffixRanges.DisplayedFor(items.Single(item => item.Quality == ItemQuality.Rare));
        Assert.Equal(new RollRange(1, 40), rare["maxhp"]);
        Assert.Equal(new RollRange(10, 120), rare["tohit"]);
        Assert.Equal(new RollRange(3, 30), rare["coldresist"]);
        Assert.Equal(new RollRange(1, 14), rare["maxdamage"]);
    }

    [CorpusFact]
    public void Of_two_of_one_unique_charm_the_first_in_the_save_counts_whichever_is_the_stronger()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // SeqPala's two Annihilus, the +30 Vitality first (screenshot): "Vitality 55". With the +60
        // first she read 85.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "SeqPalaCharms.d2s"));
        Assert.Equal(55, CharacterSheet.For(save, data).Vitality.Value);

        var charms = save.PlayerItemList!.Items;
        int first = charms.FindIndex(item => item.Code.Trim() == "cm1"), second = charms.FindLastIndex(item => item.Code.Trim() == "cm1");
        (charms[first], charms[second]) = (charms[second], charms[first]);
        Assert.Equal(85, CharacterSheet.For(save, data).Vitality.Value);
    }

    [CorpusFact]
    public void A_mercenary_under_its_rows_level_counts_its_columns_down()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // SeqBarb's Hell guard, row level 75, set to 40 (screenshots): "Strength 112", "Dexterity 87",
        // "Base Damage: 1h: 12-19", "Base Def: dex: 21 equip: 47", "Base AR: dex: 420", "Damage Reduction: 9/9%", resists 7.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "SeqBarbMercLow.d2s"));
        var mercenary = CharacterSheet.ForMercenary(save, data, new CharacterSheet.Options(OutOfTown: false, NothingCast: true))!;

        Assert.Equal((112, 87), (mercenary.Strength.Value, mercenary.Dexterity.Value));
        Assert.Equal((12, 19), (mercenary.Totals.Of("mindamage"), mercenary.Totals.Of("maxdamage")));
        Assert.Equal((9, 9), (mercenary.Totals.Of("normal_damage_reduction"), mercenary.Totals.Of("damageresist")));
    }

    [CorpusFact]
    public void The_lengths_say_what_they_are_made_of()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // A level 90 Amazon on a Gargoyle's Bite: "Cold Resist ... Length: 50%" from her Half Freeze Duration, poison "Length: 200%" in Hell, less nothing.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "amazon-90-2--sheet-ingame-2026-10-30.d2s"));
        var sheet = CharacterSheet.For(save, data);

        Assert.Equal(50, sheet.ColdLengthSources.Value);
        Assert.Equal(50, sheet.ColdLengthSources.Parts.Sum(part => part.Value));
        Assert.Contains(sheet.ColdLengthSources.Parts, part => part.Value == -50 && part.Source.Length > 0);

        Assert.Equal(new[] { 100, 140, 200 }, new[] { 0, 1, 2 }.Select(difficulty => sheet.PoisonLengthSources(difficulty).Value));
        Assert.Equal(200, sheet.PoisonLengthSources(2).Parts.Sum(part => part.Value));
        Assert.Equal(sheet.CurseLength, sheet.CurseLengthSources.Value);
    }

    [CorpusFact]
    public void The_guards_defiance_under_battle_command_reaches_the_characters_own_damage_reduction()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // SeqBarb, Battle Command 10 cast, a Defiance 18 guard beside him, out of town (screenshot):
        // "Damage Reduction: 10/10%" - Defiance 20, two levels from the cry. The guard's own page at one level of the cry
        // read 9/9%, as lvl/2 gives at 18 and at 19.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-31", "SeqBarbGuard.d2s"));

        var cast = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true));
        Assert.Contains(cast.Buffs, buff => buff is { Kind: BuffKind.MercenaryAura, Name: "Defiance", Level: 20, BaseLevel: 18 });
        Assert.Equal((10, 10), (cast.Totals.Of("normal_damage_reduction"), cast.Totals.Of("damageresist")));

        var bare = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true, NothingCast: true));
        Assert.Equal((9, 9), (bare.Totals.Of("normal_damage_reduction"), bare.Totals.Of("damageresist")));
    }

    [CorpusFact]
    public void An_aura_the_mercenary_wears_gives_it_the_owners_lines_in_town_and_out()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // MercAura's Act 5 Barbarian in a Quilted Armor with Holy Fire 10, read after a load (screenshot,
        // in game): "Added Damage ... fire 20-35" - Holy Fire's owner line, edmn-edmx at 10; the party's lines are its
        // enemies'. The rest of its page as it was: resists 53, "Base Damage: 1h: 52-56".
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-29", "MercAura.d2s"));

        foreach (bool outside in new[] { true, false })
        {
            var mercenary = CharacterSheet.ForMercenary(save, data, new CharacterSheet.Options(OutOfTown: outside))!;
            Assert.Equal((20, 35), (mercenary.Totals.Of("firemindam"), mercenary.Totals.Of("firemaxdam")));
            Assert.Equal((52, 56), (mercenary.Totals.Of("mindamage"), mercenary.Totals.Of("maxdamage")));
        }
    }

    [CorpusFact]
    public void An_aura_the_mercenary_wears_puts_out_its_own_of_the_skill_whatever_the_levels()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // The same Barbarian at 90, Might 18 of his own, in Templar's Might (Might 8) and nothing in hand (
        // screenshots): "Damage 251-270" = 55-59 x (100 + 268 + 90) / 100 - the 8's half, where his 18 would
        // read 306-329. With Last Wish (20) beside it 563-953, the two worn ones one of 28.
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-29", "MercAuraTemplar.d2s"));
        var sheet = CharacterSheet.For(save, data);

        var might = sheet.Buffs.Where(buff => buff.Name == "Might").ToList();
        Assert.Contains(might, buff => buff is { Kind: BuffKind.ItemAura, OnMercenary: true, Level: 8, Counted: true });
        Assert.Contains(might, buff => buff is { Kind: BuffKind.MercenaryAura, Level: 18, Counted: false });
        Assert.Equal((55, 59), (CharacterSheet.ForMercenary(save, data)!.Totals.Of("mindamage"), CharacterSheet.ForMercenary(save, data)!.Totals.Of("maxdamage")));
    }

    [CorpusFact]
    public void A_season_14_rogues_vigor_gives_its_page_no_run_walk_of_its_own()
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        // Multishot's Fire Rogue, level 93, read in town and out the same, nothing cast (screenshots,
        // in game): "Hit Recovery: 29 Run/Walk: 40", "Projectile Pierce: 96".
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-28", "Multishot.d2s"));
        var nothingCast = CharacterSheet.For(save, data).Buffs.Where(buff => buff.Kind == BuffKind.Cast).ToDictionary(buff => buff.Key, _ => false);
        var rogue = CharacterSheet.ForMercenary(save, data, new CharacterSheet.Options(Pressed: nothingCast))!;

        Assert.Equal(40, rogue.Stat("item_fastermovevelocity").Value);
        Assert.Equal(29, rogue.Stat("item_fastergethitrate").Value);
        Assert.Equal(96, rogue.ProjectilePierce.Value);
    }

    /// <summary>
    /// An aura the mercenary wears and the same from the party count once, the higher level:
    /// both had their lines added. Not read in game; the rule is the sheet's own.
    /// </summary>
    [CorpusTheory]
    [InlineData(20, true, true)]
    [InlineData(3, true, false)]
    [InlineData(20, false, true)]
    [InlineData(3, false, false)]
    public void An_aura_the_mercenary_wears_and_the_party_gives_counts_once(int partyLevel, bool outOfTown, bool partyWins)
    {
        if (!game.Available || D2SLib.Model.TXT.StatEncoding.PreviousSeason != "S13")
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "sheet", "ingame-2026-10-29", "MercAuraTemplar.d2s"));
        var options = new CharacterSheet.Options(OutOfTown: outOfTown);

        // Might on a cap the character wears, as the Templar's Might the mercenary wears gives its 8.
        save.PlayerItemList.Items.RemoveAll(item => item.Mode == ItemMode.Equipped && item.Location == ItemLocation.Head);
        var cap = ItemForge.PlainBase("cap", 0, Game.Stamp).Item;
        (cap.Mode, cap.Location, cap.Page) = (ItemMode.Equipped, ItemLocation.Head, (byte)0);
        ItemStats.Home(cap).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("item_aura"), Stat = "item_aura", Param = SkillRows.Named("Might")!.Id, Value = partyLevel });
        save.PlayerItemList.Items.Add(cap);

        var mercenary = CharacterSheet.ForMercenary(save, data, options)!;
        var part = Assert.Single(mercenary.Totals.Sum("damagepercent").Parts, part => part.Source.Contains("Might", StringComparison.Ordinal));

        Assert.True(part.Source.Contains("(mercenary)", StringComparison.Ordinal) != partyWins, part.Source);
    }

    /// <summary>What the hands cannot cast takes no place in its lot: Holy Sword up high beside a shield leaves Holy Shield up.</summary>
    [CorpusFact]
    public void A_buff_the_hands_cannot_cast_leaves_its_lots_place_to_the_one_they_can()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "aliases", "paladin-97-1--sheet-ingame-2026-10-13.d2s"));
        save.PlayerItemList.Items.Single(item => item.Mode == ItemMode.Equipped && item.Location == ItemLocation.LeftHand)
            .StatLists[0].Stats.Add(new ItemStat { Id = 107, Stat = "item_singleskill", Param = 116, Value = 99 });

        var sheet = CharacterSheet.For(save, data, new CharacterSheet.Options(OtherWeaponSet: false));

        Assert.True(Named(sheet, "Holy Sword") is { On: false, Needs: "Two-Handed Sword" });
        Assert.True(Named(sheet, "Holy Shield") is { On: true, Counted: true, Needs: null });

        // Pressed off, it stays off: a press is the player's.
        var pressed = new Dictionary<string, bool> { [Named(sheet, "Holy Shield").Key] = false };
        Assert.True(Named(CharacterSheet.For(save, data, new CharacterSheet.Options(OtherWeaponSet: false, Pressed: pressed)), "Holy Shield") is { On: false });
    }

    /// <summary>The Iron Golem's item counts on both sheets, in neither list: its change or its going is a change of what they are worked out from.</summary>
    [CorpusFact]
    public void The_sheets_inputs_hold_the_Iron_Golem_and_its_item()
    {
        if (!game.Available)
            return;

        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        int none = CharacterSheet.InputsOf(save);

        save.Golem.Exists = true;
        save.Golem.Item = ItemForge.PlainBase("7s7", 0, Game.Stamp).Item;
        int standing = CharacterSheet.InputsOf(save);
        Assert.NotEqual(none, standing);

        ItemStats.Home(save.Golem.Item).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("item_aura"), Stat = "item_aura", Param = SkillRows.Named("Might")!.Id, Value = 5 });
        Assert.NotEqual(standing, CharacterSheet.InputsOf(save));

        save.Golem.Exists = false;
        save.Golem.Item = null!;
        Assert.Equal(none, CharacterSheet.InputsOf(save));
    }
}
