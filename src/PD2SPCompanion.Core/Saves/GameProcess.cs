using System.Diagnostics;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// Whether the game is running from a game folder, asked of the processes: the one test under every refusal to write
/// the game's files - the editor's (GameSync) and the command line's (pd2spc).
/// </summary>
public static class GameProcess
{
    /// <summary>
    /// Whether a <c>Game.exe</c> from this game folder is running - PD2's <c>ProjectD2\Game.exe</c>
    /// or the plain one beside it. One whose path cannot be read counts: the name is the game's,
    /// and a wrong yes only makes Save wait.
    /// </summary>
    public static bool IsRunningFrom(string gameRoot) => Starts(gameRoot, when: false).Count > 0;

    /// <summary>
    /// The games running from this folder: one entry each, its start (UTC) where <paramref name="when"/> asks for it
    /// and it can be read, else null. A process whose path cannot be read counts, and so does a listing that fails
    /// - a wrong yes only makes Save wait.
    /// </summary>
    public static List<DateTime?> Starts(string gameRoot, bool when)
    {
        // For tests, which never start the game: while the file PD2SPC_FAKE_GAME names exists, the
        // game counts as running, started when the file was written - as PD2SPC_VAULTS points the vaults at a copy.
        if (Environment.GetEnvironmentVariable("PD2SPC_FAKE_GAME") is { Length: > 0 } fake)
            return File.Exists(fake) ? [File.GetLastWriteTimeUtc(fake)] : [];

        Process[] found;
        try
        {
            found = Process.GetProcessesByName("Game");
        }
        catch (Exception)
        {
            return [null];
        }

        var starts = new List<DateTime?>();
        foreach (var process in found)
        {
            using (process)
            {
                string? file;
                try
                {
                    file = process.MainModule?.FileName;
                }
                catch (Exception)
                {
                    starts.Add(null);
                    continue;
                }

                if (file is null || gameRoot.Length == 0
                    || Path.GetFullPath(file).StartsWith(Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase))
                    starts.Add(when ? StartOf(process) : null);
            }
        }

        return starts;

        static DateTime? StartOf(Process process)
        {
            try
            {
                return process.StartTime.ToUniversalTime();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
