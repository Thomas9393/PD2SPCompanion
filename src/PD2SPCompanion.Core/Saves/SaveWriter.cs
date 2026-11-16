using System.Text.RegularExpressions;
using D2SLib.Model.Save;
using D2SLib.Model.TXT;

namespace PD2SPCompanion.Core.Saves;

public sealed record EditOutcome(
    string Description,
    int Before,
    int After,
    string? BackupPath,
    bool Written);

/// <summary>
/// The only path that writes a save file. Every edit goes through the same sequence,
/// and any step that fails aborts before the original is touched:
/// <list type="number">
///   <item>load, refusing anything that does not round-trip byte-identical</item>
///   <item>apply the change, rejecting values the format cannot hold</item>
///   <item>serialize, then <b>re-parse the result</b> and confirm it round-trips and
///         actually carries the change</item>
///   <item>copy the original to a timestamped .bak</item>
///   <item>write</item>
/// </list>
/// </summary>
public static class SaveWriter
{
    /// <summary>Changes one stat on one item, addressed by its index in a dump.</summary>
    public static EditOutcome SetItemStat(string path, int itemIndex, string statName, int value, bool dryRun)
    {
        var (save, _) = SaveVerifier.Load(path);

        var items = save.PlayerItemList?.Items ?? [];
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(itemIndex),
                $"item #{itemIndex} does not exist; this character has {items.Count} items (0-{items.Count - 1}).");

        var item = items[itemIndex];
        var stat = item.StatLists
            .SelectMany(list => list.Stats)
            .FirstOrDefault(s => string.Equals(s.Stat, statName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"item #{itemIndex} ({item.Code?.Trim()}) has no stat '{statName}'. It has: "
                + string.Join(", ", item.StatLists.SelectMany(l => l.Stats).Select(s => s.Stat)));

        // An edit makes a legacy item this season's, first, so what it writes takes this season's fields. The same stat, still on the item, fitted: read for "before" after it.
        ItemSeason.MakeCurrent(item);

        int before = stat.Value;
        StatFields.Validate(statName, value);
        stat.Value = value;

        string description = $"item #{itemIndex} ({item.Code?.Trim()}): {statName} {before} -> {value}";

        if (dryRun)
        {
            // Still prove the edit would serialize soundly, just do not keep it.
            VerifyReadBack(D2S.Write(save), itemIndex, statName, value);
            return new EditOutcome(description, before, value, null, Written: false);
        }

        string backup = Commit(path, save, written => VerifyReadBack(written, itemIndex, statName, value));
        return new EditOutcome(description, before, value, backup, Written: true);
    }

    /// <summary>
    /// Puts a property on an item that did not have one, at the lowest value its field
    /// holds - or at <paramref name="value"/>, with <paramref name="skill"/> for one that names a skill (an aura when
    /// equipped, for a test character) - and proves the result parses back.
    /// <para>
    /// This is the one edit that <b>adds</b> bits rather than changing them, so it is the
    /// one worth a command of its own: everything else writes a field that was already
    /// there and is the same width whatever is in it.
    /// </para>
    /// </summary>
    public static EditOutcome AddItemStat(string path, int itemIndex, int statId, bool dryRun, int? value = null, int? skill = null)
    {
        var (save, _) = SaveVerifier.Load(path);

        var items = save.PlayerItemList?.Items ?? [];
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(itemIndex),
                $"item #{itemIndex} does not exist; this character has {items.Count} items (0-{items.Count - 1}).");

        var item = items[itemIndex];
        if (item.IsCompact)
            throw new ArgumentException($"item #{itemIndex} ({item.Code?.Trim()}) is a simple item and holds no properties.");

        // The same bar the page applies, so the two cannot drift: saving a full record is
        // not the test, being something an affix can reach is.
        if (!StatCatalogue.CanCarry(item.Code))
            throw new ArgumentException(
                $"item #{itemIndex} ({item.Code?.Trim()}) is not something the game puts properties on.");

        ItemSeason.MakeCurrent(item);

        var list = item.StatLists.FirstOrDefault();
        if (list is null)
        {
            list = new ItemStatList();
            item.StatLists.Add(list);
        }

        var run = StatCatalogue.RunFrom(statId);
        var added = new List<string>();

        foreach (int id in run)
        {
            string name = StatCatalogue.NameOf(id)
                ?? throw new ArgumentException($"no property has id {id}.");

            int floor = id == statId && value is { } given ? given : Math.Max(0, StatFields.RangeOf(name)?.Min ?? 0);
            // A skill where the property keeps it: a chance to cast's and a charge's packed with its level (SkillId),
            // any other's in the plain parameter - an aura when equipped (it wrote Attack).
            int? named = id == statId ? skill : null;
            bool packed = StatText.IsPacked(name);
            list.Stats.Add(new ItemStat { Id = (ushort)id, Stat = name, Value = floor, SkillId = packed ? named : null, Param = packed ? null : named });
            added.Add(named is { } its ? $"{name} {floor} (skill {its})" : $"{name} {floor}");
        }

        string description =
            $"item #{itemIndex} ({item.Code?.Trim()}): added {string.Join(", ", added)}";

        void ReadBack(byte[] written)
        {
            var reread = D2SLib.Core.ReadD2S(written);
            var back = (reread.PlayerItemList?.Items ?? [])[itemIndex].StatLists
                .SelectMany(each => each.Stats)
                .Select(stat => stat.Stat)
                .ToList();

            foreach (int id in run)
            {
                string name = StatCatalogue.NameOf(id)!;
                if (!back.Contains(name))
                {
                    throw new InvalidOperationException(
                        $"the written save does not read back with '{name}' on item #{itemIndex}.");
                }
            }
        }

        if (dryRun)
        {
            ReadBack(D2S.Write(save));
            return new EditOutcome(description, 0, 0, null, Written: false);
        }

        string backup = Commit(path, save, ReadBack);
        return new EditOutcome(description, 0, 0, backup, Written: true);
    }

    /// <summary>
    /// Corrupts an item: the same thing the cube does, either rolled or chosen.
    /// <para>
    /// Rolled, it picks a number from 1 to 1000 and takes the band it falls in, exactly as
    /// <c>cubemain.txt</c> has the game do it. Chosen, it takes the named id, and the id
    /// written on the item is the one whose properties were written - which is the whole
    /// point of choosing through this rather than by hand.
    /// </para>
    /// </summary>
    public static EditOutcome CorruptItem(string path, int itemIndex, int? id, bool dryRun)
    {
        var (save, _) = SaveVerifier.Load(path);

        var items = save.PlayerItemList?.Items ?? [];
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(itemIndex),
                $"item #{itemIndex} does not exist; this character has {items.Count} items (0-{items.Count - 1}).");

        var item = items[itemIndex];

        // A shard spent to no effect is corrupted with nothing: a corruption may take its place.
        if (Corruptions.IsCorrupted(item) && !Corruptions.IsSpent(item))
            throw new ArgumentException($"item #{itemIndex} ({item.Code?.Trim()}) is already corrupted.");

        var open = Corruptions.Available(item);
        if (open.Count == 0)
            throw new ArgumentException($"nothing corrupts a {item.Code?.Trim()}.");

        var random = new Random();

        var chosen = id is { } wanted
            ? open.FirstOrDefault(option => option.Id == wanted)
              ?? throw new ArgumentException(
                  $"corruption {wanted} is not one this item can take. It can take: "
                  + string.Join(", ", open.Select(option => option.Id).Distinct()))
            : Corruptions.Roll(item, random)!;

        Corruptions.Apply(item, chosen, random);

        string description =
            $"item #{itemIndex} ({item.Code?.Trim()}): corrupted {chosen.Id} - {chosen.Summary}";

        void ReadBack(byte[] written)
        {
            var reread = D2SLib.Core.ReadD2S(written);
            var back = (reread.PlayerItemList?.Items ?? [])[itemIndex];

            if (Corruptions.IdOn(back) != chosen.Id)
            {
                throw new InvalidOperationException(
                    $"the written save reads back corruption {Corruptions.IdOn(back)} "
                    + $"where {chosen.Id} was written.");
            }
        }

        if (dryRun)
        {
            ReadBack(D2S.Write(save));
            return new EditOutcome(description, 0, chosen.Id, null, Written: false);
        }

        string backup = Commit(path, save, ReadBack);
        return new EditOutcome(description, 0, chosen.Id, backup, Written: true);
    }

    /// <summary>
    /// The Demonic Cube: rolls every property on one item again inside its own range. The
    /// item keeps its identity and its list of properties - only the numbers move.
    /// </summary>
    public static EditOutcome RerollItem(string path, int itemIndex, bool dryRun)
    {
        var (save, _) = SaveVerifier.Load(path);

        var items = save.PlayerItemList?.Items ?? [];
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(itemIndex), $"item #{itemIndex} does not exist.");

        var item = items[itemIndex];
        if (!DemonicCube.CanTake(item))
            throw new ArgumentException($"item #{itemIndex} ({item.Code?.Trim()}) has nothing to roll.");

        ItemSeason.MakeCurrent(item);

        var moved = DemonicCube.Reroll(item, new Random());

        string description = $"item #{itemIndex} ({item.Code?.Trim()}): rerolled"
            + (moved.Count == 0 ? " - every number came back the same"
                : ". " + string.Join("; ", moved.Select(each => $"{each.Stat} {each.Was} -> {each.Now}")));

        if (dryRun)
        {
            _ = D2S.Write(save);
            return new EditOutcome(description, 0, 0, null, Written: false);
        }

        string backup = Commit(path, save, null);
        return new EditOutcome(description, 0, 0, backup, Written: true);
    }

    /// <summary>
    /// Takes every stored item - inventory, cube, stash - off a character, leaving what it
    /// wears and its belt alone. A test instrument for emptying a character between rounds; a
    /// socketed item goes with its parent, since it is nested inside it.
    /// </summary>
    public static EditOutcome ClearStored(string path, bool dryRun)
    {
        var (save, _) = SaveVerifier.Load(path);

        var list = save.PlayerItemList
            ?? throw new InvalidOperationException("this character has no item list.");

        int before = list.Items.Count;
        list.Items.RemoveAll(item => item.Mode == ItemMode.Stored);
        list.Count = (ushort)list.Items.Count;
        int removed = before - list.Items.Count;

        string description = $"removed {removed} stored item(s); {list.Items.Count} left (worn and belt)";

        void ReadBack(byte[] written)
        {
            var reread = D2SLib.Core.ReadD2S(written);
            if ((reread.PlayerItemList?.Items ?? []).Any(item => item.Mode == ItemMode.Stored))
                throw new InvalidOperationException("the written save still holds a stored item.");
        }

        if (dryRun)
        {
            ReadBack(D2S.Write(save));
            return new EditOutcome(description, before, list.Items.Count, null, Written: false);
        }

        string backup = Commit(path, save, ReadBack);
        return new EditOutcome(description, before, list.Items.Count, backup, Written: true);
    }

    /// <summary>
    /// Takes the corruption off an item, and says what it had to choose where the original
    /// could not be recovered.
    /// </summary>
    public static EditOutcome CleanItem(string path, int itemIndex, bool dryRun)
    {
        var (save, _) = SaveVerifier.Load(path);

        var items = save.PlayerItemList?.Items ?? [];
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(itemIndex), $"item #{itemIndex} does not exist.");

        var item = items[itemIndex];
        if (!Corruptions.IsCorrupted(item))
            throw new ArgumentException($"item #{itemIndex} ({item.Code?.Trim()}) is not corrupted.");

        // The clean closes the sockets the corruption bored, and the writer writes only what the count says: what sits
        // in them would be gone with no word. The app's panel puts them in the inventory first.
        if (Corruptions.IdOn(item) == Corruptions.Sockets && item.SocketedItems.Count > Corruptions.SocketsWithout(item))
            throw new ArgumentException(L($"item #{itemIndex} ({item.Code?.Trim()}) holds {item.SocketedItems.Count} socketed items in sockets the clean would close: take them out first."));

        var told = Corruptions.Clean(item, new Random());

        string description = $"item #{itemIndex} ({item.Code?.Trim()}): cleaned"
            + (told.Count == 0 ? "" : ". " + string.Join("; ", told.Select(each =>
                each.Removed
                    ? $"{each.Stat} removed"
                    : $"{each.Stat} {each.Was} -> {each.Now}")));

        void ReadBack(byte[] written)
        {
            var reread = D2SLib.Core.ReadD2S(written);
            if (Corruptions.IsCorrupted((reread.PlayerItemList?.Items ?? [])[itemIndex]))
                throw new InvalidOperationException("the written save still reads as corrupted.");
        }

        if (dryRun)
        {
            ReadBack(D2S.Write(save));
            return new EditOutcome(description, 0, 0, null, Written: false);
        }

        string backup = Commit(path, save, ReadBack);
        return new EditOutcome(description, 0, 0, backup, Written: true);
    }

    /// <summary>
    /// How many older backups to keep for a character, past the recent ones (see <see cref="Prune"/>).
    /// Null keeps every one - the command line's default; zero keeps the recent ones only, the
    /// Settings page's "Never".
    /// </summary>
    public static int? BackupsToKeep { get; set; }

    /// <summary>
    /// Serializes a character and writes it, backing the original up first. Nothing
    /// reaches disk until the bytes have been re-parsed and shown to round-trip, plus
    /// whatever extra check the caller supplies. Returns the backup's path.
    /// </summary>
    public static string Commit(string path, D2S save, Action<byte[]>? alsoVerify = null) =>
        Replace(path, Prepared(save, alsoVerify));

    /// <summary>
    /// The bytes this save would be written as, having checked they read back.
    /// <para>
    /// Separate from writing them, because a character is not always the only file being
    /// saved. Where two files are saved together, every one of them is prepared before any
    /// of them is replaced - otherwise the first can be written and the second refuse, and
    /// an item moved between them ends up in neither.
    /// </para>
    /// </summary>
    public static byte[] Prepared(D2S save, Action<byte[]>? alsoVerify = null)
    {
        // Written and read back with the layout the save was read with, whatever was read since.
        using var layout = save.Layout is { } read ? D2SLib.Globals.Use(read) : default;
        byte[] written = D2S.Write(save);

        var reread = D2SLib.Core.ReadD2S(written);
        if (!D2S.Write(reread).AsSpan().SequenceEqual(written))
            throw new InvalidOperationException(L("edited save does not round-trip."));

        alsoVerify?.Invoke(written);

        return written;
    }

    /// <summary>
    /// Puts prepared bytes in place of the file, keeping a timestamped copy of what was
    /// there. Returns where that copy went.
    /// </summary>
    public static string Replace(string path, byte[] written)
    {
        string backup = Backup(path);
        WriteSafely(path, written);
        KeepAsWritten(path, written);

        // After the new one exists, never before: a prune that ran first could leave a
        // character with no backup at all if the write then failed.
        Prune(path, BackupsToKeep);

        return backup;
    }

    // ---- the file as it was last written --------------------------------------
    // A backup is the file as it was BEFORE a save. So the newest backup never holds what the last save put in
    // - and if the file then becomes unreadable (the game's doing, or a disk's), what that save brought to the
    // character is in no copy at all: an item just taken out of the vault, say. The vault's own record of
    // what left it covered that case, and is gone. In its place:
    // one more copy, rolling, of the very bytes each save wrote - which were read back before they were
    // written. It is not a backup among the others: one file, replaced at every save, never thinned.

    /// <summary>What the rolling copy's name ends in, after the file's own.</summary>
    public const string AsWrittenSuffix = ".last";

    /// <summary>Where a file's rolling copy is kept: with its backups, or in the folder named.</summary>
    public static string AsWrittenPath(string path, string? intoFolder = null) =>
        Path.Combine(intoFolder ?? BackupFolderFor(path), Path.GetFileName(path) + AsWrittenSuffix);

    /// <summary>The bytes just written to a file, kept beside its backups. Never fails the save it follows.</summary>
    public static void KeepAsWritten(string path, byte[] written, string? intoFolder = null)
    {
        try
        {
            string target = AsWrittenPath(path, intoFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            WriteSafely(target, written);
        }
        catch (Exception problem)
        {
            Diagnostics.Log.Caught(problem);
        }
    }

    /// <summary>
    /// The file's rolling copy, where it says something the file does not: the file is gone, or differs from
    /// it - the game wrote it since, or it will not read. Null where the two are one and the same, which is
    /// the usual case and nothing to offer.
    /// </summary>
    public static FileInfo? AsWritten(string path, string? intoFolder = null)
    {
        try
        {
            var copy = new FileInfo(AsWrittenPath(path, intoFolder));
            if (!copy.Exists)
                return null;

            var file = new FileInfo(path);
            if (file.Exists && file.Length == copy.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(copy.FullName)))
                return null;

            return copy;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The suffix of the new file while it is being written - see <see cref="WriteSafely"/>.</summary>
    public const string PendingSuffix = ".pd2spc-new";

    /// <summary>How long a held file is waited for before each try again, in milliseconds.</summary>
    private static readonly int[] Waits = [20, 60, 150, 400];

    /// <summary>
    /// Puts <paramref name="bytes"/> in place of the file at <paramref name="path"/> with no
    /// moment at which a half-written file is there. The bytes go to a file beside it first, flushed to the disk, and are read back and
    /// compared; only then is it swapped in with <see cref="File.Replace(string, string, string?)"/>,
    /// one step of the file system. A crash leaves the old file or the new one, never part of one.
    /// <para>
    /// The caller keeps its own timestamped copy first, as every save does - this is the write, not
    /// the backup. A file held by another program is tried a few times, then refused, the old file
    /// untouched. What a crash leaves beside a save is cleared by <see cref="ClearPending"/>.
    /// </para>
    /// </summary>
    public static void WriteSafely(string path, byte[] bytes)
    {
        string pending = path + PendingSuffix;

        using (var file = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }

        try
        {
            if (!File.ReadAllBytes(pending).AsSpan().SequenceEqual(bytes))
                throw new IOException(L("written file did not read back. Save unchanged."));

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                        File.Replace(pending, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    else
                        File.Move(pending, path);
                    return;
                }
                catch (Exception held) when (held is IOException or UnauthorizedAccessException && attempt <= Waits.Length)
                {
                    // Held a moment by another program - an indexer, a virus scanner looking at the file
                    // just written. Asked again soon, then less soon: it is the UI thread that waits, and
                    // a flat 150 ms first wait froze one autosave in ten in a traced run.
                    // A scanner's hold can read as access denied, which is no IOException.
                    PD2SPCompanion.Core.Diagnostics.Log.Info("save", $"{Path.GetFileName(path)} held by another program, trying again ({held.Message.Trim()})");
                    System.Threading.Thread.Sleep(Waits[attempt - 1]);
                }
            }
        }
        finally
        {
            // Swapped in, it is gone already; refused, it must not be mistaken for the save. But a replace that failed
            // after moving the old file away leaves no save at all, and this is the only copy of what was to be written.
            if (File.Exists(pending))
            {
                if (File.Exists(path))
                    File.Delete(pending);
                else
                    File.Move(pending, path);
            }
        }
    }

    /// <summary>
    /// Deletes the new files a crash left beside the saves in <paramref name="folder"/> before they
    /// were swapped in. Never a save: only names ending in <see cref="PendingSuffix"/>, which are ours.
    /// Returns how many went.
    /// </summary>
    public static int ClearPending(string folder)
    {
        int cleared = 0;
        try
        {
            foreach (string stale in Directory.GetFiles(folder, "*" + PendingSuffix))
            {
                try
                {
                    File.Delete(stale);
                    cleared++;
                }
                catch (Exception)
                {
                    // Held by something: left for the next look.
                }
            }
        }
        catch (Exception)
        {
        }

        return cleared;
    }

    /// <summary>
    /// Re-reads the bytes about to be written. This catches a write that produces a file
    /// the reader cannot reproduce, which is the failure that would corrupt a character.
    /// </summary>
    private static void VerifyReadBack(byte[] written, int itemIndex, string statName, int expected)
    {
        var reread = D2SLib.Core.ReadD2S(written);

        var stat = (reread.PlayerItemList?.Items ?? [])[itemIndex].StatLists
            .SelectMany(list => list.Stats)
            .FirstOrDefault(s => string.Equals(s.Stat, statName, StringComparison.OrdinalIgnoreCase));

        if (stat is null || stat.Value != expected)
            throw new InvalidOperationException(
                $"after writing, {statName} reads back as "
                + $"{(stat is null ? "missing" : stat.Value.ToString())} instead of {expected}; refusing to write.");
    }

    /// <summary>
    /// The folder backups live in, under the save folder and named so it cannot be taken
    /// for anything the game or PlugY put there.
    /// <para>
    /// PlugY makes a folder per character directly under Save, so this keeps its own root
    /// out of the way rather than sitting alongside them.
    /// </para>
    /// </summary>
    public const string BackupFolderName = "PD2SPCompanion Backups";

    /// <summary>Where one character's backups are kept: a folder of its own.</summary>
    public static string BackupFolderFor(string path)
    {
        string saves = Path.GetDirectoryName(path) ?? ".";
        return Path.Combine(saves, BackupFolderName, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>The root every character's backup folder sits under.</summary>
    public static string BackupRoot(string saveFolder) =>
        Path.Combine(saveFolder, BackupFolderName);

    /// <summary>
    /// Copies a file aside before it is replaced, and says where the copy went.
    /// <para>
    /// Public because a character is not the only thing saved: the shared stash is written
    /// alongside it, and a second way of keeping copies would mean one of them was the
    /// wrong one. This keeps them in the same place, named the same way, prunable by the
    /// same rule.
    /// </para>
    /// </summary>
    public static string Backup(string path) => Backup(path, Moment(), null);

    /// <summary>The stamp a set of files saved together share.</summary>
    public static string Moment() => DateTime.Now.ToString("yyyyMMdd-HHmmss");

    /// <summary>
    /// Copies a file aside before it is replaced, under a given moment and optionally into
    /// somebody else's folder.
    /// <para>
    /// Files saved together share one moment and one folder, so a set of copies is
    /// recognisable as <b>one state of everything</b> rather than several that happen to be
    /// close in time. Restoring can then take the whole set - which matters, because
    /// restoring a character without the stash it was saved with puts back an item that is
    /// still in the stash as well.
    /// </para>
    /// </summary>
    public static string Backup(string path, string moment, string? intoFolder)
    {
        string folder = intoFolder ?? BackupFolderFor(path);
        Directory.CreateDirectory(folder);

        // The file's own name stays on the copy, so it is still recognisable if it is ever
        // moved or looked at outside the editor.
        string stem = Path.Combine(folder, $"{Path.GetFileName(path)}.{moment}");

        // The stamp is only accurate to the second, so two saves inside one second would
        // land on the same name. Copying never overwrites - a backup that quietly replaced
        // an earlier one would defeat the point - so the second gets a suffix.
        string backup = $"{stem}.bak";
        for (int n = 2; File.Exists(backup); n++)
            backup = $"{stem}-{n}.bak";

        File.Copy(path, backup, overwrite: false);
        return backup;
    }

    // ---- captions ------------------------------------------------------------------------------

    /// <summary>
    /// The file beside a character's backups that says what each one is: a
    /// line per backup, its file name, a tab, and its caption. Kept apart from the backups, which
    /// stay the game's own bytes.
    /// </summary>
    public const string CaptionsFile = "captions.txt";

    /// <summary>
    /// Says what a backup is, in the words of the action it was taken before - "Before: Tome of
    /// Town Portal stored in the vault." A backup holds the file as it was just before that save,
    /// so its caption says so, literally.
    /// </summary>
    public static void Caption(string backupPath, string caption)
    {
        try
        {
            string folder = Path.GetDirectoryName(backupPath) ?? ".";
            string line = $"{Path.GetFileName(backupPath)}\t{caption.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')}";
            File.AppendAllLines(Path.Combine(folder, CaptionsFile), [line]);
        }
        catch (Exception)
        {
            // A caption is a convenience; a backup without one is still the backup.
        }
    }

    /// <summary>Every caption in a backup folder, by file name - the last written for a name wins.</summary>
    public static IReadOnlyDictionary<string, string> Captions(string folder)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string file = Path.Combine(folder, CaptionsFile);
            if (!File.Exists(file))
                return found;

            foreach (string line in File.ReadAllLines(file))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0)
                    found[line[..tab]] = line[(tab + 1)..];
            }
        }
        catch (Exception)
        {
            // Unreadable: no captions, the backups as they are.
        }

        return found;
    }

    /// <summary>The caption of one backup, or empty.</summary>
    public static string CaptionOf(FileInfo backup) =>
        backup.DirectoryName is { } folder && Captions(folder).TryGetValue(backup.Name, out var caption) ? caption : "";

    /// <summary>A backup deleted: its line goes from the captions too.</summary>
    public static void ForgetCaption(FileInfo backup)
    {
        try
        {
            string file = Path.Combine(backup.DirectoryName ?? ".", CaptionsFile);
            if (!File.Exists(file))
                return;

            var lines = File.ReadAllLines(file);
            var kept = lines.Where(line => !line.StartsWith(backup.Name + "\t", StringComparison.OrdinalIgnoreCase)).ToArray();

            // The last one gone, the file goes too, so an emptied folder is empty.
            if (kept.Length == 0)
                File.Delete(file);
            else if (kept.Length != lines.Length)
                File.WriteAllLines(file, kept);
        }
        catch (Exception)
        {
            // Left: a line for a file that is gone is never shown.
        }
    }

    /// <summary>The backups of a folder renamed, from one file-name start to another: their captions go with them.</summary>
    public static void RenameCaptions(string folder, string oldStart, string newStart)
    {
        try
        {
            string file = Path.Combine(folder, CaptionsFile);
            if (!File.Exists(file))
                return;

            var lines = File.ReadAllLines(file);
            var moved = lines.Select(line => line.StartsWith(oldStart, StringComparison.OrdinalIgnoreCase) ? newStart + line[oldStart.Length..] : line).ToArray();
            if (!moved.SequenceEqual(lines))
                File.WriteAllLines(file, moved);
        }
        catch (Exception)
        {
            // A caption is a convenience; a backup without one is still the backup.
        }
    }

    /// <summary>How long every backup is kept whole, before thinning reaches it.</summary>
    public static readonly TimeSpan KeptWhole = TimeSpan.FromMinutes(15);

    /// <summary>How many backups of a file <see cref="Prune"/> would delete now.</summary>
    public static int Surplus(string path, int? keep) =>
        keep is not { } count ? 0 : BackupsOf(path) is var backups ? backups.Count - Kept(backups, Math.Max(0, count), DateTime.Now).Count : 0;

    /// <summary>What <see cref="Prune"/> keeps: every recent one, and the thinned older ones up to the count.</summary>
    private static HashSet<FileInfo> Kept(IReadOnlyList<FileInfo> newestFirst, int keep, DateTime now)
    {
        var thinned = Thinned(newestFirst, now);
        bool Recent(FileInfo file) => now - TakenAt(file) < KeptWhole;
        return [.. thinned.Where(Recent), .. thinned.Where(file => !Recent(file)).Take(keep)];
    }

    /// <summary>
    /// The backups thinning keeps, newest first (see <see cref="Prune"/>): the newest and every
    /// recent one, then the earliest in each span - ten minutes within the hour, an hour within
    /// the day, a day before.
    /// </summary>
    public static IReadOnlyList<FileInfo> Thinned(IReadOnlyList<FileInfo> newestFirst, DateTime now)
    {
        if (newestFirst.Count == 0)
            return [];

        var recent = new List<FileInfo>();
        var spans = new Dictionary<(int Tier, long Slot), FileInfo>();
        foreach (var file in newestFirst.Skip(1))
        {
            DateTime taken = TakenAt(file);
            TimeSpan age = now - taken;
            if (age < KeptWhole)
            {
                recent.Add(file);
                continue;
            }

            var (tier, length) = age < TimeSpan.FromHours(1) ? (0, TimeSpan.FromMinutes(10))
                               : age < TimeSpan.FromDays(1) ? (1, TimeSpan.FromHours(1))
                               : (2, TimeSpan.FromDays(1));

            // Newest first, so the last one seen in a span is its earliest.
            spans[(tier, taken.Ticks / length.Ticks)] = file;
        }

        return [newestFirst[0], .. recent, .. spans.Values.OrderByDescending(TakenAt)];
    }

    /// <summary>When a backup was taken: the moment in its name, or else the file's own time.</summary>
    public static DateTime TakenAt(FileInfo file)
    {
        // "PhySpearZon.d2s.20260928-211405.bak", or "-2.bak" after it.
        var match = Regex.Match(file.Name, @"\.(\d{8}-\d{6})(-\d+)?\.bak$", RegexOptions.IgnoreCase);
        return match.Success
               && DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                                         System.Globalization.DateTimeStyles.AssumeLocal, out var at)
            ? at
            : file.LastWriteTime;
    }

    /// <summary>
    /// Backups newest first, by when each was taken (see <see cref="BackupsOf"/>); within one second by the count after
    /// it, "-3.bak" before "-2.bak" before the plain ".bak", which was the first. By name the plain one sorted first, and
    /// the newest of two saves in one second was listed second.
    /// </summary>
    private static IReadOnlyList<FileInfo> NewestFirst(IEnumerable<FileInfo> files) =>
        [.. files.OrderByDescending(TakenAt).ThenByDescending(SecondsCount).ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Which save of its second a backup was: 1 for "...-211405.bak", 2 for "...-211405-2.bak".</summary>
    private static int SecondsCount(FileInfo file) =>
        Regex.Match(file.Name, @"\.\d{8}-\d{6}-(\d+)\.bak$", RegexOptions.IgnoreCase) is { Success: true } match
        && int.TryParse(match.Groups[1].Value, out int n)
            ? n
            : 1;

    /// <summary>The backups of the file named <paramref name="name"/> in a folder, newest first.</summary>
    private static IReadOnlyList<FileInfo> BackupsIn(string folder, string name)
    {
        try
        {
            var found = new DirectoryInfo(folder);
            return found.Exists ? NewestFirst(found.GetFiles($"{name}.*.bak")) : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Every backup of one character, newest first.
    /// <para>
    /// Only the character's own folder. The save folder belongs to the game and to
    /// whatever else is installed alongside it - PlugY keeps its own copies there - so
    /// reading it would offer files this editor did not write and cannot vouch for.
    /// Anything written before the grouping is moved in by
    /// <see cref="MigrateLooseBackups"/> rather than read where it lies.
    /// </para>
    /// </summary>
    public static IReadOnlyList<FileInfo> BackupsOf(string path)
    {
        try
        {
            var folder = new DirectoryInfo(BackupFolderFor(path));
            if (!folder.Exists)
                return [];

            // By when each was taken - the moment in its name (TakenAt). Not the file's write
            // time: a backup is a copy, and a copy keeps the write time of the save it copied, so
            // a save last written days ago made a new backup look old. Listed that way the picker
            // was out of order, and thinning - which keeps the first as the newest - could keep a
            // stale copy and delete a new one.
            return NewestFirst(folder.GetFiles($"{Path.GetFileName(path)}.*.bak"));
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Backups this editor wrote, by name: the character's file, a timestamp, and a
    /// suffix if two landed in the same second.
    /// <para>
    /// Deliberately strict. The save folder holds other tools' files, and moving one of
    /// those because it happened to end in .bak would be taking something that is not
    /// ours to take.
    /// </para>
    /// </summary>
    private static readonly Regex OurBackup =
        new(@"^(?<character>.+\.d2s)\.\d{8}-\d{6}(-\d+)?\.bak$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Moves backups written before they were grouped into the folder they now belong in,
    /// and reports how many moved.
    /// <para>
    /// Only ever a move, never a delete: a file that will not move, or whose place is
    /// already taken, is left exactly where it is.
    /// </para>
    /// </summary>
    public static int MigrateLooseBackups(string saveFolder)
    {
        int moved = 0;

        try
        {
            var folder = new DirectoryInfo(saveFolder);
            if (!folder.Exists)
                return 0;

            foreach (var file in folder.GetFiles("*.bak"))
            {
                var match = OurBackup.Match(file.Name);
                if (!match.Success)
                    continue;

                string character = Path.Combine(saveFolder, match.Groups["character"].Value);
                string destination = Path.Combine(BackupFolderFor(character), file.Name);

                if (File.Exists(destination))
                    continue;

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    file.MoveTo(destination);
                    moved++;
                }
                catch (Exception)
                {
                    // Open elsewhere, or read-only. Left where it is rather than lost.
                }
            }
        }
        catch (Exception)
        {
            // Nothing to move is not a failure, and neither is a folder we cannot read.
        }

        return moved;
    }

    /// <summary>
    /// Thins one file's backups by age. Every one from the last <see cref="KeptWhole"/> stays;
    /// before that, one per ten minutes for the last hour, one per hour for the last day and one
    /// per day before, of which at most <paramref name="keep"/> stay, newest first. Zero keeps the
    /// recent ones and none before; null keeps every one, thinned or not.
    /// <para>
    /// Every save takes a backup - the rule a save is never written without - and the editor
    /// saves itself after every change, so backups come by the dozen. The earliest in each span is the one kept: the
    /// state before that span's changes began, a character as it was opened among them. The
    /// recent ones are never counted against <paramref name="keep"/>: a restore's copy of the
    /// character as it was must still be there a minute later.
    /// </para>
    /// <para>
    /// Only files named after this file are considered, so a folder holding several keeps
    /// that many each rather than that many in total. <paramref name="folder"/> is where they
    /// are when not the file's own - the shared stash's copies sit beside each character's.
    /// </para>
    /// </summary>
    public static int Prune(string path, int? keep, string? folder = null)
    {
        if (keep is not { } count)
            return 0;

        var backups = folder is null ? BackupsOf(path) : BackupsIn(folder, Path.GetFileName(path));
        var kept = Kept(backups, Math.Max(0, count), DateTime.Now);

        int removed = 0;
        foreach (var file in backups.Where(file => !kept.Contains(file)))
        {
            try
            {
                file.Delete();
                ForgetCaption(file);
                removed++;
            }
            catch (Exception)
            {
                // A backup that will not delete - open elsewhere, read-only - is left be.
                // Failing a save over tidying up would be the wrong trade.
            }
        }

        return removed;
    }
}
