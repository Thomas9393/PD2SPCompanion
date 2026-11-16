namespace PD2SPCompanion.Core.Items;

/// <summary>
/// Skill id to name, from the game's own skills.txt. Item stats such as
/// <c>item_singleskill</c> store a skill id as their parameter, so this is what turns
/// "param=254" into "Tiger Strike".
/// </summary>
public sealed class SkillNames
{
    private readonly Dictionary<int, (string Name, string CharClass, int Required)> _byId = [];

    /// <summary>
    /// The <b>internal</b> name each id answers to - <c>skills.txt</c>'s own <c>skill</c>
    /// column, not the name the game prints.
    /// <para>
    /// Needed because <c>uniqueitems.txt</c> and <c>setitems.txt</c> may write a skill into a
    /// <c>par</c> column <b>by name</b> rather than by id, and the name they write is the
    /// internal one: <c>teeth</c>, <c>combustion</c>, <c>Chilling Armor SelfAura</c>. All 141
    /// such rows resolve off this column and none off the printed name, which for a druid's
    /// Poison Creeper is not even the same word - the table calls it <c>Plague Poppy</c>.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, int> _byInternalName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The id a skill's internal name stands for, or null where nothing does. Case does not
    /// matter; the tables are inconsistent about it.
    /// </summary>
    public static int? IdOf(string name) =>
        name.Length > 0 && Current is { } current
        && current._byInternalName.TryGetValue(name.Trim(), out int id)
            ? id
            : null;

    /// <summary>
    /// The ids that are really auras.
    /// <para>
    /// <c>skills.txt</c> flags fifty with <c>aura</c>, and four of them are the masteries -
    /// Javelin and Spear, Sword, One Hand and Mace - which the game implements as auras
    /// without being any such thing. The table separates them itself: a projected aura
    /// carries an <c>aurafilter</c> saying who it reaches, and a mastery has none, holding
    /// a <c>passivestate</c> instead. Requiring the filter drops exactly those four.
    /// </para>
    /// </summary>
    private readonly HashSet<int> _auras = [];

    /// <summary>The ids that sit somewhere on a skill tree. See <see cref="TreePages"/>.</summary>
    private readonly HashSet<int> _onTree = [];

    public static SkillNames? Current { get; private set; }

    /// <summary>The skill a stored id names, or null where nothing does.</summary>
    public static string? NameOf(int id) =>
        id > 0 ? Current?.All.FirstOrDefault(skill => skill.Id == id)?.Name : null;

    /// <summary>The skill's <c>reqlevel</c>, or 0 where the skill is unknown.</summary>
    public static int RequiredLevelOf(int id) =>
        Current is { } current && current._byId.TryGetValue(id, out var skill) ? skill.Required : 0;

    /// <summary>Whether the skill has a cooldown (<c>delay</c> above 0): Blaze, Meteor, Blizzard.</summary>
    public static bool HasCooldown(int id) =>
        Current is { } current && current._delayById.TryGetValue(id, out int delay) && delay > 0;

    private readonly Dictionary<int, int> _delayById = [];

    public static SkillNames Load(GameData game)
    {
        var (table, _) = game.ReadExcel("skills");
        var names = new SkillNames();
        var described = Described(game);

        // The id column is "Id" in PD2's table; older layouts use "*Id".
        string idColumn = table.IndexOfColumn("Id") >= 0 ? "Id" : "*Id";
        for (int row = 0; row < table.Rows.Count; row++)
        {
            string id = table.Value(row, idColumn).Trim();
            string skill = table.Value(row, "skill").Trim();
            if (int.TryParse(id, out int value) && skill.Length > 0)
            {
                // Kept before the printed name replaces it below - a row writing a skill by
                // name writes this one.
                names._byInternalName.TryAdd(skill, value);

                // The name the game prints, which is not the name the table keeps: the
                // druid's Poison Creeper is "Plague Poppy" in skills.txt. skilldesc carries
                // the string key and the string tables carry the wording, so the internal
                // name is only the fallback for a skill nothing words.
                string described_key = table.Value(row, "skilldesc").Trim();
                if (described.TryGetValue(described_key, out var entry)
                    && Shown(entry.Name) is { Length: > 0 } printed)
                {
                    skill = printed;
                }

                // reqlevel is what gates a staff mod: PD2 needs the item's level to reach
                // the skill's required level plus seven before it can roll.
                _ = int.TryParse(table.Value(row, "reqlevel").Trim(), out int required);
                names._byId.TryAdd(
                    value, (skill, table.Value(row, "charclass").Trim(), required));
                if (int.TryParse(table.Value(row, "delay").Trim(), out int delay))
                    names._delayById.TryAdd(value, delay);

                if (described.TryGetValue(described_key, out var where) && where.Page > 0)
                    names._onTree.Add(value);

                if (table.Value(row, "aura").Trim() is { Length: > 0 } aura && aura != "0"
                    && table.Value(row, "aurafilter").Trim().Length > 0)
                {
                    names._auras.Add(value);
                }
            }
        }

        names.All =
        [
            .. names._byId
                .Select(entry => new SkillOption(
                    entry.Key,
                    entry.Value.Name,
                    entry.Value.CharClass,
                    ClassNameOf(entry.Value.CharClass),
                    entry.Value.CharClass.Length > 0
                        ? $"{entry.Value.Name} ({Shorthand(entry.Value.CharClass)})"
                        : entry.Value.Name,
                    entry.Value.Required,
                    names._onTree.Contains(entry.Key),
                    names._auras.Contains(entry.Key)))
                .OrderBy(skill => skill.CharClass)
                .ThenBy(skill => skill.Id),
        ];

        names.Group();

        Current = names;
        return names;
    }

    /// <summary>
    /// Every skill the game has, for choosing between them. A skill is stored as a number
    /// and nobody knows the numbers, so anywhere one can be set it is set from this.
    /// <para>
    /// Ordered by class and then by id, which puts a class's own skills together and in the
    /// order its tree lists them; the classless ones come first.
    /// </para>
    /// </summary>
    public IReadOnlyList<SkillOption> All { get; private set; } = [];

    /// <summary>The skill's name, with its class when it has one, or null if unknown.</summary>
    public string? Describe(int id)
    {
        if (!_byId.TryGetValue(id, out var skill))
            return null;
        return skill.CharClass.Length > 0 ? $"{skill.Name} ({skill.CharClass})" : skill.Name;
    }

    /// <summary>
    /// Every skill, in eight sections: one per class in the order a save numbers them, and
    /// a last one for the skills that belong to no class. Seven hundred-odd skills in a
    /// single list is not something anyone can pick from.
    /// </summary>
    public IReadOnlyList<SkillGroup> Groups { get; private set; } = [];

    /// <summary>
    /// Only the auras, in the same sections. What an item grants "when equipped" is an
    /// aura and nothing else, so offering seven hundred skills there is offering the wrong
    /// question. <c>skills.txt</c> flags them itself, which is why this is a filter rather
    /// than a list: the masteries it counts as auras are counted here too, because the
    /// game counts them.
    /// </summary>
    public IReadOnlyList<SkillGroup> Auras { get; private set; } = [];

    private void Group()
    {
        var groups = new List<SkillGroup>();

        for (int classId = 0; classId < CharacterClasses.Codes.Count; classId++)
        {
            string code = CharacterClasses.Codes[classId];
            // A class's own skills, less the ones that are in the tables but not in the
            // game. PD2 reserved twenty-one ids for added skills, 364 to 384, seven a row
            // one per class, and has filled thirteen of them; the rest are named
            // "amatemp2", "drutemp3" and so on and describe themselves as Attack. Two that
            // look real - Shattering Arrow and Blade Dance - are data for a season that
            // has not come. All eight sit on no skill tree page, and the current season's
            // loot filter matches on exactly the other thirteen, which is the same answer
            // from a second direction.
            var mine = All.Where(skill =>
                skill.CharClass.Equals(code, StringComparison.OrdinalIgnoreCase)
                && skill.OnTree).ToList();

            if (mine.Count > 0)
                groups.Add(new SkillGroup(CharacterClasses.Names[classId], mine));
        }

        // Everything with no class of its own: monster skills, item procs and the rest.
        var rest = All.Where(skill => skill.CharClass.Length == 0
            || !CharacterClasses.Codes.Contains(skill.CharClass, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (rest.Count > 0)
            groups.Add(new SkillGroup(L("Other"), rest));

        Groups = groups;

        Auras =
        [
            .. groups
                .Select(group => new SkillGroup(
                    group.Name,
                    [.. group.Skills.Where(skill => skill.IsAura)]))
                .Where(group => group.Skills.Count > 0),
        ];
    }

    /// <summary>
    /// The class code as we care to show it. The tables use "ass" for the Assassin, which
    /// is the game's own spelling and not one worth repeating down a list.
    /// </summary>
    private static string Shorthand(string code) =>
        code.Equals("ass", StringComparison.OrdinalIgnoreCase) ? "asn" : code;

    /// <summary>
    /// The class a skills.txt code stands for, spelled out. The tables abbreviate - "ass",
    /// "sor" - which is fine in a list already grouped by class and no use in a sentence.
    /// </summary>
    public static string ClassNameOf(string code) => CharacterClasses.NameOfCode(code) ?? "";

    /// <summary>
    /// Which skill-tree page each description sits on, by its <c>skilldesc</c> key.
    /// <para>
    /// <c>SkillPage 0</c> means the skill appears on no tree, so no character can learn it:
    /// every monster and proc skill is like this, and so is a class skill the mod has
    /// reserved an id for and not yet written.
    /// </para>
    /// </summary>
    private static Dictionary<string, (int Page, string Name)> Described(GameData game)
    {
        var found = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var (table, _) = game.ReadExcel("skilldesc");

            for (int row = 0; row < table.Rows.Count; row++)
            {
                string key = table.Value(row, "skilldesc").Trim();
                if (key.Length == 0)
                    continue;

                _ = int.TryParse(table.Value(row, "SkillPage").Trim(), out int page);
                found.TryAdd(key, (page, table.Value(row, "str name").Trim()));
            }
        }
        catch (Exception)
        {
            // Without the table nothing is filtered and nothing is renamed, which is the
            // old behaviour: the internal names, and every skill treated as on a tree.
        }

        return found;
    }

    /// <summary>
    /// What a string key resolves to, or nothing where it resolves to itself - which is what
    /// the table hands back for a key it does not carry.
    /// </summary>
    private static string Shown(string key)
    {
        if (key.Length == 0 || StringTable.Current?.Get(key) is not { Length: > 0 } text)
            return "";

        return text == key ? "" : text;
    }

    /// <summary>The option for one id, or null when the tables do not have it.</summary>
    public SkillOption? Option(int id) => All.FirstOrDefault(skill => skill.Id == id);
}

/// <summary>One section of the skill picker: a class, or everything else.</summary>
public sealed record SkillGroup(string Name, IReadOnlyList<SkillOption> Skills);

/// <summary>One skill, as something to pick.</summary>
/// <param name="RequiredLevel">
/// The character level the skill itself needs, from <c>reqlevel</c>. A staff mod needs the
/// item to be seven levels above it.
/// </param>
/// <param name="OnTree">
/// Whether the skill sits on a skill tree at all. One that does not cannot be learned - it
/// is a monster skill, an item proc, or an id the mod has reserved and not yet filled.
/// </param>
/// <param name="IsAura">
/// Whether `skills.txt` flags it an aura, which is what an item can grant when equipped.
/// </param>
public sealed record SkillOption(
    int Id, string Name, string CharClass, string ClassName, string Display,
    int RequiredLevel = 0, bool OnTree = false, bool IsAura = false)
{
    public override string ToString() => Display;
}
