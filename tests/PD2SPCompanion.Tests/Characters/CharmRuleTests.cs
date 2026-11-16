using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// The stat sheet counts only the charms that work, by the charm
/// inventory rule: wholly in the charm area, one of each unique, identified (<see cref="Requirements.Counted"/>).
/// A second Annihilus counted in full, and a charm in the top rows gave its lines.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CharmRuleTests(Game game)
{
    /// <summary>The corpus's own character, read and never written, its inventory charms taken out in memory.</summary>
    private static D2S Character()
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        save.PlayerItemList.Items.RemoveAll(item => item.Mode == ItemMode.Stored && Requirements.IsCharm(item));
        save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;
        return save;
    }

    private static Item Stored(D2S save, Item item, int x, int y)
    {
        item.Mode = ItemMode.Stored;
        item.Page = InventoryLayout.InventoryPage;
        item.Location = ItemLocation.None;
        (item.X, item.Y) = ((byte)x, (byte)y);
        save.PlayerItemList.Items.Add(item);
        save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;
        return item;
    }

    private static bool Counts(D2S save, Item item) => Requirements.Counted(save, 0).Any(each => ReferenceEquals(each, item));

    [CorpusFact]
    public void Only_the_charms_that_work_count()
    {
        if (!game.Available)
            return;

        var save = Character();
        Assert.Equal(new GridSize(10, 8), CharmArea.InventoryOf(save.ClassId));

        // The charm area is the bottom half: the top rows hold a charm the game does not count.
        var below = Stored(save, ItemForge.BaseAs("cm1", ItemQuality.Magic, "103").Item, 0, 4);
        var above = Stored(save, ItemForge.BaseAs("cm1", ItemQuality.Magic, "103").Item, 1, 0);
        // A grand charm reaching up across the line: not wholly in the area.
        var across = Stored(save, ItemForge.BaseAs("cm3", ItemQuality.Magic, "103").Item, 2, 2);
        Assert.True(Counts(save, below));
        Assert.False(Counts(save, above));
        Assert.False(Counts(save, across));

        // One of each unique: the second Annihilus is carried, not counted.
        var first = Stored(save, ItemForge.Unique("Annihilus", "103").Item, 9, 7);
        var second = Stored(save, ItemForge.Unique("Annihilus", "103").Item, 8, 7);
        Assert.True(Counts(save, first));
        Assert.False(Counts(save, second));

        // Unidentified: nothing.
        var hidden = Stored(save, ItemForge.BaseAs("cm1", ItemQuality.Magic, "103").Item, 7, 7);
        hidden.IsIdentified = false;
        Assert.False(Counts(save, hidden));
    }

    /// <summary>
    /// The tile's red mark says the same (the second unique charm marked too), and why: the
    /// first Annihilus in the save's order counts, the second does not; moved above the line, the first is outside
    /// and the second counts - what a landing asks before the item is put down.
    /// </summary>
    [CorpusFact]
    public void The_mark_names_the_charm_that_does_nothing()
    {
        if (!game.Available)
            return;

        var save = Character();
        var size = CharmArea.InventoryOf(save.ClassId)!;
        var first = Stored(save, ItemForge.Unique("Annihilus", "103").Item, 9, 7);
        var second = Stored(save, ItemForge.Unique("Annihilus", "103").Item, 8, 7);
        var inventory = save.PlayerItemList.Items.Where(item => item.Mode == ItemMode.Stored).ToList();

        Assert.Equal(CharmIdle.None, CharmArea.IdleAt(first, inventory, size, first.Y, 1));
        Assert.Equal(CharmIdle.AnotherCounts, CharmArea.IdleAt(second, inventory, size, second.Y, 1));

        // The first lifted above the line: there it is outside, and the second takes its place.
        Assert.Equal(CharmIdle.OutsideArea, CharmArea.IdleAt(first, inventory, size, 0, 1));
        first.Y = 0;
        Assert.Equal(CharmIdle.None, CharmArea.IdleAt(second, inventory, size, second.Y, 1));
        Assert.True(Counts(save, second));
    }
}
