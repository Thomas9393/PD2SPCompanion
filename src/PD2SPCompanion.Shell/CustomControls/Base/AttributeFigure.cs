using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Rendering;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// An attribute's figure on the Character page: its own points, and after them what the gear adds - "25 +54".
/// Drawn by one control, because where each part stands is arithmetic no panel does.
/// The rule, settled in a fifth round over drafts:
/// <list type="bullet">
/// <item><b>Two kinds of row, each sized among its own</b>: the rows the gear adds nothing to, and the rows
/// it adds to. Rows of both kinds stand on one page.</item>
/// <item><b>A row is as wide as the longest figure of its kind</b>, and no wider: four rows of two digits
/// are two digits wide; one of them at 120, all are three. Shorter points stand against the longest's right
/// end, so the digits are in a column. (Not a width fixed at three digits, which was built first: "Current
/// result still leaves a lot of gaps".) The steppers therefore move when the longest figure gains a digit.</item>
/// <item><b>With the gear's part</b>, the same for the whole figure: the longest points, the sign, the longest
/// part. The signs stand in a line down the rows and each part's digits run from its sign (the pick of two drafts).</item>
/// <item><b>One baseline</b>. The sign is in the body's font - the titles' font draws a plus as an ornate
/// cross that read as an asterisk - and a line of another font is of another height: as texts side by side,
/// each centered, the sign sat low and the blue digits off the points' line. Here every part is put on the
/// points' baseline, whatever the fonts are.</item>
/// </list>
/// What the other rows of its kind show is handed in (<see cref="PointsOfAll"/>, <see cref="GearOfAll"/>): the
/// widths are measured here, in the figure's own font, which the player can change.
/// Tried and turned down, do not go back: the points and the gear's part in two columns shared by all four
/// rows; each row's figure centered whole in a room the rows shared; rooms for the gear's part on every row
/// once any row had one; rooms of a fixed three digits.
/// </summary>
public sealed class AttributeFigure : Control, ICustomHitTest
{
    public static readonly StyledProperty<string?> PointsProperty =
        AvaloniaProperty.Register<AttributeFigure, string?>(nameof(Points));

    /// <summary>"+" or "-"; empty where the gear adds nothing, and the figure is then the points alone.</summary>
    public static readonly StyledProperty<string?> GearSignProperty =
        AvaloniaProperty.Register<AttributeFigure, string?>(nameof(GearSign));

    public static readonly StyledProperty<string?> GearNumberProperty =
        AvaloniaProperty.Register<AttributeFigure, string?>(nameof(GearNumber));

    /// <summary>The points of every row of this row's kind, its own among them: the longest sets the width.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> PointsOfAllProperty =
        AvaloniaProperty.Register<AttributeFigure, IReadOnlyList<string>?>(nameof(PointsOfAll));

    /// <summary>The gear's part on every row that has one, as <see cref="PointsOfAll"/>.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> GearOfAllProperty =
        AvaloniaProperty.Register<AttributeFigure, IReadOnlyList<string>?>(nameof(GearOfAll));

    public static readonly StyledProperty<IBrush?> GearBrushProperty =
        AvaloniaProperty.Register<AttributeFigure, IBrush?>(nameof(GearBrush));

    /// <summary>The sign's font: the body's.</summary>
    public static readonly StyledProperty<FontFamily> SignFontProperty =
        AvaloniaProperty.Register<AttributeFigure, FontFamily>(nameof(SignFont), FontFamily.Default);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner<AttributeFigure>();
    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<AttributeFigure>();
    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextElement.FontWeightProperty.AddOwner<AttributeFigure>();
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<AttributeFigure>();

    static AttributeFigure()
    {
        AffectsMeasure<AttributeFigure>(
            PointsProperty, GearSignProperty, GearNumberProperty, PointsOfAllProperty, GearOfAllProperty, SignFontProperty,
            FontFamilyProperty, FontSizeProperty, FontWeightProperty);
        AffectsRender<AttributeFigure>(
            PointsProperty, GearSignProperty, GearNumberProperty, PointsOfAllProperty, GearOfAllProperty,
            GearBrushProperty, ForegroundProperty);
    }

    public string? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public string? GearSign { get => GetValue(GearSignProperty); set => SetValue(GearSignProperty, value); }
    public string? GearNumber { get => GetValue(GearNumberProperty); set => SetValue(GearNumberProperty, value); }
    public IReadOnlyList<string>? PointsOfAll { get => GetValue(PointsOfAllProperty); set => SetValue(PointsOfAllProperty, value); }
    public IReadOnlyList<string>? GearOfAll { get => GetValue(GearOfAllProperty); set => SetValue(GearOfAllProperty, value); }
    public IBrush? GearBrush { get => GetValue(GearBrushProperty); set => SetValue(GearBrushProperty, value); }
    public FontFamily SignFont { get => GetValue(SignFontProperty); set => SetValue(SignFontProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>Between the points and the gear's sign, and between the sign and its digits.</summary>
    private const double Gap = 6, SignGap = 1;

    /// <summary>Whether the gear adds something: the longer of the two kinds of figure.</summary>
    public bool HasGear => GearSign is { Length: > 0 } && GearNumber is { Length: > 0 };

    /// <summary>What the figure reads as: "25 +54", or "30".</summary>
    public string Said => HasGear ? $"{Points} {GearSign}{GearNumber}" : Points ?? "";

    private TextLayout Text(string text, FontFamily family, IBrush? brush) =>
        new(text, new Typeface(family, FontStyle.Normal, FontWeight), FontSize, brush);

    /// <summary>The width of the longest of some texts in the figure's font, a text of its own among them.</summary>
    private double Longest(string? own, IReadOnlyList<string>? all)
    {
        double longest = 0;
        foreach (string text in all ?? [])
            longest = Math.Max(longest, Width(text));

        return Math.Ceiling(Math.Max(longest, Width(own ?? "")));

        double Width(string text)
        {
            using var layout = Text(text, FontFamily, null);
            return layout.WidthIncludingTrailingWhitespace;
        }
    }

    /// <summary>The room the points have: the longest points of the rows of this kind.</summary>
    private double Room() => Longest(Points, PointsOfAll);

    private double SignWidth()
    {
        using var sign = Text("+", SignFont, null);
        return Math.Ceiling(sign.WidthIncludingTrailingWhitespace);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        using var line = Text("0", FontFamily, null);
        double width = Room();
        if (HasGear)
            width += Gap + SignWidth() + SignGap + Longest(GearNumber, GearOfAll);

        return new Size(width, Math.Ceiling(line.Height));
    }

    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public override void Render(DrawingContext context)
    {
        double room = Room();

        // The line every part stands on: the points', in the middle of the height.
        using var points = Text(Points ?? "", FontFamily, Foreground);
        double baseline = Math.Round((Bounds.Height - points.Height) / 2) + points.Baseline;

        // The points end where the longest points of their kind end.
        points.Draw(context, new Point(room - points.WidthIncludingTrailingWhitespace, baseline - points.Baseline));
        if (!HasGear)
            return;

        // The sign at one place down the rows, its digits running from it.
        using var sign = Text(GearSign!, SignFont, GearBrush);
        double at = room + Gap;
        sign.Draw(context, new Point(at, baseline - sign.Baseline));

        using var number = Text(GearNumber!, FontFamily, GearBrush);
        number.Draw(context, new Point(at + SignWidth() + SignGap, baseline - number.Baseline));
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FigurePeer(this);

    /// <summary>The figure as a text, read whole: what its text blocks said between them.</summary>
    private sealed class FigurePeer(AttributeFigure owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(TextBlock);

        protected override string? GetNameCore() => owner.Said;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
