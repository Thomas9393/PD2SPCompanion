using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PD2SPCompanion.Shell.Tests;

/// <summary>
/// What the rest of the suite stands on. Without the game or the corpus every test on the program is skipped
/// (<see cref="CorpusFactAttribute"/>); with the corpus but no pad each returns at <see cref="Program.Ready"/> and counts as
/// passed, and a test that looks for one file of the corpus returns without it: here, one
/// test fails and says what is missing. <c>PD2SPC_NO_GAME=1</c>, which <c>check.py --no-game</c> sets, says this is meant.
/// </summary>
[Collection(WithTheProgram.Name)]
public sealed class TestKitTests(Program program)
{
    private static bool NoGameMeant => TestPaths.NoGameMeant;

    [Fact]
    public void The_program_could_be_built()
    {
        if (NoGameMeant)
            return;

        Assert.True(program.Available,
            $"No program: the game at '{Program.GameRoot}' or the pad under '{Program.Corpus}' is missing. PD2SPC_GAME and PD2SPC_CORPUS, or {TestPaths.LocalFile}; `python tests/check.py quick --no-game` for what runs without them.");
    }

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

            foreach (Match named in Regex.Matches(File.ReadAllText(file), """Path\.Combine\(Program\.Corpus((?:,\s*"[^"\n]+")+)\)"""))
            {
                var parts = Regex.Matches(named.Groups[1].Value, "\"([^\"]+)\"").Select(part => part.Groups[1].Value);
                string specimen = Path.Combine([Program.Corpus, .. parts]);
                if (!File.Exists(specimen) && !Directory.Exists(specimen))
                    missing.Add($"{specimen} ({Path.GetFileName(file)})");
            }
        }

        Assert.True(missing.Count == 0, "Named by a test, not in the corpus:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    private static string OwnFolder([CallerFilePath] string file = "") => Path.GetDirectoryName(file) ?? "";
}
