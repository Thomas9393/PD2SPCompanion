namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// What the Armory Import box makes of what was typed: a PD2 armory link is
/// fetched as soon as it is pasted, any other link is a warning, and a bare name waits for Fetch.
/// </summary>
public sealed class ArmoryRecogniseTests
{
    [Theory]
    [InlineData("https://www.projectdiablo2.com/character/SomeName", "SomeName")]
    [InlineData("https://projectdiablo2.com/character/SomeName/", "SomeName")]
    [InlineData("projectdiablo2.com/character/Some_Name?tab=items", "Some_Name")]
    [InlineData("http://api.projectdiablo2.com/game/character/SomeName", "SomeName")]
    public void A_PD2_armory_link_gives_its_character(string typed, string name)
    {
        var (found, isLink, problem) = ArmoryImport.Recognise(typed);

        Assert.Equal(name, found);
        Assert.True(isLink);
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("https://example.com/character/SomeName", "Not a PD2 armory link.")]
    [InlineData("https://www.projectdiablo2.com/market", "No character in that link.")]
    [InlineData("https://www.projectdiablo2.com/character/", "No character in that link.")]
    [InlineData("ftp://projectdiablo2.com/character/SomeName", "Not a valid link.")]
    public void Any_other_link_is_a_warning(string typed, string warning)
    {
        var (found, isLink, problem) = ArmoryImport.Recognise(typed);

        Assert.Null(found);
        Assert.True(isLink);
        Assert.Equal(warning, problem);
    }

    [Fact]
    public void A_bare_name_is_a_name_and_not_a_link()
    {
        Assert.Equal(("SomeName", false, (string?)null), ArmoryImport.Recognise("  SomeName "));
        Assert.Equal(((string?)null, false, "Not a character name."), ArmoryImport.Recognise("Some Name"));
        Assert.Equal(((string?)null, false, (string?)null), ArmoryImport.Recognise("   "));
    }
}
