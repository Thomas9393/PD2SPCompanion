using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PD2SPCompanion.Shell.Imaging;
using PD2SPCompanion.Shell.Services.Settings;
using System;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A character's figure in its gear (<see cref="FigureFrames"/>), played as the select screen plays it:
/// every character at one scale (<see cref="BodyRoom"/> game pixels to the control's height), its body
/// centred, what it holds reaching past. Played and paused as every <see cref="AnimatedFrames"/> is.
/// </summary>
public sealed class FigureImage : AnimatedFrames
{
    public static readonly StyledProperty<FigureFrames?> FigureProperty =
        AvaloniaProperty.Register<FigureImage, FigureFrames?>(nameof(Figure));

    public FigureFrames? Figure
    {
        get => GetValue(FigureProperty);
        set => SetValue(FigureProperty, value);
    }

    protected override int FrameCount => Figure?.Frames.Count ?? 0;

    protected override double FramesPerSecond => Figure?.FramesPerSecond ?? 8;

    public static readonly StyledProperty<double> MostProperty =
        AvaloniaProperty.Register<FigureImage, double>(nameof(Most), MostScale);

    /// <summary>
    /// The most a game pixel is drawn across here: <see cref="MostScale"/> unless the view says otherwise -
    /// the opened card does, up to <see cref="OpenedScale"/>.
    /// </summary>
    public double Most
    {
        get => GetValue(MostProperty);
        set => SetValue(MostProperty, value);
    }

    static FigureImage()
    {
        AffectsMeasure<FigureImage>(FigureProperty, MostProperty);
        AffectsRender<FigureImage>(FigureProperty, MostProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FigureProperty)
        {
            // A figure arriving on a card already on screen fades in; one there from the start, or a
            // figure replaced by another (an edit to the gear), simply shows.
            if (change.OldValue is null && change.NewValue is not null && VisualRoot is not null && !Motion.Off)
                FadeIn();
            Restart();
        }
    }

    private System.Threading.CancellationTokenSource? _fading;

    // A quarter of a second from nothing: the card was blank while the figure was drawn (CharacterCard.AwaitsFigure).
    private void FadeIn()
    {
        _fading?.Cancel();
        _fading = new System.Threading.CancellationTokenSource();
        var fade = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(250),
            Easing = new Avalonia.Animation.Easings.QuadraticEaseOut(),
            FillMode = Avalonia.Animation.FillMode.Forward,
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0.0) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1.0) } },
            },
        };
        _ = fade.RunAsync(this, _fading.Token);
    }

    /// <summary>
    /// The body height, in the game's pixels, that fills the control's height: one scale for every
    /// character, whatever it holds (a spear shrank the one holding it). A bare
    /// Barbarian is 78 tall, an Amazon in a horned helm 87; the rest is headroom.
    /// </summary>
    public const double BodyRoom = 96;

    /// <summary>
    /// The most a game pixel is drawn across: the landing page's large view filled its height at five times
    /// and more, and the pixels showed through any smoothing. The game's
    /// own select screen draws a character at about 2.4 times on a 1440-line screen, and so does this (three
    /// was still a bit large). The cards (about 1.2 and 1.8) are under it.
    /// </summary>
    public const double MostScale = 2.4;

    /// <summary>
    /// The most on the opened card, where the figure stands in its town (TownBackdrop): one and a half times
    /// the rest (of the figure and the town together; 1.75 first, then this) - with
    /// ground under it the figure is a part of a scene, not a picture alone. Where the card has the height
    /// for it: the view says how much.
    /// </summary>
    public const double OpenedScale = MostScale * 1.5;

    private double Scale(Size room) =>
        double.IsFinite(room.Height) && room.Height > 0 ? Math.Min(room.Height / BodyRoom, Most) : 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Figure is not { } figure)
            return default;
        // No taller than the capped figure: a row sized to its content - the landing page's large view, the
        // figure centred with the name below it - is the figure's height and no more.
        double scale = Scale(availableSize);
        double height = Math.Min(availableSize.Height, BodyRoom * Most);
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : figure.Width * scale;
        return new Size(width, height);
    }

    /// <summary>An image to UI Automation, named by <c>AutomationProperties.Name</c> - the tests find it so.</summary>
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(FigureImage owner) : Avalonia.Automation.Peers.ControlAutomationPeer(owner)
    {
        protected override Avalonia.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            Avalonia.Automation.Peers.AutomationControlType.Image;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }

    // The body centred in the control, top to bottom and across its anchor; a weapon or a helm's horns
    // that reach past the control are drawn there all the same, as the game's spear reaches its frame.
    // It stood on the bottom edge first, and the room a body does not fill all gathered above its head
    // (a bit too low). The frames are enlarged already, so only smoothed down.
    public override void Render(DrawingContext context)
    {
        if (Figure is not { } figure || figure.Frames.Count == 0)
            return;
        double scale = Scale(Bounds.Size);
        double body = (figure.BodyBottom - figure.BodyTop) * scale;
        double feetX = Bounds.Width / 2, feetY = (Bounds.Height + body) / 2 - figure.BodyBottom * scale;
        var place = new Rect(feetX - figure.AnchorX * scale, feetY - figure.AnchorY * scale,
                             figure.Width * scale, figure.Height * scale);
        Bitmap frame = figure.Frames[Math.Min(Frame, figure.Frames.Count - 1)];
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            context.DrawImage(frame, new Rect(frame.Size), place);
    }
}
