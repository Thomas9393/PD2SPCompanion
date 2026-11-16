using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// The character's sheet, worked out from the save: what the game's own Advanced Stats page shows of a
/// character, line by line, each number with what it is made of.
/// <para>
/// <b>What counts</b>: the gear the character has the use of (<see cref="Requirements.Judge"/> - worn, of the
/// weapon set asked for, and the charms; nothing from an item its level, strength or dexterity does not meet),
/// what is socketed in it, what its sets give; and every passive skill the character has a level in. Of two
/// weapons in hand one gives nothing that is about the attack (the stat table's <c>damagerelated</c>): a
/// Barbarian with two Twin Axes, 18-54 each, reads "Base Damage 1h: 18-54" and "Attack Rate: 132" - one axe's
/// damage and one axe's speed. <b>The one that counts is the faster</b> - the higher of what its own speed and
/// its own increased attack speed come to - as the game's page has it: a level 99 Barbarian with a Ghost Glaive (speed 20) in
/// one hand and a Flying Knife (0, with 10% increased attack speed) in the other, reads the knife's attack rate
/// 100, its damage, its attack rating and its critical damage, and not the glaive's crushing blow or "attacker
/// takes damage" (screenshots).
/// </para>
/// <para>
/// <b>One mechanism at a time</b>: a line is shown when the game's page has been matched on it; a line that
/// waits on a formula not yet measured is null, and the page says "-" rather than a number that may be wrong.
/// </para>
/// </summary>
public sealed class CharacterSheet
{
    /// <param name="WeaponSet">The weapon set the sheet is for: <see cref="WeaponSets.InHand"/> or <see cref="WeaponSets.Swapped"/>.</param>
    /// <param name="OutOfTown">
    /// Whether the character is out of town, its mercenary and its spirit beside it - where their auras reach
    /// the character. In town they do not: LightFury's damage reduction reads 24 / 30% in town and 33 / 39% out
    /// of it, her mercenary's Defiance; a level 95 Druid's Spirit of Barbs, summoned in town, gives her none of its deadly
    /// strike or "attacker takes damage" (screenshots). An aura of the character's own
    /// works in town: a level 96 Paladin's Lightsabre's Resist Lightning is in his page's lightning line there.
    /// </param>
    /// <param name="Pressed">What the player pressed on or off in the buff row, by <see cref="Buff.Key"/>; the rest is as it begins.</param>
    /// <param name="NothingCast">
    /// The character as it stands right after it is loaded: nothing cast - no form, no cry, no spirit - but for
    /// what <paramref name="Pressed"/> turns on; a Paladin's own aura is still the skill boxes' to say, and what
    /// is up for good is up. The game's own page in town, as the screenshots were taken.
    /// </param>
    /// <param name="OtherWeaponSet">
    /// Whether what the other weapon set casts is in the buff row (<see cref="Buffs.WithOtherSet"/>) - the page's
    /// way. Off, only the set in hand's: as the specimens' screenshots were taken.
    /// </param>
    public sealed record Options(int WeaponSet = WeaponSets.InHand, bool OutOfTown = true, IReadOnlyDictionary<string, bool>? Pressed = null,
                                 bool NothingCast = false, bool OtherWeaponSet = true);

    /// <summary>A resistance: what it comes to in a difficulty - not held to its ceiling, as the game's page shows it - and the ceiling.</summary>
    public sealed record Resistance(SheetFigure Value, SheetFigure Maximum);

    /// <summary>A skill's level with every bonus, and its hard points - 0 for one an item lends.</summary>
    public sealed record SkillLevel(int Id, string Name, int Level, int BaseLevel);

    /// <summary>The most a resistance's ceiling can be raised to.</summary>
    public const int HighestResistance = 90;

    /// <summary>What each difficulty takes from every resistance, in the expansion: Normal, Nightmare, Hell.</summary>
    public static readonly IReadOnlyList<int> ResistancePenalty = [0, -40, -100];

    /// <summary>What a Scroll of Resistance gives - Prison of Ice's reward, one a difficulty.</summary>
    public const int ScrollResistance = 10;

    private readonly SheetTotals _totals;

    private CharacterSheet(SheetTotals totals) => _totals = totals;

    public string Name { get; private init; } = "";

    /// <summary>The character's class, by id: 0 the Amazon ... 6 the Assassin.</summary>
    public byte ClassId { get; private init; }
    public int Level { get; private init; }

    /// <summary>The difficulty the character is in: 0 Normal, 1 Nightmare, 2 Hell.</summary>
    public int Difficulty { get; private init; }

    /// <summary>Every stat with its sources - what the lines are read from.</summary>
    public SheetTotals Totals => _totals;

    /// <summary>What a stat comes to, and what from.</summary>
    public SheetFigure Stat(params string[] stats) => _totals.Sum(stats);

    public SheetFigure Strength { get; private init; } = SheetFigure.None;
    public SheetFigure Dexterity { get; private init; } = SheetFigure.None;
    public SheetFigure Vitality { get; private init; } = SheetFigure.None;
    public SheetFigure Energy { get; private init; } = SheetFigure.None;

    /// <summary>The Scrolls of Resistance read - one for each difficulty whose Prison of Ice gave its own.</summary>
    public int Scrolls { get; private init; }

    /// <summary>
    /// A resistance in a difficulty (0 Normal, 1 Nightmare, 2 Hell): gear and skills, 10 for each Scroll of
    /// Resistance, less the difficulty's penalty; and its ceiling, 75 and what raises it, 90 at most. Fury, no
    /// gear, Normal: 30 / 75 with three scrolls; LightFury in Hell: lightning 108 / 83 (screenshots).
    /// </summary>
    public Resistance Resist(string element, int difficulty)
    {
        var (stat, most) = element switch
        {
            "fire" => ("fireresist", "maxfireresist"),
            "cold" => ("coldresist", "maxcoldresist"),
            "lightning" => ("lightresist", "maxlightresist"),
            _ => ("poisonresist", "maxpoisonresist"),
        };

        var value = _totals.Sum(stat).With(
            new SheetPart(L("Scroll of Resistance"), Scrolls * ScrollResistance),
            new SheetPart(DifficultyName(difficulty), ResistancePenalty[Math.Clamp(difficulty, 0, 2)]));

        var ceiling = SheetFigure.Of(new[] { new SheetPart(L("Base"), 75) }.Concat(_totals.Sum(most).Parts));
        if (ceiling.Value > HighestResistance)
            ceiling = ceiling.With(new SheetPart(L("Highest possible"), HighestResistance - ceiling.Value));

        return new Resistance(value, ceiling);
    }

    public static string DifficultyName(int difficulty) => difficulty switch { 0 => L("Normal"), 1 => L("Nightmare"), _ => L("Hell") };

    /// <summary>How long a chill lasts on the character, in percent: nothing with Cannot Be Frozen, half for each Half Freeze Duration, two at most.</summary>
    public int ColdLength => _totals.Of("item_cannotbefrozen") > 0 ? 0 : 100 - 50 * Math.Min(_totals.Of("item_halffreezeduration"), 2);

    /// <summary>How long poison lasts on the character in a difficulty, in percent: 100, more by the difficulty's penalty, less by Poison Length Reduced.</summary>
    public int PoisonLength(int difficulty) => 100 - ResistancePenalty[Math.Clamp(difficulty, 0, 2)] - _totals.Of("item_poisonlengthresist");

    /// <summary>What <see cref="ColdLength"/> is made of, for the line's tip: each item's halving, and Cannot Be Frozen.</summary>
    public SheetFigure ColdLengthSources => new(ColdLength,
    [
        new SheetPart(L("Base"), 100),
        .. _totals.Sum("item_halffreezeduration").Parts.Select(part => new SheetPart(part.Source, -50 * part.Value)),
        .. _totals.Sum("item_cannotbefrozen").Parts.Select(part => new SheetPart(part.Source, -100)),
    ]);

    /// <summary>What <see cref="PoisonLength"/> is made of at a difficulty: the difficulty's own, and each item's reduction.</summary>
    public SheetFigure PoisonLengthSources(int difficulty) => new(PoisonLength(difficulty),
    [
        new SheetPart(L("Base"), 100),
        .. ResistancePenalty[Math.Clamp(difficulty, 0, 2)] is not 0 and var penalty ? new[] { new SheetPart(DifficultyName(difficulty), -penalty) } : [],
        .. _totals.Sum("item_poisonlengthresist").Parts.Select(part => new SheetPart(part.Source, -part.Value)),
    ]);

    /// <summary>What <see cref="CurseLength"/> is made of: each item's curse resistance, 25% at the least.</summary>
    public SheetFigure CurseLengthSources => new(CurseLength,
    [
        new SheetPart(L("Base"), 100),
        .. _totals.Sum("curse_resistance").Parts.Select(part => new SheetPart(part.Source, -part.Value)),
    ]);

    /// <summary>Curse resistance, 75 at most.</summary>
    public SheetFigure CurseResist => _totals.Sum("curse_effectiveness");

    /// <summary>
    /// Life per kill, whole: a mercenary's mastery gives 74 at its level 32, and the page reads 74 once the character is
    /// loaded again - 63 only while the mercenary is just hired (the Fire Iron Wolf: 63 jumps to
    /// 74 after a save). Not held to 63, which pages read just after a hire show.
    /// </summary>
    public SheetFigure LifePerKill => _totals.Sum("item_healafterkill");

    /// <summary>How long a curse lasts on the character, in percent - a quarter at the least.</summary>
    public int CurseLength => Math.Max(100 - _totals.Of("curse_resistance"), 25);

    /// <summary>Attack rating before any gear: dexterity x 5, the class's <c>ToHitFactor</c>, less 35.</summary>
    public SheetFigure AttackFromDexterity { get; private init; } = SheetFigure.None;

    /// <summary>Defense from dexterity: a quarter of it.</summary>
    public SheetFigure DefenseFromDexterity { get; private init; } = SheetFigure.None;

    /// <summary>
    /// A critical strike's chance: the Critical Strike skill, the items', and a throwing mastery's with a throw
    /// on the right hand - 75 at most - and what it deals: 200% and what raises it. As the game's page has it
    /// (screenshots): a level 99 Barbarian's Throwing Mastery gives its 31 with Double Throw on the
    /// right hand (skills.txt <c>range</c> <c>rng</c>) and nothing with Attack or Frenzy; a level 99 Assassin's Claw and
    /// Dagger Mastery - a melee mastery, 25 by its row - gives nothing with Fists of Fire nor with Attack. A
    /// melee mastery's critical strike is not on the page; whether it counts on a hit, the page cannot say.
    /// </summary>
    public SheetFigure CriticalStrike => RightSkill?.Range.Equals("rng", StringComparison.OrdinalIgnoreCase) == true
        ? _totals.Sum("passive_critical_strike", "item_crit_chance", "passive_mastery_throw_crit")
        : _totals.Sum("passive_critical_strike", "item_crit_chance");
    public SheetFigure CriticalMultiplier => SheetFigure.Of(new[] { new SheetPart(L("Base"), 200) }.Concat(_totals.Sum("item_crit_multiplier").Parts));

    /// <summary>The most Deadly Strike counts for: 75 and what raises the ceiling, 100 at most.</summary>
    public int DeadlyStrikeCap => Math.Min(75 + _totals.Of("item_maxdeadlystrike"), 100);
    public SheetFigure DeadlyMultiplier => SheetFigure.Of(new[] { new SheetPart(L("Base"), 150) }.Concat(_totals.Sum("item_ds_multiplier").Parts));

    public SheetFigure CrushingBlowEfficiency => SheetFigure.Of(new[] { new SheetPart(L("Base"), 100) }.Concat(_totals.Sum("item_crushingblow_efficiency").Parts));

    /// <summary>
    /// What open wounds deal a second at a character level, before what gear adds: so much a frame in 256ths -
    /// 25 at level 1 and 9 more a level to 15, 18 to 30, 27 to 45, 36 to 60, 45 after - times the 25 frames of
    /// a second, rounded down. The bands are vanilla's; in Season 13 the start was 15 under vanilla's 40. Read off
    /// the game's page at ten levels then (screenshots): 5 at level 4, 5 at 5, 12 at 12, 23
    /// at 20, 54 at 35, 98 at 50, 155 at 65, 243 at 85, 247 at 86, 295 at 97 - levels 4 and 12 were asked for
    /// because a start of 24 or 23 fits the other eight as well, and reads 4 at level 4 (or 11 at 12).
    /// <para>
    /// Season 14 is vanilla's again - no table carries it, the game's own code does: 266 at level 90, 271 at 91,
    /// 275 at 92 (PsnStrike, CorpseExp and LightFury), where the start of 15 under read 2
    /// short at 91 and 1 at 90 and 92.
    /// </para>
    /// </summary>
    public static int OpenWoundsAt(int level) => OpenWoundsAt(level, SeasonNumber());

    /// <summary><see cref="OpenWoundsAt(int)"/> as a season had it: 13 and before 15 under vanilla's start, 14 on vanilla's.</summary>
    public static int OpenWoundsAt(int level, int season)
    {
        int frame = level < 15 ? 9 * level + 31
            : level < 30 ? 18 * level - 104
            : level < 45 ? 27 * level - 374
            : level < 60 ? 36 * level - 779
            : 45 * level - 1319;
        return (frame - (season is > 0 and < 14 ? 15 : 0)) * 25 / 256;
    }

    /// <summary>The installed tables' season, as a number - the one after their previous season's columns; 0 where they name none.</summary>
    private static int SeasonNumber() =>
        D2SLib.Model.TXT.StatEncoding.PreviousSeason is { Length: > 1 } previous
        && int.TryParse(previous[1..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int was)
            ? was + 1
            : 0;

    /// <summary>The page's "+N" beside open wounds: the level's, and "+ to open wounds damage per second" from gear and skills.</summary>
    public SheetFigure OpenWoundsDamage =>
        SheetFigure.Of(new[] { new SheetPart(L($"Level {Level}"), OpenWoundsAt(Level)) }.Concat(_totals.Sum("deep_wounds").Parts));

    /// <summary>The items' pierce and the Pierce skill's.</summary>
    public SheetFigure ProjectilePierce => _totals.Sum("item_pierce", "skill_pierce");

    /// <summary>
    /// An element's added damage as the game's page shows it: the gear's, raised by the element's mastery -
    /// each end with its own share rounded down.
    /// </summary>
    public (int Min, int Max) Added(string minimum, string maximum, string mastery)
    {
        int percent = _totals.Of(mastery);
        int low = _totals.Of(minimum), high = _totals.Of(maximum);
        return (low + low * percent / 100, high + high * percent / 100);
    }

    /// <summary>
    /// The poison line as the game's page shows it: the damage over its whole length - each end's rate, which is
    /// in 256ths a frame, times the frames, rounded down - and the length in seconds (25 frames each); a skill's
    /// own length where it sets one. Matched with one source of poison (a Paladin's "129-129 over 2.0s" from
    /// 665 a frame for 50 frames); <b>with several sources the stats add up, rate and length both,
    /// and the page was not yet seen</b>.
    /// </summary>
    public (int Min, int Max, double Seconds) Poison
    {
        get
        {
            int frames = _totals.Of("skill_poison_override_length") is > 0 and var set ? set : _totals.Of("poisonlength");
            int percent = _totals.Of("passive_pois_mastery");
            int low = _totals.Of("poisonmindam"), high = _totals.Of("poisonmaxdam");
            low += low * percent / 100;
            high += high * percent / 100;
            return ((int)(low / 256.0 * frames), (int)(high / 256.0 * frames), frames / 25.0);
        }
    }

    /// <summary>The weapon in hand that the attack is made with - null for bare hands.</summary>
    public Item? Weapon { get; private init; }

    /// <summary>How the weapon in hand is held (weapons.txt <c>wclass</c>) - <c>hth</c> for bare hands.</summary>
    public string WeaponClass { get; private init; } = "hth";

    /// <summary>
    /// The attack rate before any item speed: 100, less the weapon's speed (weapons.txt <c>speed</c>: a javelin's
    /// -10 is 110), plus what skills add.
    /// </summary>
    public SheetFigure AttackRate { get; private init; } = SheetFigure.None;

    /// <summary>
    /// The chance to block, 75 at most: (the shield's own + the class's + what gear and skills add) x (dexterity
    /// - 15) / (2 x level), rounded down; nothing without a shield. Matched in game on a Paladin of level 97
    /// with dexterity 152 and a Stormshield: 22 + 30 + 25 = 77 reads "Chance to Block: 54%", and with Holy Shield
    /// 29 cast (36 more) "75%" (screenshots).
    /// </summary>
    public SheetFigure? Blocking { get; private init; }
    public int? Block { get; private init; }

    /// <summary>The dexterity, gear and all, at which blocking is at its most - null without a shield.</summary>
    public int? DexterityForMostBlock { get; private init; }

    public Breakpoints.Row? CastRateRow { get; private init; }
    public Breakpoints.Row? HitRecoveryRow { get; private init; }

    /// <summary>The block's row (<see cref="Breakpoints.BlockRate"/>) - null where the animation is in no table.</summary>
    public Breakpoints.Row? BlockRateRow { get; private init; }

    /// <summary>The skill the breakpoints are for: the right hand's, as the game's page heads them.</summary>
    public SkillRow? RightSkill { get; private init; }
    public SkillRow? LeftSkill { get; private init; }

    /// <summary>
    /// What the right skill box may hold, as the game's own list offers it: the skills every class has, then each
    /// the character has a level in - its own and the ones items lend - and each an item has charges of; no
    /// passive. The skill the box holds now is always in it.
    /// </summary>
    public IReadOnlyList<SkillLevel> RightChoices { get; private init; } = [];

    /// <summary>The same for the left box: only what the skill table lets sit on the left (<c>leftskill</c>).</summary>
    public IReadOnlyList<SkillLevel> LeftChoices { get; private init; } = [];

    /// <summary>The attack's row for the right hand's skill - null where its timing is not known.</summary>
    public Breakpoints.Row? AttackRow { get; private init; }

    /// <summary>Why <see cref="AttackRow"/> is null, for the printout.</summary>
    public string AttackRowNote { get; private init; } = "";

    /// <summary>The mercenary's sheet (<see cref="ForMercenary"/>): no skill boxes; its buff row the character's buffs that reach it and what it casts itself.</summary>
    public bool IsMercenary { get; private init; }

    /// <summary>What the breakpoints are headed by where it is not the right skill: the mercenary's attack, by its act.</summary>
    public string? AttackName { get; private init; }

    /// <summary>The character's skills as its buffs are worked out with - which the party's share of them is counted by.</summary>
    internal SkillCalc.Context? CasterContext { get; private init; }

    /// <summary>What a buff in this sheet's row gives its holder, each line by its stat; none on a sheet with no skills to ask.</summary>
    public IReadOnlyList<(string Stat, int Value)> GivenBy(Buff buff) =>
        CasterContext is { } context ? [.. Characters.Buffs.Gives(buff, context)] : [];

    /// <summary>The buff row: everything that could be up, and which of it is.</summary>
    public IReadOnlyList<Buff> Buffs { get; private init; } = [];

    /// <summary>The form the character is in - the wolf, the bear - or null for its own.</summary>
    public SkillStates.State? Form { get; private init; }

    /// <summary>Every skill the character has a level in, its own and the ones items lend.</summary>
    public IReadOnlyList<SkillLevel> Skills { get; private init; } = [];

    /// <summary>The items that count, in the order they are counted.</summary>
    public IReadOnlyList<Item> Gear { get; private init; } = [];

    /// <summary>Worn pieces left out: what the character does not meet (<see cref="Requirements.Judged.Short"/>).</summary>
    public IReadOnlyList<(Item Item, string Why)> LeftOut { get; private init; } = [];

    /// <summary>
    /// What both sheets are worked out from, as one number: the class, level, attributes, skills and the four skill
    /// boxes, the quests (the scrolls read), the difficulty, the mercenary, the Iron Golem and what it is made of, and
    /// every item worn, in the inventory or on the mercenary as it would be written. The stash, the cube and the belt are none of it. A move there left the
    /// sheets as they were and they were worked out again, 6-7 ms of each move with them unfolded
    /// (measured).
    /// </summary>
    public static int InputsOf(D2S save)
    {
        var inputs = new HashCode();
        inputs.Add(save.ClassId);
        inputs.Add(save.Level);
        inputs.Add(save.Name);
        inputs.Add(save.Status?.IsExpansion);
        inputs.Add(save.ActiveWeapon);
        if (save.Attributes is { } attributes)
            inputs.AddBytes(Attributes.Write(attributes));
        if (save.ClassSkills is { } skills)
            inputs.AddBytes(ClassSkills.Write(skills));
        foreach (var box in new[] { save.LeftSkill, save.RightSkill, save.LeftSwapSkill, save.RightSwapSkill })
            inputs.Add(box?.Id);
        if (save.Quests is { } quests)
            inputs.AddBytes(QuestsSection.Write(quests));
        if (save.Location is { } location)
            inputs.AddBytes(Locations.Write(location));
        if (save.Mercenary is { } mercenary)
            inputs.AddBytes(Mercenary.Write(mercenary));

        uint version = save.Header.Version;
        foreach (var item in save.PlayerItemList?.Items ?? [])
        {
            if (item.Mode != ItemMode.Equipped && !(item.Mode == ItemMode.Stored && item.Page == Saves.InventoryLayout.InventoryPage))
                continue;
            inputs.Add((int)item.Mode);
            inputs.Add((int)item.Location);
            inputs.AddBytes(Item.Write(item, version));
        }
        foreach (var item in save.MercenaryItemList?.ItemList?.Items ?? [])
            inputs.AddBytes(Item.Write(item, version));

        // The golem's item is in neither list, and its aura counts on both sheets (Buffs.Candidates).
        inputs.Add(save.Golem?.Exists);
        if (save.Golem is { Exists: true, Item: { } golem })
            inputs.AddBytes(Item.Write(golem, version));

        return inputs.ToHashCode();
    }

    public static CharacterSheet For(D2S save, GameData game, Options? options = null)
    {
        options ??= new Options();
        int level = save.Level;
        int classId = save.ClassId;
        var classStats = ClassStats.For(game, save.ClassId);
        var tree = SkillTree.For(game, save.ClassId);

        int Base(string stat) => AttributeScale.Display(stat, save.Attributes.Stats.GetValueOrDefault(stat));

        var judged = Requirements.Judge(save, options.WeaponSet, Base("strength"), Base("dexterity"));
        var gear = judged.Where(each => each.Met).Select(each => each.Item).ToList();

        // The weapons in hand: of two the faster is the one that counts, and the other gives nothing about the
        // attack. Alike in speed, the other slot's: a level 99 Barbarian's two Winds, speed -20 and 40% increased attack
        // speed each, read the left hand's 254% enhanced damage, "116-157", not the main slot's 263%
        // (a screenshot) - as the game's page has it, which takes the main hand's only when it is the
        // faster.
        int Swiftness(Item item) =>
            -(ItemBases.Of(item.Code)?.Speed ?? 0)
            + Requirements.GearStatsBySource([item]).Where(each => each.Stat == "item_fasterattackrate").Sum(each => each.Value);

        var inHand = gear.Where(item => item.Mode == ItemMode.Equipped && IsHand(item.Location)
                                        && ItemBases.Of(item.Code) is { WeaponClass.Length: > 0 })
            .OrderByDescending(Swiftness)
            .ThenBy(item => item.Location is ItemLocation.RightHand or ItemLocation.SwapRight ? 1 : 0)
            .ToList();
        var weapon = inHand.FirstOrDefault();
        var second = inHand.Skip(1).FirstOrDefault();

        var totals = new SheetTotals(level) { EtherealsWorn = Requirements.EtherealsWorn(gear) };
        var names = new Dictionary<Item, string>(ReferenceEqualityComparer.Instance);
        string NameOf(Item item) => names.TryGetValue(item, out string? name) ? name : names[item] = ItemPresenter.Describe(item).Name;

        var counted = CountGear(totals, gear, weapon, second, level, NameOf);

        bool swapped = options.WeaponSet == WeaponSets.Swapped;
        var right = SkillRows.Of((int)((swapped ? save.RightSwapSkill : save.RightSkill)?.Id ?? 0));
        var left = SkillRows.Of((int)((swapped ? save.LeftSwapSkill : save.LeftSkill)?.Id ?? 0));

        // Skills: their levels from the gear, the buffs they and the gear make, then what the passives give.
        var plain = counted.Select(each => (each.Stat, each.Value, each.Param)).ToList();
        var levels = LevelsOf(save, tree, plain, classId, 0);
        var weaponTypes = weapon is null ? [] : ItemTypes.Ancestry(ItemTypes.TypeOf(weapon.Code));

        // A box whose skill the character no longer has - the item that lent it, or held its charges, taken off -
        // holds Attack; the save keeps its own, and the skill comes back with the item.
        right = HeldOrAttack(right, levels, counted);
        left = HeldOrAttack(left, levels, counted);

        SkillCalc.Context ContextOf(Dictionary<int, (int Level, int BaseLevel)> known) => new(
            name => SkillRows.Named(name) is { } row && known.TryGetValue(row.Id, out var found) ? (found.Level, found.BaseLevel) : (0, 0),
            level,
            totals.Of);

        // The other weapon set: what is cast with it in hand stays through the swap back - Call to Arms on the
        // switch, its cries cast, then the weapons fought with: every set's possible buff counts, so long as it does not need
        // that item kept on, as an aura does. A cast, a spirit, a charge that set gives higher, or that only it has;
        // never what the item must be worn for - its aura, what it keeps cast, what it casts when struck - nor a
        // Paladin's own aura, which is the skill he holds. Its levels are that set's, the rest of the gear as worn.
        int otherSet = swapped ? WeaponSets.InHand : WeaponSets.Swapped;
        int otherTab = (WeaponSets.TabInHand(save) == 1) == (otherSet == WeaponSets.InHand) ? 1 : 2;
        string otherName = otherTab == 1 ? L("weapon set I") : L("weapon set II");
        var otherGear = Requirements.Judge(save, otherSet, Base("strength"), Base("dexterity")).Where(each => each.Met).Select(each => each.Item).ToList();
        var otherCounted = Requirements.GearStatsBySource(otherGear).ToList();
        var otherPlain = otherCounted.Select(each => (each.Stat, each.Value, each.Param)).ToList();
        var otherWeapons = otherGear.Where(item => !gear.Contains(item, ReferenceEqualityComparer.Instance)).ToList();
        var otherLevels = LevelsOf(save, tree, otherPlain, classId, 0);

        // Where a buff of the other set is from: the item of that set that lends it or holds its charges, else the set.
        string FromOther(Buff buff)
        {
            if (buff.From.Length > 0)
                return $"{buff.From} ({otherName})";

            var lender = otherWeapons.FirstOrDefault(item => Requirements.GearStatsBySource([item])
                .Any(each => each.Stat == "item_nonclassskill" && each.Param == buff.Face.Id));
            return lender is null ? otherName : $"{NameOf(lender)} ({otherName})";
        }

        // What each set holds, shield and all: what a buff must be cast with is asked of the set it is cast with (Buff.Needs).
        HashSet<string> Held(IEnumerable<Item> items) =>
            [.. items.Where(item => item.Mode == ItemMode.Equipped && IsHand(item.Location))
                     .SelectMany(item => ItemTypes.Ancestry(ItemTypes.TypeOf(item.Code)))];
        var held = Held(gear);
        var otherHeld = Held(otherGear);

        List<Buff> BuffsOf(Dictionary<int, (int Level, int BaseLevel)> known, Dictionary<int, (int Level, int BaseLevel)> other) => Characters.Buffs.Resolve(
            Characters.Buffs.WithOtherSet(
                Characters.Buffs.Candidates(counted, known, ContextOf(known), row => SkillNameOf(tree, row), NameOf, save, game, options.OutOfTown, held: held),
                options.OtherWeaponSet
                    ? Characters.Buffs.Candidates(otherCounted, other, ContextOf(other), row => SkillNameOf(tree, row), NameOf, save, game, options.OutOfTown, withMercenary: false, held: otherHeld)
                    : [],
                FromOther),
            options.Pressed, left, right, options.NothingCast);

        // A cry that raises every skill is cast first, at the level it has before itself, and the rest after it
        // a level higher - with either weapon set.
        var buffs = BuffsOf(levels, otherLevels);
        int raised = buffs.Where(buff => buff.Counted)
            .SelectMany(buff => Characters.Buffs.Gives(buff, ContextOf(levels)))
            .Where(gives => gives.Stat == "item_allskills").Sum(gives => gives.Value);
        if (raised > 0)
        {
            static bool RaisesAll(int id) => SkillRows.Of(id) is { } row && row.Auras.Any(line => line.Stat == "item_allskills");

            var before = levels;
            levels = LevelsOf(save, tree, plain, classId, raised);
            foreach (int cry in before.Keys.Where(RaisesAll))
                levels[cry] = before[cry];

            var otherBefore = otherLevels;
            otherLevels = LevelsOf(save, tree, otherPlain, classId, raised);
            foreach (int cry in otherBefore.Keys.Where(RaisesAll))
                otherLevels[cry] = otherBefore[cry];

            buffs = BuffsOf(levels, otherLevels);
        }

        // What raises the party's skills raises the mercenary's aura too: Whirlwind's Battle Command took his guard's
        // Defiance from 18 to 20 - "Damage Reduction: 10/50%" on the guard's page (a screenshot) -
        // and what it gives the party with it.
        int party = buffs.Where(buff => buff.Counted && buff.Kind != BuffKind.MercenaryAura && Characters.Buffs.ReachesOthers(buff))
            .SelectMany(buff => Characters.Buffs.Gives(buff, ContextOf(levels), toOthers: true))
            .Where(gives => gives.Stat == "item_allskills").Sum(gives => gives.Value);
        if (party > 0)
            buffs = [.. buffs.Select(buff => buff.Kind == BuffKind.MercenaryAura ? buff with { Level = buff.Level + party } : buff)];

        var context = ContextOf(levels);
        AddPassives(totals, levels, context, weaponTypes, row => SkillNameOf(tree, row));

        foreach (var buff in buffs.Where(buff => buff.Counted))
        {
            string source = buff.From.Length > 0 ? buff.Name + " - " + buff.From : buff.Name;
            foreach (var (stat, value) in Characters.Buffs.Gives(buff, context))
                totals.Add(stat, value, buff, source);
        }

        // A form's breakpoints are counted on the monster it turns the character into.
        var form = buffs.Where(buff => buff.Counted).Select(buff => SkillStates.Of(buff.Skill.AuraState))
            .FirstOrDefault(state => state?.FormToken is not null);

        SheetFigure Attribute(string stat, string name) =>
            SheetFigure.Of(new[] { new SheetPart(name, Base(stat)) }.Concat(totals.Sum(stat).Parts));

        var dexterity = Attribute("dexterity", L("Dexterity"));
        var weaponBasis = weapon is null ? null : ItemBases.Of(weapon.Code);

        // Blocking: only with a shield, whose own chance the class and the gear add to. A shield by its type, not its
        // chance: a Buckler's own is 0, and the class's and the gear's still count (AuraTest: "none" with one on).
        var shield = gear.FirstOrDefault(item => item.Mode == ItemMode.Equipped && IsHand(item.Location)
                                                 && ItemBases.Of(item.Code) is { IsArmor: true, Type.Length: > 0 } basis
                                                 && ItemTypes.Ancestry(basis.Type).Contains("shld", StringComparer.OrdinalIgnoreCase));
        SheetFigure? blocking = null;
        int? block = null, mostBlockAt = null;
        if (shield is not null && ItemBases.Of(shield.Code) is { } shieldBasis)
        {
            blocking = SheetFigure.Of(new[] { new SheetPart(L($"{NameOf(shield)} (base)"), shieldBasis.Block), new SheetPart(classStats.Name, classStats.BlockFactor) }
                .Concat(totals.Sum("toblock").Parts));
            block = Math.Clamp(blocking.Value * (dexterity.Value - 15) / (2 * level), 0, MostBlock);
            mostBlockAt = blocking.Value <= 0 ? null : (2 * level * MostBlock + blocking.Value - 1) / blocking.Value + 15;
        }

        // A Barbarian may hold a sword for two hands in one, beside a shield or a second weapon.
        bool otherHandHeld = gear.Any(item => item.Mode == ItemMode.Equipped && IsHand(item.Location) && !ReferenceEquals(item, weapon)
                                              && ItemBases.Of(item.Code) is { WeaponClass.Length: > 0 } or { IsArmor: true });
        string weaponClass = HeldClass(weaponBasis, oneHanded: classId == Barbarian && otherHandHeld);
        var attackRate = SheetFigure.Of(new[] { new SheetPart(L("Base"), 100), new SheetPart(weapon is null ? "" : NameOf(weapon), -(weaponBasis?.Speed ?? 0)) }
            .Concat(totals.Sum("attackrate").Parts));

        Breakpoints.Row? Row(Breakpoints.Rule rule, string stat) =>
            (form?.FormToken is { } token ? AnimData.Of(token, rule.Mode, "HTH") : AnimData.Of(classId, rule.Mode, weaponClass)) is { } animation
                ? new Breakpoints.Row(Breakpoints.Of(animation, rule), totals.Of(stat))
                : null;

        // A block is drawn with what is in hand where the class has that animation, else bare-handed: a shield is beside
        // a one-handed weapon or nothing, and a two-handed one has no block of its own.
        var blockRule = buffs.Any(buff => buff.Counted && buff.Skill.Id == HolyShield) ? Breakpoints.HolyShieldBlockRate : Breakpoints.BlockRate;
        var blockAnimation = form?.FormToken is { } blockToken ? AnimData.Of(blockToken, "BL", "HTH")
            : AnimData.Of(classId, "BL", weaponClass) ?? AnimData.Of(classId, "BL", "HTH");
        var blockRow = blockAnimation is null ? null : new Breakpoints.Row(Breakpoints.Of(blockAnimation, blockRule), totals.Of("item_fasterblockrate"));

        // A skill that strikes more than once in one go: how many times, by its own formula at its level.
        string? secondClass = second is null ? null : ItemBases.Of(second.Code)?.WeaponClass;
        int? strikes = right is not null && RollbackOf(right) is not null && levels.TryGetValue(right.Id, out var rightLevel) && rightLevel.Level > 0
            ? new SkillCalc(right, rightLevel.Level, rightLevel.BaseLevel, context).Evaluate(right.Text("calc1"))
            : null;
        // Smite on the right hand brings its own crushing blow to the page, 5 + its hard points: "Crushing Blow: 6 / 100%"
        // at one point, with nothing else giving any (SeqPala, a screenshot).
        if (right?.Id == Smite && levels.TryGetValue(Smite, out var smite) && smite.Level > 0)
            totals.Add("item_crushingblow", new SkillCalc(right, smite.Level, smite.BaseLevel, context).Evaluate(right.Text("calc3")) ?? 0, right, right.Name);

        var (attackRow, attackNote) = form?.FormToken is { } formToken
            ? FormAttackRowOf(formToken, right, classId, weaponBasis, weapon is null ? 0 : OwnSpeed(weapon), attackRate.Value, totals.Of("item_fasterattackrate"))
            : AttackRowOf(right, classId, weapon, weaponClass, secondClass, attackRate.Value, totals.Of("item_fasterattackrate"), strikes);

        return new CharacterSheet(totals)
        {
            Name = save.Name,
            ClassId = save.ClassId,
            Level = level,
            Difficulty = save.Location?.Hell?.Active == true ? 2 : save.Location?.Nightmare?.Active == true ? 1 : 0,
            Strength = Attribute("strength", L("Strength")),
            Dexterity = dexterity,
            Vitality = Attribute("vitality", L("Vitality")),
            Energy = Attribute("energy", L("Energy")),
            Scrolls = ScrollsRead(save),
            AttackFromDexterity = SheetFigure.Of([new SheetPart(L("Dexterity"), dexterity.Value * 5), new SheetPart(classStats.Name, classStats.ToHitFactor - 35)]),
            DefenseFromDexterity = SheetFigure.Of([new SheetPart(L("Dexterity"), dexterity.Value / 4)]),
            Weapon = weapon,
            WeaponClass = weaponClass,
            AttackRate = attackRate,
            Blocking = blocking,
            Block = block,
            DexterityForMostBlock = mostBlockAt,
            CastRateRow = form is null && right is not null && IsSlowCast(classId, right, out int slowCast)
                ? new Breakpoints.Row(Breakpoints.Of(new AnimData.Animation(slowCast, 256, 0), Breakpoints.CastRate), totals.Of("item_fastercastrate"))
                : Row(Breakpoints.CastRate, "item_fastercastrate"),
            HitRecoveryRow = Row(Breakpoints.HitRecovery, "item_fastergethitrate"),
            BlockRateRow = blockRow,
            RightSkill = right,
            LeftSkill = left,
            RightChoices = ChoicesOf(tree, levels, counted, inHand, right, onTheLeft: false, form),
            LeftChoices = ChoicesOf(tree, levels, counted, inHand, left, onTheLeft: true, form),
            AttackRow = attackRow,
            AttackRowNote = attackNote,
            Buffs = buffs,
            Form = form,
            Skills = [.. levels.Where(pair => pair.Value.Level > 0)
                               .Select(pair => new SkillLevel(pair.Key, SkillRows.Of(pair.Key) is { } row ? SkillNameOf(tree, row) : pair.Key.ToString(), pair.Value.Level, pair.Value.BaseLevel))],
            Gear = gear,
            LeftOut = [.. judged.Where(each => !each.Met).Select(each => (each.Item, each.Short ?? ""))],
            CasterContext = context,
        };
    }

    /// <summary>
    /// What the skills held give for good - each with a state of its own (<c>passivestate</c>), its passive lines at
    /// its level: the character's, and the mercenary's: the Act 1 mercenary was missing its innate Pierce.
    /// A summon's and an aura's own lines are the summon's, and the aura's while it is up.
    /// </summary>
    private static void AddPassives(
        SheetTotals totals, IReadOnlyDictionary<int, (int Level, int BaseLevel)> levels, SkillCalc.Context context,
        IEnumerable<string> weaponTypes, Func<SkillRow, string> nameOf)
    {
        foreach (var (id, (skillLevel, baseLevel)) in levels)
        {
            if (skillLevel <= 0 || SkillRows.Of(id) is not { PassiveState.Length: > 0, Passives.Count: > 0 } row)
                continue;

            // A passive that names a weapon type keeps its mastery lines for that weapon - and gives the rest
            // whatever is in hand: Weapon Block (claws) gives its "Block Rate: 5" to an Assassin with a sword and
            // a shield (a level 98 Assassin's page, a screenshot).
            bool otherWeapon = row.PassiveItemType.Length > 0 && !weaponTypes.Contains(row.PassiveItemType);

            var calc = new SkillCalc(row, skillLevel, baseLevel, context);
            string source = nameOf(row);
            foreach (var (stat, formula) in row.Passives)
            {
                if (otherWeapon && stat.StartsWith("passive_mastery_", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (calc.Evaluate(formula) is { } value)
                    totals.Add(stat, value, row, source);
            }
        }
    }

    /// <summary>
    /// What the gear gives, into the totals - every stat with the item or set it is from - and what was counted:
    /// all of it but what the second weapon in hand has about the attack. Each piece's own defense, raised by its
    /// own percent, and the weapon's own damage, raised by its own; an item's own percent is of its own base, not
    /// the wearer's.
    /// </summary>
    private static List<Requirements.Sourced> CountGear(
        SheetTotals totals, List<Item> gear, Item? weapon, Item? second, int level, Func<Item, string> nameOf)
    {
        var counted = new List<Requirements.Sourced>();
        foreach (var each in Requirements.GearStatsBySource(gear))
        {
            if (each.Item is { } holder && ReferenceEquals(holder, second) && IsDamageRelated(each.Stat))
                continue;

            counted.Add(each);

            if (each.Item is { } own && OfItsOwnBase(each.Stat, own))
                continue;

            if (each.Item is { } item)
                totals.Add(each.Stat, each.Value, item, nameOf(item));
            else
                totals.Add(each.Stat, each.Value, each.Set ?? "", each.Set ?? "");
        }

        foreach (var item in gear)
        {
            int Own(string stat) => counted.Where(each => ReferenceEquals(each.Item, item) && each.Stat == stat).Sum(each => each.Value);

            if (item.Armor > 0)
                totals.Add("armorclass", item.Armor * (100 + Own("item_armor_percent") + (Own("item_armorpercent_perlevel") * level >> 3)) / 100, item, nameOf(item));

            if (!ReferenceEquals(item, weapon) || ItemBases.Of(item.Code) is not { } basis)
                continue;

            // The per-level max damage is its own share of the base, rounded down apart from the enhanced damage: the
            // Act 2 guard's ethereal superior (20%) Pride War Pike, 278 x 120% + 278 x 360% = 333 + 1000 = 1333 at level
            // 90 and 333 + 1089 = 1422 at 98, where one sum of the two gives 1334 and 1423 (Whirlwind's guard,
            // MercTest's, screenshots).
            int least = Own("item_mindamage_percent");
            int most = Own("item_maxdamage_percent");
            int perLevel = Own("item_maxdamage_percent_perlevel") * level >> 3;
            int Based(int from) => item.IsEthereal ? from * 125 / 100 : from;
            int Raised(int from, int percent) => Based(from) * (100 + percent) / 100;

            void Pay(int? low, int? high, string lowStat, string highStat)
            {
                if (low is not { } from || high is not { } to || to <= 0)
                    return;

                totals.Add(lowStat, Raised(from, least), item, nameOf(item));
                totals.Add(highStat, Raised(to, most) + Based(to) * perLevel / 100, item, nameOf(item));
            }

            Pay(basis.MinimumDamage, basis.MaximumDamage, "mindamage", "maxdamage");
            Pay(basis.TwoHandMinimum, basis.TwoHandMaximum, "secondary_mindamage", "secondary_maxdamage");
            Pay(basis.ThrowMinimum, basis.ThrowMaximum, "item_throw_mindamage", "item_throw_maxdamage");
        }

        return counted;
    }

    /// <summary>
    /// The mercenary's sheet, as the game's own page shows it with the mercenary's screen open, a separate
    /// Advanced Stats (Mercenary) section: the same
    /// page, read off the mercenary - its own numbers from hireling.txt at its level (<see cref="Hirelings.BaseOf"/>),
    /// the gear it has the use of, and what reaches it from the party, out of town: every aura, a spirit's, and the
    /// cries and Enchant the character casts on others (<see cref="Buffs.ReachesOthers"/>), as the character's own
    /// buff row has them on. Null with no mercenary.
    /// <para>
    /// The page keeps two quirks of its own for the mercenary, kept here: a resistance is less the player's
    /// difficulty's penalty, and the attack rating from dexterity is counted with the <b>player's</b> class factor.
    /// Its hit recovery and cast rows are counted on the mercenary's monster's animations - the panel's own tables
    /// for the five acts, every one (A1 6 / 13 / 20 / 32 / 52 / 86 / 174 / 600; A3 casts on 18 frames). <b>A first
    /// cut, not yet matched in game</b>: the base numbers' rounding, its base damage on the page, its block, its attack row.
    /// </para>
    /// </summary>
    /// <param name="character">The character's own sheet, worked out already with the same options - not twice.</param>
    public static CharacterSheet? ForMercenary(D2S save, GameData game, Options? options = null, CharacterSheet? character = null)
    {
        options ??= new Options();
        if (save.Mercenary is not { Id: not 0 } hired)
            return null;

        int level = MercenaryProfile.Read(save, game).Level;
        bool expansion = save.Status.IsExpansion;
        if (level <= 0 || Hirelings.BaseOf(hired.TypeId, level, expansion) is not { } own)
            return null;

        var player = character ?? For(save, game, options);
        var classStats = ClassStats.For(game, save.ClassId);
        var judged = Requirements.JudgeMercenary(save, level);
        var gear = judged.Where(each => each.Met).Select(each => each.Item).ToList();
        var weapon = gear.FirstOrDefault(item => IsHand(item.Location) && ItemBases.Of(item.Code) is { WeaponClass.Length: > 0 });

        var totals = new SheetTotals(level) { EtherealsWorn = Requirements.EtherealsWorn(gear) };
        var names = new Dictionary<Item, string>(ReferenceEqualityComparer.Instance);
        string NameOf(Item item) => names.TryGetValue(item, out string? name) ? name : names[item] = ItemPresenter.Describe(item).Name;

        string itself = L("Mercenary");
        // Its own attack rating is not on the page: "Base AR: dex: 830 equip: 0" for LightFury's Desert Guard,
        // whose table gives it 1592 (a screenshot). Its own defense and damage are.
        foreach (var (stat, value) in new[]
                 {
                     ("strength", own.Strength), ("dexterity", own.Dexterity), ("armorclass", own.Defense),
                     ("mindamage", own.MinDamage), ("maxdamage", own.MaxDamage),
                     ("fireresist", own.Resist), ("coldresist", own.Resist), ("lightresist", own.Resist), ("poisonresist", own.Resist),
                 })
            totals.Add(stat, value, itself, itself);

        CountGear(totals, gear, weapon, null, level, NameOf);

        // What the party gives it first - Battle Command's skills among it - then its own skills at the levels that
        // makes them.
        // A buff it can put on itself waits until its own level is known: of the two, the higher is counted (below).
        var chances = Requirements.GearStatsBySource(gear).Where(each => Characters.Buffs.ChanceStats.Contains(each.Stat)).ToList();
        var itsOwnStates = Hirelings.SkillsOf(hired.TypeId, level, expansion).Select(skill => SkillRows.Named(skill.Skill))
            .Concat(chances.Select(each => SkillRows.Of(each.Param)))
            .OfType<SkillRow>().Where(row => row.AuraState.Length > 0 && !row.IsAura).Select(row => row.AuraState)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        void CountFromThePlayer(Buff buff)
        {
            string source = buff.From.Length > 0 ? buff.Name + " - " + buff.From : buff.Name;
            foreach (var (stat, value) in Characters.Buffs.Gives(buff, player.CasterContext!, toOthers: true))
                totals.Add(stat, value, buff, source);
        }

        var fromThePlayer = player.Buffs.Where(buff => buff.Counted && Characters.Buffs.ReachesOthers(buff) && buff.Kind != BuffKind.MercenaryAura && !buff.OnMercenary).ToList();

        // A state it has by an aura of its own or one it wears: settled with the party's below, once its level is known.
        var itsAuraStates = Requirements.GearStatsBySource(gear).Where(each => each.Stat == "item_aura").Select(each => SkillRows.Of(each.Param))
            .Concat(player.Buffs.Where(buff => buff.Counted && buff.Kind == BuffKind.MercenaryAura).Select(buff => (SkillRow?)buff.Skill))
            .Concat(options.OutOfTown ? [] : Hirelings.SkillsOf(hired.TypeId, level, expansion).Select(skill => SkillRows.Named(skill.Skill)))
            .OfType<SkillRow>().Where(row => row.IsAura && row.AuraState.Length > 0).Select(row => row.AuraState)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var buff in fromThePlayer.Where(buff => !itsOwnStates.Contains(buff.Skill.AuraState) && !itsAuraStates.Contains(buff.Skill.AuraState)))
            CountFromThePlayer(buff);

        // Its own skills, as the character's are (AddPassives): the table's at its level, and what its monster has of
        // its own - the Rogue's 66% Pierce (monstats) - each raised by "+ to All Skills" on its gear and from the
        // party: Whirlwind's Battle Command took his guard's Defiance from 18 to 20, "Damage Reduction: 10/50%" on its
        // page where its gear gives 40% (a screenshot). What gives for good: the Rogue's Dodge and
        // Evade, the Iron Wolf's and the Ascendant's masteries. Not Vigor's
        // passive run/walk (blvl, 18): the Fire Rogue's page reads "Run/Walk: 40", War Traveler's alone, in town and out
        // (Multishot's, screenshots) - it moves her as an ability's run/walk does, off the item
        // line. Nor Cleansing's (blvl curse and poison length, 30): the Fire Iron Wolf's page reads "Curse
        // Resist: 25 / 75 Length: 100%" - its aura's 25, none of its passive - and poison "Length: 200%" (MercTest).
        // Nor Meditation's (blvl / 2 mana per kill, 14): the Cold Rogue's page reads "HP/MP per Kill: 0 / 0"
        // at level 98, after a load (MercTestColdRogue). Each passive seen on its own: Prayer's (life regen)
        // and Blessed Aim's (target defense) are on no page, so still count, unseen.
        // A mastery (merc_passive_mastery) stops at its row's 32: the Iron Wolf's cold mastery read 196% at level 96 and
        // at 98, `4 + lvl*3 + min(blvl*3, 150)` at 32, where hireling.txt says 33 and 34 (MercTest's and MercC's pages,
        // screenshots), and the fire one 298% at 95 and 97, `10 + lvl*5 + min(blvl*4, 384)`
        // at 32. Only the masteries: the Ascendant's Sanctuary works at 30 over its 20 ("magic 459-474"). Seen with
        // nothing worn - the lightning and Act 4 ones, and whether "+ to All Skills" lifts it past 32, not yet.
        int more = totals.Of("item_allskills");
        var skills = new Dictionary<int, (int Level, int BaseLevel)>();
        foreach (var (skill, skillLevel) in Hirelings.SkillsOf(hired.TypeId, level, expansion).Concat(Hirelings.InnateOf(hired.TypeId, expansion)))
        {
            if (SkillRows.Named(skill) is not { } row || skills.ContainsKey(row.Id))
                continue;
            int held = row is { PassiveState: "merc_passive_mastery", MaxLevel: > 0 } ? Math.Min(skillLevel, row.MaxLevel) : skillLevel;
            skills[row.Id] = (held + more, held);
        }

        var context = new SkillCalc.Context(
            name => SkillRows.Named(name) is { } row && skills.TryGetValue(row.Id, out var found) ? found : (0, 0),
            level,
            totals.Of);
        var weaponTypes = weapon is null ? [] : ItemTypes.Ancestry(ItemTypes.TypeOf(weapon.Code));
        AddPassives(totals, skills.Where(skill => SkillRows.Of(skill.Key) is not { PassiveState: "passive_runwalk" or "passive_cleanse" or "passive_meditate" }).ToDictionary(), context, weaponTypes,
            row => SkillNames.NameOf(row.Id) ?? row.Name);

        // Its own aura at that level, its own skills and stats - and, as a Paladin's own does him, the lines the row
        // keeps for its holder: Might's whole damage, the party's half (Buffs.Gives). In town it reaches no one else, so
        // the character's row has none of it, and it still works on itself, as a character's own aura does in town: the
        // Ascendant's Sanctuary, "magic 459-474" on its page in the Pandemonium Fortress (MercD, a screenshot).
        var ownAuras = player.Buffs.Where(buff => buff.Counted && buff.Kind == BuffKind.MercenaryAura).ToList();
        if (!options.OutOfTown)
        {
            foreach (var (skill, _) in Hirelings.SkillsOf(hired.TypeId, level, expansion))
            {
                if (SkillRows.Named(skill) is { IsAura: true } row)
                    ownAuras.Add(new Buff(row, SkillNames.NameOf(row.Id) ?? row.Name, 0, 0, BuffKind.MercenaryAura, itself));
            }
        }

        // And what it wears of auras, in town and out: its owner's lines, as an aura the character wears gives the
        // character - a Holy Fire 10 armor reads "Added Damage: fire 20-35" on its page, the row's edmn-edmx (MercAura's
        // Barbarian, a screenshot). Of its own aura and one it wears of the same skill, the worn one
        // (Buffs.Mercenarys).
        var worn = Characters.Buffs.WornAuras(
            Requirements.GearStatsBySource(gear).Where(each => each is { Stat: "item_aura", Item: not null } && SkillRows.Of(each.Param) is not null)
                .Select(each => (SkillRows.Of(each.Param)!, each.Value, NameOf(each.Item!))),
            row => SkillNames.NameOf(row.Id) ?? row.Name, items => L($"{items} (mercenary)")).Select(buff => buff with { Worn = true, OnMercenary = true });
        var kept = ownAuras.Select(buff => buff with { Level = skills.TryGetValue(buff.Skill.Id, out var its) ? its.Level : buff.Level })
            .Concat(worn)
            .GroupBy(buff => buff.Skill.AuraState.Length > 0 ? buff.Skill.AuraState : $"#{buff.Skill.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(same => Characters.Buffs.Mercenarys(same).MaxBy(buff => buff.Level)! is var one && one.Kind == BuffKind.MercenaryAura
                ? one with { Kind = BuffKind.OwnAura } : one)
            .ToList();

        // Of that and the same from the party, the higher level is counted, as between any two of one state (Buffs.Resolve):
        // counted both ways, a Paladin's Fanaticism and a Beast it wears read twice over.
        foreach (var party in fromThePlayer.Where(buff => itsAuraStates.Contains(buff.Skill.AuraState)))
        {
            var mine = kept.FirstOrDefault(buff => buff.Skill.AuraState.Equals(party.Skill.AuraState, StringComparison.OrdinalIgnoreCase));
            if (mine is not null && mine.Level >= party.Level)
                continue;

            if (mine is not null)
                kept.Remove(mine);
            CountFromThePlayer(party);
        }

        foreach (var buff in kept)
        {
            string source = buff.From.Length > 0 ? buff.Name + " - " + buff.From : buff.Name;
            foreach (var (stat, value) in Characters.Buffs.Gives(buff, context))
                totals.Add(stat, value, buff, source);
        }

        // What it casts on itself, pressed in its own row: Bone Armor, Battle Orders, what its gear casts by chance. Of
        // one of these and the same from the character, the higher level is counted, as between any two of one state.
        // What its gear keeps cast counts as on a character, at hard level 0: Hustle's Quickness reads Attack Rate 115 on both.
        var keptCast = Requirements.GearStatsBySource(gear)
            .Where(each => each is { Stat: "item_skillonequip", Item: not null } && SkillRows.Of(each.Param) is not null)
            .Select(each => SkillRows.Of(each.Param)! is var row
                ? new Buff(row, SkillNames.NameOf(row.Id) ?? row.Name, each.Value, 0, BuffKind.WornSkill, NameOf(each.Item!)) { OnMercenary = true }
                : null!);
        var itsOwn = Characters.Buffs.Resolve(
            [.. Characters.Buffs.OfMercenary(skills, chances, context, row => SkillNames.NameOf(row.Id) ?? row.Name, NameOf, itself), .. keptCast],
            options.Pressed, null, null, options.NothingCast);
        var alsoFromThePlayer = fromThePlayer.Where(buff => itsOwnStates.Contains(buff.Skill.AuraState)).ToList();
        foreach (var buff in alsoFromThePlayer)
        {
            if (!itsOwn.Any(mine => mine.Counted && mine.Skill.AuraState.Equals(buff.Skill.AuraState, StringComparison.OrdinalIgnoreCase) && mine.Level >= buff.Level))
                CountFromThePlayer(buff);
        }

        itsOwn = [.. itsOwn.Select(mine => mine.Counted && alsoFromThePlayer.Any(buff => buff.Level > mine.Level && buff.Skill.AuraState.Equals(mine.Skill.AuraState, StringComparison.OrdinalIgnoreCase))
            ? mine with { Counted = false } : mine)];
        foreach (var buff in itsOwn.Where(buff => buff.Counted))
        {
            string source = buff.Name + " - " + buff.From;
            foreach (var (stat, value) in Characters.Buffs.Gives(buff, context))
                totals.Add(stat, value, buff, source);
        }

        var dexterity = totals.Sum("dexterity");
        var basis = weapon is null ? null : ItemBases.Of(weapon.Code);
        string weaponClass = HeldClass(basis, oneHanded: false);
        var attackRate = SheetFigure.Of(new[] { new SheetPart(L("Base"), 100), new SheetPart(weapon is null ? "" : NameOf(weapon), -(basis?.Speed ?? 0)) }
            .Concat(totals.Sum("attackrate").Parts));

        var shield = gear.FirstOrDefault(item => IsHand(item.Location) && ItemBases.Of(item.Code) is { IsArmor: true, Block: > 0 });
        SheetFigure? blocking = null;
        int? block = null, mostBlockAt = null;
        if (shield is not null && ItemBases.Of(shield.Code) is { } shieldBasis)
        {
            blocking = SheetFigure.Of(new[] { new SheetPart(L($"{NameOf(shield)} (base)"), shieldBasis.Block) }.Concat(totals.Sum("toblock").Parts));
            block = Math.Clamp(blocking.Value * (dexterity.Value - 15) / (2 * level), 0, MostBlock);
            mostBlockAt = blocking.Value <= 0 ? null : (2 * level * MostBlock + blocking.Value - 1) / blocking.Value + 15;
        }

        // Its monster's animation in a mode: with the weapon it holds, else its monster's own with nothing in hand
        // (monstats2 BaseW - the Barbarian's two-handed sword, Hirelings.Base), else hand to hand, else the one there is.
        AnimData.Animation? Animation(string mode) =>
            own.Token.Length == 0 ? null
            : AnimData.Of(own.Token, mode, weaponClass) ?? AnimData.Of(own.Token, mode, own.WeaponClass) ?? AnimData.Of(own.Token, mode, "HTH")
              ?? AnimData.All.Where(pair => pair.Key.StartsWith(own.Token + mode, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value).FirstOrDefault();

        Breakpoints.Row? Row(Breakpoints.Rule rule, string stat) =>
            Animation(rule.Mode) is { } animation ? new Breakpoints.Row(Breakpoints.Of(animation, rule), totals.Of(stat)) : null;

        // Its attack, as the page picks it: the first of its skills that is one of the mercenaries' attacks, in the
        // monster mode it is used in - a sequence by its frames in MonSeq.txt, counted as a monster's: no penalty,
        // speed 256 (the Desert Guard's Jab, 14 frames: "0 / 4 / 9 / 16 / 24 / 37 / 54 / 80 / 125 (9) / 208 / 456"
        // at attack rate 80, LightFury's mercenary, a screenshot). Whirlwind's frames are its
        // own: the page reads "0 (n)", n its row's third parameter.
        int act = Hirelings.ActOf(hired.TypeId, expansion);
        int itemSpeed = totals.Of("item_fasterattackrate");
        var use = Hirelings.UsesOf(hired.TypeId, expansion)
            .FirstOrDefault(each => SkillRows.Named(each.Skill) is { } row && MercenaryAttacks.Contains(row.Id));
        (Breakpoints.Row? Row, string Note) Attack()
        {
            if (use is null || SkillRows.Named(use.Skill) is not { } skill)
                return (null, "no attack: a caster");
            if (skill.Id == MercenaryWhirlwind)
                return (new Breakpoints.Row([(0, skill.Params[2])], itemSpeed), "Whirlwind, its own frames");
            if (use.Mode == Hirelings.SequenceMode)
                return use.SequenceFrames > 0
                    ? (new Breakpoints.Row(Breakpoints.AttackRow(use.SequenceFrames, 256, 0, attackRate.Value, sequence: false), itemSpeed), $"{use.Skill}, a sequence of {use.SequenceFrames} frames")
                    : (null, $"{use.Skill}: its sequence is in no table");

            string mode = use.Mode == Hirelings.SecondAttackMode ? "A2" : "A1";
            return Animation(mode) is { } swing
                ? (new Breakpoints.Row(Breakpoints.AttackRow(swing.Frames, swing.Speed, 0, attackRate.Value, sequence: false), itemSpeed), $"{use.Skill}, its monster's {mode}; bare-handed matched in game (A5), with a weapon not yet")
                : (null, $"{use.Skill}: no {mode} animation");
        }

        var (attackRow, attackNote) = Attack();

        return new CharacterSheet(totals)
        {
            Name = MercenaryEdit.NameOf(save, game),
            ClassId = save.ClassId,
            Level = level,
            Difficulty = player.Difficulty,
            IsMercenary = true,
            AttackName = act switch
            {
                1 => L("Fire/Cold/Magic Arrow"),
                2 => L("Jab"),
                3 => L("Fireball/Ice Blast/Lightning"),
                4 => L("Holy Nova/Bone Spear"),
                _ => L("Concentrate/Whirlwind"),
            },
            Strength = totals.Sum("strength"),
            Dexterity = dexterity,
            AttackFromDexterity = SheetFigure.Of([new SheetPart(L("Dexterity"), dexterity.Value * 5), new SheetPart(classStats.Name, classStats.ToHitFactor - 35)]),
            DefenseFromDexterity = SheetFigure.Of([new SheetPart(L("Dexterity"), dexterity.Value / 4)]),
            Weapon = weapon,
            WeaponClass = weaponClass,
            AttackRate = attackRate,
            Blocking = blocking,
            Block = block,
            DexterityForMostBlock = mostBlockAt,
            CastRateRow = Row(Breakpoints.CastRate, "item_fastercastrate"),
            HitRecoveryRow = Row(Breakpoints.HitRecovery, "item_fastergethitrate"),
            AttackRow = attackRow,
            AttackRowNote = attackNote,
            Buffs = [.. player.Buffs.Where(buff => Characters.Buffs.ReachesOthers(buff) && !(buff.OnMercenary && buff.Kind == BuffKind.Cast)), .. itsOwn],
            CasterContext = context,
            Gear = gear,
            LeftOut = [.. judged.Where(each => !each.Met).Select(each => (each.Item, each.Short ?? ""))],
        };
    }

    /// <summary>The skills the page counts a mercenary's attack row by: the Rogue's three arrows, Jab, Concentrate and the Barbarian's Whirlwind.</summary>
    private static readonly HashSet<int> MercenaryAttacks = [385, 386, 545, 10, 144, MercenaryWhirlwind];

    private const int MercenaryWhirlwind = 425;

    private const int Barbarian = 4;

    /// <summary>Holy Shield's skill id: up, a Paladin blocks from 100 (<see cref="Breakpoints.HolyShieldBlockRate"/>).</summary>
    private const int HolyShield = 117;

    /// <summary>
    /// The class a weapon in hand is swung with - its animation's: <c>hth</c> bare-handed; a weapon for two hands
    /// held in both, its two-handed class (weapons.txt <c>2handedwclass</c> - a two-handed sword's <c>2hs</c>: the
    /// Druid's 21 frames, not the 19 of its <c>1hs</c>; PD2's breakpoint tables give a "2H Sword" row to every
    /// class); held in one, which a Barbarian may (<c>1or2handed</c>), its own <c>wclass</c>.
    /// </summary>
    private static string HeldClass(ItemBases.Basis? basis, bool oneHanded) =>
        basis is not { WeaponClass.Length: > 0 } ? "hth"
        : basis is { TwoHanded: true, TwoHandedClass.Length: > 0 } && !(oneHanded && basis.OneOrTwoHanded) ? basis.TwoHandedClass
        : basis.WeaponClass;

    /// <summary>The wolf's and the bear's monster codes, whose animations a wereform's are.</summary>
    private const string WolfToken = "40", BearToken = "TG";

    /// <summary>The increased attack speed a weapon itself carries, sockets and all - its own, not the wearer's.</summary>
    private static int OwnSpeed(Item weapon) =>
        Requirements.GearStatsBySource([weapon]).Where(each => each.Stat == "item_fasterattackrate").Sum(each => each.Value);

    /// <summary>
    /// A form's attack row - the wolf's, the bear's.
    /// The game's own page shows none; PD2's wiki has the tables, and this reads every cell of its four single-hit ones
    /// (the bear's attack and Hunger, the wolf's attack and Rabies: 2,210 of 2,210). The form is as quick as the character's own
    /// attack with the weapon, by the weapon's own increased attack speed only (<see cref="WereformSpeed"/>); every
    /// attack speed then counts on that, on the form's own frames for the skill. A skill of several hits (Fury's first
    /// two) is not counted. Another form - the vampire's - attacks on its monster's own animation, as the game's page
    /// counts it.
    /// </summary>
    private static (Breakpoints.Row? Row, string Note) FormAttackRowOf(
        string token, SkillRow? skill, int classId, ItemBases.Basis? weapon, int weaponSpeed, int attackRate, int itemSpeed)
    {
        string mode = skill?.Anim.ToUpperInvariant() ?? "A1";
        if (mode is "SC" or "" or "RN")
            return (null, "a cast or an aura");
        if (skill is not null && RollbackOf(skill) is not null)
            return (null, "a skill of several hits in a form: not counted");
        if (AnimData.Of(token, mode, "HTH") is not { } attack)
            return (null, $"no {mode} for the form {token}");

        // Any other form attacks with its monster's own animation at its own speed: the vampire's VAA1HTH, 14 frames
        // at 176 - "IAS (Frames): 0 (20) / 4 / 9 / 16 / 24 / 37 / 52 / 75 / 109 / 168" at attack rate 100
        // (a level 98 Necromancer, a screenshot; the game's own page has this row).
        if (token is not (WolfToken or BearToken))
            return (new Breakpoints.Row(Breakpoints.AttackRow(attack.Frames, attack.Speed, 0, attackRate, sequence: false), itemSpeed),
                    $"form {token} {mode}: {attack.Frames} frames at its own speed {attack.Speed}");
        if (WereformSpeed(token, classId, weapon?.WeaponClass ?? "", weaponSpeed - (weapon?.Speed ?? 0)) is not { } speed)
            return (null, $"no neutral or own attack for the form {token}");

        return (new Breakpoints.Row(Breakpoints.AttackRow(attack.Frames, speed, 0, attackRate, sequence: false), itemSpeed),
                $"form {token} {mode}: {attack.Frames} frames at speed {speed}");
    }

    /// <summary>
    /// How quick a form is, in 256ths of a frame a tick, before any attack speed: the form's neutral frames
    /// (animdata <c>NU</c> - the bear 10, the wolf 9) over the frames the character's own attack takes with the weapon
    /// - <c>A1</c> of its <c>wclass</c>, the one-handed class (a two-handed sword is swung as <c>1hs</c>), less the
    /// frames it skips at its start (the Amazon's and the Sorceress's 2), at its own speed but 256 at most (a claw's
    /// 208 counts, a crossbow's 282 does not) - sped by the <paramref name="threshold"/> alone: the weapon's own
    /// increased attack speed less its speed (weapons.txt <c>speed</c>), uncapped - PD2's "Wereform Weapon Threshold".
    /// Every class's, as PD2's wiki tables them for a form an item lends ("OSkill Wereform": 7,430 of 7,698 cells; the
    /// Amazon's staves and polearms, which it groups with her spears at 16 frames, read 18 here - and the game's own
    /// page swings her polearm on that 18, LightFury's Battlebranch: the wiki's slip). Null without the
    /// animations.
    /// </summary>
    public static int? WereformSpeed(string token, int classId, string weaponClass, int threshold)
    {
        string held = weaponClass.Length > 0 ? weaponClass : "HTH";
        if (AnimData.Of(token, "NU", "HTH") is not { } neutral || AnimData.Of(classId, "A1", held) is not { } own)
            return null;

        int swing = Math.Min(own.Speed, 256) * Math.Max(100 + threshold, Breakpoints.SlowestAttack) / 100;
        int ownLength = 256 * (own.Frames - StartFrames(classId, "A1", held)) / swing;
        return ownLength <= 0 ? null : 256 * neutral.Frames / ownLength;
    }

    /// <summary>The most a character blocks, in percent.</summary>
    public const int MostBlock = 75;

    private static bool IsHand(ItemLocation location) =>
        location is ItemLocation.RightHand or ItemLocation.LeftHand or ItemLocation.SwapRight or ItemLocation.SwapLeft;

    /// <summary>Whether a stat is about the attack (the stat table's <c>damagerelated</c>): a second weapon's is not the character's.</summary>
    private static bool IsDamageRelated(string stat) =>
        D2SLib.Core.TXT.ItemStatCostTXT[stat] is { } row && row["damagerelated"].Value?.Trim() == "1";

    /// <summary>
    /// Whether a percent an item holds is of its own base and no one else's: enhanced defense on what has a
    /// defense, enhanced damage on a weapon (the stat table's op 13).
    /// </summary>
    private static bool OfItsOwnBase(string stat, Item holder) => stat switch
    {
        "item_armor_percent" or "item_armorpercent_perlevel" => holder.Armor > 0,
        "item_mindamage_percent" or "item_maxdamage_percent" or "item_maxdamage_percent_perlevel" =>
            ItemBases.Of(holder.Code) is { WeaponClass.Length: > 0 },
        _ => false,
    };

    /// <summary>The skills of no class every character may put in a box: Attack, Throw, Unsummon.</summary>
    private static readonly int[] EveryClasses = [0, 2, 3];

    /// <summary>The skills every character has without a point or an item: Attack to Left Hand Swing (0-5).</summary>
    private const int LastBasic = 5;

    /// <summary>
    /// A box's skill where the character has it - a basic skill, a level, or an item's charges - else Attack, as a
    /// box whose skill is gone holds in game.
    /// </summary>
    private static SkillRow? HeldOrAttack(SkillRow? row, Dictionary<int, (int Level, int BaseLevel)> levels, List<Requirements.Sourced> gear) =>
        row is null || row.Id <= LastBasic || levels.GetValueOrDefault(row.Id).Level > 0
        || gear.Any(each => each.Stat == "item_charged_skill" && each.Param == row.Id)
            ? row
            : SkillRows.Of(0) ?? row;

    /// <summary>
    /// What one skill box may hold (<see cref="RightChoices"/>), in the game's own order. Of the skills every class
    /// has, one that needs a kind of weapon only with it in hand: Throw with a throwing weapon.
    /// In a form, only what that form may use, as the skill table says (<c>restrict</c>: 1 any form, 2 only the
    /// forms its states name, 0 none) - no Teleport as a bear.
    /// </summary>
    private static List<SkillLevel> ChoicesOf(
        SkillTree tree, Dictionary<int, (int Level, int BaseLevel)> levels, List<Requirements.Sourced> gear, List<Item> inHand,
        SkillRow? now, bool onTheLeft, SkillStates.State? form)
    {
        bool InForm(SkillRow row) => form is null || row.Restrict == 1
            || (row.Restrict == 2 && row.States.Contains(form.Name, StringComparer.OrdinalIgnoreCase));

        var held = inHand.SelectMany(item => ItemTypes.Ancestry(ItemTypes.TypeOf(item.Code))).ToHashSet();
        var found = new List<SkillLevel>();
        void Offer(SkillRow? row, int level, int baseLevel)
        {
            if (row is null || found.Any(each => each.Id == row.Id))
                return;
            if (row != now && (row.IsPassive || row.IconCel < 0 || (onTheLeft && row.Text("leftskill") != "1") || !InForm(row)))
                return;

            found.Add(new SkillLevel(row.Id, SkillNameOf(tree, row), level, baseLevel));
        }

        foreach (int id in EveryClasses)
        {
            if (SkillRows.Of(id) is { } basic && basic.Text("itypea1") is var needs && (needs is "" or "weap" || held.Contains(needs)))
                Offer(basic, 1, 1);
        }
        foreach (var (id, (level, baseLevel)) in levels.Where(pair => pair.Value.Level > 0))
            Offer(SkillRows.Of(id), level, baseLevel);
        foreach (var charges in gear.Where(each => each.Stat == "item_charged_skill"))
            Offer(SkillRows.Of(charges.Param), charges.SkillLevel, 0);
        Offer(now, levels.GetValueOrDefault(now?.Id ?? -1).Level, levels.GetValueOrDefault(now?.Id ?? -1).BaseLevel);

        return found;
    }

    /// <summary>A skill's name as the tree shows it, else as the game names it, else the table's own.</summary>
    private static string SkillNameOf(SkillTree tree, SkillRow row) =>
        tree.Pages.SelectMany(page => page.Skills).FirstOrDefault(skill => skill.Id == row.Id)?.Name
        ?? (SkillNames.NameOf(row.Id) is { Length: > 0 } named ? named : row.Name);

    /// <summary>
    /// The Scrolls of Resistance read: Prison of Ice's flag 7 in each difficulty - set in the word a finished
    /// quest has and in the one the program writes, with which a character made here reads 30 in game (Fury).
    /// </summary>
    private static int ScrollsRead(D2S save)
    {
        if (save.Quests is null || QuestProgress.All.FirstOrDefault(quest => quest.Key == "ActV.PrisonOfIce") is not { } prison)
            return 0;

        int read = 0;
        for (int difficulty = 0; difficulty < 3; difficulty++)
        {
            string word = QuestProgress.WordOf(QuestProgress.QuestOf(save.Quests, difficulty, prison));
            if (word.Length > 7 && word[7] == '1')
                read++;
        }

        return read;
    }

    /// <summary>
    /// Each skill's level: the class's own - hard points and what gear adds to a skill that holds one
    /// (<see cref="SkillTips.BonusFrom"/>, the tree's own count) - and the skills of other classes an item lends,
    /// which take the item's levels and "+ to All Skills", and have no hard point: a lent skill's <c>blvl</c> is
    /// 0 (Battle Command from Call to Arms reads "+1", a screenshot).
    /// <paramref name="raised"/> is what a cry already up adds to every skill.
    /// </summary>
    private static Dictionary<int, (int Level, int BaseLevel)> LevelsOf(
        D2S save, SkillTree tree, List<(string Stat, int Value, int Param)> gear, int classId, int raised)
    {
        var levels = new Dictionary<int, (int, int)>();
        foreach (var skill in tree.Pages.SelectMany(page => page.Skills))
        {
            int hard = skill.Slot >= 0 && skill.Slot < (save.ClassSkills?.Skills.Count ?? 0) ? save.ClassSkills!.Skills[skill.Slot].Points : 0;
            // A skill items grant without a hard point takes a cry's levels too, as every other bonus.
            int bonus = SkillTips.BonusFrom(gear, skill, classId, hard);
            levels[skill.Id] = (hard + bonus + (hard > 0 || bonus > 0 ? raised : 0), hard);
        }

        int all = raised + gear.Where(each => each.Stat == "item_allskills").Sum(each => each.Value);
        foreach (var lent in gear.Where(each => each.Stat == "item_nonclassskill" && !levels.ContainsKey(each.Param)).GroupBy(each => each.Param))
            levels[lent.Key] = (lent.Sum(each => each.Value) + all, 0);

        return levels;
    }

    /// <summary>
    /// The right hand's skill's attack row, where its timing is known: a plain attack or a throw, counted on the
    /// class's own animation - with two weapons the same one (a Barbarian's is 16 frames however the two are
    /// held: "0 / 9 / 18 / 30 (12) / 48 / 75 / 125" at attack rate 100, a screenshot). A skill that is a sequence waits on its frames, which are in no table;
    /// a cast has no attack row.
    /// </summary>
    private static (Breakpoints.Row? Row, string Note) AttackRowOf(
        SkillRow? skill, int classId, Item? weapon, string weaponClass, string? secondClass, int attackRate, int itemSpeed, int? strikes)
    {
        bool twoWeapons = secondClass is not null;
        string mode = skill?.Anim.ToUpperInvariant() ?? "A1";
        if (mode is "SC" or "" or "RN")
            return (null, "a cast or an aura");
        if (mode == "SQ" && IsSlowCast(classId, skill!, out _))
            return (null, "a cast");
        if (skill?.Id == Charge)
            return (new Breakpoints.Row(Breakpoints.ChargeRow(attackRate, skill.Params[5]), itemSpeed), "Charge, by its own rule");
        // One number, whatever the attack speed: its third parameter, one more with two weapons - "0 (6)" on a Short Sword,
        // "0 (7)" on two (SeqBarb, screenshots).
        if (skill?.Id == Whirlwind)
            return (new Breakpoints.Row([(0, skill.Params[2] + (twoWeapons ? 1 : 0))], itemSpeed), "Whirlwind, its own frames");
        if (mode == "SQ")
        {
            string held = twoWeapons ? $"{weaponClass}+{secondClass}" : weaponClass;
            if (!MeasuredSequences.TryGetValue((classId, skill!.Sequence, held), out int length)
                && !SequencesByAnyWeapon.TryGetValue((classId, skill.Sequence), out length))
                return (null, $"sequence {skill.Sequence} with {held}: its frames are in no table");

            return (new Breakpoints.Row(Breakpoints.AttackRow(length, 256, 0, attackRate, sequence: true), itemSpeed), $"sequence {skill.Sequence} with {held}: {length} frames, measured in game");
        }
        // The Assassin's traps and sentries lay on S2's own frames: "0 (15) / 9 / 18 / 30 / 48 / 78 / 125" at attack rate
        // 100 with Fire Blast and with Death Sentry; Smite strikes on S1's: "0 (11) / 11 / 26 / 48 / 86 / 180" (SeqAssn,
        // SeqPala, screenshots).
        if (mode is not ("A1" or "TH" or "KK" or "S1" or "S2"))
            return (null, $"mode {mode} not measured");

        // A throw with a weapon that is not thrown is a plain attack.
        bool thrown = weapon is not null && ItemTypes.Ancestry(ItemTypes.TypeOf(weapon.Code)).Contains("thro");
        if (mode == "TH" && !thrown)
            mode = "A1";

        if (AnimData.Of(classId, mode, weaponClass) is not { } animation)
            return (null, $"no animation {AnimData.TokenOf(classId)}{mode}{weaponClass}");

        int start = StartFrames(classId, mode, weaponClass);
        if (skill is not null && RollbackOf(skill) is { } rollback)
        {
            if (strikes is not > 0)
                return (null, "its strikes are not known");

            var (hitPoints, hits) = Breakpoints.MultiHitRow(strikes.Value, animation.ActionFrame, animation.Frames, animation.Speed, start, attackRate, rollback);
            return (new Breakpoints.Row(hitPoints, itemSpeed, hits), $"{AnimData.TokenOf(classId)}{mode}{weaponClass}: {animation.Frames} frames acting on {animation.ActionFrame}, start {start}, {strikes} strikes put back {rollback}%");
        }

        var points = Breakpoints.AttackRow(animation.Frames, animation.Speed, start, attackRate, sequence: false);
        return (new Breakpoints.Row(points, itemSpeed), $"{AnimData.TokenOf(classId)}{mode}{weaponClass}: {animation.Frames} frames at {animation.Speed}, start {start}");
    }

    /// <summary>
    /// A spell that is cast as a sequence, and so by a cast row of its own: the frames of the sequence, by class
    /// and sequence. The Sorceress's 12 - Chain Lightning and Frozen Orb - is 19: the game's own page lists "7 /
    /// 15 / 23 / 35 / 52 / 78 / 117 / 194" for the two (its stat page's source), and 19 frames
    /// is the one length that gives that row - seen in game with Chain Lightning, and the plain row with
    /// Lightning and Ice Blast (a level 97 Sorceress, screenshots, the same day).
    /// </summary>
    private static readonly Dictionary<(int Class, int Sequence), int> CastSequences = new()
    {
        [(1, 12)] = 19,
    };

    private static bool IsSlowCast(int classId, SkillRow skill, out int frames)
    {
        frames = 0;
        return skill.Anim.Equals("SQ", StringComparison.OrdinalIgnoreCase) && CastSequences.TryGetValue((classId, skill.Sequence), out frames);
    }

    /// <summary>
    /// A sequence skill's frames, which no table holds - they are the engine's own - by class, sequence and how
    /// the weapons are held; each solved from one row of the game's page, the only row that fits it.
    /// <list type="bullet">
    /// <item>Amazon, sequence 1 (Jab), a two-handed thrusting weapon: 18 - "0 / 4 / 7 / 13 / 18 / 24 / 35 / 46 /
    /// 63 / 83 / 117 / 168 / 280" at attack rate 110, 18 frames at IAS 18 (SpearZon).</item>
    /// <item>Amazon, sequence 1 (Jab), a one-handed thrusting weapon: 16 - "0 / 2 / 6 / 11 / 18 / 24 / 37 / 50 /
    /// 70 (12) / 99 / 147 / 250" at attack rate 110 (a level 90 Amazon, a screenshot). Both Jabs are
    /// PD2's own: the base game's table has 18 and 21.</item>
    /// <item>Assassin, sequence 16 (Dragon Claw), a Katar: 12 - "0 (15) / 2 / 7 / 15 / 24 / 40 / 63 / 99 / 168 / 395" at
    /// attack rate 110; two: 16 - "0 (20) / 2 / 6 / 11 / 18 / 24 / 37 / 50 / 70 / 99 / 147 / 250" (SeqAssn).
    /// Blade Fury, sequence 23, the page says N/A: no entry here, the row stays empty.</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<(int Class, int Sequence, string Held), int> MeasuredSequences = new()
    {
        [(0, 1, "1ht")] = 16,
        [(0, 1, "2ht")] = 18,
        [(6, 16, "ht1")] = 12,
        [(6, 16, "ht1+ht1")] = 16,
    };

    /// <summary>
    /// A sequence of the same length however the weapons are held, as the engine's own table has them (D2MOO's
    /// SequenceTbls.cpp) and each seen in game - which that table is not enough for: it is wrong on PD2's Jab.
    /// <list type="bullet">
    /// <item>Barbarian, sequence 11 (Double Swing, Frenzy, Berserk): 17 - with two swinging weapons "IAS (Frames): 0 (16) /
    /// 6 / 14 / 24 / 39 / 60 / 95 / 163" at attack rate 132, with two thrusting ones "0 / 3 / 6 / 9 / 14 / 19 / 24 / 32
    /// (17) / 40 / 54 / 70 / 92 / 125 / 180 / 292 / 638" at attack rate 100; that same row with Berserk on a
    /// Short Sword and a Two-Handed Sword and Frenzy on a Short Sword (SeqBarb).</item>
    /// <item>Barbarian, sequence 13 (Leap; in PD2 Leap Attack too, the base game's 14): 15 - "0 (21) / 3 / 6 / 11 / 16 /
    /// 23 / 30 / 40 / 56 / 75 / 102 / 152 / 240 / 507" at attack rate 100, both on a Short Sword (SeqBarb).</item>
    /// <item>Druid, sequence 18 (Arctic Blast): 8, where the engine's table has 15 - "0 (11) / 5 / 13 / 24 / 40 / 72 / 138
    /// / 377" at attack rate 100, bare-handed (SeqDruid); with a weapon not seen. Inferno, sequence 6: the
    /// page says N/A, no entry.</item>
    /// <item>Barbarian, sequence 15 (Double Throw): 12 - "0 (17) / 2 / 6 / 13 / 19 / 29 / 40 / 60 / 89 / 138 / 240 / 680"
    /// at attack rate 100, two thrusting weapons and an axe with a knife; "0 (20) / 2 / 5 / 8 / 13 / 18 / 26
    /// / 34 / 46 / 60 / 86 / 125 / 194 / 360 / 1680" at 90, two Throwing Axes (SeqBarb).</item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<(int Class, int Sequence), int> SequencesByAnyWeapon = new()
    {
        [(4, 11)] = 17,
        [(4, 13)] = 15,
        [(4, 15)] = 12,
        [(5, 18)] = 8,
    };

    /// <summary>
    /// The frames an attack skips at its start: an Amazon's or a Sorceress's attack starts two frames in with a
    /// weapon swung, thrust or a staff, and one with bare hands; with a bow or a crossbow, in a throw or a
    /// kick, and for every other class, none (the engine's rule, as the attack-speed calculators have it). Seen
    /// in game: an Amazon's two-handed thrust (2: a Ceremonial Spear's "0 / 6 / 16 / 30 / 52 / 89")
    /// and her throw (0: LightFury's Lightning Fury, the same row on 16 frames).
    /// </summary>
    private static int StartFrames(int classId, string mode, string weaponClass)
    {
        if (mode != "A1" || classId is not (0 or 1))
            return 0;

        return weaponClass.ToLowerInvariant() switch
        {
            "hth" => 1,
            "1hs" or "1ht" or "2hs" or "2ht" or "stf" => 2,
            _ => 0,
        };
    }

    /// <summary>The Paladin's Charge, whose row the game's page counts its own way (<see cref="Breakpoints.ChargeRow"/>).</summary>
    private const int Charge = 107;

    /// <summary>The Barbarian's Whirlwind, whose row is one number (<see cref="AttackRowOf"/>).</summary>
    private const int Whirlwind = 151;

    /// <summary>The Paladin's Smite, whose crushing blow the page counts while it is on the right hand.</summary>
    private const int Smite = 97;

    /// <summary>
    /// How far a skill that strikes more than once in one go is put back after each strike, in percent - null
    /// for every other skill. The game's own page knows them by id and reads the percent off the skill's row:
    /// Strafe (26) <c>par6</c>; Fend (30), Zeal (106) and Fury (248) <c>par2</c>; Dragon Talon (255) the whole
    /// way, which is in no table.
    /// </summary>
    private static int? RollbackOf(SkillRow skill) => skill.Id switch
    {
        26 => skill.Params[5],
        30 or 106 or 248 => skill.Params[1],
        255 => 100,
        _ => null,
    };
}
