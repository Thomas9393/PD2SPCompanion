using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PD2SPCompanion.Tests;

/// <summary>
/// What the rest of the suite stands on. Without the game or the corpus most tests are skipped (<see cref="GameFactAttribute"/>)
/// and the rest return at their first line and count as passed: here, one test fails and
/// says what is missing. <c>PD2SPC_NO_GAME=1</c>, which <c>check.py --no-game</c> sets, says this is meant.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class TestKitTests(Game game)
{
    private static bool NoGameMeant => TestPaths.NoGameMeant;

    private const string Set = $"PD2SPC_GAME and PD2SPC_CORPUS, or {TestPaths.LocalFile}; `python tests/check.py quick --no-game` for what runs without them";

    [Fact]
    public void The_game_and_the_corpus_are_there()
    {
        if (NoGameMeant)
            return;

        Assert.True(game.Available, $"No game at '{Game.Root}': the tests that need it are skipped. {Set}.");
        Assert.True(Directory.Exists(Game.Corpus), $"No corpus at '{Game.Corpus}': the tests that need it are skipped. {Set}.");
    }

    /// <summary>A specimen gone or renamed retires every test that looks for it, with no sign: each one a test names must be there.</summary>
    [Fact]
    public void Every_specimen_a_test_names_is_there()
    {
        if (NoGameMeant)
            return;

        string folder = OwnFolder();
        if (!Directory.Exists(folder))
            return;

        var missing = new SortedSet<string>();
        foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            foreach (Match named in Regex.Matches(File.ReadAllText(file), """Path\.Combine\(Game\.Corpus((?:,\s*"[^"\n]+")+)\)"""))
            {
                var parts = Regex.Matches(named.Groups[1].Value, "\"([^\"]+)\"").Select(part => part.Groups[1].Value);
                string specimen = Path.Combine([Game.Corpus, .. parts]);
                if (!File.Exists(specimen) && !Directory.Exists(specimen))
                    missing.Add($"{specimen} ({Path.GetFileName(file)})");
            }
        }

        Assert.True(missing.Count == 0, "Named by a test, not in the corpus:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// The pinned pass runs on the season it names. A tables folder missing is taken quietly, the tables stay the
    /// installed ones, and every test of the pass returns at its Pinned. <c>check.py</c> says what it expects.
    /// </summary>
    [Fact]
    [Trait("Season", "S13")]
    public void A_pinned_pass_runs_on_the_season_it_asked_for()
    {
        if (Environment.GetEnvironmentVariable("PD2SPC_EXPECT_SEASON") is not { Length: > 0 } expected)
            return;

        Assert.True(game.Available, $"No game at '{Game.Root}' (PD2SPC_GAME, or {TestPaths.LocalFile}).");
        Assert.Equal(expected, Game.Season);
    }

    private static string OwnFolder([CallerFilePath] string file = "") => Path.GetDirectoryName(file) ?? "";
}
