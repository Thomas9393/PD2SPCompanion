namespace PD2SPCompanion.Tests.GameFiles;

/// <summary>
/// The seven classes as one fixed list: the lookups, and that the list is what
/// the season's own tables say - a season that renamed or reordered a class fails here.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CharacterClassesTests(Game game)
{
    [GameFact]
    public void Ids_codes_and_names_pair_up()
    {
        Assert.Equal("Amazon", CharacterClasses.NameOfCode("AMA"));
        Assert.Equal("ass", CharacterClasses.CodeOf(6));
        Assert.Equal("Druid", CharacterClasses.NameOf(5));
        Assert.Null(CharacterClasses.NameOf(7));
        Assert.Null(CharacterClasses.CodeOf(-1));
        Assert.Null(CharacterClasses.NameOfCode(null));
        Assert.Null(CharacterClasses.NameOfCode("zon"));
    }

    [GameFact]
    public void The_fixed_list_is_the_seasons_own()
    {
        if (!game.Available)
            return;

        Assert.Equal(CharacterClasses.Names, SkillTabs.Classes);
    }
}
