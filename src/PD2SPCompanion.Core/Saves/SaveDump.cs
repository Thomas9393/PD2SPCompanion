using System.Text;
using D2SLib.Model.Save;
using D2SLib.Model.TXT;
using PD2SPCompanion.Core.Graphics;

namespace PD2SPCompanion.Core.Saves;

/// <summary>
/// Renders a parsed character as text, for checking what the parser believes against
/// what the game shows. Everything here is presentation: it never changes the save.
/// </summary>
public static class SaveDump
{
    public static string Render(D2S save, SaveFormat format, bool showStats)
    {
        var text = new StringBuilder();
        Header(text, save, format);
        Attributes(text, save);
        Items(text, save, showStats);
        return text.ToString();
    }

    private static void Header(StringBuilder text, D2S save, SaveFormat format)
    {
        string className = CharacterClasses.NameOf(save.ClassId) ?? $"class {save.ClassId}";
        var tags = new List<string>();
        if (save.Status.IsHardcore) tags.Add("hardcore");
        // The flag is "has died", and only a hardcore character stays dead of it.
        if (save.Status.IsDead) tags.Add(save.Status.IsHardcore ? "DEAD" : "died");
        if (save.Status.IsExpansion) tags.Add("expansion");
        if (save.Status.IsLadder) tags.Add("ladder");

        text.AppendLine($"{save.Name}  -  level {save.Level} {className}"
                        + (tags.Count > 0 ? $"  [{string.Join(", ", tags)}]" : ""));
        text.AppendLine($"format {format}, d2s version {save.Header.Version}, {save.Header.Filesize} bytes");

        // What PD2's .played / .kills / .deaths would print for this character.
        if (PlayerStats.FromTrailer(save.Trailer) is { } stats)
        {
            text.AppendLine($"stats: played {stats.Played}, {stats.MonsterKills} kills, {stats.Deaths} deaths; "
                            + $"act bosses {stats.Andariel}/{stats.Duriel}/{stats.Mephisto}/{stats.Diablo}/{stats.Baal}; "
                            + $"maps {stats.MapBosses}, dungeons {stats.DungeonBosses}, ubers {stats.UberTristram}, "
                            + $"ancients {stats.UberAncients}, dclone {stats.DiabloClone}+{stats.DiabloCloneTier2}, "
                            + $"rathma {stats.Rathma}+{stats.RathmaTier2}, pvp {stats.PlayerKills}");
        }

        foreach (var (name, location) in new[]
                 {
                     ("normal", save.Location.Normal),
                     ("nightmare", save.Location.Nightmare),
                     ("hell", save.Location.Hell),
                 })
        {
            if (location.Active)
                text.AppendLine($"  {name}: act {location.Act}");
        }
        text.AppendLine();
    }

    private static void Attributes(StringBuilder text, D2S save)
    {
        text.AppendLine("attributes");
        foreach (var (stat, raw) in save.Attributes.Stats)
        {
            // Scaled attributes are stored with a fraction; show what the game shows.
            string note = AttributeScale.HasFraction(stat, raw) ? $"  (raw {raw})" : "";
            // Experience is a uint32 in a signed field: level 99 is past 2^31 (LevelEditor.Experience).
            string shown = stat == "experience" ? unchecked((uint)raw).ToString() : AttributeScale.Display(stat, raw).ToString();
            text.AppendLine($"  {stat,-16} {shown}{note}");
        }
        text.AppendLine();
    }

    private static void Items(StringBuilder text, D2S save, bool showStats)
    {
        var items = save.PlayerItemList?.Items ?? [];
        text.AppendLine($"items ({items.Count})");

        // The index is the item's position in the player item list, which is how an edit
        // addresses it. Grouping is only for readability and never renumbers anything.
        var numbered = items.Select((item, index) => (Item: item, Index: index)).ToList();

        foreach (var group in numbered.GroupBy(entry => Where(entry.Item)).OrderBy(g => g.Key))
        {
            text.AppendLine($"\n  == {group.Key} ({group.Count()})");
            foreach (var (item, index) in group)
                Describe(text, item, showStats, "  ", index);
        }

        // The mercenary's own list, which is a separate section of the save. Checking an
        // item against its tooltip is half the point of this dump, and a mercenary wears
        // seven of them.
        var worn = save.MercenaryItemList?.ItemList?.Items ?? [];
        if (worn.Count > 0)
        {
            text.AppendLine($"\n  == mercenary ({worn.Count})");
            foreach (var item in worn)
                Describe(text, item, showStats, "  ");
        }

        // The Iron Golem's item, a section of its own too.
        if (save.Golem is { Exists: true, Item: { } golem })
        {
            text.AppendLine();
            text.AppendLine($"  == {nameof(save.Golem).ToLowerInvariant()} (1)");
            Describe(text, golem, showStats, "  ");
        }
    }

    /// <summary>Equipped slot, belt, or the storage page an item sits on.</summary>
    private static string Where(Item item) => item.Mode switch
    {
        ItemMode.Equipped => $"equipped: {item.Location}",
        ItemMode.Belt => "belt",
        ItemMode.Socket => "socketed",
        // Storage page ids as the game writes them; 2 and 3 are unused.
        _ => item.Page switch
        {
            1 => "inventory",
            InventoryLayout.CubePage => "cube",
            InventoryLayout.ExpansionStashPage => "stash",
            InventoryLayout.ClassicStashPage => "classic stash",
            _ => $"storage page {item.Page}",
        },
    };

    /// <summary>
    /// One item, described the way a character's dump describes it. For a stash, which has
    /// pages of items and no character to render around them.
    /// </summary>
    public static string Describe(Item item, bool showStats)
    {
        var text = new StringBuilder();
        Describe(text, item, showStats, "  ");
        return text.ToString();
    }

    private static void Describe(StringBuilder text, Item item, bool showStats, string indent, int? index = null)
    {
        string code = item.Code?.Trim() ?? "?";
        var flags = new List<string>();
        if (item.IsEthereal) flags.Add("ethereal");
        if (item.IsSocketed) flags.Add($"{item.NumberOfSocketedItems}/{item.TotalNumberOfSockets} sockets");
        if (item.IsRuneword) flags.Add($"runeword {item.RunewordId}");
        if (item.IsPersonalized) flags.Add($"personalized: {item.PlayerName}");
        if (item.IsStarterItem) flags.Add("starter");
        if (!item.IsIdentified) flags.Add("UNIDENTIFIED");
        flags.Add($"v{item.Version}");
        // Any count at all, not just more than one: a stack of one is a real state and a
        // different thing from an item that has no count.
        if (item.Quantity > 0) flags.Add($"qty {item.Quantity}");
        if (item.IsAutoAffix) flags.Add($"auto affix {item.AutoAffixId}");
        if (item.RarePrefixId > 0 || item.RareSuffixId > 0)
            flags.Add($"rare name {item.RarePrefixId}/{item.RareSuffixId}");
        if (item.MagicPrefixIds is { } prefixes && prefixes.Any(id => id > 0))
            flags.Add("prefix " + string.Join("/", prefixes.Where(id => id > 0)));
        if (item.MagicSuffixIds is { } suffixes && suffixes.Any(id => id > 0))
            flags.Add("suffix " + string.Join("/", suffixes.Where(id => id > 0)));

        var view = ItemPresenter.Describe(item);
        string name = view.Name;
        string? warning = view.Warning;
        var sprite = SpriteLoader.Current?.Load(view.SpriteName, view.SpriteTint);
        // A compact item carries no quality or item level - it is just a code and a spot.
        // The id too: two items sharing one inside a save is how a dupe shows.
        string quality = item.IsCompact ? "simple" : $"{item.Quality}, ilvl {item.ItemLevel}, id {item.Id}";

        // A unique or set item's name and its property ranges both come from this row
        // number, so a dump that is being checked against a tooltip should show it.
        if (item.Quality is ItemQuality.Unique or ItemQuality.Set)
            quality += $", row {item.FileIndex}";

        string label = index is not null ? $"#{index,-3}" : "    ";
        string art = view.SpriteName is null ? "no invfile"
            : sprite is null ? $"{view.SpriteName} MISSING"
            : $"{view.SpriteName} {sprite.Width}x{sprite.Height}";
        text.AppendLine($"{indent}{label} [{code,-4}] {name}   ({view.GridWidth}x{view.GridHeight}, {art})");
        text.AppendLine($"{indent}          {quality}, at {item.X},{item.Y}"
                        + (flags.Count > 0 ? $", {string.Join(", ", flags)}" : ""));
        if (warning is not null)
            text.AppendLine($"{indent}          !! {warning}");

        if (item.MaxDurability > 0)
            text.AppendLine($"{indent}          durability {item.Durability}/{item.MaxDurability}");
        if (item.Armor > 0)
            text.AppendLine($"{indent}          defense {item.Armor}");

        if (showStats)
        {
            // Two different ranges, and the difference is the point. The bracket is what
            // the game paints - the union over every affix the base could have rolled at
            // its item level - so a line reads straight across against a tooltip. Beside
            // it, where it says something else, what this item's own sources could have
            // rolled, which is the range that actually bounds the value.
            var shown = AffixRanges.DisplayedFor(item);
            var rolls = AffixRanges.For(item);

            // What the corruption on this item granted, so a dump can be read against the
            // red marks the game paints.
            var corrupted = Corruptions.StatsOn(item);
            var desecrated = Desecrations.StatsOn(item);

            // Named by something on the item, whether or not a range can be put to it.
            var accounted = AffixRanges.Accounted(item);

            // A runeword or a set item keeps more than one: the base's own properties in the
            // first, and what the runeword or the set added in the ones after it. Said out
            // loud, because code that reads only the first list sees an empty item.
            for (int which = 0; which < item.StatLists.Count; which++)
            {
                var list = item.StatLists[which];

                if (item.StatLists.Count > 1)
                    text.AppendLine($"{indent}          -- list {which}: {list.Stats.Count} properties");

                foreach (var stat in list.Stats)
                {
                    // By the skill it names as well as by its own name, the way the panel
                    // asks: a staff's own Fire Bolt and a runeword's are two properties and
                    // are ranged apart.
                    bool explained = rolls.TryGetValue(AffixRanges.KeyOf(stat), out var roll)
                                     || rolls.TryGetValue(stat.Stat ?? "", out roll);

                    if (!shown.TryGetValue(AffixRanges.KeyOf(stat), out var bracket))
                        _ = shown.TryGetValue(stat.Stat ?? "", out bracket);

                    // Nothing the item carries accounts for this property. Not the same as
                    // illegitimate - it is what an unread source looks like too, which is
                    // the point of printing it.
                    // A corruption accounts for a property as surely as an affix does, and
                    // it is the one source the game itself marks, so it is said rather than
                    // folded into the ranges.
                    // The range and the mark are two different things and a corrupted
                    // property has both: its range is the affix and the corruption added.
                    if (!explained)
                        explained = accounted.Contains(stat.Stat ?? "");

                    string range = !explained ? "  <- unexplained"
                        : bracket is { } painted && painted.Min != painted.Max
                            ? $"  [{painted.Min} - {painted.Max}]"
                        : "";

                    // Said only where the two disagree, which is every affixed item and no
                    // unique. On a stat two affixes wrote it is the wider of the two - a
                    // helm holding 144 Attack Rating is bracketed [10 - 120] by the game
                    // and 111-160 by its own affixes - so the bracket alone would read as
                    // an impossible item.
                    if (explained && roll is { } source && !Equals(source, bracket))
                        range += $"  affixes {source}";

                    if (corrupted.Contains(stat.Stat ?? "", StringComparer.OrdinalIgnoreCase))
                        range += "  *corrupted";

                    if (desecrated.Contains(stat.Stat ?? "", StringComparer.OrdinalIgnoreCase))
                        range += "  *desecrated";

                    text.AppendLine($"{indent}          + {stat.Stat,-30} {stat.Value,6}{Qualifier(stat)}   (id {stat.Id}){range}");
                }
            }
        }

        foreach (var socketed in item.SocketedItems)
            Describe(text, socketed, showStats, indent + "    ");
    }

    /// <summary>
    /// The skill or parameter a stat refers to, named where possible. Stats like
    /// item_singleskill carry a skill id in their parameter; proc stats carry it in
    /// SkillId. Showing the name is what makes a dump checkable against a tooltip.
    /// </summary>
    private static string Qualifier(ItemStat stat)
    {
        if (stat.SkillId is int procSkill)
        {
            string named = SkillNames.Current?.Describe(procSkill) ?? procSkill.ToString();
            return $"  [{named}, level {stat.SkillLevel}]";
        }
        if (stat.Param is int param)
        {
            // The stats whose parameter picks the wording say it themselves, and say it the
            // way the game does. Asked as a skill id instead, a reanimate read as whatever
            // skill happened to share the monster's number.
            if (StatText.ByParameter(stat.Stat ?? "", stat.Value, param) is { } worded)
                return $"  [{worded}]";

            // A parameter is a skill id where the table words the stat as one. descfunc
            // 27 and 28 are the single-skill forms and 16 is "Level [n] [skill] ... When
            // Equipped" - item_aura, which has no "skill" in its name and was showing a
            // bare number.
            // item_elemskill_magic and its kin have "skill" in the name and a parameter that is no skill:
            // read as one, a forged Heavenly Garb's showed "[Attack]".
            bool aboutASkill = StatText.FunctionOf(stat.Stat ?? "") is 16 or 27 or 28
                || ((stat.Stat?.Contains("skill", StringComparison.OrdinalIgnoreCase) ?? false)
                    && !(stat.Stat?.StartsWith("item_elemskill", StringComparison.OrdinalIgnoreCase) ?? false));

            string? named = aboutASkill ? SkillNames.Current?.Describe(param) : null;
            return named is not null ? $"  [{named}]" : $"  param={param}";
        }
        return "";
    }

}
