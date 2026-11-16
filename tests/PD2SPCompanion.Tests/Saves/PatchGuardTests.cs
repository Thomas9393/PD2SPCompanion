namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// The patch guard's check: every save in a folder
/// read back, a failing one named, and the build fingerprint a test can fake. And the season that
/// records which build its tables came from.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class PatchGuardTests(Game game)
{
    private static string Scratch(string name)
    {
        string folder = Path.Combine(Path.GetTempPath(), "pd2spc-tests", nameof(PatchGuardTests), name);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(Path.Combine(Game.Corpus, "saves")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        return folder;
    }

    [CorpusFact]
    public void Every_real_save_reads_back()
    {
        if (!game.Available)
            return;

        var results = PatchGuard.Verify(Scratch("good"));
        Assert.NotEmpty(results);
        Assert.All(results, each => Assert.True(each.Good, $"{each.File}: {each.Problem}"));
    }

    [CorpusFact]
    public void A_save_that_does_not_read_back_is_named()
    {
        if (!game.Available)
            return;

        string folder = Scratch("bad");
        File.WriteAllBytes(Path.Combine(folder, "Broken.d2s"), [.. File.ReadAllBytes(Path.Combine(folder, "PhySpearZon.d2s")).Take(2000)]);

        var bad = PatchGuard.Verify(folder).Where(each => !each.Good).ToList();
        Assert.Equal(["Broken.d2s"], bad.Select(each => each.File));
    }

    [CorpusFact]
    public void The_fingerprint_follows_a_part_that_changes()
    {
        // Worked out again only when a part's length or write time moved: the
        // same install asked twice is not read twice, and a patched part is seen.
        string root = Path.Combine(Path.GetTempPath(), "pd2spc-tests", nameof(PatchGuardTests), "build");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(Path.Combine(root, "ProjectD2"));
        foreach (string part in PatchGuard.Parts)
            File.WriteAllText(Path.Combine(root, part), part);

        string first = PatchGuard.Fingerprint(root);
        Assert.NotEmpty(first);
        Assert.Equal(first, PatchGuard.Fingerprint(root));

        string archive = Path.Combine(root, PatchGuard.Parts[0]);
        File.WriteAllText(archive, "patched");
        File.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddMinutes(1));
        string patched = PatchGuard.Fingerprint(root);
        Assert.NotEqual(first, patched);

        // The same content as before, written anew: the digest is of the content, so the build is the first again.
        File.WriteAllText(archive, PatchGuard.Parts[0]);
        File.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddMinutes(2));
        Assert.Equal(first, PatchGuard.Fingerprint(root));
    }

    [CorpusFact]
    public void A_faked_build_is_what_the_fingerprint_says()
    {
        Environment.SetEnvironmentVariable(PatchGuard.FakeBuild, "test-build");
        try
        {
            Assert.Equal("test-build", PatchGuard.Fingerprint(Game.Root));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PatchGuard.FakeBuild, null);
        }
    }

    [CorpusFact]
    public void A_season_knows_its_build_and_is_the_current_one()
    {
        if (!game.Available)
            return;

        using var season = Season.Open(Game.Root, Game.Tables);
        Assert.Same(season, Season.Current);
        Assert.Equal(PatchGuard.Fingerprint(Game.Root), season.Build);
        Assert.NotEmpty(season.Build);
    }
}
