namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Ormus' Robes' <c>sorc-skill-rand-ctc</c> - func 25, the one row of any table on it: "+2 to [a Sorceress skill]" and
/// a chance to cast that same skill when casting (the Forge left both lines off, the function having no writer). The row says neither the skill nor
/// the rule: <c>par 2, min 12, max 26</c>. What the game does, measured on 126 S13 ladder robes:
/// <list type="bullet">
/// <item><c>item_singleskill</c> at <c>par</c> (2), the skill its parameter.</item>
/// <item><c>item_skilloncast</c> of the same skill: the chance <c>min</c> (12%), two thirds of it (8%) for a skill
/// with a cooldown - Blaze, Meteor, Blizzard; the level <c>max</c> (26) less one for each six levels of the
/// skill's required level past the first six - Frozen Orb, required 30, casts at 22. All 18 skills seen fit
/// both, and S14's 20.</item>
/// </list>
/// The skills drawn among are the ones seen (<see cref="Pool"/>): a Sorceress's direct damage, no mastery, armor,
/// Warmth, Teleport, Thunder Storm, Enchant or Inferno. Season 13's 126 robes held 18; <b>Season 14 added Telekinesis
/// and Fire Wall</b> - 350 S14 ladder robes on the market hold all 20, 5 to 28 each, and both casts fit
/// the rule (Fire Wall has a cooldown: 8% at 25). Static Field and Energy Shield are in neither. A season's tables
/// can move it: the pool is measured, not read. Season 13's items are not checked (legacy), so one pool serves.
/// </summary>
public static class RandomSkillCast
{
    /// <summary>The property function: a mod slot of PD2's own, its rule in code rather than in the tables.</summary>
    public const string Function = "25";

    /// <summary>The skills the game draws among, as 350 S14 ladder robes hold them (Telekinesis 43 and Fire Wall 51 new).</summary>
    public static readonly IReadOnlyList<int> Pool =
        [36, 38, 39, 43, 44, 45, 46, 47, 48, 49, 51, 53, 55, 56, 59, 62, 64, 369, 376, 383];

    /// <summary>Whether a property is this one.</summary>
    public static bool Is(string propCode) => AffixRanges.FunctionOf(propCode) == Function;

    /// <summary>How often it casts: the row's <c>min</c>, two thirds of it for a skill with a cooldown.</summary>
    public static int Chance(int skill, int min) =>
        SkillNames.HasCooldown(skill) ? min * 2 / 3 : min;

    /// <summary>The level it casts at: the row's <c>max</c>, less one for each six levels of the skill's requirement past six.</summary>
    public static int Level(int skill, int max) =>
        max - Math.Max(0, SkillNames.RequiredLevelOf(skill) / 6 - 1);

    /// <summary>The two lines, for a skill drawn: the levels granted, and the cast of the same skill.</summary>
    public static IReadOnlyList<AffixRanges.Wrote> Written(int par, int min, int max, int skill) =>
    [
        new("item_singleskill", par, Param: skill),
        new("item_skilloncast", Chance(skill, min), SkillId: skill, SkillLevel: Level(skill, max)),
    ];

    /// <summary>A skill drawn from the pool - the first where nothing is drawn (an item made at fixed numbers).</summary>
    public static int Drawn(Random? random) => Pool[random?.Next(Pool.Count) ?? 0];

    /// <summary>Another skill of the pool than <paramref name="held"/>, as a die gives another.</summary>
    public static int DrawnAgain(int held, Random random)
    {
        var others = Pool.Where(skill => skill != held).ToList();
        return others[random.Next(others.Count)];
    }

    /// <summary>
    /// The item's two lines of the same skill, as the Demonic Cube leaves them: another skill drawn, and the cast's
    /// chance and level as the rule gives them for it (the PD2 wiki, Chests: "The same skill will be chosen for both
    /// random affixes"; seen changing under the cube on OrmusTest). False for an item without them.
    /// </summary>
    public static bool Reroll(D2SLib.Model.Save.Item item, Random random)
    {
        if (AffixRanges.RandomSkillCastsOn(item).FirstOrDefault() is not { Max: > 0 } row)
            return false;

        var stats = item.StatLists.SelectMany(list => list.Stats).ToList();
        var grant = stats.FirstOrDefault(stat => stat.Stat == "item_singleskill" && Pool.Contains(stat.Param ?? -1));
        var cast = stats.FirstOrDefault(stat => stat.Stat == "item_skilloncast" && stat.SkillId == grant?.Param);
        if (grant?.Param is not { } held || cast is null)
            return false;

        int skill = DrawnAgain(held, random);
        grant.Param = skill;
        cast.SkillId = skill;
        cast.Value = Chance(skill, row.Min);
        cast.SkillLevel = Level(skill, row.Max);
        return true;
    }
}
