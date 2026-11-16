using System.Text;
using PD2SPCompanion.Core.Testbed;

namespace PD2SPCompanion.Tests.Testbed;

/// <summary>
/// The dagger test recipe, a temporary cube recipe to spawn a dagger: one row added to
/// the game's own cubemain, nothing else changed, its columns where the header says they are.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class DaggerTestTests(Game game)
{
    [GameFact]
    public void The_row_is_appended_whole_and_nothing_else_moves()
    {
        if (!game.Available)
            return;

        using var data = GameData.Open(Game.Root, Game.Tables);
        var (table, _) = data.ReadExcel("cubemain");
        int before = table.Rows.Count;
        string untouched = Encoding.UTF8.GetString(DensityOverride.Serialize(table));

        DaggerTest.AppendTo(table);
        string written = Encoding.UTF8.GetString(DensityOverride.Serialize(table));

        Assert.Equal(before + 1, table.Rows.Count);
        Assert.StartsWith(untouched.TrimEnd('\r', '\n'), written);

        var lines = written.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split('\t');
        var row = lines[^1].Split('\t');
        string Cell(string column) => row[Array.IndexOf(header, column)];

        Assert.Equal(header.Length, row.Length);
        Assert.Equal("1", Cell("enabled"));
        Assert.Equal("99", Cell("lvl"));
        Assert.Equal("isc", Cell("input 1"));
        Assert.Equal("dgr,mag", Cell("output"));
    }
}
