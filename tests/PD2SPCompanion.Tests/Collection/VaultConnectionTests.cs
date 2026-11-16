using System;
using System.IO;

using Microsoft.Data.Sqlite;

using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A vault file's connection is let go of whole when the vault is (a collection
/// could not be deleted). The undo log's TEMP triggers live on the connection and call a function
/// registered on it; handed back to SQLite's pool and reopened, they fired with no function behind
/// them - "no such function: pd2spc_vault_changed" - and the failed open then held the file.
/// </summary>
public sealed class VaultConnectionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-vault-tests-" + Guid.NewGuid().ToString("N"));

    public VaultConnectionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string PathOf(string name) => Path.Combine(_folder, name);

    [Fact]
    public void A_vault_with_undo_reopens_and_deletes()
    {
        string path = PathOf("collection.pd2lib");

        using (var vault = Vault.Open(path, "S13", 10))
            vault.StartUndo(() => { });

        // The delete's own steps: open for the backup copy, close, clear the pool, delete.
        using (var again = Vault.Open(path, "S13", 10))
            again.StartUndo(() => { });

        SqliteConnection.ClearAllPools();
        foreach (string file in Directory.GetFiles(_folder, "collection.pd2lib*"))
            File.Delete(file);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_failed_open_leaves_the_file_free()
    {
        // A file whose vault table is not the vault's: the stamp's insert fails, and Open with it.
        string path = PathOf("broken.pd2lib");
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = "CREATE TABLE vault (id INTEGER PRIMARY KEY, other TEXT NOT NULL);";
            make.ExecuteNonQuery();
        }

        Assert.ThrowsAny<Exception>(() => Vault.Open(path, "S13", 10));

        SqliteConnection.ClearAllPools();
        foreach (string file in Directory.GetFiles(_folder, "broken.pd2lib*"))
            File.Delete(file);

        Assert.False(File.Exists(path));
    }
}
