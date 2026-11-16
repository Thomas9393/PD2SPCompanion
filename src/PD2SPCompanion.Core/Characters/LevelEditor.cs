using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// A character's level and experience, moved the way the game moves them.
/// Each level gained brings what a level-up brings - the class's attribute points
/// (<c>StatPerLevel</c>), one skill point, and its life, stamina and mana per level - and a level
/// given back takes the same away. What it takes comes out of the unspent pools, so a level goes
/// no lower than they cover: points spent stay spent, and a respec is the way to free them.
/// <para>
/// The level is kept in two places, the header's byte and the <c>level</c> attribute, and both
/// are written. Setting a level puts experience at that level's start; setting experience moves
/// the level to the one it falls in. Experience is a 32-bit field the save layer reads as signed,
/// so everything past 2,147,483,647 - level 94 onwards - arrives negative and is read unsigned.
/// </para>
/// </summary>
public sealed class LevelEditor(D2S save, ClassStats stats, ExperienceTable table)
{
    /// <summary>A level-up's skill point. No table says so; it is the engine's.</summary>
    public const int SkillsPerLevel = 1;

    private const string StatPool = "statpts";
    private const string SkillPool = "newskills";
    private const string LevelStat = "level";
    private const string ExperienceStat = "experience";

    private readonly Dictionary<string, int> _stats = save.Attributes.Stats;

    /// <summary>False when the class table or the experience table could not be read.</summary>
    public bool IsAvailable => stats != ClassStats.Unknown && stats.StatPerLevel > 0 && table.IsAvailable;

    public int Level => save.Level;

    public long Experience => unchecked((uint)Raw(ExperienceStat));

    /// <summary>
    /// As low as the level can go: each level given back takes its points from the unspent
    /// pools, which cannot go below nought.
    /// </summary>
    public int LowestLevel =>
        Math.Max(1, Level - Math.Min(Raw(StatPool) / stats.StatPerLevel, Raw(SkillPool) / SkillsPerLevel));

    /// <summary>Why the level goes no lower, or null where it can: level 1, or a level's points are spent.</summary>
    public string? CannotLower =>
        Level <= 1 ? L("At level 1.")
        : LowestLevel >= Level ? L($"Require {stats.StatPerLevel} attribute points and {SkillsPerLevel} skill point unspent.")
        : null;

    /// <summary>Why the level goes no higher, or null where it can: the table's last level, or a pool's field full.</summary>
    public string? CannotRaise =>
        Level >= table.MaxLevel ? L($"At max level {table.MaxLevel}.")
        : HighestLevel <= Level ? L("Points to spend at field maximum.")
        : null;

    /// <summary>As high as it can go: the table's last level, or where a pool's field would overflow.</summary>
    public int HighestLevel
    {
        get
        {
            int roomForStats = (AttributeEditor.PoolCeiling(StatPool) - Raw(StatPool)) / stats.StatPerLevel;
            int roomForSkills = (AttributeEditor.PoolCeiling(SkillPool) - Raw(SkillPool)) / SkillsPerLevel;
            return Math.Min(table.MaxLevel, Level + Math.Min(roomForStats, roomForSkills));
        }
    }

    /// <summary>
    /// Moves to <paramref name="level"/>, held to <see cref="LowestLevel"/> and
    /// <see cref="HighestLevel"/>, with experience at its start. Says whether anything changed.
    /// </summary>
    public bool SetLevel(int level)
    {
        if (!IsAvailable)
            return false;

        // A level held where it is - refused, or asked for as it stands - keeps its experience:
        // resetting that to the level's start would take away what was earned within it.
        int target = Math.Clamp(level, LowestLevel, HighestLevel);
        if (target == Level)
            return false;

        Move(target - Level);
        SetExperienceField(table.Start(target));
        return true;
    }

    /// <summary>
    /// Sets experience, and moves the level to the one it falls in - held, like the level, to
    /// what the pools allow: experience a level the pools cannot give back is raised to that
    /// level's start, and experience past the highest reachable level stops at its end.
    /// </summary>
    public bool SetExperience(long experience)
    {
        if (!IsAvailable)
            return false;

        long lowest = table.Start(LowestLevel);
        int highest = HighestLevel;
        long ceiling = highest >= table.MaxLevel ? table.Cap : table.Start(highest + 1) - 1;
        long wanted = Math.Clamp(experience, lowest, ceiling);

        long before = Experience;
        int from = Level;

        Move(table.LevelOf(wanted) - from);
        SetExperienceField(wanted);

        return Level != from || Experience != before;
    }

    /// <summary>Whether the level is the table's last: there is no next level to be part way to.</summary>
    public bool AtMaxLevel => Level >= table.MaxLevel;

    /// <summary>The experience this level begins at, and the next one's - the span the progress is of.</summary>
    public long LevelStart => table.Start(Level);
    public long NextLevelStart => AtMaxLevel ? table.Cap : table.Start(Level + 1);

    /// <summary>
    /// How far into this level the experience is, in whole percent, 0 to 99 - rounded down, so 99 is
    /// as far as a level goes before the next. 100 at the last level.
    /// </summary>
    public int Progress
    {
        get
        {
            long span = NextLevelStart - LevelStart;
            if (AtMaxLevel || span <= 0)
                return 100;

            return (int)Math.Clamp((Experience - LevelStart) * 100 / span, 0, 99);
        }
    }

    /// <summary>
    /// Puts experience <paramref name="percent"/> of the way into this level, the level kept. Rounded up,
    /// so <see cref="Progress"/> reads back the percent asked for. Not at the last level. Says whether
    /// anything changed.
    /// </summary>
    public bool SetProgress(int percent)
    {
        if (!IsAvailable || AtMaxLevel)
            return false;

        long span = NextLevelStart - LevelStart;
        long into = (span * Math.Clamp(percent, 0, 99) + 99) / 100;
        return SetExperience(LevelStart + into);
    }

    /// <summary>Base life, mana and stamina - what the save stores, before any gear adds to it.</summary>
    public (int Life, int Mana, int Stamina) Base =>
        (AttributeScale.Display("maxhp", Raw("maxhp")),
         AttributeScale.Display("maxmana", Raw("maxmana")),
         AttributeScale.Display("maxstamina", Raw("maxstamina")));

    /// <summary>What <paramref name="levels"/> level-ups bring, or take back when negative.</summary>
    private void Move(int levels)
    {
        if (levels == 0)
            return;

        SetPool(StatPool, Raw(StatPool) + levels * stats.StatPerLevel);
        SetPool(SkillPool, Raw(SkillPool) + levels * SkillsPerLevel);

        foreach (var (max, current, quarters) in new[]
                 {
                     ("maxhp", "hitpoints", stats.LifePerLevel),
                     ("maxstamina", "stamina", stats.StaminaPerLevel),
                     ("maxmana", "mana", stats.ManaPerLevel),
                 })
        {
            // Quarters, in the stat's stored scale, as the attribute editor works them.
            int delta = levels * quarters * (1 << AttributeScale.ShiftFor(max)) / ClassStats.PerPointDivisor;
            Set(max, Raw(max) + delta);
            Set(current, Math.Min(Raw(current) + delta, Raw(max)));
        }

        int level = Level + levels;
        save.Level = (byte)level;
        _stats[LevelStat] = level;
    }

    /// <summary>
    /// The 32 bits as the save layer holds them: past the signed range, a negative number. None
    /// at all for nought, as the game writes a character that has earned nothing.
    /// </summary>
    private void SetExperienceField(long experience)
    {
        if (experience <= 0)
            _stats.Remove(ExperienceStat);
        else
            _stats[ExperienceStat] = unchecked((int)(uint)experience);
    }

    private int Raw(string stat) => _stats.TryGetValue(stat, out int value) ? value : 0;

    private void Set(string stat, int value) => _stats[stat] = Math.Max(0, value);

    /// <summary>
    /// A pool of points, left out of the save at nought as the game leaves it out - so a level
    /// raised and given back is the file it was, byte for byte.
    /// </summary>
    private void SetPool(string pool, int value)
    {
        if (value <= 0)
            _stats.Remove(pool);
        else
            _stats[pool] = value;
    }
}
