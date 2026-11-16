using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// An affix swapped out takes all it wrote with it - a property that names no stat too. "of Ages" gives Indestructible
/// by <c>indestruct</c>, which names none; swapped for "of Teleportation" on the Forge, the line stayed and the item was
/// flagged for it (a staff).
/// </summary>
[Collection(nameof(GameTables))]
public sealed class AffixSwapLeavesNothingTests(Game game)
{
    private const int OfAges = 404, OfTeleportation = 532;

    private static bool Has(Item item, string stat) =>
        item.StatLists.FirstOrDefault()?.Stats.Any(each => each.Stat == stat) == true;

    private static void Suffix(Item item, int id) =>
        MagicAffixes.Apply(item,
            new Dictionary<(int, bool), MagicAffixes.AffixPick> { [(0, false)] = new(id, MagicAffixes.Roll(MagicAffixes.Find(id, prefix: false)!, new Random(3))) },
            new Random(3));

    [GameFact]
    public void Of_Ages_swapped_for_of_Teleportation_takes_Indestructible_with_it()
    {
        if (!game.Available)
            return;

        var staff = ItemForge.BaseAs("6bs", ItemQuality.Magic, "103", new Random(5)).Item;
        staff.ItemLevel = 99;
        Array.Clear(staff.MagicPrefixIds!);
        Array.Clear(staff.MagicSuffixIds!);

        Suffix(staff, OfAges);
        Assert.True(Has(staff, "item_indesctructible"), "of Ages wrote no Indestructible");

        Suffix(staff, OfTeleportation);
        Assert.Equal(OfTeleportation, staff.MagicSuffixIds![0]);
        Assert.False(Has(staff, "item_indesctructible"), "Indestructible stayed after of Ages left");
        Assert.True(Has(staff, "item_charged_skill"));
    }
}
