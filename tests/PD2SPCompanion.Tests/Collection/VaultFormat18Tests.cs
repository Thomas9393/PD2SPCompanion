using Microsoft.Data.Sqlite;

using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The vault keeps only what stays true, taking on no responsibility it cannot
/// fully control: format 18. An older file keeps
/// its items, each with the day it was added, and its gold as one number; the notes, an item's source and who
/// stored it, and the rows a withdrawal kept are gone, a copy taken first - and, since format 19, the page and the
/// cell each item lay at; since format 20 the grail's finds, the withdrawals
/// and the made items.
/// And the date an item was added is the date of its stay: kept while it lies there, whatever is done to it
/// in place, and new when it comes again.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class VaultFormat18Tests(Game game) : IDisposable
{
    private readonly string _vaults = Path.Combine(Path.GetTempPath(), "pd2spc-format18-" + Guid.NewGuid().ToString("N"));
    private readonly string? _vaultsBefore = Environment.GetEnvironmentVariable("PD2SPC_VAULTS");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaultsBefore);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_vaults, recursive: true); } catch (IOException) { }
    }

    /// <summary>A vault folder of this test's own: a copy taken before an upgrade never lands in the player's.</summary>
    private string OwnVault()
    {
        Directory.CreateDirectory(_vaults);
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaults);
        return Path.Combine(_vaults, "format.pd2lib");
    }

    private static List<D2SLib.Model.Save.Item> Items()
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        return [.. save.PlayerItemList!.Items.Where(item => !item.IsCompact).Take(4)];
    }

    private static void Run(string path, string sql)
    {
        using var db = new SqliteConnection($"Data Source={path};Pooling=False");
        db.Open();
        using var write = db.CreateCommand();
        write.CommandText = sql;
        write.ExecuteNonQuery();
    }

    private static List<string> Names(Vault vault, string sql)
    {
        var found = new List<string>();
        using var read = vault.Connection.CreateCommand();
        read.CommandText = sql;
        using var row = read.ExecuteReader();
        while (row.Read())
            found.Add(row.GetString(0));
        return found;
    }

    [CorpusFact]
    public void An_older_vault_keeps_its_items_their_dates_and_its_gold_and_loses_the_rest()
    {
        if (!game.Available)
            return;

        string path = OwnVault();
        long marked;
        using (var vault = Vault.Open(path, "S13", 10))
        {
            var keys = Items().Select(item => vault.Add(item, out _)).OfType<long>().ToList();
            Assert.Equal(4, keys.Count);
            marked = keys[0];
            vault.StoredLongAgo(marked);
        }

        // The file as format 17 had it: the tables and columns that steps 18 and 20 take away, filled - the grail's
        // finds, the withdrawals and the made items among them, which a file made now has none of.
        SqliteConnection.ClearAllPools();
        Run(path, $"""
            CREATE TABLE grail_find (id INTEGER PRIMARY KEY, kind TEXT NOT NULL, row INTEGER NOT NULL,
                                     ethereal INTEGER NOT NULL DEFAULT 0, character TEXT NOT NULL, found_at INTEGER NOT NULL,
                                     item_id INTEGER, name TEXT NOT NULL DEFAULT '');
            CREATE INDEX grail_by_entry ON grail_find(kind, row, ethereal);
            INSERT INTO grail_find (kind, row, character, found_at, item_id, name) VALUES ('unique', 73, 'Tester', 1, 777, 'Coif of Glory');
            CREATE TABLE withdrawal (id INTEGER PRIMARY KEY, at INTEGER NOT NULL, character TEXT NOT NULL,
                                     item_key INTEGER NOT NULL, item_id INTEGER NOT NULL, rule TEXT NOT NULL,
                                     collection TEXT NOT NULL DEFAULT '');
            CREATE INDEX withdrawal_by_item ON withdrawal(item_id);
            INSERT INTO withdrawal (at, character, item_key, item_id, rule, collection) VALUES (1, 'Tester', 1, 888, 'Move', 'c');
            CREATE TABLE made (id INTEGER PRIMARY KEY, at INTEGER NOT NULL, character TEXT NOT NULL, item_id INTEGER NOT NULL);
            INSERT INTO made (at, character, item_id) VALUES (1, 'Tester', 999);

            CREATE TABLE item_note (item_key INTEGER PRIMARY KEY REFERENCES item(key) ON DELETE CASCADE,
                                    remark TEXT NOT NULL, edited_at INTEGER NOT NULL);
            INSERT INTO item_note VALUES ({marked}, 'keep for the ubers', 1);
            CREATE TABLE parked_note (item_id INTEGER PRIMARY KEY, remark TEXT NOT NULL, character TEXT NOT NULL, parked_at INTEGER NOT NULL);
            INSERT INTO parked_note VALUES (555, 'waiting', 'Tester', 1);

            ALTER TABLE item ADD COLUMN source TEXT NOT NULL DEFAULT '';
            ALTER TABLE item ADD COLUMN stored_by TEXT NOT NULL DEFAULT '';
            UPDATE item SET source = 'Tester.d2s', stored_by = 'Tester';

            -- And what format 16 gave every item and format 19 takes away: a page and a cell, their index, and
            -- the vault's width. A file of format 17 had them; left out here, the step that drops them would be
            -- passed by, and 17 to 19 in one open never run.
            ALTER TABLE item ADD COLUMN page   INTEGER;
            ALTER TABLE item ADD COLUMN cell_x INTEGER;
            ALTER TABLE item ADD COLUMN cell_y INTEGER;
            CREATE INDEX item_by_place ON item(folder_id, page);
            UPDATE item SET page = 0, cell_x = (key * 2) % 18, cell_y = 0;
            INSERT INTO about (key, value) VALUES ('width', '30') ON CONFLICT(key) DO UPDATE SET value = '30';

            ALTER TABLE withdrawal ADD COLUMN blob BLOB;
            ALTER TABLE withdrawal ADD COLUMN origin_id INTEGER;
            ALTER TABLE withdrawal ADD COLUMN source TEXT NOT NULL DEFAULT '';
            ALTER TABLE withdrawal ADD COLUMN folder_id INTEGER;
            ALTER TABLE withdrawal ADD COLUMN stored_by TEXT NOT NULL DEFAULT '';
            ALTER TABLE withdrawal ADD COLUMN added_at INTEGER;
            ALTER TABLE withdrawal ADD COLUMN name TEXT NOT NULL DEFAULT '';
            ALTER TABLE withdrawal ADD COLUMN remark TEXT NOT NULL DEFAULT '';
            ALTER TABLE withdrawal ADD COLUMN returned_at INTEGER;
            UPDATE withdrawal SET blob = x'0102', name = 'Something';

            CREATE TABLE gold_move (id INTEGER PRIMARY KEY, at INTEGER NOT NULL, character TEXT NOT NULL,
                                    purse TEXT NOT NULL, amount INTEGER NOT NULL);
            INSERT INTO gold_move (at, character, purse, amount)
                VALUES (1, 'Tester', 'stash', 1000), (2, 'Tester', 'inventory', -250), (3, '', 'edited', 5);
            DELETE FROM about WHERE key IN ('gold', 'gold_writes');

            -- A save's journal still open: the stash's gold written (one move of one), the inventory's not (one of two).
            ALTER TABLE pending_move ADD COLUMN source TEXT NOT NULL DEFAULT '';
            INSERT INTO pending_move (batch, at, kind, path, character, name, item_id, code, blob, collection, vault_key,
                                      folder_id, source, purse, amount, file_before, file_after, vault_before, vault_after)
                VALUES (7, 1, 'gold', 'x', 'Tester', 'gold', NULL, '', NULL, 'c', NULL, NULL, '', 'stash', 1000, 0, 0, 0, 1),
                       (7, 1, 'gold', 'x', 'Tester', 'gold', NULL, '', NULL, 'c', NULL, NULL, '', 'inventory', -250, 0, 0, 1, 2);

            UPDATE vault SET schema_version = 17;
            """);

        using (var vault = Vault.Open(path, "S13", 10))
        {
            // The items, each with the day it was added.
            Assert.Equal(4, vault.Count());
            var rows = vault.RowsOf(vault.AllKeys());
            Assert.Equal(VaultDates.LongAgo, Assert.Single(rows, row => row.Key == marked).Added);
            Assert.All(rows.Where(row => row.Key != marked), row => Assert.True(row.Added > DateTimeOffset.UtcNow.AddMinutes(-5)));

            // The gold, as one number.
            Assert.Equal(755, vault.GoldBalance());
            Assert.Equal(3, vault.GoldWrites());

            // The journal, marked again by the writes of the gold: reached, and not.
            var journal = vault.Journal();
            Assert.True(vault.GoldWrites() >= journal.Single(move => move.Purse == "stash").VaultAfter);
            Assert.True(vault.GoldWrites() < journal.Single(move => move.Purse == "inventory").VaultAfter);

            // The rest is gone.
            var tables = Names(vault, "SELECT name FROM sqlite_master WHERE type = 'table';");
            Assert.DoesNotContain("item_note", tables);
            Assert.DoesNotContain("parked_note", tables);
            Assert.DoesNotContain("gold_move", tables);
            Assert.Equal(["key", "blob", "hash", "origin_id", "added_at", "folder_id"],
                         Names(vault, "SELECT name FROM pragma_table_info('item');"));
            Assert.DoesNotContain("source", Names(vault, "SELECT name FROM pragma_table_info('pending_move');"));

            // And, in the same open, format 20's: the grail is the game's, so the finds go, and the two ledgers that
            // told a find from an item the editor handed out - their indexes with them. The trades stay.
            Assert.DoesNotContain("grail_find", tables);
            Assert.DoesNotContain("withdrawal", tables);
            Assert.DoesNotContain("made", tables);
            Assert.Contains("trade", tables);
            var indexes = Names(vault, "SELECT name FROM sqlite_master WHERE type = 'index';");
            Assert.DoesNotContain("grail_by_entry", indexes);
            Assert.DoesNotContain("withdrawal_by_item", indexes);
            Assert.Equal(Vault.SchemaVersion, Convert.ToInt32(Names(vault, "SELECT CAST(schema_version AS TEXT) FROM vault;")[0]));

            // A copy taken first, which holds all of it.
            Assert.NotEmpty(Directory.GetFiles(Vault.BackupFolder, "format.v17.*.pd2lib"));

            // And the places with it, in the same open (format 19): no index of them, no width, the items laid out by the rule.
            Assert.DoesNotContain("item_by_place", Names(vault, "SELECT name FROM sqlite_master WHERE type = 'index';"));
            Assert.Empty(Names(vault, "SELECT value FROM about WHERE key = 'width';"));
            Assert.Equal([Vault.SchemaVersion.ToString()], Names(vault, "SELECT CAST(schema_version AS TEXT) FROM vault WHERE id = 1;"));
            Assert.Equal(4, vault.Sheet(null, "Stored", false, 20).Marks.Count);
        }

        // And a copy of it as it was, taken before the step.
        Assert.Single(Directory.GetFiles(Vault.BackupFolder, "format.v17.*.pd2lib"));
    }

    [CorpusFact]
    public void The_gold_is_one_balance_and_each_write_of_it_is_counted()
    {
        if (!game.Available)
            return;

        using var vault = Vault.Open(OwnVault(), "S13", 10);
        Assert.Equal(0, vault.GoldBalance());
        Assert.Equal(0, vault.GoldWrites());

        // A save's gold going in, from two purses: one write.
        vault.RecordGold([new GoldMove("stash", 100), new GoldMove("inventory", 50)]);
        Assert.Equal(150, vault.GoldBalance());
        Assert.Equal(1, vault.GoldWrites());

        vault.RecordGold([new GoldMove("shared", -30)]);
        Assert.Equal(120, vault.GoldBalance());
        Assert.Equal(2, vault.GoldWrites());

        // Nothing moved is nothing written.
        vault.RecordGold([]);
        Assert.Equal(2, vault.GoldWrites());

        // The gold deleted (Settings): none left, and the items stay.
        vault.Add(Items()[0], out _);
        Assert.NotNull(vault.Clear(items: false, trades: false, gold: true));
        Assert.Equal(0, vault.GoldBalance());
        Assert.Equal(1, vault.Count());
    }

    [CorpusFact]
    public void The_date_added_is_the_stays_kept_in_place_and_put_back_and_new_when_the_item_comes_again()
    {
        if (!game.Available)
            return;

        using var vault = Vault.Open(OwnVault(), "S13", 10);
        var item = Items()[0];
        var started = DateTimeOffset.UtcNow.AddSeconds(-2);

        // Stored: now.
        long key = vault.Add(item, out _) ?? 0;
        Assert.True(vault.RowsOf([key]).Single().Added >= started);

        // It has lain there long. Moved to a folder and edited in place, it never left.
        vault.StoredLongAgo(key);
        long folder = vault.CreateFolder(null, "Kept");
        vault.MoveItems([key], folder);
        Assert.NotNull(vault.Replace(key, item, out _));
        var row = vault.RowsOf([key]).Single();
        Assert.Equal(VaultDates.LongAgo, row.Added);
        Assert.Equal(folder, row.FolderId);

        // Withdrawn and the withdrawal undone: put back as the row it was, its stay going on.
        vault.Remove([key]);
        long back = vault.PutBack(row, out _) ?? 0;
        Assert.Equal(VaultDates.LongAgo, vault.RowsOf([back]).Single().Added);

        // Withdrawn and stored again: a new record, added now.
        vault.Remove([back]);
        long again = vault.Add(item, folder, out _) ?? 0;
        Assert.True(vault.RowsOf([again]).Single().Added >= started);

        // Ordered by it, the newest last.
        long old = vault.Add(Items()[1], folder, out _) ?? 0;
        vault.StoredLongAgo(old);
        Assert.Equal([old, again], vault.Page(folder, "Stored", descending: false, 0, 10).Select(each => each.Key));
    }
}
