using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// A new character, made to order: a class, a level, a name, and its quests -
/// none, or all three difficulties done with the three services on an item left waiting at their
/// NPCs, the Hellforge left to the game, Akara's respec on offer, and the Horadric Cube those
/// quests found. Written on a template save, as the armory import
/// is (<see cref="NewCharacter"/>).
/// </summary>
public static class CharacterCreation
{
    /// <summary>
    /// The quests left with their reward waiting when every quest is done, so the player chooses
    /// the item: Charsi's imbue, Larzuk's socket and Anya's personalization.
    /// </summary>
    public static IReadOnlyList<string> KeptWaiting { get; } =
        ["ActI.ToolsOfTheTrade", "ActV.SiegeOnHarrogath", "ActV.BetrayalOfHarrogath"];

    /// <summary>
    /// The save, built on <paramref name="templatePath"/> (read afresh from disk, so nothing of the
    /// open character leaks in), verified to read back. At its level it holds what the level-ups and,
    /// with <paramref name="questsDone"/>, every other quest reward gave it - as unspent points and as
    /// life - and nothing spent: the class's own attributes, no skill.
    /// </summary>
    public static byte[] Build(GameData game, string templatePath, string name, byte classId, int level, bool hardcore, bool questsDone)
    {
        var (save, _) = SaveVerifier.Load(templatePath);
        level = Math.Clamp(level, 1, 99);
        string era = NewCharacter.ItemEra(save);

        NewCharacter.Begin(save, name, classId, level, hardcore, save.Status.IsExpansion, questsDone);
        if (questsDone)
        {
            LeaveWaiting(save.Quests);

            // The Horadric Cube: Act II's quests cannot be done without it, so
            // a character that did them has one - in the inventory's first cell, as one picked up.
            var cube = ItemForge.Cube(era);
            cube.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
            ItemMover.Place(cube, InventoryLayout.InventoryPage, 0, 0);
            save.PlayerItemList.Items.Add(cube);
            save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;

            // No Soulstone: Cain hands it over in Act IV, and starts the forge.
        }

        var stats = ClassStats.For(game, classId);
        if (stats == ClassStats.Unknown)
            throw new InvalidDataException(L($"charstats.txt has no row for class {classId}."));

        var table = ExperienceTable.For(game, stats.Name);
        if (!table.IsAvailable)
            throw new InvalidDataException(L("experience.txt could not be read."));

        var gifts = questsDone ? NewCharacter.QuestGifts.All : NewCharacter.QuestGifts.None;
        var (life, mana, stamina) = NewCharacter.Vitals(game, classId, level, stats.Vitality, stats.Energy, gifts.Life);

        var values = new Dictionary<string, long>
        {
            ["strength"] = stats.Strength,
            ["energy"] = stats.Energy,
            ["dexterity"] = stats.Dexterity,
            ["vitality"] = stats.Vitality,
            ["statpts"] = (level - 1) * stats.StatPerLevel + gifts.StatPoints,
            ["newskills"] = (level - 1) * LevelEditor.SkillsPerLevel + gifts.SkillPoints,
        };
        foreach (var (stat, value) in NewCharacter.Scaled(life, mana, stamina))
            values[stat] = value;
        values["level"] = level;
        values["experience"] = table.Start(level);

        NewCharacter.SetAttributes(save, values);
        return SaveWriter.Prepared(save);
    }

    /// <summary>
    /// What stays open when every quest is done, in each difficulty: the kept
    /// quests' reward waiting, as the game leaves it before the NPC is spoken to; the Hellforge not
    /// started, for Cain to start in Act IV with the soulstone he hands over; and Akara's respec on offer.
    /// </summary>
    private static void LeaveWaiting(QuestsSection quests)
    {
        var forge = QuestProgress.All.First(each => each.Key == QuestProgress.Forge);
        var respec = new QuestProgress.Info(5, "ActV", "Completion", "", "");
        for (int difficulty = 0; difficulty < 3; difficulty++)
        {
            foreach (string key in KeptWaiting)
            {
                var quest = QuestProgress.All.First(each => each.Key == key);
                QuestProgress.SetWord(QuestProgress.QuestOf(quests, difficulty, quest), QuestProgress.WaitingWord(quest, difficulty));
            }

            QuestProgress.SetWord(QuestProgress.QuestOf(quests, difficulty, forge), "................");
            QuestProgress.SetWord(QuestProgress.QuestOf(quests, difficulty, respec), QuestProgress.RespecOnOffer);
        }
    }
}
