using System.Globalization;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// A skill as <c>skills.txt</c> writes it, for the character sheet: what a passive gives and what an aura or a
/// lingering buff gives, each a stat by a formula; the state it puts on; its parameters and the columns the
/// formulas read; and how it is used - its animation, its sequence, the weapon it asks for.
/// <para>
/// The table itself says what a skill is:
/// <c>passive</c> = 1 with <c>passivestat1..5</c> is up for good; <c>aura</c> = 1 is a Paladin's aura; an
/// <c>aurastate</c> with <c>aurastat1..6</c> is a buff that lingers once cast. No list of ours.
/// </para>
/// </summary>
public sealed class SkillRow
{
    private readonly TxtTable _table;
    private readonly string[] _fields;

    internal SkillRow(TxtTable table, string[] fields)
    {
        _table = table;
        _fields = fields;
        Id = Number("Id");
        Name = Text("skill");
        CharClass = Text("charclass");
        Passives = Pairs("passivestat", "passivecalc", 5);
        Auras = Pairs("aurastat", "aurastatcalc", 6);
        Params = [.. Enumerable.Range(1, 8).Select(n => Number($"Param{n}"))];
    }

    public int Id { get; }

    /// <summary>
    /// Its icon in its class's sheet of skill icons (skilldesc.txt <c>IconCel</c>, by the row's <c>skilldesc</c>); -1
    /// for a row with none.
    /// </summary>
    public int IconCel { get; internal set; } = -1;

    /// <summary>The table's own name of it - "Wearwolf", "Quickness" - which the formulas name skills by.</summary>
    public string Name { get; }

    /// <summary>The class it is a skill of (<c>ama</c>, <c>sor</c>...), or "" for one no class has - an item's, a monster's.</summary>
    public string CharClass { get; }

    /// <summary>What it gives while it has a level, with no casting: stat by formula.</summary>
    public IReadOnlyList<(string Stat, string Calc)> Passives { get; }

    /// <summary>What it gives while its aura or its buff is up: stat by formula.</summary>
    public IReadOnlyList<(string Stat, string Calc)> Auras { get; }

    public int[] Params { get; }

    public bool IsPassive => Text("passive") == "1";
    public bool IsAura => Text("aura") == "1";

    /// <summary>
    /// The state a passive carries for good - "" for a skill whose passive lines are not the character's: a
    /// summon's are the summon's, an aura's its own while it is up.
    /// </summary>
    public string PassiveState => Text("passivestate");

    /// <summary>The weapon type a passive asks for in hand - a mastery's; "" for none.</summary>
    public string PassiveItemType => Text("passiveitype");

    /// <summary>The level the row tops out at (<c>maxlvl</c>); 0 for none.</summary>
    public int MaxLevel => Number("maxlvl");

    /// <summary>The state its aura or buff puts on: states.txt says which exclude one another.</summary>
    public string AuraState => Text("aurastate");

    /// <summary>How long its state stays once cast, in frames (25 a second), as a formula; "" for one that says none.</summary>
    public string AuraLength => Text("auralencalc");

    /// <summary>
    /// How the game does the skill (<c>srvdofunc</c>) - which says whose its aura lines are: the caster's own
    /// for <see cref="SetsAState"/> (the armors, Holy Shield, Burst of Speed, Fade, Venom), 25 (Enchant), 65 (a
    /// Paladin's aura that helps), 68 (a war cry that helps), <see cref="ChangesForm"/>, 9 (Frenzy), 165
    /// (Hunger) and <see cref="BuildsUpByCharges"/>; an enemy's for the rest - a curse, Conviction (66), Holy
    /// Freeze (81), Cloak of Shadows (47).
    /// </summary>
    public int ServerDo => Number("srvdofunc");

    public const int SetsAState = 18, ChangesForm = 116, BuildsUpByCharges = 120;

    /// <summary>Whether its aura lines are the caster's own (<see cref="ServerDo"/>).</summary>
    public bool GivesTheCaster => ServerDo is SetsAState or 25 or 65 or 68 or ChangesForm or 9 or 165 or BuildsUpByCharges;

    /// <summary>What it summons (<c>summon</c>) - "" for a skill that summons nothing.</summary>
    public string Summon => Text("summon");

    /// <summary>The skill its summon uses first (<c>sumskill1</c>) - a spirit's aura - and the level it has it at, a formula.</summary>
    public string SummonSkill => Text("sumskill1");
    public string SummonSkillLevel => Text("sumsk1calc");

    /// <summary>
    /// Whether a form may use it (<c>restrict</c>): 0 no, 1 yes, 2 only in the states it names
    /// (<see cref="States"/>) - Feral Rage as a wolf, Maul as a bear.
    /// </summary>
    public int Restrict => Number("restrict");

    public IReadOnlyList<string> States => [.. new[] { Text("State1"), Text("State2"), Text("State3") }.Where(state => state.Length > 0)];

    /// <summary>A charge-up (<c>progressive</c>): what it builds is spent by a finishing move, and no line of the sheet's.</summary>
    public bool IsProgressive => Text("progressive") == "1";

    /// <summary>
    /// What kind of attack it is (<c>range</c>): <c>h2h</c> hand to hand, <c>rng</c> from afar - a throw, a shot -
    /// <c>both</c> for the plain attack, which is whichever the weapon is, <c>none</c> for what is no attack.
    /// </summary>
    public string Range => Text("range");

    /// <summary>How it is done: A1 an attack, TH a throw, KK a kick, S1.. a special, SC a cast, SQ a sequence.</summary>
    public string Anim => Text("anim");

    /// <summary>Its sequence, where <see cref="Anim"/> is SQ; 0 for none.</summary>
    public int Sequence => Number("seqnum");

    /// <summary>A column's text, trimmed; "" where the table has no such column.</summary>
    public string Text(string column)
    {
        int at = _table.IndexOfColumn(column);
        return at >= 0 && at < _fields.Length ? _fields[at].Trim() : "";
    }

    public int Number(string column) => int.TryParse(Text(column), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    private List<(string, string)> Pairs(string stat, string calc, int count)
    {
        var pairs = new List<(string, string)>();
        for (int n = 1; n <= count; n++)
        {
            if (Text($"{stat}{n}") is { Length: > 0 } name)
                pairs.Add((name, Text($"{calc}{n}")));
        }

        return pairs;
    }
}

/// <summary>The skills table's rows by id and by name - PD2's own table, read from the archive chain.</summary>
public static class SkillRows
{
    private static Dictionary<int, SkillRow> _byId = [];
    private static Dictionary<string, SkillRow> _byName = new(StringComparer.OrdinalIgnoreCase);

    public static SkillRow? Of(int id) => _byId.GetValueOrDefault(id);

    public static SkillRow? Named(string name) => _byName.GetValueOrDefault(name);

    public static IEnumerable<SkillRow> All => _byId.Values;

    public static void Load(GameData game)
    {
        var byId = new Dictionary<int, SkillRow>();
        var byName = new Dictionary<string, SkillRow>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Each description's icon, by its key: what a row's skilldesc names.
            var icons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var descriptions = game.ReadExcel("skilldesc").Table;
                for (int at = 0; at < descriptions.Rows.Count; at++)
                {
                    if (descriptions.Value(at, "skilldesc").Trim() is { Length: > 0 } key
                        && int.TryParse(descriptions.Value(at, "IconCel").Trim(), out int cel))
                    {
                        icons.TryAdd(key, cel);
                    }
                }
            }
            catch (Exception problem)
            {
                // No descriptions: no icons; the skills read as ever.
                Log.Caught(problem);
            }

            var table = game.ReadExcel("skills").Table;
            int idColumn = table.IndexOfColumn("Id");
            foreach (var fields in table.Rows)
            {
                if (idColumn < 0 || idColumn >= fields.Length || !int.TryParse(fields[idColumn].Trim(), out _))
                    continue;

                var row = new SkillRow(table, fields);
                if (icons.TryGetValue(row.Text("skilldesc"), out int icon))
                    row.IconCel = icon;
                byId.TryAdd(row.Id, row);
                byName.TryAdd(row.Name, row);
            }
        }
        catch (Exception problem)
        {
            // No skills: the sheet's lines stay as the items make them.
            Log.Caught(problem);
        }

        _byId = byId;
        _byName = byName;
    }
}

/// <summary>
/// The skills table's formula language, read as the engine counts it - in whole numbers, each division
/// rounded toward nothing where it stands: numbers, <c>+ - * /</c>, comparisons, <c>?:</c>, parentheses,
/// <c>min</c> and <c>max</c>, and the names -
/// <list type="bullet">
/// <item><c>lvl</c> the skill's level with every bonus, <c>blvl</c> its hard points (<b>0 for a skill an item
/// lends</b>: Battle Command from Call to Arms at level 18 reads "Bonus All Skills: +1" by <c>1 + blvl / 10</c>
/// - a screenshot), <c>ulvl</c> the character's level;</item>
/// <item><c>par1</c>..<c>par8</c>; <c>ln12</c> = par1 + (lvl - 1) x par2 (and ln34, ln56, ln78); <c>clc1</c>..<c>clc4</c>
/// the row's own <c>calc1</c>..<c>calc4</c>, at its level;</item>
/// <item><c>dm12</c>, the diminishing curve from par1 toward par2: <b><c>110 x lvl / (lvl + 6)</c> first, then
/// <c>x (par2 - par1) / 100</c>, then <c>+ par1</c></b>. The order is the game's: a Werewolf of level 1 is +20
/// attack rate (10..80: 15, then 10, then 20 - "Attack Rate: 120", a screenshot); the
/// product taken first gives 21. Every row measured before agreed with both;</item>
/// <item><c>edmn</c>, <c>edmx</c>, <c>edln</c>, the elemental columns by level band (not shifted for a passive:
/// Pierce at level 2 is 20 + 2), the first two raised by the skill's synergies; <c>edns</c> and <c>edxs</c>, the
/// same two shifted up by the row's <c>HitShift</c>; <c>toht</c>;</item>
/// <item><c>skill('Name'.x)</c>, another skill's <c>lvl</c>, <c>blvl</c> or any name above read in its row;
/// <c>stat('name'.accr)</c>, a stat of the character's.</item>
/// </list>
/// A formula that reads a name this does not know answers null, and its line shows nothing.
/// <para>
/// Checked against the game: Critical Strike <c>dm12</c> 5..75 at level 5 is 40, Dodge's <c>4 + lvl</c>, Pierce's
/// <c>edmn</c> (SpearZon); Battle Command's <c>ln34</c> 20 + 17 x 3 = 71 and <c>1 + blvl / 10</c>,
/// Werewolf's <c>dm34</c> and <c>1 + lvl / 3</c>, Frenzy 20's <c>dm56</c> = 42, Defiance 18's <c>lvl / 2</c> = 9
/// (screenshots).
/// </para>
/// </summary>
public sealed class SkillCalc
{
    /// <summary>What a formula may ask beyond its own skill: another skill's levels, the character's level, a stat.</summary>
    public sealed record Context(Func<string, (int Level, int BaseLevel)> Skill, int CharacterLevel, Func<string, int> Stat);

    private readonly SkillRow _row;
    private readonly int _level, _baseLevel;
    private readonly Context _context;
    private string _text = "";
    private int _at;

    public SkillCalc(SkillRow row, int level, int baseLevel, Context context)
    {
        _row = row;
        _level = level;
        _baseLevel = baseLevel;
        _context = context;
    }

    /// <summary>A formula's value, or null where it reads something this does not know.</summary>
    public int? Evaluate(string formula)
    {
        // The table quotes whatever holds a comma, sometimes in the middle of a formula: -"min(ln34, 10)".
        _text = formula.Replace("\"", "").Trim();
        _at = 0;
        if (_text.Length == 0)
            return null;

        try
        {
            int value = Conditional();
            Skip();
            return _at == _text.Length ? value : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private int Conditional()
    {
        int test = Comparison();
        Skip();
        if (!Take('?'))
            return test;

        int yes = Conditional();
        Skip();
        if (!Take(':'))
            throw new FormatException();
        int no = Conditional();
        return test != 0 ? yes : no;
    }

    private int Comparison()
    {
        int left = Sum();
        while (true)
        {
            Skip();
            if (Take(">=")) left = left >= Sum() ? 1 : 0;
            else if (Take("<=")) left = left <= Sum() ? 1 : 0;
            else if (Take("==")) left = left == Sum() ? 1 : 0;
            else if (Take("!=")) left = left != Sum() ? 1 : 0;
            else if (Take('>')) left = left > Sum() ? 1 : 0;
            else if (Take('<')) left = left < Sum() ? 1 : 0;
            else return left;
        }
    }

    private int Sum()
    {
        int left = Product();
        while (true)
        {
            Skip();
            if (Take('+')) left += Product();
            else if (Take('-')) left -= Product();
            else return left;
        }
    }

    private int Product()
    {
        int left = Unary();
        while (true)
        {
            Skip();
            if (Take('*')) left *= Unary();
            else if (Take('/'))
            {
                int right = Unary();
                left = right == 0 ? 0 : left / right;
            }
            else return left;
        }
    }

    private int Unary()
    {
        Skip();
        return Take('-') ? -Unary() : Atom();
    }

    private int Atom()
    {
        Skip();
        if (Take('('))
        {
            int inner = Conditional();
            Skip();
            if (!Take(')'))
                throw new FormatException();
            return inner;
        }

        if (_at < _text.Length && char.IsDigit(_text[_at]))
        {
            int start = _at;
            while (_at < _text.Length && char.IsDigit(_text[_at]))
                _at++;
            return int.Parse(_text[start.._at], CultureInfo.InvariantCulture);
        }

        string name = Word();
        Skip();

        if (name is "min" or "max")
        {
            if (!Take('('))
                throw new FormatException();
            int a = Conditional();
            Skip();
            if (!Take(','))
                throw new FormatException();
            int b = Conditional();
            Skip();
            if (!Take(')'))
                throw new FormatException();
            return name == "min" ? Math.Min(a, b) : Math.Max(a, b);
        }

        if (name is "skill" or "stat")
        {
            // skill('Name'.lvl), skill('Name'.ln12), stat('name'.accr): something of another skill's, or a stat.
            if (!Take('('))
                throw new FormatException();
            Skip();
            if (!Take('\''))
                throw new FormatException();
            int end = _text.IndexOf('\'', _at);
            if (end < 0)
                throw new FormatException();
            string of = _text[_at..end];
            _at = end + 1;
            if (!Take('.'))
                throw new FormatException();
            string field = Word();
            Skip();
            if (!Take(')'))
                throw new FormatException();

            if (name == "stat")
                return _context.Stat(of);

            var (level, baseLevel) = _context.Skill(of);
            if (field == "blvl")
                return baseLevel;
            if (field == "lvl")
                return level;

            // Any other name is read in that skill's own row, at its level: skill('Shape Shifting'.ln12).
            // A skill with no level gives nothing - a synergy not learned.
            if (SkillRows.Named(of) is not { } other)
                throw new FormatException();
            return level <= 0 ? 0 : new SkillCalc(other, level, baseLevel, _context).Named(field);
        }

        return Named(name);
    }

    private int Par(int n) => n >= 1 && n <= _row.Params.Length ? _row.Params[n - 1] : 0;

    private int Named(string name)
    {
        switch (name)
        {
            case "lvl": return _level;
            case "blvl": return _baseLevel;
            case "ulvl": return _context.CharacterLevel;
            case "edmn": return WithSynergies(ByBand(_row.Number("EMin"), "EMinLev", 5));
            case "edmx": return WithSynergies(ByBand(_row.Number("EMax"), "EMaxLev", 5));
            case "edln": return ByBand(_row.Number("ELen"), "ELevLen", 3);

            // The same two in the row's own fine units - shifted up by its HitShift: Holy Fire 27 (HitShift 6)
            // gives the Paladin "edns * par5 / 256" = 226 x 64 x 4 / 256 = 226 fire damage, and 285 at its most:
            // his page's fire line goes from 284-555 to 555-897 with the aura up, a mastery of 20% on both
            // (screenshots).
            case "edns": return WithSynergies(ByBand(_row.Number("EMin"), "EMinLev", 5)) << _row.Number("HitShift");
            case "edxs": return WithSynergies(ByBand(_row.Number("EMax"), "EMaxLev", 5)) << _row.Number("HitShift");
            case "toht": return _row.Number("ToHit") + (_level - 1) * _row.Number("LevToHit");
        }

        if (name.StartsWith("par", StringComparison.Ordinal) && int.TryParse(name[3..], out int n))
            return Par(n);

        // ln12: linear from par1 by par2 a level; dm12: diminishing from par1 toward par2.
        if (name.Length == 4 && int.TryParse(name.AsSpan(2, 1), out int a) && int.TryParse(name.AsSpan(3, 1), out int b))
        {
            if (name.StartsWith("ln", StringComparison.Ordinal))
                return Par(a) + (_level - 1) * Par(b);
            if (name.StartsWith("dm", StringComparison.Ordinal))
                return Par(a) + 110 * _level / (_level + 6) * (Par(b) - Par(a)) / 100;
        }

        // clc1..clc4: the row's own calc1..calc4, read at its level - Feral Rage's leech reads its calc2 through
        // skill('Feral Rage'.clc2).
        if (name.Length == 4 && name.StartsWith("clc", StringComparison.Ordinal) && name[3] is >= '1' and <= '4')
            return new SkillCalc(_row, _level, _baseLevel, _context).Evaluate(_row.Text($"calc{name[3]}")) ?? throw new FormatException();

        throw new FormatException();
    }

    /// <summary>
    /// The elemental columns' value raised by the skill's synergies - <c>EDmgSymPerCalc</c>, a percent by a
    /// formula of its own, mostly other skills' hard points: Deep Wounds 8 gives 220 open wounds damage a second
    /// by its columns and 387 with Grim Ward's 19 hard points at 4% each - a level 99 Barbarian's page reads "+842": 304 for
    /// level 99, 151 from his boots, and 387 (a screenshot). Rounded down.
    /// </summary>
    private int WithSynergies(int value)
    {
        if (_inSynergy || _row.Text("EDmgSymPerCalc") is not { Length: > 0 } formula)
            return value;

        // Read by a reader of its own: this one is in the middle of another formula.
        int percent = new SkillCalc(_row, _level, _baseLevel, _context) { _inSynergy = true }.Evaluate(formula) ?? 0;
        return value * (100 + percent) / 100;
    }

    private bool _inSynergy;

    /// <summary>
    /// An elemental column by level: the base, then so much a level in bands - 2-8, 9-16, 17-22, 23-28, 29 and
    /// on for damage (five columns), 2-8, 9-16, 17 and on for length (three).
    /// </summary>
    private int ByBand(int start, string perLevelColumn, int columns)
    {
        int[] ends = columns == 5 ? [8, 16, 22, 28, int.MaxValue] : [8, 16, int.MaxValue];
        int value = start, from = 1;
        for (int band = 0; band < ends.Length && _level > from; band++)
        {
            int to = Math.Min(_level, ends[band]);
            value += (to - from) * _row.Number($"{perLevelColumn}{band + 1}");
            from = to;
        }

        return value;
    }

    private string Word()
    {
        Skip();
        int start = _at;
        while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] == '_'))
            _at++;
        if (_at == start)
            throw new FormatException();
        return _text[start.._at];
    }

    private void Skip()
    {
        while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
            _at++;
    }

    private bool Take(char expected)
    {
        if (_at < _text.Length && _text[_at] == expected)
        {
            _at++;
            return true;
        }

        return false;
    }

    private bool Take(string expected)
    {
        if (string.CompareOrdinal(_text, _at, expected, 0, expected.Length) == 0)
        {
            _at += expected.Length;
            return true;
        }

        return false;
    }
}
