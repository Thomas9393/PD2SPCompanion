using D2SLib.Model.Save;
using D2SLib.Model.TXT;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// What an item's stat fields can hold, read from ItemStatCost's save columns: the width a
/// value is stored in, its parameter's width, how it packs itself. The format's limits, not
/// what the game would roll - every writer checks against these before a byte changes.
/// </summary>
public static class StatFields
{
    /// <summary>A stat's value field: (value + <paramref name="Add"/>) stored in <paramref name="Bits"/> bits.</summary>
    public readonly record struct StatField(int Bits, int Add)
    {
        /// <summary>The least the field holds: stored as nought.</summary>
        public int Min => -Add;

        /// <summary>The most the field holds: every bit set.</summary>
        public int Max => (int)(((1L << Bits) - 1) - Add);

        /// <summary>A number held at the field's edges, as the game holds it: it clamps, never wraps.</summary>
        public int Hold(int value) => Math.Clamp(value, Min, Max);
    }

    /// <summary>
    /// A stat's field by the installed season's columns, whatever the parser's switch says. Null for a stat this
    /// season does not carry, or one with no value field of a width an int can hold.
    /// </summary>
    public static StatField? FieldOf(string stat) =>
        D2SLib.Core.TXT.ItemStatCostTXT[stat] is { } row ? Usable(row["Save Bits"].ToInt32(), row["Save Add"].ToInt32()) : null;

    /// <summary>
    /// A stat's field by the columns the parser's switch reads now: the season before's while a last-season file or
    /// an old item's scope has it on. Not the same answer as <see cref="FieldOf(string)"/> for the rows those columns
    /// moved, and the callers of each keep the one they had.
    /// </summary>
    public static StatField? FieldAsSwitched(string stat) =>
        D2SLib.Core.TXT.ItemStatCostTXT[stat] is { } row ? Usable(StatEncoding.SaveBits(row), StatEncoding.SaveAdd(row)) : null;

    /// <summary>
    /// A stat's field on this item, by its own stamp's columns (<c>Globals.ItemEra</c>): a legacy item's are the
    /// season before's - Season 13's one-bit <c>corruptor</c> where this season's has twelve. A stamp of neither
    /// season leaves the switch as it stands.
    /// </summary>
    public static StatField? FieldOf(Item item, string stat)
    {
        using var era = D2SLib.Globals.ItemEra(item.Version);
        return FieldAsSwitched(stat);
    }

    private static StatField? Usable(int bits, int add) => bits is <= 0 or > 31 ? null : new StatField(bits, add);

    /// <summary>The most a stat's field holds by <see cref="FieldOf(Item, string)"/>; a sum past it is stored at it.</summary>
    public static int CeilingFor(Item item, string stat) => FieldOf(item, stat)?.Max ?? int.MaxValue;

    /// <summary>A number held at what a stat's field holds by <see cref="FieldOf(string)"/>; as it is with no such field.</summary>
    public static int Held(string stat, int value) => FieldOf(stat)?.Hold(value) ?? value;

    /// <summary>
    /// The largest value a stat's field can hold, or null for a stat this season does not
    /// carry.
    /// <para>
    /// Not the same question as what the game would roll, and normally the wider of the two -
    /// but where a table row asks for more than the field holds, <b>this is what an item
    /// actually carries</b>, because the game clamps. Eaglehorn's row asks 1250 of six bits
    /// and a real one off the trade site reads 63.
    /// </para>
    /// </summary>
    public static int? CeilingOf(string statName) => FieldOf(statName)?.Max;

    /// <summary>
    /// The values a stat can actually hold, from the bits the format gives it.
    /// <para>
    /// This is the real limit, not a chosen one: a stat is stored as (value + Save Add) in
    /// Save Bits bits, so anything outside this wraps. It is not the same as a sensible
    /// value - the tables do not say what a reasonable roll is, only what fits - but it is
    /// the line past which the item stops being readable.
    /// </para>
    /// </summary>
    public static (int Min, int Max)? RangeOf(string statName) =>
        FieldOf(statName) is { } field ? (field.Min, field.Max) : null;

    /// <summary>
    /// What the character's own attribute block can hold for a stat.
    /// <para>
    /// A different column from <see cref="RangeOf"/>: items store a stat in <c>Save Bits</c>,
    /// the character block stores it in <c>CSvBits</c>, and for the gold stats the item
    /// column is empty entirely - they are never on an item.
    /// </para>
    /// </summary>
    public static (int Min, int Max)? AttributeRangeOf(string statName)
    {
        var row = D2SLib.Core.TXT.ItemStatCostTXT[statName];
        if (row is null)
            return null;

        if (!int.TryParse(row["CSvBits"].Value, out int bits) || bits <= 0 || bits > 31)
            return null;

        bool signed = int.TryParse(row["CSvSigned"].Value, out int flag) && flag != 0;

        return signed
            ? (-(int)(1L << (bits - 1)), (int)((1L << (bits - 1)) - 1))
            : (0, (int)((1L << bits) - 1));
    }

    /// <summary>
    /// What a stat's parameter can hold, from <c>Save Param Bits</c>. Separate from the
    /// value's own range: a stat that names a skill stores the skill beside the number, in
    /// a field of its own width.
    /// </summary>
    public static int? ParamMaximumOf(string statName)
    {
        var row = D2SLib.Core.TXT.ItemStatCostTXT[statName];
        if (row is null)
            return null;

        return int.TryParse(row["Save Param Bits"].Value, out int bits) && bits > 0 && bits < 32
            ? (int)((1L << bits) - 1)
            : null;
    }

    /// <summary>
    /// How a stat packs itself, from <c>Encode</c>.
    /// <para>
    /// Two is a skill that fires by chance - the value is the chance and the parameter
    /// carries the skill and the level it casts at. Three is a charged skill, whose value
    /// carries both the charges left and the charges it holds. Either way the stat is
    /// three or four numbers wearing one row, and offering only the value edits a third
    /// of it.
    /// </para>
    /// </summary>
    public static int EncodingOf(string statName)
    {
        var row = D2SLib.Core.TXT.ItemStatCostTXT[statName];

        return row is not null && int.TryParse(row["Encode"].Value, out int encode)
            ? encode
            : 0;
    }

    /// <summary>
    /// A stat's value is stored in a fixed number of bits as (value + Save Add).
    /// Writing something that does not fit would silently wrap and corrupt the item.
    /// </summary>
    public static void Validate(string statName, int value)
    {
        var row = D2SLib.Core.TXT.ItemStatCostTXT[statName]
            ?? throw new ArgumentException($"'{statName}' is not a stat in this season's ItemStatCost.txt.");

        int bits = row["Save Bits"].ToInt32();
        int add = row["Save Add"].ToInt32();
        long stored = (long)value + add;
        long max = (1L << bits) - 1;

        if (stored < 0 || stored > max)
        {
            // A charged skill's bits are two fields, not one number, so quoting the whole
            // width says a charge count of 65535 is fine when 256 is already too many.
            string room = row["Encode"].ToInt32() == 3
                ? "a byte of charges left and a byte of charges held, so 0 to 255 each"
                : $"{-add} to {max - add}";

            throw new ArgumentOutOfRangeException(nameof(value),
                $"{statName} is stored in {bits} bits with Save Add {add}, so it holds "
                + $"{room}. {value} does not fit.");
        }
    }
}
