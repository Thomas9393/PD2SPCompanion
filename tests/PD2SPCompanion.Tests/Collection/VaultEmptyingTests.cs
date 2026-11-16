using Microsoft.Data.Sqlite;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A vault's items and folders deleted whole: Settings' Clean with "Items and folders" ticked
/// (<c>Vault.Clear</c>), a vault emptied (<c>Empty</c>) and a scratch copy's items dropped (<c>DropItems</c>).
/// <para>
/// Cleaning a hardcore vault failed, "Not cleaned: SQLite Error 19: 'UNIQUE
/// constraint failed: index 'folder_sibling''". Each of the three cut every folder's parent before deleting the
/// folders, which for a moment made every folder a top folder - and two of one name under different parents,
/// as sorted storing makes them (Rings/Rare and Amulets/Rare), then stood side by side, which the vault refuses.
/// </para>
/// </summary>
[Collection(nameof(GameTables))]
public sealed class VaultEmptyingTests(Game game) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-vault-emptying-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>A vault as sorted storing leaves one: folders of one name under different parents, items in them.</summary>
    private Vault Sorted(string name)
    {
        Directory.CreateDirectory(_folder);
        var vault = Vault.Open(Path.Combine(_folder, name + ".pd2lib"), "S13", 10);

        long rings = vault.FolderFor(["Rings", "Rare"]);
        long amulets = vault.FolderFor(["Amulets", "Rare"]);
        long deep = vault.FolderFor(["Rings", "Rare", "Old"]);
        vault.FolderFor(["Amulets", "Magic"]);
        vault.FolderFor(["Rings", "Magic"]);
        vault.SetLocked(amulets, true);

        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        long?[] into = [rings, amulets, deep, null];
        int stored = 0;
        foreach (var item in save.PlayerItemList!.Items.Take(12))
        {
            if (vault.Add(item, into[stored % into.Length], out _) is not null)
                stored++;
        }

        Assert.True(stored >= 4, $"{stored} items stored");
        Assert.Equal(7, vault.Folders().Count);
        return vault;
    }

    [CorpusFact]
    public void Cleaning_the_items_and_folders_leaves_none_whatever_the_folders_are_named()
    {
        if (!game.Available)
            return;

        using var vault = Sorted("clean");
        vault.RecordGold([new GoldMove("shared", 500)]);

        Assert.NotNull(vault.Clear(items: true, trades: false, gold: false));

        Assert.Equal(0, vault.Count());
        Assert.Empty(vault.Folders());
        Assert.Empty(vault.LockedFolders());

        // What was not ticked is left.
        Assert.Equal(500, vault.GoldBalance());

        // And the vault takes folders and items again.
        Assert.True(vault.FolderFor(["Rings", "Rare"]) > 0);
    }

    [CorpusFact]
    public void A_vault_emptied_or_its_items_dropped_leaves_none_either()
    {
        if (!game.Available)
            return;

        using (var emptied = Sorted("empty"))
        {
            Assert.NotNull(emptied.Empty());
            Assert.Equal(0, emptied.Count());
            Assert.Empty(emptied.Folders());
        }

        using var dropped = Sorted("drop");
        dropped.DropItems();
        Assert.Equal(0, dropped.Count());
        Assert.Empty(dropped.Folders());
    }
}
