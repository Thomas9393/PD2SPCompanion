namespace PD2SPCompanion.Core.Localization;

/// <summary>
/// The editor's words in the player's language. Every string a player
/// reads goes through here: <c>L("No free space.")</c>, <c>L($"Moved {moved} items.")</c>,
/// <c>Plural(n, $"{n} item", $"{n} items")</c> - imported everywhere by <c>using static</c>.
/// <para>
/// The English text is the key, its holes named after the code that fills them ("Moved {moved}
/// items."), so a call site reads as what it says and a translator sees what each slot holds. With no
/// pack in use - English, the only language so far - an interpolated call formats straight to its
/// string, as a plain <c>$"..."</c> does: English costs nothing.
/// </para>
/// </summary>
public static class Words
{
    /// <summary>A fixed string, in the language in use.</summary>
    public static string L(string english)
    {
        if (Language.Active?.Find(english) is not { } translated)
            return english;
        Language.Remember(translated, english);
        return translated;
    }

    /// <summary>
    /// A name kept in English where it is defined, because code compares or stores it - a sort order, a
    /// filter choice, a difficulty - and translated with <see cref="L(string)"/> where it is shown. It
    /// returns its argument: it only tells tests/words.py the name is a key for the translators' template.
    /// </summary>
    public static string N(string english) => english;

    /// <summary>"A", "A and B", "A, B and C" - a list as a sentence says it, in the language in use.</summary>
    public static string Joined(IEnumerable<string> items)
    {
        var all = items.ToList();
        return all.Count switch
        {
            0 => "",
            1 => all[0],
            _ => string.Join(L(", "), all.Take(all.Count - 1)) + L(" and ") + all[^1],
        };
    }

    /// <summary>A string with holes, in the language in use; the holes keep their values.</summary>
    public static string L(ref LocalizedText text) => text.Finish();

    /// <summary>
    /// One of two forms by a count: English has one ("1 item") and other ("2 items"). A pack gives as
    /// many forms as its language has (zero, one, two, few, many, other), keyed by the English
    /// <paramref name="other"/> form.
    /// </summary>
    public static string Plural(long count, ref LocalizedText one, ref LocalizedText other) =>
        LocalizedText.Pick(count, ref one, ref other);

    /// <summary>The same, for a fixed singular ("1 item") and a plural with holes ($"{n} items").</summary>
    public static string Plural(long count, string one, ref LocalizedText other) =>
        LocalizedText.Pick(count, one, ref other);

    /// <summary>The same, for two fixed forms.</summary>
    public static string Plural(long count, string one, string other) =>
        Language.Active?.FindPlural(other, count) ?? (count == 1 ? one : other);
}
