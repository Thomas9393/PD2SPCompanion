using System;
using System.Numerics;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;

using PD2SPCompanion.Shell.Helper;
using PD2SPCompanion.Shell.Services.Settings;

namespace PD2SPCompanion.Shell.Views;

/// <summary>
/// The splash: the editor's badge, name and version, and the step start-up is on (<see cref="Say"/>), over a
/// gold run sweeping along a track.
/// <para>
/// Start-up runs on the UI thread, so the line can change only between steps, and the sweep is the
/// compositor's: it keeps moving through a step the UI thread is busy with, as the game's own loading screen
/// does.
/// </para>
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>One pass of the run along the track.</summary>
    private static readonly TimeSpan Pass = TimeSpan.FromSeconds(1.2);

    public SplashWindow()
    {
        InitializeComponent();
        Version.Text = PD2SPCompanion.Core.AppVersion.Label;
        Opened += (_, _) => Win11Frame.Apply(this, Win11Frame.Gold);
        Run.AttachedToVisualTree += (_, _) => Sweep();
    }

    /// <summary>
    /// The step start-up is on, drawn before it begins: the returned task ends once a frame with the new
    /// line has been put on screen - or after a short wait, should no frame come (a splash not shown yet).
    /// </summary>
    public Task Say(string step)
    {
        Status.Text = step;
        var drawn = new TaskCompletionSource();
        // The frame's callback runs as it is laid out; what is posted from it runs after it is drawn.
        RequestAnimationFrame(_ => Dispatcher.UIThread.Post(() => drawn.TrySetResult(), DispatcherPriority.Background));
        return Task.WhenAny(drawn.Task, Task.Delay(100));
    }

    /// <summary>The run slid along the track, over and over, by the compositor; held still where animations are off.</summary>
    private void Sweep()
    {
        if (Motion.Off || ElementComposition.GetElementVisual(Run) is not { } visual)
        {
            Run.IsVisible = !Motion.Off;
            return;
        }

        double from = -Run.Width, to = Track.Width;
        var slide = visual.Compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3((float)from, 0, 0));
        slide.InsertKeyFrame(1f, new Vector3((float)to, 0, 0));
        slide.Duration = Pass;
        slide.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Offset", slide);
    }
}
