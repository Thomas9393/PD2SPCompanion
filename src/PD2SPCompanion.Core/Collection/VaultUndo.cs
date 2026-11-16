using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Data.Sqlite;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// Undo and redo for a vault file's own changes - folders, cards deleted or moved, an item's
/// edits kept - so the session's history treats the vault as it treats the character,
/// consistently, so a user feels no difference.
/// <para>
/// SQLite's own undo/redo pattern: a trigger on every table writes the inverse of each change as a
/// statement into a log - a row inserted is logged as its delete, a row changed as the update back,
/// a row deleted as its insert. Undoing an action runs its range of the log backwards; the triggers
/// log those changes in turn, and that new range is its redo. Nothing is hand-written per command,
/// so what is added to the vault later is covered without a word here.
/// </para>
/// <para>
/// Triggers and log are TEMP: they live in this connection only, never in the file - the vault on
/// disk is unchanged, and another program opening it sees no trace.
/// </para>
/// </summary>
public sealed class VaultUndo
{
    private readonly SqliteConnection _db;
    private readonly Action _changed;

    /// <summary>The move journal is recovery's own, never undone.</summary>
    private static readonly HashSet<string> Left = new(StringComparer.OrdinalIgnoreCase) { "pending_move" };

    internal VaultUndo(SqliteConnection db, Action changed)
    {
        _db = db;
        _changed = changed;
    }

    /// <summary>While set, changes are logged but not reported: a save or an undo is writing.</summary>
    public bool Quiet { get; set; }

    internal void Attach()
    {
        // A REPLACE's implicit delete fires the delete trigger only with this on - without it a row
        // written over would not come back.
        Run("PRAGMA recursive_triggers=ON;");

        _db.CreateFunction("pd2spc_vault_changed", () =>
        {
            if (!Quiet)
                _changed();
            return 0;
        });

        // AUTOINCREMENT: a number is never given twice. Without it the log, let go of whole (Forget), numbered
        // its next change from 1 again - under every mark its reader had kept (a vault edit after a
        // save that wrote the vault was no step, and Undo took back the step before it).
        Run("CREATE TEMP TABLE IF NOT EXISTS vault_undo(seq INTEGER PRIMARY KEY AUTOINCREMENT, sql TEXT NOT NULL);");

        var tables = new List<string>();
        using (var read = _db.CreateCommand())
        {
            read.CommandText = "SELECT name FROM main.sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var row = read.ExecuteReader();
            while (row.Read())
                tables.Add(row.GetString(0));
        }

        foreach (string table in tables.Where(table => !Left.Contains(table)))
            AttachTo(table);
    }

    private void AttachTo(string table)
    {
        var columns = new List<string>();
        using (var read = _db.CreateCommand())
        {
            read.CommandText = $"PRAGMA main.table_info(\"{table}\");";
            using var row = read.ExecuteReader();
            while (row.Read())
                columns.Add(row.GetString(1));
        }

        if (columns.Count == 0)
            return;

        string q = $"main.\"{table}\"";
        string names = string.Join(",", columns.Select(column => $"\"{column}\""));
        string oldValues = string.Join(" || ',' || ", columns.Select(column => $"quote(old.\"{column}\")"));
        string sets = string.Join(" || ',' || ", columns.Select(column => $"'\"{column}\"=' || quote(old.\"{column}\")"));

        // The statements are built as SQL text; a table's own name inside a string literal is doubled.
        string lit = q.Replace("'", "''");

        Run($"""
            CREATE TEMP TRIGGER IF NOT EXISTS "vault_undo_{table}_i" AFTER INSERT ON {q} BEGIN
              INSERT INTO vault_undo(sql) VALUES('DELETE FROM {lit} WHERE rowid=' || new.rowid);
              SELECT pd2spc_vault_changed();
            END;
            CREATE TEMP TRIGGER IF NOT EXISTS "vault_undo_{table}_u" AFTER UPDATE ON {q} BEGIN
              INSERT INTO vault_undo(sql) VALUES('UPDATE {lit} SET ' || {sets} || ' WHERE rowid=' || old.rowid);
              SELECT pd2spc_vault_changed();
            END;
            CREATE TEMP TRIGGER IF NOT EXISTS "vault_undo_{table}_d" BEFORE DELETE ON {q} BEGIN
              INSERT INTO vault_undo(sql) VALUES('INSERT INTO {lit}(rowid,{names.Replace("'", "''")}) VALUES(' || old.rowid || ',' || {oldValues} || ')');
              SELECT pd2spc_vault_changed();
            END;
            """);
    }

    /// <summary>
    /// Where the log stands: every change so far is at or below it, and every later one above - whatever
    /// <see cref="Forget"/> has let go of since. The highest number the log ever gave, which SQLite keeps for
    /// an AUTOINCREMENT table, not the highest still in it.
    /// </summary>
    public long Mark
    {
        get
        {
            using var read = _db.CreateCommand();
            read.CommandText = "SELECT COALESCE((SELECT seq FROM temp.sqlite_sequence WHERE name = 'vault_undo'), 0);";
            return Convert.ToInt64(read.ExecuteScalar());
        }
    }

    /// <summary>
    /// Takes back the changes logged after <paramref name="from"/> up to <paramref name="to"/>,
    /// newest first, in one transaction - and returns the range the triggers logged while doing it,
    /// which takes this back in turn: an undo's redo, a redo's undo. Foreign keys are checked at the
    /// end, since a cascade logs a parent before its children and their undo runs the other way.
    /// </summary>
    public (long From, long To) Apply(long from, long to)
    {
        var statements = new List<string>();
        using (var read = _db.CreateCommand())
        {
            read.CommandText = "SELECT sql FROM vault_undo WHERE seq > $from AND seq <= $to ORDER BY seq DESC;";
            read.Parameters.AddWithValue("$from", from);
            read.Parameters.AddWithValue("$to", to);
            using var row = read.ExecuteReader();
            while (row.Read())
                statements.Add(row.GetString(0));
        }

        bool wasQuiet = Quiet;
        Quiet = true;
        try
        {
            long start = Mark;
            using var transaction = _db.BeginTransaction();
            Run("PRAGMA defer_foreign_keys=ON;", transaction);
            foreach (string statement in statements)
                Run(statement, transaction);
            transaction.Commit();
            return (start, Mark);
        }
        finally
        {
            Quiet = wasQuiet;
        }
    }

    /// <summary>
    /// What a range did, table by table: rows added, changed or deleted - read off the inverse each
    /// change logged (a row added is logged as its delete, and so on). For naming a step no message
    /// named.
    /// </summary>
    public IReadOnlyList<(string Table, string Done, int Count)> Summary(long from, long to)
    {
        var counts = new Dictionary<(string, string), int>();
        using var read = _db.CreateCommand();
        read.CommandText = "SELECT sql FROM vault_undo WHERE seq > $from AND seq <= $to;";
        read.Parameters.AddWithValue("$from", from);
        read.Parameters.AddWithValue("$to", to);
        using var row = read.ExecuteReader();
        while (row.Read())
        {
            string sql = row.GetString(0);
            string done = sql.StartsWith("DELETE", StringComparison.Ordinal) ? "added"
                : sql.StartsWith("UPDATE", StringComparison.Ordinal) ? "changed"
                : "deleted";
            int start = sql.IndexOf("main.\"", StringComparison.Ordinal) + 6;
            int end = start > 5 ? sql.IndexOf('"', start) : -1;
            string table = end > start ? sql[start..end] : "";
            counts[(table, done)] = counts.GetValueOrDefault((table, done)) + 1;
        }

        return [.. counts.Select(each => (each.Key.Item1, each.Key.Item2, each.Value))];
    }

    /// <summary>Lets go of what the history no longer reaches.</summary>
    public void Forget(long upTo)
    {
        using var write = _db.CreateCommand();
        write.CommandText = "DELETE FROM vault_undo WHERE seq <= $upTo;";
        write.Parameters.AddWithValue("$upTo", upTo);
        write.ExecuteNonQuery();
    }

    private void Run(string sql, SqliteTransaction? transaction = null)
    {
        using var command = _db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
