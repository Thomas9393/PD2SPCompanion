using Microsoft.Data.Sqlite;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// One item as the vault holds it: the item, and where it lies. Nothing about its past or its future - where
/// it came from, who stored it, a note on it: no extra responsibility for what the editor cannot fully
/// control. What is kept
/// must stay true whatever the game does with the item once it is out.
/// </summary>
/// <param name="OriginId">
/// The item's own id, kept out of the blob but not thrown away - it answers "is this the same
/// item", where the hash answers "is this the same kind of item". Null for a compact item,
/// which carries no id at all.
/// </param>
/// <param name="Added">
/// When this stay in the vault began. An item withdrawn and stored again is a new row with a new date; one
/// moved between folders or edited in place keeps its own; so does one a withdrawal undone puts back, and
/// one carried into the next season's vault.
/// </param>
public sealed record VaultRow(
    long Key,
    byte[] Blob,
    uint? OriginId,
    DateTimeOffset Added,
    string Name,
    int Quality,
    int ItemLevel,
    long? FolderId = null);

/// <summary>
/// What the search panel asks of a folder's items. Every part is optional and null means "Any";
/// they all have to hold. The type is a span of <see cref="ItemOrder"/> ranks, so a group ("Weapons")
/// and a single kind ("Weapons: Axes") are asked the same way - see <see cref="ItemOrder.TypeSpans"/>.
/// Nothing of when an item was added: the vault orders by it, and the search does not ask it.
/// </summary>
public sealed record VaultQuery(
    string? Name = null,
    int? Rarity = null,
    int? TypeFrom = null, int? TypeTo = null,
    string? Base = null,
    bool? Ethereal = null,
    bool? Corrupted = null, bool? Desecrated = null,
    int? SocketsMin = null, int? SocketsMax = null,
    IReadOnlyList<PropertyWant>? Properties = null,
    IReadOnlyList<long>? Folders = null,
    string? CraftFamily = null,
    IReadOnlyList<uint>? ItemIds = null)
{
    public static readonly VaultQuery Everything = new();

    /// <summary>Whether it keeps anything out at all.</summary>
    public bool IsFiltering =>
        Properties is { Count: > 0 }
        || this with { Name = Name?.Trim() is { Length: > 0 } n ? n : null, Properties = null } != Everything;
}

/// <summary>
/// One property row of a search: the item must have a number under <paramref name="Key"/> -
/// or under any key starting with it, for a family - between <paramref name="Min"/> and
/// <paramref name="Max"/>, either of which may be open. See <see cref="PropertyIndex"/>.
/// </summary>
public sealed record PropertyWant(string Key, bool IsFamily = false, double? Min = null, double? Max = null);

/// <summary>A folder in the vault. A null parent is the top level.</summary>
public sealed record VaultFolder(long Id, long? ParentId, string Name);

/// <summary>
/// One trade in the ledger: when, who, bought or sold, the site's ids for it, the id it has in the
/// game here, what it was called, what was asked, what moved, and the play mode at the time.
/// </summary>
public sealed record TradeRecord(
    DateTimeOffset At, string Character, string Kind,
    string? ListingId, string? SiteItemId, string? SiteHash, uint? ItemId,
    string Name, string Price, string Paid, string Mode);

// A withdrawal and an item the editor made had a record each until format 20: their one reader was the
// grail, telling a find from an item the editor handed out, and the grail is the game's Discovery since.

/// <summary>
/// Gold moving between the vault and a purse: which purse ("inventory", "stash" or "shared"), and how much -
/// into the vault positive, out negative. What a save stages and its journal counts; the vault itself keeps
/// one number, its balance (<see cref="Vault.GoldBalance"/>), and no list of what made it.
/// </summary>
public sealed record GoldMove(string Purse, long Amount);

/// <summary>
/// An item made ready for the vault and checked to read back, not yet written - see
/// <see cref="Vault.Capture"/>. The normalised copy, its blob and hash, and the id the original
/// carried.
/// </summary>
public sealed record VaultCapture(Item Copy, byte[] Blob, byte[] Hash, uint? OriginId);

// A find on the grail had a record of its own - the entry, who found it and when, the item's id - from format 3 to
// 20: the grail is the game's Discovery since, which the editor reads and never keeps: the record is no longer
// the editor's responsibility.

/// <summary>
/// The vault: an endless library of item templates, one SQLite file, one season.
/// <para>
/// Why it is shaped this way, in short: an item is stored as its own bitstream - <see cref="ItemBlob"/> - normalised so
/// two captures of the same item hash alike, and proven to read back before it is committed.
/// Depositing <b>copies</b>; the item stays where it was.
/// </para>
/// <para>
/// <b>Two lifetimes, kept in separate tables on purpose.</b> <c>item_meta</c> and <c>item_stat</c> are
/// derived and may be dropped and rebuilt whenever the decoder improves. <c>item</c> is the item itself and
/// where the player laid it, and no rebuild touches it. Everything keys on our own <c>item.key</c>, never on
/// anything derived, so a rebuild cannot reach it.
/// </para>
/// </summary>
public sealed partial class Vault : IDisposable
{
    /// <summary>
    /// The file format. Bumped when the tables change shape, and every bump has a step in
    /// <see cref="Upgrade"/>.
    /// <list type="bullet">
    ///   <item>1 - items, their projection and remarks</item>
    ///   <item>2 - folders</item>
    ///   <item>3 - the grail's finds (gone with 20)</item>
    ///   <item>4 - the id of the item each find was</item>
    ///   <item>5 - trades and withdrawals</item>
    ///   <item>6 - items the editor made</item>
    ///   <item>7 - a short caption beside the note (one build only)</item>
    ///   <item>8 - the caption folded back into the note: one note, one line</item>
    ///   <item>9 - <c>about</c>: what a file is - a collection or a profile, as they were then called,
    ///   and whose mode - when a season's vault became several files; and which collection a
    ///   withdrawal came from</item>
    ///   <item>10 - <c>item.stored_by</c>: the character loaded when the item was stored - the
    ///   card's "By". A character file's source already said it; the shared stash's did not</item>
    ///   <item>11 - <c>gold_move</c>: the vault's gold, as every move in or out (<see cref="GoldMove"/>)</item>
    ///   <item>12 - <c>folder_link</c>: which folder smart sort delivers each category to (<see cref="FolderFor"/>)</item>
    ///   <item>13 - <c>parked_note</c>: the note of an item moved out, waiting for it to come back (gone with 18)</item>
    ///   <item>14 - a withdrawal keeps the row it took, whole, so a restore can give it back; and
    ///   <c>pending_move</c>, the moves a save is part-way through (<see cref="JournalMoves"/>)</item>
    ///   <item>16 - places: the page and the cell an item lay at in its folder (gone with 19)</item>
    ///   <item>17 - one sheet a folder, of the vault's width: the pages laid two across (see the step)</item>
    ///   <item>18 - only what stays true: the
    ///   notes and the waiting notes go, an item's source and who stored it, the rows a withdrawal kept, and
    ///   the vault's gold is one balance in <c>about</c>, not its every move</item>
    ///   <item>19 - no item has a place of its own (Vault.Sheet.cs): the page and the cell of 16 go, and the vault's width of 17 -
    ///   a folder is laid out as it is shown, by a rule (<see cref="VaultLayout"/>)</item>
    ///   <item>20 - the grail is the game's: the
    ///   finds of 3 go, and the withdrawals of 5 and the made items of 6, which only told a find from an item the
    ///   editor handed out. The trades stay: the repeat buy's warning reads them</item>
    /// </list>
    /// </summary>
    public const int SchemaVersion = 20;

    /// <summary>The <c>about</c> key the vault's gold is kept under.</summary>
    private const string GoldKey = "gold";

    /// <summary>The <c>about</c> key that counts the writes of the vault's gold - see <see cref="GoldWrites"/>.</summary>
    private const string GoldWritesKey = "gold_writes";

    /// <summary>
    /// Our decoder. Bumped when the projection would come out differently, which is the signal
    /// to drop <c>item_meta</c> and derive it again - see <see cref="Reproject"/>. The blob is
    /// untouched by it.
    /// <list type="bullet">
    ///   <item>1 - name, quality, item level</item>
    ///   <item>2 - base name, and the Rarity and Type ranks (<see cref="ItemOrder"/>)</item>
    ///   <item>3 - base code, ethereal, identified, sockets, corrupted, desecrated: the search
    ///   panel's Basics (<see cref="VaultQuery"/>)</item>
    ///   <item>4 - identified dropped: the filter went, and a column nothing reads
    ///   is one more thing a rebuild has to get right</item>
    ///   <item>5 - <c>item_stat</c>: each item's number for each property the search offers
    ///   (<see cref="PropertyIndex"/>)</item>
    ///   <item>6 - <c>craft_family</c>: the recipe family a crafted item was made by, read from
    ///   what it carries (<see cref="AffixRanges.CraftFamilyOf"/>) - the grail's Crafting tab
    ///   filters by it</item>
    ///   <item>7 - <c>grail_kind</c>, <c>grail_row</c>, <c>grail_eth</c>: the grail card a unique or set
    ///   piece is (<see cref="GrailCatalog.EntryOf"/>) - by its row, not its name, so the eight Rainbow
    ///   Facets stay apart - for the grail's count of copies held (<see cref="GrailCounts"/>)</item>
    ///   <item>8 - the same for runewords, by the card their runes and base make</item>
    ///   <item>9 - every line the item's tooltip says, for the search's text box, with the language they are
    ///       in (the game's stash search finds "ANY text contained on the item")</item>
    ///   <item>10 - of those lines, the name, the base and the stats only: not what every item of a kind
    ///       says - its damage, what it asks, its levels</item>
    ///   <item>11 - and the name of what is in each socket; and the numbers a property row asks count what the
    ///       sockets give, as the lines do</item>
    ///   <item>12 - a socketed jewel's own numbers beside its host's sums, so a row finds the item by them
    ///       too</item>
    ///   <item>13 - <c>grail_eth</c> dropped, and the runewords' cards of 8: the grail is one card a unique or set row
    ///       since its ethereal lists and its runeword tab went, ethereal copies counted with the rest</item>
    /// </list>
    /// </summary>
    public const int ProjectionVersion = 13;

    /// <summary>
    /// The derived table, whole. Created only by <see cref="Reproject"/>, never with
    /// <c>IF NOT EXISTS</c> beside the others: an index on a column an older vault's table lacks
    /// would fail on opening it, before any step had the chance to rebuild it.
    /// </summary>
    private const string MetaTable = """
        CREATE TABLE item_meta (
          item_key  INTEGER PRIMARY KEY REFERENCES item(key) ON DELETE CASCADE,
          name      TEXT    NOT NULL DEFAULT '',
          base_name TEXT    NOT NULL DEFAULT '',
          quality   INTEGER NOT NULL DEFAULT 0,
          ilvl      INTEGER NOT NULL DEFAULT 0,
          rarity    INTEGER NOT NULL DEFAULT 0,
          type_rank INTEGER NOT NULL DEFAULT 0,
          base_code TEXT    NOT NULL DEFAULT '',
          ethereal   INTEGER NOT NULL DEFAULT 0,
          sockets    INTEGER NOT NULL DEFAULT 0,
          corrupted  INTEGER NOT NULL DEFAULT 0,
          desecrated INTEGER NOT NULL DEFAULT 0,
          craft_family TEXT  NOT NULL DEFAULT '',
          grail_kind TEXT    NOT NULL DEFAULT '',
          grail_row  INTEGER NOT NULL DEFAULT -1,
          text       TEXT    NOT NULL DEFAULT '');

        CREATE INDEX item_meta_name   ON item_meta(name COLLATE NOCASE);
        CREATE INDEX item_meta_grail  ON item_meta(grail_kind, grail_row);
        CREATE INDEX item_meta_rarity ON item_meta(rarity, name COLLATE NOCASE);
        CREATE INDEX item_meta_type   ON item_meta(type_rank, base_name COLLATE NOCASE, name COLLATE NOCASE);

        CREATE TABLE item_stat (
          item_key INTEGER NOT NULL REFERENCES item(key) ON DELETE CASCADE,
          key      TEXT    NOT NULL,
          value    REAL    NOT NULL);

        CREATE INDEX item_stat_key  ON item_stat(key, value);
        CREATE INDEX item_stat_item ON item_stat(item_key);

        CREATE TABLE item_meta_language (code TEXT NOT NULL);
        """;

    /// <summary>
    /// An item's numbers for the search's properties, one row each. On the connection and
    /// transaction the item's own row was written in, so the two cannot disagree.
    /// </summary>
    private static void WriteStats(SqliteConnection db, SqliteTransaction? transaction, long key, Item item) =>
        WriteStats(db, transaction, key, StatsOf(item));

    /// <summary>The rows <see cref="WriteStats(SqliteConnection, SqliteTransaction?, long, Item)"/> writes: worked out apart from the writing, for <see cref="Reproject"/>.</summary>
    private static List<KeyValuePair<string, double>> StatsOf(Item item)
    {
        var rows = new List<KeyValuePair<string, double>>(PropertyIndex.ValuesOf(item));

        // And each socketed jewel's own numbers, rows more under the item's key: an item of 210% enhanced
        // damage holding a jewel of 40% says 250, and is found by 250 - and by 40, which is how the jewel is
        // found where it sits. A search lists items, and a jewel in a socket is none of its own. Jewels only: what a
        // gem or a rune gives is the same wherever it is, and its name finds it. A row with only a most
        // therefore finds such an item by its jewel - "at most 50" the 250 one - and the list says the
        // number it was found by (ValuesOf).
        foreach (var jewel in item.SocketedItems.Where(socketed => !socketed.IsCompact))
            rows.AddRange(PropertyIndex.ValuesOf(jewel));

        return rows;
    }

    private static void WriteStats(SqliteConnection db, SqliteTransaction? transaction, long key, List<KeyValuePair<string, double>> rows)
    {
        using var write = db.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = "INSERT INTO item_stat (item_key, key, value) VALUES ($key, $stat, $value);";

        var keyParameter = write.Parameters.AddWithValue("$key", key);
        var stat = write.Parameters.Add("$stat", SqliteType.Text);
        var value = write.Parameters.Add("$value", SqliteType.Real);

        foreach (var (name, number) in rows)
        {
            stat.Value = name;
            value.Value = number;
            write.ExecuteNonQuery();
        }
    }

    /// <summary>One item's row of <c>item_meta</c>, for the item whose key is <c>$key</c>.</summary>
    private const string MetaInsert = """
        INSERT INTO item_meta (item_key, name, base_name, quality, ilvl, rarity, type_rank,
                               base_code, ethereal, sockets, corrupted, desecrated, craft_family,
                               grail_kind, grail_row, text)
        VALUES ($key, $name, $base, $quality, $ilvl, $rarity, $type,
                $code, $ethereal, $sockets, $corrupted, $desecrated, $craft,
                $grailKind, $grailRow, $text);
        """;

    /// <summary>
    /// Fills <see cref="MetaInsert"/>'s values, other than the key, from an item. With the
    /// <paramref name="hash"/> of its stored bytes where the caller has it: the lines of the same bytes are
    /// then worded once in a session (<see cref="ItemLines.TextOf(Item, byte[])"/>).
    /// </summary>
    private static void Project(SqliteCommand command, Item item, byte[]? hash = null)
    {
        var view = ItemPresenter.Describe(item);

        // What the search's text box is matched against: the name and every line of the tooltip.
        command.Parameters.AddWithValue("$text", hash is null ? ItemLines.TextOf(item) : ItemLines.TextOf(item, hash));

        command.Parameters.AddWithValue("$name", view.Name);
        command.Parameters.AddWithValue("$base", view.BaseName);
        command.Parameters.AddWithValue("$quality", (int)item.Quality);
        command.Parameters.AddWithValue("$ilvl", (int)item.ItemLevel);
        command.Parameters.AddWithValue("$rarity", ItemOrder.Rarity(item));
        command.Parameters.AddWithValue("$type", ItemOrder.TypeRank(item));
        command.Parameters.AddWithValue("$code", item.Code?.Trim() ?? "");
        command.Parameters.AddWithValue("$ethereal", item.IsEthereal ? 1 : 0);
        command.Parameters.AddWithValue("$sockets", (int)item.TotalNumberOfSockets);
        command.Parameters.AddWithValue("$corrupted", Corruptions.IsCorrupted(item) ? 1 : 0);
        command.Parameters.AddWithValue("$desecrated", Corruptions.IsDesecrated(item) ? 1 : 0);
        command.Parameters.AddWithValue("$craft",
            item.Quality == ItemQuality.Craft ? AffixRanges.CraftFamilyOf(item) ?? "" : "");

        // The grail card it is, for uniques and set pieces - the cards the count of copies is shown on. Crafted items
        // are not counted.
        var entry = item.Quality is ItemQuality.Unique or ItemQuality.Set ? GrailCatalog.EntryOf(item) : null;
        command.Parameters.AddWithValue("$grailKind", entry?.Kind ?? "");
        command.Parameters.AddWithValue("$grailRow", entry?.Row ?? -1);
    }

    /// <summary>
    /// How many copies of each unique and set piece this collection holds, by the grail card each is - its kind and
    /// row (<see cref="GrailCatalog.EntryOf"/>), ethereal or not. Rows in <paramref name="leaving"/> are left out:
    /// withdrawn under the Move rule, they are the character's until the save takes them out of here.
    /// </summary>
    public IReadOnlyDictionary<(string Kind, int Row), int> GrailCounts(IReadOnlyCollection<long> leaving)
    {
        var counts = new Dictionary<(string Kind, int Row), int>();
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT item_key, grail_kind, grail_row FROM item_meta WHERE grail_kind <> '';";
        using var row = read.ExecuteReader();
        while (row.Read())
        {
            if (leaving.Contains(row.GetInt64(0)))
                continue;

            var key = (row.GetString(1), row.GetInt32(2));
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }

    /// <summary>
    /// Where vaults live. Beside the editor's settings rather than in the game folder: a
    /// vault is the person's own library and outlives any one install of PD2.
    /// <para>
    /// <c>PD2SPC_VAULTS</c> points it elsewhere - how a test works on a copy and never on the
    /// person's own vault.
    /// </para>
    /// </summary>
    public static string Folder =>
        Environment.GetEnvironmentVariable("PD2SPC_VAULTS") is { Length: > 0 } elsewhere
            ? elsewhere
            : EditorData.Vaults;

    /// <summary>One file per season, named for the season it holds.</summary>
    public static string PathFor(string season) =>
        Path.Combine(Folder, $"{season}.pd2lib");

    /// <summary>
    /// The season the installed tables belong to, or why it cannot be told.
    /// <para>
    /// PD2 ships the <b>previous</b> season's encoding columns beside the current ones -
    /// `Save Bits S12` - so the table names S12 and the season in play is S13. Where the
    /// pattern does not hold, no name at all rather than a guessed one: a wrong season
    /// would open another season's vault, or start an empty one beside the real one. So the
    /// vault stays closed until the editor learns the new pattern - the fail-safe rule.
    /// </para>
    /// </summary>
    public static bool TryInstalledSeason(out string season, out string why)
    {
        if (SeasonAfter(D2SLib.Model.TXT.StatEncoding.PreviousSeason) is { } name)
        {
            season = name;
            why = "";
            return true;
        }

        season = "";
        why = D2SLib.Core.TXT is null
            ? L("No game tables loaded.")
            : L("Season unknown: game tables name none.");
        return false;
    }

    /// <summary>
    /// The season the installed tables belong to. Refused, with the reason, where it cannot be told:
    /// a caller that can do without one asks <see cref="TryInstalledSeason"/>.
    /// </summary>
    public static string InstalledSeason() =>
        TryInstalledSeason(out string season, out string why) ? season : throw new InvalidOperationException(why);

    /// <summary>The season after the one previous-season columns name ("S12" -> "S13"), or null for any other name.</summary>
    public static string? SeasonAfter(string previous) =>
        previous.Length > 1 && previous[0] is 'S' or 's'
        && int.TryParse(previous[1..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int was)
            ? $"S{was + 1}"
            : null;

    private readonly SqliteConnection _db;

    private Vault(SqliteConnection db, string path, string season, int statIdBits)
    {
        _db = db;
        FilePath = path;
        Season = season;
        StatIdBits = statIdBits;
    }

    /// <summary>The file this vault is, which is where its backups are named after.</summary>
    public string FilePath { get; }

    /// <summary>Which season's tables read this vault's items.</summary>
    public string Season { get; }

    /// <summary>The stat-id width its items were written at.</summary>
    public int StatIdBits { get; }

    /// <summary>What <see cref="Open"/> is given for a vault that lives in memory only.</summary>
    public const string MemoryPath = ":memory:";

    /// <summary>
    /// A vault in memory, gone when disposed: for asking the vault's search of items that are not
    /// stored - the inventory's, which the Items page searches with the vault's own query, so the two can never answer differently.
    /// </summary>
    public static Vault OpenInMemory(string season, int statIdBits) => Open(MemoryPath, season, statIdBits);

    /// <summary>
    /// Opens a vault, creating it if it is not there yet.
    /// <para>
    /// <c>foreign_keys</c> is off by default in SQLite and every cascade here depends on it, so
    /// it is turned on per connection rather than assumed.
    /// </para>
    /// </summary>
    public static Vault Open(string path, string season, int statIdBits)
    {
        bool inMemory = path == MemoryPath;
        if (!inMemory)
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        // Never pooled in memory: a pooled connection handed back would be another's vault.
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = !inMemory,
        }.ToString());

        // A failed open lets its connection go whole: left to the pool it held the file, and a
        // collection could not be deleted after it.
        try
        {
            db.Open();
            return Ready(db, path, season, statIdBits);
        }
        catch
        {
            Let(db);
            throw;
        }
    }

    /// <summary>Closes a connection for good: out of SQLite's pool too, so nothing of it lives on.</summary>
    private static void Let(SqliteConnection db)
    {
        db.Dispose();
        SqliteConnection.ClearPool(db);
    }

    /// <summary>The open connection made a vault: its tables, its stamp, its upgrades.</summary>
    private static Vault Ready(SqliteConnection db, string path, string season, int statIdBits)
    {
        Run(db, "PRAGMA journal_mode=WAL;");
        Run(db, "PRAGMA foreign_keys=ON;");

        // SQLite has the REGEXP operator and no function behind it; this is it, for the search's text box.
        db.CreateFunction("regexp", (string? pattern, string? text) => SearchPattern.Matches(pattern, text), isDeterministic: true);
        // Every commit on the disk before it returns. With NORMAL a power cut could undo a store the
        // character file (written through) had already given up the item for - the item gone from both,
        // and the journal with it. FULL costs ~0.4 ms a commit (measured on both disks).
        Run(db, "PRAGMA synchronous=FULL;");

        // What a session that never closed the file left in its write-ahead log, folded into the file
        // itself: a build that closed no vault on exit left the player's softcore collection
        // a 4 KB file beside a 2.6 MB log - whole only with its -wal, which a copy of the
        // .pd2lib alone would have left behind. SQLite reads the log either way; this makes the file
        // stand on its own again from the first open.
        Run(db, "PRAGMA wal_checkpoint(TRUNCATE);");

        Run(db, """
            CREATE TABLE IF NOT EXISTS vault (
              id                 INTEGER PRIMARY KEY CHECK (id = 1),
              schema_version     INTEGER NOT NULL,
              projection_version INTEGER NOT NULL,
              season             TEXT    NOT NULL,
              stat_id_bits       INTEGER NOT NULL,
              item_version       INTEGER NOT NULL,
              created_at         INTEGER NOT NULL);

            CREATE TABLE IF NOT EXISTS item (
              key       INTEGER PRIMARY KEY,
              blob      BLOB    NOT NULL,
              hash      BLOB    NOT NULL,
              origin_id INTEGER,
              source    TEXT    NOT NULL DEFAULT '',
              added_at  INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS item_by_hash   ON item(hash);
            CREATE INDEX IF NOT EXISTS item_by_origin ON item(origin_id);

            CREATE INDEX IF NOT EXISTS item_added     ON item(added_at);

            CREATE TABLE IF NOT EXISTS item_note (
              item_key  INTEGER PRIMARY KEY REFERENCES item(key) ON DELETE CASCADE,
              remark    TEXT    NOT NULL,
              edited_at INTEGER NOT NULL);
            """);

        using (var stamp = db.CreateCommand())
        {
            stamp.CommandText = """
                INSERT OR IGNORE INTO vault
                  (id, schema_version, projection_version, season, stat_id_bits,
                   item_version, created_at)
                VALUES (1, $schema, $projection, $season, $bits, $version, $now);
                """;
            // Stamped as the first shape, whatever this build is: a new vault then walks the
            // same upgrade steps an old one does, so they are run every time one is made
            // rather than once, on someone's only copy. Projection 0 is "none yet", which is
            // what has Reproject create item_meta in the first place.
            stamp.Parameters.AddWithValue("$schema", 1);
            stamp.Parameters.AddWithValue("$projection", 0);
            stamp.Parameters.AddWithValue("$season", season);
            stamp.Parameters.AddWithValue("$bits", statIdBits);
            stamp.Parameters.AddWithValue("$version", ItemBlob.Version);
            stamp.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            stamp.ExecuteNonQuery();
        }

        string held = season;
        int heldBits = statIdBits;

        using (var read = db.CreateCommand())
        {
            read.CommandText = "SELECT season, stat_id_bits FROM vault WHERE id = 1;";
            using var row = read.ExecuteReader();
            if (row.Read())
            {
                held = row.GetString(0);
                heldBits = row.GetInt32(1);
            }
        }

        Upgrade(db, path);
        Reproject(db, heldBits);

        return new Vault(db, path, held, heldBits);
    }

    /// <summary>
    /// Derives <c>item_meta</c> again from every blob, when it was made by a different
    /// <see cref="ProjectionVersion"/> than this build's.
    /// <para>
    /// Only the derived table is touched - blobs, places and folders are not - so there is
    /// nothing to back up: whatever this writes, the next rebuild can write again. An item that
    /// cannot be read back gets no row rather than stopping the rest; a page lists it all the
    /// same, last in the orders that need the row. Different rather than older, so a vault an
    /// older editor opened after a newer one is made right by the newer one on its next open.
    /// </para>
    /// <para>
    /// Needs the game tables loaded, as depositing does - names and types come from them.
    /// </para>
    /// <para>
    /// And when it was made in another language than the one in use: the lines kept for the search are
    /// words (<see cref="ItemLines"/>), and a search typed in one language does not find them in another.
    /// </para>
    /// </summary>
    private static void Reproject(SqliteConnection db, int statIdBits)
    {
        int at = Convert.ToInt32(Scalar(db, "SELECT projection_version FROM vault WHERE id = 1;"));
        if (at == ProjectionVersion
            && Scalar(db, "SELECT code FROM item_meta_language LIMIT 1;") is string language
            && language == Language.Code)
        {
            return;
        }

        // Read in full before writing: the rows are rewritten on the connection they come from.
        var stored = new List<(long Key, byte[] Blob)>();

        using (var read = db.CreateCommand())
        {
            read.CommandText = "SELECT key, blob FROM item;";
            using var row = read.ExecuteReader();
            while (row.Read())
                stored.Add((row.GetInt64(0), (byte[])row[1]));
        }

        using var transaction = db.BeginTransaction();

        Run(db, transaction,
            "DROP TABLE IF EXISTS item_stat; DROP TABLE IF EXISTS item_meta; DROP TABLE IF EXISTS item_meta_language;" + MetaTable);

        using (var said = db.CreateCommand())
        {
            said.Transaction = transaction;
            said.CommandText = "INSERT INTO item_meta_language (code) VALUES ($code);";
            said.Parameters.AddWithValue("$code", Language.Code);
            said.ExecuteNonQuery();
        }

        int skipped = 0;
        foreach (var (key, blob) in stored)
        {
            using var write = db.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = MetaInsert;
            write.Parameters.AddWithValue("$key", key);

            // Everything that reads the item before anything is written of it: one that fails has no row in either
            // table - both were dropped above - and the others go on.
            List<KeyValuePair<string, double>> stats;
            try
            {
                var item = ItemBlob.Restore(blob, statIdBits);
                Project(write, item);
                stats = StatsOf(item);
            }
            catch (Exception problem)
            {
                skipped++;
                Diagnostics.Log.Warning("vault", $"Item {key} left out of the search: {problem.Message}", problem);
                continue;
            }

            write.ExecuteNonQuery();
            WriteStats(db, transaction, key, stats);
        }

        if (skipped > 0)
            Diagnostics.Log.Warning("vault", $"{skipped} of {stored.Count} items left out of the search.");

        Run(db, transaction, $"UPDATE vault SET projection_version = {ProjectionVersion} WHERE id = 1;");
        transaction.Commit();
    }

    /// <summary>
    /// Brings an older vault up to this build's shape, one step at a time.
    /// <para>
    /// A vault is a document, not a cache - it holds items that exist nowhere else - so it is
    /// changed in place and never recreated. A vault that holds anything is copied first, with
    /// <c>VACUUM INTO</c>: copying the file of an open database can catch it half-written.
    /// Each step runs in one transaction with the version bump, so a failed step leaves the
    /// vault as it was.
    /// </para>
    /// </summary>
    private static void Upgrade(SqliteConnection db, string path)
    {
        int at = Convert.ToInt32(Scalar(db, "SELECT schema_version FROM vault WHERE id = 1;"));

        if (at > SchemaVersion)
        {
            db.Dispose();
            throw new InvalidOperationException(
                L($"Vault from a newer version (format {at}, this one reads up to {SchemaVersion})."));
        }

        if (at == SchemaVersion)
            return;

        // Not of a file staged to be imported: its source is the copy, and one of the scratch file stood in
        // the backups' list under the scratch file's name.
        if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM item;")) > 0 && !IsStaged(path))
            BackUp(db, path, $"v{at}");

        // Read before the transaction opens: a command outside it is refused once it has.
        bool hasCaption = Convert.ToInt32(Scalar(db,
            "SELECT COUNT(*) FROM pragma_table_info('item_note') WHERE name = 'caption';")) > 0;

        // Whether the file still has what step 18 takes away: asked of the file, not of its number, so the
        // step cannot be run twice over one file - a dropped table is not there to drop again.
        bool hasSource = Convert.ToInt32(Scalar(db,
            "SELECT COUNT(*) FROM pragma_table_info('item') WHERE name = 'source';")) > 0;

        // And what step 19 takes away: the columns of 16, there in the file or about to be made by that step.
        bool hasPlace = at < 16 || Convert.ToInt32(Scalar(db,
            "SELECT COUNT(*) FROM pragma_table_info('item') WHERE name = 'page';")) > 0;

        // And step 20's: the finds table, there in the file or about to be made by step 3.
        bool hasFinds = at < 3 || Convert.ToInt32(Scalar(db,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'grail_find';")) > 0;

        using var transaction = db.BeginTransaction();

        if (at < 2)
        {
            // Folders. The column is on item, which no rebuild touches, and never in the blob,
            // so the hash is unaffected. NULL is the root - and SQLite requires it anyway: a
            // column added with REFERENCES may have no other default. No ON DELETE on it, so
            // deleting a folder that still holds items is refused by the database and what
            // happens to them is decided in code. COALESCE because SQLite counts two NULL
            // parents as different, which would let two top-level folders share a name.
            Run(db, transaction, """
                CREATE TABLE folder (
                  id        INTEGER PRIMARY KEY,
                  parent_id INTEGER REFERENCES folder(id),
                  name      TEXT    NOT NULL);
                CREATE UNIQUE INDEX folder_sibling
                  ON folder(COALESCE(parent_id, 0), name COLLATE NOCASE);

                ALTER TABLE item ADD COLUMN folder_id INTEGER REFERENCES folder(id);
                CREATE INDEX item_by_folder ON item(folder_id, added_at);
                """);
        }

        if (at < 3)
        {
            // The grail. A row per find, not per entry: two characters finding the same unique
            // are two finds, and the history is what the card shows. Keyed by the table row,
            // never the name - eight Rainbow Facets share one. In the vault because a vault is
            // one season's, and so is a grail.
            Run(db, transaction, """
                CREATE TABLE grail_find (
                  id        INTEGER PRIMARY KEY,
                  kind      TEXT    NOT NULL,
                  row       INTEGER NOT NULL,
                  ethereal  INTEGER NOT NULL DEFAULT 0,
                  character TEXT    NOT NULL,
                  found_at  INTEGER NOT NULL);
                CREATE INDEX grail_by_entry ON grail_find(kind, row, ethereal);
                """);
        }

        if (at < 4)
        {
            // The item a find was. Null for a find logged before this - nothing can say now.
            Run(db, transaction, """
                ALTER TABLE grail_find ADD COLUMN item_id INTEGER;
                CREATE INDEX grail_by_item ON grail_find(item_id);
                """);
        }

        if (at < 5)
        {
            // What the editor handed out, which a save's items cannot say about themselves: the
            // trades - what a repeat buy is told by - and the items that came out of the vault.
            // Both tell a find from what was not one. The site's ids kept as it writes them, text.
            Run(db, transaction, """
                CREATE TABLE trade (
                  id           INTEGER PRIMARY KEY,
                  at           INTEGER NOT NULL,
                  character    TEXT    NOT NULL,
                  kind         TEXT    NOT NULL,
                  listing_id   TEXT,
                  site_item_id TEXT,
                  site_hash    TEXT,
                  item_id      INTEGER,
                  name         TEXT    NOT NULL,
                  price        TEXT    NOT NULL,
                  paid         TEXT    NOT NULL,
                  mode         TEXT    NOT NULL);
                CREATE INDEX trade_by_listing ON trade(listing_id);
                CREATE INDEX trade_by_site_item ON trade(site_item_id);
                CREATE INDEX trade_by_hash ON trade(site_hash);

                CREATE TABLE withdrawal (
                  id        INTEGER PRIMARY KEY,
                  at        INTEGER NOT NULL,
                  character TEXT    NOT NULL,
                  item_key  INTEGER NOT NULL,
                  item_id   INTEGER NOT NULL,
                  rule      TEXT    NOT NULL);
                CREATE INDEX withdrawal_by_item ON withdrawal(item_id);
                """);
        }

        if (at < 6)
        {
            // Items the editor made itself - a duplicate, a craft's copy. Never a find either.
            Run(db, transaction, """
                CREATE TABLE made (
                  id        INTEGER PRIMARY KEY,
                  at        INTEGER NOT NULL,
                  character TEXT    NOT NULL,
                  item_id   INTEGER NOT NULL);
                CREATE INDEX made_by_item ON made(item_id);
                """);
        }

        // 7 was a caption column beside the note, which the icon view showed; they then became
        // one note. Only a vault that one build opened has it - one other than the
        // note is folded into it, after it where both were written, and the column goes.
        if (at < 8 && hasCaption)
        {
            Run(db, transaction, """
                UPDATE item_note SET remark = CASE
                    WHEN remark = '' THEN caption
                    WHEN caption = '' THEN remark
                    ELSE remark || ' ' || caption END;
                DELETE FROM item_note WHERE remark = '';
                ALTER TABLE item_note DROP COLUMN caption;
                """);
        }

        if (at < 9)
        {
            // What the file is. One key per fact, so a later one (a forged collection's recipe)
            // is a row rather than another step. A file written before this knew nothing of
            // collections: it says nothing, and VaultLibrary decides what it was when it splits it.
            Run(db, transaction, """
                CREATE TABLE about (
                  key   TEXT PRIMARY KEY,
                  value TEXT NOT NULL);
                ALTER TABLE withdrawal ADD COLUMN collection TEXT NOT NULL DEFAULT '';
                """);
        }

        if (at < 10)
        {
            // Who stored it. Read back from the source where that was a character's file - the
            // shared stash's names only its page, and a forged or imported item no one at all.
            Run(db, transaction, """
                ALTER TABLE item ADD COLUMN stored_by TEXT NOT NULL DEFAULT '';
                UPDATE item SET stored_by = substr(source, 1, length(source) - 4)
                 WHERE lower(source) LIKE '%.d2s';
                """);
        }

        if (at < 11)
        {
            // The vault's gold, kept as its moves, as the other records are.
            Run(db, transaction, """
                CREATE TABLE gold_move (
                  id        INTEGER PRIMARY KEY,
                  at        INTEGER NOT NULL,
                  character TEXT    NOT NULL,
                  purse     TEXT    NOT NULL,
                  amount    INTEGER NOT NULL);
                """);
        }

        if (at < 12)
        {
            // Smart sort's categories and the folder each goes to - "Charms/Skillers" - so a
            // folder renamed or moved by hand still receives its items. No reference to folder:
            // a link to a folder since deleted is simply not followed, and replaced.
            Run(db, transaction, """
                CREATE TABLE folder_link (
                  category  TEXT    PRIMARY KEY COLLATE NOCASE,
                  folder_id INTEGER NOT NULL);
                """);
        }

        if (at < 13)
        {
            // The note of an item withdrawn under Move, kept by the item's own id until it is stored
            // again or has waited three months.
            Run(db, transaction, """
                CREATE TABLE parked_note (
                  item_id   INTEGER PRIMARY KEY,
                  remark    TEXT    NOT NULL,
                  character TEXT    NOT NULL,
                  parked_at INTEGER NOT NULL);
                """);
        }

        if (at < 14)
        {
            // A withdrawal keeps what it took out - the row as it was - so restoring the
            // character from before it can give the item back to the vault rather than lose it
            // returned_at marks one given back already.
            // pending_move: a save's moves across files, written before the first file and
            // cleared after the last - one left behind is a save that did not finish.
            Run(db, transaction, """
                ALTER TABLE withdrawal ADD COLUMN blob BLOB;
                ALTER TABLE withdrawal ADD COLUMN origin_id INTEGER;
                ALTER TABLE withdrawal ADD COLUMN source TEXT NOT NULL DEFAULT '';
                ALTER TABLE withdrawal ADD COLUMN folder_id INTEGER;
                ALTER TABLE withdrawal ADD COLUMN stored_by TEXT NOT NULL DEFAULT '';
                ALTER TABLE withdrawal ADD COLUMN added_at INTEGER;
                ALTER TABLE withdrawal ADD COLUMN name TEXT NOT NULL DEFAULT '';
                ALTER TABLE withdrawal ADD COLUMN remark TEXT NOT NULL DEFAULT '';
                ALTER TABLE withdrawal ADD COLUMN returned_at INTEGER;
                CREATE INDEX withdrawal_by_character ON withdrawal(character, at);

                CREATE TABLE pending_move (
                  id           INTEGER PRIMARY KEY,
                  batch        INTEGER NOT NULL,
                  at           INTEGER NOT NULL,
                  kind         TEXT    NOT NULL,
                  path         TEXT    NOT NULL,
                  character    TEXT    NOT NULL,
                  name         TEXT    NOT NULL,
                  item_id      INTEGER,
                  code         TEXT    NOT NULL,
                  blob         BLOB,
                  collection   TEXT    NOT NULL,
                  vault_key    INTEGER,
                  folder_id    INTEGER,
                  source       TEXT    NOT NULL,
                  purse        TEXT    NOT NULL,
                  amount       INTEGER NOT NULL,
                  file_before  INTEGER NOT NULL,
                  file_after   INTEGER NOT NULL,
                  vault_before INTEGER NOT NULL,
                  vault_after  INTEGER NOT NULL);
                """);
        }

        if (at < 15)
        {
            // A find keeps its entry's name beside its row, so a row that names another item in another
            // season is caught. Filled for older finds from
            // the tables they were found under, by GrailCatalog.NameFinds.
            Run(db, transaction, "ALTER TABLE grail_find ADD COLUMN name TEXT NOT NULL DEFAULT '';");
        }

        if (at < 16)
        {
            // Places: the page and the cell an item lay at in its folder, from here to format 19, which drops
            // them (Vault.Sheet.cs). On item, as the folder is, and never in the blob.
            Run(db, transaction, """
                ALTER TABLE item ADD COLUMN page   INTEGER;
                ALTER TABLE item ADD COLUMN cell_x INTEGER;
                ALTER TABLE item ADD COLUMN cell_y INTEGER;
                CREATE INDEX item_by_place ON item(folder_id, page);
                """);
        }

        if (at < 17)
        {
            // One sheet a folder: a grid of the vault's width, 20 to
            // begin with, where a folder was pages of 10 by 15. The pages are laid as they were mostly seen,
            // two across - an odd page beside the even one before it - so nothing lies otherwise than it
            // did: `page` counts the sheet in stretches of 15 rows from here on, and the row within stays.
            Run(db, transaction, """
                UPDATE item SET cell_x = cell_x + 10 * (page % 2), page = page / 2 WHERE page IS NOT NULL;
                INSERT INTO about (key, value) VALUES ('width', '20') ON CONFLICT(key) DO UPDATE SET value = '20';
                """);
        }

        if (at < 18 && hasSource)
        {
            // Only what stays true whatever the game does with an item once it is out of the vault: no extra
            // responsibility for what the editor cannot fully control. Out: the notes and
            // the notes waiting for an item to come back; where an item came from and who stored it; the row
            // a withdrawal kept of what it took, which a restore gave back - a restore reads the files in
            // hand now, and only adds (RestoreReconcile) - the index it asked by goes with it. The ids a
            // withdrawal took stay: the grail reads them. The copy taken above holds all of it, should any
            // be wanted back.
            //
            // The gold: one balance, where it was every move. A save's journal told how far it got by a
            // purse's count of moves; it counts the writes of the balance from here on, so a journal still
            // open across this step is re-marked - done where its count said done, not done where not.
            Run(db, transaction, $"""
                DROP TABLE item_note;
                DROP TABLE parked_note;
                ALTER TABLE item DROP COLUMN source;
                ALTER TABLE item DROP COLUMN stored_by;

                ALTER TABLE withdrawal DROP COLUMN blob;
                ALTER TABLE withdrawal DROP COLUMN origin_id;
                ALTER TABLE withdrawal DROP COLUMN source;
                ALTER TABLE withdrawal DROP COLUMN folder_id;
                ALTER TABLE withdrawal DROP COLUMN stored_by;
                ALTER TABLE withdrawal DROP COLUMN added_at;
                ALTER TABLE withdrawal DROP COLUMN name;
                ALTER TABLE withdrawal DROP COLUMN remark;
                ALTER TABLE withdrawal DROP COLUMN returned_at;
                DROP INDEX IF EXISTS withdrawal_by_character;

                UPDATE pending_move SET
                    vault_before = 0,
                    vault_after = CASE WHEN (SELECT COUNT(*) FROM gold_move g
                                              WHERE g.character = pending_move.character COLLATE NOCASE
                                                AND g.purse = pending_move.purse) >= vault_after
                                       THEN 0 ELSE 9223372036854775807 END
                 WHERE kind = '{MoveRecovery.Gold}';
                ALTER TABLE pending_move DROP COLUMN source;

                INSERT INTO about (key, value) SELECT '{GoldKey}', CAST(COALESCE(SUM(amount), 0) AS TEXT) FROM gold_move
                    WHERE 1 ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                INSERT INTO about (key, value) SELECT '{GoldWritesKey}', CAST(COUNT(*) AS TEXT) FROM gold_move
                    WHERE 1 ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                DROP TABLE gold_move;
                """);
        }

        if (at < 19 && hasPlace)
        {
            // No item has a place of its own (Vault.Sheet.cs): the three columns of 16 go - their index first, a
            // column in one cannot be dropped - and the width of 17 with them. What was laid out by hand is not
            // kept: a folder is laid out by its order from here on.
            Run(db, transaction, """
                DROP INDEX IF EXISTS item_by_place;
                ALTER TABLE item DROP COLUMN page;
                ALTER TABLE item DROP COLUMN cell_x;
                ALTER TABLE item DROP COLUMN cell_y;
                DELETE FROM about WHERE key = 'width';
                """);
        }

        if (at < 20 && hasFinds)
        {
            // The grail is the game's: the finds, and the two ledgers whose one
            // reader told a find from an item the editor handed out. Their indexes go with them. The copy taken above
            // holds all three.
            Run(db, transaction, """
                DROP TABLE grail_find;
                DROP TABLE IF EXISTS withdrawal;
                DROP TABLE IF EXISTS made;
                """);
        }

        Run(db, transaction, $"UPDATE vault SET schema_version = {SchemaVersion} WHERE id = 1;");
        transaction.Commit();
    }

    /// <summary>The open connection, for checks that read the file whole (the CLI's vaultundocheck).</summary>
    public SqliteConnection Connection => _db;

    /// <summary>The undo log, once started - see <see cref="StartUndo"/>.</summary>
    public VaultUndo? Undo { get; private set; }

    /// <summary>
    /// Starts logging this file's changes for undo and redo (<see cref="VaultUndo"/>), calling
    /// <paramref name="changed"/> after each one. Once per connection; later calls return the log.
    /// </summary>
    public VaultUndo StartUndo(Action changed)
    {
        if (Undo is null)
        {
            Undo = new VaultUndo(_db, changed);
            Undo.Attach();
        }

        return Undo;
    }

    /// <summary>
    /// Where a vault's copies go before anything rewrites it: a folder for each season and mode
    /// (<see cref="CopiesFolderFor"/>). Those made before there were lie here at the top, and stay.
    /// </summary>
    public static string BackupFolder => Path.Combine(Folder, "Backups");

    /// <summary>
    /// Where copies of the vault at <paramref name="path"/> go: <c>Backups\S14\Softcore</c>, as the vault lies in
    /// <c>S14\Softcore</c>. Softcore's and hardcore's first vaults have one name, "S14 Main" (<see cref="VaultLibrary.MainName"/>),
    /// and they were one vault to the count: a softcore session's copies thinned hardcore's.
    /// A file anywhere else - a scratch file, a test's, a layout from before modes - has <see cref="BackupFolder"/> itself.
    /// </summary>
    public static string CopiesFolderFor(string path)
    {
        string root = Path.GetFullPath(Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string place = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        if (!place.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return BackupFolder;

        return place[root.Length..].Split(Path.DirectorySeparatorChar) is [var season, var mode] && VaultLibrary.IsModeName(mode)
            ? Path.Combine(BackupFolder, season, mode)
            : BackupFolder;
    }

    /// <summary>The folders copies lie in: the given one, for those from before and those of no mode, and its seasons' modes.</summary>
    private static IEnumerable<DirectoryInfo> CopyFolders(DirectoryInfo copies)
    {
        yield return copies;

        foreach (var season in copies.GetDirectories())
        {
            if (RetiredName.IsMatch(season.Name))
                continue;

            foreach (var mode in season.GetDirectories())
            {
                if (VaultLibrary.IsModeName(mode.Name))
                    yield return mode;
            }
        }
    }

    /// <summary>
    /// A consistent copy of the open vault, with <c>VACUUM INTO</c> rather than a file copy.
    /// Returns where it went.
    /// </summary>
    public string BackUp(string why) => BackUp(_db, FilePath, why);

    /// <summary>
    /// How many of a vault's older copies are kept (<see cref="PruneBackups"/>); null keeps every one. The
    /// settings' own number for the vault, apart from a character's - a vault's copy is a hundred times a save's size).
    /// </summary>
    public static int? CopiesToKeep { get; set; }

    // ---- a copy a session: the one restore per session the vault needs ----
    // The vault was copied only before something rewrote it whole - an upgrade, an emptying, a merge. What a
    // session of ordinary use did to it had no copy to go back to. Two moments cover it, both asked by whoever
    // opens the vault for a session (the Shell's VaultService) and never by the CLI or a scratch vault:
    //   as it is opened, where the file is newer than its newest copy - changed since, by a session that
    //     ended without a copy (a crash), or put there by hand;
    //   as it is closed, where the session changed it - the vault as that session left it.
    // So the newest copy always holds the vault as it last was, and a session that changed nothing adds none.

    private long _changesAtCopy;

    private long TotalChanges()
    {
        using var ask = _db.CreateCommand();
        ask.CommandText = "SELECT total_changes();";
        return Convert.ToInt64(ask.ExecuteScalar());
    }

    /// <summary>
    /// When this vault's newest copy was taken, by its name, in its own folder; null where it has none. The copies from
    /// before each mode had a folder are not asked: their name does not say whose they are.
    /// </summary>
    private DateTime? NewestCopy()
    {
        var folder = new DirectoryInfo(CopiesFolderFor(FilePath));
        if (!folder.Exists)
            return null;

        string name = Path.GetFileNameWithoutExtension(FilePath);
        DateTime? newest = null;
        foreach (var file in folder.GetFiles("*.pd2lib"))
        {
            var named = CopyName.Match(file.Name);
            if (!named.Success || !named.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase)
                || !DateTime.TryParseExact(named.Groups["at"].Value, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                                           System.Globalization.DateTimeStyles.AssumeLocal, out var at))
            {
                continue;
            }

            if (newest is null || at > newest)
                newest = at;
        }

        return newest;
    }

    /// <summary>
    /// The vault opened for a session: copied where it holds anything and its file is newer than its newest
    /// copy. Returns where the copy went, or null where none was needed - or none could be made, which never
    /// stops the vault from opening.
    /// </summary>
    public string? CopyAsFound()
    {
        if (FilePath == MemoryPath)
            return null;

        try
        {
            // A second's grace: a copy's name is to the second, and the file was last written just before it.
            if (NewestCopy() is { } copied && File.GetLastWriteTime(FilePath) <= copied.AddSeconds(1))
                return null;

            return Count() == 0 ? null : BackUp(_db, FilePath, SessionCopy);
        }
        catch (Exception problem)
        {
            Diagnostics.Log.Caught(problem);
            return null;
        }
    }

    /// <summary>The session's own upkeep is done: what changes from here on is the session's.</summary>
    public void SessionBegins()
    {
        if (FilePath != MemoryPath)
            _changesAtCopy = TotalChanges();
    }

    /// <summary>
    /// The vault about to be closed: copied where the session changed it, so its newest copy is the vault as
    /// it was left. The log is written into the file first - the copy's moment is then after the file's last
    /// write, and the next opening finds nothing newer than its copy.
    /// </summary>
    public string? CopyAsLeft()
    {
        if (FilePath == MemoryPath)
            return null;

        try
        {
            if (TotalChanges() == _changesAtCopy)
                return null;

            Run(_db, "PRAGMA wal_checkpoint(TRUNCATE);");
            string made = BackUp(_db, FilePath, SessionCopy);
            _changesAtCopy = TotalChanges();
            return made;
        }
        catch (Exception problem)
        {
            Diagnostics.Log.Caught(problem);
            return null;
        }
    }

    /// <summary>
    /// A consistent copy of this file at <paramref name="target"/> - an export - with
    /// <c>VACUUM INTO</c>, which reads through the write-ahead log a file copy would miss.
    /// </summary>
    public void CopyTo(string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
        if (File.Exists(target))
            File.Delete(target);

        using var copy = _db.CreateCommand();
        copy.CommandText = "VACUUM INTO $target;";
        copy.Parameters.AddWithValue("$target", target);
        copy.ExecuteNonQuery();
    }

    // ---- what the file is ------------------------------------------------------------------

    /// <summary>
    /// What the file is (<see cref="VaultLibrary.VaultKind"/>, <see cref="VaultLibrary.FolderKind"/>): a
    /// mode's vault, a folder written out of one or a collection of the older layout, or "" for a file
    /// from before collections.
    /// </summary>
    public string Kind => About("kind") ?? "";

    /// <summary>Whose mode the file is: hardcore, softcore, or null where it does not say.</summary>
    public bool? IsHardcore => About("mode") switch
    {
        "hardcore" => true,
        "softcore" => false,
        _ => null,
    };

    public string? About(string key)
    {
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT value FROM about WHERE key = $key;";
        read.Parameters.AddWithValue("$key", key);
        return read.ExecuteScalar() as string;
    }

    public void SetAbout(string key, string value)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "INSERT INTO about (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = $value;";
        write.Parameters.AddWithValue("$key", key);
        write.Parameters.AddWithValue("$value", value);
        write.ExecuteNonQuery();
    }

    /// <summary>Says what the file is - a mode's vault, or a folder written out - and for which mode.</summary>
    public void Describe(string kind, bool hardcore)
    {
        SetAbout("kind", kind);
        SetAbout("mode", hardcore ? "hardcore" : "softcore");
    }

    /// <summary>
    /// Drops the player's records - the trades and the gold - leaving the items:
    /// what the Default collection kept of the one-file vault it was split from
    /// (<see cref="VaultLibrary.Split"/>). No copy is taken; the caller works on a copy already.
    /// </summary>
    public void DropRecords()
    {
        // In one transaction, as DropItems is: a failure half way kept half the records.
        using var transaction = _db.BeginTransaction();
        Run(_db, transaction,
            $"DELETE FROM trade; DELETE FROM about WHERE key IN ('{GoldKey}', '{GoldWritesKey}');");
        transaction.Commit();
    }

    /// <summary>
    /// Drops the items and their folders, leaving the records: the other half of that split,
    /// and what a search's scratch index is emptied by. No copy is taken; the caller works on a copy
    /// already.
    /// </summary>
    public void DropItems()
    {
        using var transaction = _db.BeginTransaction();
        DeleteItemsAndFolders(transaction);
        transaction.Commit();
    }

    private static string BackUp(SqliteConnection db, string path, string why)
    {
        string target = BackupTarget(path, why);

        using var copy = db.CreateCommand();
        copy.CommandText = "VACUUM INTO $target;";
        copy.Parameters.AddWithValue("$target", target);
        copy.ExecuteNonQuery();

        // And the older copies of this vault held to the count, as a save thins its character's: of its own
        // folder, so never another mode's of the same name.
        try
        {
            if (CopiesToKeep is { } keep)
                PruneFolder(new DirectoryInfo(CopiesFolderFor(path)), keep, Path.GetFileNameWithoutExtension(path), DateTime.Now);
        }
        catch (Exception)
        {
            // Tidying up never fails the copy it follows.
        }

        return target;
    }

    /// <summary>The name a copy of this vault takes in <see cref="CopiesFolderFor"/>, the folder made, for this moment and free.</summary>
    internal static string BackupTarget(string path, string why)
    {
        string folder = CopiesFolderFor(path);
        Directory.CreateDirectory(folder);

        string name = Path.GetFileNameWithoutExtension(path);
        string stem = Path.Combine(folder, $"{name}.{why}.{DateTime.Now:yyyyMMdd-HHmmss}");

        // The stamp is only to the second, and every mode has a "Default" and an "Item Library": both
        // upgraded on one open landed on one name, and VACUUM INTO refused the second - its upgrade
        // undone, the vault left unopened until the next try. A suffix, as a save's backup
        // takes (SaveWriter.Backup).
        string target = $"{stem}.pd2lib";
        for (int n = 2; File.Exists(target); n++)
            target = $"{stem}-{n}.pd2lib";

        return target;
    }

    /// <summary>A copy's name: the vault's, why it was taken, when - and a number where two fell in one second.</summary>
    private static readonly System.Text.RegularExpressions.Regex CopyName = new(
        @"^(?<name>.+?)\.(?<why>[^.]+)\.(?<at>\d{8}-\d{6})(-(?<n>\d+))?\.pd2lib$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Holds the vaults' copies in <see cref="BackupFolder"/> to a count: they
    /// never were - 94 files in twelve days, where a character's are thinned at every save). Of each vault,
    /// by its folder and name: every copy of the last quarter hour (<see cref="Saves.SaveWriter.KeptWhole"/>) stays,
    /// and the newest <paramref name="keep"/> before that; null keeps every one. Returns how many went.
    /// <para>
    /// <b>The session's copies are counted apart from the others</b>: ten sessions
    /// took the copy before an emptying or an upgrade with them, though none stands for another.
    /// </para>
    /// <para>
    /// <b>By count alone, not by age as a character's are.</b> A character's backups are one thing saved over
    /// and over, and the earliest of a day stands for the day. A vault's are each the last sight of something:
    /// the vault before an upgrade, before a merge, before it was deleted - three taken in one day are three
    /// different vaults' worth, and none stands for another.
    /// </para>
    /// <para>
    /// Only files named as <see cref="BackUp(string)"/> names them; a folder of retired collections, and
    /// anything else put there, is left alone. <paramref name="onlyName"/>: one vault's, after a copy of it.
    /// </para>
    /// </summary>
    public static int PruneBackups(int? keep, string? onlyName = null) =>
        PruneCopies(BackupFolder, keep, onlyName, DateTime.Now);

    /// <summary><see cref="PruneBackups"/> on a folder, at a moment: what it does, apart from where and when.</summary>
    public static int PruneCopies(string copies, int? keep, string? onlyName, DateTime now)
    {
        if (keep is not { } count)
            return 0;

        var folder = new DirectoryInfo(copies);
        return folder.Exists ? CopyFolders(folder).ToList().Sum(each => PruneFolder(each, count, onlyName, now)) : 0;
    }

    /// <summary><see cref="PruneCopies"/> of one folder, and none below it.</summary>
    private static int PruneFolder(DirectoryInfo folder, int count, string? onlyName, DateTime now)
    {
        if (!folder.Exists)
            return 0;

        static DateTime TakenAt(FileInfo file, System.Text.RegularExpressions.Match named) =>
            DateTime.TryParseExact(named.Groups["at"].Value, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.AssumeLocal, out var at)
                ? at
                : file.LastWriteTime;

        var byVault = folder.GetFiles("*.pd2lib")
            .Select(file => (File: file, Named: CopyName.Match(file.Name)))
            .Where(each => each.Named.Success)
            .Select(each => (each.File, Vault: each.Named.Groups["name"].Value, Why: each.Named.Groups["why"].Value, At: TakenAt(each.File, each.Named),
                             N: int.TryParse(each.Named.Groups["n"].Value, out int n) ? n : 1))
            .Where(each => onlyName is null || each.Vault.Equals(onlyName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(each => (Vault: each.Vault.ToUpperInvariant(), Session: each.Why.Equals(SessionCopy, StringComparison.OrdinalIgnoreCase)));

        int removed = 0;
        foreach (var vault in byVault)
        {
            // Within one second by the count after it, as a save's backups are (SaveWriter.NewestFirst): by name the
            // plain one, the first, sorted first and the newer of the two was the one thinned away.
            var older = vault.Where(each => now - each.At >= Saves.SaveWriter.KeptWhole)
                             .OrderByDescending(each => each.At).ThenByDescending(each => each.N)
                             .ThenByDescending(each => each.File.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var (file, _, _, _, _) in older.Skip(Math.Max(0, count)))
            {
                try
                {
                    file.Delete();
                    removed++;
                }
                catch (Exception)
                {
                    // A copy that will not delete - open elsewhere, read-only - is left be.
                }
            }
        }

        return removed;
    }

    /// <summary>The tag a session's copy has in its name: what <see cref="CopyAsFound"/> and <see cref="CopyAsLeft"/> take.</summary>
    private const string SessionCopy = "session";

    /// <summary>How a file staged to be imported is named (VaultLibrary.Stage): a scratch copy, in the temp folder.</summary>
    public const string StagedPrefix = "pd2spc-import-";

    private static bool IsStaged(string path) =>
        System.IO.Path.GetFileName(path).StartsWith(StagedPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>A copy of a vault in <see cref="BackupFolder"/>: whose it is, why it was taken, when, and how large.</summary>
    public sealed record BackupCopy(string Path, string Vault, string Why, DateTime At, long Bytes);

    /// <summary>
    /// The vaults' copies, newest first: every vault's - one that is no more, too - of whatever season and
    /// mode, in their folders and those from before there were. Only files named as <see cref="BackUp(string)"/> names them. Listed
    /// for Settings, where one is brought back by the import (the copies were counted
    /// there, and nothing more could be done with them).
    /// </summary>
    public static IReadOnlyList<BackupCopy> Copies() => CopiesIn(BackupFolder);

    /// <summary><see cref="Copies"/> of a folder: what it does, apart from where.</summary>
    public static IReadOnlyList<BackupCopy> CopiesIn(string copies)
    {
        var folder = new DirectoryInfo(copies);
        if (!folder.Exists)
            return [];

        var listed = new List<BackupCopy>();
        foreach (var file in CopyFolders(folder).SelectMany(each => each.GetFiles("*.pd2lib")))
        {
            if (CopyName.Match(file.Name) is not { Success: true } named || IsStaged(file.Name))
                continue;

            var at = DateTime.TryParseExact(named.Groups["at"].Value, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.AssumeLocal, out var taken)
                ? taken
                : file.LastWriteTime;
            listed.Add(new BackupCopy(file.FullName, named.Groups["name"].Value, named.Groups["why"].Value, at, file.Length));
        }

        return [.. listed.OrderByDescending(each => each.At).ThenByDescending(each => each.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>How long a copy of a vault that is no more is kept: thirty days.</summary>
    public static readonly TimeSpan OrphanAge = TimeSpan.FromDays(30);

    /// <summary>A folder of collections retired by the merge into one vault a mode: "Collections merged 20261007-084310".</summary>
    private static readonly System.Text.RegularExpressions.Regex RetiredName = new(
        @"^Collections merged (?<at>\d{8}-\d{6})$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The vaults there are, by their files' names without the ending: every season's and mode's, the copies' folder
    /// apart. Each by its name, for a copy that says nothing more, and by its place under <see cref="Folder"/> -
    /// <c>S14\Softcore\S14 Main</c> - for a copy in that mode's folder.
    /// </summary>
    public static IReadOnlySet<string> LiveNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = new DirectoryInfo(Folder);
        if (!root.Exists)
            return names;

        string copies = Path.GetFullPath(BackupFolder) + Path.DirectorySeparatorChar;
        foreach (var file in root.EnumerateFiles("*.pd2lib", SearchOption.AllDirectories))
        {
            if (file.FullName.StartsWith(copies, StringComparison.OrdinalIgnoreCase))
                continue;

            string name = Path.GetFileNameWithoutExtension(file.Name);
            names.Add(name);
            names.Add(Path.Combine(Path.GetRelativePath(root.FullName, file.DirectoryName ?? root.FullName), name));
        }

        return names;
    }

    /// <summary>
    /// The copies nothing holds to a count (they were two thirds of a data folder of 28 MB): those of a vault that is no more - deleted,
    /// renamed, merged away. <see cref="PruneBackups"/> thins a vault's copies as it makes another, which a vault
    /// that is gone never does, so its last ten stood for ever. Past <see cref="OrphanAge"/> they go, the copy
    /// taken as it was deleted with them: a month to miss it in. The folders of collections retired by the
    /// merge go at the same age. Returns how many files and folders went.
    /// </summary>
    public static int PruneOrphans() => PruneOrphans(BackupFolder, LiveNames(), OrphanAge, DateTime.Now);

    /// <summary><see cref="PruneOrphans()"/> on a folder, at a moment, given the vaults there are.</summary>
    public static int PruneOrphans(string copies, IReadOnlySet<string> live, TimeSpan age, DateTime now)
    {
        var folder = new DirectoryInfo(copies);
        if (!folder.Exists)
            return 0;

        static DateTime? Stamp(string at) =>
            DateTime.TryParseExact(at, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.AssumeLocal, out var taken) ? taken : null;

        int removed = 0;
        foreach (var file in CopyFolders(folder).ToList().SelectMany(each => each.GetFiles("*.pd2lib")))
        {
            var named = CopyName.Match(file.Name);
            if (!named.Success)
                continue;

            // A copy taken while the vault's file was named whole ("S13 Main.pd2lib.v15...") is that vault's too.
            string vault = named.Groups["name"].Value;
            if (vault.EndsWith(".pd2lib", StringComparison.OrdinalIgnoreCase))
                vault = vault[..^".pd2lib".Length];

            // In a mode's folder, that mode's vault: softcore's S14 Main does not keep what hardcore's left.
            string place = Path.GetRelativePath(folder.FullName, file.DirectoryName ?? folder.FullName);
            string whose = place == "." ? vault : Path.Combine(place, vault);

            if (live.Contains(whose) || now - (Stamp(named.Groups["at"].Value) ?? file.LastWriteTime) < age)
                continue;

            try
            {
                file.Delete();
                removed++;
            }
            catch (Exception)
            {
                // A copy that will not delete - open elsewhere, read-only - is left be.
            }
        }

        foreach (var retired in folder.GetDirectories())
        {
            var named = RetiredName.Match(retired.Name);
            if (!named.Success || now - (Stamp(named.Groups["at"].Value) ?? retired.LastWriteTime) < age)
                continue;

            try
            {
                retired.Delete(recursive: true);
                removed++;
            }
            catch (Exception)
            {
                // Left be, as a copy is.
            }
        }

        return removed;
    }

    /// <summary>
    /// Whether this vault was written under the season now installed. A vault from an older
    /// season is not read - it is migrated first. See the design note on why one season per
    /// vault, and how far back a migration can reach.
    /// </summary>
    public bool MatchesSeason(string installed) =>
        string.Equals(Season, installed, StringComparison.OrdinalIgnoreCase);

    private static string Named(string name)
    {
        name = name.Trim();

        if (name.Length == 0)
            throw new InvalidOperationException(L("Require folder name."));

        return name;
    }

    private int Execute(SqliteTransaction transaction, string sql)
    {
        using var command = _db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private static object Db(long? value) => value.HasValue ? value.Value : DBNull.Value;


    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Run(SqliteConnection db, SqliteTransaction transaction, string sql)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>
    /// Closes the file. With the undo log started the connection is let go whole, never pooled: its
    /// TEMP triggers live on the connection and call a function registered on this object, so a
    /// pooled reuse fired them with nothing behind them - "no such function: pd2spc_vault_changed",
    /// and a collection that could not be deleted.
    /// </summary>
    public void Dispose()
    {
        if (Undo is not null)
            Let(_db);
        else
            _db.Dispose();
    }
}
