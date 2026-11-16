using System.Text.Json;
using System.Text.RegularExpressions;

namespace PD2SPCompanion.Core.Localization;

/// <summary>
/// One language's words, read from <c>Languages\&lt;code&gt;.json</c> beside the editor:
/// <code>
/// { "code": "de-DE", "name": "Deutsch", "plural": "en",
///   "strings": { "No free space.": "Kein Platz frei.",
///                "{n} items": { "one": "{n} Gegenstand", "other": "{n} Gegenstände" } } }
/// </code>
/// A key is the English text with its holes named (<see cref="LocalizedText"/>). A translation naming a
/// hole its key does not have is dropped - it would show "{name}" to the player - and so is an empty one,
/// which is a translation not written yet: both fall back to the English.
/// </summary>
public sealed partial class LanguagePack
{
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _plurals = new(StringComparer.Ordinal);
    private readonly Func<long, string> _rule;

    /// <summary>The language's code, as Windows names it: en-US, de-DE, pt-BR.</summary>
    public string Code { get; }

    /// <summary>The language's name in itself: Deutsch, Français.</summary>
    public string Name { get; }

    /// <summary>Entries dropped on reading: a hole the English does not have.</summary>
    public IReadOnlyList<string> Dropped { get; }

    public int Count => _strings.Count + _plurals.Count;

    private LanguagePack(string code, string name, Func<long, string> rule, List<string> dropped)
    {
        Code = code;
        Name = name;
        _rule = rule;
        Dropped = dropped;
    }

    /// <summary>The translation of a fixed or interpolated key, or null.</summary>
    public string? Find(string key) => _strings.TryGetValue(key, out string? text) ? text : null;

    /// <summary>The form of a plural key for a count, by this language's rule; null when not translated.</summary>
    public string? FindPlural(string key, long count)
    {
        if (!_plurals.TryGetValue(key, out var forms))
            return null;
        return forms.TryGetValue(_rule(count), out string? form) ? form
            : forms.TryGetValue("other", out string? other) ? other : null;
    }

    /// <summary>Reads a pack, or says why it cannot.</summary>
    public static LanguagePack Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;

        string code = Text(root, "code") ?? Path.GetFileNameWithoutExtension(path);
        string name = Text(root, "name") ?? code;
        string rule = Text(root, "plural") ?? code.Split('-')[0];
        var dropped = new List<string>();
        var pack = new LanguagePack(code, name, PluralRules.For(rule), dropped);

        if (!root.TryGetProperty("strings", out var strings) || strings.ValueKind != JsonValueKind.Object)
            return pack;

        foreach (var entry in strings.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                string text = entry.Value.GetString() ?? "";
                if (text.Length == 0)
                    continue;
                if (Fits(entry.Name, text))
                    pack._strings[entry.Name] = text;
                else
                    dropped.Add(entry.Name);
            }
            else if (entry.Value.ValueKind == JsonValueKind.Object)
            {
                var forms = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var form in entry.Value.EnumerateObject())
                {
                    string text = form.Value.GetString() ?? "";
                    if (text.Length > 0 && PluralRules.Categories.Contains(form.Name) && Fits(entry.Name, text))
                        forms[form.Name] = text;
                }

                if (forms.Count > 0)
                    pack._plurals[entry.Name] = forms;
                else
                    dropped.Add(entry.Name);
            }
        }

        return pack;
    }

    /// <summary>The holes a key or translation names: {tile.Name}, {moved}.</summary>
    public static IEnumerable<string> HolesOf(string text) =>
        HolePattern().Matches(text).Select(match => match.Groups[1].Value);

    /// <summary>A translation fits its key when every hole it names is one of the key's.</summary>
    private static bool Fits(string key, string translated)
    {
        var known = HolesOf(key).ToHashSet(StringComparer.Ordinal);
        return HolesOf(translated).All(known.Contains);
    }

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text ? text : null;

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex HolePattern();
}
