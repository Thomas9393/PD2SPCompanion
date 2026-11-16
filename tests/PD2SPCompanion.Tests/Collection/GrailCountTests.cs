using Microsoft.Data.Sqlite;
using Xunit;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// The grail's count of copies a collection holds: by the card each copy is - its row,
/// ethereal or not (one card a row) - and without the rows a withdrawal is taking out.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class GrailCountTests(Game game) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pd2spc-grail-count-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [CorpusFact]
    public void A_collection_counts_its_copies_by_grail_card()
    {
        if (!game.Available)
            return;

        // A unique that drops ethereal as well as plain, and a set piece.
        var unique = GrailCatalog.Uniques().First(entry => GrailCatalog.HasEtherealDrop(entry) && !entry.Item.IsEthereal);
        var set = GrailCatalog.Sets()[0];

        // The stamp a real item of this season carries - the corpus character's - so the copies write.
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        string stamp = save.PlayerItemList!.Items.First(item => item.Version is { Length: > 0 }).Version;

        Directory.CreateDirectory(_folder);
        using var vault = Vault.Open(Path.Combine(_folder, "collection.pd2lib"), "S13", 10);

        long? added = vault.Add(ItemForge.UniqueAt(unique.Row, stamp, RollAs.Random, new Random(1)).Item, null, out string? refused);
        Assert.True(added is not null, refused);
        long first = added!.Value;
        vault.Add(ItemForge.UniqueAt(unique.Row, stamp, RollAs.Random, new Random(2)).Item, null, out _);
        vault.Add(ItemForge.UniqueAt(unique.Row, stamp, RollAs.Random, new Random(3), ethereal: true).Item, null, out _);
        vault.Add(ItemForge.SetItemAt(set.Row, stamp, RollAs.Random, new Random(4)).Item, null, out _);

        // A runeword: no card of its own, so not counted.
        var word = Runewords.All.First(each => each.Types.Any(type => ItemForge.BaseFor([type], each.ExcludedTypes) is not null)
                                               && ItemForge.BestBaseFor([.. each.Types], each.ExcludedTypes, each.Runes.Length) is not null);
        string runewordBase = ItemForge.BestBaseFor([.. word.Types], word.ExcludedTypes, word.Runes.Length)!;
        var runeword = ItemForge.Runeword(word, runewordBase, stamp).Item;
        Assert.Null(GrailCatalog.EntryOf(runeword));
        vault.Add(runeword, null, out _);

        // The unique's three copies on its one card, the ethereal one among them; the set piece's one.
        var counts = vault.GrailCounts([]);
        Assert.Equal(3, counts.GetValueOrDefault((unique.Kind, unique.Row)));
        Assert.Equal(1, counts.GetValueOrDefault((set.Kind, set.Row)));
        Assert.Equal(2, counts.Count);

        // A row being withdrawn is the character's already.
        var leaving = vault.GrailCounts([first]);
        Assert.Equal(2, leaving.GetValueOrDefault((unique.Kind, unique.Row)));
    }
}
