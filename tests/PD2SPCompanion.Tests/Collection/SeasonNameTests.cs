using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The season is named from the tables' previous-season columns, and from nothing else:
/// any other suffix names no season, and the vault stays
/// closed rather than open under a guess.
/// </summary>
public sealed class SeasonNameTests
{
    [Theory]
    [InlineData("S12", "S13")]
    [InlineData("s9", "S10")]
    [InlineData("S13", "S14")]
    public void A_season_marker_names_the_season_after_it(string previous, string season) =>
        Assert.Equal(season, Vault.SeasonAfter(previous));

    [Theory]
    [InlineData("")]
    [InlineData("S")]
    [InlineData("S13a")]
    [InlineData("X12")]
    [InlineData("S-1")]
    [InlineData("S 12")]
    public void Anything_else_names_no_season(string previous) =>
        Assert.Null(Vault.SeasonAfter(previous));
}
