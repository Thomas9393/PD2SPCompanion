using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Which affixes a rare or crafted item rolled, worked out from its properties and the ranges
/// the trade site gives beside them - the one thing a bought rare cannot otherwise say.
/// <para>
/// The site gives each rolled line the range of the affix behind it: Strength 16-20, Fire Resist
/// 11-20. Two affixes writing one property show as the sum - enhanced damage 382-500 is two. A
/// line the affix fixes, +2 to Druid Skills, has no range. So this is a small fitting problem:
/// up to three prefixes and three suffixes, each from a different group, each one the item
/// could have rolled at its level on its base, whose ranges add up to every ranged line
/// exactly, and which write nothing the item does not have. Of the fits, the one that also
/// explains the most fixed lines, then the one with fewest affixes, then the lowest rows - so
/// the same listing always comes out the same.
/// </para>
/// <para>
/// A craft recipe's own properties and the base's automagic take no affix slot: their share of a
/// fixed line is counted first, and a range the site gave leaves them out already. Two rows that
/// write the same thing are one answer: the lowest is taken.
/// </para>
/// </summary>
public static class AffixSearch
{
    /// <summary>
    /// A property's key for the search: <see cref="AffixRanges.KeyOf(string, int)"/>, and for a
    /// chance to cast the level too - "of Frost Nova" casts level 18, and a line casting level 30 at
    /// the same chance is not it.
    /// <para>
    /// <b>Not for charges</b>, which are matched by the skill alone and count as one each (see
    /// <see cref="Counted"/>): a staff mod's level and charges come from the item level
    /// (<see cref="AffixRanges.PackedFrom"/>), and the item holds its current charges - a rare axe's
    /// Life Tap at 5 of 22 matched no row written as 20 charges (the ladder). The
    /// legitimacy check judges the level and the charges.
    /// </para>
    /// </summary>
    public static string KeyOf(string stat, int param, int? level) =>
        AffixRanges.KeyOf(stat, param)
        + (level is { } at && at > 0 && StatShapes.Of(stat) != StatShape.Charges ? $"@{at}" : "");

    /// <summary>What a line counts for in the search: its value, or one for a charged skill (see <see cref="KeyOf(string, int, int?)"/>).</summary>
    public static int Counted(string stat, int value) => StatShapes.Of(stat) == StatShape.Charges ? 1 : value;

    /// <summary>The same, for a property already on an item.</summary>
    public static string KeyOf(D2SLib.Model.Save.ItemStat stat) =>
        KeyOf(stat.Stat ?? "", ItemStats.SkillOrParam(stat), stat.SkillLevel);

    /// <summary>The same, for what a property writes.</summary>
    public static string KeyOf(AffixRanges.Wrote wrote) =>
        KeyOf(wrote.Stat, ItemStats.SkillOrParam(wrote), wrote.SkillLevel);

    /// <summary>
    /// One property line as the search sees it: its key (see <see cref="KeyOf(string, int, int?)"/>),
    /// the range the site gave or none, and the value the item holds of it.
    /// </summary>
    /// <param name="Low">For a fixed line, the least its value may be, where the site's words leave it open - a poison
    /// number said as the tooltip shows it stands for every stored number that shows the same. Null: the value.</param>
    /// <param name="High">The most, likewise.</param>
    public sealed record Line(string Key, int Min, int Max, bool Ranged, int Value, int? Low = null, int? High = null);

    /// <summary>
    /// The properties the site splits into its parts when they are not all equal, giving each
    /// part the range of the other affixes on it and none of this one's: a ring's all
    /// resistances 4 beside a poison affix of 11-20 reads Poison Resist 24 (11-20) and a bare
    /// Lightning Resist 4 (a level 99 Barbarian). A row granting one of these is fitted by
    /// what is left of each line rather than by the ranges.
    /// </summary>
    private static readonly HashSet<string> SpreadCodes = new(StringComparer.OrdinalIgnoreCase) { "res-all" };

    /// <summary>The affixes found, and how many of the lines they explain.</summary>
    public sealed record Found(
        IReadOnlyList<MagicAffixes.AffixOption> Prefixes, IReadOnlyList<MagicAffixes.AffixOption> Suffixes,
        int RangedLines, int FixedExplained, int FixedLines);

    /// <summary>
    /// How many fits are weighed before the search stops looking - a guard. It was 4,000 until a
    /// desecrated Bone Necklace ran out before reaching its answer, once a line could take a second
    /// row and a desecration could be one (a level 99 Amazon); at 100,000 a rebuild still
    /// averages 31 ms over the ladder's 11,425 items, the same as before.
    /// </summary>
    private const int MostFits = 100_000;

    /// <summary>
    /// The affixes behind <paramref name="lines"/> on <paramref name="item"/>, or null when no
    /// set of rows the item could carry adds up to every ranged line.
    /// </summary>
    public static Found? Find(Item item, IReadOnlyList<Line> lines)
    {
        if (item.Quality is not (ItemQuality.Rare or ItemQuality.Craft or ItemQuality.Magic))
            return null;

        // A craft whose recipe its lines cannot tell is searched once per recipe it could be, and
        // the answer explaining the most kept: a Gothic Axe carrying both Blood's and Brilliant's
        // properties had none taken off, so "of the Leech" for its life steal looked as good as
        // "of Self-Repair" for the durability nothing else gives (a level 99 Druid). Each
        // that fits, where several do - the likeliest by its lines alone is a guess its affixes
        // settle (AffixRanges.CraftsFitting) - and every one where none does.
        if (item.Quality == ItemQuality.Craft
            && (AffixRanges.CraftsFitting(item) is { Count: > 1 } fitting ? fitting
                : AffixRanges.CraftOn(item) is null ? AffixRanges.CraftFamilies(item) : []) is { Count: > 1 } families)
        {
            Found? best = null;
            foreach (var family in families)
            {
                if (FindWith(item, lines, family) is { } found
                    && (best is null || found.FixedExplained - found.FixedLines > best.FixedExplained - best.FixedLines))
                    best = found;
            }

            return best;
        }

        return FindWith(item, lines, AffixRanges.CraftOn(item));
    }

    private static Found? FindWith(Item item, IReadOnlyList<Line> lines, AffixRanges.CraftRecipe? recipe)
    {
        // What must be met exactly, and what may be explained.
        var target = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
        // A fixed line holds what is left of it for affixes, as a range: its value, less whatever
        // the recipe and the automagic may have rolled of it.
        var fixedLines = new Dictionary<string, (int Low, int High)>(StringComparer.OrdinalIgnoreCase);

        // How much of a ranged line an all-resistances affix could hold: what is left of it over
        // its range. See SpreadCodes.
        var room = new Dictionary<string, (int Low, int High)>(StringComparer.OrdinalIgnoreCase);

        // What a ranged line holds, for the fixed parts the site leaves out of its range.
        var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (line.Ranged)
            {
                target[line.Key] = Add(target.GetValueOrDefault(line.Key), (line.Min, line.Max));
                room[line.Key] = (line.Value - line.Max, line.Value - line.Min);
                held[line.Key] = held.GetValueOrDefault(line.Key) + line.Value;
            }
            else if (line.Value != 0)
                fixedLines[line.Key] = Add(fixedLines.GetValueOrDefault(line.Key), (line.Low ?? line.Value, line.High ?? line.Value));
        }

        // The recipe's and the automagic's own share, taken off the fixed lines first - all of what
        // they could have rolled, since the roll is not known: a Caster amulet's 10-20 cast rate
        // beside of the Apprentice's 10 leaves 5 to 15 of a 25. Not off a range: the site's range
        // is the affixes' alone - a Hit Power War Pike's enhanced damage reads 182-300 beside 333,
        // the recipe's 50-80 being the rest.
        // The recipe's, automagic's, corruption's and upgrades' share of each ranged line - part of
        // its value and not of the site's range. See Keep.
        var grantedOnTarget = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, min, max) in Granted(item, recipe))
        {
            // On a ranged line the room an all-resistances share has is smaller by the same: a
            // Bone Emblem's lightning 68 over 31-40 is its corruption's 15-20 as well (a level 99 Barbarian).
            if (target.ContainsKey(key) && room.TryGetValue(key, out var space))
            {
                room[key] = (space.Low - max, space.High - min);
                var had = grantedOnTarget.GetValueOrDefault(key);
                grantedOnTarget[key] = (had.Min + Math.Min(min, max), had.Max + Math.Max(min, max));
            }

            if (target.ContainsKey(key) || !fixedLines.TryGetValue(key, out var left))
                continue;

            fixedLines[key] = (left.Low - max, left.High - min);
        }

        var present = new HashSet<string>(lines.Select(line => line.Key), StringComparer.OrdinalIgnoreCase);

        // Every row the item could have rolled whose every property is on the item - up to the affix level too, where a
        // magic level lifts it past the item level: Season 14's site gives the real item level (Season 13's gave 96 to
        // nearly all), and a Diadem at 87 lost Priest's (+2 Paladin, level 90) that it rolls at 99 (the S14 ladder).
        var options = new List<Option>();
        foreach (var row in MagicAffixes.ReachableBy(item, MagicAffixes.AffixLevelsAcrossUpgrades(item).Max()))
            Offer(row, free: false);

        // And a desecration's mod, where the item was desecrated: stored nowhere, one of its pool,
        // no affix slot. Unknown to the search, a desecrated amulet's all resistances 7-10 beside
        // a corruption's own left its bare lines to affixes, and the prefix they took pushed out
        // the +2 Lightning Skills the amulet's Charged prefix gave (a level 99 Sorceress).
        if (Desecrations.ValueOn(item) is { } desecrated && desecrated > 0)
        {
            int number = 0;
            foreach (var mod in Desecrations.Pool(item).Where(mod => Desecrations.Fits(item, mod, desecrated)))
            {
                Offer(new MagicAffixes.AffixOption(
                    --number, IsPrefix: false, "desecration", DesecrationGroup, 0, 0, 1,
                    [.. mod.Grants.Select(grant => (grant.Code, grant.Param, grant.Min, grant.Max))],
                    [], [], ForRare: true), free: true);
            }
        }

        // Two rows writing one ranged line: the site's range is the first source's alone and the
        // value is both. War Bolts read open wounds damage 694 with 407-563 beside it - Gorelust's
        // 407-563, a jewel's 109 and "of Gory"'s 33-46 or "of Sanguinary"'s 122-172, whose 10%
        // open wounds the line's 30 needs (a level 98 Sorceress). Asked to add up to the range,
        // the suffix could not be taken, and nothing else explained anything. So where one row's
        // range is the line's, a row writing that line beside others may also stand with its
        // share outside the range, in the value - as a fixed part does.
        foreach (var (key, want) in target)
        {
            if (!options.Any(option => !option.Spread && option.Writes.TryGetValue(key, out var gives) && gives == want))
                continue;

            foreach (var option in options.ToList())
            {
                if (option.Spread || option.Free || option.Writes.Count < 2
                    || !option.Writes.TryGetValue(key, out var gives) || gives == want)
                    continue;

                var writes = new Dictionary<string, (int Min, int Max)>(option.Writes, StringComparer.OrdinalIgnoreCase);
                writes.Remove(key);
                var onTarget = new Dictionary<string, (int Min, int Max)>(
                    option.OnTarget ?? new Dictionary<string, (int Min, int Max)>(), StringComparer.OrdinalIgnoreCase)
                {
                    [key] = gives,
                };
                options.Add(option with { Writes = writes, OnTarget = onTarget });
            }
        }

        bool desecrationOffered = options.Any(option => option.Free);

        // The most any offered desecration writes to each ranged line.
        var desecrationOnTarget = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options.Where(option => option.Free))
        {
            foreach (var (key, gives) in Writes(option.Row))
            {
                if (target.ContainsKey(key))
                    desecrationOnTarget[key] = Math.Max(desecrationOnTarget.GetValueOrDefault(key), Math.Max(gives.Min, gives.Max));
            }
        }

        void Offer(MagicAffixes.AffixOption row, bool free)
        {
            var writes = Writes(row);

            // A damage property is written for every kind of weapon at once and the item keeps
            // the one its base is: of Carnage's maximum damage lands as one-handed, two-handed
            // or thrown. The kinds the item does not keep are not a property it lacks.
            foreach (var family in DamageKinds)
            {
                if (family.Any(present.Contains))
                {
                    foreach (string kind in family.Where(kind => !present.Contains(kind)))
                        writes.Remove(kind);
                }
            }

            if (writes.Count == 0 || writes.Keys.Any(key => !present.Contains(key)))
                return;

            bool spread = row.Grants.Count > 0 && row.Grants.All(grant => SpreadCodes.Contains(grant.Code));

            // The site's range beside a line is the rolls that ranged; a fixed part is in the value
            // and not the range. War Bolts read lightning 7-747 with 1-6 beside it: "of Maelstrom"'s
            // 1-6 and Arcing's fixed 1 (a level 99 Necromancer). Such a part is kept out of the
            // range match and checked against the value instead (see Keep).
            var onTarget = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
            if (!spread)
            {
                foreach (var (key, gives) in writes.ToList())
                {
                    if (target.ContainsKey(key) && gives.Min == gives.Max && writes.Count > 1)
                    {
                        onTarget[key] = (gives.Min, gives.Min);
                        writes.Remove(key);
                    }
                }
            }

            options.Add(new Option(row, writes, spread, onTarget, free));
        }

        bool magic = item.Quality == ItemQuality.Magic;
        int prefixCap = magic ? 1 : MagicAffixes.Slots, suffixCap = magic ? 1 : MagicAffixes.Slots;

        // And the most in all: a craft rolls four at most, three a side notwithstanding - two
        // ladder crafts came out with five and six (a level 99 Paladin, a level 99 Assassin).
        int totalCap = MagicAffixes.CountFor(item).Most;

        // Sockets nothing but a socket prefix explains take a prefix of their own, which no line asks for - the prefix
        // writes the count alone (TradeItems.SocketPrefix). Not kept, a Diadem's mana per level went to Mnemonic beside
        // Godly and Priest's, and its 3 sockets had no slot left (a level 94 Paladin, the S14 ladder); "of
        // Memory" gives the same line as a suffix.
        if (!magic && item.TotalNumberOfSockets > Legitimacy.SocketsAllowed(item).Most)
        {
            prefixCap--;
            totalCap--;
        }

        // The ranged lines, the one with fewest ways to meet it first.
        var order = target.Keys
            .OrderBy(key => options.Count(option => option.Writes.ContainsKey(key)))
            .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Candidate? best = null;
        int reach = MagicAffixes.AffixLevel(item);
        int fits = 0;

        void Consider(List<Option> chosen, Dictionary<string, (int Min, int Max)> sums)
        {
            // Nothing written beyond what the lines hold: a ranged line is met exactly already,
            // and a fixed one must not be exceeded.
            foreach (var (key, range) in sums)
            {
                if (target.ContainsKey(key))
                    continue;
                if (!fixedLines.TryGetValue(key, out var left) || Beyond(range.Min, left))
                    return;
            }

            // Then the fixed lines, each by one more row that fixes exactly that much, where
            // one fits in what is left.
            ExplainFixed(chosen, sums, 0);
        }

        // firstOption: where the options start for line `from` - after the one just added to it, so a
        // line two rows explain together (a desecration's 10% cast rate and of the Apprentice's 10,
        // a level 99 Druid's amulet) is tried in one order only.
        void ExplainFixed(List<Option> chosen, Dictionary<string, (int Min, int Max)> sums, int from, int firstOption = 0)
        {
            if (++fits > MostFits)
                return;

            var keys = fixedLines.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
            Keep(chosen, sums);

            for (int i = from; i < keys.Count; i++)
            {
                string key = keys[i];
                int have = sums.TryGetValue(key, out var got) ? got.Min : 0;
                if (Within(have, fixedLines[key]) || Beyond(have, fixedLines[key]))
                    continue;

                for (int j = i == from ? firstOption : 0; j < options.Count; j++)
                {
                    var candidate = options[j];
                    if (!candidate.Writes.ContainsKey(key) || !Fits(candidate, chosen, prefixCap, suffixCap, totalCap))
                        continue;

                    // Only fixed lines, and each within what is left of it. A row that rolls a
                    // range counts as the part of it that fits: the site gives no range beside
                    // minimum damage, so a ring's +11 from "of Performance" (10-13) comes as a bare
                    // 11 (a level 99 Assassin), and only a fixed row could ever explain it.
                    var option = Narrowed(candidate, sums);
                    if (option is null)
                        continue;

                    chosen.Add(option);
                    var next = With(sums, option);

                    // Short of the line still: another row may make up the rest of it.
                    int now = next.TryGetValue(key, out var reached) ? reached.Min : 0;
                    if (!Within(now, fixedLines[key]) && !Beyond(now, fixedLines[key]))
                        ExplainFixed(chosen, next, i, j + 1);
                    else
                        ExplainFixed(chosen, next, i + 1);
                    chosen.RemoveAt(chosen.Count - 1);

                    if (fits > MostFits)
                        return;
                }
            }
        }

        // The row as it would stand on the fixed lines: each range it writes cut to the rolls that
        // do not go past what is left of that line, and to those that meet it where some do - or
        // null where every roll goes past, or it writes a ranged line. Worked away from nought, as
        // Beyond is, so a negative line cuts the same way.
        Option? Narrowed(Option option, Dictionary<string, (int Min, int Max)> sums)
        {
            if (option.Spread)
                return Spread(option, sums);

            var writes = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, gives) in option.Writes)
            {
                if (target.ContainsKey(key) || !fixedLines.TryGetValue(key, out var left))
                    return null;

                int sign = left.High <= 0 && left.Low < 0 ? -1 : 1;
                int have = sign * (sums.TryGetValue(key, out var sum) ? sum.Min : 0);
                int rollLow = Math.Min(sign * gives.Min, sign * gives.Max), rollHigh = Math.Max(sign * gives.Min, sign * gives.Max);
                int needLow = Math.Min(sign * left.Low, sign * left.High) - have, needHigh = Math.Max(sign * left.Low, sign * left.High) - have;

                if (rollLow > needHigh)
                    return null;

                (int low, int high) = rollHigh >= needLow
                    ? (Math.Max(rollLow, needLow), Math.Min(rollHigh, needHigh))
                    : (rollLow, rollHigh);

                writes[key] = sign > 0 ? (low, high) : (-high, -low);
            }

            return option with { Writes = writes };
        }

        // A spread row writes one roll to every part, so the roll has to fit what is left of each:
        // exactly a fixed line's remainder, and within a ranged line's room. Only the
        // fixed lines are counted as its - the others' ranges never included it.
        Option? Spread(Option option, Dictionary<string, (int Min, int Max)> sums)
        {
            // The rolls it may take: none past what is left of any line it writes. A fixed line may
            // be met only in part, for a second all-resistances source to make up - a desecration's
            // 7-10 and a prefix's share of a Bone Emblem's bare 20s (a level 99 Barbarian) - and a
            // ranged line's room is only a ceiling, since the room may be shared.
            int low = int.MinValue, high = int.MaxValue;
            int needLow = int.MinValue, needHigh = int.MaxValue;
            var fixedKeys = new List<string>();

            foreach (var (key, gives) in option.Writes)
            {
                (int Low, int High) window;
                if (!target.ContainsKey(key) && fixedLines.TryGetValue(key, out var left))
                {
                    int have = sums.TryGetValue(key, out var sum) ? sum.Min : 0;
                    window = (Math.Min(left.Low, left.High) - have, Math.Max(left.Low, left.High) - have);
                    needLow = Math.Max(needLow, window.Low);
                    needHigh = Math.Min(needHigh, window.High);
                    fixedKeys.Add(key);
                }
                else if (!room.TryGetValue(key, out window))
                {
                    return null;
                }

                low = Math.Max(low, gives.Min);
                high = Math.Min(high, Math.Min(gives.Max, window.High));
            }

            if (low > high || fixedKeys.Count == 0)
                return null;

            // Meeting the fixed lines where a roll can; otherwise the part it gives.
            if (Math.Max(low, needLow) <= Math.Min(high, needHigh))
                (low, high) = (Math.Max(low, needLow), Math.Min(high, needHigh));

            return option with { Writes = fixedKeys.ToDictionary(key => key, _ => (low, high), StringComparer.OrdinalIgnoreCase) };
        }

        void Keep(List<Option> chosen, Dictionary<string, (int Min, int Max)> sums)
        {

            // A ranged line's value must be one the chosen rows can reach: the range the site gave,
            // plus what it leaves out - fixed parts beside it, an all-resistances share told on the
            // bare lines, and the recipe's, automagic's or corruption's share. Unasked, an all
            // resistances Chromatic (21-30) met a Gargoyle Head's fire range of 21-30 by itself, and
            // its fire 55 - a fire prefix's 27 and Chromatic's 28 - went unexplained (a level 96 Necromancer).
            foreach (var (key, want) in target)
            {
                if (!held.TryGetValue(key, out int value))
                    continue;

                int low = Math.Min(want.Min, want.Max), high = Math.Max(want.Min, want.Max);
                foreach (var option in chosen)
                {
                    if (option.OnTarget?.TryGetValue(key, out var part) == true)
                    {
                        low += Math.Min(part.Min, part.Max);
                        high += Math.Max(part.Min, part.Max);
                    }
                }

                foreach (var option in chosen.Where(option => option.Spread && !option.Writes.ContainsKey(key)
                                                              && option.Writes.Count > 0 && Writes(option.Row).ContainsKey(key)))
                {
                    var share = option.Writes.Values.First();
                    low += share.Min;
                    high += share.Max;
                }

                if (grantedOnTarget.TryGetValue(key, out var granted))
                {
                    low += granted.Min;
                    high += granted.Max;
                }

                // A desecration writing only ranged lines is never chosen - nothing bare tells it -
                // yet its share is there: a Death Heart's all resistances 27 beside Prismatic's
                // 16-20 is 7-10 of a desecration (a level 99 Barbarian). Not stored, so allowed, not added.
                if (!chosen.Any(option => option.Free) && desecrationOnTarget.TryGetValue(key, out int most))
                    high += most;

                if (value < low || value > high)
                    return;
            }

            // A line is explained when what the affixes give falls in what is left of it - nothing,
            // where the recipe or the automagic can account for all of it.
            // Where the affixes give a range, explained is some roll of it landing there: a
            // quiver's bare lightning maximum 353 is "of Maelstrom"'s 303-403 and not "of Storms"'s
            // 85-126, which the site's range for the minimum - 1-6 for both - cannot tell apart
            // (a level 99 Druid). Asked to lie wholly inside, neither did, and the lower
            // row won.
            int explained = fixedLines.Count(each =>
                sums.TryGetValue(each.Key, out var sum)
                    ? Overlaps(sum, each.Value)
                    : Within(0, each.Value));

            // A desecrated item holds one of the pool's mods, so among answers explaining as much,
            // one with it is the item. Without that, a Bone Emblem's bare 20s went to a Prismatic
            // prefix alone and the check then added the desecration's 7-10 on top (a level 99 Barbarian).
            // A preference, not a rule: made one, two desecrated amulets whose mod
            // could not be placed got no affixes at all.
            var candidate = new Candidate([.. chosen], explained, desecrationOffered && chosen.Any(option => option.Free),
                chosen.Count(option => option.Row.Level > reach || (option.Row.MaxLevel > 0 && reach > option.Row.MaxLevel)));
            if (best is null || candidate.BetterThan(best))
                best = candidate;
        }

        // lineStart: how many were chosen before this line, so only this line's own picks are ordered.
        void Meet(int at, List<Option> chosen, Dictionary<string, (int Min, int Max)> sums, int lineStart)
        {
            if (fits > MostFits)
                return;

            if (at == order.Count)
            {
                Consider(chosen, sums);
                return;
            }

            string key = order[at];
            var want = target[key];
            var have = sums.GetValueOrDefault(key);

            if (have == want)
            {
                Meet(at + 1, chosen, sums, chosen.Count);
                return;
            }

            if (Past(have, want))
                return;

            foreach (var option in options)
            {
                // A spread row meets a range too, where the site shows it merged - "All Resistances
                // +7" with 3-7 beside it. Split, the range leaves it out and adding it goes past.
                if (!option.Writes.TryGetValue(key, out _) || !Fits(option, chosen, prefixCap, suffixCap, totalCap))
                    continue;

                // This line's own picks in row order, so the same set is not tried twice over. Only
                // its own: a row an earlier line chose - King's, for its attack rating - says
                // nothing about which rows this line may add beside it.
                if (chosen.Count > lineStart
                    && (option.Row.IsPrefix ? 0 : 1, option.Row.Id).CompareTo((chosen[^1].Row.IsPrefix ? 0 : 1, chosen[^1].Row.Id)) < 0)
                    continue;

                var next = With(sums, option);

                // Nothing past a ranged line's target, on this line or any other.
                if (next.Any(each => target.TryGetValue(each.Key, out var goal) && Past(each.Value, goal)))
                    continue;

                chosen.Add(option);
                Meet(at, chosen, next, lineStart);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }

        Meet(0, [], new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase), 0);

        if (best is null)
            return null;

        return new Found(
            [.. best.Chosen.Where(option => !option.Free && option.Row.IsPrefix).Select(option => option.Row).OrderBy(row => row.Id)],
            [.. best.Chosen.Where(option => !option.Free && !option.Row.IsPrefix).Select(option => option.Row).OrderBy(row => row.Id)],
            target.Count, best.Explained, fixedLines.Count);
    }

    /// <summary>Writes the affixes found into the item's six slots, prefixes first-come.</summary>
    public static void Apply(Item item, Found found)
    {
        for (int slot = 0; slot < MagicAffixes.Slots; slot++)
        {
            item.MagicPrefixIds[slot] = slot < found.Prefixes.Count ? (ushort)found.Prefixes[slot].Id : (ushort)0;
            item.MagicSuffixIds[slot] = slot < found.Suffixes.Count ? (ushort)found.Suffixes[slot].Id : (ushort)0;
        }
    }

    /// <summary>The damage stats a property writes for each kind of weapon, of which an item keeps its own.</summary>
    private static readonly string[][] DamageKinds =
    [
        ["maxdamage", "secondary_maxdamage", "item_throw_maxdamage"],
        ["mindamage", "secondary_mindamage", "item_throw_mindamage"],
    ];

    /// <param name="OnTarget">
    /// What the row writes to ranged lines outside the site's range - in the value, checked there
    /// (see Keep): a fixed part, or a second source's roll.
    /// </param>
    /// <param name="Free">A desecration's mod: no affix slot, one at most (its group).</param>
    private sealed record Option(
        MagicAffixes.AffixOption Row, Dictionary<string, (int Min, int Max)> Writes, bool Spread = false,
        Dictionary<string, (int Min, int Max)>? OnTarget = null, bool Free = false);

    /// <summary>The group every desecration candidate shares, so one is chosen at most.</summary>
    private const string DesecrationGroup = "(desecration)";

    /// <param name="OutOfReach">
    /// How many of its rows the item could not roll at its affix level. Two rows can write the same
    /// thing and differ only there: a boot's "of Balance" is row 262 (armour, up to level 45) and
    /// row 989 (boots, no ceiling), and the lower id won on eleven ladder boots.
    /// </param>
    private sealed record Candidate(List<Option> Chosen, int Explained, bool Desecration, int OutOfReach = 0)
    {
        public bool BetterThan(Candidate other)
        {
            if (Explained != other.Explained)
                return Explained > other.Explained;
            if (Desecration != other.Desecration)
                return Desecration;
            if (OutOfReach != other.OutOfReach)
                return OutOfReach < other.OutOfReach;
            if (Chosen.Count != other.Chosen.Count)
                return Chosen.Count < other.Chosen.Count;

            var mine = Chosen.Select(option => (option.Row.IsPrefix ? 0 : 1, option.Row.Id)).Order().ToList();
            var theirs = other.Chosen.Select(option => (option.Row.IsPrefix ? 0 : 1, option.Row.Id)).Order().ToList();
            for (int i = 0; i < mine.Count; i++)
            {
                if (mine[i] != theirs[i])
                    return mine[i].CompareTo(theirs[i]) < 0;
            }

            return false;
        }
    }

    private static bool Fits(Option option, List<Option> chosen, int prefixCap, int suffixCap, int totalCap)
    {
        if (chosen.Any(each => each.Row == option.Row))
            return false;

        if (!option.Free)
        {
            if (chosen.Count(each => !each.Free) >= totalCap)
                return false;

            int same = chosen.Count(each => !each.Free && each.Row.IsPrefix == option.Row.IsPrefix);
            if (same >= (option.Row.IsPrefix ? prefixCap : suffixCap))
                return false;
        }

        return option.Row.Group.Length == 0
               || !chosen.Any(each => each.Row.Group.Equals(option.Row.Group, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, (int Min, int Max)> With(Dictionary<string, (int Min, int Max)> sums, Option option)
    {
        var next = new Dictionary<string, (int Min, int Max)>(sums, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, range) in option.Writes)
            next[key] = Add(next.GetValueOrDefault(key), range);
        return next;
    }

    /// <summary>
    /// Whether a sum has gone past what a line holds. Sums move away from nought as affixes are
    /// added, so past means further from nought: above a positive line, below a negative one -
    /// "of Freedom" writes -15% to requirements, and read as a rise every sum was past it at once.
    /// </summary>
    private static bool Past((int Min, int Max) have, (int Min, int Max) want) =>
        want.Max <= 0 && want.Min < 0
            ? have.Min < want.Min || have.Max < want.Max
            : have.Min > want.Min || have.Max > want.Max;

    /// <summary>The same for a single number against what is left of a fixed line: further from nought than its far end.</summary>
    private static bool Beyond(int have, (int Low, int High) left) =>
        left.High <= 0 && left.Low < 0 ? have < left.Low : have > left.High;

    /// <summary>Whether a number falls in what is left of a fixed line.</summary>
    private static bool Within(int have, (int Low, int High) left) =>
        Math.Min(left.Low, left.High) <= have && have <= Math.Max(left.Low, left.High);

    /// <summary>Whether some number of a range falls in what is left of a fixed line.</summary>
    private static bool Overlaps((int Min, int Max) range, (int Low, int High) left) =>
        range.Min <= Math.Max(left.Low, left.High) && range.Max >= Math.Min(left.Low, left.High);

    private static (int Min, int Max) Add((int Min, int Max) one, (int Min, int Max) other) =>
        (one.Min + other.Min, one.Max + other.Max);

    /// <summary>What a row writes, by key, as a range: its lowest roll and its highest.</summary>
    public static Dictionary<string, (int Min, int Max)> Writes(MagicAffixes.AffixOption row)
    {
        var writes = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, param, min, max) in row.Grants)
        {
            var low = AffixRanges.Written(code, min, max, min, param);
            var high = AffixRanges.Written(code, min, max, max, param);

            for (int i = 0; i < low.Count && i < high.Count; i++)
            {
                string key = KeyOf(low[i]);
                var range = StatShapes.Of(low[i].Stat) == StatShape.Charges
                    ? (1, 1)
                    : (Math.Min(low[i].Value, high[i].Value), Math.Max(low[i].Value, high[i].Value));
                writes[key] = Add(writes.GetValueOrDefault(key), range);
            }
        }

        return writes;
    }

    /// <summary>
    /// What the item's craft recipe, its automagic, its corruption and its base upgrades write, by key, as ranges.
    /// The corruption's line comes bare when an affix beside it rolls a fixed number - a wand's 30%
    /// cast rate is a corruption's 20 and "of the Apprentice"'s 10, and the site shows a range for
    /// neither (a level 99 Necromancer) - so its share is taken off like the recipe's.
    /// </summary>
    private static IEnumerable<(string Key, int Min, int Max)> Granted(Item item, AffixRanges.CraftRecipe? craft)
    {
        // A Horadric Cube upgrade's step mod too: Razor Arrows keep the Sharp Arrows' automagic, +10
        // maximum damage, and the step gives 8 more - left to affixes, the 8 took a prefix slot
        // and the +13 minimum damage beside it went unexplained (a level 99 Amazon).
        foreach (var grant in Corruptions.On(item).Concat(BaseUpgrades.Grants(item)).Concat(MapRecipes.Grants(item)))
        {
            var low = AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Min, grant.Param);
            var high = AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Max, grant.Param);
            for (int i = 0; i < low.Count && i < high.Count; i++)
                yield return (KeyOf(low[i]),
                              Math.Min(low[i].Value, high[i].Value), Math.Max(low[i].Value, high[i].Value));
        }

        if (item.Quality == ItemQuality.Craft && craft is { } recipe)
        {
            foreach (var grant in recipe.Grants)
            {
                var low = AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Min, grant.Param);
                var high = AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Max, grant.Param);
                for (int i = 0; i < low.Count && i < high.Count; i++)
                    yield return (KeyOf(low[i]),
                                  Math.Min(low[i].Value, high[i].Value), Math.Max(low[i].Value, high[i].Value));
            }
        }

        foreach (var (key, min, max, level) in AffixRanges.AutomagicGrants(item))
            yield return (level is { } at && at > 0 ? $"{key}@{at}" : key, min, max);
    }
}
