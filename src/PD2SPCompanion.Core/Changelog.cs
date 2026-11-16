namespace PD2SPCompanion.Core;

/// <summary>
/// The release notes, CHANGELOG.md at the repo's root, built into the editor: what Settings' "What's new"
/// shows, and the notice once after an update. The file's form is its own header's - a
/// <c>## [version] - date</c> heading per release, an optional line, <c>###</c> groups of <c>-</c> lines.
/// </summary>
public static class Changelog
{
    /// <summary>The releases in a changelog's text, newest first as written; Unreleased is left out.</summary>
    public static IReadOnlyList<ChangelogRelease> Parse(string text)
    {
        var releases = new List<ChangelogRelease>();
        string? version = null, date = "";
        var about = new List<string>();
        var groups = new List<ChangelogGroup>();
        string? heading = null;
        var lines = new List<string>();

        void EndGroup()
        {
            if (heading is not null && lines.Count > 0)
                groups.Add(new ChangelogGroup(heading, [.. lines]));
            heading = null;
            lines = [];
        }

        void EndRelease()
        {
            EndGroup();
            if (version is not null && !version.Equals("Unreleased", StringComparison.OrdinalIgnoreCase))
                releases.Add(new ChangelogRelease(version, date ?? "", string.Join(" ", about), [.. groups]));
            version = null;
            date = "";
            about.Clear();
            groups = [];
        }

        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                EndRelease();
                string head = line[3..].Trim();
                int close = head.IndexOf(']');
                version = head.StartsWith('[') && close > 0 ? head[1..close] : head.Split(' ')[0];
                int dash = head.IndexOf(" - ", StringComparison.Ordinal);
                date = dash >= 0 ? head[(dash + 3)..].Trim() : "";
            }
            else if (version is null)
            {
                continue;
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                EndGroup();
                heading = line[4..].Trim();
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                lines.Add(line[2..].Trim());
            }
            else if (line.StartsWith("  ", StringComparison.Ordinal) && lines.Count > 0)
            {
                // A wrapped line belongs to the change above it.
                lines[^1] += " " + line.Trim();
            }
            else if (line.Length > 0 && heading is null)
            {
                about.Add(line.Trim());
            }
        }

        EndRelease();
        return releases;
    }

    /// <summary>The changelog built into this editor (CHANGELOG.md), read once.</summary>
    public static IReadOnlyList<ChangelogRelease> Bundled => _bundled ??= Parse(BundledText());

    private static IReadOnlyList<ChangelogRelease>? _bundled;

    private static string BundledText()
    {
        using var stream = typeof(Changelog).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null)
            return "";

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The releases after <paramref name="version"/>, up to this build's, newest first.</summary>
    public static IReadOnlyList<ChangelogRelease> Since(IReadOnlyList<ChangelogRelease> releases, string version, string build) =>
        [.. releases.Where(release =>
            System.Version.TryParse(release.Version, out var number)
            && System.Version.TryParse(version.Split('-')[0], out var then)
            && System.Version.TryParse(build.Split('-')[0], out var now)
            && number > then && number <= now)];
}

/// <summary>One release in the changelog: its number, its date, the line about it and its groups of changes.</summary>
public sealed record ChangelogRelease(string Version, string Date, string About, IReadOnlyList<ChangelogGroup> Groups)
{
    /// <summary>"1.0.0 · 2026-11-16".</summary>
    public string Title => Date.Length > 0 ? $"{Version} · {Date}" : Version;

    /// <summary>The line about it, where there is one.</summary>
    public bool HasAbout => About.Length > 0;
}

/// <summary>One group of changes in a release, "Added" and its lines.</summary>
public sealed record ChangelogGroup(string Heading, IReadOnlyList<string> Lines);
