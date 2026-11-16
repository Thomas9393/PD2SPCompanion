using System.Runtime.CompilerServices;

namespace PD2SPCompanion.Core.Localization;

/// <summary>
/// An interpolated string on its way into the language in use (<see cref="Words.L(ref LocalizedText)"/>).
/// It always writes the English, exactly as <c>$"..."</c> would. Only with a pack in use does it also
/// write the key - the literal parts with each hole named after its code, <c>{tile.Name}</c> - and keep
/// each hole's value, so the translation's holes can be filled by name.
/// </summary>
[InterpolatedStringHandler]
public ref struct LocalizedText
{
    private DefaultInterpolatedStringHandler _english;
    private DefaultInterpolatedStringHandler _key;
    private readonly LanguagePack? _pack;
    private List<(string Name, string Value)>? _holes;

    public LocalizedText(int literalLength, int formattedCount)
    {
        _english = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
        _pack = Language.Active;
        if (_pack is not null)
        {
            _key = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
            _holes = new List<(string, string)>(formattedCount);
        }
    }

    public void AppendLiteral(string value)
    {
        _english.AppendLiteral(value);
        if (_pack is not null)
            _key.AppendLiteral(value);
    }

    public void AppendFormatted<T>(T value, string? format = null, [CallerArgumentExpression(nameof(value))] string name = "")
    {
        if (_pack is null)
        {
            _english.AppendFormatted(value, format);
            return;
        }

        string shown = Shown(value, format);
        _english.AppendLiteral(shown);
        Hole(name, shown);
    }

    public void AppendFormatted<T>(T value, int alignment, string? format = null, [CallerArgumentExpression(nameof(value))] string name = "")
    {
        if (_pack is null)
        {
            _english.AppendFormatted(value, alignment, format);
            return;
        }

        string shown = alignment < 0 ? Shown(value, format).PadRight(-alignment) : Shown(value, format).PadLeft(alignment);
        _english.AppendLiteral(shown);
        Hole(name, shown);
    }

    /// <summary>The English, or the pack's translation with its holes filled.</summary>
    internal string Finish()
    {
        string english = _english.ToStringAndClear();
        if (_pack is null)
            return english;

        string key = _key.ToStringAndClear();
        if (_pack.Find(key) is not { } translated)
            return english;
        string filled = Fill(translated, _holes!);
        Language.Remember(filled, english);
        return filled;
    }

    /// <summary>The form for a count: English one or other, or the pack's form for its plural rule.</summary>
    internal static string Pick(long count, ref LocalizedText one, ref LocalizedText other)
    {
        string singular = one._english.ToStringAndClear();
        string plural = other._english.ToStringAndClear();
        if (other._pack is not { } pack)
            return count == 1 ? singular : plural;

        if (one._pack is not null)
            one._key.ToStringAndClear();
        string key = other._key.ToStringAndClear();
        if (pack.FindPlural(key, count) is not { } form)
            return count == 1 ? singular : plural;

        // The singular may name holes the plural does not (or the other way round): both fill.
        var holes = new List<(string, string)>(other._holes!);
        if (one._holes is { } more)
            holes.AddRange(more);
        return Fill(form, holes);
    }

    /// <summary>As <see cref="Pick(long, ref LocalizedText, ref LocalizedText)"/>, the singular fixed.</summary>
    internal static string Pick(long count, string one, ref LocalizedText other)
    {
        string plural = other._english.ToStringAndClear();
        if (other._pack is not { } pack)
            return count == 1 ? one : plural;

        string key = other._key.ToStringAndClear();
        return pack.FindPlural(key, count) is { } form ? Fill(form, other._holes!) : count == 1 ? one : plural;
    }

    private void Hole(string name, string shown)
    {
        string named = name.Length > 0 ? name : _holes!.Count.ToString();
        _key.AppendLiteral("{");
        _key.AppendLiteral(named);
        _key.AppendLiteral("}");
        _holes!.Add((named, shown));
    }

    private static string Shown<T>(T value, string? format) =>
        value is IFormattable formattable ? formattable.ToString(format, null) : value?.ToString() ?? "";

    private static string Fill(string translated, List<(string Name, string Value)> holes)
    {
        foreach (var (name, value) in holes)
            translated = translated.Replace("{" + name + "}", value, StringComparison.Ordinal);
        return translated;
    }
}
