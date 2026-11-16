using System.Text;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Testbed;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Cli;

/// <summary>
/// pd2spc: the editor's command-line tool - for the player, for tests/regress.py, and for anyone
/// working on the project, who must be able to run all of it. Every command is in <see cref="Commands"/>, which `pd2spc help` prints; nothing asks
/// for input. Exit codes: 0 done and clean, 1 a finding, a refusal or an error, 2 wrong usage.
/// Commands that write a save take a backup first; those that write into the game folder are in
/// the "game" group. The bodies live in Commands/, one file per group.
/// </summary>
internal static partial class Cli
{
    private static List<string> arguments = [];

    /// <summary>
    /// One command: its name, group, usage (after the name), one-line summary, and what runs it. <paramref name="Writes"/>: a
    /// check that writes to a path it is named, which the game guard asks about though its group goes free.
    /// </summary>
    private sealed record Command(string Name, string Group, string Usage, string Summary, Func<int> Run, bool Writes = false);

    /// <summary>What each group holds, in the order help lists them.</summary>
    private static readonly (string Group, string Says)[] Groups =
    [
        ("look", "read-only views of saves, stashes and tables"),
        ("edit", "writes a save or stash (backup first; many take --dry-run)"),
        ("vault", "the item vault"),
        ("game", "the Experimental page's files for the game folder"),
        ("check", "verification and regression checks (read-only or scratch copies)"),
        ("research", "one-off investigations, candidates to retire"),
    ];

    private static readonly Command[] Commands =
    [
        new("dump", "look", "<path> [--page <value>] [--no-stats]", "A character, stash or shared stash: attributes and every item", () => Dump(GameRoot(), Positional(1))),
        new("extract", "look", "[--out <outputDir>]", "Write every excel table to disk as .txt", () => Extract(GameRoot(), Option("--out") ?? "data")),
        new("files", "look", "<text>", "Files an archive listfile names that contain the text", () => FilesCommand(GameRoot(), Positional(1))),
        new("info", "look", "", "The archive chain and every excel table it resolves", () => Info(GameRoot())),
        new("itemcode", "look", "<code>", "What an item code is: its base row, type and size", () => ItemCode(GameRoot(), Positional(1))),
        new("quests", "look", "<saves...>", "Every quest's sixteen flags per difficulty, saves side by side", () => QuestsCommand(GameRoot(), Named())),
        new("ranges", "look", "<path> [--item <value>] [--prefix <value>] [--suffix <value>]", "Every stat range on one item, and what stacked into it", () => Ranges(GameRoot(), Positional(1))),
        new("reqs", "look", "<path>", "Every worn item and charm: what it asks and whether the character meets it", () => ReqsCommand(GameRoot(), Positional(1))),
        new("anim", "look", "<name start>", "The animations whose names start so (AMSC = the Amazon casting): frames, speed, the frame it acts on, and the cast-rate and hit-recovery rows counted on each", () => AnimCommand(GameRoot(), Positional(1))),
        new("formulas", "look", "", "Every skill line the character sheet counts - a passive's, a buff's - whose formula it cannot read: what a new season's tables may bring", () => FormulasCommand(GameRoot())),
        new("sheet", "look", "<save.d2s> [--set <1|2>] [--sources] [--town] [--bare] [--on <Kind:skill,...>] [--off <Kind:skill,...>] [--right <skill>] [--left <skill>]", "The character sheet as the game's Advanced Stats page lays it out; --sources lists what each number is made of, --town leaves the mercenary's auras out, --bare is nothing cast, --on and --off press buffs of its row, --right and --left stand in for the skill boxes", () => SheetCommand(GameRoot(), Positional(1))),
        new("runewords", "look", "<path> [--stash <value>]", "Runewords a save (or stash) could make from the runes and bases it holds", () => RunewordsCommand(GameRoot(), Positional(1))),
        new("dcc", "look", "<archive path> [--dir <n>] [--out <png>]", "Decode one direction of a DCC animation; --out writes its frames side by side", () => DccCommand(GameRoot(), Positional(1))),
        new("figure", "look", "<save.d2s> [--mode <TN>] [--dir <n|all>] [--out <png>]", "The character drawn in its gear, as the select screen draws it; --out writes the frames side by side", () => FigureCommand(GameRoot(), Positional(1))),
        new("sprite", "look", "<invFile> [--out <value>] [--tint <value>]", "Decode an inventory graphic from the archives to PNG", () => Sprite(GameRoot(), Positional(1))),
        new("town", "look", "<act 1-5> [--out <png>] [--at <column,row>] [--size <width,height>] [--objects] [--no-roofs] [--backdrop]", "An act's town drawn from its map and tiles: the whole map, or a window centred on a cell; --objects marks what is placed on it", () => Town(GameRoot(), Positional(1))),
        new("statdiff", "look", "<oldPath> <newPath>", "Compare two ItemStatCost tables", () => StatDiff(Positional(1), Positional(2))),
        new("strpos", "look", "<keys...>", "Each string key, its text and where in the string tables it sits", () => StringPositions(GameRoot(), Named())),
        new("table", "look", "<name> [--rows <rows>]", "One table from the archive chain, as the game reads it", () => Table(GameRoot(), Positional(1), int.TryParse(Option("--rows"), out int rows) ? rows : 20)),
        new("wornfields", "look", "<paths...>", "Every worn item's placement fields as the game wrote them", () => WornFields(GameRoot(), Named())),
        new("addstat", "edit", "<save.d2s> --item <n> --id <stat id> [--value <v>] [--skill <id>] [--dry-run]", "Put a new property on one item - at its lowest value, or the one given; --skill for one that names a skill (an aura when equipped)", () => AddStat(GameRoot(), Positional(1))),
        new("affix", "edit", "<save.d2s> --item <n> [--slot <1-3>] [--set <id>] [--suffix-side] [--all] [--merc] [--dry-run]", "An item's affix pairs and what could replace them; --set writes one", () => Affixes(GameRoot(), Positional(1))),
        new("armory", "edit", "<who> [--template <template>] [--out <output>] [--name <name>]", "Build an armory character into a save (--out, never the save folder unless named)", () => ArmoryCommand(GameRoot(), Positional(1), Option("--template"), Option("--out"), Option("--name"))),
        new("armorybuild", "edit", "<json> [--template <template>] [--out <output>] [--name <name>]", "The armory import offline, from a saved armory JSON", () => ArmoryBuildCommand(GameRoot(), Positional(1), Option("--template"), Option("--out"), Option("--name"))),
        new("clear", "edit", "<save.d2s> [--dry-run]", "Remove every stored item; worn and belt stay", () => Clear(GameRoot(), Positional(1))),
        new("corrupt", "edit", "<save.d2s> --item <n> [--id <corruption>] [--list [--as-quality <q>] [--merc]] [--clean] [--dry-run]", "Corrupt one item, rolled or chosen; --clean takes it off", () => Corrupt(GameRoot(), Positional(1))),
        new("craft", "edit", "<save.d2s> --item <n> [--family <name>] [--seed <n>]", "Craft a copy of an item in memory and print what it kept", () => Craft(GameRoot(), Positional(1))),
        new("create", "edit", "--class <Amazon..Assassin> --template <save.d2s> --out <Name.d2s> [--level <n>] [--quests] [--hardcore [--dead]] [--complete <ActI.DenOfEvil,...>] [--waypoints <Normal|all>] [--golem <item code> [--golem-by <unique>]]", "Create a character from a template save, as the Experimental page does; --golem stands an Iron Golem made of that base beside it, for tests - its skill from points, or from that unique worn (Metalgrid)", () => CreateCommand(GameRoot(), Option("--class"), Option("--level"), arguments.Contains("--quests"), arguments.Contains("--hardcore"), Option("--template"), Option("--out"), Option("--complete"), Option("--waypoints"), arguments.Contains("--dead"), Option("--golem"), Option("--golem-by"))),
        new("ethereal", "edit", "<save.d2s> --item <n> [--dry-run]", "Turn one item ethereal or back, an armour's defence moved with it as the editor moves it", () => EtherealCommand(GameRoot(), Positional(1))),
        new("dupe", "edit", "<save.d2s> --item <n> [--dry-run]", "An exact second of one item, same id", () => Dupe(GameRoot(), Positional(1))),
        new("fill", "edit", "<path>", "Fill a test character's free cells with chipped gems", () => FillCommand(GameRoot(), Positional(1))),
        new("give", "edit", "<save.d2s> [--unique <name>]... [--base <code:sockets>]... [--forge <code:quality>]... [--runeword <name:code[:eth][:sup<N>]>]... [--skin <value>] [--skill <id>] [--unidentified] --backup <folder>", "Uniques by name, plain socketed bases, bases made as the Forge makes them (magic, rare, craft: rolled, every number at its top) and runewords as the Forge makes them, the base ethereal (eth) or superior with N% enhanced damage (sup20), into a character, backup first; --skin puts the skin stat on the uniques, --skill sets a random-skill unique's skill (Ormus' Robes)", () => Give(GameRoot(), Positional(1), Options("--unique"), Options("--base"), Options("--forge"), Option("--backup"), int.TryParse(Option("--skin"), out int skin) ? skin : null, int.TryParse(Option("--skill"), out int skill) ? skill : null, Options("--runeword"), arguments.Contains("--unidentified"))),
        new("specimens", "edit", "<save.d2s> --backup <folder> [--round 2]", "One forged item of each kind into a character - set, magic, rare, craft, runeword, ethereal, corrupted, desecrated, plain, charm, jewel, map - for reading the game's Ctrl+C text against", () => Specimens(GameRoot(), Positional(1), Option("--backup"), Option("--round"))),
        new("giveset", "edit", "<save.d2s> <set name> --backup <folder>", "Every piece of one set into a character, backup first", () => GiveSet(GameRoot(), Positional(1), Positional(2), Option("--backup"))),
        new("materials", "edit", "<pd2_shared.stash> [--set <slot=count>] [--check <save,save>]", "The shared stash's materials tab; --set writes a count, --check compares forged materials", () => MaterialsCommand(GameRoot(), Positional(1))),
        new("merc", "edit", "<save.d2s> [--level <n>] [--variant <type id>] [--name <place>] [--revive] [--dead] [--dry-run]", "The mercenary within its type: level, variant, name, revive; --dead for testing Revive. Alone, what it could be", () => MercCommand(GameRoot(), Positional(1))),
        new("migratefiles", "edit", "--from <tables> <save|stash> | --corpus <dir> [--out <dir>] [--items] [--keep-sets] [--keep-broken] [--stamp <n>]", "Items of save or stash files read under an older season's tables, fitted to this one; --out writes the stashes", () => Migrate(GameRoot(), Option("--from"), Option("--corpus"), arguments.Count > 1 && !arguments[1].StartsWith("--") ? arguments[1] : null, Option("--out"))),
        new("openacts", "edit", "<path> [--difficulty <difficultyName>]", "Open every act in one difficulty for a test character", () => OpenActsCommand(GameRoot(), Positional(1), Option("--difficulty") ?? "Normal")),
        new("questitem", "edit", "<path> <code> [--start <start>] [--difficulty <difficultyText>]", "Give a test character a quest item", () => QuestItemCommand(GameRoot(), Positional(1), Positional(2), Option("--start"), Option("--difficulty"))),
        new("copyitem", "edit", "<save.d2s> --from <save.d2s> --item <n> --count <k> --backup <folder>", "Copies of one item of another save, its season's stamp kept, under new ids, into a test character", () => CopyItemCommand(GameRoot(), Positional(1), Option("--from") ?? throw new ArgumentException("copyitem needs --from <save.d2s>"), int.Parse(Option("--item") ?? throw new ArgumentException("copyitem needs --item <n>")), int.Parse(Option("--count") ?? "1"), Option("--backup"))),
        new("skillpoints", "edit", "<save.d2s> --skill <name:points>... [--right <name>] --backup <folder>", "Hard points into a test character's skills, prerequisites left out, and its right skill box", () => SkillPointsCommand(GameRoot(), Positional(1), Options("--skill"), Option("--right"), Option("--backup"))),
        new("rename", "edit", "<path> <name>", "Rename a character's files (use on a copy of a save folder)", () => RenameCommand(GameRoot(), Positional(1), Positional(2))),
        new("reroll", "edit", "<save.d2s> --item <n> [--dry-run]", "Roll every property of one item again (the Demonic Cube)", () => Reroll(GameRoot(), Positional(1))),
        new("set", "edit", "<save.d2s> --item <n> --stat <name> --value <v> [--dry-run]", "Change one stat on one item", () => Set(GameRoot(), Positional(1))),
        new("setfix", "edit", "<save|vault...> [--write]", "Repair forged set pieces whose green lists were written wrong", () => SetFix(GameRoot(), Named(), arguments.Contains("--write"))),
        new("damagefix", "edit", "<save|stash|vault...> [--write]", "Repair a flat damage written for the wrong kinds: armour built by the trade import or the forge before 2026-10-04 with its one-handed copy alone", () => DamageFix(GameRoot(), Named(), arguments.Contains("--write"))),
        new("elemfix", "edit", "<save|stash|vault...> [--write]", "Repair a \"+N to Fire Skills\" whose grant and words disagree: forged before 2026-10-05 with no element, so it granted nothing", () => ElemFix(GameRoot(), Named(), arguments.Contains("--write"))),
        new("stackfix", "edit", "<save|vault...> [--write]", "Repair forged throwing weapons and quivers stored with no quantity", () => StackFix(GameRoot(), Named(), arguments.Contains("--write"))),
        new("vaultio", "vault", "[--vault <name>] [--hardcore] [--new <name> [--copy <name>]] [--delete <name>] [--rename-vault <name> --as <new>] [--export <file>] [--import <file> [--as-folder [--folders <A/B;C;/>]]] [--rename <old> --as <new>]", "A mode's vaults as Settings handles them: made, copied, renamed, deleted, written out; a file brought in as a vault or as a folder of chosen folders; a character renamed in every vault", () => VaultIo(GameRoot(), arguments.Contains("--hardcore"), Option, arguments.Contains)),
        new("charmigrate", "edit", "<save.d2s...> [--stash <pd2_shared.stash>] [--dry-run]", "Bring characters of the season before into the installed one, in place: their gold into the shared stash, their base life and mana by the new class table, their items kept as they are (legacy); a report each", () => MigrateCharacters(GameRoot(), [.. arguments.Skip(1).TakeWhile(each => !each.StartsWith("--"))], Option("--stash"), arguments.Contains("--dry-run"))),
        new("migrate", "vault", "[--from <season>] [--vault <name>] [--hardcore] [--dry-run] [--tables <old tables>] [--again]", "Migrate an older season's vault into the installed one, under its own name: items, folders, notes, grail, gold, history, play mode; a report beside the vault. Without --from, lists the vaults waiting", () => MigrateVault(GameRoot(), Option("--from"), arguments.Contains("--hardcore"), arguments.Contains("--dry-run"), Option("--tables"), arguments.Contains("--again"), Option("--vault"))),
        new("pastvault", "vault", "<stash|save> --season <season> --tables <its tables> [--take <n>] [--name <vault>]", "An older season's sample vault, for testing the migration: items, a folder, a find and gold", () => PastVault(GameRoot(), Positional(1), Option("--season"), Option("--tables"), int.TryParse(Option("--take"), out int take) ? take : 40, Option("--name"))),
        new("snapshot", "vault", "[--out <dir>]", "Keep the installed season's tables outside the game, before a patch replaces them", () => Snapshot(GameRoot(), Option("--out"))),
        new("vaults", "vault", "[--split] [--hardcore] [--merge]", "The installed season's vault files: each mode's vaults, and what older layouts left to bring in", () => VaultFiles(GameRoot(), arguments.Contains("--split"), arguments.Contains("--hardcore"), arguments.Contains("--merge"))),
        new("treevault", "vault", "<save.d2s> --file <vault> [--most <n>]", "A vault with something in every folder the smart sort makes, for looking at the vault's tree: every unique, set piece and runeword, and bases plain, magic, rare and crafted in every family - into a vault file of its own, never a player's", () => TreeVault(GameRoot(), Positional(1), Option("--file"), int.TryParse(Option("--most"), out int most) ? most : 2)),
        new("smartsort", "vault", "<staged> [--rules <rulesFile>] [--all]", "Where smart sort would file each item in a staged folder", () => SmartSortCommand(GameRoot(), Positional(1), Option("--rules"), arguments.Contains("--all"))),
        new("stageitems", "vault", "<sources...>", "Rebuild every item in cached armory folders once and write it out for staging", () => StageItems(GameRoot(), Named())),
        new("vault", "vault", "[--file <vault>] [--vault <name>] [--hardcore] [--add <save|stash>] [--list [--by <Stored|Name|Rarity|Type>] [--desc] [--find <text>]] [--check] [--empty] [--tree] [--mkdir <A/B>] [--rename <A/B> --as <name>] [--mvdir <A/B> --to <C|/>] [--put <key,key> --to <A/B|/>] [--rmdir <A/B> [--all]]", "The item vault: copy items in, list, check blobs, folders", () => VaultCommand(GameRoot(), Option("--file"), Option("--add"), arguments.Contains("--list"), arguments.Contains("--check"), Option, arguments.Contains)),
        new("vaultrename", "vault", "<path> <from> <to>", "Rename a character in a vault's records", () => VaultRenameCommand(GameRoot(), Positional(1), Positional(2), Positional(3))),
        new("density", "game", "<multiplier>", "Check the density override's round trip and arithmetic (writes nothing)", () => Density(GameRoot(), double.TryParse(Positional(1), out double n) ? n : 5)),
        new("daggertest", "game", "[--remove]", "Install (or --remove) the dagger test recipe in the game folder: a Scroll of Identify cubed into a magic Dagger at item level 99", () => DaggerTestCommand(GameRoot(), arguments.Contains("--remove"))),
        new("desectest", "game", "[--remove]", "Install (or --remove) the desecration test recipes in the game folder", () => DesecrationTestCommand(GameRoot(), arguments.Contains("--remove"))),
        new("drops", "game", "[--every] [--materials <materials>] [--runes <highRunes>] [--boost <key=k>]... [--odds] [--out <output>]", "Build the drop settings' treasureclassex.txt into a file (--out); nothing installed. --runes sets Lem to Zod, --boost one drop panel entry (lpp=20, r33=5); --odds prints every entry's odds before and after", () => Drops(GameRoot(), arguments.Contains("--every"), int.TryParse(Option("--materials"), out int m) ? m : 1, int.TryParse(Option("--runes"), out int r) ? r : 1, Options("--boost"), arguments.Contains("--odds"), Option("--out"))),
        new("adopttest", "check", "<path> <listingId>", "A cached listing's item taken into a character, in memory only", () => AdoptTest(GameRoot(), Positional(1), Positional(2))),
        new("affixtest", "check", "[--corpus <corpus>] [<single>] [--why] [--tables <dir>]", "The affix search on real items whose affixes are known", () => AffixSearchTest(GameRoot(), Tables(), Option("--corpus"), arguments.Count > 1 && !arguments[1].StartsWith("--") ? arguments[1] : null)),
        new("armorystress", "check", "<names...> | --all [--out <folder>] [--template <save>] [--quick] [--verbose]", "Build ladder characters from cached armory JSON: every item whole and silent", () => ArmoryStress(GameRoot(), [.. arguments.Skip(1).Where((each, at) => !each.StartsWith("--") && !(at > 0 && arguments[at].StartsWith("--")))], Option("--template"), Option("--out")), Writes: true),
        new("beltcheck", "check", "<path>", "A save's belt against the belt rules", () => BeltCheck(GameRoot(), Positional(1))),
        new("beltrules", "check", "", "The belt rules' table: columns and potions per belt", () => BeltRules()),
        new("blob", "check", "<save|stash> | --corpus <dir>", "Capture every item as a vault blob and prove it reads back", () => Blob(GameRoot(), Option("--corpus"), arguments.Count > 1 && !arguments[1].StartsWith("--") ? arguments[1] : null)),
        new("cleancheck", "check", "[--baseline]", "Every enabled unique built, corrupted and cleaned: the legitimacy check stays silent", () => CleanCheck(GameRoot())),
        new("cubecheck", "check", "<folder>", "The editor's Horadric Cube against the game's own", () => CubeCheck(GameRoot(), Positional(1))),
        new("desecscan", "check", "<folder> [--verbose]", "Every desecrated listing in a folder of market pages, rebuilt", () => DesecrationScan(GameRoot(), Positional(1), arguments.Contains("--verbose"))),
        new("clipcheck", "check", "<save.d2s> <pasted.txt>...", "The game's Ctrl+C item texts read back into items, each held against the one in the save it was copied from", () => ClipCheck(GameRoot(), Positional(1), [.. Named().Skip(1)])),
        new("forgeword", "check", "<name>", "One runeword forged in its best base, and the check on it", () => ForgeWord(GameRoot(), Positional(1))),
        new("guard", "check", "<saveFolder>", "The patch guard's check: the build fingerprint, and every save in the folder read back", () => Guard(GameRoot(), Positional(1))),
        new("journalcheck", "check", "<save> <folder>", "The move journal on a save and a scratch folder", () => JournalCheck(GameRoot(), Positional(1), Positional(2)), Writes: true),
        new("leftoff", "check", "<folders...>", "Every saved trade listing rebuilt as Buy rebuilds it, and what is left over", () => LeftOff(GameRoot(), Named())),
        new("legit", "check", "<save|stash> | --corpus <dir> [--items] [--page <n>] [--top <n>] [--tables <dir>]", "Check items against what the game could roll (legitimacy)", () => Legit(GameRoot(), Tables(), Option("--corpus"), arguments.Count > 1 && !arguments[1].StartsWith("--") ? arguments[1] : null)),
        new("leveltest", "check", "<path>", "The level editor on a save, in memory only", () => LevelTest(GameRoot(), Positional(1))),
        new("linktest", "check", "", "Smart sort's folder links on a scratch vault", () => LinkTest(GameRoot())),
        new("listingcheck", "check", "<file>", "One saved market listing (or page) rebuilt as Buy rebuilds it", () => ListingCheck(GameRoot(), Positional(1))),
        new("damagekinds", "check", "<save|stash|folder...> [--items] [--tables <dir>]", "Which of a flat damage's three copies each item holds, by kind of base, against the rule (DamageKinds)", () => DamageKindsCheck(GameRoot(), Named())),
        new("legitcost", "check", "<save|stash|folder...> [--rounds <n>]", "What the legitimacy check costs per item, rule by rule", () => LegitCost(GameRoot(), Named())),
        new("props", "check", "", "Every property that writes several fields, checked field by field", () => Props(GameRoot())),
        new("reqsim", "check", "<path>", "Requirements simulated for every worn item in a save", () => ReqSim(GameRoot(), Positional(1), [.. arguments.Skip(2).Where(each => each.Contains('='))])),
        new("rollcheck", "check", "<sources...> [--skipped] [--twice] [--verbose] [--bytes]", "Reroll every item in the sources many times: the legitimacy check stays silent; a hash of the results, --bytes each one", () => RollCheck(GameRoot(), Named())),
        new("rwbuild", "check", "[--rolls <rolls>]", "The runeword builder over every word: built, rerolled, silent", () => RunewordBuildTest(GameRoot(), int.TryParse(Option("--rolls"), out int rwRolls) ? rwRolls : 3)),
        new("rwon", "check", "<path> [--item <number>] [--word <wordName>]", "A runeword built on a save's own item, and what the legitimacy check says", () => RunewordOn(GameRoot(), Positional(1), int.Parse(Option("--item") ?? "0"), Option("--word") ?? "")),
        new("safewritecheck", "check", "<folder>", "The safe-write path on a scratch folder: replace, fail, recover", () => SafeWriteCheck(Positional(1)), Writes: true),
        new("setlists", "check", "<sources...>", "Set pieces' green lists against their table rows", () => SetLists(GameRoot(), Named())),
        new("soulstonecheck", "check", "<places...>", "Mephisto's Soulstone as the editor makes it against real ones", () => SoulstoneCheck(GameRoot(), Named())),
        new("stats", "check", "[--out <outPath>]", "Every property as a spreadsheet, with a column to write in", () => Stats(GameRoot(), Option("--out")), Writes: true),
        new("thincheck", "check", "<folder>", "Backup thinning on a scratch folder", () => ThinCheck(Positional(1)), Writes: true),
        new("tradeaffixes", "check", "[--captured] [--criteria] [--search] [--why]", "Every cached rare or crafted listing rebuilt: are its affixes found", () => TradeAffixes(GameRoot())),
        new("unequiptest", "check", "<path>", "Take every worn item off a save in memory; it still reads back", () => UnequipTest(GameRoot(), Positional(1))),
        new("upgradecheck", "check", "<sources...> [--show] [--verbose]", "Every unique and set piece upgraded a tier: the legitimacy check stays silent", () => UpgradeCheck(GameRoot(), Named())),
        new("vaultundocheck", "check", "", "Seven vault actions undone and redone on a scratch vault", () => VaultUndoCheck(GameRoot())),
        new("seasonfacts", "check", "", "What the code knows about this season that no table says, read against the installed tables", () => SeasonFactsCommand(GameRoot())),
        new("lastseason", "check", "<save|stash> --out <file>", "The file in last season's encoding, as the next season's first launch will find it: for testing a rollover", () => LastSeason(GameRoot(), Positional(1), Option("--out")), Writes: true),
        new("padseason", "check", "<pad folder> --layout-from <this season's pd2_shared.stash>", "A test pad of the season before brought into the installed one, in place: vaults and characters migrated, the shared stash in this season's layout, every item made this season's - for the Shell tests, never a player's files", () => PadSeason(GameRoot(), Positional(1), Option("--layout-from")), Writes: true),
        new("verify", "check", "<save|stash> | --corpus <dir> [--explain] [--dump <file>] [--trace]", "Parse, re-serialize and byte-compare a save, stash or folder", () => Verify(GameRoot(), Option("--corpus"), arguments.Count > 1 && !arguments[1].StartsWith("--") ? arguments[1] : null)),
        new("figuretime", "research", "<folder>", "How long each character's figure takes: the save read, the look, the layers composed, the frames enlarged", () => FigureTime(GameRoot(), Positional(1))),
        new("figuretest", "research", "<save.d2s> [--case <affixes|orb-alone|orb-suffix|shield-prefix>] [--era-from <save>] [--read]", "Gear a character for the figure's open questions (graphic bytes, worn affix and automagic colours); --read prints what the game wrote", () => FigureTest(GameRoot(), Positional(1), arguments.Contains("--read"))),
        new("armoryfetch", "research", "<ladderPath> [--plan <plan>] [--out <root>]", "Fetch many armory characters from the PD2 site into a scratch folder (network)", () => ArmoryFetch(Positional(1), Option("--plan") ?? "softcore:50", Option("--out"))),
        new("autoids", "research", "[--corpus <corpus>] [<single>] [--tables <dir>]", "Every item's base, quality and automagic id", () => AutomagicIds(GameRoot(), Tables(), Option("--corpus"), arguments.Count > 1 && !arguments[1].StartsWith("--") ? arguments[1] : null)),
        new("chargescan", "research", "[--corpus <corpus>] [<single>] [--tables <dir>]", "Every runeword item, one tab-separated line each", () => ChargeScan(GameRoot(), Tables(), Option("--corpus"), arguments.Count > 1 && !arguments[1].StartsWith("--") ? arguments[1] : null)),
        new("questsurvey", "research", "<folder> [--quest <only>]", "Quest flag words across every save in a folder", () => QuestSurvey(GameRoot(), Positional(1), Option("--quest"))),
        new("rwids", "research", "[--corpus <corpus>] [<single>] [--tables <dir>]", "Runeword ids against the string table", () => RunewordIds(GameRoot(), Tables(), Option("--corpus"), arguments.Count > 1 && !arguments[1].StartsWith("--") ? arguments[1] : null)),
    ];

    private static int Main(string[] args)
    {
        arguments = [.. args];
        string name = arguments.Count > 0 ? arguments[0] : "help";

        if (name is "help" or "--help" or "-h" or "/?")
            return Help(arguments.Count > 1 ? arguments[1] : null);

        // The build, as the editor's Settings page says it.
        if (name is "version" or "--version" or "-v")
        {
            Console.WriteLine($"pd2spc {AppVersion.Label}  ({AppVersion.Built})");
            return 0;
        }

        if (Commands.FirstOrDefault(each => each.Name == name) is not { } command)
        {
            Console.Error.WriteLine($"unknown command '{name}'. pd2spc help lists them.");
            return 2;
        }

        if (arguments.Contains("--help") || arguments.Contains("-h"))
            return Help(name);

        try
        {
            // In here: a path the gate cannot read is wrong usage, said as such, and no crash.
            if (RefusedWhileGameRuns(command) is { } refused)
            {
                Console.Error.WriteLine(refused);
                return 1;
            }

            return command.Run();
        }
        catch (ArgumentException wrong)
        {
            // Wrong usage: a missing or bad argument. Said with the command's usage.
            Console.Error.WriteLine($"error: {wrong.Message}");
            Console.Error.WriteLine($"usage: pd2spc {command.Name} {command.Usage}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");

            // The whole thing when asked: a message alone rarely says which of a dozen table
            // lookups went looking for something that was not there.
            if (arguments.Contains("--trace"))
                Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>Every command by group, or one command's usage. Unknown: exit 2.</summary>
    private static int Help(string? name)
    {
        if (name is not null)
        {
            if (Commands.FirstOrDefault(each => each.Name == name) is not { } one)
            {
                Console.Error.WriteLine($"unknown command '{name}'.");
                return 2;
            }

            Console.WriteLine($"pd2spc {one.Name} {one.Usage}".TrimEnd());
            Console.WriteLine($"  {one.Summary}");
            Console.WriteLine($"  group: {one.Group}");
            return 0;
        }

        Console.WriteLine("pd2spc - Project Diablo 2 editor tooling");
        Console.WriteLine();
        Console.WriteLine("  pd2spc help [command]      this list, or one command's usage");
        Console.WriteLine("  pd2spc --version           the build: version, date and commit");
        Console.WriteLine("  common options: --game <dir> (else PD2SPC_GAME), --tables <dir>, --trace");
        Console.WriteLine("  exit codes: 0 clean, 1 finding/refusal/error, 2 wrong usage");
        foreach (var (group, says) in Groups)
        {
            Console.WriteLine();
            Console.WriteLine($"{group} - {says}");
            foreach (var each in Commands.Where(command => command.Group == group))
                Console.WriteLine($"  {each.Name,-16} {each.Summary}");
        }

        return 0;
    }

    /// <summary>
    /// The game folder a command was told of, or null: --game, else PD2SPC_GAME. PD2 installs alongside the vanilla game, so
    /// this is the game root, not the ProjectD2 subfolder.
    /// </summary>
    static string? GameRootGiven() =>
        Option("--game") ?? (Environment.GetEnvironmentVariable("PD2SPC_GAME") is { Length: > 0 } set ? set : null);

    /// <summary>The game folder, for a command that reads it: wrong usage where none was given, as there is no guessing it.</summary>
    static string GameRoot() =>
        GameRootGiven() ?? throw new ArgumentException("no game folder: give --game <dir> or set PD2SPC_GAME");

    /// <summary>
    /// Why a command may not run now, or null: the game running from the game folder while the command would write
    /// there - a "game" group command, which writes into the game folder itself, or any other but a look or a check
    /// named with a path inside it (the command line wrote saves with no look).
    /// A look or a check goes free for reading and for its scratch copies, but not a check that writes to a path it is
    /// named (<see cref="Command.Writes"/>): that one is asked as the others are.
    /// Copies elsewhere stay free while the game runs; --game and --tables only name what is read.
    /// With no game folder given nothing can say a path is outside it, so any game running refuses; with none running, a
    /// command that reads the game says what it needs (<see cref="GameRoot"/>) and one that does not goes on.
    /// </summary>
    static string? RefusedWhileGameRuns(Command command)
    {
        if (command.Group is "look" or "check" && !command.Writes)
            return null;

        if (GameRootGiven() is not { } given)
        {
            return PD2SPCompanion.Core.Saves.GameProcess.IsRunningFrom("")
                ? "refused: the game is running and no game folder is given (--game or PD2SPC_GAME). Close it, or name the folder."
                : null;
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(given));
        string? inside = null;
        if (command.Group != "game")
        {
            inside = Words(ReadOnlyOptions).FirstOrDefault(each => Inside(each, root));
            if (inside is null)
                return null;
        }

        if (!PD2SPCompanion.Core.Saves.GameProcess.IsRunningFrom(root))
            return null;

        return inside is null
            ? $"refused: the game is running from {root}. Close it first."
            : $"refused: the game is running and {inside} is in its folder. Close it first.";

        static bool Inside(string path, string root)
        {
            try
            {
                return Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // A folder of loose .txt tables, read ahead of the MPQ chain exactly as the game's -direct
    // does. This is how an item from an older season gets read with the tables it was made
    // under: the season's balance, affix ids and stat widths all have to match the item, and
    // the installed season's will not.
    static string? Tables() => Option("--tables");

    static string? Option(string name)
    {
        int i = arguments.IndexOf(name);
        return i >= 0 && i + 1 < arguments.Count ? arguments[i + 1] : null;
    }

    // Every value of an option that may be given more than once: --unique A --unique B.
    static List<string> Options(string name) =>
        [.. arguments.Select((each, i) => (each, i))
                     .Where(entry => entry.each == name && entry.i + 1 < arguments.Count)
                     .Select(entry => arguments[entry.i + 1])];

    /// <summary>The options of every command that only name what is read, each with a value after it.</summary>
    private static readonly string[] ReadOnlyOptions = ["--game", "--tables"];

    /// <summary>
    /// What a command is pointed at: the words after its name, with every option and every option's value left out - the
    /// ones its usage writes as <c>--option &lt;value&gt;</c>, and the common two. A <c>--game</c>'s folder taken for a save, or
    /// walked for saves, is what this ends.
    /// </summary>
    static List<string> Named()
    {
        string usage = Commands.First(each => each.Name == arguments[0]).Usage;
        return Words([.. ReadOnlyOptions, .. System.Text.RegularExpressions.Regex.Matches(usage, @"(--[a-z-]+) <").Select(match => match.Groups[1].Value)]);
    }

    /// <summary>The words after the command's name that are no option, nor the value of one of those given.</summary>
    static List<string> Words(IReadOnlyCollection<string> takingValue)
    {
        var words = new List<string>();
        for (int at = 1; at < arguments.Count; at++)
        {
            if (!arguments[at].StartsWith("--"))
                words.Add(arguments[at]);
            else if (takingValue.Contains(arguments[at]))
                at++;
        }
        return words;
    }

    static string Positional(int index) =>
        index < arguments.Count && !arguments[index].StartsWith("--")
            ? arguments[index]
            : throw new ArgumentException($"missing argument {index}");

}
