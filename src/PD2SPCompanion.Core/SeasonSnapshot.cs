using System.Security.Cryptography;
using System.Text.Json;
using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Storage;

namespace PD2SPCompanion.Core;

/// <summary>
/// A season's game data kept outside the game, as it was played. A PD2 patch rewrites the archives in place: the season before it - its affix rows,
/// uniques, runewords, ranges - is gone from the install the moment it lands. Only its stat encoding
/// survives, as the next season's previous-season columns, and only for one season.
/// <para>
/// So the editor keeps every file the table install read (<see cref="GameFiles.GameData.ServedPaths"/>)
/// and every Excel table the archives list (<see cref="GameFiles.GameData.ListedTables"/>) - a table
/// read later, on demand, is kept too - in <c>Data/Seasons/&lt;season&gt;</c>, the first time it sees a
/// season and again whenever the build changes. <c>Season.Open(gameRoot, folder)</c> then reads that
/// season back exactly: a loose folder wins over the archives, and nothing is missing from it to fall
/// back on.
/// </para>
/// <para>
/// Never shipped, never shared: it is the player's own install, kept on their own machine.
/// </para>
/// </summary>
public static class SeasonSnapshot
{
    public const string ManifestName = "season.json";

    /// <summary>One file kept: its path in the archives, its size and its SHA-256.</summary>
    public sealed record KeptFile(string Path, long Size, string Sha256);

    /// <summary>
    /// What a snapshot is of.
    /// </summary>
    /// <param name="Season">The season's name, as the vault names it ("S13").</param>
    /// <param name="Build">The build's fingerprint (<see cref="PatchGuard.Fingerprint"/>).</param>
    /// <param name="ModVersion">The mod version the launcher reported ("12.0.6b"), or empty.</param>
    /// <param name="PreviousSeason">The previous-season columns the tables carried ("S12"), or empty.</param>
    /// <param name="ItemVersion">The item version stamp the season writes ("103"), once a character of it was read.</param>
    /// <param name="TakenAt">When it was taken.</param>
    /// <param name="Files">Every file kept.</param>
    public sealed record Manifest(
        string Season, string Build, string ModVersion, string PreviousSeason, string? ItemVersion,
        DateTimeOffset TakenAt, IReadOnlyList<KeptFile> Files);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Where a season's snapshot is kept.</summary>
    public static string FolderFor(string season) => Path.Combine(EditorData.Seasons, season);

    /// <summary>The manifest of the snapshot in <paramref name="folder"/>, or null where there is none to read.</summary>
    public static Manifest? Read(string folder)
    {
        try
        {
            string path = Path.Combine(folder, ManifestName);
            return File.Exists(path) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception problem)
        {
            // A manifest that will not read is one there is not: a season still opens without it.
            Log.Error("season", $"Snapshot manifest in {folder} not read", problem);
            return null;
        }
    }

    /// <summary>
    /// Keeps <paramref name="season"/>'s files in <paramref name="folder"/>, replacing what was there.
    /// Written beside it first and swapped in, so a snapshot cut short never stands in for a whole one.
    /// Call it right after the season is opened: what was served then is what the tables were read from.
    /// </summary>
    public static Manifest Take(Season season, string name, string folder, string? itemVersion = null)
    {
        string taking = folder + ".taking";
        if (Directory.Exists(taking))
            Directory.Delete(taking, recursive: true);
        Directory.CreateDirectory(taking);

        var kept = new List<KeptFile>();
        var paths = new SortedSet<string>(season.Game.ServedPaths, StringComparer.OrdinalIgnoreCase);
        paths.UnionWith(season.Game.ListedTables());

        foreach (string path in paths)
        {
            if (season.Game.Read(path) is not { } hit)
                continue;

            // An archive path is relative and backslashed; anything else would write outside the folder.
            string relative = path.Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(taking, relative));
            if (!target.StartsWith(Path.GetFullPath(taking), StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, hit.Data);
            kept.Add(new KeptFile(path, hit.Data.Length, Convert.ToHexString(SHA256.HashData(hit.Data))));
        }

        var manifest = new Manifest(name, season.Build, season.Name, D2SLib.Model.TXT.StatEncoding.PreviousSeason,
                                    itemVersion, DateTimeOffset.UtcNow, kept);
        File.WriteAllText(Path.Combine(taking, ManifestName), JsonSerializer.Serialize(manifest, Json));

        // The build it replaces: its encoding kept beside it where the patch changed it, with those kept before.
        if (KeepEarlierBuilds(folder, taking, Read(folder), kept, season.Build))
            Log.Info("season", $"Snapshot of {name}: the encoding of build {Read(folder)?.Build} kept, the patch changed it");

        string replaced = folder + ".replaced";
        if (Directory.Exists(replaced))
            Directory.Delete(replaced, recursive: true);
        if (Directory.Exists(folder))
            Directory.Move(folder, replaced);
        Directory.Move(taking, folder);
        if (Directory.Exists(replaced))
            Directory.Delete(replaced, recursive: true);

        Log.Info("season", $"Snapshot of {name} (build {season.Build}): {kept.Count} files, "
                           + $"{kept.Sum(file => file.Size) / 1024} KB, in {folder}");
        return manifest;
    }

    /// <summary>
    /// The tables the save parser reads an item's bits by (<see cref="SaveTables.Load"/>): a build's encoding. A patch that
    /// changes one of them changes how the items written before it are read.
    /// </summary>
    public static readonly string[] EncodingTables = ["itemstatcost", "armor", "weapons", "misc", "uniqueitems", "setitems", "runes"];

    /// <summary>
    /// The tables an item's legitimacy is judged by that a patch has changed within a season: an item made before the patch
    /// keeps what it was given then. cubemain's corruptions: S14's second patch re-dealt three of them.
    /// </summary>
    public static readonly string[] RuleTables = ["cubemain", "magicprefix", "magicsuffix"];

    /// <summary>Whether a kept file is one of the <see cref="EncodingTables"/>: what an item's bits are read by.</summary>
    public static bool IsEncodingFile(string path) =>
        EncodingTables.Any(table => path.Equals(GameFiles.GameData.ExcelPath(table), StringComparison.OrdinalIgnoreCase));

    /// <summary>The folder in a season's snapshot that holds the encoding and rules of each earlier build of it, a folder each.</summary>
    public const string BuildsFolder = "builds";

    /// <summary>
    /// Keeps, in a snapshot being taken (<paramref name="taking"/>), the encoding and the rules of the build it replaces:
    /// PD2 changes a field's width at a season's start and ships the old widths beside the new
    /// (<c>Save Bits S13</c>), but a patch within a season that changed one would leave the items written before it
    /// readable by nothing - the snapshot of the season was replaced by the new build's, and with it the only record of
    /// the old widths. So where <paramref name="earlier"/> is another build and any of its <see cref="EncodingTables"/>
    /// differs from what is taken now (<paramref name="now"/>), those tables are copied into
    /// <c>builds/&lt;its build&gt;</c> under its own manifest - a folder <c>Season.Open</c> reads first, the rest coming
    /// from the install. The builds kept before go along. Answers whether the replaced build's was kept.
    /// </summary>
    public static bool KeepEarlierBuilds(string folder, string taking, Manifest? earlier, IReadOnlyList<KeptFile> now, string build)
    {
        string before = Path.Combine(folder, BuildsFolder);
        if (Directory.Exists(before))
            CopyTree(before, Path.Combine(taking, BuildsFolder));

        if (earlier is null || earlier.Build == build)
            return false;

        // The rule tables too (RuleTables): a patch that changed what an item may hold leaves the items made before it
        // judged by tables no longer installed.
        static bool IsEncoding(KeptFile file) =>
            EncodingTables.Concat(RuleTables).Any(table => file.Path.Equals(GameFiles.GameData.ExcelPath(table), StringComparison.OrdinalIgnoreCase));

        var then = earlier.Files.Where(IsEncoding).ToList();
        var shaNow = now.Where(IsEncoding).ToDictionary(file => file.Path, file => file.Sha256, StringComparer.OrdinalIgnoreCase);
        if (then.Count == 0 || then.All(file => shaNow.TryGetValue(file.Path, out string? sha) && sha == file.Sha256))
            return false;

        string into = Path.Combine(taking, BuildsFolder, earlier.Build);
        foreach (var file in then)
        {
            string relative = file.Path.Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            string from = Path.Combine(folder, relative), to = Path.Combine(into, relative);
            if (!File.Exists(from))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
        }

        File.WriteAllText(Path.Combine(into, ManifestName), JsonSerializer.Serialize(earlier with { Files = then }, Json));
        return true;
    }

    /// <summary>
    /// The corruptions and the affix tables of the installed season's earlier builds put beside its own
    /// (<see cref="Items.Corruptions.LoadEarlier"/>, <see cref="Items.MagicAffixes.LoadEarlier"/>): each kept build's,
    /// newest first, the installed build's own left out. None over loose tables, which are no install of a season.
    /// </summary>
    public static void ApplyEarlierRules(Season season)
    {
        if (season.LooseTables is not null)
        {
            Items.Corruptions.LoadEarlier([]);
            Items.MagicAffixes.LoadEarlier([]);
            return;
        }

        string Kept(string folder, string table) =>
            Path.Combine(folder, GameFiles.GameData.ExcelPath(table).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
        string[]? Lines(string file) => File.Exists(file) ? File.ReadAllLines(file, System.Text.Encoding.Latin1) : null;

        var builds = EarlierBuilds(NameFor(season)).Where(each => each.Manifest.Build != season.Build).Reverse().ToList();
        Items.Corruptions.LoadEarlier(builds.Select(each => Lines(Kept(each.Folder, "cubemain"))).OfType<string[]>());

        // And their affix tables, judged by and never rolled by (MagicAffixes.Judging).
        Items.MagicAffixes.LoadEarlier(builds
            .Select(each => (Prefixes: Lines(Kept(each.Folder, "magicprefix")), Suffixes: Lines(Kept(each.Folder, "magicsuffix"))))
            .Where(each => each.Prefixes is not null && each.Suffixes is not null)
            .Select(each => (each.Prefixes!, each.Suffixes!)));
    }

    /// <summary>The earlier builds of a season whose encoding or rules its snapshot keeps (<see cref="KeepEarlierBuilds"/>), oldest first.</summary>
    public static IReadOnlyList<(Manifest Manifest, string Folder)> EarlierBuilds(string season)
    {
        string builds = Path.Combine(FolderFor(season), BuildsFolder);
        if (!Directory.Exists(builds))
            return [];

        return [.. Directory.GetDirectories(builds)
            .Select(folder => (Manifest: Read(folder), Folder: folder))
            .Where(each => each.Manifest is not null)
            .Select(each => (each.Manifest!, each.Folder))
            .OrderBy(each => each.Item1.TakenAt)];
    }

    private static void CopyTree(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// Makes sure the installed season has a snapshot of its build: taken the first time a season is
    /// seen, and again when its build changes. A season the editor cannot name is kept under its build
    /// (<see cref="NameFor"/>) - that is when its tables matter most. Nothing for a season opened over
    /// loose tables: it is not the install. Returns the manifest, or null where none was kept.
    /// </summary>
    public static Manifest? EnsureFor(Season season)
    {
        if (season.LooseTables is not null)
            return null;

        string name = NameFor(season);

        string folder = FolderFor(name);
        if (Read(folder) is { } kept && kept.Build == season.Build)
            return kept;

        var taken = Take(season, name, folder, Read(folder)?.ItemVersion);

        // A new season's tables kept: the oldest go, past the few a migration can reach back to.
        try
        {
            Prune(EditorData.Seasons, EditorData.Vaults);
        }
        catch (Exception problem)
        {
            Log.Error("season", "Old snapshots not thinned", problem);
        }

        return taken;
    }

    /// <summary>How many seasons' snapshots are kept: the installed one and the two before it.</summary>
    public const int Kept = 3;

    /// <summary>
    /// Holds the snapshots to the newest <paramref name="keep"/>, by when each was taken (a season's tables are some 5 MB, and every season's stood for
    /// ever). A season that still has a vault of its own under <paramref name="vaults"/> stays whatever its age:
    /// its tables are what that vault is migrated by. A folder with no manifest is not a snapshot, and is left
    /// alone. Returns how many went.
    /// </summary>
    public static int Prune(string seasons, string vaults, int keep = Kept)
    {
        var root = new DirectoryInfo(seasons);
        if (!root.Exists)
            return 0;

        var snapshots = root.GetDirectories()
            .Select(folder => (Folder: folder, Manifest: Read(folder.FullName)))
            .Where(each => each.Manifest is not null)
            .OrderByDescending(each => each.Manifest!.TakenAt)
            .ToList();

        int removed = 0;
        foreach (var (folder, _) in snapshots.Skip(Math.Max(1, keep)))
        {
            var ofSeason = new DirectoryInfo(Path.Combine(vaults, folder.Name));
            if (ofSeason.Exists && ofSeason.EnumerateFiles("*.pd2lib", SearchOption.AllDirectories).Any())
                continue;

            try
            {
                folder.Delete(recursive: true);
                removed++;
                Log.Info("season", $"Snapshot of {folder.Name} removed: {keep} newer are kept, and no vault of its season is left");
            }
            catch (Exception problem)
            {
                Log.Error("season", $"Snapshot of {folder.Name} not removed", problem);
            }
        }

        return removed;
    }

    /// <summary>
    /// The item stamp each season writes, as read from the game's own saves: S13 103, S14 105. Shipped, because a
    /// player who starts at this season has no snapshot of the last one and never will, and a character of theirs
    /// holds that season's items all the same. A new season goes here at its rollover (SeasonFacts).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> KnownStamps =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["S13"] = "103", ["S14"] = "105" };

    /// <summary>
    /// The columns of charstats.txt a character's base life, mana and stamina come from, for the season before, by class
    /// id. Shipped as <see cref="KnownStamps"/> is, and for the same player: with no snapshot of the last season, its
    /// characters' base had nothing to move from, and they stayed offered for good. A snapshot wins where there is one. S13's, read from its charstats.txt. A new season goes here at its
    /// rollover (SeasonFacts).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<Characters.NewCharacter.ClassVitals>> KnownClassValues =
        new Dictionary<string, IReadOnlyList<Characters.NewCharacter.ClassVitals>>(StringComparer.OrdinalIgnoreCase)
        {
            ["S13"] =
            [
                //  Vitality, Energy, Stamina, HpAdd, Life/level, Stamina/level, Mana/level, Life/vitality, Stamina/vitality, Mana/energy
                new(20, 15, 184, 30, 8, 4, 8, 12, 4, 6), // Amazon
                new(10, 35, 149, 30, 4, 4, 8, 8, 4, 8), // Sorceress
                new(15, 25, 154, 30, 6, 4, 8, 8, 4, 8), // Necromancer
                new(25, 15, 189, 30, 8, 4, 8, 12, 4, 6), // Paladin
                new(25, 10, 217, 30, 8, 4, 6, 16, 4, 4), // Barbarian
                new(25, 20, 159, 30, 6, 4, 8, 8, 4, 8), // Druid
                new(20, 25, 220, 30, 8, 5, 8, 12, 5, 7), // Assassin
            ],
        };

    /// <summary>
    /// Tells the save reader the item stamps <paramref name="season"/> and the season before it write: the known ones,
    /// else from their snapshots, where those were kept and a character of the season was read. Unknown
    /// stamps leave the reader to plausibility alone.
    /// </summary>
    public static void ApplyStamps(Season season)
    {
#if DEBUG
        // A new season's first launch, its stamp not yet in the table, for the UI case that can only be met there: with
        // the stamps known no file reads as last season's, and the guard on one never speaks (LastSeasonGuard).
        if (string.Equals(Environment.GetEnvironmentVariable("PD2SPC_STAMPS"), "off", StringComparison.OrdinalIgnoreCase))
        {
            Saves.SeasonReading.SetStamps(null, null);
            return;
        }
#endif
        string previous = D2SLib.Model.TXT.StatEncoding.PreviousSeason;
        Saves.SeasonReading.SetStamps(StampOf(NameFor(season)), previous.Length > 0 ? StampOf(previous) : null);
    }

    private static string? StampOf(string season) =>
        KnownStamps.TryGetValue(season, out string? known) ? known : Read(FolderFor(season))?.ItemVersion;

    /// <summary>What a snapshot of <paramref name="season"/> is kept as: its season, or "unknown-" and its build.</summary>
    public static string NameFor(Season season) =>
        Vault.TryInstalledSeason(out string name, out _) ? name : $"unknown-{season.Build}";

    /// <summary>
    /// A known season's stamp, or one older: never a later season's. 1.0 may still be the build played when S15 comes,
    /// with a character of S13's items opened first.
    /// </summary>
    public static bool IsOfAnEarlierSeason(string itemVersion) =>
        int.TryParse(itemVersion, out int stamp)
        && KnownStamps.Values.Any(known => int.TryParse(known, out int each) && each >= stamp);

    /// <summary>
    /// Writes down the item version stamp a season's characters carry, the first time one is read: no
    /// table says it, and a migration into a season has to stamp what that season writes.
    /// </summary>
    public static void NoteItemVersion(string season, string itemVersion)
    {
        // Known already, and a first character holding last season's item must not teach otherwise.
        if (KnownStamps.ContainsKey(season))
            return;

        if (IsOfAnEarlierSeason(itemVersion))
            return;

        string folder = FolderFor(season);
        if (Read(folder) is not { } manifest || manifest.ItemVersion == itemVersion)
            return;

        // The first one seen stands: a later different stamp is a file from another era, not news.
        if (manifest.ItemVersion is not null)
            return;

        File.WriteAllText(Path.Combine(folder, ManifestName),
                          JsonSerializer.Serialize(manifest with { ItemVersion = itemVersion }, Json));

        if (Season.Current is { } installed && NameFor(installed) == season)
            ApplyStamps(installed);
    }
}
