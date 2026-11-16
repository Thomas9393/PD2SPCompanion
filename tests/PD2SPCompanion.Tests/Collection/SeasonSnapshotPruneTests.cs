using System.Text.Json;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The seasons' snapshots are held to the newest three (SeasonSnapshot.Prune, against
/// uncontrolled growth of data - a season's tables are some 5 MB, and every season's stood for ever): the
/// oldest go as a new season's is taken - but for a season that still has a vault of its own, whose tables are
/// what that vault is migrated by, and a folder that is no snapshot.
/// </summary>
public class SeasonSnapshotPruneTests
{
    private static readonly DateTimeOffset Now = new(2027, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static void Snapshot(string seasons, string name, int monthsAgo)
    {
        string folder = Path.Combine(seasons, name);
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        File.WriteAllText(Path.Combine(folder, "data", "table.txt"), "rows");
        var manifest = new SeasonSnapshot.Manifest(name, "build", "", "", null, Now.AddMonths(-monthsAgo), []);
        File.WriteAllText(Path.Combine(folder, SeasonSnapshot.ManifestName), JsonSerializer.Serialize(manifest));
    }

    [Fact]
    public void The_newest_three_stay_and_a_season_that_still_has_a_vault()
    {
        string root = Path.Combine(Path.GetTempPath(), "pd2spc-snapshot-prune-" + Guid.NewGuid().ToString("N"));
        string seasons = Path.Combine(root, "Seasons"), vaults = Path.Combine(root, "Vaults");
        try
        {
            Snapshot(seasons, "S16", 0);
            Snapshot(seasons, "S15", 4);
            Snapshot(seasons, "S14", 8);
            Snapshot(seasons, "S13", 12);
            Snapshot(seasons, "S12", 16);
            Directory.CreateDirectory(Path.Combine(seasons, "notes"));

            // S12 still has a vault of its own, never migrated; S13's folder is there and empty.
            Directory.CreateDirectory(Path.Combine(vaults, "S12", "Softcore"));
            File.WriteAllText(Path.Combine(vaults, "S12", "Softcore", "S12 Main.pd2lib"), "vault");
            Directory.CreateDirectory(Path.Combine(vaults, "S13", "Softcore"));

            Assert.Equal(1, SeasonSnapshot.Prune(seasons, vaults));
            Assert.Equal(
                ["S12", "S14", "S15", "S16", "notes"],
                Directory.GetDirectories(seasons).Select(Path.GetFileName).Order(StringComparer.Ordinal));

            // And nothing where there is nothing to hold.
            Assert.Equal(0, SeasonSnapshot.Prune(Path.Combine(root, "none"), vaults));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
