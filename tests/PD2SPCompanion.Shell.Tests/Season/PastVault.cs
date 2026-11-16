using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Core.Saves;

namespace PD2SPCompanion.Shell.Tests.Season;

/// <summary>
/// An older season's vault beside the run's own, as the command line tool's <c>pastvault</c> makes one for the
/// UI scripts: the first non-compact items of a shared stash, read under that season's own tables, in a softcore
/// vault of that season - half in a folder - with some gold. Made in the program's own process: the old season's
/// tables are opened for the reading and the program's own installed again after, as a migration itself does.
/// </summary>
internal static class PastVault
{
    /// <summary>The S13 pad's stash (the pad before <c>pd2spc padseason</c> brought it into S14), and S13's own tables.</summary>
    private static string S13Stash => Path.Combine(Program.Corpus, "ui-pad", "s13", "pad", "saves", "pd2_shared.stash");

    private static string S13Tables => TestPaths.S13Tables;

    private static string S7Pack => Path.Combine(Program.Corpus, "packs", "betweenwalls-s7", "PD2-Singleplayer-main", "Diablo II");

    /// <summary>Whether the corpus has what the old seasons are read from.</summary>
    public static bool Available => File.Exists(S13Stash) && Directory.Exists(S13Tables) && Directory.Exists(S7Pack);

    /// <summary>
    /// The S13 vault made here: one of its own name, as the pad's own "S13 Main" was migrated when the pad was brought
    /// into S14 - a season's vaults are migrated one by one, and this one has not been.
    /// </summary>
    public const string S13Name = "S13 Second";

    /// <summary>Season 13's, forty items: one season back, read through the installed tables' previous-season columns.</summary>
    public static int S13(Program program) => Make(program, "S13", S13Tables, S13Stash, 40, S13Name);

    /// <summary>Season 7's, twenty items: seasons back, with no snapshot of its tables in the run's data.</summary>
    public static int S7(Program program) =>
        Make(program, "S7", Path.Combine(S7Pack, "ProjectD2", "data", "global", "excel", "modpacks", "official"),
             Path.Combine(S7Pack, "Save", "Library", "_LOD_SharedStashSave.sss"), 20, null);

    /// <summary>Asked on the program's thread. Returns how many items the vault holds - the season's first, or <paramref name="name"/>.</summary>
    private static int Make(Program program, string season, string tables, string file, int take, string? name)
    {
        var session = program.Main.Session;

        int stored = 0;

        // Reinstall sets the item stamps from the season's snapshot, and a season on laid-over tables (PD2SPC_TABLES)
        // keeps none: what the open character told is put back with the season.
        var stamps = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        try
        {
            using var old = Core.Season.Open(Program.GameRoot, tables);
            var items = file.EndsWith(".stash", StringComparison.OrdinalIgnoreCase)
                ? SharedStash.Read(file).Items.Items ?? []
                : (SaveVerifier.LoadStash(file).Stash.PageList ?? []).SelectMany(page => page.PageItems?.Items ?? []).ToList();

            using var vault = name is null ? VaultLibrary.OpenVault(season, false, 10) : VaultLibrary.OpenVault(season, false, name, 10);
            long folder = vault.CreateFolder(null, "Sample");
            foreach (var item in items.Where(item => !item.IsCompact).Take(take))
            {
                if (vault.Add(item, stored % 2 == 0 ? null : folder, out _) is not null)
                    stored++;
            }

            vault.RecordGold([new GoldMove("stash", 1000)]);
        }
        finally
        {
            // The program's own season back, and the parser in the open character's layout.
            session.Season!.Reinstall();
            if (SeasonReading.CurrentStamp is null)
                SeasonReading.SetStamps(stamps.CurrentStamp, stamps.PreviousStamp);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (session.IsLoaded)
                session.UseMyLayout();
        }

        return stored;
    }
}
