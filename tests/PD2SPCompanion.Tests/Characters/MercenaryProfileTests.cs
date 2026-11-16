using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// Who the mercenary is: named from the row of <c>hireling.txt</c> for the character's game
/// version. The classic rows come first in the table and differ - an expansion character's Might Barbarian
/// read "1hs", and the
/// cold Rogue's level was worked out from the classic row's experience per level.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class MercenaryProfileTests(Game game)
{
    private static D2S Hired(ushort type, bool expansion = true)
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        save.Mercenary.Id = 1;
        save.Mercenary.TypeId = type;
        save.Status.IsExpansion = expansion;
        return save;
    }

    /// <summary>Every kind hired in Normal, as PD2's wiki lists them: the kind, the variant and the aura (or what stands for one).</summary>
    [CorpusTheory]
    [InlineData(0, "Rogue", "Fire", "Vigor")]
    [InlineData(1, "Rogue", "Cold", "Meditation")]
    [InlineData(36, "Rogue", "Physical", "Slow Movement")]
    [InlineData(7, "Desert Guard", "Defensive", "Defiance")]
    [InlineData(8, "Desert Guard", "Offensive", "Blessed Aim")]
    [InlineData(6, "Desert Guard", "Combat", "Thorns")]
    [InlineData(15, "Iron Wolf", "Fire", "Cleansing")]
    [InlineData(16, "Iron Wolf", "Cold", "Prayer")]
    [InlineData(17, "Iron Wolf", "Lightning", "Holy Shock")]
    [InlineData(30, "Ascendant", "Dark", "Amplify Damage")]
    [InlineData(31, "Ascendant", "Light", "Sanctuary")]
    [InlineData(24, "Barbarian", "Combat", "Might")]
    [InlineData(25, "Barbarian", "Warcries", "Battle Orders")]
    public void Each_kind_is_named_as_the_wiki_names_it(int type, string kind, string variant, string skill)
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var profile = MercenaryProfile.Read(Hired((ushort)type), data);

        Assert.True(profile.IsHired);
        Assert.Equal(kind, profile.Kind);
        Assert.Equal(variant, profile.Variant);
        Assert.Equal(skill, profile.Skill);
        Assert.Equal("Normal", profile.Difficulty);
        Assert.StartsWith($"{variant} ({skill}), Normal", profile.Detail);
    }

    /// <summary>The difficulty hired in is the row's: a Hell Barbarian of the corpus, type 28.</summary>
    [CorpusFact]
    public void A_mercenary_hired_in_Hell_says_so()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var profile = MercenaryProfile.Read(Hired(28), data);
        Assert.Equal(("Barbarian", "Combat", "Might", "Hell"), (profile.Kind, profile.Variant, profile.Skill, profile.Difficulty));
    }

    /// <summary>
    /// The level comes off the version's own row: the cold Rogue costs 100 experience a step in the expansion
    /// and 105 in classic, so 100 x 10 x 10 x 11 experience is level 10 for the one and 9 for the other.
    /// </summary>
    [CorpusFact]
    public void The_level_is_worked_out_from_the_versions_own_row()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var expansion = Hired(1);
        expansion.Mercenary.Experience = 110_000;
        Assert.Equal(10, MercenaryProfile.Read(expansion, data).Level);

        var classic = Hired(1, expansion: false);
        classic.Mercenary.Experience = 110_000;
        Assert.Equal(9, MercenaryProfile.Read(classic, data).Level);
    }
}
