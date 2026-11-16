using System;
using System.IO;

using Microsoft.Data.Sqlite;

using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The vault's quick access, a section for fast access just like Windows Explorer's: the
/// folders pinned, kept in the vault's own file in the order pinned - so they go where the vault goes - a folder
/// pinned once, taken off, and no longer listed once it is no more.
/// </summary>
public sealed class VaultQuickAccessTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-vault-quick-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private Vault Open()
    {
        Directory.CreateDirectory(_folder);
        return Vault.Open(Path.Combine(_folder, "quick.pd2lib"), "S13", 10);
    }

    [Fact]
    public void A_deleted_folders_pin_does_not_pass_to_the_next_folder_of_its_id()
    {
        using var vault = Open();
        long keep = vault.CreateFolder(null, "Keep");
        long doomed = vault.CreateFolder(null, "Doomed");
        vault.SetQuickAccess(keep, true);
        vault.SetQuickAccess(doomed, true);

        vault.DeleteFolder(doomed, false);
        long next = vault.CreateFolder(null, "Next");
        Assert.Equal(doomed, next);
        Assert.Equal(new[] { keep }, vault.QuickAccess());

        // Emptied of its folders, ids begin again and no old pin comes back.
        vault.SetQuickAccess(next, true);
        vault.Clear(items: true, trades: false, gold: false);
        Assert.Equal(keep, vault.CreateFolder(null, "Again"));
        Assert.Empty(vault.QuickAccess());
    }

    [Fact]
    public void Folders_are_listed_in_the_order_pinned_each_once_and_one_taken_off_is_gone()
    {
        long charms, small, jewels;
        using (var vault = Open())
        {
            Assert.Empty(vault.QuickAccess());

            charms = vault.CreateFolder(null, "Charms");
            small = vault.CreateFolder(charms, "Small");
            jewels = vault.CreateFolder(null, "Jewels");

            vault.SetQuickAccess(small, true);
            vault.SetQuickAccess(jewels, true);
            Assert.Equal(new[] { small, jewels }, vault.QuickAccess());

            // Pinned again: once, and at the list's end.
            vault.SetQuickAccess(small, true);
            Assert.Equal(new[] { jewels, small }, vault.QuickAccess());

            vault.SetQuickAccess(jewels, false);
            Assert.Equal(new[] { small }, vault.QuickAccess());

            // Taken off where it was not on: nothing.
            vault.SetQuickAccess(charms, false);
            Assert.Equal(new[] { small }, vault.QuickAccess());
            vault.SetQuickAccess(charms, true);
        }

        // Kept in the file.
        SqliteConnection.ClearAllPools();
        using var again = Open();
        Assert.Equal(new[] { small, charms }, again.QuickAccess());
    }

    [Fact]
    public void A_folder_that_is_no_more_is_no_longer_listed_and_one_that_never_was_cannot_be_pinned()
    {
        using var vault = Open();
        long charms = vault.CreateFolder(null, "Charms");
        long jewels = vault.CreateFolder(null, "Jewels");
        vault.SetQuickAccess(charms, true);
        vault.SetQuickAccess(jewels, true);

        vault.DeleteFolder(charms, everything: true);
        Assert.Equal(new[] { jewels }, vault.QuickAccess());

        Assert.ThrowsAny<Exception>(() => vault.SetQuickAccess(987654, true));
        Assert.Equal(new[] { jewels }, vault.QuickAccess());
    }
}
