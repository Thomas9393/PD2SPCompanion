using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// What being <b>superior</b> gives an item.
/// <para>
/// A superior item carries real properties that no affix granted - a Superior Spear holds
/// <c>item_tohit_percent 17</c> with nothing on it to account for that - and they come from
/// <c>qualityitems.txt</c>, eight rows of them. Without this they read as properties nothing
/// explains, which on an ordinary shop item is a poor thing for an editor to say.
/// </para>
/// <para>
/// Inferior quality has no such table: <c>lowqualityitems.txt</c> is four names and nothing
/// else, because the penalty is applied to the base rather than written as a property.
/// </para>
/// </summary>
public static class QualityItems
{
    /// <summary>One way of being superior: what it grants, to what, from what level.</summary>
    private sealed record Row(int Index, string[] Kinds, int Level, (string Code, int Min, int Max)[] Mods);

    private static Row[] _rows = [];

    /// <summary>
    /// The item kinds the table's own columns name, and the item type that answers to each.
    /// </summary>
    private static readonly (string Column, string Type)[] Kinds =
    [
        ("armor", "armo"), ("weapon", "weap"), ("shield", "shie"), ("thrown", "thro"),
        ("scepter", "scep"), ("wand", "wand"), ("staff", "staf"), ("bow", "bow"),
        ("boots", "boot"), ("gloves", "glov"), ("belt", "belt"),
    ];

    /// <summary>
    /// What superior could have given this item, as a range a stat.
    /// <para>
    /// <b>By the rows that fit what the item carries</b> (<see cref="Fitting"/>), where its own list holds
    /// any of a row's stats: two bonuses on it are a pair's, each five lower than alone - <c>ac%</c> 20
    /// beside <c>dur%</c> 20 is what no row gives, and passes where every item is
    /// answered with the widest of every row.
    /// </para>
    /// <para>
    /// <b>Otherwise the union across every row it could have rolled</b> - a superior item with no bonus
    /// yet, or with a set of them no row gives: a weapon's <c>att%</c> is then 10-25.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string Code, int Min, int Max)> For(Item item)
    {
        if (item.Quality != ItemQuality.Superior || _rows.Length == 0)
            return [];

        if (Fitting(item) is { Count: > 0 } fitting)
        {
            var across = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (code, min, max) in fitting.SelectMany(bonus => bonus.Mods))
            {
                across[code] = across.TryGetValue(code, out var already)
                    ? (Math.Min(already.Min, min), Math.Max(already.Max, max))
                    : (min, max);
            }

            // A line the corruption can hold alone may be its alone, so the bonus may have given it nothing.
            var theirs = TheirsAlone(item);
            return [.. across.Select(each => (each.Key,
                theirs.Count > 0 && AffixRanges.StatsWritten(each.Key).All(theirs.Contains) ? 0 : each.Value.Min,
                each.Value.Max))];
        }

        var lineage = ItemTypes.LineageOfBase(item.Code);
        if (lineage.Count == 0)
            return [];

        var widest = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows)
        {
            if (item.ItemLevel < row.Level)
                continue;

            if (!row.Kinds.Any(kind => lineage.Contains(kind, StringComparer.OrdinalIgnoreCase)))
                continue;

            foreach (var (code, min, max) in row.Mods)
            {
                widest[code] = widest.TryGetValue(code, out var already)
                    ? (Math.Min(already.Min, min), Math.Max(already.Max, max))
                    : (min, max);
            }
        }

        return [.. widest.Select(each => (each.Key, each.Value.Min, each.Value.Max))];
    }

    // ---- one row, chosen ----------------------------------

    /// <summary>
    /// One way of being superior, as something to choose: the table's row and what it grants. One bonus
    /// or two - a bonus alone is the larger roll, paired each is five lower (S13).
    /// </summary>
    public sealed record Bonus(int Row, IReadOnlyList<(string Code, int Min, int Max)> Mods);

    /// <summary>
    /// The rows this base could have rolled at its item level, in the table's order. Asked of the base,
    /// not of the item's quality: what a base can be is known before it is made superior.
    /// </summary>
    public static IReadOnlyList<Bonus> RowsFor(Item item)
    {
        var lineage = ItemTypes.LineageOfBase(item.Code);
        if (lineage.Count == 0)
            return [];

        return [.. _rows
            .Where(row => item.ItemLevel >= row.Level
                          && row.Kinds.Any(kind => lineage.Contains(kind, StringComparer.OrdinalIgnoreCase)))
            .Select(row => new Bonus(row.Index, row.Mods))];
    }

    private static HashSet<string> StatsOf(IEnumerable<(string Code, int Min, int Max)> mods) =>
        new(mods.SelectMany(mod => AffixRanges.StatsWritten(mod.Code)), StringComparer.OrdinalIgnoreCase);

    /// <summary>Every stat any row writes: what a superior bonus is made of.</summary>
    private static HashSet<string> EveryStat() => StatsOf(_rows.SelectMany(row => row.Mods));

    /// <summary>
    /// The row the item carries: the one whose stats, and no other row's, are in the item's own list. Null
    /// with none of them there, or with a set no row grants. Read off the stats, never off the three bits
    /// the item stores - on 2,166 real items those name the row carried about half the time.
    /// <para>
    /// <b>A line the corruption can hold alone is not read as the row's</b> (<see cref="TheirsAlone"/>): a Superior
    /// armor whose corruption gave +60% Enhanced Defense read as the Enhanced Defense row, and the brush took the
    /// corruption's line off. The save stores the sum, so a bonus folded inside what the corruption reaches cannot be
    /// told from it: such a line reads as the corruption's. Read without those lines first, then with them, for a pair
    /// whose other half no row writes alone: Enhanced Damage's maximum under a corruption, its minimum on its own.
    /// </para>
    /// </summary>
    public static Bonus? RowOn(Item item)
    {
        if (item.StatLists.FirstOrDefault() is not { } list)
            return null;

        var every = EveryStat();
        var carried = new HashSet<string>(
            list.Stats.Select(stat => stat.Stat ?? "").Where(every.Contains), StringComparer.OrdinalIgnoreCase);

        var own = new HashSet<string>(carried.Except(TheirsAlone(item), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        if (own.Count == 0)
            return null;

        var rows = RowsFor(item);
        return rows.FirstOrDefault(bonus => StatsOf(bonus.Mods).SetEquals(own))
               ?? rows.FirstOrDefault(bonus => StatsOf(bonus.Mods).SetEquals(carried));
    }

    /// <summary>The stats a superior row writes that the corruption or the desecration on the item writes too.</summary>
    private static HashSet<string> SharedWithCorruption(Item item)
    {
        var every = EveryStat();
        return new HashSet<string>(
            Corruptions.On(item).Concat(Desecrations.On(item))
                .SelectMany(grant => AffixRanges.StatsWritten(grant.Code)).Where(every.Contains),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The stats of the item's own list that the corruption and the desecration on it can account for alone: a line a
    /// superior row writes, holding no more than they reach. Empty on an item with neither.
    /// </summary>
    private static HashSet<string> TheirsAlone(Item item)
    {
        var theirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shared = SharedWithCorruption(item);
        if (shared.Count == 0 || item.StatLists.FirstOrDefault() is not { } list)
            return theirs;

        foreach (string stat in shared)
        {
            int reach = Corruptions.On(item).Concat(Desecrations.On(item))
                .Where(grant => AffixRanges.StatsWritten(grant.Code).Contains(stat, StringComparer.OrdinalIgnoreCase))
                .Sum(grant => AffixRanges.RangeFor(grant.Code, stat, grant.Min, grant.Max, grant.Param).Max);

            int held = list.Stats.Where(line => string.Equals(line.Stat, stat, StringComparison.OrdinalIgnoreCase)).Sum(line => line.Value);
            if (held > 0 && held <= reach)
                theirs.Add(stat);
        }

        return theirs;
    }

    /// <summary>The flat damage a weapon's superior damage bonus is written as, where it is (AffixRanges.Sources).</summary>
    private static readonly string[] FlatDamage =
    [
        "mindamage", "maxdamage", "secondary_mindamage", "secondary_maxdamage", "item_throw_mindamage", "item_throw_maxdamage",
    ];

    /// <summary>
    /// The rows that fit what the item carries of a superior's stats; none where it carries none.
    /// <para>
    /// <b>A plain base: the one row its list names.</b> Nothing else writes into a white item's own list,
    /// so Enhanced Damage alone is the row of Enhanced Damage alone, 10-20. One thing stands in the way of
    /// reading it straight off: <b>a weapon's damage bonus may be there as flat damage</b> - a superior
    /// Scimitar of base maximum 6 holds <c>maxdamage 1</c> - so flat damage on a plain weapon counts as
    /// the damage bonus carried. Without that the full regression flagged 17 library weapons whose +1 had
    /// lost its source.
    /// </para>
    /// <para>
    /// <b>A runeword's base: every row that holds what the list shows, its partner there or not.</b> In a
    /// save the word's lines are a list of their own and the base's bonus stands apart.
    /// An item built from the armory's page has only totals, and a bonus the word also
    /// grants cannot be told from the word's roll: on 86 ladder items the exact row flagged a real bonus
    /// - a Faith crossbow's Enhanced Damage of 5-9 whose Attack Rating partner sat inside Faith's own
    /// +300%, an ethereal armour's Durability of 10-14 whose Enhanced Defense sat inside the word's.
    /// So a lone bonus on a runeword is 5 lower at the bottom, as its pair would have it, and two bonuses
    /// shown are still held to their pair.
    /// </para>
    /// <para>
    /// <b>A corrupted item: the rows that fit with or without a line the corruption can hold alone</b>
    /// (<see cref="TheirsAlone"/>). A Durability bonus of 20 beside a corruption's Enhanced Defense was held to the
    /// pair's 15, where the game rolled Durability alone and corrupted it after.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Bonus> Fitting(Item item)
    {
        if (item.StatLists.FirstOrDefault() is not { } list)
            return [];

        var every = EveryStat();
        var carried = new HashSet<string>(
            list.Stats.Select(stat => stat.Stat ?? "").Where(every.Contains), StringComparer.OrdinalIgnoreCase);

        var rows = RowsFor(item);
        var damage = StatsOf(rows.SelectMany(bonus => bonus.Mods).Where(mod => mod.Code.Equals("dmg%", StringComparison.OrdinalIgnoreCase)));
        bool flat = !item.IsRuneword && damage.Count > 0
                    && list.Stats.Any(stat => stat.Stat is { } name && FlatDamage.Contains(name, StringComparer.OrdinalIgnoreCase));

        var fitting = Fitting(item, rows, carried, flat, damage);

        var theirs = TheirsAlone(item);
        if (theirs.Count == 0 || !theirs.Overlaps(carried))
            return fitting;

        var own = new HashSet<string>(carried.Except(theirs, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        return [.. fitting.Concat(Fitting(item, rows, own, flat, damage)).DistinctBy(bonus => bonus.Row)];
    }

    /// <summary>The rows of <paramref name="rows"/> that fit <paramref name="carried"/>, by the rule above.</summary>
    private static IReadOnlyList<Bonus> Fitting(Item item, IReadOnlyList<Bonus> rows, HashSet<string> carried, bool flat, HashSet<string> damage)
    {
        if (carried.Count == 0 && !flat)
            return [];

        if (!item.IsRuneword)
        {
            var whole = new HashSet<string>(carried, StringComparer.OrdinalIgnoreCase);
            if (flat)
                whole.UnionWith(damage);

            if (rows.Where(bonus => StatsOf(bonus.Mods).SetEquals(whole)).ToList() is { Count: > 0 } exact)
                return exact;
        }

        return [.. rows.Where(bonus => StatsOf(bonus.Mods) is var stats
                                       && carried.IsSubsetOf(stats)
                                       && (!flat || damage.IsSubsetOf(stats)))];
    }

    /// <summary>
    /// Puts a row on the item in place of whatever superior bonus it carried - or, with none given, takes
    /// the bonus off. Each number at the top of its range; the page's own boxes change them.
    /// <para>
    /// <b>Enhanced Defense takes the stored defence to the base's top plus one</b>, as an armour that
    /// spawns with it has: 304 of 304 real superior armours carrying it store exactly that. Taken off,
    /// the defence stays - the base was settled when the item was made.
    /// </para>
    /// <para>
    /// <b>A line a corruption shares is subtracted from, not removed</b> (<see cref="TakeRowOff"/>): removed, a
    /// superior holding 15 + 60 given another row lost the whole 75 while the corruption stayed recorded.
    /// </para>
    /// </summary>
    public static void Put(Item item, Bonus? bonus)
    {
        var list = ItemStats.Home(item);
        var every = EveryStat();
        var shared = SharedWithCorruption(item);

        TakeRowOff(item, shared);
        list.Stats.RemoveAll(stat => stat.Stat is { } name && every.Contains(name) && !shared.Contains(name));

        foreach (var (code, min, max) in bonus?.Mods ?? [])
        {
            // In the order the property writes them, one after the other: Enhanced Damage is two stats
            // that the file keeps side by side.
            foreach (var wrote in AffixRanges.Written(code, min, max, max))
            {
                // On the corruption's line where it has one: the game keeps one line carrying both.
                if (shared.Contains(wrote.Stat)
                    && list.Stats.FirstOrDefault(line => string.Equals(line.Stat, wrote.Stat, StringComparison.OrdinalIgnoreCase)) is { } theirs)
                {
                    theirs.Value = Math.Min(theirs.Value + wrote.Value, StatFields.CeilingFor(item, wrote.Stat));
                    continue;
                }

                list.Stats.Add(ItemStats.LineFrom(wrote));
            }
        }

        if (bonus is not null && StatsOf(bonus.Mods).Contains("item_armor_percent") && ItemBases.Of(item.Code) is { MaximumDefense: { } top })
            item.Armor = (ushort)(item.IsEthereal ? ItemBases.Ethereal(top + 1) : top + 1);

        // A flat damage as the base holds it (DamageKinds): what every writer of an item ends on.
        DamageKinds.Settle(item);
    }

    /// <summary>
    /// The carried row's part of the lines a corruption shares, off by <see cref="Corruptions.TakeOff"/>, as
    /// <see cref="MagicAffixes.ApplyFresh"/> takes old affixes off. At the top of its range, as <see cref="Put"/> writes
    /// a row, so picking row after row never moves the corruption's share. Asked as if not superior: the bonus leaving
    /// is no source of what is left.
    /// </summary>
    private static void TakeRowOff(Item item, IReadOnlySet<string> shared)
    {
        if (shared.Count == 0 || RowOn(item) is not { } carried)
            return;

        var leaving = carried.Mods
            .Where(mod => AffixRanges.StatsWritten(mod.Code).Any(shared.Contains))
            .Select(mod => new CorruptionGrant(mod.Code, mod.Max, mod.Max, 0))
            .ToList();
        if (leaving.Count == 0)
            return;

        // Told now and kept, as a corruption's clean does: the lines it is told from are about to move.
        Desecrations.Pin(item);

        var quality = item.Quality;
        item.Quality = ItemQuality.Normal;
        try
        {
            Corruptions.TakeOff(item, leaving, null, withCorruption: true);
        }
        finally
        {
            item.Quality = quality;
        }
    }

    public static void Load(GameData game)
    {
        var found = new List<Row>();

        try
        {
            if (game.Lines("qualityitems") is not { } lines)
                return;
            if (lines.Length == 0)
                return;

            string[] header = lines[0].Split('\t');

            // The table's own row number, a row left out here counted too.
            int index = -1;

            foreach (string line in lines.Skip(1))
            {
                index++;
                string[] cells = line.Split('\t');
                if (cells.Length < header.Length)
                    continue;

                string Cell(string name)
                {
                    int column = Array.IndexOf(header, name);
                    return column >= 0 && cells.Length > column ? cells[column].Trim() : "";
                }

                var mods = new List<(string, int, int)>();

                for (int n = 1; n <= 2; n++)
                {
                    if (Cell($"mod{n}code") is not { Length: > 0 } code)
                        continue;

                    _ = int.TryParse(Cell($"mod{n}min"), out int min);
                    _ = int.TryParse(Cell($"mod{n}max"), out int max);
                    mods.Add((code, Math.Min(min, max), Math.Max(min, max)));
                }

                if (mods.Count == 0)
                    continue;

                string[] kinds = [.. Kinds
                    .Where(kind => Cell(kind.Column) is { Length: > 0 } set && set != "0")
                    .Select(kind => kind.Type)];

                if (kinds.Length == 0)
                    continue;

                _ = int.TryParse(Cell("level"), out int level);
                found.Add(new Row(index, kinds, level, [.. mods]));
            }
        }
        catch (Exception)
        {
            // No rows means a superior item's properties go unexplained, which is where
            // this started rather than something worse.
        }

        _rows = [.. found];
    }
}
