using Microsoft.Data.Sqlite;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Collection;

// The vault's items: adding, reading a page of them, putting back, removing.
public sealed partial class Vault
{
    /// <summary>
    /// Copies an item into the vault. The item is not touched: what is stored is a normalised
    /// copy of it, so the caller's item keeps its id and its place.
    /// <para>
    /// Returns the new key, or null with <paramref name="refused"/> saying why. **Refusing is
    /// the point** - a blob that cannot be read back is worse than no blob, because it will not
    /// be noticed for months and may by then be the only copy.
    /// </para>
    /// </summary>
    public long? Add(Item item, out string? refused) =>
        Add(item, folder: null, out refused);

    /// <summary>
    /// Copies an item into a folder. A null folder is the root, and so is one that is no longer
    /// there - the page that named it may have been showing a folder since deleted.
    /// </summary>
    public long? Add(Item item, long? folder, out string? refused)
    {
        if (Capture(item, out refused) is not { } captured)
            return null;

        return AddCaptured([(captured, folder)]) is [var key] ? key : null;
    }

    /// <summary>
    /// An item made ready to store and checked to read back, with nothing written: the first
    /// half of <see cref="Add(Item, long?, out string?)"/>. A save that moves items into
    /// the vault captures every one of them before any file is touched, so an item that would be
    /// refused is refused while nothing has changed. Null with <paramref name="refused"/> saying
    /// why.
    /// </summary>
    public VaultCapture? Capture(Item item, out string? refused)
    {
        refused = null;

        Item copy;

        try
        {
            copy = ItemBlob.Copy(item, StatIdBits);
        }
        catch (Exception problem)
        {
            refused = L($"copy failed: {problem.Message}");
            return null;
        }

        // The id is read off the original, before normalising wipes it from the copy: the blob
        // must not carry it, or two captures of one item would hash differently - but the
        // identity is worth keeping beside the blob, and is what tells a duplicate later.
        uint? origin = item.IsCompact ? null : item.Id;

        ItemBlob.Normalise(copy);

        if (!ItemBlob.TryCapture(copy, StatIdBits, out var blob, out var hash, out refused))
            return null;

        return new VaultCapture(copy, blob, hash, origin);
    }

    /// <summary>
    /// Writes captured items in, <b>all in one transaction</b>: a search must never find an item
    /// that is half there, and a save that moves several must never store some of them. Returns
    /// the new keys in the order given. Each is added now: its stay begins (<see cref="VaultRow"/>).
    /// </summary>
    public IReadOnlyList<long> AddCaptured(IReadOnlyList<(VaultCapture Item, long? Folder)> items)
    {
        var keys = new List<long>(items.Count);
        if (items.Count == 0)
            return keys;

        var folders = Folders().Select(folder => folder.Id).ToHashSet();

        using var transaction = _db.BeginTransaction();
        var arrivedIn = new List<long?>();

        foreach (var (captured, wanted) in items)
        {
            long? folder = wanted is { } id && folders.Contains(id) ? id : null;
            if (!arrivedIn.Contains(folder))
                arrivedIn.Add(folder);

            using var add = _db.CreateCommand();
            add.Transaction = transaction;
            add.CommandText = $"""
                INSERT INTO item (blob, hash, origin_id, added_at, folder_id)
                VALUES ($blob, $hash, $origin, $now, $folder);
                {MetaInsert.Replace("$key", "last_insert_rowid()")}
                SELECT last_insert_rowid();
                """;
            add.Parameters.AddWithValue("$blob", captured.Blob);
            add.Parameters.AddWithValue("$hash", captured.Hash);
            add.Parameters.AddWithValue("$origin", captured.OriginId.HasValue ? captured.OriginId.Value : DBNull.Value);
            add.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            add.Parameters.AddWithValue("$folder", Db(folder));
            Project(add, captured.Copy, captured.Hash);

            if (add.ExecuteScalar() is not long key)
                throw new InvalidOperationException("The vault did not say which key it gave an item.");

            WriteStats(_db, transaction, key, captured.Copy);
            keys.Add(key);
        }

        transaction.Commit();
        return keys;
    }

    /// <summary>
    /// Puts an edited item back in place of the one stored under <paramref name="key"/>: same
    /// key, folder, place and date added - it never left; new blob, and <b>a new id</b>. An edited item is not
    /// the item that was stored - the id is what tells two items apart, and a copy withdrawn
    /// before the edit must not pass for this one. The item handed in is given the new id too,
    /// so what the page shows agrees with what was stored.
    /// <para>
    /// Refused, and nothing written, when the item cannot be read back - the rule every deposit
    /// is held to. Returns the new id, or null with <paramref name="refused"/> saying why.
    /// </para>
    /// <para>
    /// <paramref name="keepId"/>: the id an earlier change of the same run gave it (the Edit page keeps each
    /// change as it is made, and one edit of an item is one new id, not one a number typed).
    /// </para>
    /// </summary>
    public uint? Replace(long key, Item item, out string? refused, uint? keepId = null)
    {
        if (CaptureFor(item, keepId, out refused) is not { } captured)
            return null;

        using var transaction = _db.BeginTransaction();
        if (!WriteOver(transaction, key, captured))
        {
            refused = L("no longer in vault");
            return null;
        }

        transaction.Commit();

        if (captured.OriginId is { } given)
            item.Id = given;

        return captured.OriginId ?? 0;
    }

    /// <summary>
    /// Every row given written over with its item, <b>all or none</b>: each is captured and read back before the first
    /// is written, and the writes are one transaction - an encoding repair's rows (EncodingRepair), where half a file in
    /// each encoding reads by neither. A compact item that will not capture stays as it is: it has no stats for an
    /// encoding to misread. Null where all went; else the name of the row refused, and why, and nothing is written.
    /// </summary>
    public string? ReplaceAll(IReadOnlyList<(VaultRow Row, Item Item)> rows, out string? refused)
    {
        refused = null;

        var captured = new List<(VaultRow Row, Item Item, VaultCapture Captured)>(rows.Count);
        foreach (var (row, item) in rows)
        {
            if (CaptureFor(item, row.OriginId, out string? why) is { } one)
                captured.Add((row, item, one));
            else if (!item.IsCompact)
            {
                refused = why;
                return row.Name;
            }
        }

        using (var transaction = _db.BeginTransaction())
        {
            foreach (var (row, _, one) in captured)
            {
                if (!WriteOver(transaction, row.Key, one))
                {
                    refused = L("no longer in vault");
                    return row.Name;
                }
            }

            transaction.Commit();
        }

        foreach (var (_, item, one) in captured)
        {
            if (one.OriginId is { } given)
                item.Id = given;
        }

        return null;
    }

    /// <summary>An item made ready to write over a row: its copy, blob and hash, and the id it is to have - or null, and why.</summary>
    private VaultCapture? CaptureFor(Item item, uint? keepId, out string? refused)
    {
        refused = null;

        uint? id = item.IsCompact ? null : keepId is > 0 ? keepId : (uint)Random.Shared.NextInt64(1, uint.MaxValue);

        Item copy;
        try
        {
            copy = ItemBlob.Copy(item, StatIdBits);
        }
        catch (Exception problem)
        {
            refused = L($"copy failed: {problem.Message}");
            return null;
        }

        ItemBlob.Normalise(copy);

        return ItemBlob.TryCapture(copy, StatIdBits, out var blob, out var hash, out refused)
            ? new VaultCapture(copy, blob, hash, id)
            : null;
    }

    /// <summary>A row's item, what is read off it and its stats written over, in the transaction given; false where the row is gone.</summary>
    private bool WriteOver(Microsoft.Data.Sqlite.SqliteTransaction transaction, long key, VaultCapture captured)
    {
        using (var write = _db.CreateCommand())
        {
            write.Transaction = transaction;
            write.CommandText = """
                UPDATE item SET blob = $blob, hash = $hash, origin_id = $origin WHERE key = $key;
                DELETE FROM item_stat WHERE item_key = $key;
                DELETE FROM item_meta WHERE item_key = $key;
                """;
            write.Parameters.AddWithValue("$blob", captured.Blob);
            write.Parameters.AddWithValue("$hash", captured.Hash);
            write.Parameters.AddWithValue("$origin", captured.OriginId.HasValue ? captured.OriginId.Value : DBNull.Value);
            write.Parameters.AddWithValue("$key", key);

            if (write.ExecuteNonQuery() == 0)
                return false;
        }

        using (var meta = _db.CreateCommand())
        {
            meta.Transaction = transaction;
            meta.CommandText = MetaInsert;
            meta.Parameters.AddWithValue("$key", key);
            Project(meta, captured.Copy, captured.Hash);
            meta.ExecuteNonQuery();
        }

        WriteStats(_db, transaction, key, captured.Copy);
        return true;
    }

    /// <summary>How a page may be ordered. The panel names one of these.</summary>
    public static readonly string[] Orders = [N("Stored"), N("Name"), N("Rarity"), N("Type")];

    /// <summary>
    /// One page of the vault, ordered and cut <b>in the database</b>.
    /// <para>
    /// Not read in full and paged in memory. That cost <b>229 ms an page turn</b> on 2567
    /// items - every row and every blob fetched to show thirty - and it is the whole reason
    /// the page felt slow. Now the index does the ordering and SQLite returns thirty rows.
    /// </para>
    /// <para>
    /// <b>OFFSET, not keyset</b>, and that is a correction to the design note. Keyset paging
    /// is the better tool for "the next page" because it carries the last row's sort value;
    /// it cannot answer "page 40 of 86", which is what numbered pages ask. With an index on
    /// the sort column the walk SQLite does instead is cheap, and the rows it walks are index
    /// entries rather than blobs.
    /// </para>
    /// </summary>
    public IReadOnlyList<VaultRow> Page(string by, bool descending, int skip, int take) =>
        Page(everywhere: true, folder: null, query: null, by, descending, skip, take);

    /// <summary>
    /// One page of one folder's own items - not its subfolders', which is how Explorer shows a
    /// folder. A null folder is the root.
    /// </summary>
    public IReadOnlyList<VaultRow> Page(long? folder, string by, bool descending, int skip, int take) =>
        Page(everywhere: false, folder, query: null, by, descending, skip, take);

    /// <summary>
    /// One page of a folder's own items that <paramref name="query"/> keeps. Counted by
    /// <see cref="Count(long?, VaultQuery?)"/> with the same condition, so the pager and the page
    /// agree. Null keeps everything.
    /// </summary>
    public IReadOnlyList<VaultRow> Page(long? folder, VaultQuery? query, string by, bool descending, int skip, int take) =>
        Page(everywhere: false, folder, query, by, descending, skip, take);

    /// <summary>
    /// One page of every item in the vault, whatever folder it is in, that the query keeps -
    /// what a search shows. Each row says its folder, for the card to say where it is.
    /// </summary>
    public IReadOnlyList<VaultRow> Page(VaultQuery? query, string by, bool descending, int skip, int take) =>
        Page(everywhere: true, folder: null, query, by, descending, skip, take);

    /// <summary>
    /// The search panel's condition, shared by the page and its count. One fixed statement with
    /// every filter a parameter that may be null - null is "Any" - so nothing a person types is
    /// ever spliced into SQL. The text box is a LIKE over what the item is and does - its name, its
    /// base and its stats (<see cref="ItemLines.TextOf(Item)"/>, a line each, so what is typed is
    /// found within one line: the phrase as typed, as the game's stash search has it), with its
    /// wildcards escaped, so "50%" looks for a percent sign; SQLite's LIKE ignores
    /// case for ASCII, which the game's wording is. Where what is typed is a pattern as well - the game's
    /// search takes a regular expression, <see cref="SearchPattern"/> - it is asked that too, and either
    /// answer keeps the item. An item with
    /// no derived row (one a rebuild could not read) has nulls here and so is kept only while no
    /// filter is set.
    /// </summary>
    private const string Matches = """
        ($name IS NULL OR m.text LIKE '%' || $name || '%' ESCAPE '\'
                       OR ($pattern IS NOT NULL AND m.text REGEXP $pattern))
        AND ($rarity     IS NULL OR m.rarity = $rarity)
        AND ($typeFrom   IS NULL OR m.type_rank BETWEEN $typeFrom AND $typeTo)
        AND ($code       IS NULL OR m.base_code = $code)
        AND ($ethereal   IS NULL OR m.ethereal = $ethereal)
        AND ($corrupted  IS NULL OR m.corrupted = $corrupted)
        AND ($desecrated IS NULL OR m.desecrated = $desecrated)
        AND ($socketsMin IS NULL OR m.sockets >= $socketsMin)
        AND ($socketsMax IS NULL OR m.sockets <= $socketsMax)
        AND ($craft       IS NULL OR m.craft_family = $craft COLLATE NOCASE)
        """;

    /// <summary>
    /// <see cref="Matches"/>, and for each property row one more condition: the item has a
    /// number under that key - or, for a row that picks a skill and has none picked, under any
    /// key of that family - between the row's Min and Max. Parameter names are numbered here;
    /// nothing typed reaches the text of the statement.
    /// </summary>
    private static string Condition(VaultQuery? query)
    {
        var properties = query?.Properties ?? [];
        var folders = query?.Folders ?? [];
        if (properties.Count == 0 && folders.Count == 0 && query?.ItemIds is null)
            return Matches;

        var text = new System.Text.StringBuilder(Matches);

        // Kept to these folders - a folder and everything under it, for the search's scope.
        if (folders.Count > 0)
            text.Append($"\nAND i.folder_id IN ({string.Join(", ", folders.Select((_, i) => $"$fo{i}"))})");

        // Kept to these items, by their own ids - one item's copies, or every duplicate. None
        // asked for is nothing found.
        if (query?.ItemIds is { } ids)
        {
            text.Append(ids.Count == 0
                ? "\nAND 0"
                : $"\nAND i.origin_id IN ({string.Join(", ", ids.Select(id => (long)id))})");
        }

        for (int i = 0; i < properties.Count; i++)
        {
            string match = properties[i].IsFamily
                ? $"substr(s.key, 1, length($pk{i})) = $pk{i}"
                : $"s.key = $pk{i}";

            text.Append($"""

                AND EXISTS (SELECT 1 FROM item_stat s
                            WHERE s.item_key = i.key AND {match}
                              AND ($pmin{i} IS NULL OR s.value >= $pmin{i})
                              AND ($pmax{i} IS NULL OR s.value <= $pmax{i}))
                """);
        }

        return text.ToString();
    }

    private static void Bind(SqliteCommand command, VaultQuery? query)
    {
        var q = query ?? VaultQuery.Everything;

        static object Or(object? value) => value ?? DBNull.Value;
        static object Flag(bool? value) => value is { } on ? (on ? 1 : 0) : DBNull.Value;

        command.Parameters.AddWithValue("$name",
            q.Name?.Trim() is { Length: > 0 } typed
                ? typed.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")
                : DBNull.Value);
        command.Parameters.AddWithValue("$pattern", Or(SearchPattern.Of(q.Name?.Trim())));
        command.Parameters.AddWithValue("$rarity", Or(q.Rarity));
        command.Parameters.AddWithValue("$typeFrom", Or(q.TypeFrom));
        command.Parameters.AddWithValue("$typeTo", Or(q.TypeTo ?? q.TypeFrom));
        command.Parameters.AddWithValue("$code", q.Base?.Trim() is { Length: > 0 } code ? code : DBNull.Value);
        command.Parameters.AddWithValue("$ethereal", Flag(q.Ethereal));
        command.Parameters.AddWithValue("$corrupted", Flag(q.Corrupted));
        command.Parameters.AddWithValue("$desecrated", Flag(q.Desecrated));
        command.Parameters.AddWithValue("$socketsMin", Or(q.SocketsMin));
        command.Parameters.AddWithValue("$socketsMax", Or(q.SocketsMax));
        command.Parameters.AddWithValue("$craft", q.CraftFamily is { Length: > 0 } family ? family : DBNull.Value);

        var folders = q.Folders ?? [];
        for (int i = 0; i < folders.Count; i++)
            command.Parameters.AddWithValue($"$fo{i}", folders[i]);

        var properties = q.Properties ?? [];
        for (int i = 0; i < properties.Count; i++)
        {
            command.Parameters.AddWithValue($"$pk{i}", properties[i].Key);
            command.Parameters.AddWithValue($"$pmin{i}", Or(properties[i].Min));
            command.Parameters.AddWithValue($"$pmax{i}", Or(properties[i].Max));
        }
    }

    /// <summary>
    /// The ORDER BY a page is read in. Whitelisted rather than interpolated: this is the one place
    /// a caller's string would reach SQL, and "ORDER BY " + anything is how that goes wrong. An
    /// item with no derived row - one a rebuild could not read - ranks after every other. Every
    /// key takes the direction, so Descending is the whole order read backwards rather than the
    /// groups reversed and each group still ascending; the key breaks ties the same way.
    /// </summary>
    private static string OrderOf(string by, bool descending)
    {
        string[] keys = by switch
        {
            "Name" => ["m.name COLLATE NOCASE"],
            "Rarity" => ["COALESCE(m.rarity, 99)", "m.name COLLATE NOCASE"],
            "Type" => ["COALESCE(m.type_rank, 9999)", "m.base_name COLLATE NOCASE", "m.name COLLATE NOCASE"],
            _ => ["i.added_at"],
        };

        string way = descending ? "DESC" : "ASC";
        return $"{string.Join($" {way}, ", keys)} {way}, i.key {way}";
    }

    private IReadOnlyList<VaultRow> Page(bool everywhere, long? folder, VaultQuery? query, string by, bool descending, int skip, int take)
    {
        var found = new List<VaultRow>();

        using var read = _db.CreateCommand();
        read.CommandText = $"""
            {RowSelect}
            WHERE ($everywhere OR i.folder_id IS $folder) AND {Condition(query)}
            ORDER BY {OrderOf(by, descending)}
            LIMIT $take OFFSET $skip;
            """;
        read.Parameters.AddWithValue("$everywhere", everywhere ? 1 : 0);
        read.Parameters.AddWithValue("$folder", Db(folder));
        Bind(read, query);
        read.Parameters.AddWithValue("$take", take);
        read.Parameters.AddWithValue("$skip", skip);

        using var row = read.ExecuteReader();

        while (row.Read())
            found.Add(ReadRow(row));

        return found;
    }

    /// <summary>A whole row, as every reader of rows asks for it: the item, its projection and its folder.</summary>
    private const string RowSelect = """
        SELECT i.key, i.blob, i.origin_id, i.added_at,
               COALESCE(m.name, ''), COALESCE(m.quality, 0), COALESCE(m.ilvl, 0),
               i.folder_id
        FROM item i
        LEFT JOIN item_meta m ON m.item_key = i.key
        """;

    private static VaultRow ReadRow(SqliteDataReader row) => new(
        row.GetInt64(0),
        (byte[])row[1],
        row.IsDBNull(2) ? null : (uint)row.GetInt64(2),
        DateTimeOffset.FromUnixTimeSeconds(row.GetInt64(3)),
        row.GetString(4),
        row.GetInt32(5),
        row.GetInt32(6),
        row.IsDBNull(7) ? null : row.GetInt64(7));

    /// <summary>
    /// What each of these items holds under each wanted property, in the order of <paramref name="wants"/>:
    /// the number, or null where it has none - the largest where a family of keys is asked (any skill of a
    /// kind). What a search's list says beside a find: the rolls it was found by.
    /// <para>
    /// An item has more than one number under a key where a jewel in its sockets has one of its own beside
    /// the item's sum (WriteStats). The one within the row's bounds is said - the largest of those - so an
    /// item found by its jewel's 40 says 40 and not its own 250; with none within them, the largest.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<long, double?[]> ValuesOf(IReadOnlyCollection<long> keys, IReadOnlyList<PropertyWant> wants)
    {
        var values = keys.Distinct().ToDictionary(key => key, _ => new double?[wants.Count]);
        if (values.Count == 0 || wants.Count == 0)
            return values;

        // Keys are numbers of the vault's own: written into the statement, nothing typed is.
        string among = string.Join(", ", values.Keys);

        for (int i = 0; i < wants.Count; i++)
        {
            string match = wants[i].IsFamily ? "substr(s.key, 1, length($key)) = $key" : "s.key = $key";

            using var read = _db.CreateCommand();
            read.CommandText = $"""
                SELECT s.item_key,
                       COALESCE(MAX(CASE WHEN ($min IS NULL OR s.value >= $min) AND ($max IS NULL OR s.value <= $max) THEN s.value END),
                                MAX(s.value))
                FROM item_stat s WHERE s.item_key IN ({among}) AND {match} GROUP BY s.item_key;
                """;
            read.Parameters.AddWithValue("$key", wants[i].Key);
            read.Parameters.AddWithValue("$min", wants[i].Min is { } least ? least : DBNull.Value);
            read.Parameters.AddWithValue("$max", wants[i].Max is { } most ? most : DBNull.Value);

            using var row = read.ExecuteReader();
            while (row.Read())
                values[row.GetInt64(0)][i] = row.GetDouble(1);
        }

        return values;
    }

    /// <summary>
    /// The rows under these keys, whole - blob, folder, place, when added - in no particular
    /// order; a key no longer there is left out. What a session's history keeps of a row a move
    /// took out, to put it back (<see cref="PutBack"/>).
    /// </summary>
    public IReadOnlyList<VaultRow> RowsOf(IEnumerable<long> keys)
    {
        var found = new List<VaultRow>();
        foreach (long key in keys.Distinct())
        {
            using var read = _db.CreateCommand();
            read.CommandText = $"{RowSelect} WHERE i.key = $key;";
            read.Parameters.AddWithValue("$key", key);

            using var row = read.ExecuteReader();
            if (row.Read())
                found.Add(ReadRow(row));
        }

        return found;
    }

    /// <summary>
    /// A row taken out, put back as it was - the same item and identity, folder and date
    /// added - under a new key, which it returns; null with
    /// <paramref name="refused"/> when it cannot be read back. A folder deleted since sends it to
    /// the root. What a session's undo does with a withdrawal: the withdrawal did not happen, so the stay it would
    /// have ended goes on, under its own date.
    /// </summary>
    public long? PutBack(VaultRow row, out string? refused)
    {
        refused = null;

        Item item;
        try
        {
            item = ItemBlob.Restore(row.Blob, StatIdBits);
        }
        catch (Exception problem)
        {
            refused = L($"unreadable: {problem.Message}");
            return null;
        }

        if (row.OriginId is { } origin)
            item.Id = origin;

        if (Capture(item, out refused) is not { } captured)
            return null;

        long key = AddCaptured([(captured, row.FolderId)]) is [var added] ? added : 0;
        if (key == 0)
        {
            refused = L("refused by vault");
            return null;
        }

        SetAdded(key, row.Added);
        return key;
    }

    /// <summary>Says when a row's stay began, where it is not now: a row put back, or carried from another file.</summary>
    private void SetAdded(long key, DateTimeOffset added)
    {
        using var date = _db.CreateCommand();
        date.CommandText = "UPDATE item SET added_at = $at WHERE key = $key;";
        date.Parameters.AddWithValue("$at", added.ToUnixTimeSeconds());
        date.Parameters.AddWithValue("$key", key);
        date.ExecuteNonQuery();
    }

    /// <summary>Takes items out of the vault for good: a row gone is a stay ended, and nothing is kept of it.</summary>
    public int Remove(IEnumerable<long> keys)
    {
        var going = keys.ToList();
        if (going.Count == 0)
            return 0;

        using var transaction = _db.BeginTransaction();
        int gone = 0;

        foreach (long key in going)
        {
            using var drop = _db.CreateCommand();
            drop.Transaction = transaction;
            drop.CommandText = "DELETE FROM item WHERE key = $key;";
            drop.Parameters.AddWithValue("$key", key);
            gone += drop.ExecuteNonQuery();
        }

        transaction.Commit();
        return gone;
    }

    /// <summary>
    /// Deletes every item and every folder. The file stays, with its season and format, empty.
    /// <para>
    /// Copied first with <c>VACUUM INTO</c>, as an upgrade is, since this is the one thing
    /// that throws away what was written in it. Returns where the copy went, or null when
    /// there was nothing to copy.
    /// </para>
    /// </summary>
    public string? Empty()
    {
        bool holds = Count() > 0 || Folders().Count > 0;
        string? backup = holds ? BackUp(_db, FilePath, "emptied") : null;

        using var transaction = _db.BeginTransaction();
        DeleteItemsAndFolders(transaction);
        transaction.Commit();
        return backup;
    }

    /// <summary>
    /// Every item and every folder gone, with what was kept of the folders: their links and their locks (the
    /// top's lock is the vault's own and stays).
    /// <para>
    /// Items first - they point at folders, and no reference cascades. The folders point at each other, and go
    /// in one statement: the database looks at what a statement leaves, and this one leaves no folder to point
    /// at one deleted. Cutting their parents first (<c>UPDATE folder SET parent_id = NULL</c>)
    /// would for that moment make every folder a top folder - and two of one name under different parents, as
    /// sorted storing makes them (Rings/Rare, Amulets/Rare), then stand side by side, which the vault refuses:
    /// "UNIQUE constraint failed: index 'folder_sibling'", and a vault that could not be cleaned.
    /// A lock or a quick-access pin left behind would be the next folder's that came by the id.
    /// </para>
    /// </summary>
    private void DeleteItemsAndFolders(SqliteTransaction transaction) =>
        Run(_db, transaction, $"""
            DELETE FROM item;
            DELETE FROM folder;
            DELETE FROM folder_link;
            DELETE FROM about WHERE key LIKE '{LockKeys}%' AND key <> '{LockKeys}top';
            DELETE FROM about WHERE key = '{QuickKey}';
            """);

    /// <summary>
    /// Deletes parts of what the vault holds, the player's choice of them, by a checklist: the items with their folders,
    /// the trade records, the gold. Copied to Backups first, once, whatever is chosen -
    /// none of it is written anywhere else. (The grail's finds were a choice too, until they went with format 20.)
    /// Returns where the copy went, or null where nothing was chosen.
    /// </summary>
    public string? Clear(bool items, bool trades, bool gold)
    {
        if (!items && !trades && !gold)
            return null;

        string backup = BackUp(_db, FilePath, "cleared");
        using var transaction = _db.BeginTransaction();

        if (items)
            DeleteItemsAndFolders(transaction);

        if (trades)
            Run(_db, transaction, "DELETE FROM trade;");
        if (gold)
            Run(_db, transaction, $"UPDATE about SET value = '0' WHERE key = '{GoldKey}';");

        transaction.Commit();
        return backup;
    }

    public int Count()
    {
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM item;";
        return Convert.ToInt32(read.ExecuteScalar());
    }

    /// <summary>How many items a folder holds itself, not counting its subfolders.</summary>
    public int Count(long? folder) => Count(folder, query: null);

    /// <summary>How many of a folder's own items a query keeps - see the querying Page.</summary>
    public int Count(long? folder, VaultQuery? query) => Count(everywhere: false, folder, query);

    /// <summary>
    /// How many items in the whole vault a query keeps - see the vault-wide Page. Named apart
    /// from <see cref="Count(long?)"/>: as an overload, <c>Count(null)</c> could have meant
    /// either, and the CLI's did.
    /// </summary>
    public int CountEverywhere(VaultQuery? query) => Count(everywhere: true, folder: null, query);

    private int Count(bool everywhere, long? folder, VaultQuery? query)
    {
        using var read = _db.CreateCommand();
        read.CommandText = $"""
            SELECT COUNT(*) FROM item i
            LEFT JOIN item_meta m ON m.item_key = i.key
            WHERE ($everywhere OR i.folder_id IS $folder) AND {Condition(query)};
            """;
        read.Parameters.AddWithValue("$everywhere", everywhere ? 1 : 0);
        read.Parameters.AddWithValue("$folder", Db(folder));
        Bind(read, query);
        return Convert.ToInt32(read.ExecuteScalar());
    }
}
