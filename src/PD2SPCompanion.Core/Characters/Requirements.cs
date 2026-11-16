using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// What an item asks of whoever uses it - level, strength, dexterity - and whether a character's
/// worn gear gets it, counting what the gear itself gives.
/// <para>
/// Each rule is the tables' where they say, vanilla's where they do not; the vanilla ones are
/// named in <see cref="Need.Unverified"/> so a check can say which answer is still a guess.
/// </para>
/// </summary>
public static class Requirements
{
    /// <summary>An item's requirements, and the rules behind them not yet measured in PD2.</summary>
    public sealed record Need(int Level, int Strength, int Dexterity, IReadOnlyList<string> Unverified)
    {
        public static readonly Need None = new(0, 0, 0, []);
    }

    /// <summary>Strength and dexterity an item gives - its own stats and what is socketed in it.</summary>
    public readonly record struct Bonus(int Strength, int Dexterity)
    {
        public static Bonus operator +(Bonus a, Bonus b) => new(a.Strength + b.Strength, a.Dexterity + b.Dexterity);
        public static Bonus operator -(Bonus a, Bonus b) => new(a.Strength - b.Strength, a.Dexterity - b.Dexterity);
    }

    /// <summary>
    /// One worn item or charm judged against the character: met, or what it lacks - and the gear
    /// bonus that counted toward it (never its own), so each requirement can be told apart.
    /// </summary>
    public sealed record Judged(Item Item, Need Need, bool Met, string? Short, Bonus Helped)
    {
        /// <summary>Each requirement, met or not, for a character of this level and base.</summary>
        public (bool Level, bool Strength, bool Dexterity) Lines(int level, int strength, int dexterity) =>
            (level >= Need.Level, strength + Helped.Strength >= Need.Strength, dexterity + Helped.Dexterity >= Need.Dexterity);
    }

    private static Dictionary<int, int> _uniqueLevels = [];
    private static Dictionary<int, int> _setLevels = [];

    /// <summary>Each automagic row's level, by its one-based id: <c>levelreq</c>, and its class's <c>classlevelreq</c>.</summary>
    private static Dictionary<int, (int Level, string ForClass, int ClassLevel)> _automagicLevels = [];

    /// <summary>A set's own bonuses from <c>sets.txt</c>: by pieces worn (partial), and for the whole set (full).</summary>
    private sealed record SetRow(Dictionary<int, List<(string Code, int Min, int Max, int Param)>> Partial,
                                 List<(string Code, int Min, int Max, int Param)> Full);

    private static Dictionary<string, SetRow> _sets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary><c>itemtypes.txt</c>'s <c>Shoots</c>: the ammunition a type fires - <c>bowq</c> for bows.</summary>
    private static Dictionary<string, string> _shoots = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(GameData game)
    {
        _uniqueLevels = ReadLevels(game, "uniqueitems");
        _setLevels = ReadLevels(game, "setitems");
        _automagicLevels = ReadAutomagicLevels(game);
        _sets = ReadSets(game);
        _pieces = null;
        _shoots = ReadShoots(game);
    }

    private static Dictionary<int, (int, string, int)> ReadAutomagicLevels(GameData game)
    {
        var levels = new Dictionary<int, (int, string, int)>();
        try
        {
            var (table, _) = game.ReadExcel("automagic");
            for (int row = 0; row < table.Rows.Count; row++)
            {
                int.TryParse(table.Value(row, "levelreq").Trim(), out int level);
                int.TryParse(table.Value(row, "classlevelreq").Trim(), out int classLevel);
                levels[row + 1] = (level, table.Value(row, "class").Trim(), classLevel);
            }
        }
        catch (Exception)
        {
            // Without the table an automagic asks nothing - as it was before it was read.
        }

        return levels;
    }

    private static Dictionary<string, string> ReadShoots(GameData game)
    {
        var shoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var (table, _) = game.ReadExcel("itemtypes");
            for (int row = 0; row < table.Rows.Count; row++)
            {
                string code = table.Value(row, "Code").Trim(), ammunition = table.Value(row, "Shoots").Trim();
                if (code.Length > 0 && ammunition.Length > 0)
                    shoots[code] = ammunition;
            }
        }
        catch (Exception)
        {
            // Without the table no weapon is known to need ammunition, so none is marked for it.
        }

        return shoots;
    }

    /// <summary>
    /// Why a worn bow or crossbow cannot be used, or null: it fires what its type's <c>Shoots</c>
    /// names, and the other hand of its weapon set holds none - a quiver of that type, a magic one
    /// counting by its parent. Not a requirement: the game draws it red, and it still counts as
    /// worn - M'avina's Caster, red without arrows, gave its set's full bonus
    /// (screenshots).
    /// </summary>
    public static string? AmmunitionMissing(D2S save, Item item)
    {
        if (item.Mode != ItemMode.Equipped)
            return null;

        string? fires = ItemTypes.Ancestry(ItemTypes.TypeOf(item.Code))
            .Select(type => _shoots.TryGetValue(type, out string? ammunition) ? ammunition : null)
            .FirstOrDefault(ammunition => ammunition is not null);
        if (fires is null)
            return null;

        var other = item.Location switch
        {
            ItemLocation.RightHand => ItemLocation.LeftHand,
            ItemLocation.LeftHand => ItemLocation.RightHand,
            ItemLocation.SwapRight => ItemLocation.SwapLeft,
            ItemLocation.SwapLeft => ItemLocation.SwapRight,
            _ => ItemLocation.None,
        };

        bool loaded = (save.PlayerItemList?.Items ?? [])
            .Any(each => each.Mode == ItemMode.Equipped && each.Location == other
                         && ItemTypes.Ancestry(ItemTypes.TypeOf(each.Code)).Contains(fires));

        return loaded ? null : fires.StartsWith('x') ? L("No bolts") : L("No arrows");
    }

    /// <summary><c>sets.txt</c>: PCode{n}a/b for n pieces worn, FCode1-8 for all of them.</summary>
    private static Dictionary<string, SetRow> ReadSets(GameData game)
    {
        var sets = new Dictionary<string, SetRow>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var (table, _) = game.ReadExcel("sets");
            for (int row = 0; row < table.Rows.Count; row++)
            {
                string name = table.Value(row, "index").Trim();
                if (name.Length == 0)
                    continue;

                (string, int, int, int)? Read(string code, string param, string min, string max)
                {
                    string what = table.Value(row, code).Trim();
                    if (what.Length == 0)
                        return null;

                    _ = int.TryParse(table.Value(row, min).Trim(), out int low);
                    int high = int.TryParse(table.Value(row, max).Trim(), out int top) ? top : low;
                    return (what, low, high, AffixRanges.ParameterOf(what, table.Value(row, param).Trim()));
                }

                var partial = new Dictionary<int, List<(string, int, int, int)>>();
                for (int pieces = 2; pieces <= 5; pieces++)
                {
                    foreach (string half in new[] { "a", "b" })
                    {
                        try
                        {
                            if (Read($"PCode{pieces}{half}", $"PParam{pieces}{half}", $"PMin{pieces}{half}", $"PMax{pieces}{half}") is { } mod)
                                (partial.TryGetValue(pieces, out var list) ? list : partial[pieces] = []).Add(mod);
                        }
                        catch (KeyNotFoundException) { }
                    }
                }

                var full = new List<(string, int, int, int)>();
                for (int n = 1; n <= 8; n++)
                {
                    try
                    {
                        if (Read($"FCode{n}", $"FParam{n}", $"FMin{n}", $"FMax{n}") is { } mod)
                            full.Add(mod);
                    }
                    catch (KeyNotFoundException) { }
                }

                sets[name] = new SetRow(partial, full);
            }
        }
        catch (Exception)
        {
            // Without the table a set gives what its pieces carry, and nothing for the set.
        }

        return sets;
    }

    /// <summary>A unique or set table's <c>lvl req</c> by the FileIndex an item keeps.</summary>
    private static Dictionary<int, int> ReadLevels(GameData game, string name)
    {
        var levels = new Dictionary<int, int>();
        try
        {
            var (table, _) = game.ReadExcel(name);
            int marker = QualityRows.MarkerIn(table);
            for (int row = 0; row < table.Rows.Count; row++)
            {
                int index = QualityRows.IndexOf(row, marker);
                if (index >= 0 && int.TryParse(table.Value(row, "lvl req").Trim(), out int level))
                    levels[index] = level;
            }
        }
        catch (Exception)
        {
            // Without the table a unique or set piece asks only what its base asks.
        }

        return levels;
    }

    /// <summary>A base's column, as a number - 0 where it is blank or the table has no such column (misc has no <c>reqstr</c>).</summary>
    private static int BaseInt(string? code, string column) =>
        int.TryParse(D2SLib.Core.TXT.ItemsTXT.GetByCode(code?.Trim() ?? "")?.Get(column)?.Value?.Trim(), out int value) ? value : 0;

    /// <summary>What <paramref name="item"/> asks, of a character of class <paramref name="classCode"/> (<c>ama</c>...).</summary>
    public static Need Of(Item item, string? classCode)
    {
        var unverified = new List<string>();
        int level = BaseInt(item.Code, "levelreq");

        var affixLevels = MagicAffixes.PairsOn(item)
            .SelectMany(pair => new[] { pair.Prefix, pair.Suffix })
            .OfType<MagicAffixes.AffixOption>()
            .Select(affix => affix.LevelFor(classCode))
            .ToList();

        if (affixLevels.Count > 0)
            level = Math.Max(level, affixLevels.Max());

        // The automagic asks its level too: a magic Targe whose prefix asks 22 and whose Knight's asks 42
        // shows Required Level 42 in game (its tooltip) - left out, the editor would say 22.
        if (item.IsAutoAffix && _automagicLevels.TryGetValue(item.AutoAffixId, out var automagic))
        {
            bool own = automagic.ClassLevel > 0 && automagic.ForClass.Length > 0
                       && string.Equals(automagic.ForClass, classCode, StringComparison.OrdinalIgnoreCase);
            level = Math.Max(level, own ? automagic.ClassLevel : automagic.Level);
        }

        // Crafted: vanilla adds 10 and 3 an affix; PD2 adds nothing. Stone Barb - base 45,
        // affixes up to 69, four of them - asks Required Level 69 in game (its tooltip),
        // where vanilla's rule gave 91.

        var fixedLevels = item.Quality switch
        {
            ItemQuality.Unique => _uniqueLevels,
            ItemQuality.Set => _setLevels,
            _ => null,
        };
        if (fixedLevels is not null && fixedLevels.TryGetValue((int)item.FileIndex, out int fixedLevel))
            level = Math.Max(level, fixedLevel);

        foreach (var socketed in item.SocketedItems)
            level = Math.Max(level, Of(socketed, classCode).Level);

        var stats = OwnStats(item).Concat(item.SocketedItems.SelectMany(socketed => StatsOf(socketed, item))).ToList();
        level += stats.Where(each => each.Stat == "item_levelreq").Sum(each => each.Value);

        // The percentage takes the reduction rounded toward nothing, and ethereal's 10 comes off
        // after it - both read off the game's own tooltips: Veil of Steel,
        // 192 with Hel's -15%, asks 164 (192 - 28), not 163; an ethereal Cuirass, 65 with -20%,
        // asks 42 (65 - 13 - 10).
        int percent = stats.Where(each => each.Stat == "item_req_percent").Sum(each => each.Value);
        int Scaled(int required)
        {
            if (required <= 0)
                return 0;

            int value = required + required * percent / 100;
            if (item.IsEthereal)
                value -= 10;

            return Math.Max(0, value);
        }

        int strength = BaseInt(item.Code, "reqstr"), dexterity = BaseInt(item.Code, "reqdex");

        return new Need(level, Scaled(strength), Scaled(dexterity), unverified);
    }

    /// <summary>An item's own stats and a runeword's.</summary>
    private static IEnumerable<(string Stat, int Value, int Param)> OwnStats(Item item) =>
        OwnRaw(item).Select(stat => (stat.Stat!, stat.Value, ParamOf(stat)));

    /// <summary><see cref="OwnStats"/> as the item holds them - with a cast's or a charge's level, which the three numbers leave out.</summary>
    private static IEnumerable<ItemStat> OwnRaw(Item item)
    {
        if (item.StatLists.Count == 0)
            yield break;

        foreach (var stat in item.StatLists[0].Stats ?? [])
            if (stat.Stat is { Length: > 0 })
                yield return stat;

        if (item.IsRuneword && item.StatLists.Count > 1)
            foreach (var stat in item.StatLists[^1].Stats ?? [])
                if (stat.Stat is { Length: > 0 })
                    yield return stat;
    }

    /// <summary>
    /// The one number that says what a stat is for: the skill a chance to cast or a charge names, else its
    /// parameter - the class of a +class skills, the class and tree of a +tree, the skill of a +skill.
    /// </summary>
    private static int ParamOf(ItemStat stat) => stat.SkillId ?? stat.Param ?? 0;

    /// <summary>
    /// A socketed item's stats: a jewel's own list, or for a gem or rune what <c>gems.txt</c>
    /// gives the host it is in (as the reverted character sheet counted them).
    /// </summary>
    private static IEnumerable<(string Stat, int Value, int Param)> StatsOf(Item socketed, Item host) =>
        RawOf(socketed, host).Select(stat => (stat.Stat!, stat.Value, ParamOf(stat)));

    /// <summary><see cref="StatsOf"/> as stats.</summary>
    private static IEnumerable<ItemStat> RawOf(Item socketed, Item host) =>
        socketed.IsCompact
            ? SocketEffects.StatsIn(socketed.Code, host).Where(gave => gave.Stat is { Length: > 0 })
            : OwnRaw(socketed);

    /// <summary>
    /// The strength and dexterity an item gives, with what is socketed in it, at a character
    /// level - a per-level stat (the stat table's op 2 or 4 on <c>level</c>) as value x level /
    /// 2^param. <b>Set bonuses are not counted</b> - see the class notes.
    /// </summary>
    public static Bonus BonusOf(Item item, int characterLevel) =>
        Tally(OwnStats(item).Concat(item.SocketedItems.SelectMany(socketed => StatsOf(socketed, item))), characterLevel);

    /// <summary>
    /// What a set of worn items and charms gives together: each one's own, and what their sets
    /// add for the pieces among them - a piece's green list for tier k from k + 1 pieces
    /// (<c>add func</c> 2; the one <c>add func</c> 1 piece, Civerb's Ward, whose lists answer to
    /// named partners, is not counted), the set's partial bonuses for n pieces, and its full
    /// bonus with every piece. Pieces are counted once each, however many are worn.
    /// </summary>
    public static Bonus TotalOf(IEnumerable<Item> items, int characterLevel) =>
        TotalOf(items, characterLevel, item => BonusOf(item, characterLevel));

    /// <summary><see cref="TotalOf(IEnumerable{Item}, int)"/> with each item's own bonus from <paramref name="bonusOf"/> - a <see cref="Lot"/>'s, worked out once.</summary>
    private static Bonus TotalOf(IEnumerable<Item> items, int characterLevel, Func<Item, Bonus> bonusOf)
    {
        var all = items.ToList();
        var total = all.Aggregate(default(Bonus), (sum, item) => sum + bonusOf(item));

        return total + Tally(SetStats(all).Select(each => (each.Stat, each.Value, each.Param)), characterLevel);
    }

    /// <summary>
    /// Every stat a lot of worn items and charms gives together - each one's own, what is socketed in it,
    /// and what their sets add (<see cref="SetStats"/>) - with the parameter that says what it is for. What
    /// the skill tree counts its bonus levels from (SkillTips), by the rules the requirements count by.
    /// </summary>
    public static IEnumerable<(string Stat, int Value, int Param)> GearStats(IEnumerable<Item> items) =>
        GearStatsBySource(items).Select(each => (each.Stat, each.Value, each.Param));

    /// <summary>
    /// One stat of some gear's and where it is from: the item that holds it - its own, a runeword's, what is
    /// socketed in it, a set piece's green lines - or, for what a set gives for its pieces together, the set
    /// by its table name and no item. <paramref name="SkillLevel"/> is the level of the skill a chance to cast
    /// or a charge names - 0 for any other stat.
    /// </summary>
    public readonly record struct Sourced(string Stat, int Value, int Param, Item? Item, string? Set, int SkillLevel = 0);

    /// <summary>
    /// <see cref="GearStats"/> with each stat's source - what the character sheet says a number is made of,
    /// for the tooltip over a stat that names its sources. The same stats in the same order: one rule for what counts.
    /// </summary>
    public static IEnumerable<Sourced> GearStatsBySource(IEnumerable<Item> items)
    {
        var all = items.ToList();
        return all.SelectMany(item => OwnRaw(item).Concat(item.SocketedItems.SelectMany(socketed => RawOf(socketed, item)))
                                                  .Select(stat => new Sourced(stat.Stat!, stat.Value, ParamOf(stat), item, null, stat.SkillLevel ?? 0)))
            .Concat(SetStats(all));
    }

    /// <summary>
    /// What the sets among some worn items add for the pieces among them: a piece's green list for tier k
    /// from k + 1 pieces (<c>add func</c> 2; the one <c>add func</c> 1 piece, Civerb's Ward, whose lists
    /// answer to named partners, is not counted), the set's partial bonuses for n pieces, and its full
    /// bonus with every piece. Pieces are counted once each, however many are worn.
    /// </summary>
    private static IEnumerable<Sourced> SetStats(List<Item> all)
    {
        foreach (var set in all.Where(item => item.Quality == ItemQuality.Set)
                     .GroupBy(item => ItemForge.SetOf((int)item.FileIndex) ?? "", StringComparer.OrdinalIgnoreCase))
        {
            if (set.Key.Length == 0)
                continue;

            int worn = set.Select(item => item.FileIndex).Distinct().Count();

            foreach (var piece in set.DistinctBy(item => item.FileIndex))
            {
                int list = 0;
                for (int tier = 1; tier <= 5; tier++)
                {
                    if ((piece.SetItemMask & (1 << (tier - 1))) == 0)
                        continue;

                    list++;
                    if (worn >= tier + 1 && list < piece.StatLists.Count)
                    {
                        foreach (var stat in piece.StatLists[list].Stats ?? [])
                            if (stat.Stat is { Length: > 0 } name)
                                yield return new Sourced(name, stat.Value, ParamOf(stat), piece, null);
                    }
                }
            }

            if (!_sets.TryGetValue(set.Key, out var row))
                continue;

            var mods = row.Partial.Where(pair => pair.Key <= worn).SelectMany(pair => pair.Value).ToList();
            if (worn >= PiecesIn(set.Key))
                mods.AddRange(row.Full);

            foreach (var wrote in mods.SelectMany(mod => AffixRanges.Written(mod.Code, mod.Min, mod.Max, mod.Min, mod.Param)))
                yield return new Sourced(wrote.Stat, wrote.Value, wrote.SkillId ?? wrote.Param ?? 0, null, set.Key);
        }
    }

    /// <summary>
    /// What the rest of a lot of worn items gives each of them, for a lot that is only narrowed: each
    /// item's own bonus worked out once, and each item's help once until the lot changes. Asked afresh,
    /// it was every other item's bonus read again from its stats, two or three times per item per round
    /// - 10 ms of every inventory move here, 18 on a slower PC (measured). The sums are the
    /// same; only the repeats are gone.
    /// </summary>
    private sealed class Lot(List<Item> items, int level)
    {
        private readonly Dictionary<Item, Bonus> _own = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Item, Bonus> _helped = new(ReferenceEqualityComparer.Instance);

        public List<Item> Items { get; } = items;

        /// <summary>What the rest of the lot gives an item - never itself, and its set counted without it.</summary>
        public Bonus Helped(Item item)
        {
            if (!_helped.TryGetValue(item, out var helped))
                _helped[item] = helped = TotalOf(Items.Where(each => !ReferenceEquals(each, item)), level, Own);
            return helped;
        }

        /// <summary>What the whole lot gives.</summary>
        public Bonus Total() => TotalOf(Items, level, Own);

        /// <summary>
        /// Keeps only what a character could put on, one piece after another, from its own stats, by a possible
        /// path: each piece is a step open once what is on already meets its
        /// requirement. Two that hold only each other up have no first step, where narrowing alone called both met.
        /// What gives stats goes on first: an order can never do better than all of those on, so this finds every piece
        /// any order reaches. What takes stats away comes last, and <see cref="Remove"/>'s narrowing answers for it.
        /// </summary>
        public void KeepWhatHasAPath(IReadOnlyDictionary<Item, Need> needs, int strength, int dexterity)
        {
            var on = new List<Item>();

            bool Open(Item item)
            {
                var total = TotalOf(on, level, Own);
                return IsCharm(item) || (strength + total.Strength >= needs[item].Strength && dexterity + total.Dexterity >= needs[item].Dexterity);
            }

            var waiting = Items.Where(item => Own(item) is { Strength: >= 0, Dexterity: >= 0 }).ToList();
            while (waiting.FirstOrDefault(Open) is { } next)
            {
                on.Add(next);
                waiting.Remove(next);
            }

            on.AddRange(Items.Where(item => Own(item) is not { Strength: >= 0, Dexterity: >= 0 }).Where(Open).ToList());
            Remove(Items.Where(item => !on.Contains(item)).ToList());
        }

        /// <summary>The items that fail are out: what the rest gives the others is asked again.</summary>
        public void Remove(List<Item> failing)
        {
            Items.RemoveAll(item => failing.Contains(item));
            _helped.Clear();
        }

        private Bonus Own(Item item)
        {
            if (!_own.TryGetValue(item, out var own))
                _own[item] = own = BonusOf(item, level);
            return own;
        }
    }

    private static Dictionary<string, int>? _pieces;

    /// <summary>How many pieces a set has - what the full bonus asks to be worn.</summary>
    private static int PiecesIn(string set)
    {
        _pieces ??= ItemForge.SetItems()
            .GroupBy(index => ItemForge.SetOf(index) ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return _pieces.TryGetValue(set, out int count) ? count : int.MaxValue;
    }

    /// <summary>Strength and dexterity among some stats, a per-level one times the level.</summary>
    private static Bonus Tally(IEnumerable<(string Stat, int Value, int Param)> stats, int characterLevel)
    {
        int strength = 0, dexterity = 0;

        Gather(stats, characterLevel, (stat, value) =>
        {
            if (stat == "strength")
                strength += value;
            else if (stat == "dexterity")
                dexterity += value;
        });

        return new Bonus(strength, dexterity);
    }

    /// <summary>
    /// Some stats handed on one by one, each as it is written and - a per-level one (the stat table's op 2 or
    /// 4 on <c>level</c>) - as what it comes to at a character level, value x level / 2^param, under the stats
    /// it is paid into.
    /// </summary>
    private static void Gather(IEnumerable<(string Stat, int Value, int Param)> stats, int characterLevel, Action<string, int> count)
    {
        foreach (var (stat, value, _) in stats)
        {
            count(stat, value);

            foreach (var (target, gained) in PaidByLevel(stat, value, characterLevel))
                count(target, gained);
        }
    }

    /// <summary>
    /// What a per-level stat (the stat table's op 2 or 4 on <c>level</c>) comes to at a character level - value
    /// x level / 2^param - under each stat it is paid into; nothing for any other stat. And one paid by the ethereal
    /// items worn (op base <c>equipped_eth</c>: Purgatory's "+60% Enhanced Damage per equipped ethereal item"), by
    /// <paramref name="etherealsWorn"/>. Left out, the sheet counted none of it.
    /// </summary>
    public static IEnumerable<(string Stat, int Value)> PaidByLevel(string stat, int value, int characterLevel, int etherealsWorn = 0)
    {
        var row = D2SLib.Core.TXT.ItemStatCostTXT[stat];
        if (row is null || row["op"].ToInt32() is not (2 or 4))
            yield break;

        int by = row["op base"].Value?.Trim() switch
        {
            "level" => characterLevel,
            "equipped_eth" => etherealsWorn,
            _ => -1,
        };
        if (by < 0)
            yield break;

        int gained = value * by >> row["op param"].ToInt32();
        foreach (string column in new[] { "op stat1", "op stat2", "op stat3" })
            if (row[column].Value?.Trim() is { Length: > 0 } target)
                yield return (target, gained);
    }

    /// <summary>
    /// What a stat paid by the ethereal items worn (op base <c>equipped_eth</c>) comes to for a holder wearing
    /// <paramref name="etherealsWorn"/> of them, or null for any other stat. The game's tooltip says the total -
    /// Purgatory's "+300% Enhanced Damage (based on Equipped Ethereals)" with five worn, and the same lifted into the
    /// inventory (AuraTest).
    /// </summary>
    public static int? PaidByEthereals(string stat, int value, int etherealsWorn) =>
        D2SLib.Core.TXT.ItemStatCostTXT[stat] is { } row && row["op base"].Value?.Trim() == "equipped_eth"
            ? PaidByLevel(stat, value, 0, etherealsWorn).Select(paid => (int?)paid.Value).FirstOrDefault()
            : null;

    /// <summary>The ethereal items worn among the gear that counts - the one a per-ethereal stat is on counted with them.</summary>
    public static int EtherealsWorn(IEnumerable<Item> gear) =>
        gear.Count(item => item.Mode == ItemMode.Equipped && item.IsEthereal);

    /// <summary>
    /// What some gear's stats (<see cref="GearStats"/>) come to for each of the four attributes at a character
    /// level - strength, dexterity, vitality and energy, by the save's names - a per-level stat counted as the
    /// requirements count it. What the character page shows after an attribute's own points
    /// (strength 30 + 20).
    /// </summary>
    public static IReadOnlyDictionary<string, int> AttributesFrom(IEnumerable<(string Stat, int Value, int Param)> stats, int characterLevel)
    {
        var sums = AttributeEditor.Editable.ToDictionary(stat => stat, _ => 0, StringComparer.Ordinal);
        Gather(stats, characterLevel, (stat, value) =>
        {
            if (sums.ContainsKey(stat))
                sums[stat] += value;
        });
        return sums;
    }

    /// <summary>
    /// The gear a character has the use of: what is worn in hand and the charms in the inventory
    /// (<see cref="Counted"/>), less what its level, strength or dexterity does not meet - the game gives
    /// nothing for an item it marks red (<see cref="Judge"/>).
    /// </summary>
    public static IReadOnlyList<Item> InUse(D2S save)
    {
        int strength = Saves.AttributeScale.Display("strength", save.Attributes.Stats.GetValueOrDefault("strength"));
        int dexterity = Saves.AttributeScale.Display("dexterity", save.Attributes.Stats.GetValueOrDefault("dexterity"));
        return [.. Judge(save, WeaponSets.InHand, strength, dexterity).Where(judged => judged.Met).Select(judged => judged.Item)];
    }

    /// <summary>Whether an item is a charm - counted from the inventory, needing only its level.</summary>
    public static bool IsCharm(Item item) => ItemTypes.Ancestry(ItemTypes.TypeOf(item.Code)).Contains("char");

    /// <summary>
    /// What counts toward a character with weapon set <paramref name="set"/> in hand - 0 the main slots'
    /// pair, 1 the swap slots' (<see cref="WeaponSets"/>: in the game it is always the main slots'; the swap
    /// pair is asked for to judge it as it would be in hand): every worn piece but the other pair, and the
    /// charms in the inventory that work (<see cref="CharmWorks"/>).
    /// </summary>
    public static IReadOnlyList<Item> Counted(D2S save, int set)
    {
        var counted = new List<Item>();
        var size = CharmArea.InventoryOf(save.ClassId);
        var inventory = (save.PlayerItemList?.Items ?? [])
            .Where(item => item.Mode == ItemMode.Stored && item.Page == InventoryLayout.InventoryPage)
            .ToList();
        foreach (var item in save.PlayerItemList?.Items ?? [])
        {
            bool counts = item.Mode == ItemMode.Equipped
                ? item.Location switch
                {
                    ItemLocation.RightHand or ItemLocation.LeftHand => set == 0,
                    ItemLocation.SwapRight or ItemLocation.SwapLeft => set == 1,
                    ItemLocation.None => false,
                    _ => true,
                }
                : item.Mode == ItemMode.Stored && item.Page == InventoryLayout.InventoryPage && IsCharm(item)
                  && CharmWorks(item, inventory, size);

            if (counts)
                counted.Add(item);
        }

        return counted;
    }

    /// <summary>
    /// Whether a charm in the inventory works, by PD2's rules (the wiki's General Changes: "inventory (10x8, still only 10x4 available for active
    /// charms)", "still only 1 of each charm can be active at a time"):
    /// <list type="bullet">
    /// <item>wholly in the charm area (<see cref="CharmArea.Holds"/>, the bottom half) - the area the page already
    /// draws and the idle mark already says, which the totals did not ask;</item>
    /// <item>one of each unique charm - the first in the save's order, whichever is the stronger: two Annihilus, +60 and +30
    /// Vitality, read 85 with the +60 first and 55 with the +30 first (SeqPala, screenshots);</item>
    /// <item>identified, as vanilla's rule for any item: a charm reads nothing until Cain has seen it (SeqPala).</item>
    /// </list>
    /// A second Annihilus counted in full, and a charm in the top rows gave its lines
    /// (ShockSacri: an unidentified Bronze Small Charm at row 1 counted its 2 attack rating). The area and the one of
    /// each are <see cref="CharmArea.IdleAt"/>'s, which the tiles' red mark asks too. Without the tables (no inventory
    /// size) neither is asked.
    /// </summary>
    private static bool CharmWorks(Item item, IReadOnlyList<Item> inventory, GridSize? size) =>
        item.IsIdentified
        && (size is null || CharmArea.IdleAt(item, inventory, size, item.Y, ItemMover.Footprint(item).Height) == CharmIdle.None);

    /// <summary>
    /// Every worn item and charm for weapon set <paramref name="set"/>, judged against base
    /// <paramref name="strength"/> and <paramref name="dexterity"/>. With
    /// <paramref name="countGear"/> the gear's own strength and dexterity help - never an item's
    /// own toward itself, and never from an item whose requirement is not met: only what has a way on
    /// from the character's own stats counts (<see cref="Lot.KeepWhatHasAPath"/>), and the lot is
    /// narrowed until nothing more drops out.
    /// </summary>
    public static IReadOnlyList<Judged> Judge(D2S save, int set, int strength, int dexterity, bool countGear = true)
    {
        string? classCode = CharacterClasses.CodeOf(save.ClassId);
        int level = save.Level;

        var counted = Counted(save, set);
        var needs = counted.ToDictionary(item => item, item => Of(item, classCode), (System.Collections.Generic.IEqualityComparer<D2SLib.Model.Save.Item>)ReferenceEqualityComparer.Instance);

        // Level cannot be helped; what fails it is out from the start.
        var lot = new Lot(counted.Where(item => needs[item].Level <= level).ToList(), level);
        var active = lot.Items;

        // What the rest of the lot gives an item - never itself, and its set counted without it.
        Bonus Helped(Item item) => countGear ? lot.Helped(item) : default;

        if (countGear)
            lot.KeepWhatHasAPath(needs, strength, dexterity);

        while (true)
        {
            var failing = active
                .Where(item => !IsCharm(item) && Helped(item) is var helped
                               && (strength + helped.Strength < needs[item].Strength
                                   || dexterity + helped.Dexterity < needs[item].Dexterity))
                .ToList();

            if (failing.Count == 0)
                break;

            lot.Remove(failing);
        }

        var finalTotal = countGear ? lot.Total() : default;

        return
        [
            .. counted.Select(item =>
            {
                var need = needs[item];
                if (active.Contains(item))
                    return new Judged(item, need, true, null, Helped(item));

                // Out of the lot, so all the lot gives helps it - and still falls short: the
                // lot only shrank while it was being narrowed.
                string? why = need.Level > level ? L($"Level {need.Level}")
                    : strength + finalTotal.Strength < need.Strength ? L($"Strength {need.Strength}")
                    : dexterity + finalTotal.Dexterity < need.Dexterity ? L($"Dexterity {need.Dexterity}")
                    : L("Requirements not met");
                return new Judged(item, need, false, why, finalTotal);
            }),
        ];
    }

    /// <summary>
    /// What the mercenary wears, judged against its own level and its strength and dexterity at
    /// that level (<see cref="Hirelings.StatsOf"/>): the same two rules as the character's - its
    /// gear helps, never an item toward itself, nothing from an item it cannot use - over its own
    /// items alone. Empty with no mercenary, or one of a kind the table does not know.
    /// </summary>
    public static IReadOnlyList<Judged> JudgeMercenary(D2S save, int level)
    {
        var worn = (save.MercenaryItemList?.ItemList?.Items ?? []).Where(item => item.Mode == ItemMode.Equipped).ToList();
        if (save.Mercenary is not { Id: not 0 } hired
            || Hirelings.StatsOf(hired.TypeId, level, save.Status.IsExpansion) is not { } stats)
        {
            return [];
        }

        var needs = worn.ToDictionary(item => item, item => Of(item, null), (System.Collections.Generic.IEqualityComparer<D2SLib.Model.Save.Item>)ReferenceEqualityComparer.Instance);
        var lot = new Lot(worn.Where(item => needs[item].Level <= level).ToList(), level);
        var active = lot.Items;

        Bonus Helped(Item item) => lot.Helped(item);

        lot.KeepWhatHasAPath(needs, stats.Strength, stats.Dexterity);

        while (true)
        {
            var failing = active.Where(item => Helped(item) is var helped
                                               && (stats.Strength + helped.Strength < needs[item].Strength
                                                   || stats.Dexterity + helped.Dexterity < needs[item].Dexterity)).ToList();
            if (failing.Count == 0)
                break;

            lot.Remove(failing);
        }

        var finalTotal = lot.Total();
        return
        [
            .. worn.Select(item =>
            {
                var need = needs[item];
                if (active.Contains(item))
                    return new Judged(item, need, true, null, Helped(item));

                string why = need.Level > level ? L($"Level {need.Level}")
                    : stats.Strength + finalTotal.Strength < need.Strength ? L($"Strength {need.Strength}")
                    : L($"Dexterity {need.Dexterity}");
                return new Judged(item, need, false, why, finalTotal);
            }),
        ];
    }

    /// <summary>
    /// The least base strength and dexterity that let every worn item of both weapon sets be
    /// used, each set with its own gear - Refit's target. Without <paramref name="countGear"/>
    /// the base alone must meet each item. With it, the gear helps <b>as it is put on, one piece at
    /// a time</b> (<see cref="PutOnOneByOne"/>): a piece goes on when the base and what is already worn meet
    /// it, and then helps the next. Of the bases that let every piece go on, the one with the fewest
    /// points in all - strength and dexterity together - and of those the least strength. Items whose
    /// level is too high are left out, and their bonus with them.
    /// <para>
    /// Each item is not counted helped by every other at once: two pieces that only
    /// go on with each other's bonus - each needing what the other gives - would be taken to help each
    /// other on, which no order of putting them on does: the target would come out short and one stay red.
    /// </para>
    /// <para>
    /// <paramref name="from"/> counts the points from a base the character has, where only what it lacks costs:
    /// a base that already lets every piece go on costs nothing, and no point moves for a smaller total elsewhere.
    /// </para>
    /// </summary>
    public static (int Strength, int Dexterity) Least(D2S save, bool countGear, (int Strength, int Dexterity)? from = null)
    {
        string? classCode = CharacterClasses.CodeOf(save.ClassId);
        int level = save.Level;

        var sets = new[] { 0, 1 }
            .Select(set => Counted(save, set).Where(item => Of(item, classCode).Level <= level).ToList())
            .ToList();
        var needs = sets.SelectMany(items => items).Distinct(ReferenceEqualityComparer.Instance).Cast<Item>()
            .ToDictionary(item => item, item => Of(item, classCode), (IEqualityComparer<Item>)ReferenceEqualityComparer.Instance);

        // The base alone: each item's own need, the most of them.
        int most = 0, mostDexterity = 0;
        foreach (var item in needs.Keys.Where(item => !IsCharm(item)))
        {
            most = Math.Max(most, needs[item].Strength);
            mostDexterity = Math.Max(mostDexterity, needs[item].Dexterity);
        }

        if (!countGear)
            return (most, mostDexterity);

        var own = new Dictionary<Item, Bonus>(ReferenceEqualityComparer.Instance);
        Bonus Own(Item item) => own.TryGetValue(item, out var bonus) ? bonus : own[item] = BonusOf(item, level);

        bool Fits(int strength, int dexterity) =>
            sets.All(items => PutOnOneByOne(items, needs, level, strength, dexterity, Own));

        int Points(int strength, int dexterity) => from is { } held
            ? Math.Max(0, strength - held.Strength) + Math.Max(0, dexterity - held.Dexterity)
            : strength + dexterity;

        // The base alone always does; for each strength up to it, the least dexterity that does (more never
        // hurts: a piece put on only adds), and the pair with the fewest points.
        (int Strength, int Dexterity) best = (most, mostDexterity);
        for (int strength = 0; strength <= most; strength++)
        {
            if (!Fits(strength, mostDexterity))
                continue;

            int low = 0, high = mostDexterity;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (Fits(strength, middle))
                    high = middle;
                else
                    low = middle + 1;
            }

            if (Points(strength, low) < Points(best.Strength, best.Dexterity))
                best = (strength, low);
        }

        return best;
    }

    /// <summary>
    /// Whether every piece of <paramref name="items"/> can be put on with this base, one at a time: what needs only
    /// a level - a charm - is on from the start; then, again and again, whatever piece the base and what is on
    /// already meet goes on, and helps the next - Stormshield's strength putting on a heavier armour, the order the
    /// player would take. Never a piece toward itself.
    /// </summary>
    private static bool PutOnOneByOne(
        List<Item> items, Dictionary<Item, Need> needs, int level, int strength, int dexterity, Func<Item, Bonus> own)
    {
        var on = items.Where(IsCharm).ToList();
        var waiting = items.Where(item => !IsCharm(item)).ToList();

        while (waiting.Count > 0)
        {
            var worn = TotalOf(on, level, own);
            var goes = waiting.Where(item => strength + worn.Strength >= needs[item].Strength
                                             && dexterity + worn.Dexterity >= needs[item].Dexterity).ToList();
            if (goes.Count == 0)
                return false;

            on.AddRange(goes);
            waiting.RemoveAll(goes.Contains);
        }

        return true;
    }
}
