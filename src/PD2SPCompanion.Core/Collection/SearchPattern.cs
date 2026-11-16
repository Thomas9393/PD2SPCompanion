using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// What is typed in the search's text box, read as a pattern - a regular expression, as the game's stash
/// search takes one. There is no switch for it: what is typed is looked for as the phrase
/// it is, and as a pattern too where it is one, and an item found either way is found. So "attack speed"
/// finds what it always did, <c>attack speed|cast rate</c> finds both, and <c>+20%</c> - no pattern, a
/// <c>+</c> with nothing before it - is the phrase alone.
/// <para>
/// Matched a line at a time over <see cref="ItemLines.TextOf(D2SLib.Model.Save.Item)"/>: <c>^</c> and
/// <c>$</c> are a line's ends and <c>.</c> stays within one. Letters match in either case.
/// </para>
/// </summary>
public static class SearchPattern
{
    /// <summary>The characters that make a pattern of a phrase. Without one, the phrase says it all.</summary>
    private static readonly char[] Meta = ['\\', '*', '+', '?', '|', '{', '[', '(', ')', '^', '$', '.'];

    /// <summary>
    /// <paramref name="typed"/> as a pattern, or null where it is not one: nothing in it a pattern is made
    /// of, not a pattern that can be read - half typed, say, with a bracket still open - or one that matches
    /// nothing at all and so every item (<c>speed|</c>, on the way to <c>speed|rate</c>).
    /// </summary>
    public static string? Of(string? typed)
    {
        if (typed is not { Length: > 0 } || typed.IndexOfAny(Meta) < 0)
            return null;

        return Read(typed) is { } pattern && !Found(pattern, "") ? typed : null;
    }

    /// <summary>
    /// Whether <paramref name="text"/> has what <paramref name="pattern"/> describes - the vault's
    /// <c>REGEXP</c>. False for a pattern that cannot be read and for no text.
    /// </summary>
    public static bool Matches(string? pattern, string? text) =>
        pattern is not null && text is not null && Read(pattern) is { } read && Found(read, text);

    private static bool Found(Regex pattern, string text)
    {
        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// The pattern, read once. Without backtracking where it can be - the time it takes then grows with the
    /// text and no faster, whatever is typed; a pattern that needs it (a look around, a back reference) is
    /// given a moment an item and no more.
    /// </summary>
    private static Regex? Read(string pattern)
    {
        if (_read.TryGetValue(pattern, out var known))
            return known;

        const RegexOptions How = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline;
        Regex? read;

        try
        {
            read = new Regex(pattern, How | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            try
            {
                read = new Regex(pattern, How, TimeSpan.FromMilliseconds(20));
            }
            catch (ArgumentException)
            {
                read = null;
            }
        }
        catch (ArgumentException)
        {
            read = null;
        }

        // Every letter typed is a pattern of its own; the few last are all a search asks again.
        if (_read.Count >= 200)
            _read.Clear();

        return _read[pattern] = read;
    }

    private static readonly ConcurrentDictionary<string, Regex?> _read = new();
}
