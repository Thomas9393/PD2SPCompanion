using PD2SPCompanion.Core.Diagnostics;

namespace PD2SPCompanion.Core.Localization;

/// <summary>
/// The language in use, chosen once at start (Settings' Language takes effect on restart, as the game
/// folder does). English is built in: it is the code's own text, so it
/// needs no pack and is what every missing translation falls back to.
/// </summary>
public static class Language
{
    public const string English = "en-US";
    public const string EnglishName = "English (US)";

    /// <summary>The pack in use, or null for English.</summary>
    public static LanguagePack? Active { get; private set; }

    /// <summary>The code of the language in use.</summary>
    public static string Code => Active?.Code ?? English;

    /// <summary>
    /// Where packs are found: <c>Languages</c> beside the editor. A Debug build takes PD2SPC_LANGUAGES
    /// instead when it is set, so a test can bring its own pack without touching the build.
    /// </summary>
    public static string Folder
    {
        get
        {
#if DEBUG
            if (Environment.GetEnvironmentVariable("PD2SPC_LANGUAGES") is { Length: > 0 } chosen)
                return chosen;
#endif
            return Path.Combine(AppContext.BaseDirectory, "Languages");
        }
    }

    /// <summary>
    /// The English of a string shown in the language in use - or the string itself, in English or when it
    /// was never translated here. For code that reads the house wording back: the message strip tells a
    /// refusal by its English ("Require ...", "Not ...") whatever language the player reads.
    /// A message joined from several sentences has no entry of its own: its first and last sentences are
    /// read back - the longest translations it starts and ends with - and what lies between is kept.
    /// </summary>
    public static string EnglishOf(string shown)
    {
        if (Active is null)
            return shown;
        if (_english.TryGetValue(shown, out string? whole))
            return whole;

        KeyValuePair<string, string>? head = null, tail = null;
        foreach (var pair in _english)
        {
            if (pair.Key.Length < 3 || pair.Key.Length >= shown.Length)
                continue;   // a word like ", " is no sentence
            if (shown.StartsWith(pair.Key, StringComparison.Ordinal) && pair.Key.Length > (head?.Key.Length ?? 0))
                head = pair;
            if (shown.EndsWith(pair.Key, StringComparison.Ordinal) && pair.Key.Length > (tail?.Key.Length ?? 0))
                tail = pair;
        }

        int from = head?.Key.Length ?? 0, to = shown.Length - (tail?.Key.Length ?? 0);
        if (to < from)
            (tail, to) = (null, shown.Length);   // the two overlap: the start is what a refusal reads
        return (head?.Value ?? "") + shown[from..to] + (tail?.Value ?? "");
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _english = new(StringComparer.Ordinal);

    /// <summary>Notes a translation's English for <see cref="EnglishOf"/>; kept small, as only recent messages are read back.</summary>
    internal static void Remember(string translated, string english)
    {
        if (_english.Count > 4096)
            _english.Clear();
        _english[translated] = english;
    }

    /// <summary>Every language offered: English first, then each readable pack, by name.</summary>
    public static IReadOnlyList<(string Code, string Name)> Available()
    {
        var found = new List<(string Code, string Name)>();
        if (Directory.Exists(Folder))
        {
            foreach (string path in Directory.EnumerateFiles(Folder, "*.json"))
            {
                if (Path.GetFileName(path).StartsWith('_'))
                    continue;   // _template.json: every key, nothing translated
                try
                {
                    var pack = LanguagePack.Read(path);
                    if (!pack.Code.Equals(English, StringComparison.OrdinalIgnoreCase))
                        found.Add((pack.Code, pack.Name));
                }
                catch (Exception problem)
                {
                    Log.Info("Language", $"Pack {Path.GetFileName(path)} not read: {problem.Message}");
                }
            }
        }

        return [(English, EnglishName), .. found.OrderBy(each => each.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Whether a language has been put in use by its code (<see cref="Use(string?)"/>): the program does it at
    /// its first lines, before a worker reads the game's tables and words what they name (GameAhead), and the
    /// app does not do it again under that worker.
    /// </summary>
    public static bool InUse { get; private set; }

    /// <summary>Puts a pack in use as it is, or English for null - for tests, which bring their own.</summary>
    public static void Use(LanguagePack? pack)
    {
        Active = pack;
        _english.Clear();
    }

    /// <summary>
    /// Puts a language in use: its pack from <see cref="Folder"/>, or English when the code is English,
    /// empty, or names no readable pack. Called once, before the first window is built.
    /// </summary>
    public static void Use(string? code)
    {
        InUse = true;
        Active = null;
        if (string.IsNullOrWhiteSpace(code) || code.Equals(English, StringComparison.OrdinalIgnoreCase))
            return;

        string path = Path.Combine(Folder, code + ".json");
        if (!File.Exists(path))
        {
            Log.Info("Language", $"{code}: no pack at {path}; English used.");
            return;
        }

        try
        {
            var pack = LanguagePack.Read(path);
            Active = pack;
            Log.Info("Language", $"{pack.Code} ({pack.Name}): {pack.Count} strings"
                     + (pack.Dropped.Count > 0 ? $", {pack.Dropped.Count} dropped (holes not in the English)" : ""));
        }
        catch (Exception problem)
        {
            Log.Info("Language", $"{code} not read: {problem.Message}; English used.");
        }
    }
}
