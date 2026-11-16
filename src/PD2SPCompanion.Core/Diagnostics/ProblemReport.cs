using System.IO.Compression;

namespace PD2SPCompanion.Core.Diagnostics;

/// <summary>
/// What a bug report needs, in one file: the logs, the
/// settings and a page saying what was running, zipped into <c>Data\Reports</c> for the player to attach
/// wherever the problem is reported. <b>Nothing is sent anywhere</b>: there is nowhere of ours to send it, and
/// a player's files do not leave the machine unseen. A character's files and a copy of the vault go in only
/// where the player says so - most problems with an item cannot be found without the save it is in.
/// <para>
/// A report is itself something kept: the newest <see cref="Kept"/> stay, the rest go as one is made.
/// </para>
/// </summary>
public static class ProblemReport
{
    /// <summary>How many reports are kept in the folder.</summary>
    public const int Kept = 3;

    /// <summary>The page inside the zip that says what was running.</summary>
    public const string AboutName = "about.txt";

    /// <summary>
    /// Writes the report into <paramref name="folder"/> (the data folder's Reports) and returns its path.
    /// <paramref name="files"/>: each file to put in, and the name it has inside - one that is not there, or
    /// cannot be read just now, is left out and named on the about page.
    /// </summary>
    public static string Write(string folder, string about, IEnumerable<(string Source, string Entry)> files, DateTime now)
    {
        Directory.CreateDirectory(folder);

        string stem = Path.Combine(folder, $"pd2spc-report-{now:yyyyMMdd-HHmmss}");
        string path = stem + ".zip";
        for (int n = 2; File.Exists(path); n++)
            path = $"{stem}-{n}.zip";

        var missing = new List<string>();
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (source, entry) in files)
            {
                try
                {
                    if (!File.Exists(source))
                        continue;

                    // Read shared: the log is being written, a save may be the game's this moment.
                    using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var to = zip.CreateEntry(entry.Replace('\\', '/'), CompressionLevel.Optimal).Open();
                    from.CopyTo(to);
                }
                catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
                {
                    missing.Add($"{entry}: {problem.Message}");
                }
            }

            using var page = new StreamWriter(zip.CreateEntry(AboutName, CompressionLevel.Optimal).Open());
            page.Write(about.TrimEnd());
            page.WriteLine();
            foreach (string line in missing)
                page.WriteLine($"Not included - {line}");
        }

        Prune(folder);
        return path;
    }

    /// <summary>The logs as a report takes them: every log of a folder - the data folder's Logs - under "Logs".</summary>
    public static IEnumerable<(string Source, string Entry)> Logs(string logs)
    {
        var folder = new DirectoryInfo(logs);
        return folder.Exists
            ? folder.GetFiles("*.log").Select(file => (file.FullName, "Logs/" + file.Name))
            : [];
    }

    /// <summary>Holds the folder to its newest <see cref="Kept"/> reports. Returns how many went.</summary>
    public static int Prune(string folder)
    {
        var reports = new DirectoryInfo(folder);
        if (!reports.Exists)
            return 0;

        int removed = 0;
        foreach (var old in reports.GetFiles("pd2spc-report-*.zip").OrderByDescending(file => file.LastWriteTimeUtc).ThenByDescending(file => file.Name).Skip(Kept))
        {
            try
            {
                old.Delete();
                removed++;
            }
            catch (Exception)
            {
                // One that will not delete is left be.
            }
        }

        return removed;
    }
}
