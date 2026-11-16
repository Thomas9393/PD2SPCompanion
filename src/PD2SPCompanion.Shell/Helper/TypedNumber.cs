namespace PD2SPCompanion.Shell.Helper;

/// <summary>
/// A number on its way to being typed. Held to its range on every keystroke, a box whose range starts at
/// 12 could never be typed into: the first digit is below 12 and was put back to 12 before the second
/// could follow (Replenish Life always fell back to its minimum). A number is
/// held at once only where no further digit can bring it into range; the rest waits until the box is
/// left or Enter is pressed. The box (NumericTextBox) and the rows behind it ask the same question here.
/// </summary>
public static class TypedNumber
{
    /// <summary>
    /// True where the number is outside the range but more digits could still bring it in: a positive
    /// number under the minimum (1, on the way to 15), or a negative one over the maximum (-2, on the way
    /// to -25). Digits only ever take a number further from nought.
    /// </summary>
    public static bool IsUnfinished(int typed, int? minimum, int? maximum) =>
        (minimum is { } low && typed >= 0 && typed < low)
        || (maximum is { } high && typed < 0 && typed > high);

    /// <summary>The number held inside the range.</summary>
    public static int Held(int typed, int? minimum, int? maximum)
    {
        if (minimum is { } low)
            typed = System.Math.Max(typed, low);
        if (maximum is { } high)
            typed = System.Math.Min(typed, high);
        return typed;
    }
}
