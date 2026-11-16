using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// Carrying an item from the season it was made in to the season now installed.
/// <para>
/// <b>Most of it is free.</b> The reader decodes a property as <c>rawBits - SaveAdd</c> and the
/// writer encodes it as <c>value + SaveAdd</c>, so an item read under one season's tables and
/// written under another's keeps its <i>values</i> while the encoding adapts to the new widths
/// and offsets. Measured on BetweenWalls' S9 pack against the installed S13 tables:
/// <b>5824 of 5981 items came back byte-for-byte equal in every property</b>, with nothing
/// thrown.
/// </para>
/// <para>
/// What is not free is a property whose field <b>narrowed</b>. Three did between S9 and S13 -
/// <c>corruptor</c> 12 bits to 1, <c>map_glob_monsterrarity</c> 12 to 5,
/// <c>eaglehorn_raven</c> 11 to 6 - and a value that no longer fits is silently truncated by
/// the writer, which turns 1476 into 0. This class is what stands between that and the save
/// file: every property is checked against what the target season can hold, and anything that
/// does not fit is brought into range and <b>reported</b>.
/// </para>
/// <para>
/// <b>The hazard to respect.</b> <see cref="SaveTables"/> and <c>Globals</c> are static, so a
/// migration reads under one season and writes under another with a swap in between. Every
/// item has to be fully in memory before the swap - an item still half-read is written with
/// the wrong widths. Read, then swap, then write; never interleave.
/// </para>
/// </summary>
public static class Migration
{
    /// <summary>One property the target season could not hold as it stood.</summary>
    /// <param name="Stat">The property's name.</param>
    /// <param name="Was">What it held under the season it came from.</param>
    /// <param name="Now">What it holds now.</param>
    /// <param name="Why">Said plainly, for a report a person reads.</param>
    public sealed record Change(string Stat, int Was, int Now, string Why);

    /// <summary>
    /// Brings every property of an item into what the <b>installed</b> tables can hold, and
    /// says what it had to change. An empty list means the item carries across untouched.
    /// <para>
    /// Call it with the target season installed, not the source: the ranges come from
    /// whichever <c>ItemStatCost</c> is loaded.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Change> Fit(Item item, string? stampAs = null)
    {
        var changes = new List<Change>();

        // The item carries its own ten-bit version stamp, and a save holds one era of them:
        // every item in a live S13 character reads 103, every item in the pre-S10 pack reads
        // 100 or 101. Re-encoding the properties is not enough on its own - the game refused
        // a character outright when seven v101 items were placed among eleven v103 ones,
        // with nothing else wrong: the header size and checksum were right and the file
        // round-tripped byte-identical through our own reader.
        //
        // The target is not derivable from the tables, so the caller supplies it - the
        // simplest honest source is an item already in the save being migrated into.
        if (stampAs is { Length: > 0 } stamp && item.Version != stamp)
        {
            changes.Add(new Change("version", 0, 0,
                $"stamped v{item.Version} as v{stamp}; a save holds one era of items."));

            item.Version = stamp;
        }

        // A runeword stores its row's name key as a string index (Runewords.IdOf), and a season
        // whose string tables grew moves every key after the change: S14's moved patchstring's by
        // 130, so an S13 Ferocity holding 3261 names another string. The game re-stamps it on
        // load - ShockSacri's Ferocity came back 3391 - and so does this, by the runes
        // the item holds, which no season's strings move.
        if (Renumber(item) is { } renumbered)
            changes.Add(renumbered);

        foreach (var stat in ItemStats.Every(item))
        {
            if (stat.Stat is not { Length: > 0 } name)
                continue;

            var row = D2SLib.Core.TXT.ItemStatCostTXT[name];
            if (row is null)
            {
                // Nothing to fit it into. Reported rather than changed, because dropping a
                // property is a decision for the caller and guessing a replacement is worse.
                changes.Add(new Change(name, stat.Value, stat.Value,
                    $"{name} is not a property this season has."));

                continue;
            }

            // The parameter is a second field, and it narrows independently of the value.
            // Between S9 and S13 three changed: item_addclassskills and item_elemskill both
            // widened, which costs nothing, and `mirrored` lost its parameter entirely on
            // the way to being a single flag. A parameter the target cannot store has to go,
            // and going quietly is only acceptable when there was nothing in it - a skill id
            // or a monster index disappearing without a word is exactly the damage this
            // class exists to prevent.
            if (row["Save Param Bits"].ToInt32() <= 0 && Parameterised(stat))
            {
                if (Carries(stat))
                {
                    changes.Add(new Change(name, stat.Param ?? 0, 0,
                        $"{name} has no parameter field this season; "
                        + $"{Held(stat)} cannot be carried across."));
                }

                stat.Param = null;
                stat.SkillId = null;
                stat.SkillLevel = null;
                stat.SkillTab = null;
                stat.CharacterClass = null;
                stat.MaxCharges = null;
            }

            if (StatFields.FieldOf(name) is not { } field)
                continue;

            int floor = field.Min, ceiling = field.Max;
            int fitted = field.Hold(stat.Value);
            if (fitted == stat.Value)
                continue;

            // A field narrowed to a single bit is no longer a number, it is a flag, and
            // clamping a count into it would say something false. PD2 did this to
            // `corruptor` between S9 and S13: it held the corruption roll, 1001 and up, and
            // became a yes-or-no. The corruption's own identity lives in `corrupted`, which
            // did not change, so setting the flag keeps the meaning rather than losing it.
            if (field.Bits == 1 && stat.Value != 0)
            {
                fitted = Math.Min(1, ceiling);
                changes.Add(new Change(name, stat.Value, fitted,
                    $"{name} is a single flag this season, not a number; set rather than "
                    + $"cut down from {stat.Value}."));
            }
            else
            {
                changes.Add(new Change(name, stat.Value, fitted,
                    $"{name} was {stat.Value}; this season holds {floor} to {ceiling}."));
            }

            stat.Value = fitted;
        }

        return changes;
    }

    /// <summary>
    /// A runeword's stored id numbered as the installed strings number it, where it was not - what the game itself does
    /// to an old item on load, and so all that is done to a legacy one (<see cref="Saves.ItemSeason"/>). Null where the
    /// id stood.
    /// </summary>
    public static Change? Renumber(Item item)
    {
        if (StaleRuneword(item) is not { } word || Runewords.IdOf(word) is not { } id)
            return null;

        var change = new Change("runeword", (int)item.RunewordId, id,
            L($"{Runewords.GameName(word)} stored as runeword {item.RunewordId}; this season numbers it {id}."));
        item.RunewordId = (uint)id;
        return change;
    }

    /// <summary>The runeword an item was made with, where the id it stores is not the one the installed strings give it.</summary>
    private static Runewords.Word? StaleRuneword(Item item) =>
        Runewords.On(item) is { } word && Runewords.IdOf(word) is { } id && item.RunewordId != id ? word : null;

    /// <summary>Whether the property is holding anything in its parameter fields at all.</summary>
    private static bool Parameterised(ItemStat stat) =>
        stat.Param is not null || stat.SkillId is not null || stat.SkillLevel is not null
        || stat.SkillTab is not null || stat.CharacterClass is not null
        || stat.MaxCharges is not null;

    /// <summary>Whether any of them is holding something other than nothing.</summary>
    private static bool Carries(ItemStat stat) =>
        (stat.Param ?? 0) != 0 || (stat.SkillId ?? 0) != 0 || (stat.SkillLevel ?? 0) != 0
        || (stat.SkillTab ?? 0) != 0 || (stat.CharacterClass ?? 0) != 0
        || (stat.MaxCharges ?? 0) != 0;

    /// <summary>What the parameter fields hold, for a person reading the report.</summary>
    private static string Held(ItemStat stat)
    {
        var parts = new List<string>();

        if ((stat.SkillId ?? 0) != 0) parts.Add($"skill {SkillNames.NameOf(stat.SkillId ?? 0)}");
        if ((stat.SkillLevel ?? 0) != 0) parts.Add($"level {stat.SkillLevel}");
        if ((stat.SkillTab ?? 0) != 0) parts.Add($"tab {stat.SkillTab}");
        if ((stat.CharacterClass ?? 0) != 0) parts.Add($"class {stat.CharacterClass}");
        if ((stat.MaxCharges ?? 0) != 0) parts.Add($"{stat.MaxCharges} charges");
        if ((stat.Param ?? 0) != 0) parts.Add($"parameter {stat.Param}");

        return parts.Count > 0 ? string.Join(", ", parts) : "its parameter";
    }
}
