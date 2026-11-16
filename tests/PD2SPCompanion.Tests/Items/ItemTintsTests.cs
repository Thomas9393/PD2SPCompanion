using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// An item's tint (when it moved from Graphics to Items): the corpus items draw in
/// the colours they drew in before the move, magic and rare ones through their affixes' colours.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class ItemTintsTests(Game game)
{
    [CorpusFact]
    public void Corpus_items_are_tinted_by_their_affixes_and_rows()
    {
        if (!game.Available)
            return;

        var items = new List<Item>();
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        items.AddRange(save.PlayerItemList?.Items ?? []);
        string stash = Path.Combine(Game.Corpus, "saves", "pd2_shared.stash");
        if (File.Exists(stash))
            items.AddRange(SharedStash.Read(stash).Items.Items);

        var tinted = items
            .Select(item => (item, tint: ItemPresenter.Describe(item).SpriteTint))
            .Where(each => each.tint is not null)
            .ToList();

        Assert.Contains(tinted, each => each.item.Quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Craft);
        Assert.Contains(tinted, each => each.item.Quality is ItemQuality.Unique or ItemQuality.Set);
    }
}
