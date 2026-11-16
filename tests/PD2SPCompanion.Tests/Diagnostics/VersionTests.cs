using Xunit;

namespace PD2SPCompanion.Tests.Diagnostics;

/// <summary>
/// The editor's version and its release notes: the build's stamp reads back, the
/// changelog parses, and the version being built always has its section - raising the version without
/// writing what changed fails here.
/// </summary>
public sealed class VersionTests
{
    [Fact]
    public void The_build_stamp_reads_as_number_label_and_build()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Number);
        Assert.StartsWith(AppVersion.Number, AppVersion.Label);
        Assert.Equal(AppVersion.Label.Contains('-'), AppVersion.IsDev);
        Assert.NotEqual("", AppVersion.Built);
    }

    [Theory]
    [InlineData("0.0.9", true)]
    [InlineData("0.0.9-dev", true)]
    [InlineData("99.0.0", false)]
    [InlineData("", false)]
    [InlineData("not a version", false)]
    public void Older_versions_are_older(string then, bool older) =>
        Assert.Equal(older, AppVersion.IsNewerThan(then));

    [Fact]
    public void The_changelog_reads_releases_groups_and_wrapped_lines()
    {
        const string text = """
            # Changelog
            Intro, not a release.

            ## [Unreleased]
            ### Added
            - Not out yet

            ## [0.2.0] - 2026-11-01
            The season update.
            ### Added
            - One thing
            - A long thing
              that wraps
            ### Fixed
            - A fix

            ## [0.1.0] - 2026-10-01
            ### Added
            - The first
            """;

        var releases = Changelog.Parse(text);

        Assert.Equal(["0.2.0", "0.1.0"], releases.Select(release => release.Version));
        var latest = releases[0];
        Assert.Equal("2026-11-01", latest.Date);
        Assert.Equal("0.2.0 · 2026-11-01", latest.Title);
        Assert.Equal("The season update.", latest.About);
        Assert.Equal(["Added", "Fixed"], latest.Groups.Select(group => group.Heading));
        Assert.Equal(["One thing", "A long thing that wraps"], latest.Groups[0].Lines);

        Assert.Equal(["0.2.0"], Changelog.Since(releases, "0.1.0", "99.0.0").Select(release => release.Version));
        Assert.Empty(Changelog.Since(releases, "0.2.0", "99.0.0"));
    }

    [Fact]
    public void The_version_being_built_has_its_release_notes()
    {
        var current = Changelog.Bundled.FirstOrDefault(release => release.Version == AppVersion.Number);

        Assert.True(current is not null,
            $"CHANGELOG.md has no section for {AppVersion.Number}: add one when raising the version in src/Directory.Build.props.");
        Assert.NotEmpty(current!.Groups);
    }
}
