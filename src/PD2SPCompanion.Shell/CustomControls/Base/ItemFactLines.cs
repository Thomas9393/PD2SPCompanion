using System;
using System.Collections.Generic;
using System.Collections.Specialized;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// An item's lines, drawn by one control. A line was six
/// controls and fourteen bindings - a presenter, a stack, the rule, a wrap panel, the words and the green
/// bracket - and a tooltip of twenty lines some hundred and fifty of them, built at every hover. Here the lines
/// are laid out and painted in turn, as <see cref="TileLayer"/> does a grid's tiles.
/// <para>
/// A line is drawn as its controls drew it: the words wrapped at the width there is, in the color of the line's
/// tone, with the game's asterisks after them - red for a corruption's, gold for a desecration's; the green
/// bracket beside the words, or under them where the two have no room in one row; a rule as a thin line across,
/// with air over and under it. Centered (<see cref="IsCentered"/>) as a tooltip has its lines.
/// </para>
/// <para>
/// The colors are styled properties, set where the tones' colors have always been said
/// (ItemWording.axaml): one vocabulary, in one place. The font and the plain color are the text's own,
/// inherited as a text block inherits them. UI Automation is told of a text a line and one a bracket, as it was
/// of the two text blocks (<see cref="ItemFactLinesPeer"/>).
/// </para>
/// </summary>
public sealed class ItemFactLines : Control
{
    public static readonly StyledProperty<IEnumerable<ItemFact>?> ItemsProperty =
        AvaloniaProperty.Register<ItemFactLines, IEnumerable<ItemFact>?>(nameof(Items));

    /// <summary>Each line in the middle of the width, as a tooltip's; off, at the left, as a card's.</summary>
    public static readonly StyledProperty<bool> IsCenteredProperty =
        AvaloniaProperty.Register<ItemFactLines, bool>(nameof(IsCentered));

    public static readonly StyledProperty<IBrush?> FactBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(FactBrush));
    public static readonly StyledProperty<IBrush?> MagicBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(MagicBrush));
    public static readonly StyledProperty<IBrush?> CorruptBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(CorruptBrush));
    public static readonly StyledProperty<IBrush?> DesecrateBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(DesecrateBrush));
    public static readonly StyledProperty<IBrush?> RunesBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(RunesBrush));
    public static readonly StyledProperty<IBrush?> SkinBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(SkinBrush));
    public static readonly StyledProperty<IBrush?> SetBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(SetBrush));
    public static readonly StyledProperty<IBrush?> WarningBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(WarningBrush));
    public static readonly StyledProperty<IBrush?> BracketBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(BracketBrush));
    public static readonly StyledProperty<IBrush?> RuleBrushProperty = AvaloniaProperty.Register<ItemFactLines, IBrush?>(nameof(RuleBrush));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner<ItemFactLines>();
    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<ItemFactLines>();
    public static readonly StyledProperty<FontStyle> FontStyleProperty = TextElement.FontStyleProperty.AddOwner<ItemFactLines>();
    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextElement.FontWeightProperty.AddOwner<ItemFactLines>();
    public static readonly StyledProperty<FontStretch> FontStretchProperty = TextElement.FontStretchProperty.AddOwner<ItemFactLines>();
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<ItemFactLines>();

    static ItemFactLines()
    {
        AffectsMeasure<ItemFactLines>(
            ItemsProperty, IsCenteredProperty,
            FactBrushProperty, MagicBrushProperty, CorruptBrushProperty, DesecrateBrushProperty, RunesBrushProperty,
            SkinBrushProperty, SetBrushProperty, WarningBrushProperty, BracketBrushProperty,
            FontFamilyProperty, FontSizeProperty, FontStyleProperty, FontWeightProperty, FontStretchProperty, ForegroundProperty);
        AffectsRender<ItemFactLines>(RuleBrushProperty);
    }

    public IEnumerable<ItemFact>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public bool IsCentered
    {
        get => GetValue(IsCenteredProperty);
        set => SetValue(IsCenteredProperty, value);
    }

    public IBrush? FactBrush { get => GetValue(FactBrushProperty); set => SetValue(FactBrushProperty, value); }
    public IBrush? MagicBrush { get => GetValue(MagicBrushProperty); set => SetValue(MagicBrushProperty, value); }
    public IBrush? CorruptBrush { get => GetValue(CorruptBrushProperty); set => SetValue(CorruptBrushProperty, value); }
    public IBrush? DesecrateBrush { get => GetValue(DesecrateBrushProperty); set => SetValue(DesecrateBrushProperty, value); }
    public IBrush? RunesBrush { get => GetValue(RunesBrushProperty); set => SetValue(RunesBrushProperty, value); }
    public IBrush? SkinBrush { get => GetValue(SkinBrushProperty); set => SetValue(SkinBrushProperty, value); }
    public IBrush? SetBrush { get => GetValue(SetBrushProperty); set => SetValue(SetBrushProperty, value); }
    public IBrush? WarningBrush { get => GetValue(WarningBrushProperty); set => SetValue(WarningBrushProperty, value); }
    public IBrush? BracketBrush { get => GetValue(BracketBrushProperty); set => SetValue(BracketBrushProperty, value); }
    public IBrush? RuleBrush { get => GetValue(RuleBrushProperty); set => SetValue(RuleBrushProperty, value); }

    /// <summary>The air over and under a rule, and its own thickness: the nine pixels its border took.</summary>
    private const double RuleAir = 4, RuleThick = 1;

    /// <summary>Between a line's words and its bracket.</summary>
    private const double BracketGap = 5;

    /// <summary>The faded grey of a rule: the tooltip's text color at four tenths.</summary>
    private const double RuleOpacity = 0.4;

    /// <summary>
    /// One line laid out: where it starts down the control, how much it takes, its words and its bracket - and
    /// where the bracket lies from the line's own corner. A rule has neither.
    /// </summary>
    private sealed record Row(ItemFact Fact, double Top, Size Size, TextLayout? Words, TextLayout? Bracket, Point BracketAt);

    private readonly List<Row> _rows = [];
    private double _laidFor = double.NaN;
    private INotifyCollectionChanged? _watched;

    /// <summary>The lines shown, in order - rules left out: what UI Automation is told of.</summary>
    internal IEnumerable<ItemFact> Words
    {
        get
        {
            foreach (var fact in Items ?? [])
            {
                if (fact.IsWords)
                    yield return fact;
            }
        }
    }

    /// <summary>Raised when the lines change: the automation peer's children are the lines.</summary>
    internal event Action? LinesChanged;

    /// <summary>A line's words as they are read: its text with the asterisks after it.</summary>
    internal static string Said(ItemFact fact) => fact.Text + fact.CorruptStar + fact.DesecrateStar;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            if (_watched is not null)
                _watched.CollectionChanged -= OnItemsChanged;

            _watched = change.NewValue as INotifyCollectionChanged;
            if (_watched is not null)
                _watched.CollectionChanged += OnItemsChanged;

            LinesChanged?.Invoke();
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateMeasure();
        LinesChanged?.Invoke();
    }

    protected override void OnMeasureInvalidated()
    {
        Forget();
        base.OnMeasureInvalidated();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Forget();
    }

    private void Forget()
    {
        foreach (var row in _rows)
        {
            row.Words?.Dispose();
            row.Bracket?.Dispose();
        }

        _rows.Clear();
        _laidFor = double.NaN;
    }

    private IBrush? BrushOf(ItemFact fact) => fact.Tone switch
    {
        ItemFact.Fact => FactBrush,
        ItemFact.Magic => MagicBrush,
        ItemFact.Corrupt => CorruptBrush,
        ItemFact.Desecrate => DesecrateBrush,
        ItemFact.Runes => RunesBrush,
        ItemFact.Skin => SkinBrush,
        ItemFact.SetBonus => SetBrush,
        ItemFact.Unmet or ItemFact.Warning => WarningBrush,
        _ => GetValue(ForegroundProperty),
    };

    private Size Whole(double width, double height) => DrawnText.Whole(this, width, height);

    private double Snapped(double value) => DrawnText.Snapped(this, value);

    /// <summary>The lines laid out for a width: built once for it, and kept until the lines or the text change.</summary>
    private Size Lay(double width)
    {
        if (_laidFor.Equals(width) && _rows.Count > 0)
            return Taken();

        Forget();
        _laidFor = width;

        var typeface = new Typeface(GetValue(FontFamilyProperty), GetValue(FontStyleProperty), GetValue(FontWeightProperty), GetValue(FontStretchProperty));
        double size = GetValue(FontSizeProperty);
        double top = 0;

        foreach (var fact in Items ?? [])
        {
            if (fact.IsRule)
            {
                var took = new Size(0, RuleAir + RuleThick + RuleAir);
                _rows.Add(new Row(fact, top, took, null, null, default));
                top += took.Height;
                continue;
            }

            var brush = BrushOf(fact);
            string said = Said(fact);

            // The asterisks in their own colors, as runs of the line: they never wrap away from it.
            List<ValueSpan<TextRunProperties>>? stars = null;
            int at = fact.Text.Length;
            if (fact.Corrupted)
                (stars ??= []).Add(new ValueSpan<TextRunProperties>(at++, 1, new GenericTextRunProperties(typeface, size, foregroundBrush: CorruptBrush)));
            if (fact.Desecrated)
                (stars ??= []).Add(new ValueSpan<TextRunProperties>(at, 1, new GenericTextRunProperties(typeface, size, foregroundBrush: DesecrateBrush)));

            var words = new TextLayout(said, typeface, size, brush, TextAlignment.Left, TextWrapping.Wrap,
                maxWidth: width, textStyleOverrides: stars);
            var wordsSize = Whole(words.WidthIncludingTrailingWhitespace, words.Height);

            TextLayout? bracket = null;
            Point bracketAt = default;
            var rowSize = wordsSize;
            if (fact.HasRange)
            {
                bracket = new TextLayout(fact.Range, typeface, size, BracketBrush, maxWidth: Math.Max(0, width - BracketGap));
                var bracketSize = Whole(bracket.WidthIncludingTrailingWhitespace + BracketGap, bracket.Height);

                // Beside the words where the row has room for both, else under them - as a wrap panel put it.
                if (wordsSize.Width + bracketSize.Width <= width)
                {
                    bracketAt = new Point(wordsSize.Width + BracketGap, 0);
                    rowSize = new Size(wordsSize.Width + bracketSize.Width, Math.Max(wordsSize.Height, bracketSize.Height));
                }
                else
                {
                    bracketAt = new Point(BracketGap, wordsSize.Height);
                    rowSize = new Size(Math.Max(wordsSize.Width, bracketSize.Width), wordsSize.Height + bracketSize.Height);
                }
            }

            _rows.Add(new Row(fact, top, rowSize, words, bracket, bracketAt));
            top += rowSize.Height;
        }

        return Taken();
    }

    private Size Taken()
    {
        double width = 0, height = 0;
        foreach (var row in _rows)
        {
            width = Math.Max(width, row.Size.Width);
            height = row.Top + row.Size.Height;
        }

        return new Size(width, height);
    }

    protected override Size MeasureOverride(Size availableSize) => Lay(availableSize.Width);

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Measured against the width there was, and given no less: the same lines. Given less - a host that
        // cut it - they are laid out again for what there is. But not for a pixel: at 125% a tooltip gives its
        // lines one device pixel under what they measured, its padding rounded to the screen's pixels, and laid
        // out again a bracket that just fitted went under its words - a row the tooltip had made no room for,
        // and its last line was cut off (a Necromancer's amulet did not show its FCR).
        // A line a pixel over draws over the edge, where nothing is cut.
        double pixel = 1 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        if (finalSize.Width < Taken().Width - pixel - 1e-6)
            Lay(finalSize.Width);

        return finalSize;
    }

    /// <summary>Where a line's own corner is across the control: the middle, or the left edge.</summary>
    private double LeftOf(Row row) => IsCentered ? Snapped((Bounds.Width - row.Size.Width) / 2) : 0;

    public override void Render(DrawingContext context)
    {
        foreach (var row in _rows)
        {
            if (row.Words is null)
            {
                if (RuleBrush is { } rule)
                {
                    using (context.PushOpacity(RuleOpacity))
                        context.FillRectangle(rule, new Rect(0, row.Top + RuleAir, Bounds.Width, RuleThick));
                }

                continue;
            }

            double left = LeftOf(row);
            row.Words.Draw(context, new Point(left, row.Top));
            row.Bracket?.Draw(context, new Point(left + row.BracketAt.X, row.Top + row.BracketAt.Y));
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ItemFactLinesPeer(this);
}

/// <summary>
/// What UI Automation is told of an item's lines, now that no line is a control (<see cref="ItemFactLines"/>):
/// a text a line - its words with the asterisks after them - and a text a bracket, in order, as the two text
/// blocks of a line were. The window tests read a tooltip by its texts.
/// </summary>
internal sealed class ItemFactLinesPeer : ControlAutomationPeer
{
    public ItemFactLinesPeer(ItemFactLines owner) : base(owner)
    {
        owner.LinesChanged += InvalidateChildren;
    }

    public new ItemFactLines Owner => (ItemFactLines)base.Owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override bool IsContentElementCore() => false;

    protected override bool IsControlElementCore() => true;

    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
    {
        var children = new List<AutomationPeer>();
        foreach (var fact in Owner.Words)
        {
            children.Add(new DrawnTextPeer(this, ItemFactLines.Said(fact)));
            if (fact.HasRange)
                children.Add(new DrawnTextPeer(this, fact.Range));
        }

        return children;
    }
}
