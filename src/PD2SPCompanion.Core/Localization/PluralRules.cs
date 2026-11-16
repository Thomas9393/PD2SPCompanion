namespace PD2SPCompanion.Core.Localization;

/// <summary>
/// Which plural form a count takes, by language family - the CLDR rules for whole numbers, since .NET
/// has none of its own. A pack names its family ("plural": "ru"), or its code's first part is used;
/// an unknown family counts as English.
/// </summary>
public static class PluralRules
{
    public static readonly IReadOnlySet<string> Categories =
        new HashSet<string>(StringComparer.Ordinal) { "zero", "one", "two", "few", "many", "other" };

    public static Func<long, string> For(string family) => family.ToLowerInvariant() switch
    {
        // No plural at all.
        "ja" or "zh" or "ko" or "vi" or "th" or "id" or "ms" or "tr" => _ => "other",
        // 0 and 1 are singular.
        "fr" or "pt-br" => n => n is 0 or 1 ? "one" : "other",
        // Slavic, counted by last digits.
        "ru" or "uk" or "be" => n => Mod(n, 10) == 1 && Mod(n, 100) != 11 ? "one"
            : Mod(n, 10) is >= 2 and <= 4 && Mod(n, 100) is < 12 or > 14 ? "few" : "many",
        "pl" => n => n == 1 ? "one"
            : Mod(n, 10) is >= 2 and <= 4 && Mod(n, 100) is < 12 or > 14 ? "few" : "many",
        "cs" or "sk" => n => n == 1 ? "one" : n is >= 2 and <= 4 ? "few" : "other",
        "ar" => n => n == 0 ? "zero" : n == 1 ? "one" : n == 2 ? "two"
            : Mod(n, 100) is >= 3 and <= 10 ? "few" : Mod(n, 100) is >= 11 and <= 99 ? "many" : "other",
        // English, German, Spanish, Italian, Dutch, the Nordic languages and the rest.
        _ => n => n == 1 ? "one" : "other",
    };

    private static long Mod(long n, long by) => Math.Abs(n) % by;
}
