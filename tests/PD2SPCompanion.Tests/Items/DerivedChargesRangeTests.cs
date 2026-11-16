using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// A charged row written negative - a staff "of Teleportation", <c>charged 54 -30 -6</c> - gives the charges its item
/// level works out (52 at item level 99, 48 at 95), and its
/// range is those, not the row's 30. Held to 30, the Edit page would mark a staff "?" and say "affix 30".
/// </summary>
[Collection(nameof(GameTables))]
public sealed class DerivedChargesRangeTests(Game game)
{
    private static RollRange ChargesOf(int itemLevel)
    {
        var staff = ItemForge.BaseAs("6bs", ItemQuality.Magic, "103", new Random(5)).Item;
        staff.ItemLevel = (byte)itemLevel;
        Array.Clear(staff.MagicPrefixIds!);
        Array.Clear(staff.MagicSuffixIds!);
        staff.MagicSuffixIds![0] = 532;
        return AffixRanges.For(staff).Single(range => range.Key.StartsWith("item_charged_skill", StringComparison.Ordinal)).Value;
    }

    [GameFact]
    public void A_staff_of_Teleportation_is_held_to_the_charges_its_item_level_gives()
    {
        if (!game.Available)
            return;

        Assert.Equal((52, 52), (ChargesOf(99).Min, ChargesOf(99).Max));
        Assert.Equal((48, 48), (ChargesOf(95).Min, ChargesOf(95).Max));
    }
}
