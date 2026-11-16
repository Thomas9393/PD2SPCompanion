using System.Reflection;

namespace PD2SPCompanion.Core;

/// <summary>
/// The editor's version, as the build stamped it (src/Directory.Build.props): every project carries the
/// same one, so this reads its own assembly's. <c>1.0.0-dev+&lt;commit&gt;</c> splits into the number
/// (<c>1.0.0</c>), the label shown (<c>1.0.0-dev</c>), the commit and the build date.
/// </summary>
public static class AppVersion
{
    /// <summary>The application's name, where people see it.</summary>
    public const string AppName = "Project Diablo II Single Player Companion";

    private static readonly Assembly Self = typeof(AppVersion).Assembly;

    /// <summary>The whole stamp: <c>1.0.0-dev+&lt;commit&gt;</c>.</summary>
    public static string Informational { get; } =
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>What is shown: the number, and <c>-dev</c> on a build that is not a release.</summary>
    public static string Label { get; } = Informational.Split('+')[0];

    /// <summary>The release number alone, <c>1.0.0</c>: what "What's new" compares.</summary>
    public static string Number { get; } = Label.Split('-')[0];

    /// <summary>A build that is not a release: run from the source, not handed out.</summary>
    public static bool IsDev { get; } = Label.Contains('-');

    /// <summary>The commit it was built from, short; null when the build did not know it.</summary>
    public static string? Commit { get; } =
        Informational.Split('+') is [_, var sha, ..] && sha.Length > 0 ? sha[..Math.Min(7, sha.Length)] : null;

    /// <summary>The day it was built, <c>yyyy-MM-dd</c>; null when the build did not say.</summary>
    public static string? BuildDate { get; } =
        Self.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(each => each.Key == "BuildDate")?.Value;

    /// <summary>Under the version: "Built 2026-10-01 from 994e076".</summary>
    public static string Built =>
        (BuildDate, Commit) switch
        {
            ({ } date, { } commit) => L($"Built {date} from {commit}"),
            ({ } date, null) => L($"Built {date}"),
            (null, { } commit) => L($"Built from {commit}"),
            _ => "",
        };

    /// <summary>
    /// Whether <paramref name="version"/> is a release number older than this one - "1.0.0" before
    /// "1.1.0". Anything that does not read as a number is not older.
    /// </summary>
    public static bool IsNewerThan(string version) =>
        System.Version.TryParse(Number, out var now) && System.Version.TryParse(version.Split('-')[0], out var then) && now > then;
}
