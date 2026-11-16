using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PD2SPCompanion.Shell.Services.Settings;
using System;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// Frames played in turn, at a pace, only where they can be seen: still on the first frame while hidden
/// (a page behind another, PageDeck), while the window is not in use, or with animations off
/// (<see cref="Motion"/>). What the frames are and how they are drawn is the control's own - a character's
/// figure (<see cref="FigureImage"/>), the game's burning logo (<see cref="FrameImage"/>).
/// </summary>
public abstract class AnimatedFrames : Control
{
    public static readonly StyledProperty<bool> AnimateProperty =
        AvaloniaProperty.Register<AnimatedFrames, bool>(nameof(Animate), true);

    /// <summary>Whether it plays at all; the landing's list plays only its selected character.</summary>
    public bool Animate
    {
        get => GetValue(AnimateProperty);
        set => SetValue(AnimateProperty, value);
    }

    /// <summary>The frame shown now.</summary>
    protected int Frame { get; private set; }

    /// <summary>How many frames there are to play; one or none plays nothing.</summary>
    protected abstract int FrameCount { get; }

    /// <summary>How fast they play.</summary>
    protected abstract double FramesPerSecond { get; }

    private DispatcherTimer? _timer;

    private static readonly System.Collections.Generic.HashSet<AnimatedFrames> Attached = [];

    /// <summary>Everything in the window playing where it can be seen and still where not (PageDeck, Motion).</summary>
    public static void PauseHidden()
    {
        foreach (var played in Attached)
            played.Pace();
    }

    static AnimatedFrames() =>
        IsVisibleProperty.Changed.AddClassHandler<Visual>((_, _) => PauseHiddenSoon());

    /// <summary>
    /// Hidden or shown by a panel around it, not only by its page: any visibility change in the window
    /// paces everything again, once, after it settles (Avalonia keeps its own event for it internal).
    /// </summary>
    private static bool _paceQueued;

    private static void PauseHiddenSoon()
    {
        if (_paceQueued || Attached.Count == 0)
            return;
        _paceQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _paceQueued = false;
            PauseHidden();
        }, DispatcherPriority.Background);
    }

    /// <summary>New frames: from the first, paced again.</summary>
    protected void Restart()
    {
        Frame = 0;
        Pace();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AnimateProperty || change.Property == IsVisibleProperty)
            Pace();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Attached.Add(this);
        Pace();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Attached.Remove(this);
        _timer?.Stop();
    }

    // Runs the timer only while there is something to play and somewhere to see it.
    private void Pace()
    {
        // Effectively: a page hidden behind another (PageDeck) is not somewhere to see it.
        // Nor are animations off, or the window asleep (Motion): the timer stops rather than ticking for nothing.
        bool playing = FrameCount > 1 && Animate && IsEffectivelyVisible && VisualRoot is not null
                       && !Motion.Off && !Motion.Asleep;
        if (!playing)
        {
            _timer?.Stop();
            if (Frame != 0)
            {
                Frame = 0;
                InvalidateVisual();
            }
            return;
        }

        // Left alone where it already runs at that pace: setting a timer's interval starts its count again, and any
        // visibility change in the window paces every animation - a sweep over a grid's tiles, which shows and hides
        // their tooltip anchors, held the figure and the logo back a frame at each tile.
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(1 / 8.0), DispatcherPriority.Render, (_, _) => Step());
        var pace = TimeSpan.FromSeconds(1 / Math.Clamp(FramesPerSecond, 1, 25));
        if (_timer.Interval != pace)
            _timer.Interval = pace;
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    private void Step()
    {
        if (FrameCount == 0 || Motion.Off)
            return;
        Frame = (Frame + 1) % FrameCount;
        InvalidateVisual();
    }
}
