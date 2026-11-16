namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// Backups are listed newest first by when they were taken - the moment in the name - not by the
/// file's write time, which a copy takes from the save it copied (else the picker is
/// out of order, and thinning keeps the first as the newest).
/// </summary>
public sealed class BackupOrderTests : IDisposable
{
    private readonly string _saves = Path.Combine(Path.GetTempPath(), "pd2spc-backups-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_saves, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Newest_first_by_the_moment_in_the_name()
    {
        string save = Path.Combine(_saves, "Tester.d2s");
        string folder = SaveWriter.BackupFolderFor(save);
        Directory.CreateDirectory(folder);

        // Taken in this order, but written the other way round, as copies of older saves are.
        string[] taken = ["Tester.d2s.20260929-100000.bak", "Tester.d2s.20260930-090000.bak", "Tester.d2s.20260930-120000.bak"];
        for (int n = 0; n < taken.Length; n++)
        {
            string file = Path.Combine(folder, taken[n]);
            File.WriteAllText(file, "x");
            File.SetLastWriteTimeUtc(file, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-n));
        }

        var listed = SaveWriter.BackupsOf(save).Select(file => file.Name).ToList();

        Assert.Equal(taken.Reverse(), listed);
    }

    /// <summary>
    /// Two saves inside one second: the second backup is "-2.bak", and it is the newer. Listed by name descending, the
    /// plain ".bak" sorted after "-2.bak" and the older came first - a restore of the newest opened the one before it.
    /// </summary>
    [Fact]
    public void Within_one_second_the_later_backup_comes_first()
    {
        string save = Path.Combine(_saves, "Tester.d2s");
        string folder = SaveWriter.BackupFolderFor(save);
        Directory.CreateDirectory(folder);

        string[] taken = ["Tester.d2s.20261103-132912.bak", "Tester.d2s.20261103-132912-2.bak", "Tester.d2s.20261103-132912-3.bak",
                          "Tester.d2s.20261103-132913.bak"];
        foreach (string name in taken)
            File.WriteAllText(Path.Combine(folder, name), "x");

        Assert.Equal(taken.Reverse(), SaveWriter.BackupsOf(save).Select(file => file.Name));
    }
}
