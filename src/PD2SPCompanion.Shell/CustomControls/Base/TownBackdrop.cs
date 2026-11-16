using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using PD2SPCompanion.Shell.Imaging;
using System;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// The town a character card's figure stands in (Core's TownScene): drawn under a <see cref="FigureImage"/> in
/// the same box, at the figure's own scale, the place to stand - the picture's middle - under the figure's
/// feet. It asks for no room of its own and reaches past its box on every side, as the figure's spear does,
/// out to the card that clips it - and darkens toward that card's rim, so the scene ends in the card's black
/// rather than at a cut edge, at whatever scale it is drawn.
/// </summary>
public sealed class TownBackdrop : Control
{
    public static readonly StyledProperty<Bitmap?> PictureProperty =
        AvaloniaProperty.Register<TownBackdrop, Bitmap?>(nameof(Picture));

    /// <summary>The backdrop, in the game's own pixels (Imaging/TownBitmaps).</summary>
    public Bitmap? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    public static readonly StyledProperty<FigureFrames?> FigureProperty =
        AvaloniaProperty.Register<TownBackdrop, FigureFrames?>(nameof(Figure));

    /// <summary>
    /// The figure standing on it, the one the <see cref="FigureImage"/> over this draws: its feet say where
    /// the ground is, and its shadow is cast there. None, and the ground is laid where a figure of
    /// <see cref="BodyHeight"/> would stand, with no shadow.
    /// </summary>
    public FigureFrames? Figure
    {
        get => GetValue(FigureProperty);
        set => SetValue(FigureProperty, value);
    }

    public static readonly StyledProperty<double> MostProperty = FigureImage.MostProperty.AddOwner<TownBackdrop>();

    /// <summary>The most a game pixel is drawn across: the figure's own (<see cref="FigureImage.Most"/>), set alike.</summary>
    public double Most
    {
        get => GetValue(MostProperty);
        set => SetValue(MostProperty, value);
    }

    static TownBackdrop() => AffectsRender<TownBackdrop>(PictureProperty, FigureProperty, MostProperty);

    /// <summary>
    /// A standing figure's height in the game's pixels, feet to crown, where no figure says its own: the
    /// figure is centred in its box top to bottom (FigureImage.Render), so its feet are half of this under
    /// the box's middle. A bare Barbarian is 78, an Amazon in a horned helm 87.
    /// </summary>
    private const double BodyHeight = 82;

    /// <summary>
    /// The shadow under the feet, in the game's pixels: across, deep, how far right of the feet its middle
    /// lies and how far above the body's lowest pixel - the toes. Not the game's - that one is the figure
    /// itself laid flat - only enough that the figure stands on the ground (without
    /// one it floated).
    /// </summary>
    private const double ShadowWidth = 54, ShadowHeight = 21, ShadowAside = 5, ShadowUp = 5;

    private static readonly IBrush Shadow = new RadialGradientBrush
    {
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0xD0, 0, 0, 0), 0),
            new GradientStop(Color.FromArgb(0xB0, 0, 0, 0), 0.5),
            new GradientStop(Color.FromArgb(0, 0, 0, 0), 1),
        },
    }.ToImmutable();

    /// <summary>
    /// Over the town, across the whole card: clear at its middle, black at its corners, most of the way there
    /// at the middle of each side.
    /// </summary>
    private static readonly IBrush Rim = new RadialGradientBrush
    {
        RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55),
            new GradientStop(Color.FromArgb(0x50, 0, 0, 0), 0.72),
            new GradientStop(Color.FromArgb(0xC0, 0, 0, 0), 0.9),
            new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 1),
        },
    }.ToImmutable();

    protected override Size MeasureOverride(Size availableSize) => default;

    /// <summary>What of this the card shows: the nearest thing around it that clips, in this control's own space.</summary>
    private Rect Seen()
    {
        foreach (var around in this.GetVisualAncestors())
        {
            if (around is Control { ClipToBounds: true } card && card.TransformToVisual(this) is { } here)
                return new Rect(card.Bounds.Size).TransformToAABB(here);
        }

        return new Rect(Bounds.Size);
    }

    public override void Render(DrawingContext context)
    {
        if (Picture is not { } picture || Bounds.Height <= 0)
            return;

        // The figure's scale, as FigureImage works it out for the same box.
        double scale = Math.Min(Bounds.Height / FigureImage.BodyRoom, Most);
        double width = picture.PixelSize.Width * scale, height = picture.PixelSize.Height * scale;

        // The figure's own ground point, as FigureImage places it: the body centred, the anchor where it falls.
        double feetX = Bounds.Width / 2;
        double feetY = Figure is { } figure
            ? (Bounds.Height + (figure.BodyBottom - figure.BodyTop) * scale) / 2 - figure.BodyBottom * scale
            : Bounds.Height / 2 + BodyHeight / 2 * scale;

        var seen = Seen();
        using (context.PushClip(seen))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            context.DrawImage(picture, new Rect(picture.Size), new Rect(feetX - width / 2, feetY - height / 2, width, height));
        context.DrawRectangle(Rim, null, seen.Inflate(1));

        if (Figure is { } standing)
        {
            double toes = (Bounds.Height + (standing.BodyBottom - standing.BodyTop) * scale) / 2;
            context.DrawEllipse(Shadow, null, new Point(feetX + ShadowAside * scale, toes - ShadowUp * scale),
                                ShadowWidth / 2 * scale, ShadowHeight / 2 * scale);
        }
    }
}
