using Xunit;

namespace PD2SPCompanion.Tests.GameFiles;

/// <summary>
/// The season facts in code read against the installed
/// tables: every one gives a line, none fails, and the lists the code holds are all in the tables.
/// What each line says is tests/season.py's to compare across a rollover.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SeasonFactsTests(Game game)
{
    [GameFact]
    public void Every_fact_reads_against_the_installed_tables()
    {
        if (!game.Available)
            return;

        var read = SeasonFacts.Read();

        Assert.Equal(SeasonFacts.All.Count, read.Count);
        Assert.All(read, each => Assert.DoesNotContain("failed", each.State));
        Assert.All(read.Where(each => each.Fact.Name is "Materials tab" or "Materials tab, Season 14's" or "Skinned uniques"),
                   each => Assert.EndsWith("all in the tables", each.State));
        Assert.StartsWith("row ", read.Single(each => each.Fact.Name == "Kadala's Heirloom").State);

        // What the code says of Season 14: its stash places every slot, its stamps ship, its rolls read off the cube.
        Assert.Contains("v3 places 163/163", read.Single(each => each.Fact.Name == "Shared stash layouts").State);
        Assert.Contains("S14 105", read.Single(each => each.Fact.Name == "Shipped item stamps").State);
        Assert.Contains("1000 on gear, 3000 on maps", read.Single(each => each.Fact.Name == "Corruption rolls").State);
    }
}
