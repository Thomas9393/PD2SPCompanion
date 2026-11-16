using D2SLib.Model.Save;

using PD2SPCompanion.Shell.Tests.Edit;

namespace PD2SPCompanion.Shell.Tests.Forge;

/// <summary>
/// A Glyph Scratch made on the Forge - a
/// Matriarchal Spear, crafted, Blood, "of Amplify Damage" among its affixes changed a few times - came out with five
/// "12% Chance to cast level 1 (none) on striking", and none of the Blood craft's lines. The affix's hit-skill is skill
/// 442 at level 27, one line however often it is picked; the craft's lines stay through the affixes changing.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class ForgeHitSkillTests(Program program)
{
    /// <summary>The affix die rolled again and again on the Blood-crafted spear: after each roll, every hit-skill line its skill's, as many as the affixes grant, the craft's lines kept.</summary>
    [CorpusFact]
    public void The_affix_die_rolled_again_and_again_keeps_each_hit_skill_whole_and_the_craft()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("matriarchal spear", "Matriarchal Spear");
            bench.Rarity("Crafted");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));
        program.On(() =>
        {
            var craft = bench.Panel.Crafting;
            craft.OpenPickerCommand.Execute(null);
            craft.PickCommand.Execute(craft.Choices.First(choice => choice.Family == "Blood"));
            craft.ClosePickerCommand.Execute(null);
        });
        program.Settled(bench.Panel);

        static IEnumerable<ItemStat> Stats(Item item) => item.StatLists.SelectMany(list => list.Stats);
        string Said(Item item) => string.Join("; ", Stats(item).Select(stat => $"{stat.Stat} {stat.Value} skill {stat.SkillId} level {stat.SkillLevel}"));

        int withHit = 0;
        for (int roll = 1; roll <= 40; roll++)
        {
            program.On(() => bench.Panel.Affixes.RerollAllAffixesNowCommand.Execute(null));
            program.Settled(bench.Panel);
            program.On(() =>
            {
                var item = bench.Held;
                int granted = PD2SPCompanion.Core.Items.MagicAffixes.PairsOn(item).SelectMany(pair => new[] { pair.Prefix, pair.Suffix })
                    .Where(option => option is not null).Sum(option => option!.Grants.Count(grant => grant.Code == "hit-skill"));
                var hits = Stats(item).Where(stat => stat.Stat == "item_skillonhit").ToList();
                withHit += granted > 0 ? 1 : 0;
                Assert.True(hits.Count <= granted && hits.All(stat => stat.SkillId is > 0 && stat.SkillLevel is > 0),
                            $"roll {roll}, {granted} granted: {Said(item)}");
                Assert.True(Stats(item).Any(stat => stat.Stat == "lifedrainmindam") && Stats(item).Any(stat => stat.Stat == "maxhp"),
                            $"roll {roll}, the craft gone: {Said(item)}");
            });
        }
    }

    /// <summary>
    /// The cause: a change worked out while an earlier one is still staged - a picker open, nothing
    /// kept - read the staged chance to cast as its number alone (VP_ViewModel_Inv_R.PendingAnew), skill 0 at level 1,
    /// and staged it again as a line of its own at every change. In one open picker: of Amplify Damage, then two more
    /// affixes, each worked out from the item with the first still staged.
    /// </summary>
    [CorpusFact]
    public void Affixes_picked_after_a_staged_hit_skill_keep_it_one_line_with_its_skill_and_level()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        static IEnumerable<ItemStat> Stats(Item item) => item.StatLists.SelectMany(list => list.Stats);
        string Said(Item item) => string.Join("; ", Stats(item).Select(stat => $"{stat.Stat} {stat.Value}{(stat.SkillId is { } skill ? $" skill {skill} level {stat.SkillLevel}" : "")}"));

        program.On(() =>
        {
            bench.Find("matriarchal spear", "Matriarchal Spear");
            bench.Rarity("Crafted");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));
        program.On(() =>
        {
            var affixes = bench.Panel.Affixes;
            affixes.OpenAffixPickerCommand.Execute(null);
            void Pick(string where, string search, string name)
            {
                affixes.AimAffixCommand.Execute(where);
                affixes.AffixSearch = search;
                var row = affixes.AffixChoices.First(row => row.Name == name);
                Assert.True(row.CanPick, $"{name} at {where}: {row.LevelNote} - {Said(bench.Held)}");
                affixes.ChooseAffixCommand.Execute(row);
            }

            // Every slot emptied first: a roll holding an affix of a pick's group in another slot greys the pick "in
            // another slot", and it was refused - Amplify Damage's by a suffix, Cruel's by a prefix, one Forge run in
            // four, the roll the Forge's.
            foreach (var pair in affixes.StagedPairs.ToList())
                foreach (string side in new[] { "prefix", "suffix" })
                {
                    affixes.AimAffixCommand.Execute($"{pair.Slot}:{side}");
                    affixes.ClearAffixCommand.Execute(null);
                }

            Pick("1:suffix", "Amplify Damage", "of Amplify Damage");
            Pick("0:prefix", "Cruel", "Cruel");
            Pick("0:suffix", "Quickness", "of Quickness");
            affixes.AffixSearch = "";
            affixes.CloseAffixPickerCommand.Execute(null);
        });
        program.Settled(bench.Panel);
        program.On(() =>
        {
            var hits = Stats(bench.Held).Where(stat => stat.Stat == "item_skillonhit").ToList();
            Assert.True(hits.Count == 1 && hits[0].SkillId is > 0 && hits[0].SkillLevel is > 1, Said(bench.Held));
        });
    }

    /// <summary>
    /// Blood crafted three or four times over: the Crafting picker stays open, as the corruption's
    /// does, and Blood clicked in it again and again, each pick staged on the last and kept only when it closes.
    /// </summary>
    [CorpusFact]
    public void Blood_picked_again_and_again_in_the_open_crafting_picker_keeps_the_hit_skill_whole_and_one_craft()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        static IEnumerable<ItemStat> Stats(Item item) => item.StatLists.SelectMany(list => list.Stats);
        string Said(Item item) => string.Join("; ", Stats(item).Select(stat => $"{stat.Stat} {stat.Value}{(stat.SkillId is { } skill ? $" skill {skill} level {stat.SkillLevel}" : "")}"));

        for (int roll = 0; roll < 6; roll++)
        {
            program.On(() =>
            {
                bench.Find("matriarchal spear", "Matriarchal Spear");
                bench.Rarity("Crafted");
            });
            Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));
            program.On(() =>
            {
                var affixes = bench.Panel.Affixes;
                affixes.OpenAffixPickerCommand.Execute(null);
                affixes.AimAffixCommand.Execute("1:suffix");
                affixes.AffixSearch = "Amplify Damage";
                if (affixes.AffixChoices.FirstOrDefault(row => row.Name == "of Amplify Damage") is { } amplify)
                    affixes.ChooseAffixCommand.Execute(amplify);
                affixes.AffixSearch = "";
                affixes.CloseAffixPickerCommand.Execute(null);
            });
            program.Settled(bench.Panel);

            string before = program.On(() => Said(bench.Held));
            program.On(() =>
            {
                var craft = bench.Panel.Crafting;
                craft.OpenPickerCommand.Execute(null);
                for (int n = 0; n < 4; n++)
                    craft.PickCommand.Execute(craft.Choices.First(choice => choice.Family == "Blood"));
                craft.ClosePickerCommand.Execute(null);
            });
            program.Settled(bench.Panel);
            program.On(() =>
            {
                var item = bench.Held;
                string said = $"roll {roll}:{Environment.NewLine}before: {before}{Environment.NewLine}after:  {Said(item)}";
                Assert.True(Stats(item).Where(stat => stat.Stat == "item_skillonhit").All(stat => stat.SkillId is > 0 && stat.SkillLevel is > 0), said);
                Assert.True(Stats(item).Count(stat => stat.Stat == "item_skillonhit") <= 1, said);
                Assert.True(Stats(item).Count(stat => stat.Stat == "maxhp") == 1 && Stats(item).Count(stat => stat.Stat == "lifedrainmindam") == 1, said);
                Assert.Equal("Blood", PD2SPCompanion.Core.Items.AffixRanges.CraftFamilyOf(item));
            });
        }
    }

    /// <summary>
    /// The defect: "pick of Amplify Damage" then "craft Caster", and the hit-skill line lost its
    /// skill and level - "12% Chance to cast level 1 (none)". On many Forge rolls, each family in turn after
    /// the pick: the line whole after every one.
    /// </summary>
    [CorpusFact]
    public void A_craft_changed_after_a_hit_skill_affix_keeps_the_hit_skills_skill_and_level()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        static IEnumerable<ItemStat> Stats(Item item) => item.StatLists.SelectMany(list => list.Stats);
        string Said(Item item) => string.Join("; ", Stats(item).Select(stat => $"{stat.Stat} {stat.Value}{(stat.SkillId is { } skill ? $" skill {skill} level {stat.SkillLevel}" : "")}"));

        for (int roll = 0; roll < 12; roll++)
        {
            program.On(() =>
            {
                bench.Find("matriarchal spear", "Matriarchal Spear");
                bench.Rarity("Crafted");
            });
            Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));
            string rolled = program.On(() => $"{Said(bench.Held)} [{PD2SPCompanion.Core.Items.AffixRanges.CraftFamilyOf(bench.Held)}; " +
                $"{string.Join(", ", PD2SPCompanion.Core.Items.MagicAffixes.PairsOn(bench.Held).SelectMany(pair => new[] { pair.Prefix, pair.Suffix }).Where(option => option is not null).Select(option => $"{option!.Name}({option.Id})"))}]");
            string? chosen = null;
            program.On(() =>
            {
                var affixes = bench.Panel.Affixes;
                affixes.OpenAffixPickerCommand.Execute(null);
                affixes.AimAffixCommand.Execute("1:suffix");
                affixes.AffixSearch = "Amplify Damage";
                if (affixes.AffixChoices.FirstOrDefault(row => row.Name == "of Amplify Damage") is { } amplify)
                {
                    chosen = $"{amplify.Name} ({string.Join(", ", affixes.AffixChoices.Where(row => row.Name == "of Amplify Damage").Select(row => row.ToString()))})";
                    affixes.ChooseAffixCommand.Execute(amplify);
                }
                affixes.AffixSearch = "";
                affixes.CloseAffixPickerCommand.Execute(null);
            });
            program.Settled(bench.Panel);
            string picked = program.On(() => Said(bench.Held));
            program.On(() => Assert.True(
                Stats(bench.Held).Where(stat => stat.Stat == "item_skillonhit").All(stat => stat.SkillId is > 0 && stat.SkillLevel is > 0),
                $"roll {roll}, the pick of {chosen}:{Environment.NewLine}rolled: {rolled}{Environment.NewLine}picked: {picked}"));

            foreach (string family in new[] { "Caster", "Blood", "Brilliant", "Hitpower", "Safety" })
            {
                string before = program.On(() => Said(bench.Held));
                string? told = program.On(() => PD2SPCompanion.Core.Items.AffixRanges.CraftFamilyOf(bench.Held));
                program.On(() =>
                {
                    var craft = bench.Panel.Crafting;
                    craft.OpenPickerCommand.Execute(null);
                    if (craft.Choices.FirstOrDefault(choice => choice.Family == family) is { } chosen)
                        craft.PickCommand.Execute(chosen);
                    craft.ClosePickerCommand.Execute(null);
                });
                program.Settled(bench.Panel);
                program.On(() =>
                {
                    var item = bench.Held;
                    Assert.True(Stats(item).Where(stat => stat.Stat == "item_skillonhit").All(stat => stat.SkillId is > 0 && stat.SkillLevel is > 0),
                                $"roll {roll}, {told} -> {family}:{Environment.NewLine}before: {before}{Environment.NewLine}after:  {Said(item)}");
                });
            }
        }
    }

    /// <summary>
    /// The kinds of change in any order, many times (a sweep: the order that broke one item is not known) - the affix
    /// die, a slot's die, a pick into a slot, a slot emptied, the craft's family, the automagic: after each, every hit-skill
    /// line its skill's and no more of them than the affixes grant.
    /// </summary>
    [CorpusFact]
    [Trait("Speed", "Slow")]
    public void Any_order_of_affix_craft_and_automagic_changes_keeps_each_hit_skill_whole()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("matriarchal spear", "Matriarchal Spear");
            bench.Rarity("Crafted");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

        static IEnumerable<ItemStat> Stats(Item item) => item.StatLists.SelectMany(list => list.Stats);
        string Said(Item item) => string.Join("; ", Stats(item).Select(stat => $"{stat.Stat} {stat.Value} skill {stat.SkillId} level {stat.SkillLevel}"));

        foreach (int seed in Enumerable.Range(1, 40))
        {
        // Each from a fresh roll of the Forge's, which is not seeded: the starts vary.
        program.On(() =>
        {
            bench.Find("matriarchal spear", "Matriarchal Spear");
            bench.Rarity("Crafted");
        });
        program.Settled(bench.Panel);
        var random = new Random(seed);
        var done = new List<string>();
        for (int step = 1; step <= 120; step++)
        {
            string before = program.On(() => Said(bench.Held));
            program.On(() =>
            {
                var panel = bench.Panel;
                switch (random.Next(6))
                {
                    case 0:
                        done.Add("die");
                        panel.Affixes.RerollAllAffixesNowCommand.Execute(null);
                        break;
                    case 1:
                    case 2:
                    {
                        var affixes = panel.Affixes;
                        affixes.OpenAffixPickerCommand.Execute(null);
                        var slots = affixes.StagedPairs.ToList();
                        string where = $"{random.Next(Math.Max(1, slots.Count))}:{(random.Next(2) == 0 ? "prefix" : "suffix")}";
                        affixes.AimAffixCommand.Execute(where);
                        affixes.AffixSearch = random.Next(2) == 0 ? "Amplify Damage" : "";
                        var choices = affixes.AffixChoices;
                        if (choices.Count > 0)
                        {
                            var choice = choices[random.Next(choices.Count)];
                            done.Add($"pick {choice.Name} at {where}");
                            affixes.ChooseAffixCommand.Execute(choice);
                        }
                        else if (random.Next(2) == 0)
                        {
                            done.Add($"clear {where}");
                            affixes.ClearAffixCommand.Execute(null);
                        }
                        affixes.AffixSearch = "";
                        affixes.CloseAffixPickerCommand.Execute(null);
                        break;
                    }
                    case 3:
                    {
                        var craft = panel.Crafting;
                        craft.OpenPickerCommand.Execute(null);
                        var family = craft.Choices[random.Next(craft.Choices.Count)];
                        done.Add($"craft {family.Family}");
                        craft.PickCommand.Execute(family);
                        craft.ClosePickerCommand.Execute(null);
                        break;
                    }
                    case 4:
                    {
                        var automagic = panel.Automagic;
                        automagic.OpenPickerCommand.Execute(null);
                        if (automagic.Choices.Count > 0)
                        {
                            var choice = automagic.Choices[random.Next(automagic.Choices.Count)];
                            done.Add($"automagic {string.Join(",", choice.Lines)}");
                            automagic.PickCommand.Execute(choice);
                        }
                        automagic.ClosePickerCommand.Execute(null);
                        break;
                    }
                    default:
                        done.Add("slot die");
                        panel.Affixes.RollAffixCommand.Execute(null);
                        break;
                }
            });
            program.Settled(bench.Panel);
            program.On(() =>
            {
                var item = bench.Held;
                int granted = PD2SPCompanion.Core.Items.MagicAffixes.PairsOn(item).SelectMany(pair => new[] { pair.Prefix, pair.Suffix })
                    .Where(option => option is not null).Sum(option => option!.Grants.Count(grant => grant.Code == "hit-skill"));
                var hits = Stats(item).Where(stat => stat.Stat == "item_skillonhit").ToList();
                Assert.True(hits.Count <= granted && hits.All(stat => stat.SkillId is > 0 && stat.SkillLevel is > 0),
                            $"seed {seed} step {step}, {granted} granted: {Said(item)}{Environment.NewLine}before: {before}{Environment.NewLine}after: {string.Join(" > ", done.TakeLast(8))}");
            });
        }
        }
    }

    [CorpusFact]
    public void A_hit_skill_affix_picked_again_and_again_is_one_line_with_its_skill_and_level_and_the_craft_stays()
    {
        if (!program.Ready(changes: true))
            return;

        using var bench = new ForgeBench(program);
        program.On(() =>
        {
            bench.Find("matriarchal spear", "Matriarchal Spear");
            bench.Rarity("Crafted");
        });
        Assert.True(bench.Until(() => bench.Forge.HasDraft && bench.Panel.Summary is not null));

        // The item that broke began with a crafted spear whose recipe the editor could not tell ("cannot find the crafting
        // recipe"): two fitting, none told. The likeliest is told now (CraftRecipeTieTests) - every Forge roll has one.
        for (int again = 0; again < 20; again++)
        {
            Assert.NotNull(program.On(() => PD2SPCompanion.Core.Items.AffixRanges.CraftFamilyOf(bench.Held)));
            program.On(() =>
            {
                bench.Find("matriarchal spear", "Matriarchal Spear");
                bench.Rarity("Crafted");
            });
            program.Settled(bench.Panel);
        }

        void Craft(string family) => program.On(() =>
        {
            var craft = bench.Panel.Crafting;
            craft.OpenPickerCommand.Execute(null);
            craft.PickCommand.Execute(craft.Choices.First(choice => choice.Family == family));
            craft.ClosePickerCommand.Execute(null);
        });

        string Said(Item item) => string.Join("; ", item.StatLists.SelectMany(list => list.Stats)
            .Select(stat => $"{stat.Stat} {stat.Value} skill {stat.SkillId} level {stat.SkillLevel}"));

        program.On(() =>
        {
            var affixes = bench.Panel.Affixes;
            affixes.OpenAffixPickerCommand.Execute(null);
            foreach (var pair in affixes.StagedPairs.ToList())
            {
                affixes.AimAffixCommand.Execute($"{pair.Slot}:suffix");
                affixes.ClearAffixCommand.Execute(null);
            }

            // Picked, cleared, picked again, three times over - as the affixes were changed on the item that broke.
            for (int n = 0; n < 3; n++)
            {
                affixes.AimAffixCommand.Execute("0:suffix");
                affixes.AffixSearch = "Amplify Damage";
                affixes.ChooseAffixCommand.Execute(affixes.AffixChoices.First(row => row.Name == "of Amplify Damage"));
            }

            affixes.CloseAffixPickerCommand.Execute(null);
        });
        program.Settled(bench.Panel);

        // Then Blood, the spear's automagic to +3 Javelin and Spear Skills, and Blood again - the log's order.
        Craft("Blood");
        program.Settled(bench.Panel);
        program.On(() =>
        {
            var automagic = bench.Panel.Automagic;
            automagic.OpenPickerCommand.Execute(null);
            automagic.PickCommand.Execute(automagic.Choices.First(choice => choice.Lines.Any(line => line.StartsWith("+3 to Javelin and Spear Skills"))));
            automagic.ClosePickerCommand.Execute(null);
        });
        program.Settled(bench.Panel);
        Craft("Blood");
        program.Settled(bench.Panel);

        program.On(() =>
        {
            var item = bench.Held;
            var hits = item.StatLists.SelectMany(list => list.Stats).Where(stat => stat.Stat == "item_skillonhit").ToList();
            var hit = Assert.Single(hits);
            Assert.True(hit.SkillId == 442 && hit.SkillLevel > 1, Said(item));

            // The Blood craft's lines are still there.
            Assert.Contains(item.StatLists.SelectMany(list => list.Stats), stat => stat.Stat == "lifedrainmindam");
            Assert.Contains(item.StatLists.SelectMany(list => list.Stats), stat => stat.Stat == "maxhp");

            // And the recipe told, where Master's and Quickness make Brilliant fit too (CraftRecipeTieTests).
            Assert.Equal("Blood", PD2SPCompanion.Core.Items.AffixRanges.CraftFamilyOf(item));
        });
    }
}
