using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using D2SLib.Model.Save;
using D2SLib.Model.TXT;
using PD2SPCompanion.Core.Items;

namespace PD2SPCompanion.Core.Trade;

/// <summary>
/// The item PD2 copies to the clipboard on Ctrl+C over one in game, made again, for an import
/// of that text into the Forge. The text is JSON - its shape, and what it leaves out, measured against thirty
/// forged items copied back.
/// <para>
/// <b>It rebuilds, it does not clone.</b> The text has no item id, no durability and no picture, a rare's or a
/// craft's affix ids are worked out from its lines, and three things the game does not copy cannot be told at
/// all: a second stat under a name already used (a rare's maximum damage per level beside its maximum damage),
/// a stat the game never words, and whether a magic or rare item's skill is an oskill. Each is said in
/// <see cref="Forged.Notes"/>.
/// </para>
/// <para>
/// Two ways in, by what the item is:
/// <list type="bullet">
/// <item>A unique, a set piece, a runeword: made as the Forge makes it from its row - its lists, its runes, its
/// green bonuses, its base's defence, every line the game did not copy - then every number the text gives put
/// over the Forge's, and what the text has beyond the row (a corruption's line, a superior base's bonus) added.</item>
/// <item>Anything else: the text told as a trade listing and built by <see cref="TradeItems.Build"/>, which
/// already finds a magic item's affixes in its name, a rare's in its lines, and the base's automod.</item>
/// </list>
/// </para>
/// </summary>
public static partial class GameClipboard
{
    /// <summary>Whether a text is, at a glance, an item the game copied: JSON naming a quality and a type.</summary>
    public static bool LooksLike(string? text) =>
        text is { Length: > 20 }
        && text.AsSpan().TrimStart().StartsWith("{")
        && text.Contains("\"quality\"", StringComparison.Ordinal)
        && text.Contains("\"type\"", StringComparison.Ordinal);

    /// <summary>
    /// What the first item in the text is called, as the game shows it, without making it - for a button that
    /// says what it would paste. A unique and a set piece by their row, the text naming them by its index ("Iros
    /// Torch" for the Torch of Iro); a runeword by its word; anything else by its own name, or its base's. Null
    /// where the text is no item.
    /// </summary>
    public static string? NameOf(string? text)
    {
        if (!LooksLike(text))
            return null;

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text!), new JsonReaderOptions { AllowMultipleValues = true, AllowTrailingCommas = true });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    continue;

                using var one = JsonDocument.ParseValue(ref reader);
                var pasted = one.RootElement;
                string name = Clean(Text(pasted, "name") ?? ""), type = Clean(Text(pasted, "type") ?? "");

                if (Flag(pasted, "isRuneword") && Clean(Text(pasted, "runeword") ?? "") is { Length: > 0 } word)
                    return word;

                var listed = QualityOf(Text(pasted, "quality")) switch
                {
                    ItemQuality.Unique => GrailCatalog.Uniques(),
                    ItemQuality.Set => GrailCatalog.Sets(),
                    _ => null,
                };

                if (listed?.FirstOrDefault(entry => Same(GrailCatalog.IndexName(entry), name)) is { } row)
                    return GrailCatalog.ShownName(row);

                return name.Length > 0 ? name : type.Length > 0 ? type : null;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>The first item in the text, or null where it is not one. Stamped <paramref name="stampAs"/>, the character's item era.</summary>
    public static Forged? Read(string text, string stampAs) => ReadAll(text, stampAs).FirstOrDefault();

    /// <summary>Every item in the text, one after another as the game's copies are when pasted together.</summary>
    public static IReadOnlyList<Forged> ReadAll(string text, string stampAs)
    {
        var made = new List<Forged>();
        if (!LooksLike(text))
            return made;

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowMultipleValues = true, AllowTrailingCommas = true });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    continue;

                using var one = JsonDocument.ParseValue(ref reader);
                made.Add(Build(one.RootElement, stampAs));
            }
        }
        catch (JsonException)
        {
            // What was read before the text broke off stands.
        }

        return made;
    }

    /// <summary>One pasted object as an item.</summary>
    public static Forged Build(JsonElement pasted, string stampAs)
    {
        // The editor's own copy, this season's: the item itself (GameClipboard.Write).
        if (FromOwnKey(pasted, stampAs) is { } own)
            return own;

        var notes = new List<string>();
        bool whole = true;

        var quality = QualityOf(Text(pasted, "quality"));
        string name = Clean(Text(pasted, "name") ?? "");
        string type = Clean(Text(pasted, "type") ?? "");

        var item = quality switch
        {
            ItemQuality.Unique or ItemQuality.Set => FromRow(pasted, quality, name, type, stampAs, notes, ref whole),
            _ when Flag(pasted, "isRuneword") => FromWord(pasted, quality, type, stampAs, notes, ref whole),
            _ => null,
        } ?? FromListing(pasted, quality, name, type, stampAs, notes, ref whole);

        Fill(item, pasted, stampAs, notes, ref whole);
        ItemStats.InIdOrder(item);
        return new Forged(item, notes, whole);
    }

    // ---- a unique, a set piece, a runeword: the Forge's item, the text's numbers ----------------------

    private static Item? FromRow(JsonElement pasted, ItemQuality quality, string name, string type, string stampAs,
                                 List<string> notes, ref bool whole)
    {
        Item? item = null;
        try
        {
            // The text names the row by its index, not by what the game shows: "Iros Torch" for the Torch of Iro.
            item = quality == ItemQuality.Unique
                ? ItemForge.Unique(name, stampAs, RollAs.Maximum).Item
                : SetPiece(name, stampAs);
        }
        catch (ArgumentException)
        {
        }

        // Then by the name shown, as an older copy of the game may have given it.
        item ??= (quality == ItemQuality.Unique ? GrailCatalog.Uniques() : GrailCatalog.Sets())
            .Where(entry => Same(GrailCatalog.ShownName(entry), name))
            .Select(entry => quality == ItemQuality.Unique
                ? ItemForge.UniqueAt(entry.Row, stampAs, RollAs.Maximum).Item
                : ItemForge.SetItemAt(entry.Row, stampAs, RollAs.Maximum).Item)
            .FirstOrDefault();

        if (item is null)
        {
            notes.Add($"no {quality.ToString().ToLowerInvariant()} called {name} this season");
            whole = false;
            return null;
        }

        // An upgraded one keeps its row on the better base.
        if (CodeOf(type, item.Code) is { } code && !Same(code, item.Code.Trim()))
        {
            var plain = ItemForge.PlainBase(code, 0, stampAs).Item;
            item.Code = plain.Code;
            (item.MaxDurability, item.Durability) = (plain.MaxDurability, plain.Durability);
            if (ItemBases.Of(code) is { IsArmor: true, MaximumDefense: { } top })
                item.Armor = (ushort)(ItemStats.Every(item).Any(stat => Same(stat.Stat, "item_armor_percent")) ? top + 1 : top);
            notes.Add($"on a {ItemPresenter.BaseName(code)}, not its own base");
        }

        Over(item, pasted, notes, ref whole);
        return item;
    }

    private static Item? FromWord(JsonElement pasted, ItemQuality quality, string type, string stampAs,
                                  List<string> notes, ref bool whole)
    {
        string wordName = Clean(Text(pasted, "runeword") ?? "");
        if (CodeOf(type, null) is not { } code)
            return null;

        // Several rows can share a name - Wind for melee and for throwing: the one whose types the base is.
        var lineage = ItemTypes.LineageOfBase(code);
        var named = Runewords.All.Where(word => Same(word.Name, wordName) || Same(Runewords.DisplayName(word), wordName)).ToList();
        var row = named.FirstOrDefault(word => word.Types.Any(kind => lineage.Contains(kind, StringComparer.OrdinalIgnoreCase)
                                               && !word.ExcludedTypes.Any(not => lineage.Contains(not, StringComparer.OrdinalIgnoreCase))))
                  ?? named.FirstOrDefault();
        if (row is null)
        {
            notes.Add($"no runeword called {wordName} this season");
            whole = false;
            return null;
        }

        var item = ItemForge.Runeword(row, code, stampAs).Item;
        if (quality is ItemQuality.Superior or ItemQuality.Inferior)
            item.Quality = quality;

        Over(item, pasted, notes, ref whole);
        return item;
    }

    /// <summary>
    /// The text's numbers over the Forge's: a line the item has takes the pasted number, wherever its list; a
    /// line it has not is added to the item's own list - a corruption's, a desecration's, a superior base's.
    /// </summary>
    private static void Over(Item item, JsonElement pasted, List<string> notes, ref bool whole)
    {
        var mine = ItemStats.Every(item).ToList();
        var taken = new HashSet<ItemStat>(ReferenceEqualityComparer.Instance);

        foreach (var entry in Entries(pasted))
        {
            foreach (var stat in Stats(entry, item, notes, ref whole))
            {
                // By its key; and a line the text gives a number and no subject for - a weapon's splash, which packs
                // a skill and a level the text has not - by its stat alone.
                string key = AffixRanges.KeyOf(stat);
                var had = mine.FirstOrDefault(each => !taken.Contains(each) && Same(AffixRanges.KeyOf(each), key))
                          ?? ((stat.SkillId ?? stat.Param ?? 0) == 0
                              ? mine.FirstOrDefault(each => !taken.Contains(each) && each.Id == stat.Id)
                              : null);
                if (had is null)
                {
                    ItemStats.Home(item).Stats.Add(stat);
                    continue;
                }

                taken.Add(had);
                had.Value = stat.Value;
                if (stat.SkillLevel is not null) had.SkillLevel = stat.SkillLevel;
                if (stat.MaxCharges is not null) had.MaxCharges = stat.MaxCharges;

                // A flat damage is three stats under one name (DamageKinds), and the text has the first: the
                // copies beside it take its number.
                if (DamageKinds.Ends.FirstOrDefault(end => end.Contains(had.Stat ?? "", StringComparer.OrdinalIgnoreCase)) is { } copies
                    && item.StatLists.FirstOrDefault(list => list.Stats.Contains(had)) is { } beside)
                {
                    foreach (var copy in beside.Stats.Where(each => !taken.Contains(each) && copies.Contains(each.Stat ?? "", StringComparer.OrdinalIgnoreCase)))
                    {
                        copy.Value = stat.Value;
                        taken.Add(copy);
                    }
                }
            }
        }

        // What the item's own list has and the text has not, and could have rolled otherwise: said, as it stands
        // at the Forge's best. The text is keyed by each line's name, so the second of two under one name is not in
        // it - a unique map's second "Monsters have" line (see Unsaid).
        var ranges = AffixRanges.For(item);
        foreach (var stat in item.StatLists.FirstOrDefault()?.Stats ?? [])
        {
            if (!taken.Contains(stat) && mine.Contains(stat) && stat.Stat is { Length: > 0 } left
                && ranges.TryGetValue(AffixRanges.KeyOf(stat), out var range) && range.Max > range.Min && Unsaid(item, left))
            {
                notes.Add($"{StatText.Describe(left, stat.Value) ?? left}: not in the game's text, left at its best");
            }
        }

        // The flag beside a corruption's id, which the game's text drops where a desecration's mark shares its name.
        if (ItemStats.Every(item).Any(stat => Same(stat.Stat, Corruptions.IdStat))
            && !ItemStats.Every(item).Any(stat => Same(stat.Stat, Corruptions.FlagStat)))
        {
            ItemStats.Home(item).Stats.Add(Stat(Corruptions.FlagStat, Corruptions.FlagFor(item, Corruptions.IdOn(item) ?? 0, random: null)));
        }
    }

    // ---- everything else: told as a trade listing --------------------------------------------------

    private static Item FromListing(JsonElement pasted, ItemQuality quality, string name, string type, string stampAs,
                                    List<string> notes, ref bool whole)
    {
        string code = CodeOf(type, null) ?? "";
        if (code.Length == 0)
        {
            notes.Add($"no base called {type}");
            whole = false;
        }

        var listing = new JsonObject
        {
            ["base_code"] = code,
            ["name"] = name.Length > 0 ? name : ItemPresenter.BaseName(code),
            ["quality"] = new JsonObject { ["id"] = (int)quality },
            ["item_level"] = Number(pasted, "iLevel") ?? 1,
            ["is_identified"] = true,
            ["is_ethereal"] = Flag(pasted, "isEthereal"),
            ["socket_count"] = Number(pasted, "sockets") ?? 0,
        };

        if (Number(pasted, "quantity") is { } quantity)
            listing["quantity"] = quantity;

        // What the text does not carry, as the base starts out.
        if (code.Length > 0)
        {
            var plain = ItemForge.PlainBase(code, 0, stampAs).Item;
            if (plain.MaxDurability > 0)
                listing["durability"] = new JsonObject { ["maximum"] = plain.MaxDurability, ["current"] = plain.Durability };
        }

        var probe = new Item { Code = code.PadRight(4, ' '), Quality = quality, Flags = new BitArray(32) };
        var modifiers = new JsonArray();
        bool corrupted = false;
        foreach (var entry in Entries(pasted))
        {
            foreach (var stat in Stats(entry, probe, notes, ref whole))
            {
                corrupted |= Same(stat.Stat, Corruptions.IdStat) || Same(stat.Stat, Corruptions.FlagStat);
                Subject(stat, probe);
                modifiers.Add(Modifier(stat));
            }
        }

        listing["modifiers"] = modifiers;
        if (corrupted)
            listing["corrupted"] = true;

        var built = TradeItems.Build(JsonSerializer.SerializeToElement(listing), stampAs);
        if (quality is ItemQuality.Rare or ItemQuality.Craft)
            built = Recovered(built, listing, stampAs, notes);
        else if (quality == ItemQuality.Magic)
            PutBack(built.Item, notes);

        notes.AddRange(built.Notes);
        whole &= built.Sound;
        return built.Item;
    }

    /// <summary>
    /// The skill and level of a stat that packs them, where the text gave its number alone: a melee weapon's
    /// splash, "Melee Attacks Deal Splash Damage" 100 - which the base's automod writes, and names both.
    /// </summary>
    private static void Subject(ItemStat stat, Item probe)
    {
        if (stat.SkillId is not null || StatShapes.Of(stat.Stat ?? "") is not (StatShape.Chance or StatShape.Charges))
            return;

        foreach (var grant in AffixRanges.AutomagicRowsFor(probe).SelectMany(row => row.Grants))
        {
            if (AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Max, grant.Param)
                    .FirstOrDefault(each => Same(each.Stat, stat.Stat)) is not { } wrote)
                continue;

            (stat.SkillId, stat.SkillLevel, stat.MaxCharges) = (wrote.SkillId, wrote.SkillLevel, wrote.MaxCharges);
            return;
        }
    }

    // ---- what the text leaves out ---------------------------------------------------------------------

    /// <summary>
    /// Whether the game's text would have no line for this stat on this item. <b>The text is keyed by each line's
    /// name</b> - the stat's <c>descstrpos</c> string - so of two stats under one name it keeps the first: a rare
    /// weapon's "to Maximum Damage" (22) and not its per-level twin (218), a map's first "Monsters have" line (432)
    /// and not its second (496), a desecration's mark (207) and not the corruption's (361). Measured on the thirty
    /// items copied back. A stat with no name at all is never in it.
    /// </summary>
    private static bool Unsaid(Item item, string stat)
    {
        string name = D2SLib.Core.TXT.ItemStatCostTXT[stat]?["descstrpos"]?.Value?.Trim() ?? "";
        if (name.Length == 0)
            return true;

        return ItemStats.Every(item).Any(each => !Same(each.Stat, stat)
            && Same(D2SLib.Core.TXT.ItemStatCostTXT[each.Stat ?? ""]?["descstrpos"]?.Value, name));
    }

    /// <summary>What an affix writes at the top of its ranges.</summary>
    private static List<AffixRanges.Wrote> Top(MagicAffixes.AffixOption affix) =>
        [.. affix.Grants.SelectMany(grant => AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Max, grant.Param))];

    /// <summary>
    /// A magic item's lines the text left out (<see cref="Unsaid"/>), put back: its affixes are in its name, and
    /// an affix always writes all it has. At the top of its range, the number being nowhere in the text. And an
    /// affix none of whose lines came through is any row of its name the item could carry - "of the Armorer" is
    /// four rows - so the best of them.
    /// </summary>
    private static void PutBack(Item item, List<string> notes)
    {
        var home = ItemStats.Home(item);
        bool Has(string stat) => home.Stats.Any(each => Same(each.Stat, stat));

        foreach (bool prefix in new[] { true, false })
        {
            var ids = prefix ? item.MagicPrefixIds : item.MagicSuffixIds;
            for (int slot = 0; slot < (ids?.Length ?? 0); slot++)
            {
                if (ids![slot] == 0)
                    continue;

                var options = MagicAffixes.Available(item, prefix, forSlot: slot, outgrown: true);
                if (options.FirstOrDefault(option => option.Id == ids[slot]) is not { } own)
                    continue;

                var wrote = Top(own);
                var absent = wrote.Where(each => !Has(each.Stat)).ToList();
                if (absent.Count == 0 || !absent.All(each => Unsaid(item, each.Stat)))
                    continue;

                if (absent.Count == wrote.Count)
                {
                    own = options
                        .Where(option => Same(option.Name, own.Name) && Top(option) is { Count: > 0 } other
                                         && other.All(each => !Has(each.Stat) && Unsaid(item, each.Stat)))
                        .OrderByDescending(option => Top(option).Sum(each => each.Value))
                        .ThenByDescending(option => option.Frequency)
                        .First();
                    ids[slot] = (ushort)own.Id;
                    absent = Top(own);
                }

                foreach (var each in absent)
                {
                    home.Stats.Add(ItemStats.LineFrom(each));
                    notes.Add($"{StatText.Describe(each.Stat, each.Value) ?? each.Stat}: not in the game's text, put back at {own.Name}'s best");
                }
            }
        }

        DamageKinds.Settle(item);
    }

    /// <summary>
    /// A rare or crafted item built again with a line the text left out (<see cref="Unsaid"/>), where that
    /// explains it better. Its affixes are found from its lines, so one whose second line is missing is not found
    /// and its first line stands unaccounted for - Blood Call's attack rating per level, of an affix that writes
    /// maximum damage per level beside it, a line the text dropped for sharing "to Maximum Damage". So: each affix
    /// the item could carry that writes a line the item has and others it has not, all of them ones the text would
    /// leave out; the item rebuilt with those; kept where the legitimacy check then finds less. Nothing is tried
    /// on an item the check passes.
    /// <para>
    /// <b>Only for a line nothing on the item accounts for</b>, and only an affix that writes that line at the
    /// number it has. Tried for every affix sharing any line with the item, a paste of Blood Call took 3.2 s -
    /// forty rebuilds, each a search of its affixes; held to the unaccounted line it is one.
    /// </para>
    /// </summary>
    private static Forged Recovered(Forged built, JsonObject listing, string stampAs, List<string> notes)
    {
        const int Rounds = 3, Tries = 40;

        int wrong = Legitimacy.Judge(built.Item).Count;
        for (int round = 0; round < Rounds && wrong > 0; round++)
        {
            var item = built.Item;
            var home = ItemStats.Home(item);
            bool Has(string stat) => home.Stats.Any(each => Same(each.Stat, stat));

            // The lines no affix found, and nothing else of the item's own, accounts for.
            var accounted = AffixRanges.For(item);
            var loose = home.Stats.Where(stat => stat.Stat is { Length: > 0 } && !accounted.ContainsKey(AffixRanges.KeyOf(stat))).ToList();
            if (loose.Count == 0)
                break;

            var tried = new HashSet<string>(StringComparer.Ordinal);
            (Forged Built, JsonObject Listing, List<AffixRanges.Wrote> Added)? best = null;

            foreach (var affix in MagicAffixes.Available(item, prefix: true, outgrown: true)
                         .Concat(MagicAffixes.Available(item, prefix: false, outgrown: true)))
            {
                var wrote = Top(affix);
                var absent = wrote.Where(each => !Has(each.Stat)).ToList();
                if (absent.Count == 0 || absent.Count == wrote.Count || !absent.All(each => Unsaid(item, each.Stat)))
                    continue;

                // It writes a loose line, at the number the item has.
                var least = affix.Grants.SelectMany(grant => AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Min, grant.Param)).ToList();
                bool explains = loose.Any(stat => Enumerable.Range(0, Math.Min(wrote.Count, least.Count)).Any(at =>
                    Same(wrote[at].Stat, stat.Stat)
                    && stat.Value >= Math.Min(least[at].Value, wrote[at].Value) && stat.Value <= Math.Max(least[at].Value, wrote[at].Value)));
                if (!explains || !tried.Add(string.Join(",", absent.Select(each => $"{each.Stat}={each.Value}"))))
                    continue;

                if (tried.Count > Tries)
                    break;

                var again = (JsonObject)listing.DeepClone();
                foreach (var each in absent)
                    ((JsonArray)again["modifiers"]!).Add(Modifier(ItemStats.LineFrom(each)));

                var rebuilt = TradeItems.Build(JsonSerializer.SerializeToElement(again), stampAs);
                int now = Legitimacy.Judge(rebuilt.Item).Count;
                if (now < wrong)
                    (wrong, best) = (now, (rebuilt, again, absent));
            }

            if (best is not { } better)
                break;

            (built, listing) = (better.Built, better.Listing);
            foreach (var each in better.Added)
                notes.Add($"{StatText.Describe(each.Stat, each.Value) ?? each.Stat}: not in the game's text, put back at its best");
        }

        // A rare map's affixes mostly share one name, and the text keeps one of them.
        if (wrong > 0 && ItemTypes.LineageOfBase(built.Item.Code).Contains("map", StringComparer.OrdinalIgnoreCase))
            notes.Add("the game's text keeps one \"Monsters have\" line of a map and drops the rest");

        return built;
    }

    /// <summary>A stat as the trade listing's reader takes it: the stored numbers, in the order its kind is read.</summary>
    private static JsonObject Modifier(ItemStat stat)
    {
        string name = stat.Stat ?? "";
        var row = D2SLib.Core.TXT.ItemStatCostTXT[name];
        int encode = row?["Encode"].ToInt32() ?? 0;
        int shift = StatText.PerLevelShift(name);

        static JsonArray Of(params double[] numbers) => new([.. numbers.Select(number => (JsonNode?)JsonValue.Create(number))]);

        JsonArray values = encode switch
        {
            2 => Of(stat.SkillLevel ?? 0, stat.SkillId ?? 0, stat.Value),
            3 => Of(stat.SkillLevel ?? 0, stat.SkillId ?? 0, stat.Value, stat.MaxCharges ?? stat.Value),
            _ when (stat.SkillId ?? stat.Param) is { } subject && row is not null && StatEncoding.SaveParamBits(row) > 0 => Of(subject, stat.Value),

            // The listing gives a per-level stat by the level where the game works it so; told back that way,
            // its reader stores the number the text gave.
            _ when shift > 0 && row?["op"].ToInt32() == 2 => Of(stat.Value / (double)(1 << shift)),
            _ => Of(stat.Value),
        };

        return new JsonObject { ["name"] = name, ["label"] = name, ["values"] = values };
    }

    // ---- what both ways end on -----------------------------------------------------------------------

    /// <summary>The item's own facts the text gives beside its lines, and what is in its sockets.</summary>
    private static void Fill(Item item, JsonElement pasted, string stampAs, List<string> notes, ref bool whole)
    {
        item.ItemLevel = (byte)Math.Clamp(Number(pasted, "iLevel") ?? item.ItemLevel, 1, 99);

        bool ethereal = Flag(pasted, "isEthereal");
        var basis = ItemBases.Of(item.Code);

        // Stored defence: the base's top plus one where the item enhances it - as every such armor the game
        // makes - else what the text shows, less the flat defence its lines add. Ethereal is in the stored number.
        if (basis is { IsArmor: true, MaximumDefense: { } top })
        {
            bool enhanced = ItemStats.Every(item).Any(stat => Same(stat.Stat, "item_armor_percent"));
            int flat = ItemStats.Every(item).Where(stat => Same(stat.Stat, "armorclass")).Sum(stat => stat.Value);
            if (enhanced)
                item.Armor = (ushort)(ethereal ? ItemBases.Ethereal(top + 1) : top + 1);
            else if (Number(pasted, "defense") is { } shown && shown - flat > 0)
                item.Armor = (ushort)(shown - flat);
            else if (ethereal && !item.IsEthereal && item.Armor > 0)
                item.Armor = (ushort)ItemBases.Ethereal(item.Armor);
        }

        item.IsEthereal = ethereal;

        if (Number(pasted, "quantity") is { } quantity && Stacks.IsStackable(item.Code))
            item.Quantity = (ushort)Math.Clamp(quantity, 0, Stacks.FieldMaximum);

        // A runeword's runes are in it already, as its word names them.
        if (!item.IsRuneword)
        {
            item.SocketedItems.Clear();
            item.NumberOfSocketedItems = 0;
            foreach (var child in Children(pasted))
            {
                string kind = Clean(Text(child, "type") ?? "");
                if (Flag(child, "isRune") || Flag(child, "isGem"))
                {
                    if (CodeOf(kind, null) is { } gem)
                        ItemForge.Socket(item, ItemForge.Material(gem, 1, stampAs));
                    else
                        Lost($"socketed {kind} not known", notes, ref whole);
                    continue;
                }

                var inside = Build(child, stampAs);
                notes.AddRange(inside.Notes.Select(note => $"{kind}: {note}"));
                whole &= inside.Sound;

                // A jewel of no row this season would be row 0's: out of its socket, said.
                if (TradeItems.NoRow(inside) is null)
                    ItemForge.Socket(item, inside.Item);
                else
                    whole = false;
            }
        }

        int sockets = Math.Max(Number(pasted, "sockets") ?? 0, item.SocketedItems.Count);
        item.TotalNumberOfSockets = (byte)Math.Clamp(sockets, 0, 15);
        item.Flags[ItemForge.Socketed] = sockets > 0;

        // A map's unworded lines, which the text has none of: as a plain one of its base holds them.
        if (ItemTypes.LineageOfBase(item.Code).Contains("map", StringComparer.OrdinalIgnoreCase)
            && item.Quality is ItemQuality.Magic or ItemQuality.Rare)
        {
            var home = ItemStats.Home(item);
            foreach (var stat in ItemStats.Every(ItemForge.PlainBase(item.Code.Trim(), 0, stampAs).Item))
            {
                if (!home.Stats.Any(each => each.Id == stat.Id))
                    home.Stats.Add(stat);
            }
        }

        if (!item.IsCompact && item.Id == 0)
            item.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
    }

    private static void Lost(string what, List<string> notes, ref bool whole)
    {
        notes.Add(what);
        whole = false;
    }

    // ---- the text's lines as stats --------------------------------------------------------------------

    private static IEnumerable<JsonElement> Entries(JsonElement pasted) =>
        pasted.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Array ? stats.EnumerateArray() : [];

    private static IEnumerable<JsonElement> Children(JsonElement pasted) =>
        pasted.TryGetProperty("socketed", out var inside) && inside.ValueKind == JsonValueKind.Array ? inside.EnumerateArray() : [];

    [GeneratedRegex(@"^(\d+)% Chance to cast level (\d+) (.+?) (on attack|on striking|when struck|when you Kill an Enemy|when you Die|when you Level-Up)$", RegexOptions.IgnoreCase)]
    private static partial Regex Cast();

    [GeneratedRegex(@"^Level (\d+) (.+?) Aura When Equipped$", RegexOptions.IgnoreCase)]
    private static partial Regex Aura();

    [GeneratedRegex(@"\((\d+)/(\d+) Charges\)", RegexOptions.IgnoreCase)]
    private static partial Regex Charges();

    private static readonly Dictionary<string, string> CastOn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["on attack"] = "item_skillonattack",
        ["on striking"] = "item_skillonhit",
        ["when struck"] = "item_skillongethit",
        ["when you Kill an Enemy"] = "item_skillonkill",
        ["when you Die"] = "item_skillondeath",
        ["when you Level-Up"] = "item_skillonlevelup",
    };

    /// <summary>
    /// One of the text's lines as the stat an item keeps - none for the game's extra "% Enhanced Damage" line,
    /// which says again what the two stats beside it hold, and none, with a note, for a line not understood.
    /// </summary>
    /// <param name="item">The item so far: its own skills settle whether a "+N to Skill" is an oskill.</param>
    private static IEnumerable<ItemStat> Stats(JsonElement entry, Item item, List<string> notes, ref bool whole)
    {
        string name = Clean(Text(entry, "name") ?? "");
        int value = Number(entry, "value") ?? 0;
        string skill = Clean(Text(entry, "skill") ?? "");

        // With its id: the stored number, as the save has it.
        if (Number(entry, "stat_id") is { } id)
        {
            if (StatCatalogue.NameOf(id) is { } known)
            {
                // A stat the save keeps a parameter beside has one - the text's, the editor's own lines giving it, else
                // nought, as a corruption's id has. One that packs a skill takes the text's skill, level and charges where
                // it gives them (GameClipboard.Write's lines; the game's have none); else it is given them where the item
                // is known (Subject).
                var stat = Stat(known, value);
                if (D2SLib.Core.TXT.ItemStatCostTXT[known] is { } row)
                {
                    int encode = row["Encode"].ToInt32();
                    if (encode is 2 or 3)
                    {
                        if (Number(entry, "skill_id") is { } skillId)
                        {
                            stat.SkillId = skillId;
                            stat.SkillLevel = Number(entry, "level");
                            if (encode == 3)
                                stat.MaxCharges = Number(entry, "max_charges") ?? value;
                        }
                    }
                    else if (StatEncoding.SaveParamBits(row) > 0)
                    {
                        stat.Param = Number(entry, "param") ?? 0;
                        if (Same(known, "item_addskill_tab"))
                            (stat.SkillTab, stat.CharacterClass) = (stat.Param & 0x7, (stat.Param >> 3) & 0x1fff);
                    }
                }

                return [stat];
            }

            Lost($"left off: {name} (stat {id})", notes, ref whole);
            return [];
        }

        // Chance to cast: the skill and the moment are only in the sentence.
        if (entry.TryGetProperty("chance", out _) && Cast().Match(name) is { Success: true } cast
            && CastOn.TryGetValue(cast.Groups[4].Value, out string? on) && SkillOf(cast.Groups[3].Value) is { } cast_skill)
        {
            return [Stat(on, int.Parse(cast.Groups[1].Value), cast_skill, int.Parse(cast.Groups[2].Value))];
        }

        // Charges: no name at all - the skill, its level, and "(45/45 Charges)".
        if (Text(entry, "charges") is { } charges && Charges().Match(charges) is { Success: true } held && SkillOf(skill) is { } charged)
        {
            return [Stat("item_charged_skill", int.Parse(held.Groups[1].Value), charged, Number(entry, "level") ?? 1,
                         int.Parse(held.Groups[2].Value))];
        }

        if (Aura().Match(name) is { Success: true } aura && SkillOf(aura.Groups[2].Value) is { } auraSkill)
        {
            var stat = Stat("item_aura", int.Parse(aura.Groups[1].Value));
            stat.Param = auraSkill;
            return [stat];
        }

        if (skill.Length > 0)
        {
            // A class's skills: the name is the Amazon's words whatever the class, which `skill` gives.
            if (name.Contains("Skill Levels", StringComparison.OrdinalIgnoreCase))
            {
                int classId = Enumerable.Range(0, CharacterClasses.Names.Count)
                    .FirstOrDefault(each => skill.StartsWith(CharacterClasses.Names[each], StringComparison.OrdinalIgnoreCase), -1);
                if (classId >= 0)
                {
                    var stat = Stat("item_addclassskills", value);
                    stat.Param = classId;
                    return [stat];
                }
            }
            else if (TabOf(skill) is { } tab)
            {
                var stat = Stat("item_addskill_tab", value);
                stat.Param = tab.Class * 8 + tab.Tab;
                stat.SkillTab = tab.Tab;
                stat.CharacterClass = tab.Class;
                return [stat];
            }
            else if (SkillOf(skill) is { } single)
            {
                // The text words an oskill and a class's own skill alike; the item's row knows which it has.
                bool other = ItemStats.Every(item).Any(each => Same(each.Stat, "item_nonclassskill") && (each.SkillId ?? each.Param) == single);
                var stat = Stat(other ? "item_nonclassskill" : "item_singleskill", value);
                stat.Param = single;
                stat.SkillId = single;
                return [stat];
            }
        }

        // Strength is stat 0, and the game leaves a 0 id out.
        if (Same(name, "to Strength"))
            return [Stat("strength", value)];

        if (Same(name, "% Enhanced Damage"))
            return [];

        Lost($"left off: {(name.Length > 0 ? name : skill)}", notes, ref whole);
        return [];
    }

    private static ItemStat Stat(string name, int value, int? skillId = null, int? skillLevel = null, int? maxCharges = null) =>
        new()
        {
            Id = StatCatalogue.IdOf(name),
            Stat = name,
            Value = value,
            SkillId = skillId,
            SkillLevel = skillLevel,
            MaxCharges = maxCharges,
        };

    // ---- names ---------------------------------------------------------------------------------------

    private static ItemQuality QualityOf(string? word) => (word ?? "").Trim().ToLowerInvariant() switch
    {
        "unique" => ItemQuality.Unique,
        "set" => ItemQuality.Set,
        "rare" => ItemQuality.Rare,
        "crafted" or "craft" => ItemQuality.Craft,
        "magic" => ItemQuality.Magic,
        "superior" => ItemQuality.Superior,
        "low quality" or "inferior" or "low" => ItemQuality.Inferior,
        _ => ItemQuality.Normal,
    };

    /// <summary>
    /// The game's colour codes taken out - "ÿc1Corrupted" - in the form they arrive, and in the two characters a
    /// file saved as UTF-8 turns the first into.
    /// </summary>
    private static string Clean(string text) =>
        ColourCode().Replace(text, "").Trim();

    [GeneratedRegex(@"(ÿ|Ã¿|�)c.")]
    private static partial Regex ColourCode();

    private static Dictionary<string, List<string>>? _codes;
    private static long _codesAt = -1;

    /// <summary>
    /// A base's code by the name the game shows. Several bases can share a name - an Amulet and Rathma's own -
    /// so the one <paramref name="near"/> names wins where it is among them, then one in its upgrade line, then
    /// the first.
    /// </summary>
    private static string? CodeOf(string type, string? near)
    {
        if (_codes is null || _codesAt != GameFiles.InstalledTables.Generation)
        {
            var codes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<TXTRow> rows =
            [
                .. D2SLib.Core.TXT.ItemsTXT.WeaponsTXT?.Rows ?? [],
                .. D2SLib.Core.TXT.ItemsTXT.ArmorTXT?.Rows ?? [],
                .. D2SLib.Core.TXT.ItemsTXT.MiscTXT?.Rows ?? [],
            ];

            foreach (var row in rows)
            {
                string code = row["code"]?.Value?.Trim() ?? "";
                if (code.Length == 0)
                    continue;

                string shown = ItemPresenter.BaseName(code);
                if (!codes.TryGetValue(shown, out var named))
                    codes[shown] = named = [];
                named.Add(code);
            }

            (_codes, _codesAt) = (codes, GameFiles.InstalledTables.Generation);
        }

        if (!_codes.TryGetValue(type, out var found) || found.Count == 0)
            return null;

        string own = near?.Trim() ?? "";
        if (own.Length > 0 && found.Contains(own, StringComparer.OrdinalIgnoreCase))
            return own;

        if (own.Length > 0)
        {
            for (string? up = ItemTier.Next(own); up is not null; up = ItemTier.Next(up))
            {
                if (found.Contains(up, StringComparer.OrdinalIgnoreCase))
                    return up;
            }
        }

        return found[0];
    }

    private static Dictionary<string, int>? _skills;

    /// <summary>A skill's id by the name the game shows.</summary>
    private static int? SkillOf(string shown)
    {
        if (_skills is null)
        {
            var skills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int id = 0; id < 1024; id++)
            {
                if (SkillNames.NameOf(id) is { Length: > 0 } name)
                    skills.TryAdd(name, id);
            }

            _skills = skills;
        }

        return _skills.TryGetValue(shown.Trim(), out int found) ? found : SkillNames.IdOf(shown);
    }

    /// <summary>
    /// A skill tree from the game's words for it, "Javalin and Spear Skills (Amazon)": the class in the brackets,
    /// then the tree of its three with the most words in common - the game's own text spells some otherwise.
    /// </summary>
    private static (int Class, int Tab)? TabOf(string shown)
    {
        int open = shown.LastIndexOf('(');
        if (open < 0 || !shown.EndsWith(')'))
            return null;

        string className = shown[(open + 1)..^1].Trim();
        int classId = Enumerable.Range(0, CharacterClasses.Names.Count)
            .FirstOrDefault(each => Same(CharacterClasses.Names[each], className), -1);
        if (classId < 0)
            return null;

        static HashSet<string> Words(string text) =>
            new(text.Split([' ', '-', ','], StringSplitOptions.RemoveEmptyEntries).Select(word => word.ToLowerInvariant()));

        var asked = Words(shown[..open]);
        var best = SkillTabs.For(classId)
            .Select((name, tab) => (Tab: tab, Alike: Words(name).Count(asked.Contains)))
            .OrderByDescending(each => each.Alike)
            .First();

        return best.Alike > 0 ? (classId, best.Tab) : null;
    }

    /// <summary>The set piece of that index name, made as the Forge makes it.</summary>
    private static Item? SetPiece(string name, string stampAs) =>
        ItemForge.SetItemNamed(name) is { } row ? ItemForge.SetItemAt(row, stampAs, RollAs.Maximum).Item : null;

    private static bool Same(string? one, string? other) =>
        string.Equals(one?.Trim(), other?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static int? Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(value.GetDouble())
            : null;
}
