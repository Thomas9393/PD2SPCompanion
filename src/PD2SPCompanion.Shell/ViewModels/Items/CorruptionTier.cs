using System.Collections.Generic;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One rarity group of corruptions, as the wiki lays them out: a heading, the chance of
/// landing anywhere in it, and the outcomes themselves.
/// </summary>
public sealed record CorruptionTier(
    string Name,
    string Chance,
    IReadOnlyList<CorruptionChoiceRow> Options);

/// <summary>
/// One corruption in the list, and whether it is the one already drafted - the same shape
/// the affix list uses, so both panels mark their current choice the same way.
/// </summary>
public sealed record CorruptionChoiceRow(Corruptions.CorruptionOption Option, bool IsHeld)
    : IPickRow
{
    public string Readable => Option.Readable;
    public string Chance => Option.Chance;
}

/// <summary>
/// One of the eight a desecration draws from, with its share of those eight - not of the
/// thousand a corruption is rolled out of.
/// </summary>
public sealed record DesecrationChoiceRow(Corruptions.CorruptionOption Option, bool IsHeld, string Chance)
    : IPickRow
{
    public string Readable => Option.Readable;
}
