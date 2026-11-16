using System.Security.Cryptography;
using System.Text.Json;

namespace PD2SPCompanion.Core.GameFiles;

/// <summary>
/// Which build of PD2 is installed: what it calls itself, and a fingerprint of the archive
/// the tables actually come from.
/// </summary>
/// <param name="Name">
/// The mod version as the launcher reports it - "12.0.6b". Empty where no launcher has
/// written one, which is why it is never the thing compared.
/// </param>
/// <param name="Fingerprint">
/// A digest of <c>pd2data.mpq</c>, which is where every table this editor reads is served
/// from. Empty where the archive could not be read.
/// </param>
public sealed record GameBuild(string Name, string Fingerprint)
{
    public static readonly GameBuild Unknown = new("", "");

    /// <summary>Whether this build can be told apart from another at all.</summary>
    public bool IsKnown => Fingerprint.Length > 0;

    /// <summary>
    /// The build as a sentence would name it, for a version that may not have been
    /// recorded.
    /// </summary>
    public string Shown => Name.Length > 0 ? Name : L("an earlier version");

    /// <summary>
    /// Whether these are the same build. Two unknowns are not treated as a match: with
    /// nothing to compare, nothing should be claimed.
    /// </summary>
    public bool Matches(string? fingerprint) =>
        IsKnown && !string.IsNullOrEmpty(fingerprint)
        && string.Equals(Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads which build of PD2 is installed.
/// <para>
/// The version string comes from the launcher and the fingerprint from the archive, and the
/// two are kept apart on purpose: the string is what a person recognises, but it is written
/// by a launcher that may not be the one used, so it is shown and never compared. The
/// archive is the thing the tables are read out of, so it is what decides whether anything
/// has moved.
/// </para>
/// </summary>
public static class ModVersion
{
    /// <summary>The archive every table this editor reads is served from.</summary>
    private const string Archive = @"ProjectD2\pd2data.mpq";

    /// <summary>
    /// Both PD2 launchers, either of which may have written the version. The first with one
    /// wins; neither having one is ordinary rather than a fault.
    /// </summary>
    private static readonly string[] Launchers = ["pd2-soe-launcher", "pd2-spp-launcher"];

    public static GameBuild Of(string gameRoot) => new(Named(), Fingerprinted(gameRoot));

    /// <summary>
    /// A digest of the archive. It is small - about two megabytes - so this is read in full
    /// rather than sampled, and a rebuilt archive with the same size cannot slip past.
    /// </summary>
    private static string Fingerprinted(string gameRoot)
    {
        try
        {
            string path = Path.Combine(gameRoot, Archive);
            if (!File.Exists(path))
                return "";

            using var file = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(file))[..16].ToLowerInvariant();
        }
        catch (Exception)
        {
            // An unreadable archive leaves the build unknown, which shows nothing rather
            // than showing something wrong.
            return "";
        }
    }

    private static string Named()
    {
        foreach (string launcher in Launchers)
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    launcher, "settings.json");

                if (!File.Exists(path))
                    continue;

                using var settings = JsonDocument.Parse(File.ReadAllText(path));

                if (settings.RootElement.TryGetProperty("about", out var about)
                    && about.TryGetProperty("mod_version", out var version)
                    && version.GetString() is { Length: > 0 } found)
                {
                    return found;
                }
            }
            catch (Exception)
            {
                // A launcher that will not parse is simply one that did not answer.
            }
        }

        return "";
    }
}
