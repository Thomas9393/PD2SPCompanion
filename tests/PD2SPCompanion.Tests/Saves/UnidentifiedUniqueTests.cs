using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// Season 14 writes four bits after an unidentified unique's row id (Item.UnidentifiedTail): a character stopped
/// opening once the game saved it with three uniques dropped unidentified.
/// Read with them, it reads back byte for byte and the three hold their rows' stats; identified, an
/// item is written without them - the shape every identified unique has.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class UnidentifiedUniqueTests(Game game)
{
    private static string Specimen =>
        Path.Combine(Game.Corpus, "specimens", "s14-corrupt-desecrate-2026-10-26", "PhysSpear-unidentified-uniques.d2s");

    private bool Ready => game.Available && Game.Tables is null && File.Exists(Specimen);

    private static string Copy()
    {
        string copy = Path.Combine(Path.GetTempPath(), "pd2spc-unid-" + Guid.NewGuid().ToString("N") + ".d2s");
        File.Copy(Specimen, copy);
        return copy;
    }

    private static int Held(Item item, string stat) => ItemStats.Every(item).First(each => each.Stat == stat).Value;

    [CorpusFact]
    public void A_character_holding_unidentified_uniques_dropped_in_Season_14_reads_back_exactly()
    {
        if (!Ready)
            return;

        string copy = Copy();
        try
        {
            var (save, result) = SaveVerifier.Load(copy);
            Assert.True(result.RoundTripped);

            var unidentified = save.PlayerItemList.Items.Where(item => item.Quality == ItemQuality.Unique && !item.IsIdentified).ToList();
            Assert.Equal(["Ichorsting", "Stormspire", "Wall of the Eyeless"],
                         unidentified.Select(item => ItemPresenter.Describe(item).Name).Order());

            var ichorsting = unidentified.Single(item => item.Code.Trim() == "mxb");
            Assert.Equal(20, Held(ichorsting, "dexterity"));
            Assert.Equal(50, Held(ichorsting, "item_pierce"));
            var wall = unidentified.Single(item => item.Code.Trim() == "bsh");
            Assert.Equal(20, Held(wall, "item_fastercastrate"));
            Assert.Equal(40, wall.MaxDurability);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    /// <summary>
    /// A second specimen, the same evening: an ethereal Trang-Oul's Claws dropped unidentified, beside an
    /// unidentified Snowclash - a set item carries the four bits too. Its own lines and its three-piece bonus as its row
    /// gives them.
    /// </summary>
    [CorpusFact]
    public void An_unidentified_set_item_carries_them_too()
    {
        string specimen = Path.Combine(Path.GetDirectoryName(Specimen)!, "PhysSpear-unidentified-set.d2s");
        if (!Ready || !File.Exists(specimen))
            return;

        string copy = Path.Combine(Path.GetTempPath(), "pd2spc-unid-set-" + Guid.NewGuid().ToString("N") + ".d2s");
        File.Copy(specimen, copy);
        try
        {
            var (save, result) = SaveVerifier.Load(copy);
            Assert.True(result.RoundTripped);

            var claws = save.PlayerItemList.Items.Single(item => item.Code.Trim() == "xmg" && item.Quality == ItemQuality.Set);
            Assert.False(claws.IsIdentified);
            Assert.True(claws.IsEthereal);
            Assert.Equal(30, Held(claws, "coldresist"));
            Assert.Equal(20, Held(claws, "item_fastercastrate"));
            Assert.Equal(16, claws.MaxDurability);
            Assert.Equal(20, claws.StatLists[1].Stats.Single().Value);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [CorpusFact]
    public void Identified_it_is_written_without_the_four_bits_and_reads_back()
    {
        if (!Ready)
            return;

        string copy = Copy();
        try
        {
            var (save, _) = SaveVerifier.Load(copy);
            var stormspire = save.PlayerItemList.Items.Single(item => item.Code.Trim() == "7wc" && !item.IsIdentified);
            int bytesBefore = Item.Write(stormspire, 0x60).Length * 8;
            string before = string.Join(";", ItemStats.Every(stormspire).Select(stat => $"{stat.Stat}={stat.Value}"));

            stormspire.IsIdentified = true;
            byte[] written = Item.Write(stormspire, 0x60);
            var again = Item.Read(written, 0x60);

            Assert.True(again.IsIdentified);
            Assert.Equal(before, string.Join(";", ItemStats.Every(again).Select(stat => $"{stat.Stat}={stat.Value}")));
            Assert.True(written.Length * 8 <= bytesBefore);
        }
        finally
        {
            File.Delete(copy);
        }
    }
}
