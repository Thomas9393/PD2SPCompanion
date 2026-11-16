using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Items;

/// <summary>What a property's row asks for beside its numbers: nothing, a skill, or an aura.</summary>
public enum SkillPick { None, Skill, Aura }

/// <summary>
/// One property the vault search can ask for, as the trade site's Property picker offers them.
/// <paramref name="Key"/> is what <see cref="PropertyIndex.ValuesOf"/> stores it under; an option
/// that picks a skill is a family of keys, <c>stat{skill}</c>.
/// </summary>
public sealed record PropertyOption(string Key, string Label, SkillPick Picks = SkillPick.None) : IPropertyChoice
{
    public string Text => Label;

    public string Note => "";

    public bool HasNote => false;

    /// <summary>A composite of several stats, or one per parameter: no single id to show.</summary>
    public string IdText => "";

    /// <summary>A map's alone - see <see cref="StatCatalogue.IsMapOnly"/>.</summary>
    public bool IsMapOnly => StatCatalogue.IsMapOnly(Key);

    public override string ToString() => Label;
}

/// <summary>
/// The search's properties: the list to pick from, and the number each item has for each.
/// <para>
/// Modelled on the trade site's Property picker, read off its code (see the vault
/// design note). Every worded stat is an option; the four stats that name a skill or a tree are
/// one option per parameter ("+X to Blizzard (Sorceress Only)"); chance-to-cast, charges and
/// auras are one option with a skill picked beside it; and five composites stand in for what
/// is written as several stats - All Resistances, All Attributes, Minimum and Maximum Damage,
/// Enhanced Damage.
/// </para>
/// <para>
/// <b>What an item has, for now:</b> its own list and its runeword's. Not the set bonuses, which
/// hold only with other pieces worn, and not what is socketed - deliberately the
/// item only, first.
/// </para>
/// </summary>
public static class PropertyIndex
{
    public const string AllResist = "all_resist";
    public const string AllAttributes = "all_attributes";
    public const string MinDamage = "min_damage";
    public const string MaxDamage = "max_damage";
    public const string EnhancedDamage = "enhanced_damage";

    /// <summary>
    /// Flat damage is written three times - one-handed, two-handed and thrown - by one property,
    /// so asking any one of them would miss items the other two describe. Measured on the vault:
    /// every item with one had all three.
    /// </summary>
    private static readonly string[] MaxDamageStats = ["maxdamage", "secondary_maxdamage", "item_throw_maxdamage"];
    private static readonly string[] MinDamageStats = ["mindamage", "secondary_mindamage", "item_throw_mindamage"];
    private static readonly string[] Resists = ["fireresist", "coldresist", "lightresist", "poisonresist"];
    private static readonly string[] Attributes = ["strength", "dexterity", "vitality", "energy"];

    /// <summary>The stats offered one option per parameter.</summary>
    private static readonly HashSet<string> Expanded =
        ["item_addclassskills", "item_addskill_tab", "item_singleskill", "item_nonclassskill"];

    /// <summary>The stats offered only through a composite, never bare.</summary>
    private static readonly HashSet<string> Folded =
        [.. MaxDamageStats, .. MinDamageStats, "item_maxdamage_percent", "item_mindamage_percent"];

    private const string Aura = "item_aura";

    private static IReadOnlyList<PropertyOption>? _options;

    /// <summary>The install the list was built under (<see cref="GameFiles.InstalledTables"/>): built again under another.</summary>
    private static int _builtAt = -1;

    /// <summary>
    /// Every property the search offers, by label. The wording and the skills come from the
    /// tables, so it is kept only once they are loaded: asked earlier - the vault's panel is
    /// built while the app starts - it would have kept the five composites and nothing else,
    /// which is exactly what the picker first showed.
    /// </summary>
    public static IReadOnlyList<PropertyOption> Options
    {
        get
        {
            if (_options is { } kept && _builtAt == GameFiles.InstalledTables.Generation)
                return kept;

            var built = Build();
            if (StatCatalogue.All.Count > 0 && SkillNames.Current is not null)
            {
                _options = built;
                _builtAt = GameFiles.InstalledTables.Generation;
            }

            return built;
        }
    }

    private static IReadOnlyList<PropertyOption> Build()
    {
        var found = new List<PropertyOption>
        {
            new(AllResist, L("All Resistances +X%")),
            new(AllAttributes, L("+X to All Attributes")),
            new(MinDamage, L("+X to Minimum Damage")),
            new(MaxDamage, L("+X to Maximum Damage")),
            new(EnhancedDamage, L("+X% Enhanced Damage")),
        };

        foreach (var choice in StatCatalogue.All)
        {
            string name = choice.Name;

            if (Expanded.Contains(name) || Folded.Contains(name) || StatCatalogue.IsObsolete(name)
                || StatCatalogue.IsEngineOnly(choice.Id)
                || name.StartsWith("mon_", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_bytime", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var picks = PicksOf(name);

            // The skill is picked beside the row, so the wording names it generically.
            string? label = picks == SkillPick.Aura
                ? StatText.Template(name)?.Replace("(skill)", "(aura)")
                : StatText.Template(name);
            if (label is not { Length: > 0 })
                continue;

            found.Add(new PropertyOption(name, Relabelled(name) ?? label, picks));

            // The second of a run - the maximum after the minimum - is written with the first and is
            // not on the catalogue's list, but an item holds its number like any other's. The site
            // offers the five maximums (its list), so the search does too.
            foreach (int follower in StatCatalogue.RunFrom(choice.Id).Skip(1))
            {
                if (StatCatalogue.NameOf(follower) is { } next
                    && next.EndsWith("maxdam", StringComparison.OrdinalIgnoreCase)
                    && (Relabelled(next) ?? StatText.Template(next)) is { Length: > 0 } nextLabel)
                {
                    found.Add(new PropertyOption(next, nextLabel));
                }
            }
        }

        // The four that name a class, a tree or one skill: an option each, as the site has them.
        foreach (var (stat, param, label) in ByParameter())
            found.Add(new PropertyOption(KeyOf(stat, param), label));

        return [.. found
            .GroupBy(option => option.Key).Select(same => same.First())
            .OrderBy(option => Sortable(option.Label), StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The class, the tree or the skill each of the four <see cref="Expanded"/> stats names, one entry
    /// apiece, worded as the game prints them - the site's list has one line for each (read off its
    /// code: 7 classes, 21 trees, then every skill twice). The search makes its options of
    /// them and the item panel's picker its choices, so the two lists read alike.
    /// </summary>
    public static IEnumerable<(string Stat, int Param, string Label)> ByParameter()
    {
        for (int cls = 0; cls < 7; cls++)
        {
            if (StatText.ByParameter("item_addclassskills", 1, cls) is { } classLabel)
                yield return ("item_addclassskills", cls, Numeral(classLabel));

            for (int tab = 0; tab < 3; tab++)
            {
                int packed = (cls << 3) | tab;
                if (StatText.ByParameter("item_addskill_tab", 1, packed) is { } tabLabel)
                    yield return ("item_addskill_tab", packed, Numeral(tabLabel));
            }
        }

        foreach (var skill in ClassSkills())
        {
            yield return ("item_singleskill", skill.Id, L($"+X to {skill.Name} ({skill.ClassName} Only)"));
            yield return ("item_nonclassskill", skill.Id, L($"+X to {skill.Name} (oskill)"));
        }
    }

    /// <summary>Whether a stat is offered one line per parameter (<see cref="ByParameter"/>).</summary>
    public static bool IsByParameter(string stat) => Expanded.Contains(stat);

    /// <summary>
    /// Our words where the game's say the same for two options: its string for both magic damage
    /// stats is "+%d magic damage", so minimum and maximum read as the other four elements' do.
    /// </summary>
    private static string? Relabelled(string stat) => stat.ToLowerInvariant() switch
    {
        "magicmindam" => L("+X to Minimum Magic Damage"),
        "magicmaxdam" => L("+X to Maximum Magic Damage"),
        _ => null,
    };

    /// <summary>
    /// The skills a class can learn, one each - what the "+X to a skill" options are made from.
    /// The ones on no tree are monster and proc skills no item names this way, and so are the ones
    /// with no class: a monster's Meteor borrows the Sorceress's description, tree and all, and
    /// read "+X to Meteor ( Only)" - 120 such lines, which the site leaves out too.
    /// </summary>
    public static IEnumerable<SkillOption> ClassSkills() =>
        (SkillNames.Current?.All ?? [])
            .Where(skill => skill.OnTree && skill.ClassName.Length > 0)
            .GroupBy(skill => skill.Id).Select(same => same.First())
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>What a skill-picking row offers: every named skill, or only the auras.</summary>
    public static IEnumerable<SkillOption> SkillsFor(SkillPick picks) =>
        (SkillNames.Current?.All ?? [])
            .Where(skill => skill.Name.Length > 0 && (picks != SkillPick.Aura || skill.IsAura))
            .GroupBy(skill => skill.Id).Select(same => same.First())
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase);

    private static SkillPick PicksOf(string name) =>
        name.Equals(Aura, StringComparison.OrdinalIgnoreCase) ? SkillPick.Aura
        : StatFields.EncodingOf(name) is 2 or 3 ? SkillPick.Skill
        : SkillPick.None;

    /// <summary>A stat and its parameter, as one key: <c>item_singleskill{59}</c>.</summary>
    public static string KeyOf(string stat, int param) => $"{stat}{{{param}}}";

    /// <summary>The start every key of a skill-picking option shares, for "any skill".</summary>
    public static string FamilyOf(string stat) => $"{stat}{{";

    /// <summary>"+1 to Fire Skills" read as "+X to Fire Skills", the way the other labels read.</summary>
    private static string Numeral(string label) => label.Replace("+1 ", "+X ");

    /// <summary>
    /// Sorted by the words, not by the sign and the placeholder in front of them. The item panel's
    /// property picker sorts by it too, so the two lists read alike.
    /// </summary>
    public static string Sortable(string label) =>
        label.TrimStart('+', '-', 'X', '%', ' ', '#');

    /// <summary>
    /// The number an item has for each property it carries, keyed as <see cref="Options"/> are.
    /// <para>
    /// The number is the one the row's Min and Max are compared against: a chance to cast is its
    /// chance, a charged skill its level, an aura its level, and a per-level stat what it gives
    /// a level - the table's eighths shifted down - never the stored ratio. The same key twice,
    /// as a runeword's list beside its base's can hold, is summed, as the game sums them.
    /// </para>
    /// <para>
    /// <b>With what its sockets give counted in</b>, as its tooltip counts them (<see cref="ItemLines"/>):
    /// a spear with two
    /// Shael runes has 40 under attack speed, and a search for at least 40 finds it. The item's own alone
    /// until then, so a property row and the text box beside it answered differently.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, double> ValuesOf(Item item)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var stat in ItemLines.WithSockets(item, OwnStats(item)))
        {
            if (Place(stat) is { } placed)
                values[placed.Key] = values.GetValueOrDefault(placed.Key) + placed.Value;
        }

        // The composites. All of a set, or not at all: "All Resistances 30" means each of
        // the four is at least 30, so the least of them is the number.
        if (Resists.All(values.ContainsKey))
            values[AllResist] = Resists.Min(each => values[each]);
        if (Attributes.All(values.ContainsKey))
            values[AllAttributes] = Attributes.Min(each => values[each]);
        if (MaxDamageStats.Any(values.ContainsKey))
            values[MaxDamage] = MaxDamageStats.Where(values.ContainsKey).Max(each => values[each]);
        if (MinDamageStats.Any(values.ContainsKey))
            values[MinDamage] = MinDamageStats.Where(values.ContainsKey).Max(each => values[each]);
        if (values.TryGetValue("item_maxdamage_percent", out double enhanced))
            values[EnhancedDamage] = enhanced;

        return values;
    }

    /// <summary>
    /// Where one stat is counted: its key, the option that offers it - the stat itself for one that
    /// picks a skill beside the row - the skill picked there, and its number in the row's units. See
    /// <see cref="ValuesOf"/>, which sums these; the trading page's search starts from them too.
    /// </summary>
    public static (string Key, string Option, int? Skill, double Value)? Place(ItemStat stat)
    {
        if (stat.Stat is not { Length: > 0 } name)
            return null;

        int encoding = StatFields.EncodingOf(name);
        bool picks = encoding is 2 or 3 || name.Equals(Aura, StringComparison.OrdinalIgnoreCase);

        double value = encoding switch
        {
            2 => stat.Value,
            3 => stat.SkillLevel ?? 0,
            _ => StatText.PerLevelShift(name) is > 0 and var shift
                ? stat.Value / (double)(1 << shift)
                : stat.Value,
        };

        if (picks)
        {
            int skill = encoding is 2 or 3 ? stat.SkillId ?? 0 : stat.Param ?? 0;
            return (KeyOf(name, skill), name, skill, value);
        }

        if (Expanded.Contains(name))
        {
            string key = KeyOf(name, stat.Param ?? 0);
            return (key, key, null, value);
        }

        return (name, name, null, value);
    }

    /// <summary>A key split back into its stat and the parameter in braces, if it has one.</summary>
    public static (string Stat, int? Param) Split(string key)
    {
        int brace = key.IndexOf('{');
        if (brace < 0)
            return (key, null);

        string stat = key[..brace];
        return int.TryParse(key.AsSpan(brace + 1).TrimEnd('}'), out int param) ? (stat, param) : (stat, null);
    }

    /// <summary>The four resistances and the four attributes, each set folded into one composite.</summary>
    public static IReadOnlyList<string> ResistStats => Resists;

    public static IReadOnlyList<string> AttributeStats => Attributes;

    public static IReadOnlyList<string> MaxDamageKinds => MaxDamageStats;

    public static IReadOnlyList<string> MinDamageKinds => MinDamageStats;

    public static bool IsAura(string stat) => stat.Equals(Aura, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The item's own list and its runeword's. The runeword's is the last list, after any a set
    /// item carries - and a set item is never a runeword, so there is nothing to tell apart.
    /// </summary>
    private static IEnumerable<ItemStat> OwnStats(Item item)
    {
        if (item.StatLists.Count == 0)
            return [];

        var own = item.StatLists[0].Stats ?? [];

        return item.IsRuneword && item.StatLists.Count > 1
            ? own.Concat(item.StatLists[^1].Stats ?? [])
            : own;
    }
}
