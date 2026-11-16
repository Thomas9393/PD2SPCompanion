using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// Ctrl- and shift-clicking a button, which cannot be done the obvious way.
/// <para>
/// A <see cref="Button"/> marks <c>PointerPressed</c> handled in its own class handler, and
/// Avalonia puts class handlers ahead of instance handlers in the event route. So a handler
/// attached in XAML runs after the button has already acted, or does not run at all, and the
/// modifiers appear to do nothing. Catching the press on the way down is the only way to see
/// it first.
/// </para>
/// <para>
/// This has to go on the page rather than on the buttons: they generally live in a template
/// and do not exist when the page is constructed.
/// </para>
/// </summary>
public static class ModifierClick
{
    /// <summary>
    /// Calls <paramref name="act"/> when a button under this page is pressed with a modifier
    /// key held. Return true from it to consume the press, which stops the button's own
    /// command running as well - otherwise a ctrl-click would do both things.
    /// <para>
    /// A plain click never arrives here, so the button's <c>Command</c> stays the one place
    /// the ordinary case is written.
    /// </para>
    /// </summary>
    public static void OnModifiedPress(this InputElement page, Func<Button, KeyModifiers, bool> act)
    {
        page.AddHandler(
            InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) =>
            {
                if (e.KeyModifiers == KeyModifiers.None)
                    return;

                if (e.Source is not Visual source)
                    return;

                var button = source as Button ?? source.FindAncestorOfType<Button>();
                // A disabled button's plain click does nothing, so its modified one must not
                // either.
                if (button is null || !button.IsEffectivelyEnabled)
                    return;

                if (act(button, e.KeyModifiers))
                    e.Handled = true;
            },
            RoutingStrategies.Tunnel);
    }
}
