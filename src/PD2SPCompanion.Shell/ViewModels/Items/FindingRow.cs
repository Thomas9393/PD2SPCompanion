using Avalonia.Media;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// One thing the legitimacy check found, dressed for the panel.
/// <para>
/// Two colours, not three. Red is kept for what the game could not hold at all; a property
/// the game would not have rolled and one the editor cannot explain are both worth a glance
/// rather than an alarm, and share the caution colour with every other "allowed, but worth
/// knowing" mark on the page.
/// </para>
/// </summary>
public sealed record FindingRow(Finding Finding)
{
    public string Said => Finding.Said;

    public bool IsImpossible => Finding.Certainty is Certainty.Impossible;

    public string Kind => Finding.Certainty switch
    {
        Certainty.Impossible => "impossible",
        Certainty.Unrolled => L("not rolled"),
        _ => "unaccounted",
    };

    public IBrush Colour => Brush(IsImpossible ? "[Text] Danger" : "[Text] Warning");

    private static IBrush Brush(string key) =>
        Avalonia.Application.Current is { } app
        && app.TryGetResource(key, app.ActualThemeVariant, out object? found)
        && found is IBrush brush
            ? brush
            : Brushes.Gainsboro;
}
