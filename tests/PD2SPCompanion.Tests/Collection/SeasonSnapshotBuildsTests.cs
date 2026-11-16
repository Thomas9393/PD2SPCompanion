using System.Text.Json;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A patch within a season replaces the season's snapshot with the new build's; where it changed a table the save
/// parser reads an item's bits by, the old build's copy of those tables is kept in the new snapshot's <c>builds</c>
/// folder (SeasonSnapshot.KeepEarlierBuilds) - the only record of the widths the items written
/// before the patch hold.
/// </summary>
public class SeasonSnapshotBuildsTests
{
    private static string Stats => GameData.ExcelPath("itemstatcost");

    private static SeasonSnapshot.KeptFile File(string path, string sha) => new(path, 4, sha);

    /// <summary>A snapshot folder of one build: its itemstatcost and a table no parser reads, and its manifest.</summary>
    private static SeasonSnapshot.Manifest Snapshot(string folder, string build, string statsSha, string text)
    {
        foreach (string path in new[] { Stats, GameData.ExcelPath("levels") })
        {
            string target = Path.Combine(folder, path.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.File.WriteAllText(target, text);
        }

        var manifest = new SeasonSnapshot.Manifest("S14", build, "", "S13", "105", DateTimeOffset.UtcNow,
                                                   [File(Stats, statsSha), File(GameData.ExcelPath("levels"), "L")]);
        System.IO.File.WriteAllText(Path.Combine(folder, SeasonSnapshot.ManifestName), JsonSerializer.Serialize(manifest));
        return manifest;
    }

    [Fact]
    public void A_patch_that_changes_the_encoding_keeps_the_old_builds_and_those_kept_before()
    {
        string root = Path.Combine(Path.GetTempPath(), "pd2spc-snapshot-builds-" + Guid.NewGuid().ToString("N"));
        try
        {
            string folder = Path.Combine(root, "S14"), taking = folder + ".taking";
            var first = Snapshot(folder, "aaaa", "OLD", "widths of aaaa");
            Directory.CreateDirectory(taking);

            Assert.True(SeasonSnapshot.KeepEarlierBuilds(folder, taking, first, [File(Stats, "NEW"), File(GameData.ExcelPath("levels"), "L")], "bbbb"));

            string kept = Path.Combine(taking, SeasonSnapshot.BuildsFolder, "aaaa");
            Assert.Equal("widths of aaaa", System.IO.File.ReadAllText(Path.Combine(kept, Stats.Replace('\\', Path.DirectorySeparatorChar))));
            Assert.Equal("aaaa", SeasonSnapshot.Read(kept)?.Build);
            Assert.Equal([Stats], SeasonSnapshot.Read(kept)!.Files.Select(file => file.Path));

            // The next patch, the encoding the same again: nothing new kept, the first build's carried along.
            Directory.Delete(folder, recursive: true);
            Directory.Move(taking, folder);
            var second = first with { Build = "bbbb", Files = [File(Stats, "NEW")] };
            string next = folder + ".taking";
            Directory.CreateDirectory(next);
            Assert.False(SeasonSnapshot.KeepEarlierBuilds(folder, next, second, [File(Stats, "NEW")], "cccc"));
            Assert.True(Directory.Exists(Path.Combine(next, SeasonSnapshot.BuildsFolder, "aaaa")));
            Assert.False(Directory.Exists(Path.Combine(next, SeasonSnapshot.BuildsFolder, "bbbb")));

            // The same build taken again: nothing to keep.
            Assert.False(SeasonSnapshot.KeepEarlierBuilds(folder, next, second, [File(Stats, "OTHER")], "bbbb"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
