using System.Text;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Testbed;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Cli;

// One-off investigations kept for their method. Candidates to retire.
internal static partial class Cli
{
    // The character figure's open questions, put in front of the game: a character spawned with the equipment,
    // to load in game. The
    // character's worn gear replaced by one --case:
    //   affixes (default, a Barbarian) - a Jawbone Cap and a Buckler (the helm and shield graphic bytes),
    //     a magic Short Sword with a coloured prefix and suffix, a rare Breast Plate with a coloured
    //     prefix and two coloured suffixes: which affix colours an item on the character;
    //   orb-alone (a Sorceress) - an orb with a coloured automagic (Drake's) and uncoloured affixes: does
    //     the automagic colour it at all;
    //   orb-suffix (a Sorceress) - the same orb with a coloured suffix: the automagic or the suffix;
    //   shield-prefix (a Paladin) - an auric shield with Knight's and a coloured prefix, no suffix: the
    //     automagic or the prefix; and magic Leather Armor with a coloured prefix alone.
    // Prints what each rule predicts; once the game has saved, `figuretest --read` prints what it wrote.
    // The automagic's own property is not written - its id is what colours.
    static int FigureTest(string gameRoot, string path, bool read)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;
        string[] parts = CharacterFigure.Parts;

        var (save, _) = SaveVerifier.Load(path);
        var looks = typeof(Appearances).GetProperties()
            .Select(p => (Appearance)p.GetValue(save.Appearances)!).ToList();
        string Colour(int row) => row >= 0 && game.Rows("colors") is { Count: > 1 } colours && row + 1 < colours.Count ? colours[row + 1][1] : $"#{row}";
        string Byte(int part) => looks[part].Tint == 255 ? "none"
            : $"table {looks[part].Tint >> 5}, {((looks[part].Tint & 31) == 0 ? "no colour" : Colour((looks[part].Tint & 31) - 1))}";

        if (read)
        {
            foreach (int part in new[] { 0, 1, 5, 6, 7 })
                Console.WriteLine($"{parts[part]}: graphic {looks[part].Graphic}, tint {looks[part].Tint} ({Byte(part)})");
            return 0;
        }

        // The items' stamp: the character's own, or - a new character holds none - another save's (--era-from).
        string? era = save.PlayerItemList.Items.Select(item => item.Version).FirstOrDefault(v => v is { Length: > 0 });
        if (era is null && Option("--era-from") is { } other)
        {
            era = NewCharacter.ItemEra(SaveVerifier.Load(other).Save);
            SaveVerifier.Load(path);   // the layout back to this character's
        }
        if (era is null)
            throw new ArgumentException("the character carries no item to take the item stamp from; give --era-from <save>");
        save.PlayerItemList.Items.RemoveAll(item => item.Mode == ItemMode.Equipped);

        // A table row's colour where its transform is set: an affix by its id (its data row), an
        // automagic by its id less one (its id counts rows from one).
        string? RowColour(string table, int dataRow)
        {
            var rows = game.Rows(table);
            var head = rows[0];
            int tr = Array.IndexOf(head, "transform"), c = Array.IndexOf(head, "transformcolor");
            var row = dataRow + 1 < rows.Count ? rows[dataRow + 1] : null;
            return row is not null && tr < row.Length && row[tr].Trim() == "1" && c < row.Length && row[c].Trim().Length > 0 ? row[c].Trim() : null;
        }
        string? AffixColour(bool prefix, int id) => RowColour(prefix ? "magicprefix" : "magicsuffix", id);

        var random = new Random(7);
        Item Forge(string code, ItemQuality quality, ItemLocation at)
        {
            var item = ItemForge.PlainBase(code, 0, era).Item;
            item.ItemLevel = 60;
            item.Quality = quality;
            item.Flags[ItemForge.Identified] = true;
            item.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);   // its own, as a created character's cube gets
            Equipping.Place(item, at);
            save.PlayerItemList.Items.Add(item);
            return item;
        }

        // The automagic row of that name, by its id; checked against the base's own group.
        string Automagic(Item item, string name, string group)
        {
            var rows = game.Rows("automagic");
            int n = Array.IndexOf(rows[0], "Name");
            int id = rows.Select((row, at) => (row, at)).First(r => r.at > 0 && n < r.row.Length && r.row[n] == name).at;
            if (AffixRanges.AutomagicGroupOf(id) != group)
                throw new InvalidOperationException($"automagic {name} as id {id} is in group {AffixRanges.AutomagicGroupOf(id) ?? "none"}, not {group}");
            item.IsAutoAffix = true;
            item.AutoAffixId = (ushort)id;
            return RowColour("automagic", id - 1) ?? throw new InvalidOperationException($"{name} has no colour");
        }

        // Affixes for the slots asked, each coloured or not as asked; a coloured one in a colour none
        // of the others has (nor the item's automagic). Written with their stats.
        List<(bool Prefix, int Slot, MagicAffixes.AffixOption Option, string? Colour)> Affixes(
            Item item, (bool Prefix, int Slot, bool Coloured)[] wanted, params string[] taken)
        {
            var chosen = new List<(bool Prefix, int Slot, MagicAffixes.AffixOption Option, string? Colour)>();
            var colours = new HashSet<string>(taken);
            var groups = new HashSet<string>();
            foreach (var (prefix, slot, coloured) in wanted)
            {
                var pick = MagicAffixes.Available(item, prefix, slot)
                    .Select(option => (Option: option, Colour: AffixColour(prefix, option.Id)))
                    .FirstOrDefault(o => !groups.Contains(o.Option.Group)
                                         && (coloured ? o.Colour is { } c && !colours.Contains(c) : o.Colour is null));
                if (pick.Option is null)
                    throw new InvalidOperationException($"no {(coloured ? "coloured" : "plain")} {(prefix ? "prefix" : "suffix")} for {item.Code.Trim()} slot {slot + 1}");
                if (pick.Colour is { } colour)
                    colours.Add(colour);
                groups.Add(pick.Option.Group);
                chosen.Add((prefix, slot, pick.Option, pick.Colour));
            }
            MagicAffixes.Apply(item,
                chosen.ToDictionary(c => (c.Slot, c.Prefix), c => new MagicAffixes.AffixPick(c.Option.Id, MagicAffixes.Roll(c.Option, random))),
                random);
            return chosen;
        }

        static string Said(IEnumerable<(bool Prefix, int Slot, MagicAffixes.AffixOption Option, string? Colour)> picks) =>
            string.Join(", ", picks.Select(p => $"{(p.Prefix ? "prefix" : "suffix")} {p.Option.Name} ({p.Colour ?? "no colour"})"));

        string gear = Option("--case") ?? "affixes";
        switch (gear)
        {
            case "affixes":
            {
                Forge("ba1", ItemQuality.Normal, ItemLocation.Head);
                Forge("buc", ItemQuality.Normal, ItemLocation.LeftHand);
                var sword = Forge("ssd", ItemQuality.Magic, ItemLocation.RightHand);
                var onSword = Affixes(sword, [(true, 0, true), (false, 0, true)]);
                var plate = Forge("brs", ItemQuality.Rare, ItemLocation.Torso);
                plate.RarePrefixId = (ushort)AffixNames.FirstNames(plate, "")[0].Id;
                plate.RareSuffixId = (ushort)AffixNames.SecondNames(plate, "")[0].Id;
                var onPlate = Affixes(plate, [(true, 0, true), (false, 0, true), (false, 1, true)]);

                Console.WriteLine("worn: Jawbone Cap, Buckler, magic Short Sword, rare Breast Plate");
                Console.WriteLine($"  helm ba1 predicts graphic {AltIndex(game, "armor", "ba1") + 61}; shield buc {AltIndex(game, "armor", "buc") + 61} if shields share the list");
                Console.WriteLine($"  sword: {Said(onSword)}");
                Console.WriteLine($"  plate: {Said(onPlate)}");
                break;
            }
            case "orb-alone" or "orb-suffix":
            {
                var orb = Forge("ob1", ItemQuality.Magic, ItemLocation.RightHand);
                string auto = Automagic(orb, "Drake's", "303");
                var picks = Affixes(orb, [(true, 0, false), (false, 0, gear == "orb-suffix")], auto);
                Console.WriteLine($"worn: a magic Eagle Orb, automagic Drake's ({auto}); {Said(picks)}");
                Console.WriteLine(gear == "orb-alone"
                    ? $"  RH tint {auto}: the automagic colours it; none: it does not"
                    : $"  RH tint {auto}: the automagic wins; {picks[1].Colour}: the suffix wins");
                break;
            }
            case "shield-prefix":
            {
                var shield = Forge("pa1", ItemQuality.Magic, ItemLocation.LeftHand);
                string auto = Automagic(shield, "Knight's", "304");
                var onShield = Affixes(shield, [(true, 0, true)], auto);
                var armour = Forge("lea", ItemQuality.Magic, ItemLocation.Torso);
                var onArmour = Affixes(armour, [(true, 0, true)]);
                Console.WriteLine($"worn: a magic Targe, automagic Knight's ({auto}); {Said(onShield)}");
                Console.WriteLine($"  SH tint {auto}: the automagic wins; {onShield[0].Colour}: the prefix wins");
                Console.WriteLine($"  and magic Leather Armor, {Said(onArmour)} - TR tint {onArmour[0].Colour}: a prefix alone colours it");
                break;
            }
            default:
                throw new ArgumentException($"--case is affixes, orb-alone, orb-suffix or shield-prefix, not {gear}");
        }

        // The list's own count is what the writer writes, not the list's length.
        save.PlayerItemList.Count = (ushort)save.PlayerItemList.Items.Count;
        string backup = SaveWriter.Commit(path, save, null);
        Console.WriteLine($"written; backup {backup}");
        return 0;
    }

    // What the character figure costs per character, step by step as the landing page
    // does it: the save read (on the UI thread there), its look, the layers composed and the frames enlarged
    // once (a worker there). Every save in a folder, twice: cold, then with the decoded layers kept - a
    // second character in the same armour decodes nothing.
    static int FigureTime(string gameRoot, string folder)
    {
        using var installed = Season.Open(gameRoot);
        var loader = FigureLoader.Current ?? throw new InvalidOperationException("no figure loader installed");
        var saves = Directory.GetFiles(folder, "*.d2s");
        foreach (string pass in new[] { "cold", "warm" })
        {
            double read = 0, look = 0, compose = 0, enlarge = 0;
            long bytes = 0;
            int drawn = 0;
            var clock = new System.Diagnostics.Stopwatch();
            foreach (string path in saves)
            {
                clock.Restart();
                D2S save;
                try { save = SaveVerifier.Load(path).Save; }
                catch (InvalidDataException) { continue; }
                read += clock.Elapsed.TotalMilliseconds;

                clock.Restart();
                var figureLook = CharacterFigure.LookOf(save);
                look += clock.Elapsed.TotalMilliseconds;

                clock.Restart();
                var figure = loader.Compose(figureLook);
                compose += clock.Elapsed.TotalMilliseconds;
                if (figure is null)
                    continue;

                clock.Restart();
                foreach (var frame in figure.Frames)
                    bytes += PixelArt.Scale2x(frame.Bgra, figure.Width, figure.Height, 1).Length * 4L;
                enlarge += clock.Elapsed.TotalMilliseconds;
                drawn++;
            }
            Console.WriteLine($"{pass}: {drawn} of {saves.Length} drawn - per character read {read / drawn:0.0} ms, look {look / drawn:0.0} ms, "
                              + $"compose {compose / drawn:0.0} ms, enlarge {enlarge / drawn:0.0} ms; total {(read + look + compose + enlarge):0} ms; "
                              + $"frames {bytes / drawn / 1024.0 / 1024:0.0} MB a character at 2x");
        }
        return 0;
    }

    // Where a code's art stands in a table's list of distinct alternateGfx codes, first occurrence first.
    static int AltIndex(GameData game, string table, string code)
    {
        var rows = game.Rows(table);
        int g = Array.FindIndex(rows[0], h => h.Equals("alternategfx", StringComparison.OrdinalIgnoreCase));
        int c = Array.IndexOf(rows[0], "code");
        var seen = new List<string>();
        foreach (var row in rows.Skip(1))
            if (g < row.Length && row[g].Length > 0 && !seen.Contains(row[g]))
                seen.Add(row[g]);
        string art = rows.Skip(1).First(row => c < row.Length && row[c] == code)[g];
        return seen.IndexOf(art);
    }

    // Across every save in a folder: each quest's flag words per difficulty and how often each
    // occurs - what "finished" looks like in saves the game or other tools wrote. --quest narrows it.
    static int QuestSurvey(string gameRoot, string? folder, string? only)
    {
        if (folder is null)
            throw new ArgumentException("questsurvey <folder> [--quest Name]");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        string Bits(D2SLib.Model.Save.Quest quest) =>
            string.Concat(typeof(D2SLib.Model.Save.Quest).GetProperties().Where(p => p.PropertyType == typeof(bool))
                                                                .Select(p => (bool)p.GetValue(quest)! ? "1" : "."));

        var counts = new Dictionary<(string Difficulty, string Quest, string Word), int>();
        int read = 0, refused = 0;
        foreach (string path in Directory.GetFiles(folder, "*.d2s", SearchOption.AllDirectories))
        {
            D2SLib.Model.Save.D2S save;
            try { save = SaveVerifier.Load(path).Save; } catch { refused++; continue; }
            read++;
            foreach (string difficulty in new[] { "Normal", "Nightmare", "Hell" })
            {
                var diff = (D2SLib.Model.Save.QuestsDifficulty)typeof(D2SLib.Model.Save.QuestsSection).GetProperty(difficulty)!.GetValue(save.Quests)!;
                foreach (var act in typeof(D2SLib.Model.Save.QuestsDifficulty).GetProperties())
                    foreach (var q in act.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(D2SLib.Model.Save.Quest)))
                    {
                        if (q.Name.StartsWith("Extra") || (only is not null && q.Name != only)) continue;
                        var key = (difficulty, $"{act.Name}.{q.Name}", Bits((D2SLib.Model.Save.Quest)q.GetValue(act.GetValue(diff)!)!));
                        counts[key] = counts.GetValueOrDefault(key) + 1;
                    }
            }
        }

        Console.WriteLine($"{read} saves read, {refused} refused");
        foreach (var group in counts.GroupBy(pair => (pair.Key.Difficulty, pair.Key.Quest)))
        {
            Console.WriteLine($"{group.Key.Difficulty,-9} {group.Key.Quest,-32} " +
                string.Join("  ", group.OrderByDescending(pair => pair.Value).Take(5).Select(pair => $"{pair.Key.Word} x{pair.Value}")));
        }
        return 0;
    }

    // Imports many armory characters into a scratch folder and says, per character, what came through:
    // read back, items whole, legitimacy, skill points written against the armory's, what was left out.
    // Armory answers are cached as <name>.json in the folder, so a rerun asks the site nothing.
    // The ladder's characters' armories into <out>/<mode>/, one JSON each - only those not there yet,
    // so a run stopped part way picks up where it left off. The plan is ladders and depths in order,
    // "softcore:50,hardcore:50,softcore:200,hardcore:200"; each is walked rank by rank across the
    // classes, so whatever has arrived covers every class.
    //
    // Paced at one request every 12-18 s (240 an hour, a deliberate ceiling), through TradeGate
    // as every request is. One process for the whole plan, because the gate counts
    // its hour in the process: a second run started straight after one that reached the ceiling sent
    // 23 more inside the hour. The gate's own refusals send nothing and are waited out; a server error
    // is retried once after two minutes, then the name goes in _failed.txt, which a later run skips.
    static int ArmoryFetch(string ladderPath, string plan, string? root)
    {
        if (root is null || !File.Exists(ladderPath))
            throw new ArgumentException("armoryfetch <ladder.json> --plan softcore:50,hardcore:50 --out <folder>");

        using var ladder = System.Text.Json.JsonDocument.Parse(File.ReadAllText(ladderPath));
        int fetchedAll = 0;

        // One request every 12-18 s, at random within that - about 240 an hour, and never on a beat.
        TradeGate.AllowPerHour(240);
        var jitter = new Random();
        var lastRequest = DateTime.MinValue;
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} pace: every 12-18 s, at most {TradeGate.HourlyCeiling} an hour");

        foreach (string step in plan.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string mode = step.Split(':')[0];
            int per = int.Parse(step.Split(':')[1]);
            string folder = Path.Combine(root, mode);
            Directory.CreateDirectory(folder);

            var classes = ladder.RootElement.GetProperty(mode).EnumerateObject()
                .Where(each => each.Name != "overall")
                .Select(each => each.Value.EnumerateArray().Take(per).Select(entry => entry.GetProperty("name").GetString() ?? "").ToList())
                .ToList();

            var names = new List<string>();
            for (int rank = 0; rank < per; rank++)
                foreach (var list in classes)
                    if (rank < list.Count && list[rank].Length > 0 && !names.Contains(list[rank]))
                        names.Add(list[rank]);

            string failedLog = Path.Combine(folder, "_failed.txt");
            var failedBefore = File.Exists(failedLog) ? File.ReadAllLines(failedLog).Select(line => line.Split('\t')[0]).ToHashSet() : [];
            int have = 0, fetched = 0, failed = 0;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {mode} top {per}: {names.Count} names");

            foreach (string name in names)
            {
                string cache = Path.Combine(folder, name + ".json");
                if (File.Exists(cache) || failedBefore.Contains(name))
                {
                    have++;
                    continue;
                }

                int errors = 0;
                while (true)
                {
                    var wait = lastRequest + TimeSpan.FromSeconds(12 + jitter.NextDouble() * 6) - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                        Thread.Sleep(wait);
                    lastRequest = DateTime.UtcNow;

                    try
                    {
                        var armory = ArmoryImport.Fetch(name).GetAwaiter().GetResult();
                        File.WriteAllText(cache, armory.Root.GetRawText());
                        fetched++;
                        fetchedAll++;
                        break;
                    }
                    catch (ArmoryImport.ArmoryException problem) when (problem.Message.StartsWith("Trade site busy. Retry in", StringComparison.Ordinal)
                                                                       || problem.Message.StartsWith("Hourly request limit", StringComparison.Ordinal))
                    {
                        // The gate refused without asking: wait for it.
                        Thread.Sleep(TimeSpan.FromMinutes(1));
                    }
                    catch (ArmoryImport.ArmoryException problem) when (problem.InnerException is TradeGate.TradeGateException && ++errors < 2)
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss} {name}: {problem.Message} - waiting");
                        Thread.Sleep(TimeSpan.FromMinutes(2));
                    }
                    catch (Exception problem)
                    {
                        failed++;
                        File.AppendAllText(failedLog, $"{name}\t{problem.Message}\n");
                        break;
                    }
                }

                Console.WriteLine($"{DateTime.Now:HH:mm:ss} {mode} {have + fetched + failed}/{names.Count} {name}  (fetched {fetched}, failed {failed})");
            }

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {mode} top {per} done: {have} already here, {fetched} fetched, {failed} failed");
        }

        return 0;
    }

    /// <summary>
    /// Every item's base, quality, item level and automagic - whether it carries one and which row -
    /// one tab-separated line each, for measuring which items the game gives one and from where.
    /// </summary>
    static int AutomagicIds(string gameRoot, string? tables, string? corpus, string? single)
    {
        if (corpus is null && single is null)
            throw new ArgumentException("autoids needs a save or stash file, or --corpus <dir>");

        using var installed = Season.Open(gameRoot, tables);
        var game = installed.Game;

        foreach (var file in corpus is not null ? SavesUnder(corpus) : [single!])
        {
            List<(string Where, Item Item)> held;
            try
            {
                held = Contents(file);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"refused  {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            foreach (var (_, item) in held)
            {
                if (item.IsCompact)
                    continue;

                string stats = string.Join(",", (item.StatLists.FirstOrDefault()?.Stats ?? []).Select(stat => $"{stat.Stat}={stat.Value}"));

                // What the editor would pick from the properties alone, the stored answer out of sight.
                bool had = item.IsAutoAffix;
                ushort stored = item.AutoAffixId;
                item.IsAutoAffix = false;
                item.AutoAffixId = 0;
                string picked = AffixRanges.AutomagicFor(item)?.ToString() ?? "-";
                item.IsAutoAffix = had;
                item.AutoAffixId = stored;
                Console.WriteLine(string.Join('\t', Path.GetFileName(file), item.Code.Trim(), item.Quality, item.ItemLevel,
                    item.IsAutoAffix ? item.AutoAffixId.ToString() : "-", item.IsRuneword ? "rw" : "", stats, picked));
            }
        }

        return 0;
    }

    /// <summary>
    /// Every runeword item in a save, a stash or a library, one tab-separated line each: the file, the
    /// 12-bit id the item stores, its socketed runes in order and its base. For measuring what that id
    /// is - the one number no rule yet reads off a runeword's name.
    /// </summary>
    // Every charged skill and chance to cast in a corpus beside the affix row that granted it: the
    // item's level and affix level, the row's own numbers, and what the item holds - for measuring
    // how a row written negative, or with level 0, turns into what the game stores.
    static int ChargeScan(string gameRoot, string? tables, string? corpus, string? single)
    {
        if (corpus is null && single is null)
            throw new ArgumentException("chargescan needs a save or stash file, or --corpus <dir>");

        using var installed = Season.Open(gameRoot, tables);
        var game = installed.Game;

        Console.WriteLine("file	code	quality	ilvl	alvl	affix	rowlevel	skill	rowmin	rowmax	stat	level	first	charges");
        foreach (var file in corpus is not null ? SavesUnder(corpus) : [single!])
        {
            List<(string Where, Item Item)> held;
            try
            {
                held = Contents(file);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"refused  {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            foreach (var (_, item) in held)
            {
                if (item.IsCompact)
                    continue;

                // A unique or set piece: its casts and charges beside its item level, the row being
                // read by hand - for the level-0 casts (Snowclash).
                if (item.Quality is ItemQuality.Unique or ItemQuality.Set)
                {
                    foreach (var stat in ItemStats.Every(item).Where(stat => stat.SkillId is not null && stat.SkillLevel is not null))
                        Console.WriteLine($"{Path.GetFileName(file)}	{item.Code.Trim()}	{item.Quality}	{item.ItemLevel}	-	{ItemPresenter.Describe(item).Name}	-	{stat.SkillId}	-	-	{stat.Stat}	{stat.SkillLevel}	{stat.Value}	{stat.MaxCharges}");
                    continue;
                }

                if (!MagicAffixes.Carries(item))
                    continue;

                var rows = new List<MagicAffixes.AffixOption>();
                for (int slot = 0; slot < MagicAffixes.Slots; slot++)
                {
                    foreach (bool prefix in new[] { true, false })
                    {
                        if (MagicAffixes.At(item, slot, prefix) is { } row)
                            rows.Add(row);
                    }
                }

                foreach (var stat in ItemStats.Every(item))
                {
                    if (stat.SkillId is not { } skill || stat.SkillLevel is null)
                        continue;

                    foreach (var row in rows)
                    {
                        foreach (var grant in row.Grants.Where(grant => grant.Param == skill))
                        {
                            Console.WriteLine($"{Path.GetFileName(file)}	{item.Code.Trim()}	{item.Quality}	{item.ItemLevel}	{MagicAffixes.AffixLevel(item)}	{row.Name}	{row.Level}	{skill}	{grant.Min}	{grant.Max}	{stat.Stat}	{stat.SkillLevel}	{stat.Value}	{stat.MaxCharges}");
                        }
                    }
                }
            }
        }

        return 0;
    }

    static int RunewordIds(string gameRoot, string? tables, string? corpus, string? single)
    {
        if (corpus is null && single is null)
            throw new ArgumentException("rwids needs a save or stash file, or --corpus <dir>");

        using var installed = Season.Open(gameRoot, tables);
        var game = installed.Game;

        foreach (var file in corpus is not null ? SavesUnder(corpus) : [single!])
        {
            List<(string Where, Item Item)> held;
            try
            {
                held = Contents(file);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"refused  {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            foreach (var (_, item) in held)
            {
                if (!item.IsRuneword)
                    continue;

                string runes = string.Join(" ", item.SocketedItems.Select(rune => rune.Code.Trim()));
                var word = Runewords.On(item);
                string rule = word is not null && Runewords.IdOf(word) is { } id ? id.ToString() : "-";
                Console.WriteLine($"{Path.GetFileName(file)}\t{item.RunewordId}\t{runes}\t{item.Code.Trim()}\t{item.Version}\t{word?.Key}\t{rule}");
            }
        }

        return 0;
    }
}
