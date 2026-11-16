using System;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;

using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The undo log's mark never goes back.
/// The session's history lets go of what it no longer reaches (<see cref="VaultUndo.Forget"/>) - after a
/// save that wrote the vault, that is the whole log. The mark then read 0 again and the next changes were
/// numbered from 1, under the mark the history had kept: a vault edit made then was no step, and Undo took
/// back the step before it.
/// </summary>
public sealed class VaultUndoMarkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-vault-undo-" + Guid.NewGuid().ToString("N"));

    public VaultUndoMarkTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void The_mark_stays_where_it_was_when_the_whole_log_is_let_go_of()
    {
        using var vault = Vault.Open(Path.Combine(_folder, "mark.pd2lib"), "S13", 10);
        var log = vault.StartUndo(() => { });

        vault.CreateFolder(null, "Charms");
        long mark = log.Mark;
        Assert.True(mark > 0, "the folder made was not logged");

        log.Forget(mark);

        Assert.Equal(mark, log.Mark);
    }

    [Fact]
    public void A_change_after_the_log_was_let_go_of_is_above_the_mark_and_taken_back_alone()
    {
        using var vault = Vault.Open(Path.Combine(_folder, "after.pd2lib"), "S13", 10);
        var log = vault.StartUndo(() => { });

        long made = vault.CreateFolder(null, "Charms");
        long mark = log.Mark;
        log.Forget(mark);

        vault.RenameFolder(made, "Small charms");
        Assert.True(log.Mark > mark, $"the rename is at {log.Mark}, not above the mark kept, {mark}");

        // The range above the mark is the rename and nothing else: the folder is there still, under its first name.
        log.Apply(mark, log.Mark);
        Assert.Equal("Charms", vault.Folders().Single(folder => folder.Id == made).Name);
    }
}
