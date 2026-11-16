using System.Runtime.CompilerServices;

using D2SLib.Model.Save;
using D2SLib.Model.TXT;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Lucion's desecration, PD2's second corruption (Season 10 on): an amulet already corrupted takes
/// one more corruption mod from the high-rarity amulet corruptions.
/// <para>
/// <b>Stored</b> as a stat pair shaped like corruption's: <c>desecrated</c> (206) and <c>desecrator</c> (207) -
/// Season 14's ten and twelve bits holding the mod's corruption id and 1001 (<see cref="NamesTheMod"/>), Season 13's two
/// bits and one the paragraphs below are about. The mod itself goes into the item's list like a corruption's,
/// summed into a line the item already has - and <b>which mod it was is stored nowhere</b>.
/// <c>desecrated</c> is 2 when the mod was enhanced damage and 3 for any other (all 261
/// desecrated amulets on the market: 20 of 20 and 69 of 69). The game draws the
/// amulet's desecrated picture whenever it is non-zero (tested in game).
/// </para>
/// <para>
/// So the mod has to be <b>identified</b> from the item: of the pool mods whose lines it
/// carries, the one its other sources cannot explain - see <see cref="Identify"/>.
/// </para>
/// </summary>
public static class Desecrations
{
    public const string ValueStat = "desecrated";
    public const string FlagStat = "desecrator";

    /// <summary>What <c>desecrated</c> holds for enhanced damage, and for every other mod.</summary>
    public const int ForDamage = 2;
    public const int ForOthers = 3;

    public static bool IsDesecrated(Item item) => Corruptions.IsDesecrated(item);

    /// <summary>Only amulets are desecrated - every one of the market's 261 - and only corrupted ones.</summary>
    public static bool CanTake(Item item) =>
        ItemTypes.Ancestry(ItemTypes.TypeOf(item.Code)).Contains("amul") && Corruptions.IsCorrupted(item);

    /// <summary>
    /// The mods a desecration draws from: the high-rarity tier of this item's corruption ladder -
    /// cubemain's rows 819-826 for an amulet in S13, read from the table rather than listed here.
    /// </summary>
    public static IReadOnlyList<Corruptions.CorruptionOption> Pool(Item item) =>
        [.. Corruptions.Available(item, asIfClean: true)
            .Where(option => option.Tier == Corruptions.Rarity.High && !option.GivesSockets)];

    /// <summary>
    /// Whether this item's <c>desecrated</c> names the mod: Season 14's ten-bit field holds the mod's corruption id -
    /// PhysSpear's Atma's Scarab, desecrated in game, 44 beside the +10% Faster Run/Walk that is the amulet's id 44
    /// (<c>move1 10</c>), and <c>desecrator</c> 1001. Season 13's two bits - a legacy item's, by its stamp -
    /// said only enhanced damage (2) or not (3).
    /// </summary>
    public static bool NamesTheMod(Item item) => StatFields.CeilingFor(item, ValueStat) > ForOthers;

    /// <summary>
    /// The value <c>desecrated</c> takes for a mod on this item: the mod's id where the field names it
    /// (<see cref="NamesTheMod"/>), else 2 for enhanced damage and 3 for the rest.
    /// </summary>
    public static int ValueFor(Item item, Corruptions.CorruptionOption option) =>
        NamesTheMod(item) ? option.Id : IsDamage(option) ? ForDamage : ForOthers;

    /// <summary>What <c>desecrator</c> takes beside it: Season 14's 1001 (seen once, as the corruption's own 1001), Season 13's one bit 1.</summary>
    public static int FlagFor(Item item) => StatFields.CeilingFor(item, FlagStat) > Corruptions.Thousand ? Corruptions.Thousand + 1 : 1;

    /// <summary>
    /// Whether a value the item holds can be this mod's: its id, where the field names the mod - or, where no mod of the
    /// pool has that id, Season 13's 2 or 3 written into this season's field (the editor's own, from older versions),
    /// read the old way; any mod where the value is neither.
    /// </summary>
    public static bool Fits(Item item, Corruptions.CorruptionOption option, int value)
    {
        if (NamesTheMod(item) && Pool(item).Any(each => each.Id == value))
            return option.Id == value;

        return value switch
        {
            ForDamage => IsDamage(option),
            ForOthers => !IsDamage(option),
            _ => true,
        };
    }

    private static bool IsDamage(Corruptions.CorruptionOption option) =>
        option.Grants.Any(grant => grant.Code.Equals("dmg%", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// One from the pool the way a band picks it - by width. The eight are 18 or 19 wide each
    /// in S13, so near enough even; the market's lean (52 max resists to 13 curse) is what
    /// sellers list, not the odds. PD2 rolls it in its own code, so this is unmeasured.
    /// </summary>
    public static Corruptions.CorruptionOption? Roll(Item item, Random random)
    {
        var pool = Pool(item);
        int total = pool.Sum(option => Math.Max(1, option.Width));
        if (total == 0)
            return null;

        int landed = random.Next(total);
        foreach (var option in pool)
        {
            landed -= Math.Max(1, option.Width);
            if (landed < 0)
                return option;
        }

        return pool[^1];
    }

    /// <summary>A pool mod's share of the pool, as the list shows it.</summary>
    public static string ChanceOf(Item item, Corruptions.CorruptionOption option)
    {
        int total = Pool(item).Sum(each => Math.Max(1, each.Width));
        return total == 0 ? "" : $"{100.0 * Math.Max(1, option.Width) / total:0.#}%";
    }

    /// <summary>The value the item holds, or null on one not desecrated.</summary>
    public static int? ValueOn(Item item) =>
        ItemStats.Every(item).FirstOrDefault(stat => string.Equals(stat.Stat, ValueStat, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>
    /// Which pool mod the desecration added, and - where it cannot be told - every one it could be.
    /// </summary>
    public sealed record Identified(Corruptions.CorruptionOption? Found, IReadOnlyList<Corruptions.CorruptionOption> Candidates);

    [ThreadStatic] private static bool _identifying;

    /// <summary>
    /// Mods told rather than worked out: the player's answer where identifying finds more than
    /// one, and the one just written, which the page's copies carry until KEEP. Held beside the
    /// item, never in it - the file has nowhere to keep it.
    /// </summary>
    private static readonly ConditionalWeakTable<Item, Corruptions.CorruptionOption> Told = new();

    /// <summary>Items whose desecration was pinned while it could not be told. See <see cref="Pin"/>.</summary>
    private static readonly ConditionalWeakTable<Item, object> Untold = new();

    /// <summary>Says which pool mod this item's desecration is; null forgets it.</summary>
    public static void Assume(Item item, Corruptions.CorruptionOption? mod)
    {
        Untold.Remove(item);
        if (mod is null)
            Told.Remove(item);
        else
            Told.AddOrUpdate(item, mod);
    }

    /// <summary>
    /// Keeps the desecration as it is told now, before the lines it is told from move - and keeps
    /// it untold where it cannot be told. Left free, an untold one was told again from what was
    /// left: a Wraith Talisman whose max resistances 2 fit its corruption or its desecration
    /// became the max resistances desecration once a reroll took the other lines away, and read
    /// 4 against its 2 (rollcheck).
    /// </summary>
    public static void Pin(Item item)
    {
        if (Held(item) is { } desecration)
            Assume(item, desecration);
        else if (IsDesecrated(item))
            Untold.AddOrUpdate(item, new object());
    }

    /// <summary>Whether the item's desecration was told or pinned untold, which is in none of its bytes.</summary>
    public static bool IsPinned(Item item) => Told.TryGetValue(item, out _) || Untold.TryGetValue(item, out _);

    /// <summary>The mod this item was told it carries, if it was.</summary>
    public static Corruptions.CorruptionOption? Assumed(Item item) =>
        Told.TryGetValue(item, out var mod) ? mod : null;

    /// <summary>The mod the desecration is: the one told, or else the one identified.</summary>
    public static Corruptions.CorruptionOption? Held(Item item) =>
        !IsDesecrated(item) || Untold.TryGetValue(item, out _) ? null : Assumed(item) ?? Identify(item).Found;

    /// <summary>
    /// The desecration's mod, worked out from the item. Its value narrows the pool - 2 is enhanced
    /// damage, 3 any other. Of those, a mod is <i>possible</i> when the item carries its lines and
    /// each line's number fits the item's other sources plus the mod, and <i>needed</i> when it
    /// also does not fit them without it. One needed mod is the answer; failing that, one possible
    /// mod. More than one, and the candidates are handed back for the player to choose.
    /// <para>
    /// Two things loosen the fit, both met on the market. A magic, rare or crafted item's other
    /// sources are affixes that <i>could</i> have rolled, not ones known to be there, so the line
    /// may be the mod's alone. And a line cannot hold more than its field does: Cannot Be Frozen
    /// is one bit, so a Saracen's Chance corrupted and desecrated with it holds 1, not 2.
    /// </para>
    /// </summary>
    public static Identified Identify(Item item)
    {
        if (ValueOn(item) is not { } value || value == 0)
            return new Identified(null, []);

        var pool = Pool(item).Where(option => Fits(item, option, value)).ToList();

        IReadOnlyDictionary<string, RollRange> without;
        _identifying = true;
        try
        {
            without = AffixRanges.For(item);
        }
        finally
        {
            _identifying = false;
        }

        var held = ItemStats.Every(item)
            .Where(stat => stat.Stat is { Length: > 0 })
            .GroupBy(AffixRanges.KeyOf, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(stat => stat.Value), StringComparer.OrdinalIgnoreCase);

        bool optional = item.Quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Craft;

        var possible = new List<Corruptions.CorruptionOption>();
        var needed = new List<Corruptions.CorruptionOption>();

        foreach (var option in pool)
        {
            bool fits = true, wanted = false;

            foreach (var grant in option.Grants)
            {
                foreach (string stat in AffixRanges.StatsOf(grant.Code))
                {
                    string key = AffixRanges.KeyOf(stat, grant.Param);
                    int has = held.GetValueOrDefault(key);
                    var mod = AffixRanges.RangeFor(grant.Code, stat, grant.Min, grant.Max, grant.Param);
                    var other = without.GetValueOrDefault(key) ?? new RollRange(0, 0, 0);

                    int ceiling = StatFields.CeilingFor(item, stat);
                    int low = Math.Min((optional ? 0 : other.Min) + mod.Min, ceiling);
                    int high = Math.Min(other.Max + mod.Max, ceiling);

                    if (has <= 0 || has < low || has > high)
                        fits = false;
                    if (has < Math.Min(other.Min, ceiling) || has > Math.Min(other.Max, ceiling))
                        wanted = true;
                }
            }

            if (!fits)
                continue;

            possible.Add(option);
            if (wanted)
                needed.Add(option);
        }

        if (needed.Count == 1)
            return new Identified(needed[0], possible);
        if (needed.Count == 0 && possible.Count == 1)
            return new Identified(possible[0], possible);

        return new Identified(null, needed.Count > 1 ? needed : possible);
    }

    /// <summary>
    /// What the desecration granted, where it can be told - a source like the corruption, for the
    /// ranges the item panel and the legitimacy check read. Empty on an item not desecrated, on one
    /// whose mod cannot be told, and while the mod is being identified.
    /// </summary>
    public static IReadOnlyList<CorruptionGrant> On(Item item) =>
        _identifying || Held(item) is not { } found ? [] : found.Grants;

    /// <summary>The stats the desecration wrote, which is what a page marks.</summary>
    public static IReadOnlyList<string> StatsOn(Item item) =>
        [.. On(item).SelectMany(grant => AffixRanges.StatsWritten(grant.Code)).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Desecrates an item with a draft (see <see cref="Corruptions.Draft"/>): the mod's numbers,
    /// summed where the item already has the line, then the pair of stats. An item already
    /// desecrated has its old mod taken off first - <paramref name="old"/>, or the one identified.
    /// </summary>
    /// <returns>False where the old mod could not be told and none was named.</returns>
    public static bool Apply(Item item, Corruptions.CorruptionDraft draft, Corruptions.CorruptionOption? old = null, Random? random = null)
    {
        if (IsDesecrated(item) && !Clean(item, old, random, out _))
            return false;

        var list = ItemStats.WriteValues(item, draft.Values);
        Corruptions.AddStat(list, ValueStat, ValueFor(item, draft.Option));
        Corruptions.AddStat(list, FlagStat, FlagFor(item));
        ItemStats.InIdOrder(item);
        Assume(item, draft.Option);
        return true;
    }

    /// <summary>
    /// Takes the desecration off: the pair of stats, and the lines its mod wrote rolled again from
    /// what is left, as a corruption's clean does - see <see cref="Corruptions.Clean"/>.
    /// </summary>
    /// <returns>False where the mod could not be told and none was named; nothing is changed then.</returns>
    public static bool Clean(Item item, Corruptions.CorruptionOption? mod, Random? random, out IReadOnlyList<Corruptions.CleanedStat> told)
    {
        told = [];
        var which = mod ?? Held(item);
        if (which is null)
            return false;

        foreach (var list in item.StatLists)
        {
            list.Stats.RemoveAll(stat =>
                string.Equals(stat.Stat, ValueStat, StringComparison.OrdinalIgnoreCase)
                || string.Equals(stat.Stat, FlagStat, StringComparison.OrdinalIgnoreCase));
        }

        told = Corruptions.TakeOff(item, which.Grants, random, withCorruption: true);
        Assume(item, null);
        return true;
    }
}
