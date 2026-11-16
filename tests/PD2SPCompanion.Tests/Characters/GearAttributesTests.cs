namespace PD2SPCompanion.Tests.Characters;

/// <summary>
/// What gear's stats come to for the four attributes (Requirements.AttributesFrom): an
/// attribute shown as its own points and what the gear adds, "30 + 20": each of the four summed by the save's
/// name, a per-level stat as value x level / 8 under the attribute it is paid into, and nothing else counted.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class GearAttributesTests(Game game)
{
    [GameFact]
    public void The_four_attributes_are_summed_and_a_per_level_stat_is_counted_at_the_level()
    {
        if (!game.Available)
            return;

        var sums = Requirements.AttributesFrom(
            [
                ("strength", 20, 0), ("strength", 5, 0), ("vitality", 15, 0), ("energy", 3, 0),

                // An eighth a level: 8 x 40 / 8.
                ("item_strength_perlevel", 8, 0),

                // Not an attribute, whatever it gives.
                ("maxhp", 40, 0), ("item_allskills", 2, 0),
            ],
            characterLevel: 40);

        Assert.Equal(20 + 5 + 40, sums["strength"]);
        Assert.Equal(0, sums["dexterity"]);
        Assert.Equal(15, sums["vitality"]);
        Assert.Equal(3, sums["energy"]);
        Assert.Equal(4, sums.Count);
    }

    [GameFact]
    public void No_gear_adds_nothing_to_any_of_the_four()
    {
        if (!game.Available)
            return;

        var sums = Requirements.AttributesFrom([], characterLevel: 99);
        Assert.Equal(AttributeEditor.Editable.Order(), sums.Keys.Order());
        Assert.All(sums.Values, value => Assert.Equal(0, value));
    }
}
