using System;
using System.IO;
using System.Linq;

using Xunit;
using Xunit.Abstractions;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A corruption granting every resistance
/// read as four lines, one a resistance, in the picker's list - and as "Fire Resist" alone at its top. It reads
/// "All Resistances", as the game and the wiki say it.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CorruptionWordingTests(Game game, ITestOutputHelper output)
{
    [CorpusFact]
    public void A_corruption_granting_every_resistance_reads_all_resistances()
    {
        if (!game.Available)
            return;

        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        var options = save.PlayerItemList!.Items
            .SelectMany(item => Corruptions.Available(item, asIfClean: true))
            .Where(option => option.Grants.Any(grant => grant.Code.Equals("res-all", StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(option => option.Id)
            .ToList();

        Assert.NotEmpty(options);
        foreach (var option in options)
        {
            output.WriteLine($"{option.Id}: {option.Readable}");
            Assert.Contains("All Resistances", option.Readable);
            Assert.DoesNotContain("Fire Resist", option.Readable);
            Assert.DoesNotContain("Poison Resist", option.Readable);
        }
    }
}
