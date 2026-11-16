using System.Text;
using PD2SPCompanion.Core.Graphics;
using PD2SPCompanion.Core.Testbed;
using D2SLib.Model.Save;

namespace PD2SPCompanion.Cli;

// The item vault.
internal static partial class Cli
{
    // Every unique and set piece forged, and every market item in the given folders, taken up a
    // tier at a time to elite with BaseUpgrades: an upgrade must not make the legitimacy check say
    // more than it did, and the item must read back the same through its own blob.
    // The vault's side of a rename, on a copy of a vault file: moves the rows kept under one
    // character's name to another, and says how many.
    static int VaultRenameCommand(string gameRoot, string? path, string? from, string? to)
    {
        if (path is null || from is null || to is null)
            throw new ArgumentException("vaultrename <vault copy> <old name> <new name>");
        if (!File.Exists(path))
            throw new ArgumentException($"no vault at {path}");

        // The tables first: a vault opened with none empties its search rows, and stamps them current (its Reproject).
        using var installed = Season.Open(gameRoot);
        using var vault = Vault.Open(path, Vault.InstalledSeason(), 10);
        Console.WriteLine($"moved {vault.RenameCharacter(from, to)} rows from {from} to {to}");
        return 0;
    }

    /// <summary>
    /// Every item in cached armory folders (or single armory files), rebuilt once and written out one
    /// tab-separated line each - what it is, and its bytes - so a question about the ladder's items
    /// reads a file instead of rebuilding 68,000 items. tests/stage.py runs it; the columns are
    /// its header. Offline: it reads only what is cached.
    /// </summary>
    static int StageItems(string gameRoot, IReadOnlyList<string> sources)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        foreach (string source in sources)
        {
            foreach (string file in Directory.Exists(source) ? [.. Directory.GetFiles(source, "*.json").Order()] : new[] { source })
            {
                if (Path.GetFileName(file).StartsWith('_'))
                    continue;

                string ladder = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(file))) ?? "";
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                var armory = new ArmoryImport.Armory(document.RootElement.Clone());

                int at = 0;
                foreach (var (json, mercenary) in armory.Items.Select(each => (each, false))
                             .Concat(armory.MercenaryItems.Select(each => (each, true))))
                {
                    at++;
                    string where = $"{ladder}	{armory.Name}	{armory.Level}	{at}	{(mercenary ? 1 : 0)}";
                    Item item;
                    try { item = TradeItems.Build(json).Item; }
                    catch (Exception problem)
                    {
                        Console.WriteLine($"{where}														FAILED: {problem.Message.ReplaceLineEndings(" ").Replace('	', ' ')}	");
                        continue;
                    }

                    string blob = Convert.ToBase64String(Item.Write(item, ItemBlob.Version));
                    string ids(ushort[]? each) => string.Join(",", each ?? []);
                    Console.WriteLine(string.Join('	',
                        where, item.Code.Trim(), item.Quality, item.ItemLevel, item.IsEthereal ? 1 : 0,
                        item.IsRuneword ? item.RunewordId : 0, item.IsAutoAffix ? item.AutoAffixId : 0,
                        ids(item.MagicPrefixIds), ids(item.MagicSuffixIds), $"{item.RarePrefixId},{item.RareSuffixId}",
                        Corruptions.IdOn(item), Desecrations.ValueOn(item), item.SocketedItems.Count,
                        ItemPresenter.Describe(item).Name.Replace('	', ' '), blob));
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// An older season's vault migrated into the installed season:
    /// the editor's own migration, from the command line - one vault, into a vault of its own name;
    /// <c>--vault</c> names it, and without it the season's first is meant. Old tables from the season's
    /// snapshot unless given; a dry run first is the way to use it. Without a season, the vaults waiting.
    /// Set PD2SPC_VAULTS to work on a copy.
    /// </summary>
    static int MigrateVault(string gameRoot, string? from, bool hardcore, bool dryRun, string? tables, bool again, string? vault)
    {
        using var installed = Season.Open(gameRoot);
        if (from is null)
        {
            string to = SeasonSnapshot.NameFor(installed);
            var pending = SeasonMigration.Pending(to);
            Console.WriteLine(pending.Count == 0
                ? $"No older vault waits to be migrated into {to}."
                : $"Waiting to be migrated into {to}, oldest first: "
                  + string.Join("; ", pending.Select(season => $"{season}: {string.Join(", ", SeasonMigration.PendingVaults(season, to).Select(each => $"{each.Name} ({(each.Hardcore ? "hardcore" : "softcore")})"))}")));
            return 0;
        }

        var report = SeasonMigration.Run(installed, new SeasonMigration.Request(from, hardcore, dryRun, tables, again, Vault: vault));
        Console.Write(report.Text());
        Console.WriteLine();
        Console.WriteLine($"report: {report.ReportPath}");
        return report.Count(SeasonMigration.Outcome.Failed) > 0 ? 1 : 0;
    }

    /// <summary>Characters brought into the installed season in place, in one run (CharacterMigration.RunAll); never point it at the live save folder.</summary>
    static int MigrateCharacters(string gameRoot, IReadOnlyList<string> saves, string? stash, bool dryRun)
    {
        if (saves.Count == 0)
            throw new ArgumentException("charmigrate <save.d2s...> [--stash <pd2_shared.stash>] [--dry-run]");

        using var installed = Season.Open(gameRoot);
        var done = CharacterMigration.RunAll(installed, [.. saves.Select(save => new CharacterMigration.Request(save, stash, dryRun))]);
        foreach (var each in done)
            Console.Write(each.Report?.Text() ?? $"{Path.GetFileName(each.Request.SavePath)}: {each.Refused}{Environment.NewLine}");
        return done.Any(each => each.Report is null || each.Report.Count(SeasonMigration.Outcome.Failed) > 0) ? 1 : 0;
    }

    /// <summary>
    /// An older season's vault to migrate, for testing the migration:
    /// the non-compact items of a stash or save, read under that season's <paramref name="tables"/>, in its
    /// softcore vault - half in a folder - with some gold. Into PD2SPC_VAULTS; refused where
    /// that season already has a vault - of that <paramref name="name"/>, where one is given: a season's vaults are
    /// migrated one by one, and a pad's own has been. (The older layout, items in collections beside the
    /// records, is the unit tests' to make: SeasonMigrationTests.)
    /// </summary>
    static int PastVault(string gameRoot, string? file, string? season, string? tables, int take, string? name)
    {
        if (file is null || season is null || tables is null)
            throw new ArgumentException("pastvault <stash|save> --season <S12> --tables <its tables> [--name <vault>]");
        if (name is null ? SeasonMigration.Modes(season).Count > 0 : VaultLibrary.ItemSources(season, false, name).Count > 0)
            throw new InvalidOperationException($"{season} has a vault{(name is null ? "" : $" \"{name}\"")} already, in {Vault.Folder}.");

        using var old = Season.Open(gameRoot, tables);
        var items = file.EndsWith(".d2s", StringComparison.OrdinalIgnoreCase)
            ? SaveVerifier.LoadAnyLayout(file).Save.PlayerItemList?.Items ?? []
            : file.EndsWith(".stash", StringComparison.OrdinalIgnoreCase)
                ? SharedStash.Read(file).Items.Items ?? []
                : (SaveVerifier.LoadStash(file).Stash.PageList ?? []).SelectMany(page => page.PageItems?.Items ?? []).ToList();

        int stored = 0;
        using (var vault = name is null ? VaultLibrary.OpenVault(season, false, 10) : VaultLibrary.OpenVault(season, false, name, 10))
        {
            long folder = vault.CreateFolder(null, "Sample");
            foreach (var item in items.Where(item => !item.IsCompact).Take(take))
            {
                if (vault.Add(item, stored % 2 == 0 ? null : folder, out _) is not null)
                    stored++;
            }

            vault.RecordGold([new GoldMove("stash", 1000)]);
        }

        Console.WriteLine($"{season}: {stored} items, 1,000 gold in {VaultLibrary.SeasonFolder(season)}");
        return 0;
    }

    /// <summary>
    /// The installed season's tables, kept outside the game:
    /// in the editor's Seasons folder, as the editor keeps them itself, or in <paramref name="into"/>.
    /// Taken again whatever is there - by hand is on purpose.
    /// </summary>
    static int Snapshot(string gameRoot, string? into)
    {
        using var installed = Season.Open(gameRoot);
        string season = SeasonSnapshot.NameFor(installed);

        string folder = into ?? SeasonSnapshot.FolderFor(season);
        var manifest = SeasonSnapshot.Take(installed, season, folder, SeasonSnapshot.Read(folder)?.ItemVersion);

        Console.WriteLine($"season {manifest.Season}, build {manifest.Build}, mod version {(manifest.ModVersion.Length > 0 ? manifest.ModVersion : "unknown")}"
                          + $", previous-season columns {(manifest.PreviousSeason.Length > 0 ? manifest.PreviousSeason : "none")}");
        Console.WriteLine($"  {manifest.Files.Count} files, {manifest.Files.Sum(file => file.Size) / 1024} KB, item version {manifest.ItemVersion ?? "not yet seen"}");
        Console.WriteLine($"  in {folder}");
        return 0;
    }

    /// <summary>
    /// The installed season's vault files: each mode's vaults, and what is left of the layouts before - the
    /// one file of before collections, collections not yet folded in, a mode's one vault not yet given its
    /// name. <c>--split</c> splits the one file (as the editor does on first open), <c>--hardcore</c> for
    /// that mode. <c>--merge</c> folds each mode's collections into its first vault, as the editor does on
    /// the first open of the mode. The one vault of before is moved to its name here as there. Set
    /// PD2SPC_VAULTS to work on a copy.
    /// </summary>
    static int VaultFiles(string gameRoot, bool split, bool hardcore, bool merge)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        string season = Vault.InstalledSeason();
        Console.WriteLine($"vaults in {Vault.Folder}, season {season}");

        if (VaultLibrary.NeedsSplit(season))
        {
            Console.WriteLine($"  one file of before collections: {Vault.PathFor(season)}");
            if (!split)
                return 0;

            Console.WriteLine($"  split into {(hardcore ? "hardcore" : "softcore")}; copy at {VaultLibrary.Split(season, hardcore, 10)}");
        }

        Console.WriteLine($"  collections moved from the folder both modes shared: {VaultLibrary.Rehome(season)}");
        Console.WriteLine($"  vaults moved to their names: {VaultLibrary.Rehouse(season)}");

        foreach (bool mode in new[] { false, true })
        {
            foreach (var each in VaultLibrary.Vaults(season, mode))
            {
                using var vault = VaultLibrary.OpenVault(season, mode, each.Name, 10);
                Console.WriteLine($"  {(mode ? "hardcore" : "softcore")} vault \"{each.Name}\": {vault.Count()} items, {vault.Folders().Count} folders, "
                                  + $"{vault.Trades().Count} trades, play mode {(each.PlayMode.Length > 0 ? each.PlayMode : "not kept")}");
            }
        }

        foreach (var collection in VaultLibrary.Collections(season, false).Concat(VaultLibrary.Collections(season, true)))
        {
            Console.WriteLine($"  not folded in: collection {collection.Name} ({(collection.Hardcore ? "hardcore" : "softcore")}), "
                              + $"{collection.Items} items");
        }

        if (!merge)
            return 0;

        foreach (bool mode in new[] { false, true })
        {
            if (!VaultLibrary.NeedsMerge(season, mode))
                continue;

            var report = VaultLibrary.MergeCollections(season, mode, 10);
            Console.WriteLine($"  folded into the {(mode ? "hardcore" : "softcore")} vault: {report.Collections} collections, {report.Items} items, "
                              + $"{report.Failed} not read; the vault before at {report.Backup}; the files in {report.Retired}");
        }

        return 0;
    }

    /// <summary>
    /// A mode's vaults as the settings page handles them, without the file pickers. <c>--new &lt;name&gt;
    /// [--copy &lt;name&gt;]</c> makes one, empty or a copy; <c>--delete &lt;name&gt;</c>;
    /// <c>--rename-vault &lt;name&gt; --as &lt;new&gt;</c>. <c>--export &lt;file&gt;</c> writes a vault out.
    /// <c>--import &lt;file&gt;</c> brings a file in as a vault of its own; with <c>--as-folder</c>, as a folder
    /// of a vault, and with <c>--folders "A/B;C;/"</c> only those of its folders - a folder's own items, "/"
    /// for the items at the file's top. <c>--rename &lt;old&gt; --as &lt;new&gt;</c> is a character renamed, in every
    /// vault of the season. <c>--vault &lt;name&gt;</c> for a vault other than the mode's first,
    /// <c>--hardcore</c> for that mode. Set PD2SPC_VAULTS to work on a copy.
    /// </summary>
    static int VaultIo(string gameRoot, bool hardcore, Func<string, string?> option, Func<string, bool> flag)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;
        string season = Vault.InstalledSeason();
        string named = option("--vault") ?? VaultLibrary.MainName(season);
        string? renameAs = option("--as");

        if (option("--new") is { } made)
        {
            if (VaultLibrary.RefuseName(season, hardcore, made) is { } refused)
                throw new InvalidOperationException(refused);

            VaultLibrary.CreateVault(season, hardcore, made, option("--copy"), 10);
            Console.WriteLine(option("--copy") is { } copied ? $"vault {made} made, a copy of {copied}" : $"vault {made} made, empty");
        }

        if (option("--delete") is { } doomed)
            Console.WriteLine($"vault {doomed} deleted; its copy at {VaultLibrary.DeleteVault(season, hardcore, doomed, 10)}");

        if (option("--rename-vault") is { } renamed)
        {
            if (renameAs is null)
                throw new ArgumentException("--rename-vault <name> needs --as <new name>");
            if (VaultLibrary.RefuseName(season, hardcore, renameAs, renamed) is { } refused)
                throw new InvalidOperationException(refused);

            VaultLibrary.RenameVault(season, hardcore, renamed, renameAs);
            Console.WriteLine($"vault {renamed} renamed {renameAs}");
        }

        if (option("--rename") is { } character && renameAs is not null)
            Console.WriteLine($"renamed {character} to {renameAs} in {VaultLibrary.RenameCharacter(season, character, renameAs, 10)} rows");

        if (option("--export") is { } export)
        {
            using var vault = VaultLibrary.OpenVault(season, hardcore, named, 10);
            vault.CopyTo(export);
            Console.WriteLine($"vault {named} exported to {export}");
        }

        if (option("--import") is { } import)
        {
            using var incoming = VaultLibrary.Stage(import, season, 10);
            if (!flag("--as-folder"))
            {
                Console.WriteLine($"imported as vault {VaultLibrary.AddAsVault(incoming, season, hardcore, 10)}: {incoming.Items} items");
                return 0;
            }

            // "A/B" to the file's folder of that path; "/" is its top.
            Func<long?, bool>? takes = null;
            if (option("--folders") is { } list)
            {
                var ticked = new HashSet<long>();
                bool top = false;
                foreach (string path in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    long? at = null;
                    foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        at = incoming.Folders.FirstOrDefault(each => each.ParentId == at
                                 && string.Equals(each.Name, part, StringComparison.OrdinalIgnoreCase))?.Id
                             ?? throw new InvalidOperationException($"the file has no folder {path}");
                    }

                    if (at is { } id)
                        ticked.Add(id);
                    else
                        top = true;
                }

                takes = folder => folder is { } id ? ticked.Contains(id) : top;
            }

            using var into = VaultLibrary.OpenVault(season, hardcore, named, 10);
            var (folder, added, failed) = VaultLibrary.AddAsFolder(into, incoming, takes);
            Console.WriteLine($"imported into vault {named} as folder {folder}: {added} items, {failed} not read");
        }

        return 0;
    }

    /// <summary>
    /// Smart sort over a staged items file (tests/stage.py's): how many items each folder would
    /// take, sorted and as crafting bases, what fell to Unsorted, and what the rules file said
    /// was wrong with it. --rules reads a rules file other than the default.
    /// </summary>
    static int SmartSortCommand(string gameRoot, string? staged, string? rulesFile, bool all)
    {
        if (staged is null)
            throw new ArgumentException("smartsort <staged.tsv | forged> [--rules <file>] [--all]");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var rules = SmartSort.Parse(rulesFile is null ? SmartSort.DefaultText : File.ReadAllText(rulesFile));
        foreach (string problem in rules.Problems)
            Console.WriteLine($"problem: {problem}");

        // What the game calls each set, and the group the rules put it in.
        foreach (int row in ItemForge.SetItems())
        {
            var piece = ItemForge.SetItemAt(row, "103").Item;
            if (SetNames.Of(piece) is { } set && ItemForge.SetItems().First(each => ItemForge.SetOf(each) == ItemForge.SetOf(row)) == row)
                Console.WriteLine($"set {ItemForge.SetOf(row)} = {set} -> {string.Join("/", SmartSort.Classify(piece, rules))}");
        }

        // A magic circlet: whether a craft takes it, by the tables.
        var circlet = ItemForge.Unique(ItemForge.UniqueNames().First(), "103").Item;
        circlet.Code = "ci0 ";
        circlet.Quality = ItemQuality.Magic;
        Console.WriteLine($"magic circlet craftable: {SmartSort.CanBeCraftingBase(circlet)} ({string.Join(",", AffixRanges.CraftFamilies(circlet).Select(each => each.Family))})");

        IEnumerable<(Item Item, string Name)> Source()
        {
            if (staged == "forged")
            {
                foreach (var named in EveryNamedItem("103"))
                    yield return named;
                yield break;
            }

            foreach (string line in File.ReadLines(staged).Skip(1))
            {
                string[] cells = line.Split('	');
                if (cells.Length < 19 || cells[18].Length == 0)
                    continue;

                Item? read = null;
                try { read = Item.Read(new D2SLib.IO.BitReader(Convert.FromBase64String(cells[18])), ItemBlob.Version); }
                catch (Exception) { }
                yield return (read!, cells[17]);
            }
        }
        var sorted = new Dictionary<string, int>();
        var crafting = new Dictionary<string, int>();
        var unsorted = new Dictionary<string, int>();
        int items = 0, failed = 0;

        foreach (var (item, name) in Source())
        {
            if (item is null)
            {
                failed++;
                continue;
            }

            items++;
            string path = string.Join("/", SmartSort.Classify(item, rules));
            sorted[path] = sorted.GetValueOrDefault(path) + 1;
            if (path == SmartSort.Unsorted)
                unsorted[$"{item.Code.Trim()} {item.Quality} {name}"] = unsorted.GetValueOrDefault($"{item.Code.Trim()} {item.Quality} {name}") + 1;

            if (SmartSort.ClassifyAsCraftingBase(item, rules) is { } asBase)
                crafting[string.Join("/", asBase)] = crafting.GetValueOrDefault(string.Join("/", asBase)) + 1;
        }

        Console.WriteLine($"{items} items, {failed} unreadable, {sorted.Count} folders");
        foreach (var (path, count) in sorted.OrderBy(each => each.Key, StringComparer.Ordinal).Take(all ? int.MaxValue : 400))
            Console.WriteLine($"{count,7}  {path}");
        Console.WriteLine("crafting bases:");
        foreach (var (path, count) in crafting.OrderBy(each => each.Key, StringComparer.Ordinal))
            Console.WriteLine($"{count,7}  {path}");
        Console.WriteLine("unsorted:");
        foreach (var (what, count) in unsorted.OrderByDescending(each => each.Value).Take(40))
            Console.WriteLine($"{count,7}  {what}");

        return 0;
    }

    /// <summary>
    /// A vault whose tree is as full as the smart sort makes it, for looking at the vault page with: every unique, set piece
    /// and runeword; of every base the Forge makes one plain, one magic and one rare, a few to a folder
    /// (<paramref name="most"/>); a crafted one through every recipe family that takes it; a skiller for every class;
    /// runes, gems and materials; and magic and rare ones again as crafting bases. Each is stored where the default
    /// rules put it. Into a file named outright - a scratch vault, never one of the player's - stamped for the era of
    /// the save named.
    /// </summary>
    static int TreeVault(string gameRoot, string? save, string? file, int most)
    {
        if (save is null || file is null)
            throw new ArgumentException("treevault <save.d2s> --file <vault> [--most <n>]");
        if (File.Exists(file))
            throw new ArgumentException($"{file} is there already: a tree vault is made new.");

        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        var (read, _) = SaveVerifier.Load(save);
        string era = EraOf(read);

        var rules = SmartSort.Parse(SmartSort.DefaultText);
        var random = new Random(20261012);
        using var vault = Vault.Open(file, Vault.InstalledSeason(), 10);

        var held = new Dictionary<string, int>(StringComparer.Ordinal);
        uint id = 3_000_000_000;
        int stored = 0, refused = 0;

        // Stored where the rules put it, where its folder has room for one more of its sort ("all" has always).
        bool Store(Item item, IReadOnlyList<string> path, string sort, bool all = false)
        {
            string key = string.Join("/", path) + "|" + sort;
            if (!all && held.GetValueOrDefault(key) >= most)
                return false;

            item.Id = id++;
            if (vault.Add(item, vault.FolderFor(path), out _) is null)
            {
                refused++;
                return false;
            }

            held[key] = held.GetValueOrDefault(key) + 1;
            stored++;
            return true;
        }

        Item? Made(Func<Item> make)
        {
            try { return make(); }
            catch (Exception) { return null; }
        }

        // Everything with a name.
        foreach (var (item, _) in EveryNamedItem(era))
            Store(item, SmartSort.Classify(item, rules), "named", all: true);

        // Every base, in each quality it is made in.
        var search = new ForgeSearch();
        search.Load();
        var codes = new List<string>();
        foreach (var type in search.Types.Where(type => !type.IsAny))
        {
            // The bases are listed a type at a time, as the Forge's box lists them.
            search.SetType(type);
            codes.AddRange(search.Bases.Where(line => line.Code is not null).Select(line => line.Code!).Where(code => !codes.Contains(code)));
        }

        foreach (string code in codes)
        {
            foreach (var quality in new[] { ItemQuality.Normal, ItemQuality.Superior, ItemQuality.Magic, ItemQuality.Rare })
            {
                if (Made(() => ItemForge.BaseAs(code, quality, era, random).Item) is not { } item)
                    continue;

                Store(item, SmartSort.Classify(item, rules), quality.ToString());

                // And again as a crafting base, where a recipe takes it.
                if (SmartSort.ClassifyAsCraftingBase(item, rules) is { } asBase
                    && held.GetValueOrDefault(string.Join("/", asBase) + "|base") < most
                    && Made(() => ItemForge.BaseAs(code, quality, era, random).Item) is { } again)
                {
                    Store(again, asBase, "base");
                }
            }

            // Crafted, through every recipe family that takes it.
            if (Made(() => ItemForge.BaseAs(code, ItemQuality.Magic, era, random).Item) is { } probe)
            {
                foreach (var recipe in AffixRanges.CraftFamilies(probe))
                {
                    if (Made(() =>
                        {
                            var crafted = ItemForge.BaseAs(code, ItemQuality.Magic, era, random).Item;
                            Crafts.CraftFrom(crafted, recipe, random);
                            return crafted;
                        }) is { } crafted)
                    {
                        Store(crafted, SmartSort.Classify(crafted, rules), "crafted " + recipe.Family);
                    }
                }
            }
        }

        // A skiller for every class: a magic charm under each prefix it can have, kept where the rules call it one.
        foreach (string code in codes)
        {
            if (Made(() => ItemForge.BaseAs(code, ItemQuality.Magic, era, random).Item) is not { } charm
                || SmartSort.Classify(charm, rules) is not ["Charms", ..])
            {
                continue;
            }

            foreach (var option in MagicAffixes.Available(charm, prefix: true, forSlot: 0))
            {
                if (Made(() =>
                    {
                        var made = ItemForge.BaseAs(code, ItemQuality.Magic, era, random).Item;
                        MagicAffixes.Apply(made, new Dictionary<(int, bool), MagicAffixes.AffixPick>
                        {
                            [(0, true)] = new(option.Id, MagicAffixes.Roll(option, random)),
                        }, random);
                        return made;
                    }) is { } skiller
                    && SmartSort.Classify(skiller, rules) is ["Charms", "Skillers", ..] path)
                {
                    Store(skiller, path, "skiller");
                }
            }
        }

        // Runes, gems and what the materials tab holds: whatever of these codes the tables have.
        string[] loose =
        [
            .. Enumerable.Range(1, 33).Select(n => $"r{n:00}"),
            "gcv", "gfv", "gsv", "gzv", "gpv", "gcy", "gpy", "gcb", "gpb", "gcg", "gpg", "gcr", "gpr", "gcw", "gpw", "skc", "skz",
            "pk1", "pk2", "pk3", "tes", "ceh", "bet", "fed", "toa", "key", "tbk", "ibk", "wss", "lbox",
        ];
        foreach (string code in loose)
        {
            if (Made(() => ItemForge.Material(code, 1, era)) is { } item)
                Store(item, SmartSort.Classify(item, rules), "loose", all: true);
        }

        var folders = vault.Folders();
        Console.WriteLine($"{file}: {stored} items in {folders.Count} folders ({refused} refused)");
        foreach (var top in held.Keys.Select(key => key.Split('|')[0]).Distinct().GroupBy(path => path.Split('/')[0]).OrderBy(group => group.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {top.Key,-18} {top.Count(),3} folder(s) with items");
        return 0;
    }

    /// <summary>
    /// Every enabled unique and every set piece, then every runeword once for each kind of base it can be made
    /// in - weapon, armor, shield - in the best base that holds it, all at the top of every range: a set of
    /// items of every name, for measuring what sorts or checks them (smartsort's "forged"). Stamped in
    /// <paramref name="stampAs"/>.
    /// </summary>
    static IEnumerable<(Item Item, string Name)> EveryNamedItem(string stampAs)
    {
        Item? Made(Func<Item> make)
        {
            try { return make(); }
            catch (Exception) { return null; }
        }

        foreach (int row in ItemForge.EnabledUniques())
        {
            if (Made(() => ItemForge.UniqueAt(row, stampAs, RollAs.Maximum).Item) is { } unique)
                yield return (unique, ItemPresenter.Describe(unique).Name);
        }

        foreach (int row in ItemForge.SetItems())
        {
            if (Made(() => ItemForge.SetItemAt(row, stampAs, RollAs.Maximum).Item) is { } piece)
                yield return (piece, ItemPresenter.Describe(piece).Name);
        }

        foreach (var word in Runewords.All)
        {
            var kinds = word.Types
                .Where(type => ItemForge.BaseFor([type], word.ExcludedTypes) is not null)
                .GroupBy(type => SocketEffects.KindOf(ItemForge.BaseFor([type], word.ExcludedTypes)!));

            foreach (var kind in kinds)
            {
                if (ItemForge.BestBaseFor([.. kind], word.ExcludedTypes, word.Runes.Length) is { } code
                    && Made(() => ItemForge.Runeword(word, code, stampAs).Item) is { } made)
                {
                    yield return (made, $"{Runewords.DisplayName(word)} ({ItemPresenter.BaseName(code)})");
                }
            }
        }
    }

    /// <summary>
    /// The vault, from the command line: copy a save or stash into it, or list what it holds.
    /// <para>
    /// A file the editor's vault page opens - the installed season's first softcore vault, with
    /// <c>--hardcore</c> the hardcore one, with <c>--vault</c> another of the mode's by name
    /// (<see cref="VaultLibrary.VaultPath(string, bool, string)"/>) - so this is the way to see what the page
    /// will see. <c>--file</c> names any file, for trying something on a copy.
    /// </para>
    /// </summary>
    static int VaultCommand(string gameRoot, string? file, string? add, bool list, bool check,
        Func<string, string?> option, Func<string, bool> flag)
    {
        using var installed = Season.Open(gameRoot);
        var game = installed.Game;

        string season = Vault.InstalledSeason();
        string path = file ?? VaultLibrary.VaultPath(season, flag("--hardcore"), option("--vault") ?? VaultLibrary.MainName(season));

        using var vault = Vault.Open(path, season, 10);
        Console.WriteLine($"vault {path}  (season {vault.Season}, {vault.Count()} items)");

        if (flag("--empty"))
        {
            string? backup = vault.Empty();
            Console.WriteLine(backup is null
                ? "  already empty"
                : $"  emptied: {vault.Count()} items, {vault.Folders().Count} folders; backup {backup}");
        }

        if (add is not null)
        {
            int taken = 0, refused = 0;

            foreach (var (_, item) in Contents(add))
            {
                if (vault.Add(item, out string? why) is null)
                {
                    refused++;
                    Console.WriteLine($"  refused {item.Code?.Trim()}: {why}");
                }
                else
                {
                    taken++;
                }
            }

            Console.WriteLine($"  {taken} copied in, {refused} refused");
        }

        if (check)
        {
            int good = 0;
            var trouble = new List<string>();

            // What a withdraw will meet: the eras the items are stamped with - a character takes
            // only its own - and the quest items, which the vault keeps and never hands out.
            var eras = new SortedDictionary<string, int>();
            var quests = new List<string>();

            foreach (var row in vault.Page("Stored", descending: true, 0, int.MaxValue))
            {
                try
                {
                    var restored = ItemBlob.Restore(row.Blob, vault.StatIdBits);
                    _ = ItemPresenter.Describe(restored);
                    good++;

                    string era = restored.Version is { Length: > 0 } v ? v : "(none)";
                    eras[era] = eras.GetValueOrDefault(era) + 1;
                    if (ItemBases.IsQuestItem(restored.Code))
                        quests.Add(row.Name);
                }
                catch (Exception problem)
                {
                    if (trouble.Count < 6)
                        trouble.Add($"#{row.Key} {row.Name}: {Line(problem)}");
                }
            }

            Console.WriteLine($"  {good} decode, {vault.Count() - good} do not");
            Console.WriteLine($"  item eras: {string.Join(", ", eras.Select(pair => $"v{pair.Key} x{pair.Value}"))}");
            Console.WriteLine($"  quest items: {(quests.Count == 0 ? "none" : string.Join(", ", quests))}");
            foreach (string said in trouble)
                Console.WriteLine($"    {said}");
        }

        if (Folders(vault, option, flag) is { } failed)
        {
            Console.WriteLine($"  refused: {failed}");
            return 1;
        }

        if (list)
        {
            // The page's own orders and query, so this lists what the page shows. Rarity and group
            // are worked out again from the blob here rather than read back, so a listing in those
            // orders is also a check that the stored ranks agree with the derivation.
            string by = option("--by") ?? "Stored";
            if (!Vault.Orders.Contains(by, StringComparer.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"--by is one of {string.Join(", ", Vault.Orders)}.");
                return 2;
            }

            by = Vault.Orders.First(each => each.Equals(by, StringComparison.OrdinalIgnoreCase));
            bool descending = option("--by") is null || flag("--desc");

            // --find is the page's search, over the top level as the page searches one folder.
            string? find = option("--find");
            var rows = find is null
                ? vault.Page(by, descending, 0, int.MaxValue)
                : vault.Page(null, new VaultQuery(Name: find), by, descending, 0, int.MaxValue);

            if (find is not null)
                Console.WriteLine($"  {vault.Count(null, new VaultQuery(Name: find))} at the top level match \"{find}\"");

            foreach (var row in rows)
            {
                string rank = "?";
                try
                {
                    var item = ItemBlob.Restore(row.Blob, vault.StatIdBits);
                    rank = $"{ItemOrder.Rarities[ItemOrder.Rarity(item)]} / {ItemOrder.TypeName(ItemOrder.TypeRank(item))}";
                }
                catch (Exception)
                {
                }

                Console.WriteLine($"  #{row.Key,-4} {row.Name,-34} {rank,-30} ilvl {row.ItemLevel,-3} "
                                  + $"id {(row.OriginId?.ToString() ?? "-"),-10} "
                                  + $"{row.Added.ToLocalTime():yyyy-MM-dd HH:mm}");
            }
        }

        return 0;
    }
}
