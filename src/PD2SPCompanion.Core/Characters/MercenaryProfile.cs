using System.Text;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// Who the character's mercenary is, read from the save and named from the installed
/// hireling table.
/// <para>
/// The save stores a type id and a name id, not text. The type id is a row of <c>hireling.txt</c>
/// (<see cref="Hirelings.KindOf"/>, by the character's game version). The name id is an offset into a range
/// of string keys that table also names, and that part is <b>unverified</b>.
/// </para>
/// <para>
/// <b>What it is called</b> (with the wiki's table of mercenaries beside it): the kind
/// by its act - Rogue, Desert Guard, Iron Wolf, Ascendant, Barbarian - the variant by the line the hiring
/// window describes it with (Fire, Cold, Physical; Defensive, Offensive, Combat; Lightning; Dark, Light;
/// Combat, Warcries), and its first skill by the name the game prints - the aura, for most: "Barbarian",
/// "Combat (Might), Normal, level 80". The table's own labels are its makers' shorthand ("Eastern Sorceror",
/// "Mght-Normal", "A4 AmpDmg") and stand in only where a newer table has a kind these words do not know.
/// </para>
/// </summary>
public sealed record MercenaryProfile(
    bool IsHired,
    string Kind,
    string Variant,
    bool IsDead,
    uint Experience,
    int ExperiencePerLevel = 0,
    string Skill = "",
    string Difficulty = "")
{
    public static readonly MercenaryProfile None =
        new(false, "", "", false, 0);

    /// <summary>
    /// What level the mercenary is. <b>The save does not store it</b> - only experience -
    /// so it is worked back out the way the game works it out: a mercenary reaches level
    /// <c>n</c> at <c>Exp/Lvl * n * n * (n + 1)</c> experience, with <c>Exp/Lvl</c> coming
    /// from its own row of <c>hireling.txt</c> and differing by kind and difficulty.
    /// <para>
    /// Checked against the game: a Rogue Scout, Fire - Normal, <c>Exp/Lvl</c> 100, showed
    /// level 5 at 15,000 experience and named 25,200 as the next - which is 100*25*6 and
    /// 100*36*7 exactly.
    /// </para>
    /// </summary>
    public int Level
    {
        get
        {
            if (!IsHired || ExperiencePerLevel <= 0)
                return 0;

            int level = 1;
            while (level < 99 && NeededFor(ExperiencePerLevel, level + 1) <= Experience)
                level++;

            return level;
        }
    }

    /// <summary>The experience a mercenary needs to reach a level.</summary>
    public static long NeededFor(int perLevel, int level) =>
        (long)perLevel * level * level * (level + 1);

    /// <summary>What to call it, as far as the tables allow.</summary>
    public string Title => !IsHired ? L("No mercenary")
        : Kind.Length == 0 ? L("Mercenary")
        : Kind;

    /// <summary>
    /// The variant with its skill - "Dark (Amplify Damage)", "Combat (Might)" - and <b>not</b> the difficulty
    /// it was hired in, as PD2 no longer makes a difference. Checked
    /// against PD2's table: brought to one level, a variant hired in Normal, Nightmare or Hell is the
    /// same mercenary, bar five points of one skill's cast chance on two of the Iron Wolf's Nightmare rows.
    /// </summary>
    public string VariantText =>
        Variant.Length > 0 && Skill.Length > 0 ? $"{Variant} ({Skill})"
        : Variant.Length > 0 ? Variant
        : Skill;

    /// <summary>
    /// Its level and what it is, as a character's card reads: "Level 77 Barbarian". Not that it is dead: a badge
    /// says that beside the line, as a dead character's does (<see cref="IsDead"/>).
    /// </summary>
    public string LevelLine => !IsHired ? ""
        : Level > 0 ? L($"Level {Level} {Title}") : Title;

    /// <summary>Everything in a line, the difficulty it was hired in too: what the command line says of it.</summary>
    public string Detail => !IsHired ? ""
        : string.Join(", ", new[]
        {
            Variant.Length > 0 && Skill.Length > 0 ? $"{Variant} ({Skill})"
                : Variant.Length > 0 ? Variant
                : Skill.Length > 0 ? Skill : null,
            Difficulty.Length > 0 ? Difficulty : null,
            IsDead ? L("dead") : null,
            Level > 0 ? L($"level {Level}") : null,
        }.Where(part => part is not null));

    public static MercenaryProfile Read(D2S save, GameData? game)
    {
        var merc = save.Mercenary;

        // Id zero is the game's way of saying none was ever hired; the other fields are
        // then meaningless rather than zeroed facts.
        if (merc is null || merc.Id == 0)
            return None;

        // The tables are the installed game's (Hirelings, loaded with them): without a game, unnamed.
        if (game is null || Hirelings.KindOf(merc.TypeId, save.Status.IsExpansion) is not { } kind)
            return new MercenaryProfile(true, "", "", merc.IsDead != 0, merc.Experience);

        return new MercenaryProfile(
            IsHired: true,
            Kind: KindName(kind),
            Variant: VariantName(kind),
            IsDead: merc.IsDead != 0,
            Experience: merc.Experience,
            ExperiencePerLevel: kind.ExperiencePerLevel,
            Skill: SkillName(kind.FirstSkill),
            Difficulty: kind.Difficulty switch { 1 => L("Normal"), 2 => L("Nightmare"), 3 => L("Hell"), _ => "" });
    }

    /// <summary>The kind by its act, as players and the wiki name it; the table's own label for an act these do not know.</summary>
    private static string KindName(Hirelings.Kind kind) => kind.Act switch
    {
        1 => L("Rogue"),
        2 => L("Desert Guard"),
        3 => L("Iron Wolf"),
        4 => L("Ascendant"),
        5 => L("Barbarian"),
        _ => kind.Hireling,
    };

    /// <summary>
    /// The variant by its hiring line's code (<c>HireDesc</c>); for a code these do not know, the table's
    /// <c>SubType</c> without the difficulty it ends in ("Mght-Normal", "Fire - Hell").
    /// </summary>
    private static string VariantName(Hirelings.Kind kind) => kind.HireDesc.ToLowerInvariant() switch
    {
        "farw" or "fire" => L("Fire"),
        "carw" or "cold" => L("Cold"),
        "parw" => L("Physical"),
        "ltng" => L("Lightning"),
        "def" => L("Defensive"),
        "off" => L("Offensive"),
        "comb" or "mght" => L("Combat"),
        "wwnd" => L("Warcries"),
        "ascd" => L("Dark"),
        "ascl" => L("Light"),
        _ => kind.SubType.Split('-')[0].Trim(),
    };

    /// <summary>A skill of the table by the name the game prints ("A4 AmpDmg" is Amplify Damage); the table's where nothing words it.</summary>
    private static string SkillName(string skill) =>
        skill.Length == 0 ? ""
        : Items.SkillNames.IdOf(skill) is { } id && Items.SkillNames.NameOf(id) is { Length: > 0 } printed ? printed
        : skill;
}
