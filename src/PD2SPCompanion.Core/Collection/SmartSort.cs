using System.Text.RegularExpressions;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Collection;

/// <summary>
/// Smart sort: which vault folder an item goes to when it is stored sorted (Ctrl + Store) or
/// as a crafting base (Shift + Store).
/// <para>
/// The rules are a text file beside the vaults, <see cref="FileName"/>, not code: a player who
/// wants a different tree edits it in any text editor, and the editor has no screen for it. The
/// editor writes the default the first time and never again, so an edit is never overwritten;
/// <see cref="DefaultFileName"/> is rewritten every time, to compare with or copy back.
/// </para>
/// <para>
/// <b>Written as PD2's loot filter is</b>, so a player need not learn a different system again: a rule is <c>ItemDisplay[conditions]: Folder</c>, the
/// conditions the filter's own - UNI, RW HELM, (MAG OR RARE) JEWELRY, SOCK>0, ILVL~80-82, FRES+CRES>40, base codes,
/// !, OR, brackets - and the folder in place of what the filter would show, with tokens in the filter's style
/// (<c>%SET%</c>, <c>%SLOT%</c>). The first rule that fits wins, as in the filter. Shift + Store is a condition of its
/// own, <c>CRAFTBASE</c>. What the filter asks of where the item lies or who holds it (GROUND, SHOP, CLVL ...) means
/// nothing in a vault and is said to be wrong, as is anything the file names that does not exist. Nothing is ever
/// refused: an item no rule takes goes to <see cref="Unsorted"/>.
/// </para>
/// <para>
/// A type group is the item's type or any type above it in <c>itemtypes.txt</c>, as the game nests them: HELM takes
/// circlets, barbarian helms and druid pelts; SHIELD paladin shields and necromancer heads; JEWELRY rings and amulets.
/// </para>
/// </summary>
public static class SmartSort
{
    public const string FileName = "smart-sort.filter";
    public const string DefaultFileName = "smart-sort.default.filter";

    /// <summary>The rules as they were first written - words, "->" and folders in braces - converted once.</summary>
    public const string OldFileName = "smart-sort.txt";

    /// <summary>The old words' reference copy, once rewritten every start: taken away.</summary>
    private const string OldDefaultFileName = "smart-sort.default.txt";

    /// <summary>Where an item goes when no rule takes it.</summary>
    public const string Unsorted = "Unsorted";

    /// <summary>What a crafted item's <c>%CRAFT%</c> is when its recipe cannot be told.</summary>
    public const string UnknownRecipe = "Unknown recipe";

    /// <summary>One rule, and the line it is on - what a problem with it names.</summary>
    public sealed record Rule(int Line, Condition When, string Folder);

    /// <summary>
    /// A rules file read: its rules in order, its set groups by set, and what was wrong with it, a line each. A wrong
    /// line is left out; the rest still work.
    /// </summary>
    public sealed record Rules(
        IReadOnlyList<Rule> All,
        IReadOnlyDictionary<string, string> SetGroupOf,
        IReadOnlyList<string> Problems);

    public static string FilePath => Path.Combine(Vault.Folder, FileName);

    private static readonly object _gate = new();
    private static (string Path, DateTime Written, Rules Rules)? _cached;

    /// <summary>The vault folder whose reference copy of the rules was put right this run.</summary>
    private static string? _referenceChecked;

    /// <summary>
    /// The rules in force: the file beside the vaults, written with the default where there is
    /// none, read again whenever it has changed since. Never throws - a file that cannot be read
    /// gives the default rules and says why. An old rules file (<see cref="OldFileName"/>) is
    /// converted the first time, and kept beside it.
    /// </summary>
    public static Rules Current()
    {
        lock (_gate)
        {
            string path = FilePath;

            try
            {
                Directory.CreateDirectory(Vault.Folder);

                if (!File.Exists(path))
                    File.WriteAllText(path, FirstText());

                // The reference copy put right once a run (a folder): read whole at every store and every wizard's
                // refresh before.
                if (!string.Equals(_referenceChecked, Vault.Folder, StringComparison.OrdinalIgnoreCase))
                {
                    // A file that holds an earlier default untouched is the default: it follows it.
                    if (IsEarlierDefault(File.ReadAllText(path)))
                        File.WriteAllText(path, DefaultText);

                    // The old words' reference copy, ours and read by nothing since the rules are the filter's.
                    File.Delete(Path.Combine(Vault.Folder, OldDefaultFileName));

                    string reference = Path.Combine(Vault.Folder, DefaultFileName);
                    if (!File.Exists(reference) || File.ReadAllText(reference) != DefaultText)
                        File.WriteAllText(reference, DefaultText);
                    _referenceChecked = Vault.Folder;
                }

                var written = File.GetLastWriteTimeUtc(path);
                if (_cached is { } cached && cached.Path == path && cached.Written == written)
                    return cached.Rules;

                var rules = Parse(File.ReadAllText(path));
                _cached = (path, written, rules);
                return rules;
            }
            catch (Exception problem)
            {
                var rules = Parse(DefaultText);
                return rules with { Problems = [L($"{FileName} not read, default used: {problem.Message}")] };
            }
        }
    }

    /// <summary>
    /// How the default's rules changed since they were first written as the filter writes them, oldest
    /// first: each rule as it was, and as it is now. A file holding the rules of an earlier default, word for word
    /// whatever its comments say, was never edited - it takes the new default (<see cref="IsEarlierDefault"/>); an
    /// edited one is the player's and is left as it is.
    /// </summary>
    private static readonly (string Was, string Now)[] DefaultChanges =
    [
        // A skiller under its skill tree.
        ("ItemDisplay[CHARM STAT188>0]: Charms/Skillers/%CLASS%", "ItemDisplay[CHARM STAT188>0]: Charms/Skillers/%CLASS%/%SKILLTAB%"),
    ];

    /// <summary>The rules of a filter file - its lines but the blank and the comments, their spaces made one.</summary>
    private static List<string> FilterRuleLines(string text) =>
        [.. text.Replace("\r", "").Split('\n').Select(line => Regex.Replace(line.Trim(), @"\s+", " "))
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))];

    /// <summary>Whether <paramref name="text"/> holds the rules of an earlier default unchanged (<see cref="DefaultChanges"/>).</summary>
    public static bool IsEarlierDefault(string text)
    {
        var rules = FilterRuleLines(text);
        var earlier = FilterRuleLines(DefaultText);
        for (int back = DefaultChanges.Length - 1; back >= 0; back--)
        {
            int at = earlier.IndexOf(DefaultChanges[back].Now);
            if (at < 0)
                return false;

            earlier[at] = DefaultChanges[back].Was;
            if (rules.SequenceEqual(earlier))
                return true;
        }

        return false;
    }

    /// <summary>
    /// What a new rules file starts as: the default - or, where an old one was written in the words of before, that
    /// one converted (<see cref="Convert"/>), the old file renamed beside it so nothing is lost.
    /// </summary>
    private static string FirstText()
    {
        string old = Path.Combine(Vault.Folder, OldFileName);
        if (!File.Exists(old))
            return DefaultText;

        string text = File.ReadAllText(old);
        string kept = Path.Combine(Vault.Folder, OldFileName + ".old");
        if (!File.Exists(kept))
            File.Move(old, kept);

        return Unchanged(text) ? DefaultText : Convert(text);
    }

    // ---------------------------------------------------------------------------------------
    // Reading the file

    /// <summary>A rule: <c>ItemDisplay[...]: folder</c>, the folder up to a <c>//</c> comment.</summary>
    private static readonly Regex RuleLine = new(@"^ItemDisplay\[(?<when>.*)\]\s*:(?<folder>.*?)(//.*)?$", RegexOptions.IgnoreCase);

    /// <summary>A set group: <c>SetGroup[Name]: set, set, ...</c>.</summary>
    private static readonly Regex SetGroupLine = new(@"^SetGroup\[(?<name>[^\]]*)\]\s*:(?<sets>.*?)(//.*)?$", RegexOptions.IgnoreCase);

    public static Rules Parse(string text)
    {
        var rules = new List<Rule>();
        var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        string[] lines = text.Replace("\r", "").Split('\n');
        for (int at = 0; at < lines.Length; at++)
        {
            int number = at + 1;
            string line = lines[at].Trim();

            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (SetGroupLine.Match(line) is { Success: true } group)
            {
                string name = group.Groups["name"].Value.Trim();
                if (name.Length == 0)
                {
                    problems.Add(L($"line {number}: a set group is written SetGroup[Name]: set, set, ..."));
                    continue;
                }

                foreach (string set in group.Groups["sets"].Value.Split(',').Select(each => each.Trim()).Where(each => each.Length > 0))
                    groups[set] = name;
                continue;
            }

            if (RuleLine.Match(line) is not { Success: true } rule)
            {
                problems.Add(L($"line {number}: a rule is written ItemDisplay[conditions]: Folder/Subfolder."));
                continue;
            }

            string folder = rule.Groups["folder"].Value.Trim();
            if (Segments(folder).Count == 0)
            {
                problems.Add(L($"line {number}: the rule names no folder."));
                continue;
            }

            if (TokenProblem(folder) is { } token)
            {
                problems.Add(L($"line {number}: {token}"));
                continue;
            }

            if (Conditions.Parse(rule.Groups["when"].Value, out string? said) is not { } when)
            {
                problems.Add(L($"line {number}: {said}"));
                continue;
            }

            rules.Add(new Rule(number, when, folder));
        }

        return new Rules(rules, groups, problems);
    }

    /// <summary>The tokens a folder can name, in the filter's style: what fills them is said in the default's head.</summary>
    public static readonly IReadOnlyList<string> Tokens =
        ["SLOT", "WEAPON", "CLASS", "SKILLTAB", "SET", "SETGROUP", "CRAFT", "MAPTIER", "TIER", "SIZE", "QUALITY", "NAME", "BASENAME", "CODE", "ILVL", "SOCKETS"];

    private static string? TokenProblem(string folder)
    {
        var parts = folder.Split('%');
        if (parts.Length % 2 == 0)
            return L("a % with no closing %.");

        for (int at = 1; at < parts.Length; at += 2)
        {
            if (!Tokens.Contains(parts[at], StringComparer.OrdinalIgnoreCase))
                return L($"no folder token is called %{parts[at]}%.");
        }

        return null;
    }

    private static IReadOnlyList<string> Segments(string folder) =>
        [.. folder.Split('/').Select(each => each.Trim()).Where(each => each.Length > 0)];

    // ---------------------------------------------------------------------------------------
    // Sorting

    /// <summary>
    /// The folder an item goes to stored sorted, top first - never null: <see cref="Unsorted"/>
    /// takes what no rule does.
    /// </summary>
    public static IReadOnlyList<string> Classify(Item item, Rules rules) =>
        First(rules.All, new Facts(item, rules, asCraftingBase: false)) ?? [Unsorted];

    /// <summary>
    /// The folder an item goes to stored as a crafting base, or null where it is not one: a
    /// crafting base is a magic or rare item a craft recipe takes. The rules are asked with CRAFTBASE true. An item
    /// the rules do not place goes to the top of Crafting Bases.
    /// </summary>
    public static IReadOnlyList<string>? ClassifyAsCraftingBase(Item item, Rules rules) =>
        CanBeCraftingBase(item) ? First(rules.All, new Facts(item, rules, asCraftingBase: true)) ?? [CraftingBasesFolder] : null;

    /// <summary>
    /// The vault's folder of crafting bases, at its top: where the shipped rules send every base, each under
    /// its slot, and where the crafting wizard looks for them (CraftPlan).
    /// </summary>
    public const string CraftingBasesFolder = "Crafting Bases";

    /// <summary>
    /// The vault's folder of white bases, at its top: where the shipped rules send every normal, superior and
    /// low-quality item, each under its slot.
    /// </summary>
    public const string BasesFolder = "Bases";

    /// <summary>
    /// The top categories the rules deliver white bases to: the first level of every rule that asks for a normal,
    /// superior or inferior item (NMAG, SUP, INF, not negated) - "Bases" of the shipped <c>ItemDisplay[NMAG !RW ...]</c>.
    /// What the rune wizard counts a runeword's bases from, so that it looks where the sorter delivers; a folder that holds other things
    /// besides costs nothing, since only an item that is a base is counted. The shipped name where no rule asks.
    /// </summary>
    public static IReadOnlyList<string> BasesRoots(Rules rules) =>
        Roots(rules.All.Where(rule => !rule.When.Asks("CRAFTBASE") && (rule.When.Asks("NMAG") || rule.When.Asks("SUP") || rule.When.Asks("INF"))))
            is { Count: > 0 } named ? named : [BasesFolder];

    /// <summary>
    /// The top categories the rules deliver crafting bases to: the first level of every rule asking CRAFTBASE, and
    /// the name an item no rule places goes under (<see cref="ClassifyAsCraftingBase"/>).
    /// </summary>
    public static IReadOnlyList<string> CraftingBasesRoots(Rules rules) =>
        [.. Roots(rules.All.Where(rule => rule.When.Asks("CRAFTBASE"))).Append(CraftingBasesFolder).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The first level of each rule's folder, each once; not one that is a token of the item ("%SLOT%/...").</summary>
    private static IReadOnlyList<string> Roots(IEnumerable<Rule> rules) =>
        [.. rules.Select(rule => Segments(rule.Folder).FirstOrDefault() ?? "")
                 .Where(root => root.Length > 0 && !root.Contains('%'))
                 .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// A magic or rare item some craft recipe in the installed <c>cubemain</c> takes - by its
    /// type, as the cube matches it, circlets excepted (see <see cref="AffixRanges.CraftFamilies"/>).
    /// Nothing else says "keep this for crafting".
    /// </summary>
    public static bool CanBeCraftingBase(Item item) =>
        !item.IsRuneword
        && item.Quality is ItemQuality.Magic or ItemQuality.Rare
        && AffixRanges.CraftFamilies(item).Count > 0;

    private static IReadOnlyList<string>? First(IReadOnlyList<Rule> rules, Facts facts)
    {
        foreach (var rule in rules)
        {
            if (!rule.When.Holds(facts))
                continue;

            if (facts.Fill(rule.Folder) is { } folder && Segments(folder) is { Count: > 0 } path)
                return path;
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------
    // The conditions

    /// <summary>A rule's conditions, as the filter writes them: a tree of tests, NOT, AND and OR.</summary>
    public abstract record Condition
    {
        public abstract bool Holds(Facts facts);

        /// <summary>Whether the rule asks this flag of the item, not negated - what <see cref="BasesRoots"/> reads.</summary>
        public virtual bool Asks(string flag) => false;
    }

    private sealed record Always : Condition
    {
        public override bool Holds(Facts facts) => true;
    }

    private sealed record Flag(string Name) : Condition
    {
        public override bool Holds(Facts facts) => facts.Flag(Name);
        public override bool Asks(string flag) => Name.Equals(flag, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record BaseCode(string Code) : Condition
    {
        public override bool Holds(Facts facts) => facts.IsCode(Code);
    }

    private sealed record Compare(IReadOnlyList<string> Terms, char Op, int Low, int High) : Condition
    {
        /// <summary>
        /// The terms summed and compared - or, asked of an item's affixes alone (PREFIX, SUFFIX), each of them: a rare
        /// holds up to three of each, and the filter's SUFFIX=898 fits where any one is 898 (asked of
        /// the first alone, 49 of 56 ladder weapons with an Amplify Damage suffix were missed).
        /// </summary>
        public override bool Holds(Facts facts) =>
            Terms.Count == 1 && facts.Each(Terms[0]) is { } each
                ? each.Any(Fits)
                : Fits(Terms.Sum(facts.Value));

        private bool Fits(int value) => Op switch
        {
            '<' => value < Low,
            '>' => value > Low,
            '=' => value == Low,
            _ => value >= Low && value <= High,
        };
    }

    private sealed record Not(Condition Inner) : Condition
    {
        public override bool Holds(Facts facts) => !Inner.Holds(facts);
    }

    private sealed record All(IReadOnlyList<Condition> Parts) : Condition
    {
        public override bool Holds(Facts facts) => Parts.All(part => part.Holds(facts));
        public override bool Asks(string flag) => Parts.Any(part => part.Asks(flag));
    }

    private sealed record Any(IReadOnlyList<Condition> Parts) : Condition
    {
        public override bool Holds(Facts facts) => Parts.Any(part => part.Holds(facts));
        public override bool Asks(string flag) => Parts.Any(part => part.Asks(flag));
    }

    /// <summary>The filter's condition language, read: the flags, the values, the codes and the operators it knows.</summary>
    private static class Conditions
    {
        /// <summary>The flags an item answers yes or no.</summary>
        public static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
        {
            // Quality and state.
            "NMAG", "MAG", "RARE", "UNI", "SET", "CRAFT", "RW", "INF", "SUP", "ETH", "ID", "GEMMED",
            // Tier.
            "NORM", "EXC", "ELT",
            // Type groups, as itemtypes.txt nests them.
            "HELM", "CHEST", "SHIELD", "GLOVES", "BOOTS", "BELT", "CIRC", "ARMOR",
            "WEAPON", "AXE", "MACE", "CLUB", "HAMMER", "SWORD", "DAGGER", "THROWING", "JAV", "SPEAR", "POLEARM",
            "BOW", "XBOW", "STAFF", "WAND", "SCEPTER", "1H", "2H",
            "JEWELRY", "CHARM", "QUIVER", "MISC",
            // Class items.
            "DRU", "BAR", "DIN", "NEC", "SIN", "SOR", "ZON", "CLASS",
            // The sort's own: how the item is being stored (Shift + Store).
            "CRAFTBASE",
            "TRUE", "FALSE",
        };

        /// <summary>The numbers an item has, by the filter's names.</summary>
        public static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
        {
            "SOCK", "SOCKETS", "ILVL", "ALVL", "QLVL", "DEF", "ED", "EDEF", "EDAM", "MAXDUR", "AR", "ARPER",
            "RES", "FRES", "CRES", "LRES", "PRES", "FRW", "IAS", "FCR", "FHR", "FBR", "STR", "DEX", "LIFE", "MANA",
            "MFIND", "GFIND", "MAEK", "DTM", "REPLIFE", "REPAIR", "QTY", "RUNE", "GEM", "GEMLEVEL", "MAPTIER",
            "PREFIX", "SUFFIX", "AUTOMOD",
        };

        /// <summary>What the filter asks of where an item lies or of who holds it: nothing of that is so in a vault.</summary>
        public static readonly HashSet<string> Elsewhere = new(StringComparer.OrdinalIgnoreCase)
        {
            "SHOP", "EQUIPPED", "MERC", "INVENTORY", "CUBE", "STASH", "GROUND", "TOWN",
            "AMAZON", "ASSASSIN", "BARBARIAN", "DRUID", "NECROMANCER", "PALADIN", "SORCERESS",
            "CLVL", "DIFF", "FILTLVL", "MAPID", "GOLD", "PRICE", "CHARSTAT",
        };

        /// <summary>A stat or skill number: STAT127, SK54, OS54, CLSK1, TABSK8.</summary>
        private static readonly Regex Numbered = new(@"^(STAT|SK|OS|CLSK|TABSK)(\d+)$", RegexOptions.IgnoreCase);

        /// <summary>A comparison: terms summed with +, then &lt;, &gt;, = or ~ and its number or range.</summary>
        private static readonly Regex Comparison = new(@"^(?<terms>[A-Za-z0-9]+(\+[A-Za-z0-9]+)*)(?<op>[<>=~])(?<low>\d+)(-(?<high>\d+))?$");

        public static Condition? Parse(string text, out string? said)
        {
            said = null;
            var tokens = Tokenise(text);
            if (tokens.Count == 0)
                return new Always();

            int at = 0;
            var tree = Or(tokens, ref at, ref said);
            if (said is not null)
                return null;
            if (at < tokens.Count)
            {
                said = tokens[at] == ")" ? L("a ) with no (.") : L($"{tokens[at]} is not where it can be read.");
                return null;
            }

            return tree;
        }

        private static List<string> Tokenise(string text)
        {
            var tokens = new List<string>();
            var word = new System.Text.StringBuilder();

            void Flush()
            {
                if (word.Length > 0)
                    tokens.Add(word.ToString());
                word.Clear();
            }

            foreach (char each in text)
            {
                if (char.IsWhiteSpace(each))
                    Flush();
                else if (each is '(' or ')' || (each == '!' && word.Length == 0))
                {
                    Flush();
                    tokens.Add(each.ToString());
                }
                else
                {
                    word.Append(each);
                }
            }

            Flush();
            return tokens;
        }

        private static Condition? Or(List<string> tokens, ref int at, ref string? said)
        {
            var parts = new List<Condition>();
            if (And(tokens, ref at, ref said) is not { } first)
                return null;
            parts.Add(first);

            while (at < tokens.Count && tokens[at].Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                at++;
                if (And(tokens, ref at, ref said) is not { } next)
                    return null;
                parts.Add(next);
            }

            return parts.Count == 1 ? parts[0] : new Any(parts);
        }

        private static Condition? And(List<string> tokens, ref int at, ref string? said)
        {
            var parts = new List<Condition>();
            while (at < tokens.Count && tokens[at] != ")" && !tokens[at].Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens[at].Equals("AND", StringComparison.OrdinalIgnoreCase))
                {
                    at++;
                    continue;
                }

                if (Unary(tokens, ref at, ref said) is not { } part)
                    return null;
                parts.Add(part);
            }

            if (parts.Count == 0)
            {
                said = L("a condition is missing - next to OR, AND or a bracket.");
                return null;
            }

            return parts.Count == 1 ? parts[0] : new All(parts);
        }

        private static Condition? Unary(List<string> tokens, ref int at, ref string? said)
        {
            string token = tokens[at++];

            if (token == "!")
            {
                if (at >= tokens.Count)
                {
                    said = L("a ! with nothing after it.");
                    return null;
                }

                return Unary(tokens, ref at, ref said) is { } inner ? new Not(inner) : null;
            }

            if (token == "(")
            {
                if (Or(tokens, ref at, ref said) is not { } inner)
                    return null;
                if (at >= tokens.Count || tokens[at] != ")")
                {
                    said = L("a ( with no ).");
                    return null;
                }

                at++;
                return inner;
            }

            return Atom(token, ref said);
        }

        private static Condition? Atom(string token, ref string? said)
        {
            if (Comparison.Match(token) is { Success: true } compare)
            {
                var terms = compare.Groups["terms"].Value.Split('+');
                foreach (string term in terms)
                {
                    if (Elsewhere.Contains(term) || term.StartsWith("CHARSTAT", StringComparison.OrdinalIgnoreCase))
                    {
                        said = L($"{term} means nothing in the vault.");
                        return null;
                    }

                    if (!Values.Contains(term) && !Numbered.IsMatch(term))
                    {
                        said = L($"no value is called {term}.");
                        return null;
                    }
                }

                int low = int.Parse(compare.Groups["low"].Value);
                char op = compare.Groups["op"].Value[0];
                if (op == '~' && !compare.Groups["high"].Success)
                {
                    said = L($"{token}: a range is written {terms[0]}~low-high.");
                    return null;
                }

                int high = compare.Groups["high"].Success ? int.Parse(compare.Groups["high"].Value) : low;
                return new Compare(terms, op, low, high);
            }

            if (Flags.Contains(token))
            {
                return token.ToUpperInvariant() switch
                {
                    "TRUE" => new Always(),
                    "FALSE" => new Not(new Always()),
                    _ => new Flag(token.ToUpperInvariant()),
                };
            }

            if (Elsewhere.Contains(token))
            {
                said = L($"{token} means nothing in the vault.");
                return null;
            }

            if (Values.Contains(token) || Numbered.IsMatch(token))
            {
                said = L($"{token} is a number: compare it, as {token}>0.");
                return null;
            }

            // A base code: the game's own - amu, 6bs, r33, cm1.
            if (ItemTypes.TypeOf(token) is not null)
                return new BaseCode(token);

            said = L($"no condition or base code is called {token}.");
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------
    // What is true of one item

    /// <summary>What is true of one item, worked out once for all the rules asked of it.</summary>
    public sealed class Facts
    {
        private readonly Item _item;
        private readonly Rules _rules;
        private readonly bool _asCraftingBase;
        private readonly string _code;
        private readonly IReadOnlyList<string> _lineage;
        private readonly Dictionary<string, string?> _known = new(StringComparer.OrdinalIgnoreCase);

        internal Facts(Item item, Rules rules, bool asCraftingBase)
        {
            _item = item;
            _rules = rules;
            _asCraftingBase = asCraftingBase;
            _code = StackOf(item.Code?.Trim() ?? "");
            _lineage = ItemTypes.LineageOfBase(_code);
        }

        private bool Is(string type) => _lineage.Contains(type, StringComparer.OrdinalIgnoreCase);

        internal bool IsCode(string code) =>
            string.Equals(_item.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase) || string.Equals(_code, code, StringComparison.OrdinalIgnoreCase);

        internal bool Flag(string name) => name switch
        {
            "NMAG" => _item.Quality is ItemQuality.Inferior or ItemQuality.Normal or ItemQuality.Superior,
            "MAG" => _item.Quality is ItemQuality.Magic,
            "RARE" => _item.Quality is ItemQuality.Rare,
            "UNI" => _item.Quality is ItemQuality.Unique,
            "SET" => _item.Quality is ItemQuality.Set,
            "CRAFT" => _item.Quality is ItemQuality.Craft,
            "RW" => _item.IsRuneword,
            "INF" => _item.Quality is ItemQuality.Inferior,
            "SUP" => _item.Quality is ItemQuality.Superior,
            "ETH" => _item.IsEthereal,
            "ID" => _item.IsIdentified,
            "GEMMED" => _item.SocketedItems.Count > 0,

            "NORM" => ItemTier.Rank(_code) == 0,
            "EXC" => ItemTier.Rank(_code) == 1,
            "ELT" => ItemTier.Rank(_code) == 2,

            "HELM" => Is("helm"),
            "CHEST" => Is("tors"),
            "SHIELD" => Is("shld"),
            "GLOVES" => Is("glov"),
            "BOOTS" => Is("boot"),
            "BELT" => Is("belt"),
            "CIRC" => Is("circ"),
            "ARMOR" => Is("armo"),
            "WEAPON" => Is("weap"),
            "AXE" => Is("axe"),
            "MACE" => Is("mace"),
            "CLUB" => Is("club"),
            "HAMMER" => Is("hamm"),
            "SWORD" => Is("swor"),
            "DAGGER" => Is("knif"),
            "THROWING" => Is("thro"),
            "JAV" => Is("jave"),
            "SPEAR" => Is("spea"),
            "POLEARM" => Is("pole"),
            "BOW" => Is("bow"),
            "XBOW" => Is("xbow"),
            "STAFF" => Is("staf"),
            "WAND" => Is("wand"),
            "SCEPTER" => Is("scep"),
            "1H" => Is("weap") && ItemBases.Of(_code) is { TwoHanded: false },
            "2H" => Is("weap") && ItemBases.Of(_code) is { TwoHanded: true },
            "JEWELRY" => Is("ring") || Is("amul"),
            "CHARM" => Is("char"),
            "QUIVER" => Is("misl"),
            "MISC" => Is("misc"),

            "DRU" => Is("pelt"),
            "BAR" => Is("phlm"),
            "DIN" => Is("ashd"),
            "NEC" => Is("head"),
            "SIN" => Is("h2h"),
            "SOR" => Is("orb"),
            "ZON" => Is("amaz"),
            "CLASS" => ItemTypes.ClassOfBase(_code) is not null,

            "CRAFTBASE" => _asCraftingBase,
            _ => false,
        };

        internal int Value(string name)
        {
            if (Regex.Match(name, @"^(STAT|SK|OS|CLSK|TABSK)(\d+)$", RegexOptions.IgnoreCase) is { Success: true } numbered)
            {
                int id = int.Parse(numbered.Groups[2].Value);
                return numbered.Groups[1].Value.ToUpperInvariant() switch
                {
                    "STAT" => Lines().Where(stat => stat.Id == id).Sum(stat => stat.Value),
                    // Both hold their skill as the param (encode 1), not as SkillId - asked of SkillId alone, no
                    // SK or OS rule ever fit.
                    "SK" => Sum("item_singleskill", stat => (stat.SkillId ?? stat.Param) == id),
                    "OS" => Sum("item_nonclassskill", stat => (stat.SkillId ?? stat.Param) == id),
                    "CLSK" => Sum("item_addclassskills", stat => stat.CharacterClass == id),
                    _ => Sum("item_addskill_tab", stat => (stat.CharacterClass ?? 0) * 8 + (stat.SkillTab ?? 0) == id),
                };
            }

            return name.ToUpperInvariant() switch
            {
                "SOCK" or "SOCKETS" => _item.TotalNumberOfSockets,
                "ILVL" => _item.ItemLevel,
                "ALVL" => MagicAffixes.AffixLevel(_item),
                "QLVL" => ItemBases.Of(_code)?.QualityLevel ?? 0,
                "DEF" => _item.Armor,
                "ED" => Math.Max(Stat("item_maxdamage_percent"), Stat("item_armor_percent")),
                "EDEF" => Stat("item_armor_percent"),
                "EDAM" => Stat("item_maxdamage_percent"),
                "MAXDUR" => _item.MaxDurability,
                "AR" => Stat("tohit"),
                "ARPER" => Stat("item_tohit_percent"),
                "RES" => new[] { Stat("fireresist"), Stat("coldresist"), Stat("lightresist"), Stat("poisonresist") }.Min(),
                "FRES" => Stat("fireresist"),
                "CRES" => Stat("coldresist"),
                "LRES" => Stat("lightresist"),
                "PRES" => Stat("poisonresist"),
                "FRW" => Stat("item_fastermovevelocity"),
                "IAS" => Stat("item_fasterattackrate"),
                "FCR" => Stat("item_fastercastrate"),
                "FHR" => Stat("item_fastergethitrate"),
                "FBR" => Stat("item_fasterblockrate"),
                "STR" => Stat("strength"),
                "DEX" => Stat("dexterity"),
                "LIFE" => Stat("maxhp"),
                "MANA" => Stat("maxmana"),
                "MFIND" => Stat("item_magicbonus"),
                "GFIND" => Stat("item_goldbonus"),
                "MAEK" => Stat("item_manaafterkill"),
                "DTM" => Stat("item_damagetomana"),
                "REPLIFE" => Stat("hpregen"),
                "REPAIR" => Stat("item_replenish_durability"),
                "QTY" => _item.Quantity,
                "RUNE" => RuneNumber(),
                "GEM" or "GEMLEVEL" => GemLevel(),
                "MAPTIER" => MapTierNumber(),
                "PREFIX" => Each("PREFIX")![0],
                "SUFFIX" => Each("SUFFIX")![0],
                "AUTOMOD" => _item.IsAutoAffix ? _item.AutoAffixId : 0,
                _ => 0,
            };
        }

        /// <summary>
        /// The values a name holds one apiece, each compared alone: an item's prefixes or suffixes, as the save numbers
        /// them - the filter's own numbers (Kryszard's 662/898/899 name "of Amplify Damage" by them). One 0 where it has
        /// none. Null for any other name: it holds one value.
        /// </summary>
        internal IReadOnlyList<int>? Each(string name)
        {
            ushort[]? ids = name.ToUpperInvariant() switch
            {
                "PREFIX" => _item.MagicPrefixIds ?? [],
                "SUFFIX" => _item.MagicSuffixIds ?? [],
                _ => null,
            };
            if (ids is null)
                return null;

            var held = ids.Where(id => id > 0).Select(id => (int)id).ToList();
            return held.Count > 0 ? held : [0];
        }

        private IEnumerable<ItemStat> Lines() => ItemStats.Every(_item);

        private int Stat(string name) =>
            Lines().Where(stat => string.Equals(stat.Stat, name, StringComparison.OrdinalIgnoreCase)).Sum(stat => stat.Value);

        private int Sum(string name, Func<ItemStat, bool> which) =>
            Lines().Where(stat => string.Equals(stat.Stat, name, StringComparison.OrdinalIgnoreCase) && which(stat)).Sum(stat => stat.Value);

        /// <summary>A rune's number, El 1 to Zod 33, from its code r01 to r33.</summary>
        private int RuneNumber() =>
            Is("rune") && _code.Length == 3 && _code[0] == 'r' && int.TryParse(_code[1..], out int number) ? number : 0;

        /// <summary>A gem's grade, chipped 1 to perfect 5: the second letter of its code (gcv, gfv, gsv, gzv/glv, gpv; skc ... skz).</summary>
        private int GemLevel()
        {
            if (!Is("gem") || _code.Length != 3)
                return 0;

            char grade = _code.StartsWith("sk", StringComparison.OrdinalIgnoreCase) ? _code[2] : _code[1];
            return char.ToLowerInvariant(grade) switch
            {
                'c' => 1,
                'f' => 2,
                's' or 'u' => 3,
                'l' or 'z' => grade == 'z' && _code.StartsWith("sk", StringComparison.OrdinalIgnoreCase) ? 5 : 4,
                'p' => 5,
                _ => 0,
            };
        }

        /// <summary>A map's tier, 1 to 5, the nearest tier type above it - T3 maps descend from T2 and T1; 0 for anything else.</summary>
        private int MapTierNumber()
        {
            foreach (string step in _lineage)
            {
                switch (step.ToLowerInvariant())
                {
                    case "t1m": return 1;
                    case "t2m": return 2;
                    case "t3m": return 3;
                    case "t4m": return 4;
                    case "t5m": return 5;
                }
            }

            return 0;
        }

        /// <summary>The folder with its tokens filled in, or null where one has no value here.</summary>
        public string? Fill(string folder)
        {
            var parts = folder.Split('%');
            var text = new System.Text.StringBuilder(parts[0]);
            for (int at = 1; at < parts.Length; at += 2)
            {
                if (Token(parts[at]) is not { Length: > 0 } value)
                    return null;

                // A token is one folder, whatever it holds.
                text.Append(value.Replace('/', '-'));
                if (at + 1 < parts.Length)
                    text.Append(parts[at + 1]);
            }

            return text.ToString();
        }

        private string? Token(string token)
        {
            if (_known.TryGetValue(token, out string? known))
                return known;

            string? value = token.ToUpperInvariant() switch
            {
                "SLOT" => Slot,
                "WEAPON" => Weapon(),
                "CLASS" => CharacterClasses.NameOfCode(ItemTypes.ClassOfBase(_item.Code)) ?? SkillTabClass(),
                "SKILLTAB" => SkillTab(),
                "SET" => SetNames.Of(_item),
                "SETGROUP" => SetGroup(),
                "CRAFT" => _item.Quality is ItemQuality.Craft && !_item.IsRuneword
                    ? AffixRanges.CraftFamilyOf(_item) ?? UnknownRecipe
                    : null,
                "MAPTIER" => MapTier(),
                "TIER" => ItemTier.Rank(_item.Code) switch { 0 => "Normal", 1 => "Exceptional", 2 => "Elite", _ => null },
                "SIZE" => Size,
                "QUALITY" => ItemOrder.Rarities[ItemOrder.Rarity(_item)],
                "NAME" => ItemPresenter.Describe(_item).Name,
                "BASENAME" => ItemPresenter.Describe(_item).BaseName,
                "CODE" => _code,
                "ILVL" => _item.ItemLevel.ToString(),
                "SOCKETS" => _item.TotalNumberOfSockets.ToString(),
                _ => null,
            };

            _known[token] = value;
            return value;
        }

        /// <summary>"Helms", "Weapons", ... - the vault's own Type groups.</summary>
        private string Slot
        {
            get
            {
                string type = ItemOrder.TypeName(ItemOrder.TypeRankOf(_code));
                int colon = type.IndexOf(':');
                return colon < 0 ? type : type[..colon];
            }
        }

        /// <summary>
        /// A weapon's kind as the wiki's unique weapon pages have them: class weapons together,
        /// thrown ones together, hammers with the maces.
        /// </summary>
        private string? Weapon()
        {
            if (Slot != "Weapons")
                return null;

            if (ItemTypes.ClassOfBase(_item.Code) is not null)
                return "Class Weapons";

            if (Is("thro"))
                return "Throwing";

            string kind = ItemOrder.TypeName(ItemOrder.TypeRankOf(_item.Code));
            kind = kind[(kind.IndexOf(':') + 1)..].Trim();
            return kind == "Hammers" ? "Maces" : kind;
        }

        private string? SetGroup()
        {
            if (SetNames.Of(_item) is not { } set)
                return null;

            if (_rules.SetGroupOf.TryGetValue(set, out string? group))
                return group;

            // A set the list does not name yet - a new season's: the tables still say whether
            // it is built around a class.
            return SetNames.IsClassSet(set) ? "Class-Focused" : "Uncommon";
        }

        /// <summary>A map's tier as a folder: Tier 1 to Tier 5, or Uber.</summary>
        private string? MapTier()
        {
            if (Is("ubmp"))
                return "Uber";

            return MapTierNumber() is > 0 and var tier ? $"Tier {tier}" : null;
        }

        private string? Size
        {
            get
            {
                if (Is("scha"))
                    return "Small";
                if (Is("mcha"))
                    return "Large";
                if (Is("lcha"))
                    return "Grand";
                return null;
            }
        }

        /// <summary>
        /// The skill tree the item raises, as the game names it: "Javelin and Spear Skills", "Warcries" - a skiller's
        /// folder under its class, a further layer down by skill tab.
        /// </summary>
        private string? SkillTab()
        {
            foreach (var stat in Lines())
            {
                if (string.Equals(stat.Stat, "item_addskill_tab", StringComparison.OrdinalIgnoreCase)
                    && SkillTabs.Name(stat.CharacterClass ?? -1, stat.SkillTab ?? -1) is { Length: > 0 } named)
                {
                    return named;
                }
            }

            return null;
        }

        /// <summary>The class whose skill tree the item raises, as "+1 to Warcries (Barbarian Only)".</summary>
        private string? SkillTabClass()
        {
            foreach (var stat in Lines())
            {
                if (string.Equals(stat.Stat, "item_addskill_tab", StringComparison.OrdinalIgnoreCase)
                    && SkillTabs.ClassOf(stat.CharacterClass ?? -1) is { Length: > 0 } named)
                {
                    return named;
                }
            }

            return null;
        }

        /// <summary>A materials-tab stack is sorted as the item it is a stack of.</summary>
        private static string StackOf(string code) =>
            code.Length > 1 && code.EndsWith('s') && Materials.SlotFor(code) is not null
            && ItemTypes.TypeOf(code[..^1]) is not null
                ? code[..^1]
                : code;
    }

    // ---------------------------------------------------------------------------------------
    // The rules of before, converted once

    /// <summary>The old default's rules and set groups, its comments left out: an old file that holds just these is the default.</summary>
    private const string OldDefaultRules = """
        [set groups]
        Common = Arctic Gear, Hsarus' Defense, Berserker's Arsenal, Cleglaw's Brace, Infernal Tools, Death's Disguise, Sigon's Complete Steel, Isenhart's Armory, Civerb's Vestments, Cathan's Traps, Angelic Raiment, Vidala's Rig, Arcanna's Tricks, Iratha's Finery, Milabrega's Regalia, Tancred's Battlegear
        Uncommon = Cow King's Leathers, Sander's Folly, Hwanin's Majesty, Orphan's Call, The Disciple, Naj's Ancient Vestige, Sazabi's Grand Tribute, Heaven's Brethren, Bul-Kathos' Children
        Class-Focused = Aldur's Watchtower, Griswold's Legacy, Immortal King, M'avina's Battle Hymn, Natalya's Odium, Tal Rasha's Wrappings, Trang-Oul's Avatar
        [sorted]
        set                    -> Sets/{set group}/{set}
        runeword helm          -> Runewords/Helms
        runeword chest         -> Runewords/Chests
        runeword shield        -> Runewords/Shields
        runeword quiver        -> Runewords/Quivers
        runeword               -> Runewords/Weapons
        map                    -> Maps/{map tier}
        charm unique           -> Charms/Unique
        charm skiller          -> Charms/Skillers/{class}
        charm                  -> Charms/{size}
        ring unique            -> Rings/Unique
        ring crafted           -> Rings/Crafted/{family}
        ring rare              -> Rings/Rare
        ring magic             -> Rings/Magic
        amulet unique          -> Amulets/Unique
        amulet crafted         -> Amulets/Crafted/{family}
        amulet rare            -> Amulets/Rare
        amulet magic           -> Amulets/Magic
        jewel unique           -> Jewels/Unique
        jewel crafted          -> Jewels/Crafted/{family}
        jewel rare             -> Jewels/Rare
        jewel magic            -> Jewels/Magic
        unique weapon          -> Unique Weapons/{weapon}
        unique                 -> Other Uniques/{slot}
        crafted                -> Crafted/{family}/{slot}
        rare                   -> Rare/{slot}
        magic                  -> Magic/{slot}
        rune|gem               -> Runes & Gems
        material               -> Materials
        normal|superior|low-quality -> Bases/{slot}
        *                      -> Unsorted
        [crafting bases]
        *                      -> Crafting Bases/{slot}
        """;

    /// <summary>The lines that count of an old file: neither empty nor a comment, spaces folded.</summary>
    private static IEnumerable<string> RuleLines(string text) =>
        text.Replace("\r", "").Split('\n').Select(line => Regex.Replace(line.Trim(), @"\s+", " "))
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

    /// <summary>Whether an old rules file holds the old default's rules unchanged - whatever its comments say.</summary>
    private static bool Unchanged(string old) => RuleLines(old).SequenceEqual(RuleLines(OldDefaultRules));

    /// <summary>The old words, as the filter writes them.</summary>
    private static readonly Dictionary<string, string> OldWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["unique"] = "UNI", ["set"] = "SET", ["runeword"] = "RW", ["crafted"] = "CRAFT", ["rare"] = "RARE",
        ["magic"] = "MAG", ["superior"] = "SUP", ["normal"] = "(NMAG !SUP !INF)", ["low-quality"] = "INF",
        ["simple"] = "MISC", ["helm"] = "HELM", ["chest"] = "CHEST", ["shield"] = "SHIELD", ["gloves"] = "GLOVES",
        ["boots"] = "BOOTS", ["belt"] = "BELT", ["amulet"] = "amu", ["ring"] = "rin", ["weapon"] = "WEAPON",
        ["quiver"] = "QUIVER", ["charm"] = "CHARM", ["jewel"] = "jew", ["rune"] = "RUNE>0", ["gem"] = "GEM>0",
        ["map"] = "MAPTIER>0", ["material"] = "(MISC !JEWELRY !CHARM !QUIVER)", ["ethereal"] = "ETH",
        ["socketed"] = "SOCK>0", ["class-item"] = "CLASS", ["skiller"] = "STAT188>0", ["two-handed"] = "2H",
        ["throwing"] = "THROWING", ["small"] = "cm1", ["large"] = "cm2", ["grand"] = "cm3",
        ["exceptional"] = "EXC", ["elite"] = "ELT",
    };

    /// <summary>The old folder facts, as tokens.</summary>
    private static readonly Dictionary<string, string> OldFacts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["slot"] = "SLOT", ["weapon"] = "WEAPON", ["class"] = "CLASS", ["set"] = "SET", ["set group"] = "SETGROUP",
        ["family"] = "CRAFT", ["map tier"] = "MAPTIER", ["tier"] = "TIER", ["size"] = "SIZE", ["quality"] = "QUALITY",
        ["name"] = "NAME",
    };

    /// <summary>
    /// An old rules file in the filter's words, rule for rule: its comments kept as <c>//</c>, its set groups as
    /// <c>SetGroup[...]</c>, its crafting bases' rules first with CRAFTBASE. What has no word in the filter
    /// (<c>type:</c>, <c>other</c>) is left a comment, said so, to be written again.
    /// </summary>
    public static string Convert(string old)
    {
        var top = new List<string>
        {
            "// Converted from smart-sort.txt (kept beside it as smart-sort.txt.old): the rules are written",
            "// as PD2's loot filter writes its conditions now. smart-sort.default.filter shows every word.",
            "",
        };
        var bases = new List<string>();
        var sorted = new List<string>();
        string section = "";

        foreach (string raw in old.Replace("\r", "").Split('\n'))
        {
            string line = raw.Trim();
            var into = section == "crafting bases" ? bases : sorted;

            if (line.StartsWith('#'))
            {
                (section == "set groups" ? top : into).Add("//" + line[1..]);
                continue;
            }

            if (line.Length == 0)
            {
                (section == "set groups" ? top : into).Add("");
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            if (section == "set groups")
            {
                int equals = line.IndexOf('=');
                top.Add(equals > 0 ? $"SetGroup[{line[..equals].Trim()}]: {line[(equals + 1)..].Trim()}" : "// " + line);
                continue;
            }

            into.Add(ConvertRule(line, crafting: section == "crafting bases"));
        }

        return string.Join("\n", top.Concat(bases).Append("").Concat(sorted)).TrimEnd() + "\n";
    }

    private static string ConvertRule(string line, bool crafting)
    {
        int arrow = line.IndexOf("->", StringComparison.Ordinal);
        if (arrow < 0)
            return "// not converted, no -> in it: " + line;

        var conditions = new List<string>();
        if (crafting)
            conditions.Add("CRAFTBASE");

        foreach (string token in line[..arrow].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "*")
                continue;

            bool negated = token.StartsWith('!');
            var words = new List<string>();
            foreach (string word in token.TrimStart('!').Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.StartsWith("code:", StringComparison.OrdinalIgnoreCase))
                    words.Add(word[5..]);
                else if (OldWords.TryGetValue(word, out string? filter))
                    words.Add(filter);
                else
                    return $"// not converted, the filter has no word for {word}: {line}";
            }

            string written = words.Count == 1 ? words[0] : "(" + string.Join(" OR ", words) + ")";
            conditions.Add(negated ? "!" + written : written);
        }

        string folder = Regex.Replace(line[(arrow + 2)..].Trim(), @"\{([^}]*)\}",
            fact => OldFacts.TryGetValue(fact.Groups[1].Value.Trim(), out string? token) ? $"%{token}%" : fact.Value);
        return $"ItemDisplay[{string.Join(" ", conditions)}]: {folder}";
    }

    // ---------------------------------------------------------------------------------------
    // The default

    /// <summary>
    /// The rules the editor ships. Uniques,
    /// sets and runewords as the PD2 wiki's main page has them; charms, rings, amulets and jewels
    /// at the top, being what players compare most; everything else by quality, then slot. Written in the filter's
    /// words, folder for folder the same tree as before.
    /// </summary>
    public const string DefaultText = """
        // Project Diablo II Single Player Companion smart sort
        //
        // Written as PD2's loot filter writes its conditions: a rule is
        //     ItemDisplay[conditions]: Folder/Subfolder
        // and the first rule whose conditions fit the item picks its folder - where the filter would show
        // the item's name, the folder goes. An item no rule takes goes to Unsorted.
        //
        // Ctrl + Store stores sorted. Shift + Store stores a magic or rare item a craft recipe takes as a
        // crafting base: the rules are asked with CRAFTBASE true. Plain Store puts it in the folder you have open.
        //
        // Edit this file in any text editor; the companion reads it again the next time you store.
        // smart-sort.default.filter beside it is the original, rewritten every time the companion starts.
        // Delete this file to go back to it.
        //
        // Conditions, as in the filter (several side by side must all fit; OR, ! and brackets as there):
        //   quality    UNI SET RW CRAFT RARE MAG SUP INF NMAG (normal, superior or inferior)  ETH ID GEMMED
        //   tier       NORM EXC ELT
        //   type       HELM CHEST SHIELD GLOVES BOOTS BELT CIRC ARMOR  JEWELRY CHARM QUIVER MISC
        //              WEAPON AXE MACE CLUB HAMMER SWORD DAGGER THROWING JAV SPEAR POLEARM BOW XBOW
        //              STAFF WAND SCEPTER 1H 2H
        //              a type takes the types under it in the game's own tree: HELM takes circlets,
        //              barbarian helms and druid pelts; SHIELD paladin shields and necromancer heads
        //   class      DRU BAR DIN NEC SIN SOR ZON, CLASS for any of them
        //   base code  the game's own: amu rin jew cm1 cm2 cm3 6bs r33 ...
        //   numbers    SOCK ILVL ALVL QLVL DEF ED EDEF EDAM MAXDUR AR ARPER RES FRES CRES LRES PRES
        //              FRW IAS FCR FHR FBR STR DEX LIFE MANA MFIND GFIND MAEK DTM REPLIFE REPAIR QTY
        //              RUNE (El 1 to Zod 33)  GEM (chipped 1 to perfect 5)  MAPTIER  PREFIX SUFFIX AUTOMOD
        //              STATn (a stat by its number)  SKn OSn CLSKn TABSKn (skills)
        //              compared with < > = or a range ~ (ILVL~80-82), added with + (FRES+CRES>40)
        //   CRAFTBASE  the item is being stored as a crafting base (Shift + Store)
        // What the filter asks of where an item lies or who holds it (GROUND, SHOP, CLVL ...) means
        // nothing in the vault, and the line is said to be wrong.
        //
        // A folder can name a fact of the item:
        //   %SLOT%      Helms, Chests, Shields, Gloves, Boots, Belts, Amulets, Rings, Weapons,
        //               Quivers, Charms, Jewels, Runes, Gems, Maps, Other
        //   %WEAPON%    Axes, Maces, Swords, Daggers, Throwing, Spears, Polearms, Bows, Crossbows,
        //               Staves, Wands, Scepters, Class Weapons ...
        //   %CLASS%     the class a base is made for, or whose skill tree it raises
        //   %SKILLTAB%  the skill tree it raises: Javelin and Spear Skills, Warcries ...
        //   %SET%       the set's name        %SETGROUP%  its group below
        //   %CRAFT%     a crafted item's recipe family, or "Unknown recipe"
        //   %MAPTIER%   Tier 1 to Tier 5, Uber
        //   %TIER%      Normal, Exceptional, Elite        %SIZE%  Small, Large, Grand
        //   %QUALITY%   Unique, Set, Runeword, Crafted, Rare, Magic, Superior, Normal ...
        //   %NAME% %BASENAME% %CODE% %ILVL% %SOCKETS%
        // A rule whose folder names a fact the item does not have is skipped for it.
        //
        // A folder the sorter makes remembers what it is for. Rename it or move it and items
        // still go to it; delete it and the next one makes it again. Items already stored are
        // never moved.

        // The sets' groups, as the PD2 wiki has them, by the names the game shows. A set not named here goes to
        // Class-Focused when one of its pieces is a class item, and to Uncommon otherwise.
        SetGroup[Common]: Arctic Gear, Hsarus' Defense, Berserker's Arsenal, Cleglaw's Brace, Infernal Tools, Death's Disguise, Sigon's Complete Steel, Isenhart's Armory, Civerb's Vestments, Cathan's Traps, Angelic Raiment, Vidala's Rig, Arcanna's Tricks, Iratha's Finery, Milabrega's Regalia, Tancred's Battlegear
        SetGroup[Uncommon]: Cow King's Leathers, Sander's Folly, Hwanin's Majesty, Orphan's Call, The Disciple, Naj's Ancient Vestige, Sazabi's Grand Tribute, Heaven's Brethren, Bul-Kathos' Children
        SetGroup[Class-Focused]: Aldur's Watchtower, Griswold's Legacy, Immortal King, M'avina's Battle Hymn, Natalya's Odium, Tal Rasha's Wrappings, Trang-Oul's Avatar

        // Shift + Store: crafting bases.
        ItemDisplay[CRAFTBASE]: Crafting Bases/%SLOT%

        ItemDisplay[SET]: Sets/%SETGROUP%/%SET%

        ItemDisplay[RW HELM]: Runewords/Helms
        ItemDisplay[RW CHEST]: Runewords/Chests
        ItemDisplay[RW SHIELD]: Runewords/Shields
        ItemDisplay[RW QUIVER]: Runewords/Quivers
        ItemDisplay[RW]: Runewords/Weapons

        ItemDisplay[MAPTIER>0]: Maps/%MAPTIER%

        ItemDisplay[CHARM UNI]: Charms/Unique
        ItemDisplay[CHARM STAT188>0]: Charms/Skillers/%CLASS%/%SKILLTAB%
        ItemDisplay[CHARM]: Charms/%SIZE%

        // Amulets are jewelry that is no ring: amu, and the few uniques on bases of their own.
        ItemDisplay[rin UNI]: Rings/Unique
        ItemDisplay[rin CRAFT]: Rings/Crafted/%CRAFT%
        ItemDisplay[rin RARE]: Rings/Rare
        ItemDisplay[rin MAG]: Rings/Magic
        ItemDisplay[JEWELRY !rin UNI]: Amulets/Unique
        ItemDisplay[JEWELRY !rin CRAFT]: Amulets/Crafted/%CRAFT%
        ItemDisplay[JEWELRY !rin RARE]: Amulets/Rare
        ItemDisplay[JEWELRY !rin MAG]: Amulets/Magic
        ItemDisplay[jew UNI]: Jewels/Unique
        ItemDisplay[jew CRAFT]: Jewels/Crafted/%CRAFT%
        ItemDisplay[jew RARE]: Jewels/Rare
        ItemDisplay[jew MAG]: Jewels/Magic

        ItemDisplay[UNI WEAPON]: Unique Weapons/%WEAPON%
        ItemDisplay[UNI]: Other Uniques/%SLOT%

        ItemDisplay[CRAFT]: Crafted/%CRAFT%/%SLOT%
        ItemDisplay[RARE]: Rare/%SLOT%
        ItemDisplay[MAG]: Magic/%SLOT%

        ItemDisplay[RUNE>0 OR GEM>0]: Runes & Gems
        ItemDisplay[MISC !JEWELRY !CHARM !QUIVER]: Materials
        ItemDisplay[NMAG]: Bases/%SLOT%

        ItemDisplay[]: Unsorted

        """;
}
