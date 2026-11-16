using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// What the Forge is asked to make: four choices - a type, a base, a
/// rarity, and for a unique, a set piece or a runeword which one - each narrowing the others, and the item
/// they come to. The page's whole logic, with no page in it, so every order of choosing can be run against
/// the tables (ForgeSearchTests).
/// <para>
/// <b>How the four hang together.</b> The type lists the bases. The base lists the rarities it can be
/// (a ring is never Normal) and, with the type, the names. A runeword chosen lists the bases that take
/// it - with no type chosen too, which is the one time a base is offered without one - so the word is
/// chosen first and its base after, or the other way round. A choice the others no longer allow is let
/// go: a base outside a new type, a name not made on a new base; a rarity a base cannot be falls to
/// Normal, else Magic, else the first it can be - a base that exists only as its unique is Unique. One
/// name left is chosen by itself.
/// </para>
/// </summary>
public sealed class ForgeSearch
{
    /// <summary>
    /// One line of the type, base or rarity box: what it says, and what it stands for - a span of type
    /// ranks, a base's code, a rarity's rank. "Any" stands for nothing.
    /// </summary>
    /// <param name="Tier">A base's tier - Normal, Exceptional, Elite - where it has one: said beside its name.</param>
    public sealed record Choice(string Label, int? From = null, int? To = null, string? Code = null, string Tier = "")
    {
        public bool IsAny => From is null && Code is null;

        /// <summary>The label in the language in use; a base's name is the game's own already.</summary>
        public string Shown => Code is null ? L(Label) : Label;

        /// <summary>
        /// The tier in the language in use, or nothing. A map has one of its own - "Tier 3", "Dungeon" - said
        /// in the same place.
        /// </summary>
        public string TierShown => Tier.Length > 0 ? L(Tier) : MapRecipes.TierName(Code) ?? "";

        public bool HasTier => TierShown.Length > 0;

        public override string ToString() => Shown;
    }

    /// <summary>
    /// One thing the Forge can name - a unique, a set piece, a runeword in one kind of base - or a base,
    /// which the search finds too.
    /// </summary>
    /// <param name="Detail">The base it is on, the kinds of base a runeword takes, or a base's tier.</param>
    /// <param name="Rarity">Its place in <see cref="ItemOrder.Rarities"/>; a base is listed as Normal.</param>
    /// <param name="Code">The base's code; none for a runeword, which is made in any base of its kinds.</param>
    /// <param name="Row">A unique's or a set piece's table row.</param>
    /// <param name="Word">A runeword's row.</param>
    /// <param name="Types">
    /// The kinds of base this entry of the word is made in. A word made in swords and in shields is two
    /// entries - what its runes add depends on the kind - and each takes only its own bases.
    /// </param>
    /// <param name="Sample">An item to colour the name by, as the Grail shows it.</param>
    public sealed record Entry(
        string Name, string Detail, int Rarity, string? Code = null, int Row = -1, Runewords.Word? Word = null,
        IReadOnlyList<string>? Types = null, Item? Sample = null)
    {
        public bool HasDetail => Detail.Length > 0;

        /// <summary>
        /// Which of the several rows of its name this is, in a few words - "Battle Cry on striking",
        /// "Skeleton Warriors", "Lightning die facet" - or nothing for a name with one row (ToldApart).
        /// </summary>
        public string Variant { get; init; } = "";

        public bool HasVariant => Variant.Length > 0;

        /// <summary>A runeword's kinds are a list that runs long: said on a line of its own.</summary>
        public bool IsWord => Word is not null;

        public override string ToString() => Name;
    }

    /// <summary>What the four choices come to: nothing to make is no order at all.</summary>
    public sealed record Order(int Rarity, Entry? Named, string? Code);

    public const int Unique = 0, Set = 1, Runeword = 2, Crafted = 3, Rare = 4, Magic = 5, Superior = 6, Normal = 7;

    public static readonly Choice Any = new(N("Any"));

    /// <summary>The rarities the Forge makes, in the order every list of them has. Not low quality, not what has none.</summary>
    public static readonly IReadOnlyList<Choice> AllRarities =
        [.. ItemOrder.Rarities.Take(Normal + 1).Select((label, rank) => new Choice(label, From: rank, To: rank))];

    private List<Entry> _uniques = [], _sets = [], _runewords = [], _bases = [];

    /// <summary>Every base the Forge makes, with what a runeword asks of it: its lineage and its sockets.</summary>
    private readonly Dictionary<string, (int Rank, HashSet<string> Lineage, int Sockets, Choice Line)> _made =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, HashSet<int>> _allowed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The lists read from the tables in use: every base the Forge makes, every unique, set piece and runeword.</summary>
    public void Load()
    {
        _made.Clear();
        _allowed.Clear();

        // On a base the Forge makes.
        _uniques = [.. GrailCatalog.Uniques().Select(entry => Listed(entry, Unique)).Where(entry => ItemForge.Makes(entry.Code!))];
        _sets = [.. GrailCatalog.Sets().Select(entry => Listed(entry, Set)).Where(entry => ItemForge.Makes(entry.Code!))];

        foreach (string code in ItemBases.Codes.Where(ItemForge.Makes))
        {
            string name = ForgeScope.Label(code);
            if (name.Length == 0 || name.Equals("Not used", StringComparison.OrdinalIgnoreCase))
                continue;

            // A base that is its unique and nothing else, with no mark of the table's to tell it by, is
            // told by the unique: the eight unique maps are all "Map" to the game.
            if (ForgeScope.OnlyNamed(code) && ForgeScope.Mark(code).Length == 0
                && _uniques.Where(entry => string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase))
                           .Select(entry => entry.Name).Distinct().ToList() is [var only])
            {
                name = $"{name} ({only})";
            }

            _made[code] = (ItemOrder.TypeRankOf(code),
                new HashSet<string>(ItemTypes.LineageOfBase(code), StringComparer.OrdinalIgnoreCase),
                ItemBases.MaximumSockets(new Item { Code = code.PadRight(4, ' '), ItemLevel = 99 }),
                new Choice(name, Code: code, Tier: ItemTier.Of(code) ?? ""));
        }

        // And only on one that got a line: a unique listed on a base that is not chose nothing (the every-order
        // tests show it: a Forge that made no maps listed Warlord of Blood).
        _uniques.RemoveAll(entry => !_made.ContainsKey(entry.Code!));
        _sets.RemoveAll(entry => !_made.ContainsKey(entry.Code!));
        ToldApart(_uniques);
        ToldApart(_sets);
        _runewords =
        [
            .. GrailCatalog.RunewordEntries()
                .SelectMany(entry => entry.Versions)
                .Select(version => new Entry(
                    Runewords.DisplayName(version.Word),
                    string.Join(", ", version.Types.Select(GrailCatalog.TypeLabel)),
                    Runeword, Word: version.Word, Types: version.Types, Sample: version.Item))
                .Where(entry => _made.Keys.Any(code => Takes(entry, code))),
        ];
        _bases =
        [
            .. _made
                .Select(each => new Entry(each.Value.Line.Label, each.Value.Line.TierShown.Length > 0 ? each.Value.Line.TierShown : TypeAt(each.Value.Rank).Shown, Normal, Code: each.Key))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
        ];

        Types =
        [
            Any,
            .. ItemOrder.TypeSpans()
                .Where(span => _made.Values.Any(each => each.Rank >= span.From && each.Rank <= span.To))
                .Select(span => new Choice(span.Label, span.From, span.To)),
        ];

        if (!Types.Contains(Type))
            Type = Any;

        Settle();
    }

    private static Entry Listed(GrailCatalog.Entry entry, int rarity) =>
        new(ItemPresenter.Describe(entry.Item).Name, ForgeScope.Label(entry.Item.Code.Trim()), rarity,
            Code: entry.Item.Code.Trim(), Row: entry.Row, Sample: entry.Item);

    /// <summary>
    /// The game has names with several rows, each of which drops: Cyclopean Roar casts Battle Cry on striking
    /// or on casting, Arm of King Leoric raises warriors or archers, Akarat's Devotion two ways, Rainbow Facet
    /// eight - an element, and on death or on a level gained. Listed by row they stood as one name on one base
    /// twice over, and read as a mistake. Each says its variant
    /// after its name (<see cref="Entry.Variant"/>), the base still at the right: "Roar - Variant: proc on
    /// striking/casting", the skill named, and a facet by what players call it ("lightning die facet" rather than
    /// the skill on death).
    /// <para>
    /// Worked out from the rows, so a season that adds such a name is followed: the line a row alone has among
    /// those of its name; of a chance to cast, the skill and when - "Battle Cry on striking"; of any other,
    /// what is left once the words the rows share at its start are cut, with the word before it where that is
    /// one word - "Skeleton Warriors". A jewel that casts on death or on a level gained and gives one element
    /// is the element and the trigger.
    /// </para>
    /// </summary>
    private static void ToldApart(List<Entry> entries)
    {
        foreach (var same in entries.GroupBy(entry => (entry.Name, entry.Code)).Where(group => group.Count() > 1).ToList())
        {
            var lines = same.ToDictionary(
                entry => entry,
                entry => entry.Sample is { StatLists.Count: > 0 } sample ? ItemLines.Worded(sample.StatLists[0].Stats) : [],
                (IEqualityComparer<Entry>)ReferenceEqualityComparer.Instance);

            // The line each alone has.
            var own = same.ToDictionary(
                entry => entry,
                entry => lines[entry].FirstOrDefault(line =>
                    same.All(other => ReferenceEquals(other, entry) || !lines[other].Contains(line, StringComparer.OrdinalIgnoreCase))),
                (IEqualityComparer<Entry>)ReferenceEqualityComparer.Instance);

            // The words those lines share at their start.
            var words = own.Values.Where(line => line is not null).Select(line => line!.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
            int shared = 0;
            while (words.Count > 1 && words.All(line => line.Length > shared + 1)
                   && words.All(line => line[shared].Equals(words[0][shared], StringComparison.OrdinalIgnoreCase)))
            {
                shared++;
            }

            foreach (var entry in same)
            {
                string? variant = Trigger(entry) ?? Cast(own[entry]) ?? Rest(own[entry], shared);
                if (variant is { Length: > 0 })
                    entries[entries.IndexOf(entry)] = entry with { Variant = variant };
            }
        }

        // A jewel that casts when its wearer dies or gains a level, of one element: "Lightning die facet".
        static string? Trigger(Entry entry)
        {
            var stats = entry.Sample?.StatLists.FirstOrDefault()?.Stats ?? [];
            bool dies = stats.Any(stat => stat.Stat == "item_skillondeath"), levels = stats.Any(stat => stat.Stat == "item_skillonlevelup");
            if (dies == levels)
                return null;

            string? element = stats.Select(stat => stat.Stat ?? "").Select(name =>
                    name.Contains("_ltng_", StringComparison.Ordinal) ? L("Lightning")
                    : name.Contains("_cold_", StringComparison.Ordinal) ? L("Cold")
                    : name.Contains("_fire_", StringComparison.Ordinal) ? L("Fire")
                    : name.Contains("_pois_", StringComparison.Ordinal) ? L("Poison")
                    : null)
                .FirstOrDefault(found => found is not null);
            if (element is null)
                return null;

            return dies ? L($"{element} die facet") : L($"{element} level-up facet");
        }

        // "8% Chance to cast level 6 Battle Cry on striking": the skill, and when.
        static string? Cast(string? line)
        {
            if (line is null)
                return null;

            var found = System.Text.RegularExpressions.Regex.Match(line, @"^\d+% .*\blevel \d+ (?<what>.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return found.Success ? found.Groups["what"].Value : null;
        }

        // What is left of a line once the words the rows share at its start are cut - and a number leading it.
        static string? Rest(string? line, int shared)
        {
            if (line is null)
                return null;

            var all = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var rest = all.Skip(shared).SkipWhile(word => word.TrimStart('+', '-').TrimEnd('%').All(char.IsDigit)).ToList();
            if (rest.Count == 0)
                return line;

            if (rest.Count == 1 && shared > 0)
                rest.Insert(0, all[shared - 1]);
            return string.Join(' ', rest);
        }
    }

    /// <summary>The narrowest of the types that holds a rank: "Weapons: Polearms", not "Weapons".</summary>
    private Choice TypeAt(int rank) =>
        ItemOrder.TypeSpans()
            .Where(span => span.From <= rank && span.To >= rank)
            .OrderBy(span => span.To - span.From)
            .Select(span => new Choice(span.Label, span.From, span.To))
            .FirstOrDefault() ?? Any;

    /// <summary>Whether a named thing is made on this base: its own base, or for a runeword one of its kinds with the sockets.</summary>
    public bool Takes(Entry entry, string code)
    {
        if (entry.Word is not { } word)
            return string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase);

        // Never in a base that exists only as its unique: no white one was ever there to put runes in.
        return _made.TryGetValue(code, out var made)
               && !ForgeScope.OnlyNamed(code)
               && (entry.Types ?? word.Types).Any(made.Lineage.Contains)
               && !word.ExcludedTypes.Any(made.Lineage.Contains)
               && made.Sockets >= word.Runes.Length;
    }

    private bool InType(string code) =>
        Type.From is not { } from || Type.To is not { } to
        || (_made.TryGetValue(code, out var made) && made.Rank >= from && made.Rank <= to);

    /// <summary>Whether a named thing is made on a base of the type chosen.</summary>
    private bool IsOfType(Entry entry) =>
        Type.IsAny
        || (entry.Word is null
            ? entry.Code is { } own && _made.ContainsKey(own) && InType(own)
            : _made.Keys.Any(code => InType(code) && Takes(entry, code)));

    // ---- the four boxes ---------------------------------------------------------------------------

    public IReadOnlyList<Choice> Types { get; private set; } = [Any];

    /// <summary>The bases of the type chosen - and, a runeword chosen, only those that take it, with no type too.</summary>
    public IReadOnlyList<Choice> Bases { get; private set; } = [Any];

    /// <summary>What the base chosen can be: every rarity with none chosen.</summary>
    public IReadOnlyList<Choice> Rarities { get; private set; } = AllRarities;

    /// <summary>The uniques, set pieces or runewords the type and the base leave, by name.</summary>
    public IReadOnlyList<Entry> Names { get; private set; } = [];

    public Choice Type { get; private set; } = Any;

    public Choice Base { get; private set; } = Any;

    public Choice Rarity { get; private set; } = AllRarities[Unique];

    public Entry? Named { get; private set; }

    /// <summary>A base is offered within a type, or for a runeword chosen: more than "Any" to choose from.</summary>
    public bool CanPickBase => Bases.Count > 1;

    /// <summary>A name is picked for a unique, a set piece and a runeword; the rest are their base.</summary>
    public bool CanPickName => Rarity.From <= Runeword;

    public void SetType(Choice type)
    {
        if (!Types.Contains(type) || type == Type)
            return;

        Type = type;

        // A new type makes the base chosen under the old one meaningless, as in the search panel.
        Base = Any;
        Settle();
    }

    public void SetBase(Choice @base)
    {
        if (!Bases.Contains(@base) || @base == Base)
            return;

        Base = @base;
        Settle();
    }

    public void SetRarity(Choice rarity)
    {
        if (!Rarities.Contains(rarity) || rarity == Rarity)
            return;

        Rarity = rarity;
        Settle();
    }

    public void SetNamed(Entry named)
    {
        if (!Names.Contains(named) || ReferenceEquals(named, Named))
            return;

        Named = named;
        Settle();
    }

    /// <summary>
    /// The four boxes set to what an item is - one that came from elsewhere, the game's copied text
    /// (<see cref="Trade.GameClipboard"/>) - where the Forge lists it: its type and base, its rarity, and the
    /// unique, set piece or runeword it is. False, and the boxes as they were, for one it lists nothing of: a
    /// base outside the Forge.
    /// <para>
    /// An upgraded unique or set piece is on a base its row does not name: the boxes are set to the base it is
    /// made on - Goldwrap's Heavy Belt for one on a Battle Belt - and the page says the rest
    /// (<see cref="Upgraded"/>).
    /// </para>
    /// </summary>
    public bool Show(Item item)
    {
        string code = item.Code.Trim();
        if (item is { IsRuneword: false, Quality: ItemQuality.Unique or ItemQuality.Set })
        {
            var rows = item.Quality == ItemQuality.Unique ? _uniques : _sets;
            if (!rows.Any(entry => entry.Row == (int)item.FileIndex && Takes(entry, code))
                && rows.FirstOrDefault(entry => entry.Row == (int)item.FileIndex && entry.Code is { } own && Upgraded(own, code)) is { Code: { } native })
            {
                code = native;
            }
        }

        if (!_made.TryGetValue(code, out var made))
            return false;

        int rarity = item.IsRuneword
            ? Runeword
            : item.Quality switch
            {
                ItemQuality.Unique => Unique,
                ItemQuality.Set => Set,
                ItemQuality.Craft => Crafted,
                ItemQuality.Rare => Rare,
                ItemQuality.Magic => Magic,
                ItemQuality.Superior => Superior,
                _ => Normal,
            };

        if (!Allowed(code).Contains(rarity))
            return false;

        var word = rarity == Runeword ? Runewords.On(item) : null;
        var named = rarity switch
        {
            Unique => _uniques.FirstOrDefault(entry => entry.Row == (int)item.FileIndex && Takes(entry, code)),
            Set => _sets.FirstOrDefault(entry => entry.Row == (int)item.FileIndex && Takes(entry, code)),
            Runeword => _runewords.FirstOrDefault(entry => word is not null && entry.Word == word && Takes(entry, code)),
            _ => null,
        };

        if (rarity <= Runeword && named is null)
            return false;

        // As a find chosen sets them (Pick), and for a runeword its base too.
        Type = Types.Where(span => span.From <= made.Rank && span.To >= made.Rank)
                    .OrderBy(span => span.To - span.From)
                    .FirstOrDefault() ?? Any;
        Base = made.Line;
        Rarity = AllRarities[rarity];
        Named = named;
        Settle();

        return Rarity.From == rarity && Base.Code == code && ReferenceEquals(Named, named);
    }

    /// <summary>Whether <paramref name="held"/> is the base <paramref name="made"/> taken a tier up, or two (ItemTier.Next).</summary>
    public static bool Upgraded(string? made, string? held)
    {
        if (made is null || held is null)
            return false;

        for (string? up = ItemTier.Next(made.Trim()); up is not null; up = ItemTier.Next(up))
        {
            if (up.Equals(held.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// One of the search's finds chosen: its type, its base and its rarity go in the boxes, and its name
    /// where it has one. A runeword keeps the type and the base already chosen where they take it, and
    /// lets them go where they do not.
    /// </summary>
    public void Pick(Entry find)
    {
        if (find.Code is { } code && _made.TryGetValue(code, out var made))
        {
            Type = Types.Where(span => span.From <= made.Rank && span.To >= made.Rank)
                        .OrderBy(span => span.To - span.From)
                        .FirstOrDefault() ?? Any;
            Base = made.Line;
        }
        else if (find.Word is not null)
        {
            if (Base.Code is { } chosen && !Takes(find, chosen))
                Base = Any;
            if (!IsOfType(find))
            {
                Type = Any;
                Base = Any;
            }
        }

        Rarity = AllRarities[find.Rarity];
        Named = find.Rarity <= Runeword ? find : null;
        Settle();
    }

    /// <summary>
    /// After any of the four changed: the lists that hang on it made again, and what they no longer hold
    /// let go. A runeword's base list hangs on the name, and the names on the base, so it is gone round
    /// until nothing moves - twice at most.
    /// </summary>
    private void Settle()
    {
        for (int round = 0; round < 4; round++)
        {
            var word = Rarity.From == Runeword ? Named : null;

            // The bases: the type's, a runeword's only where it is chosen - and those with no type at all.
            IEnumerable<string> codes = _made.Keys.Where(InType);
            if (word is not null)
                codes = codes.Where(code => Takes(word, code));
            else if (Type.IsAny)
                codes = [];

            // By tier first - normal, exceptional, elite - then by name: the tier is
            // what a base is chosen by. A base with no tiers (a throwing potion, a ring) comes after them,
            // a group of its own - among the normal ones it broke the list up.
            // Maps go by their own tiers, one to three and then the dungeons.
            Bases =
            [
                Any,
                .. codes.Select(code => _made[code].Line)
                        .OrderBy(line => ItemTier.Rank(line.Code) ?? MapRecipes.TierRank(line.Code) ?? 5)
                        .ThenBy(line => line.Label, StringComparer.OrdinalIgnoreCase),
            ];
            if (!Bases.Contains(Base))
                Base = Any;

            // The rarities: what the base can be.
            Rarities = Base.Code is { } chosen ? [.. AllRarities.Where(rarity => Allowed(chosen).Contains(rarity.From ?? -1))] : AllRarities;
            if (!Rarities.Contains(Rarity))
            {
                Rarity = Rarities.FirstOrDefault(rarity => rarity.From == Normal)
                         ?? Rarities.FirstOrDefault(rarity => rarity.From == Magic)
                         ?? Rarities.FirstOrDefault()
                         ?? AllRarities[Normal];
            }

            // The names: of the rarity, on the base, or of the type.
            var all = Rarity.From switch { Unique => _uniques, Set => _sets, Runeword => _runewords, _ => null };
            Names = all is null
                ? []
                : [.. all.Where(entry => Base.Code is { } on ? Takes(entry, on) : IsOfType(entry))
                         .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(entry => entry.Detail, StringComparer.OrdinalIgnoreCase)];

            var was = Named;
            if (Named is null || !Names.Contains(Named))
                Named = Names.Count == 1 ? Names[0] : null;

            // The base list was made for the word that stood at the top of the round: another, another round.
            var now = Rarity.From == Runeword ? Named : null;
            if (ReferenceEquals(now, word) && ReferenceEquals(was, Named))
                break;
        }
    }

    /// <summary>What a base can be, by its place in the rarities: what is named on it, then its own qualities.</summary>
    private HashSet<int> Allowed(string code)
    {
        if (_allowed.TryGetValue(code, out var known))
            return known;

        var allowed = new HashSet<int>();
        if (_uniques.Any(entry => Takes(entry, code)))
            allowed.Add(Unique);
        if (_sets.Any(entry => Takes(entry, code)))
            allowed.Add(Set);
        if (_runewords.Any(entry => Takes(entry, code)))
            allowed.Add(Runeword);

        foreach (var quality in ItemForge.QualitiesOf(code))
        {
            allowed.Add(quality switch
            {
                ItemQuality.Craft => Crafted,
                ItemQuality.Rare => Rare,
                ItemQuality.Magic => Magic,
                ItemQuality.Superior => Superior,
                _ => Normal,
            });
        }

        return _allowed[code] = allowed;
    }

    // ---- the search box ---------------------------------------------------------------------------

    /// <summary>
    /// What a typed text finds: every word of it, in any order, in a name - or in the base a unique or a
    /// set piece is on. Named things first, then bases; the first <paramref name="most"/> of them.
    /// </summary>
    public IReadOnlyList<Entry> Find(string text, int most, out int total)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            total = 0;
            return [];
        }

        var found = _uniques.Concat(_sets).Concat(_runewords).Concat(_bases)
            .Where(entry => words.All(word => entry.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                                              || (entry.Word is null && entry.Rarity != Normal
                                                  && entry.Detail.Contains(word, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        total = found.Count;
        return [.. found.Take(most)];
    }

    // ---- the item ---------------------------------------------------------------------------------

    /// <summary>What the four choices name, or null while they name nothing that can be made.</summary>
    public Order? What
    {
        get
        {
            int rarity = Rarity.From ?? Normal;
            if (rarity <= Runeword)
                return Named is { } named ? new Order(rarity, named, named.Word is null ? named.Code : Base.Code) : null;

            return Base.Code is { } code ? new Order(rarity, null, code) : null;
        }
    }

    /// <summary>
    /// The order made, stamped for a save of an era: a unique or a set piece from its row, a runeword in
    /// the base chosen - or with none chosen the best of its kinds that takes its runes - and a base in
    /// its quality with nothing rolled onto it (<see cref="ItemForge.BaseAs"/>).
    /// </summary>
    public static Item Make(Order order, string era) => order.Rarity switch
    {
        Unique => ItemForge.UniqueAt(order.Named!.Row, era).Item,
        Set => ItemForge.SetItemAt(order.Named!.Row, era).Item,
        Runeword => ItemForge.Runeword(order.Named!.Word!, order.Code ?? BaseOf(order.Named), era).Item,
        Crafted => ItemForge.BaseAs(order.Code!, ItemQuality.Craft, era).Item,
        Rare => ItemForge.BaseAs(order.Code!, ItemQuality.Rare, era).Item,
        Magic => ItemForge.BaseAs(order.Code!, ItemQuality.Magic, era).Item,
        Superior => ItemForge.BaseAs(order.Code!, ItemQuality.Superior, era).Item,
        _ => ItemForge.BaseAs(order.Code!, ItemQuality.Normal, era).Item,
    };

    private static string BaseOf(Entry entry)
    {
        var word = entry.Word!;
        var types = entry.Types ?? word.Types;
        return ItemForge.BestBaseFor(types, word.ExcludedTypes, word.Runes.Length)
               ?? ItemForge.BaseFor(types, word.ExcludedTypes)
               ?? throw new InvalidOperationException($"No base takes {Runewords.GameName(word)}.");
    }
}
