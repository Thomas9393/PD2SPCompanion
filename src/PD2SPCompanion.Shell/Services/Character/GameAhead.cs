using System;
using System.Threading.Tasks;

using PD2SPCompanion.Core;
using PD2SPCompanion.Core.Diagnostics;
using PD2SPCompanion.Core.GameFiles;
using PD2SPCompanion.Core.Localization;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// The game's tables begun on a worker at the program's first lines, read beside everything a start does
/// before it needs them - the framework's own start, the app's styles, the splash made and shown - where they
/// were read after the pages were built, with the window waiting: the loading sped up,
/// not just shifted to the front.
/// <para>
/// The tables fill their static classes on the worker and <b>nobody is told</b> (Season.Open, announce off):
/// <see cref="CharacterSession.EnsureGame"/> takes the season and says it on the UI thread, at the step it was
/// always said at, to the two that listen (the logo's art, the season banner). Everything else asks
/// InstalledTables.Generation, which has moved by then.
/// </para>
/// <para>
/// <b>Nothing of the tables may be read before <see cref="Done"/> is done</b>: start-up waits for it before it
/// builds the pages. Until then the UI thread makes windows and reads XAML, which ask no table. The language
/// is put in use here first - a table read on the worker words what it names (L) as the pages will.
/// </para>
/// </summary>
public static class GameAhead
{
    private static Task<Season>? _task;
    private static string _root = "";

    /// <summary>
    /// Begins it, where the settings name a folder that holds the game; a first run, with none, reads the
    /// tables when its setup has settled one. PD2SPC_AHEAD=0: not begun - read behind the splash as they
    /// were, to measure the one against the other.
    /// </summary>
    public static void Begin()
    {
        if (_task is not null || Environment.GetEnvironmentVariable("PD2SPC_AHEAD") == "0")
            return;

        try
        {
            string root = GameLocation.Tidy(EditorSettings.StoredGameFolder());
            if (!GameLocation.Holds(root))
                return;

            Language.Use(EditorSettings.StoredLanguage());
            _root = root;
            _task = Task.Run(() =>
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                var season = Season.Open(root, announce: false);
                try
                {
                    // Kept outside the game the first time a season is seen, as an open made by the session keeps it.
                    SeasonSnapshot.EnsureFor(season);
                }
                catch (Exception problem)
                {
                    Log.Caught(problem);
                }

                Log.Info("Start", $"Reading the game files, ahead: {timer.ElapsedMilliseconds} ms (allocated {(GC.GetAllocatedBytesForCurrentThread() - allocated) / (1024 * 1024)} MB)");

                // And what the grail is made of, which is the tables' alone and kept by their install's number
                // (GrailCatalog, AffixRanges.NeverApart): made here, the splash's grail step only finds them.
                timer.Restart();
                allocated = GC.GetAllocatedBytesForCurrentThread();
                try
                {
                    PD2SPCompanion.Core.Collection.GrailCatalog.Uniques();
                    PD2SPCompanion.Core.Collection.GrailCatalog.Sets();
                    PD2SPCompanion.Core.Collection.GrailCatalog.RunewordEntries();
                    PD2SPCompanion.Core.Items.AffixRanges.NeverApart();
                }
                catch (Exception problem)
                {
                    // Made behind the splash, then, where it is said.
                    Log.Caught(problem);
                }

                Log.Info("Start", $"Preparing the grail, ahead: {timer.ElapsedMilliseconds} ms (allocated {(GC.GetAllocatedBytesForCurrentThread() - allocated) / (1024 * 1024)} MB)");
                return season;
            });
        }
        catch (Exception problem)
        {
            // Read where they always were, then: behind the splash.
            Log.Caught(problem);
        }
    }

    /// <summary>Done when the open begun is - or has failed, which whoever takes it is told; done at once where none was begun.</summary>
    public static Task Done => _task is { } task
        ? task.ContinueWith(static _ => { }, TaskScheduler.Default)
        : Task.CompletedTask;

    /// <summary>
    /// The season begun, handed over once - waited for where it is not done, and what its open threw thrown
    /// here. Null where none was begun, or it was taken. One begun for another folder than
    /// <paramref name="root"/> is let go: the caller opens its own.
    /// </summary>
    public static Season? Take(string root)
    {
        if (_task is not { } task)
            return null;

        _task = null;
        var season = task.GetAwaiter().GetResult();
        if (string.Equals(root, _root, StringComparison.OrdinalIgnoreCase))
            return season;

        season.Dispose();
        return null;
    }
}
