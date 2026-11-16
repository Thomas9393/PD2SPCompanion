namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// A mercenary's strength and dexterity at its level, and the weapons it takes, from
/// <c>hireling.txt</c>.
/// <para>
/// <b>Which row.</b> The table repeats each <c>Id</c>: once per game version (0 classic, 100
/// expansion) and, for some, once per level band. The row is the character's version's, the one
/// with the highest <c>Level</c> not above the mercenary's - or the lowest, below them all.
/// </para>
/// <para>
/// <b>The stats</b>, vanilla's rule until the game's own mercenary screen settles it:
/// <c>Str + (level - Level) x Str/Lvl / 8</c>, and the same for dexterity - the per-level columns
/// are in eighths. Unverified in PD2.
/// </para>
/// <para>
/// <b>Who it is</b> (<see cref="KindOf"/>) comes off the same row. The version matters there too: the classic
/// rows come first in the table and differ - a Barbarian of id 24 is "1hs" in classic and the Might one in the
/// expansion, and <c>Exp/Lvl</c>, which a mercenary's level is worked out from, is 105 against 100 for the cold
/// Rogue. Taken from the first row of the id, every expansion character's mercenary would be named
/// and levelled from its classic row: the Might Barbarian would not show correctly.
/// </para>
/// </summary>
public static class Hirelings
{
    private sealed record Row(int Id, int Version, int Level, int Strength, int StrengthPerLevel,
                              int Dexterity, int DexterityPerLevel, string Weapon1, string Weapon2,
                              string Hireling, string SubType, int Act, int Difficulty, int ExperiencePerLevel,
                              string HireDesc, string FirstSkill, string NameFirst, string NameLast,
                              IReadOnlyList<(string Skill, int Level, int LevelPerLevel, int Mode)> Skills)
    {
        public int Class { get; init; }
        public (int At, int PerLevel) Life { get; init; }
        public (int At, int PerLevel) Defense { get; init; }
        public (int At, int PerLevel) AttackRating { get; init; }
        public (int Min, int Max, int PerLevel) Damage { get; init; }
        public (int At, int PerLevel) Resist { get; init; }
    }

    /// <summary>
    /// A mercenary's own numbers at its level, before its gear: as the table gives them at its row's level, and
    /// for every level above it so much more - life, defense and attack rating whole, strength, dexterity and
    /// damage in eighths, resistance in quarters (vanilla's rule; the table's own rows, 3, 25 and 49 for a Rogue
    /// of Normal, are each the one before raised so). <see cref="Token"/> is its monster's code, which its
    /// animations go by: RG the Rogue, GU the Desert Guard, IW the Iron Wolf, $2 the Ascendant, 0A the Barbarian.
    /// <see cref="WeaponClass"/> is the weapon class its monster stands in with nothing in hand (monstats2 <c>BaseW</c>):
    /// the Barbarian of <c>act5hire2</c> swings a two-handed sword's animation, 16 frames - its Advanced Stats page's
    /// "0 (15) / 9 / 18 / 30 / 48 / 75 / 125" bare-handed (MercE, a screenshot).
    /// </summary>
    public sealed record Base(int Life, int Defense, int Strength, int Dexterity, int AttackRating, int MinDamage, int MaxDamage,
                              int Resist, string Token, string WeaponClass);

    private static Dictionary<int, string> _tokens = [];

    /// <summary>Each hireling monster's weapon class with nothing in hand, by its <c>hcIdx</c> (monstats <c>MonStatsEx</c>, monstats2 <c>BaseW</c>).</summary>
    private static Dictionary<int, string> _weaponClasses = [];

    /// <summary>The sequence each hireling monster uses a skill in (monstats <c>Sk1mode</c>..), by its <c>hcIdx</c> and the skill.</summary>
    private static Dictionary<(int Class, string Skill), string> _sequenceOf = [];

    /// <summary>
    /// What each hireling monster has of its own, by its <c>hcIdx</c>: monstats' <c>Skill1</c>..<c>Skill8</c> at
    /// <c>Sk1lvl</c>.. - the Rogue's <c>RoguePierce</c> 1, her 66% pierce, which hireling.txt does not give.
    /// </summary>
    private static Dictionary<int, List<(string Skill, int Level)>> _innate = [];

    /// <summary>Each monster sequence's length in frames: its rows in MonSeq.txt.</summary>
    private static Dictionary<string, int> _sequenceFrames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The mode a hireling uses a skill in, as the table numbers a monster's: 4 its attack, 5 its second, 7 a cast, 14 a sequence.</summary>
    public const int AttackMode = 4, SecondAttackMode = 5, CastMode = 7, SequenceMode = 14;

    /// <summary>
    /// A mercenary's skill as it uses it: the skill, the monster mode, and - for a sequence - the sequence's
    /// frames (the Desert Guard's Jab is <c>seq_act2guardjab</c>, 14 frames).
    /// </summary>
    public sealed record SkillUse(string Skill, int Mode, int SequenceFrames);

    /// <summary>
    /// Who a mercenary is, as the table has it: its act, the table's own name for the kind and the variant,
    /// the code of the line the hiring window describes it by (<c>HireDesc</c>), the difficulty it was hired
    /// in (1 to 3), what a level costs it in experience, and its first skill - its aura, for most.
    /// </summary>
    public sealed record Kind(int Act, string Hireling, string SubType, string HireDesc, int Difficulty, int ExperiencePerLevel, string FirstSkill);

    private static List<Row> _rows = [];

    public static void Load(GameData game)
    {
        var rows = new List<Row>();
        try
        {
            var (table, _) = game.ReadExcel("hireling");
            for (int at = 0; at < table.Rows.Count; at++)
            {
                int Number(string column) => int.TryParse(table.Value(at, column).Trim(), out int value) ? value : 0;

                if (!int.TryParse(table.Value(at, "Id").Trim(), out int id))
                    continue;

                string Text(string column) => table.IndexOfColumn(column) >= 0 ? table.Value(at, column).Trim() : "";

                rows.Add(new Row(id, Number("Version"), Number("Level"), Number("Str"), Number("Str/Lvl"),
                                 Number("Dex"), Number("Dex/Lvl"), table.Value(at, "WType1").Trim(), table.Value(at, "WType2").Trim(),
                                 Text("Hireling"), Text("SubType"), Number("Act"), Number("Difficulty"), Number("Exp/Lvl"),
                                 Text("HireDesc"), Text("Skill1"), Text("NameFirst"), Text("NameLast"),
                                 [.. Enumerable.Range(1, 6).Where(n => Text($"Skill{n}").Length > 0)
                                                .Select(n => (Text($"Skill{n}"), Number($"Level{n}"), Number($"LvlPerLvl{n}"), Number($"Mode{n}")))])
                {
                    Class = Number("Class"),
                    Life = (Number("HP"), Number("HP/Lvl")),
                    Defense = (Number("Defense"), Number("Def/Lvl")),
                    AttackRating = (Number("AR"), Number("AR/Lvl")),
                    Damage = (Number("Dmg-Min"), Number("Dmg-Max"), Number("Dmg/Lvl")),
                    Resist = (Number("Resist"), Number("Resist/Lvl")),
                });
            }
        }
        catch (Exception)
        {
            // Without the table a mercenary's stats are unknown and nothing is judged for it.
        }

        _rows = rows;

        var tokens = new Dictionary<int, string>();
        var weaponClasses = new Dictionary<int, string>();
        var sequenceOf = new Dictionary<(int, string), string>();
        var innate = new Dictionary<int, List<(string Skill, int Level)>>();
        var sequenceFrames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var classes = rows.Select(row => row.Class).ToHashSet();
        try
        {
            var (monsters, _) = game.ReadExcel("monstats");
            var extra = new Dictionary<int, string>();
            for (int at = 0; at < monsters.Rows.Count; at++)
            {
                if (!int.TryParse(monsters.Value(at, "hcIdx").Trim(), out int index))
                    continue;

                if (monsters.Value(at, "Code").Trim() is { Length: > 0 } code)
                    tokens.TryAdd(index, code);

                if (!classes.Contains(index))
                    continue;

                if (monsters.Value(at, "MonStatsEx").Trim() is { Length: > 0 } ex)
                    extra.TryAdd(index, ex);

                for (int n = 1; n <= 8; n++)
                {
                    if (monsters.IndexOfColumn($"Skill{n}") < 0 || monsters.Value(at, $"Skill{n}").Trim() is not { Length: > 0 } skill)
                        continue;

                    if (monsters.Value(at, $"Sk{n}mode").Trim() is { Length: > 0 } mode)
                        sequenceOf.TryAdd((index, skill), mode);

                    if (!innate.TryGetValue(index, out var own))
                        innate[index] = own = [];
                    if (own.All(each => each.Skill != skill))
                        own.Add((skill, int.TryParse(monsters.Value(at, $"Sk{n}lvl").Trim(), out int skillLevel) ? skillLevel : 0));
                }
            }

            var (looks, _) = game.ReadExcel("monstats2");
            var baseWeapon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int at = 0; at < looks.Rows.Count; at++)
            {
                if (looks.Value(at, "Id").Trim() is { Length: > 0 } id && looks.Value(at, "BaseW").Trim() is { Length: > 0 } weapon)
                    baseWeapon.TryAdd(id, weapon);
            }
            foreach (var (index, ex) in extra)
            {
                if (baseWeapon.TryGetValue(ex, out string? weapon))
                    weaponClasses[index] = weapon.ToUpperInvariant();
            }

            var (sequences, _) = game.ReadExcel("monseq");
            for (int at = 0; at < sequences.Rows.Count; at++)
            {
                if (sequences.Value(at, "sequence").Trim() is { Length: > 0 } name)
                    sequenceFrames[name] = sequenceFrames.GetValueOrDefault(name) + 1;
            }
        }
        catch (Exception)
        {
            // Without the monster tables a mercenary's animations are unknown, and its rows are left out.
        }

        _tokens = tokens;
        _weaponClasses = weaponClasses;
        _sequenceOf = sequenceOf;
        _innate = innate;
        _sequenceFrames = sequenceFrames;
    }

    /// <summary>A mercenary's skills as it uses them (<see cref="SkillUse"/>), in the table's order - empty for an unknown kind.</summary>
    public static IReadOnlyList<SkillUse> UsesOf(int typeId, bool expansion) =>
        RowOf(typeId, 99, expansion) is { } row
            ? [.. row.Skills.Select(each => new SkillUse(each.Skill, each.Mode,
                each.Mode == SequenceMode && _sequenceOf.TryGetValue((row.Class, each.Skill), out string? sequence)
                    ? _sequenceFrames.GetValueOrDefault(sequence) : 0))]
            : [];

    /// <summary>A mercenary's own numbers at its level (<see cref="Base"/>) - null for an unknown kind.</summary>
    public static Base? BaseOf(int typeId, int level, bool expansion)
    {
        if (RowOf(typeId, level, expansion) is not { } row)
            return null;

        // Under its row's level too, the columns counting down: SeqBarb's Hell guard (row level 75) set to 40 reads Strength
        // 112, Dexterity 87, Base Damage 12-19, resists 7, equip defense 47 (screenshots).
        int above = level - row.Level;
        return new Base(
            row.Life.At + above * row.Life.PerLevel,
            row.Defense.At + above * row.Defense.PerLevel,
            row.Strength + above * row.StrengthPerLevel / 8,
            row.Dexterity + above * row.DexterityPerLevel / 8,
            row.AttackRating.At + above * row.AttackRating.PerLevel,
            row.Damage.Min + above * row.Damage.PerLevel / 8,
            row.Damage.Max + above * row.Damage.PerLevel / 8,
            row.Resist.At + above * row.Resist.PerLevel / 4,
            _tokens.GetValueOrDefault(row.Class, ""),
            _weaponClasses.GetValueOrDefault(row.Class, "HTH"));
    }

    private static Row? RowOf(int typeId, int level, bool expansion)
    {
        var mine = _rows.Where(row => row.Id == typeId && row.Version == (expansion ? 100 : 0)).ToList();
        if (mine.Count == 0)
            mine = [.. _rows.Where(row => row.Id == typeId)];

        return mine.Where(row => row.Level <= level).MaxBy(row => row.Level) ?? mine.MinBy(row => row.Level);
    }

    /// <summary>Who the mercenary of a type id is, for a character of that game version - null for an unknown kind.</summary>
    public static Kind? KindOf(int typeId, bool expansion) =>
        RowOf(typeId, 0, expansion) is { } row
            ? new Kind(row.Act, row.Hireling, row.SubType, row.HireDesc, row.Difficulty, row.ExperiencePerLevel, row.FirstSkill)
            : null;

    /// <summary>
    /// The type ids of the same act as <paramref name="typeId"/>'s, for that game version: the variants a
    /// mercenary may be made without becoming another type (MercenaryEdit). By difficulty, then by id.
    /// </summary>
    public static IReadOnlyList<int> SameAct(int typeId, bool expansion)
    {
        if (RowOf(typeId, 0, expansion) is not { } mine)
            return [];

        return [.. _rows.Where(row => row.Version == mine.Version && row.Act == mine.Act)
                        .GroupBy(row => row.Id)
                        .Select(rows => rows.First())
                        .OrderBy(row => row.Difficulty).ThenBy(row => row.Id)
                        .Select(row => row.Id)];
    }

    /// <summary>The string keys a type's names run between (<c>NameFirst</c>, <c>NameLast</c>) - null for an unknown kind or a row without them.</summary>
    public static (string First, string Last)? NameKeys(int typeId, bool expansion) =>
        RowOf(typeId, 0, expansion) is { NameFirst.Length: > 0, NameLast.Length: > 0 } row ? (row.NameFirst, row.NameLast) : null;

    /// <summary>A mercenary's own strength and dexterity at its level, before its gear - null for an unknown kind.</summary>
    public static (int Strength, int Dexterity)? StatsOf(int typeId, int level, bool expansion) =>
        RowOf(typeId, level, expansion) is { } row
            ? (row.Strength + (level - row.Level) * row.StrengthPerLevel / 8,
               row.Dexterity + (level - row.Level) * row.DexterityPerLevel / 8)
            : null;

    /// <summary>
    /// A mercenary's own skills at its level, by the table's names (<c>Skill1</c>-<c>Skill6</c>): the row's level
    /// of each and, for every level the mercenary has above the row's, a thirty-second of <c>LvlPerLvl</c> - the
    /// engine's rule for a hireling. Its aura is among them: a Desert Mercenary "Def-Hell" of level 86 has
    /// Defiance 18, and the player beside it reads 9 and 9% more damage reduction (screenshots).
    /// Empty for an unknown kind.
    /// </summary>
    public static IReadOnlyList<(string Skill, int Level)> SkillsOf(int typeId, int level, bool expansion) =>
        RowOf(typeId, level, expansion) is { } row
            ? [.. row.Skills.Select(each => (each.Skill, each.Level + Math.Max(0, level - row.Level) * each.LevelPerLevel / 32))]
            : [];

    /// <summary>
    /// What a kind of mercenary's monster has of its own (monstats, <see cref="_innate"/>) - its attacks, and the
    /// Rogue's Pierce. Empty for an unknown kind.
    /// </summary>
    public static IReadOnlyList<(string Skill, int Level)> InnateOf(int typeId, bool expansion) =>
        RowOf(typeId, 99, expansion) is { } row && _innate.TryGetValue(row.Class, out var own) ? own : [];

    /// <summary>The act a kind of mercenary is hired in - 1 the Rogue Scout, 2 the Desert Guard, 3 the Iron Wolf, 4 the Ascendant, 5 the Barbarian; 0 for an unknown kind.</summary>
    public static int ActOf(int typeId, bool expansion) => RowOf(typeId, 99, expansion)?.Act ?? 0;

    /// <summary>The weapon types it takes (<c>WType1</c>, <c>WType2</c>) - a shield among them goes in the other hand.</summary>
    public static IReadOnlyList<string> WeaponsOf(int typeId, bool expansion) =>
        RowOf(typeId, 99, expansion) is { } row
            ? [.. new[] { row.Weapon1, row.Weapon2 }.Where(type => type.Length > 0)]
            : [];
}
