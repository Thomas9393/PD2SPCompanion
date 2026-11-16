using System.Text;
using D2SLib.Model.Save;
using PD2SPCompanion.Core.Collection;

namespace PD2SPCompanion.Core;

/// <summary>
/// Migrates a vault of an older season into the season installed now: its items, folders and smart-sort links, and the
/// player's records: the vault gold, and the play mode it was played under. Not the trade ledger: PD2 clears
/// trades on its server at a new season. Grail finds, withdrawals and made items were
/// carried too until format 20: the grail is the game's Discovery since.
/// <para>
/// <b>One vault at a time, into a vault of its own name</b>: "S13 Main" is "S13 Main"
/// in S14 too, beside the "S14 Main" the new season begins with - a player migrates a vault, or leaves it
/// and starts anew. One left stays where it is, to be migrated later.
/// </para>
/// <para>
/// <b>The old season is only read</b>, from copies (<see cref="VaultLibrary.CopyForReading"/>): it stays
/// as it was, to be read again or migrated again. Items are read under the old season's own tables where
/// its snapshot was kept - then each value keeps its place in its range (<see cref="SeasonFit.Rescale"/>)
/// - and otherwise through the new tables' previous-season columns, the number kept.
/// </para>
/// <para>
/// An item of the season before comes across as it is, a legacy item (<see cref="Saves.ItemSeason"/>).
/// One of an older season is fitted to the new one (<see cref="Migration.Fit"/>), stamped
/// with the item version the new season writes and checked against the new tables. Each is written in its
/// own folder - or, where it does not migrate cleanly, into <see cref="ReviewFolder"/>, the report saying
/// why. Nothing is left behind. A dry run does all of it but the writing.
/// </para>
/// </summary>
public static class SeasonMigration
{
    /// <summary>Where items that did not migrate cleanly go, their own folders beneath it.</summary>
    public const string ReviewFolder = "Migrated - check these";

    /// <summary>The <c>about</c> key a migrated file is marked with: the seasons it was migrated from, "S12,S13".</summary>
    public const string MigratedFromKey = "migrated_from";

    /// <summary>
    /// The <c>about</c> key that says which vaults were migrated into a file: "S13/S13 Main|S13/Playtest" - a
    /// season, a slash, the vault's name, and a bar between two (no file name holds one). A file marked
    /// before vaults had names says only the season (<see cref="MigratedFromKey"/>): that season's first vault.
    /// </summary>
    public const string MigratedVaultsKey = "migrated_vaults";

    public enum Outcome
    {
        /// <summary>Came across as it was.</summary>
        Clean,

        /// <summary>Came across with something changed that the new season asks: a stamp, a flag, a value moved in its range.</summary>
        Adjusted,

        /// <summary>Came across into the review folder: newly flagged by the new tables, a set piece flagged, or a value it could not hold.</summary>
        Review,

        /// <summary>Did not come across: it would not read, or not read back. Still in the old season's vault.</summary>
        Failed,
    }

    public sealed record ItemReport(string Collection, string Name, string Folder, Outcome Outcome, IReadOnlyList<string> Why);

    /// <summary>What to migrate: from which season, which mode, which of its vaults, and how.</summary>
    /// <param name="From">The season migrated from ("S13").</param>
    /// <param name="Hardcore">The mode: its vault, and any collections of the older layout it still has.</param>
    /// <param name="DryRun">Everything but the writing.</param>
    /// <param name="OldTables">The old season's tables; null for its snapshot, where one was kept.</param>
    /// <param name="Again">Migrate into a season already migrated into from this one.</param>
    /// <param name="KeepReport">Write the report beside the vault; false for a preview on screen, which is redone each time it is shown.</param>
    /// <param name="Vault">The vault's name; null for the season's first (<see cref="VaultLibrary.MainName"/>).</param>
    public sealed record Request(string From, bool Hardcore, bool DryRun, string? OldTables = null, bool Again = false,
                                 int StatIdBits = 10, bool KeepReport = true, string? Vault = null)
    {
        /// <summary>The vault migrated, by name.</summary>
        public string Name => Vault ?? VaultLibrary.MainName(From);
    }

    public sealed record Report(
        string From, string To, bool Hardcore, string Vault, bool DryRun, bool Rescaled,
        IReadOnlyList<ItemReport> Items,
        long Gold,
        IReadOnlyList<string> Notes, string? ReportPath)
    {
        public int Count(Outcome outcome) => Items.Count(item => item.Outcome == outcome);

        /// <summary>The report as a person reads it: what came across, then every item that needs a look.</summary>
        public string Text()
        {
            var text = new StringBuilder();
            text.AppendLine($"{(DryRun ? "Dry run - nothing written. " : "")}Migrate {From} to {To}, {(Hardcore ? "hardcore" : "softcore")} vault \"{Vault}\", {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
            text.AppendLine(Rescaled
                ? "Values keep their place in their range (the old season's tables were read)."
                : "Values kept as numbers: no snapshot of the old season's tables to read its ranges from.");
            text.AppendLine();
            text.AppendLine($"Items: {Items.Count} - clean {Count(Outcome.Clean)}, adjusted {Count(Outcome.Adjusted)}, "
                            + $"to check {Count(Outcome.Review)} (in \"{ReviewFolder}\"), not migrated {Count(Outcome.Failed)}");
            text.AppendLine($"Vault gold: {Gold:N0}.");
            foreach (string note in Notes)
                text.AppendLine(note);

            foreach (var outcome in new[] { Outcome.Failed, Outcome.Review, Outcome.Adjusted })
            {
                var these = Items.Where(item => item.Outcome == outcome).ToList();
                if (these.Count == 0)
                    continue;

                text.AppendLine();
                text.AppendLine(outcome switch
                {
                    Outcome.Failed => "Not migrated (still in the old season's vault):",
                    Outcome.Review => $"To check (in \"{ReviewFolder}\"):",
                    _ => "Adjusted:",
                });
                foreach (var item in these)
                    text.AppendLine($"  {item.Collection} / {(item.Folder.Length > 0 ? item.Folder + " / " : "")}{item.Name}: {string.Join("; ", item.Why)}");
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// The older seasons still to migrate into <paramref name="to"/>, oldest first: each with a vault in a
    /// mode that no later season, up to <paramref name="to"/>, was migrated from. A season migrated into
    /// the one after it is done - its items went on with that season's - and a player who sat seasons out
    /// migrates each one they played straight into this one, through its snapshot.
    /// </summary>
    public static IReadOnlyList<string> Pending(string to) =>
        [.. Older(to).Where(season => PendingModes(season, to).Count > 0)];

    /// <summary>Every season before <paramref name="to"/> with a vault, in either mode, migrated or not; oldest first.</summary>
    public static IReadOnlyList<string> Older(string to)
    {
        if (Number(to) is not { } now || !Directory.Exists(Vault.Folder))
            return [];

        return [.. Directory.GetDirectories(Vault.Folder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => Number(name) is { } number && number < now && Modes(name).Count > 0)
            .OrderBy(name => Number(name))];
    }

    /// <summary>
    /// The vaults of <paramref name="from"/> - softcore's first, by name - not migrated into any season after
    /// it up to <paramref name="to"/>.
    /// </summary>
    public static IReadOnlyList<(bool Hardcore, string Name)> PendingVaults(string from, string to)
    {
        if (Number(from) is not { } then || Number(to) is not { } now)
            return [];

        var later = Directory.Exists(Vault.Folder)
            ? Directory.GetDirectories(Vault.Folder).Select(Path.GetFileName).OfType<string>()
                .Where(name => Number(name) is { } number && number > then && number <= now).ToList()
            : [];

        var pending = new List<(bool, string)>();
        foreach (bool hardcore in new[] { false, true })
        {
            var migrated = later.SelectMany(season => MigratedVaults(season, hardcore))
                                .Where(each => string.Equals(each.Season, from, StringComparison.OrdinalIgnoreCase))
                                .Select(each => each.Name)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            pending.AddRange(VaultsOf(from, hardcore).Where(name => !migrated.Contains(name)).Select(name => (hardcore, name)));
        }

        return pending;
    }

    /// <summary>The modes of <paramref name="from"/>, softcore first, with a vault not migrated into any season after it up to <paramref name="to"/>.</summary>
    public static IReadOnlyList<bool> PendingModes(string from, string to) =>
        [.. PendingVaults(from, to).Select(each => each.Hardcore).Distinct()];

    /// <summary>
    /// A mode's vaults in <paramref name="season"/>, by name: its files, and - where it still has collections
    /// of the older layout and no vault file - the first vault those are to become.
    /// </summary>
    private static IReadOnlyList<string> VaultsOf(string season, bool hardcore)
    {
        var names = VaultLibrary.Vaults(season, hardcore).Select(each => each.Name).ToList();
        string main = VaultLibrary.MainName(season);
        if (VaultLibrary.Collections(season, hardcore).Count > 0 && !names.Contains(main, StringComparer.OrdinalIgnoreCase))
            names.Insert(0, main);

        return names;
    }

    /// <summary>The modes, softcore first, that <paramref name="season"/> has a vault in.</summary>
    public static IReadOnlyList<bool> Modes(string season) =>
        [.. new[] { false, true }.Where(hardcore => VaultsOf(season, hardcore).Count > 0)];

    /// <summary>The seasons <paramref name="season"/>'s vaults in one mode were migrated from, oldest first, read without opening them.</summary>
    public static IReadOnlyList<string> MigratedFrom(string season, bool hardcore) =>
        [.. VaultLibrary.Vaults(season, hardcore)
            .SelectMany(each => Split(VaultLibrary.ReadAbout(each.Path, MigratedFromKey)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(each => Number(each) ?? int.MaxValue)];

    /// <summary>The vaults - season and name - migrated into <paramref name="season"/>'s vaults of one mode.</summary>
    private static IReadOnlyList<(string Season, string Name)> MigratedVaults(string season, bool hardcore) =>
        [.. VaultLibrary.Vaults(season, hardcore).SelectMany(each =>
            MigratedVaults(VaultLibrary.ReadAbout(each.Path, MigratedFromKey), VaultLibrary.ReadAbout(each.Path, MigratedVaultsKey)))];

    /// <summary>
    /// A file's two marks as the vaults they name: each "season/name" of the newer mark, and for a season
    /// only the older one names - a file migrated into before vaults had names - that season's first vault.
    /// </summary>
    private static IEnumerable<(string Season, string Name)> MigratedVaults(string? seasons, string? vaults)
    {
        var named = (vaults ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(each => each.Split('/', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => (Season: parts[0], Name: parts[1]))
            .ToList();

        return named.Concat(Split(seasons)
            .Where(season => !named.Any(each => string.Equals(each.Season, season, StringComparison.OrdinalIgnoreCase)))
            .Select(season => (season, VaultLibrary.MainName(season))));
    }

    /// <summary>"S12" from "S12": a season's number, or null for a name that is not one.</summary>
    private static int? Number(string season) =>
        season.Length > 1 && season[0] is 'S' or 's' && int.TryParse(season.AsSpan(1), out int number) ? number : null;

    private static string[] Split(string? list) =>
        (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// A file's migrated-from list with <paramref name="from"/> added: more than one older season can be
    /// migrated into a season - one a player never migrated, and the one before.
    /// </summary>
    private static string Adding(Vault target, string from) =>
        string.Join(",", Split(target.About(MigratedFromKey)).Append(from).Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>One item as read from the old season, with what is needed to judge and write it.</summary>
    private sealed record Read(string Collection, VaultRow Row, IReadOnlyList<string> Path, Item? Item,
                               IReadOnlyDictionary<string, RollRange>? Before, int FindingsBefore, string? Unread);

    /// <summary>
    /// Migrates <see cref="Request.From"/>'s vault, in one mode, into <paramref name="installed"/>'s season.
    /// <paramref name="installed"/> is the caller's own season - the editor's, or the command line's - and
    /// its tables are installed again before this returns, whatever was opened on the way. Refused, with
    /// the reason, where the migration cannot be done safely; nothing is written then.
    /// </summary>
    public static Report Run(Season installed, Request request)
    {
        string from = request.From;
        string to = SeasonSnapshot.NameFor(installed);
        if (!Vault.TryInstalledSeason(out _, out string why))
            throw new InvalidOperationException(why);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L($"{to} is the installed season. Nothing to migrate into."));

        if (Saves.SeasonReading.CurrentStamp is not { } stamp)
        {
            throw new InvalidOperationException(
                L($"{to} item version unknown. Require one {to} character opened."));
        }

        string name = request.Name;
        if (VaultLibrary.ItemSources(from, request.Hardcore, name).Count == 0)
            throw new InvalidOperationException($"{from} has no {(request.Hardcore ? "hardcore" : "softcore")} vault \"{name}\" to migrate.");

        // Already migrated: migrating again would put every item in twice.
        if (!request.Again && !PendingVaults(from, to).Contains((request.Hardcore, name)))
            throw new InvalidOperationException($"{from} was migrated to {to} already.");

        // The old season's own tables: the ones given, else its snapshot. Without them, its items are read
        // through the new tables' previous-season columns - which reach exactly one season back.
        string? oldTables = request.OldTables
                            ?? (SeasonSnapshot.Read(SeasonSnapshot.FolderFor(from)) is not null ? SeasonSnapshot.FolderFor(from) : null);
        bool throughPrevious = oldTables is null;
        if (throughPrevious && !string.Equals(D2SLib.Model.TXT.StatEncoding.PreviousSeason, from, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"No snapshot of {from}'s tables was kept, and {to}'s tables can only read "
                + $"{(D2SLib.Model.TXT.StatEncoding.PreviousSeason is { Length: > 0 } previous ? previous : "no older season")}'s items.");
        }

        var notes = new List<string>();
        string work = Path.Combine(VaultLibrary.SeasonFolder(to), $"migrating-{from}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(work);

        try
        {
            // ---- read: the old season, from copies --------------------------------------------------
            List<Read> read;
            Records records;
            if (throughPrevious)
            {
                (read, records) = ReadAll(from, request, work, previousColumns: true, rescale: false);
            }
            else
            {
                try
                {
                    using (Season.Open(installed.Root, oldTables))
                        (read, records) = ReadAll(from, request, work, previousColumns: false, rescale: true);
                }
                finally
                {
                    // The caller's season back, whatever happened above.
                    installed.Reinstall();
                }
            }

            // ---- fit and judge: the new season ------------------------------------------------------
            var judged = read.Select(each => Judge(each, stamp, request.StatIdBits, rescale: !throughPrevious)).ToList();

            // ---- write ------------------------------------------------------------------------------
            if (!request.DryRun)
                judged = Write(to, from, request, judged, records, notes, work);

            var report = new Report(from, to, request.Hardcore, name, request.DryRun, !throughPrevious,
                                    [.. judged.Select(each => each.Report)], records.Gold, notes, null);

            if (!request.KeepReport)
                return report;

            string path = Path.Combine(VaultLibrary.SeasonFolder(to),
                $"Migrate {from} to {to} {(request.Hardcore ? "hardcore" : "softcore")} {name}{(request.DryRun ? " (dry run)" : "")} {DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, report.Text());
            Log.Info("season", $"Migrate {from} -> {to} ({(request.Hardcore ? "hardcore" : "softcore")} \"{name}\"{(request.DryRun ? ", dry run" : "")}): "
                               + $"{report.Items.Count} items, clean {report.Count(Outcome.Clean)}, adjusted {report.Count(Outcome.Adjusted)}, "
                               + $"to check {report.Count(Outcome.Review)}, not migrated {report.Count(Outcome.Failed)}; report {path}");
            return report with { ReportPath = path };
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The player's records, as read from the old season's vault, and the play mode it kept.</summary>
    private sealed record Records(long Gold,
                                  IReadOnlyList<(string Collection, IReadOnlyList<(string Category, IReadOnlyList<string> Path)> Links)> Links,
                                  string? PlayMode = null, string? PlayRules = null,
                                  IReadOnlyList<IReadOnlyList<string>>? Locked = null,
                                  IReadOnlyList<IReadOnlyList<string>>? Folders = null);

    /// <summary>
    /// The old season's vault, read from copies in <paramref name="work"/>: every file of it that holds
    /// items - the vault itself, and for a season's first vault its collections where it was never opened
    /// since they were folded in, each to its place in the vault - and the vault's records.
    /// </summary>
    private static (List<Read> Items, Records Records) ReadAll(string from, Request request, string work, bool previousColumns, bool rescale)
    {
        var items = new List<Read>();
        var links = new List<(string, IReadOnlyList<(string, IReadOnlyList<string>)>)>();

        // The folders that were locked, by their paths in the new vault; the empty path is the vault's top.
        var locked = new List<IReadOnlyList<string>>();

        // Every folder, by its path: an empty one too, which no item's path names (the pad's "New folder" was lost).
        var every = new List<IReadOnlyList<string>>();

        foreach (var collection in VaultLibrary.ItemSources(from, request.Hardcore, request.Name))
        {
            string copy = Path.Combine(work, $"collection-{items.Count}-{Guid.NewGuid():N}.pd2lib");
            VaultLibrary.CopyForReading(collection.Path, copy);

            using var vault = Vault.Open(copy, from, request.StatIdBits);
            var folders = vault.Folders().ToDictionary(folder => folder.Id);
            IReadOnlyList<string> PathOf(long? id)
            {
                var path = new List<string>();
                for (var at = id; at is { } folder && folders.TryGetValue(folder, out var each); at = each.ParentId)
                    path.Insert(0, each.Name);
                return [.. collection.Under, .. path];
            }

            foreach (var row in vault.RowsOf(vault.AllKeys()))
            {
                Item? item = null;
                string? unread = null;
                IReadOnlyDictionary<string, RollRange>? before = null;
                int findings = 0;
                try
                {
                    using (D2SLib.Globals.Use(new D2SLib.ParserLayout(false, request.StatIdBits, previousColumns)))
                        item = ItemBlob.Restore(row.Blob, request.StatIdBits);

                    if (rescale)
                    {
                        try { before = AffixRanges.For(item); }
                        catch (Exception) { /* no ranges to keep a place in: the number is kept */ }
                    }

                    // Flags the old season's own tables raised: only new ones send an item to be checked.
                    findings = previousColumns ? -1 : Legitimacy.Check(item).Count;
                }
                catch (Exception problem)
                {
                    unread = $"would not read: {problem.Message}";
                }

                items.Add(new Read(collection.Name, row, PathOf(row.FolderId), item, before, findings, unread));
            }

            links.Add((collection.Name, [.. vault.FolderLinks().Select(link => (link.Category, PathOf(link.FolderId)))]));
            locked.AddRange(vault.LockedFolders().Select(id => PathOf(id)));
            if (vault.IsLocked(null))
                locked.Add(PathOf(null));
            every.AddRange(folders.Keys.Order().Select(id => PathOf(id)));
        }

        var records = new Records(0, links, Locked: locked, Folders: every);
        string vaultPath = VaultLibrary.ExistingPath(from, request.Hardcore, request.Name);
        if (File.Exists(vaultPath))
        {
            string copy = Path.Combine(work, "records.pd2lib");
            VaultLibrary.CopyForReading(vaultPath, copy);
            using var vault = Vault.Open(copy, from, request.StatIdBits);
            records = new Records(vault.GoldBalance(), links,
                                  vault.About(VaultLibrary.PlayModeKey), vault.About(VaultLibrary.PlayRulesKey), locked, every);
        }

        return (items, records);
    }

    /// <summary>An item judged under the new season, ready to write.</summary>
    private sealed record Judged(Read Read, Item? Item, ItemReport Report);

    /// <summary>Fits one item to the new season and says how it came across.</summary>
    private static Judged Judge(Read read, string stamp, int statIdBits, bool rescale)
    {
        string name = read.Row.Name.Length > 0 ? read.Row.Name : "(unnamed)";
        string folder = string.Join(" / ", read.Path);
        if (read.Item is not { } item)
            return new Judged(read, null, new ItemReport(read.Collection, name, folder, Outcome.Failed, [read.Unread ?? "would not read"]));

        var (outcome, why) = Carry(item, rescale ? read.Before : null, read.FindingsBefore, stamp, statIdBits);
        return new Judged(read, outcome == Outcome.Failed ? null : item, new ItemReport(read.Collection, name, folder, outcome, why));
    }

    /// <summary>
    /// Carries one item read from an old season into this one, in place, and says how it came across - what the vault's
    /// migration does to each item: its fields fitted and its stamp set (<see cref="Migration.Fit"/>), its values kept at their place in ranges that
    /// moved (<see cref="SeasonFit.Rescale"/>, where <paramref name="before"/> - its ranges under the old season's tables -
    /// is given), its rows followed where the old season found nothing wrong with it (<see cref="SeasonRows.Follow"/>), and
    /// a write and read back in this season's encoding. <paramref name="findingsBefore"/> is what the old tables found
    /// wrong with it, -1 where they were not read.
    /// <para>
    /// <b>The season before's item is carried as it is</b> - a legacy item (<see cref="Saves.ItemSeason"/>):
    /// stamp, values and lines as that season made them, which the game reads by its own stamp and never
    /// rewrites. Only what the game does to one on load: a runeword's id numbered as this season numbers it. The fitting
    /// above is for an item of an older season still, which the game reads no more.
    /// </para>
    /// </summary>
    public static (Outcome Outcome, IReadOnlyList<string> Why) Carry(
        Item item, IReadOnlyDictionary<string, RollRange>? before, int findingsBefore, string stamp, int statIdBits)
    {
        if (Saves.ItemSeason.IsLegacy(item))
        {
            IReadOnlyList<string> kept = Migration.Renumber(item) is { } renumbered ? [renumbered.Why] : [];
            if (Unwritable(item, statIdBits) is { } unwritten)
                return (Outcome.Failed, [$"would not write: {unwritten}"]);

            return (kept.Count > 0 ? Outcome.Adjusted : Outcome.Clean, kept);
        }

        var why = new List<string>();
        bool lossy = false;
        foreach (var change in Migration.Fit(item, stampAs: stamp))
        {
            if (change.Stat == "version")
                continue;

            why.Add(change.Why);
            lossy |= change.Why.Contains("cannot be carried") || change.Why.Contains("not a property this season has")
                     || change.Why.Contains("this season holds");
        }

        int moved = before is not null ? SeasonFit.Rescale(item, before) : 0;
        if (moved > 0)
            why.Add(moved == 1 ? "1 value kept its place in a changed range" : $"{moved} values kept their place in changed ranges");

        // Found right by the old season's tables and wrong by this one's: the season moved its rows, and the item
        // follows them (the S14 rehearsal's rebalanced items).
        if (findingsBefore == 0 && Legitimacy.Check(item).Count > 0)
            why.AddRange(SeasonRows.Follow(item, before));

        var findings = Legitimacy.Check(item);
        bool newlyFlagged = findingsBefore >= 0 ? findings.Count > findingsBefore : findings.Count > 0;
        bool setFlagged = item.Quality == ItemQuality.Set && findings.Count > 0;
        if (newlyFlagged || setFlagged)
            why.AddRange(findings.Take(3).Select(finding => finding.Said));

        // It must write and read back in the new season's encoding before anything is written.
        if (Unwritable(item, statIdBits) is { } refused)
            return (Outcome.Failed, [$"would not write: {refused}"]);

        var outcome = lossy || newlyFlagged || setFlagged ? Outcome.Review
            : why.Count > 0 ? Outcome.Adjusted
            : Outcome.Clean;
        return (outcome, why);
    }

    /// <summary>
    /// Why an item will not write and read back in this season's encoding, or null where it will. One that holds a stat
    /// this season has no row for throws in the writer: that item stays in the old vault, the rest of the vault still comes.
    /// </summary>
    private static string? Unwritable(Item item, int statIdBits)
    {
        try
        {
            ItemBlob.TryCapture(ItemBlob.Copy(item, statIdBits), statIdBits, out _, out _, out string? refused);
            return refused;
        }
        catch (Exception problem)
        {
            return problem.Message;
        }
    }

    /// <summary>For tests: how many items a migration writes before it fails, as a full disk would.</summary>
    public const string FakeFailAfter = "PD2SPC_FAKE_MIGRATE_FAIL_AFTER";

    /// <summary>
    /// Writes the migrated items and records into the new season's vault of the same name, backed up first
    /// where it was there.
    /// <para>
    /// <b>Into a copy, swapped in whole once all of it is there</b>: each item commits by
    /// itself and the marks saying it was migrated come last, so one that failed part way left the items with no mark,
    /// and trying again added every one of them a second time. Failed now, the vault is as it was, and no mark is needed.
    /// </para>
    /// </summary>
    private static List<Judged> Write(string to, string from, Request request, List<Judged> judged, Records records,
                                      List<string> notes, string work)
    {
        string real = VaultLibrary.VaultPath(to, request.Hardcore, request.Name);
        string aside = Path.Combine(work, "target.pd2lib");
        bool hadVault = File.Exists(real);
        if (hadVault)
        {
            using (var there = VaultLibrary.OpenVault(to, request.Hardcore, request.Name, request.StatIdBits))
                notes.Add($"The {to} vault \"{request.Name}\" was there already: added to, backed up first ({Path.GetFileName(there.BackUp("before-migration"))}).");

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            VaultLibrary.CopyForReading(real, aside);
        }

        List<Judged> written;
        using (var target = VaultLibrary.OpenVaultAt(aside, to, request.Hardcore, request.StatIdBits))
            written = WriteInto(target, hadVault, to, from, request, judged, records);

        // Let go, so the file stands alone with no log beside it; then in its place in one move.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.CreateDirectory(Path.GetDirectoryName(real)!);
        File.Delete(real + "-wal");
        File.Delete(real + "-shm");
        File.Move(aside, real, overwrite: true);
        return written;
    }

    /// <summary>The items and records into <paramref name="target"/>.</summary>
    private static List<Judged> WriteInto(Vault target, bool hadVault, string to, string from, Request request, List<Judged> judged, Records records)
    {
        var written = new List<Judged>();
        int stillToFail = int.TryParse(Environment.GetEnvironmentVariable(FakeFailAfter), out int after) ? after : int.MaxValue;

        {
            var made = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var fresh = new HashSet<long>();
            long? FolderFor(IReadOnlyList<string> path)
            {
                long? parent = null;
                string key = "";
                foreach (string level in path)
                {
                    key += "/" + level;
                    if (!made.TryGetValue(key, out long id))
                    {
                        if (target.Folders().FirstOrDefault(folder => folder.ParentId == parent
                                                              && string.Equals(folder.Name, level, StringComparison.OrdinalIgnoreCase)) is { } there)
                        {
                            id = there.Id;
                        }
                        else
                        {
                            id = target.CreateFolder(parent, level);
                            fresh.Add(id);
                        }

                        made[key] = id;
                    }

                    parent = id;
                }

                return parent;
            }

            foreach (var each in judged)
            {
                if (each.Item is not { } item || each.Report.Outcome == Outcome.Failed)
                {
                    written.Add(each);
                    continue;
                }

                if (stillToFail-- == 0)
                    throw new IOException("Disk full (test).");

                IReadOnlyList<string> path = each.Report.Outcome == Outcome.Review ? [ReviewFolder, .. each.Read.Path] : each.Read.Path;
                if (target.AddMigrated(item, each.Read.Row, FolderFor(path), out string? refused) is null)
                    written.Add(each with { Report = each.Report with { Outcome = Outcome.Failed, Why = [.. each.Report.Why, $"not written: {refused}"] } });
                else
                    written.Add(each);
            }

            // Every folder there was, an empty one too.
            foreach (var path in records.Folders ?? [])
                FolderFor(path);

            // Smart sort's links, where the new season has none for the category yet: the first file's that names one.
            var linked = target.FolderLinks().Select(link => link.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (category, path) in records.Links.SelectMany(links => links.Links))
            {
                if (linked.Add(category) && path.Count > 0 && FolderFor(path) is { } folder)
                    target.LinkFolder(category, folder);
            }

            // A folder's lock (once left behind): onto a folder this migration made - a folder the new
            // season's vault had already keeps the lock it has or has not. The top's, where the vault itself is new.
            foreach (var path in records.Locked ?? [])
            {
                if (path.Count == 0)
                {
                    if (!hadVault)
                        target.SetLocked(null, true);
                }
                else if (made.TryGetValue("/" + string.Join("/", path), out long folder) && fresh.Contains(folder))
                {
                    target.SetLocked(folder, true);
                }
            }
        }

        {
            if (records.Gold != 0)
                target.RecordGold([new GoldMove("", records.Gold)]);
            target.SetAbout(MigratedFromKey, Adding(target, from));
            target.SetAbout(MigratedVaultsKey, string.Join("|",
                (target.About(MigratedVaultsKey) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Append($"{from}/{request.Name}").Distinct(StringComparer.OrdinalIgnoreCase)));

            // The play mode it was played under goes with it, where the vault it joins keeps none yet.
            if (records.PlayMode is { Length: > 0 } mode && target.About(VaultLibrary.PlayModeKey) is not { Length: > 0 })
            {
                target.SetAbout(VaultLibrary.PlayModeKey, mode);
                if (records.PlayRules is { Length: > 0 } rules)
                    target.SetAbout(VaultLibrary.PlayRulesKey, rules);
            }
            target.SetAbout("migrated_at", DateTimeOffset.UtcNow.ToString("O"));
        }

        return written;
    }
}
