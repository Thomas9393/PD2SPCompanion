namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// What the editor does to the player's save folder on its own: the write that swaps a file in, and the two tidyings at
/// start-up, which may take only what is the editor's (<see cref="SaveWriter.ClearPending"/>,
/// <see cref="SaveWriter.MigrateLooseBackups"/>). The save folder holds other tools' files. On a scratch folder; no tables.
/// </summary>
public sealed class SaveFolderTidyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-tidy-" + Guid.NewGuid().ToString("N"));

    public SaveFolderTidyTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private string Made(string name, params byte[] bytes)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string[] Names() =>
        [.. Directory.GetFiles(_folder).Select(path => Path.GetFileName(path)).Order(StringComparer.OrdinalIgnoreCase)];

    [Fact]
    public void A_write_swaps_in_the_new_bytes_and_leaves_nothing_beside_them()
    {
        string path = Made("safe.d2s", 1, 2, 3, 4);

        SaveWriter.WriteSafely(path, [5, 6, 7, 8, 9]);

        Assert.Equal([5, 6, 7, 8, 9], File.ReadAllBytes(path));
        Assert.Equal(["safe.d2s"], Names());
    }

    [Fact]
    public void A_write_to_a_new_name_makes_the_file()
    {
        string path = Path.Combine(_folder, "fresh.d2s");

        SaveWriter.WriteSafely(path, [1, 2]);

        Assert.Equal([1, 2], File.ReadAllBytes(path));
        Assert.Equal(["fresh.d2s"], Names());
    }

    [Fact]
    public void A_file_held_by_another_program_refuses_the_write_and_stays_as_it_was()
    {
        string path = Made("held.d2s", 1, 2, 3, 4);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => SaveWriter.WriteSafely(path, [9, 9]));

        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(path));
        Assert.Equal(["held.d2s"], Names());
    }

    [Fact]
    public void A_file_let_go_while_the_write_waits_is_written()
    {
        string path = Made("brief.d2s", 1, 2, 3, 4);

        // Held a moment, as a virus scanner holds what was just written: the waits are 20, 60, 150 and 400 ms.
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        using var letGo = new Timer(_ => held.Dispose(), null, 50, Timeout.Infinite);

        SaveWriter.WriteSafely(path, [7, 7]);

        Assert.Equal([7, 7], File.ReadAllBytes(path));
        Assert.Equal(["brief.d2s"], Names());
    }

    [Fact]
    public void A_file_denied_a_moment_is_asked_again_and_written()
    {
        string path = Made("denied.d2s", 1, 2, 3, 4);

        // Access denied, which is no IOException: a read-only file throws it where a scanner's hold can.
        File.SetAttributes(path, FileAttributes.ReadOnly);
        using var letGo = new Timer(_ => File.SetAttributes(path, FileAttributes.Normal), null, 50, Timeout.Infinite);

        SaveWriter.WriteSafely(path, [7, 7]);

        Assert.Equal([7, 7], File.ReadAllBytes(path));
        Assert.Equal(["denied.d2s"], Names());
    }

    [Fact]
    public void Clearing_pending_takes_a_crashs_leftovers_and_nothing_else()
    {
        Made("safe.d2s", 1);
        Made("safe.d2s" + SaveWriter.PendingSuffix, 2);
        Made("pd2_shared.stash" + SaveWriter.PendingSuffix, 3);
        Made("keep.d2s.bak", 4);
        Made("keep.pd2spc-new.bak", 5);
        Made("keep.pd2spc-newer", 6);

        Assert.Equal(2, SaveWriter.ClearPending(_folder));

        Assert.Equal(["keep.d2s.bak", "keep.pd2spc-new.bak", "keep.pd2spc-newer", "safe.d2s"], Names());
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(_folder, "safe.d2s")));
    }

    [Fact]
    public void Clearing_pending_leaves_one_still_held_and_says_only_what_went()
    {
        string held = Made("held.d2s" + SaveWriter.PendingSuffix, 1);
        Made("gone.d2s" + SaveWriter.PendingSuffix, 2);

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(1, SaveWriter.ClearPending(_folder));

        Assert.Equal(["held.d2s" + SaveWriter.PendingSuffix], Names());
    }

    [Fact]
    public void Clearing_a_folder_not_there_clears_nothing()
    {
        Assert.Equal(0, SaveWriter.ClearPending(Path.Combine(_folder, "not-there")));
    }

    [Fact]
    public void Loose_backups_move_to_their_characters_folder_and_other_tools_files_stay()
    {
        string ours = "Zon.d2s.20260928-211405.bak", second = "Zon.d2s.20260928-211405-2.bak", another = "Other.D2S.20260101-000000.BAK";
        Made(ours, 1);
        Made(second, 2);
        Made(another, 3);

        // PlugY's, a save's own, and names that are nearly ours.
        string[] foreign = ["PlugY.d2s.bak", "Zon.d2s.2026-09-28.bak", "Zon.d2s.20260928-211405.old", "pd2_shared.stash.20260928-211405.bak", "20260928-211405.bak", "Zon.d2s"];
        foreach (string name in foreign)
            Made(name, 9);

        Assert.Equal(3, SaveWriter.MigrateLooseBackups(_folder));

        Assert.Equal([.. foreign.Order(StringComparer.OrdinalIgnoreCase)], Names());
        foreach (string name in foreign)
            Assert.Equal([9], File.ReadAllBytes(Path.Combine(_folder, name)));

        string zon = SaveWriter.BackupFolderFor(Path.Combine(_folder, "Zon.d2s"));
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(zon, ours)));
        Assert.Equal([2], File.ReadAllBytes(Path.Combine(zon, second)));
        Assert.Equal([3], File.ReadAllBytes(Path.Combine(SaveWriter.BackupFolderFor(Path.Combine(_folder, "Other.D2S")), another)));

        // And they are listed as the character's own.
        Assert.Equal(2, SaveWriter.BackupsOf(Path.Combine(_folder, "Zon.d2s")).Count);
    }

    [Fact]
    public void A_loose_backup_whose_place_is_taken_is_left_and_nothing_overwritten()
    {
        string name = "Zon.d2s.20260928-211405.bak";
        Made(name, 1);

        string zon = SaveWriter.BackupFolderFor(Path.Combine(_folder, "Zon.d2s"));
        Directory.CreateDirectory(zon);
        File.WriteAllBytes(Path.Combine(zon, name), [2]);

        Assert.Equal(0, SaveWriter.MigrateLooseBackups(_folder));

        Assert.Equal([1], File.ReadAllBytes(Path.Combine(_folder, name)));
        Assert.Equal([2], File.ReadAllBytes(Path.Combine(zon, name)));
    }

    [Fact]
    public void A_loose_backup_held_by_another_program_is_left_where_it_lies()
    {
        string name = "Zon.d2s.20260928-211405.bak";
        string path = Made(name, 1);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(0, SaveWriter.MigrateLooseBackups(_folder));

        Assert.Equal([1], File.ReadAllBytes(path));
    }

    [Fact]
    public void Moving_backups_of_a_folder_not_there_moves_nothing()
    {
        Assert.Equal(0, SaveWriter.MigrateLooseBackups(Path.Combine(_folder, "not-there")));
    }
}
