namespace PD2SPCompanion.Tests.Items;

/// <summary>
/// What the Basic section's dice roll over: a stack between
/// the table's <c>minstack</c> and <c>maxstack</c>, and a current durability as a new item's lands - half the
/// maximum to one under it, as the cube's upgrade was measured to roll it.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class BasicRollTests(Game game)
{
    [GameFact]
    public void A_stack_rolls_between_the_tables_smallest_and_largest()
    {
        if (!game.Available)
            return;

        Assert.Equal(1, Stacks.MinimumFor("key"));
        Assert.Equal(50, Stacks.MaximumFor("key"));
        Assert.Equal(100, Stacks.MinimumFor("aqv"));
        Assert.Equal(350, Stacks.MaximumFor("aqv"));
        // Padded as a save pads it, and a base that does not stack.
        Assert.Equal(25, Stacks.MinimumFor("jav "));
        Assert.Null(Stacks.MinimumFor("rin"));
    }

    [GameTheory]
    [InlineData(36, 18, 35)]
    [InlineData(30, 15, 29)]
    [InlineData(3, 1, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 0, 0)]
    public void Durability_rolls_from_half_the_maximum_to_one_under_it(int most, int low, int high) =>
        Assert.Equal((low, high), ItemBases.DurabilityRoll(most));
}
