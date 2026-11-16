namespace PD2SPCompanion.Tests.Collection;

/// <summary>What's new: the releases after the last one run, and none past this build's.</summary>
public sealed class ChangelogSinceTests
{
    private static ChangelogRelease Release(string version) => new(version, "", "", []);

    [Fact]
    public void The_releases_after_the_last_run_up_to_this_build()
    {
        ChangelogRelease[] releases = [Release("Unreleased"), Release("1.2.0"), Release("1.1.0"), Release("1.0.0")];

        var since = Changelog.Since(releases, "1.0.0-beta", "1.1.0-beta");

        Assert.Equal(["1.1.0"], since.Select(release => release.Version));
    }
}
