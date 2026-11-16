using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// The Iron Golem and its skill: the save keeps a standing golem and the item it was made of,
/// and the game refuses one whose skill holds no point ("bad hireables", after a respec in the editor). The
/// skill is found by the table (pettype irongolem); emptying it - by a respec, or by the last point of a
/// skill it stands on - leaves a golem to dismiss, and dismissing hands its item back.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class IronGolemTests(Game game)
{
    private const byte Necromancer = 2;

    /// <summary>A Necromancer of the corpus character's shell, a point in each golem skill, a golem made of a Balrog Spear.</summary>
    private static (D2S Save, SkillEditor Skills, Item Made) Standing(GameData data)
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        NewCharacter.Begin(save, "Golemancer", Necromancer, 60, hardcore: false, expansion: true, finished: false);

        var skills = new SkillEditor(save, SkillTree.For(data, Necromancer));
        skills.AddUnspent(30);
        int golem = skills.Tree.IronGolemSlot!.Value;
        var iron = skills.Tree.BySlot[golem];

        // Its prerequisites first, down the chain, then the skill itself: added freely, as Free mode does.
        var chain = new List<SkillEntry>();
        void Need(SkillEntry skill)
        {
            foreach (int id in skill.Requires)
                Need(skills.Tree.BySlot.Values.First(each => each.Id == id));
            if (!chain.Contains(skill))
                chain.Add(skill);
        }
        Need(iron);
        foreach (var skill in chain)
            Assert.Equal(1, skills.Add(skill, 1, 99, freely: true).Added);

        var made = ItemForge.PlainBase("7s7", 0, "103").Item;
        save.Golem.Exists = true;
        save.Golem.Item = made;
        return (save, skills, made);
    }

    [CorpusFact]
    public void The_golems_skill_is_found_in_the_table()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var tree = SkillTree.For(data, Necromancer);
        Assert.NotNull(tree.IronGolemSlot);
        Assert.Equal(90, tree.BySlot[tree.IronGolemSlot.Value].Id);
        Assert.Null(SkillTree.For(data, 0).IronGolemSlot);   // an Amazon keeps none
    }

    [CorpusFact]
    public void A_respec_leaves_a_golem_to_dismiss_and_its_item_comes_back()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, skills, made) = Standing(data);
        Assert.False(skills.GolemStandsUnskilled);
        Assert.Same(made, skills.GolemItem);

        Assert.True(skills.RespecEmptiesGolemSkill);
        skills.RespecSkills();
        Assert.True(skills.GolemStandsUnskilled);

        Assert.Same(made, skills.DismissGolem());
        Assert.False(save.Golem.Exists);
        Assert.False(skills.GolemStandsUnskilled);
        Assert.Null(skills.GolemItem);

        // And the save writes and reads back with no golem.
        var again = D2SLib.Core.ReadD2S(SaveWriter.Prepared(save));
        Assert.False(again.Golem.Exists);
    }

    [CorpusFact]
    public void The_last_point_of_a_skill_the_golem_stands_on_empties_its_skill()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var (_, skills, _) = Standing(data);
        var iron = skills.Tree.BySlot[skills.Tree.IronGolemSlot!.Value];
        var first = skills.Tree.BySlot.Values.First(each => each.Id == 75);   // Clay Golem, the chain's root
        var other = skills.Tree.BySlot.Values.First(each => !each.Requires.Any() && each.Id != 75);

        Assert.True(skills.RemovalEmptiesGolemSkill(iron, 1));
        Assert.True(skills.RemovalEmptiesGolemSkill(first, 1));
        Assert.False(skills.RemovalEmptiesGolemSkill(other, 1));

        skills.Remove(first, 1);
        Assert.True(skills.GolemStandsUnskilled);
    }

    [CorpusFact]
    public void An_item_worn_that_gives_the_skill_keeps_the_golem()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, skills, _) = Standing(data);

        // PD2's Metalgrid gives Iron Golem as an other class's skill (oskill IronGolem): the stat, on an amulet worn.
        var grid = ItemForge.PlainBase("amu", 0, "103").Item;
        grid.StatLists[0].Stats.Add(new ItemStat { Id = 97, Stat = "item_nonclassskill", Param = skills.Tree.IronGolemId, Value = 25 });
        grid.Mode = ItemMode.Equipped;
        grid.Location = ItemLocation.Neck;
        save.PlayerItemList.Items.Add(grid);
        save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;

        var iron = skills.Tree.BySlot[skills.Tree.IronGolemSlot!.Value];
        Assert.False(skills.RemovalEmptiesGolemSkill(iron, 1));
        Assert.False(skills.RespecEmptiesGolemSkill);

        skills.RespecSkills();
        Assert.True(skills.HasGolemSkill());
        Assert.False(skills.GolemStandsUnskilled);
        Assert.False(skills.HasGolemSkill(without: grid));

        // Taken off, nothing gives the skill: the golem is one to dismiss.
        save.PlayerItemList.Items.Remove(grid);
        Assert.True(skills.GolemStandsUnskilled);
    }

    [CorpusFact]
    public void The_skill_is_known_by_id_to_every_class()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        Assert.Equal(90, SkillTree.For(data, Necromancer).IronGolemId);
        Assert.Equal(90, SkillTree.For(data, 0).IronGolemId);   // an Amazon may wear what gives it
    }

    [CorpusFact]
    public void The_golems_aura_reaches_the_character_out_of_town()
    {
        if (!game.Available)
            return;

        // A golem made of an item with Meditation 12, as Insight gives: the character sheet
        // must count an Iron Golem's item (an aura, say).
        using var data = GameData.Open(Game.Root, Game.Tables);
        var (save, _, made) = Standing(data);
        const int meditation = 120;
        ItemStats.Home(made).Stats.Add(new ItemStat { Id = StatCatalogue.IdOf("item_aura"), Stat = "item_aura", SkillId = meditation, Value = 12 });

        var outside = CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true));
        Assert.Contains(outside.Buffs, buff => buff is { Kind: BuffKind.ItemAura, Name: "Meditation", Level: 12, On: true }
                                               && buff.From.EndsWith("(Iron Golem)"));

        // In town, as the mercenary's, it does not reach him; and no golem, no aura.
        Assert.DoesNotContain(CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: false)).Buffs, buff => buff.Name == "Meditation");
        save.Golem.Exists = false;
        Assert.DoesNotContain(CharacterSheet.For(save, data, new CharacterSheet.Options(OutOfTown: true)).Buffs, buff => buff.Name == "Meditation");
    }
}
