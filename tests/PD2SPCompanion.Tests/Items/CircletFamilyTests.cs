using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// The circlets' line of four (ItemTier): a Circlet's row names itself, the Coronet above it and no elite,
/// and the Coronet is the normal tier of Coronet, Tiara, Diadem. By the upgrade recipes' own test a Circlet
/// is a basic item and cubes into a Coronet (PD2's loot filter says so of one; so does the game).
/// It was a base with no tier here, could not be upgraded, and a set Circlet cubed in game
/// would have lost its name.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CircletFamilyTests(Game game)
{
    private const string Stamp = "103";

    [GameFact]
    public void The_circlets_are_a_line_of_four()
    {
        if (!game.Available)
            return;

        Assert.Equal(ItemTier.Normal, ItemTier.Of("ci0"));
        Assert.Equal(ItemTier.Normal, ItemTier.Of("ci1"));
        Assert.Equal(ItemTier.Exceptional, ItemTier.Of("ci2"));
        Assert.Equal(ItemTier.Elite, ItemTier.Of("ci3"));

        Assert.Equal("ci1", ItemTier.Next("ci0"));
        Assert.Equal("ci2", ItemTier.Next("ci1"));
        Assert.Equal("ci3", ItemTier.Next("ci2"));
        Assert.Null(ItemTier.Next("ci3"));

        Assert.True(ItemTier.SameFamily("ci0", "ci1"));
        Assert.True(ItemTier.SameFamily("ci0", "ci3"));
        Assert.False(ItemTier.SameFamily("ci0", "cap"));

        Assert.Equal(["ci0"], ItemTier.Before("ci3"));
        Assert.Empty(ItemTier.Before("uar"));

        // Every other tiered base is as it was: three codes, and the next one up.
        Assert.Equal(["cap", "xap", "uap"], ItemTier.Family("cap"));
        Assert.Equal("xap", ItemTier.Next("cap"));
        Assert.Null(ItemTier.Next("uap"));
    }

    [GameFact]
    public void A_set_circlet_upgraded_keeps_its_name_all_the_way_up()
    {
        if (!game.Available)
            return;

        int row = ItemForge.SetItems().First(each => ItemForge.SetItemAt(each, Stamp).Item.Code.Trim() == "ci0");
        var item = ItemForge.SetItemAt(row, Stamp).Item;
        string name = ItemPresenter.Describe(item).Name;
        Assert.Equal("Naj's Circlet", name);

        // As the game made it: a Coronet of 40 defence and 30 durability, a Tiara of 50 and 25, a
        // Diadem of 60 and 20 - each base's top defence, the forged piece being at its own top, and each base's
        // own durability, which goes down as these go up.
        Assert.Equal((30, 35), ((int)item.Armor, (int)item.MaxDurability));

        foreach (var (next, defense, durability) in new[] { ("ci1", 40, 30), ("ci2", 50, 25), ("ci3", 60, 20) })
        {
            Assert.True(BaseUpgrades.CanUpgrade(item, anyItem: false), $"not offered an upgrade to {next}");
            BaseUpgrades.Upgrade(item, new Random(1));
            Assert.Equal(next, item.Code.Trim());
            Assert.Equal(defense, (int)item.Armor);
            Assert.Equal(durability, (int)item.MaxDurability);

            // Rolled anew, as the game rolled it: 20, 22, 20 and 17 of 30; 20 of 25; 10 of 20.
            Assert.InRange((int)item.Durability, durability / 2, durability - 1);

            var shown = ItemPresenter.Describe(item);
            Assert.Equal(name, shown.Name);
            Assert.Null(shown.Warning);
            Assert.Empty(Legitimacy.Check(item));

            byte[] bytes = Item.Write(item, 0x60);
            Assert.True(bytes.AsSpan().SequenceEqual(Item.Write(Item.Read(bytes, 0x60), 0x60)));
        }

        Assert.False(BaseUpgrades.CanUpgrade(item, anyItem: false));
    }

    [GameFact]
    public void An_upgrade_rolls_the_current_durability_whatever_the_item_had()
    {
        if (!game.Available)
            return;

        int row = ItemForge.SetItems().First(each => ItemForge.SetItemAt(each, Stamp).Item.Code.Trim() == "ci0");
        var seen = new HashSet<int>();

        // The game's own three: Circlets at 30, 25 and 5 of 35 came out at 22, 20 and 17 of 30.
        foreach (int had in new[] { 35, 30, 25, 5 })
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var item = ItemForge.SetItemAt(row, Stamp).Item;
                item.Durability = (ushort)had;
                BaseUpgrades.Upgrade(item, new Random(seed));

                Assert.Equal(30, (int)item.MaxDurability);
                Assert.InRange((int)item.Durability, 15, 29);
                seen.Add(item.Durability);
            }
        }

        // A roll, not a figure: every one of the game's four is among what comes out.
        Assert.Superset(new HashSet<int> { 17, 20, 22 }, seen);
    }
}
