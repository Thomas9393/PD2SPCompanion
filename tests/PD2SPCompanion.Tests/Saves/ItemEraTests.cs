using D2SLib;
using D2SLib.Model.Save;
using D2SLib.Model.TXT;
using Xunit;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// Each item read and written in its own season's encoding, by its stamp (<see cref="Globals.ItemEra"/>), as the game
/// does: an S14 file holds S13 items beside its own - NewS ten 105 items and a 103 Bonehew, ShockSacri 89 at 103 and 6
/// at 105. Read as one season the file is not: an old
/// item carrying a stat whose encoding moved read wrong. The stat here is one the installed table encodes differently
/// in its previous season's columns, so the test holds under any season's tables.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ItemEraTests(Game game)
{
    private const string Now = "105", Then = "103";

    /// <summary>A plain stat (no parameter) whose width or offset the previous season's columns give otherwise.</summary>
    private static (string Name, ushort Id)? MovedStat()
    {
        var table = D2SLib.Core.TXT.ItemStatCostTXT;
        foreach (var row in table.Rows)
        {
            if (!int.TryParse(row["ID"].Value?.Trim(), out int id) || row["Stat"].Value?.Trim() is not { Length: > 0 } name)
                continue;

            (int Bits, int Add, int Param) Encoding(bool previous)
            {
                using (Globals.Use(new ParserLayout(false, 10, previous)))
                    return (StatEncoding.SaveBits(row), StatEncoding.SaveAdd(row), StatEncoding.SaveParamBits(row));
            }

            var now = Encoding(false);
            var then = Encoding(true);
            if (now.Param == 0 && then.Param == 0 && now.Bits > 1 && then.Bits > 1 && now != then)
                return (name, (ushort)id);
        }

        return null;
    }

    private static Item Carrying(string stamp, (string Name, ushort Id) stat)
    {
        var item = ItemForge.PlainBase("7o7", 0, stamp).Item;
        item.Id = 0x1234;
        ItemStats.Home(item).Stats.Add(new ItemStat { Stat = stat.Name, Id = stat.Id, Value = 1 });
        return item;
    }

    [GameFact]
    public void An_old_item_in_a_new_file_reads_and_writes_in_its_own_seasons_encoding()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason || MovedStat() is not { } stat)
            return;

        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        try
        {
            // As the game wrote each: the old one in last season's encoding, the new one in this season's.
            SeasonReading.SetStamps(null, null);
            byte[] old, fresh;
            using (Globals.Use(new ParserLayout(false, 10, true)))
                old = Item.Write(Carrying(Then, stat), 0x60);
            using (Globals.Use(new ParserLayout(false, 10, false)))
                fresh = Item.Write(Carrying(Now, stat), 0x60);

            // Read inside one this-season file, with both seasons' stamps known.
            SeasonReading.SetStamps(Now, Then);
            using (Globals.Use(new ParserLayout(false, 10, false)))
            {
                var readOld = Item.Read(old, 0x60);
                var readNew = Item.Read(fresh, 0x60);

                Assert.Equal(1, ItemStats.Every(readOld).Single(each => each.Stat == stat.Name).Value);
                Assert.Equal(1, ItemStats.Every(readNew).Single(each => each.Stat == stat.Name).Value);
                Assert.Equal(old, Item.Write(readOld, 0x60));
                Assert.Equal(fresh, Item.Write(readNew, 0x60));
            }
        }
        finally
        {
            SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp);
        }
    }

    [GameFact]
    public void A_stamp_of_neither_season_keeps_the_files_encoding()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason || MovedStat() is not { } stat)
            return;

        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        try
        {
            SeasonReading.SetStamps(null, null);
            byte[] asLast;
            using (Globals.Use(new ParserLayout(false, 10, true)))
                asLast = Item.Write(Carrying("101", stat), 0x60);

            SeasonReading.SetStamps(Now, Then);
            using (Globals.Use(new ParserLayout(false, 10, true)))
                Assert.Equal(asLast, Item.Write(Item.Read(asLast, 0x60), 0x60));
        }
        finally
        {
            SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp);
        }
    }
}
