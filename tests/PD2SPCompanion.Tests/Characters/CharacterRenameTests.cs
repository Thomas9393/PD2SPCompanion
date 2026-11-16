using PD2SPCompanion.Core.Characters;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// A rename moves a save and every file named after its character, and puts all of it back if a step fails
/// (<see cref="CharacterRename.Rename"/>). On a scratch copy of a corpus character,
/// a file held open standing in for one another program holds.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CharacterRenameTests(Game game) : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "pd2spc-rename-" + Guid.NewGuid().ToString("N"));

    private static readonly string[] Companions = [".key", ".ma0", ".ma1", ".ma2", ".ma3", ".map"];

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { }
    }

    /// <summary>A copy of the character with a file of each kind beside it, each holding its own bytes.</summary>
    private string Character()
    {
        string save = Game.CopyOfCharacter(_scratch);
        string stem = Path.ChangeExtension(save, null);
        foreach (var (extension, at) in Companions.Select((extension, at) => (extension, at)))
            File.WriteAllBytes(stem + extension, [(byte)at, 1, 2]);
        return save;
    }

    /// <summary>Every file directly in the folder, the case of its name as it is on disk, with its bytes.</summary>
    private Dictionary<string, byte[]> Files() =>
        Directory.GetFiles(_scratch).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);

    private static void AssertSame(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after)
    {
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, bytes) in before)
            Assert.True(bytes.AsSpan().SequenceEqual(after[name]), $"{name} changed");
    }

    private static string Items(string save) =>
        string.Join("|", SaveVerifier.Load(save).Save.PlayerItemList.Items.Select(item => $"{item.Code}:{item.Id}"));

    [CorpusFact]
    public void A_rename_moves_the_save_and_every_file_beside_it()
    {
        if (!game.Available)
            return;

        string save = Character();
        string items = Items(save);
        var companions = Files();

        string renamed = CharacterRename.Rename(save, "NewName");

        Assert.Equal(Path.Combine(_scratch, "NewName.d2s"), renamed);
        Assert.Equal(new[] { "NewName.d2s", "NewName.key", "NewName.ma0", "NewName.ma1", "NewName.ma2", "NewName.ma3", "NewName.map" }.Order(StringComparer.Ordinal),
                     Files().Keys.Order(StringComparer.Ordinal));
        foreach (string extension in Companions)
            Assert.Equal(companions["PhySpearZon" + extension], File.ReadAllBytes(Path.Combine(_scratch, "NewName" + extension)));

        var (read, result) = SaveVerifier.Load(renamed);
        Assert.True(result.RoundTripped);
        Assert.Equal("NewName", read.Name);
        Assert.Equal(items, Items(renamed));
    }

    [CorpusFact]
    public void Other_files_are_not_moved()
    {
        if (!game.Available)
            return;

        string save = Character();
        File.WriteAllBytes(Path.Combine(_scratch, "PhySpearZonOther.key"), [9]);
        File.WriteAllBytes(Path.Combine(_scratch, "PhySpearZon.d2s.20260101-000000.bak"), [9]);
        File.WriteAllBytes(Path.Combine(_scratch, "pd2_shared.stash"), [9]);

        CharacterRename.Rename(save, "NewName");

        Assert.True(File.Exists(Path.Combine(_scratch, "PhySpearZonOther.key")));
        Assert.True(File.Exists(Path.Combine(_scratch, "PhySpearZon.d2s.20260101-000000.bak")));
        Assert.True(File.Exists(Path.Combine(_scratch, "pd2_shared.stash")));
        Assert.False(File.Exists(Path.Combine(_scratch, "NewName.d2s.20260101-000000.bak")));
    }

    [CorpusFact]
    public void A_rename_takes_the_characters_backups_with_it_under_the_new_name()
    {
        if (!game.Available)
            return;

        string save = Character();
        string backup = SaveWriter.Backup(save);
        SaveWriter.KeepAsWritten(save, [1, 2, 3]);

        string renamed = CharacterRename.Rename(save, "NewName");

        Assert.False(File.Exists(backup));
        Assert.False(Directory.Exists(SaveWriter.BackupFolderFor(save)));

        // The one taken before, and the one the rename takes of the old file.
        var moved = SaveWriter.BackupsOf(renamed);
        Assert.Equal(2, moved.Count);
        Assert.All(moved, file => Assert.StartsWith("NewName.d2s.", file.Name, StringComparison.Ordinal));

        // The rolling copy was the old name's: it goes.
        Assert.False(File.Exists(SaveWriter.AsWrittenPath(save, SaveWriter.BackupFolderFor(renamed))));
    }

    [CorpusFact]
    public void A_rename_changing_only_the_case_keeps_everything_under_the_new_case()
    {
        if (!game.Available)
            return;

        string save = Character();
        string items = Items(save);
        byte[] was = File.ReadAllBytes(save);
        var companions = Files();

        string renamed = CharacterRename.Rename(save, "PHYSPEARZON");

        // The case itself, as the folder lists it: a plain move on Windows would skip it.
        Assert.Equal(new[] { "PHYSPEARZON.d2s", "PHYSPEARZON.key", "PHYSPEARZON.ma0", "PHYSPEARZON.ma1", "PHYSPEARZON.ma2", "PHYSPEARZON.ma3", "PHYSPEARZON.map" }.Order(StringComparer.Ordinal),
                     Files().Keys.Order(StringComparer.Ordinal));
        foreach (string extension in Companions)
            Assert.Equal(companions["PhySpearZon" + extension], File.ReadAllBytes(Path.Combine(_scratch, "PHYSPEARZON" + extension)));

        // Same length of bytes, the character's as it was but for its name and the checksum.
        var (read, result) = SaveVerifier.Load(renamed);
        Assert.True(result.RoundTripped);
        Assert.Equal("PHYSPEARZON", read.Name);
        Assert.Equal(was.Length, File.ReadAllBytes(renamed).Length);
        Assert.Equal(items, Items(renamed));

        // The backups' folder too, with the file as it was in it.
        var backup = Assert.Single(SaveWriter.BackupsOf(renamed));
        Assert.StartsWith("PHYSPEARZON.d2s.", backup.Name, StringComparison.Ordinal);
        Assert.Equal(was, File.ReadAllBytes(backup.FullName));
    }

    [CorpusFact]
    public void A_companion_held_by_another_program_puts_every_file_back()
    {
        if (!game.Available)
            return;

        string save = Character();
        var before = Files();

        // The last moved, so that the others have gone before it fails, and have to come back.
        using (new FileStream(Path.ChangeExtension(save, ".map"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => CharacterRename.Rename(save, "NewName"));

        AssertSame(before, Files());
    }

    [CorpusFact]
    public void A_save_that_cannot_be_deleted_puts_every_file_back()
    {
        if (!game.Available)
            return;

        string save = Character();
        var before = Files();

        // Readable - the rename reads it and copies it - but not deletable, its very last step.
        using (new FileStream(save, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<Exception>(() => CharacterRename.Rename(save, "NewName"));

        AssertSame(before, Files());
    }

    [CorpusFact]
    public void A_held_companion_on_a_rename_of_case_puts_every_file_back_in_its_case()
    {
        if (!game.Available)
            return;

        string save = Character();
        var before = Files();

        using (new FileStream(Path.ChangeExtension(save, ".map"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => CharacterRename.Rename(save, "PHYSPEARZON"));

        // The stand-in gone, the old name's case back on the save, its bytes the old ones.
        AssertSame(before, Files());
    }

    [CorpusTheory]
    [InlineData("Taken.d2s")]
    [InlineData("Taken.key")]
    public void A_name_taken_by_any_of_its_files_is_refused_with_nothing_moved(string taken)
    {
        if (!game.Available)
            return;

        string save = Character();
        File.WriteAllBytes(Path.Combine(_scratch, taken), [1]);
        var before = Files();

        var refused = Assert.Throws<InvalidOperationException>(() => CharacterRename.Rename(save, "Taken"));

        Assert.Contains("already exists", refused.Message);
        AssertSame(before, Files());
    }

    [CorpusTheory]
    [InlineData("PhySpearZon")]
    [InlineData("1")]
    [InlineData("Bad Name")]
    public void The_same_name_or_one_the_game_would_not_take_is_refused_with_nothing_moved(string name)
    {
        if (!game.Available)
            return;

        string save = Character();
        var before = Files();

        Assert.Throws<InvalidOperationException>(() => CharacterRename.Rename(save, name));

        AssertSame(before, Files());
        Assert.False(Directory.Exists(SaveWriter.BackupFolderFor(save)));
    }
}
