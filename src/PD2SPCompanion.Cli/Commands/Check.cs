using System.Text;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Testbed;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Cli;

// Verification and regression checks: read-only, or on scratch copies. tests/regress.py runs several.
internal static partial class Cli
{
    /// <summary>
    /// The level editor on a save, in memory only - never written: a level up and back, experience
    /// past the signed range, and a refusal below what the unspent points cover, each read back from
    /// the written bytes.
    /// </summary>
    static int LevelTest(string gameRoot, string path)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        // As the editor opens it: the layout that round-trips, installed first.
        var (save, _) = SaveVerifier.Load(path);
        byte[] original = D2SLib.Model.Save.D2S.Write(save);
        var stats = ClassStats.For(game, save.ClassId);
        var table = ExperienceTable.For(game, stats.Name);

        string Show(D2SLib.Model.Save.D2S s)
        {
            var e = new LevelEditor(s, stats, table);
            var a = s.Attributes.Stats;
            return $"level {s.Level} (stat {a.GetValueOrDefault("level")}) exp {e.Experience:N0} statpts {a.GetValueOrDefault("statpts")} "
                   + $"newskills {a.GetValueOrDefault("newskills")} base {e.Base} lowest {e.LowestLevel} highest {e.HighestLevel}";
        }

        D2SLib.Model.Save.D2S Reread(D2SLib.Model.Save.D2S s) => D2SLib.Core.ReadD2S(D2SLib.Model.Save.D2S.Write(s));

        Console.WriteLine($"{stats.Name}: +{stats.StatPerLevel} stats, life {stats.LifePerLevel}/4, stamina {stats.StaminaPerLevel}/4, mana {stats.ManaPerLevel}/4 a level; max {table.MaxLevel}, cap {table.Cap:N0}");
        Console.WriteLine($"as read:       {Show(save)}");

        new LevelEditor(save, stats, table).SetLevel(50);
        save = Reread(save);
        Console.WriteLine($"level 50:      {Show(save)}");

        new LevelEditor(save, stats, table).SetExperience(3_600_000_000);
        save = Reread(save);
        Console.WriteLine($"exp 3.6e9:     {Show(save)}");

        new LevelEditor(save, stats, table).SetLevel(1);
        save = Reread(save);
        Console.WriteLine($"back to 1:     {Show(save)}");
        Console.WriteLine($"bytes as read: {D2SLib.Model.Save.D2S.Write(save).AsSpan().SequenceEqual(D2SLib.Model.Save.D2S.Write(D2SLib.Core.ReadD2S(original)))}");

        // Spend what level 30 gave, then ask for level 1: refused below what is unspent.
        var editor = new LevelEditor(save, stats, table);
        editor.SetLevel(30);
        save.Attributes.Stats["statpts"] -= 20;
        save.Attributes.Stats["strength"] = save.Attributes.Stats.GetValueOrDefault("strength") + 20;
        editor.SetLevel(1);
        save = Reread(save);
        Console.WriteLine($"20 spent, 1:   {Show(save)}");
        return 0;
    }

    /// <summary>
    /// A cached listing's item as the trading page takes it into a character, in memory only: built
    /// with the save's version stamp, added to the item list, the save written as marking it edited
    /// does - and every stat of it and its sockets printed before and after, to see what changes.
    /// </summary>
    static int AdoptTest(string gameRoot, string path, string listingId)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        string file = Path.Combine(TradeListings.CacheFolder, $"{listingId}.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        var listed = document.RootElement.GetProperty("data")[0].GetProperty("item");

        string? era = save.PlayerItemList?.Items.Select(item => item.Version).FirstOrDefault(v => v is { Length: > 0 });
        var item = TradeItems.Build(listed, era).Item;

        void Show(string when)
        {
            Console.WriteLine(when);
            foreach (var (owner, stat) in item.StatLists.SelectMany(list => list.Stats).Select(stat => ("item", stat))
                         .Concat(item.SocketedItems.SelectMany(socket => socket.StatLists.SelectMany(list => list.Stats).Select(stat => ("sock", stat)))))
            {
                Console.WriteLine($"  {owner} {stat.Stat} id{stat.Id?.ToString() ?? "null"} v{stat.Value} p{stat.Param?.ToString() ?? "null"} "
                                  + $"s{stat.SkillId?.ToString() ?? "null"} l{stat.SkillLevel?.ToString() ?? "null"}");
            }
        }

        Show($"built (era {era}):");
        save.PlayerItemList!.Items.Add(item);
        save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;
        _ = D2SLib.Model.Save.D2S.Write(save);
        Show("after the save is written:");

        // What the sale's search starts from: a property once, the item's and its jewels' summed.
        foreach (var row in TradeSearch.PrefillFor(item).Properties)
            Console.WriteLine($"  prefill {row.Option} skill={row.Skill} roll={row.Roll} min={row.Min}");
        return 0;
    }

    static int ArmoryStress(string gameRoot, IReadOnlyList<string> names, string? template, string? folder)
    {
        // --all: every armory cached in the folder. --quick: each item rebuilt and checked on its own,
        // no save built - a ladder of 1,400 in minutes instead of an hour, for going round the fixes.
        bool quick = arguments.Contains("--quick");
        if (arguments.Contains("--all") && folder is not null && Directory.Exists(folder))
            names = [.. Directory.GetFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).Where(name => name is { Length: > 0 } && name[0] != '_').Select(name => name!).Order()];

        if (names.Count == 0 || (template is null && !quick) || folder is null)
            throw new ArgumentException("armorystress <name>... | --all  --template <save.d2s> --out <folder> [--quick] [--verbose]");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;
        Directory.CreateDirectory(folder);

        int built = 0, failed = 0, itemsAll = 0, itemsWhole = 0, silentItems = 0, flaggedItems = 0, leftOut = 0;
        var slowest = new List<(double Seconds, string Item)>();
        var shapes = new Dictionary<string, (int Count, string Example)>();

        static void Flagged(string who, Item item, IReadOnlyList<Finding> found)
        {
            string Affixes(Item each) => string.Join(", ", MagicAffixes.PairsOn(each).Select(pair => $"{pair.PrefixName}|{pair.SuffixName}"));
            Console.WriteLine($"  -- {who}: {ItemPresenter.Describe(item).Name} [{item.Code.Trim()} {item.Quality} ilvl {item.ItemLevel}{(item.IsEthereal ? " eth" : "")}{(item.IsRuneword ? $" rw {item.RunewordId}" : "")}{(item.IsAutoAffix ? $" auto {item.AutoAffixId}" : "")}] corrupt {Corruptions.IdOn(item)} desecrated {Desecrations.ValueOn(item)} sockets {item.SocketedItems.Count} affixes {Affixes(item)}");
            foreach (var stat in ItemStats.Every(item))
                Console.WriteLine($"       {stat.Stat}[{stat.Param}] = {stat.Value}");
            foreach (var inside in item.SocketedItems)
                Console.WriteLine($"       socketed: {ItemPresenter.Describe(inside).Name} [{inside.Code.Trim()}]");
            foreach (var finding in found)
                Console.WriteLine($"     ! {finding.Certainty}: {finding.Said}");
        }
        var noteShapes = new Dictionary<string, int>();

        foreach (string name in names)
        {
            string cache = Path.Combine(folder, name + ".json");
            ArmoryImport.Armory armory;
            try
            {
                if (File.Exists(cache))
                {
                    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cache));
                    armory = new ArmoryImport.Armory(document.RootElement.Clone());
                }
                else if (quick)
                {
                    // Offline: the audit reads what is cached and never asks the armory itself -
                    // fetching is armoryfetch's, at its agreed pace. A name not here is skipped.
                    Console.WriteLine($"{name,-16} not cached, skipped");
                    continue;
                }
                else
                {
                    armory = ArmoryImport.Fetch(name).GetAwaiter().GetResult();
                    File.WriteAllText(cache, armory.Root.GetRawText());
                }
            }
            catch (Exception problem)
            {
                failed++;
                Console.WriteLine($"{name,-16} FETCH FAILED: {problem.Message}");
                continue;
            }

            if (quick)
            {
                int all = 0, whole = 0, silent = 0, flagged = 0;
                foreach (var json in armory.Items.Concat(armory.MercenaryItems))
                {
                    all++;
                    Forged forged;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        forged = TradeItems.Build(json);
                        slowest.Add((clock.Elapsed.TotalSeconds, $"{armory.Name}: {forged.Item.Code.Trim()} {forged.Item.Quality} {ItemPresenter.Describe(forged.Item).Name}"));
                    }
                    catch (Exception problem)
                    {
                        Console.WriteLine($"  -- {armory.Name}: BUILD THREW {problem.Message}");
                        continue;
                    }

                    // A unique no table row names - Kadala's Heirloom. The import leaves these out;
                    // said, not checked.
                    if (TradeItems.NoRow(forged) is { } online)
                    {
                        leftOut++;
                        Console.WriteLine($"  -- {armory.Name}: left out, {online}");
                        continue;
                    }

                    if (forged.Sound) whole++;
                    var found = Legitimacy.Check(forged.Item);
                    if (found.Count == 0) { silent++; continue; }
                    flagged++;
                    if (arguments.Contains("--verbose"))
                        Flagged(armory.Name, forged.Item, found);
                    foreach (var finding in found)
                    {
                        string shape = $"{finding.Certainty} {System.Text.RegularExpressions.Regex.Replace(finding.Said, "[0-9]+", "#")}";
                        shapes[shape] = shapes.TryGetValue(shape, out var had) ? (had.Count + 1, had.Example) : (1, $"{armory.Name} {ItemPresenter.Describe(forged.Item).Name}: {finding.Said}");
                    }
                }

                built++;
                itemsAll += all; itemsWhole += whole; silentItems += silent; flaggedItems += flagged;
                if (flagged > 0 || arguments.Contains("--verbose"))
                    Console.WriteLine($"{armory.Name,-16} {armory.ClassName,-11} lvl {armory.Level,2} {(armory.IsHardcore ? "HC" : "SC")}  items {all,3} whole {whole,3}  legit {silent,3}/{silent + flagged,-3}");
                continue;
            }

            try
            {
                // The whole of every item, as the rebuild says it.
                int whole = 0, all = 0;
                foreach (var json in armory.Items.Concat(armory.MercenaryItems))
                {
                    all++;
                    if (TradeItems.Build(json).Sound) whole++;
                }

                string output = Path.Combine(folder, armory.Name + ".d2s");
                // Not --quick, so the check at the top has made sure of the template.
                if (File.Exists(output))
                    throw new InvalidOperationException($"{output} exists; nothing is written over a character");
                var (bytes, notes) = ArmoryImport.Build(game, template!, armory, armory.Name);
                SaveWriter.WriteSafely(output, bytes);
                var (save, result) = SaveVerifier.Load(output);

                int written = save.ClassSkills.Skills.Sum(skill => skill.Points);
                int asked = armory.Skills.Values.Sum();

                int silent = 0, flagged = 0;
                foreach (var item in SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary))
                {
                    var found = Legitimacy.Check(item);
                    if (found.Count == 0) { silent++; continue; }
                    flagged++;
                    if (arguments.Contains("--verbose"))
                        Flagged(armory.Name, item, found);
                    foreach (var finding in found)
                    {
                        string shape = $"{finding.Certainty} {System.Text.RegularExpressions.Regex.Replace(finding.Said, "[0-9]+", "#")}";
                        shapes[shape] = shapes.TryGetValue(shape, out var had) ? (had.Count + 1, had.Example) : (1, $"{armory.Name} {ItemPresenter.Describe(item).Name}: {finding.Said}");
                    }
                }

                foreach (string note in notes)
                {
                    string shape = System.Text.RegularExpressions.Regex.Replace(note, "[0-9]+", "#");
                    noteShapes[shape] = noteShapes.GetValueOrDefault(shape) + 1;
                }

                built++;
                itemsAll += all; itemsWhole += whole; silentItems += silent; flaggedItems += flagged;
                Console.WriteLine($"{armory.Name,-16} {armory.ClassName,-11} lvl {armory.Level,2} {(armory.IsHardcore ? "HC" : "SC")}  items {all,3} whole {whole,3}  legit {silent,3}/{silent + flagged,-3}  skills {written}/{asked}{(written == asked ? "" : "  <- DIFFER")}  {result.Format}{(notes.Count > 0 ? $"  notes {notes.Count}" : "")}");
            }
            catch (Exception problem)
            {
                failed++;
                Console.WriteLine($"{name,-16} BUILD FAILED: {problem.Message}");
            }
        }

        Console.WriteLine();
        if (slowest.Count > 0)
            Console.WriteLine($"slowest rebuilds (of {slowest.Count}, {slowest.Sum(each => each.Seconds):0} s in all): "
                + string.Join("; ", slowest.OrderByDescending(each => each.Seconds).Take(8).Select(each => $"{each.Seconds:0.0} s {each.Item}")));
        Console.WriteLine($"{built} built, {failed} failed; items {itemsWhole}/{itemsAll} rebuilt whole; legitimacy {silentItems}/{silentItems + flaggedItems} silent{(leftOut > 0 ? $"; {leftOut} with no table row left out" : "")}");
        foreach (var (shape, count) in noteShapes.OrderByDescending(pair => pair.Value))
            Console.WriteLine($"  note x{count}: {shape}");
        foreach (var (shape, (count, example)) in shapes.OrderByDescending(pair => pair.Value.Count).Take(20))
            Console.WriteLine($"  {count,3}  {shape}   e.g. {example}");
        return failed == 0 ? 0 : 1;
    }

    // Mephisto's Soulstone as the editor makes it (ItemForge.QuestItem), against every one in the .d2s
    // files under the folders or files given - byte for byte, place wiped from both - with the quest
    // value each carries and the difficulty its character was last in, which that value should name.
    static int SoulstoneCheck(string gameRoot, IReadOnlyList<string> places)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;
        int bits = D2SLib.Globals.stat_id_bits;

        byte[] Blob(Item it)
        {
            var copy = ItemBlob.Restore(Item.Write(it, ItemBlob.Version), bits);
            ItemBlob.Normalise(copy);
            copy.IsNew = false;
            return ItemBlob.TryCapture(copy, bits, out var blob, out _, out string? why) ? blob : throw new InvalidOperationException(why);
        }

        var files = places.SelectMany(place => File.Exists(place) ? [place]
                        : Directory.EnumerateFiles(place, "*.d2s", SearchOption.AllDirectories))
                          .Where(file => !file.Contains("scratchpad", StringComparison.OrdinalIgnoreCase) || places.Contains(file));
        int found = 0, same = 0;
        var values = new SortedDictionary<string, int>();
        foreach (string file in files)
        {
            D2S save;
            try { save = SaveVerifier.Load(file).Save; }
            catch (Exception) { continue; }

            string last = save.Location.Hell.Active ? "Hell" : save.Location.Nightmare.Active ? "Nightmare" : "Normal";
            foreach (var item in (save.PlayerItemList?.Items ?? []).Where(each => each.Code?.Trim() == "mss"))
            {
                found++;
                string key = $"value {item.NumberOfSocketedItems}, character last in {last}";
                values[key] = values.GetValueOrDefault(key) + 1;
                var ours = Blob(ItemForge.QuestItem("mss", item.NumberOfSocketedItems, item.Version));
                var theirs = Blob(ItemBlob.Copy(item, bits));
                if (ours.AsSpan().SequenceEqual(theirs))
                    same++;
                else
                    Console.WriteLine($"  DIFFERS {Path.GetFileName(file)}: flags [{string.Join(",", Enumerable.Range(0, item.Flags.Length).Where(i => item.Flags[i]))}] ver {item.Version}");
            }
        }

        Console.WriteLine($"{found} soulstones, {same} made byte-identical to the game's");
        foreach (var (key, count) in values)
            Console.WriteLine($"  {key}: {count}");
        return same == found ? 0 : 1;
    }

    // The Horadric Cube the editor makes (ItemForge.Cube, given to a character created with every quest
    // done), against every cube in the .d2s files under a folder - byte for byte, with what differs from
    // one cube to the next by where and when it dropped wiped from both: id, place, item level, and the
    // game's "just picked up" flag.
    static int CubeCheck(string gameRoot, string folder)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;
        int bits = D2SLib.Globals.stat_id_bits;

        byte[] Blob(Item it)
        {
            var copy = ItemBlob.Restore(Item.Write(it, ItemBlob.Version), bits);
            ItemBlob.Normalise(copy);
            copy.ItemLevel = 0;
            copy.IsNew = false;
            return ItemBlob.TryCapture(copy, bits, out var blob, out _, out string? why) ? blob : throw new InvalidOperationException(why);
        }

        string Fields(Item it) =>
            $"compact {it.IsCompact} ver {it.Version} ilvl {it.ItemLevel} q {it.Quality} gfx {it.HasMultipleGraphics}/{it.GraphicId} "
            + $"lists {it.StatLists.Count} stats {it.StatLists.Sum(l => l.Stats.Count)} flags [{string.Join(",", Enumerable.Range(0, it.Flags.Length).Where(i => it.Flags[i]))}]";

        int cubes = 0, same = 0, unread = 0;
        var levels = new SortedDictionary<int, int>();
        var differing = new Dictionary<string, int>();
        foreach (string file in Directory.EnumerateFiles(folder, "*.d2s", SearchOption.AllDirectories))
        {
            if (file.Contains("scratchpad", StringComparison.OrdinalIgnoreCase))
                continue;

            D2S save;
            try { save = SaveVerifier.Load(file).Save; }
            catch (Exception) { unread++; continue; }

            foreach (var item in (save.PlayerItemList?.Items ?? []).Where(each => each.Code?.Trim() == "box"))
            {
                cubes++;
                levels[item.ItemLevel] = levels.GetValueOrDefault(item.ItemLevel) + 1;
                var ours = Blob(ItemForge.Cube(item.Version));
                var theirs = Blob(ItemBlob.Copy(item, bits));
                if (ours.AsSpan().SequenceEqual(theirs))
                    same++;
                else
                {
                    string key = $"game: {Fields(item)}\n      ours: {Fields(ItemForge.Cube(item.Version))}";
                    differing[key] = differing.GetValueOrDefault(key) + 1;
                }
            }
        }

        Console.WriteLine($"{cubes} cubes, {same} made byte-identical to the game's ({unread} saves unread)");
        Console.WriteLine("  item levels: " + string.Join(", ", levels.Select(pair => $"{pair.Key} x{pair.Value}")));
        foreach (var (key, count) in differing.OrderByDescending(pair => pair.Value))
            Console.WriteLine($"  DIFFERS x{count}\n      {key}");
        return same == cubes ? 0 : 1;
    }

    static int UpgradeCheck(string gameRoot, IReadOnlyList<string> sources)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var items = new List<(string Name, Item Item)>();
        foreach (int index in ItemForge.EnabledUniques())
        {
            var forged = ItemForge.UniqueAt(index, "PD2SPC", RollAs.Random, new Random(index)).Item;
            items.Add((ItemPresenter.Describe(forged).Name, forged));
        }
        foreach (int index in ItemForge.SetItems())
        {
            var forged = ItemForge.SetItemAt(index, "PD2SPC", RollAs.Random, new Random(index)).Item;
            items.Add((ItemPresenter.Describe(forged).Name, forged));
        }
        // Stamped as a real S13 item is, so each one writes through its own blob.
        foreach (var (_, forged) in items) forged.Version = "101";
        int forgedCount = items.Count;

        var seen = new HashSet<string>();
        int alreadyUp = 0, alreadyUpFlagged = 0;
        foreach (string source in sources)
        {
            foreach (string file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                if (!document.RootElement.TryGetProperty("data", out var data)) continue;
                foreach (var record in data.EnumerateArray())
                {
                    if (!record.TryGetProperty("item", out var listed) || listed.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    if (!seen.Add(listed.TryGetProperty("hash", out var hash) ? hash.GetString() ?? "" : Guid.NewGuid().ToString())) continue;
                    var built = TradeItems.Build(listed).Item;
                    if (ItemTier.Rank(built.Code) is null) continue;

                    // Already upgraded in game, as far as its row or automagic says.
                    if (BaseUpgrades.Grants(built).Any())
                    {
                        alreadyUp++;
                        var found = Legitimacy.Check(built);
                        if (found.Count > 0) alreadyUpFlagged++;
                        if (arguments.Contains("--verbose"))
                            Console.WriteLine($"  upgraded in game: {ItemPresenter.Describe(built).Name} [{built.Code.Trim()} {built.Quality}] grants {string.Join(", ", BaseUpgrades.Grants(built))}; {string.Join("; ", found.Select(f => f.Said))}");
                    }
                    items.Add((ItemPresenter.Describe(built).Name, built));
                }
            }
        }

        int tried = 0, steps = 0, noisier = 0, refused = 0, blobBad = 0, skipped = 0, shownDefence = 0;
        var shapes = new Dictionary<string, (int Count, string Example)>();
        var recipes = new Dictionary<string, int>();

        foreach (var (name, item) in items)
        {
            if (!BaseUpgrades.CanUpgrade(item, anyItem: false))
            {
                if (BaseUpgrades.CanUpgrade(item, anyItem: true)) refused++;
                continue;
            }

            int before = Legitimacy.Check(item).Count;
            if (before > 0) { skipped++; continue; }
            tried++;

            var copy = ItemBlob.Copy(item, 10);
            var random = new Random(tried);
            var told = new List<string>();
            while (BaseUpgrades.CanUpgrade(copy, anyItem: false))
            {
                told.AddRange(BaseUpgrades.Upgrade(copy, random));
                steps++;
            }
            foreach (string line in told.Where(line => line.StartsWith("recipe adds")))
                recipes[line] = recipes.GetValueOrDefault(line) + 1;
            if (arguments.Contains("--show") && told.Any(line => line.StartsWith("defence")) && shownDefence++ < 14)
                Console.WriteLine($"  {name} [{item.Quality}{(item.IsEthereal ? ", eth" : "")}]: {string.Join("; ", told)}");

            var back = ItemBlob.Copy(copy, 10);
            if (back.Code != copy.Code || back.Armor != copy.Armor || back.MaxDurability != copy.MaxDurability)
                blobBad++;

            var found = Legitimacy.Check(back);
            if (found.Count > before)
            {
                noisier++;
                if (arguments.Contains("--verbose") && noisier <= 20)
                {
                    Console.WriteLine($"== {name} [{item.Code.Trim()} -> {copy.Code.Trim()} {item.Quality}] {string.Join("; ", told)}");
                    Console.WriteLine($"   ! {string.Join("; ", found.Select(f => f.Said))}");
                }
                foreach (var finding in found)
                {
                    string shape = $"{finding.Certainty} {System.Text.RegularExpressions.Regex.Replace(finding.Said, "[0-9]+", "#")}";
                    shapes[shape] = shapes.TryGetValue(shape, out var had) ? (had.Count + 1, had.Example) : (1, $"{name}: {finding.Said}");
                }
            }
        }

        Console.WriteLine($"items: {forgedCount} forged uniques and set pieces, {items.Count - forgedCount} from the market");
        Console.WriteLine($"upgraded {tried} items ({steps} tier steps); {skipped} flagged before and left out; {refused} only a free upgrade takes");
        Console.WriteLine($"noisier after: {noisier}; blob read-back differs: {blobBad}");
        Console.WriteLine($"market items already upgraded in game (by row or automagic): {alreadyUp}, of which flagged: {alreadyUpFlagged}");
        foreach (var (line, count) in recipes.OrderByDescending(pair => pair.Value))
            Console.WriteLine($"  {count,5}  {line}");
        foreach (var (shape, (count, example)) in shapes.OrderByDescending(pair => pair.Value.Count).Take(15))
            Console.WriteLine($"  {count,5}  {shape}   e.g. {example}");
        return 0;
    }

    static int RollCheck(string gameRoot, IReadOnlyList<string> sources)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var items = new List<(string Name, Item Item)>();
        foreach (string source in sources)
        {
            if (Directory.Exists(source))
            {
                foreach (string file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
                {
                    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                    if (!document.RootElement.TryGetProperty("data", out var data)) continue;
                    foreach (var record in data.EnumerateArray())
                    {
                        if (!record.TryGetProperty("item", out var listed) || listed.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                        var built = TradeItems.Build(listed).Item;
                        if (MagicAffixes.Carries(built)) items.Add((ItemPresenter.Describe(built).Name, built));
                    }
                }
            }
            else
            {
                foreach (var (_, item) in Contents(source))
                    if (!item.IsCompact && MagicAffixes.Carries(item)) items.Add((ItemPresenter.Describe(item).Name, item));
            }
        }

        const int Rolls = 40;
        using var bytes = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        int rolls = 0, noisier = 0, empty = 0, skipped = 0, tintMoved = 0;
        var shown = new HashSet<string>();
        var counts = new Dictionary<(ItemQuality, int), int>();
        var shapes = new Dictionary<string, (int Count, string Example)>();

        foreach (var (name, item) in items)
        {
            var already = Legitimacy.Check(item);
            if (already.Count > 0)
            {
                skipped++;
                if (arguments.Contains("--skipped"))
                    Console.WriteLine($"   skipped {name} [{item.Code.Trim()} {item.Quality}]: {string.Join(" | ", already.Select(finding => finding.Said))}");
                continue;
            }
            for (int n = 0; n < Rolls; n++)
            {
                var copy = ItemBlob.Copy(item, 10);
                var random = new Random(rolls);
                var picks = MagicAffixes.RollFresh(copy, random);
                MagicAffixes.ApplyFresh(copy, picks, random);

                // And again on the result: a reroll of a reroll must leave nothing of the first.
                if (arguments.Contains("--twice"))
                    MagicAffixes.ApplyFresh(copy, MagicAffixes.RollFresh(copy, random), random);
                rolls++;

                // Without its id: a listing that names none draws one at random, as any new item does.
                uint own = copy.Id;
                copy.Id = 0;
                bytes.AppendData(ItemBlob.Bytes(copy, 10));
                copy.Id = own;
                if (arguments.Contains("--bytes"))
                    Console.WriteLine($"  {rolls} {name} {copy.Id} {string.Join(" ", ItemStats.Every(copy).Select(stat => $"{stat.Stat}={stat.Value}"))} {Convert.ToHexString(ItemBlob.Bytes(copy, 10))}");
                if (!Equals(ItemPresenter.Describe(item).SpriteTint, ItemPresenter.Describe(copy).SpriteTint)) tintMoved++;

                int carried = (copy.MagicPrefixIds ?? []).Count(id => id > 0) + (copy.MagicSuffixIds ?? []).Count(id => id > 0);
                counts[(copy.Quality, carried)] = counts.GetValueOrDefault((copy.Quality, carried)) + 1;
                if (carried == 0) empty++;

                var found = Legitimacy.Check(copy);
                if (found.Count > 0)
                {
                    if (arguments.Contains("--verbose") && shown.Add(name))
                    {
                        string Affixes(Item each) => string.Join(", ", MagicAffixes.PairsOn(each).Select(pair => $"{pair.PrefixName}|{pair.SuffixName}"))
                            + (each.IsAutoAffix ? $" auto {each.AutoAffixId}" : "");
                        string Lines(Item each) => string.Join(" ", ItemStats.Every(each).Select(stat => $"{stat.Stat}={stat.Value}"));
                        Console.WriteLine($"== {name} [{item.Code.Trim()} {item.Quality} ilvl {item.ItemLevel}] corrupt {Corruptions.IdOn(item)} recipe {AffixRanges.CraftFamilyOf(item) ?? "(not told)"}; could be {string.Join(", ", AffixRanges.CraftFamilies(item).Select(r => r.Family))}");
                        var probe = ItemBlob.Copy(item, 10);
                        Console.WriteLine($"   pinned: {AffixRanges.PinRecipe(probe)} -> {AffixRanges.CraftFamilyOf(probe)}");
                        var held = ItemStats.Every(item).Select(stat => stat.Stat).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var family in AffixRanges.CraftFamilies(item))
                            Console.WriteLine($"     {family.Family}: {string.Join(", ", family.Grants.Select(g => $"{g}"))}");
                        Console.WriteLine($"   ranges as it is: {string.Join(" ", AffixRanges.For(item).Select(pair => $"{pair.Key}={pair.Value}"))}");
                        Console.WriteLine($"   before: {Affixes(item)}");
                        Console.WriteLine($"     {Lines(item)}");
                        Console.WriteLine($"   after:  {Affixes(copy)}");
                        Console.WriteLine($"     {Lines(copy)}");
                        foreach (var finding in found) Console.WriteLine($"   ! {finding.Said}");
                        var again = ItemBlob.Copy(item, 10);
                        var replay = new Random(rolls - 1);
                        var picksAgain = MagicAffixes.RollFresh(again, replay);
                        Console.WriteLine($"   ranges without affixes: {string.Join(" ", AffixRanges.For(WithoutAffixes(item)).Select(pair => $"{pair.Key}={pair.Value}"))}");
                        Console.WriteLine($"   told: {string.Join("; ", MagicAffixes.ApplyFresh(again, picksAgain, replay))}");
                    }
                    noisier++;
                    foreach (var finding in found)
                    {
                        string shape = $"{finding.Certainty} {System.Text.RegularExpressions.Regex.Replace(finding.Said, "[0-9]+", "#")}";
                        shapes[shape] = shapes.TryGetValue(shape, out var had) ? (had.Count + 1, had.Example) : (1, $"{name}: {finding.Said}");
                    }
                }
            }
        }

        Console.WriteLine($"{items.Count} items ({skipped} already flagged, left out), {rolls} rolls: flagged {noisier}, no affix at all {empty}; picture colour changed {tintMoved}");
        Console.WriteLine($"bytes {Convert.ToHexString(bytes.GetHashAndReset())}");
        foreach (var ((quality, carried), count) in counts.OrderBy(pair => pair.Key.Item1).ThenBy(pair => pair.Key.Item2))
            Console.WriteLine($"  {quality,-6} {carried} affixes: {count}");
        foreach (var (shape, (count, example)) in shapes.OrderByDescending(pair => pair.Value.Count).Take(15))
            Console.WriteLine($"  {count,5}  {shape}   e.g. {example}");
        return 0;
    }

    /// <summary>
    /// Every saved listing under the given folders, rebuilt as Buy rebuilds it, and what the rebuild
    /// had to leave off - the lines the site says that nothing here knows how to write.
    /// </summary>
    static int LeftOff(string gameRoot, IReadOnlyList<string> folders)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var seen = new HashSet<string>();
        var left = new Dictionary<string, (int Count, string Example)>();
        int built = 0, whole = 0;

        foreach (string folder in folders)
        {
            foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
            {
                System.Text.Json.JsonDocument document;
                try { document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file)); }
                catch (Exception) { continue; }

                using (document)
                {
                    if (!document.RootElement.TryGetProperty("data", out var data))
                        continue;

                    foreach (var record in data.EnumerateArray())
                    {
                        // Not every listing is an item - some sell a service or gold.
                        if (!record.TryGetProperty("item", out var listed) || listed.ValueKind != System.Text.Json.JsonValueKind.Object)
                            continue;
                        if (!seen.Add(listed.TryGetProperty("hash", out var hash) ? hash.GetString() ?? file : file))
                            continue;

                        var forged = TradeItems.Build(listed);
                        built++;
                        if (forged.Sound) whole++;

                        foreach (string note in forged.Notes.Where(note => note.StartsWith("left off", StringComparison.OrdinalIgnoreCase)))
                        {
                            string name = listed.TryGetProperty("name", out var named) ? named.GetString() ?? "" : "";
                            left[note] = left.TryGetValue(note, out var had) ? (had.Count + 1, had.Example) : (1, name);
                        }
                    }
                }
            }
        }

        Console.WriteLine($"{built} listings rebuilt, {whole} whole");
        foreach (var (note, (count, example)) in left.OrderByDescending(pair => pair.Value.Count))
            Console.WriteLine($"  {count,4}  {note}   (e.g. {example})");
        return 0;
    }

    /// <summary>
    /// One saved market listing (or a page of them) rebuilt as Buy rebuilds it: what corruption the
    /// editor reads off it, what it could corrupt it with, and what the legitimacy check says.
    /// </summary>
    static int ListingCheck(string gameRoot, string file)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        foreach (var record in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var item = TradeItems.Build(record.GetProperty("item")).Item;
            Console.WriteLine($"{ItemPresenter.Describe(item).Name} ({item.Code}) sprite {ItemPresenter.Describe(item).SpriteName}, named {ItemPresenter.QualityIndex(item)}");
            foreach (var stat in ItemStats.Every(item))
                Console.WriteLine($"    {stat.Stat}[{stat.Param}] = {stat.Value}   \"{StatText.Describe(stat.Stat ?? "", stat.Value)}\"");
            Console.WriteLine($"  corruption {Corruptions.IdOn(item)}: {string.Join(", ", Corruptions.On(item).Select(g => $"{g.Code} {g.Min}-{g.Max}"))}");
            Console.WriteLine($"  shard {Corruptions.ShardFor(item)}; offered: {string.Join(" | ", Corruptions.Available(item, asIfClean: true).Select(o => $"{o.Id} {o.Readable} {o.Width / 10.0:0.#}%"))}");
            var findings = Legitimacy.Check(item);
            Console.WriteLine($"  legitimacy: {(findings.Count == 0 ? "silent" : string.Join("; ", findings.Select(f => f.Said)))}");
        }

        return 0;
    }

    /// <summary>
    /// Every enabled unique, built from its row, corrupted with every corruption it can take and
    /// cleaned again - and, where it is an amulet, desecrated and cleaned too. Counts where the
    /// legitimacy check says more after a step than it said of the item before it. With
    /// <c>--baseline</c>, only how many forged uniques and set items the check is silent on.
    /// </summary>
    static int CleanCheck(string gameRoot)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        int cases = 0, corruptNoisy = 0, cleanNoisy = 0, desecCases = 0, desecNoisy = 0, desecCleanNoisy = 0, bothCleanNoisy = 0;
        var shown = new Dictionary<string, int>();
        var cleanShapes = new Dictionary<string, int>();

        void Say(string kind, string what)
        {
            shown[kind] = shown.GetValueOrDefault(kind) + 1;
            if (shown[kind] <= 6)
                Console.WriteLine($"  {kind}: {what}");
        }

        if (arguments.Contains("--baseline"))
        {
            int items = 0, quiet = 0, total = 0;
            var subjects = new Dictionary<string, int>();
            using var bytes = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (int index in ItemForge.EnabledUniques())
            {
                // A stamp the writer can read; the bytes are held to a baseline, so a new season's accepts it once.
                var forged = ItemForge.UniqueAt(index, "105", RollAs.Random, new Random(index)).Item;
                var found = Legitimacy.Check(forged);
                items++; total += found.Count; if (found.Count == 0) quiet++;
                foreach (var f in found) subjects[f.Said] = subjects.GetValueOrDefault(f.Said) + 1;
                bytes.AppendData(ItemBlob.Bytes(forged, 10));

                // Corrupted and cleaned too: silence says a line is allowed, not that it holds the same number as before.
                foreach (var option in Corruptions.Available(forged).Where(o => !o.GivesSockets))
                {
                    var item = ItemBlob.Copy(forged, 10);
                    var random = new Random(index * 131 + option.Id);
                    Corruptions.Apply(item, Corruptions.Draft(item, option, random));
                    bytes.AppendData(ItemBlob.Bytes(item, 10));
                    Corruptions.Clean(item, random);
                    bytes.AppendData(ItemBlob.Bytes(item, 10));
                }
            }
            foreach (int index in ItemForge.SetItems())
            {
                var forged = ItemForge.SetItemAt(index, "105", RollAs.Random, new Random(index)).Item;
                var found = Legitimacy.Check(forged);
                items++; total += found.Count; if (found.Count == 0) quiet++;
                foreach (var f in found) subjects[f.Said] = subjects.GetValueOrDefault(f.Said) + 1;
                bytes.AppendData(ItemBlob.Bytes(forged, 10));
            }
            Console.WriteLine($"forged {items}: silent {quiet}, findings {total}");
            Console.WriteLine($"bytes {Convert.ToHexString(bytes.GetHashAndReset())}");
            Console.WriteLine("can drop skinned: " + string.Join(", ", ItemForge.EnabledUniques()
                .Select(index => ItemForge.UniqueAt(index, "PD2SPC").Item)
                .Where(Skins.CanHave)
                .Select(each => $"{ItemPresenter.Describe(each).Name} ({ItemPresenter.Describe(each).SpriteName})")));
            foreach (var (said, count) in subjects.OrderBy(pair => pair.Key)) Console.WriteLine($"  {count} {said}");
            return 0;
        }

        foreach (int fileIndex in ItemForge.EnabledUniques())
        {
            Item Fresh(int seed) => ItemForge.UniqueAt(fileIndex, "PD2SPC", RollAs.Random, new Random(seed)).Item;

            var probe = Fresh(fileIndex);
            int start = Legitimacy.Check(probe).Count;
            string name = ItemPresenter.Describe(probe).Name;

            foreach (var option in Corruptions.Available(probe).Where(o => !o.GivesSockets))
            {
                var item = Fresh(fileIndex);
                var random = new Random(fileIndex * 131 + option.Id);
                Corruptions.Apply(item, Corruptions.Draft(item, option, random));
                cases++;

                int corrupted = Legitimacy.Check(item).Count;
                if (corrupted > start) { corruptNoisy++; Say("corrupt", $"{name} + {option.Summary}: {Legitimacy.Check(item)[0].Said}"); }

                if (Desecrations.CanTake(item))
                {
                    foreach (var mod in Desecrations.Pool(item))
                    {
                        var both = Fresh(fileIndex);
                        var r = new Random(fileIndex * 7919 + option.Id * 31 + mod.Id);
                        Corruptions.Apply(both, Corruptions.Draft(both, option, r));
                        Desecrations.Apply(both, Corruptions.Draft(both, mod, r));
                        desecCases++;

                        int desecrated = Legitimacy.Check(both).Count;
                        if (desecrated > start) { desecNoisy++; Say("desecrate", $"{name} + {option.Summary} + {mod.Summary}: {Legitimacy.Check(both)[0].Said}"); }

                        // Forgotten, as after a reload: the clean has to tell the mod from the item.
                        Desecrations.Assume(both, null);
                        var copy = Fresh(fileIndex);
                        var r2 = new Random(fileIndex * 7919 + option.Id * 31 + mod.Id);
                        Corruptions.Apply(copy, Corruptions.Draft(copy, option, r2));
                        Desecrations.Apply(copy, Corruptions.Draft(copy, mod, r2));
                        Desecrations.Assume(copy, null);

                        Desecrations.Clean(both, null, r, out _);
                        if (Legitimacy.Check(both).Count > start) { desecCleanNoisy++; Say("desecration clean", $"{name} + {option.Summary} + {mod.Summary} (told {Desecrations.Identify(copy).Found?.Summary ?? "?"}): {Legitimacy.Check(both).FirstOrDefault()?.Said}"); }

                        Corruptions.Clean(copy, r);
                        if (Legitimacy.Check(copy).Count > start) { bothCleanNoisy++; Say("corruption clean under desecration", $"{name} + {option.Summary} + {mod.Summary}: {Legitimacy.Check(copy).FirstOrDefault()?.Said}"); }
                    }
                }

                Corruptions.Clean(item, random);
                if (Legitimacy.Check(item).Count > start)
                {
                    cleanNoisy++;
                    Say("clean", $"{name} + {option.Summary}: {Legitimacy.Check(item)[0].Said}");
                    string shape = $"{option.Wants} id {option.Id} {option.Summary} -> {Legitimacy.Check(item)[0].Subject}";
                    cleanShapes[shape] = cleanShapes.GetValueOrDefault(shape) + 1;
                }
            }
        }

        foreach (var (shape, count) in cleanShapes.OrderByDescending(pair => pair.Value).Take(25))
            Console.WriteLine($"    {count,4}  {shape}");
        Console.WriteLine($"corruptions {cases}: corrupting noisier {corruptNoisy}, cleaning noisier {cleanNoisy}");
        Console.WriteLine($"desecrations {desecCases}: desecrating noisier {desecNoisy}, desecration clean noisier {desecCleanNoisy}, corruption clean under it noisier {bothCleanNoisy}");
        return 0;
    }

    /// <summary>
    /// Every desecrated listing in a folder of saved market pages, rebuilt as Buy rebuilds it: which
    /// pool mod identification finds, whether a value-2 one is enhanced damage, and what the
    /// legitimacy check still says.
    /// </summary>
    static int DesecrationScan(string gameRoot, string folder, bool verbose)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var seen = new HashSet<string>();
        int corruptCleans = 0, corruptNoisier = 0, tradeRight = 0, tradeRefused = 0;
        int trips = 0, tripNotCleaned = 0, tripTold = 0, tripAmbiguous = 0, tripWrong = 0, tripNoisier = 0;
        int found = 0, ambiguous = 0, none = 0, damageRight = 0, damageWrong = 0, clean = 0, flagged = 0;
        var byMod = new Dictionary<string, int>();

        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            foreach (var record in document.RootElement.GetProperty("data").EnumerateArray())
            {
                var listed = record.GetProperty("item");
                if (!seen.Add(listed.GetProperty("hash").GetString() ?? ""))
                    continue;

                var item = TradeItems.Build(listed).Item;
                if (!Desecrations.IsDesecrated(item))
                    continue;

                // Buy's side: what the listing says, through the bytes and back.
                {
                    int said = listed.GetProperty("modifiers").EnumerateArray()
                        .Where(m => m.TryGetProperty("name", out var n) && n.GetString() == Desecrations.ValueStat)
                        .Select(m => m.TryGetProperty("values", out var v) && v.GetArrayLength() > 0 ? (int)v[0].GetDouble() : -1)
                        .FirstOrDefault(-1);
                    string Lines(Item each) => string.Join(" ", ItemStats.Every(each).OrderBy(stat => stat.Id).ThenBy(stat => stat.Param)
                        .Select(stat => $"{stat.Stat}#{stat.Param}={stat.Value}"));

                    if (!ItemBlob.TryCapture(item, 10, out var blob, out _, out var refusedWhy))
                    {
                        tradeRefused++;
                        Console.WriteLine($"  trade refused: {listed.GetProperty("name").GetString()}: {refusedWhy}");
                    }
                    else
                    {
                        var back = ItemBlob.Restore(blob, 10);
                        bool valueRight = Desecrations.ValueOn(back) == said;
                        bool flagRight = ItemStats.Every(back).Any(stat => stat.Stat == Desecrations.FlagStat && stat.Value == 1);
                        bool linesRight = Lines(back) == Lines(item);
                        bool drawn = ItemPresenter.Describe(back).SpriteName?.EndsWith("d") == true;
                        if (valueRight && flagRight && linesRight && drawn) tradeRight++;
                        else Console.WriteLine($"  trade wrong: {listed.GetProperty("name").GetString()} value {Desecrations.ValueOn(back)} vs listed {said}, flag {flagRight}, lines {linesRight}, sprite {ItemPresenter.Describe(back).SpriteName}");
                    }
                }

                var identified = Desecrations.Identify(item);
                int value = Desecrations.ValueOn(item) ?? 0;
                string name = listed.GetProperty("name").GetString() ?? "";
                string quality = listed.GetProperty("quality").GetProperty("name").GetString() ?? "";

                if (identified.Found is { } mod)
                {
                    found++;
                    string key = $"{mod.Summary}  (width {mod.Width})";
                    byMod[key] = byMod.GetValueOrDefault(key) + 1;
                    if (value == Desecrations.ForDamage)
                    {
                        if (Desecrations.Fits(item, mod, Desecrations.ForDamage)) damageRight++; else damageWrong++;
                    }
                }
                else if (identified.Candidates.Count > 0) ambiguous++;
                else none++;

                var findings = Legitimacy.Check(item);
                if (findings.Count == 0) clean++; else flagged++;

                // Round trip: clean the mod off, desecrate again at random, and ask whether the new
                // mod is told from the item alone and whether the check stays as quiet as before.
                if (identified.Found is not null)
                {
                    var again = TradeItems.Build(listed).Item;
                    var random = new Random(seen.Count);
                    var before = ItemStats.Every(again).Select(stat => $"{stat.Stat}={stat.Value}").ToList();
                    bool cleaned = Desecrations.Clean(again, null, random, out var cleanTold) && !Desecrations.IsDesecrated(again);
                    int afterClean = Legitimacy.Check(again).Count;
                    if (verbose && afterClean > findings.Count)
                    {
                        Console.WriteLine($"    old {identified.Found.Summary} {again.Quality}; told {string.Join(", ", cleanTold.Select(c => $"{c.Stat} {c.Was}->{c.Now}{(c.Removed ? " removed" : "")}"))}");
                        Console.WriteLine($"    before {string.Join(" ", before)}");
                        foreach (var finding in Legitimacy.Check(again)) Console.WriteLine($"    {finding.Said}");
                    }
                    var rolled = Desecrations.Roll(again, random)!;
                    Desecrations.Apply(again, Corruptions.Draft(again, rolled, random));
                    var told = Desecrations.Identify(again);
                    int afterApply = Legitimacy.Check(again).Count;

                    trips++;
                    if (!cleaned) tripNotCleaned++;
                    if (told.Found?.Id == rolled.Id) tripTold++;
                    else if (told.Found is null && told.Candidates.Any(c => c.Id == rolled.Id)) tripAmbiguous++;
                    else { tripWrong++; Console.WriteLine($"  trip wrong: {name} rolled {rolled.Summary} told {told.Found?.Summary ?? "?"}"); }
                    if (afterClean > findings.Count || afterApply > findings.Count)
                    {
                        tripNoisier++;
                        Console.WriteLine($"  trip noisier: {name} {findings.Count} -> clean {afterClean}, apply {afterApply} ({rolled.Summary}): {Legitimacy.Check(again).FirstOrDefault()}");
                    }
                }

                // The corruption's clean goes through the same subtraction: never noisier either.
                {
                    var bare = TradeItems.Build(listed).Item;
                    var moved = Corruptions.Clean(bare, new Random(seen.Count));
                    int after = Legitimacy.Check(bare).Count;
                    corruptCleans++;
                    if (after > findings.Count)
                    {
                        corruptNoisier++;
                        Console.WriteLine($"  corruption clean noisier: {name} {findings.Count} -> {after}: {Legitimacy.Check(bare).FirstOrDefault()?.Said} [{string.Join(", ", moved.Select(c => $"{c.Stat} {c.Was}->{c.Now}"))}]");
                    }
                }

                if (verbose && identified.Found is null && findings.Count == 0)
                {
                    var ranges = AffixRanges.For(item);
                    foreach (var stat in ItemStats.Every(item))
                        Console.WriteLine($"      {stat.Stat}[{stat.Param}] = {stat.Value}  range {ranges.GetValueOrDefault(AffixRanges.KeyOf(stat))}");
                    Console.WriteLine($"      corruption: {string.Join(", ", Corruptions.On(item).Select(g => $"{g.Code} {g.Min}-{g.Max}"))}");
                }

                if (identified.Found is null || findings.Count > 0)
                    Console.WriteLine($"  {quality,-8} {name,-28} v{value} -> {(identified.Found?.Summary ?? $"? {string.Join(" | ", identified.Candidates.Select(c => c.Summary))}")}"
                                      + (findings.Count > 0 ? $"  [{findings.Count} findings: {findings[0]}]" : ""));
            }
        }

        Console.WriteLine($"identified {found}, ambiguous {ambiguous}, nothing fits {none}; value 2 as damage {damageRight}, not {damageWrong}; legitimacy silent {clean}, flagged {flagged}");
        Console.WriteLine($"trade rebuilt through bytes and back, all right: {tradeRight}, refused {tradeRefused}");
        Console.WriteLine($"corruption cleans {corruptCleans}: noisier {corruptNoisier}");
        Console.WriteLine($"round trips {trips}: not cleaned {tripNotCleaned}, new mod told {tripTold}, ambiguous {tripAmbiguous}, wrong {tripWrong}, check noisier {tripNoisier}");
        foreach (var (mod, count) in byMod.OrderByDescending(pair => pair.Value))
            Console.WriteLine($"  {count,4}  {mod}");
        return 0;
    }

    // Every property an item can carry, as a spreadsheet: what the engine calls it, what the
    // game calls it, what shape it is and what the editor offers for it - with an empty column
    // to write in. For going over the properties section and finding what is missing.
    static int Stats(string gameRoot, string? outPath)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        string path = outPath is { Length: > 0 } named
            ? named
            : Path.Combine(Environment.CurrentDirectory, "properties.csv");

        // Excel reads a comma-separated file as the local encoding unless the bytes say
        // otherwise, so the mark goes on the front and the pound signs and degrees survive.
        var text = new StringBuilder();

        text.AppendLine(string.Join(',',
            "Id", "Engine name", "Game wording", "Shape", "Editor fields", "Note",
            "Value bits", "Param bits", "Encode", "descfunc", "Range", "Part of a run",
            "Your wording", "Comments"));

        foreach (var choice in StatCatalogue.All.OrderBy(entry => entry.Id))
        {
            var shape = StatShapes.Of(choice.Name);

            string range = StatFields.RangeOf(choice.Name) is { } span
                ? $"{span.Min} to {span.Max}"
                : "";

            var row = D2SLib.Core.TXT.ItemStatCostTXT[choice.Name];

            string Cell(string column) => row?[column]?.Value?.Trim() ?? "";

            text.AppendLine(string.Join(',', new[]
            {
                choice.Id.ToString(),
                choice.Name,
                choice.Wording,
                shape.ToString(),
                StatShapes.Fields(shape),
                choice.Note,
                Cell("Save Bits"),
                Cell("Save Param Bits"),
                Cell("Encode"),
                Cell("descfunc"),
                range,
                ItemStats.IsInRun(choice.Id) ? "yes" : "",

                // Started off with what the editor says today, so the column is a thing to
                // correct rather than a blank page. Where it is empty the tables word the
                // property not at all, which is exactly where someone's own words are worth
                // most.
                choice.Wording,
                "",
            }.Select(Quoted)));
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Console.WriteLine($"{StatCatalogue.All.Count} properties written to {path}");
        return 0;
    }

    // Every property that writes more than one stat, checked part by part against what the
    // table says each part should hold. A property is not one stat, and a part that goes
    // missing goes missing quietly: the item writes, it round-trips, the audit says nothing,
    // and the effect is simply absent in game.
    static int Props(string gameRoot)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        if (game.Read(@"data\global\excel\properties.txt") is not { } hit)
            throw new FileNotFoundException("properties.txt is not in any loaded archive.");

        string[] lines = System.Text.Encoding.ASCII.GetString(hit.Data)
            .Replace("\r", "").Split('\n');

        string[] header = lines[0].Split('\t');
        int Column(string name) => Array.IndexOf(header, name);

        // Numbers chosen to be told apart: the roll, the two ends, and the parameter are all
        // different, so a part holding the wrong one names itself.
        const int Low = 11, High = 29, Roll = 17, Param = 7;

        int looked = 0, wrong = 0;

        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split('\t');
            if (cells.Length < header.Length || cells[0].Trim() is not { Length: > 0 } code)
                continue;

            string Cell(int at) => at >= 0 && cells.Length > at ? cells[at].Trim() : "";

            var parts = new List<(string Stat, string Func, int At)>();

            for (int n = 1; n <= 7; n++)
            {
                string stat = Cell(Column($"stat{n}"));
                if (stat.Length > 0)
                    parts.Add((stat, Cell(Column($"func{n}")), n - 1));
            }

            // One stat is still several fields where the function packs a skill into it, and
            // an audit that only looked at multi-stat rows never saw those: fourteen properties
            // wrote a chance with no skill behind it and nothing said so.
            bool packs = parts.Any(part => part.Func is "11" or "19");

            // A func-17 property whose row gives no range takes its number from the parameter.
            // Another single-stat shape, and the second one found writing nothing at all: every
            // repair affix read "Repairs 0 durability per second".
            bool fromParam = parts.Any(part => part.Func == "17");

            if (parts.Count < 2 && !packs && !fromParam)
                continue;

            looked++;

            var written = AffixRanges.Written(code, Low, High, Roll, Param)
                .ToDictionary(each => each.Stat, each => each, StringComparer.OrdinalIgnoreCase);

            var faults = new List<string>();
            int? previous = null;

            // Whether the pair survives the way in. A packed property's two numbers are the
            // chance and the level, in that order, and sorting them - which every other property
            // wants - handed the level to the chance.
            var kept = AffixRanges.Ordered(code, High, Low);

            if (packs && kept != (High, Low))
                faults.Add($"the pair is sorted on the way in, so the chance takes the level");

            if (!packs && kept != (Low, High))
                faults.Add($"the pair is left unsorted, so a range can read backwards");

            foreach (var (stat, func, at) in parts)
            {
                if (!written.TryGetValue(stat, out var got))
                {
                    faults.Add($"{stat} (func {func}) not written at all");
                    previous = null;
                    continue;
                }

                // The packed functions are three fields wearing one row, so all three are asked
                // after: how often or how many, which skill, and at what level.
                if (func is "11" or "19")
                {
                    string Said(int? held) => held?.ToString() ?? "nothing";

                    if (got.Value != Low)
                        faults.Add($"{stat} (func {func}) holds {got.Value}, expected {Low}");

                    if (got.SkillId != Param)
                        faults.Add($"{stat} (func {func}) names skill {Said(got.SkillId)}, expected {Param}");

                    if (got.SkillLevel != High)
                        faults.Add($"{stat} (func {func}) casts at level {Said(got.SkillLevel)}, expected {High}");

                    if (func is "19" && got.MaxCharges != Low)
                        faults.Add($"{stat} (func 19) holds {Said(got.MaxCharges)} charges, expected {Low}");

                    previous = got.Value;
                    continue;
                }

                // What the table says this part should hold.
                int? want = func switch
                {
                    "15" => Low,
                    "16" => High,
                    "17" => at > 0 ? Param : Roll,
                    "3" => previous,
                    _ => Roll,
                };

                // Held at the field's edge where the number is more than it stores, as the game holds it (PD2 clamps,
                // never wraps): a 4-bit item_elemskill takes 15 of a 17, and that is the item right, not a fault.
                if (want is { } asked && Math.Min(asked, StatFields.CeilingOf(stat) ?? int.MaxValue) is var expected
                    && got.Value != expected)
                    faults.Add($"{stat} (func {func}) holds {got.Value}, expected {expected}");

                previous = got.Value;
            }

            if (fromParam)
            {
                // The same property asked with nothing in its range, which is how the rows that
                // carry their number in the parameter are written.
                var blank = AffixRanges.Written(code, 0, 0, 0, Param)
                    .ToDictionary(each => each.Stat, each => each.Value, StringComparer.OrdinalIgnoreCase);

                foreach (var (stat, _, _) in parts.Where(part => part.Func == "17"))
                {
                    if (!blank.TryGetValue(stat, out int got))
                        faults.Add($"{stat} (func 17, no range) not written at all");
                    else if (got != Param)
                        faults.Add($"{stat} (func 17, no range) holds {got}, expected the parameter {Param}");
                }
            }

            if (faults.Count == 0)
                continue;

            wrong++;
            Console.WriteLine($"{code}");
            foreach (string fault in faults)
                Console.WriteLine($"    {fault}");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"{looked} properties write more than one field; {wrong} disagree with the table.");
        return wrong == 0 ? 0 : 1;
    }

    /// <summary>
    /// Smart sort's folder links on a scratch vault: a category's folder followed when renamed and
    /// moved, made again when deleted, and an unlinked folder of the name taken on.
    /// </summary>
    static int LinkTest(string gameRoot)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        string file = Path.Combine(Path.GetTempPath(), $"linktest-{Guid.NewGuid():N}.pd2lib");
        int failures = 0;
        void Expect(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }

        using (var vault = Vault.Open(file, "S13", 10))
        {
            string PathOf(long id)
            {
                var all = vault.Folders().ToDictionary(each => each.Id);
                var names = new List<string>();
                for (long? at = id; at is { } step; at = all[step].ParentId)
                    names.Insert(0, all[step].Name);
                return string.Join("/", names);
            }

            long small = vault.FolderFor(["Charms", "Small"]);
            Expect(PathOf(small) == "Charms/Small", $"made Charms/Small ({PathOf(small)})");
            Expect(vault.FolderFor(["Charms", "Small"]) == small, "same category, same folder");

            long charms = vault.Folders().Single(each => each.Name == "Charms").Id;
            vault.RenameFolder(charms, "My Charms");
            Expect(vault.FolderFor(["Charms", "Small"]) == small, "followed after the parent was renamed");
            long grand = vault.FolderFor(["Charms", "Grand"]);
            Expect(PathOf(grand) == "My Charms/Grand", $"a new level goes inside the renamed parent ({PathOf(grand)})");

            long elsewhere = vault.CreateFolder(null, "Elsewhere");
            vault.MoveFolder(small, elsewhere);
            Expect(vault.FolderFor(["Charms", "Small"]) == small, "followed after being moved");

            vault.DeleteFolder(small, everything: false);
            long again = vault.FolderFor(["Charms", "Small"]);
            Expect(again != small && PathOf(again) == "My Charms/Small", $"made again after delete ({PathOf(again)})");

            long mine = vault.CreateFolder(null, "Runewords");
            long runewords = vault.FolderFor(["Runewords", "Helms"]);
            Expect(vault.Folders().Single(each => each.Id == runewords).ParentId == mine, "an unlinked folder of the name is taken on");

            vault.RenameFolder(mine, "Words");
            long taken = vault.CreateFolder(null, "Runewords");
            Expect(vault.FolderFor(["Runewords", "Helms"]) == runewords, "a linked folder wins over a new one of the old name");
            Expect(vault.Folders().Count(each => each.Name == "Runewords" && each.ParentId is null) == 1 && taken > 0, "and the new one is left alone");

            vault.DeleteFolder(vault.Folders().Single(each => each.Name == "My Charms").Id, everything: true);
            long fresh = vault.FolderFor(["Charms", "Small"]);
            Expect(PathOf(fresh) == "Charms/Small", $"whole tree deleted: made fresh ({PathOf(fresh)})");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string each in new[] { file, file + "-wal", file + "-shm" })
            if (File.Exists(each)) File.Delete(each);

        Console.WriteLine(failures == 0 ? "all passed" : $"{failures} failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Taking gear off as the editor does, checked through a write and a read: the character's
    /// gloves and the mercenary's first item placed in the stash's first free cells, the file written
    /// to a temporary copy and read back. Never the save itself.
    /// </summary>
    static int SetLists(string gameRoot, IReadOnlyList<string> sources)
    {
        // Read-only: every set piece in these saves, with the lists it stores beside the bonus tiers
        // its setitems.txt row has - whether the game keeps a piece's green bonuses on the item.
        using var installed = Season.Open(gameRoot, Tables());
        var game = installed.Game;

        var (rows, _) = game.ReadExcel("setitems");
        int marker = QualityRows.MarkerIn(rows);

        int seen = 0, withLists = 0, withTiers = 0, matched = 0;
        foreach (string source in sources)
        {
            foreach (string file in Directory.Exists(source) ? Directory.GetFiles(source, "*.d2s", SearchOption.AllDirectories) : [source])
            {
                D2S save;
                try { save = SaveVerifier.Load(file).Save; }
                catch (Exception) { continue; }

                foreach (var item in SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary))
                {
                    if (item.Quality != ItemQuality.Set)
                        continue;

                    int row = QualityRows.RowOf((int)item.FileIndex, marker);
                    int tiers = row < rows.Rows.Count
                        ? Enumerable.Range(1, 5).Count(tier => rows.Value(row, $"aprop{tier}a").Trim().Length > 0 || rows.Value(row, $"aprop{tier}b").Trim().Length > 0)
                        : -1;

                    seen++;
                    if (item.StatLists.Count > 1) withLists++;
                    if (tiers > 0) withTiers++;
                    // The same row forged, beside the game's own: mask and each green list's stats.
                    var forged = ItemForge.SetItemAt((int)item.FileIndex, item.Version ?? "", RollAs.Maximum).Item;
                    string Lists(Item each) => string.Join(" / ", each.StatLists.Skip(1).Take(5).Select(list =>
                        string.Join(",", list.Stats.Select(stat => stat.Stat).OrderBy(name => name))));
                    bool same = forged.SetItemMask == item.SetItemMask && Lists(forged) == Lists(item);
                    if (same) matched++;

                    if (seen <= 40 || (tiers > 0 && item.SetItemMask == 0) || !same)
                    {
                        Console.WriteLine($"{Path.GetFileName(file),-24} {ItemPresenter.Describe(item).Name,-30} mask {Convert.ToString(item.SetItemMask, 2).PadLeft(5, '0')} lists {item.StatLists.Count}  table tiers {tiers}  add func {(row < rows.Rows.Count ? rows.Value(row, "add func").Trim() : "?")}"
                                          + $"  forged {(same ? "same" : $"DIFFERS: mask {Convert.ToString(forged.SetItemMask, 2).PadLeft(5, '0')} [{Lists(forged)}] vs [{Lists(item)}]")}");
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{seen} set pieces; {withLists} store extra lists; {withTiers} have bonus tiers in the table; {matched} forged the same");
        return 0;
    }

    static int SafeWriteCheck(string folder)
    {
        // SaveWriter.WriteSafely on files of its own in the folder given - never a save: a write that
        // swaps in and reads back, a write refused while another program holds the file (the old one
        // left byte for byte, nothing beside it), and ClearPending taking only its own leftovers.
        Directory.CreateDirectory(folder);
        int failures = 0;
        void Say(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "pass" : "FAIL")}  {what}");
            if (!ok) failures++;
        }

        string path = Path.Combine(folder, "safewrite.d2s");
        byte[] before = [1, 2, 3, 4], after = [5, 6, 7, 8, 9];
        File.WriteAllBytes(path, before);

        SaveWriter.WriteSafely(path, after);
        Say(File.ReadAllBytes(path).AsSpan().SequenceEqual(after) && !File.Exists(path + SaveWriter.PendingSuffix),
            "written, swapped in, nothing left beside it");

        string fresh = Path.Combine(folder, "fresh.d2s");
        File.Delete(fresh);
        SaveWriter.WriteSafely(fresh, before);
        Say(File.ReadAllBytes(fresh).AsSpan().SequenceEqual(before), "a new file, moved into place");

        bool refused = false;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try
            {
                SaveWriter.WriteSafely(path, before);
            }
            catch (Exception)
            {
                refused = true;
            }
        }
        Say(refused && File.ReadAllBytes(path).AsSpan().SequenceEqual(after) && !File.Exists(path + SaveWriter.PendingSuffix),
            "held by another program: refused, the old file untouched, nothing left beside it");

        File.WriteAllBytes(path + SaveWriter.PendingSuffix, before);
        File.WriteAllBytes(Path.Combine(folder, "keep.d2s.bak"), before);
        int cleared = SaveWriter.ClearPending(folder);
        Say(cleared == 1 && !File.Exists(path + SaveWriter.PendingSuffix) && File.Exists(Path.Combine(folder, "keep.d2s.bak"))
            && File.Exists(path), "a crash's leftover cleared, and only that");

        Console.WriteLine(failures == 0 ? "all passed" : $"{failures} failed");
        return failures == 0 ? 0 : 1;
    }

    static int VaultUndoCheck(string gameRoot)
    {
        // The vault's undo log (VaultUndo) on a vault of its own: each action logged, then taken back
        // newest first - the whole file as it was before each - then redone - as it was after each.
        // A note written over (a REPLACE), a card deleted with its note and index rows (a cascade),
        // folders made, renamed and filled.
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        string folder = Path.Combine(Path.GetTempPath(), $"vaultundo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        int failures = 0;
        void Say(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "pass" : "FAIL")}  {what}");
            if (!ok) failures++;
        }

        using var vault = Vault.Open(Path.Combine(folder, "c.pd2lib"), "S13", 10);
        int reported = 0;
        var log = vault.StartUndo(() => reported++);

        string Dump()
        {
            var text = new System.Text.StringBuilder();
            foreach (string table in new[] { "item", "item_meta", "item_stat", "folder", "folder_link", "about" })
            {
                using var read = vault.Connection.CreateCommand();
                read.CommandText = $"SELECT rowid, * FROM \"{table}\" ORDER BY rowid;";
                try
                {
                    using var row = read.ExecuteReader();
                    while (row.Read())
                    {
                        text.Append(table).Append(':');
                        for (int i = 0; i < row.FieldCount; i++)
                            text.Append(row.GetValue(i) is byte[] bytes ? Convert.ToHexString(bytes) : row.GetValue(i)?.ToString()).Append('|');
                        text.Append('\n');
                    }
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                    text.Append(table).Append(": absent\n");
                }
            }
            return text.ToString();
        }

        var item = ItemForge.Unique(ItemForge.UniqueNames().First(), "103").Item;
        item.Id = 0x2345678;
        long key = 0, made = 0;
        var steps = new List<(string What, string Before, string After, long From, long To)>();
        void Step(string what, Action act)
        {
            string before = Dump();
            long from = log.Mark;
            act();
            steps.Add((what, before, Dump(), from, log.Mark));
        }

        Step("card stored", () => key = vault.Add(item, out _) ?? 0);
        Step("gold put in", () => vault.RecordGold([new GoldMove("edited", 500)]));
        Step("gold taken out", () => vault.RecordGold([new GoldMove("edited", -200)]));
        Step("folder made", () => made = vault.CreateFolder(null, "Charms"));
        Step("folder renamed", () => vault.RenameFolder(made, "Small charms"));
        Step("card moved into it", () => vault.MoveItems([key], made));
        Step("card deleted", () => vault.Remove([key]));

        Say(reported > 0 && steps.All(each => each.To > each.From), $"every action logged ({reported} changes reported)");

        var redo = new List<(long From, long To)>();
        for (int n = steps.Count - 1; n >= 0; n--)
        {
            var (what, before, _, from, to) = steps[n];
            redo.Insert(0, log.Apply(from, to));
            Say(Dump() == before, $"undo: {what}");
        }

        for (int n = 0; n < steps.Count; n++)
        {
            var (what, _, after, _, _) = steps[n];
            log.Apply(redo[n].From, redo[n].To);
            Say(Dump() == after, $"redo: {what}");
        }

        using (var check = vault.Connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM main.sqlite_master WHERE name LIKE 'vault_undo%';";
            Say(Convert.ToInt64(check.ExecuteScalar()) == 0, "no trigger or log table in the file itself");
        }

        Console.WriteLine(failures == 0 ? "all passed" : $"{failures} failed");
        return failures == 0 ? 0 : 1;
    }

    static int ThinCheck(string folder)
    {
        // Backup thinning on empty files named as backups, in a folder of their own: every recent
        // one stays whatever the count, the older ones are one per span and capped, the earliest in
        // each span kept.
        int failures = 0;
        void Say(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "pass" : "FAIL")}  {what}");
            if (!ok) failures++;
        }

        // A folder of its own inside the one named, and only that goes again: the one named may hold anything.
        string scratch = Path.Combine(folder, $"thincheck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            return Thinning(scratch, Say, () => failures);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>What <see cref="ThinCheck"/> proves, in the scratch folder it made.</summary>
    static int Thinning(string folder, Action<bool, string> Say, Func<int> failures)
    {
        string save = Path.Combine(folder, "Tester.d2s");
        File.WriteAllBytes(save, [0]);
        string backups = SaveWriter.BackupFolderFor(save);
        Directory.CreateDirectory(backups);

        DateTime now = DateTime.Now;
        void Make(TimeSpan ago)
        {
            DateTime at = now - ago;
            string file = Path.Combine(backups, $"Tester.d2s.{at:yyyyMMdd-HHmmss}.bak");
            File.WriteAllBytes(file, [0]);
            File.SetLastWriteTime(file, at);
        }

        // Twenty in the last ten minutes, as autosave leaves them; five an hour apart before.
        for (int n = 0; n < 20; n++)
            Make(TimeSpan.FromSeconds(30 * n + 5));
        for (int n = 2; n <= 6; n++)
            Make(TimeSpan.FromHours(n));

        int removed = SaveWriter.Prune(save, 2);
        var left = SaveWriter.BackupsOf(save);
        int recent = left.Count(file => now - SaveWriter.TakenAt(file) < SaveWriter.KeptWhole);
        Say(recent == 20, $"every recent one kept whatever the count ({recent} of 20)");
        Say(left.Count - recent == 2 && removed == 3, $"older ones capped at the count ({left.Count - recent} left, {removed} deleted)");
        Say(left.Where(file => now - SaveWriter.TakenAt(file) >= SaveWriter.KeptWhole)
                .All(file => now - SaveWriter.TakenAt(file) < TimeSpan.FromHours(3.5)), "the newest older ones kept");

        // Within one span, the earliest stays: three at 40, 41 and 42 minutes ago share a ten-minute span
        // only if they fall in it - the fixed clock below makes sure they do.
        var fixedNow = new DateTime(2026, 9, 28, 12, 0, 0);
        var files = new[] { 44, 43, 42 }
            .Select(minutes => new FileInfo(Path.Combine(backups, $"Tester.d2s.{fixedNow.AddMinutes(-minutes):yyyyMMdd-HHmmss}.bak")))
            .OrderByDescending(file => SaveWriter.TakenAt(file))
            .ToList();
        var thinned = SaveWriter.Thinned([new FileInfo(save), .. files], fixedNow);
        Say(thinned.Count == 2 && SaveWriter.TakenAt(thinned[1]) == fixedNow.AddMinutes(-44),
            $"one per span, its earliest ({string.Join(", ", thinned.Skip(1).Select(file => SaveWriter.TakenAt(file).ToString("HH:mm")))})");

        Say(SaveWriter.Prune(save, null) == 0 && SaveWriter.BackupsOf(save).Count == left.Count, "no limit keeps every one");

        // "Never" on the Settings page: the recent ones stay, nothing older.
        SaveWriter.Prune(save, 0);
        var never = SaveWriter.BackupsOf(save);
        Say(never.All(file => now - SaveWriter.TakenAt(file) < SaveWriter.KeptWhole) && never.Count == recent,
            $"zero keeps the recent ones and nothing older ({never.Count} left)");

        Console.WriteLine(failures() == 0 ? "all passed" : $"{failures()} failed");
        return failures() == 0 ? 0 : 1;
    }

    static int JournalCheck(string gameRoot, string save, string folder)
    {
        // MoveRecovery on saves cut short, each made by hand on a copy of the save given, in a
        // vault of its own in the folder: a store and a withdrawal that left a copy
        // in both places (said, left), the same two with the item in neither (put back), gold both
        // ways, a finished save whose journal was not cleared (nothing said), and a cleared one.
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        Directory.CreateDirectory(folder);
        int failures = 0;
        void Say(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "pass" : "FAIL")}  {what}");
            if (!ok) failures++;
        }

        string copy = Path.Combine(folder, "Tester.d2s");
        File.Copy(save, copy, overwrite: true);
        foreach (string file in Directory.GetFiles(folder, "v.pd2lib*"))
            File.Delete(file);

        var (character, _) = SaveVerifier.Load(copy);
        var item = character.PlayerItemList.Items.First(each => !each.IsCompact && each.Mode != ItemMode.Equipped);
        uint id = item.Id;
        Console.WriteLine($"item: {item.Code.Trim()} id {id:X}");

        using var vault = Vault.Open(Path.Combine(folder, "v.pd2lib"), "S13", 10);
        IReadOnlyList<string> Recover()
        {
            var said = MoveRecovery.Recover(vault);
            foreach (string line in said)
                Console.WriteLine($"      said: {line}");
            return said;
        }

        long InFile() => SaveCounts.Read(copy).Count(id, item.Code);
        long Purse() => SaveCounts.Read(copy).Purse(MoveRecovery.CarriedPurse);
        void WriteFile(bool withItem)
        {
            var (fresh, _) = SaveVerifier.Load(copy);
            var list = fresh.PlayerItemList;
            list.Items.RemoveAll(each => !each.IsCompact && each.Id == id);
            if (withItem)
                list.Items.Add(item);
            list.Count = (ushort)list.Items.Count;
            File.WriteAllBytes(copy, D2S.Write(fresh));
        }

        var captured = vault.Capture(item, out string? refused) ?? throw new InvalidOperationException(refused);
        PendingMove StoreMove(long file, long vault) => new(MoveRecovery.Store, copy, "Tester", "Test item", id, item.Code,
            captured.Blob, "c", null, null, "", 0, file, file - 1, vault, vault + 1);

        // A: store, vault written, file not - in both.
        long f = InFile(), v = vault.CountHeld(id, captured.Blob);
        vault.JournalMoves([StoreMove(f, v)]);
        vault.AddCaptured([(captured, null)]);
        var said = Recover();
        Say(said.Count == 1 && said[0].Contains("in both") && vault.CountHeld(id, null) == v + 1 && InFile() == f
            && vault.Journal().Count == 0, "store cut short after the vault: in both, said, left, journal cleared");

        // B: store, file written without it, vault not - in neither: put back.
        f = InFile(); v = vault.CountHeld(id, null);
        vault.JournalMoves([StoreMove(f, v)]);
        WriteFile(withItem: false);
        said = Recover();
        Say(said.Count == 1 && said[0].Contains("put back") && vault.CountHeld(id, null) == v + 1 && InFile() == f - 1,
            $"store with the file written first: put back in the vault ({vault.CountHeld(id, null)} rows)");

        // C: withdraw, file written with it, row not removed - in both.
        var row = vault.Page("added", false, 0, 100).First(each => each.OriginId == id);
        f = InFile();
        PendingMove WithdrawMove(VaultRow taken, long file) => new(MoveRecovery.Withdraw, copy, "Tester", "Test item", id,
            item.Code, taken.Blob, "c", taken.Key, taken.FolderId, "", 0, file, file + 1, 1, 0);
        vault.JournalMoves([WithdrawMove(row, f)]);
        WriteFile(withItem: true);
        long rows = vault.CountHeld(id, null);
        said = Recover();
        Say(said.Count == 1 && said[0].Contains("in both") && vault.HasRow(row.Key) && vault.CountHeld(id, null) == rows,
            "withdraw cut short after the file: in both, said, the row left");

        // D: withdraw, row removed, file not written - in neither: put back.
        f = InFile(); rows = vault.CountHeld(id, null);
        vault.JournalMoves([WithdrawMove(row, f)]);
        vault.Remove([row.Key]);
        said = Recover();
        Say(said.Count == 1 && said[0].Contains("put back") && vault.CountHeld(id, null) == rows && InFile() == f,
            "withdraw with the row removed first: put back in the vault");

        // E: gold in, vault written, purse not - in both.
        long purse = Purse(), moves = vault.GoldWrites(), gold = vault.GoldBalance();
        vault.JournalMoves([new PendingMove(MoveRecovery.Gold, copy, "Tester", "gold", null, "", null, "c", null, null,
            MoveRecovery.CarriedPurse, 100, purse, purse - 100, moves, moves + 1)]);
        vault.RecordGold([new GoldMove(MoveRecovery.CarriedPurse, 100)]);
        said = Recover();
        Say(said.Count == 1 && said[0].Contains("in both") && vault.GoldBalance() == gold + 100, "gold in cut short: in both, said");

        // E2: gold in, neither written - nothing to say, nothing changed.
        moves = vault.GoldWrites();
        vault.JournalMoves([new PendingMove(MoveRecovery.Gold, copy, "Tester", "gold", null, "", null, "c", null, null,
            MoveRecovery.CarriedPurse, 100, purse, purse - 100, moves, moves + 1)]);
        said = Recover();
        Say(said.Count == 0 && vault.GoldBalance() == gold + 100, "gold in, nothing written: nothing said");

        // F: gold out, vault written first (the wrong order), purse not - in neither: paid back.
        moves = vault.GoldWrites();
        vault.JournalMoves([new PendingMove(MoveRecovery.Gold, copy, "Tester", "gold", null, "", null, "c", null, null,
            MoveRecovery.CarriedPurse, -60, purse, purse + 60, moves, moves + 1)]);
        vault.RecordGold([new GoldMove(MoveRecovery.CarriedPurse, -60)]);
        said = Recover();
        Say(said.Count == 1 && said[0].Contains("put back") && vault.GoldBalance() == gold + 100, $"gold out lost: paid back ({vault.GoldBalance()})");

        // G: a save that finished but was not cleared - nothing to say.
        f = InFile(); v = vault.CountHeld(id, null);
        vault.JournalMoves([StoreMove(f, v)]);
        vault.AddCaptured([(captured, null)]);
        WriteFile(withItem: false);
        said = Recover();
        Say(said.Count == 0 && vault.Journal().Count == 0, "finished, not cleared: nothing said, journal cleared");

        // H: cleared - nothing there.
        long batch = vault.JournalMoves([StoreMove(InFile(), vault.CountHeld(id, null))]);
        vault.ClearJournal(batch);
        Say(Recover().Count == 0, "cleared: nothing to check");

        // I: the save there but not readable - the game writing it, say: said, and kept for the next start, which
        // checks it once it reads (it was cleared unchecked).
        byte[] whole = File.ReadAllBytes(copy);
        vault.JournalMoves([StoreMove(InFile(), vault.CountHeld(id, null))]);
        File.WriteAllBytes(copy, whole[..64]);
        said = Recover();
        Say(said.Count == 1 && said[0].Contains("not checked") && vault.Journal().Count == 1, "unreadable: said, kept for the next start");
        File.WriteAllBytes(copy, whole);
        said = Recover();
        Say(said.Count == 0 && vault.Journal().Count == 0, "read at the next start: checked, journal cleared");

        Console.WriteLine(failures == 0 ? "all passed" : $"{failures} failed");
        return failures == 0 ? 0 : 1;
    }

    static int ReqSim(string gameRoot, string path, IReadOnlyList<string> wearing)
    {
        // Read-only: the character dressed otherwise in memory - "Head=Veil of Steel" puts that item
        // on and what was there into the inventory - and what it then comes to, weapon set 1 in hand.
        // For holding the gear count against the game's character screen, loadout by loadout.
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var own = save.PlayerItemList.Items;
        save.ActiveWeapon = 0;

        foreach (string pair in wearing)
        {
            var (where, name) = (pair[..pair.IndexOf('=')], pair[(pair.IndexOf('=') + 1)..]);
            var location = Enum.Parse<ItemLocation>(where, ignoreCase: true);
            var item = own.FirstOrDefault(each => ItemPresenter.Describe(each).Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException($"No item called {name}.");

            foreach (var worn in own.Where(each => each.Mode == ItemMode.Equipped && each.Location == location).ToList())
                ItemMover.Place(worn, 0, 0, 0);

            item.Mode = ItemMode.Equipped;
            item.Location = location;
        }

        int strength = AttributeScale.Display("strength", save.Attributes.Stats.GetValueOrDefault("strength"));
        int dexterity = AttributeScale.Display("dexterity", save.Attributes.Stats.GetValueOrDefault("dexterity"));

        var judged = Requirements.Judge(save, 0, strength, dexterity);
        foreach (var each in judged.Where(each => each.Item.Mode == ItemMode.Equipped))
            Console.WriteLine($"  {(each.Met ? "ok " : "RED")} {each.Item.Location,-11} {ItemPresenter.Describe(each.Item).Name}{(each.Short is { } why ? "  <- " + why : "")}");

        var met = judged.Where(each => each.Met).Select(each => each.Item).ToList();
        var total = Requirements.TotalOf(met, save.Level);
        var plain = met.Aggregate(default(Requirements.Bonus), (sum, item) => sum + Requirements.BonusOf(item, save.Level));
        Console.WriteLine($"strength {strength + total.Strength}, dexterity {dexterity + total.Dexterity}  (set bonuses {total.Strength - plain.Strength} / {total.Dexterity - plain.Dexterity})");
        return 0;
    }

    static int Guard(string gameRoot, string saveFolder)
    {
        // What the editor's patch guard checks at start, for the new-season checklist: the installed
        // build's fingerprint, and every character and shared stash in the folder read back in memory.
        // Nothing is written.
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        Console.WriteLine($"build {PatchGuard.Fingerprint(gameRoot)}  ({ModVersion.Of(gameRoot).Shown})");
        var results = PatchGuard.Verify(saveFolder);
        foreach (var each in results)
            Console.WriteLine($"  {(each.Good ? "ok  " : "FAIL")}  {each.File}{(each.Good ? "" : "  " + each.Problem)}");

        int bad = results.Count(each => !each.Good);
        Console.WriteLine($"{results.Count - bad} of {results.Count} read back exactly");
        return bad == 0 ? 0 : 1;
    }

    static int BeltRules()
    {
        // The placement rule on belts made up for the purpose (the rule's own example first), four rows.
        int failed = 0;
        void Case(string name, Dictionary<int, string?> taken, string kind, int? column, int? expected)
        {
            int? got = Belts.BoxFor(kind, taken, 4, column);
            bool ok = got == expected;
            failed += ok ? 0 : 1;
            Console.WriteLine($"{(ok ? "pass" : "FAIL")}  {name,-62} -> {got?.ToString() ?? "none"} (want {expected?.ToString() ?? "none"})");
        }

        Case("col 1 empty, col 2 one hp: hp goes to col 2", new() { [1] = "hpot" }, "hpot", null, 5);
        Case("col 2 full of hp, cols 3-4 mana: hp to empty col 1", new() { [1] = "hpot", [5] = "hpot", [9] = "hpot", [13] = "hpot", [2] = "mpot", [3] = "mpot" }, "hpot", null, 0);
        Case("col 2 full of hp, cols 3-4 full: hp to empty col 1", new() { [1] = "hpot", [5] = "hpot", [9] = "hpot", [13] = "hpot", [2] = "mpot", [6] = "mpot", [10] = "mpot", [14] = "mpot", [3] = "rpot", [7] = "rpot", [11] = "rpot", [15] = "rpot" }, "hpot", null, 0);
        Case("no hp column, no empty column: nowhere", new() { [0] = "mpot", [1] = "mpot", [2] = "rpot", [3] = "rpot" }, "hpot", null, null);
        Case("two hp columns: the first with room", new() { [0] = "hpot", [4] = "hpot", [8] = "hpot", [12] = "hpot", [2] = "hpot" }, "hpot", null, 6);
        Case("dropped on col 3 (mana there): its lowest free box", new() { [3] = "mpot" }, "hpot", 3, 7);
        Case("dropped on a full col 3: its kind's column first", new() { [3] = "mpot", [7] = "mpot", [11] = "mpot", [15] = "mpot", [1] = "hpot" }, "hpot", 3, 5);
        Case("dropped on a full col, nothing of its kind: an empty col", new() { [3] = "mpot", [7] = "mpot", [11] = "mpot", [15] = "mpot", [1] = "rpot" }, "hpot", 3, 0);
        Case("dropped on a full col, no kind, no empty: any room (fill)", new() { [0] = "mpot", [1] = "rpot", [2] = "rpot", [3] = "mpot", [7] = "mpot", [11] = "mpot", [15] = "mpot" }, "hpot", 3, 4);
        Case("belt full: nowhere", Enumerable.Range(0, 16).ToDictionary(box => box, _ => (string?)"rpot"), "rpot", 0, null);

        // Settle: a gap at the bottom of a column closes, order kept.
        var items = new[] { (X: 4, Code: "a"), (X: 12, Code: "b"), (X: 1, Code: "c") }
            .Select(each => new Item { X = (byte)each.X, Code = each.Code, Mode = ItemMode.Belt }).ToList();
        Belts.Settle(items);
        string settled = string.Join(" ", items.Select(item => $"{item.Code}:{item.X}"));
        bool settleOk = settled == "a:0 b:4 c:1";
        failed += settleOk ? 0 : 1;
        Console.WriteLine($"{(settleOk ? "pass" : "FAIL")}  settle a@4 b@12 c@1 -> {settled} (want a:0 b:4 c:1)");

        return failed == 0 ? 0 : 1;
    }

    static int BeltCheck(string gameRoot, string path)
    {
        // The belt as the file has it, every field; then a potion from the inventory put in by the
        // editor's rules, written, and read back - on a copy, never the file itself.
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var own = save.PlayerItemList;
        var worn = own.Items.FirstOrDefault(i => i.Mode == ItemMode.Equipped && i.Location == ItemLocation.Waist);
        int rows = Belts.RowsFor(worn?.Code);
        Console.WriteLine($"belt worn: {worn?.Code ?? "none"}, {rows} rows");

        foreach (var item in own.Items.Where(i => i.Mode == ItemMode.Belt).OrderBy(i => i.X))
            Console.WriteLine($"  [{item.Code,-4}] kind {Belts.KindOf(item),-5} box {item.X,2} (col {item.X % 4}, row {item.X / 4})  location {item.Location} page {item.Page} y {item.Y}");

        var taken = own.Items.Where(i => i.Mode == ItemMode.Belt).GroupBy(i => (int)i.X).ToDictionary(g => g.Key, g => Belts.KindOf(g.First()));
        Console.WriteLine();
        foreach (var probe in own.Items.Where(i => i.Mode == ItemMode.Stored).Where(Belts.Takes).Take(3))
        {
            Console.WriteLine($"stored [{probe.Code}] kind {Belts.KindOf(probe)}: game's choice box {Belts.BoxFor(Belts.KindOf(probe), taken, rows, null)?.ToString() ?? "none"}, "
                              + $"dropped on col 0: {Belts.BoxFor(Belts.KindOf(probe), taken, rows, 0)?.ToString() ?? "none"}");
        }

        foreach (var code in new[] { "hp5", "mp5", "rvl", "tsc", "isc", "yps", "cm1", "key" })
        {
            var fake = own.Items.FirstOrDefault(i => i.Code?.Trim() == code);
            Console.WriteLine($"  {code}: {(fake is null ? "not carried" : (Belts.Takes(fake) ? "takes" : "refused") + $" (kind {Belts.KindOf(fake)})")}");
        }

        if (own.Items.FirstOrDefault(i => i.Mode == ItemMode.Belt) is { } lifted
            && Belts.BoxFor(Belts.KindOf(lifted), taken.Where(pair => pair.Key != lifted.X).ToDictionary(), rows, 3) is { } box)
        {
            uint id = lifted.Id;
            int from = lifted.X;
            lifted.X = (byte)box;
            Belts.Settle(own.Items.Where(i => i.Mode == ItemMode.Belt));
            string copy = Path.Combine(Path.GetTempPath(), $"belt-{Guid.NewGuid():N}.d2s");
            File.WriteAllBytes(copy, SaveWriter.Prepared(save));
            var (back, _) = SaveVerifier.Load(copy);
            File.Delete(copy);
            var again = back.PlayerItemList.Items.First(i => i.Id == id);
            Console.WriteLine();
            Console.WriteLine($"moved [{again.Code}] box {from} -> dropped on col 3, landed box {again.X} mode {again.Mode} location {again.Location} page {again.Page} y {again.Y}");
            foreach (var item in back.PlayerItemList.Items.Where(i => i.Mode == ItemMode.Belt).OrderBy(i => i.X))
                Console.WriteLine($"  after: [{item.Code,-4}] box {item.X,2}");
        }

        return 0;
    }

    static int UnequipTest(string gameRoot, string path)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var own = save.PlayerItemList;
        var hired = save.MercenaryItemList?.ItemList;
        Console.WriteLine($"before: {own.Items.Count} own ({own.Items.Count(i => i.Mode == ItemMode.Equipped)} worn), {hired?.Items.Count ?? 0} mercenary");

        var gloves = own.Items.First(i => i.Mode == ItemMode.Equipped && i.Location == ItemLocation.Gloves);
        ItemMover.Place(gloves, 6, 7, 0);
        uint glovesId = gloves.Id;

        uint? mercId = null;
        if (hired?.Items.FirstOrDefault() is { } merc)
        {
            hired.Items.Remove(merc);
            hired.Count = (ushort)hired.Items.Count;
            ItemMover.Place(merc, 6, 7, 4);
            own.Items.Add(merc);
            own.Count = (ushort)own.Items.Count;
            mercId = merc.Id;
        }

        string copy = Path.Combine(Path.GetTempPath(), $"unequip-{Guid.NewGuid():N}.d2s");
        File.WriteAllBytes(copy, SaveWriter.Prepared(save));
        var (back, _) = SaveVerifier.Load(copy);
        File.Delete(copy);

        var g = back.PlayerItemList.Items.First(i => i.Id == glovesId);
        Console.WriteLine($"gloves: mode {g.Mode}, location {g.Location}, page {g.Page}, at {g.X},{g.Y}");
        if (mercId is { } id)
        {
            var m = back.PlayerItemList.Items.First(i => i.Id == id);
            Console.WriteLine($"mercenary item: mode {m.Mode}, location {m.Location}, page {m.Page}, at {m.X},{m.Y}; mercenary now holds {back.MercenaryItemList?.ItemList?.Items.Count}");
        }
        Console.WriteLine($"after: {back.PlayerItemList.Items.Count} own ({back.PlayerItemList.Items.Count(i => i.Mode == ItemMode.Equipped)} worn)");
        return 0;
    }

    /// <summary>A word built on a save's own item - by dump's number - and what the legitimacy check says of it.</summary>
    static int RunewordOn(string gameRoot, string path, int number, string wordName)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var item = save.PlayerItemList.Items.First(each => each.Code.Trim() == "6ls" && !each.IsRuneword);
        Console.WriteLine($"{item.Code.Trim()} {item.Quality}, sockets {item.TotalNumberOfSockets}, runeword {item.IsRuneword}; before: {Legitimacy.Check(item).Count} finding(s)");
        foreach (var stat in ItemStats.Every(item))
            Console.WriteLine($"  base {stat.Stat}[{stat.Param}] = {stat.Value}");

        var word = RunewordBuilder.WordsFor(item).First(each => Runewords.DisplayName(each).StartsWith(wordName, StringComparison.OrdinalIgnoreCase));
        var made = ItemForge.MakeRuneword(ItemBlob.Copy(item, 10), RunewordBuilder.Roll(word, new Random(1))).Item;
        for (int list = 0; list < made.StatLists.Count; list++)
            foreach (var stat in made.StatLists[list].Stats)
                Console.WriteLine($"  list {list}: {stat.Stat}[{stat.Param}] = {stat.Value}");
        foreach (var finding in Legitimacy.Check(ItemBlob.Copy(made, 10)))
            Console.WriteLine($"  ! {finding.Certainty}: {finding.Said}");
        return 0;
    }

    /// <summary>
    /// The runeword builder over every word: for each kind of base it goes in, a white base with the
    /// word's sockets (the forged runeword stripped back), the word built on it at random rolls - so
    /// many times - and the legitimacy check asked of each. Also that WordsFor offers the word.
    /// </summary>
    static int RunewordBuildTest(string gameRoot, int rolls)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var random = new Random(28);
        int built = 0, flagged = 0, notOffered = 0, failed = 0, swaps = 0, cleans = 0;

        foreach (var word in Runewords.All)
        {
            var kinds = word.Types
                .Where(type => ItemForge.BaseFor([type], word.ExcludedTypes) is not null)
                .GroupBy(type => SocketEffects.KindOf(ItemForge.BaseFor([type], word.ExcludedTypes)!));

            foreach (var kind in kinds)
            {
                if (ItemForge.BestBaseFor([.. kind], word.ExcludedTypes, word.Runes.Length) is not { } code)
                    continue;

                for (int n = 0; n < rolls; n++)
                {
                    var basis = ItemForge.Runeword(word, code, "103").Item;
                    basis.IsRuneword = false;
                    basis.RunewordId = 0;
                    basis.SocketedItems.Clear();
                    basis.NumberOfSocketedItems = 0;
                    while (basis.StatLists.Count > 1)
                        basis.StatLists.RemoveAt(1);
                    basis.StatLists[0].Stats.Clear();

                    if (!RunewordBuilder.WordsFor(basis).Contains(word))
                    {
                        notOffered++;
                        Console.WriteLine($"not offered: {Runewords.DisplayName(word)} in {code}");
                        break;
                    }

                    try
                    {
                        var made = ItemForge.MakeRuneword(basis, RunewordBuilder.Roll(word, random)).Item;
                        var copy = ItemBlob.Copy(made, 10);
                        built++;

                        if (Runewords.On(copy) != word)
                            Console.WriteLine($"reads back as {Runewords.On(copy)?.Name ?? "nothing"}: {Runewords.DisplayName(word)} in {code}");

                        if (Legitimacy.Check(copy) is { Count: > 0 } found)
                        {
                            flagged++;
                            Console.WriteLine($"flagged: {Runewords.DisplayName(word)} in {code}: {found[0].Said}");
                        }

                        // Swapped for another word the base holds, then cleaned back to white.
                        var others = RunewordBuilder.WordsFor(copy).Where(other => other != word).ToList();
                        if (others.Count > 0)
                        {
                            var other = others[random.Next(others.Count)];
                            var swapped = ItemBlob.Copy(ItemForge.MakeRuneword(copy, RunewordBuilder.Roll(other, random)).Item, 10);
                            swaps++;
                            if (Runewords.On(swapped) != other || Legitimacy.Check(swapped) is { Count: > 0 })
                            {
                                flagged++;
                                Console.WriteLine($"swap wrong: {Runewords.DisplayName(word)} to {Runewords.DisplayName(other)} in {code}");
                            }
                        }

                        RunewordBuilder.Clean(copy);
                        var white = ItemBlob.Copy(copy, 10);
                        cleans++;
                        if (white.IsRuneword || white.SocketedItems.Count > 0 || white.TotalNumberOfSockets != word.Runes.Length
                            || Legitimacy.Check(white) is { Count: > 0 })
                        {
                            flagged++;
                            Console.WriteLine($"clean wrong: {Runewords.DisplayName(word)} in {code}");
                        }
                    }
                    catch (Exception problem)
                    {
                        failed++;
                        Console.WriteLine($"failed: {Runewords.DisplayName(word)} in {code}: {problem.Message}");
                    }
                }
            }
        }

        Console.WriteLine($"{built} built, {swaps} swapped, {cleans} cleaned; {flagged} flagged, {failed} failed, {notOffered} not offered");
        return flagged + failed + notOffered == 0 ? 0 : 1;
    }

    /// <summary>
    /// The game's Ctrl+C texts read back into items (<see cref="GameClipboard"/>) and each held against the item
    /// it was copied from, in the save it is still in: what it is, its sockets and what is in them, and every
    /// stat in every list. A difference is printed; none is the round trip the import is for.
    /// <para>
    /// Three ends: <c>ok</c>, the same item; <c>~</c>, a legal item that differs only where the text cannot say -
    /// another affix row that writes the same lines, or a line the text left out and the import put back at its
    /// best; <c>!</c>, anything else, which is the import's to fix.
    /// </para>
    /// </summary>
    static int ClipCheck(string gameRoot, string savePath, IReadOnlyList<string> files)
    {
        using var installed = Season.Open(gameRoot);

        var (save, _) = SaveVerifier.Load(savePath);
        var real = save.PlayerItemList.Items;
        string era = real.Select(item => item.Version).FirstOrDefault(version => version is { Length: > 0 }) ?? "";

        static string Line(ItemStat stat) =>
            $"{stat.Id}={stat.Value}" + ((stat.SkillId ?? stat.Param) is { } subject ? $"[{subject}]" : "")
            + (stat.SkillLevel is { } level ? $"L{level}" : "") + (stat.MaxCharges is { } most ? $"/{most}" : "");

        static List<string> Lines(Item item) => [.. ItemStats.Every(item).Select(Line).OrderBy(each => each, StringComparer.Ordinal)];

        static string Affixes(Item item) =>
            string.Join("/", (item.MagicPrefixIds ?? []).Where(id => id > 0).OrderBy(id => id)) + " | "
            + string.Join("/", (item.MagicSuffixIds ?? []).Where(id => id > 0).OrderBy(id => id));

        int alike = 0, near = 0, apart = 0, lost = 0;
        var used = new HashSet<Item>(ReferenceEqualityComparer.Instance);

        foreach (string file in files)
        {
            foreach (var forged in GameClipboard.ReadAll(File.ReadAllText(file), era))
            {
                var made = forged.Item;
                string name = ItemPresenter.Describe(made).Name;

                // The save's own: the same base and quality, the most lines alike, each taken once.
                var mine = Lines(made);
                var match = real
                    .Where(item => !used.Contains(item) && item.Code?.Trim() == made.Code?.Trim() && item.Quality == made.Quality)
                    .OrderByDescending(item => Lines(item).Intersect(mine).Count())
                    .ThenByDescending(item => ItemPresenter.Describe(item).Name == name)
                    .FirstOrDefault();

                if (match is null)
                {
                    Console.WriteLine($"?  {name} [{made.Code?.Trim()}]: nothing like it in the save");
                    lost++;
                    continue;
                }

                used.Add(match);
                var said = new List<string>();
                bool beyondTheText = false;
                void Differ(string what, object? theirs, object? ours)
                {
                    if (Equals(theirs, ours))
                        return;

                    said.Add($"{what}: save {theirs}, made {ours}");
                    beyondTheText |= what != "affixes";
                }

                Differ("name", ItemPresenter.Describe(match).Name, name);
                Differ("item level", match.ItemLevel, made.ItemLevel);
                Differ("ethereal", match.IsEthereal, made.IsEthereal);
                Differ("sockets", match.TotalNumberOfSockets, made.TotalNumberOfSockets);
                Differ("quantity", match.Quantity, made.Quantity);
                Differ("defence stored", match.Armor, made.Armor);
                Differ("durability", $"{match.Durability}/{match.MaxDurability}", $"{made.Durability}/{made.MaxDurability}");
                Differ("row", match.FileIndex, made.FileIndex);
                Differ("runeword", match.RunewordId, made.RunewordId);
                Differ("automod", match.IsAutoAffix ? match.AutoAffixId : 0, made.IsAutoAffix ? made.AutoAffixId : 0);
                Differ("affixes", Affixes(match), Affixes(made));
                Differ("lists", match.StatLists.Count, made.StatLists.Count);
                Differ("in sockets", string.Join(",", match.SocketedItems.Select(each => each.Code?.Trim())),
                       string.Join(",", made.SocketedItems.Select(each => each.Code?.Trim())));

                // A stat both have, at another number, where the import said the text had no line for one.
                bool putBack = forged.Notes.Any(note => note.Contains("not in the game's text", StringComparison.Ordinal));
                static string IdOf(string line) => line[..line.IndexOf('=')];

                var theirs = Lines(match);
                foreach (string only in theirs.Except(mine))
                {
                    said.Add($"stat only in the save: {only}");
                    beyondTheText |= !putBack || !mine.Any(each => IdOf(each) == IdOf(only));
                }

                foreach (string only in mine.Except(theirs))
                {
                    said.Add($"stat only in the made: {only}");
                    beyondTheText |= !putBack || !theirs.Any(each => IdOf(each) == IdOf(only));
                }

                for (int n = 0; n < Math.Min(match.SocketedItems.Count, made.SocketedItems.Count); n++)
                {
                    var (a, b) = (Lines(match.SocketedItems[n]), Lines(made.SocketedItems[n]));
                    if (!a.SequenceEqual(b))
                    {
                        said.Add($"socket {n + 1}: save {string.Join(" ", a)}; made {string.Join(" ", b)}");
                        beyondTheText = true;
                    }
                }

                foreach (var finding in Legitimacy.Check(made))
                {
                    said.Add($"legitimacy: {finding.Subject} - {finding.Said}");
                    beyondTheText = true;
                }

                if (said.Count == 0)
                {
                    Console.WriteLine($"ok {name}" + (forged.Notes.Count > 0 ? $"   ({string.Join("; ", forged.Notes)})" : ""));
                    alike++;
                    continue;
                }

                if (beyondTheText)
                    apart++;
                else
                    near++;

                Console.WriteLine($"{(beyondTheText ? "! " : "~ ")} {name} [{made.Code?.Trim()}]");
                foreach (string each in said)
                    Console.WriteLine($"     {each}");
                foreach (string note in forged.Notes)
                    Console.WriteLine($"     note: {note}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{alike} alike, {near} legal and apart only where the text cannot say, {apart} apart, {lost} with nothing to hold against");
        return apart == 0 ? 0 : 1;
    }

    /// <summary>
    /// One runeword forged in its best base, every property at its top, and what the legitimacy check makes of
    /// it: its base, its runes, the word the runes are read back as, and each finding.
    /// </summary>
    static int ForgeWord(string gameRoot, string name)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        foreach (var word in Runewords.All.Where(each => Runewords.DisplayName(each).StartsWith(name, StringComparison.OrdinalIgnoreCase)))
        {
            string? code = ItemForge.BestBaseFor(word.Types, word.ExcludedTypes, word.Runes.Length);
            Console.WriteLine($"{Runewords.DisplayName(word)}: types {string.Join(",", word.Types)} -> base {code}");
            if (code is null)
                continue;

            var forged = ItemForge.Runeword(word, code, "103");
            var item = forged.Item;
            Console.WriteLine($"  sockets {item.TotalNumberOfSockets}, socketed {string.Join(",", item.SocketedItems.Select(gem => gem.Code?.Trim()))}, "
                              + $"runeword {item.IsRuneword} id {item.RunewordId}, read back as {Runewords.On(item)?.Name ?? "nothing"}");
            foreach (var stat in ItemStats.Every(item))
                Console.WriteLine($"    {stat.Stat}[{stat.Param}] = {stat.Value}");
            foreach (var note in forged.Notes)
                Console.WriteLine($"  note: {note}");
            foreach (var finding in Legitimacy.Check(item))
                Console.WriteLine($"  ! {finding.Certainty}: {finding.Said}");
        }

        return 0;
    }

    /// <summary>
    /// Captures every item as a standalone blob the way the vault will, and reports what would
    /// be refused. Read-only - nothing is written anywhere.
    /// <para>
    /// This is the proof that <see cref="ItemBlob.TryCapture"/> holds over real items rather than
    /// over the two the author thought of, and the dry run to point at a save before importing it.
    /// </para>
    /// </summary>
    static int Blob(string gameRoot, string? corpus, string? single)
    {
        if (corpus is null && single is null)
        {
            Console.Error.WriteLine("usage: blob <save|stash> | --corpus <dir>");
            return 1;
        }

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var files = corpus is not null ? SavesUnder(corpus) : [single!];
        int taken = 0, refused = 0, unread = 0;
        long bytes = 0;
        var seen = new Dictionary<string, int>();
        int distinctIds = 0, shareAnId = 0, compact = 0;
        var reasons = new List<string>();

        foreach (string file in files)
        {
            List<(string Where, Item Item)> held;

            try
            {
                held = Contents(file);
            }
            catch (Exception problem)
            {
                unread++;
                Console.WriteLine($"unread   {Path.GetFileName(file)}: {Line(problem)}");
                continue;
            }

            // Per file, deliberately. Every character has its own id space, so pooling ids
            // across a corpus manufactures collisions that mean nothing.
            var idsHere = new Dictionary<uint, int>();

            foreach (var (_, item) in held)
            {
                // On a copy, so the corpus is never touched - the same round trip the vault
                // would do, then normalised the way a template is.
                Item copy;

                try
                {
                    copy = ItemBlob.Copy(item, D2SLib.Globals.stat_id_bits);
                }
                catch (Exception problem)
                {
                    refused++;
                    if (reasons.Count < 8) reasons.Add($"{item.Code?.Trim() ?? "?"}: {problem.Message}");
                    continue;
                }

                ItemBlob.Normalise(copy);

                if (!ItemBlob.TryCapture(copy, D2SLib.Globals.stat_id_bits,
                                         out var blob, out var hash, out string? why))
                {
                    refused++;
                    if (reasons.Count < 8) reasons.Add($"{item.Code?.Trim() ?? "?"}: {why}");
                    continue;
                }

                taken++;
                bytes += blob.Length;

                // Identity, which is a different question from the hash: the hash says "the same
                // kind of item", the id says "the same item". A compact item has no id at all -
                // Id is read in ReadComplete - so it can never be tracked this way.
                if (item.IsCompact) compact++;
                else
                {
                    uint id = item.Id;
                    idsHere[id] = idsHere.TryGetValue(id, out int had) ? had + 1 : 1;
                }
                string key = Convert.ToHexString(hash);
                seen[key] = seen.TryGetValue(key, out int already) ? already + 1 : 1;
            }

            int clashHere = idsHere.Values.Where(n => n > 1).Sum();
            distinctIds += idsHere.Count;
            shareAnId += clashHere;

            if (clashHere > 0)
                Console.WriteLine($"  {Path.GetFileName(file)}: {clashHere} item(s) share an id");
        }

        Console.WriteLine();
        Console.WriteLine($"{taken} captured, {refused} refused, {unread} file(s) unread");

        if (taken > 0)
        {
            Console.WriteLine($"  {bytes} bytes, {bytes / taken} per item");
            Console.WriteLine($"  {seen.Count} distinct templates, "
                              + $"{taken - seen.Count} would dedup ({100.0 * (taken - seen.Count) / taken:F1}%)");

            Console.WriteLine($"  {compact} compact (no id at all), {distinctIds} distinct ids, "
                              + $"{shareAnId} sharing an id within their own file");
        }

        foreach (string reason in reasons)
            Console.WriteLine($"  refused: {reason}");

        return refused > 0 ? 1 : 0;
    }

    /// <summary>
    /// Every cached trade listing of a rare or crafted item rebuilt for a save, and whether its affixes
    /// came out of the listing's ranges - the affix search on the site's own numbers.
    /// </summary>
    static int TradeAffixes(string gameRoot)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        int found = 0, missed = 0;
        foreach (string file in Directory.EnumerateFiles(TradeListings.CacheFolder, "*.json"))
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if (!document.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                continue;

            var listed = data[0].GetProperty("item");
            string quality = listed.GetProperty("quality").GetProperty("name").GetString() ?? "";
            if (quality is not ("Rare" or "Crafted"))
                continue;

            var made = TradeItems.Build(listed);
            var item = made.Item;
            var names = item.MagicPrefixIds.Where(id => id > 0).Select(id => MagicAffixes.Find(id, true)?.Name ?? $"#{id}")
                .Concat(item.MagicSuffixIds.Where(id => id > 0).Select(id => MagicAffixes.Find(id, false)?.Name ?? $"#{id}"))
                .ToList();

            bool any = names.Count > 0;
            if (any) found++; else missed++;

            var findings = Legitimacy.Check(item);
            Console.WriteLine($"{(any ? "found " : "none  ")} {quality,-8} {listed.GetProperty("name").GetString(),-28} "
                              + $"{string.Join(" + ", names),-70} legitimacy: {findings.Count}");
            foreach (var finding in findings)
                Console.WriteLine($"        {finding}");
            foreach (var note in made.Notes)
                Console.WriteLine($"        note: {note}");
            if (arguments.Contains("--criteria"))
            {
                if (arguments.Contains("--captured"))
                    ItemBlob.TryCapture(item, D2SLib.Globals.stat_id_bits, out _, out _, out _);
                var prefill = TradeSearch.PrefillFor(item);
                Console.WriteLine($"        prefill name={prefill.Name} rarity={prefill.Rarity} type={prefill.TypeFrom}-{prefill.TypeTo} base={prefill.Base} "
                                  + $"eth={prefill.Ethereal} corrupt={prefill.Corrupted} desecrated={prefill.Desecrated} sockets={prefill.SocketsMin}");
                foreach (var row in prefill.Properties)
                    Console.WriteLine($"        row {row.Option} skill={row.Skill} roll={row.Roll} {row.Min}-{row.Max}");
                if (arguments.Contains("--search"))
                {
                    // The prefill as the vault's form would ask it, sent: does the item find its own listing?
                    var query = new PD2SPCompanion.Core.Collection.VaultQuery(
                        Name: prefill.Name, Rarity: prefill.Rarity, TypeFrom: prefill.TypeFrom, TypeTo: prefill.TypeTo,
                        Base: prefill.Base, Ethereal: prefill.Ethereal, Corrupted: prefill.Corrupted, Desecrated: prefill.Desecrated,
                        SocketsMin: prefill.SocketsMin,
                        Properties: [.. prefill.Properties.Select(row => new PD2SPCompanion.Core.Collection.PropertyWant(
                            row.Skill is { } skill ? PropertyIndex.KeyOf(row.Option, skill) : row.Option, false, row.Min, row.Max))]);
                    var listings = TradeSearch.Search(query, hardcore: false).GetAwaiter().GetResult();
                    Console.WriteLine($"        search: {listings.Count} found; own listing among them: {listings.Any(each => each.Id == data[0].GetProperty("_id").GetString())}");
                    foreach (var each in listings)
                        Console.WriteLine($"          {each.Id} {each.Item.GetProperty("name").GetString()}");
                }
                foreach (var stat in item.StatLists.SelectMany(list => list.Stats).Concat(item.SocketedItems.SelectMany(s => s.StatLists.SelectMany(l => l.Stats))))
                    Console.WriteLine($"        stat {stat.Stat} v{stat.Value} p{stat.Param?.ToString() ?? "null"} s{stat.SkillId?.ToString() ?? "null"} l{stat.SkillLevel?.ToString() ?? "null"}");
            }
            if (quality == "Crafted" && arguments.Contains("--why"))
            {
                Console.WriteLine($"        recipe recognised: {AffixRanges.CraftOn(item)?.Family ?? "none"}");
                foreach (var family in AffixRanges.CraftFamilies(item))
                    Console.WriteLine($"        could be {family.Family}: " + string.Join(", ", family.Grants.Select(grant => $"{grant.Code}#{grant.Param} {grant.Min}-{grant.Max}")));
                foreach (var stat in item.StatLists.FirstOrDefault()?.Stats ?? [])
                    Console.WriteLine($"        holds {AffixSearch.KeyOf(stat)} = {stat.Value}");
                foreach (string key in new[] { "item_maxdamage_percent", "tohit", "secondary_maxdamage", "item_addskill_tab#32" })
                {
                    var rows = MagicAffixes.ReachableBy(item).Where(row => AffixSearch.Writes(row).ContainsKey(key))
                        .Select(row => $"{(row.IsPrefix ? "P" : "S")}:{row.Name}[{row.Group}] " + string.Join(";", AffixSearch.Writes(row).Select(each => $"{each.Key}={each.Value.Min}-{each.Value.Max}")));
                    Console.WriteLine($"        {key}: " + string.Join(" | ", rows));
                }
            }
        }

        Console.WriteLine($"{found} with affixes, {missed} without");
        return 0;
    }

    /// <summary>
    /// The affix search, tried on real items whose affixes are known: each item's lines are shown
    /// the way the trade site shows them - the true affixes' summed range on every line they roll,
    /// the value alone where they fix it - and the search's answer set against the true one. Exact
    /// is the same rows; equivalent is rows that write the same things, which no reading of the
    /// item can tell apart.
    /// </summary>
    static int AffixSearchTest(string gameRoot, string? tables, string? corpus, string? single)
    {
        if (corpus is null && single is null)
            throw new ArgumentException("affixtest needs a save or stash file, or --corpus <dir>");

        using var installed = Season.Open(gameRoot, tables);
        var game = installed.Game;

        var tally = new Dictionary<string, int>();
        var examples = new Dictionary<string, string>();

        void Count(string what, string example)
        {
            tally[what] = tally.GetValueOrDefault(what) + 1;
            examples.TryAdd(what, example);
        }

        foreach (var file in corpus is not null ? SavesUnder(corpus) : [single!])
        {
            List<(string Where, Item Item)> held;
            try
            {
                held = Contents(file);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var (_, item) in held)
            {
                if (item.Quality is not (ItemQuality.Rare or ItemQuality.Craft or ItemQuality.Magic) || item.IsRuneword)
                    continue;

                var truth = item.MagicPrefixIds.Where(id => id > 0).Select(id => MagicAffixes.Find(id, true))
                    .Concat(item.MagicSuffixIds.Where(id => id > 0).Select(id => MagicAffixes.Find(id, false)))
                    .OfType<MagicAffixes.AffixOption>().ToList();
                if (truth.Count == 0)
                    continue;

                var ranges = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in truth)
                    foreach (var (key, range) in AffixSearch.Writes(row))
                        ranges[key] = ranges.TryGetValue(key, out var had) ? (had.Min + range.Min, had.Max + range.Max) : range;
                var automagic = AffixRanges.AutomagicGrants(item).Select(each => each.Level is { } at && at > 0 ? $"{each.Key}@{at}" : each.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var lines = new List<AffixSearch.Line>();
                foreach (var stat in item.StatLists.FirstOrDefault()?.Stats ?? [])
                {
                    string key = AffixSearch.KeyOf(stat);
                    if (ranges.TryGetValue(key, out var range))
                        lines.Add(new AffixSearch.Line(key, range.Min, range.Max, range.Min != range.Max, stat.Value));
                    else if (automagic.Contains(key))
                        lines.Add(new AffixSearch.Line(key, 0, 0, false, stat.Value));
                }

                string name = $"{item.Quality} {item.Code.Trim()} ilvl {item.ItemLevel}: {string.Join(" + ", truth.Select(row => row.Name))}";
                var found = AffixSearch.Find(item, lines);
                if (found is null)
                {
                    Count($"{item.Quality}: none found", name);
                    if (arguments.Contains("--why"))
                    {
                        var reachable = MagicAffixes.ReachableBy(item).Select(row => (row.IsPrefix, row.Id)).ToHashSet();
                        var keys = lines.Select(line => line.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        Console.WriteLine($"  none: {name}");
                        foreach (var row in truth)
                            Console.WriteLine($"    {row.Name,-14} reachable={reachable.Contains((row.IsPrefix, row.Id))} lvl {row.Level} "
                                + string.Join(" ", AffixSearch.Writes(row).Select(each => $"{each.Key}={each.Value.Min}-{each.Value.Max}{(keys.Contains(each.Key) ? "" : "(absent)")}")));
                        foreach (var line in lines)
                            Console.WriteLine($"    line {line.Key} {(line.Ranged ? $"{line.Min}-{line.Max}" : "fixed")} value {line.Value}");
                    }
                    continue;
                }

                var got = found.Prefixes.Concat(found.Suffixes).ToList();
                static string Shape(MagicAffixes.AffixOption row) =>
                    (row.IsPrefix ? "P:" : "S:") + string.Join(";", AffixSearch.Writes(row).OrderBy(each => each.Key)
                        .Select(each => $"{each.Key}={each.Value.Min}-{each.Value.Max}"));

                bool exact = got.Select(row => (row.IsPrefix, row.Id)).Order().SequenceEqual(truth.Select(row => (row.IsPrefix, row.Id)).Order());
                bool same = got.Select(Shape).Order().SequenceEqual(truth.Select(Shape).Order());

                // The same properties, a prefix standing where a suffix was - "Large" for "of Health"
                // - which nothing on the item or its listing can tell apart.
                static string Effect(MagicAffixes.AffixOption row) => Shape(row)[2..];
                bool sameEffect = got.Select(Effect).Order().SequenceEqual(truth.Select(Effect).Order());

                Count($"{item.Quality}: {(exact ? "exact" : same ? "equivalent" : sameEffect ? "same effect" : "different")}",
                      $"{name}  ->  {string.Join(" + ", got.Select(row => row.Name))}");
            }
        }

        foreach (var (what, count) in tally.OrderBy(each => each.Key))
            Console.WriteLine($"{count,6}  {what,-26} e.g. {examples[what]}");
        return 0;
    }

    /// <summary>
    /// The legitimacy check over a save, a stash or a whole library.
    /// <para>
    /// Grouping the findings by shape is the point of the corpus form. A list of six thousand
    /// remarks says nothing; the same list grouped by what was said, with a count and one
    /// example each, is what turned a wall of "not an affix this item can carry" into two bugs
    /// in our own filter.
    /// </para>
    /// </summary>
    static int Legit(string gameRoot, string? tables, string? corpus, string? single)
    {
        if (corpus is null && single is null)
            throw new ArgumentException("legit needs a save or stash file, or --corpus <dir>");

        using var installed = Season.Open(gameRoot, tables);
        var game = installed.Game;

        var files = corpus is not null ? SavesUnder(corpus) : [single!];
        if (files.Count == 0)
        {
            Console.WriteLine($"no saves or stashes under {corpus}");
            return 1;
        }

        string? onlyPage = Option("--page");
        bool everyItem = corpus is null || arguments.Contains("--items");

        int seen = 0, silent = 0, refused = 0;
        var counted = new Dictionary<Certainty, int>();
        var shapes = new Dictionary<(Certainty Certainty, string Shape), (int Count, string Example)>();

        foreach (var file in files)
        {
            List<(string Where, Item Item)> held;
            try
            {
                held = Contents(file);
            }
            catch (Exception ex)
            {
                refused++;
                Console.WriteLine($"refused  {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            foreach (var (where, item) in held)
            {
                if (onlyPage is not null && !where.Contains(onlyPage, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.IsCompact)
                    continue;

                seen++;
                var findings = Legitimacy.Check(item);
                if (findings.Count == 0)
                {
                    silent++;
                    continue;
                }

                string told = ItemPresenter.Describe(item).Name;

                if (everyItem)
                    Console.WriteLine($"{told} ({item.Code?.Trim()}, {item.Quality}, v{item.Version})"
                                      + (where.Length > 0 ? $" on {where}" : ""));

                foreach (var finding in findings)
                {
                    counted[finding.Certainty] = counted.GetValueOrDefault(finding.Certainty) + 1;

                    if (everyItem)
                    {
                        Console.WriteLine($"    {finding.Certainty,-12} {finding.Said}");
                        continue;
                    }

                    // Numbers and names out, so the same defect lands in one bucket.
                    string shape = string.Join(" ", finding.Said.Split(' ')
                        .Select(word => word.Any(char.IsDigit) ? "N" : word));

                    // Keyed by certainty as well as by shape. Without it the summary printed a
                    // "certainty" column it never filled, so an Impossible - the one verdict that
                    // says the game could not have made this - could not be told from a remark.
                    shapes.TryGetValue((finding.Certainty, shape), out var tally);
                    shapes[(finding.Certainty, shape)] = (tally.Count + 1,
                        tally.Example ?? $"{told} ({item.Code?.Trim()}, {item.Quality}, v{item.Version})"
                                         + $"{(where.Length > 0 ? $" on {where}" : "")}: {finding.Said}");
                }
            }
        }

        if (!everyItem)
        {
            // Forty is enough to read and not enough to diff: comparing two corpus runs after a
            // change, every shape that moved sat in the tail. --top says how many.
            int top = int.TryParse(Option("--top"), out int asked) && asked > 0 ? asked : 40;

            Console.WriteLine($"{"count",7}  {"certainty",-12} what was said");

            // Impossible first whatever its count: it is the only verdict that refuses an item,
            // and it is the one to read when a run says more than it used to.
            foreach (var (key, tally) in shapes
                         .OrderBy(each => each.Key.Certainty)
                         .ThenByDescending(each => each.Value.Count)
                         .Take(top))
            {
                Console.WriteLine($"{tally.Count,7}  {key.Certainty,-12} {key.Shape}");
                Console.WriteLine($"         e.g. {tally.Example}");
            }
        }

        Console.WriteLine($"\n{seen} items read from {files.Count} file(s), {refused} refused");
        Console.WriteLine($"  {silent} silent ({(seen > 0 ? 100.0 * silent / seen : 0):F1}%)");
        foreach (var certainty in (Certainty[])[Certainty.Impossible, Certainty.Unrolled, Certainty.Unaccounted])
            Console.WriteLine($"  {counted.GetValueOrDefault(certainty),6} {certainty.ToString().ToLowerInvariant()} findings");

        return counted.GetValueOrDefault(Certainty.Impossible) > 0 ? 1 : 0;
    }

    /// <summary>
    /// A character or shared stash rewritten in last season's encoding - the installed tables'
    /// previous-season columns - which is what this season's files will be on the next season's first
    /// launch. For testing that the editor reads such files
    /// and guards them; never for a file a player uses. A value last season's field cannot hold is cut
    /// down by the writer, so the copy is not always the same items.
    /// </summary>
    static int LastSeason(string gameRoot, string? path, string? into)
    {
        if (path is null || into is null)
            throw new ArgumentException("lastseason needs <save|stash> --out <file>");
        if (File.Exists(into))
            throw new ArgumentException($"{into} exists; nothing is written over it");

        using var installed = Season.Open(gameRoot);
        if (!SeasonReading.HasPreviousSeason)
            throw new InvalidOperationException("the installed tables carry no previous season's encoding");

        // With the seasons' stamps known each item is written by its own stamp, whatever the file's switch says, and
        // the copy would come out in this season's encoding after all. A file of a first launch knows no stamp.
        SeasonReading.SetStamps(null, null);

        byte[] bytes;
        if (path.EndsWith(".d2s", StringComparison.OrdinalIgnoreCase))
        {
            var (save, _) = SaveVerifier.Load(path);
            save.Layout = new D2SLib.ParserLayout(true, 10, true);
            bytes = D2S.Write(save);
        }
        else
        {
            bytes = SharedStash.Read(path).Write(lastSeason: true);
        }

        SaveWriter.WriteSafely(into, bytes);
        Console.WriteLine($"{Path.GetFileName(path)} in {D2SLib.Model.TXT.StatEncoding.PreviousSeason}'s encoding: {into}");
        return 0;
    }

    /// <summary>
    /// A test pad (<c>TestCorpus\ui-pad</c>: a <c>saves</c> and a <c>vtsmart</c> folder) of the season before, brought
    /// into the installed one for the Shell tests: its vaults migrated as Settings migrates them, its characters as
    /// Settings migrates them, its shared stash into this season's layout - the header of <paramref name="layoutFrom"/>,
    /// a stash this season's game wrote, holding the pad's own pages, counts and gold - and then every item made this
    /// season's, as an edit makes a legacy one (<see cref="ItemSeason.MakeCurrent"/>): a pad whose items are judged and
    /// show their ranges, as a player's of this season are. In place: give it a copy. Never for a player's files.
    /// </summary>
    static int PadSeason(string gameRoot, string? pad, string? layoutFrom)
    {
        if (pad is null || layoutFrom is null)
            throw new ArgumentException("padseason needs <pad folder> --layout-from <this season's pd2_shared.stash>");

        string saves = Path.Combine(pad, "saves"), stashPath = Path.Combine(saves, "pd2_shared.stash");
        Environment.SetEnvironmentVariable("PD2SPC_VAULTS", Path.Combine(pad, "vtsmart"));

        using var installed = Season.Open(gameRoot);
        string from = D2SLib.Model.TXT.StatEncoding.PreviousSeason, to = SeasonSnapshot.NameFor(installed);

        // Made this season's, and put to this season's rows where they moved (SeasonRows.Follow, the vault migration's
        // way for an item the old season found nothing wrong with): the pad was silent in its own season.
        int followed = 0;
        int Current(IEnumerable<Item> items) =>
            items.WithSockets().Count(item =>
            {
                if (!ItemSeason.MakeCurrent(item))
                    return false;
                if (!item.IsCompact && Legitimacy.Check(item).Count > 0 && SeasonRows.Follow(item, null).Count > 0)
                    followed++;
                return true;
            });

        // A rune, gem or skull lying loose as this season keeps one, the plain item with a quantity (S14: "no longer a
        // stacked and unstacked version") - an old stack ("Nef Rune (old)", r04s) the plain rune holding its count, a
        // compact one what ItemForge.Loose gives back. In a socket it stays compact, as the game writes it.
        string stamp = SeasonReading.CurrentStamp ?? throw new InvalidOperationException($"{to}'s item version is unknown");
        int loosened = 0;
        void Loosen(List<Item>? items)
        {
            for (int at = 0; at < (items?.Count ?? 0); at++)
            {
                var item = items![at];
                string code = item.Code?.Trim() ?? "";
                var made = Materials.Takes(item) is { } taken && Materials.Plain(taken.Slot) != code
                    ? ItemForge.Material(Materials.Plain(taken.Slot), taken.Quantity, stamp)
                    : item.IsCompact && Stacks.IsStackable(code) ? ItemForge.Material(code, 1, stamp) : null;
                if (made is null)
                    continue;

                (made.Id, made.Mode, made.Location, made.X, made.Y, made.Page) = (item.Id, item.Mode, item.Location, item.X, item.Y, item.Page);
                items[at] = made;
                loosened++;
            }
        }

        // ---- the shared stash, where the pad has one: this season's header, the pad's counts, gold and pages ----------
        if (File.Exists(stashPath))
        {
            var old = SharedStash.Read(stashPath);
            var stash = SharedStash.Read(layoutFrom);
            foreach (int slot in Enumerable.Range(1, Materials.Every).Where(stash.Holds))
                stash.SetMaterialCount(slot, old.Holds(slot) ? old.MaterialCount(slot) : 0);
            stash.SetGold((int)old.Gold);
            var pages = stash.Items.Items ?? throw new InvalidDataException($"{layoutFrom} has no item list");
            pages.Clear();
            pages.AddRange(old.Items.Items ?? []);
            Loosen(pages);
            int stashed = Current(pages);
            byte[] stashBytes = stash.Write();
            SharedStash.Read(stashBytes, "pd2_shared.stash");
            SaveWriter.WriteSafely(stashPath, stashBytes);
            Console.WriteLine($"shared stash: layout {old.Shape.Version} -> {stash.Shape.Version}, {pages.Count} items, {stashed} made {to}'s");
        }

        // ---- the characters: migrated, then every item made this season's -------------------------------------------
        var files = Directory.GetFiles(saves, "*.d2s");
        foreach (var done in CharacterMigration.RunAll(installed, [.. files.Select(file => new CharacterMigration.Request(file, stashPath, false))]))
        {
            if (done.Report is not { } report)
                throw new InvalidOperationException($"{Path.GetFileName(done.Request.SavePath)}: {done.Refused}");

            // The migration's own backup is of the pad as it was, which the pad keeps no use for.
            foreach (string backup in report.Backups)
                File.Delete(backup);

            var (save, _) = SaveVerifier.Load(done.Request.SavePath);
            Loosen(save.PlayerItemList?.Items);
            Loosen(save.MercenaryItemList?.ItemList?.Items);
            int made = Current([.. SaveItems.Walk(save, ItemScope.Own | ItemScope.Mercenary | ItemScope.Corpses | ItemScope.Golem)]);
            save.Layout = new D2SLib.ParserLayout(true, 10, false);
            SaveWriter.WriteSafely(done.Request.SavePath, SaveWriter.Prepared(save));
            Console.WriteLine($"{report.Name}: gold moved {report.GoldMoved:N0}, {made} items made {to}'s");
        }

        // ---- the vaults: migrated under their own names, then under this season's main name -------------------------
        foreach (var (hardcore, name) in SeasonMigration.PendingVaults(from, to))
        {
            var report = SeasonMigration.Run(installed, new SeasonMigration.Request(from, hardcore, false, KeepReport: false, Vault: name));
            string named = name == VaultLibrary.MainName(from) ? VaultLibrary.MainName(to) : name;
            if (named != name)
                VaultLibrary.RenameVault(to, hardcore, name, named);

            int made = 0, refused = 0;
            using (var vault = VaultLibrary.OpenVault(to, hardcore, named, 10))
            {
                foreach (var row in vault.RowsOf(vault.AllKeys()))
                {
                    var item = ItemBlob.Restore(row.Blob, 10);
                    if (Current([item]) == 0)
                        continue;
                    if (vault.Replace(row.Key, item, out _, keepId: row.OriginId) is null)
                        refused++;
                    else
                        made++;
                }
            }

            Console.WriteLine($"{(hardcore ? "hardcore" : "softcore")} vault {named}: {report.Items.Count} items migrated, "
                              + $"{report.Count(SeasonMigration.Outcome.Failed)} not; {made} made {to}'s, {refused} refused");
        }

        Console.WriteLine($"{followed} items put to {to}'s rows, {loosened} runes, gems and skulls made loose as {to} keeps them");
        return 0;
    }

    static int Verify(string gameRoot, string? corpus, string? single)
    {
        if (corpus is null && single is null)
            throw new ArgumentException("verify needs a save file, or --corpus <dir>");

        using var installed = Season.Open(gameRoot, Tables());
        var game = installed.Game;

        if (arguments.Contains("--trace"))
        {
            D2SLib.Globals.writeConsole_ItemsRead = true;
            D2SLib.Globals.writeConsole_ItemsReadComplete = true;
        }

        var files = corpus is not null ? SavesUnder(corpus) : [single!];

        if (files.Count == 0)
        {
            Console.WriteLine($"no saves or stashes under {corpus}");
            return 1;
        }

        // A stash is not a character: it has no header to round-trip and its own reader picks
        // the stat-id width. Verified on its own terms and reported in the same column.
        var stashes = files.Where(IsStash).ToList();
        files = files.Where(file => !IsStash(file)).ToList();

        foreach (var file in stashes)
        {
            string name = corpus is not null ? Path.GetRelativePath(corpus, file) : Path.GetFileName(file);
            try
            {
                // PD2's own stash first: it is not a PlugY one, and handing it to the PlugY reader
                // is what reported a good pd2_shared.stash as "most likely written by a different
                // PD2 season" - the same misrouting the other commands had and lost, this one kept.
                // SharedStash.Read refuses anything it cannot write back byte for byte, so reading
                // it is the round-trip.
                if (IsSharedStash(file))
                {
                    var shared = SharedStash.Read(file);
                    Console.WriteLine($"{"ok",-15} {$"pd2/{shared.StatIdBits}b",-7} {shared.Items.Items.Count,4} items  {name}");
                    continue;
                }

                var (stash, bits) = SaveVerifier.LoadStash(file);
                int held = (stash.PageList ?? []).Sum(page => page.PageItems?.Items?.Count ?? 0);
                Console.WriteLine($"{"ok",-15} {$"stash/{bits}b",-7} {held,4} items  {name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{"REFUSED",-15} {"stash",-7} {0,4} items  {name}");
                Console.WriteLine($"                {ex.Message}");
            }
        }

        if (files.Count == 0)
            return 0;

        SaveVerifier.DumpPath = Option("--dump");

        if (arguments.Contains("--explain") && files.Count == 1)
        {
            foreach (var attempt in SaveVerifier.Explain(files[0]))
                Console.WriteLine($"  as {attempt.Format,-7} {attempt.Summary,-15} {attempt.Error}");
            return 0;
        }

        var results = new List<VerifyResult>(files.Count);
        foreach (var file in files)
        {
            var result = SaveVerifier.Verify(file);
            results.Add(result);

            string name = corpus is not null ? Path.GetRelativePath(corpus, file) : Path.GetFileName(file);
            Console.WriteLine($"{result.Summary,-15} {result.Format,-7} {result.ItemCount,4} items  {name}");
            if (result.Error is not null)
                Console.WriteLine($"                {result.Error}");
            else if (!result.RoundTripped)
                Console.WriteLine($"                first difference at 0x{result.FirstDifference:X}, " +
                                  $"{result.OriginalLength} bytes in, {result.WrittenLength} out");
        }

        int ok = results.Count(r => r.RoundTripped);
        Console.WriteLine($"\n{ok}/{results.Count} round-tripped byte-identical");
        foreach (var group in results.GroupBy(r => (r.Format, r.Summary)).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {group.Key.Format,-7} {group.Key.Summary,-15} {group.Count()}");

        return ok == results.Count ? 0 : 1;
    }

    /// <summary>
    /// What the legitimacy check costs: every item in the saves, stashes or folders given, checked
    /// --rounds times (default 20); the mean per item, the worst items, and each rule's share, timed
    /// by calling the rules one by one.
    /// </summary>
    static int LegitCost(string gameRoot, IReadOnlyList<string> sources)
    {
        if (sources.Count == 0)
            throw new ArgumentException("legitcost <save|stash|folder...> [--rounds <n>]");

        int rounds = int.TryParse(Option("--rounds"), out int given) && given > 0 ? given : 20;
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var items = new List<Item>();
        foreach (string source in sources)
        {
            var files = Directory.Exists(source) ? SavesUnder(source) : [source];
            foreach (string file in files)
            {
                try
                {
                    foreach (var (_, item) in Contents(file))
                        items.AddRange(item.WithSockets().Where(each => !each.IsCompact));
                }
                catch (Exception)
                {
                    // A file this season cannot read is not what is being measured.
                }
            }
        }

        Console.WriteLine($"{items.Count} items, {rounds} rounds");
        foreach (var item in items)
            Legitimacy.Check(item);   // warm: every table cache filled once

        var rules = new[] { "Affixes", "Counts", "Sockets", "SocketSource", "Groups", "Values" }
            .Select(name => (Name: name, Run: (Action<Item, List<Finding>>)Delegate.CreateDelegate(
                typeof(Action<Item, List<Finding>>),
                typeof(Legitimacy).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!)))
            .ToList();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int round = 0; round < rounds; round++)
        {
            foreach (var item in items)
                Legitimacy.Check(item);
        }
        double whole = clock.Elapsed.TotalMilliseconds;
        Console.WriteLine($"Check, remembered: {whole / (rounds * items.Count) * 1000:F1} us per item, {whole / rounds:F1} ms per pass over all");

        var fresh = System.Diagnostics.Stopwatch.StartNew();
        for (int round = 0; round < rounds; round++)
        {
            foreach (var item in items)
                Legitimacy.Judge(item);
        }
        double judged = fresh.Elapsed.TotalMilliseconds;
        Console.WriteLine($"Judge, every time: {judged / (rounds * items.Count) * 1000:F1} us per item, {judged / rounds:F1} ms per pass over all");

        // The memo must never change an answer: every item judged fresh against what Check remembers.
        int differ = items.Count(item => !Legitimacy.Judge(item).Select(each => each.Said).SequenceEqual(Legitimacy.Check(item).Select(each => each.Said)));
        Console.WriteLine($"remembered answers that differ from fresh ones: {differ}");

        foreach (var (name, run) in rules)
        {
            var found = new List<Finding>();
            var each = System.Diagnostics.Stopwatch.StartNew();
            for (int round = 0; round < rounds; round++)
            {
                foreach (var item in items)
                {
                    found.Clear();
                    run(item, found);
                }
            }
            double ms = each.Elapsed.TotalMilliseconds;
            Console.WriteLine($"  {name,-13} {ms / (rounds * items.Count) * 1000,8:F1} us per item (alone, fresh)");
        }

        Console.WriteLine("slowest items, judged fresh:");
        var slow = items.Select(item =>
        {
            var one = System.Diagnostics.Stopwatch.StartNew();
            for (int round = 0; round < rounds; round++)
                Legitimacy.Judge(item);
            return (Item: item, Us: one.Elapsed.TotalMilliseconds * 1000 / rounds);
        }).OrderByDescending(each => each.Us).Take(8).ToList();
        foreach (var (item, us) in slow)
            Console.WriteLine($"  {us,8:F1} us  {ItemPresenter.Describe(item).Name} [{item.Code?.Trim()} {item.Quality}] stats {item.StatLists.Sum(list => list.Stats.Count)}");

        return 0;
    }

    /// <summary>
    /// The season facts in code (<see cref="SeasonFacts"/>), one line each - name, then state - for
    /// tests/season.py to compare across a rollover; where each is written and what to do on a change below.
    /// </summary>
    static int SeasonFactsCommand(string gameRoot)
    {
        using var installed = Season.Open(gameRoot);
        var read = SeasonFacts.Read();
        foreach (var (fact, state) in read)
            Console.WriteLine($"{fact.Name}: {state}");

        Console.WriteLine();
        foreach (var (fact, _) in read)
            Console.WriteLine($"  {fact.Name} - {fact.Where}. On a change: {fact.AtRollover}.");

        return 0;
    }

    /// <summary>
    /// Which of a flat damage's copies the items of real files hold (after armor.txt's own
    /// "mindam" was found to make the trade import read every armour as a one-handed weapon): "+9 to
    /// Maximum Damage" is maxdamage, secondary_maxdamage and item_throw_maxdamage, and DamageKinds says
    /// which of them a base holds. Every item of the files given - their sockets' too - that holds any of
    /// the six stats, counted by the kind of base and the copies held, and set against the rule: an item
    /// holding other than the rule says is listed. --items lists every one.
    /// </summary>
    static int DamageKindsCheck(string gameRoot, IReadOnlyList<string> sources)
    {
        if (sources.Count == 0)
            throw new ArgumentException("damagekinds <save|stash|folder...> [--items]");

        using var installed = Season.Open(gameRoot, Tables());
        bool everyItem = arguments.Contains("--items");

        string[][] ends =
        [
            ["mindamage", "secondary_mindamage", "item_throw_mindamage"],
            ["maxdamage", "secondary_maxdamage", "item_throw_maxdamage"],
        ];
        string[] marks = ["1h", "2h", "throw"];

        var counted = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var apart = new List<string>();
        int files = 0, unread = 0, seen = 0;

        foreach (string source in sources)
        {
            foreach (string file in Directory.Exists(source) ? SavesUnder(source) : [source])
            {
                List<(string Where, Item Item)> held;
                try
                {
                    held = Contents(file);
                    files++;
                }
                catch (Exception)
                {
                    unread++;
                    continue;
                }

                foreach (var (where, outer) in held)
                {
                    foreach (var item in outer.WithSockets())
                    {
                        if (item.IsCompact)
                            continue;

                        string code = item.Code.Trim();
                        var row = D2SLib.Core.TXT.ItemsTXT.GetByCode(code);
                        string Column(string name)
                        {
                            try { return row?[name]?.Value?.Trim() ?? ""; }
                            catch (KeyNotFoundException) { return ""; }
                        }

                        string kind = D2SLib.Core.TXT.ItemsTXT.IsWeapon(code)
                            ? "weapon " + string.Join("+", new[] { ("1h", "mindam"), ("2h", "2handmindam"), ("throw", "minmisdam") }
                                .Where(each => Column(each.Item2).Length > 0).Select(each => each.Item1))
                            : D2SLib.Core.TXT.ItemsTXT.IsArmor(code) ? "armor" : "misc";

                        for (int list = 0; list < item.StatLists.Count; list++)
                        {
                            foreach (string[] copies in ends)
                            {
                                var values = copies.Select(stat => item.StatLists[list].Stats
                                    .Where(each => string.Equals(each.Stat, stat, StringComparison.OrdinalIgnoreCase))
                                    .Select(each => (int?)each.Value).FirstOrDefault()).ToArray();
                                if (values.All(value => value is null))
                                    continue;

                                seen++;
                                string holds = string.Join("+", marks.Where((_, n) => values[n] is not null));
                                string wanted = string.Join("+", marks.Where((_, n) => DamageKinds.HoldsIn(item, list, copies[n])));
                                bool alike = values.Where(value => value is not null).Distinct().Count() == 1;
                                string key = $"{kind,-18} {(list == 0 ? "own list" : item.IsRuneword ? "runeword" : "set bonus"),-10} holds {holds,-12} rule {wanted,-12} {(holds == wanted ? "as the rule" : "NOT the rule")}{(alike ? "" : ", numbers differ")}";
                                counted[key] = counted.GetValueOrDefault(key) + 1;

                                if (everyItem || holds != wanted || !alike)
                                {
                                    apart.Add($"{Path.GetFileName(file)} {where} {ItemPresenter.Describe(item).Name} ({code}, {kind}, list {list}): "
                                              + string.Join(", ", copies.Select((stat, n) => $"{stat} {values[n]?.ToString() ?? "-"}")));
                                }
                            }
                        }
                    }
                }
            }
        }

        Console.WriteLine($"{files} files read, {unread} not read; {seen} flat damage sets");
        foreach (var (key, count) in counted)
            Console.WriteLine($"{count,6}  {key}");

        foreach (string line in apart.Take(everyItem ? int.MaxValue : 60))
            Console.WriteLine("  " + line);
        if (!everyItem && apart.Count > 60)
            Console.WriteLine($"  ... {apart.Count - 60} more");

        return 0;
    }
}
