using System;
using System.Collections.Generic;
using System.Linq;

namespace PD2SPCompanion.Shell.Services.Settings;

/// <summary>
/// A set of the drop panel's multiples worth keeping, under a name, and a vanilla
/// to revert to (as many as wanted, as the search's presets). Vanilla is not one of these: it is every entry at 1x,
/// built in, neither renamed nor deleted. A plain class with settable properties because it is written to and read
/// back from <c>settings.json</c>.
/// </summary>
public sealed class DropPreset
{
    public string Name { get; set; } = "";

    /// <summary>When it was kept, so the list can be put back in the order it was built up.</summary>
    public DateTimeOffset Created { get; set; }

    /// <summary>The multiples by entry (DropBoosts: an item's code, r20 to r33); an entry left out is 1x.</summary>
    public Dictionary<string, int> Boosts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether these multiples are those, whatever the preset is called: 1x and absent are the same.</summary>
    public bool Holds(IReadOnlyDictionary<string, int> boosts) => Same(Boosts, boosts);

    public static bool Same(IReadOnlyDictionary<string, int> one, IReadOnlyDictionary<string, int> other)
    {
        static IEnumerable<KeyValuePair<string, int>> Raised(IReadOnlyDictionary<string, int> boosts) =>
            boosts.Where(boost => boost.Value > 1);

        var mine = Raised(one).ToDictionary(boost => boost.Key, boost => boost.Value, StringComparer.OrdinalIgnoreCase);
        var theirs = Raised(other).ToList();
        return mine.Count == theirs.Count && theirs.All(boost => mine.TryGetValue(boost.Key, out int value) && value == boost.Value);
    }
}
