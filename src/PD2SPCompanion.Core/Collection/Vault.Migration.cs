using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Collection;

// What migrating a season's vault into the next one reads and writes (SeasonMigration): a file's every row, folder link and record, read whole, and
// an item written in with the row it was.
public sealed partial class Vault
{
    /// <summary>Every item's key, oldest first.</summary>
    public IReadOnlyList<long> AllKeys()
    {
        var keys = new List<long>();
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT key FROM item ORDER BY added_at, key;";
        using var row = read.ExecuteReader();
        while (row.Read())
            keys.Add(row.GetInt64(0));
        return keys;
    }

    /// <summary>
    /// An item migrated in from another season's vault, kept as the row it was there - its identity and
    /// when it was added: its stay goes on - in <paramref name="folder"/>.
    /// The item is this season's already (fitted); its blob is written in this season's encoding and
    /// read back first, as every deposit is. The new key, or null with <paramref name="refused"/>.
    /// </summary>
    public long? AddMigrated(Item item, VaultRow was, long? folder, out string? refused)
    {
        if (was.OriginId is { } origin)
            item.Id = origin;

        if (Capture(item, out refused) is not { } captured)
            return null;

        long key = AddCaptured([(captured, folder)]) is [var added] ? added : 0;
        if (key == 0)
        {
            refused = L("refused by vault");
            return null;
        }

        SetAdded(key, was.Added);
        return key;
    }

    /// <summary>
    /// Takes in what another vault file of this season holds - its items as the rows they were (identity,
    /// when each was added, where it lay), its folders, the empty ones too, and its
    /// smart-sort links where this file has none for the category - under the folder path
    /// <paramref name="under"/>, or at the top for an empty one. With <paramref name="takes"/>, only the
    /// folders it says yes to give their items - asked of each folder of the source by its id, and of the
    /// source's top level as null - and only those are made here, with the folders above them. How a
    /// collection of the older layout is folded into a vault, and how a vault's file is brought in as a
    /// folder. Returns how many items came in, and how many would not be read or written.
    /// <para>
    /// <b>A folder's lock comes with it</b> - onto a folder this made. A
    /// folder that was here already, the incoming one's items joining it, keeps the lock it has or has not:
    /// what was arranged here is the player's of here. The source's top, locked, locks the folder it comes
    /// in as.
    /// </para>
    /// </summary>
    public (int Added, int Failed) Absorb(Vault source, IReadOnlyList<string> under, Func<long?, bool>? takes = null)
    {
        var theirs = source.Folders().ToDictionary(folder => folder.Id);

        // A folder's path here: under the place given.
        IReadOnlyList<string> PathOf(long? id)
        {
            var path = new List<string>();
            for (var at = id; at is { } folder && theirs.TryGetValue(folder, out var each); at = each.ParentId)
                path.Insert(0, each.Name);
            return [.. under, .. path];
        }

        var made = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var fresh = new HashSet<long>();
        long? FolderAt(IReadOnlyList<string> path)
        {
            long? parent = null;
            string key = "";
            foreach (string level in path)
            {
                key += "/" + level;
                if (!made.TryGetValue(key, out long id))
                {
                    if (Folders().FirstOrDefault(folder => folder.ParentId == parent
                                                   && string.Equals(folder.Name, level, StringComparison.OrdinalIgnoreCase)) is { } there)
                    {
                        id = there.Id;
                    }
                    else
                    {
                        id = CreateFolder(parent, level);
                        fresh.Add(id);
                    }

                    made[key] = id;
                }

                parent = id;
            }

            return parent;
        }

        var locked = source.LockedFolders();
        if (FolderAt(under) is { } top && fresh.Contains(top) && source.IsLocked(null) && takes?.Invoke(null) != false)
            SetLocked(top, true);

        foreach (var folder in theirs.Values.Where(folder => takes?.Invoke(folder.Id) != false))
        {
            if (FolderAt(PathOf(folder.Id)) is { } here && fresh.Contains(here) && locked.Contains(folder.Id))
                SetLocked(here, true);
        }

        int added = 0, failed = 0;
        foreach (var row in source.RowsOf(source.AllKeys()))
        {
            if (takes?.Invoke(row.FolderId) == false)
                continue;

            Item item;
            try
            {
                item = ItemBlob.Restore(row.Blob, StatIdBits);
            }
            catch (Exception)
            {
                failed++;
                continue;
            }

            if (AddMigrated(item, row, FolderAt(PathOf(row.FolderId)), out _) is null)
                failed++;
            else
                added++;
        }

        var linked = FolderLinks().Select(link => link.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, folderId) in source.FolderLinks())
        {
            if (!linked.Contains(category) && takes?.Invoke(folderId) != false && theirs.ContainsKey(folderId)
                && PathOf(folderId) is { Count: > 0 } path && FolderAt(path) is { } here)
            {
                LinkFolder(category, here);
            }
        }

        return (added, failed);
    }

    /// <summary>Which folder smart sort delivers each category to.</summary>
    public IReadOnlyList<(string Category, long FolderId)> FolderLinks()
    {
        var links = new List<(string, long)>();
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT category, folder_id FROM folder_link;";
        using var row = read.ExecuteReader();
        while (row.Read())
            links.Add((row.GetString(0), row.GetInt64(1)));
        return links;
    }

    /// <summary>Sends a smart-sort category to a folder.</summary>
    public void LinkFolder(string category, long folderId)
    {
        using var link = _db.CreateCommand();
        link.CommandText = "INSERT OR REPLACE INTO folder_link (category, folder_id) VALUES ($category, $id);";
        link.Parameters.AddWithValue("$category", category);
        link.Parameters.AddWithValue("$id", folderId);
        link.ExecuteNonQuery();
    }
}
