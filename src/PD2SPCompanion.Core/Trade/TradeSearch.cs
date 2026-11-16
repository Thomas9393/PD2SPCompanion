using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using D2SLib.Model.Save;
using D2SLib.Model.TXT;

namespace PD2SPCompanion.Core.Trade;

/// <summary>
/// Listings like an item the player owns, for them to judge a price by - asking prices, not sales,
/// and no averaging: the player names the price.
/// <para>
/// The search is the vault's - the same pickers and property rows, less the dates and folders a
/// listing has none of: <see cref="PrefillFor"/> fills them from
/// the item, every property it has at four fifths of its roll, and <see cref="Search"/> asks the site
/// what they say. Ladder only, softcore or hardcore as the character is.
/// </para>
/// </summary>
public static class TradeSearch
{
    /// <summary>How many listings a search brings back.</summary>
    public const int Count = 10;

    /// <summary>
    /// The vault's search filled in from an item: what <see cref="VaultQuery"/> holds, before it is
    /// a query - the rows still options and rolls, for the pickers to show.
    /// </summary>
    public sealed record Prefill(
        string? Name, int Rarity, int? TypeFrom, int? TypeTo, string? Base,
        bool Ethereal, bool Corrupted, bool Desecrated, int? SocketsMin,
        IReadOnlyList<PrefillProperty> Properties);

    /// <summary>One property row: its option, the skill picked beside it, the bounds, and this item's number.</summary>
    public sealed record PrefillProperty(string Option, int? Skill, double? Min, double? Max, double Roll);

    // ---- the item, as the vault's search ------------------------------------------------------

    /// <summary>
    /// The search to start from. A unique, a set piece or a runeword by its name; anything else by
    /// the narrowest type the vault's picker has for it - and by the base as well where that is all
    /// there is to tell it by: a plain base, or one alone in its item type, as a charm is. Its
    /// rarity, its ethereal, corrupted and desecrated states, its sockets at least, and every
    /// property the site can be asked about.
    /// </summary>
    public static Prefill PrefillFor(Item item)
    {
        bool named = item.IsRuneword || item.Quality is ItemQuality.Unique or ItemQuality.Set;

        int? typeFrom = null, typeTo = null;
        string? code = null;
        if (!named)
        {
            int rank = ItemOrder.TypeRank(item);
            foreach (var span in ItemOrder.TypeSpans()
                         .Where(each => rank >= each.From && rank <= each.To)
                         .OrderBy(each => each.To - each.From)
                         .Take(1))
            {
                (typeFrom, typeTo) = (span.From, span.To);
            }

            // The Type picker's lines are groups - "Charms" is small, large and grand alike - so the
            // base as well where the item's own type has only the one, as a Grand Charm's does.
            if (item.Quality is ItemQuality.Normal or ItemQuality.Superior or ItemQuality.Inferior
                || ItemTypes.TypeOf(item.Code) is { } type && ItemTypes.BasesOf(type).Count == 1)
            {
                code = item.Code.Trim();
            }
        }

        return new Prefill(
            named ? ItemPresenter.Describe(item).Name : null,
            ItemOrder.Rarity(item), typeFrom, typeTo, code,
            item.IsEthereal,
            Corruptions.IsCorrupted(item),
            Corruptions.IsDesecrated(item),
            item.TotalNumberOfSockets > 0 ? item.TotalNumberOfSockets : null,
            PropertiesOf(item));
    }

    /// <summary>The stats that mark a corruption or a desecration: the filters above ask those, not a row.</summary>
    private static readonly HashSet<string> Marks =
        new([Corruptions.FlagStat, Corruptions.IdStat, "desecrated", "desecrator"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every property of the item the site can be asked about, as the vault's property rows, in the
    /// order the tooltip lists them. The item's own <b>and its jewels'</b>, since the site's numbers
    /// include them. The four resistances as All Resistances where they are equal, and the
    /// attributes likewise, as the site writes them; enhanced and flat damage as their composites.
    /// Left out: a property the game never prints, and the corruption's marks, which the Corrupted
    /// and Desecrated pickers ask - as a row, the corruption flag found no one.
    /// </summary>
    private static List<PrefillProperty> PropertiesOf(Item item)
    {
        var stats = item.StatLists.SelectMany(list => list.Stats)
            .Concat(item.SocketedItems.Where(socketed => !socketed.IsCompact)
                        .SelectMany(socketed => socketed.StatLists.SelectMany(list => list.Stats)))
            .Where(stat => stat.Stat is { Length: > 0 })
            .ToList();

        double Sum(string name) => stats.Where(stat => Same(stat.Stat, name)).Sum(stat => (double)stat.Value);
        bool Has(string name) => stats.Any(stat => Same(stat.Stat, name));

        var rows = new Dictionary<(string Option, int? Skill), (int Priority, double Roll)>();
        var order = new List<(string Option, int? Skill)>();
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "item_mindamage_percent" };

        void Add(string option, int? skill, int priority, double roll)
        {
            var key = (option, skill);
            if (rows.TryGetValue(key, out var held))
            {
                rows[key] = (held.Priority, held.Roll + roll);
                return;
            }

            rows[key] = (priority, roll);
            order.Add(key);
        }

        foreach (var (composite, members) in new[]
                 {
                     (PropertyIndex.AllResist, PropertyIndex.ResistStats),
                     (PropertyIndex.AllAttributes, PropertyIndex.AttributeStats),
                 })
        {
            if (members.All(Has) && members.Select(Sum).Distinct().Count() == 1)
            {
                Add(composite, null, Priority(members[0]) + 1, Sum(members[0]));
                done.UnionWith(members);
            }
        }

        if (Has("item_maxdamage_percent"))
        {
            Add(PropertyIndex.EnhancedDamage, null, Priority("item_maxdamage_percent"), Sum("item_maxdamage_percent"));
            done.Add("item_maxdamage_percent");
        }

        // Flat damage is written once for each kind of weapon; one kind's number is the item's.
        foreach (var (composite, kinds) in new[]
                 {
                     (PropertyIndex.MaxDamage, PropertyIndex.MaxDamageKinds),
                     (PropertyIndex.MinDamage, PropertyIndex.MinDamageKinds),
                 })
        {
            if (kinds.FirstOrDefault(Has) is { } kind)
                Add(composite, null, Priority(kind), Sum(kind));
            done.UnionWith(kinds);
        }

        var offered = PropertyIndex.Options.Select(option => option.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var stat in stats)
        {
            string name = stat.Stat!;
            if (done.Contains(name) || Marks.Contains(name) || stat.Value == 0)
                continue;

            var row = D2SLib.Core.TXT.ItemStatCostTXT[name];
            if (row is null || row["descfunc"].ToInt32() == 0)
                continue;
            if ((stat.Id ?? StatCatalogue.IdOf(name)) is { } id && ItemStats.IsInRun(id))
                continue;

            if (PropertyIndex.Place(stat) is { } placed && offered.Contains(placed.Option))
                Add(placed.Option, placed.Skill, row["descpriority"].ToInt32(), placed.Value);
        }

        // By priority, and among equals in the order met - OrderBy is stable.
        return [.. order
            .OrderByDescending(key => rows[key].Priority)
            .Select(key => Band(key.Option, key.Skill, rows[key].Roll))];
    }

    /// <summary>
    /// A row at four fifths of the roll: at least that much, or - for a property where lower is
    /// better, a negative one such as reduced requirements - at most that much. Whole numbers stay
    /// whole and never drop to nothing; a per-level fraction keeps two places.
    /// </summary>
    private static PrefillProperty Band(string option, int? skill, double roll)
    {
        double fifths = roll * 0.8;

        if (roll < 0)
            return new PrefillProperty(option, skill, null, Math.Ceiling(fifths), roll);

        double least = roll >= 1 ? Math.Max(1, Math.Floor(fifths)) : Math.Round(fifths, 2);
        return new PrefillProperty(option, skill, least, null, roll);
    }

    // ---- the vault's search, as the site's -----------------------------------------------------

    /// <summary>
    /// The newest listings meeting <paramref name="query"/>, through <see cref="TradeGate"/> and kept
    /// there ten minutes, so asking the same again is not another request. Its dates, folders and
    /// craft family are not asked: a listing has none of them.
    /// </summary>
    /// <exception cref="TradeGate.TradeGateException">Refused, failed or unreachable.</exception>
    public static async Task<IReadOnlyList<TradeListings.Listing>> Search(VaultQuery query, bool hardcore, CancellationToken cancel = default)
    {
        var pairs = new List<(string Key, string Value)>();

        // Part of the name, any case: the site takes a regular expression, as probed.
        if (query.Name?.Trim() is { Length: > 0 } typed)
        {
            pairs.Add(("item.name[$regex]", Regex.Escape(typed)));
            pairs.Add(("item.name[$options]", "i"));
        }

        if (query.Rarity is { } rarity)
            pairs.AddRange(RarityPairs(rarity));

        if (query.TypeFrom is { } from && query.TypeTo is { } to)
        {
            int at = 0;
            foreach (string type in TypesIn(from, to))
                pairs.Add(($"item.base.type_code[$in][{at++}]", type));
        }

        if (query.Base is { Length: > 0 } code)
            pairs.Add(("item.base_code", code));

        if (query.Ethereal is { } ethereal)
            pairs.Add(("item.is_ethereal", Flag(ethereal)));
        if (query.Corrupted is { } corrupted)
            pairs.Add(("item.corrupted", Flag(corrupted)));
        if (query.Desecrated is { } desecrated)
            pairs.Add(("item.desecrated", Flag(desecrated)));
        if (query.SocketsMin is { } fewest)
            pairs.Add(("item.socket_count[$gte]", Number(fewest)));
        if (query.SocketsMax is { } most)
            pairs.Add(("item.socket_count[$lte]", Number(most)));

        int row = 0;
        foreach (var want in query.Properties ?? [])
            pairs.AddRange(PropertyPairs(want, $"item.modifiers[$all][{row++}][$elemMatch]"));

        return await Ask(pairs, hardcore, cancel);
    }

    /// <summary>
    /// A rarity of the vault's order as the site keeps it: a quality's number, and runewords apart.
    /// A runeword is a normal or superior item underneath, so those two say they are not one.
    /// </summary>
    private static IEnumerable<(string, string)> RarityPairs(int rank) => ItemOrder.Rarities[rank] switch
    {
        "Unique" => [("item.quality.id", "7")],
        "Set" => [("item.quality.id", "5")],
        "Runeword" => [("item.is_runeword", "true")],
        "Crafted" => [("item.quality.id[$in][0]", "8"), ("item.quality.id[$in][1]", "9")],
        "Rare" => [("item.quality.id", "6")],
        "Magic" => [("item.quality.id", "4")],
        "Superior" => [("item.quality.id", "3"), ("item.is_runeword", "false")],
        "Normal" => [("item.quality.id", "2"), ("item.is_runeword", "false"), ("item.is_simple", "false")],
        "Low quality" => [("item.quality.id", "1")],
        _ => [("item.is_simple", "true")],
    };

    /// <summary>The item types a span of the vault's Type order takes in - what the site files a base under.</summary>
    private static IEnumerable<string> TypesIn(int from, int to) =>
        ItemBases.Codes
            .Where(code => ItemOrder.TypeRankOf(code) is var rank && rank >= from && rank <= to)
            .Select(code => ItemTypes.TypeOf(code))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A property row as one <c>$elemMatch</c>. Shapes seen in the cache: four equal resistances are
    /// one <c>all_resist</c>, four equal attributes one <c>all_attributes</c> - so a single one is
    /// asked as itself or its composite, by a regular expression on the name, the site's parser
    /// stopping one level short of an <c>$in</c> there (probed on Arachnid Mesh, whose
    /// resistances are merged, found by Fire Resist). Enhanced damage is <c>maxdamage_percent</c>
    /// with the maximum first; flat damage <c>min_damage</c> / <c>max_damage</c> whatever the weapon;
    /// a property with a parameter [parameter, value]; a chance to cast [level, skill, chance];
    /// charges [level, skill, charges, most]; a per-level stat per level where the game's op is 2.
    /// </summary>
    private static IEnumerable<(string, string)> PropertyPairs(PropertyWant want, string each)
    {
        var (stat, param) = want.IsFamily ? (want.Key.TrimEnd('{'), (int?)null) : PropertyIndex.Split(want.Key);
        int encoding = StatFields.EncodingOf(stat);

        string site = stat;
        int? paramAt = null;
        int valueAt = 0;
        double scale = 1;

        if (Same(stat, PropertyIndex.EnhancedDamage))
            site = "maxdamage_percent";
        else if (PropertyIndex.ResistStats.Contains(stat, StringComparer.OrdinalIgnoreCase))
            site = $"^({stat}|{PropertyIndex.AllResist})$";
        else if (PropertyIndex.AttributeStats.Contains(stat, StringComparer.OrdinalIgnoreCase))
            site = $"^({stat}|{PropertyIndex.AllAttributes})$";
        else if (encoding == 2)
            (paramAt, valueAt) = (1, 2);
        else if (encoding == 3)
            (paramAt, valueAt) = (1, 0);
        else if (D2SLib.Core.TXT.ItemStatCostTXT[stat] is { } row)
        {
            if (StatEncoding.SaveParamBits(row) > 0)
                (paramAt, valueAt) = (0, 1);
            else if (StatText.PerLevelShift(stat) is > 0 and var shift && row["op"].ToInt32() != 2)
                scale = 1 << shift;
        }

        yield return site.StartsWith('^') ? ($"{each}[name][$regex]", site) : ($"{each}[name]", site);

        if (param is { } picked && paramAt is { } at)
            yield return ($"{each}[values.{at}]", Number(picked));
        if (want.Min is { } least)
            yield return ($"{each}[values.{valueAt}][$gte]", (least * scale).ToString(CultureInfo.InvariantCulture));
        if (want.Max is { } highest)
            yield return ($"{each}[values.{valueAt}][$lte]", (highest * scale).ToString(CultureInfo.InvariantCulture));
    }

    private static async Task<IReadOnlyList<TradeListings.Listing>> Ask(
        IReadOnlyList<(string Key, string Value)> pairs, bool hardcore, CancellationToken cancel)
    {
        var query = new StringBuilder("https://api.projectdiablo2.com/market/listing?type=item&is_ladder=true");
        query.Append("&is_hardcore=").Append(hardcore ? "true" : "false");

        foreach (var (key, value) in pairs)
            query.Append('&').Append(key.Replace("$", "%24")).Append('=').Append(Uri.EscapeDataString(value));

        // Feathers' own words, "$" written as %24: newest first, ten, and only what a listing row
        // needs - its price, when it was posted, and the item to draw.
        query.Append("&%24sort[created_at]=-1&%24limit=").Append(Count);
        query.Append("&%24select[]=price&%24select[]=created_at&%24select[]=item");

        return ListingsIn(await TradeGate.Get(query.ToString(), keep: true, cancel));
    }

    /// <summary>
    /// The listings of a query's answer - <c>{ total, data: [ ... ] }</c>. The answer is the network's: one that is no such
    /// thing is refused in words, and a record that is no listing - no object, an <c>_id</c> that is no text, no item - is
    /// passed over.
    /// </summary>
    /// <exception cref="TradeGate.TradeGateException">The answer is not JSON, or not an object.</exception>
    public static List<TradeListings.Listing> ListingsIn(string body)
    {
        var found = new List<TradeListings.Listing>();
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new TradeGate.TradeGateException(L("Trade site answer unreadable."));

            if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var record in data.EnumerateArray())
                {
                    if (record.ValueKind != JsonValueKind.Object
                        || !record.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
                        continue;

                    string id = "";
                    if (record.TryGetProperty("_id", out var listed))
                    {
                        if (listed.ValueKind != JsonValueKind.String)
                            continue;
                        id = listed.GetString() ?? "";
                    }

                    found.Add(new TradeListings.Listing(id, record.Clone(), FromCache: false));
                }
            }
        }
        catch (JsonException problem)
        {
            throw new TradeGate.TradeGateException(L("Trade site answer unreadable."), problem);
        }

        return found;
    }

    private static int Priority(string name) =>
        D2SLib.Core.TXT.ItemStatCostTXT[name]?["descpriority"].ToInt32() ?? 0;

    private static bool Same(string? one, string other) =>
        string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    private static string Flag(bool value) => value ? "true" : "false";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
