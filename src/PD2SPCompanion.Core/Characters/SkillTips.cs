using System.Globalization;
using PD2SPCompanion.Core.GameFiles;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// What the skill tree's tooltip says of a skill beyond its name: what it
/// does, in its general description line, its hard points and the levels items add, and the synergies it
/// receives, and no more. Not a simulation: a first cut also
/// showed the game's lines that move with hard points ("Maximum Curses: 2"), but gear moves those too (King
/// Leoric's arm's extra skeletons, Den Mother's grizzlies), so they went the same day.
/// <para>
/// The synergies are <c>skilldesc.txt</c>'s <c>dsc3</c> section, worded as the game words it - its numbers are
/// written out or are the skill's own parameters - under "Receives bonuses from:" without the skill's name.
/// </para>
/// </summary>
public static class SkillTips
{
    private static TxtTable? _skills;
    private static TxtTable? _descriptions;
    private static readonly Dictionary<int, string[]> _byId = [];
    private static readonly Dictionary<string, string[]> _byName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string[]> _descByKey = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(GameData game)
    {
        _byId.Clear();
        _byName.Clear();
        _descByKey.Clear();
        try
        {
            _skills = game.Excel("skills");
            _descriptions = game.Excel("skilldesc");

            int id = _skills.IndexOfColumn("Id"), name = _skills.IndexOfColumn("skill");
            foreach (var row in _skills.Rows)
            {
                if (int.TryParse(Cell(row, id), out int skill))
                    _byId.TryAdd(skill, row);
                if (Cell(row, name) is { Length: > 0 } named)
                    _byName.TryAdd(named, row);
            }

            int key = _descriptions.IndexOfColumn("skilldesc");
            foreach (var row in _descriptions.Rows)
                if (Cell(row, key) is { Length: > 0 } described)
                    _descByKey.TryAdd(described, row);
        }
        catch (Exception)
        {
            // Without the tables the tooltip keeps the name and the points.
            _skills = _descriptions = null;
        }
    }

    // ---- the levels items add ---------------------------------------------------------------------------

    /// <summary>
    /// The levels a character's gear adds to a skill - the soft points: all skills, the class's skills, the
    /// skill's tree and its element ("+1 to Fire Skills") <b>only where the character has the skill</b>, as the
    /// game has it: a hard point, or "+N to Skill (Class Only)" (<c>item_singleskill</c>) or "+N to Skill" (an
    /// oskill, <c>item_nonclassskill</c>), which grant it whatever it holds: without a hard point, a single
    /// skill or an oskill still works, and a skill tab, class skill or all skills bonus alone does not light it
    /// up; once either has lit it, all the rest apply too. <paramref name="gear"/> is what counts
    /// (<see cref="Requirements.GearStats"/> of <see cref="Requirements.Counted"/>). Seen in game: a Raise
    /// Skeleton Warrior of 20 hard points with King Leoric's arm on reads level 25 here and in game.
    /// <para>
    /// An oskill of the character's own class counts for <see cref="OwnClassOskillCap"/> at most from each item
    /// (+6 skeleton to a Necromancer is capped at +3, on each item) - and the
    /// tree's skills are all its own class's. Each oskill line is one item's: an item holds one line a skill.
    /// </para>
    /// </summary>
    public static int BonusFrom(IEnumerable<(string Stat, int Value, int Param)> gear, SkillEntry skill, int classId, int hard)
    {
        int granted = 0, soft = 0;
        string element = Element(skill);
        foreach (var (stat, value, param) in gear)
        {
            if (stat == "item_nonclassskill")
            {
                if (param == skill.Id)
                    granted += Math.Min(value, OwnClassOskillCap);
                continue;
            }

            if (stat == "item_singleskill")
            {
                if (param == skill.Id)
                    granted += value;
                continue;
            }

            soft += stat switch
            {
                "item_allskills" => value,
                "item_addclassskills" when param == classId => value,
                // The class three bits above the tree: tree 0 is the class's StrSkillTab1, as the page is (SkillTabs).
                "item_addskill_tab" when (param >> 3) == classId && (param & 0x7) == skill.Page - 1 => value,
                // "+N to Fire Skills" and its kin:
                // item_elemskill, the half that grants - its partners item_elemskill_fire and so on only print
                // the line (StatCatalogue) - against the skill's EType, as the engine reads it.
                "item_elemskill" when element.Length > 0 && param >= 0 && param < ElementTypes.Length
                                      && ElementTypes[param] == element => value,
                _ => 0,
            };
        }

        return granted + (hard > 0 || granted > 0 ? soft : 0);
    }

    /// <summary>The most levels an item's oskill adds to a skill of the character's own class.</summary>
    public const int OwnClassOskillCap = 3;

    /// <summary>
    /// <c>item_elemskill</c>'s parameter to the <c>EType</c> of the skills it raises - the order of
    /// <see cref="Items.StatText.Elements"/>, and the <c>val1</c> properties.txt gives <c>fireskill</c> (1),
    /// <c>ltngskill</c> (2), <c>magskill</c> (3), <c>coldskill</c> (4) and <c>poisskill</c> (5).
    /// </summary>
    private static readonly string[] ElementTypes = ["", "fire", "ltng", "mag", "cold", "pois"];

    /// <summary>The skill's element (skills.txt <c>EType</c>), or "" for none.</summary>
    private static string Element(SkillEntry skill) => ElementOf(skill.Id);

    /// <summary>A skill's element by its id (skills.txt <c>EType</c>: fire, ltng, cold...), or "" for none or unknown.</summary>
    public static string ElementOf(int skillId) =>
        _skills is not null && _byId.TryGetValue(skillId, out var row) ? Cell(row, _skills.IndexOfColumn("EType")) : "";

    /// <summary>
    /// The levels <paramref name="gear"/> would add to a skill holding no hard point once it has one: all, class,
    /// tree and element bonuses, which the game leaves out until then - none where an oskill or the skill's own line
    /// grants it already.
    /// </summary>
    public static int Waiting(IEnumerable<(string Stat, int Value, int Param)> gear, SkillEntry skill, int classId)
    {
        var counted = gear as IReadOnlyCollection<(string Stat, int Value, int Param)> ?? [.. gear];
        return BonusFrom(counted, skill, classId, hard: 1) - BonusFrom(counted, skill, classId, hard: 0);
    }

    // ---- what it does ------------------------------------------------------------------------------------

    /// <summary>
    /// What the skill does, as the game's tooltip says it: the long description's lines - which the string keeps
    /// last first - or the short one where there is no long one.
    /// </summary>
    public static IReadOnlyList<string> Description(SkillEntry skill)
    {
        if (_skills is null || _descriptions is null || !_byId.TryGetValue(skill.Id, out var row)
            || !_descByKey.TryGetValue(Cell(row, _skills.IndexOfColumn("skilldesc")), out var description))
        {
            return [];
        }

        var strings = StringTable.Current;
        return [.. (Text(strings, Desc(description, "str long")) ?? Text(strings, Desc(description, "str short")) ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Reverse()];
    }

    // ---- the synergies -----------------------------------------------------------------------------------

    /// <summary>
    /// The synergies <paramref name="skill"/> receives, as the game lists them - "Receives bonuses from:" and a
    /// line for each ("Curse Mastery: +1% Reduced Physical Resistance Per Two Levels"); empty for a skill that
    /// receives none.
    /// </summary>
    public static IReadOnlyList<string> Synergies(SkillEntry skill)
    {
        if (_skills is null || _descriptions is null || !_byId.TryGetValue(skill.Id, out var row)
            || !_descByKey.TryGetValue(Cell(row, _skills.IndexOfColumn("skilldesc")), out var description))
        {
            return [];
        }

        var strings = StringTable.Current;
        var lines = new List<string>();
        for (int i = 1; i <= 8; i++)
        {
            if (!int.TryParse(Desc(description, $"dsc3line{i}"), out int func))
                continue;

            string a = Text(strings, Desc(description, $"dsc3texta{i}")) ?? "";
            string b = Text(strings, Desc(description, $"dsc3textb{i}")) ?? "";
            int? ca = Value(Desc(description, $"dsc3calca{i}"), row);
            int? cb = Value(Desc(description, $"dsc3calcb{i}"), row);
            if (Synergy(func, a, b, ca, cb) is { Length: > 0 } line)
                lines.Add(line);
        }

        return lines;
    }

    /// <summary>A synergy-section line in the game's words, by its <c>descline</c> function.</summary>
    private static string? Synergy(int func, string a, string b, int? ca, int? cb) => a.Trim().Length == 0 ? null : func switch
    {
        // The game's "Amplify Damage Receives Bonuses From:" - the skill's name is the tooltip's own heading
        // already, so "receives bonus from" is enough.
        40 => L("Receives bonuses from:"),
        // "Curse Mastery: +1% Reduced Physical Resistance Per Two Levels"
        63 => ca is { } percent ? $"{a}: +{percent}% {b}" : $"{a}: {b}",
        // "Curse Mastery: +0.6 Radius per Three Levels" - tenths
        42 => cb is { } tenths ? $"{a}: +{(tenths / 10.0).ToString("0.#", CultureInfo.InvariantCulture)} {b}" : $"{a}: {b}",
        // "Penetrate: +40 Attack Rating per Level"
        67 => ca is { } number ? $"{a}: +{number} {b}" : $"{a}: {b}",
        // "Oak Sage: +3-6 Life Healed per Level"
        52 => ca is { } low && cb is { } high ? $"{a}+{low}-{high}{b}" : null,
        // a line of words: "Critical Strike", "Gains +3 Additional Absorb per Energy"
        18 or 25 => a,
        _ => null,
    };

    // ---- the calc language, as far as a synergy's numbers go ------------------------------------------------

    /// <summary>A synergy line's number - written out, or the skill's <c>par1</c>-<c>par8</c> - or null where it asks for more.</summary>
    private static int? Value(string calc, string[] row)
    {
        if (string.IsNullOrWhiteSpace(calc))
            return null;
        try
        {
            return new Calc(calc.Trim().Trim('"'), row).Evaluate();
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The game's skill calc language in its integer arithmetic, as far as the synergy section uses it: numbers,
    /// a skill's <c>par1</c>-<c>par8</c> and <c>skill('Name'.parN)</c>, <c>+ - * /</c> and brackets. Anything
    /// else is not supported, and the line keeps its words without the number.
    /// </summary>
    private sealed class Calc(string text, string[] row)
    {
        private int _at;

        public int Evaluate()
        {
            int value = Additive();
            Skip();
            if (_at != text.Length)
                throw new FormatException(text);
            return value;
        }

        private int Additive()
        {
            int value = Term();
            while (true)
            {
                Skip();
                if (Eat('+')) value += Term();
                else if (Eat('-')) value -= Term();
                else return value;
            }
        }

        private int Term()
        {
            int value = Unary();
            while (true)
            {
                Skip();
                if (Eat('*')) value *= Unary();
                else if (Eat('/'))
                {
                    int by = Unary();
                    value = by == 0 ? 0 : value / by;
                }
                else return value;
            }
        }

        private int Unary()
        {
            Skip();
            if (Eat('-')) return -Unary();
            if (Eat('+')) return Unary();
            return Primary();
        }

        private int Primary()
        {
            Skip();
            if (Eat('('))
            {
                int inner = Additive();
                Skip();
                if (!Eat(')')) throw new FormatException(text);
                return inner;
            }

            if (_at < text.Length && char.IsDigit(text[_at]))
            {
                int start = _at;
                while (_at < text.Length && char.IsDigit(text[_at])) _at++;
                return int.Parse(text.AsSpan(start, _at - start), CultureInfo.InvariantCulture);
            }

            string word = Word();
            Skip();
            if (word == "skill")
            {
                if (!Eat('(') || !Eat('\'')) throw new FormatException(text);
                int close = text.IndexOf('\'', _at);
                if (close < 0) throw new FormatException(text);
                string name = text[_at..close];
                _at = close + 1;
                if (!Eat('.')) throw new FormatException(text);
                string field = Word();
                Skip();
                if (!Eat(')')) throw new FormatException(text);
                return _byName.TryGetValue(name, out var other) && Param(other, field) is { } theirs
                    ? theirs
                    : throw new NotSupportedException(name);
            }

            return Param(row, word) ?? throw new NotSupportedException(word);
        }

        private string Word()
        {
            int start = _at;
            while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] == '_')) _at++;
            return text[start.._at];
        }

        private bool Eat(char c)
        {
            if (_at < text.Length && text[_at] == c)
            {
                _at++;
                return true;
            }
            return false;
        }

        private void Skip()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
        }
    }

    /// <summary>A skill's <c>par1</c>-<c>par8</c> (skills.txt <c>Param1</c>-<c>Param8</c>), or null for another word.</summary>
    private static int? Param(string[] row, string word)
    {
        if (_skills is null || word.Length != 4 || !word.StartsWith("par", StringComparison.OrdinalIgnoreCase) || !char.IsDigit(word[3]))
            return null;
        return int.TryParse(Cell(row, _skills.IndexOfColumn($"Param{word[3]}")), out int value) ? value : 0;
    }

    private static string Desc(string[] row, string column) => _descriptions is null ? "" : Cell(row, _descriptions.IndexOfColumn(column));

    /// <summary>A string by its key, colour codes taken out; null where the tables have none.</summary>
    private static string? Text(StringTable? strings, string key)
    {
        if (key.Length == 0 || strings?.Get(key) is not { } text)
            return null;

        var plain = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            // ÿc and a colour letter.
            if (text[i] == 'ÿ' && i + 1 < text.Length && text[i + 1] == 'c')
            {
                i += 2;
                continue;
            }
            plain.Append(text[i]);
        }
        return plain.ToString();
    }

    private static string Cell(string[] row, int at) => at >= 0 && at < row.Length ? row[at].Trim() : "";
}
