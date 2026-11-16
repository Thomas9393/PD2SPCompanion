using System.Text;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Testbed;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Cli;

// The Experimental page's files for the game folder.
internal static partial class Cli
{
    // The two claims the density override rests on, asked before anything is installed:
    // that the table survives a parse and a serialize untouched, and that the arithmetic
    // agrees with the reference harness.
    static int Density(string root, double multiplier)
    {
        using var game = PD2SPCompanion.Core.GameFiles.GameData.Open(root);

        bool exact = PD2SPCompanion.Core.Testbed.DensityOverride.RoundTrips(game, "levels");
        Console.WriteLine($"levels.txt round-trips byte-identical: {exact}");

        if (!exact)
        {
            Console.Error.WriteLine("refusing to go further - the writer is not the reader's inverse");
            return 1;
        }

        var (file, report) = PD2SPCompanion.Core.Testbed.DensityOverride.Build(game, multiplier);
        Console.WriteLine($"x{multiplier}: {report}");
        Console.WriteLine($"generated {file.Bytes.Length} bytes");
        return 0;
    }

    /// <summary>
    /// Why a test recipe cannot be put in beside what is installed, or null. The test commands rebuild only the
    /// density table and cubemain: installed drop settings or packs - their tables not rebuilt - would be lost, and
    /// the marker would still say them. The cube table has to read back as the game's, as
    /// the Experimental page asks.
    /// </summary>
    static string? TestRefused(GameData game, DensityState state)
    {
        if (state.Installed && (state.Drops is { Any: true } || state.Packs > 1))
            return "Refused: drop settings or packs are installed. Remove them on the Experimental page first.";

        return DensityOverride.RoundTrips(game, "cubemain") ? null : "Refused: cubemain.txt does not round-trip.";
    }

    /// <summary>
    /// What is installed taken off for these files - asked first whether they can go in, so a refusal keeps it.
    /// </summary>
    static string? Replaced(string gameRoot, DensityState state, IReadOnlyList<ModdedFile> files)
    {
        if (DensityInstaller.Blocked(gameRoot, files.Select(file => file.Table)) is { } blocked)
            return blocked;

        return state.Installed ? DensityInstaller.Remove(gameRoot) : null;
    }

    /// <summary>
    /// Installs the desecration test recipes beside whatever the Experimental page has installed - its
    /// density and cow portal kept as the marker records them - or, with --remove, puts back just
    /// what the marker records. The Experimental page's own Apply or Remove also clears the test rows.
    /// </summary>
    static int DesecrationTestCommand(string gameRoot, bool remove)
    {
        using var game = GameData.Open(gameRoot);
        var state = DensityInstaller.Current(gameRoot);
        if (TestRefused(game, state) is { } no)
        {
            Console.WriteLine(no);
            return 1;
        }

        double multiplier = state.Installed ? state.Multiplier : 1;
        bool cowPortal = state.Installed && state.CowPortal;

        var files = new List<ModdedFile>();
        if (Math.Abs(multiplier - 1) > 0.001)
            files.Add(DensityOverride.Build(game, multiplier, state.Scope).File);

        var (cubemain, _) = game.ReadExcel("cubemain");
        if (cowPortal)
            CowPortalOverride.AppendTo(cubemain);
        if (!remove)
            DesecrationTest.AppendTo(cubemain);
        if (cowPortal || !remove)
            files.Add(new ModdedFile("cubemain", DensityOverride.Serialize(cubemain)));

        if (Replaced(gameRoot, state, files) is { } stuck)
        {
            Console.WriteLine(stuck);
            return 1;
        }

        string? refused = files.Count == 0 ? null
            : DensityInstaller.Install(gameRoot, multiplier, cowPortal, ModVersion.Of(gameRoot), files, state.Scope, state.Packs);
        Console.WriteLine(refused ?? (remove
            ? $"Test rows removed; kept density x{multiplier}, cow portal {cowPortal}."
            : $"Installed density x{multiplier}, cow portal {cowPortal}, and the desecration test: "
              + string.Join("; ", DesecrationTest.Rows.Select(row => $"amulet + {row.Name} -> desecrated {row.Value}"))));
        return refused is null ? 0 : 1;
    }

    /// <summary>
    /// Installs the dagger test recipe (DaggerTest) beside whatever the Experimental page has installed - its density
    /// and cow portal kept as the marker records them - or, with --remove, puts back just what the marker records.
    /// Refused while drop settings or packs are installed (TestRefused): their tables are not rebuilt here.
    /// The desecration test rows, if installed, are not kept.
    /// </summary>
    static int DaggerTestCommand(string gameRoot, bool remove)
    {
        using var game = GameData.Open(gameRoot);
        var state = DensityInstaller.Current(gameRoot);
        if (TestRefused(game, state) is { } no)
        {
            Console.WriteLine(no);
            return 1;
        }

        double multiplier = state.Installed ? state.Multiplier : 1;
        bool cowPortal = state.Installed && state.CowPortal;

        var files = new List<ModdedFile>();
        if (Math.Abs(multiplier - 1) > 0.001)
            files.Add(DensityOverride.Build(game, multiplier, state.Scope).File);

        var (cubemain, _) = game.ReadExcel("cubemain");
        if (cowPortal)
            CowPortalOverride.AppendTo(cubemain);
        if (!remove)
            DaggerTest.AppendTo(cubemain);
        if (cowPortal || !remove)
            files.Add(new ModdedFile("cubemain", DensityOverride.Serialize(cubemain)));

        if (Replaced(gameRoot, state, files) is { } stuck)
        {
            Console.WriteLine(stuck);
            return 1;
        }

        string? refused = files.Count == 0 ? null
            : DensityInstaller.Install(gameRoot, multiplier, cowPortal, ModVersion.Of(gameRoot), files, state.Scope, state.Packs);
        Console.WriteLine(refused ?? (remove
            ? $"Dagger test removed; kept density x{multiplier}, cow portal {cowPortal}."
            : $"Installed density x{multiplier}, cow portal {cowPortal}, and the dagger test: {DaggerTest.Recipe}."));
        return refused is null ? 0 : 1;
    }

    /// <summary>
    /// Builds the drop settings' treasureclassex.txt (DropOverride) into a file of your choosing,
    /// never the game folder - for measuring what a setting does before offering it.
    /// </summary>
    static int Drops(string gameRoot, bool everyKill, int materials, int highRunes, IReadOnlyList<string> boosted, bool odds, string? output)
    {
        using var game = GameData.Open(gameRoot);

        // --materials and --runes as the page's two boxes were: every material, every rune from Lem.
        var boosts = new Dictionary<string, int>(DropChoice.FromOld(highRunes, materials), StringComparer.OrdinalIgnoreCase);
        foreach (string each in boosted)
        {
            var parts = each.Split('=', 2);
            if (parts.Length == 2 && int.TryParse(parts[1], out int times))
                boosts[parts[0].Trim()] = times;
        }

        var (file, report) = DropOverride.Build(game, everyKill, boosts);
        Console.WriteLine(report);

        if (odds)
        {
            // The item tables, for the entries' names.
            using var installed = Season.Open(gameRoot, Tables());

            static string Said(double? chance) => chance is { } p ? $"1 in {1 / p:N0}" : "-";

            var book = DropBoosts.Book(game);
            var all = book.Odds(boosts);
            foreach (var entry in book.Entries)
            {
                Console.WriteLine($"{entry.Key,-5} {entry.Name ?? ItemPresenter.BaseName(entry.Icon ?? entry.Key),-26} x{DropBoosts.Of(boosts, entry.Key),-3} "
                    + string.Join("  ", all[entry.Key].Select(column => $"{column.Column}: {Said(column.Before)} -> {Said(column.After)}")));
            }

            foreach (string unknown in book.Unknown)
                Console.WriteLine($"unknown item-less row: {unknown}");
        }

        if (output is not null)
        {
            File.WriteAllBytes(output, file.Bytes);
            Console.WriteLine($"written to {output}");
        }

        return 0;
    }
}
