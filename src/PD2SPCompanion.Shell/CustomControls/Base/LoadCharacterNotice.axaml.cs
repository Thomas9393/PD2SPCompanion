using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// What a page shows in place of itself while no character is open.
/// <para>
/// The pages used to be unreachable instead - their sidebar buttons were disabled until
/// something was loaded, which says "not now" without saying why. A page that opens and
/// explains itself is the friendlier of the two, and it puts the way out in the same place
/// as the explanation: the line is a button, and it goes home.
/// </para>
/// </summary>
public partial class LoadCharacterNotice : UserControl
{
    /// <summary>What clicking the line does - going home, in every use of it so far.</summary>
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<LoadCharacterNotice, ICommand?>(nameof(Command));

    /// <summary>The line itself, for a page that wants to say something of its own.</summary>
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<LoadCharacterNotice, string>(
            nameof(Message), "Please load a character.");

    /// <summary>
    /// Whether to draw the watermark behind the line. A page giving over its whole panel
    /// wants it; the same notice tucked under a heading does not.
    /// </summary>
    public static readonly StyledProperty<bool> ShowWatermarkProperty =
        AvaloniaProperty.Register<LoadCharacterNotice, bool>(nameof(ShowWatermark), true);

    /// <summary>
    /// How big the line is. Large enough to be the only thing on a panel by default, and
    /// set smaller where it sits beneath something else.
    /// </summary>
    public static readonly StyledProperty<double> TextSizeProperty =
        AvaloniaProperty.Register<LoadCharacterNotice, double>(nameof(TextSize), 27);

    /// <summary>A smaller line under it, in the hint lines' face - "(3 selected)". None by default.</summary>
    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<LoadCharacterNotice, string?>(nameof(Detail));

    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>
    /// What the keys do here, a line each - "Shift + Drag: select area" - drawn under the words as key caps
    /// (KeyedTip). None by default. The Edit page's two notices say how several items are picked on a page packed
    /// tight, the right panel being the more obvious place to say so.
    /// </summary>
    public static readonly StyledProperty<string?> KeysProperty =
        AvaloniaProperty.Register<LoadCharacterNotice, string?>(nameof(Keys));

    /// <summary>Between the words and the keys under them.</summary>
    private const double KeysGap = 14;

    public string? Keys
    {
        get => GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    public bool ShowWatermark
    {
        get => GetValue(ShowWatermarkProperty);
        set => SetValue(ShowWatermarkProperty, value);
    }

    public double TextSize
    {
        get => GetValue(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>
    /// Centred unless a page says otherwise.
    /// <para>
    /// The alignment is bound to the content alignment so a page can put the line where it
    /// wants it - but those two default to <c>Stretch</c>, not <c>Center</c>, so binding
    /// them without saying this sent every notice to the top of its panel.
    /// </para>
    /// </summary>
    static LoadCharacterNotice()
    {
        HorizontalContentAlignmentProperty.OverrideDefaultValue<LoadCharacterNotice>(
            HorizontalAlignment.Center);

        VerticalContentAlignmentProperty.OverrideDefaultValue<LoadCharacterNotice>(
            VerticalAlignment.Center);
    }

    public LoadCharacterNotice()
    {
        InitializeComponent();

        // The keys' line stands under the words and counts for nothing in the panel that centres them: as much
        // taken off below as it is tall, with its gap.
        KeysTip.PropertyChanged += (_, change) =>
        {
            if (change.Property != BoundsProperty || change.NewValue is not Rect { Height: > 0 } now)
                return;

            var wanted = new Thickness(0, KeysGap, 0, -(now.Height + KeysGap));
            if (KeysTip.Margin != wanted)
                KeysTip.Margin = wanted;
        };
    }
}
