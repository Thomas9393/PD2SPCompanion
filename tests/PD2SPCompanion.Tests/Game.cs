namespace PD2SPCompanion.Tests;

/// <summary>
/// The installed game's tables, opened once for every test that needs them. The rules read PD2's
/// tables live and never bundle them, so these tests need an install, and the corpus of real saves (TestCorpus):
/// <see cref="TestPaths"/> says where. Without the game <see cref="Available"/> is false;
/// <see cref="TestKitTests"/> is the one that fails and says so (PD2SPC_NO_GAME=1 where that is meant).
/// <para>
/// PD2SPC_TABLES lays a folder of tables over the archives, as the CLI's <c>--tables</c> does: a season's
/// snapshot (<c>Data\Seasons\S13</c>) puts the season the corpus and its in-game specimens were taken in
/// back under them while another is installed (the S14 open beta).
/// </para>
/// </summary>
public sealed class Game : IDisposable
{
    public static readonly string Root = TestPaths.Game;

    public static readonly string? Tables =
        Environment.GetEnvironmentVariable("PD2SPC_TABLES") is { Length: > 0 } tables ? tables : null;

    public static readonly string Corpus = TestPaths.Corpus;

    private readonly GameData? _game;

    public bool Available => _game is not null;

    public Game()
    {
        if (!GameLocation.Holds(Root))
            return;

        _game = GameData.Open(Root, Tables);
        SaveTables.Install(_game);

        // The stamps the app takes from its season snapshots (SeasonSnapshot.ApplyStamps), which live in the data
        // folder the tests keep off: unset, an item's widths were the file switch's - whatever the last file read left -
        // and a map forged as 105 wrote its Monster Rarity of 40 in S13's five bits, 8 (under S14's tables).
        UseStamps();

        // And the earlier builds the app keeps of its season (SeasonSnapshot.ApplyEarlierRules), from the corpus: the items
        // of the ladders and the trade site made before a patch keep what that build gave them.
        UseEarlierBuilds();
    }

    /// <summary>
    /// The kept earlier builds of the loaded season, from the corpus's <c>builds</c> folder (<c>S14-&lt;build&gt;</c>, a
    /// snapshot each), put beside its corruptions as the app puts its snapshot's (Corruptions.LoadEarlier), newest first.
    /// </summary>
    public static void UseEarlierBuilds()
    {
        PD2SPCompanion.Core.Items.Corruptions.LoadEarlier(EarlierCubemains());
        PD2SPCompanion.Core.Items.MagicAffixes.LoadEarlier(EarlierAffixTables());
    }

    /// <summary>Each kept earlier build's magicprefix and magicsuffix of the loaded season, newest first.</summary>
    public static IEnumerable<(string[] Prefixes, string[] Suffixes)> EarlierAffixTables() =>
        EarlierFolders()
            .Select(folder => (Prefixes: Path.Combine(folder, "data", "global", "excel", "magicprefix.txt"),
                               Suffixes: Path.Combine(folder, "data", "global", "excel", "magicsuffix.txt")))
            .Where(each => File.Exists(each.Prefixes) && File.Exists(each.Suffixes))
            .Select(each => (File.ReadAllLines(each.Prefixes, System.Text.Encoding.Latin1), File.ReadAllLines(each.Suffixes, System.Text.Encoding.Latin1)))
            .ToList();

    /// <summary>The corpus's kept earlier builds of the loaded season, newest first.</summary>
    private static IEnumerable<string> EarlierFolders()
    {
        string builds = Path.Combine(Corpus, "builds");
        if (!Directory.Exists(builds))
            return [];

        return Directory.GetDirectories(builds, Season + "-*")
            .OrderByDescending(folder => SeasonSnapshot.Read(folder)?.TakenAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>Each kept earlier build's cubemain of the loaded season, newest first.</summary>
    public static IEnumerable<string[]> EarlierCubemains() =>
        EarlierFolders()
            .Select(folder => Path.Combine(folder, "data", "global", "excel", "cubemain.txt"))
            .Where(File.Exists)
            .Select(file => File.ReadAllLines(file, System.Text.Encoding.Latin1))
            .ToList();

    /// <summary>
    /// The loaded season's stamps, put (back) on the reader: a test that opens a <see cref="Season"/> applies its
    /// snapshots' (SeasonSnapshot.ApplyStamps) - none in the tests' data folder - and leaves them unknown for the next.
    /// A test that writes an item standing alone, with no file to say its widths, asks for them first.
    /// </summary>
    public static void UseStamps() => SeasonReading.SetStamps(Stamp, PreviousStamp);

    /// <summary>The stamp the season before the loaded one wrote, where the tests know it.</summary>
    public static string? PreviousStamp => Season == "S14" ? "103" : null;

    public void Dispose() => _game?.Dispose();

    /// <summary>
    /// The season whose tables are loaded, told by the previous-season columns they carry (S14's carry S13's): S14
    /// installed, or S13 laid over it by PD2SPC_TABLES for the tests pinned to it (<see cref="Pinned"/>).
    /// </summary>
    public static string Season => D2SLib.Model.TXT.StatEncoding.PreviousSeason switch
    {
        "S13" => "S14",
        "S12" => "S13",
        var before => $"after {before}",
    };

    /// <summary>
    /// The stamp the loaded season writes on a new item - what a test forges with, so the item takes this season's
    /// field widths. Forged as 103 under S14's tables, an item was a legacy one: written by S13's widths, a map's
    /// Monster Rarity of 50-60 did not fit its five bits.
    /// </summary>
    public static string Stamp => Season == "S13" ? "103" : "105";

    /// <summary>
    /// A test of something measured in one season - its game pages, its specimens, its table's counts. Run only with that
    /// season's tables: <c>check.py</c> runs the S13 ones in a pass of their own, <c>PD2SPC_TABLES</c> at
    /// <c>Data\Seasons\S13</c> and <c>--filter Season=S13</c>; under another season's tables they say nothing.
    /// </summary>
    public bool Pinned(string season) => Available && Season == season;

    /// <summary>A real character of the corpus, copied to a scratch folder a test may change.</summary>
    public static string CopyOfCharacter(string scratch)
    {
        Directory.CreateDirectory(scratch);
        string from = Path.Combine(Corpus, "saves", "PhySpearZon.d2s");
        string to = Path.Combine(scratch, "PhySpearZon.d2s");
        File.Copy(from, to, overwrite: true);
        return to;
    }
}

/// <summary>
/// Every test runs off the player's data. The editor keeps its data where <c>PD2SPC_DATA</c> says (a Debug
/// build), and on the developer's machine that is the player's own folder - so a test that opened a vault of
/// its own in a temp folder still had the vault's copies written to the player's <c>Vaults\Backups</c>: the
/// backup folder hangs off the data folder, not off the vault's file. 45 files in an afternoon, from the
/// tests of a vault's upgrade and of its width. Set once, before any test, to a folder
/// of the run's own; a test that points <c>PD2SPC_VAULTS</c> at its own folder still does, and puts this back.
/// </summary>
internal static class OffLiveData
{
    internal static readonly string Root = Path.Combine(Path.GetTempPath(), "pd2spc-tests-data-" + Environment.ProcessId);

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Set()
    {
        Environment.SetEnvironmentVariable("PD2SPC_DATA", Root);
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", Path.Combine(Root, "Vaults"));
        KeepEarlierBuilds();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (Exception)
            {
                // A file still held as the run ends is left in the temp folder: nothing of the player's.
            }
        };
    }

    /// <summary>
    /// The run's data folder given the earlier builds the corpus keeps (<c>builds\S14-&lt;build&gt;</c>), where the app's
    /// season snapshot keeps them (<c>Seasons\S14\builds\&lt;build&gt;</c>): a test that opens a season reads them as the
    /// app does (SeasonSnapshot.ApplyEarlierRules), and items made before a patch keep what that build gave them.
    /// </summary>
    private static void KeepEarlierBuilds()
    {
        string kept = Path.Combine(TestPaths.Corpus, "builds");
        if (!Directory.Exists(kept))
            return;

        foreach (string folder in Directory.GetDirectories(kept))
        {
            string name = Path.GetFileName(folder);
            int dash = name.IndexOf('-');
            if (dash <= 0)
                continue;

            string into = Path.Combine(Root, "Seasons", name[..dash], "builds", name[(dash + 1)..]);
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(into, Path.GetRelativePath(folder, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
    }
}

/// <summary>Every test class using the tables shares one <see cref="Game"/>: they are global, so they run one at a time.</summary>
[CollectionDefinition(nameof(GameTables))]
public sealed class GameTables : ICollectionFixture<Game>;
