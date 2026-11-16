using System.Text.Json;

namespace PD2SPCompanion.Core.GameFiles;

/// <summary>
/// Finds the Diablo II folder PD2 is installed into, and says whether a folder is one.
/// <para>
/// The editor reads every table, string and sprite out of that folder's archives, so it is
/// the one thing it cannot work without and the one thing it cannot guess wrong quietly -
/// a wrong folder means editing saves against another game's tables.
/// </para>
/// </summary>
public static class GameLocation
{
    /// <summary>
    /// The base game's archive, and the mod's. Both are required: without the first this is
    /// not Diablo II, and without the second it is Diablo II without Project Diablo 2,
    /// whose tables are the ones every number in this editor is read from.
    /// </summary>
    private const string BaseArchive = "d2data.mpq";
    private const string ModArchive = @"ProjectD2\pd2data.mpq";

    /// <summary>Where the game lives, named the way the folder holds it.</summary>
    public static bool Holds(string? folder) => Problem(folder) is null;

    /// <summary>
    /// What is wrong with this folder, or null where nothing is. The sentence is the one
    /// shown to whoever picked it, so it names what was looked for.
    /// </summary>
    public static string? Problem(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return L("No folder.");

        try
        {
            if (!Directory.Exists(folder))
                return L("Folder not found.");

            if (!File.Exists(Path.Combine(folder, BaseArchive)))
                return L($"Invalid folder, requires folder containing {BaseArchive}.");

            if (!File.Exists(Path.Combine(folder, ModArchive)))
                return L("Project Diablo 2 installation not found.");

            return null;
        }
        catch (Exception)
        {
            // A path that cannot even be looked at is a path that cannot be used.
            return L("Invalid folder.");
        }
    }

    /// <summary>
    /// Every folder worth offering, best first, each one checked before it is offered.
    /// Usually one; empty where the game is somewhere nobody would think to look, which is
    /// what the browse button is for.
    /// </summary>
    public static IReadOnlyList<string> Detect()
    {
        var found = new List<string>();

        foreach (string candidate in Guesses())
        {
            string tidy = Tidy(candidate);

            if (tidy.Length > 0
                && Holds(tidy)
                && !found.Contains(tidy, StringComparer.OrdinalIgnoreCase))
            {
                found.Add(tidy);
            }
        }

        return found;
    }

    /// <summary>
    /// The launchers first, because they are the only source that knows rather than
    /// guesses: both PD2 launchers record where they installed the game.
    /// </summary>
    private static IEnumerable<string> Guesses()
    {
        foreach (string told in FromLaunchers())
            yield return told;

        foreach (string usual in Usual())
            yield return usual;
    }

    private static IEnumerable<string> FromLaunchers()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        foreach (string launcher in new[] { "pd2-soe-launcher", "pd2-spp-launcher" })
        {
            string path = Path.Combine(roaming, launcher, "settings.json");
            JsonDocument? settings = null;

            try
            {
                if (File.Exists(path))
                    settings = JsonDocument.Parse(File.ReadAllText(path));
            }
            catch (Exception)
            {
                // A launcher whose settings will not parse simply did not answer.
            }

            if (settings is null)
                continue;

            using (settings)
            {
                if (!settings.RootElement.TryGetProperty("preferences", out var preferences))
                    continue;

                // Both point inside the game folder rather than at it: gameDirectory is the
                // ProjectD2 folder and saveDirectory is Save, so the folder wanted is the
                // parent of either.
                foreach (string key in new[] { "gameDirectory", "saveDirectory" })
                {
                    if (preferences.TryGetProperty(key, out var told)
                        && told.GetString() is { Length: > 0 } inside)
                    {
                        yield return Above(inside);
                    }
                }
            }
        }
    }

    /// <summary>The places an installer would have put it, for a machine with no launcher.</summary>
    private static IEnumerable<string> Usual()
    {
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.ProgramFiles,
                 })
        {
            string root = Environment.GetFolderPath(folder);
            if (root.Length > 0)
                yield return Path.Combine(root, "Diablo II");
        }

        string[] drives;

        try
        {
            drives = [.. DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed)
                .Select(drive => drive.RootDirectory.FullName)];
        }
        catch (Exception)
        {
            yield break;
        }

        // Only the two spellings someone installing by hand would use. This is a guess of
        // last resort, so it stays cheap rather than searching.
        foreach (string drive in drives)
        {
            yield return Path.Combine(drive, "Diablo II");
            yield return Path.Combine(drive, "Games", "Diablo II");
        }
    }

    /// <summary>The folder above this one, for a path that points inside the game folder.</summary>
    private static string Above(string inside)
    {
        try
        {
            return Path.GetDirectoryName(Tidy(inside)) ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// The folder as picked, and where it is PD2's own - ProjectD2, holding pd2data.mpq - the Diablo II
    /// folder around it, which is the one wanted. "Project Diablo II game folder" sends people into
    /// ProjectD2 as often as not. Anything else comes back tidied, as it was.
    /// </summary>
    public static string Resolve(string? path)
    {
        string tidy = Tidy(path);
        try
        {
            if (tidy.Length > 0 && File.Exists(Path.Combine(tidy, Path.GetFileName(ModArchive)))
                && Directory.GetParent(tidy)?.FullName is { } parent && Holds(parent))
                return Tidy(parent);
        }
        catch (Exception)
        {
            // A path that cannot be looked at is left for Problem to say so.
        }

        return tidy;
    }

    /// <summary>
    /// A path as a comparison can use it: no trailing separator, since the launchers write
    /// one and a folder picker does not, and the two must not read as different folders.
    /// </summary>
    public static string Tidy(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        string trimmed = path.Trim();

        return trimmed.Length > 3
            ? trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : trimmed;
    }
}
