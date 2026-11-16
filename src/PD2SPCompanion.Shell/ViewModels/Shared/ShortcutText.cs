namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// A tooltip's keyboard shortcut, said one way everywhere (inline, as Windows menus say it):
/// "Duplicate selected item (Ctrl + V)" - the key in brackets after the words, on the tooltip's first line; any line
/// under it (a requirement, a warning) stays as it was. The keys are written as the steppers' modifier lines write
/// theirs, "Ctrl + LMB": spaced, so the two read alike.
/// </summary>
public static class ShortcutText
{
    public static string With(string what, string key) => $"{what} ({key})";

    /// <summary>
    /// A dimmed control's tooltip: what it is, and under it why not - one short line, as a refusal
    /// is worded ("Require open character."). No colon in the reason: a "Keys: what" line draws as key caps.
    /// </summary>
    public static string Dimmed(string what, string? why) => string.IsNullOrEmpty(why) ? what : what + System.Environment.NewLine + why;
}
