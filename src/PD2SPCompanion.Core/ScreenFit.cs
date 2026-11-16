namespace PD2SPCompanion.Core;

/// <summary>
/// The arithmetic of the editor's "UI scale", which replicates one monitor's look on a different DPI setting. Here,
/// apart from the window that applies it (the Shell's <c>UiScale</c>), so that it is tested.
/// </summary>
public static class ScreenFit
{
    /// <summary>
    /// The room the layout was drawn for, in the window's units: the reference monitor's working area -
    /// 2560 x 1440 at 125%, less its taskbar.
    /// </summary>
    public const double ReferenceWidth = 2048, ReferenceHeight = 1104;

    /// <summary>A grid's cell, in the window's units: what is kept on whole screen pixels.</summary>
    public const double Cell = 28;

    /// <summary>How far Automatic may go from what Windows asks, either way: a step, no more.</summary>
    public const double Least = 0.75, Most = 1.25;

    /// <summary>
    /// Automatic: the scale at which a screen gives the layout the reference's room, or the nearest under it.
    /// <paramref name="roomWidth"/> and <paramref name="roomHeight"/> are the screen's working area in the
    /// window's units - its pixels over <paramref name="scaling"/>, Windows' own scale there.
    /// <list type="bullet">
    /// <item>Left at 1 where the screen is nearly the reference already (within a twentieth under, an eighth
    /// over): a taskbar a little taller is no reason to redraw everything a hair smaller.</item>
    /// <item>Never further than a step from what Windows asks (<see cref="Least"/>, <see cref="Most"/>): the
    /// person set 150% on a small screen for a reason, and three quarters of it is as far as this undoes it.</item>
    /// <item>On a value that puts a grid's cell on whole screen pixels - <see cref="Cell"/> units times Windows'
    /// scale times this - so the grids' lines stay as sharp as a scaled window lets them.</item>
    /// </list>
    /// </summary>
    public static double Automatic(double roomWidth, double roomHeight, double scaling)
    {
        if (roomWidth <= 0 || roomHeight <= 0 || scaling <= 0)
            return 1;

        double fit = Math.Min(roomWidth / ReferenceWidth, roomHeight / ReferenceHeight);
        if (fit is >= 0.95 and <= 1.125)
            return 1;

        fit = Math.Clamp(fit, Least, Most);

        // The cell in screen pixels at this scale, brought down to a whole number - and up again where that
        // would pass under the least.
        double cell = Cell * scaling;
        double pixels = Math.Floor(cell * fit + 0.0001);
        if (pixels / cell < Least - 0.0001)
            pixels = Math.Ceiling(cell * Least - 0.0001);

        return pixels / cell;
    }
}
