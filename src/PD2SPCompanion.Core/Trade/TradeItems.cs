using System.Collections;
using System.Text.Json;
using D2SLib.Model.Save;
using D2SLib.Model.TXT;

namespace PD2SPCompanion.Core.Trade;

/// <summary>
/// An item rebuilt from a PD2 trade listing - see <see cref="TradeListings"/>.
/// <para>
/// A listing is a <b>decoded item</b>, not a tooltip: its base, quality, item level, flags,
/// sockets and every property as raw numbers. What it does not carry is what the game keeps as
/// ids - a unique's FileIndex, a rare's two name halves, a magic item's affixes. Those are found
/// here by the item's name, and where a name matches nothing the item is still built and a note
/// says what is missing.
/// </para>
/// <para>
/// <b>A runeword</b> is written the way the game keeps one: the runeword flag, the id its row's key
/// stands for (see <see cref="Runewords.IdOf"/>), and its own properties - the ones the listing's
/// <c>runeword.modifiers</c> names - in a second list of their own, the base's in the first.
/// </para>
/// </summary>
public static class TradeItems
{
    /// <summary>
    /// Each property list as the game writes one: in stat-id order, with no zero-valued line. Every
    /// one of the 102 game-written lists in real saves is in that order and none holds a zero;
    /// the game re-sorted an imported character's items and dropped a listing's
    /// "+0 to Minimum Fire Damage" when it saved. Keeps the runs that must be adjacent in id order.
    /// </summary>
    private static void AsTheGameWrites(Item item)
    {
        foreach (var list in item.StatLists)
        {
            list.Stats.RemoveAll(stat => stat.Value == 0 && (stat.Param ?? 0) == 0 && stat.SkillId is null);
        }

        ItemStats.InIdOrder(item);

        foreach (var inside in item.SocketedItems)
            AsTheGameWrites(inside);
    }

    /// <summary>
    /// How a note begins when the item is a unique or set piece no row of this season's tables
    /// names - Kadala's Heirloom, which PD2's own code builds for Gheed's map event in one of six
    /// variants (its name ships in patchstring.tbl; no row in uniqueitems has it). Written anyway,
    /// it would carry row 0's number and be that unique instead. Which number a real one keeps is
    /// open: a Kadala's Heirloom dropped in single player, dumped, settles it.
    /// </summary>
    public const string NotThisSeason = "no table row: ";

    /// <summary>
    /// How a note begins when no row has the listing's name, and the one unique or set piece of its base was taken for it:
    /// a row's index is a key, not the name shown, so it may be the item - or another with the listing's lines on it.
    /// </summary>
    public const string TakenByBase = "taken by its base: ";

    /// <summary>
    /// Why the item has no row of this season's to be (<see cref="NotThisSeason"/>), or null. Written anyway it would be
    /// another item, or will not write at all: what is made from a text or a listing is left out on this.
    /// </summary>
    public static string? NoRow(Forged forged) =>
        forged.Notes.FirstOrDefault(note => note.StartsWith(NotThisSeason, StringComparison.Ordinal));

    /// <summary>
    /// The item a listing describes, with notes on anything that could not be carried over.
    /// <see cref="Forged.Sound"/> is false when a property or a name was left off.
    /// </summary>
    /// <param name="item">The listing's <c>item</c>.</param>
    /// <param name="stampAs">
    /// The item version to stamp - see <see cref="ItemForge.Unique"/>. Left out, the listing's
    /// own <c>format_version</c>, which is the stamp the season writes (103 in S13).
    /// </param>
    /// <param name="shown">
    /// The item as the game's tooltip shows it, not as a save keeps it: what its runes and jewels
    /// grant counted in its own lines - "40% Chance of Crushing Blow" on an Infinity, from its two
    /// Bers. The listing's own lines, as they come. <b>For looking at only</b>: written to a save,
    /// the game would count the sockets a second time.
    /// </param>
    public static Forged Build(JsonElement item, string? stampAs = null, bool shown = false)
    {
        var notes = new List<string>();
        bool whole = true;
        string stamp = stampAs ?? Number(item, "format_version")?.ToString() ?? "";
        var built = Make(item, stamp, shown, notes, ref whole);
        AffixRanges.SplitSiteReanimate(built);
        AsTheGameWrites(built);

        // Rolled before a patch that moved where an affix may roll: its affixes are searched for as that build allowed
        // them, where this build's leave the item unexplained (MagicAffixes.Judging).
        if (MagicAffixes.EarlierBuilds > 0 && MagicAffixes.Carries(built) && Legitimacy.Judge(built).Count > 0)
        {
            for (int build = 0; build < MagicAffixes.EarlierBuilds; build++)
            {
                using (MagicAffixes.Judging(build))
                {
                    var thenNotes = new List<string>();
                    bool thenWhole = true;
                    var then = Make(item, stamp, shown, thenNotes, ref thenWhole);
                    AffixRanges.SplitSiteReanimate(then);
                    AsTheGameWrites(then);
                    if (Legitimacy.Judge(then).Count > 0)
                        continue;

                    (built, notes, whole) = (then, thenNotes, thenWhole);
                    break;
                }
            }
        }

        // Taken by its base, and its lines are not that row's: not the item asked for.
        if (!shown && notes.Any(note => note.StartsWith(TakenByBase, StringComparison.Ordinal)) && Legitimacy.Judge(built).Count > 0)
            whole = false;

        return new Forged(built, notes, whole);
    }

    private static Item Make(JsonElement listed, string stampAs, bool shown, List<string> notes, ref bool whole)
    {
        string code = Text(listed, "base_code") ?? "";

        // A rune, a gem, a potion: nothing but its code. What it grants in a socket is the
        // table's, not the item's.
        if (Flag(listed, "is_simple"))
            return ItemForge.Material(code, 1, stampAs);

        var item = new Item
        {
            Header = 0x4D4A,
            Code = code.PadRight(4, ' '),
            Quality = (ItemQuality)(Number(listed, "quality", "id") ?? (int)ItemQuality.Normal),
            Version = stampAs,
            Mode = ItemMode.Stored,
            Location = ItemLocation.None,
            Flags = new BitArray(32),
            ItemLevel = (byte)Math.Clamp(Number(listed, "item_level") ?? 1, 1, 99),
        };

        item.Flags[ItemForge.Identified] = Flag(listed, "is_identified");
        item.Flags[ItemForge.Always] = true;
        item.Flags[ItemForge.Ethereal] = Flag(listed, "is_ethereal");
        item.IsRuneword = Flag(listed, "is_runeword");

        // The picture, where the base has several: the listing's number when it gives one, the
        // first where it says false - as the forge does.
        item.HasMultipleGraphics = ItemTypes.VariantCount(code) > 1;
        if (item.HasMultipleGraphics && Number(listed, "graphic_id") is { } graphic)
            item.GraphicId = (byte)graphic;

        int sockets = Number(listed, "socket_count") ?? 0;
        item.TotalNumberOfSockets = (byte)Math.Clamp(sockets, 0, 15);
        item.Flags[ItemForge.Socketed] = sockets > 0 || Flag(listed, "is_socketed");

        if (Number(listed, "durability", "maximum") is { } most)
        {
            item.MaxDurability = (ushort)most;
            item.Durability = (ushort)Math.Min(Number(listed, "durability", "current") ?? most, most);
        }

        // How many, for a base that stacks - a throwing weapon, a quiver, a material. Left at nought,
        // an imported character's two Wind throwing knives drew red and vanished on the first throw:
        // PD2 takes nothing off a throw, but a stack of none has nothing to
        // throw. And a bought stack of three essences arrived as one. The site's number as it is.
        if (Stacks.IsStackable(item.Code) && Number(listed, "quantity") is { } quantity)
            item.Quantity = (ushort)Math.Clamp(quantity, 0, Stacks.FieldMaximum);

        // The stored figure: the base's roll, with ethereal and enhanced defence both applied
        // by the game on top of it.
        if (Number(listed, "defense", "base") is { } armor)
            item.Armor = (ushort)armor;

        // ---- what sits in its sockets --------------------------------------------------
        var socketed = new List<JsonElement>();
        if (listed.TryGetProperty("socketed", out var holes) && holes.ValueKind == JsonValueKind.Array)
        {
            foreach (var hole in holes.EnumerateArray())
            {
                socketed.Add(hole);
                ItemForge.Socket(item, Make(hole, stampAs, shown, notes, ref whole));
            }
        }

        // ---- its properties ------------------------------------------------------------
        // The listing's are the composed tooltip: what is socketed is counted in the item's own
        // lines and again under the socket. Taken off here, the item holds only its own - a
        // Hawk Helm reads 41% cold with three jewels giving 41 of it.
        var summedHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var own = Stats(listed, notes, ref whole, summedHere);

        foreach (var hole in shown ? [] : socketed)
        {
            bool ignored = true;
            foreach (var part in Stats(hole, [], ref ignored))
            {
                if (!SummedBySite(part) && !summedHere.Contains(part.Stat ?? ""))
                    continue;
                Subtract(own, part, notes);
            }
        }

        // An elemental damage run the item does not have was all the sockets' - a Rainbow
        // Facet's fire damage - and goes whole. Told by its minimum alone, the one member the
        // site sums (see SummedBySite): a Vampire Gaze with no fire of its own listed 26-46 over
        // jewels of 22-46 and 4-15, and taken apart as a sum it came out at 0 to -15, stored as
        // +2033 (a level 99 Barbarian).
        own.RemoveAll(stat => stat.Id is { } id && ItemStats.RunContaining(id) is { Count: > 1 } run
                              && (own.Any(each => each.Id == run[0])
                                  ? own.Where(each => each.Id == run[0]).All(each => each.Value <= 0)
                                  : run.All(member => own.Where(each => each.Id == member).All(each => each.Value <= 0))));

        // Every source of enhanced damage writes its minimum and maximum alike, so the item's own
        // minimum is its maximum; the listed minimum was one source's (see ElementLeaders).
        if (own.FirstOrDefault(stat => Same(stat.Stat, "item_maxdamage_percent")) is { } damageMost
            && own.FirstOrDefault(stat => Same(stat.Stat, "item_mindamage_percent")) is { } least)
            least.Value = damageMost.Value;

        if (Flag(listed, "corrupted")
            && !own.Any(stat => Same(stat.Stat, Corruptions.FlagStat)))
        {
            // The listing names the corruption by its id and leaves the flag beside it unsaid: the band's top, a roll the
            // game could have made (Corruptions.FlagFor).
            own.Add(Stat(Corruptions.FlagStat, Corruptions.FlagFor(item, own.FirstOrDefault(stat => Same(stat.Stat, Corruptions.IdStat))?.Value ?? 0, random: null)));
        }

        var list = ItemStats.Home(item);
        list.Stats.AddRange(own);

        // The base's automagic, by the id the listing gives as class_specifics: measured
        // on the 45 items of the ladder's top characters that give one, 44 name a row
        // of the base's own automagic group and the other a quiver's lower tier, as an upgraded
        // base keeps. Before the runeword, whose split leaves the automagic's share on the base.
        if (Number(listed, "class_specifics") is { } automagicId and > 0
            && AffixRanges.AutomagicGroupOf(automagicId) is not null)
        {
            item.IsAutoAffix = true;
            item.AutoAffixId = (ushort)automagicId;
        }

        if (item.IsRuneword && !shown)
            Runeword(item, listed, notes, ref whole);

        Name(item, listed, notes, ref whole);

        if (!shown)
        {
            Stored(item, listed, notes);
            SettleLevel(item, notes);

            // Once the affixes are known: what they roll decides a poison number the words leave open.
            SettlePoison(item);
        }

        // After Name: the corruption is looked up by the unique, which Name is what sets.
        // The site lists what a line says, and a line that says nothing is not listed: a
        // corrupted Annihilus comes without the `dye 10` every one of its corruptions writes,
        // which is its red tint: the trade site ignores the dye. Put back
        // from the corruption itself - only a silent stat, and only a fixed number, so nothing
        // is guessed and nothing the listing did print is doubled.
        foreach (var grant in Corruptions.On(item).Where(grant => grant.Min == grant.Max))
        {
            foreach (var wrote in AffixRanges.Written(grant.Code, grant.Min, grant.Max, grant.Min, grant.Param))
            {
                bool silent = D2SLib.Core.TXT.ItemStatCostTXT[wrote.Stat]?["descfunc"]?.Value is not { Length: > 0 } func
                              || func.Trim() == "0";

                if (silent && !ItemStats.Every(item).Any(stat => Same(stat.Stat, wrote.Stat)))
                    ItemStats.Home(item).Stats.Add(Stat(wrote.Stat, wrote.Value));
            }
        }

        // An elemental skill bonus as the game holds it: the site lists only its words, and the grant beside them
        // gives the levels (ElementSkills.GrantWorded) - before DamageKinds.Settle, which would take the words off.
        ElementSkills.GrantWorded(item);
        foreach (var inSocket in item.SocketedItems)
            ElementSkills.GrantWorded(inSocket);

        // A flat damage as the base holds it, in every list the item ends with: the site's "+9 to
        // Maximum Damage" is one number, written here for one-handed, two-handed and thrown alike on what
        // is no weapon, and on a weapon for the kinds it is - a War Pike the two-handed alone
        // (DamageKinds, where the rule and its measuring are). Armour read as a one-handed weapon
        // here would lose the other two.
        DamageKinds.Settle(item);
        foreach (var inSocket in item.SocketedItems)
            DamageKinds.Settle(inSocket);

        return item;
    }

    /// <summary>
    /// A runeword for a save: its row - by the key the listing gives, or by its runes and base -
    /// the id that row's key stands for, and the runeword's own properties moved out of the base's
    /// list into a second one, where the game keeps them.
    /// <para>
    /// Which properties are the runeword's is its row's to say, not the listing's: the site's
    /// <c>runeword.modifiers</c> left Infinity's 320% enhanced damage out, and it landed in the
    /// base's list. Both are asked, and either is enough. A stat the base shares with its
    /// runeword - a superior Infinity's enhanced damage - goes whole with the runeword, since the
    /// listing gives one sum and the game adds the two lists up the same either way.
    /// </para>
    /// </summary>
    private static void Runeword(Item item, JsonElement listed, List<string> notes, ref bool whole)
    {
        JsonElement word = default;
        bool described = listed.TryGetProperty("runeword", out word) && word.ValueKind == JsonValueKind.Object;

        // The game's own match first - its runes and the base's nearest type - and the site's key
        // only where the runes cannot say: the site named "Runeword163" (Wind, melee) for a Winged
        // Knife the game keeps as "Wind Throwable".
        var row = Runewords.On(item) ?? Runewords.ByKey(described ? Text(word, "key") : null);
        if (row is null || Runewords.IdOf(row) is not { } id)
        {
            notes.Add($"{NotThisSeason}no runeword this season matches its runes");
            whole = false;
            return;
        }

        item.RunewordId = (uint)id;
        item.RunewordListSlot = 5;

        var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A stat the row writes for a named skill is the runeword's for that skill only: a
        // Dominion wand's own +3 Blood Golem is the base's staff mod beside the runeword's +3
        // Golem Mastery, and moved with it by name it was checked against the runeword and
        // called a skill it does not grant (two Dominions).
        var keyed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in row.Mods)
        {
            foreach (var wrote in AffixRanges.Written(mod.Code, mod.Min, mod.Max, mod.Max, mod.Param))
            {
                own.Add(wrote.Stat);
                if (wrote.SkillId is not null || wrote.Param is not null)
                {
                    if (!keyed.TryGetValue(wrote.Stat, out var keys))
                        keyed[wrote.Stat] = keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    keys.Add(AffixRanges.KeyOf(wrote));
                }
            }
        }

        if (described && word.TryGetProperty("modifiers", out var mods) && mods.ValueKind == JsonValueKind.Array)
        {
            foreach (var mod in mods.EnumerateArray())
            {
                if (Text(mod, "name") is { Length: > 0 } stat)
                    own.Add(Merged.TryGetValue(stat, out string? property) ? property : stat);
            }
        }

        if (own.Count == 0)
        {
            notes.Add($"{NotThisSeason}nothing says which properties are the runeword's");
            whole = false;
            return;
        }

        var baseList = ItemStats.Home(item);
        var runewordList = new ItemStatList();

        // Where a superior base shares a stat with its runeword, the listing gives the sum and
        // the base keeps its own share: a superior Fortitude's 219% enhanced defence is the
        // runeword's 200 and the base's 19, and all of it on the runeword read as outside 200
        // (four Fortitudes and a Call to Arms). The runeword takes as much as its
        // range allows over the least the base could have; the base keeps the rest.
        // The automagic's share too: an Insight staff's 85% cast rate is the runeword's 35 and
        // the Staff Tier's 50. And a class base's staff mod, where it rolled the runeword's own
        // skill: a Dominion wand's +5 Golem Mastery is the runeword's 2-3 and the wand's own
        // (a level 99 Necromancer). Keyed by stat and skill, as the item's lines are.
        var superior = Ranges(QualityItems.For(item).Select(grant => (grant.Code, grant.Min, grant.Max, 0)));
        foreach (var (key, min, max, _) in AffixRanges.AutomagicGrants(item))
        {
            var had = superior.GetValueOrDefault(key);
            superior[key] = (had.Min + min, had.Max + max);
        }

        if (StaffMods.Rolls(item))
        {
            foreach (var stat in baseList.Stats.Where(stat => string.Equals(stat.Stat, StaffMods.Stat, StringComparison.OrdinalIgnoreCase)))
                superior.TryAdd(AffixRanges.KeyOf(stat), (StaffMods.Levels.Min, StaffMods.Levels.Max));
        }

        var ranged = Ranges(row.Mods.Select(mod => (mod.Code, mod.Min, mod.Max, mod.Param)));

        bool Mine(ItemStat stat) =>
            keyed.TryGetValue(stat.Stat ?? "", out var keys) ? keys.Contains(AffixRanges.KeyOf(stat)) : own.Contains(stat.Stat ?? "");

        // The base's own enhanced damage, where the listing says it exactly: the minimum it gives is
        // the first list's - the base's - wherever the base has a share (see ElementLeaders).
        int? baseDamage = listed.TryGetProperty("modifiers", out var listedMods) && listedMods.ValueKind == JsonValueKind.Array
            ? listedMods.EnumerateArray()
                .Where(mod => Same(Text(mod, "name"), "maxdamage_percent"))
                .Select(mod => mod.ValueKind == JsonValueKind.Object && mod.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array
                               && values.GetArrayLength() >= 2 && values[1].ValueKind == JsonValueKind.Number && values[1].TryGetInt32(out int first) ? (int?)first : null)
                .FirstOrDefault()
            : null;

        foreach (var stat in baseList.Stats.Where(Mine).ToList())
        {
            // The first source is the runeword itself when it is the whole: the base has no share -
            // a superior of the durability kind (a level 99 Necromancer's Call to Arms, [247, 202] with an Ohm).
            if (stat.Stat is "item_maxdamage_percent" or "item_mindamage_percent" && baseDamage is { } firstSource && firstSource >= stat.Value)
            {
                runewordList.Stats.Add(stat);
                baseList.Stats.Remove(stat);
                continue;
            }

            if (stat.Stat is "item_maxdamage_percent" or "item_mindamage_percent" && baseDamage is { } told
                && superior.TryGetValue(stat.Stat, out var shareRange) && told >= shareRange.Min && told <= shareRange.Max && told < stat.Value)
            {
                runewordList.Stats.Add(new ItemStat { Id = stat.Id, Stat = stat.Stat, Value = stat.Value - told });
                stat.Value = told;
                continue;
            }

            string name = AffixRanges.KeyOf(stat);

            // A run's maximum or length the base and the runeword both give is not a sum: the site
            // lists the first source's alone - the base's - and the runeword's own is not listed at
            // all. A Venom Fanged Knife reads poison [2190, 1270, 50]: the automagic's 1012-1270
            // over 50, Venom's 1024 over 50 and a Tal's 154 in the minimum only. Split as a sum, the
            // automagic kept 1270 - 1024 = 246 of its maximum (a level 99 Barbarian). The base keeps
            // the listed number; the runeword takes its row's.
            // Elemental runs only: enhanced damage's minimum has its own rule above.
            if (!SummedBySite(stat) && stat.Stat is not "item_mindamage_percent"
                && superior.ContainsKey(name) && ranged.TryGetValue(name, out var ownRun))
            {
                runewordList.Stats.Add(new ItemStat { Id = stat.Id, Stat = stat.Stat, Param = stat.Param, Value = ownRun.Min });
                continue;
            }

            if (superior.TryGetValue(name, out var mine) && ranged.TryGetValue(name, out var its)
                && stat.Value > its.Min)
            {
                // The runeword takes as much as its range allows, the base the rest - but no less
                // than the base's least where it has any. Reserving the base's least first left a
                // superior Pride 4% attack rating on its base, whose superior kind gives none
                // (a level 99 Druid): 264 is all Pride's 260-300.
                int share = Math.Min(stat.Value, its.Max);
                int rest = stat.Value - share;
                if (rest > 0 && rest < mine.Min && stat.Value - mine.Min >= its.Min)
                    share = stat.Value - mine.Min;
                if (share < stat.Value)
                {
                    runewordList.Stats.Add(new ItemStat { Id = stat.Id, Stat = stat.Stat, Param = stat.Param, SkillId = stat.SkillId, SkillLevel = stat.SkillLevel, Value = share });
                    stat.Value -= share;
                    continue;
                }
            }

            // A stat the runeword fixes, left below it once the sockets came off: the listing left
            // the socket out. Eternity's +61 minimum damage (its row's own 61) beside a Sol rune's
            // +9 came off as 52 (a level 99 Paladin) - once; four other items sum theirs.
            if (ranged.TryGetValue(AffixRanges.KeyOf(stat), out var fixedTo) && fixedTo.Min == fixedTo.Max
                && stat.Value > 0 && stat.Value < fixedTo.Min)
                stat.Value = fixedTo.Min;

            runewordList.Stats.Add(stat);
            baseList.Stats.Remove(stat);
        }

        while (item.StatLists.Count > 1)
            item.StatLists.RemoveAt(1);
        item.StatLists.Add(runewordList);
    }

    /// <summary>
    /// Of several rows with the item's name, the one its properties fit best - the fewest things
    /// the legitimacy check finds. Arm of King Leoric has two, one raising skeleton warriors and one
    /// archers, and the first was taken for an archer one (a level 99 Necromancer). The item's
    /// FileIndex is left as it was.
    /// </summary>
    private static GrailCatalog.Entry? Fittest(Item item, List<GrailCatalog.Entry> rows)
    {
        if (rows.Count <= 1)
            return rows.FirstOrDefault();

        uint kept = item.FileIndex;
        GrailCatalog.Entry? best = null;
        int fewest = int.MaxValue;
        foreach (var row in rows)
        {
            item.FileIndex = (uint)row.Row;
            // Judged, not checked: the item changes at each step, so a remembered answer never fits, and each was
            // written and kept for nothing. The same in every search here.
            int wrong = Legitimacy.Judge(item).Count;
            if (wrong < fewest)
                (fewest, best) = (wrong, row);
        }

        item.FileIndex = kept;
        return best;
    }

    /// <summary>
    /// What some properties write, by stat (and skill, where one is named - see
    /// <see cref="AffixRanges.KeyOf(string, int)"/>), as the least and most of their rolls added up.
    /// </summary>
    private static Dictionary<string, (int Min, int Max)> Ranges(IEnumerable<(string Code, int Min, int Max, int Param)> mods)
    {
        var ranges = new Dictionary<string, (int Min, int Max)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, min, max, param) in mods)
        {
            var low = AffixRanges.Written(code, min, max, min, param);
            var high = AffixRanges.Written(code, min, max, max, param);
            for (int i = 0; i < low.Count && i < high.Count; i++)
            {
                string key = AffixRanges.KeyOf(low[i]);
                var had = ranges.GetValueOrDefault(key);
                ranges[key] = (had.Min + Math.Min(low[i].Value, high[i].Value), had.Max + Math.Max(low[i].Value, high[i].Value));
            }
        }

        return ranges;
    }

    /// <summary>
    /// The unique, set piece, rare name or magic affixes the listing's name stands for - the
    /// ids a real item keeps and a listing leaves out.
    /// </summary>
    private static void Name(Item item, JsonElement listed, List<string> notes, ref bool whole)
    {
        string name = Text(listed, "name") ?? "";
        string code = item.Code.Trim();

        switch (item.Quality)
        {
            case ItemQuality.Inferior:
            case ItemQuality.Superior:
                // Which of the kinds - "Cracked", "Superior" - is the three bits of FileIndex.
                item.FileIndex = (uint)(Number(listed, "quality", "sub_id") ?? 0);
                break;

            case ItemQuality.Unique:
            case ItemQuality.Set:
            {
                var entries = item.Quality == ItemQuality.Unique ? GrailCatalog.Uniques() : GrailCatalog.Sets();
                var onBase = entries.Where(entry => Same(entry.Item.Code?.Trim(), code)).ToList();

                // By name and base; then by name alone - an upgraded unique keeps its row on the
                // better base, and a Skull Collector cubed to an Archon Staff is still Skull
                // Collector, not the staff's own Mang Song's Lesson; the row that fits best where
                // several share the name: two rows are Arm of King Leoric, and one cubed to a Lich
                // Wand, asked for a single one, went to the Lich Wand's own Boneshade
                // (a level 96 Necromancer). Then by base alone, where one row has it - a row's
                // index is a key, not the name the game shows.
                var named = entries.Where(entry => Same(GrailCatalog.ShownName(entry), name)).ToList();
                var match = Fittest(item, [.. onBase.Where(entry => Same(GrailCatalog.ShownName(entry), name))])
                            ?? Fittest(item, named);
                if (match is null && onBase.Count == 1)
                {
                    match = onBase[0];
                    notes.Add($"{TakenByBase}{name} as {GrailCatalog.ShownName(match)}");
                }

                // Kadala's Heirloom: its row names no base and is not enabled, so no catalogue
                // entry has it - but the row is there, and a ring written with its number reads
                // right in game (see KadalasHeirloom).
                if (match is null && item.Quality == ItemQuality.Unique && KadalasHeirloom.FileIndex >= 0
                    && Same(ItemPresenter.Describe(new Item { Code = code.PadRight(4, ' '), Quality = ItemQuality.Unique, FileIndex = (uint)KadalasHeirloom.FileIndex, Flags = new BitArray(32) }).Name, name))
                {
                    item.FileIndex = (uint)KadalasHeirloom.FileIndex;
                }
                else if (match is null)
                {
                    notes.Add($"{NotThisSeason}no {item.Quality.ToString().ToLowerInvariant()} called {name} on this base");
                    whole = false;
                }
                else
                {
                    item.FileIndex = (uint)match.Row;
                }

                break;
            }

            case ItemQuality.Rare:
            case ItemQuality.Craft:
                // The site does not decode a rare's name: every rare listing seen is called by its
                // base - "Hawk Helm", "Matriarchal Pike". Left unnamed, the item is called the same
                // here. Writing one to a save will need a name picked.
                if (!Same(name, ItemPresenter.BaseName(code)) && !RareName(item, name))
                {
                    notes.Add($"no rare name {name}");
                    whole = false;
                }

                break;

            case ItemQuality.Magic:
                if (!MagicName(item, name))
                {
                    notes.Add($"no magic affixes named {name}");
                    whole = false;
                }

                break;
        }
    }

    /// <summary>
    /// What an item needs to be written that a listing does not give, for the one going into a
    /// save - the shown one is left as the site shows it.
    /// <list type="bullet">
    /// <item>A rare the site calls by its base has no name. It gets one from the halves its base
    /// allows, as a craft does - drawn from the listed item's own id, so the same listing always
    /// comes out the same. It can be renamed in the item panel.</item>
    /// <item>The listed item's own identifier, sockets included - see <see cref="GameIdOf"/> - so
    /// the same listing bought twice is the same item twice, and says so as any duplicate does. A
    /// fresh one where the listing gives none. The character may still give the top one another
    /// if it clashes.</item>
    /// </list>
    /// </summary>
    private static void Stored(Item item, JsonElement listed, List<string> notes)
    {
        var random = new Random(listed.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out long seed)
            ? (int)(seed & int.MaxValue)
            : 0);

        if (item.Quality is ItemQuality.Rare or ItemQuality.Craft && item.RarePrefixId == 0)
        {
            var first = AffixNames.FirstNames(item);
            var second = AffixNames.SecondNames(item);

            if (first.Count > 0 && second.Count > 0)
            {
                item.RarePrefixId = (ushort)first[random.Next(first.Count)].Id;
                item.RareSuffixId = (ushort)second[random.Next(second.Count)].Id;
                notes.Add($"named {AffixNames.NameFrom(item.RarePrefixId, item.RareSuffixId)}, the listing giving no name");
            }
        }

        if (!item.IsCompact)
            item.Id = GameIdOf(listed) ?? (uint)Random.Shared.NextInt64(1, uint.MaxValue);

        // The base's automagic, where its properties show it - a melee weapon's splash, a
        // staff's casting speed. Without it they are bare properties nothing accounts for.
        if (!item.IsAutoAffix && AffixRanges.AutomagicFor(item) is { } automagic)
        {
            item.IsAutoAffix = true;
            item.AutoAffixId = (ushort)automagic;
        }

        // A rare's or a craft's affixes, from the ranges the listing gives - a magic item's came
        // from its name already. After the automagic, whose share the search takes off first.
        if (item.Quality is ItemQuality.Rare or ItemQuality.Craft)
        {
            if (AffixSearch.Find(item, Lines(item, listed)) is { } found)
                AffixSearch.Apply(item, found);
            else
                notes.Add("its affixes could not be told from the listing");

            SocketPrefix(item, notes);
        }
    }

    /// <summary>
    /// The socket prefix a rare or crafted item must have rolled, where nothing else explains its
    /// sockets. Mechanist's (1-2), Artificer's (3) and Jeweler's (4) write
    /// no property, only the count - so no listing names them and the affix search, which reads
    /// properties, cannot find them. A real save keeps the prefix; without it the legitimacy check
    /// (<see cref="Legitimacy.SocketsAllowed"/>) called a real rare Diadem with three sockets and a
    /// non-socket corruption forged. Only where the Malus, the Puzzlebox and a socket corruption
    /// all fall short, in a free prefix slot, within the item's affix count, and only a prefix
    /// the game could have rolled there: its type, its group free, its level within reach.
    /// </summary>
    private static void SocketPrefix(Item item, List<string> notes)
    {
        int sockets = item.TotalNumberOfSockets;
        if (sockets <= 0 || Legitimacy.SocketsAllowed(item).Most >= sockets)
            return;

        var prefixes = item.MagicPrefixIds;
        int carried = prefixes.Count(id => id > 0) + (item.MagicSuffixIds ?? []).Count(id => id > 0);
        int slot = Array.IndexOf(prefixes, (ushort)0);
        if (slot < 0 || carried >= MagicAffixes.CountFor(item).Most)
        {
            notes.Add($"{sockets} sockets, and no room for the socket prefix that would explain them");
            return;
        }

        int cap = ItemBases.MaximumSockets(item);
        int reach = MagicAffixes.AffixLevelsAcrossUpgrades(item).DefaultIfEmpty(MagicAffixes.AffixLevel(item)).Max();

        var fit = MagicAffixes.Available(item, prefix: true, forSlot: slot, outgrown: true)
            .Where(affix => affix.Level <= reach)
            .SelectMany(affix => affix.Grants
                .Where(grant => grant.Code.Equals("sock", StringComparison.OrdinalIgnoreCase))
                .Select(grant => (Affix: affix,
                                  Least: grant.Param > 0 ? grant.Param : grant.Min,
                                  Most: grant.Param > 0 ? grant.Param : Math.Max(grant.Min, grant.Max))))
            .Where(each => sockets >= (cap > 0 ? Math.Min(each.Least, cap) : each.Least)
                           && sockets <= (cap > 0 ? Math.Min(each.Most, cap) : each.Most))
            .OrderBy(each => each.Affix.Level)
            .Select(each => each.Affix)
            .FirstOrDefault();

        if (fit is null)
        {
            notes.Add($"{sockets} sockets, and no socket prefix this item could roll gives them");
            return;
        }

        prefixes[slot] = (ushort)fit.Id;
        notes.Add($"{fit.Name} in prefix slot {slot + 1}, for its {sockets} sockets - the listing names no socket prefix");
    }

    /// <summary>
    /// The listing's lines as the affix search reads them: each property's key, the range the site
    /// gives beside it where it gives one, and what the item holds of it - its own share, the
    /// sockets' taken off. A corruption's lines are the corruption's, not an affix's, and a
    /// property the sockets gave all of is left out.
    /// </summary>
    private static List<AffixSearch.Line> Lines(Item item, JsonElement listed)
    {
        var lines = new List<AffixSearch.Line>();
        if (!listed.TryGetProperty("modifiers", out var modifiers) || modifiers.ValueKind != JsonValueKind.Array)
            return lines;

        var held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var stat in item.StatLists.FirstOrDefault()?.Stats ?? [])
        {
            string key = AffixSearch.KeyOf(stat);
            held[key] = held.GetValueOrDefault(key) + AffixSearch.Counted(stat.Stat ?? "", stat.Value);
        }

        foreach (var modifier in modifiers.EnumerateArray())
        {
            string name = Text(modifier, "name") ?? "";
            // The site's "corrupted" line is the flag.
            if (name.Length == 0 || name.Equals("corrupted", StringComparison.OrdinalIgnoreCase)
                || name.Equals(Corruptions.FlagStat, StringComparison.OrdinalIgnoreCase))
                continue;

            // A line it marks corrupted is the corruption's and maybe an affix's too: with a range
            // beside it the range is the affix's and the value the sum of both - a ring's Fire
            // Resist 35 with 21-30 beside it is 29 of an affix and 6 of a corruption's all
            // resistances (a level 99 Assassin). Bare, it is a fixed line the search takes the
            // corruption's share off. Left out, as it was, no row writing it could be chosen.

            string label = Text(modifier, "label") ?? name;
            double[] values = modifier.TryGetProperty("values", out var raw) && raw.ValueKind == JsonValueKind.Array
                ? [.. raw.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0)]
                : [];
            if (values.Length == 0)
                continue;

            double? low = modifier.TryGetProperty("min", out var lowest) && lowest.ValueKind == JsonValueKind.Number ? lowest.GetDouble() : null;
            double? high = modifier.TryGetProperty("max", out var highest) && highest.ValueKind == JsonValueKind.Number ? highest.GetDouble() : null;
            bool ranged = low is not null && high is not null;

            var now = Read(name, label, values);
            var least = ranged ? Read(name, label, WithValue(name, values, low!.Value)) : now;
            var most = ranged ? Read(name, label, WithValue(name, values, high!.Value)) : now;

            for (int i = 0; i < now.Count; i++)
            {
                string key = AffixSearch.KeyOf(now[i]);
                if (!held.TryGetValue(key, out int value) || value == 0)
                    continue;

                if (ranged && i < least.Count && i < most.Count)
                {
                    int min = Math.Min(least[i].Value, most[i].Value), max = Math.Max(least[i].Value, most[i].Value);
                    lines.Add(new AffixSearch.Line(key, min, max, min != max, value));
                }
                else if (ShownPoison(name, label, values) && now[i].Stat is "poisonmindam" or "poisonmaxdam")
                {
                    // Said as the tooltip shows it: every stored number showing the same is the line (PoisonSpan) - a
                    // War Javelin's 50 over 5 seconds is of Pestilence's 103, where the nearest, 102, fitted no row.
                    var span = PoisonSpan(values[i == 0 ? 0 : 1], (int)Math.Round(values[2] * FramesPerSecond));
                    lines.Add(new AffixSearch.Line(key, value, value, false, value,
                        value - now[i].Value + span.Low, value - now[i].Value + span.High));
                }
                else
                {
                    lines.Add(new AffixSearch.Line(key, value, value, false, value));
                }
            }
        }

        return lines;
    }

    /// <summary>
    /// The listing's numbers with its <b>value</b> put in another's place - the range's ends, read
    /// the same way the value is. Which place holds the value depends on the property: every one
    /// for a run or a merged line, the chance for a skill on hit or charges, the second for a
    /// property whose first number is its parameter, the first otherwise.
    /// </summary>
    private static double[] WithValue(string name, double[] values, double value)
    {
        double[] copy = [.. values];

        if (Runs.ContainsKey(name) || Merged.ContainsKey(name))
        {
            for (int i = 0; i < copy.Length; i++)
                copy[i] = value;
            return copy;
        }

        var row = D2SLib.Core.TXT.ItemStatCostTXT[name];
        int encode = row?["Encode"].ToInt32() ?? 0;
        int at = encode is 2 or 3 ? 2
            : row is not null && StatEncoding.SaveParamBits(row) > 0 && copy.Length >= 2 ? 1
            : 0;

        if (at < copy.Length)
            copy[at] = value;
        return copy;
    }

    /// <summary>
    /// The game's 32-bit identifier a listing's <c>item.id</c> stands for, or null without one.
    /// <para>
    /// The site writes the id's eight hex digits one after another <b>in decimal</b>: 0xACF4FAEA
    /// is "10 12 15 4 15 10 14 10", run together as 101215415101410. Measured on 41 cached
    /// listings: every one splits into exactly eight digits of 0 to 15, and their lengths run 8
    /// to 15 as that reading predicts; read as four bytes instead, 35 of 41 do not split at all.
    /// </para>
    /// <para>
    /// A "1" followed by a digit from 0 to 5 can be one digit or two, so 12 of the 41 split more
    /// than one way and the true id cannot be told. The first split in a fixed order - single
    /// digits before pairs - is taken: always the same for the same listing, and never the same
    /// for two different ones, since a split writes back to only the string it came from. That is
    /// what an id is needed for here - telling the same item from another.
    /// </para>
    /// </summary>
    public static uint? GameIdOf(JsonElement listed)
    {
        if (!listed.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
            return null;

        string digits = id.GetRawText();
        return digits.All(char.IsAsciiDigit) && Split(digits, 0, 0, 0) is { } found ? found : null;

        // Eight hex digits from here on, single digits tried first.
        static uint? Split(string digits, int at, int taken, uint value)
        {
            if (at == digits.Length)
                return taken == 8 ? value : null;
            if (taken == 8)
                return null;

            if (Split(digits, at + 1, taken + 1, (value << 4) | (uint)(digits[at] - '0')) is { } single)
                return single;

            if (digits[at] == '1' && at + 1 < digits.Length && digits[at + 1] is >= '0' and <= '5')
                return Split(digits, at + 2, taken + 1, (value << 4) | (uint)(10 + digits[at + 1] - '0'));

            return null;
        }
    }

    /// <summary>A rare's two halves, "Hawk" and "Helm", found by the words the game shows.</summary>
    private static bool RareName(Item item, string name)
    {
        foreach (var first in AffixNames.FirstNames(item, ""))
        {
            string said = AffixNames.NameFrom(first.Id, 0);
            if (said.Length == 0 || !name.StartsWith(said + " ", StringComparison.OrdinalIgnoreCase))
                continue;

            string rest = name[(said.Length + 1)..];
            var second = AffixNames.SecondNames(item, "")
                .FirstOrDefault(half => Same(AffixNames.NameFrom(0, half.Id), rest));

            if (second is null)
                continue;

            item.RarePrefixId = (ushort)first.Id;
            item.RareSuffixId = (ushort)second.Id;
            return true;
        }

        return false;
    }

    /// <summary>
    /// A magic item's prefix and suffix: its name either side of the base's. Several rows share a
    /// name - a level, a group or a kind of base apart - and the pair taken is the one the
    /// legitimacy check finds least wrong with: the rows this base can carry, at this level, with
    /// the item's own numbers inside their ranges. Its green brackets are read from that pair.
    /// </summary>
    private static bool MagicName(Item item, string name)
    {
        string baseName = ItemPresenter.BaseName(item.Code.Trim());
        int at = name.IndexOf(baseName, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return false;

        string before = name[..at].Trim(), after = name[(at + baseName.Length)..].Trim();

        IReadOnlyList<int> prefixes = before.Length == 0 ? [0] : AffixNames.MagicIdsNamed(prefix: true, before);
        IReadOnlyList<int> suffixes = after.Length == 0 ? [0] : AffixNames.MagicIdsNamed(prefix: false, after);
        if (prefixes.Count == 0 || suffixes.Count == 0)
            return false;

        int fewest = int.MaxValue;
        (int Prefix, int Suffix) best = (prefixes[0], suffixes[0]);
        byte listedLevel = item.ItemLevel;

        foreach (int prefix in prefixes)
        {
            foreach (int suffix in suffixes)
            {
                item.MagicPrefixIds[0] = (ushort)prefix;
                item.MagicSuffixIds[0] = (ushort)suffix;

                // Each pair at the item level that suits it best - the listing's is not the
                // item's (see SettleLevel), and at 96 a staff's Lizard's (mana 3-5, up to level
                // 43) was as wrong as a never-rolled row of the same name, which then won the tie.
                // A pair the item could never carry at any level is not searched for one.
                item.ItemLevel = listedLevel;
                var findings = Legitimacy.Judge(item);
                int wrong = findings.Count == 0 || findings.Any(finding => finding.Certainty == Certainty.Impossible)
                    ? findings.Count
                    : SettleLevel(item, null);
                if (wrong < fewest)
                    (fewest, best) = (wrong, (prefix, suffix));
            }
        }

        item.ItemLevel = listedLevel;
        item.MagicPrefixIds[0] = (ushort)best.Prefix;
        item.MagicSuffixIds[0] = (ushort)best.Suffix;
        return true;
    }

    /// <summary>
    /// The item level the item most likely has, set on it; returns what the legitimacy check
    /// still finds there.
    /// <para>
    /// <b>The site does not give it.</b> Its <c>item_level</c> reads 96 on 1,148 of the 1,180
    /// items of the ladder's top characters and 97 on the rest - small charms of
    /// vita, magic staves and uniques alike - and 60 of 61 market listings read 96. Taken as
    /// written, a staff holding Teleport at level 1 (which its item level sets - see
    /// <see cref="AffixRanges.PackedFrom"/>) and an "of Balance" that stops at affix level 45
    /// were both called impossible, on live items.
    /// </para>
    /// <para>
    /// So the listed level stands where nothing on the item disputes it, and otherwise the
    /// nearest level at which the fewest things are wrong is taken - below it first.
    /// </para>
    /// </summary>
    private static int SettleLevel(Item item, List<string>? notes)
    {
        byte listed = item.ItemLevel;
        var here = Legitimacy.Judge(item);
        int fewest = here.Count;
        byte best = listed;

        // Nothing the level moves, where the findings read the same at both ends of it: an
        // unmatched runeword is unmatched at any level, and walking all of them took the stress
        // run from one minute to four.
        if (fewest > 0)
        {
            item.ItemLevel = 1;
            var lowest = Legitimacy.Judge(item);
            item.ItemLevel = 99;
            var highest = Legitimacy.Judge(item);
            item.ItemLevel = listed;

            if (lowest.Select(finding => finding.Said).SequenceEqual(here.Select(finding => finding.Said))
                && highest.Select(finding => finding.Said).SequenceEqual(here.Select(finding => finding.Said)))
                return fewest;
        }

        for (int apart = 1; apart < 99 && fewest > 0; apart++)
        {
            foreach (int level in new[] { listed - apart, listed + apart })
            {
                if (level is < 1 or > 99)
                    continue;

                item.ItemLevel = (byte)level;
                int wrong = Legitimacy.Judge(item).Count;
                if (wrong < fewest)
                    (fewest, best) = (wrong, (byte)level);
            }
        }

        item.ItemLevel = best;
        if (best != listed)
            notes?.Add($"item level {best}, the listing's {listed} being the site's own");

        return fewest;
    }

    // ---- properties --------------------------------------------------------------------

    /// <summary>
    /// Names the site says for several stats at once, and the property that writes them - which
    /// this season's own table then spreads the way an item really holds them: "all_resist" is
    /// four resistances, "max_damage" a copy for each way of holding a weapon.
    /// </summary>
    private static readonly Dictionary<string, string> Merged = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all_resist"] = "res-all",

        // Harlequin Crest's "+2 to All Attributes": left off, every Shako bought came back
        // without its four attributes and read "cannot be recreated exactly".
        ["all_attributes"] = "all-stats",
        ["max_damage"] = "dmg-max",
        ["min_damage"] = "dmg-min",
    };

    /// <summary>
    /// Names the site gives a run of stats by - "colddam" is Adds 73-308 Cold Damage - and the
    /// stat leading it. The numbers are the run's members in order: minimum, maximum and, for cold
    /// and poison, the length.
    /// </summary>
    private static readonly Dictionary<string, string> Runs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["maxdamage_percent"] = "item_maxdamage_percent",
        ["firedam"] = "firemindam",
        ["lightdam"] = "lightmindam",
        ["magicdam"] = "magicmindam",
        ["colddam"] = "coldmindam",
        ["poisondam"] = "poisonmindam",
    };

    /// <param name="summed">Filled with the run members the listing gave summed over the sockets too.</param>
    private static List<ItemStat> Stats(JsonElement listed, List<string> notes, ref bool whole, HashSet<string>? summed = null)
    {
        var stats = new List<ItemStat>();
        var lines = new List<List<ItemStat>>();

        if (!listed.TryGetProperty("modifiers", out var modifiers) || modifiers.ValueKind != JsonValueKind.Array)
            return stats;

        foreach (var modifier in modifiers.EnumerateArray())
        {
            string name = Text(modifier, "name") ?? "";
            string label = Text(modifier, "label") ?? name;
            double[] values = modifier.TryGetProperty("values", out var raw) && raw.ValueKind == JsonValueKind.Array
                ? [.. raw.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0)]
                : [];

            var made = Read(name, label, values);
            if (made.Count == 0)
            {
                notes.Add($"left off: {label}");
                whole = false;
                continue;
            }

            lines.Add(made);
        }

        // A run's member said again on a line of its own is the same number twice: a small charm
        // "of Frost" lists colddam [1, 2, 25] and coldlength [25], and added up it held a length of
        // 50 no row gives (two Snowy charms and an "of Frost", the ladder). Runs first,
        // so the order the site lists them in does not matter.
        var fromRuns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var made in lines.Where(made => made.Count > 1))
        {
            foreach (var stat in made)
            {
                Add(stats, stat);
                fromRuns.Add(stat.Stat ?? "");
            }
        }

        foreach (var made in lines.Where(made => made.Count == 1))
        {
            var stat = made[0];
            if (fromRuns.Contains(stat.Stat ?? "") && stat.Id is { } id && ItemStats.IsInRun(id)
                && stats.FirstOrDefault(each => ItemStats.SameFields(each, stat)) is { } already)
            {
                // Different, it is the sum over the item and its sockets, where the run's copy is
                // the first source's alone: a Zenith lists colddam [433, 500, 100] and coldlength
                // [114] - its own 100 and a Thul's 14 (a level 99 Druid). The sum is kept,
                // and the sockets come off it (see Make).
                if (already.Value != stat.Value)
                {
                    already.Value = stat.Value;
                    summed?.Add(stat.Stat ?? "");
                }

                continue;
            }

            Add(stats, stat);
        }

        return stats;
    }

    /// <summary>One of the listing's lines as the stats an item keeps; none for a name not known.</summary>
    private static List<ItemStat> Read(string name, string label, double[] values)
    {
        if (values.Length == 0)
            return [];

        int first = (int)Math.Round(values[0]);

        if (Merged.TryGetValue(name, out string? property))
        {
            return [.. AffixRanges.Written(property, first, first, first)
                .Select(wrote => Stat(wrote.Stat, wrote.Value))];
        }

        // Season 14's site gives poison as the tooltip says it, "+75 Poison Damage over 5 Seconds" as [75, 75, 5]; Season
        // 13's gave the save's numbers, poisonmindam [65, 65, 50] beside the words "13 poison damage over 2 seconds"
        // (the S14 ladder: 440 poison lines read as save numbers, a charm's 65 as 13 and its 50 frames as
        // 2). Told by the words: the seconds said are the third number. See SettlePoison for the stored value.
        if (ShownPoison(name, label, values))
        {
            int frames = (int)Math.Round(values[2] * FramesPerSecond);
            return RunFrom("poisonmindam", [PoisonStored(values[0], frames), PoisonStored(values[1], frames), frames]);
        }

        if (Runs.TryGetValue(name, out string? leader))
        {
            // Enhanced damage is one number said once, "+320% Enhanced Damage", and both halves
            // hold it. The site's second figure has been seen short of the first on a superior
            // runeword - [320, 6] - where the game's line reads 320.
            if (leader.Equals("item_maxdamage_percent", StringComparison.OrdinalIgnoreCase))
                values = [values[0], values[0]];

            return RunFrom(leader, values);
        }

        var row = D2SLib.Core.TXT.ItemStatCostTXT[name];
        if (row is null)
            return [];

        // A stat the site gives as the whole of its run - poison as [600, 750, 50].
        if (values.Length > 1 && StatCatalogue.IdOf(name) is { } id && ItemStats.RunContaining(id) is { Count: > 1 } run
            && run[0] == id)
        {
            return RunFrom(name, values);
        }

        int encode = row["Encode"].ToInt32();
        int paramBits = StatEncoding.SaveParamBits(row);
        int Whole(int at) => at < values.Length ? (int)Math.Round(values[at]) : 0;

        // Level, skill, chance: "25% Chance to cast level 20 Chain Lightning on striking".
        if (encode == 2)
            return [Stat(name, Whole(2), skillId: Whole(1), skillLevel: Whole(0))];

        // Level, skill, charges, the most charges: "Level 2 Oak Sage (6/27 Charges)".
        if (encode == 3)
            return [Stat(name, Whole(2), skillId: Whole(1), skillLevel: Whole(0), maxCharges: Whole(3))];

        // The parameter first - the skill, the aura, the class - then the number.
        if (paramBits > 0 && values.Length >= 2)
        {
            var stat = Stat(name, Whole(1));
            stat.Param = Whole(0);

            // A skill tree keeps the same number twice: as the parameter the file writes, and
            // split into the two fields the wording reads. As MagicAffixes does.
            if (name.Equals("item_addskill_tab", StringComparison.OrdinalIgnoreCase))
            {
                stat.SkillTab = stat.Param & 0x7;
                stat.CharacterClass = (stat.Param >> 3) & 0x1fff;
            }

            return [stat];
        }

        return [Stat(name, PerLevel(name, row, values[0]))];
    }

    /// <summary>
    /// A per-level stat as it is stored. The site is not consistent about these: where the
    /// game's <c>op</c> is 2 it gives the figure per level - Infinity's Vitality "0.5", stored 4;
    /// Eaglehorn's attack rating "6", stored 12 - and for the others the stored number itself,
    /// Eaglehorn's "16" for 2% enhanced maximum damage per level. Measured.
    /// </summary>
    private static int PerLevel(string name, TXTRow row, double value)
    {
        int shift = StatText.PerLevelShift(name);
        return shift > 0 && row["op"].ToInt32() == 2
            ? (int)Math.Round(value * (1 << shift))
            : (int)Math.Round(value);
    }

    /// <summary>The game's frames a second, which a poison length counts in.</summary>
    private const int FramesPerSecond = 25;

    /// <summary>
    /// A poison line as the tooltip words it - total damage and seconds - not as the save keeps it: Season 14's site,
    /// "+75 Poison Damage over 5 Seconds" for [75, 75, 5], "Adds 117-146 Poison Damage over 2 Seconds" for a range.
    /// Season 13's gave the same name the save's numbers with words glued on, "Adds 20 Poison Damage over 50 Seconds"
    /// for [20, 20, 50] - 50 frames (a level 97 Amazon's charm, the S13 ladder). The words do not tell them; the length does: a
    /// second is 25 frames and no poison runs shorter, so a third number under 25 is seconds. The site's season, not
    /// the item's: a legacy item on today's site is said today's way.
    /// </summary>
    private static bool ShownPoison(string name, string label, double[] values) =>
        name.Equals("poisondam", StringComparison.OrdinalIgnoreCase) && values.Length >= 3
        && values[2] is > 0 and < FramesPerSecond
        && label.Contains("second", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The damage the tooltip shows for a stored poison number over a length: the stored is a frame's, in 256ths,
    /// rounded - a Pestilence grand charm's 165 over 50 frames is 32.2 and reads 32; a small one's 65 is 12.7, 13.
    /// </summary>
    private static int PoisonShown(int stored, int frames) => (int)Math.Round(stored * frames / 256.0, MidpointRounding.AwayFromZero);

    /// <summary>The nearest stored number to a damage the tooltip shows; several may show alike (SettlePoison).</summary>
    private static int PoisonStored(double shown, int frames) => frames > 0 ? (int)Math.Round(shown * 256 / frames) : 0;

    /// <summary>Every stored number that shows as that damage over that length: the least and the most.</summary>
    private static (int Low, int High) PoisonSpan(double shown, int frames)
    {
        int guess = PoisonStored(shown, frames), said = (int)Math.Round(shown);
        int low = guess, high = guess;
        while (low > 0 && PoisonShown(low - 1, frames) == said)
            low--;
        while (PoisonShown(high + 1, frames) == said)
            high++;
        return (low, high);
    }

    /// <summary>
    /// A poison number read from the tooltip's words (<see cref="ShownPoison"/>) stands for every stored number that
    /// shows the same - 13 over 2 seconds is 65, 66 or 67 - and the nearest is a guess. Where it falls outside what the
    /// item's sources roll and one of the others falls inside, that one is it: a small charm of Pestilence is 65, the
    /// row's own. Nothing changes where the guess fits, or where nothing that shows alike would.
    /// </summary>
    private static void SettlePoison(Item item)
    {
        if (item.StatLists.FirstOrDefault() is not { } own
            || own.Stats.FirstOrDefault(stat => Same(stat.Stat, "poisonlength")) is not { Value: > 0 } length)
            return;

        // The site sums the minimum over the item and its sockets, each shown rounded: taken apart, the item's own share
        // may be off by a shown point a socket - a Plague Bearer over four poison facets came out 1016 for its row's
        // 1024 (a level 93 Druid).
        int slack = item.SocketedItems.Count(inside => inside.StatLists.Any(list => list.Stats.Any(stat => Same(stat.Stat, "poisonmindam"))));

        var ranges = AffixRanges.For(item);
        foreach (var stat in own.Stats.Where(stat => Same(stat.Stat, "poisonmindam") || Same(stat.Stat, "poisonmaxdam")))
        {
            if (!ranges.TryGetValue(AffixRanges.KeyOf(stat), out var range) || range.Holds(stat.Value))
                continue;

            int shown = PoisonShown(stat.Value, length.Value);
            int? fits = Enumerable.Range(range.Min, Math.Max(0, range.Max - range.Min + 1))
                .Where(stored => Math.Abs(PoisonShown(stored, length.Value) - shown) <= slack)
                .OrderBy(stored => Math.Abs(stored - stat.Value))
                .Select(stored => (int?)stored)
                .FirstOrDefault();
            if (fits is { } settled)
                stat.Value = settled;
        }
    }

    /// <summary>The members of the run a stat leads, given its numbers in order.</summary>
    private static List<ItemStat> RunFrom(string leader, double[] values)
    {
        if (StatCatalogue.IdOf(leader) is not { } id)
            return [];

        return
        [
            .. ItemStats.RunContaining(id)
                .Select((member, at) => (Name: StatCatalogue.NameOf(member), Value: at < values.Length ? values[at] : 0))
                .Where(member => member.Name is not null)
                .Select(member => Stat(member.Name!, (int)Math.Round(member.Value))),
        ];
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

    /// <summary>Adds a stat, summed with one already there for the same subject - as the game keeps them.</summary>
    private static void Add(List<ItemStat> stats, ItemStat stat)
    {
        if (stats.FirstOrDefault(each => ItemStats.SameFields(each, stat)) is { } same)
            same.Value += stat.Value;
        else
            stats.Add(stat);
    }

    /// <summary>
    /// Whether the listing's line adds this socketed part in. Not every member of an elemental
    /// damage run: the site sums the <b>minimum</b> over the item and its sockets and gives the
    /// rest of the run - the maximum, a cold or poison length - from the first of them listing
    /// it. Lightsabre with four Rainbow Facets of 1-74 lightning reads 5-700: 1 + 4 x 1, and its
    /// own 700. A Giant Skull with no fire of its own, over jewels of 3-8 and 9-29, reads 12-8.
    /// Measured on every socketed item of the ladder's top characters that a socket
    /// adds elemental damage to - 57, all alike. Taken off as a sum, Lightsabre's maximum came
    /// out 404 and Thunderstroke's 363.
    /// </summary>
    private static bool SummedBySite(ItemStat part) =>
        part.Id is not { } id
        || ElementLeaders.Contains(part.Stat ?? "")
        || !ElementLeaders.Any(leader => StatCatalogue.IdOf(leader) is { } first
                                          && ItemStats.RunContaining(first) is { Count: > 1 } run
                                          && run.Contains(id));

    /// <summary>The first member of each elemental damage run, which the site does sum.</summary>
    private static readonly HashSet<string> ElementLeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "firemindam", "lightmindam", "magicmindam", "coldmindam", "poisonmindam",

        // Enhanced damage too, listed [max, min]: the maximum summed over the item's lists and its
        // sockets, the minimum the first source's. A superior Call to Arms with an Ohm reads
        // [247, 202] - the runeword's 202 and the Ohm's 45 - and a superior Infinity [320, 6], the
        // 6 being the superior share on the base's own list (the ladder).
        "item_maxdamage_percent",
    };

    /// <summary>Takes what a socketed item grants off the item's own; a line brought to nought goes.</summary>
    private static void Subtract(List<ItemStat> stats, ItemStat part, List<string> notes)
    {
        if (stats.FirstOrDefault(each => ItemStats.SameFields(each, part)) is not { } same)
            return;

        same.Value -= part.Value;

        // A run's members stay whatever they come to: the run is written whole or not at all.
        bool inRun = same.Id is { } id && ItemStats.IsInRun(id);
        if (same.Value == 0 && !inRun)
            stats.Remove(same);
        else if (same.Value < 0 && !inRun)
            notes.Add($"{same.Stat} came out at {same.Value} once the sockets were taken off");
    }

    // ---- reading the listing -----------------------------------------------------------

    private static bool Same(string? one, string? other) =>
        string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>A whole number along a path of properties, or null where it is missing or not a number.</summary>
    private static int? Number(JsonElement element, params string[] path)
    {
        foreach (string step in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(step, out element))
                return null;
        }

        return element.ValueKind == JsonValueKind.Number ? (int)Math.Round(element.GetDouble()) : null;
    }
}
