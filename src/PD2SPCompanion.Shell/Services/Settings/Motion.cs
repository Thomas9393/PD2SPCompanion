using System.Linq;
using System.Runtime.CompilerServices;
using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// The Animations setting, for the whole app at once: off, nothing fades, slides, turns or plays.
/// <para>
/// One switch rather than a condition on each of the fifteen transitions and the figures, which a
/// new page would forget. While it is off, any control given transitions - by its own markup or
/// by a style - has them taken away as it is given them, and a page's cross-fade with them;
/// switching it off takes them from everything already on screen, and switching it back on gives
/// them back. The figures hold their first frame (FigureImage); the editor has no GIFs since it ships no
/// game art.
/// </para>
/// <para>
/// A page's cross-fade is swapped for <see cref="Instant"/>, never taken away: the page switcher
/// keeps two presenters and leaves it to the transition to show the new one and hide the old, and
/// a cross-fade that has run holds the old one's opacity at nought until another fade lifts it.
/// With no transition at all, or one that only set visibility, every other page came up blank.
/// </para>
/// </summary>
public static class Motion
{
    private static readonly ConditionalWeakTable<Animatable, Transitions> KeptTransitions = new();
    private static readonly ConditionalWeakTable<TransitioningContentControl, IPageTransition> KeptPages = new();
    private static readonly ConditionalWeakTable<PD2SPCompanion.Shell.CustomControls.Base.PageDeck, IPageTransition> KeptDecks = new();

    /// <summary>
    /// The page switch while animations are off: the same cross-fade, over a millisecond, so it
    /// shows, hides and lifts opacity exactly as the animated one does.
    /// </summary>
    private static readonly CrossFade Instant = new(TimeSpan.FromMilliseconds(1));

    private static bool _installed;

    public static bool Off { get; private set; }

    /// <summary>
    /// The window not in use: another app in front - the game, most often - or minimised. Nothing plays
    /// meanwhile: the landing page's two GIFs and a figure kept a slower PC's core busy while idle (a
    /// laptop on battery, measured). Set by the main window as it is deactivated and activated.
    /// </summary>
    public static bool Asleep { get; private set; }

    public static void SetAsleep(bool asleep)
    {
        if (Asleep == asleep)
            return;

        Asleep = asleep;
        PD2SPCompanion.Shell.CustomControls.Base.AnimatedFrames.PauseHidden();
    }

    public static void Set(bool off)
    {
        Install();

        if (Off == off)
            return;

        Off = off;
        PD2SPCompanion.Shell.CustomControls.Base.AnimatedFrames.PauseHidden();

        foreach (var visual in EveryVisual())
        {
            if (off)
                Still(visual);
            else
                Restore(visual);
        }
    }

    /// <summary>
    /// Once: the watch on every control's transitions, which catches a control made while
    /// animations are off - a page opened for the first time, a panel, a tooltip.
    /// </summary>
    private static void Install()
    {
        if (_installed)
            return;
        _installed = true;

        Animatable.TransitionsProperty.Changed.AddClassHandler<Animatable>((animatable, change) =>
        {
            if (Off && change.NewValue is Transitions given)
            {
                KeptTransitions.AddOrUpdate(animatable, given);
                animatable.Transitions = null;
            }
        });

        PD2SPCompanion.Shell.CustomControls.Base.PageDeck.PageTransitionProperty.Changed.AddClassHandler<PD2SPCompanion.Shell.CustomControls.Base.PageDeck>((deck, change) =>
        {
            if (Off && change.NewValue is IPageTransition given && !ReferenceEquals(given, Instant))
            {
                KeptDecks.AddOrUpdate(deck, given);
                deck.PageTransition = Instant;
            }
        });

        TransitioningContentControl.PageTransitionProperty.Changed.AddClassHandler<TransitioningContentControl>((control, change) =>
        {
            if (Off && change.NewValue is IPageTransition given && !ReferenceEquals(given, Instant))
            {
                KeptPages.AddOrUpdate(control, given);
                control.PageTransition = Instant;
            }
        });
    }

    private static void Still(Visual visual)
    {
        if (visual is Animatable { Transitions: { } transitions } animatable)
        {
            KeptTransitions.AddOrUpdate(animatable, transitions);
            animatable.Transitions = null;
        }

        if (visual is TransitioningContentControl { PageTransition: { } page } control && !ReferenceEquals(page, Instant))
        {
            KeptPages.AddOrUpdate(control, page);
            control.PageTransition = Instant;
        }

        if (visual is PD2SPCompanion.Shell.CustomControls.Base.PageDeck { PageTransition: { } fade } deck && !ReferenceEquals(fade, Instant))
        {
            KeptDecks.AddOrUpdate(deck, fade);
            deck.PageTransition = Instant;
        }
    }

    private static void Restore(Visual visual)
    {
        if (visual is Animatable animatable && KeptTransitions.TryGetValue(animatable, out var transitions))
        {
            animatable.Transitions = transitions;
            KeptTransitions.Remove(animatable);
        }

        if (visual is TransitioningContentControl control && KeptPages.TryGetValue(control, out var page))
        {
            control.PageTransition = page;
            KeptPages.Remove(control);
        }

        if (visual is PD2SPCompanion.Shell.CustomControls.Base.PageDeck deck && KeptDecks.TryGetValue(deck, out var fade))
        {
            deck.PageTransition = fade;
            KeptDecks.Remove(deck);
        }
    }

    private static System.Collections.Generic.IEnumerable<Visual> EveryVisual() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.SelectMany(window => window.GetVisualDescendants().Prepend(window))
            : [];
}
