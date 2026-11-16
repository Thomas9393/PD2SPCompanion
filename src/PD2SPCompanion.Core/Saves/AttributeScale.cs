namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// How a character attribute's stored value relates to the number the game shows.
/// <para>
/// Life, mana and stamina are stored with a fractional part: ItemStatCost's
/// <c>ValShift</c> column says how many low bits are the fraction. The save model keeps
/// the raw value so writes stay faithful, so the shift is applied only for display.
/// Read from the table rather than hardcoded, so a season that changes which attributes
/// are scaled needs no code change here.
/// </para>
/// </summary>
public static class AttributeScale
{
    private static readonly Dictionary<string, int> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static int ShiftFor(string stat)
    {
        if (Cache.TryGetValue(stat, out int cached))
            return cached;

        int shift = 0;
        try
        {
            var row = D2SLib.Core.TXT?.ItemStatCostTXT?[stat];
            string? value = row?["ValShift"]?.Value;
            if (int.TryParse(value, out int parsed) && parsed > 0)
                shift = parsed;
        }
        catch (Exception)
        {
            // An attribute the table does not describe is simply unscaled.
        }

        Cache[stat] = shift;
        return shift;
    }

    /// <summary>The value as the game displays it.</summary>
    public static int Display(string stat, int raw) => raw >> ShiftFor(stat);

    /// <summary>True when the raw value carries a fraction that display would drop.</summary>
    public static bool HasFraction(string stat, int raw)
    {
        int shift = ShiftFor(stat);
        return shift > 0 && (raw & ((1 << shift) - 1)) != 0;
    }

    /// <summary>Clears the cache; call when the tables are reloaded.</summary>
    public static void Reset() => Cache.Clear();
}
