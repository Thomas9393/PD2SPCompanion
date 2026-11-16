namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// A stat paid by the ethereal items worn: Purgatory's
/// <c>item_dmgpercent_pereth</c> is op 4 on <c>equipped_eth</c>, "+60% Enhanced Damage per equipped ethereal item",
/// into both damage percents. The sheet counted none of it - only stats paid by level were. A per-level stat is paid
/// as it was.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class PerEtherealTests(Game game)
{
    [GameFact]
    public void Purgatorys_enhanced_damage_is_paid_by_the_ethereal_items_worn()
    {
        if (!game.Available)
            return;

        var paid = Requirements.PaidByLevel("item_dmgpercent_pereth", 60, characterLevel: 90, etherealsWorn: 3).ToList();
        Assert.Contains(("item_maxdamage_percent", 180), paid);
        Assert.Contains(("item_mindamage_percent", 180), paid);
        Assert.DoesNotContain(Requirements.PaidByLevel("item_dmgpercent_pereth", 60, characterLevel: 90, etherealsWorn: 0), each => each.Value != 0);

        // Paid by level, as before: 8 strength per level >> 3, at level 80, is 80.
        Assert.Contains(("strength", 80), Requirements.PaidByLevel("item_strength_perlevel", 8, characterLevel: 80, etherealsWorn: 3));
    }
}
