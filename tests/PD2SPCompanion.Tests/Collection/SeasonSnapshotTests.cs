using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A season's snapshot reads that season back exactly:
/// every file the install read is kept, so a season opened from it is served wholly from the snapshot
/// and never falls back to a newer archive's file.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class SeasonSnapshotTests(Game game) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-snapshot-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [GameFact]
    [Trait("Speed", "Slow")]
    public void A_snapshot_reopens_its_season_from_itself_alone()
    {
        if (!game.Available)
            return;

        string snapshot = Path.Combine(_folder, "S99");
        string statsBefore;
        SeasonSnapshot.Manifest taken;

        // The installed season, opened as the editor opens it, and kept straight away.
        var live = Season.Open(Game.Root, Game.Tables);
        taken = SeasonSnapshot.Take(live, "S99", snapshot, itemVersion: "103");
        statsBefore = Encoding(D2SLib.Core.TXT.ItemStatCostTXT);
        Assert.True(statsBefore.Length > 5000, "the encoding read nothing to compare");

        Assert.Contains(taken.Files, file => file.Path.EndsWith("itemstatcost.txt", StringComparison.OrdinalIgnoreCase));
        // Every table the archives list, not only what the install read: one read later, on demand, is kept too.
        foreach (string table in GameData.ExcelTables)
            Assert.Contains(taken.Files, file => file.Path.Equals(GameData.ExcelPath(table), StringComparison.OrdinalIgnoreCase));
        Assert.True(taken.Files.Count > 90, $"only {taken.Files.Count} files kept");
        Assert.Equal("S99", SeasonSnapshot.Read(snapshot)?.Season);
        Assert.Equal("103", SeasonSnapshot.Read(snapshot)?.ItemVersion);
        Assert.Equal(live.Build, SeasonSnapshot.Read(snapshot)?.Build);

        // The same season, from the snapshot: every file it read came from the snapshot, none from the archives.
        using (var again = Season.Open(Game.Root, snapshot))
        {
            foreach (string path in again.Game.ServedPaths)
                Assert.Equal("S99", again.Game.Read(path)?.Archive);

            Assert.Equal(statsBefore, Encoding(D2SLib.Core.TXT.ItemStatCostTXT));
        }

        // The live tables back for the tests after this one; left open, as the fixture's are.
        Season.Open(Game.Root, Game.Tables);
    }

    /// <summary>Every stat's encoding, as one string: what a season's item bits are read with.</summary>
    private static string Encoding(D2SLib.Model.TXT.ItemStatCostTXT table) =>
        string.Join("|", Enumerable.Range(0, 600)
            .Select(id => table[id])
            .Where(row => row is not null)
            .Select(row => $"{row!["Stat"].Value}:{row["Save Bits"].Value}:{row["Save Add"].Value}:{row["Save Param Bits"].Value}"));
}
