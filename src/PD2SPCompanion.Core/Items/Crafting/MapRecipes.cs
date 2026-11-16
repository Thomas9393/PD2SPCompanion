using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// What the cube does to a map besides corrupting it: the Standard of
/// Heroes, the Orb of Fortification, the Catalyst Shard and the seven Corrupted Ears each write a handful
/// of fixed properties onto a magic, rare or unique map, and the Horadrim Scarab makes a tier-3 map a rare
/// dungeon carrying five more. Read off <c>cubemain.txt</c>, nothing of it written here:
/// <list type="bullet">
/// <item><b>A recipe</b> is a row switched on whose first input is a map's type, whose second is one
/// item, and which hands the map back (<c>useitem</c>) with properties on it.</item>
/// <item><b>A refusal</b> is the same row with <c>op 30</c> and stands before the recipe it guards:
/// <c>map,nor</c> + <c>std</c> is why a white map cannot be made heroic. The first row an item fits
/// decides, as in the cube.</item>
/// <item><b>What says a recipe was used</b> is one stat no affix writes: the one its guard asks
/// (<c>op 18</c>: <c>heroic</c>, <c>treacherous</c>), or with no guard the first it writes
/// (<c>map_glob_skirmish_mode</c>, <c>map_force_event</c>). An ear's value says which ear.</item>
/// </list>
/// Without this a heroic Imperial Palace on the market is flagged for
/// the Standard's +20% density and +10% experience.
/// </summary>
public static class MapRecipes
{
    /// <summary>One thing done to a map with one item.</summary>
    /// <param name="Ingredient">The item that goes in the cube with the map: <c>std</c>, <c>fort</c>, <c>iwss</c>, <c>ivez</c>.</param>
    /// <param name="Wants">The type the map must be: <c>map</c> for any, <c>t1me</c> for exactly tier 1.</param>
    /// <param name="Marker">The stat that says it was done, and <paramref name="MarkerValue"/> what it holds.</param>
    /// <param name="Once">Whether the cube refuses a second (<c>op 18</c> on the marker).</param>
    /// <param name="Refuses">A row that turns the map away rather than changing it.</param>
    public sealed record Recipe(
        string Ingredient, string Wants, IReadOnlyList<string> Conditions,
        string Marker, int MarkerValue, bool Once, bool Refuses, IReadOnlyList<CorruptionGrant> Grants)
    {
        /// <summary>The ingredient's name, which is what the recipe is called: "Standard of Heroes".</summary>
        public string Name => ItemPresenter.BaseName(Ingredient);
    }

    /// <summary>Every recipe and refusal, in the table's order - the order the cube tries them in.</summary>
    private static List<Recipe> _rows = [];

    /// <summary>What a map of a type always carries because of how the cube makes it: a dungeon's five.</summary>
    private static Dictionary<string, CorruptionGrant[]> _madeWith = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsMapType(string type) =>
        type.Equals("map", StringComparison.OrdinalIgnoreCase)
        || ItemTypes.Ancestry(type).Contains("map", StringComparer.OrdinalIgnoreCase);

    private static bool Fits(Item item, Recipe row) =>
        ItemTypes.LineageOfBase(item.Code).Contains(row.Wants, StringComparer.OrdinalIgnoreCase)
        && Corruptions.Meets(item, row.Conditions, item.TotalNumberOfSockets);

    private static IEnumerable<ItemStat> Own(Item item) => item.StatLists.FirstOrDefault()?.Stats ?? [];

    /// <summary>
    /// Whether any row asks for a base of this kind at all - a map's: a question of the code alone, asked before the
    /// whole item is staged to be asked properly (<see cref="Available"/>).
    /// </summary>
    public static bool AnyFor(string code)
    {
        var lineage = ItemTypes.LineageOfBase(code);
        return _rows.Any(row => lineage.Contains(row.Wants, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What may be done to this map: for each ingredient the first row it fits, where that row is a
    /// recipe and not a refusal. Whether it has been done already is not asked - see <see cref="On"/>.
    /// </summary>
    public static IReadOnlyList<Recipe> Available(Item item)
    {
        var found = new List<Recipe>();
        var decided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows)
        {
            if (decided.Contains(row.Ingredient) || !Fits(item, row))
                continue;

            decided.Add(row.Ingredient);
            if (!row.Refuses)
                found.Add(row);
        }

        return found;
    }

    /// <summary>
    /// The recipes this map shows the mark of: its marker stat at the recipe's own number, on a row the
    /// map's type fits. Asked of the type alone and not of the quality - the mark is on the item, however
    /// it came there.
    /// </summary>
    public static IReadOnlyList<Recipe> On(Item item)
    {
        var lineage = ItemTypes.LineageOfBase(item.Code);
        var found = new List<Recipe>();
        var decided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows.Where(row => !row.Refuses))
        {
            if (decided.Contains(row.Ingredient) || !lineage.Contains(row.Wants, StringComparer.OrdinalIgnoreCase))
                continue;

            decided.Add(row.Ingredient);
            if (Own(item).Any(stat => string.Equals(stat.Stat, row.Marker, StringComparison.OrdinalIgnoreCase)
                                      && stat.Value == row.MarkerValue))
            {
                found.Add(row);
            }
        }

        return found;
    }

    /// <summary>
    /// A map's tier as the game prints it under its name, "Tier: 3": the number its base row gives the
    /// <c>MapTier</c> line (<c>calc1</c>). The unique maps have one too. Null for what the table gives
    /// none - a dungeon's line is <c>DungeonTier</c> and carries no number.
    /// </summary>
    public static int? TierOf(string? code)
    {
        if (code?.Trim() is not { Length: > 0 } id || D2SLib.Core.TXT.ItemsTXT.GetByCode(id) is not { } row)
            return null;

        return string.Equals(row.Get("spelldescstr")?.Value?.Trim(), "MapTier", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(row.Get("calc1")?.Value?.Trim(), out int tier) && tier > 0
            ? tier
            : null;
    }

    /// <summary>Whether a base is a dungeon map: the one whose tier line is <c>DungeonTier</c>, with no number.</summary>
    public static bool IsDungeon(string? code) =>
        code?.Trim() is { Length: > 0 } id
        && string.Equals(D2SLib.Core.TXT.ItemsTXT.GetByCode(id)?.Get("spelldescstr")?.Value?.Trim(), "DungeonTier", StringComparison.OrdinalIgnoreCase);

    /// <summary>A map's tier in words, for a list or a line of facts: "Tier 3", or "Dungeon". Null for what is no map.</summary>
    public static string? TierName(string? code) =>
        TierOf(code) is { } tier ? L($"Tier {tier}") : IsDungeon(code) ? L("Dungeon") : null;

    /// <summary>Where a map stands among maps: its tier, a dungeon after the third. Null for what is no map.</summary>
    public static int? TierRank(string? code) => TierOf(code) ?? (IsDungeon(code) ? 4 : null);

    /// <summary>
    /// Whether a stat is the mark one of the cube's map recipes leaves: <c>heroic</c>, <c>treacherous</c>
    /// and the like. A map's own as surely as the `map_` family is - no affix, unique, set or runeword
    /// writes one (by the tables) - though its name does not say so.
    /// </summary>
    public static bool Marks(string? stat) => stat is { Length: > 0 } && _marks.Contains(stat);

    private static HashSet<string> _marks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a map of this base always carries because the cube made it: a dungeon's five properties.</summary>
    public static IReadOnlyList<CorruptionGrant> MadeWith(string? code)
    {
        foreach (string step in ItemTypes.LineageOfBase(code))
        {
            if (_madeWith.TryGetValue(step, out var grants))
                return grants;
        }

        return [];
    }

    /// <summary>
    /// Everything the cube has put on this map outside its affixes and its corruption: each recipe it
    /// shows the mark of, and what its type is made with. A source for the legitimacy check and the
    /// ranges, as an upgrade's mod is.
    /// </summary>
    public static IEnumerable<CorruptionGrant> Grants(Item item)
    {
        if (_rows.Count == 0 && _madeWith.Count == 0)
            return [];

        return [.. On(item).SelectMany(recipe => recipe.Grants), .. MadeWith(item.Code)];
    }

    private static IReadOnlyList<AffixRanges.Wrote> Written(IEnumerable<CorruptionGrant> grants) =>
        [.. grants.SelectMany(grant => AffixRanges.Written(
            grant.Code, grant.Min, grant.Max, grant.Max <= grant.Min ? grant.Min : grant.Max, grant.Param))];

    /// <summary>
    /// Does the recipe to the map: its properties added to the lines the map has, new lines otherwise,
    /// each held at what its field holds. Nothing where the map already shows its mark.
    /// </summary>
    public static bool Apply(Item item, Recipe recipe)
    {
        if (recipe.Refuses || On(item).Any(done => done.Marker.Equals(recipe.Marker, StringComparison.OrdinalIgnoreCase)))
            return false;

        ItemStats.WriteValues(item, Written(recipe.Grants));
        return true;
    }

    /// <summary>
    /// Takes a recipe's properties off again: each fixed number subtracted from its line, and a line left
    /// at nought removed. A line standing at its field's ceiling keeps what it holds - it says nothing of
    /// how much was poured into it (monster rarity is five bits: 31, whatever was asked).
    /// </summary>
    public static bool TakeOff(Item item, Recipe recipe)
    {
        if (!On(item).Contains(recipe) || item.StatLists.FirstOrDefault() is not { } list)
            return false;

        foreach (var wrote in Written(recipe.Grants))
        {
            var line = list.Stats.FirstOrDefault(stat => ItemStats.SameSkill(stat, wrote));
            if (line is null)
                continue;

            bool marker = wrote.Stat.Equals(recipe.Marker, StringComparison.OrdinalIgnoreCase);
            if (!marker && (wrote.SkillId is not null || line.Value >= StatFields.CeilingFor(item, wrote.Stat)))
            {
                if (wrote.SkillId is not null)
                    list.Stats.Remove(line);
                continue;
            }

            int left = line.Value - wrote.Value;
            if (marker || left == 0)
                list.Stats.Remove(line);
            else
                line.Value = left;
        }

        return true;
    }

    /// <summary>
    /// Writes what a map of this base is always made with, at the top of each range - a dungeon the Forge
    /// makes is the scarab's dungeon. Nothing on a base made with nothing.
    /// </summary>
    public static void Dress(Item item)
    {
        if (MadeWith(item.Code) is { Count: > 0 } grants)
            ItemStats.WriteValues(item, Written(grants));
    }

    public static void Load(GameData game)
    {
        var rows = new List<Recipe>();
        var madeWith = new Dictionary<string, CorruptionGrant[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (game.Lines("cubemain") is not { Length: > 0 } lines)
                return;

            string[] header = lines[0].Split('\t');
            int first = Array.IndexOf(header, "input 1");
            int second = Array.IndexOf(header, "input 2");
            int third = Array.IndexOf(header, "input 3");
            int output = Array.IndexOf(header, "output");
            int live = Array.IndexOf(header, "enabled");
            int op = Array.IndexOf(header, "op");
            int param = Array.IndexOf(header, "param");
            if (first < 0 || second < 0 || output < 0)
                return;

            var slots = Enumerable.Range(1, 5)
                .Select(n => (
                    Code: Array.IndexOf(header, $"mod {n}"),
                    Low: Array.IndexOf(header, $"mod {n} min"),
                    High: Array.IndexOf(header, $"mod {n} max"),
                    Spot: Array.IndexOf(header, $"mod {n} param")))
                .Where(slot => slot.Code >= 0 && slot.Low >= 0 && slot.High >= 0)
                .ToArray();

            foreach (string line in lines.Skip(1))
            {
                string[] cells = line.Split('\t');
                string Cell(int at) => at >= 0 && at < cells.Length ? cells[at].Trim().Trim('"') : "";

                if (live >= 0 && Cell(live) != "1")
                    continue;

                string[] made = Cell(output).Split(',');
                string[] map = Cell(first).Split(',');
                string type = map[0].Trim();

                var granted = new List<CorruptionGrant>();
                foreach (var (code, low, high, spot) in slots)
                {
                    if (Cell(code) is not { Length: > 0 } what
                        || what.Equals("corrupt", StringComparison.OrdinalIgnoreCase)
                        || what.Equals("corruptnum", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _ = int.TryParse(Cell(low), out int min);
                    _ = int.TryParse(Cell(high), out int max);
                    var (least, most) = AffixRanges.Ordered(what, min, max);
                    granted.Add(new CorruptionGrant(what, least, most, AffixRanges.ParameterOf(what, Cell(spot))));
                }

                // A map the cube makes of another, with properties of the recipe's own: "t4me,rar".
                string makes = made[0].Trim();
                if (granted.Count > 0 && makes.Length > 0 && IsMapType(makes)
                    && !makes.Equals("useitem", StringComparison.OrdinalIgnoreCase))
                {
                    madeWith.TryAdd(makes, [.. granted]);
                    continue;
                }

                // Done to a map with one item, the map handed back.
                string ingredient = Cell(second).Split(',')[0].Trim();
                bool refuses = Cell(op) == "30";
                if (type.Length == 0 || !IsMapType(type) || ingredient.Length == 0 || Cell(third).Length > 0
                    || ItemBases.Of(ingredient) is null || ItemBases.Of(type) is not null
                    || Corruptions.IsShard(ingredient)
                    || !(refuses || (makes.Equals("useitem", StringComparison.OrdinalIgnoreCase) && granted.Count > 0)))
                {
                    continue;
                }

                // The mark: the stat its guard asks of, or the first it writes.
                string marker = "";
                bool once = Cell(op) == "18";
                if (!refuses)
                {
                    if (once && int.TryParse(Cell(param), out int guarded) && StatCatalogue.NameOf(guarded) is { Length: > 0 } asked)
                        marker = asked;
                    else
                        marker = AffixRanges.StatsWritten(granted[0].Code).FirstOrDefault() ?? "";

                    if (marker.Length == 0)
                        continue;
                }

                int value = granted
                    .Where(grant => AffixRanges.StatsWritten(grant.Code).Contains(marker, StringComparer.OrdinalIgnoreCase))
                    .Select(grant => grant.Min)
                    .FirstOrDefault();

                rows.Add(new Recipe(
                    ingredient, type,
                    [.. map.Skip(1).Select(part => part.Trim()).Where(part => part.Length > 0 && !part.StartsWith("qty", StringComparison.OrdinalIgnoreCase))],
                    marker, value, once, refuses, [.. granted]));
            }
        }
        catch (Exception problem)
        {
            // With nothing read no map is offered a recipe and none is credited with one, as before.
            Log.Caught(problem);
        }
        finally
        {
            // Put in place however the reading ended - the early returns too: a load whose cubemain is missing
            // or renamed its columns kept the last tables' recipes.
            _rows = rows;
            _marks = new(rows.Select(row => row.Marker).Where(mark => mark.Length > 0), StringComparer.OrdinalIgnoreCase);
            _madeWith = madeWith;
        }
    }
}
