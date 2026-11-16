namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// The skill tree's tooltip: what the skill does, its hard points and the soft
/// points items add, and the synergies it receives as the game words them - read against screenshots of
/// Amplify Damage, Curse Mastery and Raise Skeleton Warrior.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SkillTipsTests(Game game)
{
    private const byte Necromancer = 2;

    private static SkillEntry Named(SkillTree tree, string name) =>
        tree.Pages.SelectMany(page => page.Skills).Single(skill => skill.Name == name);

    [GameFact]
    public void A_skill_says_what_it_does_and_its_synergies_as_the_game_does()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var tree = SkillTree.For(data, Necromancer);
        var amplify = Named(tree, "Amplify Damage");

        // In game: "curses a group of enemies, increasing / the non-magic damage they receive".
        Assert.Equal(["curses a group of enemies, increasing", "the non-magic damage they receive"], SkillTips.Description(amplify));

        // The game's heading names the skill; the tooltip's own heading does ("Receives bonuses from" is enough).
        Assert.Equal(
            [
                "Receives bonuses from:",
                "Curse Mastery: +1% Reduced Physical Resistance Per Two Levels",
                "Curse Mastery: +0.6 Radius per Three Levels",
            ],
            SkillTips.Synergies(amplify));

        Assert.Equal(
            ["Receives bonuses from:", "Skeleton Mastery: +6% Damage per Level", "Raise Skeleton Archer: +2% Damage per Level"],
            SkillTips.Synergies(Named(tree, "Raise Skeleton Warrior")));

        // A passive that receives none says none.
        Assert.Empty(SkillTips.Synergies(Named(tree, "Curse Mastery")));
    }

    [GameFact]
    public void Items_add_levels_where_a_hard_point_is_and_an_oskill_anywhere()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var tree = SkillTree.For(data, Necromancer);
        var amplify = Named(tree, "Amplify Damage");
        int curses = (Necromancer << 3) | (amplify.Page - 1);
        (string, int, int)[] gear =
        [
            ("item_allskills", 2, 0),
            ("item_addclassskills", 1, Necromancer),
            ("item_addclassskills", 5, 0),          // an Amazon's: not this character's
            ("item_addskill_tab", 3, curses),
            ("item_singleskill", 1, amplify.Id),
        ];

        Assert.Equal(7, SkillTips.BonusFrom(gear, amplify, Necromancer, hard: 12));

        // No hard point: "+1 to Amplify Damage (Necromancer Only)" grants the skill, and all the rest then counts -
        // the 7 (Blood Warp; not "0 +6" with "+21 more"). Nothing granting it: none.
        Assert.Equal(7, SkillTips.BonusFrom(gear, amplify, Necromancer, hard: 0));
        Assert.Equal(0, SkillTips.BonusFrom([.. gear.Where(line => line.Item1 != "item_singleskill")], amplify, Necromancer, hard: 0));
        Assert.Equal(3, SkillTips.BonusFrom([("item_nonclassskill", 3, amplify.Id)], amplify, Necromancer, hard: 0));

        // An oskill of the character's own class counts for 3 at most from each item.
        Assert.Equal(3, SkillTips.BonusFrom([("item_nonclassskill", 6, amplify.Id)], amplify, Necromancer, hard: 0));
        Assert.Equal(4, SkillTips.BonusFrom([("item_nonclassskill", 2, amplify.Id), ("item_nonclassskill", 2, amplify.Id)],
            amplify, Necromancer, hard: 0));
        Assert.Equal(10, SkillTips.BonusFrom([.. gear, ("item_nonclassskill", 6, amplify.Id)], amplify, Necromancer, hard: 12));

        // "+N to Magic Skills" raises the skills whose EType is mag (Amplify Damage is), others not; with a hard
        // point. item_elemskill grants by its parameter (3 magic, 1 fire); the label stats beside it count nothing.
        (string, int, int)[] magic =
        [
            ("item_elemskill", 2, 3), ("item_elemskill_magic", 2, 0),
            ("item_elemskill", 5, 1), ("item_elemskill_fire", 5, 0),
        ];
        Assert.Equal(2, SkillTips.BonusFrom(magic, amplify, Necromancer, hard: 1));
        Assert.Equal(0, SkillTips.BonusFrom(magic, amplify, Necromancer, hard: 0));
        Assert.Equal(5, SkillTips.BonusFrom(magic, Named(tree, "Corpse Explosion"), Necromancer, hard: 1));
        Assert.Equal(0, SkillTips.BonusFrom(magic, Named(tree, "Curse Mastery"), Necromancer, hard: 1));

        // What waits for that first point: nothing where the skill is granted; without the grant, the 6 soft.
        Assert.Equal(0, SkillTips.Waiting(gear, amplify, Necromancer));
        Assert.Equal(0, SkillTips.Waiting([.. gear, ("item_nonclassskill", 3, amplify.Id)], amplify, Necromancer));
        Assert.Equal(6, SkillTips.Waiting([.. gear.Where(line => line.Item1 != "item_singleskill")], amplify, Necromancer));
    }
}
