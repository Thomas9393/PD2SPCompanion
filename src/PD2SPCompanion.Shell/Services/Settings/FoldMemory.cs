using System;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// Every section that folds, as the user left it, kept as a setting: the pages' sections, the Edit page's, the presets'. A fold is named by the property that holds
/// it - with its owner's name before it where two share a name - and kept in the settings the moment it changes.
/// <para>
/// A static, as the tooltips' actions are: the folds belong to a dozen view models, most of which are made
/// without the settings. Before the settings are read, and in a test, every section is as it was made.
/// PD2SPC_FOLDS=off (the UI tests set it) reads none and keeps none, so a test meets the sections as a new
/// install has them whatever the player folded.
/// </para>
/// </summary>
public static class FoldMemory
{
    /// <summary>The settings the folds are kept in: set when they are read.</summary>
    public static EditorSettings? Settings { get; set; }

    private static readonly bool Off =
        string.Equals(Environment.GetEnvironmentVariable("PD2SPC_FOLDS"), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a section is unfolded: as it was left, or <paramref name="initially"/> where it never was folded or unfolded.</summary>
    public static bool IsOpen(string fold, bool initially = true) =>
        !Off && Settings is { } settings && settings.Folds.TryGetValue(fold, out bool open) ? open : initially;

    /// <summary>A section folded or unfolded: kept, and written at once.</summary>
    public static void Keep(string fold, bool open)
    {
        if (Off || Settings is not { } settings || (settings.Folds.TryGetValue(fold, out bool was) && was == open))
            return;

        settings.Folds[fold] = open;
        settings.Save();
    }

    /// <summary>
    /// A part drawn to a height of the user's - the item holder's lines on the Edit page and the Forge
    /// - as it was left, or <paramref name="initially"/>. Kept with the folds, by the same rules.
    /// </summary>
    public static int Size(string part, int initially = 0) =>
        !Off && Settings is { } settings && settings.Sizes.TryGetValue(part, out int size) ? size : initially;

    /// <summary>A part's height, kept, and written at once; the part's own height again forgets it.</summary>
    public static void KeepSize(string part, int size, int initially = 0)
    {
        if (Off || Settings is not { } settings)
            return;

        bool changed = size == initially
            ? settings.Sizes.Remove(part)
            : !(settings.Sizes.TryGetValue(part, out int was) && was == size);
        if (size != initially)
            settings.Sizes[part] = size;
        if (changed)
            settings.Save();
    }

}
