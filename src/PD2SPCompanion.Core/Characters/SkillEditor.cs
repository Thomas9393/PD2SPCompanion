using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// Reads a character's skills and moves their points: one at a time, or all at once.
/// <para>
/// A skill tree is not thirty-three independent counters, so a point goes by the tables'
/// rules, and taking a prerequisite down refunds what depends on it. In goes one unspent point, below the skill's
/// <c>maxlvl</c>, only with every <c>reqskill</c> holding a point of its own - a skill an item lends or raises
/// is no prerequisite, as in the game's own tree - and, unless told to add freely, only at the skill's
/// <c>reqlevel</c>. Out comes a
/// point back to the pool; a skill emptied takes every skill that needs it down with it,
/// through the whole chain, so nothing is left standing on an empty prerequisite.
/// </para>
/// <para>
/// The save keeps hard points and nothing derived from them, and the game reads them as they
/// are: a respec is the same write, done to every skill.
/// </para>
/// </summary>
public sealed class SkillEditor(D2S save, SkillTree tree)
{
    /// <summary>Where unspent skill points live. Absent from the save when it is zero.</summary>
    private const string Pool = "newskills";

    private readonly ClassSkills _skills = save.ClassSkills;
    private readonly Dictionary<string, int> _stats = save.Attributes.Stats;

    public SkillTree Tree { get; } = tree;

    public bool IsAvailable => Tree.IsAvailable;

    /// <summary>Points the character has left to assign.</summary>
    public int Unspent => _stats.TryGetValue(Pool, out int value) ? value : 0;

    /// <summary>
    /// Adds unspent skill points - or, negative, takes them away - held to nought and to the
    /// save's field. Free mode's; the points are spent in the game, the trees here being read-only.
    /// </summary>
    /// <returns>How far the pool actually moved.</returns>
    public int AddUnspent(int points)
    {
        int before = Unspent;
        _stats[Pool] = Math.Clamp(before + points, 0, AttributeEditor.PoolCeiling(Pool));
        return Unspent - before;
    }

    /// <summary>
    /// Points sitting in skills, counting the ones the mod has retired.
    /// <para>
    /// Those are not shown - PD2 took them out of the game - but a character carried over
    /// from before may still have points in them, and a respec that quietly dropped those
    /// would lose the character points it never accounted for.
    /// </para>
    /// </summary>
    public int Spent => _skills.Skills.Sum(skill => skill.Points);

    public bool CanRespec => IsAvailable && Spent > 0;

    /// <summary>Points in one skill, by its slot among the thirty.</summary>
    public int PointsIn(int slot) =>
        slot >= 0 && slot < _skills.Skills.Count ? _skills.Skills[slot].Points : 0;

    /// <summary>
    /// Why a point cannot go into this skill, or null. <paramref name="freely"/> - Free mode - leaves out the
    /// character level. The prerequisites hold in every mode, by their own points: Lower Resist at +19 from items,
    /// Weaken with none, takes no point. <paramref name="withoutPrerequisites"/> leaves them out too, for a test
    /// character built skill by skill. The pool and the skill's maximum always hold.
    /// </summary>
    public string? CannotAdd(SkillEntry skill, int characterLevel, bool freely, bool withoutPrerequisites = false)
    {
        if (Unspent <= 0)
            return L("No skill points to spend.");

        if (PointsIn(skill.Slot) >= skill.MaxLevel)
            return L($"{skill.Name} at max level {skill.MaxLevel}.");

        if (!freely && characterLevel < skill.RequiredLevel)
            return L($"Require level {skill.RequiredLevel} for {skill.Name}.");

        if (withoutPrerequisites)
            return null;

        var missing = skill.Requires
            .Where(id => Tree.BySlot.Values.FirstOrDefault(each => each.Id == id) is { } needed && PointsIn(needed.Slot) == 0)
            .Select(id => Tree.BySlot.Values.First(each => each.Id == id).Name)
            .ToList();

        return missing.Count > 0 ? L($"Require {string.Join(", ", missing)} for {skill.Name}.") : null;
    }

    /// <summary>
    /// Puts up to <paramref name="count"/> points into a skill, each one checked as
    /// <see cref="CannotAdd"/> checks it. Returns how many went in, and why the next did not.
    /// </summary>
    public (int Added, string? Stopped) Add(SkillEntry skill, int count, int characterLevel, bool freely, bool withoutPrerequisites = false)
    {
        int added = 0;
        string? stopped = null;

        while (added < count)
        {
            if (CannotAdd(skill, characterLevel, freely, withoutPrerequisites) is { } why)
            {
                stopped = why;
                break;
            }

            _skills.Skills[skill.Slot].Points++;
            _stats[Pool] = Unspent - 1;
            added++;
        }

        return (added, stopped);
    }

    /// <summary>
    /// Takes up to <paramref name="count"/> points out of a skill, back to the pool. Emptied, it
    /// empties every skill that needs it - and every skill that needs those - refunding them
    /// all. Returns each skill and what it gave back, the one asked for first.
    /// </summary>
    public IReadOnlyList<(SkillEntry Skill, int Points)> Remove(SkillEntry skill, int count)
    {
        var refunded = new List<(SkillEntry, int)>();

        int taken = Math.Min(count, PointsIn(skill.Slot));
        if (taken <= 0)
            return refunded;

        _skills.Skills[skill.Slot].Points -= (byte)taken;
        refunded.Add((skill, taken));

        if (PointsIn(skill.Slot) == 0)
        {
            var emptied = new Queue<SkillEntry>([skill]);
            while (emptied.TryDequeue(out var gone))
            {
                foreach (var dependent in Tree.BySlot.Values.Where(each => each.Requires.Contains(gone.Id)))
                {
                    int held = PointsIn(dependent.Slot);
                    if (held == 0)
                        continue;

                    _skills.Skills[dependent.Slot].Points = 0;
                    refunded.Add((dependent, held));
                    emptied.Enqueue(dependent);
                }
            }
        }

        _stats[Pool] = Math.Min(Unspent + refunded.Sum(each => each.Item2), AttributeEditor.PoolCeiling(Pool));
        return refunded;
    }

    /// <summary>
    /// An Iron Golem stands and the character no longer has its skill: a save the game refuses ("bad
    /// hireables"). Whoever took the skill away dismisses the golem (<see cref="DismissGolem"/>).
    /// </summary>
    public bool GolemStandsUnskilled => save.Golem is { Exists: true } && !HasGolemSkill();

    /// <summary>
    /// Whether the character has the Iron Golem's skill: a point in it, or an item worn and in use that
    /// gives it - as an other class's skill (PD2's Metalgrid: <c>oskill IronGolem</c>) or by charges (the
    /// game's own Metalgrid). <paramref name="without"/> is an item about to come off, left out.
    /// <para>
    /// In use: not the weapon set that is put away, nor an item whose requirements are not met - the
    /// game gives nothing for either (Requirements.Judge).
    /// </para>
    /// </summary>
    public bool HasGolemSkill(Item? without = null) => HasGolemSkill(without is null ? [] : [without]);

    /// <summary>The same, with several items about to come off - what a save takes to the vault.</summary>
    public bool HasGolemSkill(IReadOnlyCollection<Item> without) =>
        (Tree.IronGolemSlot is { } slot && PointsIn(slot) > 0) || WornGivesGolem(without);

    /// <summary>Whether an item worn and in use, other than those in <paramref name="without"/>, gives the Iron Golem's skill.</summary>
    private bool WornGivesGolem(IReadOnlyCollection<Item>? without = null)
    {
        if (Tree.IronGolemId is not { } skill)
            return false;

        int strength = Saves.AttributeScale.Display("strength", save.Attributes.Stats.GetValueOrDefault("strength"));
        int dexterity = Saves.AttributeScale.Display("dexterity", save.Attributes.Stats.GetValueOrDefault("dexterity"));
        return Requirements.Judge(save, WeaponSets.InHand, strength, dexterity)
            .Where(judged => judged.Met && !(without ?? []).Any(each => ReferenceEquals(each, judged.Item)))
            .Any(judged => Gives(judged.Item, skill));
    }

    /// <summary>Whether an item, or what is in its sockets, gives a skill: as an other class's, or by charges.</summary>
    private static bool Gives(Item item, int skill) =>
        item.WithSockets()
            .SelectMany(each => each.StatLists)
            .SelectMany(list => list.Stats)
            .Any(stat => stat.Stat switch
            {
                "item_nonclassskill" => stat.Param == skill,
                "item_charged_skill" => stat.SkillId == skill,
                _ => false,
            });

    /// <summary>
    /// Whether a respec would leave a standing Iron Golem without its skill: no item worn gives it, so the
    /// points are all it has. Asked before the respec, which then needs room for the golem's item.
    /// </summary>
    public bool RespecEmptiesGolemSkill => save.Golem is { Exists: true } && !WornGivesGolem();

    /// <summary>The item a standing Iron Golem was made of, or null where none stands.</summary>
    public Item? GolemItem => save.Golem is { Exists: true, Item: { } item } ? item : null;

    /// <summary>
    /// Whether taking <paramref name="count"/> points out of <paramref name="skill"/> would leave the
    /// Iron Golem's skill empty: its own last point, or the last of a skill it stands on - emptied, a
    /// skill takes those that need it down with it (<see cref="Remove"/>).
    /// </summary>
    public bool RemovalEmptiesGolemSkill(SkillEntry skill, int count)
    {
        if (Tree.IronGolemSlot is not { } golem || PointsIn(golem) == 0 || count < PointsIn(skill.Slot))
            return false;

        // An item worn gives the skill whatever the points do.
        if (WornGivesGolem())
            return false;

        var emptied = new Queue<SkillEntry>([skill]);
        var seen = new HashSet<int> { skill.Slot };
        while (emptied.TryDequeue(out var gone))
        {
            if (gone.Slot == golem)
                return true;
            foreach (var dependent in Tree.BySlot.Values.Where(each => each.Requires.Contains(gone.Id) && seen.Add(each.Slot)))
                emptied.Enqueue(dependent);
        }
        return false;
    }

    /// <summary>The golem gone from the save; the item it was made of handed back, for the caller to place.</summary>
    public Item? DismissGolem()
    {
        if (save.Golem is not { Exists: true } golem)
            return null;

        var item = golem.Item;
        golem.Exists = false;
        golem.Item = null!;
        return item;
    }

    /// <summary>Empties the tree and returns every point to the pool.</summary>
    public int RespecSkills()
    {
        if (!IsAvailable)
            return 0;

        int refunded = Spent;
        if (refunded == 0)
            return 0;

        foreach (var skill in _skills.Skills)
            skill.Points = 0;

        // Held to what the save's field holds: past it the file keeps only the low bits, and the points read back as few.
        _stats[Pool] = Math.Min(Unspent + refunded, AttributeEditor.PoolCeiling(Pool));
        return refunded;
    }
}
