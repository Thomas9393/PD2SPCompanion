namespace PD2SPCompanion.Core.Collection;

/// <summary>What kind of number a property holds, which decides how it may be carried.</summary>
public enum FieldKind
{
    /// <summary>A quantity. Bigger means more, so it can be rescaled between ranges.</summary>
    Magnitude,

    /// <summary>
    /// A pointer at something in a table - a corruption, a skill, a row. The number means
    /// nothing on its own and <b>arithmetic on it is always wrong</b>.
    /// </summary>
    Identity,

    /// <summary>One bit. It is either set or it is not.</summary>
    Flag,
}

/// <summary>
/// One property's journey from the season an item was made in to the season installed now.
/// </summary>
/// <param name="Stat">The property's name.</param>
/// <param name="Was">What it held.</param>
/// <param name="Now">What it holds after fitting.</param>
/// <param name="From">What its sources could roll in the old season, if known.</param>
/// <param name="To">What they can roll now, if known.</param>
/// <param name="Action">What was done, in a word, for the report.</param>
public sealed record Carried(
    string Stat, int Was, int Now, RollRange? From, RollRange? To, string Action);

/// <summary>
/// Carrying an item from one season's numbers to another's by <b>where a value sat in its
/// range</b>, rather than by keeping the number and hoping it still fits.
/// <para>
/// The rule: <i>a former value will be migrated to the new one with the
/// same percentage it sits in the range; new stats from the new season added randomized; old
/// stats stripped.</i> This is that rule written down so that it cannot drift between runs,
/// which is the whole reason it lives in code.
/// </para>
/// <para>
/// <b>The rule cannot be applied field-blind.</b> PD2 changed <c>corruptor</c> from a
/// twelve-bit id into a one-bit flag between S7p4 and S13. Rescaling it would take the pack's
/// <c>corruptor 1140</c>, place it 27.8% up its old 0-4095 range, and land on <b>0</b> in the
/// new 0-1 range - silently un-corrupting 140 items. So every property is classified first,
/// and only a <see cref="FieldKind.Magnitude"/> is ever rescaled.
/// </para>
/// <para>
/// Every roll is <b>seeded from the item itself</b>, so carrying the same library twice gives
/// the same library. A migration that cannot be re-run cannot be checked.
/// </para>
/// </summary>
public static class SeasonFit
{
    /// <summary>
    /// Properties whose value points at a row rather than measuring anything. Arithmetic on
    /// these is meaningless, so they are carried across untouched.
    /// <para>
    /// <c>corrupted</c> is the one that matters most here: ten bits in both seasons, and it
    /// is what the game reads to know <b>which</b> corruption an item took - which is why a
    /// migrated Annihilus still shows the right line even though its <c>corruptor</c> has
    /// been reduced to a single bit.
    /// </para>
    /// <para>
    /// Not a "+N to a skill, a class's skills, a tree": there the parameter names the skill and the value is how many
    /// levels - a quantity, which keeps its place like any other. Listed here they were never carried: Halaberd's Reign's
    /// +2 to Battle Orders, the top of S13's 1-2, stayed 2 at the bottom of S14's 2-3. The parameter is not
    /// touched by a rescale either way. <c>item_nonclassskill</c> stays: it keeps the skill in its value.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> Pointers = new(StringComparer.OrdinalIgnoreCase)
    {
        "corrupted", "transform_dye", "item_nonclassskill",
        "item_skillonattack",
        "item_skillonkill", "item_skillondeath", "item_skillonhit", "item_skillonlevelup",
        "item_skillongethit", "item_skilloncast", "item_charged_skill", "state",
    };

    /// <summary>
    /// What kind of number a property holds. The width comes from the installed tables, so a
    /// field that has become a flag is recognised as one without being listed anywhere.
    /// </summary>
    public static FieldKind KindOf(string stat)
    {
        if (Pointers.Contains(stat))
            return FieldKind.Identity;

        var row = D2SLib.Core.TXT.ItemStatCostTXT[stat];
        if (row is not null
            && int.TryParse(row["Save Bits"]?.ToString(), out int bits)
            && bits == 1)
        {
            return FieldKind.Flag;
        }

        return FieldKind.Magnitude;
    }

    /// <summary>
    /// Where a value sits in a range, as a fraction. A range of one value has no inside, so
    /// the answer is null and the caller rolls rather than rescales.
    /// </summary>
    public static double? Position(int value, RollRange range)
    {
        var (low, high) = Ordered(range);
        if (high == low)
            return null;

        return Math.Clamp((value - (double)low) / (high - low), 0, 1);
    }

    /// <summary>The same fraction of the way up a different range.</summary>
    public static int At(double fraction, RollRange range)
    {
        var (low, high) = Ordered(range);
        return low + (int)Math.Round(fraction * (high - low), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A range as low-to-high, since some are written the other way round: <c>light</c> on
    /// Cage of the Unsullied reads -4 to -8, which counts down.
    /// </summary>
    public static (int Low, int High) Ordered(RollRange range) =>
        range.Min <= range.Max ? (range.Min, range.Max) : (range.Max, range.Min);

    /// <summary>
    /// A generator depending only on the item and the property, so the same library carried
    /// twice comes out the same both times. <see cref="string.GetHashCode()"/> is
    /// deliberately not used: it is salted per process, which would make every run differ.
    /// </summary>
    public static Random Seeded(uint itemId, string stat)
    {
        unchecked
        {
            // FNV-1a, for a hash that is the same in every process and every run.
            const uint Offset = 2166136261, Prime = 16777619;
            uint hash = Offset ^ itemId;
            foreach (char c in stat)
            {
                hash = (hash ^ c) * Prime;
            }

            return new Random((int)hash);
        }
    }

    /// <summary>
    /// Carries one property. <paramref name="from"/> and <paramref name="to"/> are what the
    /// item's own sources could roll in each season, or null where that is not known.
    /// </summary>
    public static Carried Fit(uint itemId, string stat, int value, RollRange? from, RollRange? to)
    {
        var kind = KindOf(stat);

        // A pointer keeps its number: it names a row, and a row is not a quantity.
        if (kind is FieldKind.Identity)
            return new Carried(stat, value, value, from, to, "kept; it names a row");

        // A flag is set or it is not, whatever it used to count.
        if (kind is FieldKind.Flag)
        {
            int bit = value != 0 ? 1 : 0;
            return new Carried(stat, value, bit, from, to,
                bit == value ? "kept; a flag" : "reduced to a flag; this season stores one bit");
        }

        if (to is null)
            return new Carried(stat, value, value, from, to, "kept; this season names no range");

        var (low, high) = Ordered(to);

        // Nothing to come from, or a fixed value that has become a range: the item had no
        // roll to preserve, so it gets one - the same treatment as a property the season has
        // newly started granting.
        if (from is null || Position(value, from) is not { } where)
        {
            int rolled = Seeded(itemId, stat).Next(low, high + 1);
            return new Carried(stat, value, rolled, from, to,
                rolled == value ? "kept; nothing to rescale from" : "rolled; it had no range before");
        }

        int fitted = Math.Clamp(At(where, to), low, high);
        return new Carried(stat, value, fitted, from, to,
            fitted == value ? "unchanged" : $"rescaled from {where:P0} of its old range");
    }

    /// <summary>
    /// Moves every value on an item to the place in this season's range that it held in the
    /// season it came from, and answers how many moved.
    /// <para>
    /// The rule: <i>a 5 in a range of 4-6 becomes a 10 when the range becomes
    /// 8-12</i> - the same fraction of the way up, not the same number. <see cref="Fit"/> has the
    /// arithmetic and the edge cases; this only pairs each stat with what it could have rolled then
    /// and what it can roll now. Moved here from the CLI's migrate, for the season carry to share.
    /// </para>
    /// <para>
    /// A stat with no <paramref name="before"/> entry is left alone rather than rolled: it means
    /// the old season named no source for it, and inventing a number there would change an item
    /// the migration had no opinion about.
    /// </para>
    /// </summary>
    public static int Rescale(D2SLib.Model.Save.Item item, IReadOnlyDictionary<string, RollRange>? before)
    {
        if (before is null)
            return 0;

        IReadOnlyDictionary<string, RollRange> after;
        try { after = AffixRanges.For(item); }
        catch { return 0; }

        int moved = 0;

        var stats = (item.StatLists ?? []).SelectMany(list => list.Stats ?? [])
            .Where(stat => stat.Stat is { Length: > 0 }).ToList();

        // A range is the item's whole: a set piece's three green lists of +15 Strength have one range,
        // 45. Held against it, each 15 was carried as though it were the 45 - Aldur's Rhythm came out
        // of the S14 rehearsal with every green line at +45 and flagged. Where a
        // property is in more than one list no range says which part moved, so its numbers are kept.
        // Keyed with what the stat names - a skill's id is SkillId, not Param: keyed by Param alone, no "+N to Battle
        // Orders" ever met its range, and none was carried (Halaberd's Reign, 1-2 to 2-3 in S14).
        var repeated = stats.GroupBy(AffixRanges.KeyOf)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();

        foreach (var stat in stats)
        {
            string name = stat.Stat!;
            string key = AffixRanges.KeyOf(stat);

            if (repeated.Contains(key) || !before.TryGetValue(key, out var from) || !after.TryGetValue(key, out var to))
                continue;

            // The same range both seasons: nothing to carry. A fixed one would otherwise be "rolled"
            // (Fit), which is right for a number that has become a range and wrong for one that stayed.
            if (Ordered(from) == Ordered(to))
                continue;

            var carried = SeasonFit.Fit(item.Id, name, stat.Value, from, to);
            if (carried.Now == stat.Value)
                continue;

            stat.Value = carried.Now;
            moved++;
        }

        return moved;
    }
}
