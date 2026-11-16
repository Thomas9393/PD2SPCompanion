using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// "+N to Fire Skills" and its kin: one property, two stats. <c>item_elemskill</c> (126) grants the levels and its
/// parameter is the element - and the game has no words for it; a partner per element (<c>item_elemskill_fire</c>,
/// 362-366) carries the words and does nothing else (StatCatalogue). Rings holding only the partner showed the line
/// and granted nothing; a Heavenly Garb gave +1 to all 22 of a Necromancer's magic skills and to none of the rest.
/// <para>
/// <b>The words follow the grant</b>, in sync, and a change of the elemental type updates what is shown too: each grant's element has its partner, with the same
/// number, and no partner stands without one. That is how every item the game makes holds them - nothing grants
/// <c>randelemskill</c>, the one property that writes 126 alone. <see cref="Settle"/> makes an item so, and every
/// writer ends on it (DamageKinds.Settle, the Edit page's KEEP); the legitimacy check says where an item is
/// <see cref="Apart"/>.
/// </para>
/// </summary>
public static class ElementSkills
{
    /// <summary>The stat that grants: its parameter is the element.</summary>
    public const string Grant = "item_elemskill";

    /// <summary>The stat that words an element's grant, or null for a number that names none.</summary>
    public static string? LabelOf(int element) =>
        AffixRanges.ElementLabels().TryGetValue(element, out string? label) ? label : null;

    /// <summary>The element a wording stat is for, or null for any other stat.</summary>
    public static int? ElementOf(string? stat)
    {
        if (stat is not { Length: > 0 })
            return null;
        foreach (var (element, label) in AffixRanges.ElementLabels())
        {
            if (label.Equals(stat, StringComparison.OrdinalIgnoreCase))
                return element;
        }
        return null;
    }

    /// <summary>Whether the stat only words an element's grant.</summary>
    public static bool IsLabel(string? stat) => ElementOf(stat) is not null;

    private static bool IsGrant(ItemStat stat) => Grant.Equals(stat.Stat, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One element of one property list where the words are not the grant.
    /// </summary>
    /// <param name="List">Which of the item's property lists: 0 its own, later a runeword's or a set's.</param>
    /// <param name="Element">The element: <c>item_elemskill</c>'s parameter, or the label's.</param>
    /// <param name="Granted">What the grant gives, 0 where there is none.</param>
    /// <param name="Shown">What the words say, 0 where there are none.</param>
    /// <param name="Named">False for a grant whose parameter names no element: it gives nothing.</param>
    public sealed record Gap(int List, int Element, int Granted, int Shown, bool Named = true);

    /// <summary>Where the item's elemental skill lines are not as the game writes them; empty where they are, or there are none.</summary>
    public static IReadOnlyList<Gap> Apart(Item item)
    {
        if (item.IsCompact)
            return [];

        var gaps = new List<Gap>();
        for (int index = 0; index < item.StatLists.Count; index++)
        {
            var stats = item.StatLists[index].Stats;
            var granted = new Dictionary<int, int>();
            var shown = new Dictionary<int, int>();

            foreach (var stat in stats)
            {
                if (IsGrant(stat))
                {
                    int element = stat.Param ?? 0;
                    if (LabelOf(element) is null)
                        gaps.Add(new Gap(index, element, stat.Value, 0, Named: false));
                    else
                        granted[element] = granted.GetValueOrDefault(element) + stat.Value;
                }
                else if (ElementOf(stat.Stat) is { } element)
                {
                    shown[element] = shown.GetValueOrDefault(element) + stat.Value;
                }
            }

            foreach (int element in granted.Keys.Union(shown.Keys).Order())
            {
                int gives = granted.GetValueOrDefault(element), says = shown.GetValueOrDefault(element);
                if (gives != says || !granted.ContainsKey(element) || !shown.ContainsKey(element))
                    gaps.Add(new Gap(index, element, gives, says));
            }
        }

        return gaps;
    }

    /// <summary>
    /// Makes each property list's elemental skill words its grants: a missing one added with the grant's number,
    /// one with another number given it, one with no grant taken off. A grant whose parameter names no element
    /// - as an older forge wrote them - takes the element of the words that have no grant, where
    /// there are such words to tell; otherwise it is left for the legitimacy check to say. Returns how many
    /// stats were changed, added or taken off; nought for an item already as the game writes it.
    /// </summary>
    public static int Settle(Item item)
    {
        if (item.IsCompact || AffixRanges.ElementLabels().Count == 0)
            return 0;

        int changed = 0;
        foreach (var list in item.StatLists)
        {
            var stats = list.Stats;
            var grants = stats.Where(IsGrant).ToList();
            if (grants.Count == 0 && !stats.Any(stat => IsLabel(stat.Stat)))
                continue;

            // A grant naming no element, told by the words beside it that grant nothing.
            foreach (var lost in grants.Where(grant => LabelOf(grant.Param ?? 0) is null))
            {
                var orphans = stats.Where(stat => ElementOf(stat.Stat) is { } element
                                                  && !grants.Any(grant => grant.Param == element)).ToList();
                var told = orphans.FirstOrDefault(orphan => orphan.Value == lost.Value) ?? orphans.FirstOrDefault();
                if (told is not null && ElementOf(told.Stat) is { } found)
                {
                    lost.Param = found;
                    changed++;
                }
            }

            var wanted = grants.Where(grant => LabelOf(grant.Param ?? 0) is not null)
                .GroupBy(grant => grant.Param ?? 0)
                .ToDictionary(group => group.Key, group => group.Sum(grant => grant.Value));

            // The words there are: kept where their element is granted, at its number; one each.
            var worded = new HashSet<int>();
            foreach (var label in stats.Where(stat => IsLabel(stat.Stat)).ToList())
            {
                int element = ElementOf(label.Stat)!.Value;
                if (!wanted.TryGetValue(element, out int number) || !worded.Add(element))
                {
                    stats.Remove(label);
                    changed++;
                    continue;
                }

                int fits = StatFields.Held(label.Stat!, number);
                if (label.Value != fits)
                {
                    label.Value = fits;
                    changed++;
                }
            }

            // And the words missing.
            foreach (var (element, number) in wanted)
            {
                if (worded.Contains(element) || LabelOf(element) is not { } name || StatCatalogue.IdOf(name) is not { } id)
                    continue;

                stats.Add(new ItemStat { Id = id, Stat = name, Param = 0, Value = StatFields.Held(name, number) });
                changed++;
            }
        }

        // And the lists as the game writes them.
        if (changed > 0)
            ItemStats.InIdOrder(item);

        return changed;
    }

    /// <summary>
    /// Puts in each property list the grant its elemental words stand for, where the list words an element and
    /// grants none - for an item read from words alone. The armory and the trade site list only the words
    /// (<c>item_elemskill_fire</c> and its kin, over a thousand ladder items, never <c>item_elemskill</c>): the
    /// game has no words for the grant, so no listing shows it. Read as they stand, <see cref="Settle"/> took the
    /// words off as a grant's leftovers, and every imported "+1 to Fire Skills" was lost - Kadala's Heirloom then
    /// lost the line that tells its variant, and was judged against all six. Returns how many
    /// grants were added.
    /// </summary>
    public static int GrantWorded(Item item)
    {
        if (item.IsCompact || StatCatalogue.IdOf(Grant) is not { } id)
            return 0;

        int added = 0;
        foreach (var list in item.StatLists)
        {
            // A grant naming no element is told its element by these words (Settle): left to it, not doubled.
            if (list.Stats.Any(stat => IsGrant(stat) && LabelOf(stat.Param ?? 0) is null))
                continue;

            foreach (var words in list.Stats.Where(stat => IsLabel(stat.Stat)).ToList())
            {
                int element = ElementOf(words.Stat)!.Value;
                if (list.Stats.Any(stat => IsGrant(stat) && stat.Param == element))
                    continue;

                list.Stats.Add(new ItemStat { Id = id, Stat = Grant, Param = element, Value = words.Value });
                added++;
            }
        }

        if (added > 0)
            ItemStats.InIdOrder(item);

        return added;
    }
}
