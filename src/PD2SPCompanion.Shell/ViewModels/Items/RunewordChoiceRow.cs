using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One runeword in the builder's list, and whether it is the one chosen - the shape the craft
/// list has, so the two panels mark their choice alike.
/// </summary>
public sealed record RunewordChoiceRow(Runewords.Word Word, bool IsHeld) : IPickRow
{
    public string Name => Runewords.DisplayName(Word);

    /// <summary>Its runes in socket order: "Tal Ort Thul".</summary>
    public string Runes => string.Join(" ", Word.Runes.Select(Runewords.RuneName));

    /// <summary>What the word grants, as ranges rather than as a roll - worded as a craft family's are.</summary>
    public IReadOnlyList<string> Lines =>
        Crafts.Ranges(new AffixRanges.CraftRecipe(Name,
            [.. Word.Mods.Select(mod => new AffixRanges.CraftGrant(mod.Code, mod.Min, mod.Max, mod.Param))]));
}
