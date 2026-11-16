using Microsoft.Data.Sqlite;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// A collection of the older layout still on disk, to be folded into its mode's vault: its name (the
/// file's), its mode, and how many items it holds.
/// </summary>
public sealed record VaultCollectionInfo(string Name, bool Hardcore, string Path, int Items);

/// <summary>
/// A vault of a mode, on disk: its name (the file's), its mode, how many items it holds, and the play mode
/// it was last played under ("" where none was ever kept).
/// </summary>
public sealed record VaultInfo(string Name, bool Hardcore, string Path, int Items, string PlayMode);

/// <summary>
/// A season's vaults as files. <b>A mode has several vaults</b> (one to go on playing
/// self-found, another to try an end-game build in, the second's items never touching the first's grail),
/// each whole - the items and their folders, and the player's own records: the
/// trade ledger and the vault's gold - and one open at a time.
/// A vault's name is its file's.
/// <code>
/// Vaults\S13\Softcore\S13 Main.pd2lib   Playtest.pd2lib
/// Vaults\S13\Hardcore\S13 Main.pd2lib
/// </code>
/// <para>
/// The first of a mode is <see cref="MainName"/>, "S13 Main" - made empty the first time the mode is opened
/// in a season, and what every layout of before becomes:
/// </para>
/// <code>
/// Vaults\S13.pd2lib                               one file a season, the first layout      (Split)
/// Vaults\S13\Collections\&lt;name&gt;.pd2lib            collections, both modes in one folder    (Rehome)
/// Vaults\S13\Collections\Softcore\&lt;name&gt;.pd2lib   items in collections, apart from ...     (MergeCollections)
/// Vaults\S13\Profiles\Softcore.pd2lib            ... the records; one vault a mode after  (Rehouse)
/// </code>
/// <para>
/// A file's <c>about</c> kind still says "profile" for a vault, and "collection" for a collection or a
/// folder written out, as one could be before the collections were merged: every file made then carries those words.
/// An older season is only ever read where it lies, in whichever layout (<see cref="Vaults"/>,
/// <see cref="ItemSources"/>). A file from outside comes in one way (<see cref="Stage"/>): as a vault of
/// its own, or as a folder of the vault open, the player ticking which of its folders.
/// </para>
/// </summary>
public static class VaultLibrary
{
    /// <summary>
    /// What a file's <c>about</c> row says it is: a whole vault, or a folder written out of one. The
    /// values are the words of the older layouts, which every file made in them carries.
    /// </summary>
    public const string FolderKind = "collection", VaultKind = "profile";

    /// <summary>
    /// What an exported file is called by - a whole vault, and a folder of one - so a player can tell
    /// one from the other in Explorer or a shared folder, and each Import lists only its own.
    /// The files the vault keeps stay <c>.pd2lib</c>: no one browses
    /// those, though a backup or an early export is one and still imports. The <c>about</c> kind is
    /// what is actually checked.
    /// </summary>
    public const string VaultExtension = ".pd2vault", FolderExtension = ".pd2folder", StoredExtension = ".pd2lib";

    /// <summary>
    /// What exports were called in the older layouts, still listed by the Imports: a <c>.pd2profile</c>
    /// comes in as a vault, a <c>.pd2collection</c> as a folder.
    /// </summary>
    public const string OldFolderExtension = ".pd2collection", OldVaultExtension = ".pd2profile";

    /// <summary>What a mode's vault is called where a file of it has to be named: in a migration's report.</summary>
    public const string VaultName = "Vault";

    public static string SeasonFolder(string season) => Path.Combine(Vault.Folder, season);

    /// <summary>Where collections lived before each mode had a folder: the files are moved on.</summary>
    private static string SharedCollectionsFolder(string season) => Path.Combine(SeasonFolder(season), "Collections");

    public static string CollectionsFolder(string season, bool hardcore) =>
        Path.Combine(SharedCollectionsFolder(season), ModeName(hardcore));

    /// <summary>A mode's vaults: one file each, named for the vault.</summary>
    public static string VaultsFolder(string season, bool hardcore) => Path.Combine(SeasonFolder(season), ModeName(hardcore));

    /// <summary>Where a mode's one vault was kept in the layout before several were: moved on by <see cref="Rehouse"/>.</summary>
    private static string OldVaultPath(string season, bool hardcore) =>
        Path.Combine(SeasonFolder(season), "Profiles", ModeName(hardcore) + ".pd2lib");

    /// <summary>The name of a mode's first vault: the season and "Main", like S13 Main.</summary>
    public static string MainName(string season) => $"{season} Main";

    /// <summary>Where exports go by default: where the file pickers open.</summary>
    public static string ExportsFolder(string season) => Path.Combine(SeasonFolder(season), "Exports");

    public static string VaultPath(string season, bool hardcore, string name) =>
        Path.Combine(VaultsFolder(season, hardcore), name + ".pd2lib");

    /// <summary>The mode's first vault (<see cref="MainName"/>).</summary>
    public static string VaultPath(string season, bool hardcore) => VaultPath(season, hardcore, MainName(season));

    /// <summary>
    /// Where a vault's file is to be read from: its own place, or - for a season's first vault never opened
    /// since vaults were given names - where the mode's one vault was kept before.
    /// </summary>
    public static string ExistingPath(string season, bool hardcore, string name)
    {
        string path = VaultPath(season, hardcore, name);
        return !File.Exists(path) && IsMain(season, name) && File.Exists(OldVaultPath(season, hardcore))
            ? OldVaultPath(season, hardcore)
            : path;
    }

    private static bool IsMain(string season, string name) =>
        string.Equals(name, MainName(season), StringComparison.OrdinalIgnoreCase);

    /// <summary>The <c>about</c> keys a vault keeps its play mode under: the mode's name, and a custom mode's rules.</summary>
    public const string PlayModeKey = "play_mode", PlayRulesKey = "play_rules";

    /// <summary>
    /// A mode's vaults, by name. Read straight from each file, without opening it as a vault: listing is
    /// not the place to rebuild a projection. A season still in the layout of before lists its one vault
    /// under the name it will have.
    /// </summary>
    public static IReadOnlyList<VaultInfo> Vaults(string season, bool hardcore)
    {
        var paths = new List<string>();
        string folder = VaultsFolder(season, hardcore);
        if (Directory.Exists(folder))
            paths.AddRange(Directory.GetFiles(folder, "*.pd2lib"));

        string old = OldVaultPath(season, hardcore);
        bool oldToo = File.Exists(old) && !File.Exists(VaultPath(season, hardcore));

        var found = new List<VaultInfo>();
        foreach (var (name, path) in paths.Select(path => (Path.GetFileNameWithoutExtension(path), path))
                     .Concat(oldToo ? [(MainName(season), old)] : Array.Empty<(string, string)>())
                     .OrderBy(each => each.Item1, StringComparer.OrdinalIgnoreCase))
        {
            int items = 0;
            try
            {
                using var db = ReadOnly(path);
                using var count = db.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM item;";
                items = Convert.ToInt32(count.ExecuteScalar());
            }
            catch (SqliteException)
            {
                // Not a vault, or one the next open will upgrade: listed by name all the same, so it can be
                // opened - which says what is wrong with it - or deleted.
            }

            found.Add(new VaultInfo(name, hardcore, path, items, ReadAbout(path, PlayModeKey) ?? ""));
        }

        return found;
    }

    /// <summary>
    /// Moves each mode's one vault of before to its place as the mode's first by name
    /// (<see cref="MainName"/>): the file as it is, its log beside it. A mode whose first vault is there
    /// already keeps the old file where it is, to be seen to by hand. Returns how many moved.
    /// </summary>
    public static int Rehouse(string season)
    {
        int moved = 0;
        foreach (bool hardcore in new[] { false, true })
        {
            string old = OldVaultPath(season, hardcore), path = VaultPath(season, hardcore);
            if (!File.Exists(old) || File.Exists(path))
                continue;

            SqliteConnection.ClearAllPools();
            Directory.CreateDirectory(VaultsFolder(season, hardcore));
            foreach (string file in FilesOf(old))
                File.Move(file, path + file[old.Length..]);
            moved++;
        }

        string folder = Path.GetDirectoryName(OldVaultPath(season, false))!;
        if (moved > 0 && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);

        return moved;
    }

    public static string CollectionPath(string season, bool hardcore, string name) =>
        Path.Combine(CollectionsFolder(season, hardcore), name + ".pd2lib");

    private static string ModeName(bool hardcore) => hardcore ? "Hardcore" : "Softcore";

    /// <summary>Whether a folder's name is a mode's (<see cref="VaultsFolder"/>).</summary>
    internal static bool IsModeName(string name) =>
        name.Equals(ModeName(false), StringComparison.OrdinalIgnoreCase) || name.Equals(ModeName(true), StringComparison.OrdinalIgnoreCase);

    /// <summary>The collection each mode started with: its items are the vault's own, where the others' become a folder's.</summary>
    public const string DefaultCollection = "Default";

    /// <summary>
    /// A file that holds a vault's items, and the folder path they belong under in it: the vault's own
    /// file, and - for a season's first vault never opened since collections were folded in - each
    /// collection the mode still has, the Default's at the top and any other under its own name.
    /// </summary>
    public sealed record ItemSource(string Name, string Path, IReadOnlyList<string> Under);

    public static IReadOnlyList<ItemSource> ItemSources(string season, bool hardcore, string name)
    {
        var sources = new List<ItemSource>();
        string vault = ExistingPath(season, hardcore, name);
        if (File.Exists(vault))
            sources.Add(new ItemSource(VaultName, vault, []));

        if (IsMain(season, name))
        {
            foreach (var collection in Collections(season, hardcore))
                sources.Add(new ItemSource(collection.Name, collection.Path, UnderFor(collection.Name)));
        }

        return sources;
    }

    private static IReadOnlyList<string> UnderFor(string collection) =>
        string.Equals(collection, DefaultCollection, StringComparison.OrdinalIgnoreCase) ? [] : [collection];

    /// <summary>Whether a mode still has collections beside its vault: files of the older layout, to fold in.</summary>
    public static bool NeedsMerge(string season, bool hardcore) => Collections(season, hardcore).Count > 0;

    /// <summary>What <see cref="MergeCollections"/> did.</summary>
    public sealed record MergeReport(int Collections, int Items, int Failed, string Backup, string Retired);

    /// <summary>
    /// Folds a mode's collections into its first vault: the Default's items and folders where they lay, every
    /// other collection as a top folder of its name. Done on a copy of the vault, which takes the vault's
    /// place only when every collection has been read - a failure leaves everything as it was. The vault
    /// as it stood is copied to Backups first, and the collections' files are moved there after, never
    /// deleted. The caller has closed the vault.
    /// </summary>
    public static MergeReport MergeCollections(string season, bool hardcore, int statIdBits)
    {
        string path = VaultPath(season, hardcore);
        string merging = path + ".merging", reading = path + ".merging-source";
        var collections = Collections(season, hardcore);

        SqliteConnection.ClearAllPools();
        foreach (string stale in FilesOf(merging).Concat(FilesOf(reading)))
            File.Delete(stale);

        int items = 0, failed = 0;
        string backup;
        try
        {
            using (var current = OpenVault(season, hardcore, statIdBits))
                current.CopyTo(merging);

            using (var merged = Vault.Open(merging, season, statIdBits))
            {
                foreach (var collection in collections)
                {
                    // From a copy: opening a file as a vault upgrades it, and these are kept as they are.
                    CopyForReading(collection.Path, reading);
                    using (var source = Vault.Open(reading, season, statIdBits))
                    {
                        var (added, lost) = merged.Absorb(source, UnderFor(collection.Name));
                        items += added;
                        failed += lost;
                    }

                    SqliteConnection.ClearAllPools();
                    foreach (string file in FilesOf(reading))
                        File.Delete(file);
                }

                merged.Describe(VaultKind, hardcore);
                merged.SetAbout("collections_merged", DateTimeOffset.UtcNow.ToString("O"));
            }

            using (var current = OpenVault(season, hardcore, statIdBits))
                backup = current.BackUp("before-merge");

            // One clean file in the vault's place, no log beside it.
            string whole = merging + ".whole";
            using (var merged = Vault.Open(merging, season, statIdBits))
                merged.CopyTo(whole);

            // The whole file over the vault in one step: until it is there the vault stands, and a failure here leaves
            // it as it was. Its log goes only then.
            SqliteConnection.ClearAllPools();
            File.Move(whole, path, overwrite: true);
            foreach (string file in FilesOf(path).Where(file => file != path).Concat(FilesOf(merging)))
                File.Delete(file);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            foreach (string file in FilesOf(merging).Concat(FilesOf(reading)).Concat(FilesOf(merging + ".whole")))
            {
                try { File.Delete(file); } catch (IOException) { }
            }

            throw;
        }

        // The collections, out of the way and kept.
        string retired = Path.Combine(Vault.BackupFolder, $"Collections merged {DateTime.Now:yyyyMMdd-HHmmss}", ModeName(hardcore));
        Directory.CreateDirectory(retired);
        foreach (var collection in collections)
        {
            foreach (string file in FilesOf(collection.Path))
                File.Move(file, Path.Combine(retired, Path.GetFileName(file)));
        }

        return new MergeReport(collections.Count, items, failed, backup, retired);
    }

    /// <summary>
    /// A vault file chosen to import, as a copy of its own to read: what it is called, and what folders and
    /// items it holds - for the check list a player ticks before it comes in as a folder
    /// (one Import, which asks where the file goes). Only the copy is opened: opening a vault
    /// upgrades and re-projects it in place, which is not for someone's export. Disposing removes the copy.
    /// </summary>
    public sealed class Incoming : IDisposable
    {
        internal Incoming(string name, string staging, Vault vault)
        {
            Name = name;
            Staging = staging;
            Vault = vault;
            Folders = vault.Folders();
            ItemsPerFolder = vault.ItemsPerFolder();
            Items = vault.Count();
            TopItems = Items - ItemsPerFolder.Values.Sum();
        }

        /// <summary>The file's name without its extension, cut to the longest a vault or folder may have.</summary>
        public string Name { get; }

        internal string Staging { get; }

        internal Vault Vault { get; }

        public IReadOnlyList<VaultFolder> Folders { get; }

        public IReadOnlyDictionary<long, int> ItemsPerFolder { get; }

        /// <summary>Every item in the file, and those lying at its top, in no folder.</summary>
        public int Items { get; }

        public int TopItems { get; }

        public void Dispose()
        {
            Vault.Dispose();
            SqliteConnection.ClearAllPools();
            foreach (string file in FilesOf(Staging))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// Stages <paramref name="source"/> to import: a vault, a folder written out of an older layout or a
    /// collection of before - one kind of file. Refused, with nothing left behind, where it is another
    /// season's or not a vault file.
    /// </summary>
    public static Incoming Stage(string source, string season, int statIdBits, string? name = null)
    {
        string staging = Path.Combine(Path.GetTempPath(), $"{Vault.StagedPrefix}{Guid.NewGuid():N}.pd2lib");
        CopyIn(source, staging, season, statIdBits);

        // Named for its file - or as the caller names it: a vault's backup is "Name.why.when", and comes back
        // as the vault it is a copy of.
        string wanted = name ?? Path.GetFileNameWithoutExtension(source);
        return new Incoming(wanted.Length > LongestName ? wanted[..LongestName] : wanted, staging,
                            Vault.Open(staging, season, statIdBits));
    }

    /// <summary>
    /// Adds a staged file to the mode's vaults, never over one: whole - items, folders and records - under
    /// a free name like the file's. Returns the name it was given.
    /// </summary>
    public static string AddAsVault(Incoming incoming, string season, bool hardcore, int statIdBits)
    {
        string name = FreeName(season, hardcore, incoming.Name);
        string target = VaultPath(season, hardcore, name);
        Directory.CreateDirectory(VaultsFolder(season, hardcore));
        incoming.Vault.CopyTo(target);

        using var added = Vault.Open(target, season, statIdBits);
        added.Describe(VaultKind, hardcore);
        return name;
    }

    /// <summary>
    /// Brings a staged file's items into <paramref name="vault"/> under a new top folder named for the file,
    /// never over a folder there: a taken name gets a number. <paramref name="takes"/> is the check list -
    /// which of the file's folders give their items, asked by a folder's id and, for the items at the
    /// file's top, as null; every one where none is given. No record comes with them - not the ledger, not gold
    /// (the made-by-the-editor marks of the items came too, until format 20). Returns the folder's name and how
    /// many items came in and did not.
    /// </summary>
    public static (string Folder, int Added, int Failed) AddAsFolder(Vault vault, Incoming incoming, Func<long?, bool>? takes = null)
    {
        string name = vault.FreeName(null, incoming.Name);
        var (added, failed) = vault.Absorb(incoming.Vault, [name], takes);
        return (name, added, failed);
    }

    /// <summary>The longest name a vault may have, or a folder an import makes: a file's name may be longer.</summary>
    public const int LongestName = 40;

    /// <summary>Opens one of a mode's vaults, making it, empty, the first time.</summary>
    public static Vault OpenVault(string season, bool hardcore, string name, int statIdBits) =>
        OpenVaultAt(VaultPath(season, hardcore, name), season, hardcore, statIdBits);

    /// <summary>The same at a path of the caller's: a copy, to be put in a vault's place.</summary>
    public static Vault OpenVaultAt(string path, string season, bool hardcore, int statIdBits)
    {
        var vault = Vault.Open(path, season, statIdBits);
        if (vault.Kind != VaultKind || vault.IsHardcore != hardcore)
            vault.Describe(VaultKind, hardcore);

        return vault;
    }

    /// <summary>Opens a mode's first vault (<see cref="MainName"/>), making it, empty, the first time.</summary>
    public static Vault OpenVault(string season, bool hardcore, int statIdBits) =>
        OpenVault(season, hardcore, MainName(season), statIdBits);

    /// <summary>
    /// Why a vault's name will not do, or null. The name is a file's, so it keeps to what a file name
    /// allows, and to a length a list can show.
    /// </summary>
    public static string? RefuseName(string season, bool hardcore, string name, string? except = null)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
            return L("Require name.");

        if (trimmed.Length > LongestName)
            return L($"At most {LongestName} characters.");

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmed.EndsWith('.'))
            return L("Invalid file name.");

        bool taken = Vaults(season, hardcore).Any(each =>
            string.Equals(each.Name, trimmed, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(each.Name, except, StringComparison.OrdinalIgnoreCase));

        return taken ? L("Name already used.") : null;
    }

    /// <summary>A name like <paramref name="wanted"/> no vault of the mode has: "Pack", "Pack (2)", ...</summary>
    public static string FreeName(string season, bool hardcore, string wanted)
    {
        var taken = Vaults(season, hardcore).Select(each => each.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(wanted))
            return wanted;

        for (int n = 2; ; n++)
        {
            string next = $"{wanted} ({n})";
            if (!taken.Contains(next))
                return next;
        }
    }

    /// <summary>
    /// Makes a vault of the mode: empty, or a copy of <paramref name="copyOf"/> - everything in it, the
    /// records too. The name has been checked
    /// (<see cref="RefuseName"/>). The one copied may be open: it is read, not touched.
    /// </summary>
    public static void CreateVault(string season, bool hardcore, string name, string? copyOf, int statIdBits)
    {
        string path = VaultPath(season, hardcore, name.Trim());
        if (File.Exists(path))
            throw new InvalidOperationException(L("Name already used."));

        Directory.CreateDirectory(VaultsFolder(season, hardcore));
        if (copyOf is not null)
            CopyForReading(VaultPath(season, hardcore, copyOf), path);

        using var made = OpenVault(season, hardcore, name.Trim(), statIdBits);
    }

    /// <summary>
    /// Deletes a vault's file, copying it to Backups first as emptying one does. The caller has made sure
    /// it is not the one open. Returns where the copy went. A file that will not open - damaged, or from a newer
    /// build - goes to Backups itself, as it is: it stays listed so it can be deleted (<see cref="Vaults"/>).
    /// </summary>
    public static string DeleteVault(string season, bool hardcore, string name, int statIdBits)
    {
        string path = VaultPath(season, hardcore, name);
        string backup;
        try
        {
            using var doomed = Vault.Open(path, season, statIdBits);
            backup = doomed.BackUp("deleted");
        }
        catch (Exception problem) when (problem is SqliteException or InvalidOperationException)
        {
            Log.Caught(problem);
            return MoveToBackups(path);
        }

        SqliteConnection.ClearAllPools();
        foreach (string file in FilesOf(path))
            File.Delete(file);

        return backup;
    }

    /// <summary>A vault's file moved to Backups under a deleted copy's name, as it is, its log and index with it.</summary>
    private static string MoveToBackups(string path)
    {
        SqliteConnection.ClearAllPools();

        string target = Vault.BackupTarget(path, "deleted");
        foreach (string file in FilesOf(path).ToList())
            File.Move(file, target + file[path.Length..]);

        return target;
    }

    /// <summary>Renames a vault's file. The caller has checked the name and made sure the vault is not open.</summary>
    public static void RenameVault(string season, bool hardcore, string name, string newName)
    {
        SqliteConnection.ClearAllPools();
        string from = VaultPath(season, hardcore, name), to = VaultPath(season, hardcore, newName.Trim());

        foreach (string file in FilesOf(from))
            File.Move(file, to + file[from.Length..]);
    }

    /// <summary>
    /// Opens one of a mode's collections of the older layout, making it - empty - the first time: for
    /// what makes an old layout to fold in or migrate - the tests, and the command line's sample of an
    /// older season. The editor opens none.
    /// </summary>
    public static Vault OpenCollection(string season, bool hardcore, string name, int statIdBits)
    {
        var collection = Vault.Open(CollectionPath(season, hardcore, name), season, statIdBits);
        if (collection.Kind != FolderKind || collection.IsHardcore != hardcore)
            collection.Describe(FolderKind, hardcore);

        return collection;
    }

    /// <summary>
    /// A mode's collections, by name. Read straight from each file, without opening it as a
    /// vault: listing is not the place to rebuild a projection.
    /// </summary>
    public static IReadOnlyList<VaultCollectionInfo> Collections(string season, bool hardcore)
    {
        string folder = CollectionsFolder(season, hardcore);
        if (!Directory.Exists(folder))
            return [];

        var found = new List<VaultCollectionInfo>();
        foreach (string path in Directory.GetFiles(folder, "*.pd2lib").Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            try
            {
                using var db = ReadOnly(path);
                using var count = db.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM item;";
                found.Add(new VaultCollectionInfo(name, hardcore, path, Convert.ToInt32(count.ExecuteScalar())));
            }
            catch (SqliteException)
            {
                // Not a vault, or one the next open will upgrade: listed by name all the same, and the
                // folding in says what is wrong with it.
                found.Add(new VaultCollectionInfo(name, hardcore, path, 0));
            }
        }

        return found;
    }

    private static SqliteConnection ReadOnly(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        db.Open();
        return db;
    }

    /// <summary>
    /// A character renamed, in every file of the season but <paramref name="open"/> - the vault held open,
    /// which the caller renames through its own connection: the other vaults, both modes, in the records
    /// that name it. An item says nothing of who stored it, so no item is touched. Returns the rows changed.
    /// </summary>
    public static int RenameCharacter(string season, string oldName, string newName, int statIdBits, string? open = null)
    {
        int changed = 0;
        foreach (bool hardcore in new[] { false, true })
        {
            foreach (var each in Vaults(season, hardcore))
            {
                if (string.Equals(each.Path, open, StringComparison.OrdinalIgnoreCase))
                    continue;

                using var vault = Vault.Open(each.Path, season, statIdBits);
                changed += vault.RenameCharacter(oldName, newName);
            }
        }

        return changed;
    }

    /// <summary>
    /// A copy of a vault file to read from, made without opening the file as a vault: opening runs its
    /// upgrades, and an upgrade that derives anything from the installed tables would derive it from
    /// another season's. The season migration reads only copies, so the season it migrates from is never
    /// changed.
    /// </summary>
    public static void CopyForReading(string path, string target)
    {
        if (File.Exists(target))
            File.Delete(target);

        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        db.Open();

        using var copy = db.CreateCommand();
        copy.CommandText = "VACUUM INTO $target;";
        copy.Parameters.AddWithValue("$target", target);
        copy.ExecuteNonQuery();
    }

    /// <summary>
    /// One of a vault file's <c>about</c> values, read without opening it as a vault (see
    /// <see cref="CopyForReading"/>): asking an older season's file what it was migrated from must not
    /// upgrade it. Null where the file, its table or the key is missing.
    /// </summary>
    public static string? ReadAbout(string path, string key)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            db.Open();

            using var read = db.CreateCommand();
            read.CommandText = "SELECT value FROM about WHERE key = $key;";
            read.Parameters.AddWithValue("$key", key);
            return read.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a season still has its vault as the one file of before collections, not yet split.
    /// </summary>
    public static bool NeedsSplit(string season) =>
        File.Exists(Vault.PathFor(season)) && !Directory.Exists(SeasonFolder(season));

    /// <summary>
    /// Splits the season's one-file vault of before collections (the first layout) into the two files of the
    /// layout that followed, for the mode its characters played (<paramref name="hardcore"/>): the
    /// records in the mode's vault file, the items in its Default collection - which
    /// <see cref="MergeCollections"/> then folds back in, by the one way every old layout comes in. The
    /// file itself is copied aside and then removed, so it is not split twice. Returns where the copy
    /// went.
    /// </summary>
    public static string Split(string season, bool hardcore, int statIdBits)
    {
        string legacy = Vault.PathFor(season);
        string first = CollectionPath(season, hardcore, DefaultCollection);
        string backup;

        using (var old = Vault.Open(legacy, season, statIdBits))
        {
            backup = old.BackUp("before-collections");
            old.CopyTo(VaultPath(season, hardcore));
            old.CopyTo(first);
        }

        using (var records = Vault.Open(VaultPath(season, hardcore), season, statIdBits))
        {
            records.DropItems();
            records.Describe(VaultKind, hardcore);
        }

        using (var collection = Vault.Open(first, season, statIdBits))
        {
            collection.DropRecords();
            collection.Describe(FolderKind, hardcore);
        }

        SqliteConnection.ClearAllPools();
        foreach (string file in FilesOf(legacy))
            File.Delete(file);

        return backup;
    }

    /// <summary>
    /// Moves collections from the one folder they shared in their first layout, into their
    /// mode's own - each by the mode its <c>about</c> row says - and renames the mode's first one,
    /// then called "Softcore" or "Hardcore", to <see cref="DefaultCollection"/>. A name the mode's
    /// folder has already keeps its file where it is, to be seen to by hand. Nothing is copied or
    /// opened as a vault: the files move as they are, the log beside them. Returns how many moved.
    /// </summary>
    public static int Rehome(string season)
    {
        string shared = SharedCollectionsFolder(season);
        if (!Directory.Exists(shared))
            return 0;

        string[] loose = Directory.GetFiles(shared, "*.pd2lib");
        if (loose.Length == 0)
            return 0;

        SqliteConnection.ClearAllPools();
        int moved = 0;

        foreach (string path in loose)
        {
            bool hardcore;
            try
            {
                using var db = ReadOnly(path);
                using var mode = db.CreateCommand();
                mode.CommandText = "SELECT value FROM about WHERE key = 'mode';";
                hardcore = (mode.ExecuteScalar() as string) == "hardcore";
            }
            catch (SqliteException)
            {
                continue;
            }

            SqliteConnection.ClearAllPools();

            string name = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, ModeName(hardcore), StringComparison.OrdinalIgnoreCase))
                name = DefaultCollection;

            string target = CollectionPath(season, hardcore, name);
            if (File.Exists(target))
                continue;

            Directory.CreateDirectory(CollectionsFolder(season, hardcore));
            foreach (string file in FilesOf(path))
                File.Move(file, target + file[path.Length..]);

            moved++;
        }

        return moved;
    }

    /// <summary>
    /// Copies a vault file from outside - with its log, where one was left beside it - to
    /// <paramref name="target"/>, and checks the copy is this season's. Only the copy is opened: opening a
    /// vault upgrades and re-projects it in place, which is not for someone's export. What the file calls
    /// itself is not asked: a vault, a folder written out and a collection are one kind of file, and the
    /// import asks the player what to make of it.
    /// </summary>
    private static void CopyIn(string source, string target, string season, int statIdBits)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
        SqliteConnection.ClearAllPools();
        foreach (string stale in FilesOf(target))
            File.Delete(stale);

        foreach (string file in FilesOf(source))
            File.Copy(file, target + file[source.Length..]);

        string? refused;
        try
        {
            using var copy = Vault.Open(target, season, statIdBits);
            refused = copy.MatchesSeason(season) ? null : L($"{copy.Season} vault, require {season}.");
        }
        catch (SqliteException)
        {
            refused = L("Not a vault file.");
        }
        catch (InvalidOperationException problem)
        {
            // From a newer build: refused in its own words, with nothing left behind.
            refused = problem.Message;
        }

        if (refused is not null)
        {
            SqliteConnection.ClearAllPools();
            foreach (string file in FilesOf(target))
                File.Delete(file);

            throw new InvalidOperationException(refused);
        }
    }

    /// <summary>A vault file with the log and index SQLite keeps beside it in WAL mode.</summary>
    private static IEnumerable<string> FilesOf(string path) =>
        new[] { path, path + "-wal", path + "-shm" }.Where(File.Exists);
}
