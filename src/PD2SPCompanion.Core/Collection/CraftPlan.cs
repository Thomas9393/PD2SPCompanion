using D2SLib.Model.Save;

using PD2SPCompanion.Core.Items;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// What can be crafted from what is on hand: the crafting wizard, a hint beside the rune wizard - how many can
/// be built, by materials and bases.
/// <para>
/// A recipe takes a base and, with it, a jewel, a rune and a perfect gem - or the family's craft infusion alone
/// in their place, an alternative that counts in. The materials are the shared stash's materials tab's; a jewel
/// is counted from the tab's Jewel Fragments, which stand for it there. The bases are handed in - the wizard
/// hands in what lies in the vault's Crafting Bases folder and nothing else. Each recipe is counted by itself:
/// two recipes that want the same rune each say what that rune would make of them alone.
/// </para>
/// </summary>
public static class CraftPlan
{
    /// <summary>The item the materials tab keeps in a jewel's stead.</summary>
    private const string JewelFragments = "jewf";

    /// <summary>One thing a recipe takes: how many of it, and how many the materials tab holds.</summary>
    public sealed record Need(string Code, int Needed, int Have)
    {
        public bool IsMet => Have >= Needed;
    }

    /// <summary>A recipe, with what there is for it.</summary>
    /// <param name="Needs">The jewel, the rune and the gem.</param>
    /// <param name="Infusion">The craft infusion that alone takes their place, where the family has one.</param>
    /// <param name="Bases">How many of the bases handed in the recipe takes.</param>
    public sealed record Cell(AffixRanges.CraftCard Card, IReadOnlyList<Need> Needs, Need? Infusion, int Bases)
    {
        /// <summary>How many times the jewel, rune and gem are all there.</summary>
        public int ByIngredients => Needs.Count == 0 ? 0 : Needs.Min(need => need.Have / need.Needed);

        /// <summary>How many the materials would make, with bases to spare: by the ingredients, and one more an infusion.</summary>
        public int ByMaterials => ByIngredients + (Infusion?.Have ?? 0);

        /// <summary>How many can be built: the materials and the bases both there.</summary>
        public int Makeable => Math.Min(ByMaterials, Bases);
    }

    /// <summary>
    /// The slot of the materials tab an ingredient is counted from, or null where the tab keeps none of it:
    /// its own, or - a jewel - the Jewel Fragments'.
    /// </summary>
    public static int? SlotOf(string code) =>
        Materials.SlotFor(code)
        ?? (string.Equals(ItemTypes.TypeOf(code), "jewl", StringComparison.OrdinalIgnoreCase) ? Materials.SlotFor(JewelFragments) : null);

    /// <summary>Whether a recipe takes this base: a magic or rare item of the recipe's type, as the cube matches it.</summary>
    public static bool Takes(AffixRanges.CraftCard card, Item item) =>
        SmartSort.CanBeCraftingBase(item)
        && ItemTypes.LineageOfBase(item.Code).Contains(card.Type, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every recipe - one a family and type, as <see cref="AffixRanges.CraftCards"/> has them - counted against
    /// the materials tab (<paramref name="stock"/>: a slot's count) and the bases.
    /// </summary>
    public static IReadOnlyList<Cell> Plan(Func<int, int> stock, IEnumerable<Item> bases)
    {
        // How many bases each type has: a base's lineage says which types of recipe take it.
        var perType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in bases.Where(SmartSort.CanBeCraftingBase))
        {
            foreach (string step in ItemTypes.LineageOfBase(item.Code).Distinct(StringComparer.OrdinalIgnoreCase))
                perType[step] = perType.GetValueOrDefault(step) + 1;
        }

        int Held(string code) => SlotOf(code) is { } slot ? stock(slot) : 0;

        return
        [
            .. AffixRanges.CraftCards().Select(card => new Cell(
                card,
                [.. card.Ingredients
                    .GroupBy(code => code, StringComparer.OrdinalIgnoreCase)
                    .Select(same => new Need(same.Key, same.Count(), Held(same.Key)))],
                card.Infusion is { } infusion ? new Need(infusion, 1, Held(infusion)) : null,
                perType.GetValueOrDefault(card.Type))),
        ];
    }
}
