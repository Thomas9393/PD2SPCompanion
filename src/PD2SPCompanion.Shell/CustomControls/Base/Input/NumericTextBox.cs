using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace PD2SPCompanion.Shell.CustomControls.Base;

/// <summary>
/// A text box that takes whole numbers, and only within a range.
/// <para>
/// Both halves have to live here. Avalonia has no control that does them: <c>MaskedTextBox</c>
/// refuses characters, but by a fixed-width pattern that cannot say 0-127;
/// <c>NumericUpDown</c> knows the range but keeps invalid text as typed and only rewrites
/// what is shown when it loses focus.
/// </para>
/// </summary>
public class NumericTextBox : TextBox
{
    /// <summary>
    /// Keeps the styling and control theme of a plain text box, so this is a restriction on
    /// what may be typed rather than a new control to dress.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(TextBox);

    public static readonly StyledProperty<int?> MinimumProperty =
        AvaloniaProperty.Register<NumericTextBox, int?>(nameof(Minimum));

    public static readonly StyledProperty<int?> MaximumProperty =
        AvaloniaProperty.Register<NumericTextBox, int?>(nameof(Maximum));

    /// <summary>The range to hold the typed number inside. Either end may be unset.</summary>
    public int? Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int? Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly StyledProperty<int> DecimalsProperty =
        AvaloniaProperty.Register<NumericTextBox, int>(nameof(Decimals));

    /// <summary>
    /// The places a number may have after its point: none, a whole number - every box but a worth in HR, which
    /// takes "0.25" and, as sellers write it, "0,25" (those were plain text boxes, which took
    /// any letter). A decimal box keeps the framework's inside, whose placeholder says "-" for no worth; Up and
    /// Down step it by a tenth, PageUp and PageDown by one.
    /// </summary>
    public int Decimals
    {
        get => GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DecimalsProperty)
            Classes.Set(NumberClass, Decimals == 0);
    }

    /// <summary>
    /// The class every number box wears, for a style to find it by: it styles itself as a plain text box
    /// (<see cref="StyleKeyOverride"/>), and a selector asks a control's style key for its type - so no
    /// selector, not even <c>:is(NumericTextBox)</c>, tells it from one (found when the slim
    /// template of ValueBox.axaml was handed to nothing).
    /// </summary>
    public const string NumberClass = "number";

    public NumericTextBox()
    {
        Classes.Add(NumberClass);

        // Covers text that arrives by any route the keyboard is not - a paste, or the
        // value being set from elsewhere.
        TextChanged += (_, _) => Clamp();

        // The box is left: whatever was typed is finished, and held to the range whole.
        LostFocus += (_, _) => Clamp(finished: true);
    }

    /// <summary>The focus came by a press of the pointer: its release selects the number whole.</summary>
    private bool _selectsOnRelease;

    /// <summary>
    /// A click into a box that did not have the focus selects its number whole, so what is typed next replaces
    /// it (a click showed only a caret in some part of it). A click into the box already being typed in places the caret,
    /// and a press
    /// dragged across part of the number keeps that part. Known by how the focus came, not by the press: the
    /// focus is given before the box hears the press.
    /// </summary>
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _selectsOnRelease = e.NavigationMethod == NavigationMethod.Pointer;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_selectsOnRelease && SelectionStart == SelectionEnd)
            SelectAll();
        _selectsOnRelease = false;
    }

    /// <summary>
    /// Enter finishes the number as leaving the box does. Up and Down step it by one, PageUp and PageDown by ten,
    /// within the range - as a spin box does (the steppers beside it were the
    /// only way, and they take the pointer).
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Decimals > 0 && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
        {
            decimal step = e.Key switch { Key.Up => 0.1m, Key.Down => -0.1m, Key.PageUp => 1m, _ => -1m };
            decimal was = Fraction(Text) ?? Minimum ?? 0;
            decimal to = Held(was + step);
            Text = to.ToString("0." + new string('#', Decimals), CultureInfo.InvariantCulture);
            CaretIndex = Text.Length;
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
        {
            int by = e.Key switch { Key.Up => 1, Key.Down => -1, Key.PageUp => 10, _ => -10 };
            long now = long.TryParse(Text, out long typed) ? typed : Minimum ?? 0;
            long next = (long)Held(now + by);
            Text = next.ToString();
            CaretIndex = Text.Length;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
        if (e.Key is Key.Enter)
            Clamp(finished: true);
    }

    /// <summary>
    /// A number held inside the range, the maximum winning, as <see cref="Helper.TypedNumber.Held"/> holds a number
    /// typed: a range whose minimum is over its maximum is no fault here. Math.Clamp throws on one, and on a key
    /// press that ended the program.
    /// </summary>
    private decimal Held(decimal number)
    {
        if (Minimum is { } low)
            number = Math.Max(number, low);
        if (Maximum is { } high)
            number = Math.Min(number, high);
        return number;
    }

    /// <summary>
    /// Refuses anything that is not part of a whole number.
    /// <para>
    /// This has to be an override rather than a handler on a plain <see cref="TextBox"/>.
    /// Class handlers run before instance ones and the box inserts typed text in its own
    /// class handler, so anything attached with <c>AddHandler</c> or a XAML event runs after
    /// the character is already in.
    /// </para>
    /// </summary>
    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (e.Text is { Length: > 0 } typed && !Accepts(typed))
        {
            e.Handled = true;
            return;
        }

        base.OnTextInput(e);

        // After the insertion rather than during it. Writing Text while the box is still
        // working through its own change is a write it may never show.
        Clamp();
    }

    /// <summary>
    /// Digits, and a minus in front of them. A decimal point is not an unfinished number
    /// here: nothing this edits is stored as a fraction.
    /// <para>
    /// The minus is asked of the text the typing would leave, a selection replaced: a click or Tab selects the
    /// number whole, and over 10 or -5 the minus was refused and the digits after it went in positive. Where the
    /// range stops at nought it is refused: the whole number selected, it would leave a box holding only "-".
    /// </para>
    /// </summary>
    private bool Accepts(string typed)
    {
        if (Decimals > 0)
            return AcceptsFraction(typed);

        foreach (char letter in typed)
        {
            if (char.IsAsciiDigit(letter))
                continue;

            if (letter == '-' && typed.Length == 1 && MinusFits())
                continue;

            return false;
        }

        return true;
    }

    /// <summary>A minus typed now would stand first, with no other behind it, in a range that goes below nought.</summary>
    private bool MinusFits()
    {
        if (Minimum is >= 0)
            return false;

        string text = Text ?? "";
        int start = Math.Min(SelectionStart, SelectionEnd), end = Math.Max(SelectionStart, SelectionEnd);
        if (start == end)
            start = end = Math.Clamp(CaretIndex, 0, text.Length);

        return start == 0 && !text[end..].StartsWith('-');
    }

    /// <summary>
    /// A decimal box: what the box would hold with the typing in, read whole - digits, one point or comma, no more
    /// places after it than <see cref="Decimals"/>, and a minus in front only where the range goes below nought.
    /// </summary>
    private bool AcceptsFraction(string typed)
    {
        string text = Text ?? "";
        int start = Math.Min(SelectionStart, SelectionEnd), end = Math.Max(SelectionStart, SelectionEnd);
        if (start == end)
            start = end = Math.Clamp(CaretIndex, 0, text.Length);

        string next = text[..start] + typed + text[end..];
        int at = next.StartsWith('-') && Minimum is < 0 ? 1 : 0, points = 0, after = 0;
        for (int i = at; i < next.Length; i++)
        {
            char letter = next[i];
            if (letter is '.' or ',')
            {
                if (++points > 1)
                    return false;
            }
            else if (!char.IsAsciiDigit(letter))
            {
                return false;
            }
            else if (points == 1 && ++after > Decimals)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A decimal box's number, a comma read as a point; null where it is none yet.</summary>
    private static decimal? Fraction(string? text) =>
        decimal.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                         CultureInfo.InvariantCulture, out decimal value) ? value : null;

    private bool _clamping;

    /// <summary>
    /// Set while the box's data context is being swapped - the item panel handed from the inventory's
    /// editor to the vault's (Vault_R), or any view kept and given other data. The bindings bring the new
    /// value and the new range one after the other, and a clamp between them held the new value to the
    /// old range and wrote that back - a spear's durability of 56 landing on a body armour's 32 (once
    /// fixed in BasicEditor by setting the bounds first). Nothing is clamped until both have arrived, as
    /// for a box just built.
    /// </summary>
    private int _rebinding;

    protected override void OnDataContextBeginUpdate()
    {
        _rebinding++;
        base.OnDataContextBeginUpdate();
    }

    protected override void OnDataContextEndUpdate()
    {
        base.OnDataContextEndUpdate();
        _rebinding--;
    }

    /// <summary>
    /// Puts a number past either end of the range back to that end.
    /// <para>
    /// Here rather than in whatever the box is bound to. A correction made on the far side
    /// of the binding is raised during the binding's own write to the source, and the
    /// binding takes a change raised there for the echo of what it is writing and drops it:
    /// the value lands and the box goes on showing what was typed. A box correcting its own
    /// text has nothing in the way, and the binding carries the result outwards as usual.
    /// </para>
    /// </summary>
    /// <param name="finished">
    /// The typing is over - the box left, Enter pressed. Until then a number that further digits could
    /// still bring into the range is left as typed (Helper/TypedNumber): held at once, a range from 12
    /// could not be typed into at all. Text set while the box does not have the keyboard is no typing.
    /// </param>
    private void Clamp(bool finished = false)
    {
        // An empty box on the way to a new number is unfinished, not out of range.
        if (_clamping || _rebinding > 0 || Text is not { Length: > 0 } text || text == "-")
            return;

        // A decimal box is held to its range once the typing is over, as typed until then.
        if (Decimals > 0)
        {
            if (finished && Fraction(text) is { } worth && (worth < Minimum || worth > Maximum))
            {
                _clamping = true;
                try
                {
                    Text = Math.Clamp(worth, Minimum ?? decimal.MinValue, Maximum ?? decimal.MaxValue).ToString(CultureInfo.InvariantCulture);
                    CaretIndex = Text.Length;
                }
                finally
                {
                    _clamping = false;
                }
            }

            return;
        }

        if (!int.TryParse(text, out int parsed))
            return;

        if (!finished && IsFocused && PD2SPCompanion.Shell.Helper.TypedNumber.IsUnfinished(parsed, Minimum, Maximum))
            return;

        int held = PD2SPCompanion.Shell.Helper.TypedNumber.Held(parsed, Minimum, Maximum);

        if (held == parsed)
            return;

        _clamping = true;
        try
        {
            Text = held.ToString();
            CaretIndex = Text.Length;
        }
        finally
        {
            _clamping = false;
        }
    }
}
