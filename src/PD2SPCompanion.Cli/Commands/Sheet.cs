namespace PD2SPCompanion.Cli;

// The character sheet, printed in the order of the game's own Advanced Stats page, to lay over a screenshot
// of it line by line.
internal static partial class Cli
{
    static int AnimCommand(string gameRoot, string start)
    {
        using var installed = Season.Open(gameRoot);

        foreach (var (name, animation) in AnimData.All.Where(pair => pair.Key.StartsWith(start, StringComparison.OrdinalIgnoreCase)).OrderBy(pair => pair.Key))
        {
            string cast = string.Join("/", Breakpoints.Of(animation, Breakpoints.CastRate).Select(point => point.Value));
            string recovery = string.Join("/", Breakpoints.Of(animation, Breakpoints.HitRecovery).Select(point => point.Value));
            Console.WriteLine($"{name,-8} frames {animation.Frames,3} speed {animation.Speed,4} acts {animation.ActionFrame,3}   as a cast {cast}   as a hit taken {recovery}");
        }

        return 0;
    }

    static int FormulasCommand(string gameRoot)
    {
        using var installed = Season.Open(gameRoot);
        var context = new SkillCalc.Context(_ => (1, 1), 50, _ => 0);
        int read = 0, unread = 0;

        foreach (var row in SkillRows.All.OrderBy(row => row.Id))
        {
            var lines = (row.PassiveState.Length > 0 ? row.Passives : [])
                .Concat(row.AuraState.Length > 0 && row.GivesTheCaster ? row.Auras : []);
            foreach (var (stat, formula) in lines)
            {
                if (new SkillCalc(row, 20, 20, context).Evaluate(formula) is not null)
                {
                    read++;
                    continue;
                }

                unread++;
                Console.WriteLine($"{row.Id,4} {row.Name,-28} {stat,-28} {formula}");
            }
        }

        Console.WriteLine($"{read} lines read, {unread} not");
        return 0;
    }

    static int SheetCommand(string gameRoot, string path)
    {
        using var installed = Season.Open(gameRoot);
        var (save, _) = SaveVerifier.Load(path);

        // --set is the tab the game shows a pair under, as its page is read: the one in hand, or the other.
        int tab = Option("--set") is "1" or "2" ? int.Parse(Option("--set")!) : WeaponSets.TabInHand(save);
        int set = tab == WeaponSets.TabInHand(save) ? WeaponSets.InHand : WeaponSets.Swapped;
        bool sources = arguments.Contains("--sources");
        // --right and --left: the skill boxes as they will stand in game, without touching the file.
        uint? Box(string option)
        {
            if (Option(option) is not { Length: > 0 } asked)
                return null;
            if (uint.TryParse(asked, out uint id))
                return id;

            var row = SkillRows.All.OrderBy(row => row.CharClass.Length == 0).FirstOrDefault(row =>
                string.Equals(SkillNames.NameOf(row.Id), asked, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Name, asked, StringComparison.OrdinalIgnoreCase));
            return row is null ? throw new ArgumentException($"no skill named '{asked}'") : (uint)row.Id;
        }

        if (Box("--right") is { } right)
        {
            if (set == WeaponSets.Swapped) save.RightSwapSkill = new D2SLib.Model.Save.Skill { Id = right };
            else save.RightSkill = new D2SLib.Model.Save.Skill { Id = right };
        }

        if (Box("--left") is { } left)
        {
            if (set == WeaponSets.Swapped) save.LeftSwapSkill = new D2SLib.Model.Save.Skill { Id = left };
            else save.LeftSkill = new D2SLib.Model.Save.Skill { Id = left };
        }

        // --bare: as a character stands right after it is loaded - nothing cast - but for what --on names.
        var pressed = new Dictionary<string, bool>();
        bool near = !arguments.Contains("--town");
        if (arguments.Contains("--bare"))
        {
            // A Paladin's own aura is not cast: it is the skill boxes' to say.
            foreach (var buff in CharacterSheet.For(save, installed.Game, new CharacterSheet.Options(set, near)).Buffs.Where(buff => buff.Toggles && buff.Kind != BuffKind.OwnAura))
                pressed[buff.Key] = false;
        }

        foreach (string key in (Option("--on") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            pressed[key] = true;
        foreach (string key in (Option("--off") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            pressed[key] = false;
        // --merc: the mercenary's page, the party's buffs as the character's row has them.
        var sheet = arguments.Contains("--merc")
            ? CharacterSheet.ForMercenary(save, installed.Game, new CharacterSheet.Options(set, near, pressed, NothingCast: arguments.Contains("--bare"))) ?? throw new ArgumentException("no mercenary")
            : CharacterSheet.For(save, installed.Game, new CharacterSheet.Options(set, near, pressed));

        void Line(string text, params SheetFigure?[] figures)
        {
            Console.WriteLine(text);
            if (!sources)
                return;

            foreach (var figure in figures.OfType<SheetFigure>().Where(figure => figure.Parts.Count > 0))
                Console.WriteLine($"      = {string.Join(" + ", figure.Parts.Select(part => $"{part.Value} {part.Source}"))}");
        }

        string Row(Breakpoints.Row? row, bool frames = false) => row is null
            ? "-"
            : string.Join(" / ", row.Points.Select((point, at) => at == row.Reached
                ? $"[{point.Value}{(frames ? $" ({(row.Hits is { } hits ? string.Join("|", hits[at]) : point.Frames)})" : "")}]"
                : $"{point.Value}"));

        Console.WriteLine($"Name: {sheet.Name}   Level: {sheet.Level}   {CharacterClasses.NameOf(save.ClassId)}, in {CharacterSheet.DifficultyName(sheet.Difficulty)}, weapon set {tab}");
        Line($"Strength {sheet.Strength.Value}  Dexterity {sheet.Dexterity.Value}  Vitality {sheet.Vitality.Value}  Energy {sheet.Energy.Value}",
             sheet.Strength, sheet.Dexterity, sheet.Vitality, sheet.Energy);
        Console.WriteLine($"Scrolls of Resistance read: {sheet.Scrolls}");
        Console.WriteLine();

        Console.WriteLine($"Buffs{(sheet.Form is { } form ? $"   (form: {form.Name}, animations {form.FormToken})" : "")}:");
        foreach (var buff in sheet.Buffs)
        {
            string state = !buff.Toggles ? "up   " : buff.On ? "[on] " : "[off]";
            Console.WriteLine($"  {state} {buff.Key,-20} {buff.Name,-22} level {buff.Level,2} ({buff.BaseLevel})"
                              + (buff.From.Length > 0 ? $"  from {buff.From}" : "")
                              + (buff.Rivals.Length > 0 ? $"  one of: {buff.Rivals}" : "")
                              + (buff.Waits is { } waits ? $"  waits for: {waits}" : "")
                              + (buff.On && !buff.Counted ? "  (outdone by a higher level)" : "")
                              + (buff.Skill.GivesTheCaster ? "" : "  (its lines are not the character's)"));
        }

        Console.WriteLine();

        foreach (int difficulty in new[] { 0, 1, 2 })
        {
            Console.WriteLine($"-- {CharacterSheet.DifficultyName(difficulty)}{(difficulty == sheet.Difficulty ? "  <- the page's" : "")}");
            foreach (var (label, element) in new[] { ("Fire Resist", "fire"), ("Cold Resist", "cold"), ("Lightning Resist", "lightning"), ("Poison Resist", "poison") })
            {
                var resist = sheet.Resist(element, difficulty);
                string more = element == "cold" ? $"  Length: {sheet.ColdLength}%" : element == "poison" ? $"  Length: {sheet.PoisonLength(difficulty)}%" : "";
                var length = difficulty != sheet.Difficulty ? null : element == "cold" ? sheet.ColdLengthSources : element == "poison" ? sheet.PoisonLengthSources(difficulty) : null;
                Line($"{label}: {resist.Value.Value} / {resist.Maximum.Value}{more}", length, difficulty == sheet.Difficulty ? resist.Value : null, difficulty == sheet.Difficulty ? resist.Maximum : null);
            }
        }

        Line($"Curse Resist: {Math.Min(sheet.CurseResist.Value, 75)} / 75  Length: {sheet.CurseLength}%", sheet.CurseResist, sheet.CurseLengthSources);
        Console.WriteLine();

        int Of(string stat) => sheet.Totals.Of(stat);
        Line($"Absorption: {Of("item_absorbfire")}/{Of("item_absorbfire_percent")}% {Of("item_absorbcold")}/{Of("item_absorbcold_percent")}% {Of("item_absorblight")}/{Of("item_absorblight_percent")}% {Of("item_absorbmagic")}/{Of("item_absorbmagic_percent")}%",
             sheet.Stat("item_absorbfire"), sheet.Stat("item_absorbfire_percent"), sheet.Stat("item_absorbcold"), sheet.Stat("item_absorbcold_percent"),
             sheet.Stat("item_absorblight"), sheet.Stat("item_absorblight_percent"), sheet.Stat("item_absorbmagic"), sheet.Stat("item_absorbmagic_percent"));
        Line($"Damage Reduction: {Of("normal_damage_reduction")}/{Of("damageresist")}% {Of("magic_damage_reduction")}/{Of("magicresist")}%",
             sheet.Stat("normal_damage_reduction"), sheet.Stat("damageresist"), sheet.Stat("magic_damage_reduction"), sheet.Stat("magicresist"));
        Line($"Attacker Takes Damage: {Of("item_attackertakesdamage")}  {Of("item_attackertakeslightdamage")}",
             sheet.Stat("item_attackertakesdamage"), sheet.Stat("item_attackertakeslightdamage"));
        Console.WriteLine();

        Line($"Elemental Mastery: {Of("passive_fire_mastery")}% {Of("passive_cold_mastery")}% {Of("passive_ltng_mastery")}% {Of("passive_pois_mastery")}% {Of("passive_mag_mastery")}%",
             sheet.Stat("passive_fire_mastery"), sheet.Stat("passive_cold_mastery"), sheet.Stat("passive_ltng_mastery"), sheet.Stat("passive_pois_mastery"), sheet.Stat("passive_mag_mastery"));
        Line($"Elemental Pierce: {Of("passive_fire_pierce")}% {Of("passive_cold_pierce")}% {Of("passive_ltng_pierce")}% {Of("passive_pois_pierce")}% {Of("passive_mag_pierce")}%",
             sheet.Stat("passive_fire_pierce"), sheet.Stat("passive_cold_pierce"), sheet.Stat("passive_ltng_pierce"), sheet.Stat("passive_pois_pierce"), sheet.Stat("passive_mag_pierce"));
        Line($"Physical Pierce: {Of("passive_phys_pierce")}%", sheet.Stat("passive_phys_pierce"));

        int attack = sheet.AttackFromDexterity.Value, gearAttack = Of("tohit");
        Line($"Base AR: dex: {attack} equip: {gearAttack} total: {attack + gearAttack}", sheet.AttackFromDexterity, sheet.Stat("tohit"));
        int defense = sheet.DefenseFromDexterity.Value, gearDefense = Of("armorclass");
        Line($"Base Def: dex: {defense} equip: {gearDefense} total: {defense + gearDefense}", sheet.Stat("armorclass"));
        Line($"Base Damage: 1h: {Of("mindamage")}-{Of("maxdamage")} 2h: {Of("secondary_mindamage")}-{Of("secondary_maxdamage")}   (thrown {Of("item_throw_mindamage")}-{Of("item_throw_maxdamage")})",
             sheet.Stat("mindamage"), sheet.Stat("maxdamage"), sheet.Stat("secondary_mindamage"), sheet.Stat("secondary_maxdamage"));
        Console.WriteLine();

        Line($"Cast Rate: {Of("item_fastercastrate")}   Block Rate: {Of("item_fasterblockrate")}", sheet.Stat("item_fastercastrate"), sheet.Stat("item_fasterblockrate"));
        Line($"Hit Recovery: {Of("item_fastergethitrate")}   Run/Walk: {Of("item_fastermovevelocity")}", sheet.Stat("item_fastergethitrate"), sheet.Stat("item_fastermovevelocity"));
        Line($"Attack Rate: {sheet.AttackRate.Value}   IAS: {Of("item_fasterattackrate")}", sheet.AttackRate, sheet.Stat("item_fasterattackrate"));
        Console.WriteLine();

        Console.WriteLine($"Breakpoints ({sheet.RightSkill?.Name ?? "Attack"}):   [left: {sheet.LeftSkill?.Name ?? "Attack"}]   weapon class {sheet.WeaponClass}");
        Console.WriteLine($"FCR: {Row(sheet.CastRateRow)}");
        Console.WriteLine($"FHR: {Row(sheet.HitRecoveryRow)}");
        Console.WriteLine($"FBR: {Row(sheet.BlockRateRow)}");
        Console.WriteLine($"IAS (Frames): {Row(sheet.AttackRow, frames: true)}   <{sheet.AttackRowNote}>");
        Console.WriteLine();

        Line($"Crushing Blow: {Of("item_crushingblow")} / {sheet.CrushingBlowEfficiency.Value}%   Open Wounds: {Of("item_openwounds")}%/+{sheet.OpenWoundsDamage.Value}",
             sheet.Stat("item_crushingblow"), sheet.Stat("item_openwounds"), sheet.OpenWoundsDamage);
        Line($"Deadly Strike: {Math.Min(Of("item_deadlystrike"), sheet.DeadlyStrikeCap)} / {sheet.DeadlyStrikeCap} / {sheet.DeadlyMultiplier.Value}%   Critical Strike: {Math.Min(sheet.CriticalStrike.Value, 75)} / 75 / {sheet.CriticalMultiplier.Value}%",
             sheet.Stat("item_deadlystrike"), sheet.CriticalStrike, sheet.CriticalMultiplier);
        Line($"Life Leech: {Of("lifedrainmindam")}   Mana Leech: {Of("manadrainmindam")}", sheet.Stat("lifedrainmindam"), sheet.Stat("manadrainmindam"));
        Line($"Projectile Pierce: {sheet.ProjectilePierce.Value}   HP/MP per Kill: {sheet.LifePerKill.Value} / {Of("item_manaafterkill")}",
             sheet.ProjectilePierce, sheet.LifePerKill, sheet.Stat("item_manaafterkill"));
        Console.WriteLine();

        var fire = sheet.Added("firemindam", "firemaxdam", "passive_fire_mastery");
        var cold = sheet.Added("coldmindam", "coldmaxdam", "passive_cold_mastery");
        var lightning = sheet.Added("lightmindam", "lightmaxdam", "passive_ltng_mastery");
        var magic = sheet.Added("magicmindam", "magicmaxdam", "passive_mag_mastery");
        Line($"Added Damage: {Of("item_normaldamage")}   magic {magic.Min}-{magic.Max}", sheet.Stat("magicmindam"), sheet.Stat("magicmaxdam"));
        Line($"fire {fire.Min}-{fire.Max}   cold {cold.Min}-{cold.Max}", sheet.Stat("firemindam"), sheet.Stat("firemaxdam"), sheet.Stat("coldmindam"), sheet.Stat("coldmaxdam"));
        var poison = sheet.Poison;
        Line(FormattableString.Invariant($"lightning {lightning.Min}-{lightning.Max}   poison {poison.Min}-{poison.Max} over {poison.Seconds:0.0}s"),
             sheet.Stat("lightmindam"), sheet.Stat("lightmaxdam"), sheet.Stat("poisonmindam"), sheet.Stat("poisonlength"));
        Line($"Magic Find: {Of("item_magicbonus")}   Gold Find: {Of("item_goldbonus")}", sheet.Stat("item_magicbonus"), sheet.Stat("item_goldbonus"));
        Console.WriteLine();

        if (sheet.Blocking is { } blocking)
            Line($"Chance to block: {sheet.Block}% (blocking {blocking.Value}; the most at dexterity {sheet.DexterityForMostBlock})", blocking);
        else
            Console.WriteLine("Chance to block: none (no shield)");
        Console.WriteLine();

        Console.WriteLine("Skills (level, hard points):");
        foreach (var skill in sheet.Skills.OrderBy(skill => skill.Id))
            Console.WriteLine($"  {skill.Id,4} {skill.Name,-28} {skill.Level,3}  ({skill.BaseLevel})");

        if (sheet.LeftOut.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Not counted (requirements not met):");
            foreach (var (item, why) in sheet.LeftOut)
                Console.WriteLine($"  {ItemPresenter.Describe(item).Name}: {why}");
        }

        return 0;
    }
}
