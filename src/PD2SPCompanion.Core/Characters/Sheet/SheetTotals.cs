namespace PD2SPCompanion.Core.Characters;

/// <summary>One thing a number of the sheet is made of: what it is from, and how much of the number is its.</summary>
public sealed record SheetPart(string Source, int Value);

/// <summary>
/// A number of the character sheet and what it is made of, for the tooltip over a stat that names its sources.
/// The parts add up to the value; a part that
/// comes to nothing is left out.
/// </summary>
public sealed record SheetFigure(int Value, IReadOnlyList<SheetPart> Parts)
{
    public static readonly SheetFigure None = new(0, []);

    public static SheetFigure Of(IEnumerable<SheetPart> parts)
    {
        var kept = parts.Where(part => part.Value != 0).ToList();
        return new SheetFigure(kept.Sum(part => part.Value), kept);
    }

    /// <summary>This and more parts after it.</summary>
    public SheetFigure With(params SheetPart[] more) => Of(Parts.Concat(more));
}

/// <summary>
/// Every stat a character has from what counts toward it, each amount kept with what it is from: a worn item
/// (what is socketed in it counted as its own), a charm, a set, a skill. A per-level stat is counted as it is
/// written and as what it comes to at the character's level, under the stats it is paid into
/// (<see cref="Requirements.PaidByLevel"/>) - one rule with the requirements.
/// </summary>
public sealed class SheetTotals(int characterLevel)
{
    /// <summary>The ethereal items worn: what a stat paid per ethereal item comes to (Purgatory's enhanced damage).</summary>
    public int EtherealsWorn { get; init; }

    /// <summary>What an amount is from: the thing itself, so two charms of one name stay two, and its name.</summary>
    private readonly record struct Entry(object From, string Source, int Value);

    private readonly Dictionary<string, List<Entry>> _stats = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds an amount of a stat from a source - <paramref name="from"/> tells two sources of one name apart.</summary>
    public void Add(string stat, int value, object from, string source)
    {
        Put(stat, value, from, source);
        foreach (var (target, gained) in Requirements.PaidByLevel(stat, value, characterLevel, EtherealsWorn))
            Put(target, gained, from, source);
    }

    private void Put(string stat, int value, object from, string source)
    {
        if (value == 0)
            return;

        if (!_stats.TryGetValue(stat, out var entries))
            _stats[stat] = entries = [];
        entries.Add(new Entry(from, source, value));
    }

    /// <summary>What a stat comes to.</summary>
    public int Of(string stat) => _stats.TryGetValue(stat, out var entries) ? entries.Sum(entry => entry.Value) : 0;

    /// <summary>What some stats come to together, and what from - one part a source, in the order they came.</summary>
    public SheetFigure Sum(params string[] stats)
    {
        var parts = new List<(object From, string Source, int Value)>();
        foreach (string stat in stats)
        {
            if (!_stats.TryGetValue(stat, out var entries))
                continue;

            foreach (var entry in entries)
            {
                int at = parts.FindIndex(part => ReferenceEquals(part.From, entry.From) || (part.From is string a && entry.From is string b && a == b));
                if (at < 0)
                    parts.Add((entry.From, entry.Source, entry.Value));
                else
                    parts[at] = (entry.From, entry.Source, parts[at].Value + entry.Value);
            }
        }

        return SheetFigure.Of(parts.Select(part => new SheetPart(part.Source, part.Value)));
    }
}
