using D2SLib.Model.Save;

using PD2SPCompanion.Tests.Items;

namespace PD2SPCompanion.Tests.Crafting;

/// <summary>
/// A Rune Prod, shown as unknown after its affixes were picked: a Hitpower-crafted
/// Matriarchal Spear given Cruel, Master's, of Quickness and of Amplify Damage. Master's attack rating and Quickness's
/// attack speed are Brilliant's two lines too, so two recipes fit its properties and the recipe was told as none -
/// "Crafting: Unknown", before Blood and after. Where several fit, the one that leaves the fewest of the item's
/// properties unexplained is the recipe: Hitpower, then Blood.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CraftRecipeTieTests(Game game)
{
    private static Item RuneProd()
    {
        var item = AffixSweepTests.Fresh("ame");
        item.Quality = ItemQuality.Craft;
        var hitpower = AffixRanges.CraftFamilies(item).First(family => family.Family == "Hitpower");
        Crafts.Reforge(item, Crafts.Draft(hitpower, new Random(5)), new Random(5));

        var picks = new Dictionary<(int, bool), MagicAffixes.AffixPick>();
        foreach (var (slot, prefix, name) in new[] { (0, true, "Cruel"), (1, true, "Master's"), (0, false, "of Quickness"), (1, false, "of Amplify Damage") })
        {
            var option = MagicAffixes.Available(item, prefix, slot).First(each => each.Name == name && each.Grants.Count > 0
                && (name != "Master's" || each.Grants.Any(grant => grant.Code == "att"))
                && (name != "of Amplify Damage" || each.Grants.Any(grant => grant.Code == "hit-skill")));
            picks[(slot, prefix)] = new MagicAffixes.AffixPick(option.Id, MagicAffixes.Roll(option, new Random(slot + 1)));
        }
        MagicAffixes.Apply(item, picks, new Random(3));
        return item;
    }

    [GameFact]
    public void Where_two_recipes_fit_the_one_that_leaves_least_unexplained_is_the_recipe_before_blood_and_after()
    {
        if (!game.Available)
            return;

        var item = RuneProd();
        Assert.Equal("Hitpower", AffixRanges.CraftFamilyOf(item));

        var blood = AffixRanges.CraftFamilies(item).First(family => family.Family == "Blood");
        Crafts.Reforge(item, Crafts.Draft(blood, new Random(6)), new Random(6));
        Assert.Equal("Blood", AffixRanges.CraftFamilyOf(item));
        Assert.Equal("Blood", AffixRanges.CraftOn(item)?.Family);
    }
}
