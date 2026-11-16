using Microsoft.Data.Sqlite;

using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// A vault's copies go to a folder of its season and mode: softcore's and hardcore's first
/// vaults are both "S14 Main", and the copies of one thinned the other's and stood for its newest. On real vaults, in a
/// folder of this test's own. What the count does in a folder is <see cref="VaultBackupTests"/>'.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class VaultCopyFoldersTests(Game game) : IDisposable
{
    private const string Season = "S14";

    private readonly string _vaults = Path.Combine(Path.GetTempPath(), "pd2spc-copy-folders-" + Guid.NewGuid().ToString("N"));
    private readonly string? _vaultsBefore = Environment.GetEnvironmentVariable("PD2SPC_VAULTS");
    private readonly int? _keepBefore = Vault.CopiesToKeep;

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaultsBefore);
        Vault.CopiesToKeep = _keepBefore;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_vaults, recursive: true); } catch (IOException) { }
    }

    private Vault Opened(bool hardcore)
    {
        Directory.CreateDirectory(_vaults);
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", _vaults);
        string path = VaultLibrary.VaultPath(Season, hardcore);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return Vault.Open(path, Season, 10);
    }

    private static string FolderOf(bool hardcore) =>
        Path.Combine(Vault.BackupFolder, Season, hardcore ? "Hardcore" : "Softcore");

    private static string OldCopy(string folder, string why, DateTime at)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"{VaultLibrary.MainName(Season)}.{why}.{at:yyyyMMdd-HHmmss}.pd2lib");
        File.WriteAllText(path, "copy");
        return path;
    }

    [CorpusFact]
    public void A_copy_goes_to_its_modes_folder_and_is_listed_and_a_file_outside_the_library_stays_at_the_top()
    {
        if (!game.Available)
            return;

        using var soft = Opened(false);
        using var hard = Opened(true);

        string fromSoft = soft.BackUp("cleared"), fromHard = hard.BackUp("cleared");

        Assert.Equal(FolderOf(false), Path.GetDirectoryName(fromSoft));
        Assert.Equal(FolderOf(true), Path.GetDirectoryName(fromHard));
        Assert.Equal(2, Vault.Copies().Count(copy => copy.Path == fromSoft || copy.Path == fromHard));

        Assert.Equal(Vault.BackupFolder, Vault.CopiesFolderFor(Path.Combine(_vaults, "format.pd2lib")));
        Assert.Equal(Vault.BackupFolder, Vault.CopiesFolderFor(Path.Combine(Path.GetTempPath(), "elsewhere.pd2lib")));
        Assert.Equal(Vault.BackupFolder, Vault.CopiesFolderFor(Path.Combine(Vault.BackupFolder, Season, "Softcore", "copy.pd2lib")));
    }

    [CorpusFact]
    public void A_softcore_copy_thins_its_own_and_not_hardcores_nor_those_from_before_the_folders()
    {
        if (!game.Available)
            return;

        using var soft = Opened(false);
        DateTime old = DateTime.Now.AddDays(-5);
        string own = OldCopy(FolderOf(false), "cleared", old);
        string other = OldCopy(FolderOf(true), "cleared", old);
        string before = OldCopy(Vault.BackupFolder, "cleared", old);

        Vault.CopiesToKeep = 0;
        soft.BackUp("cleared");

        Assert.False(File.Exists(own), "softcore's older copy was held to the count");
        Assert.True(File.Exists(other), "hardcore's copy went with a softcore copy");
        Assert.True(File.Exists(before), "a copy from before the folders went with the change");
    }

    [CorpusFact]
    public void Hardcores_newer_copy_does_not_stand_for_softcores()
    {
        if (!game.Available)
            return;

        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var item = save.PlayerItemList!.Items.First(each => !each.IsCompact);

        using var soft = Opened(false);
        Assert.NotNull(soft.Add(item, null, out _));

        // A copy of hardcore's name, and one from before the folders, both after softcore's file was last written.
        OldCopy(FolderOf(true), "session", DateTime.Now.AddMinutes(5));
        OldCopy(Vault.BackupFolder, "session", DateTime.Now.AddMinutes(5));

        string? made = soft.CopyAsFound();

        Assert.NotNull(made);
        Assert.Equal(FolderOf(false), Path.GetDirectoryName(made));

        // Softcore's own newest does answer: nothing newer than it, no second copy.
        OldCopy(FolderOf(false), "session", DateTime.Now.AddMinutes(5));
        Assert.Null(soft.CopyAsFound());
    }
}
