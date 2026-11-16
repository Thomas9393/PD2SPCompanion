using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// A point goes into a skill only with its prerequisites holding points of their own, in every mode: the game's tree
/// counts no item's skill. A Necromancer with Lower Resist at +19 from items and nothing in Weaken saw "+" lit in
/// Free mode, which left the prerequisites out.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SkillPrerequisiteTests(Game game)
{
    private const byte Necromancer = 2;

    private static SkillEditor Necro(GameData data)
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        NewCharacter.Begin(save, "Prereqs", Necromancer, 60, hardcore: false, expansion: true, finished: false);
        var skills = new SkillEditor(save, SkillTree.For(data, Necromancer));
        skills.AddUnspent(30);
        return skills;
    }

    private static SkillEntry Named(SkillEditor skills, string name) => skills.Tree.BySlot.Values.First(each => each.Name == name);

    [CorpusFact]
    public void A_skill_takes_no_point_while_its_prerequisite_has_none_of_its_own_in_any_mode()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var skills = Necro(data);
        var lowerResist = Named(skills, "Lower Resist");

        foreach (bool free in new[] { false, true })
        {
            Assert.Contains("Weaken", skills.CannotAdd(lowerResist, 60, freely: free));
            Assert.Equal(0, skills.Add(lowerResist, 1, 60, freely: free).Added);
        }

        // With the chain's own points, Free and the rest alike.
        foreach (string name in new[] { "Amplify Damage", "Life Tap", "Weaken" })
            Assert.Equal(1, skills.Add(Named(skills, name), 1, 60, freely: false).Added);
        Assert.Null(skills.CannotAdd(lowerResist, 60, freely: false));
        Assert.Null(skills.CannotAdd(lowerResist, 60, freely: true));
    }

    [CorpusFact]
    public void Free_mode_still_leaves_out_the_character_level_and_a_test_tool_the_prerequisites_too()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var skills = Necro(data);
        var amplify = Named(skills, "Amplify Damage");
        var lowerResist = Named(skills, "Lower Resist");

        Assert.NotNull(skills.CannotAdd(amplify, 0, freely: false));
        Assert.Null(skills.CannotAdd(amplify, 0, freely: true));
        Assert.Null(skills.CannotAdd(lowerResist, 1, freely: true, withoutPrerequisites: true));
    }
}
