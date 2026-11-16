using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// A new character written on a template save: what the game writes for one it has just made, and
/// the quests, waypoints and vitals of one that has played. Shared by the armory import, which fills
/// in the armory's character, and character creation, which fills in the user's choices.
/// </summary>
public static class NewCharacter
{
    /// <summary>
    /// The item era a save's items are stamped with - the version its first item carries, else
    /// the season's own. Read before <see cref="Begin"/>, which empties the lists: a save holds one.
    /// </summary>
    public static string ItemEra(D2S save) =>
        FirstStamp(save) ?? SeasonReading.CurrentStamp ?? "101";

    /// <summary>
    /// The stamp a new item takes on this character (<see cref="ItemSeason.ForNew"/>): this season's wherever the file
    /// may hold it - a new item is never a legacy one - else the file's. Null for a character holding no item in a
    /// season whose stamp is unknown.
    /// </summary>
    public static string? StampForNew(D2S save) =>
        FirstStamp(save) is { } first ? ItemSeason.ForNew(first) : SeasonReading.CurrentStamp;

    private static string? FirstStamp(D2S save) =>
        (save.PlayerItemList?.Items ?? []).Select(item => item.Version).FirstOrDefault(version => version is { Length: > 0 });

    /// <summary>
    /// The header and every part that is the template's character, made the new one's: its name,
    /// class, level and mode; nothing held, worn, dead or hired; no statistics. <paramref name="finished"/>
    /// is every quest done - the title, and Hell as the last difficulty played - else a character
    /// that has done nothing, in Normal.
    /// </summary>
    public static void Begin(D2S save, string name, byte classId, int level, bool hardcore, bool expansion, bool finished)
    {
        save.Name = name;
        save.ClassId = classId;
        save.Level = (byte)Math.Clamp(level, 1, 99);
        save.Status.IsHardcore = hardcore;
        save.Status.IsExpansion = expansion;
        save.Status.IsLadder = false;
        save.Status.IsDead = false;

        // Every quest done: Patriarch or Matriarch in the expansion, as a save with every quest done reads.
        // Nothing done: no title, as a level 1 character reads.
        save.Progression = !finished ? (byte)0 : expansion ? (byte)15 : (byte)12;

        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        save.Created = now;
        save.LastPlayed = now;
        save.MapId = (uint)Random.Shared.NextInt64(1, uint.MaxValue);

        // What the game writes for a character it has just made (BB): no hotkeys, attack
        // on both buttons, and no appearance - it is filled in by the game's next save.
        foreach (var hotkey in save.AssignedSkills)
            hotkey.Id = 0xFFFF;
        save.LeftSkill.Id = 0;
        save.RightSkill.Id = 0;
        save.LeftSwapSkill.Id = 0;
        save.RightSwapSkill.Id = 0;
        foreach (var part in typeof(Appearances).GetProperties().Where(p => p.PropertyType == typeof(Appearance)))
        {
            if (part.GetValue(save.Appearances) is Appearance look)
            {
                look.Graphic = 0xFF;
                look.Tint = 0xFF;
            }
        }

        save.Location.Normal.Active = !finished;
        save.Location.Normal.Act = 1;
        save.Location.Nightmare.Active = false;
        save.Location.Nightmare.Act = 1;
        save.Location.Hell.Active = finished;
        save.Location.Hell.Act = 1;

        // PD2's statistics - time played, kills, deaths, bosses - are the template's, in the `st`
        // block of the trailer (PlayerStats): a new character has played nothing. The blocks after it
        // are the same bytes in a fresh save (BB) and a played one (SpearZon), so they stay.
        for (int at = 0; at + PlayerStats.Length + 2 <= save.Trailer.Length; at++)
        {
            if (save.Trailer[at] == (byte)'s' && save.Trailer[at + 1] == (byte)'t'
                && save.Trailer[at + PlayerStats.Length] == (byte)'h' && save.Trailer[at + PlayerStats.Length + 1] == (byte)'t')
            {
                Array.Clear(save.Trailer, at + 2, PlayerStats.Length - 2);
                break;
            }
        }

        // Nothing held, no corpse, no golem, no mercenary.
        save.PlayerItemList.Items.Clear();
        save.PlayerItemList.Count = 0;
        save.PlayerCorpses.Corpses.Clear();
        save.PlayerCorpses.Count = 0;
        if (save.Golem is not null)
        {
            save.Golem.Exists = false;
            save.Golem.Item = null!;
        }

        save.Mercenary.IsDead = 0;
        save.Mercenary.Id = 0;
        save.Mercenary.NameId = 0;
        save.Mercenary.TypeId = 0;
        save.Mercenary.Experience = 0;
        if (save.MercenaryItemList?.ItemList is { } hired)
        {
            hired.Items.Clear();
            hired.Count = 0;
        }

        // ---- quests and waypoints -----------------------------------------------------------------
        if (finished)
        {
            CompleteQuests(save.Quests, expansion);
            foreach (var difficulty in WaypointProgress.Read(save))
                difficulty.EnableAll();
        }
        else
        {
            ClearQuests(save.Quests);

            // Only the Rogue Encampment's, in each difficulty, as level 1 saves read.
            foreach (var difficulty in WaypointProgress.Read(save))
            {
                for (int act = 0; act < difficulty.Acts.Count; act++)
                {
                    foreach (var point in difficulty.Acts[act].Points)
                        point.IsFound = act == 0 && point.IsTown;
                }
            }
        }

        // Every skill at nought: the new character spends its own points.
        var header = save.ClassSkills.Header;
        save.ClassSkills = ClassSkills.Read(new byte[ClassSkills.SectionLength], classId);
        save.ClassSkills.Header = header;
    }

    /// <summary>What quests done give that stays with the character: points and life, all three difficulties.</summary>
    public sealed record QuestGifts(int StatPoints, int SkillPoints, int Life)
    {
        public static QuestGifts None { get; } = new(0, 0, 0);

        /// <summary>
        /// Every reward taken, three difficulties: Lam Esen's 5 stat points; the Den of Evil's,
        /// Radament's and Izual's 1 + 1 + 2 skill points; the Golden Bird's 20 life - QuestProgress.Gifts.
        /// </summary>
        public static QuestGifts All { get; } = new(
            3 * QuestProgress.Gifts.Values.Sum(gift => gift.StatPoints),
            3 * QuestProgress.Gifts.Values.Sum(gift => gift.SkillPoints),
            3 * QuestProgress.Gifts.Values.Sum(gift => gift.Life));
    }

    /// <summary>
    /// Base life, mana and stamina, charstats.txt exactly (measured on BB and SpearZon): life = vit +
    /// hpadd + (level - 1) x LifePerLevel/4 + (vitality - vit) x LifePerVitality/4 + the quests' life;
    /// stamina and mana the same way with their own columns.
    /// </summary>
    public static (int Life, int Mana, int Stamina) Vitals(GameData game, byte classId, int level, int vitality, int energy, int questLife)
    {
        var stats = ClassStats.For(game, classId);
        var row = CharStats(game, classId);

        return Vitals(new ClassVitals(row.Vitality, row.Energy, row.Stamina, row.HpAdd, stats.LifePerLevel, stats.StaminaPerLevel,
                                      stats.ManaPerLevel, stats.LifePerVitality, stats.StaminaPerVitality, stats.ManaPerEnergy),
                      level, vitality, energy, questLife);
    }

    /// <summary>The same from a class's columns as given: a season's whose tables are not in hand (<see cref="SeasonSnapshot.KnownClassValues"/>).</summary>
    public static (int Life, int Mana, int Stamina) Vitals(ClassVitals row, int level, int vitality, int energy, int questLife)
    {
        int life = row.Vitality + row.HpAdd + (level - 1) * row.LifePerLevel / 4
                   + (vitality - row.Vitality) * row.LifePerVitality / 4 + questLife;
        int stamina = row.Stamina + (level - 1) * row.StaminaPerLevel / 4
                      + (vitality - row.Vitality) * row.StaminaPerVitality / 4;
        int mana = row.Energy + (level - 1) * row.ManaPerLevel / 4
                   + (energy - row.Energy) * row.ManaPerEnergy / 4;

        return (life, mana, stamina);
    }

    /// <summary>The columns of charstats.txt that base life, mana and stamina are worked out from.</summary>
    public sealed record ClassVitals(int Vitality, int Energy, int Stamina, int HpAdd, int LifePerLevel, int StaminaPerLevel,
                                     int ManaPerLevel, int LifePerVitality, int StaminaPerVitality, int ManaPerEnergy);

    /// <summary>
    /// The attributes section, whole: <paramref name="values"/> in the save's own scale - life, mana
    /// and stamina with <see cref="Scaled"/> - and nothing else; a nought is left out, as the game
    /// leaves it out.
    /// </summary>
    public static void SetAttributes(D2S save, IReadOnlyDictionary<string, long> values)
    {
        save.Attributes.Stats.Clear();
        foreach (var (stat, value) in values)
        {
            if (value != 0)
                save.Attributes.Stats[stat] = unchecked((int)value);
        }
    }

    /// <summary>Life, mana and stamina, current and most, in the save's stored scale.</summary>
    public static IEnumerable<KeyValuePair<string, long>> Scaled(int life, int mana, int stamina) =>
    [
        new("hitpoints", (long)life << AttributeScale.ShiftFor("hitpoints")),
        new("maxhp", (long)life << AttributeScale.ShiftFor("maxhp")),
        new("mana", (long)mana << AttributeScale.ShiftFor("mana")),
        new("maxmana", (long)mana << AttributeScale.ShiftFor("maxmana")),
        new("stamina", (long)stamina << AttributeScale.ShiftFor("stamina")),
        new("maxstamina", (long)stamina << AttributeScale.ShiftFor("maxstamina")),
    ];

    /// <summary>The columns of charstats.txt the life, mana and stamina start from.</summary>
    private sealed record CharStatsRow(int Vitality, int Energy, int Stamina, int HpAdd);

    private static CharStatsRow CharStats(GameData game, byte classId)
    {
        var (table, _) = game.ReadExcel("charstats");
        int name = table.IndexOfColumn("class");
        foreach (var fields in table.Rows)
        {
            if (name < 0 || name >= fields.Length || !fields[name].Equals(CharacterClasses.Names[Math.Min(classId, (byte)6)], StringComparison.OrdinalIgnoreCase))
                continue;

            int Column(string column)
            {
                int at = table.IndexOfColumn(column);
                return at >= 0 && at < fields.Length && int.TryParse(fields[at], out int value) ? value : 0;
            }

            return new CharStatsRow(Column("vit"), Column("int"), Column("stamina"), Column("hpadd"));
        }

        throw new InvalidDataException(L($"charstats.txt has no row for class {classId}."));
    }

    // ---- quests ---------------------------------------------------------------------------------

    /// <summary>
    /// Every quest finished and its reward taken, as the game writes it - QuestProgress.DoneWords,
    /// measured over 335 saves; the expansion's Act V only in the expansion.
    /// </summary>
    public static void CompleteQuests(QuestsSection quests, bool expansion) =>
        EachQuest(quests, (key, act, difficulty) =>
            QuestProgress.DoneWords.TryGetValue(key, out var words) && (expansion || act != "ActV")
                ? words[difficulty]
                : "................");

    /// <summary>No quest so much as started: every word nought, as the game writes a character it has just made.</summary>
    public static void ClearQuests(QuestsSection quests) =>
        EachQuest(quests, (_, _, _) => "................");

    private static void EachQuest(QuestsSection quests, Func<string, string, int, string> word)
    {
        string[] difficulties = ["Normal", "Nightmare", "Hell"];
        for (int d = 0; d < difficulties.Length; d++)
        {
            var difficulty = (QuestsDifficulty)typeof(QuestsSection).GetProperty(difficulties[d])!.GetValue(quests)!;
            foreach (var act in typeof(QuestsDifficulty).GetProperties())
            {
                var questsOfAct = act.GetValue(difficulty)!;
                foreach (var property in act.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(Quest)))
                    QuestProgress.SetWord((Quest)property.GetValue(questsOfAct)!, word($"{act.Name}.{property.Name}", act.Name, d));
            }
        }
    }
}
