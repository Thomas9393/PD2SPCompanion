using D2SLib.Model.Save;
using System.Linq;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// Moves attribute points the way the game does, rather than writing numbers straight into
/// the save.
/// <para>
/// Two rules make it a respec instead of a cheat. A point can only be spent if the character
/// has an unspent one, and taking a point back returns it to that pool, so the total never
/// changes. And an attribute cannot go below what the class starts with, which is the floor
/// the game itself enforces.
/// </para>
/// <para>
/// Spending a point also buys what it buys: vitality is life and stamina, energy is mana. In
/// the game those arrive the moment the point is spent and are then stored in the save, so a
/// respec that moved only the attribute would leave a character whose life does not match
/// its vitality - and nothing recomputes it on load.
/// </para>
/// </summary>
public sealed class AttributeEditor(D2S save, ClassStats stats)
{
    /// <summary>The four the player allocates.</summary>
    public static readonly string[] Editable =
    [
        "strength", "dexterity", "vitality", "energy",
    ];

    /// <summary>Where unspent points live.</summary>
    private const string Pool = "statpts";

    /// <summary>What each attribute pays for, beyond itself.</summary>
    private static readonly Dictionary<string, (string Max, string Current)[]> Derived =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vitality"] = [("maxhp", "hitpoints"), ("maxstamina", "stamina")],
            ["energy"] = [("maxmana", "mana")],
        };

    private readonly Dictionary<string, int> _stats = save.Attributes.Stats;

    public ClassStats Class { get; } = stats;

    /// <summary>Points the character has to spend.</summary>
    public int Unspent => Raw(Pool);

    /// <summary>
    /// Adds unspent attribute points out of nowhere - or, negative, takes them away - held to
    /// nought and to what the save's field stores. Free mode's; nothing else makes points.
    /// </summary>
    /// <returns>How far the pool actually moved.</returns>
    public int AddUnspent(int points)
    {
        int before = Unspent;
        Set(Pool, Math.Clamp(before + points, 0, PoolCeiling(Pool)));
        return Unspent - before;
    }

    /// <summary>The most a pool of points can hold: its field in the save, read from the tables.</summary>
    public static int PoolCeiling(string pool) => StatFields.AttributeRangeOf(pool)?.Max ?? int.MaxValue;

    public int Value(string attribute) => AttributeScale.Display(attribute, Raw(attribute));

    public int Minimum(string attribute) => Class.Minimum(attribute) ?? 0;

    /// <summary>
    /// False when the class table could not be read: without a floor there is nothing to
    /// stop a respec emptying an attribute, so none is offered.
    /// </summary>
    public bool IsAvailable => Class != ClassStats.Unknown;

    public bool CanRaise(string attribute) =>
        IsAvailable && Editable.Contains(attribute) && Unspent > 0 && Raw(attribute) < PoolCeiling(attribute);

    public bool CanLower(string attribute) =>
        IsAvailable && Editable.Contains(attribute) && Value(attribute) > Minimum(attribute);

    /// <summary>
    /// Points a full respec would hand back: everything put in beyond what the class
    /// started with.
    /// </summary>
    public int Refundable =>
        IsAvailable ? Editable.Sum(stat => Value(stat) - Minimum(stat)) : 0;

    public bool CanRespec => Refundable > 0;

    /// <summary>
    /// Returns every attribute to its class minimum, refunding each point to the pool.
    /// <para>
    /// Done one point at a time through the same path as the buttons, so life, mana and
    /// stamina unwind by exactly what they were given - rather than recomputing them from
    /// the floor and hoping the two agree.
    /// </para>
    /// </summary>
    public int RespecAttributes()
    {
        int refunded = 0;

        foreach (string stat in Editable)
        {
            while (CanLower(stat))
            {
                Lower(stat);
                refunded++;
            }
        }

        return refunded;
    }

    public void Raise(string attribute)
    {
        if (!CanRaise(attribute))
            return;

        Move(attribute, +1);
    }

    public void Lower(string attribute)
    {
        if (!CanLower(attribute))
            return;

        Move(attribute, -1);
    }

    private void Move(string attribute, int points)
    {
        Set(attribute, Raw(attribute) + points);
        Set(Pool, Raw(Pool) - points);

        if (!Derived.TryGetValue(attribute, out var effects))
            return;

        foreach (var (max, current) in effects)
        {
            int delta = PerPoint(attribute, max) * points;
            Set(max, Raw(max) + delta);

            // The game raises what you have as well as the ceiling, and lowering must not
            // leave the current value above it.
            Set(current, Math.Min(Raw(current) + delta, Raw(max)));
        }
    }

    /// <summary>
    /// What one point adds, in the stored units of the derived stat.
    /// <para>
    /// The table's figure is in quarters and life is stored shifted, so this works in the
    /// shifted scale to keep the division exact rather than rounding a display value.
    /// </para>
    /// </summary>
    private int PerPoint(string attribute, string derived)
    {
        int quarters = derived switch
        {
            "maxhp" => Class.LifePerVitality,
            "maxstamina" => Class.StaminaPerVitality,
            "maxmana" => Class.ManaPerEnergy,
            _ => 0,
        };

        return quarters * (1 << AttributeScale.ShiftFor(derived)) / ClassStats.PerPointDivisor;
    }

    /// <summary>Gold on the character, and gold in that character's own stash.</summary>
    public const string Carried = "gold";
    public const string Stashed = "goldbank";

    public int Gold(string which) => Raw(which);

    public void SetGold(string which, int value) =>
        Set(which, Math.Clamp(value, 0, GoldCeiling(which)));

    /// <summary>
    /// The most gold this purse will hold.
    /// <para>
    /// Two different limits, and the smaller wins. The field is 25 bits in PD2's
    /// <c>itemstatcost</c> - about 33.5 million - which is what the file can store. The
    /// game's own cap is 5,000,000 for each of the two, and it binds long before the field
    /// does. Gold above the game's cap is not a character with more gold, it is a number
    /// the game will not keep.
    /// </para>
    /// <para>
    /// PD2 changed both of these and they are in no table, so they are read from the mod's
    /// own documentation rather than from the install: the personal stash went from
    /// 2,500,000 to 5,000,000, and carried gold is <b>no longer tied to character level</b>
    /// - vanilla's 10,000 a level does not apply here. See the wiki's General Changes.
    /// </para>
    /// </summary>
    public int GoldCeiling(string which)
    {
        int stored = StatFields.AttributeRangeOf(which)?.Max ?? int.MaxValue;

        return Math.Min(PurseCap, stored);
    }

    /// <summary>Both purses, carried and stashed, cap at the same figure in PD2.</summary>
    private const int PurseCap = 5_000_000;

    private int Raw(string stat) => _stats.TryGetValue(stat, out int value) ? value : 0;

    /// <summary>Held to what the save's field holds: past it the file keeps only the low bits, and a number reads back near 0.</summary>
    private void Set(string stat, int value) => _stats[stat] = Math.Clamp(value, 0, StatFields.AttributeRangeOf(stat)?.Max ?? int.MaxValue);
}
