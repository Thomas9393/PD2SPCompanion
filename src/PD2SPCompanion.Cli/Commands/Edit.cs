using System.Text;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Testbed;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Cli;

// Commands that write a save or stash. Each takes a backup first; many take --dry-run.
internal static partial class Cli
{
    // Builds an armory character into a save - to --out, never the save folder unless named there -
    // on a template save, and reads it back.
    static int ArmoryCommand(string gameRoot, string? who, string? template, string? output, string? name)
    {
        if (who is null || template is null || output is null)
            throw new ArgumentException("armory <name or link> --template <save.d2s> --out <new.d2s> [--name New]");
        if (File.Exists(output))
            throw new ArgumentException($"{output} exists; nothing is written over a character");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        string wanted = ArmoryImport.NameIn(who) ?? throw new ArgumentException("not a character name or armory link");
        var armory = ArmoryImport.Fetch(wanted).GetAwaiter().GetResult();
        Console.WriteLine($"armory: {armory.Name}, {armory.ClassName} level {armory.Level}, {armory.Items.Count} items, mercenary items {armory.MercenaryItems.Count}");

        string newName = name ?? Path.GetFileNameWithoutExtension(output);
        var (bytes, notes) = ArmoryImport.Build(game, template, armory, newName);
        SaveWriter.WriteSafely(output, bytes);
        foreach (string note in notes) Console.WriteLine($"  note: {note}");

        var (save, result) = SaveVerifier.Load(output);
        Console.WriteLine($"wrote {output}: {bytes.Length} bytes, reads back as {result.Format}; '{save.Name}' class {save.ClassId} level {save.Level}, {save.PlayerItemList.Items.Count} items, mercenary {save.MercenaryItemList?.ItemList?.Items.Count ?? 0}");
        foreach (var (stat, value) in save.Attributes.Stats)
            Console.WriteLine($"  {stat,-12} {AttributeScale.Display(stat, value)}");
        return 0;
    }

    // The armory import offline: a cached armory answer (TestCorpus/ladder) built on a template, as the
    // armory command builds a fetched one - so a change to the build can be compared before and after.
    static int ArmoryBuildCommand(string gameRoot, string? json, string? template, string? output, string? name)
    {
        if (json is null || template is null || output is null)
            throw new ArgumentException("armorybuild <armory.json> --template <save.d2s> --out <new.d2s> [--name New]");
        if (File.Exists(output))
            throw new ArgumentException($"{output} exists; nothing is written over a character");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
        var armory = new ArmoryImport.Armory(document.RootElement.Clone());
        var (bytes, notes) = ArmoryImport.Build(game, template, armory, name ?? Path.GetFileNameWithoutExtension(output));
        SaveWriter.WriteSafely(output, bytes);
        foreach (string note in notes) Console.WriteLine($"  note: {note}");

        var (_, result) = SaveVerifier.Load(output);
        Console.WriteLine($"wrote {output}: {bytes.Length} bytes, reads back as {result.Format}");
        return 0;
    }

    // Character creation, as the Experimental page's section does it: a class, a
    // level, a name from the output file, and --quests for every quest done with the three item services
    // waiting. For in-game tests: --complete completes quests in Normal as the Character page's Quests
    // section does (QuestProgress.Complete - done with the editor's gift, or one of the three services
    // left waiting), and --waypoints
    // Normal|all grants waypoints. Written as the editor writes a new character - never over one that
    // exists. Reads the result back and prints what matters to the game.
    static int CreateCommand(string gameRoot, string? className, string? levelText, bool questsDone, bool hardcore, string? template, string? output,
                      string? complete, string? grantWaypoints, bool dead = false, string? golem = null, string? golemBy = null)
    {
        if (className is null || template is null || output is null)
            throw new ArgumentException("create --class <Amazon..Assassin> [--level N] [--quests] [--hardcore [--dead]] [--complete ActI.DenOfEvil,...] "
                                        + "[--waypoints Normal|all] --template <save.d2s> --out <Name.d2s>");
        if (File.Exists(output))
            throw new ArgumentException($"{output} exists; nothing is written over a character");

        int classId = CharacterClasses.Names.ToList().FindIndex(each => each.Equals(className, StringComparison.OrdinalIgnoreCase));
        if (classId < 0)
            throw new ArgumentException($"no class '{className}'");
        int level = int.TryParse(levelText, out int parsed) ? parsed : 1;

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        byte[] bytes = CharacterCreation.Build(game, template, Path.GetFileNameWithoutExtension(output), (byte)classId, level, hardcore, questsDone);

        if (complete is not null || grantWaypoints is not null)
        {
            var built = D2SLib.Core.ReadD2S(bytes);
            foreach (string key in (complete ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var quest = QuestProgress.All.FirstOrDefault(each => each.Key == key) ?? throw new ArgumentException($"no quest '{key}'");
                QuestProgress.Complete(built, 0, quest);
            }
            foreach (var difficulty in WaypointProgress.Read(built))
            {
                if (grantWaypoints is not null && (grantWaypoints.Equals("all", StringComparison.OrdinalIgnoreCase)
                                                   || grantWaypoints.Equals(difficulty.Difficulty, StringComparison.OrdinalIgnoreCase)))
                    difficulty.EnableAll();
            }
            bytes = SaveWriter.Prepared(built);
        }

        // A dead hardcore character, for testing revive.
        if (dead && hardcore)
        {
            var killed = D2SLib.Core.ReadD2S(bytes);
            killed.Status.IsDead = true;
            bytes = SaveWriter.Prepared(killed);
        }

        // An Iron Golem standing, made of a plain base, for testing its panel and its dismissal: a point in its skill and in what that needs, where
        // the class has the skill - the game refuses a golem without it.
        if (golem is not null)
        {
            var summoner = D2SLib.Core.ReadD2S(bytes);
            var skills = new SkillEditor(summoner, SkillTree.For(game, summoner.ClassId));
            if (golemBy is not null)
            {
                // The skill from an item worn, no point in it: PD2's Metalgrid gives it as an other class's skill.
                var worn = ItemForge.Unique(golemBy, "103").Item;
                worn.Mode = ItemMode.Equipped;
                worn.Location = ItemLocation.Neck;
                worn.Page = 0;
                summoner.PlayerItemList.Items.Add(worn);
                summoner.PlayerItemList.Count = (ushort)summoner.PlayerItemList.Items.Count;
            }
            else if (skills.Tree.IronGolemSlot is { } slot)
            {
                skills.AddUnspent(30);
                var chain = new List<SkillEntry>();
                void Need(SkillEntry skill)
                {
                    foreach (int id in skill.Requires)
                        Need(skills.Tree.BySlot.Values.First(each => each.Id == id));
                    if (!chain.Contains(skill))
                        chain.Add(skill);
                }
                Need(skills.Tree.BySlot[slot]);
                foreach (var skill in chain)
                    skills.Add(skill, 1, 99, freely: true, withoutPrerequisites: true);
            }

            summoner.Golem.Exists = true;
            summoner.Golem.Item = ItemForge.PlainBase(golem, 0, "103").Item;
            bytes = SaveWriter.Prepared(summoner);
        }

        SaveWriter.WriteSafely(output, bytes);

        var (save, result) = SaveVerifier.Load(output);
        if (save.Golem is { Exists: true, Item: { } made })
            Console.WriteLine($"  iron golem  made of {made.Code.Trim()}");
        Console.WriteLine($"wrote {output}: {bytes.Length} bytes, reads back as {result.Format}; '{save.Name}' {CharacterClasses.Names[save.ClassId]} level {save.Level}, "
                          + $"{(save.Status.IsHardcore ? "hardcore" : "softcore")}{(save.Status.IsDead ? ", dead" : "")}, progression {save.Progression}, items {save.PlayerItemList.Items.Count}, "
                          + $"mercenary {save.Mercenary.Id != 0}");
        foreach (var (stat, value) in save.Attributes.Stats)
            Console.WriteLine($"  {stat,-12} {(stat == "experience" ? unchecked((uint)value) : AttributeScale.Display(stat, value))}");

        var waypoints = WaypointProgress.Read(save);
        Console.WriteLine("  items       " + string.Join(", ", save.PlayerItemList.Items.Select(item => $"{item.Code.Trim()} v{item.Version} ilvl {item.ItemLevel} at {item.X},{item.Y}")));
        Console.WriteLine("  waypoints   " + string.Join(", ", waypoints.Select(each => $"{each.Difficulty} {each.Acts.Sum(act => act.Found)}")));
        var shown = QuestProgress.All.Where(each => each.Waits || QuestProgress.Gifts.ContainsKey(each.Key) || each.Key == QuestProgress.Forge
                                                    || each.Key is "ActV.RescueOnMountArreat" or "ActV.PrisonOfIce")
                                     .Append(new QuestProgress.Info(5, "ActV", "Completion", "(respec slot)", ""));
        foreach (var quest in shown)
        {
            var words = Enumerable.Range(0, 3).Select(d => $"{QuestProgress.WordOf(QuestProgress.QuestOf(save.Quests, d, quest))} {QuestProgress.StateOf(QuestProgress.QuestOf(save.Quests, d, quest))}");
            Console.WriteLine($"  {quest.Name,-26} {string.Join("  ", words)}");
        }
        return 0;
    }

    // Opens every act in one difficulty for a test character: a character made with no
    // quests has only Act I, and the waypoints of the later towns are no use until the game counts
    // the act before as finished. Writes each act's introduction and the first four acts' completion
    // as the game writes them done (QuestProgress.DoneWords), and Terror's End - Act V opens only with
    // Diablo dead: in 456 of 456 corpus difficulties with Act V open it is done, and Act IV's
    // completion alone left it shut in game - only where the word is still nought,
    // so nothing the game wrote is replaced: Act V's completion slot holds Akara's respec flag.
    // Written the editor's way over the character: a timestamped backup first, the round trip checked.
    static int OpenActsCommand(string gameRoot, string? path, string difficultyName)
    {
        if (path is null)
            throw new ArgumentException("openacts <save.d2s> [--difficulty Normal|Nightmare|Hell]");

        int difficulty = Array.FindIndex(["Normal", "Nightmare", "Hell"], each => each.Equals(difficultyName, StringComparison.OrdinalIgnoreCase));
        if (difficulty < 0)
            throw new ArgumentException($"no difficulty '{difficultyName}'");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var level = difficulty switch { 0 => save.Quests.Normal, 1 => save.Quests.Nightmare, _ => save.Quests.Hell };
        string[] keys = ["ActI.Introduction", "ActI.Completion", "ActII.Introduction", "ActII.Completion", "ActIII.Introduction",
                         "ActIII.Completion", "ActIV.Introduction", "ActIV.TerrorsEnd", "ActIV.Completion", "ActV.Introduction"];
        foreach (string key in keys)
        {
            string[] parts = key.Split('.');
            var act = typeof(QuestsDifficulty).GetProperty(parts[0])!.GetValue(level)!;
            var quest = (Quest)act.GetType().GetProperty(parts[1])!.GetValue(act)!;
            string before = QuestProgress.WordOf(quest);
            if (before.Contains('1'))
            {
                Console.WriteLine($"  {key,-20} {before} kept");
                continue;
            }
            QuestProgress.SetWord(quest, QuestProgress.DoneWords[key][difficulty]);
            Console.WriteLine($"  {key,-20} {before} -> {QuestProgress.WordOf(quest)}");
        }

        string backup = SaveWriter.Replace(path, SaveWriter.Prepared(save));
        Console.WriteLine($"written {path}; backup {backup}");
        return 0;
    }

    // Fills every free cell of a test character's inventory, cube and personal stash with chipped
    // amethysts (1x1, compact), for the no-room tests (an ejected gem with nowhere to go
    // stays in its socket). For copies only - it says so and writes with a backup.
    static int FillCommand(string gameRoot, string? path)
    {
        if (path is null)
            throw new ArgumentException("fill <save.d2s>  (a test copy)");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var list = save.PlayerItemList;
        var layout = InventoryLayout.Load(game, save.ClassId, save.Status.IsExpansion);
        string era = EraOf(save);
        int added = 0;

        foreach (var (page, size) in new[] { (InventoryLayout.InventoryPage, layout.Inventory), (InventoryLayout.CubePage, layout.Cube), (layout.StashPage, layout.Stash) })
        {
            while (true)
            {
                var gem = ItemForge.Material("gcv", 1, era);
                var stored = list.Items.Where(item => item.Mode == ItemMode.Stored && item.Page == page);
                if (ItemMover.FirstFreeCell(gem, stored, size) is not { } cell)
                    break;
                gem.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
                ItemMover.Place(gem, page, cell.X, cell.Y);
                list.Items.Add(gem);
                added++;
            }
        }
        list.Count = (ushort)list.Items.Count;

        string backup = SaveWriter.Replace(path, SaveWriter.Prepared(save));
        Console.WriteLine($"filled {added} cells in {path}; backup {backup}");
        return 0;
    }

    // Gives a test character a quest item (Lam Esen's reward is paid for the tome handed to
    // Alkor, not from a waiting word), made as ItemForge.QuestItem makes the soulstone, in the
    // inventory's first room. --start puts that quest in Normal back to started, as Alkor leaves it,
    // unless it is done.
    // Written the editor's way over the character: a timestamped backup first, the round trip checked.
    static int QuestItemCommand(string gameRoot, string? path, string? code, string? start, string? difficultyText)
    {
        if (path is null || code is null)
            throw new ArgumentException("questitem <save.d2s> <code> [--difficulty 0-2] [--start ActIII.LamEsensTome]");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var list = save.PlayerItemList;

        // As the game makes one (ItemForge.QuestItem, byte-identical to its soulstones), for the difficulty given.
        var made = ItemForge.QuestItem(code, int.TryParse(difficultyText, out int difficulty) ? difficulty : 0, EraOf(save));
        Console.WriteLine($"  made {code}, difficulty {made.NumberOfSocketedItems}, v{made.Version}");

        var stored = list.Items.Where(item => item.Mode == ItemMode.Stored && item.Page == InventoryLayout.InventoryPage);
        var cell = ItemMover.FirstFreeCell(made, stored, InventoryLayout.Load(game, save.ClassId, save.Status.IsExpansion).Inventory)
                   ?? throw new ArgumentException("no room in the inventory");
        ItemMover.Place(made, InventoryLayout.InventoryPage, cell.X, cell.Y);
        list.Items.Add(made);
        list.Count = (ushort)list.Items.Count;
        Console.WriteLine($"  placed at {cell.X},{cell.Y}");

        if (start is not null)
        {
            var info = QuestProgress.All.FirstOrDefault(each => each.Key == start) ?? throw new ArgumentException($"no quest '{start}'");
            var quest = QuestProgress.QuestOf(save.Quests, 0, info);
            string before = QuestProgress.WordOf(quest);
            if (before[0] == '1')
                Console.WriteLine($"  {start} {before} done, kept");
            else
            {
                QuestProgress.SetWord(quest, "..1.............");
                Console.WriteLine($"  {start} {before} -> {QuestProgress.WordOf(quest)}");
            }
        }

        string backup = SaveWriter.Replace(path, SaveWriter.Prepared(save));
        Console.WriteLine($"written {path}; backup {backup}");
        return 0;
    }

    // Renames a character's files - for a copy of a save folder, never the live one by accident:
    // the path is given in full. Reads the result back and says what is where.
    static int RenameCommand(string gameRoot, string? path, string? name)
    {
        if (path is null || name is null)
            throw new ArgumentException("rename <save.d2s> <new name>");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        string after = CharacterRename.Rename(Path.GetFullPath(path), name);
        var (save, result) = SaveVerifier.Load(after);
        Console.WriteLine($"renamed to {after}; name inside '{save.Name}', level {save.Level}, {save.PlayerItemList?.Items.Count} items; {result.Format}");
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(after)!).OrderBy(f => f))
            Console.WriteLine($"  {Path.GetFileName(file)}");
        string backups = SaveWriter.BackupFolderFor(after);
        if (Directory.Exists(backups))
            foreach (string file in Directory.GetFiles(backups).OrderBy(f => f))
                Console.WriteLine($"  backup: {Path.GetFileName(file)}");
        return 0;
    }

    static int Set(string gameRoot, string path)
    {
        string stat = Option("--stat") ?? throw new ArgumentException("set needs --stat <name>");
        string itemText = Option("--item") ?? throw new ArgumentException("set needs --item <n>, as shown by dump");
        string valueText = Option("--value") ?? throw new ArgumentException("set needs --value <v>");
        bool dryRun = arguments.Contains("--dry-run");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var outcome = SaveWriter.SetItemStat(path, int.Parse(itemText), stat, int.Parse(valueText), dryRun);

        Console.WriteLine(outcome.Description);
        if (outcome.Written)
        {
            Console.WriteLine($"  backup:  {outcome.BackupPath}");
            Console.WriteLine($"  written: {path}");
        }
        else
        {
            Console.WriteLine("  dry run - verified but nothing written");
        }
        return 0;
    }

    static int AddStat(string gameRoot, string path)
    {
        string itemText = Option("--item") ?? throw new ArgumentException("addstat needs --item <n>, as shown by dump");
        string idText = Option("--id") ?? throw new ArgumentException("addstat needs --id <stat id>");
        bool dryRun = arguments.Contains("--dry-run");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var outcome = SaveWriter.AddItemStat(path, int.Parse(itemText), int.Parse(idText), dryRun,
            Option("--value") is { } value ? int.Parse(value) : null, Option("--skill") is { } skill ? int.Parse(skill) : null);

        Console.WriteLine(outcome.Description);
        if (outcome.Written)
        {
            Console.WriteLine($"  backup:  {outcome.BackupPath}");
            Console.WriteLine($"  written: {path}");
        }
        else
        {
            Console.WriteLine("  dry run - verified but nothing written");
        }
        return 0;
    }

    // The affix pairs an item holds, and what could sit in a slot instead.
    static int Affixes(string gameRoot, string path)
    {
        string itemText = Option("--item") ?? throw new ArgumentException("affix needs --item <n>");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var from = arguments.Contains("--merc")
            ? save.MercenaryItemList?.ItemList?.Items ?? []
            : save.PlayerItemList?.Items ?? [];

        var item = from[int.Parse(itemText)];

        Console.WriteLine($"item #{itemText} ({item.Code?.Trim()}, {item.Quality}, ilvl {item.ItemLevel})");

        if (!MagicAffixes.Carries(item))
        {
            Console.WriteLine("  carries no magic affixes");
            return 0;
        }

        // What the affixes may ask for: the base's affix level, and every lower tier's where the
        // cube could have upgraded it - and the affix, if any, beyond all of them.
        Console.WriteLine($"  affix level {MagicAffixes.AffixLevel(item)}, across upgrades "
                          + $"{string.Join("/", MagicAffixes.AffixLevelsAcrossUpgrades(item))}; "
                          + $"out of reach: {MagicAffixes.OutOfReach(item)?.Name ?? "none"}");

        foreach (var pair in MagicAffixes.PairsOn(item))
        {
            Console.WriteLine($"  slot {pair.Slot + 1}:  {pair.PrefixName,-22} | {pair.SuffixName}");

            if (pair.Prefix is { } p)
                Console.WriteLine($"           prefix {p.Id,-4} group {p.Group,-4} {p.Readable}");

            if (pair.Suffix is { } x)
                Console.WriteLine($"           suffix {x.Id,-4} group {x.Group,-4} {x.Readable}");
        }

        // --set <id> puts an affix in the slot and makes the item's properties match it.
        if (Option("--set") is { } setText && int.TryParse(setText, out int chosen)
            && Option("--slot") is { } which && int.TryParse(which, out int target))
        {
            bool prefix = !arguments.Contains("--suffix-side");

            var picked = MagicAffixes.Find(chosen, prefix);
            var pick = new MagicAffixes.AffixPick(
                chosen,
                picked is null ? new Dictionary<string, int>() : MagicAffixes.Roll(picked, new Random()));

            ItemSeason.MakeCurrent(item);

            var told = MagicAffixes.Apply(
                item,
                new Dictionary<(int, bool), MagicAffixes.AffixPick> { [(target - 1, prefix)] = pick },
                new Random());

            Console.WriteLine($"  set {(prefix ? "prefix" : "suffix")} slot {target} to {chosen}:");
            foreach (string line in told)
                Console.WriteLine($"    {line}");

            foreach (var pair in MagicAffixes.PairsOn(item))
                Console.WriteLine($"  slot {pair.Slot + 1}:  {pair.PrefixName,-22} | {pair.SuffixName}");

            if (!arguments.Contains("--dry-run"))
            {
                string backup = SaveWriter.Commit(path, save, null);
                Console.WriteLine($"  backup:  {backup}");
            }

            return 0;
        }

        if (Option("--slot") is { } slotText && int.TryParse(slotText, out int slot))
        {
            foreach (bool prefix in new[] { true, false })
            {
                var open = MagicAffixes.Available(item, prefix, slot - 1);
                Console.WriteLine($"  {(prefix ? "prefixes" : "suffixes")} available for slot {slot}: {open.Count}");

                foreach (var option in open.Take(arguments.Contains("--all") ? int.MaxValue : 6))
                    Console.WriteLine($"    {option.Id,-5} {option.Name,-18} g{option.Group,-4} lvl {option.Level,-3} {option.Readable}");
            }
        }

        return 0;
    }

    // Crafts a copy of an item in memory and prints what it had against what it kept. Read-only
    // on purpose: the question it exists for is which of the base's own sources survive the cube -
    // an automagic's implicit, a staff mod's skill, a superior bonus - none of which is an affix,
    // and all of which live in the same property list the affixes do.
    static int Craft(string gameRoot, string path)
    {
        string itemText = Option("--item") ?? throw new ArgumentException("craft needs --item <n>, as shown by dump");

        using var installed = Season.Open(gameRoot, Tables());
        var game = installed.Game;

        var (save, result) = SaveVerifier.Load(path);
        SaveVerifier.Install(result.Format);

        var items = save.PlayerItemList?.Items ?? [];
        int which = int.Parse(itemText);
        if (which < 0 || which >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(which), $"item #{which} does not exist.");

        var item = items[which];

        static void Show(string title, Item shown)
        {
            Console.WriteLine($"  {title}");
            Console.WriteLine($"    {shown.Quality}, ilvl {shown.ItemLevel}, auto affix {shown.AutoAffixId}, "
                              + $"prefix {string.Join("/", shown.MagicPrefixIds ?? [])}, "
                              + $"suffix {string.Join("/", shown.MagicSuffixIds ?? [])}");

            foreach (var stat in shown.StatLists.SelectMany(list => list.Stats))
                Console.WriteLine($"      + {stat.Stat,-28} {stat.Value,6}  (id {stat.Id})");
        }

        Console.WriteLine($"item #{which}  [{item.Code?.Trim()}]  {ItemPresenter.Describe(item).Name}");
        Show("before", item);

        var recipes = AffixRanges.CraftFamilies(item);
        if (recipes.Count == 0)
        {
            Console.WriteLine("  no craft recipe takes this item");
            return 1;
        }

        string? wanted = Option("--family");
        var recipe = wanted is null
            ? recipes[0]
            : recipes.FirstOrDefault(each => string.Equals(each.Family, wanted, StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException(
                  $"no {wanted} recipe for this item; it takes {string.Join(", ", recipes.Select(each => each.Family))}");

        // Fixed by default so a run is repeatable; --seed searches for a particular outcome,
        // such as a craft whose affix writes a stat the automagic already wrote.
        int seed = int.TryParse(Option("--seed"), out int asked) ? asked : 1;

        var copy = Item.Read(Item.Write(item, save.Header.Version), save.Header.Version);
        var made = Crafts.CraftFrom(copy, recipe, new Random(seed), save.Level);

        Console.WriteLine();
        Console.WriteLine($"  recipes this base takes: {string.Join(", ", recipes.Select(each => each.Family))}");
        Show($"after crafting ({made.Family})", copy);

        // The point of the whole command: what the item had that no affix accounts for, and
        // whether it is still there.
        var was = item.StatLists.SelectMany(list => list.Stats)
            .Select(stat => stat.Stat ?? "").Where(name => name.Length > 0).ToHashSet();
        var now = copy.StatLists.SelectMany(list => list.Stats)
            .Select(stat => stat.Stat ?? "").Where(name => name.Length > 0).ToHashSet();

        Console.WriteLine();
        Console.WriteLine($"  lost:  {(was.Except(now).Any() ? string.Join(", ", was.Except(now)) : "nothing")}");
        Console.WriteLine($"  kept:  {(was.Intersect(now).Any() ? string.Join(", ", was.Intersect(now)) : "nothing")}");
        Console.WriteLine("  (nothing was written; this reads the save and works on a copy)");

        return 0;
    }

    static int SetFix(string gameRoot, IReadOnlyList<string> paths, bool write)
    {
        // Set pieces an earlier forge made carry no green bonus lists: each one in these files given them
        // as the forge makes them now - at the top of their ranges in a vault, which forged at the top,
        // and at random in a save, as the grail's Forge rolls. A file is written only with --write,
        // backed up first, and only when every piece mended leaves the legitimacy check no new finding.
        using var installed = Season.Open(gameRoot, Tables());
        var game = installed.Game;

        var (rows, _) = game.ReadExcel("setitems");
        int marker = QualityRows.MarkerIn(rows);
        // A piece mended, or null when it needs nothing: what it says, and whether the check stays quiet.
        (string Line, bool Clean)? Mend(Item item, RollAs roll)
        {
            int before = Legitimacy.Check(item).Count;
            var notes = new List<string>();
            int added = ItemForge.RestoreSetBonuses(item, roll, roll == RollAs.Random ? Random.Shared : null, notes);
            if (added == 0)
                return null;

            int row = QualityRows.RowOf((int)item.FileIndex, marker);
            bool ranged = Enumerable.Range(1, 5).SelectMany(tier => new[] { $"{tier}a", $"{tier}b" })
                .Any(half => rows.Value(row, $"amin{half}").Trim() != rows.Value(row, $"amax{half}").Trim()
                             && rows.Value(row, $"amax{half}").Trim().Length > 0);
            var after = Legitimacy.Check(item);
            bool clean = after.Count <= before;
            string said = $"    {ItemPresenter.Describe(item).Name,-32} +{added} list{(added == 1 ? "" : "s")}, mask {Convert.ToString(item.SetItemMask, 2).PadLeft(5, '0')}"
                          + (ranged ? $", ranged ({roll})" : "")
                          + (clean ? "" : $"  FLAGGED: {string.Join("; ", after)}");
            return (said, clean);
        }

        return MendIn(paths, write, Mend, "set piece", "without green lists", "set-bonuses");
    }

    static int StackFix(string gameRoot, IReadOnlyList<string> paths, bool write)
    {
        // Throwing weapons and quivers an earlier forge made carry no quantity: it dressed a base
        // with its durability and defence and never its stack, and a javelin of nought is gone with its
        // first throw. Each one in these files is given a full stack, the table's
        // maxstack, as the forge makes them now. Only the bases PD2 draws no number on (Stacks.IsCounted
        // false): a counted stack of one - a single rune in a stack - is stored as nought by the game itself.
        using var installed = Season.Open(gameRoot, Tables());

        (string Line, bool Clean)? Mend(Item item, RollAs roll)
        {
            string code = item.Code.Trim();
            if (item.Quantity != 0 || Stacks.IsCounted(code) || Stacks.MaximumFor(code) is not { } most)
                return null;

            int before = Legitimacy.Check(item).Count;
            item.Quantity = (ushort)most;
            var after = Legitimacy.Check(item);
            bool clean = after.Count <= before;
            return ($"    {ItemPresenter.Describe(item).Name,-32} [{code}] quantity 0 -> {most}"
                    + (clean ? "" : $"  FLAGGED: {string.Join("; ", after)}"), clean);
        }

        return MendIn(paths, write, Mend, "stack", "of nought", "stacks");
    }

    static int DamageFix(string gameRoot, IReadOnlyList<string> paths, bool write)
    {
        // A flat damage is one number written for one-handed, two-handed and thrown together on what is no
        // weapon, and on a weapon for the kinds it is (DamageKinds). An earlier trade and armory
        // import read armour as a one-handed weapon and kept that copy alone, and the forge wrote a dmg-norm
        // - War Traveler's "Adds 15-25 Damage" - as the two stats its row names: such boots give their
        // damage to a one-handed weapon and to no other. Each item in these files, and what is in its
        // sockets, is given the copies its base holds.
        using var installed = Season.Open(gameRoot, Tables());

        (string Line, bool Clean)? Mend(Item item, RollAs roll)
        {
            // The item and what is in its sockets: a jewel is an item of its own, with a list of its own.
            var each = item.WithSockets().ToList();
            var gaps = each.SelectMany(DamageKinds.Apart).ToList();
            if (gaps.Count == 0)
                return null;

            static string Kinds(IReadOnlyList<string> copies) => copies.Count == 0 ? "none" : string.Join("+", copies.Select(copy =>
                copy.StartsWith("secondary", StringComparison.OrdinalIgnoreCase) ? "2h"
                : copy.StartsWith("item_throw", StringComparison.OrdinalIgnoreCase) ? "throw" : "1h"));

            string said = string.Join("; ", gaps.Select(gap =>
                $"{(gap.Held[0].Contains("min", StringComparison.OrdinalIgnoreCase) ? "minimum" : "maximum")} {Kinds(gap.Held)} -> {Kinds(gap.Wanted)}"));

            int before = each.Sum(one => Legitimacy.Judge(one).Count);
            foreach (var one in each)
                DamageKinds.Settle(one);
            var after = each.SelectMany(Legitimacy.Judge).ToList();

            // Mended where its copies are the base's now and the check says nothing it did not say before.
            bool clean = each.All(one => DamageKinds.Apart(one).Count == 0) && after.Count <= before - gaps.Count;
            return ($"    {ItemPresenter.Describe(item).Name,-32} [{item.Code.Trim()}] {said}"
                    + (clean ? "" : $"  FLAGGED: {string.Join("; ", after.Select(found => found.Said))}"), clean);
        }

        return MendIn(paths, write, Mend, "flat damage", "written for the wrong kinds", "damage");
    }

    static int ElemFix(string gameRoot, IReadOnlyList<string> paths, bool write)
    {
        // "+N to Fire Skills" is a grant (item_elemskill, its element the parameter) and the words beside it
        // (ElementSkills). An earlier forge wrote the grant with no element, and the trade and armory
        // imports wrote the words alone: the line showed and nothing was granted. Each item in these files, and
        // what is in its sockets, is given the grant its words stand for (GrantWorded - the game always writes
        // both, so words alone are ours), has its grants told their element by their words, and its words made
        // its grants.
        using var installed = Season.Open(gameRoot, Tables());

        (string Line, bool Clean)? Mend(Item item, RollAs roll)
        {
            var each = item.WithSockets().ToList();
            var gaps = each.SelectMany(ElementSkills.Apart).ToList();
            if (gaps.Count == 0)
                return null;

            static string Name(int element) => element > 0 && element < StatText.Elements.Length ? StatText.Elements[element] : $"element {element}";
            string said = string.Join("; ", gaps.Select(gap =>
                !gap.Named ? $"grant +{gap.Granted} names no element"
                : $"{Name(gap.Element)} grants +{gap.Granted}, shows +{gap.Shown}"));

            int before = each.Sum(one => Legitimacy.Judge(one).Count);
            foreach (var one in each)
            {
                ElementSkills.GrantWorded(one);
                ElementSkills.Settle(one);
            }
            var after = each.SelectMany(Legitimacy.Judge).ToList();

            bool clean = each.All(one => ElementSkills.Apart(one).Count == 0) && after.Count <= before - gaps.Count;
            return ($"    {ItemPresenter.Describe(item).Name,-32} [{item.Code.Trim()}] {said}"
                    + (clean ? "" : $"  FLAGGED: {string.Join("; ", after.Select(found => found.Said))}"), clean);
        }

        return MendIn(paths, write, Mend, "elemental skill line", "whose grant and words disagree", "elem");
    }

    /// <summary>
    /// A repair over vaults, saves and stashes (setfix, stackfix, damagefix, elemfix): every item handed to <paramref name="mend"/>,
    /// which changes it and says what it did, or returns null where it needs nothing. A file is written only
    /// with --write, backed up first, and only when every item mended leaves the legitimacy check no new finding.
    /// </summary>
    static int MendIn(
        IReadOnlyList<string> paths, bool write, Func<Item, RollAs, (string Line, bool Clean)?> Mend,
        string noun, string fault, string backupTag)
    {
        string season = Vault.InstalledSeason();
        int mended = 0, refused = 0;

        foreach (string path in paths)
        {
            string kind = Path.GetExtension(path).ToLowerInvariant();
            var lines = new List<string>();
            bool clean = true;

            bool exported = kind is ".pd2vault" or ".pd2folder" or ".pd2collection" or ".pd2profile";
            if (kind == ".pd2lib" || exported)
            {
                // Opening a vault brings an older one up to this build's format, which writes it and
                // backs it up: a dry run reads a throwaway copy - the file and its log - with the
                // vaults folder, where that backup goes, pointed at the copy's folder too.
                string opened = path;
                string? scratch = null;
                string? vaultsWere = Environment.GetEnvironmentVariable("PD2SPC_VAULTS");
                if (!write)
                {
                    scratch = Path.Combine(Path.GetTempPath(), $"pd2spc-setfix-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(scratch);
                    opened = Path.Combine(scratch, Path.GetFileName(path));
                    foreach (string suffix in new[] { "", "-wal", "-shm" }.Where(suffix => File.Exists(path + suffix)))
                        File.Copy(path + suffix, opened + suffix);
                    Environment.SetEnvironmentVariable("PD2SPC_VAULTS", scratch);
                }

                string? wrote = null;
                using (var vault = Vault.Open(opened, season, 10))
                {
                    var fixes = new List<(long Key, Item Item)>();
                    foreach (var row in vault.Page("Stored", descending: false, 0, int.MaxValue))
                    {
                        var item = ItemBlob.Restore(row.Blob, vault.StatIdBits);
                        if (Mend(item, RollAs.Maximum) is not { } done)
                            continue;

                        lines.Add(done.Line);
                        clean &= done.Clean;
                        fixes.Add((row.Key, item));
                    }

                    if (write && fixes.Count > 0 && clean)
                    {
                        wrote = vault.BackUp($"{backupTag}-{Path.GetFileName(Path.GetDirectoryName(path))}");
                        foreach (var (key, item) in fixes)
                        {
                            if (vault.Replace(key, item, out string? why) is null)
                                throw new InvalidOperationException($"#{key} not replaced: {why}");
                        }

                        // An export stays one clean file, as VACUUM INTO first made it: no log beside it.
                        if (exported)
                            vault.CopyTo(path + ".mended");
                    }
                }

                if (wrote is not null && exported)
                {
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    foreach (string stale in new[] { path, path + "-wal", path + "-shm" }.Where(File.Exists))
                        File.Delete(stale);
                    File.Move(path + ".mended", path);
                }

                if (scratch is not null)
                {
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    Environment.SetEnvironmentVariable("PD2SPC_VAULTS", vaultsWere);
                    Directory.Delete(scratch, recursive: true);
                }

                Report(path, lines, clean, wrote);
                continue;
            }

            if (kind is ".d2s")
            {
                var (save, _) = SaveVerifier.Load(path);
                foreach (var item in SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary))
                {
                    if (Mend(item, RollAs.Random) is { } done)
                    {
                        lines.Add(done.Line);
                        clean &= done.Clean;
                    }
                }

                Report(path, lines, clean, write && lines.Count > 0 && clean ? SaveWriter.Replace(path, SaveWriter.Prepared(save)) : null);
                continue;
            }

            if (kind is ".stash" or ".d2i")
            {
                var stash = SharedStash.Read(path);
                foreach (var item in stash.Items.Items ?? [])
                {
                    if (Mend(item, RollAs.Random) is { } done)
                    {
                        lines.Add(done.Line);
                        clean &= done.Clean;
                    }
                }

                Report(path, lines, clean, write && lines.Count > 0 && clean ? SaveWriter.Replace(path, stash.Write()) : null);
                continue;
            }

            Console.WriteLine($"{path}: not a vault, save or stash; skipped");
        }

        void Report(string path, List<string> lines, bool clean, string? backup)
        {
            Console.WriteLine($"{path}: {lines.Count} {noun}{(lines.Count == 1 ? "" : "s")} {fault}"
                              + (lines.Count == 0 ? "" : !clean ? " - FLAGGED, not written" : backup is not null ? $" - mended, backup {backup}" : " - dry run"));
            foreach (string line in lines)
                Console.WriteLine(line);

            if (clean) mended += backup is not null ? lines.Count : 0;
            else refused++;
        }

        Console.WriteLine();
        Console.WriteLine(write ? $"{mended} mended; {refused} file(s) refused" : $"dry run; {refused} file(s) would be refused");
        return refused == 0 ? 0 : 1;
    }

    static int GiveSet(string gameRoot, string path, string setName, string? backupFolder)
    {
        // Every piece of one set, made from its table rows as the grail's Forge makes them, into the
        // character's personal stash - for measuring set bonuses in game. The file is copied to the
        // backup folder first; nothing is written without that copy.
        if (backupFolder is null)
        {
            Console.Error.WriteLine("--backup <folder> is required");
            return 1;
        }

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var own = save.PlayerItemList;
        string era = EraOf(save);

        var rows = ItemForge.SetItems().Where(row => string.Equals(ItemForge.SetOf(row), setName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (rows.Count == 0)
        {
            Console.Error.WriteLine($"No set called {setName}.");
            return 1;
        }

        // The stash, then the inventory, then the cube: the first with room for each piece.
        var layout = InventoryLayout.Load(game, save.ClassId, save.Status.IsExpansion);
        var places = new (byte Page, GridSize Size)[]
        {
            (save.Status.IsExpansion ? InventoryLayout.ExpansionStashPage : InventoryLayout.ClassicStashPage, layout.Stash),
            (InventoryLayout.InventoryPage, layout.Inventory),
            (InventoryLayout.CubePage, layout.Cube),
        };
        var rooms = places.Select(place => (place.Page, Room: new ItemMover.Occupancy(place.Size,
            own.Items.Where(item => item.Mode == ItemMode.Stored && item.Page == place.Page)))).ToList();

        var made = new List<Item>();
        foreach (int row in rows)
        {
            var item = ItemForge.SetItemAt(row, era, RollAs.Maximum).Item;
            var found = rooms.Select(each => (each.Page, each.Room, Cell: ItemMover.FirstFreeCell(item, each.Room)))
                             .FirstOrDefault(each => each.Cell is not null);
            if (found.Cell is not { } cell)
            {
                Console.Error.WriteLine($"No room anywhere for {ItemPresenter.Describe(item).Name}; nothing written.");
                return 1;
            }

            byte page = found.Page;
            var (width, height) = ItemMover.Footprint(item);
            found.Room.Block(cell.X, cell.Y, width, height);

            do
                item.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
            while (own.Items.Concat(made).Any(each => each != item && each.Id == item.Id));

            ItemMover.Place(item, page, cell.X, cell.Y);
            made.Add(item);
        }

        Directory.CreateDirectory(backupFolder);
        string backup = Path.Combine(backupFolder, $"{Path.GetFileNameWithoutExtension(path)}-before-{setName.Replace(' ', '_').Replace("'", "")}-{DateTime.Now:yyyyMMdd-HHmmss}.d2s");
        File.Copy(path, backup, overwrite: false);

        own.Items.AddRange(made);
        own.Count = (ushort)own.Items.Count;
        SaveWriter.Replace(path, SaveWriter.Prepared(save));

        var (back, _) = SaveVerifier.Load(path);
        Console.WriteLine($"backup: {backup}");
        foreach (var item in made)
        {
            var again = back.PlayerItemList.Items.First(each => each.Id == item.Id);
            Console.WriteLine($"  {ItemPresenter.Describe(again).Name,-28} [{again.Code?.Trim()}] page {again.Page} at {again.X},{again.Y}  v{again.Version}  lists {again.StatLists.Count}");
        }

        return 0;
    }

    /// <summary>
    /// Copies of one item of another save, as it is stored - its season's stamp kept - under new ids, into a test character
    /// (made for more of a legacy Saracen's Chance to cube, the first having bricked).
    /// </summary>
    static int CopyItemCommand(string gameRoot, string path, string from, int index, int count, string? backupFolder)
    {
        if (backupFolder is null)
        {
            Console.Error.WriteLine("--backup <folder> is required");
            return 1;
        }

        using var installed = Season.Open(gameRoot);
        var (save, _) = SaveVerifier.Load(path);

        // Each read of the file is an item of its own.
        var copies = Enumerable.Range(0, count).Select(_ => SaveVerifier.Load(from).Save.PlayerItemList.Items[index]).ToList();
        return PutOnCharacter(installed.Game, save, path, copies, backupFolder, "copyitem");
    }

    /// <summary>
    /// Hard points into skills by name (<c>Berserk:1</c>), the prerequisites left out, and the right skill box set - for
    /// a test character whose page is read with that skill on the right hand (made for the sequence rows).
    /// </summary>
    static int SkillPointsCommand(string gameRoot, string path, IReadOnlyList<string> specs, string? right, string? backupFolder)
    {
        if (backupFolder is null)
        {
            Console.Error.WriteLine("--backup <folder> is required");
            return 1;
        }

        using var installed = Season.Open(gameRoot);
        var (save, _) = SaveVerifier.Load(path);
        var tree = SkillTree.For(installed.Game, save.ClassId);
        var skills = new SkillEditor(save, tree);

        // By its name in the game's strings or its row's id: some rows' names are not the tree's.
        SkillEntry? Named(string name) => tree.BySlot.Values.FirstOrDefault(each => each.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || each.Id.ToString() == name);

        foreach (string spec in specs)
        {
            var parts = spec.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int points) || Named(parts[0]) is not { } skill)
            {
                Console.Error.WriteLine($"--skill {spec}: want name:points, the name one of this class's skills; nothing written.");
                return 1;
            }

            skills.AddUnspent(points);
            var (added, stopped) = skills.Add(skill, points, save.Level, freely: true, withoutPrerequisites: true);
            if (stopped is not null)
            {
                Console.Error.WriteLine($"--skill {spec}: {added} went in, {stopped}; nothing written.");
                return 1;
            }
        }

        if (right is not null)
        {
            if (Named(right) is not { } box)
            {
                Console.Error.WriteLine($"--right {right}: not one of this class's skills; nothing written.");
                return 1;
            }
            save.RightSkill = new Skill { Id = (uint)box.Id };
        }

        Directory.CreateDirectory(backupFolder);
        File.Copy(path, Path.Combine(backupFolder, $"{Path.GetFileNameWithoutExtension(path)}-before-skillpoints-{DateTime.Now:yyyyMMdd-HHmmss-fff}.d2s"), overwrite: false);
        SaveWriter.Replace(path, SaveWriter.Prepared(save));

        var (back, _) = SaveVerifier.Load(path);
        var backTree = new SkillEditor(back, tree);
        foreach (var skill in tree.BySlot.Values.Where(each => backTree.PointsIn(each.Slot) > 0))
            Console.WriteLine($"  {skill.Name,-26} {backTree.PointsIn(skill.Slot)}");
        Console.WriteLine($"  right skill {back.RightSkill?.Id}");
        return 0;
    }

    static int Give(string gameRoot, string path, IReadOnlyList<string> uniques, IReadOnlyList<string> bases, IReadOnlyList<string> forges, string? backupFolder,
                    int? skin = null, int? skill = null, IReadOnlyList<string>? runewords = null, bool unidentified = false)
    {
        // Uniques by name and plain bases (code:sockets) into the character's stash, then inventory,
        // then cube, as GiveSet puts a set's pieces - for putting a shape in front of the game (a
        // Troll Belt [S], The Third Eye and Cage of the
        // Unsullied, to see where the game draws their sockets). The file is copied to the backup
        // folder first; nothing is written without that copy.
        if (backupFolder is null)
        {
            Console.Error.WriteLine("--backup <folder> is required");
            return 1;
        }

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var own = save.PlayerItemList;
        string era = EraOf(save);

        var wanted = new List<Item>();
        foreach (string name in uniques)
        {
            var forged = ItemForge.Unique(name, era, RollAs.Maximum);
            foreach (string note in forged.Notes)
                Console.WriteLine($"  {name}: {note}");

            // A skin and a chosen random skill, for putting what drops too rarely to farm in front of the game
            // - a Special Edition's line, and which of
            // Ormus' Robes' three skins each element takes.
            if (skin is { } edition)
                Corruptions.AddStat(forged.Item.StatLists[0], Skins.Stat, edition);
            if (skill is { } chosen && !ChooseSkill(forged.Item, chosen))
            {
                Console.Error.WriteLine($"--skill: {name} has no random skill to choose; nothing written.");
                return 1;
            }
            wanted.Add(forged.Item);
        }
        foreach (string spec in bases)
        {
            var parts = spec.Split(':');
            int sockets = parts.Length > 1 && int.TryParse(parts[1], out int count) ? count : 0;
            var forged = ItemForge.PlainBase(parts[0], sockets, era);
            foreach (string note in forged.Notes)
                Console.WriteLine($"  {spec}: {note}");
            wanted.Add(forged.Item);
        }

        // A base made as the Forge page makes it (ItemForge.BaseAs): a magic, rare or crafted one rolled as a drop, every
        // number at its top - for putting the Forge's items in front of the game.
        foreach (string spec in forges)
        {
            var parts = spec.Split(':');
            if (parts.Length != 2 || !Enum.TryParse<ItemQuality>(parts[1], ignoreCase: true, out var quality))
            {
                Console.Error.WriteLine($"--forge {spec}: want code:quality, the quality one of normal, superior, magic, rare, craft");
                return 1;
            }
            var forged = ItemForge.BaseAs(parts[0], quality, era);
            foreach (string note in forged.Notes)
                Console.WriteLine($"  {spec}: {note}");
            wanted.Add(forged.Item);
        }

        // A runeword made as the Forge makes it (ItemForge.Runeword, every number at its top), in a base made ethereal
        // ("eth") or superior with that much enhanced damage ("sup20") - for a page read against a known weapon
        // (the Act 2 guard's Pride on an ethereal superior War Pike, its two-hand max damage).
        foreach (string spec in runewords ?? [])
        {
            var parts = spec.Split(':');
            var word = parts.Length >= 2
                ? Runewords.All.FirstOrDefault(each => Runewords.GameName(each).Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                : null;
            if (word is null)
            {
                Console.Error.WriteLine($"--runeword {spec}: want name:code[:eth][:sup<N>], the name a runeword's (Pride)");
                return 1;
            }
            var forged = ItemForge.Runeword(word, parts[1], era);
            foreach (string note in forged.Notes)
                Console.WriteLine($"  {spec}: {note}");
            var item = forged.Item;
            foreach (string extra in parts.Skip(2))
            {
                if (extra.Equals("eth", StringComparison.OrdinalIgnoreCase))
                    item.IsEthereal = true;
                else if (extra.StartsWith("sup", StringComparison.OrdinalIgnoreCase) && int.TryParse(extra[3..], out int enhanced))
                {
                    item.Quality = ItemQuality.Superior;
                    Corruptions.AddStat(ItemStats.Home(item), "item_maxdamage_percent", enhanced);
                    Corruptions.AddStat(ItemStats.Home(item), "item_mindamage_percent", enhanced);
                }
                else
                {
                    Console.Error.WriteLine($"--runeword {spec}: {extra} is neither eth nor sup<N>");
                    return 1;
                }
            }
            wanted.Add(item);
        }

        // For what the game does with one not yet identified: a charm in the charm area.
        if (unidentified)
            wanted.ForEach(item => item.IsIdentified = false);

        return PutOnCharacter(game, save, path, wanted, backupFolder, "give");
    }

    /// <summary>
    /// Sets a random-skill unique's skill (<see cref="RandomSkillCast"/>): the levels granted and the cast of the same
    /// skill, the cast's chance and level as the rule gives them for it - the row's min and max read back from the
    /// skill drawn. False where the item holds no such pair.
    /// </summary>
    static bool ChooseSkill(Item item, int skill)
    {
        var stats = item.StatLists[0].Stats;
        var grant = stats.FirstOrDefault(stat => stat.Stat == "item_singleskill");
        var cast = stats.FirstOrDefault(stat => stat.Stat == "item_skilloncast" && stat.SkillId == grant?.Param);
        if (grant?.Param is not { } drawn || cast?.SkillLevel is not { } level)
            return false;

        int min = SkillNames.HasCooldown(drawn) ? cast.Value * 3 / 2 : cast.Value;
        int max = level + Math.Max(0, SkillNames.RequiredLevelOf(drawn) / 6 - 1);
        grant.Param = skill;
        cast.SkillId = skill;
        cast.Value = RandomSkillCast.Chance(skill, min);
        cast.SkillLevel = RandomSkillCast.Level(skill, max);
        return true;
    }

    /// <summary>
    /// Items into a character's stash, then inventory, then cube - the first with room for each - under new ids, the
    /// file copied to <paramref name="backupFolder"/> first and read back after. All or nothing: no room for one, and
    /// nothing is written. What <c>give</c> and <c>specimens</c> end on.
    /// </summary>
    static int PutOnCharacter(GameData game, D2S save, string path, IReadOnlyList<Item> wanted, string backupFolder, string label)
    {
        var own = save.PlayerItemList;

        var layout = InventoryLayout.Load(game, save.ClassId, save.Status.IsExpansion);
        var places = new (byte Page, GridSize Size)[]
        {
            (save.Status.IsExpansion ? InventoryLayout.ExpansionStashPage : InventoryLayout.ClassicStashPage, layout.Stash),
            (InventoryLayout.InventoryPage, layout.Inventory),
            (InventoryLayout.CubePage, layout.Cube),
        };
        var rooms = places.Select(place => (place.Page, Room: new ItemMover.Occupancy(place.Size,
            own.Items.Where(item => item.Mode == ItemMode.Stored && item.Page == place.Page)))).ToList();

        var made = new List<Item>();
        foreach (var item in wanted)
        {
            var found = rooms.Select(each => (each.Page, each.Room, Cell: ItemMover.FirstFreeCell(item, each.Room)))
                             .FirstOrDefault(each => each.Cell is not null);
            if (found.Cell is not { } cell)
            {
                Console.Error.WriteLine($"No room anywhere for {ItemPresenter.Describe(item).Name}; nothing written.");
                return 1;
            }

            var (width, height) = ItemMover.Footprint(item);
            found.Room.Block(cell.X, cell.Y, width, height);

            do
                item.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
            while (own.Items.Concat(made).Any(each => each != item && each.Id == item.Id));

            ItemMover.Place(item, found.Page, cell.X, cell.Y);
            made.Add(item);
        }

        Directory.CreateDirectory(backupFolder);
        // To the millisecond: two gives in one second clashed on the name and the second wrote nothing.
        string backup = Path.Combine(backupFolder, $"{Path.GetFileNameWithoutExtension(path)}-before-{label}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.d2s");
        File.Copy(path, backup, overwrite: false);

        own.Items.AddRange(made);
        own.Count = (ushort)own.Items.Count;
        SaveWriter.Replace(path, SaveWriter.Prepared(save));

        var (back, _) = SaveVerifier.Load(path);
        Console.WriteLine($"backup: {backup}");
        foreach (var item in made)
        {
            var again = back.PlayerItemList.Items.First(each => each.Id == item.Id);
            Console.WriteLine($"  {ItemPresenter.Describe(again).Name,-28} [{again.Code?.Trim()}] page {again.Page} at {again.X},{again.Y}  sockets {again.TotalNumberOfSockets}  v{again.Version}");
        }

        return 0;
    }

    static int Specimens(string gameRoot, string path, string? backupFolder, string? round = null)
    {
        // One item of each kind the game's Ctrl+C over an item could word differently, into a character - so the user
        // copies each in game and the clipboard's JSON is read against an item whose every number is known, for the import of that text into the Forge. Made as the
        // Forge makes them. Not here: an item with a jewel socketed - the editor sockets nothing.
        if (backupFolder is null)
        {
            Console.Error.WriteLine("--backup <folder> is required");
            return 1;
        }

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        string era = EraOf(save);

        var random = new Random(20261006);
        var wanted = new List<Item>();
        void Add(string what, Item item)
        {
            Console.WriteLine($"  {what,-34} {ItemPresenter.Describe(item).Name} [{item.Code?.Trim()}]");
            wanted.Add(item);
        }

        // The second round, the still unseen first: the lines the first batch had none
        // of - charges, an aura, chance to cast on attack and on striking, poison, an oskill, a class's skills, a stat
        // and its per-level twin under one name, an ethereal and indestructible weapon, a count of sockets named
        // outright, a superior bonus, and sockets holding a jewel, a gem and a rune.
        if (round == "2")
        {
            Item Unique(string name) => ItemForge.Unique(name, era, RollAs.Maximum).Item;

            Add("charges, aura, cast on attack", Unique("Todesfaelle Flamme"));
            Add("poison damage", Unique("Blacktongue"));
            Add("oskill", Unique("Undead Crown"));
            Add("cast on striking", Unique("Howltusk"));
            Add("a stat and its per-level twin", Unique("Hellslayer"));
            Add("a class's skills", Unique("Iros Torch"));
            Add("ethereal, indestructible weapon", Unique("Ethereal Edge"));
            Add("sockets named outright, repairs", Unique("Hone Sundan"));

            var plate = ItemForge.BaseAs("utp", ItemQuality.Superior, era, random).Item;
            QualityItems.Put(plate, QualityItems.RowsFor(plate)[^1]);
            Add("superior armor, a bonus", plate);

            var blade = ItemForge.BaseAs("7cr", ItemQuality.Superior, era, random).Item;
            QualityItems.Put(blade, QualityItems.RowsFor(blade)[0]);
            Add("superior weapon, a bonus", blade);

            // Sockets filled three ways, in a plain base and in a unique.
            var filled = ItemForge.PlainBase("xtp", 3, era).Item;
            ItemForge.Socket(filled, ItemForge.BaseAs("jew", ItemQuality.Rare, era, random).Item);
            ItemForge.Socket(filled, ItemForge.Material("gpy", 1, era));
            ItemForge.Socket(filled, ItemForge.Material("r09", 1, era));
            Add("white armor: rare jewel, gem, rune", filled);

            var viper = Unique("Skin of the Vipermagi");
            Corruptions.Apply(viper, Corruptions.Available(viper).Last(option => option.GivesSockets), random);
            ItemForge.Socket(viper, ItemForge.BaseAs("jew", ItemQuality.Magic, era, random).Item);
            Add("unique, corrupted sockets, a jewel in", viper);

            foreach (var item in wanted)
            {
                foreach (var finding in Legitimacy.Check(item))
                    Console.WriteLine($"  ! {ItemPresenter.Describe(item).Name}: {finding.Subject} - {finding.Said}");
            }

            return PutOnCharacter(game, save, path, wanted, backupFolder, "specimens2");
        }

        // A set piece: the first row of the table.
        Add("set piece", ItemForge.SetItemAt(ItemForge.SetItems()[0], era, RollAs.Maximum).Item);

        // Magic, rare, crafted; a class base with staffmods; an automod of choice on a stack.
        Add("magic armor", ItemForge.BaseAs("xrs", ItemQuality.Magic, era, random).Item);
        Add("rare ring", ItemForge.BaseAs("rin", ItemQuality.Rare, era, random).Item);
        Add("rare weapon", ItemForge.BaseAs("9ws", ItemQuality.Rare, era, random).Item);
        Add("crafted amulet", ItemForge.BaseAs("amu", ItemQuality.Craft, era, random).Item);
        Add("magic wand (staffmods)", ItemForge.BaseAs("9wn", ItemQuality.Magic, era, random).Item);
        Add("magic javelins (automod, stack)", ItemForge.BaseAs("am5", ItemQuality.Magic, era, random).Item);

        // A runeword, its runes in.
        var spirit = Runewords.All.First(word => word.Name == "Spirit");
        Add("runeword", ItemForge.Runeword(spirit, "crs", era).Item);

        // Ethereal.
        var titan = ItemForge.Unique("Shaftstop", era, RollAs.Maximum).Item;
        if (ItemBases.Of(titan.Code) is { IsArmor: true } && titan.Armor > 0)
            titan.Armor = (ushort)ItemBases.Ethereal(titan.Armor);
        titan.IsEthereal = true;
        Add("ethereal unique", titan);

        // Corrupted: a property, and sockets.
        var ringed = ItemForge.Unique("Nagelring", era, RollAs.Maximum).Item;
        Corruptions.Apply(ringed, Corruptions.Available(ringed).First(option => !option.GivesSockets && option.Grants.Count > 0), random);
        Add("corrupted unique (a property)", ringed);

        var bored = ItemForge.Unique("Goldskin", era, RollAs.Maximum).Item;
        Corruptions.Apply(bored, Corruptions.Available(bored).Last(option => option.GivesSockets), random);
        Add("corrupted unique (sockets)", bored);

        // Desecrated: an amulet corrupted, then a mod from the pool.
        var amulet = ItemForge.Unique("Nokozan Relic", era, RollAs.Maximum).Item;
        Corruptions.Apply(amulet, Corruptions.Available(amulet).First(option => !option.GivesSockets && option.Grants.Count > 0), random);
        if (Desecrations.Roll(amulet, random) is { } mod)
            Desecrations.Apply(amulet, Corruptions.Draft(amulet, mod, random));
        Add("desecrated amulet", amulet);

        // Plain bases: white with sockets, superior.
        Add("white base, sockets", ItemForge.PlainBase("7wa", 4, era).Item);
        Add("superior base", ItemForge.BaseAs("utp", ItemQuality.Superior, era, random).Item);

        // Small things: charms, a jewel, a map.
        Add("magic small charm", ItemForge.BaseAs("cm1", ItemQuality.Magic, era, random).Item);
        Add("magic grand charm", ItemForge.BaseAs("cm3", ItemQuality.Magic, era, random).Item);
        Add("rare jewel", ItemForge.BaseAs("jew", ItemQuality.Rare, era, random).Item);
        Add("magic map", ItemForge.BaseAs("t27", ItemQuality.Magic, era, random).Item);

        // Every one silent to the legitimacy check, or the test is of something the editor itself doubts.
        foreach (var item in wanted)
        {
            foreach (var finding in Legitimacy.Check(item))
                Console.WriteLine($"  ! {ItemPresenter.Describe(item).Name}: {finding.Subject} - {finding.Said}");
        }

        return PutOnCharacter(game, save, path, wanted, backupFolder, "specimens");
    }

    static int Reroll(string gameRoot, string path)
    {
        string itemText = Option("--item") ?? throw new ArgumentException("reroll needs --item <n>, as shown by dump");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var outcome = SaveWriter.RerollItem(path, int.Parse(itemText), arguments.Contains("--dry-run"));
        Console.WriteLine(outcome.Description);

        if (outcome.BackupPath is { } backup)
            Console.WriteLine($"  backup:  {backup}");
        else
            Console.WriteLine("  dry run - nothing written");

        return 0;
    }

    static int MaterialsCommand(string gameRoot, string path)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        if (Option("--check") is { } saves)
            return CheckMaterials(saves.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var stash = SharedStash.Read(path);

        // --set 1=65535,2=32768 - the counts written through the same SharedStash code the tab
        // uses, backed up first. For probing what the game does with a count.
        if (Option("--set") is { } settings)
        {
            foreach (string pair in settings.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = pair.Split('=');
                int slot = int.Parse(parts[0]), count = int.Parse(parts[1]);
                Console.WriteLine($"  slot {slot} {Materials.NameOf(slot)}: {stash.MaterialCount(slot)} -> {count}");
                stash.SetMaterialCount(slot, count);
            }

            string backup = SaveWriter.Replace(path, stash.Write());
            Console.WriteLine($"  backup:  {backup}");
            stash = SharedStash.Read(path);
        }

        Console.WriteLine($"materials tab, {path}  (shared gold {stash.Gold:N0})");
        for (int slot = 1; slot <= Materials.Every; slot++)
        {
            if (!stash.Holds(slot))
                continue;
            int count = stash.MaterialCount(slot);
            Console.WriteLine($"  {slot,3}  {Materials.CodeAt(slot),-5} {Materials.NameOf(slot),-34} {(count == 0 ? "-" : count.ToString())}");
        }
        return 0;
    }

    static int Clear(string gameRoot, string path)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var outcome = SaveWriter.ClearStored(path, arguments.Contains("--dry-run"));
        Console.WriteLine(outcome.Description);

        if (outcome.BackupPath is { } backup)
            Console.WriteLine($"  backup:  {backup}");
        else
            Console.WriteLine("  dry run - nothing written");

        return 0;
    }

    // Ethereal on or off for one item, as the Basic section's switch does it (BasicEditor.Ethereal): an
    // armour's stored defence carries the bonus, so it moves with the flag - a quarter more, or back.
    static int EtherealCommand(string gameRoot, string path)
    {
        string itemText = Option("--item") ?? throw new ArgumentException("ethereal needs --item <n>, as shown by dump");

        using var installed = Season.Open(gameRoot, Tables());
        var (save, result) = SaveVerifier.Load(path);
        SaveVerifier.Install(result.Format);
        var items = save.PlayerItemList?.Items ?? [];
        int which = int.Parse(itemText);
        if (which < 0 || which >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(which), $"item #{which} does not exist.");

        var item = items[which];
        if (!ItemTypes.IsGear(ItemTypes.TypeOf(item.Code)))
            throw new ArgumentException($"{ItemPresenter.Describe(item).Name} is not gear: ethereal means nothing to it.");

        bool wanted = !item.IsEthereal;
        if (Ethereals.Of(item) == (wanted ? Ethereals.Form.Never : Ethereals.Form.Always))
            throw new ArgumentException($"{ItemPresenter.Describe(item).Name}: the game makes it {(wanted ? "never" : "always")} ethereal.");

        ItemSeason.MakeCurrent(item);
        int defence = item.Armor;
        if (ItemBases.Of(item.Code) is { IsArmor: true } && item.Armor > 0)
            item.Armor = (ushort)(wanted ? ItemBases.Ethereal(item.Armor) : ItemBases.BehindEthereal(item.Armor));
        item.IsEthereal = wanted;
        Console.WriteLine($"{ItemPresenter.Describe(item).Name}: {(wanted ? "ethereal" : "not ethereal")}"
                          + (item.Armor != defence ? $", defence {defence} -> {item.Armor}" : ""));

        if (arguments.Contains("--dry-run"))
        {
            Console.WriteLine("  dry run - nothing written");
            return 0;
        }
        Console.WriteLine($"  backup:  {SaveWriter.Commit(path, save, null)}");
        return 0;
    }

    // The mercenary within its type, as the Items page's row edits it (MercenaryEdit): level, variant, name,
    // revive. --dead kills it, for testing Revive. With nothing asked: the variants and names it could have.
    static int MercCommand(string gameRoot, string path)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;
        var (save, result) = SaveVerifier.Load(path);
        SaveVerifier.Install(result.Format);
        if (save.Mercenary.Id == 0)
            throw new ArgumentException("no mercenary is hired.");

        bool asked = false;
        void Done(string? refused)
        {
            asked = true;
            if (refused is not null)
                throw new ArgumentException(refused);
        }

        if (Option("--variant") is { } variant)
            Done(MercenaryEdit.SetVariant(save, game, int.Parse(variant)));
        if (Option("--level") is { } level)
            Done(MercenaryEdit.SetLevel(save, game, int.Parse(level)));
        if (Option("--name") is { } name)
            Done(MercenaryEdit.SetName(save, game, int.Parse(name)));
        if (arguments.Contains("--revive"))
            Done(MercenaryEdit.Revive(save));
        if (arguments.Contains("--dead"))
        {
            save.Mercenary.IsDead = 1;
            asked = true;
        }

        var profile = MercenaryProfile.Read(save, game);
        Console.WriteLine($"{MercenaryEdit.NameOf(save, game)}: {profile.Title} ({profile.Detail}), type {save.Mercenary.TypeId}, name {save.Mercenary.NameId}");
        if (!asked)
        {
            foreach (var each in MercenaryEdit.VariantsOf(save, game))
                Console.WriteLine($"  variant {each.TypeId,3}  {each.Label}");
            var names = MercenaryEdit.NamesOf(save, game);
            for (int place = 0; place < names.Count; place++)
                Console.WriteLine($"  name {place,3}  {names[place]}");
            return 0;
        }

        if (arguments.Contains("--dry-run"))
        {
            Console.WriteLine("  dry run - nothing written");
            return 0;
        }
        Console.WriteLine($"  backup:  {SaveWriter.Commit(path, save, null)}");
        return 0;
    }

    static int Dupe(string gameRoot, string path)
    {
        string itemText = Option("--item") ?? throw new ArgumentException("dupe needs --item <n>, as shown by dump");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (header, _) = SaveVerifier.Load(path);
        var inventory = InventoryLayout.Load(game, header.ClassId, header.Status.IsExpansion).Inventory;

        var outcome = DuplicateItem(path, int.Parse(itemText), inventory, arguments.Contains("--dry-run"));
        Console.WriteLine(outcome.Description);

        if (outcome.BackupPath is { } backup)
            Console.WriteLine($"  backup:  {backup}");
        else
            Console.WriteLine("  dry run - nothing written");

        return 0;
    }

    /// <summary>
    /// Puts an exact second of an item in the inventory, <b>keeping its id</b>.
    /// <para>
    /// A test instrument, not an edit anyone should want. The vault keeps an item's id on the
    /// way in and out, so two items sharing one inside a save is how a dupe shows - and that
    /// only works if the game leaves both alone. This makes the case to take into the game:
    /// load, save, and read the ids back.
    /// </para>
    /// </summary>
    static EditOutcome DuplicateItem(string path, int itemIndex, GridSize inventory, bool dryRun)
    {
        var (save, _) = SaveVerifier.Load(path);

        var list = save.PlayerItemList
            ?? throw new InvalidOperationException("this character has no item list.");
        var items = list.Items;
        if (itemIndex < 0 || itemIndex >= items.Count)
            throw new ArgumentOutOfRangeException(nameof(itemIndex), $"item #{itemIndex} does not exist.");

        var item = items[itemIndex];
        if (item.IsCompact)
            throw new ArgumentException($"item #{itemIndex} ({item.Code?.Trim()}) is compact and has no id.");

        // Written to bits and read back, as CharacterSession.CopyOf does, so nothing nested
        // is shared with the original. The id rides along in the bits.
        var copy = Item.Read(Item.Write(item, save.Header.Version), save.Header.Version);

        var stored = items.Where(each => each.Mode == ItemMode.Stored
                                         && each.Page == InventoryLayout.InventoryPage);
        var cell = ItemMover.FirstFreeCell(copy, stored, inventory)
            ?? throw new InvalidOperationException("the inventory has no room for a second one.");

        ItemMover.Place(copy, InventoryLayout.InventoryPage, cell.X, cell.Y);
        items.Add(copy);
        list.Count = (ushort)items.Count;

        uint id = item.Id;
        string description = $"item #{itemIndex} ({item.Code?.Trim()}): copied to inventory "
                             + $"{cell.X},{cell.Y} as #{items.Count - 1}, both id {id}";

        void ReadBack(byte[] written)
        {
            var reread = D2SLib.Core.ReadD2S(written);
            int sharing = (reread.PlayerItemList?.Items ?? []).Count(each => !each.IsCompact && each.Id == id);
            if (sharing != 2)
                throw new InvalidOperationException($"the written save holds {sharing} items with id {id}, not 2.");
        }

        if (dryRun)
        {
            ReadBack(D2S.Write(save));
            return new EditOutcome(description, 0, 0, null, Written: false);
        }

        string backup = SaveWriter.Commit(path, save, ReadBack);
        return new EditOutcome(description, 0, 0, backup, Written: true);
    }

    static int Corrupt(string gameRoot, string path)
    {
        string itemText = Option("--item") ?? throw new ArgumentException("corrupt needs --item <n>, as shown by dump");
        bool dryRun = arguments.Contains("--dry-run");

        // Corrupting and cleaning change the character's own list: asked of the mercenary, they hit another item.
        if (!arguments.Contains("--list") && (arguments.Contains("--merc") || arguments.Contains("--as-quality")))
            throw new ArgumentException("--merc and --as-quality only go with --list; nothing is written");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        // What this item could take, which is the picker's list on the page.
        if (arguments.Contains("--list"))
        {
            var (save, _) = SaveVerifier.Load(path);

            // The mercenary's gear is a separate list, and it is where half the interesting
            // items live on a character that has one.
            var from = arguments.Contains("--merc")
                ? save.MercenaryItemList?.ItemList?.Items ?? []
                : save.PlayerItemList?.Items ?? [];

            // "all" sweeps the list, which is how a change to the table is checked against
            // every kind of item a character happens to be carrying rather than one of them.
            var wanted = itemText.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? Enumerable.Range(0, from.Count)
                : [int.Parse(itemText)];

            // Asking what an item *would* be offered if it were another quality. The ladder
            // turns on quality more than on anything else - a white item is held to sockets and
            // never sees a modifier - and a character rarely carries one of each to check with.
            // Nothing is written, so this only ever changes the answer, never the save.
            var pretend = Option("--as-quality") is { Length: > 0 } named
                && Enum.TryParse<ItemQuality>(named, ignoreCase: true, out var quality)
                    ? quality
                    : (ItemQuality?)null;

            foreach (int n in wanted)
            {
                var item = from[n];
                if (pretend is { } becomes)
                    item.Quality = becomes;

                var open = Corruptions.Available(item);

                Console.WriteLine(
                    $"{open.Count} corruptions for item #{n} "
                    + $"({item.Code?.Trim()}, {item.Quality}, {item.TotalNumberOfSockets} sockets):");

                if (!itemText.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var option in open)
                        Console.WriteLine($"  {option.Tier,-7} {option.Chance,-6} id {option.Id,-4} {option.Readable}");
                }
            }

            return 0;
        }

        if (arguments.Contains("--clean"))
        {
            var cleaned = SaveWriter.CleanItem(path, int.Parse(itemText), dryRun);

            Console.WriteLine(cleaned.Description);
            Console.WriteLine(cleaned.Written
                ? $"  backup:  {cleaned.BackupPath}"
                : "  dry run - verified but nothing written");
            return 0;
        }

        var outcome = SaveWriter.CorruptItem(
            path, int.Parse(itemText),
            Option("--id") is { } id ? int.Parse(id) : null,
            dryRun);

        Console.WriteLine(outcome.Description);
        if (outcome.Written)
        {
            Console.WriteLine($"  backup:  {outcome.BackupPath}");
            Console.WriteLine($"  written: {path}");
        }
        else
        {
            Console.WriteLine("  dry run - verified but nothing written");
        }
        return 0;
    }

    /// <summary>
    /// What it would take to carry items from the season they were made in to the season
    /// installed now. Reports; writes nothing.
    /// <para>
    /// The order is the whole point, and the reason this cannot be a per-file loop:
    /// <see cref="SaveTables"/> is static, so every item is read under <c>--from</c> and held in
    /// memory <b>before</b> the installed tables replace them. An item still half-read at the
    /// swap would be written with the wrong widths.
    /// </para>
    /// </summary>
    static int Migrate(string gameRoot, string? from, string? corpus, string? single, string? into)
    {
        if (into is not null)
            return Carry(gameRoot, from, corpus, single, into);

        if (from is null)
            throw new ArgumentException("migratefiles needs --from <dir of the source season's .txt>");
        if (corpus is null && single is null)
            throw new ArgumentException("migratefiles needs a save or stash file, or --corpus <dir>");

        var files = corpus is not null ? SavesUnder(corpus) : [single!];
        if (files.Count == 0)
        {
            Console.WriteLine($"no saves or stashes under {corpus}");
            return 1;
        }

        var held = new List<(string Where, Item Item)>();
        var wasImpossible = new HashSet<Item>();
        int refused = 0;

        // --- everything read under the source season, first and completely ------------------
        using (var source = Season.Open(gameRoot, from))
        {

            foreach (var file in files)
            {
                try
                {
                    foreach (var (where, item) in Contents(file))
                    {
                        if (item.IsCompact)
                            continue;

                        if (Legitimacy.Check(item).Any(each => each.Certainty is Certainty.Impossible))
                            wasImpossible.Add(item);

                        held.Add((where.Length > 0 ? where : Path.GetFileName(file), item));
                    }
                }
                catch (Exception ex)
                {
                    refused++;
                    Console.WriteLine($"refused  {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        // --- then the target season ---------------------------------------------------------
        using var target = Season.Open(gameRoot);

        bool everyItem = arguments.Contains("--items");
        int untouched = 0, adjusted = 0, lost = 0, threw = 0, impossible = 0, newly = 0;
        var byStat = new SortedDictionary<string, int>();
        var freshly = new SortedDictionary<string, int>();

        foreach (var (where, item) in held)
        {
            string told = $"{ItemPresenter.Describe(item).Name} ({item.Code?.Trim()}, {item.Quality}, "
                          + $"v{item.Version}) on {where}";
            try
            {
                var changes = Migration.Fit(item);
                foreach (var change in changes)
                    byStat[change.Stat] = byStat.GetValueOrDefault(change.Stat) + 1;

                // What it should hold now, against what a write and a read actually keep.
                var meant = item.StatLists.Select(list => list.Stats
                    .Select(stat => $"{stat.Stat}|{stat.Value}|{stat.Param}").ToList()).ToList();

                D2SLib.Globals.ResetForFile(pd2Character: false, statIdBits: 10);
                var bytes = Item.Write(item, 0x60);   // the item version a stash item is written at
                D2SLib.Globals.ResetForFile(pd2Character: false, statIdBits: 10);
                var back = Item.Read(bytes, 0x60);

                var kept = back.StatLists.Select(list => list.Stats
                    .Select(stat => $"{stat.Stat}|{stat.Value}|{stat.Param}").ToList()).ToList();

                if (meant.Count != kept.Count
                    || !meant.Zip(kept).All(pair => pair.First.SequenceEqual(pair.Second)))
                {
                    lost++;
                    Console.WriteLine($"LOST     {told}");
                }
                else if (changes.Count == 0)
                {
                    untouched++;
                }
                else
                {
                    adjusted++;
                    if (everyItem)
                    {
                        Console.WriteLine(told);
                        foreach (var change in changes)
                            Console.WriteLine($"    {change.Why}");
                    }
                }

                var bad = Legitimacy.Check(back)
                    .Where(each => each.Certainty is Certainty.Impossible).ToList();
                if (bad.Count == 0)
                    continue;

                impossible++;
                if (wasImpossible.Contains(item))
                    continue;

                newly++;
                freshly[bad[0].Said] = freshly.GetValueOrDefault(bad[0].Said) + 1;
            }
            catch (Exception ex)
            {
                threw++;
                Console.WriteLine($"THREW    {told}: {ex.Message}");
            }
        }

        Console.WriteLine($"""

            {held.Count} items from {files.Count} file(s), {refused} refused
              carried untouched        {untouched,6}  ({(held.Count > 0 ? 100.0 * untouched / held.Count : 0):F1}%)
              carried with adjustment  {adjusted,6}
              silently lost something  {lost,6}
              threw                    {threw,6}
              impossible in the target {impossible,6}  ({newly} of them only after migrating)
            """);

        if (byStat.Count > 0)
        {
            Console.WriteLine("\nproperties the target season could not hold as they stood:");
            foreach (var (stat, count) in byStat.OrderByDescending(each => each.Value))
                Console.WriteLine($"  {count,6}  {stat}");
        }

        if (freshly.Count > 0)
        {
            Console.WriteLine("\nwhat migrating broke - affixes this season removed or re-scoped:");
            foreach (var (why, count) in freshly.OrderByDescending(each => each.Value).Take(15))
                Console.WriteLine($"  {count,6}  {why}");
        }

        return lost > 0 || threw > 0 ? 1 : 0;
    }
}
