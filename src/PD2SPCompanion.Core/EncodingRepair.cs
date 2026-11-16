using D2SLib.Model.Save;
using PD2SPCompanion.Core.Collection;
using PD2SPCompanion.Core.Saves;

namespace PD2SPCompanion.Core;

/// <summary>
/// Items written in an encoding the installed PD2 reads no more - found and written again in one it reads.
/// <para>
/// An item's stats are stored at the widths of the build that wrote it. PD2 changes a width at a season's start and
/// ships the old widths beside the new (<c>Save Bits S13</c>), so the season before's items read by their stamp. Two
/// things still leave items unreadable: <b>a patch within a season</b> that changes a width - every file the old build
/// wrote misreads, in game too - and <b>a season that ships an older season's widths no more</b>. The old widths are
/// kept (<see cref="SeasonSnapshot.KeepEarlierBuilds"/> and the seasons' snapshots); these are what reads by them.
/// </para>
/// <list type="bullet">
/// <item><see cref="Survey"/> (B): each character, shared stash and vault read under the installed tables and under
/// each kept encoding in turn. One that reads only by an earlier encoding - or, by a build of this very season, reads
/// both ways and more plausibly by the earlier - was written by it. Nothing is written.</item>
/// <item><see cref="Repair"/> (C): those read by the encoding that wrote them and written in the installed one, a
/// backup of each first. Every value, line, affix and stamp kept - a format repair, not a migration. An item whose
/// stamp the installed tables read by no column at all takes this season's stamp, and stays a legacy item by the
/// <see cref="LegacyRecord"/>.</item>
/// </list>
/// The tables are swapped to read each earlier encoding and the installed ones put back, with the stamps the run began
/// with (as <see cref="CharacterMigration"/> does): everything is read before anything is written.
/// </summary>
public static class EncodingRepair
{
    /// <summary>An encoding items may have been written in: a kept build of this season, or an earlier season's snapshot.</summary>
    /// <param name="Name">"S14 build ab6ed1cfa8951b14", "S13".</param>
    /// <param name="Folder">The tables to read by, laid over the install.</param>
    /// <param name="SameSeason">A build of the installed season: its rows are this season's, so how plausible a reading is can be compared.</param>
    public sealed record Earlier(string Name, string Folder, bool SameSeason);

    public enum Verdict
    {
        /// <summary>Reads as it is: nothing to do.</summary>
        Reads,

        /// <summary>Written in an earlier encoding (<see cref="Finding.By"/>): the repair writes it in this one.</summary>
        Repairable,

        /// <summary>Reads two ways and its evidence does not say which: left alone, said.</summary>
        Unclear,

        /// <summary>Reads by no encoding kept.</summary>
        Unreadable,
    }

    /// <summary>One file looked at.</summary>
    public sealed record Finding(string Path, Verdict Verdict, Earlier? By, int Items)
    {
        public string Name => System.IO.Path.GetFileName(Path);

        /// <summary>What wrote it, said after its name: "written by S14 build ab6ed1cfa8951b14".</summary>
        public string Said => By is null ? "" : L($"written by {By.Name}");
    }

    /// <summary>
    /// The encodings worth trying, newest first: the installed season's kept builds whose tables differ from what is
    /// installed, then the earlier seasons' snapshots.
    /// </summary>
    public static IReadOnlyList<Earlier> Candidates(Season installed)
    {
        string season = SeasonSnapshot.NameFor(installed);
        var now = SeasonSnapshot.Read(SeasonSnapshot.FolderFor(season))?.Files ?? [];
        var shas = now.ToDictionary(file => file.Path, file => file.Sha256, StringComparer.OrdinalIgnoreCase);

        // By the encoding alone: a build kept for its rules (cubemain) reads items as the installed one does.
        bool Differs(SeasonSnapshot.Manifest kept) =>
            kept.Files.Where(file => SeasonSnapshot.IsEncodingFile(file.Path))
                .Any(file => !shas.TryGetValue(file.Path, out string? sha) || sha != file.Sha256);

        var earlier = SeasonSnapshot.EarlierBuilds(season)
            .Where(each => Differs(each.Manifest))
            .OrderByDescending(each => each.Manifest.TakenAt)
            .Select(each => new Earlier(L($"{season} build {each.Manifest.Build}"), each.Folder, SameSeason: true))
            .ToList();

        if (Number(season) is { } number && Directory.Exists(Storage.EditorData.Seasons))
        {
            earlier.AddRange(Directory.GetDirectories(Storage.EditorData.Seasons)
                .Select(folder => (Folder: folder, Manifest: SeasonSnapshot.Read(folder)))
                .Where(each => each.Manifest is not null && Number(each.Manifest.Season) is { } then && then < number)
                .OrderByDescending(each => Number(each.Manifest!.Season))
                .Select(each => new Earlier(each.Manifest!.Season, each.Folder, SameSeason: false)));
        }

        return earlier;
    }

    /// <summary>
    /// The files to look at: every character and shared stash in the save folder, and the installed season's vaults in
    /// both modes. A character only ever created, never played, has no items and is passed over.
    /// </summary>
    public static IReadOnlyList<string> FilesOf(string saveFolder, string? season)
    {
        var files = new List<string>();
        if (Directory.Exists(saveFolder))
        {
            files.AddRange(Directory.EnumerateFiles(saveFolder, "*.d2s").Order(StringComparer.OrdinalIgnoreCase)
                .Where(path => new FileInfo(path).Length > SaveSummary.HeaderOnlyLength));
            files.AddRange(Directory.EnumerateFiles(saveFolder, "*.stash").Order(StringComparer.OrdinalIgnoreCase));
        }

        if (season is { Length: > 0 })
        {
            foreach (bool hardcore in new[] { false, true })
                files.AddRange(VaultLibrary.Vaults(season, hardcore).Select(each => each.Path));
        }

        return files;
    }

    /// <summary>
    /// B: how each file reads, under the installed tables and each earlier encoding. Nothing written; the installed
    /// tables are back when it returns.
    /// </summary>
    public static IReadOnlyList<Finding> Survey(Season installed, IReadOnlyList<string> files, IReadOnlyList<Earlier> earlier)
    {
        string season = SeasonSnapshot.NameFor(installed);
        var now = files.ToDictionary(path => path, path => Read(path, season), StringComparer.OrdinalIgnoreCase);

        var found = files.ToDictionary(path => path, path => now[path] is null ? Verdict.Unreadable : Verdict.Reads,
                                       StringComparer.OrdinalIgnoreCase);
        var by = new Dictionary<string, Earlier>(StringComparer.OrdinalIgnoreCase);
        var count = files.ToDictionary(path => path, path => now[path]?.Items.Count ?? 0, StringComparer.OrdinalIgnoreCase);

        // Which files read both ways, differently, by a build of this season: the plausibility of the installed reading
        // is asked once the installed tables are back.
        var weigh = new Dictionary<string, (Earlier By, int Findings)>(StringComparer.OrdinalIgnoreCase);

        var stamps = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        try
        {
            foreach (var each in earlier)
            {
                // Only what is not settled yet: a file already found written by a newer encoding stays with it.
                var open = files.Where(path => found[path] is Verdict.Unreadable || (found[path] is Verdict.Reads && each.SameSeason)).ToList();
                if (open.Count == 0)
                    continue;

                using var then = Season.Open(installed.Root, each.Folder, announce: false);
                foreach (string path in open)
                {
                    if (Read(path, season) is not { } read)
                        continue;

                    if (now[path] is not { } current)
                    {
                        found[path] = Verdict.Repairable;
                        by[path] = each;
                        count[path] = read.Items.Count;
                    }
                    else if (Values(current.Items) != Values(read.Items) && !weigh.ContainsKey(path))
                    {
                        weigh[path] = (each, Findings(read.Items));
                    }
                }
            }
        }
        finally
        {
            Restore(installed, stamps);
        }

        foreach (var (path, (each, findings)) in weigh)
        {
            int findingsNow = Findings(now[path]!.Items);
            if (findings < findingsNow)
            {
                found[path] = Verdict.Repairable;
                by[path] = each;
            }
            else if (findings == findingsNow)
            {
                found[path] = Verdict.Unclear;
            }
        }

        return [.. files.Select(path => new Finding(path, found[path], by.GetValueOrDefault(path), count[path]))];
    }

    /// <summary>What a repair did to one file.</summary>
    /// <param name="Backup">Where the file as it was went; null where no copy was taken.</param>
    /// <param name="Restamped">Items that took this season's stamp, the installed tables reading theirs by no column.</param>
    /// <param name="Refused">Why it was not repaired; null where it was.</param>
    public sealed record Repaired(string Path, string? Backup, int Items, int Restamped, string? Refused);

    /// <summary>
    /// C: each repairable file read by the encoding that wrote it and written in the installed one, a backup first, read
    /// back before it replaces the file. Refused while the game runs. The installed tables are back when it returns.
    /// </summary>
    public static IReadOnlyList<Repaired> Repair(Season installed, IReadOnlyList<Finding> findings)
    {
        if (GameProcess.IsRunningFrom(installed.Root))
            throw new InvalidOperationException(L("Game running. Require game closed."));

        string season = SeasonSnapshot.NameFor(installed);
        var asked = findings.Where(finding => finding is { Verdict: Verdict.Repairable, By: not null }).ToList();

        // ---- read: each file by the encoding that wrote it, every item's season noted while that encoding is in ------
        var held = new List<(Finding Finding, Held Read)>();
        var refused = new List<Repaired>();
        var stamps = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        try
        {
            foreach (var group in asked.GroupBy(finding => finding.By!.Folder, StringComparer.OrdinalIgnoreCase))
            {
                using var then = Season.Open(installed.Root, group.Key, announce: false);
                string? thenCurrent = SeasonReading.CurrentStamp, thenPrevious = SeasonReading.PreviousStamp;
                string thenSeason = SeasonSnapshot.NameFor(then), thenPreviousSeason = D2SLib.Model.TXT.StatEncoding.PreviousSeason;
                foreach (var finding in group)
                {
                    if (Read(finding.Path, season) is not { } read)
                    {
                        refused.Add(new Repaired(finding.Path, null, 0, 0, L("no longer reads by its encoding")));
                        continue;
                    }

                    foreach (var item in read.Items.Where(item => !item.IsCompact))
                    {
                        read.SeasonOf[item] = item.Version == thenCurrent ? thenSeason
                            : item.Version == thenPrevious && thenPreviousSeason.Length > 0 ? thenPreviousSeason
                            : null;
                    }
                    held.Add((finding, read));
                }
            }
        }
        finally
        {
            Restore(installed, stamps);
        }

        // ---- write: in the installed encoding --------------------------------------------------------------------------
        var done = new List<Repaired>(refused);
        foreach (var (finding, read) in held)
        {
            string? backup = null;
            try
            {
                var restamped = Restamp(read);
                backup = Write(finding.Path, read, season, taken => backup = taken);
                if (restamped.Count > 0)
                    LegacyRecord.Add(restamped);
                done.Add(new Repaired(finding.Path, backup, read.Items.Count, restamped.Count, null));
                Log.Info("season", $"Encoding repair: {finding.Name} written again from {finding.By!.Name}, {read.Items.Count} items, {restamped.Count} restamped; backup {backup}");
            }
            catch (Exception problem)
            {
                // Whatever the writer trips on - a stat the installed tables have no row for throws a bare Exception - is
                // that file refused, left as it was: out of here, the files after it went unrepaired and the program down.
                Log.Caught(problem);
                done.Add(new Repaired(finding.Path, backup, 0, 0, problem.Message));
            }
        }

        return done;
    }

    // ---- reading -------------------------------------------------------------------------------------------------------

    /// <summary>A file read: its items, and what writes it back.</summary>
    private sealed class Held(string kind, IReadOnlyList<Item> items)
    {
        public string Kind { get; } = kind;
        public IReadOnlyList<Item> Items { get; } = items;
        public D2S? Save { get; init; }
        public SharedStash? Stash { get; init; }
        public IReadOnlyList<(VaultRow Row, Item Item)>? Rows { get; init; }

        /// <summary>The season each item is of, by the encoding that read it - for an item whose stamp no column reads now.</summary>
        public Dictionary<Item, string?> SeasonOf { get; } = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>A file read under the tables installed now, or null where it does not read back exactly.</summary>
    private static Held? Read(string path, string season)
    {
        using var keep = D2SLib.Globals.Use(D2SLib.Globals.Layout);
        try
        {
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
            {
                case ".d2s":
                {
                    var (save, result) = SaveVerifier.Load(path);
                    if (!result.RoundTripped)
                        return null;
                    return new Held("d2s", [.. SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses | ItemScope.Golem | ItemScope.Sockets)]) { Save = save };
                }
                case ".stash":
                {
                    var stash = SharedStash.Read(path);
                    return new Held("stash", [.. stash.Items.Items.WithSockets()]) { Stash = stash };
                }
                case ".pd2lib":
                {
                    string copy = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pd2spc-repair-" + Guid.NewGuid().ToString("N") + ".pd2lib");
                    try
                    {
                        VaultLibrary.CopyForReading(path, copy);
                        using var vault = Vault.Open(copy, season, StatIdBits);
                        var rows = vault.RowsOf(vault.AllKeys())
                            .Select(row => (Row: row, Item: ItemBlob.Restore(row.Blob, StatIdBits)))
                            .ToList();
                        return new Held("vault", [.. rows.Select(each => each.Item).WithSockets()]) { Rows = rows };
                    }
                    finally
                    {
                        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                        try { File.Delete(copy); } catch (IOException) { }
                    }
                }
                default:
                    return null;
            }
        }
        catch (Exception)
        {
            // Bits read at the wrong widths throw whatever the reader trips on: a file that does not read, by this encoding.
            return null;
        }
    }

    /// <summary>The stat id width the vault keeps its items at: PD2's.</summary>
    private const int StatIdBits = 10;

    /// <summary>Every item's every property, what two readings of a file are compared by.</summary>
    private static string Values(IEnumerable<Item> items) =>
        string.Join("|", items.Where(item => !item.IsCompact)
            .Select(item => item.Code + ":" + string.Join(";", Items.ItemStats.Every(item).Select(stat => $"{stat.Stat}={stat.Value}/{stat.Param}"))));

    /// <summary>What the rules find wrong with the items under the tables installed now - a misread draws findings.</summary>
    private static int Findings(IEnumerable<Item> items) =>
        items.Where(item => !item.IsCompact).Sum(item => Items.Legitimacy.Judge(item).Count);

    // ---- writing -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The items whose stamp the installed tables read by no column - neither this season's nor the season before's -
    /// stamped as this season stamps and fitted to its fields, each with the season it is of.
    /// </summary>
    private static List<(Item Item, string Season)> Restamp(Held read)
    {
        var restamped = new List<(Item, string)>();
        if (SeasonReading.CurrentStamp is not { Length: > 0 } current)
            return restamped;

        foreach (var item in read.Items.Where(item => !item.IsCompact))
        {
            if (item.Version is not { Length: > 0 } stamp || stamp == current || stamp == SeasonReading.PreviousStamp)
                continue;

            Migration.Fit(item, stampAs: current);
            if (read.SeasonOf.GetValueOrDefault(item) is { } of)
                restamped.Add((item, of));
        }

        return restamped;
    }

    /// <summary>
    /// The file written in the installed encoding, read back first; where its backup went - said as soon as it is taken
    /// (<paramref name="backedUp"/>), for a write refused after it. A vault's rows go all together or none: one by one,
    /// a row refused half way left the items before it in the new encoding and those after in the old, a file no encoding
    /// read.
    /// </summary>
    private static string? Write(string path, Held read, string season, Action<string> backedUp)
    {
        switch (read.Kind)
        {
            case "d2s":
                return SaveWriter.Replace(path, SaveWriter.Prepared(read.Save!));

            case "stash":
            {
                byte[] bytes = read.Stash!.Write();
                SharedStash.Read(bytes, System.IO.Path.GetFileName(path));
                return SaveWriter.Replace(path, bytes);
            }

            default:
            {
                using var vault = Vault.Open(path, season, StatIdBits);
                string backup = vault.BackUp("before-encoding-repair");
                backedUp(backup);
                if (vault.ReplaceAll(read.Rows!, out string? refused) is { } named)
                    throw new InvalidOperationException(L($"{named}: {refused}"));
                return backup;
            }
        }
    }

    /// <summary>The installed tables back, with the stamps the run began with: the reinstall reads them again from the snapshots.</summary>
    private static void Restore(Season installed, (string? Current, string? Previous) stamps)
    {
        installed.Reinstall();
        SeasonReading.SetStamps(stamps.Current, stamps.Previous);
    }

    private static int? Number(string season) =>
        season.Length > 1 && season[0] is 'S' or 's' && int.TryParse(season.AsSpan(1), out int number) ? number : null;
}
