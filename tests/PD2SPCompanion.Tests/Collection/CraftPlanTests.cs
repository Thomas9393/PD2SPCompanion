using D2SLib.Model.Save;

namespace PD2SPCompanion.Tests.Collection;

/// <summary>
/// What can be crafted from what is on hand (CraftPlan: the crafting wizard's count is
/// by how many can be built, so materials and bases): a recipe takes a base and, with it, a jewel, a rune and a
/// perfect gem - or the family's infusion alone in their place, which counts in. The materials are the
/// materials tab's, a jewel counted from its Jewel Fragments; the bases are the ones handed in.
/// On the installed tables and a real character's items.
/// </summary>
[Collection(nameof(GameTables))]
public sealed class CraftPlanTests(Game game)
{
    /// <summary>The corpus character's items a craft recipe takes: its magic and rare rings among them.</summary>
    private static List<Item> Bases()
    {
        var (save, _) = SaveVerifier.Load(Path.Combine(Game.Corpus, "saves", "PhySpearZon.d2s"));
        return [.. save.PlayerItemList!.Items.Where(SmartSort.CanBeCraftingBase)];
    }

    private static CraftPlan.Cell Of(IReadOnlyList<CraftPlan.Cell> cells, string family, string type) =>
        cells.Single(cell => cell.Card.Family.Equals(family, StringComparison.OrdinalIgnoreCase)
                             && cell.Card.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    [CorpusFact]
    public void Every_recipe_takes_a_jewel_a_rune_and_a_gem_or_its_familys_infusion()
    {
        if (!game.Available)
            return;

        var cells = CraftPlan.Plan(_ => 0, []);

        Assert.Equal(GrailCatalog.TypeOrder.Count * GrailCatalog.FamilyOrder.Count, cells.Count);
        Assert.All(cells, cell =>
        {
            Assert.Equal(3, cell.Needs.Count);
            Assert.All(cell.Needs, need => Assert.Equal(1, need.Needed));
            Assert.NotNull(cell.Infusion);

            // Each of them has its place in the materials tab: the jewel the Jewel Fragments'.
            Assert.All(cell.Needs, need => Assert.NotNull(CraftPlan.SlotOf(need.Code)));
            Assert.NotNull(CraftPlan.SlotOf(cell.Infusion!.Code));
            Assert.Equal(0, cell.Makeable);
        });

        Assert.Equal(Materials.SlotFor("jewf"), CraftPlan.SlotOf("jew"));
    }

    [CorpusFact]
    public void A_recipe_is_counted_by_its_materials_and_its_bases_together_the_infusion_counting_in()
    {
        if (!game.Available)
            return;

        var bases = Bases();
        int rings = bases.Count(item => ItemTypes.LineageOfBase(item.Code).Contains("ring", StringComparer.OrdinalIgnoreCase));
        Assert.True(rings >= 2, $"the corpus character has {rings} rings a recipe takes");

        var blood = Of(CraftPlan.Plan(_ => 0, bases), "blood", "ring");
        Assert.Equal(rings, blood.Bases);
        Assert.Equal(0, blood.Makeable);

        // Materials, by the slots the recipe itself names.
        var held = new Dictionary<int, int>();
        Func<int, int> stock = slot => held.GetValueOrDefault(slot);
        IReadOnlyList<CraftPlan.Cell> Counted(IEnumerable<Item> with) => CraftPlan.Plan(stock, with);

        // Two of each but the gem, of which one: one by the ingredients.
        foreach (var need in blood.Needs)
            held[CraftPlan.SlotOf(need.Code)!.Value] = 2;
        held[CraftPlan.SlotOf(blood.Needs[^1].Code)!.Value] = 1;
        Assert.Equal(1, Of(Counted(bases), "blood", "ring").ByIngredients);
        Assert.Equal(1, Of(Counted(bases), "blood", "ring").Makeable);

        // An infusion alone stands for the three: each is one more.
        held[CraftPlan.SlotOf(blood.Infusion!.Code)!.Value] = 50;
        var rich = Of(Counted(bases), "blood", "ring");
        Assert.Equal(51, rich.ByMaterials);
        Assert.Equal(rings, rich.Makeable);

        // With no jewel and no rune the infusions still build: as many as there are bases.
        held[CraftPlan.SlotOf(blood.Needs[0].Code)!.Value] = 0;
        Assert.Equal(0, Of(Counted(bases), "blood", "ring").ByIngredients);
        Assert.Equal(rings, Of(Counted(bases), "blood", "ring").Makeable);

        // And with no base, nothing, whatever the materials.
        Assert.Equal(0, Of(Counted([]), "blood", "ring").Makeable);

        // Each recipe is counted by itself: the rings are no amulet's bases.
        Assert.Equal(0, Of(Counted(bases.Where(item => ItemTypes.LineageOfBase(item.Code).Contains("ring", StringComparer.OrdinalIgnoreCase))), "blood", "amul").Bases);
    }
}
