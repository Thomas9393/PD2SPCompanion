using Microsoft.Data.Sqlite;

namespace PD2SPCompanion.Core.Collection;

// The vault's folders.
public sealed partial class Vault
{
    // ---------------------------------------------------------------------------------------
    // Folders. Explorer's rules: an item is in exactly one folder, a folder's name is unique
    // among its siblings ignoring case, and a folder cannot go inside itself. See the design
    // note's "Folders" section for why the column is on item and what NULL means.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Every folder, alphabetical. Read whole: even a large library has hundreds of folders,
    /// not thousands, so paths, subtrees and counts are worked out in memory.
    /// </summary>
    public IReadOnlyList<VaultFolder> Folders()
    {
        var found = new List<VaultFolder>();

        using var read = _db.CreateCommand();
        read.CommandText = "SELECT id, parent_id, name FROM folder ORDER BY name COLLATE NOCASE, id;";
        using var row = read.ExecuteReader();

        while (row.Read())
            found.Add(new VaultFolder(row.GetInt64(0), row.IsDBNull(1) ? null : row.GetInt64(1), row.GetString(2)));

        return found;
    }

    /// <summary>How many items each folder holds itself. A folder holding none is absent.</summary>
    public IReadOnlyDictionary<long, int> ItemsPerFolder()
    {
        var counts = new Dictionary<long, int>();

        using var read = _db.CreateCommand();
        read.CommandText = "SELECT folder_id, COUNT(*) FROM item WHERE folder_id IS NOT NULL GROUP BY folder_id;";
        using var row = read.ExecuteReader();

        while (row.Read())
            counts[row.GetInt64(0)] = row.GetInt32(1);

        return counts;
    }

    /// <summary>
    /// A name no sibling in <paramref name="parent"/> has, Explorer's way: <c>Runes</c>, then
    /// <c>Runes (2)</c>, <c>Runes (3)</c>.
    /// </summary>
    public string FreeName(long? parent, string wanted, long? except = null) =>
        FreeName(Siblings(parent, except), wanted);

    private static string FreeName(ICollection<string> taken, string wanted)
    {
        if (!taken.Contains(wanted))
            return wanted;

        for (int n = 2; ; n++)
        {
            string tried = $"{wanted} ({n})";
            if (!taken.Contains(tried))
                return tried;
        }
    }

    private HashSet<string> Siblings(long? parent, long? except = null) =>
        Folders()
            .Where(each => each.ParentId == parent && each.Id != except)
            .Select(each => each.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Makes a folder. Refuses an empty name or one a sibling already has.</summary>
    public long CreateFolder(long? parent, string name)
    {
        name = Named(name);

        if (parent is { } under && !Exists(under))
            throw new InvalidOperationException(L("Folder not found."));

        if (Siblings(parent).Contains(name))
            throw new InvalidOperationException(L($"{name} already exists."));

        using var add = _db.CreateCommand();
        add.CommandText = """
            INSERT INTO folder (parent_id, name) VALUES ($parent, $name);
            SELECT last_insert_rowid();
            """;
        add.Parameters.AddWithValue("$parent", Db(parent));
        add.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(add.ExecuteScalar());
    }

    /// <summary>Renames a folder. A change of case alone is allowed, as in Explorer.</summary>
    public void RenameFolder(long id, string name)
    {
        name = Named(name);
        var folder = FolderOf(id);

        if (Siblings(folder.ParentId, except: id).Contains(name))
            throw new InvalidOperationException(L($"{name} already exists."));

        using var write = _db.CreateCommand();
        write.CommandText = "UPDATE folder SET name = $name WHERE id = $id;";
        write.Parameters.AddWithValue("$name", name);
        write.Parameters.AddWithValue("$id", id);
        write.ExecuteNonQuery();
    }

    /// <summary>
    /// Puts a folder inside another, or at the top level. Refuses a folder going inside itself
    /// or one of its own subfolders - nothing in the database can stop that loop, and a loop
    /// is a folder that disappears from the tree. A name already taken there gets the
    /// <c>(2)</c> treatment, as deleting a folder does. Returns the name it ended up with.
    /// </summary>
    public string MoveFolder(long id, long? parent)
    {
        var folder = FolderOf(id);

        if (parent is { } under)
        {
            if (!Exists(under))
                throw new InvalidOperationException(L("Folder not found."));

            if (Subtree(id).Contains(under))
                throw new InvalidOperationException(L($"{folder.Name} cannot go inside itself."));
        }

        if (folder.ParentId == parent)
            return folder.Name;

        string name = FreeName(parent, folder.Name);

        using var write = _db.CreateCommand();
        write.CommandText = "UPDATE folder SET parent_id = $parent, name = $name WHERE id = $id;";
        write.Parameters.AddWithValue("$parent", Db(parent));
        write.Parameters.AddWithValue("$name", name);
        write.Parameters.AddWithValue("$id", id);
        write.ExecuteNonQuery();

        return name;
    }

    /// <summary>What deleting a folder did.</summary>
    /// <param name="Items">Items moved to the top, or deleted.</param>
    /// <param name="Folders">Folders moved to the top, or deleted.</param>
    /// <param name="Renamed">Of the folders moved to the top, how many took another name there: <c>Name (2)</c>.</param>
    public sealed record FolderRemoval(int Items, int Folders, int Renamed = 0);

    /// <summary>
    /// Deletes a folder.
    /// <para>
    /// <paramref name="everything"/> false - "delete the folder only": its own items
    /// and its subfolders, with whatever they hold, go to the <b>root</b>, not to the parent,
    /// and a subfolder whose name the root already has becomes <c>Name (2)</c>. Nothing is
    /// deleted but the one folder. <see cref="FolderRemoval"/> counts what moved.
    /// </para>
    /// <para>
    /// <paramref name="everything"/> true - the folder, every folder beneath it and every item
    /// in them. <see cref="FolderRemoval"/> counts what was deleted.
    /// </para>
    /// </summary>
    public FolderRemoval DeleteFolder(long id, bool everything)
    {
        _ = FolderOf(id);

        // Worked out before the transaction opens: reads made through it and outside it would
        // not see the same thing.
        var within = Subtree(id);
        var children = Folders().Where(each => each.ParentId == id).ToList();
        var atRoot = Siblings(null);

        using var transaction = _db.BeginTransaction();

        FolderRemoval done;

        if (everything)
        {
            string ids = string.Join(",", within);

            // Items first: the folders cannot go while anything still points at them.
            int items = Execute(transaction, $"DELETE FROM item WHERE folder_id IN ({ids});");
            int folders = Execute(transaction, $"DELETE FROM folder WHERE id IN ({ids});");
            done = new FolderRemoval(items, folders);
        }
        else
        {
            int items = Execute(transaction, $"UPDATE item SET folder_id = NULL WHERE folder_id = {id};");

            int renamed = 0;
            foreach (var child in children)
            {
                string name = FreeName(atRoot, child.Name);
                atRoot.Add(name);
                if (name != child.Name)
                    renamed++;

                using var lift = _db.CreateCommand();
                lift.Transaction = transaction;
                lift.CommandText = "UPDATE folder SET parent_id = NULL, name = $name WHERE id = $id;";
                lift.Parameters.AddWithValue("$name", name);
                lift.Parameters.AddWithValue("$id", child.Id);
                lift.ExecuteNonQuery();
            }

            Execute(transaction, $"DELETE FROM folder WHERE id = {id};");
            done = new FolderRemoval(items, children.Count, renamed);
        }

        // Its links go with it: the next item of the category makes the folder again.
        Execute(transaction, "DELETE FROM folder_link WHERE folder_id NOT IN (SELECT id FROM folder);");

        // And its lock: an id is given out again, and the folder that came by it would begin locked.
        Execute(transaction, $"DELETE FROM about WHERE key LIKE '{LockKeys}%' AND key <> '{LockKeys}top' "
                             + $"AND CAST(substr(key, {LockKeys.Length + 1}) AS INTEGER) NOT IN (SELECT id FROM folder);");

        // And its pin, for the same reason.
        DropGonePins(transaction);

        transaction.Commit();
        return done;
    }

    /// <summary>Takes the folders that are no more off quick access, the rest staying in the order pinned.</summary>
    private void DropGonePins(SqliteTransaction transaction)
    {
        using var read = _db.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = $"SELECT value FROM about WHERE key = '{QuickKey}';";
        if (read.ExecuteScalar() is not string pinned)
            return;

        var kept = new List<string>();
        foreach (string part in pinned.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            using var there = _db.CreateCommand();
            there.Transaction = transaction;
            there.CommandText = "SELECT 1 FROM folder WHERE id = $id;";
            there.Parameters.AddWithValue("$id", part);
            if (there.ExecuteScalar() is not null)
                kept.Add(part);
        }

        using var write = _db.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = $"UPDATE about SET value = $value WHERE key = '{QuickKey}';";
        write.Parameters.AddWithValue("$value", string.Join(",", kept));
        write.ExecuteNonQuery();
    }

    // ---- quick access, a section for fast access as in Windows Explorer -------
    // The folders the player pinned, in the order pinned: listed over the folder tree, a click from anywhere. Kept
    // in the vault's own file, one row of "about" ("quick" = "12,40,7"), so they go where the vault goes and need no
    // change to the tables. A folder deleted is taken off it in the same step: an id is given out again.

    private const string QuickKey = "quick";

    /// <summary>The folders pinned to quick access that are still there, in the order they were pinned.</summary>
    public IReadOnlyList<long> QuickAccess()
    {
        var known = Folders().Select(folder => folder.Id).ToHashSet();
        var pinned = new List<long>();
        foreach (string part in (About(QuickKey) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long id)
                && known.Contains(id) && !pinned.Contains(id))
            {
                pinned.Add(id);
            }
        }

        return pinned;
    }

    /// <summary>Pins a folder to quick access - at the list's end - or takes it off.</summary>
    public void SetQuickAccess(long folder, bool pinned)
    {
        _ = FolderOf(folder);

        var now = QuickAccess().Where(id => id != folder).ToList();
        if (pinned)
            now.Add(folder);

        SetAbout(QuickKey, string.Join(",", now.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    // ---- a folder's lock ----------
    // A folder laid out by hand is kept from what acts on a folder whole: Sort and Tidy lay it out again,
    // Store all's sorting delivers into it, Select all takes everything in it. Locked, those pass it by - as a
    // locked stash page stays out of Store all and Select all. On the page its items take no tick either - by the
    // rectangle, Ctrl + click or Invert - as a locked stash page's take none (one lock, one rule: a
    // selection made by hand not held back would have Delete, Move and Withdraw act on
    // what the lock is to keep). The store itself holds back only the sweeps. Kept in the vault's own
    // file, a row of "about" a folder ("lock.12", "lock.top" for the vault's top), so it goes where the vault
    // goes and needs no change to the tables.

    private const string LockKeys = "lock.";

    private static string LockKey(long? folder) =>
        LockKeys + (folder is { } id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "top");

    /// <summary>Whether a folder - null, the vault's top - is locked.</summary>
    public bool IsLocked(long? folder) => About(LockKey(folder)) == "1";

    /// <summary>Locks a folder, or lets it go.</summary>
    public void SetLocked(long? folder, bool locked)
    {
        if (folder is { } id)
            _ = FolderOf(id);

        if (locked)
        {
            SetAbout(LockKey(folder), "1");
            return;
        }

        using var write = _db.CreateCommand();
        write.CommandText = "DELETE FROM about WHERE key = $key;";
        write.Parameters.AddWithValue("$key", LockKey(folder));
        write.ExecuteNonQuery();
    }

    /// <summary>The folders that are locked, by id; the top is asked of <see cref="IsLocked"/>.</summary>
    public IReadOnlySet<long> LockedFolders()
    {
        var locked = new HashSet<long>();

        using var read = _db.CreateCommand();
        read.CommandText = "SELECT key FROM about WHERE key LIKE $keys AND value = '1';";
        read.Parameters.AddWithValue("$keys", LockKeys + "%");

        using var row = read.ExecuteReader();
        while (row.Read())
        {
            if (long.TryParse(row.GetString(0).AsSpan(LockKeys.Length), System.Globalization.NumberStyles.None,
                              System.Globalization.CultureInfo.InvariantCulture, out long id))
                locked.Add(id);
        }

        return locked;
    }

    /// <summary>
    /// The folder smart sort delivers a category to, made where there is none: each level of
    /// <paramref name="path"/> - "Charms", then "Charms/Skillers", then "Charms/Skillers/Amazon" -
    /// is a category linked to a folder of its own. A linked folder is followed wherever it has
    /// been moved and whatever it has been renamed; the next level is looked for inside it. A
    /// level with no folder gets one: an unlinked folder of that name already where it would go
    /// is taken on, else one is made. The sorter never looks below the last level - folders a
    /// player makes inside are theirs to fill.
    /// </summary>
    public long FolderFor(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            throw new ArgumentException("A category has at least one level.", nameof(path));

        var folders = Folders().ToDictionary(each => each.Id);
        var links = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        using (var read = _db.CreateCommand())
        {
            read.CommandText = "SELECT category, folder_id FROM folder_link;";
            using var row = read.ExecuteReader();
            while (row.Read())
                links[row.GetString(0)] = row.GetInt64(1);
        }

        var linked = links.Values.Where(folders.ContainsKey).ToHashSet();
        long? parent = null;
        string category = "";

        foreach (string level in path)
        {
            string name = Named(level);
            category = category.Length == 0 ? name : $"{category}/{name}";

            if (links.TryGetValue(category, out long id) && folders.ContainsKey(id))
            {
                parent = id;
                continue;
            }

            var taken = folders.Values.FirstOrDefault(each =>
                each.ParentId == parent && !linked.Contains(each.Id)
                && string.Equals(each.Name, name, StringComparison.OrdinalIgnoreCase));

            id = taken?.Id ?? CreateFolder(parent, FreeName(parent, name));
            if (taken is null)
                folders[id] = new VaultFolder(id, parent, name);

            using (var link = _db.CreateCommand())
            {
                link.CommandText = "INSERT OR REPLACE INTO folder_link (category, folder_id) VALUES ($category, $id);";
                link.Parameters.AddWithValue("$category", category);
                link.Parameters.AddWithValue("$id", id);
                link.ExecuteNonQuery();
            }

            linked.Add(id);
            parent = id;
        }

        return parent!.Value;
    }

    /// <summary>Puts items in a folder, or in the root when it is null.</summary>
    public int MoveItems(IEnumerable<long> keys, long? folder)
    {
        var moving = keys.ToList();
        if (moving.Count == 0)
            return 0;

        if (folder is { } into && !Exists(into))
            throw new InvalidOperationException(L("Folder not found."));

        using var transaction = _db.BeginTransaction();
        int moved = 0;

        foreach (long key in moving)
        {
            using var write = _db.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                UPDATE item SET folder_id = $folder WHERE key = $key;
                SELECT changes();
                """;
            write.Parameters.AddWithValue("$folder", Db(folder));
            write.Parameters.AddWithValue("$key", key);
            moved += Convert.ToInt32(write.ExecuteScalar());
        }

        transaction.Commit();
        return moved;
    }

    /// <summary>
    /// The folders smart sort delivers a top category and every category under it to, as they are now - found
    /// as <see cref="FolderFor"/> finds them, and none made: each linked folder, wherever it has been moved and
    /// whatever it has been renamed ("Bases", "Bases/Weapons", ...); and where the top category has none
    /// linked yet, the unlinked top folder of its name, which the sorter would take on at its next delivery.
    /// <para>
    /// Looked for as a top folder by its name, a folder renamed or moved
    /// would go on being filled by the sorter and be counted by neither of the Extra page's wizards.
    /// </para>
    /// </summary>
    public IReadOnlyList<long> DeliveryFolders(string category)
    {
        string top = Named(category);
        var folders = Folders().ToDictionary(each => each.Id);
        var links = FolderLinks().Where(link => folders.ContainsKey(link.FolderId)).ToList();

        var found = links
            .Where(link => link.Category.Equals(top, StringComparison.OrdinalIgnoreCase)
                           || link.Category.StartsWith(top + "/", StringComparison.OrdinalIgnoreCase))
            .Select(link => link.FolderId)
            .ToList();

        if (DeliveryTop(top, folders, links) is { } id && !found.Contains(id))
            found.Add(id);

        return found;
    }

    /// <summary>The name the folder of a top category wears now, or null where the vault has none for it.</summary>
    public string? DeliveryName(string category)
    {
        var folders = Folders().ToDictionary(each => each.Id);
        var links = FolderLinks().Where(link => folders.ContainsKey(link.FolderId)).ToList();
        return DeliveryTop(Named(category), folders, links) is { } id ? folders[id].Name : null;
    }

    /// <summary>A top category's own folder: the one linked to it, else an unlinked top folder of its name.</summary>
    private static long? DeliveryTop(
        string top, Dictionary<long, VaultFolder> folders, List<(string Category, long FolderId)> links)
    {
        foreach (var link in links)
        {
            if (link.Category.Equals(top, StringComparison.OrdinalIgnoreCase))
                return link.FolderId;
        }

        var linked = links.Select(link => link.FolderId).ToHashSet();
        return folders.Values.FirstOrDefault(each =>
            each.ParentId is null && !linked.Contains(each.Id)
            && each.Name.Equals(top, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>
    /// Every item smart sort has delivered under a top category, or would have: what lies in the category's
    /// folders (<see cref="DeliveryFolders"/>) and the folders under them. What the Extra page's wizards count
    /// their bases from.
    /// </summary>
    public IReadOnlyList<VaultRow> RowsUnder(string category) =>
        [.. DeliveryFolders(category)
            .SelectMany(Subtree)
            .Distinct()
            .SelectMany(folder => Page(folder, N("Name"), false, 0, int.MaxValue))];

    /// <summary>A folder and every folder beneath it, by id.</summary>
    public IReadOnlyList<long> Subtree(long id)
    {
        var found = new List<long>();

        using var read = _db.CreateCommand();
        read.CommandText = """
            WITH RECURSIVE below(id) AS (
              SELECT $id
              UNION ALL
              SELECT f.id FROM folder f JOIN below b ON f.parent_id = b.id)
            SELECT id FROM below;
            """;
        read.Parameters.AddWithValue("$id", id);
        using var row = read.ExecuteReader();

        while (row.Read())
            found.Add(row.GetInt64(0));

        return found;
    }

    private VaultFolder FolderOf(long id) =>
        Folders().FirstOrDefault(each => each.Id == id)
        ?? throw new InvalidOperationException(L("Folder not found."));

    private bool Exists(long id) => Folders().Any(each => each.Id == id);
}
