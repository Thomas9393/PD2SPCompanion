using System;
using System.Collections.Generic;
using System.ComponentModel;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

using PD2SPCompanion.Shell.ViewModels;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// The head of a property's row on the Edit page, drawn by one control: its name as the game words it and, under
/// it, the dim line - the id and what the file keeps, the per-level note, what the affixes and the game would
/// roll, and the marks: a retired stat's note, "(corrupted)", "(runeword)", "(maps)", "(set)" and the set's
/// pieces. They were eighteen controls of a row's fifty-nine - two stacks, a wrap panel and fifteen text blocks,
/// most of them never shown - built for every row at every item selected, and let go at the next.
/// <para>
/// Drawn as the controls drew it: the name wrapped at the width there is; the line's parts side by side, a
/// part that has no room going whole to the next line, as the wrap panel put them; every text struck through
/// on a row being taken off. The one part still a control is the "?" of a questioned roll - seldom there, and a
/// tooltip is a control's: made when the row has one, and placed where the line has it.
/// </para>
/// <para>
/// The colors and the two sizes are styled properties, set beside the classes that said them
/// (VP_View_Inv_R.axaml). UI Automation is told of a text each part, as it was of the text blocks
/// (<see cref="StatRowHeadPeer"/>).
/// </para>
/// </summary>
public sealed class StatRowHead : Control
{
    public static readonly StyledProperty<StatRowViewModel?> RowProperty =
        AvaloniaProperty.Register<StatRowHead, StatRowViewModel?>(nameof(Row));

    /// <summary>Whether the play mode edits properties: what a roll would be is said only where it can be acted on.</summary>
    public static readonly StyledProperty<bool> EditsPropertiesProperty =
        AvaloniaProperty.Register<StatRowHead, bool>(nameof(EditsProperties));

    public static readonly StyledProperty<IBrush?> NameBrushProperty = AvaloniaProperty.Register<StatRowHead, IBrush?>(nameof(NameBrush));
    public static readonly StyledProperty<IBrush?> DimBrushProperty = AvaloniaProperty.Register<StatRowHead, IBrush?>(nameof(DimBrush));
    public static readonly StyledProperty<IBrush?> WarningBrushProperty = AvaloniaProperty.Register<StatRowHead, IBrush?>(nameof(WarningBrush));
    public static readonly StyledProperty<IBrush?> CorruptBrushProperty = AvaloniaProperty.Register<StatRowHead, IBrush?>(nameof(CorruptBrush));
    public static readonly StyledProperty<IBrush?> MarkBrushProperty = AvaloniaProperty.Register<StatRowHead, IBrush?>(nameof(MarkBrush));
    public static readonly StyledProperty<IBrush?> SetBrushProperty = AvaloniaProperty.Register<StatRowHead, IBrush?>(nameof(SetBrush));

    public static readonly StyledProperty<double> NameSizeProperty = AvaloniaProperty.Register<StatRowHead, double>(nameof(NameSize), 14);
    public static readonly StyledProperty<double> SmallSizeProperty = AvaloniaProperty.Register<StatRowHead, double>(nameof(SmallSize), 12);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner<StatRowHead>();
    public static readonly StyledProperty<FontStyle> FontStyleProperty = TextElement.FontStyleProperty.AddOwner<StatRowHead>();
    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextElement.FontWeightProperty.AddOwner<StatRowHead>();
    public static readonly StyledProperty<FontStretch> FontStretchProperty = TextElement.FontStretchProperty.AddOwner<StatRowHead>();

    static StatRowHead()
    {
        AffectsMeasure<StatRowHead>(
            RowProperty, EditsPropertiesProperty,
            NameBrushProperty, DimBrushProperty, WarningBrushProperty, CorruptBrushProperty, MarkBrushProperty, SetBrushProperty,
            NameSizeProperty, SmallSizeProperty,
            FontFamilyProperty, FontStyleProperty, FontWeightProperty, FontStretchProperty);
    }

    public StatRowViewModel? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    public bool EditsProperties { get => GetValue(EditsPropertiesProperty); set => SetValue(EditsPropertiesProperty, value); }

    public IBrush? NameBrush { get => GetValue(NameBrushProperty); set => SetValue(NameBrushProperty, value); }
    public IBrush? DimBrush { get => GetValue(DimBrushProperty); set => SetValue(DimBrushProperty, value); }
    public IBrush? WarningBrush { get => GetValue(WarningBrushProperty); set => SetValue(WarningBrushProperty, value); }
    public IBrush? CorruptBrush { get => GetValue(CorruptBrushProperty); set => SetValue(CorruptBrushProperty, value); }
    public IBrush? MarkBrush { get => GetValue(MarkBrushProperty); set => SetValue(MarkBrushProperty, value); }
    public IBrush? SetBrush { get => GetValue(SetBrushProperty); set => SetValue(SetBrushProperty, value); }
    public double NameSize { get => GetValue(NameSizeProperty); set => SetValue(NameSizeProperty, value); }
    public double SmallSize { get => GetValue(SmallSizeProperty); set => SetValue(SmallSizeProperty, value); }

    /// <summary>
    /// The air after the id's part and the per-level note, after what a roll would be, and between two marks: the
    /// margins and the spacing their controls had. A margin is a whole number of device pixels, as the layout
    /// made it, and a mark stands at the nearest one - at 125% six is seven and a half, and a half pixel is a
    /// blurred word.
    /// </summary>
    private const double PartGap = 4, RollGap = 6, MarkGap = 6;

    /// <summary>What of a row is drawn here: a change of any of these lays the head out again.</summary>
    private static readonly HashSet<string> Drawn =
    [
        nameof(StatRowViewModel.InGame), nameof(StatRowViewModel.HasInGame), nameof(StatRowViewModel.Name),
        nameof(StatRowViewModel.Detail), nameof(StatRowViewModel.IsPerLevel), nameof(StatRowViewModel.PerLevelNote),
        nameof(StatRowViewModel.HasRoll), nameof(StatRowViewModel.RollText), nameof(StatRowViewModel.IsOutsideRoll),
        nameof(StatRowViewModel.IsQuestionable), nameof(StatRowViewModel.ConcernsText),
        nameof(StatRowViewModel.HasStatNote), nameof(StatRowViewModel.StatNote),
        nameof(StatRowViewModel.IsCorrupted), nameof(StatRowViewModel.CorruptionMark),
        nameof(StatRowViewModel.IsRuneword), nameof(StatRowViewModel.IsMapOnly),
        nameof(StatRowViewModel.IsSetBonus), nameof(StatRowViewModel.SetPieces),
        nameof(StatRowViewModel.IsDeleted),
    ];

    /// <summary>One text laid out, and where it is drawn from the head's own corner.</summary>
    private sealed record Piece(TextLayout Layout, Point At);

    private readonly List<Piece> _pieces = [];
    private readonly List<string> _said = [];
    private string _saidLast = "";
    private Size _taken;
    private double _laidFor = double.NaN;
    private StatRowViewModel? _watched;

    /// <summary>The "?" of a questioned roll, while the row has one, and where the line has it.</summary>
    private Border? _question;
    private Rect _questionAt;

    /// <summary>The texts drawn, in order: what UI Automation is told of.</summary>
    internal IReadOnlyList<string> Said => _said;

    /// <summary>Raised when the texts change: the automation peer's children are the texts.</summary>
    internal event Action? TextsChanged;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowProperty)
            Watch(this.IsAttachedToVisualTree() ? Row : null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(Row);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Watch(null);
        Forget();
    }

    /// <summary>The row listened to while the head is on screen, and no longer: a head let go holds no row, and no row a head.</summary>
    private void Watch(StatRowViewModel? row)
    {
        if (ReferenceEquals(row, _watched))
            return;

        if (_watched is not null)
            _watched.PropertyChanged -= OnRowChanged;

        _watched = row;
        if (_watched is not null)
            _watched.PropertyChanged += OnRowChanged;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not { Length: > 0 } name || Drawn.Contains(name))
            InvalidateMeasure();
    }

    protected override void OnMeasureInvalidated()
    {
        Forget();
        base.OnMeasureInvalidated();
    }

    private void Forget()
    {
        foreach (var piece in _pieces)
            piece.Layout.Dispose();

        _pieces.Clear();
        _laidFor = double.NaN;
    }

    /// <summary>The "?" made, kept or let go, as the row has a questioned roll to show or none.</summary>
    private Border? Question(bool shown, string why)
    {
        if (!shown)
        {
            if (_question is { } gone)
            {
                _question = null;
                VisualChildren.Remove(gone);
                LogicalChildren.Remove(gone);
            }

            return null;
        }

        if (_question is null)
        {
            // Border.question and its text are styled where they always were (VP_View_Inv_R.axaml).
            _question = new Border { Child = new TextBlock() };
            _question.Classes.Add("question");
            LogicalChildren.Add(_question);
            VisualChildren.Add(_question);
        }

        ToolTip.SetTip(_question, why);
        return _question;
    }

    /// <summary>A part of the dim line: its texts side by side, the air between two of them and after the last, and the "?" among them.</summary>
    private sealed class Part
    {
        public readonly List<(TextLayout? Text, Size Size, double X)> Items = [];
        public double Width, Height, After;
        public int QuestionAt = -1;
    }

    /// <summary>The head laid out for a width: built once for it, and kept until the row or the text changes.</summary>
    private Size Lay(double width)
    {
        if (_laidFor.Equals(width) && _pieces.Count > 0)
            return _taken;

        Forget();
        _laidFor = width;
        _said.Clear();

        if (Row is not { } row)
        {
            Question(false, "");
            SayTexts();
            return _taken = default;
        }

        var family = GetValue(FontFamilyProperty);
        var plain = new Typeface(family, GetValue(FontStyleProperty), GetValue(FontWeightProperty), GetValue(FontStretchProperty));
        var bold = new Typeface(family, GetValue(FontStyleProperty), FontWeight.Bold, GetValue(FontStretchProperty));
        var struck = row.IsDeleted ? TextDecorations.Strikethrough : null;
        double small = SmallSize;

        TextLayout Text(string text, IBrush? brush, Typeface? face = null)
        {
            _said.Add(text);
            return new TextLayout(text, face ?? plain, small, brush, textDecorations: struck);
        }

        Size SizeOf(TextLayout text) => DrawnText.Whole(this, text.WidthIncludingTrailingWhitespace, text.Height);

        // The name: as the game words it, else the stat's own.
        string named = row.HasInGame ? row.InGame : row.Name;
        _said.Add(named);
        var name = new TextLayout(named, plain, NameSize, NameBrush, TextAlignment.Left, TextWrapping.Wrap,
            textDecorations: struck, maxWidth: width);
        var nameSize = DrawnText.Whole(this, name.WidthIncludingTrailingWhitespace, name.Height);
        _pieces.Add(new Piece(name, default));

        // The dim line's parts, in the order they stood in.
        var parts = new List<Part>();

        Part Of(double after, double between, params (TextLayout? Text, Size Size)[] items)
        {
            var part = new Part { After = after };
            double x = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0)
                    x += between;
                if (items[i].Text is null)
                    part.QuestionAt = i;
                part.Items.Add((items[i].Text, items[i].Size, x));
                x += items[i].Size.Width;
                part.Height = Math.Max(part.Height, items[i].Size.Height);
            }

            part.Width = DrawnText.Whole(this, x, 0).Width + after;
            parts.Add(part);
            return part;
        }

        (TextLayout? Text, Size Size) Sized(TextLayout text) => (text, SizeOf(text));

        double partGap = DrawnText.Snapped(this, PartGap), rollGap = DrawnText.Snapped(this, RollGap);
        Of(partGap, 0, Sized(Text(row.Detail, DimBrush)));

        if (row.IsPerLevel)
            Of(partGap, 0, Sized(Text($"· {row.PerLevelNote}", DimBrush)));

        // What a roll would be - where the play mode edits properties, and the row has a roll to say.
        var question = Question(EditsProperties && row.HasRoll && row.IsQuestionable, row.ConcernsText ?? "");
        if (EditsProperties)
        {
            if (row.HasRoll)
            {
                var items = new List<(TextLayout? Text, Size Size)> { Sized(Text("· ", DimBrush)) };
                if (question is not null)
                {
                    question.Measure(Size.Infinity);
                    items.Add((null, question.DesiredSize));
                }

                items.Add(Sized(row.IsOutsideRoll ? Text(row.RollText, WarningBrush, bold) : Text(row.RollText, DimBrush)));
                Of(rollGap, 0, [.. items]);
            }
            else
            {
                Of(rollGap, 0);
            }
        }

        var marks = new List<(TextLayout? Text, Size Size)>();
        if (row.HasStatNote)
            marks.Add(Sized(Text(row.StatNote, WarningBrush)));
        if (row.IsCorrupted)
            marks.Add(Sized(Text(row.CorruptionMark, CorruptBrush)));
        if (row.IsRuneword)
            marks.Add(Sized(Text(L("(runeword)"), MarkBrush)));
        if (row.IsMapOnly)
            marks.Add(Sized(Text(L("(maps)"), MarkBrush)));
        if (row.IsSetBonus)
        {
            marks.Add(Sized(Text(L("(set)"), SetBrush)));
            marks.Add(Sized(Text(row.SetPieces, SetBrush)));
        }

        Of(0, MarkGap, [.. marks]);

        // Side by side, and whole to the next line where one has no room - as the wrap panel put them.
        double top = nameSize.Height, across = 0, lineHeight = 0, widest = nameSize.Width;
        var line = new List<(Part Part, double X)>();
        _questionAt = default;

        void EndLine()
        {
            foreach (var (part, x) in line)
            {
                for (int i = 0; i < part.Items.Count; i++)
                {
                    var (text, size, at) = part.Items[i];
                    double left = x + DrawnText.Snapped(this, at);
                    if (text is not null)
                        _pieces.Add(new Piece(text, new Point(left, top)));
                    else
                        _questionAt = new Rect(left, top + DrawnText.Snapped(this, (lineHeight - size.Height) / 2), size.Width, size.Height);
                }
            }

            widest = Math.Max(widest, across);
            top += lineHeight;
            across = lineHeight = 0;
            line.Clear();
        }

        foreach (var part in parts)
        {
            if (line.Count > 0 && across + part.Width > width + 1e-6)
                EndLine();

            line.Add((part, across));
            across += part.Width;
            lineHeight = Math.Max(lineHeight, part.Height);
        }

        EndLine();

        SayTexts();
        return _taken = new Size(Math.Min(widest, width), top);
    }

    /// <summary>UI Automation told that the texts are others - where they are: a head laid out again for a new width says what it said.</summary>
    private void SayTexts()
    {
        string said = string.Join('\n', _said);
        if (said == _saidLast)
            return;

        _saidLast = said;
        TextsChanged?.Invoke();
    }

    protected override Size MeasureOverride(Size availableSize) => Lay(availableSize.Width);

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Measured against the width there was, and given no less: the same lines. Given less, they are laid out
        // again for what there is - but not for a pixel (ItemFactLines.ArrangeOverride has why).
        double pixel = 1 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        if (finalSize.Width < _taken.Width - pixel - 1e-6)
            Lay(finalSize.Width);

        _question?.Arrange(_questionAt);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        foreach (var piece in _pieces)
            piece.Layout.Draw(context, piece.At);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StatRowHeadPeer(this);
}

/// <summary>
/// What UI Automation is told of a property row's head, now that its texts are no controls
/// (<see cref="StatRowHead"/>): a text each - the name, the dim line's parts, the marks - in order, as the text
/// blocks were, after the "?" where the row has one. The window tests find a row by its name and read its line.
/// </summary>
internal sealed class StatRowHeadPeer : ControlAutomationPeer
{
    public StatRowHeadPeer(StatRowHead owner) : base(owner)
    {
        owner.TextsChanged += InvalidateChildren;
    }

    public new StatRowHead Owner => (StatRowHead)base.Owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override bool IsContentElementCore() => false;

    protected override bool IsControlElementCore() => true;

    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
    {
        var children = new List<AutomationPeer>(base.GetChildrenCore() ?? []);
        foreach (string text in Owner.Said)
            children.Add(new DrawnTextPeer(this, text));

        return children;
    }
}
