using Xunit;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// The Compendium (<see cref="Compendium"/>): a real one, first with the
/// one item deposited - Arkaine's Valor, an S13 item - then with ten (specimens/s14-compendium/ten), read
/// whole and written back to the same bytes; an item taken out and put back in; an empty one made and read again.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CompendiumTests(Game game)
{
    private static string One => Path.Combine(Game.Corpus, "specimens", "s14-discovery", "after-flamebellow", "pd2_shared.compendium");

    private static string Ten => Path.Combine(Game.Corpus, "specimens", "s14-compendium", "ten", "pd2_shared.compendium");

    /// <summary>The test's own work: S14's stamps where S14's tables are installed, as a character opened says them.</summary>
    private static T WithStamps<T>(Func<T> run)
    {
        if (Game.Tables is not null)
            return run();

        var kept = (SeasonReading.CurrentStamp, SeasonReading.PreviousStamp);
        SeasonReading.SetStamps("105", "103");
        try { return run(); }
        finally { SeasonReading.SetStamps(kept.CurrentStamp, kept.PreviousStamp); }
    }

    [CorpusFact]
    public void The_users_first_compendium_holds_the_one_item_deposited_whole()
    {
        if (!game.Available || !File.Exists(One))
            return;

        var compendium = WithStamps(() => Compendium.Read(One));

        Assert.Equal(1, compendium.Version);
        var item = Assert.Single(compendium.Items);
        Assert.Equal(D2SLib.Model.Save.ItemQuality.Unique, item.Quality);
        Assert.Equal("Arkaine's Valor", ItemPresenter.QualityIndex(item));
    }

    [CorpusFact]
    public void Ten_deposited_in_game_read_and_write_back_to_the_same_bytes_each_on_page_7()
    {
        if (!game.Available || !File.Exists(Ten) || Game.Tables is not null)
            return;

        byte[] bytes = File.ReadAllBytes(Ten);
        var compendium = WithStamps(() => Compendium.Read(Ten));

        Assert.Equal(10, compendium.Items.Count);
        Assert.All(compendium.Items, item => Assert.Equal(Compendium.Page, item.Page));
        Assert.Equal(bytes, WithStamps(compendium.Write));
    }

    [CorpusFact]
    public void An_item_taken_out_and_put_back_last_is_the_file_with_it_moved_to_the_end()
    {
        if (!game.Available || !File.Exists(Ten) || Game.Tables is not null)
            return;

        var compendium = WithStamps(() => Compendium.Read(Ten));
        var first = compendium.Items[0];
        int count = compendium.Items.Count;

        Assert.True(compendium.TakeOut(first));
        Assert.Equal(count - 1, compendium.Items.Count);
        Assert.False(compendium.TakeOut(first));

        compendium.Deposit(first);
        Assert.Same(first, compendium.Items[^1]);
        Assert.Equal(Compendium.Page, first.Page);

        // Written, it reads again - the same items, the first now last.
        byte[] written = WithStamps(compendium.Write);
        var again = WithStamps(() => Compendium.Read(written, "moved"));
        Assert.Equal(count, again.Items.Count);
        Assert.Equal(first.Id, again.Items[^1].Id);
    }

    [CorpusFact]
    public void An_empty_compendium_writes_as_the_game_begins_one_and_reads_again()
    {
        if (!game.Available)
            return;

        byte[] bytes = Compendium.Empty().Write();

        Assert.Equal([0x55, 0xBC, 0x55, 0xBC, 1, 0, 0, 0], bytes[..8]);
        Assert.Equal(bytes.Length, BitConverter.ToInt32(bytes, 8));
        Assert.Equal("JM"u8.ToArray(), bytes[0x10..0x12]);
        Assert.Empty(Compendium.Read(bytes, "empty").Items);
    }

    [CorpusFact]
    public void A_file_that_is_not_a_compendium_is_refused_and_none_in_a_folder_is_none()
    {
        if (!game.Available)
            return;

        Assert.Throws<InvalidDataException>(() => Compendium.Read(new byte[64], "nothing"));
        Assert.Null(Compendium.In(Path.Combine(Path.GetTempPath(), "pd2spc-no-such-folder"), hardcore: false));
    }
}
