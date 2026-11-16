using System;
using System.Collections.Generic;
using System.Linq;

using D2SLib.Model.Save;

namespace PD2SPCompanion.Core.Characters;

/// <summary>
/// The quests as the editor shows and completes them. A quest is sixteen flags per difficulty - bit n
/// is character n of a "word" here, the parser's own order: bit 0 done and its reward taken, bit 1
/// the reward waiting, bit 2 started, then the quest's own steps, bit 12 closed in the log.
/// <para>
/// Completing is one way, as tested in game. Three services on an item are left <b>reward waiting</b>
/// at their NPC, as the game leaves them: Charsi's imbue, Larzuk's socket, Anya's personalization.
/// The Hellforge is never completed and never started: with Mephisto dead (The Guardian done),
/// Cain hands over his Soulstone and starts the forge in game. The editor does not do it for him:
/// the player can just talk to Cain, and the game holds one at a time.
/// Every other quest is written <b>done</b>, and what it gives for good is added by the editor -
/// skill points, stat points, the Golden Bird's life - since none of it carries a mark of its own,
/// and Lam Esen pays only for the tome itself. Den of Evil keeps Akara's respec on offer. An act's
/// last quest opens the next act, and the game's last the next difficulty, as the game would once
/// the boss is dead.
/// </para>
/// </summary>
public static class QuestProgress
{
    public enum State
    {
        NotStarted,
        InProgress,
        RewardWaiting,
        Done,
    }

    /// <summary>
    /// One quest: its act (1-5), the parser's property names, its name in game, what completing it
    /// leaves waiting at an NPC - empty when nothing is left waiting - and everything it gives,
    /// as the game and PD2 hand it out: points and life,
    /// items, services, mercenaries. Empty when it gives nothing. The town discounts the guides name
    /// are left out: in game a tome cost 90 at Akara, Jamella and Malah alike.
    /// </summary>
    public sealed record Info(int Act, string ActProperty, string Property, string Name, string Reward, string Gives = "")
    {
        public string Key => $"{ActProperty}.{Property}";

        public bool Waits => Reward.Length > 0;

    }

    /// <summary>
    /// The quests as the game's quest log orders them (Hell's Forge is Act IV's second),
    /// which is not the save's: the save keeps Tools of the Trade third in Act I, Act III's first four the
    /// other way round, and Terror's End before Hell's Forge. The log's numbering is The Arreat Summit's.
    /// </summary>
    public static IReadOnlyList<string> LogOrder { get; } =
    [
        "ActI.DenOfEvil", "ActI.SistersBurialGrounds", "ActI.TheSearchForCain", "ActI.TheForgottenTower",
        "ActI.ToolsOfTheTrade", "ActI.SistersToTheSlaughter",
        "ActII.RadamentsLair", "ActII.TheHoradricStaff", "ActII.TaintedSun", "ActII.ArcaneSanctuary",
        "ActII.TheSummoner", "ActII.TheSevenTombs",
        "ActIII.TheGoldenBird", "ActIII.BladeOfTheOldReligion", "ActIII.KhalimsWill", "ActIII.LamEsensTome",
        "ActIII.TheBlackenedTemple", "ActIII.TheGuardian",
        "ActIV.TheFallenAngel", "ActIV.Hellforge", "ActIV.TerrorsEnd",
        "ActV.SiegeOnHarrogath", "ActV.RescueOnMountArreat", "ActV.PrisonOfIce", "ActV.BetrayalOfHarrogath",
        "ActV.RiteOfPassage", "ActV.EveOfDestruction",
    ];

    /// <summary>The quests of the five acts, in the save's order; the acts' own intro and completion slots left out.
    /// Shown in <see cref="LogOrder"/>.</summary>
    public static IReadOnlyList<Info> All { get; } =
    [
        new(1, "ActI", "DenOfEvil", L("Den of Evil"), "", L("1 skill point, a free respec (Akara)")),
        new(1, "ActI", "SistersBurialGrounds", L("Sisters' Burial Grounds"), "", L("A free Rogue, and Rogues for hire (Kashya)")),
        new(1, "ActI", "ToolsOfTheTrade", L("Tools of the Trade"), L("an imbue from Charsi"), L("An imbue (Charsi)")),
        new(1, "ActI", "TheSearchForCain", L("The Search for Cain"), "", L("Free identify (Cain), a ring (Akara)")),
        new(1, "ActI", "TheForgottenTower", L("The Forgotten Tower"), ""),
        new(1, "ActI", "SistersToTheSlaughter", L("Sisters to the Slaughter"), ""),
        new(2, "ActII", "RadamentsLair", L("Radament's Lair"), "", L("1 skill point (Book of Skill)")),
        new(2, "ActII", "TheHoradricStaff", L("The Horadric Staff"), ""),
        new(2, "ActII", "TaintedSun", L("Tainted Sun"), ""),
        new(2, "ActII", "ArcaneSanctuary", L("Arcane Sanctuary"), ""),
        new(2, "ActII", "TheSummoner", L("The Summoner"), ""),
        new(2, "ActII", "TheSevenTombs", L("The Seven Tombs"), ""),
        new(3, "ActIII", "LamEsensTome", L("Lam Esen's Tome"), "", L("5 stat points (Alkor)")),
        new(3, "ActIII", "KhalimsWill", L("Khalim's Will"), ""),
        new(3, "ActIII", "BladeOfTheOldReligion", L("Blade of the Old Religion"), "", L("A corrupted ring (Ormus), a free Iron Wolf (Asheara)")),
        new(3, "ActIII", "TheGoldenBird", L("The Golden Bird"), "", L("+20 life (Alkor's potion)")),
        new(3, "ActIII", "TheBlackenedTemple", L("The Blackened Temple"), ""),
        new(3, "ActIII", "TheGuardian", L("The Guardian"), ""),
        new(4, "ActIV", "TheFallenAngel", L("The Fallen Angel"), "", L("2 skill points (Tyrael)")),
        new(4, "ActIV", "TerrorsEnd", L("Terror's End"), ""),
        new(4, "ActIV", "Hellforge", L("Hell's Forge"), "", L("Gems and a rune (the forge)")),
        new(5, "ActV", "SiegeOnHarrogath", L("Siege on Harrogath"), L("a socket from Larzuk"), L("A socket (Larzuk)")),
        new(5, "ActV", "RescueOnMountArreat", L("Rescue on Mount Arreat"), "", L("Tal, Ral and Ort runes (Qual-Kehk), Barbarians for hire")),
        new(5, "ActV", "PrisonOfIce", L("Prison of Ice"), "", L("+10 all resistance (Malah's scroll), a class rare (Anya); in Hell also 3 tier 1 maps")),
        new(5, "ActV", "BetrayalOfHarrogath", L("Betrayal of Harrogath"), L("an item personalized by Anya"), L("An item personalized (Anya)")),
        new(5, "ActV", "RiteOfPassage", L("Rite of Passage"), "", L("Experience, one level at most")),
        new(5, "ActV", "EveOfDestruction", L("Eve of Destruction"), ""),
    ];

    /// <summary>
    /// Every quest finished and its reward taken, written as the game writes it: per quest and
    /// difficulty, the flag word most common among 335 characters' saves (the item packs, read at the
    /// quest section's fixed offset) with bit 0 - completed - set, a tool's "every flag
    /// on" pattern left out. The armory import writes these; loaded in game (a level 99 Barbarian, every Act V
    /// quest complete).
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> DoneWords { get; } = new Dictionary<string, string[]>
    {
        ["ActI.Introduction"] = Same("1..............."),
        ["ActI.DenOfEvil"] = Same("1...........1..."),
        ["ActI.SistersBurialGrounds"] = ["1...1.......1...", "1.111.......1...", "1.111.......1..."],
        ["ActI.ToolsOfTheTrade"] = ["1..1..1.....1...", "1..1..1.....1...", "1.11..1.....1..."],
        ["ActI.TheSearchForCain"] = ["1..11.......1...", "1..11.....1.1...", "1..11.....1.1..."],
        ["ActI.TheForgottenTower"] = Same("1.1.1.......1..."),
        ["ActI.SistersToTheSlaughter"] = Same("1..11.......1..."),
        ["ActI.Completion"] = Same("1..............."),
        ["ActII.Introduction"] = Same("1..............."),
        ["ActII.RadamentsLair"] = Same("1...1.......1..."),
        ["ActII.TheHoradricStaff"] = Same("1..1111...111..."),
        ["ActII.TaintedSun"] = Same("1.1.........1..."),
        ["ActII.ArcaneSanctuary"] = Same("1......11...1..."),
        ["ActII.TheSummoner"] = Same("1.1.........1..."),
        ["ActII.TheSevenTombs"] = Same("1.1..1......1..."),
        ["ActII.Completion"] = Same("1..............."),
        ["ActIII.Introduction"] = Same("1..............."),
        ["ActIII.LamEsensTome"] = Same("1...........1..."),
        ["ActIII.KhalimsWill"] = Same("1...........1..."),
        ["ActIII.BladeOfTheOldReligion"] = Same("1.1.111111..1..."),
        ["ActIII.TheGoldenBird"] = Same("1...........1..."),
        ["ActIII.TheBlackenedTemple"] = Same("1..1........1..."),
        ["ActIII.TheGuardian"] = Same("1........1..1..."),
        ["ActIII.Completion"] = Same("1..............."),
        ["ActIV.Introduction"] = Same("1..............."),
        ["ActIV.TheFallenAngel"] = Same("1...........1..."),
        ["ActIV.TerrorsEnd"] = Same("1.......11..1..."),
        ["ActIV.Hellforge"] = ["1...........1...", "1...........1...", "1...........1..1"],
        ["ActIV.Completion"] = Same("1..............."),
        ["ActV.Introduction"] = Same("1..............."),
        ["ActV.SiegeOnHarrogath"] = Same("1....1......1..1"),
        ["ActV.RescueOnMountArreat"] = Same("1...........1..."),
        ["ActV.PrisonOfIce"] = Same("1..1...1111.1..."),
        ["ActV.BetrayalOfHarrogath"] = ["1.111.......1...", "1.111.......1...", "1.111.......1..1"],
        ["ActV.RiteOfPassage"] = Same("1..1........1..."),
        ["ActV.EveOfDestruction"] = ["1.111.1.111.1...", "1.111.1..11.1...", "1.111.1.111.1..."],
        ["ActV.Completion"] = Same("1..............1"),
    };

    /// <summary>
    /// "Reward waiting" words read off real saves, per difficulty: bit 1 set, bit 0 clear - a quest
    /// finished and its NPC not yet spoken to (335 saves, normal difficulty; all three
    /// difficulties over 372 saves). Normal, Nightmare,
    /// Hell: Radament 77/95/75 saves, the forge 42/64/10, Larzuk's socket 98/112/119, Malah's scroll
    /// 49/79/9, Anya's personalization 17/6/87. Charsi's imbue is the one that differs: 56 in Normal,
    /// then without bit 2 - 81 and 59.
    /// </summary>
    private static readonly Dictionary<string, string[]> MeasuredWaiting = new()
    {
        ["ActI.ToolsOfTheTrade"] = [".111..1........1", ".1.1..1........1", ".1.1..1........1"],
        ["ActII.RadamentsLair"] = Same(".1..1..........1"),
        ["ActIV.Hellforge"] = Same(".1..........1..1"),
        ["ActV.SiegeOnHarrogath"] = Same(".1...1.........1"),
        ["ActV.PrisonOfIce"] = Same(".1.1...11......1"),
        ["ActV.BetrayalOfHarrogath"] = Same(".1111..........1"),
    };

    /// <summary>
    /// What a quest done gives for good, added by the editor as it completes it:
    /// Den of Evil's and Radament's skill point, Izual's two, Lam Esen's five stat
    /// points, the Golden Bird's 20 life. Prison of Ice's resistance needs nothing: the game adds
    /// it for the quest word itself.
    /// </summary>
    public sealed record Gift(int StatPoints, int SkillPoints, int Life);

    /// <summary>
    /// The item rewards a quest completed here does not hand out, deliberately
    /// (small random items, no new risk), for the confirm to say so. Difficulty is 0
    /// Normal, 1 Nightmare, 2 Hell: Anya's maps are PD2's, in Hell only.
    /// </summary>
    public static IReadOnlyList<string> NotGiven(Info quest, int difficulty) => quest.Key switch
    {
        "ActI.TheSearchForCain" => [L("Akara's ring")],
        "ActIII.BladeOfTheOldReligion" => [L("Ormus's corrupted ring")],
        "ActV.RescueOnMountArreat" => [L("Qual-Kehk's runes")],
        "ActV.PrisonOfIce" => difficulty == 2 ? [L("Anya's class rare"), L("Anya's maps")] : [L("Anya's class rare")],
        _ => [],
    };

    /// <summary>
    /// Where the player gets a quest item the editor does not hand out, for the confirm to say:
    /// the Horadric Cube, which the Horadric Staff is made in, and
    /// Mephisto's Soulstone, which the Hellforge takes after The Guardian. Null for the rest.
    /// </summary>
    public static string? HowToGet(Info quest) => quest.Key switch
    {
        "ActII.TheHoradricStaff" => L("Horadric Cube: from the chest in Act II, or in Travincal."),
        "ActIII.TheGuardian" => L("Mephisto's Soulstone: talk to Cain."),
        _ => null,
    };

    public static IReadOnlyDictionary<string, Gift> Gifts { get; } = new Dictionary<string, Gift>
    {
        ["ActI.DenOfEvil"] = new(0, 1, 0),
        ["ActII.RadamentsLair"] = new(0, 1, 0),
        ["ActIII.LamEsensTome"] = new(5, 0, 0),
        ["ActIII.TheGoldenBird"] = new(0, 0, 20),
        ["ActIV.TheFallenAngel"] = new(0, 2, 0),
    };

    /// <summary>The Hellforge: never completed here - the player smashes the soulstone at the forge.</summary>
    public const string Forge = "ActIV.Hellforge";

    /// <summary>Act III's last quest: Mephisto dead, and his Soulstone Cain's to hand over.</summary>
    public const string Guardian = "ActIII.TheGuardian";

    /// <summary>
    /// What completing a quest did: what it added for good, and what it opened - the next act, or
    /// the next difficulty - said as it would be on the strip.
    /// </summary>
    public sealed record Completed(Gift Gift, string? Opened = null, Completion? Undo = null);

    /// <summary>
    /// What one completion changed, so it can be taken back on its own (a revert,
    /// this session only): every quest word it wrote, before and after, the header's progression, and
    /// the gift. See <see cref="Revert"/>.
    /// </summary>
    public sealed record Completion(int Difficulty, string Quest,
                                    IReadOnlyList<(string Slot, string Before, string After)> Words,
                                    byte ProgressionBefore, byte ProgressionAfter, Gift Gift);

    /// <summary>
    /// Each act's last quest, the act's own completion word and the next act's introduction.
    /// The game opens an act - its waypoint tab, its caravan - only once the act
    /// before reads finished: QuestTest held every waypoint and saw Act I alone until these were
    /// written, and Act V, in 456 of 456 corpus difficulties, opens only with Terror's End done.
    /// </summary>
    private static readonly (string Last, string Completion, string Next, string Named)[] ActEnds =
    [
        ("ActI.SistersToTheSlaughter", "ActI.Completion", "ActII.Introduction", L("Act II")),
        ("ActII.TheSevenTombs", "ActII.Completion", "ActIII.Introduction", L("Act III")),
        ("ActIII.TheGuardian", "ActIII.Completion", "ActIV.Introduction", L("Act IV")),
        ("ActIV.TerrorsEnd", "ActIV.Completion", "ActV.Introduction", L("Act V")),
    ];

    /// <summary>
    /// The act after <paramref name="last"/> opened, where it was its act's last quest: the act's
    /// completion and the next one's introduction, as the game writes them done - only where still
    /// nought, and no Act V in the classic game. Act V's own completion slot is never written: it is
    /// Akara's respec (<see cref="RespecSlot"/>). The act opened, or null.
    /// </summary>
    private static string? OpenNextAct(D2S save, int difficulty, string last)
    {
        if (ActEnds.FirstOrDefault(each => each.Last == last) is not { Last: not null } end)
            return null;
        if (end.Next.StartsWith("ActV.", StringComparison.Ordinal) && !save.Status.IsExpansion)
            return null;

        bool opened = false;
        foreach (string key in new[] { end.Completion, end.Next })
        {
            string[] parts = key.Split('.');
            var quest = QuestOf(save.Quests, difficulty, new Info(0, parts[0], parts[1], "", ""));
            if (WordOf(quest).Contains('1'))
                continue;
            SetWord(quest, DoneWords[key][difficulty]);
            opened = true;
        }

        return opened ? L($"{end.Named} open.") : null;
    }

    /// <summary>
    /// The next difficulty opened when the game's last quest in this one is done - Eve of Destruction,
    /// or Terror's End in the classic game: the header's progression (the title, and which difficulties
    /// the character may enter) raised to this difficulty finished - 5, 10, 15 in the expansion, 4, 8,
    /// 12 in the classic game - never lowered. What it opened, or null.
    /// </summary>
    private static string? OpenNextDifficulty(D2S save, int difficulty, string last)
    {
        bool expansion = save.Status.IsExpansion;
        if (last != (expansion ? "ActV.EveOfDestruction" : "ActIV.TerrorsEnd"))
            return null;

        byte finished = (byte)((difficulty + 1) * (expansion ? 5 : 4));
        if (save.Progression >= finished)
            return null;

        save.Progression = finished;
        return difficulty switch { 0 => L("Nightmare open."), 1 => L("Hell open."), _ => L("Hell finished.") };
    }

    /// <summary>
    /// Akara's respec, still on offer: the word the game keeps in the slot the parser names Act V's
    /// completion - 305 corpus difficulties hold it, and QuestTest after Akara; a respec
    /// taken reads <c>1..............1</c> (710).
    /// </summary>
    public const string RespecSlot = "ActV.Completion";

    public const string RespecOnOffer = ".1.............1";

    private static string[] Same(string word) => [word, word, word];

    /// <summary>The quest's flags, in the difficulty (0 Normal, 1 Nightmare, 2 Hell).</summary>
    public static Quest QuestOf(QuestsSection quests, int difficulty, Info quest)
    {
        var level = difficulty switch { 0 => quests.Normal, 1 => quests.Nightmare, _ => quests.Hell };
        var act = typeof(QuestsDifficulty).GetProperty(quest.ActProperty)!.GetValue(level)!;
        return (Quest)act.GetType().GetProperty(quest.Property)!.GetValue(act)!;
    }

    /// <summary>A quest's sixteen flags as its word, bit n being character n.</summary>
    public static string WordOf(Quest quest)
    {
        var flags = Flags();
        return new string([.. flags.Select(flag => (bool)flag.GetValue(quest)! ? '1' : '.')]);
    }

    /// <summary>Sets a quest's sixteen flags from its word, bit n being character n.</summary>
    public static void SetWord(Quest quest, string word)
    {
        var flags = Flags();
        for (int bit = 0; bit < flags.Count && bit < word.Length; bit++)
            flags[bit].SetValue(quest, word[bit] == '1');
    }

    private static List<System.Reflection.PropertyInfo> Flags() =>
        [.. typeof(Quest).GetProperties().Where(p => p.PropertyType == typeof(bool))];

    public static State StateOf(Quest quest)
    {
        string word = WordOf(quest);
        if (word[0] == '1')
            return State.Done;
        if (word[1] == '1')
            return State.RewardWaiting;
        return word.Contains('1') ? State.InProgress : State.NotStarted;
    }

    /// <summary>
    /// Whether a quest in this state may be completed here: not done, not already waiting for what
    /// it leaves waiting, and never the Hellforge (see <see cref="Forge"/>).
    /// </summary>
    public static bool CanComplete(Info quest, State state) =>
        quest.Key != Forge && state is not State.Done && !(state is State.RewardWaiting && quest.Waits);

    /// <summary>
    /// Completes a quest, one way (see the class summary): its reward left waiting where it is one of
    /// the three services, else done - with what it gives for good added to the character, Den of
    /// Evil's respec offered where the slot has never been touched. Null when nothing changed.
    /// </summary>
    public static Completed? Complete(D2S save, int difficulty, Info quest)
    {
        var flags = QuestOf(save.Quests, difficulty, quest);
        if (!CanComplete(quest, StateOf(flags)))
            return null;

        var before = Words(save, difficulty);
        byte progression = save.Progression;

        if (quest.Waits)
        {
            SetWord(flags, WaitingWord(quest, difficulty));
            var nothing = new Gift(0, 0, 0);
            return new Completed(nothing, Undo: Record(save, difficulty, quest, before, progression, nothing));
        }

        SetWord(flags, DoneWords[quest.Key][difficulty]);

        if (quest.Key == "ActI.DenOfEvil")
        {
            var slot = QuestOf(save.Quests, difficulty, new Info(5, "ActV", "Completion", "", ""));
            if (!WordOf(slot).Contains('1'))
                SetWord(slot, RespecOnOffer);
        }

        var gift = Gifts.GetValueOrDefault(quest.Key) ?? new Gift(0, 0, 0);
        Grant(save, gift);
        string? opened = string.Join(" ", new[] { OpenNextAct(save, difficulty, quest.Key), OpenNextDifficulty(save, difficulty, quest.Key) }
                                              .OfType<string>());
        return new Completed(gift, opened.Length > 0 ? opened : null, Record(save, difficulty, quest, before, progression, gift));
    }

    /// <summary>Every quest slot of the difficulty and its word - the next act's and Akara's respec included.</summary>
    private static Dictionary<string, string> Words(D2S save, int difficulty)
    {
        var level = difficulty switch { 0 => save.Quests.Normal, 1 => save.Quests.Nightmare, _ => save.Quests.Hell };
        var words = new Dictionary<string, string>();
        foreach (var act in typeof(QuestsDifficulty).GetProperties())
        {
            if (act.GetValue(level) is not { } acts)
                continue;
            foreach (var slot in acts.GetType().GetProperties().Where(each => each.PropertyType == typeof(Quest)))
                if (slot.GetValue(acts) is Quest quest)
                    words[$"{act.Name}.{slot.Name}"] = WordOf(quest);
        }
        return words;
    }

    private static Completion Record(D2S save, int difficulty, Info quest, Dictionary<string, string> before, byte progression, Gift gift)
    {
        var after = Words(save, difficulty);
        var changed = after.Where(each => before.GetValueOrDefault(each.Key) != each.Value)
                           .Select(each => (each.Key, before.GetValueOrDefault(each.Key) ?? "", each.Value))
                           .ToList();
        return new Completion(difficulty, quest.Key, changed, progression, save.Progression, gift);
    }

    /// <summary>What <see cref="CannotRevert"/> says when a completion's words were written over since.</summary>
    public static string ChangedSince => L("Changed since. Cannot revert.");

    /// <summary>
    /// Why a completion cannot be taken back now, or null when it can: every word it wrote must still be
    /// as it wrote it - nothing since, the game or the history, has changed them - and its points still
    /// unspent, as a level given back needs (LevelEditor).
    /// </summary>
    public static string? CannotRevert(D2S save, Completion done)
    {
        var words = Words(save, done.Difficulty);
        if (done.Words.Any(each => words.GetValueOrDefault(each.Slot) != each.After)
            || (done.ProgressionAfter != done.ProgressionBefore && save.Progression != done.ProgressionAfter))
            return ChangedSince;

        var stats = save.Attributes.Stats;
        if (stats.GetValueOrDefault("statpts") < done.Gift.StatPoints || stats.GetValueOrDefault("newskills") < done.Gift.SkillPoints)
            return L("Points spent. Require respec.");

        return null;
    }

    /// <summary>
    /// Takes a completion back: its words and the progression as they were, its gift taken away - life
    /// from its most and its current, never below one. Null when done, otherwise why not (see
    /// <see cref="CannotRevert"/>).
    /// </summary>
    public static string? Revert(D2S save, Completion done)
    {
        if (CannotRevert(save, done) is { } why)
            return why;

        var level = done.Difficulty switch { 0 => save.Quests.Normal, 1 => save.Quests.Nightmare, _ => save.Quests.Hell };
        foreach (var (slot, before, _) in done.Words)
        {
            string[] parts = slot.Split('.');
            var acts = typeof(QuestsDifficulty).GetProperty(parts[0])!.GetValue(level)!;
            SetWord((Quest)acts.GetType().GetProperty(parts[1])!.GetValue(acts)!, before);
        }
        // Only what this completion raised: a later quest's progress is not this one's to take back.
        if (done.ProgressionAfter != done.ProgressionBefore)
            save.Progression = done.ProgressionBefore;

        var stats = save.Attributes.Stats;
        void Take(string stat, long amount, long least)
        {
            if (amount == 0)
                return;
            long value = Math.Max(least, (long)stats.GetValueOrDefault(stat) - amount);
            // A pool at nought is left out, as the game leaves it out (LevelEditor).
            if (value == 0)
                stats.Remove(stat);
            else
                stats[stat] = unchecked((int)value);
        }

        Take("statpts", done.Gift.StatPoints, 0);
        Take("newskills", done.Gift.SkillPoints, 0);
        long one = 1L << AttributeScale.ShiftFor("maxhp");
        Take("maxhp", (long)done.Gift.Life << AttributeScale.ShiftFor("maxhp"), one);
        Take("hitpoints", (long)done.Gift.Life << AttributeScale.ShiftFor("hitpoints"), 1L << AttributeScale.ShiftFor("hitpoints"));
        return null;
    }

    /// <summary>Adds a gift to the character: unspent points, and life to both its most and its current.</summary>
    public static void Grant(D2S save, Gift gift)
    {
        var stats = save.Attributes.Stats;
        void Add(string stat, long amount)
        {
            if (amount == 0)
                return;
            long value = stats.GetValueOrDefault(stat) + amount;
            // Held to what the save's field holds: past it the file keeps only the low bits.
            stats[stat] = (int)Math.Min(value, StatFields.AttributeRangeOf(stat)?.Max ?? value);
        }

        Add("statpts", gift.StatPoints);
        Add("newskills", gift.SkillPoints);
        Add("maxhp", (long)gift.Life << AttributeScale.ShiftFor("maxhp"));
        Add("hitpoints", (long)gift.Life << AttributeScale.ShiftFor("hitpoints"));
    }

    /// <summary>The word a quest left waiting has, in the difficulty - each read off real saves.</summary>
    public static string WaitingWord(Info quest, int difficulty) => MeasuredWaiting[quest.Key][difficulty];
}
