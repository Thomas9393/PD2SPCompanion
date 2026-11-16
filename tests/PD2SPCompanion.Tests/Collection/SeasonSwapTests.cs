using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// What is built from the tables follows a change of season in one process.
/// Left stale, after S12 then S13 the grail
/// catalogue would still hold S12's 674 entries. The S12 pack's own tables are the other season here.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SeasonSwapTests(Game game)
{
    private static string S12Tables =>
        Path.Combine(Game.Corpus, "packs", "tdlofcc-s12", "PD2-PlugY-main", "ProjectD2", "data", "global", "excel", "modpacks", "official");

    [CorpusFact]
    public void The_grail_catalogue_and_property_list_follow_a_change_of_season()
    {
        if (!game.Available || !Directory.Exists(S12Tables))
            return;

        // This season, as the fixture installed it - asked first, so it is built and kept.
        int uniquesNow = GrailCatalog.Uniques().Count;
        int propertiesNow = PropertyIndex.Options.Count;

        using (Season.Open(Game.Root, S12Tables))
        {
            Assert.NotEqual(uniquesNow, GrailCatalog.Uniques().Count);
            Assert.NotEqual(propertiesNow, PropertyIndex.Options.Count);
        }

        // Back to this season; left open, as the fixture's tables are, for the tests after this one.
        Season.Open(Game.Root, Game.Tables);
        Assert.Equal(uniquesNow, GrailCatalog.Uniques().Count);
        Assert.Equal(propertiesNow, PropertyIndex.Options.Count);
    }
}
