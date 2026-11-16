using PD2SPCompanion.Core.Items;
using PD2SPCompanion.Shell.ViewModels;
using Xunit;

namespace PD2SPCompanion.Shell.Tests.Items;

/// <summary>A tile's tooltip words a per-level line for whoever holds the item, and again when that changes.</summary>
[Collection(WithTheProgram.Name)]
public sealed class TileLevelLinesTests(Program program)
{
    [CorpusFact]
    public void A_per_level_line_follows_its_holders_level_and_says_its_figure_with_no_holder()
    {
        if (!program.Ready())
            return;

        program.On(() =>
        {
            var enigma = ItemForge.RunewordShown(Runewords.All.First(word => Runewords.GameName(word) == "Enigma"), "").Item;
            var tile = new ItemTileViewModel(enigma, 0);
            bool Says(string text) => tile.Tooltip.Any(line => line.Text == text);

            Assert.True(Says("+0.75 to Strength (Based on Character Level)"));

            tile.Judge(null, null, holderLevel: 99);
            Assert.True(Says("+74 to Strength (Based on Character Level)"));

            tile.Judge(null, null, holderLevel: 50);
            Assert.True(Says("+37 to Strength (Based on Character Level)"));

            tile.Judge(null, null);
            Assert.True(Says("+0.75 to Strength (Based on Character Level)"));
        });
    }
}
