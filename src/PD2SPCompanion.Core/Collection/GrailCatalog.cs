using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// What is on the grail: every unique and set piece the season has, made from its row - and every runeword by name,
/// which the Forge lists (the grail itself has no runeword tab).
/// <para>
/// One place, so the Grail page and the home page's grail button count the same entries: the quest items are left
/// out here, and a runeword's rows are gathered here.
/// </para>
/// </summary>
public static class GrailCatalog
{
    /// <summary>What an entry is - with its row, what a copy held is counted under (<see cref="EntryOf"/>).</summary>
    public const string UniqueKind = "unique", SetKind = "set";

    /// <summary>A unique or a set piece on the grail, and the item made from its row to show it.</summary>
    public sealed record Entry(string Kind, int Row, int Order, Item Item)
    {
        /// <summary>A weapon or a piece of armour - what can come ethereal.</summary>
        public bool IsGear => ItemTypes.IsGear(ItemTypes.TypeOf(Item.Code));
    }

    /// <summary>
    /// A runeword on the grail: every row of its name - Spirit is a row for swords and shields and
    /// another for staves - each with an item made to show it. Kept under its first row.
    /// </summary>
    /// <remarks>
    /// A version is one kind of base - weapon, armour or shield - since what the runes add depends
    /// on it: a row made in swords and shields is two versions, each with its runes' own bonus.
    /// </remarks>
    public sealed record RunewordEntry(
        int Row, int Order, IReadOnlyList<(Runewords.Word Word, IReadOnlyList<string> Types, Item Item)> Versions);

    private static List<Entry>? _uniques, _sets;

    /// <summary>
    /// An entry's name as the game shows it, worked out once an entry: a listing or a paste matched one name against
    /// every unique's - some 950 described each time - and storing an item named its entry again. Kept beside the entry, not in it: an entry is a record, compared by what it holds.
    /// </summary>
    public static string ShownName(Entry entry) =>
        ShownNames.GetValue(entry, each => ItemPresenter.Describe(each.Item).Name);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Entry, string> ShownNames = new();

    /// <summary>An entry's index name (<see cref="ItemPresenter.QualityIndex"/>), worked out once an entry.</summary>
    public static string? IndexName(Entry entry) =>
        IndexNames.GetValue(entry, each => new Boxed(ItemPresenter.QualityIndex(each.Item))).Value;

    private sealed record Boxed(string? Value);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Entry, Boxed> IndexNames = new();

    /// <summary>The entries of a list by their row, for the list it was made of.</summary>
    private static Dictionary<int, Entry> ByRow(IReadOnlyList<Entry> list) =>
        RowMaps.GetValue(list, each =>
        {
            var map = new Dictionary<int, Entry>();
            foreach (var entry in each)
                map.TryAdd(entry.Row, entry);
            return map;
        });

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<Entry>, Dictionary<int, Entry>> RowMaps = new();
    private static List<RunewordEntry>? _runewords;

    /// <summary>The install the lists were made under (<see cref="InstalledTables"/>): made again under another.</summary>
    private static int _madeAt = -1;

    /// <summary>Forgets lists made under another install of the tables.</summary>
    private static void Fresh()
    {
        if (_madeAt == InstalledTables.Generation)
            return;

        _uniques = _sets = null;
        _collected = null;
        _cards = null;
        _cardRows = null;
        _runewords = null;
        _crafts = null;
        _madeAt = InstalledTables.Generation;
    }

    /// <summary>
    /// Every enabled unique, by row. By row, not by name: a name with several rows (the eight
    /// Rainbow Facets) is several uniques. Stamped with no era: an entry is shown, never written.
    /// </summary>
    public static IReadOnlyList<Entry> Uniques()
    {
        Fresh();
        return _uniques ??= Made(UniqueKind, ItemForge.EnabledUniques(), row => ItemForge.UniqueAt(row, "").Item);
    }

    public static IReadOnlyList<Entry> Sets()
    {
        Fresh();
        return _sets ??= Made(SetKind, ItemForge.SetItems(), row => ItemForge.SetItemAt(row, "").Item);
    }

    /// <summary>
    /// The uniques the game's Uniques tab has a place for - what the Grail page lists and what may be stored in the
    /// Compendium (screenshots): not the jewels - a section for charms, none for the Rainbow
    /// Facets - and not the unique maps, which have no tab. The Forge lists every unique (<see cref="Uniques"/>).
    /// </summary>
    public static IReadOnlyList<Entry> CollectedUniques()
    {
        Fresh();
        return _collected ??= [.. Uniques().Where(entry =>
        {
            string type = ItemTypes.TypeOf(entry.Item.Code) ?? "";
            var lineage = ItemTypes.Ancestry(type);
            return !lineage.Contains("jewl") && !lineage.Contains("map") && type != UniqueMapType;
        })];
    }

    /// <summary>The unique maps' item type, whose line names no parent map type.</summary>
    private const string UniqueMapType = "t5m";

    private static List<Entry>? _collected;

    /// <summary>
    /// One card of the game's Uniques tab: a name, with every row of it - Arm of King Leoric has two and the tab one card.
    /// <paramref name="Counts"/>: whether it counts towards Discovery - the tab shows the special
    /// uniques the Ubers and dungeon bosses drop, but "Does not count towards discovery completion".
    /// </summary>
    public sealed record UniqueCard(string Name, IReadOnlyList<Entry> Rows, bool Counts);

    /// <summary>
    /// The Uniques tab's cards, in table order: <see cref="CollectedUniques"/> one card a name, those of item level 131
    /// - the eight specials, Itherael's Path to The Third Eye - shown and not counted. 427 counted in S14, the game's
    /// own "0/427" (matched).
    /// </summary>
    public static IReadOnlyList<UniqueCard> UniqueCards()
    {
        Fresh();
        if (_cards is not null)
            return _cards;

        // A unique row's own item level (lvl), by its row as an entry counts it: the marker row not numbered.
        var rows = D2SLib.Core.TXT.ItemsTXT.UniqueTXT?.Rows
            .Where(row => row["index"]?.Value is { Length: > 0 } index && !index.Equals(ItemForge.Marker, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
        int LevelOf(Entry entry) =>
            rows.ElementAtOrDefault(entry.Row)?["lvl"]?.Value is { } lvl && int.TryParse(lvl, out int level) ? level : 0;

        return _cards = [.. CollectedUniques()
            .GroupBy(ShownName)
            .Select(name => new UniqueCard(name.Key, [.. name], name.Any(entry => LevelOf(entry) < SpecialLevel)))];
    }

    private static List<UniqueCard>? _cards;

    /// <summary>
    /// The rows of the card an entry is on: every row of its name for a unique (<see cref="UniqueCards"/>), the row alone
    /// for a set piece - or none where the Grail has no card for it, a jewel, a unique map, a row not enabled. The
    /// Compendium keeps one item a card, of any of its rows (two Arm of King Leoric: both are supported,
    /// only one stored at a time).
    /// </summary>
    public static IReadOnlyList<int> CardRowsOf(string kind, int row)
    {
        if (kind == SetKind)
            return ByRow(Sets()).ContainsKey(row) ? [row] : [];

        Fresh();
        _cardRows ??= UniqueCards()
            .SelectMany(card => card.Rows.Select(entry => (entry.Row, Rows: (IReadOnlyList<int>)[.. card.Rows.Select(each => each.Row)])))
            .ToDictionary(each => each.Row, each => each.Rows);
        return kind == UniqueKind && _cardRows.TryGetValue(row, out var rows) ? rows : [];
    }

    private static Dictionary<int, IReadOnlyList<int>>? _cardRows;

    /// <summary>The item level the specials carry, which nothing drops at: 131 on all eight (Annihilus 130, Hellfire Torch 120).</summary>
    private const int SpecialLevel = 131;

    /// <summary>
    /// Whether an entry drops ethereal as well as plain - what the Forge's roll as a new drop asks.
    /// Gear, and:
    /// <list type="bullet">
    /// <item><b>Not a base with <c>nodurability</c></b> - every bow and crossbow in PD2, and the
    /// Phase Blade. None drops ethereal (0 ethereal among 94 live listings on
    /// five bow and crossbow bases). The one ethereal Phase Blade on the market was cubed up from
    /// an ethereal Crystal Sword or Dimensional Blade, which is the only way to one.</item>
    /// <item><b>Not indestructible</b> - Tyrael's Might, Stormshield, Crown of Ages and the rest
    /// (10 uniques, 5 set pieces in S13) have no ethereal version.</item>
    /// <item><b>Ethereal always</b> - Ethereal Edge, Ghostflame, Shadow Killer, Wraith Flight, Spectral Plate,
    /// whatever else holds. Not Sazabi's Cobalt Redeemer: a set row's ethereal does nothing, and it is indestructible
    /// (Ethereals.Of).</item>
    /// </list>
    /// </summary>
    public static bool HasEtherealDrop(Entry entry) =>
        entry.IsGear && (entry.Item.IsEthereal || !NeverEthereal(entry.Item));

    /// <summary>A base with no durability, or an indestructible item: never dropped ethereal.</summary>
    public static bool NeverEthereal(Item item) =>
        Ethereals.NoDurability(item.Code)
               || ItemStats.Every(item).Any(stat =>
                   string.Equals(stat.Stat, "item_indesctructible", StringComparison.OrdinalIgnoreCase) && stat.Value > 0);

    /// <summary>
    /// Whether a new drop of this item comes ethereal or plain by chance: its base can be ethereal
    /// (<see cref="Ethereals.BaseCanBe"/>), and a unique's or set piece's row neither forbids it (indestructible)
    /// nor makes it always. What the Forge's roll as a new drop draws ethereal on.
    /// </summary>
    public static bool DropsEitherWay(Item item)
    {
        // A drop, not an upgrade: a rare Phase Blade rolled as new comes plain, whatever a cubed-up one may be.
        if (!Ethereals.BaseCanBe(item.Code) || Ethereals.NoDurability(item.Code))
            return false;

        var list = item.Quality switch
        {
            ItemQuality.Unique => Uniques(),
            ItemQuality.Set => Sets(),
            _ => null,
        };
        if (list is null || !ByRow(list).TryGetValue((int)item.FileIndex, out var entry))
            return true;

        return HasEtherealDrop(entry) && !entry.Item.IsEthereal;
    }

    /// <summary>A runeword per name, its versions together.</summary>
    public static IReadOnlyList<RunewordEntry> RunewordEntries()
    {
        Fresh();
        if (_runewords is not null)
            return _runewords;

        var made = new List<RunewordEntry>();

        foreach (var name in Runewords.All.Select((word, row) => (Word: word, Row: row))
                                          .GroupBy(each => Runewords.DisplayName(each.Word)))
        {
            var versions = new List<(Runewords.Word, IReadOnlyList<string>, Item)>();
            foreach (var (word, _) in name)
            {
                // Its types, split by the kind of base each is - gems.txt's three columns.
                foreach (var kind in word.Types
                             .Where(type => ItemForge.BaseFor([type]) is not null)
                             .GroupBy(type => SocketEffects.KindOf(ItemForge.BaseFor([type])!)))
                {
                    IReadOnlyList<string> types = [.. kind];
                    try
                    {
                        versions.Add((word, types, ItemForge.RunewordShown(word, "", types).Item));
                    }
                    catch (Exception)
                    {
                        // A version no base this season can hold is not one that can be found.
                    }
                }
            }

            if (versions.Count > 0)
                made.Add(new RunewordEntry(name.First().Row, made.Count, versions));
        }

        return _runewords = made;
    }

    /// <summary>
    /// A craft recipe, as the crafting wizard's chart draws it.
    /// <paramref name="Item"/> is a plain base of the type, to draw the card with.
    /// </summary>
    public sealed record CraftEntry(AffixRanges.CraftCard Card, int Order, Item Item);

    private static List<CraftEntry>? _crafts;

    /// <summary>
    /// Every craft recipe, in the PD2 wiki's chart order - by type, then by family. The order is the
    /// wiki's, not the table's: <c>cubemain.txt</c> keeps Hitpower first.
    /// </summary>
    public static IReadOnlyList<CraftEntry> CraftEntries()
    {
        Fresh();
        if (_crafts is not null)
            return _crafts;

        var made = new List<CraftEntry>();

        foreach (var card in AffixRanges.CraftCards()
                     .OrderBy(card => ChartIndex(TypeOrder, card.Type))
                     .ThenBy(card => ChartIndex(FamilyOrder, card.Family)))
        {
            string? code = PictureFor(card.Type) ?? card.Showcase;
            if (code is null)
                continue;

            var picture = ItemForge.Material(code, 1, "");

            // A ring or an amulet wears one of several pictures; a plain one made here has no
            // number and fell back to the base's bare file. The first picture, as the forge gives.
            picture.HasMultipleGraphics = ItemTypes.VariantCount(code) > 1;

            made.Add(new CraftEntry(card, made.Count, picture));
        }

        return _crafts = made;
    }

    /// <summary>
    /// The base a recipe's card is drawn with where the user chose one - the classic look - by the
    /// base's name; any other type draws the base its recipe names ("magic heavy belt").
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Pictures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["shld"] = "Monarch",
        ["weap"] = "Long Sword",
        ["tors"] = "Light Plate",
        ["helm"] = "Helm",
    };

    private static string? PictureFor(string type) =>
        Pictures.TryGetValue(type, out string? named)
            ? ItemBases.Codes.FirstOrDefault(code =>
                string.Equals(D2SLib.Core.TXT.ItemsTXT.GetByCode(code)?.Get("name")?.Value?.Trim(), named,
                              StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>The chart's row names - "Chest", "Shield" - where the type table says "Armor", "Any Shield".</summary>
    public static string TypeLabel(string type) => type.ToLowerInvariant() switch
    {
        "amul" => L("Amulet"), "ring" => L("Ring"), "belt" => L("Belt"), "glov" => L("Gloves"), "boot" => L("Boots"),
        "helm" => L("Helm"), "tors" => L("Chest"), "misl" => L("Quiver"), "shld" => L("Shield"), "weap" => L("Weapon"),
        _ => ItemTypes.ShownNameOf(type),
    };

    /// <summary>The wiki chart's rows and columns.</summary>
    public static readonly IReadOnlyList<string> TypeOrder =
        ["amul", "ring", "belt", "glov", "boot", "helm", "tors", "misl", "shld", "weap"];

    public static readonly IReadOnlyList<string> FamilyOrder =
        ["blood", "caster", "hitpower", "safety", "vampiric", "bountiful", "brilliant"];

    private static int ChartIndex(IReadOnlyList<string> order, string value)
    {
        int at = order.ToList().FindIndex(each => each.Equals(value, StringComparison.OrdinalIgnoreCase));
        return at < 0 ? order.Count : at;
    }

    /// <summary>
    /// The grail entry an item is - a unique or set piece, by its row, ethereal or not - or null for one that is none,
    /// or one whose row the grail leaves out. What a copy held is counted under (Vault.GrailCounts).
    /// </summary>
    public static (string Kind, int Row)? EntryOf(Item item)
    {
        if (item.IsCompact)
            return null;

        var list = item.Quality switch
        {
            ItemQuality.Unique => Uniques(),
            ItemQuality.Set => Sets(),
            _ => null,
        };

        return list is not null && ByRow(list).TryGetValue((int)item.FileIndex, out var found)
            ? (found.Kind, found.Row)
            : null;
    }

    /// <summary>
    /// A row per entry. Quest items are left out - "Amulet of the Viper" is the Horadric Staff's
    /// top, not a unique to collect - and so is a row the forge cannot make.
    /// </summary>
    private static List<Entry> Made(string kind, IReadOnlyList<int> rows, Func<int, Item> make)
    {
        var made = new List<Entry>();

        for (int order = 0; order < rows.Count; order++)
        {
            Item item;
            try
            {
                item = make(rows[order]);
            }
            catch (Exception)
            {
                continue;
            }

            if (!ItemBases.IsQuestItem(item.Code))
                made.Add(new Entry(kind, rows[order], order, item));
        }

        return made;
    }
}
