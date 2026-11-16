using System.Text;
using D2SLib.Model.TXT;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// Which of the table's previous-season columns last season's items are read with (the S14 open beta):
/// its itemstatcost carries S12's columns still and S13's after them, and the offset column of S13 is spelled
/// <c>Save Adds S13</c>. The first column found named the season S12 - every S13 item read with S12's widths, the
/// installed season taken for S13 - and the offset fell back to the live column.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class PreviousSeasonColumnsTests
{
    private const string Table =
        "Stat\tID\tSave Bits\tSave Add\tSave Param Bits\tSave Bits S12\tSave Add S12\tSave Param Bits S12\tSave Bits S13\tSave Adds S13\tSave Param Bits S13\n" +
        "probe\t0\t10\t0\t0\t12\t1\t0\t2\t3\t0\n";

    [GameFact]
    public void The_newest_seasons_columns_are_read_whatever_their_spelling()
    {
        var installed = D2SLib.Core.TXT.ItemStatCostTXT;
        try
        {
            var table = ItemStatCostTXT.Read(new MemoryStream(Encoding.ASCII.GetBytes(Table)));
            StatEncoding.Discover(table);
            Assert.Equal("S13", StatEncoding.PreviousSeason);

            var row = table["probe"];
            using (D2SLib.Globals.Use(D2SLib.Globals.Layout with { PreviousSeasonStats = true }))
            {
                Assert.Equal(2, StatEncoding.SaveBits(row));
                Assert.Equal(3, StatEncoding.SaveAdd(row));
            }

            using (D2SLib.Globals.Use(D2SLib.Globals.Layout with { PreviousSeasonStats = false }))
            {
                Assert.Equal(10, StatEncoding.SaveBits(row));
                Assert.Equal(0, StatEncoding.SaveAdd(row));
            }
        }
        finally
        {
            if (installed is not null)
                StatEncoding.Discover(installed);
        }
    }
}
