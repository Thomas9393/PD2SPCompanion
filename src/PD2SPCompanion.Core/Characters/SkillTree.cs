namespace PD2SPCompanion.Core.Characters;

/// <summary>One skill, where it sits in its tree and what it needs.</summary>
public sealed record SkillEntry(
    int Id,
    string Name,
    int Page,
    int Row,
    int Column,
    int RequiredLevel,
    IReadOnlyList<int> Requires,
    int IconCel,
    string? Description,
    IReadOnlyList<string> RequiresNames,
    int MaxLevel = 20)
{
    /// <summary>Index into the save's thirty skill bytes.</summary>
    public int Slot { get; init; }
}

/// <summary>One of the three pages of a class's skills.</summary>
public sealed record SkillPage(int Number, string Name, IReadOnlyList<SkillEntry> Skills);

/// <summary>
/// A class's skill trees, laid out from the game's own tables.
/// <para>
/// The save stores thirty skill bytes per class at a fixed id offset, so slot and id are the
/// same thing counted differently. Where each one belongs on screen comes from
/// <c>skilldesc.txt</c>: page one to three, row one to six, column one to three - the same
/// three-by-six grid the game draws.
/// </para>
/// <para>
/// Skills at row or column zero are not on any page. That is how PD2 retires a skill without
/// disturbing the id space: the Amazon's Avoid and the Barbarian's separate Sword, Mace and
/// Spear masteries are all still in the file and none of them are in the game. They are left
/// out here for the same reason the game leaves them out.
/// </para>
/// </summary>
public sealed class SkillTree
{
    /// <summary>The first skill id of each class, as the save orders them.</summary>
    private static readonly int[] ClassOffsets = [6, 36, 66, 96, 126, 221, 251];

    /// <summary>
    /// How many skills a class stores. PD2 adds three to each on top of vanilla's thirty -
    /// the Paladin's Holy Nova, Holy Light and Joust - and they are kept in the save's skill
    /// section like any other.
    /// </summary>
    public const int SkillsPerClass = 33;

    public const int Rows = 6;
    public const int Columns = 3;

    public IReadOnlyList<SkillPage> Pages { get; private init; } = [];

    /// <summary>Every skill that has a place, by slot.</summary>
    public IReadOnlyDictionary<int, SkillEntry> BySlot { get; private init; } =
        new Dictionary<int, SkillEntry>();

    public bool IsAvailable => Pages.Count > 0;

    /// <summary>
    /// The slot of the skill that keeps an Iron Golem - the one whose <c>pettype</c> in skills.txt is
    /// <c>irongolem</c> (the Necromancer's IronGolem, id 90) - or null for a class with none. The save
    /// keeps the golem and the item it was made of (D2S.Golem), and the game will not load one whose
    /// skill holds no point: "bad hireables" (after a respec here).
    /// </summary>
    public int? IronGolemSlot { get; private init; }

    /// <summary>
    /// That skill's id, whatever the class: an item can give it to anyone (PD2's Metalgrid: <c>oskill
    /// IronGolem</c>), and a golem made so stands in the save as a Necromancer's does. Null where the
    /// table names no such skill.
    /// </summary>
    public int? IronGolemId { get; private init; }

    public static SkillTree Empty { get; } = new();

    public static SkillTree For(GameData game, byte classId)
    {
        try
        {
            // Read only, so the kept copies (GameData.Excel): read afresh, these were every load's 30 ms.
            var skills = game.Excel("skills");
            var descriptions = game.Excel("skilldesc");
            var chars = game.Excel("charstats");
            var strings = StringTable.Current ?? StringTable.Load(game);

            int offset = classId < ClassOffsets.Length ? ClassOffsets[classId] : ClassOffsets[0];
            string classCode = CharacterClasses.CodeOf(classId) ?? CharacterClasses.Codes[0];

            // skilldesc rows, keyed the way skills.txt refers to them.
            int descKey = descriptions.IndexOfColumn("skilldesc");
            var byKey = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in descriptions.Rows)
            {
                string key = Cell(row, descKey);
                if (key.Length > 0)
                    byKey.TryAdd(key, row);
            }

            int idColumn = skills.IndexOfColumn("Id");
            var byId = new Dictionary<int, string[]>();
            foreach (var row in skills.Rows)
            {
                if (int.TryParse(Cell(row, idColumn), out int id))
                    byId.TryAdd(id, row);
            }

            // Which skill each of the save's bytes holds.
            //
            // Vanilla's thirty run consecutively from the class's base id, but PD2's three
            // additions sit far above them - the Paladin's are 364, 371 and 378 - so the
            // order is taken from the table: every skill of this class, by id. The first
            // thirty are the original block and the last three are the additions.
            int classColumn = skills.IndexOfColumn("charclass");
            var slots = skills.Rows
                .Where(r => Cell(r, classColumn).Equals(classCode, StringComparison.OrdinalIgnoreCase))
                .Select(r => int.TryParse(Cell(r, idColumn), out int id) ? id : -1)
                .Where(id => id >= 0)
                .OrderBy(id => id)
                .ToList();

            // Without a usable list, fall back to the consecutive block, which at least
            // gets vanilla's thirty right.
            if (slots.Count < SkillsPerClass)
                slots = Enumerable.Range(offset, SkillsPerClass).ToList();

            string DisplayName(string[] skill)
            {
                string raw = Cell(skill, skills.IndexOfColumn("skill"));
                return byKey.TryGetValue(Cell(skill, skills.IndexOfColumn("skilldesc")), out var d)
                    ? strings.Get(Cell(d, descriptions.IndexOfColumn("str name"))) ?? raw
                    : raw;
            }

            var namesById = new Dictionary<int, string>();
            foreach (int id in slots)
            {
                if (byId.TryGetValue(id, out var any))
                    namesById[id] = DisplayName(any);
            }

            var entries = new List<SkillEntry>();
            for (int slot = 0; slot < slots.Count; slot++)
            {
                int id = slots[slot];
                if (!byId.TryGetValue(id, out var skill))
                    continue;

                if (!byKey.TryGetValue(Cell(skill, skills.IndexOfColumn("skilldesc")), out var description))
                    continue;

                int page = Number(description, descriptions.IndexOfColumn("SkillPage"));
                int row = Number(description, descriptions.IndexOfColumn("SkillRow"));
                int column = Number(description, descriptions.IndexOfColumn("SkillColumn"));

                // Retired by the mod, so not shown.
                if (page is < 1 or > 3 || row is < 1 or > Rows || column is < 1 or > Columns)
                    continue;

                string name = DisplayName(skill);

                int firstRequirement = skills.IndexOfColumn("reqskill1");
                var requires = new List<int>();
                for (int n = 0; n < 3 && firstRequirement >= 0; n++)
                {
                    string required = Cell(skill, firstRequirement + n);
                    if (required.Length > 0 && required != "0")
                        requires.Add(IdOfNamed(byId, idColumn, skills.IndexOfColumn("skill"), required));
                }

                var needs = requires.Where(r => r > 0).ToList();

                entries.Add(new SkillEntry(
                    Id: id,
                    Name: name,
                    Page: page,
                    Row: row,
                    Column: column,
                    RequiredLevel: Number(skill, skills.IndexOfColumn("reqlevel")),
                    Requires: needs,
                    IconCel: Number(description, descriptions.IndexOfColumn("IconCel")),
                    // A short line of prose. The fuller description is assembled from a calc
                    // language this does not speak, and a wrong number would be worse than
                    // none, so only the plain sentence is taken.
                    Description: strings.Get(Cell(description, descriptions.IndexOfColumn("str short"))),
                    RequiresNames: needs.Select(r => namesById.TryGetValue(r, out var n) ? n : L($"skill {r}"))
                                        .ToList(),
                    // The most hard points the game lets the skill take - 20 for every real one.
                    MaxLevel: Number(skill, skills.IndexOfColumn("maxlvl")) is > 0 and var most ? most : 20)
                {
                    Slot = slot,
                });
            }

            if (entries.Count == 0)
                return Empty;

            var classRow = chars.Rows.FirstOrDefault(
                r => Cell(r, 0).Equals(CharacterClasses.NameOf(classId) ?? CharacterClasses.Names[0], StringComparison.OrdinalIgnoreCase));

            var pages = new List<SkillPage>();
            for (int page = 1; page <= 3; page++)
            {
                string? key = classRow is null
                    ? null
                    : Cell(classRow, chars.IndexOfColumn($"StrSkillTab{page}"));

                pages.Add(new SkillPage(
                    Number: page,
                    Name: strings.Tab(key) ?? L($"Tree {page}"),
                    Skills: entries.Where(e => e.Page == page)
                                   .OrderBy(e => e.Row).ThenBy(e => e.Column)
                                   .ToList()));
            }

            int petColumn = skills.IndexOfColumn("pettype");
            int golemSlot = slots.FindIndex(id => byId.TryGetValue(id, out var row)
                                                  && Cell(row, petColumn).Equals("irongolem", StringComparison.OrdinalIgnoreCase));

            int golemId = byId.Where(pair => Cell(pair.Value, petColumn).Equals("irongolem", StringComparison.OrdinalIgnoreCase))
                              .Select(pair => pair.Key)
                              .DefaultIfEmpty(-1)
                              .Min();

            return new SkillTree
            {
                Pages = pages,
                BySlot = entries.ToDictionary(e => e.Slot),
                IronGolemSlot = golemSlot >= 0 ? golemSlot : null,
                IronGolemId = golemId >= 0 ? golemId : null,
            };
        }
        catch (Exception)
        {
            // Without the tables there is no tree to draw; the page says so rather than
            // inventing one.
            return Empty;
        }
    }

    private static string Cell(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index] : "";

    private static int Number(string[] fields, int index) =>
        int.TryParse(Cell(fields, index), out int value) ? value : 0;

    /// <summary>Prerequisites are given by name, so they have to be looked back up.</summary>
    private static int IdOfNamed(
        Dictionary<int, string[]> byId, int idColumn, int nameColumn, string name)
    {
        foreach (var (id, row) in byId)
        {
            if (Cell(row, nameColumn).Equals(name, StringComparison.OrdinalIgnoreCase))
                return id;
        }
        return 0;
    }
}
