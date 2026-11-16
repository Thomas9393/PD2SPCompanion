using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;

using PD2SPCompanion.Core;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The scale the whole window is drawn at, over what Windows already asks: the look of one main monitor, the best of them, replicated
/// on different DPI settings. The editor's layout is drawn for the room that monitor gives it - 2560 x 1440
/// at 125%, 2048 x 1104 of the window's units. A screen with less room starves it; scaled, the window is laid
/// out as if it had that room, and drawn smaller.
/// <para>
/// The Settings page's "UI scale": Automatic (<see cref="ScreenFit.Automatic"/>), or a step by hand. The window
/// holds the transform (<c>MainWindow.ApplyScale</c>); popups, tooltips and drop-downs take it from what they
/// open on (<c>Popup.InheritsTransform</c>, turned on for every popup in <c>App</c>).
/// </para>
/// </summary>
public static class UiScale
{
    /// <summary>What Settings offers, in percent; 0 is Automatic.</summary>
    public static IReadOnlyList<int> Choices { get; } = [0, 75, 80, 85, 90, 100, 110, 125];

    public static IReadOnlyList<string> ChoiceNames { get; } = [.. Choices.Select(percent => percent == 0 ? L("Automatic") : $"{percent}%")];

    /// <summary>A stored number as one of the choices: the nearest, and Automatic for none.</summary>
    public static int Nearest(int percent) => percent <= 0 ? 0 : Choices.Where(offered => offered > 0).MinBy(offered => Math.Abs(offered - percent));

    /// <summary>The setting as it stands: 0 for Automatic, else percent. Set by EditorSettings.Apply.</summary>
    public static int Percent { get; private set; }

    /// <summary>
    /// The scale the window is drawn at now (MainWindow.ApplyScale): what turns a length of the screen, in the
    /// window's units, into the layout's own - the room a tooltip has is the screen's over this.
    /// </summary>
    public static double Current { get; set; } = 1;

    /// <summary>The setting changed: the window scales again.</summary>
    public static event Action? Changed;

    public static void Set(int percent)
    {
        percent = Nearest(percent);
        if (percent == Percent)
            return;

        Percent = percent;
        Changed?.Invoke();
    }

    /// <summary>
    /// The scale for a screen: the step chosen, or Automatic's. <paramref name="room"/> is the screen's working
    /// area in the window's units (its pixels over <paramref name="scaling"/>, Windows' own scale there).
    /// </summary>
    public static double For(int percent, Size room, double scaling) =>
        Forced is { } forced ? forced / 100.0
        : percent > 0 ? percent / 100.0
        : ScreenFit.Automatic(room.Width, room.Height, scaling);

    /// <summary>
    /// <c>PD2SPC_UI_SCALE</c> (a percent, any from 50 to 200) says the scale instead of the setting: how a test
    /// draws the window at a scale without writing the player's settings.
    /// </summary>
    private static readonly int? Forced =
        int.TryParse(Environment.GetEnvironmentVariable("PD2SPC_UI_SCALE"), out int asked) && asked is >= 50 and <= 200 ? asked : null;
}
