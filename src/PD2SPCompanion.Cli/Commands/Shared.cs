using System.Text;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Testbed;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Cli;

// Helpers more than one command uses.
internal static partial class Cli
{
    /// <summary>
    /// The reroll-all, proven legal: every magic, rare and crafted item in the given saves and saved
    /// listings rerolled many times with MagicAffixes.RollFresh, each result put through the
    /// legitimacy check. Counts the rolls the check says more about than it said of the item.
    /// </summary>
    static Item WithoutAffixes(Item item)
    {
        var bare = ItemBlob.Copy(item, 10);
        Array.Clear(bare.MagicPrefixIds!);
        Array.Clear(bare.MagicSuffixIds!);
        return bare;
    }

    /// <summary>
    /// The item stamp for what a command gives a character, as the editor gives one (<see cref="NewCharacter.StampForNew"/>):
    /// this season's wherever the file may hold it, never the first item's legacy one.
    /// </summary>
    static string EraOf(D2S save) =>
        NewCharacter.StampForNew(save)
        ?? throw new InvalidOperationException("No item on the character to take the item era from, and this season's item stamp is unknown.");

    // A cell that may hold a comma, a quote or a line of its own.
    static string Quoted(string cell) =>
        cell.Contains(',') || cell.Contains('"') || cell.Contains('\n')
            ? $"\"{cell.Replace("\"", "\"\"")}\""
            : cell;

    /// <summary>
    /// Makes every material the given saves hold, the way the editor would, and compares it with
    /// the game's own - id and position wiped, byte for byte. The items in those saves came out of
    /// the tab in game, which makes them the answer.
    /// </summary>
    static int CheckMaterials(string[] saves)
    {
        var seen = new Dictionary<string, (bool Same, string Detail)>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in saves)
        {
            var (save, _) = SaveVerifier.Load(file);
            int bits = D2SLib.Globals.stat_id_bits;

            foreach (var item in save.PlayerItemList?.Items ?? [])
            {
                string code = item.Code?.Trim() ?? "";
                // The stack, as Season 13's tab hands it out, or the plain item, as 14's does.
                if (Materials.Takes(item) is not { } taken
                    || !(code.Equals(Materials.CodeAt(taken.Slot), StringComparison.OrdinalIgnoreCase)
                         || code.Equals(Materials.Plain(taken.Slot), StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (seen.TryGetValue(code, out var already) && !already.Same)
                    continue;

                byte[] Blob(Item it)
                {
                    var copy = ItemBlob.Restore(Item.Write(it, ItemBlob.Version), bits);
                    ItemBlob.Normalise(copy);
                    return ItemBlob.TryCapture(copy, bits, out var blob, out _, out string? why)
                        ? blob : throw new InvalidOperationException(why);
                }

                Item forged;
                byte[] ours, theirs;
                try
                {
                    forged = ItemForge.Material(code, taken.Quantity, item.Version);
                    // Flag 13 marks an item the game has just handed over and clears once it is
                    // moved - some of these carry it and some do not, by what was done with them
                    // after. It is the game's bookkeeping, not what the item is, so the answer is
                    // compared without it.
                    var answer = ItemBlob.Copy(item, bits);
                    answer.IsNew = false;
                    theirs = Blob(answer);
                    ours = Blob(forged);
                }
                catch (Exception problem)
                {
                    // Said, never skipped: a material the editor cannot make is the finding.
                    seen[code] = (false, $" - could not be made or compared: {problem.GetType().Name}: {problem.Message}"
                                         + $" at {problem.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                    continue;
                }
                bool same = ours.AsSpan().SequenceEqual(theirs);

                string Fields(Item it) =>
                    $"compact {it.IsCompact} ver {it.Version} ilvl {it.ItemLevel} q {it.Quality} qty {it.Quantity} "
                    + $"gfx {it.HasMultipleGraphics}/{it.GraphicId} lists {it.StatLists.Count} stats {it.StatLists.Sum(l => l.Stats.Count)} "
                    + $"flags [{string.Join(",", Enumerable.Range(0, it.Flags.Length).Where(i => it.Flags[i]))}] {it.RealmData?.Length}";

                seen[code] = (same, same ? "" : $"\n      game: {Fields(item)}\n      ours: {Fields(forged)}\n      bytes {theirs.Length} vs {ours.Length}");
            }
        }

        int identical = seen.Values.Count(v => v.Same);
        Console.WriteLine($"{seen.Count} material codes found, {identical} made byte-identical to the game's");
        foreach (var (code, result) in seen.Where(pair => !pair.Value.Same))
            Console.WriteLine($"  DIFFERS {code}{result.Detail}");
        foreach (string missing in Materials.Codes.Where(code => !seen.ContainsKey(code)))
            Console.WriteLine($"  not in these saves: {missing}");
        return identical == seen.Count ? 0 : 1;
    }

    /// <summary>
    /// Whether a pack page is one its author made by hand, which the pack's own items leave out.
    /// Named rather than guessed: the
    /// PlugY page names are exact where the README's page bands are not, and page 19 is called
    /// "Edited Items" by the author himself.
    /// <para>
    /// This is a policy, not a fact about the game.
    /// </para>
    /// </summary>
    static bool MadeByHand(string page) =>
        page.StartsWith("Testing ", StringComparison.OrdinalIgnoreCase)
        || page.Equals("Edited Items", StringComparison.OrdinalIgnoreCase)
        || page.EndsWith(" Recipe Ref", StringComparison.OrdinalIgnoreCase)
        || page.Equals("Socket/Repair Ref", StringComparison.OrdinalIgnoreCase)
        || page.Equals("Reg Necromancer", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Carries a library of stashes into the installed season and writes them out.
    /// <para>
    /// The source file is the template. Its magic bytes, its page flags and its page names are
    /// kept exactly as the game wrote them, and only the items inside change - which is a much
    /// smaller thing to get right than building a stash from nothing.
    /// </para>
    /// <para>
    /// Two kinds of item are left behind by default, both because they would read as broken in
    /// game rather than merely different. A <b>set</b> item drifts in its properties and can be
    /// missing ones the current season grants - a migrated Milabrega's Robe has no
    /// <c>ac% 60-100</c> where a found one does. And an item the check calls <b>impossible</b>
    /// here carries an affix this season removed or re-scoped, which no re-encoding can mend.
    /// <c>--keep-sets</c> and <c>--keep-broken</c> carry them anyway.
    /// </para>
    /// </summary>
    static int Carry(string gameRoot, string? from, string? corpus, string? single, string into)
    {
        if (from is null)
            throw new ArgumentException("migratefiles needs --from <dir of the source season's .txt>");

        var files = (corpus is not null ? SavesUnder(corpus) : [single!]).Where(IsStash).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine("nothing to carry: --out writes stashes, and none were found");
            return 1;
        }

        // Where each is written: its place under the corpus kept, so two of one name in two folders stay two. Asked of all
        // of them before anything is read or written - an --out naming the source, or a run before, is refused outright.
        string Target(string file) => Path.Combine(into, corpus is not null ? Path.GetRelativePath(corpus, file) : Path.GetFileName(file));
        if (files.FirstOrDefault(file => File.Exists(Target(file))) is { } taken)
            throw new ArgumentException($"{Target(taken)} exists; nothing is written over it");

        bool keepSets = arguments.Contains("--keep-sets");
        bool keepBroken = arguments.Contains("--keep-broken");

        // Carry each value at the same fraction of its range rather than as the same number:
        // a 5 out of 4-6 becomes a 10 when the range grows to 8-12. Opt-in,
        // so the plain re-encode stays measurable beside it.
        bool rescale = arguments.Contains("--rescale");

        // Every stash read under the season it was made in, and held, before the tables change.
        var carried = new List<(string File, D2I Stash, int Bits)>();

        // What each item's own sources could roll **in the season that made it**, kept by item.
        // It has to be taken here: the old tables are closed as soon as this block ends, and
        // after that there is nothing left to ask. Only gathered when it will be used, since it
        // is one small dictionary per item across the whole corpus.
        var before = new Dictionary<Item, IReadOnlyDictionary<string, RollRange>>(
            ReferenceEqualityComparer.Instance);

        using (var source = Season.Open(gameRoot, from))
        {

            foreach (var file in files)
            {
                try
                {
                    var (stash, bits) = SaveVerifier.LoadStash(file);
                    carried.Add((file, stash, bits));

                    if (rescale)
                    {
                        foreach (var item in (stash.PageList ?? [])
                                     .SelectMany(page => page.PageItems?.Items ?? []))
                        {
                            try { before[item] = AffixRanges.For(item); }
                            catch { /* an item whose sources will not read simply has no before */ }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"refused  {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        // ---- the installed season from here ---------------------------------------------------
        using var target = Season.Open(gameRoot);

        string stamp = Option("--stamp") ?? SeasonReading.CurrentStamp ?? StampOfThisSeason(gameRoot)
            ?? throw new ArgumentException(
                "cannot tell what item version this season stamps. Pass --stamp <n>, or keep a "
                + "played character in the game's Save folder to read it from.");

        Directory.CreateDirectory(into);
        Console.WriteLine($"carrying {carried.Count} stash(es) to v{stamp}"
                          + $"{(keepSets ? "" : ", leaving sets behind")}"
                          + $"{(keepBroken ? "" : ", leaving behind what this season cannot hold")}");
        Console.WriteLine();

        int kept = 0, madeByHand = 0, wereSets = 0, wereBroken = 0, adjusted = 0, refused = 0;
        int rescaled = 0, mismatched = 0;

        foreach (var (file, stash, bits) in carried)
        {
            string name = Path.GetFileName(file);
            foreach (var page in stash.PageList ?? [])
            {
                var holding = page.PageItems?.Items;
                if (holding is null) continue;

                if (MadeByHand(page.Name ?? ""))
                {
                    madeByHand += holding.Count;
                    holding.Clear();
                    page.PageItems!.Count = 0;
                    continue;
                }

                var keeping = new List<Item>();

                foreach (var item in holding)
                {
                    try
                    {
                        // Every item is re-stamped, so counting that as an adjustment would say
                        // "all of them" and mean nothing. Only a property that had to move is
                        // worth reporting.
                        if (Migration.Fit(item, stampAs: stamp)
                                .Any(change => change.Stat != "version"))
                        {
                            adjusted++;
                        }
                    }
                    catch
                    {
                        refused++;
                        continue;
                    }

                    // Now the values themselves, if asked. The re-encode above moved each
                    // number into a field this season can hold; this moves it to the place in
                    // this season's range that it held in the old one.
                    if (rescale && !item.IsCompact)
                    {
                        rescaled += SeasonFit.Rescale(item, before.GetValueOrDefault(item));
                    }

                    // Compact items are stamped like everything else, and only then set aside.
                    // A rune has no properties to refit, but it carries the era stamp just the
                    // same, and a save holds exactly one era. This check sat above the stamping
                    // at first, so 601 runes and potions stayed at v101 among v103 gear - the
                    // precise fault that had already made the game refuse a character once.
                    if (item.IsCompact)
                    {
                        keeping.Add(item);
                        continue;
                    }

                    if (!keepSets && item.Quality == ItemQuality.Set) { wereSets++; continue; }

                    if (!keepBroken
                        && Legitimacy.Check(item).Any(each => each.Certainty is Certainty.Impossible))
                    {
                        wereBroken++;
                        continue;
                    }

                    keeping.Add(item);
                }

                kept += keeping.Count;
                holding.Clear();
                holding.AddRange(keeping);
                page.PageItems!.Count = (ushort)keeping.Count;
            }

            // ---- write it, then read it back and check every item survived ---------------------
            string path = Target(file);
            string extension = Path.GetExtension(name);

            D2SLib.Globals.ResetForFile(pd2Character: false, statIdBits: 10);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SaveWriter.WriteSafely(path, D2SLib.Core.WriteD2I(stash, 0x60, extension));

            try
            {
                var (back, readBits) = SaveVerifier.LoadStash(path);
                int here = (stash.PageList ?? []).Sum(p => p.PageItems?.Items?.Count ?? 0);
                int there = (back.PageList ?? []).Sum(p => p.PageItems?.Items?.Count ?? 0);

                Console.WriteLine($"  {name,-34} {here,5} items, read back at {readBits} bits as "
                                  + $"{there}   {(here == there ? "ok" : "MISMATCH")}");
                if (here != there)
                    mismatched++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {name,-34} WRITTEN BUT WILL NOT READ BACK: {ex.Message}");
                return 1;
            }
        }

        Console.WriteLine($"""

            {kept} items carried into {into}
              {madeByHand,6} left on the pages the author made by hand
              {wereSets,6} sets left behind
              {wereBroken,6} left behind because this season cannot hold them
              {adjusted,6} carried with a property this season had to move
              {refused,6} refused outright
            """);

        if (rescale)
        {
            Console.WriteLine($"  {rescaled} value(s) moved to the same place in a changed range");
        }

        return mismatched == 0 ? 0 : 1;
    }

    /// <summary>
    /// The item version stamp this season writes, read off a character the game itself saved.
    /// <para>
    /// A save holds one era of items and nothing in the tables says which - the only honest
    /// source is a save the game wrote. Every item in a live S13 character reads 103.
    /// </para>
    /// </summary>
    static string? StampOfThisSeason(string gameRoot)
    {
        string folder = Path.Combine(gameRoot, "Save");
        if (!Directory.Exists(folder))
            return null;

        foreach (var path in Directory.EnumerateFiles(folder, "*.d2s"))
        {
            try
            {
                var (save, _) = SaveVerifier.Load(path);
                if ((save.PlayerItemList?.Items ?? []).FirstOrDefault() is { Version.Length: > 0 } item)
                    return item.Version;
            }
            catch
            {
                // a character from another season says nothing about this one
            }
        }

        return null;
    }

    static string Line(Exception problem) => problem.Message.Split('\n')[0].Trim();

    /// <summary>
    /// The folder verbs of <c>vault</c>, run in a fixed order so one command line can build a case
    /// and look at it. Returns why a verb was refused, or null.
    /// </summary>
    static string? Folders(Vault vault, Func<string, string?> option, Func<string, bool> flag)
    {
        // "A/B" to a folder id, walking from the root. "/" or "" is the root itself.
        long? Find(string path)
        {
            long? at = null;
            foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                at = vault.Folders().FirstOrDefault(each => each.ParentId == at
                         && string.Equals(each.Name, part, StringComparison.OrdinalIgnoreCase))?.Id
                     ?? throw new InvalidOperationException($"no folder {path}");
            }
            return at;
        }

        try
        {
            if (option("--mkdir") is { } make)
            {
                long? at = null;
                foreach (string part in make.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    at = vault.Folders().FirstOrDefault(each => each.ParentId == at
                             && string.Equals(each.Name, part, StringComparison.OrdinalIgnoreCase))?.Id
                         ?? vault.CreateFolder(at, part);
                }
                Console.WriteLine($"  made {make}");
            }

            if (option("--rename") is { } renaming)
            {
                string name = option("--as") ?? throw new InvalidOperationException("--rename needs --as <name>");
                vault.RenameFolder(Find(renaming) ?? throw new InvalidOperationException("the root has no name"), name);
                Console.WriteLine($"  renamed {renaming} to {name}");
            }

            if (option("--mvdir") is { } moving)
            {
                string to = option("--to") ?? throw new InvalidOperationException("--mvdir needs --to <folder|/>");
                string landed = vault.MoveFolder(Find(moving) ?? throw new InvalidOperationException("the root cannot move"), Find(to));
                Console.WriteLine($"  moved {moving} into {to} as {landed}");
            }

            if (option("--put") is { } keys)
            {
                string to = option("--to") ?? throw new InvalidOperationException("--put needs --to <folder|/>");
                int moved = vault.MoveItems(keys.Split(',').Select(long.Parse), Find(to));
                Console.WriteLine($"  put {moved} item(s) in {to}");
            }

            if (option("--rmdir") is { } removing)
            {
                bool all = flag("--all");
                var done = vault.DeleteFolder(Find(removing) ?? throw new InvalidOperationException("the root cannot be deleted"), all);
                Console.WriteLine(all
                    ? $"  deleted {removing}: {done.Folders} folder(s), {done.Items} item(s)"
                    : $"  deleted {removing}: {done.Items} item(s) and {done.Folders} folder(s) to the root");
            }
        }
        catch (InvalidOperationException problem)
        {
            return problem.Message;
        }

        if (flag("--tree"))
        {
            var folders = vault.Folders();
            var counts = vault.ItemsPerFolder();

            void Show(long? parent, string indent)
            {
                foreach (var each in folders.Where(f => f.ParentId == parent))
                {
                    Console.WriteLine($"  {indent}{each.Name}/  ({counts.GetValueOrDefault(each.Id)})  #{each.Id}");
                    Show(each.Id, indent + "  ");
                }
            }

            Console.WriteLine($"  /  ({vault.Count(null)})");
            Show(null, "  ");
        }

        return null;
    }

    // Every save and stash under a folder, newest format or oldest - one list so a corpus walk
    // does not silently skip the stashes, which is where most of an item library lives.
    static List<string> SavesUnder(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file).ToLowerInvariant()
                       is ".d2s" or ".sss" or ".d2x" or ".stash")
            .Order()];

    static bool IsStash(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".sss" or ".d2x" or ".stash";

    /// <summary>
    /// PD2's own shared stash, which is a different file from a PlugY one and needs a different
    /// reader - <see cref="SharedStash"/> rather than <c>SaveVerifier.LoadStash</c>.
    /// <para>
    /// Routed here, and collected by the corpus walk: a command that fell through to the character
    /// reader would fail inside <c>D2S.Read</c> and report <c>pd2_shared.stash</c> as an
    /// old-season file it is not. This is the file a player's own current-season loot lands
    /// in, which makes it the one corpus that needs no migrating.
    /// </para>
    /// </summary>
    static bool IsSharedStash(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".stash";

    // Where each item sits, so a finding can name the stash page it came from.
    static List<(string Where, Item Item)> Contents(string path)
    {
        var held = new List<(string, Item)>();

        if (!IsStash(path))
        {
            // Any layout: an item pack's saves, read under the pack's own tables (--tables).
            var (save, _) = SaveVerifier.LoadAnyLayout(path);
            foreach (var item in save.PlayerItemList?.Items ?? [])
                held.Add(("", item));

            // The mercenary's gear is a list of its own, and was being left out entirely - so
            // the check said nothing about six items a character is wearing by proxy. The one
            // that started this was a mercenary's circlet.
            foreach (var item in save.MercenaryItemList?.ItemList?.Items ?? [])
                held.Add(("mercenary", item));

            return held;
        }

        if (IsSharedStash(path))
        {
            // PD2's shared stash keeps no page list - the page is a field on the item, so the
            // items are grouped rather than walked. See SharedStash.PageOf.
            var shared = SharedStash.Read(path);
            foreach (var item in shared.Items.Items ?? [])
                held.Add(($"page {SharedStash.PageOf(item)?.ToString() ?? "?"}", item));

            return held;
        }

        var (stash, _) = SaveVerifier.LoadStash(path);
        foreach (var page in stash.PageList ?? [])
            foreach (var item in page.PageItems?.Items ?? [])
                held.Add((page.Name ?? "", item));

        return held;
    }

    static Dictionary<string, int> Index(TxtTable table)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int row = 0; row < table.Rows.Count; row++)
        {
            string stat = table.Value(row, "Stat");
            if (!string.IsNullOrWhiteSpace(stat))
                map.TryAdd(stat, row);
        }
        return map;
    }
}
