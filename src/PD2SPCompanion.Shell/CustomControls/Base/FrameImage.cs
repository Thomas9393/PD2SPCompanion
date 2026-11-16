using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// Frames of one size played in turn, drawn whole in the room given (as an Image's Uniform): the game's
/// burning logo in the landing's header (Imaging/FrontEndArt), which the editor once played from a GIF it
/// shipped. Played and paused as every <see cref="AnimatedFrames"/> is.
/// </summary>
public sealed class FrameImage : AnimatedFrames
{
    public static readonly StyledProperty<IReadOnlyList<Bitmap>?> FramesProperty =
        AvaloniaProperty.Register<FrameImage, IReadOnlyList<Bitmap>?>(nameof(Frames));

    public static readonly StyledProperty<double> RateProperty =
        AvaloniaProperty.Register<FrameImage, double>(nameof(Rate), 20);

    public IReadOnlyList<Bitmap>? Frames
    {
        get => GetValue(FramesProperty);
        set => SetValue(FramesProperty, value);
    }

    /// <summary>Frames a second.</summary>
    public double Rate
    {
        get => GetValue(RateProperty);
        set => SetValue(RateProperty, value);
    }

    protected override int FrameCount => Frames?.Count ?? 0;

    protected override double FramesPerSecond => Rate;

    static FrameImage()
    {
        AffectsMeasure<FrameImage>(FramesProperty);
        AffectsRender<FrameImage>(FramesProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FramesProperty || change.Property == RateProperty)
            Restart();
    }

    private Size FrameSize => Frames is { Count: > 0 } frames ? frames[0].Size : default;

    // The frame's shape kept, as large as the room allows.
    private Size Fit(Size room)
    {
        var frame = FrameSize;
        if (frame.Width <= 0 || frame.Height <= 0)
            return default;
        double scale = Math.Min(
            double.IsFinite(room.Width) ? room.Width / frame.Width : double.PositiveInfinity,
            double.IsFinite(room.Height) ? room.Height / frame.Height : double.PositiveInfinity);
        if (!double.IsFinite(scale))
            scale = 1;
        return new Size(frame.Width * scale, frame.Height * scale);
    }

    protected override Size MeasureOverride(Size availableSize) => Fit(availableSize);

    public override void Render(DrawingContext context)
    {
        if (Frames is not { Count: > 0 } frames)
            return;
        var size = Fit(Bounds.Size);
        var place = new Rect((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2, size.Width, size.Height);
        var frame = frames[Math.Min(Frame, frames.Count - 1)];
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            context.DrawImage(frame, new Rect(frame.Size), place);
    }
}
