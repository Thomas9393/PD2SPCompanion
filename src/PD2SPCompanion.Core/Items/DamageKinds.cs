using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Which of the flat damage stats an item holds. "+9 to Maximum Damage" is one property and three stats -
/// one-handed (<c>maxdamage</c>), two-handed (<c>secondary_maxdamage</c>) and thrown
/// (<c>item_throw_maxdamage</c>) - and a weapon keeps those of the kinds it is and no other. Measured
/// on 1,406 items: a one-handed base holds <c>maxdamage</c> alone, a two-handed
/// <c>secondary_maxdamage</c> alone, a throwing weapon also <c>item_throw_maxdamage</c>, one wielded both
/// ways both - and everything that is not a weapon holds all three: a jewel, a charm, a ring, and armour
/// (circlets, <c>pd2spc damagekinds</c>; a unique shield's <c>dmg-norm</c> came
/// out of the game carrying all six).
/// <para>
/// <b>A runeword's own list is not narrowed.</b> The properties a runeword adds are kept in a list after the
/// item's, and there a flat damage is all three copies whatever the base: Steel, Holy Thunder, Eternity and
/// Widowmaker in one-handed weapons each hold the two-handed and thrown copies beside the one-handed (the S12
/// library pack, 11 sets on 6 weapons, none otherwise - a Widowmaker's thrown copy cut to its field's 63,
/// as only a writer of all three would leave it). A set's bonus lists are narrowed as the item's own is:
/// Griswold's Redemption, a one-handed weapon, holds the one-handed copy alone in each. Measured on that pack
/// only: <b>a Steel made in game in a one-handed weapon this season, read here, would settle it.</b>
/// </para>
/// <para>
/// One rule for every writer. Without it the trade and armory import read armour as a one-handed
/// weapon - armor.txt has a "mindam" of its own - and dropped the two-handed and thrown copies, and the
/// forge wrote a <c>dmg-norm</c> as the two stats its row names: a War Traveler made either way gave its
/// damage to a one-handed weapon only. <see cref="Settle"/> is what each of them ends on now, and the
/// legitimacy check says where an item is <see cref="Apart"/> from it.
/// </para>
/// </summary>
public static class DamageKinds
{
    /// <summary>A flat damage's copies, the minimum's and the maximum's: one-handed, two-handed, thrown.</summary>
    public static readonly IReadOnlyList<IReadOnlyList<string>> Ends =
    [
        ["mindamage", "secondary_mindamage", "item_throw_mindamage"],
        ["maxdamage", "secondary_maxdamage", "item_throw_maxdamage"],
    ];

    /// <summary>Each flat damage stat, and the weapons.txt column that says a base is of its kind.</summary>
    private static readonly Dictionary<string, string> KindOf = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mindamage"] = "mindam",
        ["maxdamage"] = "mindam",
        ["secondary_mindamage"] = "2handmindam",
        ["secondary_maxdamage"] = "2handmindam",
        ["item_throw_mindamage"] = "minmisdam",
        ["item_throw_maxdamage"] = "minmisdam",
    };

    /// <summary>
    /// Whether the item holds this stat: always, for a stat that is not flat damage and for an item that is
    /// no weapon; a weapon only the kinds its base is.
    /// </summary>
    public static bool Holds(Item item, string stat)
    {
        if (!KindOf.TryGetValue(stat, out string? kind))
            return true;

        // A weapon by its table, not by its damage columns: armor.txt has a "mindam" of its own - a shield's
        // bash, a boot's kick, 0 on a helm - and read as a weapon's it made every armour a one-handed
        // weapon, its two-handed and thrown copies dropped.
        string code = item.Code.Trim();
        if (!D2SLib.Core.TXT.ItemsTXT.IsWeapon(code))
            return true;

        var row = D2SLib.Core.TXT.ItemsTXT.GetByCode(code);
        try
        {
            return row?[kind]?.Value?.Trim() is { Length: > 0 };
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether one of the item's property lists holds this stat: its own list and a set's bonus lists as
    /// <see cref="Holds(Item, string)"/> says; a runeword's list every copy, whatever the base.
    /// </summary>
    public static bool HoldsIn(Item item, int list, string stat) =>
        (list > 0 && item.IsRuneword) || Holds(item, stat);

    /// <summary>One flat damage of one property list that is not as the base holds it.</summary>
    /// <param name="List">Which of the item's property lists: 0 its own, later a runeword's or a set's.</param>
    /// <param name="Held">The copies the list has.</param>
    /// <param name="Wanted">The copies the base holds.</param>
    public sealed record Gap(int List, IReadOnlyList<string> Held, IReadOnlyList<string> Wanted);

    /// <summary>
    /// Where the item's flat damage is not the copies its base holds: one missing, or one a weapon of that
    /// kind does not keep. Empty for an item with no flat damage, and for one written as the game writes it.
    /// Its sockets' items are their own to ask.
    /// </summary>
    public static IReadOnlyList<Gap> Apart(Item item)
    {
        if (item.IsCompact)
            return [];

        var gaps = new List<Gap>();
        for (int list = 0; list < item.StatLists.Count; list++)
        {
            foreach (var copies in Ends)
            {
                var held = copies.Where(copy => item.StatLists[list].Stats.Any(stat => copy.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase))).ToList();
                if (held.Count == 0)
                    continue;

                var wanted = copies.Where(copy => HoldsIn(item, list, copy)).ToList();
                if (!held.SequenceEqual(wanted))
                    gaps.Add(new Gap(list, held, wanted));
            }
        }

        return gaps;
    }

    /// <summary>
    /// Makes each property list's flat damage the copies the base holds: a missing one added with the number
    /// the others have, one the base is not taken off. What every builder of an item ends on - the forge,
    /// the trade and armory import, a craft, a corruption, an affix - and what repairs one built before
    /// (<c>pd2spc damagefix</c>). Returns how many stats were added or taken off; nought for an item already
    /// as the game writes it.
    /// </summary>
    public static int Settle(Item item)
    {
        if (item.IsCompact)
            return 0;

        int changed = 0;
        for (int index = 0; index < item.StatLists.Count; index++)
        {
            var list = item.StatLists[index];
            foreach (var copies in Ends)
            {
                var held = list.Stats.Where(stat => copies.Any(copy => copy.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase))).ToList();
                if (held.Count == 0)
                    continue;

                // One number for all of them (AffixRanges.NeverApart): the one-handed copy's where there
                // is one, else the first there is - where two lines of one copy stand, their sum.
                int Number(string copy) => held.Where(stat => copy.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase)).Sum(stat => stat.Value);
                int value = Number(copies.First(copy => held.Any(stat => copy.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase))));

                int at = list.Stats.IndexOf(held[0]);
                foreach (string copy in copies)
                {
                    bool has = held.Any(stat => copy.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase));
                    bool wants = HoldsIn(item, index, copy);
                    if (has && !wants)
                    {
                        changed += list.Stats.RemoveAll(stat => copy.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (!has && wants && StatCatalogue.IdOf(copy) is { } id)
                    {
                        // After the copies before it, in the order the game writes them: by id.
                        int after = list.Stats.FindLastIndex(stat => copies.Any(other => other.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase)) && stat.Id < id);
                        int before = list.Stats.FindIndex(stat => copies.Any(other => other.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase)) && stat.Id > id);
                        int where = after >= 0 ? after + 1 : before >= 0 ? before : Math.Min(Math.Max(at, 0), list.Stats.Count);

                        // Held at what its field holds: the thrown copies are narrower than the others, and
                        // a Widowmaker's 280 is 63 there, as the game leaves it.
                        int fits = StatFields.Held(copy, value);
                        list.Stats.Insert(where, new ItemStat { Id = id, Stat = copy, Value = fits });
                        changed++;
                    }
                }

                // And a copy that was there is held at its field too: the thrown copies are the narrower,
                // and a number past the field would wrap in the file. A ladder Widowmaker's 280, built
                // from its listing, stood in the thrown copy whole.
                foreach (var stat in list.Stats.Where(stat => copies.Any(copy => copy.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase))))
                {
                    if (StatFields.RangeOf(stat.Stat ?? "") is { } field && (stat.Value < field.Min || stat.Value > field.Max))
                    {
                        stat.Value = Math.Clamp(stat.Value, field.Min, field.Max);
                        changed++;
                    }
                }
            }
        }

        // The elemental skill words after their grants: every builder ends here, so they are settled here too.
        changed += ElementSkills.Settle(item);

        // And the lists as the game writes them: every builder ends here.
        ItemStats.InIdOrder(item);

        return changed;
    }
}
