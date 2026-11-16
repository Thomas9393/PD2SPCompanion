using System.Text;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// How the game words a stat, so a number has some context.
/// <para>
/// The stat name and its storable range say what fits in the file; neither says what the
/// number means. <c>item_crit_multiplier</c> reading 0 to 127 is not much help until the
/// tables say it is worded as a percentage - at which point 15 is plainly 15% and 127 is
/// plainly nonsense. The phrasing comes from <c>descfunc</c>, <c>descval</c> and the
/// description string keys, resolved through the same string tables the game uses.
/// </para>
/// </summary>
public static class StatText
{
    private sealed record Form(
        int Func, int Val, string Positive, string Negative, string Second, int PerLevel = 0,
        int Encode = 0);

    private static Dictionary<string, Form> _forms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The description groups - <c>dgrp</c> - each with its wording and its members, in table
    /// order. See <see cref="Grouped"/>.
    /// </summary>
    private static List<(Form Form, List<string> Members)> _groups = [];

    public static void Load(GameData game)
    {
        var forms = new Dictionary<string, Form>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (game.Lines("itemstatcost") is { } lines)
            {
                string[] header = lines[0].Split('\t');

                int stat = Array.IndexOf(header, "Stat");
                int func = Array.IndexOf(header, "descfunc");
                int val = Array.IndexOf(header, "descval");
                int pos = Array.IndexOf(header, "descstrpos");
                int neg = Array.IndexOf(header, "descstrneg");
                int second = Array.IndexOf(header, "descstr2");

                // A stat that grows with the character keeps its per-level figure scaled
                // up, so the number in the file is not the number the game prints.
                //
                // **The shift is not the same for every one of them.** 33 of the 37 stats
                // whose `op base` is `level` shift by 3 (eighths); the four attack-rating
                // ones - item_tohit_perlevel, item_tohitpercent_perlevel, and the demon and
                // undead pair - shift by **1** (halves). An Eaglehorn storing
                // item_tohit_perlevel 12 prints +6 a level, which is 516 at level 86, and
                // assuming eighths would have printed 1. Read, never assumed.
                int basis = Array.IndexOf(header, "op base");
                int shift = Array.IndexOf(header, "op param");

                // How the stat packs itself, which is what says whether its two numbers are
                // the ends of a range or two different things.
                int packing = Array.IndexOf(header, "Encode");

                // The description group: stats said as one line when all of them are there with
                // one value - "All Resistances +10" for four resistances. Worded by columns shaped
                // exactly like the stat's own.
                int group = Array.IndexOf(header, "dgrp");
                int groupFunc = Array.IndexOf(header, "dgrpfunc");
                int groupVal = Array.IndexOf(header, "dgrpval");
                int groupPos = Array.IndexOf(header, "dgrpstrpos");
                int groupNeg = Array.IndexOf(header, "dgrpstrneg");
                int groupSecond = Array.IndexOf(header, "dgrpstr2");
                var groups = new Dictionary<int, (Form Form, List<string> Members)>();

                if (stat >= 0 && func >= 0 && pos >= 0)
                {
                    foreach (string line in lines.Skip(1))
                    {
                        string[] cells = line.Split('\t');
                        if (cells.Length <= pos || cells[stat].Trim().Length == 0)
                            continue;

                        string Cell(int index) =>
                            index >= 0 && cells.Length > index ? cells[index].Trim() : "";

                        int.TryParse(Cell(func), out int descfunc);
                        int.TryParse(Cell(val), out int descval);

                        int perLevel = 0;
                        if (Cell(basis).Equals("level", StringComparison.OrdinalIgnoreCase))
                            _ = int.TryParse(Cell(shift), out perLevel);

                        int.TryParse(Cell(packing), out int encode);

                        forms[cells[stat].Trim()] =
                            new Form(
                                descfunc, descval, Cell(pos), Cell(neg), Cell(second), perLevel,
                                encode);

                        if (int.TryParse(Cell(group), out int id) && id > 0)
                        {
                            if (!groups.TryGetValue(id, out var known))
                            {
                                int.TryParse(Cell(groupFunc), out int gf);
                                int.TryParse(Cell(groupVal), out int gv);
                                groups[id] = known = (new Form(gf, gv, Cell(groupPos), Cell(groupNeg), Cell(groupSecond)), []);
                            }

                            known.Members.Add(cells[stat].Trim());
                        }
                    }
                }

                _groups = [.. groups.Values];
            }
        }
        catch (Exception)
        {
            // Without the table a stat simply shows its own name, which is what it did
            // before any of this.
        }

        _forms = forms;
    }

    /// <summary>
    /// The <c>descfunc</c> a stat is worded by, or zero when the table words it not at all.
    /// It is what says the shape of the phrase - and for a few, that the parameter beside
    /// the value is a skill.
    /// </summary>
    public static int FunctionOf(string statName) =>
        _forms.TryGetValue(statName, out var form) ? form.Func : 0;

    /// <summary>
    /// Whether the line the game prints for a stat carries the stat's own number - and so whether
    /// it can carry a bracket. <c>descval 0</c> on the plain shapes (1 to 10) is the text alone:
    /// "Monsters have Cannot Be Frozen", "Knockback". Shape 11 prints a figure worked out from the
    /// number, "Repairs 1 durability in 33 seconds" for 3, and in game reads bare too (Warp Spell).
    /// The enhanced damage pair is <c>descval 0</c> as well, but said together with its
    /// number (see <see cref="EnhancedDamage"/>). Every other shape is left as it was - unmeasured.
    /// </summary>
    public static bool PrintsItsValue(string statName) =>
        !_forms.TryGetValue(statName, out var form)
        || form.Val != 0
        || form.Func is < 1 or > 11
        || EnhancedDamageStats.Contains(statName);

    /// <summary>
    /// How far a stat's stored number is shifted above its per-level figure, or zero for
    /// one that does not grow with the character. Three for every per-level stat in the
    /// table, which is to say the file keeps eighths.
    /// </summary>
    public static int PerLevelShift(string statName) =>
        _forms.TryGetValue(statName, out var form) ? form.PerLevel : 0;

    /// <summary>
    /// A stored number as a line says it: a per-level figure in its own units, eighths shown as a fraction where
    /// they are one - "0.5", not the 4 the file keeps - and any other number as it is. For a bracket after a
    /// line, which has to read in the line's units: "+0.5 to Life (Based on Character Level)" was followed by
    /// "[4 - 6]".
    /// </summary>
    public static string ShownNumber(string statName, int value)
    {
        int shift = PerLevelShift(statName);
        return shift > 0
            ? (value / (double)(1 << shift)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What the game would print this number as. A stat that grows with the character is
    /// stored multiplied - <c>item_thorns_perlevel 32</c> is four a level, not thirty-two -
    /// and the table says by how much, as a shift.
    /// </summary>
    private static int AsShown(Form form, int value) =>
        form.PerLevel > 0 ? value >> form.PerLevel : value;

    /// <summary>
    /// Whether a stat is a whole property packed into one row: a chance to cast, or a charged
    /// skill. Its number is one field of three, and the two the tables give it are not the
    /// ends of a range.
    /// </summary>
    public static bool IsPacked(string statName) =>
        _forms.TryGetValue(statName, out var form) && form.Encode is 2 or 3;

    /// <param name="level">
    /// The level a packed property casts or grants at. Worded "%d%% Chance to cast level %d
    /// %s", it has two numbers and one was being offered: filling both from the value said a
    /// suffix casting level 10 Nova fired a tenth of the time, when it fires three times in
    /// a hundred.
    /// </param>
    /// <param name="maxCharges">How many charges it holds, for a charged skill.</param>
    /// <summary>
    /// The damage types a skill can belong to, as <c>item_elemskill</c>'s parameter numbers
    /// them.
    /// <para>
    /// <b>This parameter is the one that does the work.</b> Stat 126 carries no wording at
    /// all - no <c>descfunc</c>, no <c>descstrpos</c> - and its partners 362 to 366 carry
    /// nothing but wording: rings holding only those printed their line and granted nothing.
    /// So the pair is a worker and a label, and 126 is the worker.
    /// </para>
    /// <para>
    /// <b>Fire is 1, measured.</b> A Leaf staff grants <c>fireskill</c> and holds 126 with a
    /// parameter of 1 beside <c>item_elemskill_fire</c>. The other four follow D2's own
    /// damage-type order, which fire at 1 agrees with, and are <b>not confirmed</b> - one
    /// item per element would settle them.
    /// </para>
    /// </summary>
    public static readonly string[] Elements =
        ["(none)", "Fire", "Lightning", "Magic", "Cold", "Poison"];

    /// <summary>
    /// The elemental damage runs, each leader to the stats said with it. A caller listing an
    /// item's properties words the leader through <see cref="ElementalDamage"/> and says
    /// nothing for the rest, because the game prints one line and not three.
    /// </summary>
    public static IReadOnlyDictionary<string, (string Max, string Length)> ElementalRuns { get; }
        = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["firemindam"] = ("firemaxdam", ""),
            ["lightmindam"] = ("lightmaxdam", ""),
            ["magicmindam"] = ("magicmaxdam", ""),
            ["coldmindam"] = ("coldmaxdam", "coldlength"),
            ["poisonmindam"] = ("poisonmaxdam", "poisonlength"),
        };

    /// <summary>
    /// The one line the game prints for a run of elemental damage stats, or null when these
    /// are not such a run.
    /// <para>
    /// The game shows <c>firemindam</c> and <c>firemaxdam</c> as a single "Adds 5-14 fire
    /// damage", never as two lines of "+5 to Minimum Fire Damage". Poison and cold carry a
    /// length as well, and <b>the stored numbers are not the numbers shown</b>: they are
    /// damage per frame in 256ths, so the total is <c>stored x length / 256</c>, and the
    /// duration is <c>length / 25</c> frames per second.
    /// </para>
    /// <para>
    /// Measured in game: a Hierophant Trophy holding <c>poisonmindam 16</c>,
    /// <c>poisonmaxdam 48</c>, <c>poisonlength 75</c> reads <b>"Adds 5-14 poison damage over
    /// 3 seconds"</b>. 16 x 75 / 256 = 4.69 rounds to 5, 48 x 75 / 256 = 14.06 rounds to 14,
    /// and 75 / 25 = 3. Until that was seen the editor printed the stored numbers, so a
    /// perfectly ordinary item read as "+16 to Minimum Poison Damage".
    /// </para>
    /// <para>
    /// <b>Only poison scales.</b> Cold carries a length in the same shape and it was tempting
    /// to treat it the same way - that guess was wrong, and a screenshot settled it: a Zenith
    /// storing <c>coldmindam 255</c> over <c>coldlength 100</c> reads "Adds 258-364 cold
    /// damage", which is the stored numbers plus what a socketed Thul adds. Cold's length is
    /// the chill duration, and the game never prints it. Fire, lightning and magic carry no
    /// length at all.
    /// </para>
    /// <para>
    /// What the game prints also includes whatever the <b>socketed</b> gems and runes add -
    /// the same Zenith shows 305-380 fire where 300-350 is stored, the difference being the
    /// 5-30 of a Ral. This words the numbers it is given; the tooltip gives it the item's with its
    /// sockets' counted in (<see cref="ItemLines"/>).
    /// </para>
    /// <para>
    /// <b>Poison whose two ends come to the same number is one number</b>: a Ruby Jewel of Envy in a
    /// spear reads "+20 poison damage over 2 seconds" in game, not "Adds 20-20" (a screenshot).
    /// Seen for poison only; the other elements are left a range until one is seen.
    /// </para>
    /// </summary>
    public static string? ElementalDamage(string statName, int min, int max, int length)
    {
        // frames per second, which is how every duration in the file is kept
        const int Frames = 25;

        // the divisor that turns damage per frame into the total over the duration
        const int PerFrame = 256;

        static int Over(int value, int length) =>
            (int)Math.Round(value * (double)length / PerFrame, MidpointRounding.AwayFromZero);

        string? Worded(string key) => StringTable.Current?.Get(key);

        switch (statName.ToLowerInvariant())
        {
            case "firemindam":
                return Fill(Worded("strModFireDamageRange"), min, max);

            case "lightmindam":
                return Fill(Worded("strModLightningDamageRange"), min, max);

            case "magicmindam":
                return Fill(Worded("strModMagicDamageRange"), min, max);

            // Cold keeps a length too, and it is tempting to scale by it the way poison
            // does. It does not: a Zenith storing coldmindam 255 with coldlength 100 reads
            // "Adds 258-364 cold damage" in game, which is 255 and 350 plus the 3-14 a Thul
            // adds - stored straight, and the length never enters it. Cold's length is how
            // long the chill lasts, not a share of the damage, and the game does not print
            // it at all. Poison is the exception, not the rule.
            case "coldmindam":
                return Fill(Worded("strModColdDamageRange"), min, max);

            case "poisonmindam":
                if (length <= 0)
                    return null;

                return Over(min, length) == Over(max, length) && Worded("strModPoisonDamage") is { Length: > 0 } single
                    ? Fill(single, Over(min, length), length / Frames)
                    : Fill(Worded("strModPoisonDamageRange"),
                           Over(min, length), Over(max, length), length / Frames);

            default:
                return null;
        }
    }

    /// <summary>
    /// The physical damage stats, said as the game says them: one line, not six.
    /// <para>
    /// A <c>dmg-min</c> or <c>dmg-max</c> writes the same number to three stats - one-handed,
    /// two-handed and thrown damage - on a jewel, a charm or an amulet, and to the one matching a
    /// weapon's own damage on a weapon (measured on 180 real items: 20 carry all three equal, a
    /// claw only <c>maxdamage</c>, a bow only <c>secondary_maxdamage</c>). The game shows one of
    /// them, and when a minimum and a maximum are both there, one line for the pair - the PD2
    /// wiki quotes Gull, stored 1 and 15, as "Adds 1-15 Damage", and Shatterblade's minimum alone
    /// as "+25 to Minimum Damage".
    /// </para>
    /// <para>
    /// Read as the one-handed copy, else the two-handed, else the thrown: they are equal wherever
    /// one property wrote them all, and where they are not the game reads the one-handed too -
    /// Raven Scarab stores 5 / 3 / 3 and reads "+5 to Maximum Damage [1 - 15]" in game.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> PhysicalDamageStats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mindamage", "secondary_mindamage", "item_throw_mindamage",
        "maxdamage", "secondary_maxdamage", "item_throw_maxdamage",
    };

    /// <summary>
    /// The line or lines for an item's physical damage, each with the stat whose range brackets
    /// it - none for the pair, which the game prints with no bracket, as it does the elements.
    /// </summary>
    /// <param name="held">An item's value for a stat, or null where it has none.</param>
    public static IReadOnlyList<(string Text, string? RangeOf)> PhysicalDamage(Func<string, int?> held)
    {
        static (int Value, string Stat)? First(Func<string, int?> held, params string[] names)
        {
            foreach (string name in names)
            {
                if (held(name) is { } value)
                    return (value, name);
            }

            return null;
        }

        var min = First(held, "mindamage", "secondary_mindamage", "item_throw_mindamage");
        var max = First(held, "maxdamage", "secondary_maxdamage", "item_throw_maxdamage");

        // Trimmed: PD2's strings carry the odd stray line break - "Enhanced Damage" ends in one.
        string? Worded(string key) => StringTable.Current?.Get(key)?.Trim();

        // Only a minimum at or under the maximum reads as a pair: arrows with 14 and 2 say "+14 to Minimum Damage [1 - 14]"
        // and "+2 to Maximum Damage [1 - 18]" (a screenshot).
        if (min is { } low && max is { } high && low.Value <= high.Value)
        {
            string? pair = low.Value == high.Value
                ? Fill(Worded("strModMinDamage"), low.Value)
                : Fill(Worded("strModMinDamageRange"), low.Value, high.Value);

            return [(pair ?? $"Adds {low.Value}-{high.Value} damage", null)];
        }

        var lines = new List<(string, string?)>();
        if (min is { } alone)
            lines.Add(($"{Signed(alone.Value)} {Worded("ModStr1g") ?? "to Minimum Damage"}", alone.Stat));
        if (max is { } top)
            lines.Add(($"{Signed(top.Value)} {Worded("ModStr1f") ?? "to Maximum Damage"}", top.Stat));

        return lines;
    }

    /// <summary>
    /// Where the game puts a stat's line among an item's properties: <c>descpriority</c>, the
    /// highest first. Raven Scarab reads Faster Hit Recovery (139), Maximum Damage (126), Life
    /// (59), Cold Resist (40) in game, in that order, whatever order the item stores them in.
    /// Nought for a stat the table gives none.
    /// </summary>
    public static int PriorityOf(string stat) =>
        int.TryParse(D2SLib.Core.TXT.ItemStatCostTXT[stat]?["descpriority"]?.Value, out int priority)
            ? priority
            : 0;

    /// <summary>The enhanced damage pair, said as the game says it.</summary>
    public static readonly IReadOnlySet<string> EnhancedDamageStats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "item_maxdamage_percent", "item_mindamage_percent",
    };

    /// <summary>
    /// The line or lines for enhanced damage. A <c>dmg%</c> writes the maximum and the minimum
    /// alike, and the game prints the pair as one "+X% Enhanced Damage" - the PD2 wiki's daggers
    /// read "+[70-90]% Enhanced Damage". Apart, each says its own with its number: the table words
    /// them with no value at all (<c>descval 0</c>), which is why they read "Enhanced Maximum
    /// Damage" and nothing else before.
    /// </summary>
    public static IReadOnlyList<(string Text, string? RangeOf)> EnhancedDamage(Func<string, int?> held)
    {
        int? most = held("item_maxdamage_percent"), least = held("item_mindamage_percent");
        string? Worded(string key) => StringTable.Current?.Get(key)?.Trim();

        if (most is { } both && least == both)
            return [($"{Signed(both)}% {Worded("strModEnhancedDamage") ?? "Enhanced Damage"}", "item_maxdamage_percent")];

        var lines = new List<(string, string?)>();
        if (most is { } upper)
            lines.Add(($"{Signed(upper)}% {Worded("ModStr2j") ?? "Enhanced Maximum Damage"}", "item_maxdamage_percent"));
        if (least is { } lower)
            lines.Add(($"{Signed(lower)}% {Worded("ModStr2k") ?? "Enhanced Minimum Damage"}", "item_mindamage_percent"));

        return lines;
    }

    /// <summary>Puts numbers into a game string's <c>%d</c> slots, in order.</summary>
    private static string? Fill(string? pattern, params int[] numbers)
    {
        if (pattern is not { Length: > 0 })
            return null;

        foreach (int number in numbers)
        {
            int at = pattern.IndexOf("%d", StringComparison.Ordinal);
            if (at < 0)
                break;

            pattern = pattern[..at] + number + pattern[(at + 2)..];
        }

        return pattern;
    }

    /// <summary>
    /// The skill a stat's line names: the one its skill field holds - a chance to cast, a charged
    /// skill - or, for an aura, the one its <b>parameter</b> holds. An Infinity stores
    /// <c>item_aura</c> 12 with 123 as the parameter and reads "Level 12 Conviction Aura When
    /// Equipped" (the PD2 trade site's own line for one); asked by the skill field alone, every
    /// aura on every item read "(none)".
    /// </summary>
    public static int SkillOf(string statName, int? skillId, int? param) =>
        skillId is > 0 ? skillId.Value
        : FunctionOf(statName) == AuraFunction ? param ?? 0
        : 0;

    /// <summary>The descfunc of an aura: "Level %d %s Aura When Equipped".</summary>
    private const int AuraFunction = 16;

    /// <summary>
    /// How the game words the three stats whose parameter picks one of a set of strings,
    /// or null for anything else.
    /// <para>
    /// <c>descstrpos</c> names only the first of each set - <c>StrSklTabItem1</c> is the
    /// Amazon's first tree, <c>ModStr3a</c> the Amazon herself - and the game walks from
    /// there by the parameter. Worded from the key alone, every skill tree in the game read
    /// as "Javelin and Spear Skills" whatever the item actually held.
    /// </para>
    /// <para>
    /// Built rather than walked, because that numbering is the string table's own - 1-3
    /// Amazon, 4-6 Paladin, 7-9 Necromancer - and does not follow the order a save numbers
    /// classes in.
    /// </para>
    /// </summary>
    public static string? ByParameter(string statName, int value, int param)
    {
        string plus = value >= 0 ? $"+{value}" : value.ToString();

        // The two a skill is named in with no string of their own: the game composes them.
        // "+3 to Cyclone Armor" is an Infinity's item_nonclassskill 3 on 235 (the trade site
        // words it so); a class's skill carries the class after it, as "+3 to Battle Cry
        // (Barbarian Only)". Worded until now only for the search's templates, so a real item
        // holding one printed the stat's bare name.
        if (FunctionOf(statName) is 27 or 28
            && SkillNames.Current?.All.FirstOrDefault(skill => skill.Id == param) is { } granted)
        {
            return FunctionOf(statName) == 27 && granted.ClassName.Length > 0
                ? $"{plus} to {granted.Name} ({granted.ClassName} Only)"
                : $"{plus} to {granted.Name}";
        }

        switch (statName)
        {
            // The parameter is two fields in one number: the class three bits above the
            // tree it names.
            case "item_addskill_tab":
                int charClass = (param >> 3) & 0x1fff;

                if (SkillTabs.Name(charClass, param & 0x7) is not { Length: > 0 } tree)
                    return null;

                return SkillTabs.ClassOf(charClass) is { Length: > 0 } who
                    ? $"{plus} to {tree} ({who} Only)"
                    : $"{plus} to {tree}";

            case "item_addclassskills":
                return SkillTabs.ClassOf(param) is { Length: > 0 } named
                    ? $"{plus} to {named} Skill Levels"
                    : null;

            // A chance and the monster it raises. The string is "Reanimate as: %s" - a slot
            // for the name and nowhere for the number - so the percentage goes in front of
            // it, which is descval 1 and is how the game prints it: "10% Reanimate as:
            // Burning Dead". Read off an item in game.
            case "item_reanimate":
                if (MonsterNames.NameOf(param) is not { } beast)
                    return null;

                string phrase = Describe(statName, value, beast) ?? "Reanimate as:";

                // descfunc 23 is a phrase with the monster put after it by the game, not a
                // slot to fill - the string really is "Reanimate as:" and nothing more - so
                // the name goes on the end unless the wording already carried it.
                if (!phrase.Contains(beast, StringComparison.OrdinalIgnoreCase))
                    phrase = $"{phrase} {beast}";

                return $"{value}% {phrase}";

            // Nothing words a state: no descfunc, no string, and a single bit for a value.
            // PD2 uses them for the auras a set paints on, so the internal name is the only
            // name there is.
            case "state":
                return StateNames.NameOf(param) is { } wearing
                    ? $"State: {wearing} ({(value != 0 ? "On" : "Off")})"
                    : null;

            // Worded here although the game words it nowhere, because the parameter is
            // what grants the skills: an item carrying only the partner that has the string
            // prints its line and does nothing. A row showing "item_elemskill 3" would hide
            // the one field on it that matters.
            case "item_elemskill":
                return param >= 0 && param < Elements.Length
                    ? $"{plus} to {Elements[param]} Skills"
                    : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// The wording with the number left open: "+X to Strength" rather than "+1 to Strength".
    /// What a property <b>is</b>, for a list of properties to choose from, where any one
    /// number would be a number somebody has to ignore.
    /// </summary>
    public static string? Template(string statName) =>
        // A dye worded at 1 is "White tint", which is one colour and not the property.
        string.Equals(statName, Dyes.Stat, StringComparison.OrdinalIgnoreCase) ? "Dye (colour)"
        : Charged(statName, Describe(statName, 1, subject: SkillSlot, numeral: Placeholder))
        ?? Composed(statName)
        ?? UnwordedLabels.GetValueOrDefault(statName);

    /// <summary>
    /// A stat the tables word not at all, said with <b>our</b> words and its number -
    /// "+5 Maximum Durability" - rather than as its bare name. Null for any other stat.
    /// </summary>
    public static string? Unworded(string statName, int value) =>
        UnwordedLabels.TryGetValue(statName, out string? label)
            ? System.Text.RegularExpressions.Regex.Replace(label, @"\bX\b", value.ToString())
            : null;

    /// <summary>
    /// A charged skill's string is only the tail - "(%d/%d Charges)" - and the game puts the
    /// level and the skill in front of it. <see cref="Describe"/> does that when it is given a
    /// level; a template has none, so it is done here, once, for every list that shows one.
    /// </summary>
    private static string? Charged(string statName, string? text) =>
        text is { Length: > 0 } && _forms.TryGetValue(statName, out var form) && form.Encode == 3
            ? $"Level {Placeholder} {SkillSlot} {text}"
            : text;

    /// <summary>What names the skill in a template, where the row picks one: "(skill)".</summary>
    private const string SkillSlot = "(skill)";

    /// <summary>
    /// The two the game words in code rather than from a string - descfunc 27 and 28 have no
    /// string of their own - in the shape the game prints them: "+3 to Blizzard (Sorceress
    /// Only)", "+3 to Teleport".
    /// </summary>
    private static string? Composed(string statName) => FunctionOf(statName) switch
    {
        27 => $"+{Placeholder} to {SkillSlot} (Class Only)",
        28 => $"+{Placeholder} to {SkillSlot}",
        _ => null,
    };

    /// <summary>
    /// <b>Our words, not the game's</b>, for the properties an item can be given that the tables
    /// word not at all - written by a <c>properties.txt</c> row, and with no <c>descfunc</c> or
    /// string. Measured: 19 such in S13, two of them the composed pair above. Named by
    /// what the property that writes them does (its code, and <c>*desc</c> where it has one).
    /// <para>
    /// The trade site was checked first: its labels are the game's own
    /// strings, by the same codes - identical on all 362 stats we word, and absent on every one
    /// of these. So there was nothing to take from it, and these had to be written.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> UnwordedLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["velocitypercent"] = "+X% Velocity",
        ["maxdurability"] = "+X Maximum Durability",
        ["item_timeduration"] = "Lasts X (Timed Item)",
        ["item_doubleherbduration"] = "Double Herb Duration",
        ["item_lightcolor"] = "Light Color X",
        ["item_levelreq"] = "Required Level X",
        ["state"] = "State X",
        ["item_elemskill"] = "+X to Elemental Skills (No Line of Its Own)",
        ["item_extrablood"] = "Extra Blood",
        ["item_throw_mindamage"] = "+X to Minimum Throw Damage",
        ["item_throw_maxdamage"] = "+X to Maximum Throw Damage",
        ["fade"] = "Fade (Monsters Only)",
        ["item_numsockets"] = "Sockets (X)",
        ["item_absorb_pois_perlevel"] = "+X Absorbs Poison Damage (Based on Character Level)",
        ["item_absorb_pois_bytime"] = "+X Absorbs Poison Damage (Increases Over Time)",
        ["map_mon_coldlength"] = "Map Monsters: Cold Length X",
        ["map_mon_poisonlength"] = "Map Monsters: Poison Length X",
        ["transform_dye"] = "Dye X",
        ["es_efficiency"] = "+X Energy Shield Efficiency",
    };

    /// <summary>What stands in for the number in a template.</summary>
    private const string Placeholder = "X";

    /// <summary>
    /// The stat as the game would word it, or null when the tables do not word it at all -
    /// which is normal for the ones whose text is assembled from a skill or a class rather
    /// than from a single string.
    /// </summary>
    public static string? Describe(
        string statName, int value, string? subject = null,
        int? level = null, int? maxCharges = null, string? numeral = null)
    {
        // A tint prints nothing in game; the editor says which colour.
        if (string.Equals(statName, Dyes.Stat, StringComparison.OrdinalIgnoreCase))
            return Dyes.Describe(value);

        // Trimmed: a few of the game's strings carry a space of their own - StrAnniversaryEdition
        // is " %dth Anniversary Edition" - which the game centres away and a list does not.
        return _forms.TryGetValue(statName, out var form)
            ? DescribeForm(form, value, subject, level, maxCharges, numeral)?.Trim()
            : null;
    }

    /// <summary>
    /// Stats the game says as one line: every member of a description group (<c>dgrp</c>) held,
    /// all with one value. Four resistances at 10 are "All Resistances +10", four attributes at 5
    /// "+5 to all Attributes" - as the PD2 wiki's craft chart reads Brilliant's grants, and as the
    /// game's tooltip does. Anything short of the whole group, or unequal, is said stat by stat.
    /// </summary>
    /// <param name="held">An item's value for a stat, or null where it has none.</param>
    public static IReadOnlyList<(string Text, IReadOnlyList<string> Stats)> Grouped(Func<string, int?> held)
    {
        var said = new List<(string, IReadOnlyList<string>)>();

        foreach (var (form, members) in _groups)
        {
            var values = members.Select(held).ToList();
            if (values.Any(value => value is null) || values.Distinct().Count() != 1)
                continue;

            if (DescribeForm(form, values[0]!.Value, null, null, null, null) is { Length: > 0 } text)
                said.Add((text, members));
        }

        return said;
    }

    /// <summary>
    /// <see cref="Grouped"/> for a list of ranges rather than an item: a group whose members all
    /// roll the same range is said once, with the range as its number - "All Resistances +5-10".
    /// </summary>
    public static IReadOnlyList<(string Text, IReadOnlyList<string> Stats)> GroupedRanges(
        IReadOnlyDictionary<string, (int Min, int Max)> spans)
    {
        var said = new List<(string, IReadOnlyList<string>)>();

        foreach (var (form, members) in _groups)
        {
            var ranges = members.Select(member => spans.TryGetValue(member, out var span) ? span : ((int, int)?)null).ToList();
            if (ranges.Any(span => span is null) || ranges.Distinct().Count() != 1)
                continue;

            var (min, max) = ranges[0]!.Value;
            string numeral = min == max ? $"{min}" : $"{min}-{max}";

            if (DescribeForm(form, max, null, null, null, numeral) is { Length: > 0 } text)
                said.Add((text, members));
        }

        return said;
    }

    private static string? DescribeForm(
        Form form, int value, string? subject, int? level, int? maxCharges, string? numeral)
    {
        // Shape 11 is one point of durability every 100 / value seconds, in a string of its own whatever the row
        // names: Purgatory's 25 reads "Repairs 1 durability in 4 seconds" in game (a screenshot)
        // and Warp Spell's 3 "in 33 seconds", where the row's "Repairs %d durability per second" put
        // 25 a second. Above 100 the game's figure is unseen, and the row's string stands.
        if (form.Func == 11 && numeral is null && value is > 0 and <= 100
            && StringTable.Current?.Get(RepairsInSeconds) is { Length: > 0 } repairs && repairs != RepairsInSeconds)
        {
            return FillNumbers(repairs, [1, 100 / value], value);
        }

        // A per-level figure is kept in eighths, and not every one is whole: an Infinity's
        // item_vitality_perlevel 4 is half a point a level, which the trade site says as "+0.5
        // to Vitality (Based on Character Level)". Shifted to a whole number it read "+0".
        if (form.PerLevel > 0 && numeral is null && value % (1 << form.PerLevel) != 0)
        {
            numeral = (value / (double)(1 << form.PerLevel))
                .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        value = AsShown(form, value);

        string key = value < 0 && form.Negative.Length > 0 ? form.Negative : form.Positive;
        if (key.Length == 0)
            return null;

        string text = StringTable.Current?.Get(key) ?? "";
        if (text.Length == 0 || text == key)
            return null;

        // A form written with placeholders is filled rather than shown raw. "Level %d %s
        // Aura When Equipped" is not a sentence until the level and the skill are in it,
        // and no amount of descval arithmetic puts them there.
        if (text.Contains('%'))
        {
            text = numeral is { Length: > 0 } open
                ? FillNumbers(text, [], 0, open)
                : FillNumbers(text, Numbers(form, value, level, maxCharges), value);

            // Nothing to name yet - a property picked and not yet pointed at a skill -
            // reads better as a gap than as the placeholder itself.
            text = text.Replace("%s", subject is { Length: > 0 } named ? named : "(none)");

            // A doubled percent is the table's escape for a literal one, and left alone it
            // reaches the screen as "10%% Chance to cast". Done last, so it cannot eat the
            // percent of a placeholder that has not been filled yet.
            text = text.Replace("%%", "%");

            // A charged skill's string is only the tail of the sentence - "(%d/%d Charges)"
            // and nothing else - and the game puts the level and the skill in front of it.
            // Filled as an ordinary two-number form it read "(1/-20 Charges)", the level
            // where the charges belong.
            if (form.Encode == 3 && level is { } at)
                text = $"Level {at} {(subject is { Length: > 0 } who ? who : "(none)")} {text}";

            return Upward(text);
        }

        // descfunc says the shape the number takes; descval says which side it sits on.
        // Only the forms that are a number and a phrase are worded here - the rest name a
        // skill or a class, which is said beside the row instead.
        // The descfunc numbering is D2's own, and the shapes that differ only by whether
        // a second string follows are listed together. 20 and 21 are the negated pair -
        // item_fractionaltargetac holds 25 and the game says "-25% Target Defense" - and
        // leaving them out dropped the number entirely.
        string number = Shaped(form.Func, numeral ?? Numeral(form.Func, value));

        string said = number.Length == 0
            ? text
            : form.Val switch
            {
                0 => text,
                2 => $"{text} {number}",
                _ => $"{number} {text}",
            };

        // Six to ten are the same shapes with a second string after the first - and after
        // the number too, which is the whole point of it: "Attacker Takes Damage of 4-6
        // (Based on Character Level)" says something the number alone does not.
        return Tailed(form, said);
    }

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

    /// <summary>
    /// The numbers a form's placeholders take, in the order the string puts them. A chance to
    /// cast leads with the chance and then the level; a charged skill leads with the level and
    /// ends with the charges it has and the charges it holds.
    /// </summary>
    private static int[] Numbers(Form form, int value, int? level, int? maxCharges) =>
        form.Encode == 3
            ? [value, maxCharges ?? value]
            : (level, maxCharges) switch
            {
                ({ } at, { } most) => [at, value, most],
                ({ } at, null) => [value, at],
                _ => [],
            };

    /// <summary>
    /// Fills a form's number placeholders one after another, rather than every one of them
    /// with the same number. Leaves <c>%%</c> alone, since that is the table's escape for a
    /// literal percent and is unescaped last of all.
    /// </summary>
    private static string FillNumbers(
        string text, IReadOnlyList<int> numbers, int only, string? open = null)
    {
        var built = new StringBuilder(text.Length + 8);

        int at = 0, next = 0;

        int Take() => next < numbers.Count ? numbers[next++] : only;

        while (at < text.Length)
        {
            if (text[at] == '%' && at + 1 < text.Length)
            {
                if (text[at + 1] == '%')
                {
                    built.Append("%%");
                    at += 2;
                    continue;
                }

                if (text[at + 1] == 'd')
                {
                    built.Append(open ?? Take().ToString());
                    at += 2;
                    continue;
                }

                if (text[at + 1] == '+' && at + 2 < text.Length && text[at + 2] == 'd')
                {
                    built.Append(open is { Length: > 0 } ? $"+{open}" : Signed(Take()));
                    at += 3;
                    continue;
                }
            }

            built.Append(text[at]);
            at++;
        }

        return built.ToString();
    }

    /// <summary>
    /// The number a form carries, before the shape goes round it. Two of the functions
    /// negate and one scales, and those belong to the number rather than to the wording -
    /// which is what lets a range be worded with the same shapes as a value.
    /// </summary>
    private static string Numeral(int func, int value) => func switch
    {
        5 or 10 => (value * 100 / 128).ToString(),
        20 or 21 => (-value).ToString(),
        _ => value.ToString(),
    };

    /// <summary>
    /// The shape a form puts round its number: a sign, a percent, or neither. Takes the
    /// numeral as text so that "50-80" wears the same shape "80" would.
    /// </summary>
    private static string Shaped(int func, string numeral) => func switch
    {
        1 or 6 or 12 => numeral.StartsWith('-') ? numeral : $"+{numeral}",
        2 or 7 or 5 or 10 => $"{numeral}%",
        3 or 9 or 19 or 21 => numeral,
        4 or 8 => numeral.StartsWith('-') ? $"{numeral}%" : $"+{numeral}%",
        20 => $"{numeral}%",
        _ => "",
    };

    /// <summary>
    /// A property worded with the range it rolls in rather than with one value: "+50-80%
    /// Enhanced Defense". What a corruption or an affix <b>is</b>, as against what one
    /// instance of it happens to hold - showing the top of a range as if it were the value
    /// says a glove has 80 when it has 51.
    /// </summary>
    public static string? DescribeRange(string statName, int min, int max, string? subject = null)
    {
        if (min == max)
            return Describe(statName, min, subject);

        if (!_forms.TryGetValue(statName, out var form))
            return null;

        // Both ends, so a per-level stat reads as the four to six a level it really is
        // rather than the thirty-two to forty-eight the file keeps.
        min = AsShown(form, min);
        max = AsShown(form, max);

        string key = form.Positive;
        if (key.Length == 0)
            return null;

        string text = StringTable.Current?.Get(key) ?? "";
        if (text.Length == 0 || text == key)
            return null;

        // A placeholder form has nowhere to put two numbers; the value form answers.
        //
        // Unless the two were never the ends of a range: a packed property keeps the chance
        // in one and the level it casts at in the other, and read as a range it printed the
        // level as the chance as well.
        if (text.Contains('%'))
        {
            return IsPacked(statName)
                ? Describe(statName, min, subject, level: max)
                : Describe(statName, max, subject);
        }

        // Two negatives share one sign: "-10-20% Target Defense", as the wiki's chart puts
        // "-[10-20]%", not "-10--20%".
        string low = Numeral(form.Func, min), high = Numeral(form.Func, max);
        string span = low.StartsWith('-') && high.StartsWith('-') ? $"{low}-{high[1..]}" : $"{low}-{high}";

        string number = Shaped(form.Func, span);

        string said = number.Length == 0
            ? text
            : form.Val switch
            {
                0 => text,
                2 => $"{text} {number}",
                _ => $"{number} {text}",
            };

        return Tailed(form, said);
    }

    /// <summary>
    /// The second string, where the form carries one. Without it a per-level stat reads as
    /// flat damage - four to six is four to six <i>a level</i>, and nothing else on the
    /// line says so.
    /// </summary>
    private static string Tailed(Form form, string said) => Upward(
        form.Func is >= 6 and <= 10
        && StringTable.Current?.Get(form.Second) is { Length: > 0 } tail
        && tail != form.Second
        && Slotless(tail) is { Length: > 0 } words
            ? $"{said} {words}"
            : said);

    /// <summary>The string the game words a repair with: "Repairs %d durability in %d seconds".</summary>
    private const string RepairsInSeconds = "ModStre9u";

    /// <summary>
    /// A line of several as the game draws them, from the bottom up - the last of a string's lines on top, as a
    /// skill's description is (SkillTips). Purgatory's second string is "(based on Equipped Ethereals)" and then
    /// "Gains +60% Enhanced Damage per equipped ethereal item", and the game puts the Gains line above the
    /// enhanced damage (a screenshot), where the editor put it under.
    /// </summary>
    private static string Upward(string text) =>
        text.Contains('\n') ? string.Join('\n', text.Split('\n').Select(line => line.Trim()).Reverse()) : text;

    /// <summary>
    /// A second string with a number slot of its own - the maps' magic damage borrows "+%d magic
    /// damage" - says the number the line already has, so the slot goes and the words stay:
    /// "Monsters have +5 magic damage", not "Monsters have +5 +%d magic damage".
    /// </summary>
    private static string Slotless(string tail) =>
        System.Text.RegularExpressions.Regex.Replace(tail, @"[+-]?%d\s*", "").Trim();
}
