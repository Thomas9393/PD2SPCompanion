using D2SLib.Model.Save;
using PD2SPCompanion.Core.Characters;

namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// A Rogue's other hand, as the game has it (no bolts with a bow, nor the reverse):
/// a quiver goes beside what it feeds, and a weapon put on over the other's quiver takes that quiver off.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class MercenaryWeaponTests(Game game)
{
    private const int Rogue = 1;
    private static readonly string[] Weapons = ["bow"];

    private static Item Base(string code) => PD2SPCompanion.Tests.Items.AffixSweepTests.Fresh(code);

    private static string? Quiver(string quiver, string weapon) =>
        Equipping.CannotWearMercenary(Base(quiver), ItemLocation.LeftHand, Rogue, Weapons,
            at => at == ItemLocation.RightHand ? Base(weapon) : null);

    [GameTheory]
    [InlineData("aqv", 350)]
    [InlineData("cqv", 250)]
    public void A_forged_quiver_is_made_full_whatever_its_quality(string code, int full)
    {
        if (!game.Available)
            return;

        foreach (var quality in new[] { ItemQuality.Normal, ItemQuality.Magic, ItemQuality.Rare })
            Assert.Equal(full, PD2SPCompanion.Core.Items.ItemForge.BaseAs(code, quality, "105", new Random(3)).Item.Quantity);
    }

    [GameFact]
    public void A_quiver_goes_beside_what_it_feeds_and_a_weapon_over_the_other_quiver_takes_it_off()
    {
        if (!game.Available)
            return;

        Assert.Null(Quiver("aqv", "sbw"));
        Assert.Null(Quiver("cqv", "lxb"));
        Assert.NotNull(Quiver("cqv", "sbw"));
        Assert.NotNull(Quiver("aqv", "lxb"));

        Item? Worn(ItemLocation at, string code) => at == ItemLocation.LeftHand ? Base(code) : null;
        Assert.Equal(ItemLocation.LeftHand, Equipping.AlsoComesOff(Base("lxb"), ItemLocation.RightHand, null, at => Worn(at, "aqv")));
        Assert.Equal(ItemLocation.LeftHand, Equipping.AlsoComesOff(Base("sbw"), ItemLocation.RightHand, null, at => Worn(at, "cqv")));
        Assert.Equal(ItemLocation.None, Equipping.AlsoComesOff(Base("sbw"), ItemLocation.RightHand, null, at => Worn(at, "aqv")));
    }
}
