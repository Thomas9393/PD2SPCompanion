using Avalonia.Threading;

using System;

namespace PD2SPCompanion.Shell.ViewModels;

/// <summary>
/// Keeps something in the layout while it fades out, and puts it there at once when it is shown.
/// <para>
/// A panel that fades has two states, not one: whether it is <i>shown</i> - what its opacity
/// follows - and whether it is <i>present</i> - what its IsVisible follows. Hidden at once, it
/// would vanish before its fade could be seen; so present stays true until the fade has had its
/// time, and is asked again then, because one shown again inside the fade must not be taken
/// away.
/// </para>
/// <para>
/// Written first as the item panel's own <c>KeepPresent</c> and copied into four more view
/// models; it is this one now.
/// </para>
/// </summary>
public static class Presence
{
    /// <summary>As long as a picker's veil takes to fade: <c>Border.veil</c> in Picker.axaml.</summary>
    public static readonly TimeSpan VeilFade = TimeSpan.FromMilliseconds(160);

    public static void Keep(bool shown, Func<bool> stillShown, Action<bool> present) =>
        Keep(shown, stillShown, present, VeilFade);

    public static void Keep(bool shown, Func<bool> stillShown, Action<bool> present, TimeSpan fade)
    {
        if (shown)
        {
            present(true);
            return;
        }

        DispatcherTimer.RunOnce(
            () =>
            {
                if (!stillShown())
                    present(false);
            },
            fade);
    }
}
