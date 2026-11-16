using PD2SPCompanion.Core.Collection;

using System;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// How well a find answers what was typed in the window's search box, for the order of its short list, fast and
/// precise: a name that starts with it, then a name that has it,
/// then a base that has it, then a find by a stat. The box lists a few of each group, so which few matters -
/// the first by name were whatever the alphabet put first.
/// </summary>
public static class FindRank
{
    /// <summary>The name starts with what was typed.</summary>
    public const int Starts = 0;

    /// <summary>The name has it, or answers its pattern.</summary>
    public const int Named = 1;

    /// <summary>The base has it.</summary>
    public const int Based = 2;

    /// <summary>Neither: found by a stat's line, or by what is in its sockets.</summary>
    public const int Other = 3;

    /// <summary>The rank of a find by its name and - where it is known - its base, for <paramref name="typed"/>.</summary>
    public static Func<string, string?, int> For(string typed)
    {
        string? pattern = SearchPattern.Of(typed);
        return (name, based) => Of(typed, pattern, name, based);
    }

    private static int Of(string typed, string? pattern, string name, string? based)
    {
        if (name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            return Starts;
        if (name.Contains(typed, StringComparison.OrdinalIgnoreCase) || SearchPattern.Matches(pattern, name))
            return Named;
        if (based is { Length: > 0 } && (based.Contains(typed, StringComparison.OrdinalIgnoreCase) || SearchPattern.Matches(pattern, based)))
            return Based;

        return Other;
    }
}
