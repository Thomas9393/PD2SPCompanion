using D2SLib;
using D2SLib.Model.Save;
using Xunit;

namespace PD2SPCompanion.Tests.Saves;

/// <summary>
/// Files in last season's encoding, as the player's files are on the first launch of a new season.
/// Made from the corpus by writing it with the installed
/// tables' previous-season columns - exactly what this season's files will be to the next.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class LastSeasonTests(Game game)
{
    private static readonly ParserLayout LastSeasonItems = new(false, 10, true);
    private static readonly ParserLayout ThisSeasonItems = new(false, 10, false);

    private static string StashPath => Path.Combine(Game.Corpus, "saves", "pd2_shared.stash");

    private static string Values(IEnumerable<Item> items) =>
        string.Join("|", items.Where(item => !item.IsCompact).SelectMany(item => item.SocketedItems.Prepend(item))
            .Select(item => string.Join(";", ItemStats.Every(item).Select(stat => $"{stat.Stat}={stat.Value}/{stat.Param}"))));

    /// <summary>
    /// A new season's first launch, its stamp not yet in the table: no item says its encoding, and the file is told by
    /// plausibility. With the stamps known, the reader goes by each item's own and a file of last season's reads as a mix.
    /// </summary>
    private sealed class StampsUnknown : IDisposable
    {
        public StampsUnknown() => SeasonReading.SetStamps(null, null);

        public void Dispose() => Game.UseStamps();
    }

    /// <summary>A shared stash file holding <paramref name="items"/>, its header the corpus stash's, in one encoding.</summary>
    private static byte[] StashOf(ItemList items, ParserLayout layout)
    {
        byte[] header = File.ReadAllBytes(StashPath)[..SharedStash.Read(StashPath).HeaderLength];
        items.Count = (ushort)(items.Items?.Count ?? 0);
        byte[] tail;
        using (Globals.Use(layout))
            tail = ItemList.Write(items, 0x60);
        var bytes = header.Concat(tail).ToArray();
        Header.Fix(bytes);
        return bytes;
    }

    [CorpusFact]
    public void A_last_season_shared_stash_reads_as_last_seasons_and_writes_back_in_it()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason)
            return;

        var stash = SharedStash.Read(StashPath);
        using var unknown = new StampsUnknown();

        string truth = Values(stash.Items.Items ?? []);
        byte[] old = StashOf(stash.Items, LastSeasonItems);

        var again = SharedStash.Read(old, "last season's stash");

        Assert.True(again.LastSeason);
        Assert.Equal(truth, Values(again.Items.Items ?? []));
        Assert.Equal(old, again.Write());
    }

    [CorpusFact]
    public void A_last_season_character_loads_as_last_seasons()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason)
            return;

        using var unknown = new StampsUnknown();

        string scratch = Path.Combine(Path.GetTempPath(), "pd2spc-lastseason-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Game.CopyOfCharacter(scratch);
            var (save, _) = SaveVerifier.Load(path);
            var items = (save.PlayerItemList?.Items ?? []).Concat(save.MercenaryItemList?.ItemList?.Items ?? []).ToList();
            string truth = Values(items);

            save.Layout = new ParserLayout(true, 10, true);
            File.WriteAllBytes(path, D2S.Write(save));

            var (again, result) = SaveVerifier.Load(path);
            Assert.Equal(SaveFormat.Pd2LastSeason, result.Format);
            Assert.Equal(truth, Values((again.PlayerItemList?.Items ?? []).Concat(again.MercenaryItemList?.ItemList?.Items ?? [])));
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The corpus items that read cleanly in both encodings with different values - a stat whose field
    /// only moved its offset (a charm of Vita's life). A file of only these is the case the order of
    /// attempts used to settle, wrongly.
    /// </summary>
    private static List<Item> ReadBothWays() => ReadBothWays(SharedStash.Read(StashPath).Items.Items ?? []);

    /// <summary>
    /// Items made from the installed tables' own rows that read both ways: the corpus stash holds none under Season 14's
    /// tables, and a stash of none proves nothing (it passed here, comparing "" with "").
    /// </summary>
    private static List<Item> MadeToReadBothWays()
    {
        // With no stamp to go by, or each reads by its own whatever the layout says.
        using var unknown = new StampsUnknown();
        using (Globals.Use(ThisSeasonItems))
        {
            var made = ItemForge.UniqueNames().Select(name => ItemForge.Unique(name, Game.Stamp).Item)
                .Concat(ItemForge.SetItems().Select(row => ItemForge.SetItemAt(row, Game.Stamp).Item));

            // As a file would hold them: a forged item's empty parameter reads back a nought, which is no difference.
            return [.. ReadBothWays(made.Select(item => Item.Read(Item.Write(item, 0x60), 0x60))).Take(4)];
        }
    }

    private static List<Item> ReadBothWays(IEnumerable<Item> items)
    {
        var both = new List<Item>();
        foreach (var item in items.Where(item => !item.IsCompact))
        {
            byte[] old;
            using (Globals.Use(LastSeasonItems))
                old = Item.Write(item, 0x60);
            try
            {
                using (Globals.Use(ThisSeasonItems))
                {
                    var asNow = Item.Read(old, 0x60);
                    if (Item.Write(asNow, 0x60).AsSpan().SequenceEqual(old) && Values([asNow]) != Values([item]))
                        both.Add(item);
                }
            }
            catch (Exception)
            {
                // Refused under this season's encoding: not the case here.
            }
        }

        return both;
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void A_stash_of_items_reading_both_ways_is_never_shown_wrong()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available || !SeasonReading.HasPreviousSeason)
            return;

        var both = ReadBothWays();
        Assert.True(both.Count >= 2, $"only {both.Count} items read both ways in the corpus stash");

        // In last season's encoding: read as last season's, values right. Never this season's - and not
        // refused either: the misread's values fall outside their ranges, which settles it.
        string truth = Values(both);
        byte[] old = StashOf(new ItemList { Items = both }, LastSeasonItems);

        var read = SharedStash.Read(old, "last season's charms");

        Assert.True(read.LastSeason, "read as this season's: every value shown wrong");
        Assert.Equal(truth, Values(read.Items.Items ?? []));
    }

    [Trait("Season", "S13")]
    [CorpusFact]
    public void Every_file_of_this_season_in_the_corpus_still_reads_as_this_seasons()
    {
        if (!game.Pinned("S13"))
            return;

        if (!game.Available)
            return;

        var characters = Directory.GetFiles(Path.Combine(Game.Corpus, "stress"), "*.d2s")
            .Append(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s")).ToList();
        Assert.True(characters.Count > 30, $"only {characters.Count} corpus characters");

        foreach (string path in characters)
            Assert.Equal(SaveFormat.Pd2, SaveVerifier.Load(path).Result.Format);

        Assert.False(SharedStash.Read(StashPath).LastSeason);
    }

    [CorpusFact]
    public void The_same_items_in_this_seasons_encoding_stay_this_seasons()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason)
            return;

        // Told by their stamp, which is this season's.
        var both = MadeToReadBothWays();
        Assert.NotEmpty(both);

        string truth = Values(both);
        byte[] now = StashOf(new ItemList { Items = both }, ThisSeasonItems);

        var read = SharedStash.Read(now, "this season's items");

        Assert.False(read.LastSeason);
        Assert.Equal(truth, Values(read.Items.Items ?? []));
    }

    [CorpusFact]
    public void The_same_items_with_no_stamps_known_are_told_by_their_rows()
    {
        if (!game.Available || !SeasonReading.HasPreviousSeason)
            return;

        var both = MadeToReadBothWays();
        Assert.NotEmpty(both);

        using var unknown = new StampsUnknown();

        string truth = Values(both);
        byte[] now = StashOf(new ItemList { Items = both }, ThisSeasonItems);

        var read = SharedStash.Read(now, "this season's items");

        Assert.False(read.LastSeason, "read as last season's: every value shown wrong");
        Assert.Equal(truth, Values(read.Items.Items ?? []));
    }
}
