using System.Runtime.CompilerServices;

namespace PD2SPCompanion.Testing;

/// <summary>
/// Where this machine keeps what the tests stand on, for both test projects (linked into each): the same three as
/// <c>tests/paths.py</c>, by the same rule - the environment variable, else the nearest <c>pd2spc.local.env</c> from
/// the repo's folder up, else not known (an empty string). <c>PD2SPC_NO_GAME=1</c> says none is to be used, whatever
/// the machine has: what a clone sees (<c>check.py --no-game</c>).
/// </summary>
public static class TestPaths
{
    public const string LocalFile = "pd2spc.local.env";

    private static readonly Lazy<Dictionary<string, string>> Local = new(ReadLocal);

    public static bool NoGameMeant => Environment.GetEnvironmentVariable("PD2SPC_NO_GAME") == "1";

    /// <summary>The Diablo II folder PD2 is installed into (<c>PD2SPC_GAME</c>).</summary>
    public static string Game { get; } = Where("PD2SPC_GAME");

    /// <summary>The corpus of real saves, TestCorpus (<c>PD2SPC_CORPUS</c>).</summary>
    public static string Corpus { get; } = Where("PD2SPC_CORPUS");

    /// <summary>Season 13's tables, the app's snapshot (<c>PD2SPC_S13_TABLES</c>).</summary>
    public static string S13Tables { get; } = Where("PD2SPC_S13_TABLES");

    /// <summary>Whether <see cref="Game"/> is a game, by the test the program itself asks.</summary>
    public static bool GameThere { get; } = PD2SPCompanion.Core.GameFiles.GameLocation.Holds(Game);

    public static bool CorpusThere { get; } = Directory.Exists(Corpus);

    private static string Where(string name)
    {
        if (NoGameMeant)
            return "";

        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } set ? set : Local.Value.GetValueOrDefault(name, "");
    }

    private static Dictionary<string, string> ReadLocal()
    {
        var found = new Dictionary<string, string>();
        for (var folder = new DirectoryInfo(Here()); folder is not null; folder = folder.Parent)
        {
            string file = Path.Combine(folder.FullName, LocalFile);
            if (!File.Exists(file))
                continue;

            foreach (string line in File.ReadAllLines(file))
            {
                int equals = line.IndexOf('=');
                if (equals > 0 && !line.TrimStart().StartsWith('#'))
                    found[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
            break;
        }

        return found;
    }

    /// <summary>This file's folder, where it was compiled: the tests of a checkout run from under that checkout.</summary>
    private static string Here([CallerFilePath] string file = "") => Path.GetDirectoryName(file) ?? "";
}

/// <summary>A test that reads the installed game's tables. Skipped, and counted as skipped, where there is no game.</summary>
public sealed class GameFactAttribute : FactAttribute
{
    public GameFactAttribute()
    {
        if (!TestPaths.GameThere)
            Skip = NeedsWhat.Game;
    }
}

/// <inheritdoc cref="GameFactAttribute"/>
public sealed class GameTheoryAttribute : TheoryAttribute
{
    public GameTheoryAttribute()
    {
        if (!TestPaths.GameThere)
            Skip = NeedsWhat.Game;
    }
}

/// <summary>A test that reads the corpus's saves, under the game's tables. Skipped where either is missing.</summary>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (!TestPaths.GameThere || !TestPaths.CorpusThere)
            Skip = NeedsWhat.Corpus;
    }
}

/// <inheritdoc cref="CorpusFactAttribute"/>
public sealed class CorpusTheoryAttribute : TheoryAttribute
{
    public CorpusTheoryAttribute()
    {
        if (!TestPaths.GameThere || !TestPaths.CorpusThere)
            Skip = NeedsWhat.Corpus;
    }
}

internal static class NeedsWhat
{
    public const string Game = "Needs the game: PD2SPC_GAME, or " + TestPaths.LocalFile + ".";

    public const string Corpus = "Needs the game and the corpus: PD2SPC_GAME and PD2SPC_CORPUS, or " + TestPaths.LocalFile + ".";
}
