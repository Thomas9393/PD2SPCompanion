namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// The rolling copy of a file as it was last written (never loss, a duplicate allowed).
/// A backup is the file from BEFORE a save, so no
/// backup holds what the last save brought; this one copy does. It is offered only where the file is no
/// longer what was written, it is not a backup among the others, and thinning leaves it alone.
/// </summary>
public sealed class AsWrittenCopyTests : IDisposable
{
    private readonly string _saves = Path.Combine(Path.GetTempPath(), "pd2spc-last-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_saves, recursive: true); } catch (IOException) { }
    }

    private string Save(byte[] bytes)
    {
        Directory.CreateDirectory(_saves);
        string save = Path.Combine(_saves, "Tester.d2s");
        File.WriteAllBytes(save, bytes);
        return save;
    }

    [Fact]
    public void Kept_beside_the_backups_and_nothing_to_offer_while_the_file_is_what_was_written()
    {
        byte[] written = [1, 2, 3, 4];
        string save = Save(written);

        SaveWriter.KeepAsWritten(save, written);

        string copy = SaveWriter.AsWrittenPath(save);
        Assert.Equal(Path.Combine(SaveWriter.BackupFolderFor(save), "Tester.d2s.last"), copy);
        Assert.Equal(written, File.ReadAllBytes(copy));
        Assert.Null(SaveWriter.AsWritten(save));
    }

    [Fact]
    public void Offered_once_the_file_was_written_by_another_or_is_gone()
    {
        byte[] written = [1, 2, 3, 4];
        string save = Save(written);
        SaveWriter.KeepAsWritten(save, written);

        // The game saved over it: same length, other bytes.
        File.WriteAllBytes(save, [1, 2, 3, 5]);
        Assert.Equal(SaveWriter.AsWrittenPath(save), SaveWriter.AsWritten(save)?.FullName);

        // Cut short, as a failed write leaves it.
        File.WriteAllBytes(save, [1, 2]);
        Assert.NotNull(SaveWriter.AsWritten(save));

        File.Delete(save);
        Assert.NotNull(SaveWriter.AsWritten(save));

        // And never written by the companion: nothing.
        Assert.Null(SaveWriter.AsWritten(Path.Combine(_saves, "Other.d2s")));
    }

    [Fact]
    public void One_copy_replaced_at_each_write_not_listed_as_a_backup_and_not_thinned()
    {
        string save = Save([1]);
        string folder = SaveWriter.BackupFolderFor(save);
        Directory.CreateDirectory(folder);
        foreach (string name in new[] { "Tester.d2s.20260929-100000.bak", "Tester.d2s.20260930-090000.bak", "Tester.d2s.20260930-120000.bak" })
            File.WriteAllText(Path.Combine(folder, name), "x");

        SaveWriter.KeepAsWritten(save, [1]);
        SaveWriter.KeepAsWritten(save, [2, 2]);

        Assert.Equal([2, 2], File.ReadAllBytes(SaveWriter.AsWrittenPath(save)));
        Assert.Single(Directory.GetFiles(folder, "*" + SaveWriter.AsWrittenSuffix));
        Assert.DoesNotContain(SaveWriter.BackupsOf(save), file => file.Name.EndsWith(SaveWriter.AsWrittenSuffix));

        SaveWriter.Prune(save, 0);
        Assert.Empty(SaveWriter.BackupsOf(save));
        Assert.True(File.Exists(SaveWriter.AsWrittenPath(save)));
    }

    [Fact]
    public void A_folder_of_its_own_for_a_file_every_character_writes()
    {
        string stash = Path.Combine(_saves, "pd2_shared.stash");
        Directory.CreateDirectory(_saves);
        File.WriteAllBytes(stash, [7]);
        string root = SaveWriter.BackupRoot(_saves);

        SaveWriter.KeepAsWritten(stash, [7], root);

        Assert.True(File.Exists(Path.Combine(root, "pd2_shared.stash.last")));
        Assert.Null(SaveWriter.AsWritten(stash, root));
        File.WriteAllBytes(stash, [8]);
        Assert.NotNull(SaveWriter.AsWritten(stash, root));
    }
}
