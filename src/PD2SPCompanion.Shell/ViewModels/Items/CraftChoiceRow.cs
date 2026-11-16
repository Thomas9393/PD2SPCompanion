using System.Collections.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One craft family in the list, and whether it is the one chosen - the same shape the
/// corruption and affix lists use, so all three panels mark their choice the same way.
/// </summary>
public sealed record CraftChoiceRow(AffixRanges.CraftRecipe Recipe, bool IsHeld) : IPickRow
{
    public string Family => Recipe.Family;

    /// <summary>What the family grants, as ranges rather than as a roll.</summary>
    public IReadOnlyList<string> Lines => Crafts.Ranges(Recipe);
}
