using Microsoft.Data.Sqlite;

namespace PD2SPCompanion.Core.Collection;

// Trades, the vault's gold, and moves part-way through a save.
public sealed partial class Vault
{
    // ---- trades ----------------------------------------------------------------------------

    /// <summary>
    /// Trades, written down together in one transaction - what a save that bought or sold adds when it is written.
    /// Withdrawals and the items the editor made were written beside them until format 20.
    /// </summary>
    public void RecordTrades(IReadOnlyList<TradeRecord> trades)
    {
        if (trades.Count == 0)
            return;

        using var transaction = _db.BeginTransaction();

        foreach (var trade in trades)
        {
            using var write = _db.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                INSERT INTO trade (at, character, kind, listing_id, site_item_id, site_hash, item_id, name, price, paid, mode)
                VALUES ($at, $character, $kind, $listing, $siteItem, $hash, $item, $name, $price, $paid, $mode);
                """;
            write.Parameters.AddWithValue("$at", trade.At.ToUnixTimeSeconds());
            write.Parameters.AddWithValue("$character", trade.Character);
            write.Parameters.AddWithValue("$kind", trade.Kind);
            write.Parameters.AddWithValue("$listing", (object?)trade.ListingId ?? DBNull.Value);
            write.Parameters.AddWithValue("$siteItem", (object?)trade.SiteItemId ?? DBNull.Value);
            write.Parameters.AddWithValue("$hash", (object?)trade.SiteHash ?? DBNull.Value);
            write.Parameters.AddWithValue("$item", trade.ItemId.HasValue ? (long)trade.ItemId.Value : DBNull.Value);
            write.Parameters.AddWithValue("$name", trade.Name);
            write.Parameters.AddWithValue("$price", trade.Price);
            write.Parameters.AddWithValue("$paid", trade.Paid);
            write.Parameters.AddWithValue("$mode", trade.Mode);
            write.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // ---- the vault's gold ------------------------------------------------------------------

    /// <summary>
    /// The gold in the vault: one number. It was the sum of every move in and out until format 18 - a list
    /// that only grew, read by a restore that no longer reads it.
    /// </summary>
    public long GoldBalance() => long.TryParse(About(GoldKey), out long gold) ? gold : 0;

    /// <summary>
    /// How many times the vault's gold was written. Not a history: a mark a save's journal reads, to tell
    /// whether its write of the gold was reached (<see cref="MoveRecovery"/>) - the balance itself cannot
    /// say, gold in and gold out of one save meeting in it.
    /// </summary>
    public long GoldWrites() => long.TryParse(About(GoldWritesKey), out long writes) ? writes : 0;

    /// <summary>
    /// Gold into the vault or out of it: the balance changed by the moves' sum, in one write - a save's gold
    /// going in, or its gold coming out.
    /// </summary>
    public void RecordGold(IReadOnlyList<GoldMove> moves)
    {
        if (moves.Count == 0)
            return;

        using var transaction = _db.BeginTransaction();
        using (var write = _db.CreateCommand())
        {
            write.Transaction = transaction;
            write.CommandText = $"""
                INSERT INTO about (key, value) VALUES ('{GoldKey}', CAST($amount AS TEXT))
                    ON CONFLICT(key) DO UPDATE SET value = CAST(CAST(value AS INTEGER) + $amount AS TEXT);
                INSERT INTO about (key, value) VALUES ('{GoldWritesKey}', '1')
                    ON CONFLICT(key) DO UPDATE SET value = CAST(CAST(value AS INTEGER) + 1 AS TEXT);
                """;
            write.Parameters.AddWithValue("$amount", moves.Sum(move => move.Amount));
            write.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // ---- moves part-way through a save -----------------------------------------------------

    /// <summary>
    /// Writes down a save's moves across files before any file is written - one batch, in one
    /// transaction - and returns the batch, which <see cref="ClearJournal"/> takes once every
    /// file is. A batch still here later is a save that did not finish (<see cref="MoveRecovery"/>).
    /// </summary>
    public long JournalMoves(IReadOnlyList<PendingMove> moves)
    {
        long batch = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        if (moves.Count == 0)
            return batch;

        using var transaction = _db.BeginTransaction();
        foreach (var move in moves)
        {
            using var write = _db.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                INSERT INTO pending_move (batch, at, kind, path, character, name, item_id, code, blob, collection,
                                          vault_key, folder_id, purse, amount,
                                          file_before, file_after, vault_before, vault_after)
                VALUES ($batch, $at, $kind, $path, $character, $name, $item, $code, $blob, $collection,
                        $key, $folder, $purse, $amount,
                        $fileBefore, $fileAfter, $vaultBefore, $vaultAfter);
                """;
            write.Parameters.AddWithValue("$batch", batch);
            write.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToUnixTimeSeconds());
            write.Parameters.AddWithValue("$kind", move.Kind);
            write.Parameters.AddWithValue("$path", move.Path);
            write.Parameters.AddWithValue("$character", move.Character);
            write.Parameters.AddWithValue("$name", move.Name);
            write.Parameters.AddWithValue("$item", move.ItemId is { } id ? (long)id : DBNull.Value);
            write.Parameters.AddWithValue("$code", move.Code);
            write.Parameters.AddWithValue("$blob", (object?)move.Blob ?? DBNull.Value);
            write.Parameters.AddWithValue("$collection", move.Collection);
            write.Parameters.AddWithValue("$key", move.VaultKey is { } key ? key : DBNull.Value);
            write.Parameters.AddWithValue("$folder", move.FolderId is { } folder ? folder : DBNull.Value);
            write.Parameters.AddWithValue("$purse", move.Purse);
            write.Parameters.AddWithValue("$amount", move.Amount);
            write.Parameters.AddWithValue("$fileBefore", move.FileBefore);
            write.Parameters.AddWithValue("$fileAfter", move.FileAfter);
            write.Parameters.AddWithValue("$vaultBefore", move.VaultBefore);
            write.Parameters.AddWithValue("$vaultAfter", move.VaultAfter);
            write.ExecuteNonQuery();
        }

        transaction.Commit();
        return batch;
    }

    /// <summary>A batch whose files are all written: nothing left to put right.</summary>
    public void ClearJournal(long batch)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "DELETE FROM pending_move WHERE batch = $batch;";
        write.Parameters.AddWithValue("$batch", batch);
        write.ExecuteNonQuery();
    }

    /// <summary>One move checked and put right: its batch may stay for another (<see cref="MoveRecovery"/>).</summary>
    public void ClearJournalMove(long id)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "DELETE FROM pending_move WHERE id = $id;";
        write.Parameters.AddWithValue("$id", id);
        write.ExecuteNonQuery();
    }

    /// <summary>Every move a save left part-way, oldest first.</summary>
    public IReadOnlyList<PendingMove> Journal()
    {
        using var read = _db.CreateCommand();
        read.CommandText = """
            SELECT kind, path, character, name, item_id, code, blob, collection, vault_key, folder_id,
                   purse, amount, file_before, file_after, vault_before, vault_after, batch, at, id
            FROM pending_move ORDER BY batch, id;
            """;

        var found = new List<PendingMove>();
        using var row = read.ExecuteReader();
        while (row.Read())
        {
            found.Add(new PendingMove(
                row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3),
                row.IsDBNull(4) ? null : (uint)row.GetInt64(4),
                row.GetString(5),
                row.IsDBNull(6) ? null : (byte[])row[6],
                row.GetString(7),
                row.IsDBNull(8) ? null : row.GetInt64(8),
                row.IsDBNull(9) ? null : row.GetInt64(9),
                row.GetString(10), row.GetInt64(11),
                row.GetInt64(12), row.GetInt64(13), row.GetInt64(14), row.GetInt64(15))
            {
                Batch = row.GetInt64(16),
                At = DateTimeOffset.FromUnixTimeSeconds(row.GetInt64(17)),
                Id = row.GetInt64(18),
            });
        }

        return found;
    }

    /// <summary>
    /// How many copies of an item the vault holds: rows with its id, or - a compact item, which
    /// has none - rows with its very blob. What a journal compares before and after.
    /// </summary>
    public long CountHeld(uint? id, byte[]? blob)
    {
        using var read = _db.CreateCommand();
        if (id is { } origin)
        {
            read.CommandText = "SELECT COUNT(*) FROM item WHERE origin_id = $id;";
            read.Parameters.AddWithValue("$id", (long)origin);
        }
        else
        {
            // By the hash first, which is indexed, then the blob itself: compared alone, every row's
            // blob was read and compared.
            read.CommandText = "SELECT COUNT(*) FROM item WHERE hash = $hash AND blob = $blob;";
            read.Parameters.AddWithValue("$hash", blob is null ? DBNull.Value : System.Security.Cryptography.SHA256.HashData(blob));
            read.Parameters.AddWithValue("$blob", (object?)blob ?? DBNull.Value);
        }

        return Convert.ToInt64(read.ExecuteScalar());
    }

    /// <summary>Every row's key and the id of the item it holds - compact items, which have none, left out.</summary>
    public IReadOnlyList<(long Key, uint Id)> OriginIds()
    {
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT key, origin_id FROM item WHERE origin_id IS NOT NULL;";

        var found = new List<(long, uint)>();
        using var row = read.ExecuteReader();
        while (row.Read())
            found.Add((row.GetInt64(0), (uint)row.GetInt64(1)));
        return found;
    }

    /// <summary>Whether a row is still there.</summary>
    public bool HasRow(long key)
    {
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM item WHERE key = $key;";
        read.Parameters.AddWithValue("$key", key);
        return Convert.ToInt64(read.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// Takes trades out of the ledger again: an undo's, of the save that wrote them (the session's history:
    /// without it, a sale undone would still read "Sold"). One row for each, the newest that says the same -
    /// when, who, which way, what, at what price and for what. Returns how many went.
    /// </summary>
    public int ForgetTrades(IReadOnlyList<TradeRecord> trades)
    {
        if (trades.Count == 0)
            return 0;

        int gone = 0;
        using var transaction = _db.BeginTransaction();
        foreach (var trade in trades)
        {
            using var forget = _db.CreateCommand();
            forget.Transaction = transaction;
            forget.CommandText = """
                DELETE FROM trade WHERE id = (
                    SELECT MAX(id) FROM trade
                    WHERE at = $at AND character = $character AND kind = $kind
                      AND name = $name AND price = $price AND paid = $paid);
                """;
            forget.Parameters.AddWithValue("$at", trade.At.ToUnixTimeSeconds());
            forget.Parameters.AddWithValue("$character", trade.Character);
            forget.Parameters.AddWithValue("$kind", trade.Kind);
            forget.Parameters.AddWithValue("$name", trade.Name);
            forget.Parameters.AddWithValue("$price", trade.Price);
            forget.Parameters.AddWithValue("$paid", trade.Paid);
            gone += forget.ExecuteNonQuery();
        }

        transaction.Commit();
        return gone;
    }

    /// <summary>Every trade, oldest first.</summary>
    public IReadOnlyList<TradeRecord> Trades() => ReadTrades("SELECT * FROM trade ORDER BY at, id;", null);

    /// <summary>
    /// The first purchase of this item, or null: the same listing, the same item relisted (the
    /// site's item id) or the same item posted twice (its hash). Empty values match nothing.
    /// </summary>
    public TradeRecord? BoughtBefore(string listingId, string siteItemId, string siteHash) =>
        ReadTrades("""
            SELECT * FROM trade
            WHERE kind = 'bought'
              AND ((listing_id = $listing AND $listing <> '')
                OR (site_item_id = $siteItem AND $siteItem <> '')
                OR (site_hash = $hash AND $hash <> ''))
            ORDER BY at, id LIMIT 1;
            """, command =>
        {
            command.Parameters.AddWithValue("$listing", listingId);
            command.Parameters.AddWithValue("$siteItem", siteItemId);
            command.Parameters.AddWithValue("$hash", siteHash);
        }).FirstOrDefault();

    /// <summary>
    /// A character renamed (<see cref="CharacterRename"/>): its trades go with it - the one table that keeps a
    /// character's name since format 20 (its finds, withdrawals and made items went). Returns the rows moved.
    /// </summary>
    public int RenameCharacter(string oldName, string newName)
    {
        using var rename = _db.CreateCommand();
        rename.CommandText = "UPDATE trade SET character = $new WHERE character = $old;";
        rename.Parameters.AddWithValue("$new", newName);
        rename.Parameters.AddWithValue("$old", oldName);
        return rename.ExecuteNonQuery();
    }

    private List<TradeRecord> ReadTrades(string sql, Action<SqliteCommand>? bind)
    {
        var trades = new List<TradeRecord>();
        using var read = _db.CreateCommand();
        read.CommandText = sql;
        bind?.Invoke(read);
        using var row = read.ExecuteReader();

        string? Text(string column) => row.IsDBNull(row.GetOrdinal(column)) ? null : row.GetString(row.GetOrdinal(column));

        while (row.Read())
        {
            trades.Add(new TradeRecord(
                DateTimeOffset.FromUnixTimeSeconds(row.GetInt64(row.GetOrdinal("at"))),
                row.GetString(row.GetOrdinal("character")),
                row.GetString(row.GetOrdinal("kind")),
                Text("listing_id"), Text("site_item_id"), Text("site_hash"),
                row.IsDBNull(row.GetOrdinal("item_id")) ? null : (uint)row.GetInt64(row.GetOrdinal("item_id")),
                row.GetString(row.GetOrdinal("name")),
                row.GetString(row.GetOrdinal("price")),
                row.GetString(row.GetOrdinal("paid")),
                row.GetString(row.GetOrdinal("mode"))));
        }

        return trades;
    }
}
