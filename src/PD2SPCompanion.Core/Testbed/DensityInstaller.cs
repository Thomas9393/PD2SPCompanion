using System.Text;
using System.Text.Json;

namespace PD2SPCompanion.Core.Testbed;

/// <summary>What is installed, if anything, and which choices produced it.</summary>
/// <param name="Version">
/// What PD2 called itself when these files were written, for showing. Empty where no
/// launcher reported one, or where the marker predates this being recorded.
/// </param>
/// <param name="Archive">
/// The fingerprint of the archive they were built from, for comparing. An installed
/// override is a snapshot of a table, so once this stops matching the installed game the
/// files are last season's and the game is reading them in preference to its own.
/// </param>
public sealed record DensityState(
    bool Installed,
    double Multiplier,
    bool CowPortal,
    string Version,
    string Archive,
    IReadOnlyList<string> Files,
    DensityScope Scope = DensityScope.All,
    int Packs = 1,
    DropChoice? Drops = null);

/// <summary>
/// The drop settings (DropOverride): every kill drops, and the drop panel's multiples by entry (DropBoosts).
/// </summary>
public sealed record DropChoice(bool EveryKill = false, IReadOnlyDictionary<string, int>? Boosts = null)
{
    public bool Any => EveryKill || (Boosts?.Values.Any(boost => boost > 1) ?? false);

    /// <summary>
    /// The panel's multiples from a marker written before it: one high rune multiple, which was each of Lem to Zod
    /// at that multiple - the same file, so the same choice.
    /// </summary>
    public static IReadOnlyDictionary<string, int> FromHighRunes(int highRunes) =>
        highRunes <= 1
            ? new Dictionary<string, int>()
            : Enumerable.Range(DropOverride.FirstHighRune, DropOverride.LastRune - DropOverride.FirstHighRune + 1).ToDictionary(rune => $"r{rune}", _ => highRunes);

    /// <summary>
    /// The panel's multiples from a marker written before it: <see cref="FromHighRunes"/>, and the material box's
    /// multiple on every material the panel boosts, which that box reached then (the box is retired).
    /// </summary>
    public static IReadOnlyDictionary<string, int> FromOld(int highRunes, int materials)
    {
        var boosts = new Dictionary<string, int>(FromHighRunes(highRunes), StringComparer.OrdinalIgnoreCase);
        if (materials > 1)
        {
            foreach (string key in DropBoosts.MaterialKeys)
                boosts[key] = materials;
        }
        return boosts;
    }
}

/// <summary>
/// Puts the loose table overrides into the game install, and takes them out again.
/// <para>
/// <b>This is the one place the editor writes to the game install, and it is a deliberate
/// exception to a standing rule.</b> The rule, as <c>EditorSettings</c> says it: the install is read-only
/// to this program apart from the saves themselves. Named here, because an undocumented exception to a
/// hard rule is how the rule dies.
/// </para>
/// <para>
/// The terms it keeps, which are what make it acceptable:
/// </para>
/// <list type="bullet">
/// <item>Every write is asked for by a button that says what it will do.</item>
/// <item><b>It only ever creates.</b> No existing install file is opened for writing; a
/// table that already exists and is not ours is refused and reported. (The game's own compiled
/// <c>.bin</c> of a table is not a table of another's: see <see cref="Blocked"/>.)</item>
/// <item>A marker records exactly what was created, and teardown removes only what the
/// marker lists - never a recursive delete.</item>
/// <item>Directories go only while empty, innermost first, and never
/// <c>ProjectD2\</c> itself.</item>
/// <item>The stock launcher is untouched: it passes no <c>-direct -txt</c>, so the override
/// is inert unless the game is started by us.</item>
/// </list>
/// <para>
/// The posture is <c>SaveWriter</c>'s: validate every step first, and abort before touching
/// anything if any of it fails.
/// </para>
/// </summary>
public static class DensityInstaller
{
    /// <summary>Where a loose table has to sit for <c>-direct</c> to find it.</summary>
    public const string OverrideFolder = @"ProjectD2\data\global\excel";

    /// <summary>
    /// What we created, so teardown never has to guess.
    /// <para>
    /// Still named for density although it now records more than that, because renaming it
    /// would orphan an override already installed under the old name - the marker is the
    /// only record of what to remove, so it is the one name here that cannot change.
    /// </para>
    /// </summary>
    public const string MarkerName = "pd2spc-density.json";

    public static string FolderIn(string gameRoot) => Path.Combine(gameRoot, OverrideFolder);

    public static string MarkerIn(string gameRoot) =>
        Path.Combine(gameRoot, @"ProjectD2\data", MarkerName);

    /// <summary>Where the game itself lives, which is what has to be started for any of this to show.</summary>
    public static string GameIn(string gameRoot) => Path.Combine(gameRoot, @"ProjectD2\Game.exe");

    /// <summary>
    /// The launcher, which is not used to start anything here but carries PD2's own icon -
    /// Game.exe wears the plain Diablo II one, and a shortcut to a PD2 testbed that looks
    /// like vanilla is a shortcut nobody finds twice.
    /// </summary>
    public static string LauncherIn(string gameRoot) =>
        Path.Combine(gameRoot, @"ProjectD2\PD2Launcher.exe");

    /// <summary>What is installed right now, read from the marker rather than guessed.</summary>
    public static DensityState Current(string gameRoot)
    {
        try
        {
            string marker = MarkerIn(gameRoot);
            if (!File.Exists(marker))
                return Nothing;

            var stored = JsonSerializer.Deserialize<Marker>(File.ReadAllText(marker));
            if (stored is null)
                return Nothing;

            // A marker from before packs were their own setting wrote its MAX as 20: density at 5x
            // and packs at 4x.
            double multiplier = stored.Multiplier;
            int packs = stored.Packs;
            if (packs <= 0)
                (multiplier, packs) = multiplier > 5 && multiplier < DensityOverride.MaxDensity ? (5d, (int)Math.Round(multiplier / 5)) : (multiplier, 1);

            return new DensityState(
                true, multiplier, stored.CowPortal,
                stored.Version ?? "", stored.Archive ?? "",
                // Kept relative to the game folder, so a moved install still finds them; an older marker
                // holds whole paths, read as they are.
                [.. (stored.Files ?? []).Select(file => Path.IsPathRooted(file) ? file : Path.Combine(gameRoot, file))],
                stored.Scope, packs,
                new DropChoice(stored.EveryKill,
                    stored.Boosts is { Count: > 0 } boosts ? boosts : DropChoice.FromOld(stored.HighRunes, stored.Materials)));
        }
        catch (Exception)
        {
            // An unreadable marker is treated as none: teardown then reports and does
            // nothing, which is the safe way round.
            return Nothing;
        }
    }

    /// <summary>Nothing installed, which is also what an unreadable marker comes to.</summary>
    private static DensityState Nothing => new(false, 1, false, "", "", []);

    /// <summary>
    /// Why these tables cannot be written, or null: a table at one of their places that this editor did not put
    /// there. Asked before anything installed comes off (the page's Apply), so a refusal leaves the install
    /// that works where it is.
    /// <para>
    /// <b>The game's compiled form of a table, a <c>.bin</c> with no table beside it, does not stop it</b>.
    /// Started with <c>-direct -txt</c> the game compiles every table into this folder -
    /// ninety files, of which Remove takes those beside the tables it listed and leaves the rest. A table the
    /// editor had not written before then found the game's own <c>.bin</c> at its place and was refused: Every
    /// kill drops could not be turned on after one modded run. Such a file is not opened for writing here: the
    /// game compiles over it at its next start, and Remove takes it with the table, as it takes every listed
    /// table's. The cost, known and taken: a mod of another tool's that ships compiled tables alone would be
    /// compiled over. A table of another's is still refused.
    /// </para>
    /// </summary>
    public static string? Blocked(string gameRoot, IEnumerable<string> tables)
    {
        var ours = Current(gameRoot);
        foreach (string table in tables)
        {
            string path = Path.Combine(FolderIn(gameRoot), $"{table}.txt");
            if (ours.Files.Contains(path, StringComparer.OrdinalIgnoreCase))
                continue;

            if (File.Exists(path))
                return L($"{path} exists, not made here.");
        }

        return null;
    }

    /// <summary>
    /// Writes the override. Returns null when it went, or why it would not.
    /// <para>
    /// Checked first, written second. Anything already at a target path that we did not put
    /// there stops the whole thing - this feature has no business editing a file someone
    /// else installed.
    /// </para>
    /// </summary>
    public static string? Install(
        string gameRoot,
        double multiplier,
        bool cowPortal,
        GameBuild build,
        IReadOnlyList<ModdedFile> files,
        DensityScope scope = DensityScope.All,
        int packs = 1,
        DropChoice? drops = null)
    {
        string folder = FolderIn(gameRoot);

        var targets = files
            .Select(file => Path.Combine(folder, $"{file.Table}.txt"))
            .ToList();

        if (Blocked(gameRoot, files.Select(file => file.Table)) is { } blocked)
            return blocked;

        if (!Directory.Exists(Path.Combine(gameRoot, "ProjectD2")))
            return L("Project Diablo 2 installation not found.");

        // The marker first, then each table whole - written aside and moved into place. A write that fails half
        // way then leaves a marker listing every table, which teardown can take out; written last, it left tables
        // no marker named, which neither Remove nor the next install would touch.
        var written = new List<string>();
        try
        {
            Directory.CreateDirectory(folder);

            File.WriteAllText(
                MarkerIn(gameRoot),
                JsonSerializer.Serialize(
                    new Marker
                    {
                        Multiplier = multiplier,
                        CowPortal = cowPortal,
                        Scope = scope,
                        Packs = packs,
                        EveryKill = drops?.EveryKill ?? false,
                        Boosts = drops?.Boosts?.Where(boost => boost.Value > 1).ToDictionary(boost => boost.Key, boost => boost.Value),
                        Version = build.Name,
                        Archive = build.Fingerprint,
                        Files = [.. targets.Select(target => Path.GetRelativePath(gameRoot, target))],
                    },
                    new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8);

            for (int n = 0; n < files.Count; n++)
            {
                string aside = targets[n] + ".pd2spc";
                File.WriteAllBytes(aside, files[n].Bytes);
                File.Move(aside, targets[n], overwrite: true);
                written.Add(targets[n]);
            }
        }
        catch (Exception problem)
        {
            // Nothing half done is left: what went in comes out, and the marker last - only once all of it went,
            // so a table that would not delete is still listed for the next Remove.
            bool cleared = true;
            foreach (string path in written.Concat(targets.Select(target => target + ".pd2spc")))
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception)
                {
                    cleared = false;
                }
            }

            try
            {
                if (cleared && File.Exists(MarkerIn(gameRoot)))
                    File.Delete(MarkerIn(gameRoot));
            }
            catch (Exception)
            {
                // A marker naming files that are gone does no harm: Remove skips what is not there.
            }

            return problem.Message;
        }

        return null;
    }

    /// <summary>
    /// Takes the override out, removing only what the marker lists. Returns null when it
    /// went, or why it would not - including the ordinary case of nothing being installed.
    /// </summary>
    public static string? Remove(string gameRoot)
    {
        var ours = Current(gameRoot);
        if (!ours.Installed)
            return L("Nothing installed.");

        try
        {
            foreach (string path in ours.Files)
            {
                if (File.Exists(path))
                    File.Delete(path);

                // The compiled form goes with its source. The game remakes it on the next
                // run, so this costs nothing - and leaving it would keep the change in
                // force for a launch that passes -direct without -txt, with no marker left
                // to say why.
                string compiled = Path.ChangeExtension(path, ".bin");
                if (File.Exists(compiled))
                    File.Delete(compiled);
            }

            string marker = MarkerIn(gameRoot);
            if (File.Exists(marker))
                File.Delete(marker);

            // Innermost first, and only while empty. A folder someone else put something in
            // is a folder that stays.
            string stop = Path.Combine(gameRoot, "ProjectD2");
            var folder = new DirectoryInfo(FolderIn(gameRoot));

            while (folder.Exists
                   && !string.Equals(folder.FullName.TrimEnd('\\'), stop.TrimEnd('\\'),
                                     StringComparison.OrdinalIgnoreCase)
                   && folder.GetFileSystemInfos().Length == 0)
            {
                var above = folder.Parent;
                folder.Delete();
                if (above is null)
                    break;

                folder = above;
            }
        }
        catch (Exception problem)
        {
            return problem.Message;
        }

        return null;
    }

    private sealed class Marker
    {
        public double Multiplier { get; set; } = 1;
        public bool CowPortal { get; set; }

        // Absent from a marker written before scopes, which reads back as every area: what those
        // markers did.
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DensityScope>))]
        public DensityScope Scope { get; set; }

        // Absent (0) from a marker written before pack size was its own setting - see Current.
        public int Packs { get; set; }

        // Absent from a marker written before the drop settings: no change (0 reads as 1).
        public bool EveryKill { get; set; }

        // Read from a marker written before the panel took the materials (one multiple for all); never written now.
        public int Materials { get; set; }

        // Read from a marker written before the drop panel (one multiple for Lem to Zod); never written now.
        public int HighRunes { get; set; }

        // The drop panel's multiples by entry; absent before it.
        public Dictionary<string, int>? Boosts { get; set; }

        // Absent from a marker written before this was recorded, which reads back as null
        // and is treated as an unknown build rather than as a mismatch.
        public string? Version { get; set; }
        public string? Archive { get; set; }

        public List<string> Files { get; set; } = [];
    }
}
