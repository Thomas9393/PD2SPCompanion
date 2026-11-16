namespace PD2SPCompanion.Tests.GameFiles;

/// <summary>
/// The game folder picked as PD2's own - ProjectD2 - stands for the Diablo II folder around it (
/// the question says "Project Diablo II game folder", which sends people there).
/// </summary>
public sealed class GameLocationResolveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pd2spc-location-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A Diablo II folder with PD2 in it, as far as the check looks: the two archives.</summary>
    private string Install(string name, bool withPd2 = true)
    {
        string game = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(game, "ProjectD2"));
        File.WriteAllText(Path.Combine(game, "d2data.mpq"), "");
        if (withPd2)
            File.WriteAllText(Path.Combine(game, "ProjectD2", "pd2data.mpq"), "");
        return game;
    }

    [Fact]
    public void ProjectD2_goes_up_to_its_game_folder()
    {
        string game = Install("Diablo II");

        Assert.Equal(game, GameLocation.Resolve(Path.Combine(game, "ProjectD2")));
        Assert.Equal(game, GameLocation.Resolve(Path.Combine(game, "ProjectD2") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void The_game_folder_itself_stays()
    {
        string game = Install("Diablo II");

        Assert.Equal(game, GameLocation.Resolve(game));
        Assert.Equal(game, GameLocation.Resolve(game + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Anything_else_is_left_for_the_check_to_refuse()
    {
        string other = Install("Diablo II Median", withPd2: false);
        string loose = Path.Combine(_root, "Loose");
        Directory.CreateDirectory(loose);
        File.WriteAllText(Path.Combine(loose, "pd2data.mpq"), "");   // a pd2data.mpq with no game around it

        Assert.Equal(other, GameLocation.Resolve(other));
        Assert.Equal(loose, GameLocation.Resolve(loose));
        Assert.Equal("", GameLocation.Resolve("   "));
    }
}
