using System.Text;
using PD2SPCompanion.Core.GameFiles;
using PD2SPCompanion.Core.Testbed;

namespace PD2SPCompanion.Tests.Testbed;

/// <summary>
/// The one path that writes into a game folder: what it installs it can always take out,
/// and what it did not put there it never touches. On a folder made for the test, never the game's.
/// </summary>
public sealed class DensityInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pd2spc-installer-" + Guid.NewGuid().ToString("N"));

    public DensityInstallerTests() => Directory.CreateDirectory(Path.Combine(_root, "ProjectD2"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static readonly GameBuild Build = new("test", "0");

    private static ModdedFile Table(string name) => new(name, Encoding.UTF8.GetBytes($"{name}\r\n"));

    private string TablePath(string name, string extension = ".txt") =>
        Path.Combine(DensityInstaller.FolderIn(_root), name + extension);

    [Fact]
    public void What_it_installs_it_takes_out_and_the_marker_is_kept_relative()
    {
        Assert.Null(DensityInstaller.Install(_root, 2, false, Build, [Table("levels"), Table("monstats")]));

        Assert.True(File.Exists(TablePath("levels")));
        Assert.Empty(Directory.GetFiles(DensityInstaller.FolderIn(_root), "*.pd2spc"));
        Assert.DoesNotContain(_root, File.ReadAllText(DensityInstaller.MarkerIn(_root)).Replace(@"\\", @"\"));
        Assert.Contains(TablePath("levels"), DensityInstaller.Current(_root).Files);

        // The game's compiled form beside it goes too.
        File.WriteAllText(TablePath("levels", ".bin"), "compiled");
        Assert.Null(DensityInstaller.Remove(_root));
        Assert.False(File.Exists(TablePath("levels")));
        Assert.False(File.Exists(TablePath("levels", ".bin")));
        Assert.False(File.Exists(DensityInstaller.MarkerIn(_root)));
        Assert.False(DensityInstaller.Current(_root).Installed);
    }

    [Fact]
    public void A_table_it_did_not_put_there_stops_it_and_stays()
    {
        Directory.CreateDirectory(DensityInstaller.FolderIn(_root));
        File.WriteAllText(TablePath("levels"), "someone else's");

        Assert.Equal($"{TablePath("levels")} exists, not made here.", DensityInstaller.Blocked(_root, ["levels"]));
        Assert.NotNull(DensityInstaller.Install(_root, 2, false, Build, [Table("levels")]));
        Assert.False(File.Exists(DensityInstaller.MarkerIn(_root)));
        Assert.Equal("someone else's", File.ReadAllText(TablePath("levels")));

        // Its own tables do not block it a second time.
        File.Delete(TablePath("levels"));
        Assert.Null(DensityInstaller.Install(_root, 2, false, Build, [Table("levels")]));
        Assert.Null(DensityInstaller.Blocked(_root, ["levels", "monstats"]));
    }

    /// <summary>
    /// "treasureclassex.bin exists, not made here." on Apply: the game, started with
    /// the modded shortcut, compiles every table into the folder, and Remove leaves the ones it did not list.
    /// A compiled table with no table beside it is the game's own, and stops nothing; it is not written here,
    /// and goes when the table beside it is removed.
    /// </summary>
    [Fact]
    public void The_games_own_compiled_table_does_not_stop_it_and_goes_with_the_table_on_Remove()
    {
        Directory.CreateDirectory(DensityInstaller.FolderIn(_root));
        File.WriteAllText(TablePath("treasureclassex", ".bin"), "compiled by the game");
        File.WriteAllText(TablePath("armor", ".bin"), "compiled by the game");

        Assert.Null(DensityInstaller.Blocked(_root, ["treasureclassex"]));
        Assert.Null(DensityInstaller.Install(_root, 2, false, Build, [Table("treasureclassex")]));
        Assert.True(File.Exists(TablePath("treasureclassex")));

        // Not opened for writing: the game compiles over it when it next starts.
        Assert.Equal("compiled by the game", File.ReadAllText(TablePath("treasureclassex", ".bin")));

        // Removed with its table; a compiled table of one never listed stays.
        Assert.Null(DensityInstaller.Remove(_root));
        Assert.False(File.Exists(TablePath("treasureclassex")));
        Assert.False(File.Exists(TablePath("treasureclassex", ".bin")));
        Assert.True(File.Exists(TablePath("armor", ".bin")));
    }

    [Fact]
    public void A_marker_written_with_whole_paths_still_reads()
    {
        Assert.Null(DensityInstaller.Install(_root, 2, false, Build, [Table("levels")]));
        string marker = DensityInstaller.MarkerIn(_root);
        string relative = Path.GetRelativePath(_root, TablePath("levels"));
        File.WriteAllText(marker, File.ReadAllText(marker).Replace(
            System.Text.Json.JsonSerializer.Serialize(relative),
            System.Text.Json.JsonSerializer.Serialize(TablePath("levels"))));

        Assert.Contains(TablePath("levels"), DensityInstaller.Current(_root).Files);
        Assert.Null(DensityInstaller.Remove(_root));
        Assert.False(File.Exists(TablePath("levels")));
    }
}
