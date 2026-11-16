using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>How a buff comes to be up - which says whether it is pressed, and how it begins.</summary>
public enum BuffKind
{
    /// <summary>An aura an item gives while worn - the character's own, or the mercenary's, which reaches the character.</summary>
    ItemAura,

    /// <summary>A skill an item casts for as long as it is worn (PD2's "SelfAura" skills).</summary>
    WornSkill,

    /// <summary>The mercenary's own aura.</summary>
    MercenaryAura,

    /// <summary>A lingering skill of the character's - its own or one an item lends - that is cast.</summary>
    Cast,

    /// <summary>A Paladin's own aura: one at a time.</summary>
    OwnAura,

    /// <summary>A spirit's aura - Oak Sage, Heart of Wolverine, Spirit of Barbs: one spirit at a time.</summary>
    Spirit,

    /// <summary>A lingering skill cast from an item's charges.</summary>
    Charges,

    /// <summary>A lingering skill an item casts by chance - when struck, on a hit, on a kill, on casting and the rest.</summary>
    ChanceToCast,
}

/// <summary>
/// One thing in the sheet's buff row:
/// what is up for good - an item's aura, the mercenary's, a skill an item casts while worn - is shown and not
/// pressed; what is cast is pressed, and begins on ("the maximized route"), one of each lot that excludes one
/// another.
/// </summary>
/// <param name="Level">The skill's level with every bonus; <paramref name="BaseLevel"/> its hard points - 0 for what an item gives.</param>
/// <param name="From">What gives it, for a buff that is not a skill of the character's own: the item, the mercenary.</param>
public sealed record Buff(SkillRow Skill, string Name, int Level, int BaseLevel, BuffKind Kind, string From)
{
    /// <summary>What a press is remembered by.</summary>
    public string Key => OnMercenary ? $"{Kind}:{Skill.Id}:m" : $"{Kind}:{Skill.Id}";

    /// <summary>The skill it is shown as - its icon: a spirit's summon rather than the aura it holds; the skill itself for the rest.</summary>
    public SkillRow Face { get; init; } = Skill;

    /// <summary>Whether it is pressed on and off - not what is up for good.</summary>
    public bool Toggles => Kind is BuffKind.Cast or BuffKind.OwnAura or BuffKind.Spirit or BuffKind.Charges or BuffKind.ChanceToCast;

    /// <summary>The lot it belongs to, of which one is up at a time - "" for a buff that excludes nothing.</summary>
    public string Rivals { get; init; } = "";

    /// <summary>How many of its lot can be up at once: one, but the spirits with Spiritkeeper's extra (<see cref="Buffs.Candidates"/>).</summary>
    public int Room { get; init; } = 1;

    public bool On { get; init; }

    /// <summary>
    /// An item aura the character itself wears - not the mercenary's or the golem's, which reach it as the party: it
    /// gives the character the lines a Paladin's own aura gives him (<see cref="Buffs.Gives"/>).
    /// </summary>
    public bool Worn { get; init; }

    /// <summary>
    /// The mercenary's: an item aura it wears, the party's lines to the character and its owner's to the mercenary - or what
    /// it casts itself, for its own sheet alone (ForMercenary).
    /// </summary>
    public bool OnMercenary { get; init; }

    /// <summary>On, and its lines counted: of two that put the same state on, the higher level's are.</summary>
    public bool Counted { get; init; }

    /// <summary>The form it needs and does not have (Feral Rage as no wolf) - null where nothing holds it back.</summary>
    public string? Waits { get; init; }

    /// <summary>
    /// What it must be cast with and the hands do not hold - "Shield" for Holy Shield with none - or null. Off, as a
    /// skill that waits for its form is: the game will not cast it (<see cref="Buffs.NeedsOf"/>).
    /// </summary>
    public string? Needs { get; init; }
}

/// <summary>
/// The states a skill puts on, from <c>states.txt</c>: which exclude one another (<c>group</c> - the cold armors;
/// Burst of Speed and Fade; Holy Shield and Holy Sword; the forms), and which turn the character into a monster
/// (<c>gfxtype</c> 1, <c>gfxclass</c> the monster) - whose animations its breakpoints are then counted on: the
/// wolf's (monstats <c>Code</c> 40) cast row 6 / 14 / 26 / 40 / 60 / 95 / 157 and the bear's (TG) hit recovery
/// 5 / 10 / 16 / 24 / 37 / 54 / 86 / 152 / 360 are the game's page's own (screenshots).
/// </summary>
public static class SkillStates
{
    public sealed record State(string Name, int Id, int Group, string? FormToken);

    private static Dictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);

    public static State? Of(string name) => _states.GetValueOrDefault(name);

    public static void Load(GameData game)
    {
        var found = new Dictionary<string, State>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var tokens = new Dictionary<int, string>();
            var (monsters, _) = game.ReadExcel("monstats");
            for (int row = 0; row < monsters.Rows.Count; row++)
            {
                if (int.TryParse(monsters.Value(row, "hcIdx").Trim(), out int index) && monsters.Value(row, "Code").Trim() is { Length: > 0 } code)
                    tokens.TryAdd(index, code);
            }

            var (states, _) = game.ReadExcel("states");
            for (int row = 0; row < states.Rows.Count; row++)
            {
                string name = states.Value(row, "state").Trim();
                if (name.Length == 0 || !int.TryParse(states.Value(row, "id").Trim(), out int id))
                    continue;

                _ = int.TryParse(states.Value(row, "group").Trim(), out int group);
                string? token = states.Value(row, "gfxtype").Trim() == "1"
                                && int.TryParse(states.Value(row, "gfxclass").Trim(), out int monster)
                                && tokens.TryGetValue(monster, out string? code) ? code : null;
                found.TryAdd(name, new State(name, id, group, token));
            }
        }
        catch (Exception problem)
        {
            // No states: no buff excludes another and no form changes a row.
            Log.Caught(problem);
        }

        _states = found;
    }
}

/// <summary>What the sheet's buff row holds for a character, and which of it is up.</summary>
internal static class Buffs
{
    /// <summary>A skill's own level and hard points for the character - (0, 0) for one it has not.</summary>
    internal delegate (int Level, int BaseLevel) LevelOf(int skill);

    /// <summary>The stats of a skill an item casts by chance, one for each thing that sets it off.</summary>
    internal static readonly IReadOnlySet<string> ChanceStats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "item_skillongethit", "item_skillonhit", "item_skillonattack", "item_skillonkill",
        "item_skillondeath", "item_skillonlevelup", "item_skilloncast",
    };

    /// <summary>
    /// What a mercenary keeps up that can be pressed: a lingering skill of its own - an Act 4
    /// Dark's Bone Armor, an Act 5 Warcries' Battle Orders - and what its gear casts by chance. Its own begins on, as a
    /// character's cast skills do; the gear's off. Kept apart from the character's by <see cref="Buff.OnMercenary"/>.
    /// </summary>
    internal static List<Buff> OfMercenary(
        IReadOnlyDictionary<int, (int Level, int BaseLevel)> skills, IEnumerable<Requirements.Sourced> gear, SkillCalc.Context context,
        Func<SkillRow, string> nameOf, Func<Item, string> itemName, string itself)
    {
        var found = new List<Buff>();
        foreach (var (id, (level, baseLevel)) in skills)
        {
            if (level > 0 && SkillRows.Of(id) is { } row && Lingers(row, level, context, baseLevel))
                found.Add(OwnCast(row, level, baseLevel, nameOf, itself));
        }

        foreach (var each in gear)
        {
            if (each.Item is not null && ChanceStats.Contains(each.Stat) && SkillRows.Of(each.Param) is { } row && Lingers(row, each.SkillLevel, context))
                found.Add(new Buff(row, nameOf(row), each.SkillLevel, 0, BuffKind.ChanceToCast, L($"{itemName(each.Item)} (mercenary)")) { OnMercenary = true });
        }

        return found;
    }

    /// <summary>
    /// The most a mercenary's cast counts at. An Act 4 Dark of level 97 reads Bone Armor's +320% (Defense 4704 = 1120 x 4.2,
    /// MercDark, screenshots), blvl 32 where its row says 34, as its masteries stop at 32.
    /// </summary>
    private const int MostAMercenaryCasts = 32;

    private static Buff OwnCast(SkillRow row, int level, int baseLevel, Func<SkillRow, string> nameOf, string itself)
    {
        int held = Math.Min(baseLevel, MostAMercenaryCasts);
        return new Buff(row, nameOf(row), level - baseLevel + held, held, BuffKind.Cast, itself) { OnMercenary = true, Rivals = RivalsOf(row) };
    }

    /// <summary>
    /// Everything that could be up: from the gear's own stats, the character's skills, and - where it is beside
    /// the character, out of town - the mercenary and what it wears.
    /// </summary>
    internal static List<Buff> Candidates(
        IReadOnlyList<Requirements.Sourced> gear, IReadOnlyDictionary<int, (int Level, int BaseLevel)> levels,
        SkillCalc.Context context, Func<SkillRow, string> nameOf, Func<Item, string> itemName,
        D2S save, GameData game, bool outOfTown, bool withMercenary = true, IReadOnlySet<string>? held = null)
    {
        var found = new List<Buff>();

        // Auras from the items one wears add up: two of one skill are one aura at the sum of their levels - Dream's
        // helm and shield, Holy Shock 15 each, one of 30 (the D2 wiki's Item Skill Bonuses; Blizzard's D2R forum).
        // Not with an aura of another source - the character's own, the mercenary's, another
        // wearer's - of which the higher is counted (Resolve). Named for every item it is of.
        void Summed(IEnumerable<(SkillRow Row, int Level, string Item)> auras, Func<string, string> from, bool worn) =>
            found.AddRange(WornAuras(auras, nameOf, from).Select(buff => buff with { Worn = worn, OnMercenary = !worn }));

        Summed(gear.Where(each => each is { Stat: "item_aura", Item: not null } && SkillRows.Of(each.Param) is not null)
                   .Select(each => (SkillRows.Of(each.Param)!, each.Value, itemName(each.Item!))), items => items, worn: true);

        foreach (var each in gear)
        {
            if (each.Item is null || SkillRows.Of(each.Param) is not { } row)
                continue;

            switch (each.Stat)
            {
                case "item_skillonequip":
                    found.Add(new Buff(row, nameOf(row), each.Value, 0, BuffKind.WornSkill, itemName(each.Item)));
                    break;
                case "item_charged_skill" when Lingers(row, each.SkillLevel, context):
                    found.Add(new Buff(row, nameOf(row), each.SkillLevel, 0, BuffKind.Charges, itemName(each.Item)));
                    break;
                // A spirit summoned off charges puts its aura out as a learned one does: Wisp Projector's Spirit of
                // Barbs 2 gives Deadly Strike 9 and Attacker Takes Damage 23 (a level 90 Amazon).
                case "item_charged_skill" when outOfTown && SpiritAura(row) is not null:
                    found.Add(Spirit(row, each.SkillLevel, 0, context, nameOf) with { Kind = BuffKind.Charges, From = itemName(each.Item) });
                    break;
                case var stat when ChanceStats.Contains(stat) && Lingers(row, each.SkillLevel, context):
                    found.Add(new Buff(row, nameOf(row), each.SkillLevel, 0, BuffKind.ChanceToCast, itemName(each.Item)));
                    break;
            }
        }

        foreach (var (id, (level, baseLevel)) in levels)
        {
            if (level <= 0 || SkillRows.Of(id) is not { } row)
                continue;

            // A spirit: its aura is a skill of its own, at the level the summon says - and like the mercenary's it
            // does not reach the character in town.
            if (SpiritAura(row) is not null)
            {
                if (outOfTown)
                    found.Add(Spirit(row, level, baseLevel, context, nameOf));
                continue;
            }

            if (row.IsAura && row.AuraState.Length > 0)
                found.Add(new Buff(row, nameOf(row), level, baseLevel, BuffKind.OwnAura, "") { Rivals = "aura" });
            else if (Lingers(row, level, context, baseLevel))
                found.Add(new Buff(row, nameOf(row), level, baseLevel, BuffKind.Cast, "") { Rivals = RivalsOf(row), Needs = NeedsOf(row, held ?? new HashSet<string>()) });
        }

        // One spirit at a time (petmax 1 + extra_spirits): a second with Spiritkeeper, the one item that gives the
        // extra (uniqueitems.txt).
        int spirits = 1 + gear.Where(each => each.Stat == "extra_spirits").Sum(each => each.Value);
        for (int at = 0; at < found.Count; at++)
        {
            if (found[at].Rivals == "spirit")
                found[at] = found[at] with { Room = spirits };
        }

        if (withMercenary && outOfTown && save.Mercenary is { Id: not 0, IsDead: 0 } hired)
        {
            int mercenaryLevel = MercenaryProfile.Read(save, game).Level;
            var worn = Requirements.JudgeMercenary(save, mercenaryLevel).Where(judged => judged.Met).Select(judged => judged.Item).ToList();
            var wornStats = Requirements.GearStatsBySource(worn).ToList();
            string mercenary = L("Mercenary");

            // Its own aura, raised by "+ to All Skills" on what it wears: a Defiance of 18 with 2 on its gear
            // gives the player 10 and 10% (a level 99 Sorceress's page out of town, a screenshot).
            int more = wornStats.Where(each => each.Stat == "item_allskills").Sum(each => each.Value);
            foreach (var (skill, level) in Hirelings.SkillsOf(hired.TypeId, mercenaryLevel, save.Status.IsExpansion))
            {
                if (SkillRows.Named(skill) is { IsAura: true } row)
                    found.Add(new Buff(row, nameOf(row), level + more, level, BuffKind.MercenaryAura, mercenary));
            }

            Summed(wornStats.Where(each => each is { Stat: "item_aura", Item: not null } && SkillRows.Of(each.Param) is not null)
                            .Select(each => (SkillRows.Of(each.Param)!, each.Value, itemName(each.Item!))),
                   items => L($"{items} (mercenary)"), worn: false);
        }

        // What the mercenary casts that reaches the party, in town too: a Warcries Barbarian's Battle Orders 21 is on the
        // character as on itself, Life 306 -> 656 and Mana 211 -> 386, the row's ln34 of 350 and half of it, its own 2187 ->
        // 2537 (MercWar, screenshots). One switch with its own row's, by the key.
        if (withMercenary && save.Mercenary is { Id: not 0, IsDead: 0 } casting)
        {
            int at = MercenaryProfile.Read(save, game).Level;
            int lifted = Requirements.GearStatsBySource(Requirements.JudgeMercenary(save, at).Where(judged => judged.Met).Select(judged => judged.Item).ToList())
                .Where(each => each.Stat == "item_allskills").Sum(each => each.Value);
            foreach (var (skill, level) in Hirelings.SkillsOf(casting.TypeId, at, save.Status.IsExpansion))
            {
                if (SkillRows.Named(skill) is { } row && Lingers(row, level + lifted, context, level)
                    && OwnCast(row, level + lifted, level, nameOf, L("Mercenary")) is var cast && ReachesOthers(cast))
                    found.Add(cast);
            }
        }

        // The Iron Golem's item: its aura is the golem's, and reaches the party as the mercenary's does - out of town.
        // Its other stats are
        // the golem's own.
        if (withMercenary && outOfTown && save.Golem is { Exists: true, Item: { } golem })
        {
            foreach (var each in Requirements.GearStatsBySource([golem]))
            {
                if (each.Stat == "item_aura" && SkillRows.Of(each.Param) is { } row)
                    found.Add(new Buff(row, nameOf(row), each.Value, 0, BuffKind.ItemAura, L($"{itemName(golem)} (Iron Golem)")));
            }
        }

        return found;
    }

    /// <summary>A spirit's aura: what the summon puts out on the caster, its <c>summon</c> skill. Null for any other skill.</summary>
    private static SkillRow? SpiritAura(SkillRow row) =>
        row.SummonSkill.Length > 0 && SkillRows.Named(row.SummonSkill) is { IsAura: true, GivesTheCaster: true } aura ? aura : null;

    /// <summary>
    /// A spirit, out of town: its aura as a skill of its own at the level the summon says. The spirit holds it all as
    /// hard points: Spirit of Barbs 25 (20 of them the Druid's) raises the deadly strike ceiling by blvl / 4 = 6 -
    /// "Deadly Strike: 50 / 81" (a level 95 Druid out of town, a screenshot).
    /// </summary>
    private static Buff Spirit(SkillRow row, int level, int baseLevel, SkillCalc.Context context, Func<SkillRow, string> nameOf)
    {
        int auraLevel = new SkillCalc(row, level, baseLevel, context).Evaluate(row.SummonSkillLevel) ?? level;
        return new Buff(SpiritAura(row)!, nameOf(row), auraLevel, auraLevel, BuffKind.Spirit, "") { Rivals = "spirit", Face = row };
    }

    /// <summary>
    /// The auras one wearer's items give, a buff a skill: two of one skill one aura at the sum of their levels, named for
    /// every item it is of (see <see cref="Candidates"/>).
    /// </summary>
    internal static IEnumerable<Buff> WornAuras(IEnumerable<(SkillRow Row, int Level, string Item)> auras, Func<SkillRow, string> nameOf,
                                                Func<string, string> from) =>
        auras.GroupBy(aura => aura.Row.Id).Select(same => new Buff(
            same.First().Row, nameOf(same.First().Row), same.Sum(aura => aura.Level), 0, BuffKind.ItemAura,
            from(string.Join(" + ", same.Select(aura => aura.Item)))));

    /// <summary>The skill functions (<c>srvdofunc</c>) of a cry - Shout, Battle Orders, Battle Command - and of Enchant, cast on others.</summary>
    internal const int Cry = 68, CastOnOthers = 25;

    /// <summary>
    /// Whether a buff that is up reaches the party - the mercenary - and not only the one it is on: an aura, an
    /// item's, the mercenary's or a Paladin's own; a spirit's; a cry; Enchant. An armor, a form, Fade, Holy Shield
    /// are the caster's (skills.txt: an aura is <c>aura</c> 1; the cries are <c>srvdofunc</c> 68).
    /// </summary>
    internal static bool ReachesOthers(Buff buff) =>
        buff.Kind is BuffKind.ItemAura or BuffKind.MercenaryAura or BuffKind.OwnAura or BuffKind.Spirit
        || buff.Skill.IsAura || buff.Skill.ServerDo is Cry or CastOnOthers;

    /// <summary>
    /// The candidates with what the other weapon set casts and the swap back keeps (<paramref name="other"/>, its
    /// own candidates): a cast, a spirit, a charge it gives at a higher level than the set in hand, or that only it
    /// has - in that buff's place, said where it is from (<paramref name="fromOther"/>). Not what must be worn to
    /// last - an item's aura, what an item keeps cast or casts when struck - nor a Paladin's own aura, the skill he
    /// holds.
    /// </summary>
    internal static List<Buff> WithOtherSet(List<Buff> mine, List<Buff> other, Func<Buff, string> fromOther)
    {
        var found = new List<Buff>(mine);
        // Only what that set can cast (Buff.Needs) - and it stands in for one the hands in use cannot, whatever its level.
        foreach (var buff in other.Where(buff => buff.Kind is (BuffKind.Cast or BuffKind.Spirit or BuffKind.Charges) && buff.Needs is null))
        {
            int at = found.FindIndex(each => each.Key == buff.Key);
            if (at >= 0 && found[at].Needs is null && found[at].Level >= buff.Level)
                continue;

            var kept = buff with { From = fromOther(buff) };
            if (at >= 0)
                found[at] = kept;
            else
                found.Add(kept);
        }

        return found;
    }

    /// <summary>
    /// Whether a skill is a buff of the caster's own that stays: it puts a state on the caster, its lines are
    /// the caster's (<see cref="SkillRow.GivesTheCaster"/>), it is no summon and no charge-up, and it lasts five
    /// seconds or more - Berserk's half second and Joust's two are the attack's own. One with no length lasts until its
    /// absorption is spent, and counts for what it gives beside that: Bone Armor's <c>item_armor_percent</c> (blvl x 10, the
    /// Act 4 Dark's +350% at 98), where Cyclone Armor gives only the absorption, which no page shows.
    /// </summary>
    private static bool Lingers(SkillRow row, int level, SkillCalc.Context context, int baseLevel = 0) =>
        row.AuraState.Length > 0 && row.GivesTheCaster && row.Auras.Count > 0
        && row.Summon.Length == 0 && !row.IsProgressive && !row.IsAura
        && (row.AuraLength.Length == 0 && row.ServerDo == SkillRow.SetsAState
            ? row.Auras.Any(aura => aura.Stat is not ("bonearmor" or "bonearmormax"))
            : (new SkillCalc(row, level, baseLevel, context).Evaluate(row.AuraLength) ?? 0) >= 125);

    /// <summary>
    /// The lot a cast skill excludes: a form excludes every other form; a state with a group excludes its
    /// group's others among the skills that set a state on the caster the plain way (<c>srvdofunc</c> 18) - the
    /// cold armors, Burst of Speed and Fade, Holy Shield and Holy Sword. The group alone does not say it: Feral
    /// Rage is of the wolf's group and is used as a wolf.
    /// </summary>
    private static string RivalsOf(SkillRow row)
    {
        if (row.ServerDo == SkillRow.ChangesForm)
            return "form";

        return row.ServerDo == SkillRow.SetsAState && SkillStates.Of(row.AuraState) is { Group: > 0 } state ? $"group {state.Group}" : "";
    }

    /// <summary>
    /// Which of the candidates are up. What is up for good is; what is pressed is as the player left it, else as
    /// it begins: a cast skill on, a Paladin's aura where a skill box holds it, a form where a skill box holds
    /// it or a skill only that form can use, and of any other lot that excludes one another the one a box holds,
    /// else the highest level. Charges and what is cast when struck begin off. A skill that needs a form waits
    /// for it; and of two that put the same state on, the higher level is the one counted.
    /// <para>
    /// A Paladin's own aura is the right box's, and the left box's only where the right holds no aura (in game:
    /// an aura may sit on both hands, but only the right hand's works, unless the right hand's is not
    /// an aura, then the left's works - Fanaticism on the left with Zeal on the right reads "Attack Rate:
    /// 117").
    /// </para>
    /// </summary>
    internal static List<Buff> Resolve(List<Buff> candidates, IReadOnlyDictionary<string, bool>? pressed, SkillRow? left, SkillRow? right,
                                       bool nothingCast = false)
    {
        // Of a Paladin's own auras only the one he holds is in the row - the right box's, else the left's where the
        // right holds no aura. Deliberately no long list of dimmed icons: one shows only when selected on a skill box.
        var held = right is { IsAura: true } ? right : left is { IsAura: true } ? left : null;
        candidates = [.. candidates.Where(buff => buff.Kind != BuffKind.OwnAura || buff.Skill.Id == held?.Id)];

        bool InBox(Buff buff) => left?.Id == buff.Skill.Id || right?.Id == buff.Skill.Id;

        // A box holding a skill only one form can use names that form.
        bool Named(Buff buff) =>
            InBox(buff) || (buff.Rivals == "form" && new[] { left, right }.OfType<SkillRow>()
                .Any(box => box.Restrict == 2 && box.States.Contains(buff.Skill.AuraState, StringComparer.OrdinalIgnoreCase)));

        // Nothing cast: whatever is pressed is not pressed is off - but a Paladin's own aura, which is the boxes'.
        bool? Pressed(Buff buff) => pressed is not null && pressed.TryGetValue(buff.Key, out bool on) ? on
            : nothingCast && buff.Kind != BuffKind.OwnAura ? false : null;

        var on = new HashSet<Buff>(ReferenceEqualityComparer.Instance);
        foreach (var buff in candidates.Where(buff => !buff.Toggles))
            on.Add(buff);

        foreach (var buff in candidates.Where(buff => buff.Toggles && buff.Rivals.Length == 0))
        {
            if (Pressed(buff) ?? buff.Kind == BuffKind.Cast)
                on.Add(buff);
        }

        foreach (var lot in candidates.Where(buff => buff.Toggles && buff.Rivals.Length > 0).GroupBy(buff => buff.Rivals))
        {
            var members = lot.ToList();
            if (members.All(member => Pressed(member) == false))
                continue;

            // What the player pressed on wins; else the one a box names; else - not a form, not a Paladin's
            // aura, which are up only where something says so - the highest level. As many as the lot has room for. What
            // the hands cannot cast takes no place (Buff.Needs): Holy Sword 20 with a shield held leaves Holy Shield up.
            int room = members.Max(member => member.Room);
            var chosen = members.Where(member => Pressed(member) == true).OrderByDescending(member => member.Level).Take(room).ToList();
            var rest = members.Where(member => Pressed(member) is null).ToList();
            Buff? Held(SkillRow? box) => box is null ? null : rest.FirstOrDefault(member => member.Skill.Id == box.Id);

            while (chosen.Count < room
                   &&((lot.Key == "aura"
                           ? Held(right) ?? (right is { IsAura: true } ? null : Held(left))
                           : rest.Where(member => member.Needs is null && Named(member)).MaxBy(member => member.Level))
                       ?? (lot.Key is "form" or "aura" ? null
                           : rest.Where(member => member.Needs is null && member.Kind is not (BuffKind.Charges or BuffKind.ChanceToCast)).MaxBy(member => member.Level))) is { } next)
            {
                chosen.Add(next);
                rest.Remove(next);
            }

            on.UnionWith(chosen);
        }

        // What the hands cannot cast is off (Buff.Needs).
        on.RemoveWhere(buff => buff.Needs is not null);

        // A skill only a form can use waits for the form.
        var waits = new Dictionary<Buff, string>(ReferenceEqualityComparer.Instance);
        foreach (var buff in on.Where(buff => buff.Skill.Restrict == 2 && buff.Skill.States.Count > 0).ToList())
        {
            if (!on.Any(other => buff.Skill.States.Contains(other.Skill.AuraState, StringComparer.OrdinalIgnoreCase)))
            {
                on.Remove(buff);
                waits[buff] = buff.Skill.States[0];
            }
        }

        var counted = on.GroupBy(buff => buff.Skill.AuraState, StringComparer.OrdinalIgnoreCase)
            .SelectMany(same => same.Key.Length == 0 ? same : Mercenarys(same).OrderByDescending(buff => buff.Level).Take(1))
            .ToHashSet<Buff>(ReferenceEqualityComparer.Instance);

        return [.. candidates.Select(buff => buff with
        {
            On = on.Contains(buff),
            Counted = counted.Contains(buff),
            Waits = waits.GetValueOrDefault(buff),
        })];
    }

    /// <summary>
    /// Of a mercenary's own aura and one it wears of the same skill, the worn one, whatever their levels: Templar's
    /// Might's Might 8 put out MercAura's Barbarian's own 18 - "Damage 251-270" = 55-59 x (100 + 268 + 90) / 100, where
    /// 18 gives 306-329 (screenshots). Between other sources the higher level is the one (Resolve).
    /// </summary>
    internal static IEnumerable<Buff> Mercenarys(IEnumerable<Buff> same) =>
        same.Any(buff => buff.OnMercenary) ? same.Where(buff => buff.Kind != BuffKind.MercenaryAura) : same;

    /// <summary>
    /// What a skill must be cast with that <paramref name="held"/> - the types in hand and those above them - lacks, as
    /// a type's name: the skill's <c>itypea1</c>-<c>itypea3</c>. Holy Sword names
    /// a two-handed sword, Holy Shield a shield; null where one is held or none is named - <c>weap</c>, every
    /// Paladin aura's, asks nothing, as the basic skills' choice reads it. A type under another it names is said by
    /// that one: Holy Sword's Crystal Sword is a two-handed sword.
    /// </summary>
    internal static string? NeedsOf(SkillRow row, IReadOnlySet<string> held)
    {
        string[] named = [.. new[] { "itypea1", "itypea2", "itypea3" }.Select(row.Text).Where(type => type is { Length: > 0 } and not "weap")];
        if (named.Length == 0 || named.Any(held.Contains))
            return null;

        var said = named.Where(type => !named.Any(other => other != type && ItemTypes.Ancestry(type).Contains(other)));
        return string.Join(L(" or "), said.Select(ItemTypes.ShownNameOf));
    }

    /// <summary>
    /// What a buff that is up gives: each of its lines by its formula at its level. A skill that builds up by
    /// hits is counted at its full: Frenzy at every stack is the row's whole value ("Attack Rate: 132" = 100 -
    /// 10 + 42); <b>Feral Rage's life steal is one percent a charge</b>, its charges at their most
    /// <c>par8 + lvl / par7</c>, under the row's own ceiling - 8 at level 10 and 13 at level 20
    /// (screenshots). The other lines of a skill that builds up by charges are not measured, and are
    /// left out.
    /// </summary>
    internal static IEnumerable<(string Stat, int Value)> Gives(Buff buff, SkillCalc.Context context, bool toOthers = false)
    {
        var row = buff.Skill;

        // A Paladin's own aura gives him, while it is up, the lines the row keeps for its owner - its passive
        // lines, on an aura with no passive state: Holy Fire's fire damage on his own attacks, which the party
        // does not get (a level 96 Paladin's fire line, 284-555 without and 555-897 with it; screenshots).
        // So does an aura its wearer wears, in place of the party's line of the same stat: AuraStack's two
        // Fanaticism 5 items (one of 10) read 6-22 on a 2-7 Short Sword - x (100 + 33 + 185) / 100, its owner's ln56
        // alone, not with the party's ln56/2 beside it; one of them 4-17, 110% (screenshots).
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if ((buff.Kind == BuffKind.OwnAura || buff is { Kind: BuffKind.ItemAura, Worn: true }) && row.PassiveState.Length == 0 && !toOthers)
        {
            var own = new SkillCalc(row, buff.Level, buff.BaseLevel, context);
            foreach (var (stat, formula) in row.Passives)
            {
                if (own.Evaluate(formula) is { } value)
                {
                    owned.Add(stat);
                    yield return (stat, value);
                }
            }
        }

        if (!row.GivesTheCaster)
            yield break;

        var calc = new SkillCalc(row, buff.Level, buff.BaseLevel, context);
        foreach (var (stat, formula) in row.Auras)
        {
            if (owned.Contains(stat) || calc.Evaluate(formula) is not { } value)
                continue;

            // Cleansing's poison length (100 - edmn, the one aura that writes it) is not on the game's page: Plague's
            // level 17 Cleansing left PsnStrike's "Length: 200%" alone in town and out, its curse line counted (48;
            // screenshots).
            if (stat == "item_poisonlengthresist")
                continue;

            if (row.ServerDo == SkillRow.BuildsUpByCharges)
            {
                if (stat != "lifedrainmindam" || row.Params[6] <= 0)
                    continue;

                value = Math.Min(value, row.Params[7] + buff.Level / row.Params[6]);
            }

            yield return (stat, value);
        }
    }
}
