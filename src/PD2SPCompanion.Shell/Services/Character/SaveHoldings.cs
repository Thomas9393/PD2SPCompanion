using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using D2SLib.Model.Save;
using PD2SPCompanion.Core.Items;

namespace PD2SPCompanion.Shell.Services.Character;

/// <summary>
/// What a file of the save set holds as it stands and as it is about to be written, counted, so the set can write a file
/// gaining an item before the file losing it (<see cref="SaveSet"/>). Told apart from the
/// bytes and not from the moves: an item leaves a file by a dozen routes, and every one of them ends in these bytes.
/// <para>
/// An item with an id counts by its id, one without by its code, a socketed one as well as the item it is in. Whatever
/// the materials tab takes counts by the tab's slot, so a rune off the tab and a rune on a page are the same thing. Gold
/// is one kind.
/// </para>
/// </summary>
public sealed class SaveHoldings(IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after)
{
    /// <summary>Whether this file gains something the other loses: then it is written first.</summary>
    public bool GainsWhatLoses(SaveHoldings other) =>
        after.Any(held => held.Value > before.GetValueOrDefault(held.Key)
                          && other.Now(held.Key) < other.Was(held.Key));

    private long Now(string kind) => after.GetValueOrDefault(kind);

    private long Was(string kind) => before.GetValueOrDefault(kind);

    /// <summary>Everything a file holds, each kind and how many.</summary>
    public static IReadOnlyDictionary<string, long> Count(IEnumerable<Item> items, long gold = 0, IEnumerable<(int Slot, int Count)>? tab = null)
    {
        var counted = new Dictionary<string, long>(StringComparer.Ordinal);

        void Add(string kind, long how) => counted[kind] = counted.GetValueOrDefault(kind) + how;

        foreach (var item in items.WithSockets())
        {
            if (Materials.TakesAny(item) is { } taken)
                Add(TabKind(taken.Slot), taken.Quantity);
            else if (!item.IsCompact && item.Id != 0)
                Add(item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), 1);
            else
                Add(item.Code.Trim(), 1);
        }

        foreach (var (slot, count) in tab ?? [])
        {
            if (count > 0)
                Add(TabKind(slot), count);
        }

        if (gold != 0)
            Add("gold", gold);

        return counted;
    }

    /// <summary>A slot of the tab as a kind: its mark tells it from an id, which is all digits, and from a code.</summary>
    private static string TabKind(int slot) => "#" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// What these bytes hold; none where there are none, a file not there yet. Once an array, as <c>VaultMoves</c> counts
    /// its files: what was written is what the next save starts from, and each count is a whole file read.
    /// </summary>
    public static IReadOnlyDictionary<string, long> Counted(byte[]? bytes, Func<byte[], IReadOnlyDictionary<string, long>> read)
    {
        if (bytes is not { Length: > 0 })
            return Empty;

        if (Known.TryGetValue(bytes, out var known))
            return known;

        var counted = read(bytes);
        Known.AddOrUpdate(bytes, counted);
        return counted;
    }

    private static readonly IReadOnlyDictionary<string, long> Empty = new Dictionary<string, long>();

    private static readonly ConditionalWeakTable<byte[], IReadOnlyDictionary<string, long>> Known = new();
}
