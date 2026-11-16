using System.Text;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Testbed;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Cli;

// Read-only views of saves, stashes and the game tables.
internal static partial class Cli
{
    static int Info(string gameRoot)
    {
        using var game = GameData.Open(gameRoot);

        Console.WriteLine($"game root: {gameRoot}");
        Console.WriteLine("archive chain (highest priority first):");
        foreach (var archive in game.LoadedArchives)
            Console.WriteLine($"  {archive}");

        Console.WriteLine();
        Console.WriteLine($"{"table",-16} {"rows",6} {"cols",5}  source");
        foreach (var name in GameData.ExcelTables)
        {
            try
            {
                var (table, archive) = game.ReadExcel(name);
                Console.WriteLine($"{name,-16} {table.Rows.Count,6} {table.Columns.Count,5}  {archive}");
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"{name,-16} {"-",6} {"-",5}  MISSING");
            }
        }
        return 0;
    }

    static int Extract(string gameRoot, string outputDir)
    {
        using var game = GameData.Open(gameRoot);
        Directory.CreateDirectory(outputDir);

        foreach (var name in GameData.ExcelTables)
        {
            var result = game.Read(GameData.ExcelPath(name));
            if (result is null)
            {
                Console.WriteLine($"  skip {name}.txt (not in any archive)");
                continue;
            }
            string path = Path.Combine(outputDir, $"{name}.txt");
            File.WriteAllBytes(path, result.Value.Data);
            Console.WriteLine($"  {name,-16} {result.Value.Data.Length,8} bytes  <- {result.Value.Archive}");
        }
        Console.WriteLine($"\nwrote to {Path.GetFullPath(outputDir)}");
        return 0;
    }

    /// <summary>Every file an archive's (listfile) names that contains the text - finding a graphic by name.</summary>
    static int FilesCommand(string gameRoot, string text)
    {
        foreach (string path in GameData.Open(gameRoot).LoadedArchives)
        {
            using var archive = PD2SPCompanion.Core.Mpq.MpqArchive.Open(path);
            var list = archive.ReadFile("(listfile)");
            if (list is null)
            {
                Console.WriteLine($"{Path.GetFileName(path)}: no (listfile)");
                continue;
            }

            var names = System.Text.Encoding.ASCII.GetString(list)
                .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Where(name => name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Console.WriteLine($"{Path.GetFileName(path)}: {names.Count}");
            foreach (string name in names)
                Console.WriteLine($"  {name}");
        }

        return 0;
    }

    // Every quest's sixteen flags, per difficulty, for one or more saves side by side - to read
    // what a finished quest looks like before writing one.
    static int QuestsCommand(string gameRoot, IReadOnlyList<string> saves)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var loaded = saves.Select(path => (Name: Path.GetFileNameWithoutExtension(path), Save: SaveVerifier.Load(path).Save)).ToList();
        Console.WriteLine("progression: " + string.Join("  ", loaded.Select(each => $"{each.Name} {each.Save.Progression} lvl {each.Save.Level}")));

        string Bits(D2SLib.Model.Save.Quest quest) =>
            string.Concat(typeof(D2SLib.Model.Save.Quest).GetProperties().Where(p => p.PropertyType == typeof(bool))
                                                                .Select(p => (bool)p.GetValue(quest)! ? "1" : "."));

        Console.WriteLine("flag order: " + string.Join(",", typeof(D2SLib.Model.Save.Quest).GetProperties().Where(p => p.PropertyType == typeof(bool)).Select(p => p.Name)));
        foreach (string difficulty in new[] { "Normal", "Nightmare", "Hell" })
        {
            Console.WriteLine($"== {difficulty}");
            var acts = typeof(D2SLib.Model.Save.QuestsDifficulty).GetProperties();
            foreach (var act in acts)
            {
                foreach (var questProperty in act.PropertyType.GetProperties().Where(p => p.PropertyType == typeof(D2SLib.Model.Save.Quest)))
                {
                    var row = loaded.Select(each =>
                    {
                        var diff = (D2SLib.Model.Save.QuestsDifficulty)typeof(D2SLib.Model.Save.QuestsSection).GetProperty(difficulty)!.GetValue(each.Save.Quests)!;
                        var a = act.GetValue(diff)!;
                        return Bits((D2SLib.Model.Save.Quest)questProperty.GetValue(a)!);
                    });
                    Console.WriteLine($"  {act.Name,-6} {questProperty.Name,-24} {string.Join("  ", row)}");
                }
            }
        }
        return 0;
    }

    /// <summary>Any table from the archive chain, as the game reads it: its columns, then the first rows.</summary>
    static int Table(string gameRoot, string name, int rows)
    {
        using var game = GameData.Open(gameRoot);
        var (table, archive) = game.ReadExcel(name);
        Console.WriteLine($"{name}.txt <- {archive}, {table.Rows.Count} rows");
        Console.WriteLine(string.Join(" | ", table.Columns));
        foreach (var row in table.Rows.Take(rows))
            Console.WriteLine(string.Join(" | ", row));
        return 0;
    }

    static int StatDiff(string oldPath, string newPath)
    {
        var oldTable = TxtTable.Parse("itemstatcost", File.ReadAllBytes(oldPath));
        var newTable = TxtTable.Parse("itemstatcost", File.ReadAllBytes(newPath));

        Console.WriteLine($"old: {oldPath}  ({oldTable.Rows.Count} rows)");
        Console.WriteLine($"new: {newPath}  ({newTable.Rows.Count} rows)");
        Console.WriteLine();

        // "Stat" is the name the game uses; the row's position is the 9-bit id written into items.
        var oldByName = Index(oldTable);
        var newByName = Index(newTable);

        var added = newByName.Keys.Except(oldByName.Keys).ToList();
        var removed = oldByName.Keys.Except(newByName.Keys).ToList();

        Console.WriteLine($"added stats:   {added.Count}");
        foreach (var stat in added.Take(40))
            Console.WriteLine($"  + [{newByName[stat],3}] {stat}");
        if (added.Count > 40)
            Console.WriteLine($"  ... and {added.Count - 40} more");

        Console.WriteLine($"\nremoved stats: {removed.Count}");
        foreach (var stat in removed)
            Console.WriteLine($"  - [{oldByName[stat],3}] {stat}");

        // The two failure modes that silently corrupt an item bitstream.
        var movedIds = new List<string>();
        var changedBits = new List<string>();
        foreach (var (stat, newRow) in newByName)
        {
            if (!oldByName.TryGetValue(stat, out int oldRow))
                continue;
            if (oldRow != newRow)
                movedIds.Add($"  ~ {stat,-32} id {oldRow} -> {newRow}");

            foreach (var column in (string[])["Save Bits", "Save Add", "Save Param Bits", "Encode"])
            {
                string before = oldTable.Value(oldRow, column);
                string after = newTable.Value(newRow, column);
                if (before != after)
                    changedBits.Add($"  ~ {stat,-32} {column}: '{before}' -> '{after}'");
            }
        }

        Console.WriteLine($"\nstats whose id moved: {movedIds.Count}");
        foreach (var line in movedIds.Take(40))
            Console.WriteLine(line);
        if (movedIds.Count > 40)
            Console.WriteLine($"  ... and {movedIds.Count - 40} more");

        Console.WriteLine($"\nstats whose save encoding changed: {changedBits.Count}");
        foreach (var line in changedBits.Take(40))
            Console.WriteLine(line);
        if (changedBits.Count > 40)
            Console.WriteLine($"  ... and {changedBits.Count - 40} more");

        return 0;
    }

    static int ItemCode(string gameRoot, string code)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;
        var items = D2SLib.Core.TXT.ItemsTXT;

        foreach (var candidate in new[] { code, code.PadRight(4) })
        {
            Console.WriteLine($"lookup [{candidate}] (length {candidate.Length})");
            Console.WriteLine($"  IsArmor={items.IsArmor(candidate)} IsWeapon={items.IsWeapon(candidate)} IsMisc={items.IsMisc(candidate)}");
            try
            {
                var row = items.GetByCode(candidate);
                Console.WriteLine(row is null ? "  row = <null>" : $"  row found, stackable=[{row["stackable"].Value}]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  lookup threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }
        return 0;
    }

    static int Dump(string gameRoot, string path)
    {
        // --tables, like verify and legit: an older season's save needs the tables it was written
        // under, and without them it is refused rather than read wrong.
        using var installed = Season.Open(gameRoot, Tables());
        var game = installed.Game;

        bool stash = IsStash(path);

        if (!stash)
        {
            var (save, result) = SaveVerifier.Load(path);
            Console.Write(SaveDump.Render(save, result.Format, showStats: !arguments.Contains("--no-stats")));
            return 0;
        }

        // A stash keeps pages of items and no character at all, so it is walked rather than
        // rendered - the same contents legit reads, printed with the affix ids that explain them.
        bool showStats = !arguments.Contains("--no-stats");
        string? onlyPage = Option("--page");
        int seen = 0;

        // PD2's own shared stash keeps no page list: every item carries its page in its Location
        // field, so the pages are grouped out rather than walked. --page takes the number here,
        // where a PlugY stash matches on the page's name.
        if (IsSharedStash(path))
        {
            var shared = SharedStash.Read(path);

            for (int page = SharedStash.FirstPage; page <= SharedStash.LastPage; page++)
            {
                if (onlyPage is not null && onlyPage.Trim() != page.ToString())
                    continue;

                var onPage = (shared.Items.Items ?? [])
                    .Where(item => SharedStash.PageOf(item) == page)
                    .ToList();

                if (onPage.Count == 0)
                    continue;

                Console.WriteLine($"  == page {page} ({onPage.Count})");

                foreach (var item in onPage)
                {
                    Console.Write(SaveDump.Describe(item, showStats));
                    seen++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{seen} items");
            return 0;
        }

        var (held, _) = SaveVerifier.LoadStash(path);

        foreach (var page in held.PageList ?? [])
        {
            var items = page.PageItems?.Items ?? [];
            if (items.Count == 0)
                continue;

            if (onlyPage is not null
                && (page.Name ?? "").IndexOf(onlyPage, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            Console.WriteLine($"  == page {page.Name} ({items.Count})");

            foreach (var item in items)
            {
                Console.Write(SaveDump.Describe(item, showStats));
                seen++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{seen} items");
        return 0;
    }

    static int Sprite(string gameRoot, string invFile)
    {
        using var game = GameData.Open(gameRoot);
        var palette = Palette.Load(game);
        var loader = SpriteLoader.Install(game, palette);

        // --tint <InvTrans> <colour row>: drawn through a colour map, as a dye draws it.
        ColourMaps.Load(game);
        Tint? tint = Option("--tint") is { } asked && asked.Split(',') is [var t, var c]
            ? new Tint(int.Parse(t), int.Parse(c))
            : null;
        if (tint is not null)
            Console.WriteLine($"tint {tint}: map {(ColourMaps.For(tint) is null ? "missing" : "found")}");

        var sprite = loader.Load(invFile, tint);
        if (sprite is null)
        {
            Console.Error.WriteLine($"no graphic named '{invFile}' in any archive "
                                    + $"(looked for {GameData.ItemSpritePath(invFile)})");
            return 1;
        }

        int opaque = sprite.Bgra.Count(pixel => (pixel >> 24) != 0);
        Console.WriteLine($"{invFile}: {sprite.Width}x{sprite.Height}, "
                          + $"{opaque} opaque of {sprite.Bgra.Length} pixels");

        string? output = Option("--out");
        if (output is not null)
        {
            Png.Write(output, sprite.Width, sprite.Height, sprite.Bgra);
            Console.WriteLine($"wrote {output}");
        }
        return 0;
    }

    // An act's town from its DS1 and DT1 files (Core's TownScene): the map the tables name, its size, what is
    // placed on it, and - with --out - the picture: the whole map, or a window centred on --at's cell. With
    // --objects every placed thing is marked, an object in yellow and a townsperson in red, so the stash can be
    // told from where it stands.
    static int Town(string gameRoot, string act)
    {
        if (!int.TryParse(act, out int number) || number is < 1 or > 5)
            throw new ArgumentException("the act: 1 to 5");

        using var game = GameData.Open(gameRoot);
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (TownScene.Open(game, number) is not { } scene)
        {
            Console.Error.WriteLine($"no town for act {number}: the tables or the archives do not give one");
            return 1;
        }

        long opened = clock.ElapsedMilliseconds;
        var map = scene.Map;
        Console.WriteLine($"act {number}: {scene.MapFile}, version {map.Version}, {map.Width}x{map.Height} cells, "
                          + $"{map.Walls.Count} wall layers, {map.Floors.Count} floor layers, {map.Objects.Count} placed");
        foreach (var group in map.Objects.Where(each => each.Type == 2).GroupBy(each => each.Id).OrderBy(group => group.Key))
            Console.WriteLine($"  object {group.Key,3}: " + string.Join("  ", group.Select(each => $"cell {each.X / 5.0:0.#},{each.Y / 5.0:0.#}")));

        if (Option("--out") is not { } output)
            return 0;

        var (left, top, width, height) = scene.Bounds();
        if (Option("--at") is { } at && at.Split(',') is [var column, var row]
            && double.TryParse(column, System.Globalization.CultureInfo.InvariantCulture, out double x)
            && double.TryParse(row, System.Globalization.CultureInfo.InvariantCulture, out double y))
        {
            (width, height) = Option("--size") is { } size && size.Split(',') is [var w, var h] ? (int.Parse(w), int.Parse(h)) : (800, 600);
            var centre = TownScene.PlaceOfPoint(x * 5, y * 5);
            (left, top) = (centre.X - width / 2, centre.Y - height / 2);
        }

        // --backdrop: the card's own picture, as the opened character card shows it.
        if (arguments.Contains("--backdrop"))
        {
            clock.Restart();
            if (scene.Backdrop() is not { } backdrop)
            {
                Console.Error.WriteLine("the map places no stash: no backdrop");
                return 1;
            }

            // What the card pays for it, once an act: the files read, the picture drawn.
            Console.WriteLine($"read in {opened} ms, drawn in {clock.ElapsedMilliseconds} ms; "
                              + $"{(GC.GetTotalAllocatedBytes(precise: true) - before) / 1048576.0:0.#} MB allocated on the way");
            Png.Write(output, backdrop.Width, backdrop.Height, backdrop.Bgra.Select(pixel => pixel >> 24 == 0 ? 0xFF000000 : pixel).ToArray());
            Console.WriteLine($"wrote {output} ({backdrop.Width}x{backdrop.Height}, the stash at cell {scene.Stash})");
            return 0;
        }

        var picture = scene.Render(left, top, width, height, roofs: !arguments.Contains("--no-roofs"));
        if (arguments.Contains("--objects"))
        {
            foreach (var placed in map.Objects)
            {
                var (px, py) = TownScene.PlaceOfPoint(placed.X, placed.Y);
                uint colour = placed.Type == 2 ? 0xFFFFFF00 : 0xFFFF3030;
                for (int dy = -4; dy <= 4; dy++)
                {
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int tx = px - left + dx, ty = py - top + dy;
                        if (tx >= 0 && ty >= 0 && tx < width && ty < height)
                            picture.Bgra[ty * width + tx] = colour;
                    }
                }
            }
        }

        // Clear where no tile reached: on black, as the game's screen is.
        var onBlack = picture.Bgra.Select(pixel => pixel >> 24 == 0 ? 0xFF000000 : pixel).ToArray();
        Png.Write(output, width, height, onBlack);
        Console.WriteLine($"wrote {output} ({width}x{height}, window at {left},{top})");
        return 0;
    }

    // One direction of a character or monster animation: each frame's place against the anchor, and a
    // strip of them all on one baseline - the proof that the DCC decoder reads what the game draws.
    static int DccCommand(string gameRoot, string archivePath)
    {
        using var game = GameData.Open(gameRoot);
        var palette = Palette.Load(game);
        if (game.Read(archivePath) is not { } found)
        {
            Console.Error.WriteLine($"'{archivePath}' is not in any loaded archive");
            return 1;
        }

        int direction = int.TryParse(Option("--dir"), out int asked) ? asked : 0;
        Console.WriteLine($"{archivePath} from {Path.GetFileName(found.Archive)}: {Dcc.Directions(found.Data)} directions");
        var frames = Dcc.Read(found.Data, direction);
        foreach (var (frame, n) in frames.Select((frame, n) => (frame, n)))
            Console.WriteLine($"  frame {n,2}: {frame.Width}x{frame.Height} at {frame.Left},{frame.Top}, "
                              + $"{frame.Indices.Count(index => index != 0)} drawn");

        if (Option("--out") is not { } output)
            return 0;

        var drawn = frames.Where(frame => frame.Width > 0).ToList();
        int left = drawn.Min(f => f.Left), top = drawn.Min(f => f.Top);
        int cellW = drawn.Max(f => f.Left + f.Width) - left + 4, cellH = drawn.Max(f => f.Top + f.Height) - top;
        var sheet = new uint[cellW * frames.Count * cellH];
        for (int n = 0; n < frames.Count; n++)
        {
            var frame = frames[n];
            for (int y = 0; y < frame.Height; y++)
                for (int x = 0; x < frame.Width; x++)
                {
                    byte index = frame.Indices[y * frame.Width + x];
                    if (index != 0)
                        sheet[(frame.Top - top + y) * cellW * frames.Count + n * cellW + frame.Left - left + x] = palette[index];
                }
        }
        Png.Write(output, cellW * frames.Count, cellH, sheet);
        Console.WriteLine($"wrote {output}");
        return 0;
    }

    // A character drawn in its gear, the way the select screen draws it (CharacterFigure): the worn
    // items and the save's appearance bytes, the COF's layers, every DCC through its tint. --dir all
    // writes the first frame of every direction side by side; a number writes that direction's frames.
    static int FigureCommand(string gameRoot, string savePath)
    {
        using var installed = Season.Open(gameRoot, Tables());
        var game = installed.Game;
        var palette = Palette.Load(game);
        ColourMaps.Load(game);
        var (save, _) = SaveVerifier.Load(savePath);

        // --ethereal: every worn item drawn as if ethereal, in memory only - to see the effect on any character.
        if (arguments.Contains("--ethereal"))
            foreach (var worn in save.PlayerItemList.Items.Where(item => item.Mode == ItemMode.Equipped))
                worn.IsEthereal = true;
        var look = CharacterFigure.LookOf(save, Option("--mode") ?? CharacterFigure.TownNeutral);
        Console.WriteLine($"{look.Token} {look.Mode} {look.WeaponClass}: "
                          + string.Join(" ", look.Parts.OrderBy(p => Array.IndexOf(CharacterFigure.Parts, p.Key)).Select(p => $"{p.Key}={p.Value}"))
                          + " | tints " + string.Join(" ", look.Tints.Select(t => $"{t.Key}={t.Value.Table}/{t.Value.Colour}")));

        string asked = Option("--dir") ?? $"{FigureLoader.Facing}";
        var directions = asked == "all" ? Enumerable.Range(0, 16).ToList() : [int.Parse(asked)];
        var figures = directions.Select(d => CharacterFigure.Compose(game, palette, look, d)).ToList();
        if (figures.Any(f => f is null))
        {
            Console.Error.WriteLine("no pose to draw (no COF for the class, pose and weapon class)");
            return 1;
        }
        var first = figures[0]!;
        Console.WriteLine($"{first.Frames.Count} frames at {first.FramesPerSecond:0.#} a second, {first.Width}x{first.Height}");
        foreach (string gone in first.Missing)
            Console.WriteLine($"  missing {gone}");
        // --order: the COF's drawing order, back to front, for the first frame of the direction.
        if (arguments.Contains("--order") && game.Read(Cof.PathFor(look.Token, look.Mode, look.WeaponClass)) is { } cofFile)
        {
            var cof = Cof.Read(cofFile.Data);
            foreach (int d in directions)
                Console.WriteLine($"  order dir {d}: " + string.Join(" ", cof.Order(d % cof.Directions, 0).Select(c => CharacterFigure.Parts[c])));
        }

        if (Option("--out") is not { } output)
            return 0;

        // One strip: every direction's first frame, or one direction's every frame, on a shared baseline.
        var cells = asked == "all"
            ? figures.Select(f => (Figure: f!, Frame: f!.Frames[0])).ToList()
            : first.Frames.Select(frame => (Figure: first, Frame: frame)).ToList();
        int above = cells.Max(c => c.Figure.AnchorY), below = cells.Max(c => c.Figure.Height - c.Figure.AnchorY);
        int leftOf = cells.Max(c => c.Figure.AnchorX), rightOf = cells.Max(c => c.Figure.Width - c.Figure.AnchorX);
        int cellW = leftOf + rightOf + 4, height = above + below, stride = cellW * cells.Count;
        var sheet = new uint[stride * height];
        for (int n = 0; n < cells.Count; n++)
        {
            var (figure, frame) = cells[n];
            int ox = n * cellW + leftOf - figure.AnchorX, oy = above - figure.AnchorY;
            for (int y = 0; y < figure.Height; y++)
                for (int x = 0; x < figure.Width; x++)
                    if (frame.Bgra[y * figure.Width + x] is var pixel and not 0)
                        sheet[(oy + y) * stride + ox + x] = pixel;
        }
        // --smooth <n>: Scale2x n times, as the pages draw it before scaling it down to their size.
        if (int.TryParse(Option("--smooth"), out int smooth) && smooth > 0)
        {
            sheet = PixelArt.Scale2x(sheet, stride, height, smooth);
            stride <<= smooth;
            height <<= smooth;
        }
        Png.Write(output, stride, height, sheet);
        Console.WriteLine($"wrote {output}");
        return 0;
    }

    // Every range the app would put beside a property, and how many sources went into each.
    // --prefix and --suffix add an affix to the item in memory, which is how a stat granted
    // twice is checked on a character that happens to carry no such item.
    static int Ranges(string gameRoot, string path)
    {
        string itemText = Option("--item") ?? throw new ArgumentException("ranges needs --item <n>");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        var item = (save.PlayerItemList?.Items ?? [])[int.Parse(itemText)];

        void Extra(ushort[] slots, string? id)
        {
            if (id is null || !ushort.TryParse(id, out ushort which))
                return;

            int free = Array.IndexOf(slots, (ushort)0);
            slots[free >= 0 ? free : slots.Length - 1] = which;
        }

        Extra(item.MagicPrefixIds, Option("--prefix"));
        Extra(item.MagicSuffixIds, Option("--suffix"));

        Console.WriteLine($"item #{itemText} ({item.Code?.Trim()}, {item.Quality})"
            + $" prefixes [{string.Join(",", item.MagicPrefixIds)}]"
            + $" suffixes [{string.Join(",", item.MagicSuffixIds)}]");

        foreach (var (stat, range) in AffixRanges.For(item).OrderBy(each => each.Key))
        {
            // By the key, and across every list. Matching the bare name against a keyed range
            // reported "held -" for a property plainly on the item - Witchwild String's
            // item_skillonhit#442 - and reading only the first list would do the same to
            // everything a runeword grants.
            string held = ItemStats.FindKeyed(item, stat)?.Value.ToString() ?? "-";

            Console.WriteLine($"  {stat,-28} held {held,-5} range [{range.Min} - {range.Max}]"
                + (range.Sources > 1 ? $"  <- {range.Sources} sources, cumulated" : ""));
        }

        return 0;
    }

    static int WornFields(string gameRoot, IReadOnlyList<string> paths)
    {
        // Read-only: every worn item's placement fields as the game wrote them - what equipping by
        // the editor must write.
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        foreach (string path in paths)
        {
            var (save, _) = SaveVerifier.Load(path);
            foreach (var (item, hired) in (save.PlayerItemList?.Items ?? []).Select(each => (each, false))
                         .Concat((save.MercenaryItemList?.ItemList?.Items ?? []).Select(each => (each, true)))
                         .Where(entry => entry.Item1.Mode == ItemMode.Equipped))
            {
                Console.WriteLine($"{Path.GetFileName(path),-20} {(hired ? "merc" : "own "),-5} {item.Location,-12} ({(int)item.Location,2})  x {item.X,2} y {item.Y}  page {item.Page}  {ItemPresenter.Describe(item).Name}");
            }
        }

        return 0;
    }

    static int ReqsCommand(string gameRoot, string path)
    {
        // Every worn item and charm: what it asks, what it gives, and whether the character gets it -
        // both weapon sets, with the gear helping and without.
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (save, _) = SaveVerifier.Load(path);
        string classCode = CharacterClasses.CodeOf(save.ClassId) ?? "?";
        int strength = AttributeScale.Display("strength", save.Attributes.Stats.GetValueOrDefault("strength"));
        int dexterity = AttributeScale.Display("dexterity", save.Attributes.Stats.GetValueOrDefault("dexterity"));
        Console.WriteLine($"{save.Name}: {classCode} level {save.Level}, base strength {strength}, dexterity {dexterity}, the weapons in hand under tab {PD2SPCompanion.Core.Characters.WeaponSets.TabInHand(save)}");
        Console.WriteLine();

        foreach (int set in new[] { 0, 1 })
        {
            foreach (bool gear in new[] { true, false })
            {
                Console.WriteLine($"-- weapon set {set + 1}, {(gear ? "gear counted" : "base only")}");
                foreach (var judged in Requirements.Judge(save, set, strength, dexterity, gear))
                {
                    var item = judged.Item;
                    var bonus = Requirements.BonusOf(item, save.Level);
                    string where = item.Mode == ItemMode.Equipped ? item.Location.ToString() : "charm";
                    string name = ItemPresenter.Describe(item).Name;
                    Console.WriteLine($"  {(judged.Met ? "ok " : "RED")} {where,-10} {name,-34} lvl {judged.Need.Level,3} str {judged.Need.Strength,3} dex {judged.Need.Dexterity,3}"
                                      + $"   gives str {bonus.Strength,3} dex {bonus.Dexterity,3}"
                                      + $"   helped str {judged.Helped.Strength,3} dex {judged.Helped.Dexterity,3}"
                                      + (judged.Short is { } why ? $"   <- {why}" : "")
                                      + (judged.Need.Unverified.Count > 0 ? $"   [unverified: {string.Join("; ", judged.Need.Unverified)}]" : ""));
                }
            }
        }

        // What a crafted or rare item's level is made of: its base and each affix.
        foreach (var item in (save.PlayerItemList?.Items ?? []).Where(item => item.Mode == ItemMode.Equipped
                 && item.Quality is ItemQuality.Craft or ItemQuality.Rare or ItemQuality.Magic))
        {
            Console.WriteLine();
            Console.WriteLine($"{ItemPresenter.Describe(item).Name} ({item.Quality}, {item.Code?.Trim()}, ilvl {item.ItemLevel}): base levelreq {D2SLib.Core.TXT.ItemsTXT.GetByCode(item.Code?.Trim() ?? "")?["levelreq"]?.Value?.Trim()}");
            foreach (var pair in MagicAffixes.PairsOn(item))
                foreach (var affix in new[] { pair.Prefix, pair.Suffix }.OfType<MagicAffixes.AffixOption>())
                    Console.WriteLine($"   {(affix.IsPrefix ? "prefix" : "suffix")} {affix.Id,4} {affix.Name,-22} levelreq {affix.RequiredLevel,3}"
                                      + (affix.ForClass.Length > 0 ? $"  ({affix.ForClass} {affix.ClassRequiredLevel})" : ""));
            foreach (var socketed in item.SocketedItems)
                Console.WriteLine($"   socketed {socketed.Code?.Trim()} level {Requirements.Of(socketed, classCode).Level}");
        }

        // What the character comes to with the gear in hand, set bonuses in: the figure the game's
        // character screen shows, to hold the count against.
        foreach (int set in new[] { 0, 1 })
        {
            var met = Requirements.Judge(save, set, strength, dexterity).Where(judged => judged.Met).Select(judged => judged.Item).ToList();
            var total = Requirements.TotalOf(met, save.Level);
            var plain = met.Aggregate(default(Requirements.Bonus), (sum, item) => sum + Requirements.BonusOf(item, save.Level));
            Console.WriteLine($"weapon set {set + 1} in hand: strength {strength + total.Strength}, dexterity {dexterity + total.Dexterity}"
                              + $"  (set bonuses {total.Strength - plain.Strength} / {total.Dexterity - plain.Dexterity})");
        }

        // The mercenary: its level from its experience, its stats at that level, and its gear judged.
        var profile = MercenaryProfile.Read(save, game);
        if (profile.IsHired && save.Mercenary is { } hired)
        {
            var stats = Hirelings.StatsOf(hired.TypeId, profile.Level, save.Status.IsExpansion);
            var worn = Requirements.JudgeMercenary(save, profile.Level);
            var total = Requirements.TotalOf(worn.Where(judged => judged.Met).Select(judged => judged.Item), profile.Level);
            Console.WriteLine();
            // Its name, by the type's own list (MercenaryEdit.NamesOf: NameId counted from NameFirst - unverified in game).
            var names = MercenaryEdit.NamesOf(save, game);
            Console.WriteLine($"mercenary name: {(MercenaryEdit.NameOf(save, game) is { Length: > 0 } named ? named : "?")} (name id {hired.NameId} of {names.Count}; {MercenaryEdit.VariantsOf(save, game).Count} variants of its type)");
            Console.WriteLine($"mercenary: {profile.Title} ({profile.Detail}), type {hired.TypeId}, weapons {string.Join("/", Hirelings.WeaponsOf(hired.TypeId, save.Status.IsExpansion))}");
            Console.WriteLine($"  own strength {stats?.Strength}, dexterity {stats?.Dexterity}; with gear {stats?.Strength + total.Strength}, {stats?.Dexterity + total.Dexterity}");
            foreach (var judged in worn)
                Console.WriteLine($"  {(judged.Met ? "ok " : "RED")} {judged.Item.Location,-10} {ItemPresenter.Describe(judged.Item).Name,-30} lvl {judged.Need.Level,3} str {judged.Need.Strength,3} dex {judged.Need.Dexterity,3}{(judged.Short is { } why ? "  <- " + why : "")}");
        }

        var least = Requirements.Least(save, countGear: true);
        var leastBare = Requirements.Least(save, countGear: false);
        Console.WriteLine();
        Console.WriteLine($"Refit target, gear counted: strength {least.Strength}, dexterity {least.Dexterity}");
        Console.WriteLine($"Refit target, base only:      strength {leastBare.Strength}, dexterity {leastBare.Dexterity}");
        return 0;
    }

    static int RunewordsCommand(string gameRoot, string path)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        // What the rune wizard counts: the character's stored items - inventory, cube, its own
        // stash - and, given a shared stash, its pages and its materials tab. Socketed runes are
        // inside their parents, so never among these.
        var (save, _) = SaveVerifier.Load(path);
        var items = (save.PlayerItemList?.Items ?? []).Where(item => item.Mode == ItemMode.Stored).ToList();

        Func<int, int>? tab = null;
        if (Option("--stash") is { } stashPath)
        {
            var stash = SharedStash.Read(stashPath);
            items.AddRange(stash.Items.Items ?? []);
            tab = stash.MaterialCount;
        }

        var stock = Runewords.Stock(items, tab);
        Console.WriteLine("runes: " + string.Join(" ", Enumerable.Range(1, 33).Where(n => stock[n] > 0)
            .Select(n => $"{ItemPresenter.BaseName($"r{n:00}").Replace(" Rune", "")}:{stock[n]}")));

        var all = Runewords.All;
        Console.WriteLine($"{all.Count} runewords; by category "
            + string.Join(", ", Runewords.Categories.Select(c => $"{c} {all.Count(w => Runewords.CategoriesOf(w).Contains(c))}")));

        var makeable = all.Where(w => Runewords.CanMake(w, stock))
            .OrderByDescending(w => w.Runes.Max(Runewords.RuneNumber))
            .ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Console.WriteLine($"can make {makeable.Count}:");
        foreach (var word in makeable)
            Console.WriteLine($"  {word.Name,-26} {string.Join(" + ", word.Runes.Select(r => ItemPresenter.BaseName(r).Replace(" Rune", "")))}   [{string.Join(", ", Runewords.CategoriesOf(word))}]");
        return 0;
    }

    /// <summary>Each key's text and its position in the string table that supplies it.</summary>
    static int StringPositions(string gameRoot, List<string> keys)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;
        foreach (string table in new[] { "string", "expansionstring", "patchstring" })
        {
            var hit = game.Read($@"data\local\lng\eng\{table}.tbl");
            Console.WriteLine($"{table}.tbl: {(hit is null ? "absent" : BitConverter.ToUInt16(hit.Value.Data, 2) + " strings")}");
        }
        foreach (string key in keys)
            Console.WriteLine($"{key}\t{StringTable.Current?.PositionOf(key)?.ToString() ?? "-"}\t{StringTable.Current?.Get(key)}");
        return 0;
    }
}
