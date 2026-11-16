using D2SLib.Model.Save;
using System.Collections.Concurrent;
using System.Text;

namespace PD2SPCompanion.Core.Items;

/// <summary>
/// One line of an item's tooltip: what it says, the range it rolled in where the game would
/// print one, and which of the game's colours it is said in.
/// </summary>
public sealed record ItemLine(string Text, string Range = "", string Tone = ItemLine.Fact,
    bool Corrupted = false, bool Desecrated = false)
{
    public const string Name = "name";
    public const string Fact = "fact";
    public const string Magic = "magic";
    public const string Corrupt = "corrupt";

    /// <summary>"Desecrated", which the game prints in gold under the red "Corrupted".</summary>
    public const string Desecrate = "desecrate";

    /// <summary>"5th Anniversary Edition", which the game prints in orange above "Corrupted".</summary>
    public const string Skin = "skin";
    public const string Warning = "warn";

    /// <summary>A requirement the character does not meet, in the game's red.</summary>
    public const string Unmet = "unmet";

    /// <summary>A set piece's green bonus, in the game's set green.</summary>
    public const string SetBonus = "setbonus";

    /// <summary>Not a line of words but a rule across the tooltip, setting what follows apart.</summary>
    public const string Rule = "rule";

    /// <summary>The runes in the item's sockets, named in one word under its base - 'ShaelShael' - in the game's gold.</summary>
    public const string Runes = "runes";
}

/// <summary>
/// An item's lines as the game's tooltip words them - the one wording the tile's tooltip, the cards and the
/// search read (the search finds "ANY text contained on the item", as the game's stash
/// search does). They were the tile's own until then; here, the vault's index can keep them
/// (<see cref="TextOf"/>) and a search for "attack speed" finds what a tooltip says it on.
/// </summary>
public static class ItemLines
{
    private const string BluntUndeadStat = "item_undeaddamage_percent";
    private const int BluntUndeadBonus = 50;

    /// <summary>
    /// What the search's text box is matched against, a line each: the item's name, its base, and its
    /// stats - the tooltip's colored lines, what its sockets give counted in as in game. <b>Not the plain
    /// lines</b>: its damage, defense and durability, what it asks of who uses it, its levels, its base
    /// attack speed (a basic category like level or requirement never filters anything, and the game's own search, typed
    /// "attack speed", shows what has the stat, not every weapon). So "attack speed" finds what gives it.
    /// <para>
    /// Kept besides the properties: the runes named under the base ('ShaelShael'), a set's bonuses,
    /// "Ethereal", "Socketed (n)", "Corrupted", "Desecrated" and an edition's name - each is said of some
    /// items and not of others. And the name of what is in each socket - a rune, a gem, a jewel - though
    /// the tooltip lists none of them. The bracketed ranges are left out: what an item could have rolled is not what it is.
    /// </para>
    /// <para>
    /// An item whose lines cannot be worded keeps its name and base: a store must not fail for a tooltip.
    /// </para>
    /// </summary>
    public static string TextOf(Item item)
    {
        var view = ItemPresenter.Describe(item);

        // With the name on a personalized one, as its tooltip heads it: a search finds it by whose it is.
        var text = new StringBuilder(Personalization.Titled(item, view.Name));

        if (view.BaseName.Length > 0 && !view.BaseName.Equals(view.Name, StringComparison.Ordinal))
            text.Append('\n').Append(view.BaseName);

        try
        {
            foreach (var line in Of(item, view, out _, out _))
            {
                if (line.Text.Length > 0 && !Plain.Contains(line.Tone))
                    text.Append('\n').Append(line.Text);
            }

            // What is in each socket, by name - a jewel by the name any magic or rare item has: the
            // tooltip does not list them, and "Perfect Skull" should still find the spear that holds one.
            foreach (var gem in item.SocketedItems)
                text.Append('\n').Append(ItemPresenter.Describe(gem).Name);
        }
        catch (Exception problem)
        {
            Log.Caught(problem);
        }

        return text.ToString();
    }

    /// <summary>
    /// <see cref="TextOf"/> for an item whose stored bytes are known by their hash: the same bytes say the
    /// same, so they are worded once. The inventory's search indexes every item again at each letter typed,
    /// and wording them was the cost of it (0.25 ms an item, measured as the tooltips' share of a load).
    /// </summary>
    public static string TextOf(Item item, byte[] hash)
    {
        string key = Convert.ToHexString(hash);
        if (_texts.TryGetValue(key, out string? known))
            return known;

        // Bounded by starting over: a session that has worded this many has long left its first ones.
        if (_texts.Count >= 50_000)
            _texts.Clear();

        return _texts[key] = TextOf(item);
    }

    private static readonly ConcurrentDictionary<string, string> _texts = new();

    /// <summary>
    /// The tones of the lines a search does not read: the white lines every item of a kind has - the base
    /// (given by name instead), damage, durability, levels, the base attack speed - a requirement met or
    /// not, a warning of the editor's, a rule.
    /// </summary>
    private static readonly HashSet<string> Plain = [ItemLine.Fact, ItemLine.Unmet, ItemLine.Warning, ItemLine.Rule];

    /// <summary>
    /// The item's name as its tooltip heads it: with how many sockets it has after it - "Stone Barb [4]" -
    /// as the game writes it (a screenshot; seen on an item whose four are all filled,
    /// so whether the game counts the holes or what is in them is not told apart - the holes, here).
    /// <para>A personalized item is headed with whose it is, as the game heads it: "Name's Stone Barb".</para>
    /// </summary>
    public static string Title(Item item, ItemView view)
    {
        // A plain item with anything in a socket is "Gemmed", a rune or a jewel too; a superior one is not (screenshots).
        string name = item.Quality == ItemQuality.Normal && item.SocketedItems.Count > 0 && Runewords.On(item) is null
            ? $"{StringTable.Current?.Get("Gemmed") ?? "Gemmed"} {view.Name}"
            : view.Name;

        return Math.Max(item.TotalNumberOfSockets, item.SocketedItems.Count) is > 0 and var sockets
            ? $"{Personalization.Titled(item, name)} [{sockets}]"
            : Personalization.Titled(item, name);
    }

    /// <summary>
    /// What the item asks of who uses it, in the game's order, each red where judged unmet: the class its
    /// base is for - "(Amazon Only)", over the rest as the game has it, red for a character of another -
    /// then dexterity, strength and level.
    /// </summary>
    public static List<ItemLine> Requirements(Item item, string? classCode, (bool Level, bool Strength, bool Dexterity)? met)
    {
        var need = Characters.Requirements.Of(item, classCode);
        var lines = new List<ItemLine>();

        string Tone(bool? has) => has == false ? ItemLine.Unmet : ItemLine.Fact;

        if (ItemTypes.ClassOfBase(item.Code) is { Length: > 0 } only && CharacterClasses.NameOfCode(only) is { Length: > 0 } named)
        {
            bool? fits = classCode is null ? null : string.Equals(classCode, only, StringComparison.OrdinalIgnoreCase);
            lines.Add(new ItemLine(L($"({named} Only)"), Tone: Tone(fits)));
        }

        if (need.Dexterity > 0)
            lines.Add(new ItemLine(L($"Required Dexterity: {need.Dexterity}"), Tone: Tone(met?.Dexterity)));
        if (need.Strength > 0)
            lines.Add(new ItemLine(L($"Required Strength: {need.Strength}"), Tone: Tone(met?.Strength)));
        if (need.Level > 1)
            lines.Add(new ItemLine(L($"Required Level: {need.Level}"), Tone: Tone(met?.Level)));

        return lines;
    }

    /// <summary>The stat PD2 words as a socket line on a base the engine gives none of its own.</summary>
    private const string SocketsTextOnly = "item_numsockets_textonly";

    /// <summary>
    /// The item as the game's own tooltip would put it: what it is, what it is worth, and
    /// then what it does, in the game's words rather than the table's names.
    /// <para>
    /// The order is the game's: the name, the base under it, the damage or defence, the
    /// wear of it, what it asks of the character, then the properties. A property carries
    /// the range it rolled in where one is known, which is the green bracket the game puts
    /// after a rolled line and leaves off a fixed one.
    /// </para>
    /// </summary>
    /// <param name="view">The item's name, base and picture, as <see cref="ItemPresenter.Describe"/> gives them.</param>
    /// <param name="requirementsAt">Where the requirement lines begin, for a caller that tones them again.</param>
    /// <param name="requirementsCount">How many requirement lines there are.</param>
    /// <param name="tally">A count the item stands for - a materials tab's slot - said in place of its own.</param>
    /// <param name="notes">Lines the place showing the item adds, under a rule.</param>
    /// <param name="classCode">The character's class, for the requirements a class lowers.</param>
    /// <param name="met">Which requirements the character meets; null where no character judges.</param>
    /// <param name="socketsApart">
    /// The other way of reading a socketed item (a key held shows it): the lines are the
    /// item's own, nothing of its sockets counted in, and under "Socketed (n)", after a rule, each thing in a
    /// socket is named with what it gives beneath. False is the game's way: all of it summed into the lines.
    /// </param>
    /// <param name="etherealsWorn">
    /// How many ethereal items the character holding it wears: a per-ethereal line says its total, as in game. Null where
    /// no character holds it - the vault, the Forge - and the line says the stored number.
    /// </param>
    /// <param name="holderLevel">
    /// The level of whoever holds it, the character or its mercenary: a "Based on Character Level" line says what it comes
    /// to at that level, as in game. Null where no one holds it, and the line says its figure a level.
    /// </param>
    public static List<ItemLine> Of(Item item, ItemView view, out int requirementsAt, out int requirementsCount,
                                    int? tally = null, IReadOnlyList<string>? notes = null,
                                    string? classCode = null, (bool Level, bool Strength, bool Dexterity)? met = null,
                                    bool socketsApart = false, int? etherealsWorn = null, int? holderLevel = null)
    {
        var written = new List<ItemLine>();

        // Empty sockets count, and a gem beyond the count the item states is still in it.
        int sockets = Math.Max(item.TotalNumberOfSockets, item.SocketedItems.Count);

        void Say(string text, string range = "", string tone = ItemLine.Fact, bool corrupted = false, bool desecrated = false)
        {
            if (text.Length > 0)
                written.Add(new ItemLine(text, range, tone, corrupted, desecrated));
        }

        // Which stats the asterisks follow: what the corruption wrote (red), and what the desecration gave (gold),
        // the red first where one line has both. The game's gold mark is a S13 slip - its two-bit field reads back as
        // Attack Rating or enhanced damage whatever the mod was - so the editor
        // marks the mod itself, pointing at the stats it affected.
        var corruptedStats = new HashSet<string>(Corruptions.StatsOn(item), StringComparer.OrdinalIgnoreCase);
        var desecratedStats = new HashSet<string>(Desecrations.StatsOn(item), StringComparer.OrdinalIgnoreCase);

        // The name itself is the header, above these. Said here as well, it was said twice.
        //
        // The base under it, as the game prints it - but only where the name is not already
        // the base's own, or a plain item says "Cap" twice.
        if (view.BaseName.Length > 0 && !view.BaseName.Equals(view.Name, StringComparison.Ordinal))
            Say(view.BaseName);

        // The runes in its sockets, in one word under the base, as the game names them - and names nothing
        // else that is in a socket: two Shaels, a skull and a jewel read 'ShaelShael' (a
        // screenshot of a Stone Barb).
        if (string.Concat(item.SocketedItems.Where(gem => Runewords.RuneNumber(gem.Code) > 0)
                                            .Select(gem => Runewords.RuneName(gem.Code))) is { Length: > 0 } runes)
        {
            Say($"'{runes}'", tone: ItemLine.Runes);
        }

        var basis = ItemBases.Of(item.Code);

        // A set piece's green lists are its set bonuses, said apart after its own lines (see
        // SetBonusLines): flattened in with the rest, a green tier's cold damage was paired with
        // the item's own - M'avina's Icy Clutch read "Adds 131-36 cold damage".
        var own = (item.Quality == ItemQuality.Set && item.SetItemMask != 0 ? item.StatLists.Take(1) : item.StatLists)
            .SelectMany(list => list.Stats);
        var stats = socketsApart ? own.ToList() : WithSockets(item, own);

        int Held(string name) =>
            stats.Where(stat => string.Equals(stat.Stat, name, StringComparison.OrdinalIgnoreCase))
                 .Sum(stat => stat.Value);

        if (basis is not null)
        {
            // Enhanced damage is a percentage of the base, and the game floors it: a
            // Brandistock's 7 to 17 with 13% reads 7 to 19, not 7.9 to 19.2. An ethereal base is
            // raised a quarter first, floored too - PD2's figure, where classic D2 raised it half:
            // an ethereal Mancatcher's 52 to 115 is 65 to 143, and with 320% the game shows 273 to
            // 600 (an Infinity, checked in game; the trade site draws it the same).
            int Damage(int from, int percent, int flat) =>
                (item.IsEthereal ? from * 125 / 100 : from) * (100 + percent) / 100 + flat;

            int least = Held("item_mindamage_percent"), most = Held("item_maxdamage_percent");

            if (!basis.IsArmor)
            {
                // Each kind with its own flat damage: a two-handed weapon holds the two-handed copy and
                // no other (DamageKinds), and read off the one-handed stat its "+9 to Maximum Damage"
                // was left out of the line.
                if (basis.TwoHandMinimum is { } twoLow && basis.TwoHandMaximum is { } twoHigh)
                {
                    int lowest = Damage(twoLow, least, Held("secondary_mindamage")),
                        highest = Damage(twoHigh, most, Held("secondary_maxdamage"));
                    Say(L($"Two-Hand Damage: {lowest} to {highest}"));
                }

                // First, as the game prints it: a Javelin's "Throw Damage: 6 to 10" over its "One-Hand Damage", a Throwing
                // Knife's 4 to 5 over 2 to 3, each from its own flat damage (LookTest, screenshots).
                if (basis.ThrowMinimum is { } thrownLow && basis.ThrowMaximum is { } thrownHigh && thrownHigh > 0)
                {
                    int lowest = Damage(thrownLow, least, Held("item_throw_mindamage")),
                        highest = Damage(thrownHigh, most, Held("item_throw_maxdamage"));
                    Say(L($"Throw Damage: {lowest} to {highest}"));
                }

                if (basis.MinimumDamage is { } low && basis.MaximumDamage is { } high && high > 0)
                {
                    int lowest = Damage(low, least, Held("mindamage")), highest = Damage(high, most, Held("maxdamage"));
                    Say(L($"One-Hand Damage: {lowest} to {highest}"));
                }
            }
        }

        // Defence as the game shows it: the percentage lifts the base, and flat defence is
        // added after. An item storing 4 and 6 shows 10.
        if (item.Armor > 0)
        {
            int defense = item.Armor * (100 + Held("item_armor_percent")) / 100 + Held("armorclass");
            Say(L($"Defense: {defense}"));
        }

        if (basis is { IsArmor: true })
        {
            var lineage = ItemTypes.LineageOfBase(item.Code);
            bool shield = lineage.Contains("shld", StringComparer.OrdinalIgnoreCase);

            // A shield's own chance and the class's, so only where a class holds it: a Buckler reads 25% on a Barbarian
            // and an Assassin, 30% on a Paladin (LookTest, LookPala, LookAssn, screenshots).
            if (shield && classCode is not null)
            {
                int chance = basis.Block + ItemBases.BlockFactorOf(classCode) + Held("toblock");
                Say(L($"Chance to Block: {chance}%"));
            }

            // Armour's own damage is a boot's kick and a shield's smite, the base's and no more:
            // armor.txt has a "mindam" as weapons.txt does, and read as a weapon's it put "One-Hand
            // Damage: 57 to 109" on a pair of War Travelers, their "Adds 15-25 Damage" added in
            // (DamageKinds has the rest of that mistake).
            // The game says each only to the class that uses it, Kick to an Assassin, Smite to a Paladin, under Defense
            // and Block (the same screenshots); with no character holding it, it is the base's to say.
            if (basis.MinimumDamage is { } kick && basis.MaximumDamage is { } most2 && most2 > 0)
            {
                bool Holds(string code) => classCode is null || classCode.Equals(code, StringComparison.OrdinalIgnoreCase);
                if (lineage.Contains("boot", StringComparer.OrdinalIgnoreCase) && Holds("ass"))
                    Say(L($"Kick Damage: {kick} to {most2}"));
                else if (shield && Holds("pal"))
                    Say(L($"Smite Damage: {kick} to {most2}"));
            }
        }

        if (item.MaxDurability > 0)
        {
            // Of the maximum the item's own properties raise it to - see ItemStats.MaxDurabilityOf.
            // The stored number alone read "146 of 50" on Duriel's Shell, which adds 100.
            Say(L($"Durability: {item.Durability} of {ItemStats.MaxDurabilityOf(item, item.MaxDurability)}"));
        }

        // Only where the number means something: PD2 gives throwing weapons and arrows
        // unlimited quantity and prints none. See Stacks.
        if (tally is { } count)
            Say(L($"Quantity: {count}"));
        else if (Stacks.IsCounted(item.Code))
            Say(L($"Quantity: {Stacks.Shown(item.Quantity).ToString()}"));

        // A map's tier, under its count as the game has it (a screenshot).
        if (MapRecipes.TierOf(item.Code) is { } tier)
            Say(L($"Tier: {tier}"));

        // What the place showing the tile has to add - the rune wizard says where a count is -
        // under a rule, so the total reads apart from what it is made of.
        if (notes is { Count: > 0 })
            written.Add(new ItemLine("", Tone: ItemLine.Rule));

        foreach (string note in notes ?? [])
            Say(note);

        // What it asks of whoever uses it - the item's own requirements, not its base's; red where the character falls short, once the
        // page has judged it. Kept where they are so a judgement can re-tone them in place.
        requirementsAt = written.Count;
        var required = Requirements(item, classCode, met);
        written.AddRange(required);
        requirementsCount = required.Count;

        // A weapon's base attack speed, where the game's tooltip has its word for it - "Spear Class - Very
        // Fast Attack Speed". The word is left out and the number given, a Basic Attack Speed like -20, 0 or 10:
        // players do not rely on that word, they check IAS breakpoints, and this is what a breakpoint is worked out from.
        if (basis is { IsArmor: false, Speed: { } speed })
            Say(L($"Base Attack Speed: {speed}"));

        // Not on a tab tile: its item was made to stand for a count, and its level is ours.
        if (!item.IsCompact && tally is null)
        {
            Say(L($"Item Level: {item.ItemLevel}"));

            // What its affixes rolled at, which is below the item level by half the base's
            // own and is the number that says which affixes it could ever have had.
            if (MagicAffixes.Carries(item))
                Say(L($"Affix Level: {MagicAffixes.AffixLevel(item)}"));
        }

        if (!item.IsIdentified)
            Say(L("Unidentified"), tone: ItemLine.Warning);

        // Above "Corrupted" and in orange, as the game prints it - a fact about the item, not
        // one of its properties (a white Shako).
        if (Skins.Has(item)
            && ItemStats.Every(item).FirstOrDefault(stat => string.Equals(stat.Stat, Skins.Stat, StringComparison.OrdinalIgnoreCase)) is { } skin
            && (StatText.Describe(Skins.Stat, skin.Value) ?? Skins.Edition(skin.Value)) is { } edition)
        {
            Say(edition, tone: ItemLine.Skin);
        }

        // A shard spent to no effect counts: it reads "Corrupted" in game too - a dungeon whose roll
        // fell in no band.
        if (Corruptions.IsCorrupted(item))
            Say(L("Corrupted"), tone: ItemLine.Corrupt);

        // Under it, as the game prints it - whatever the value.
        if (Corruptions.IsDesecrated(item))
            Say(L("Desecrated"), tone: ItemLine.Desecrate);

        // The season before's item, kept as it was (Saves.ItemSeason): the editor's word, not the game's, in its warning tone.
        string? legacyMark = Saves.ItemSeason.Mark(item);
        if (legacyMark is not null)
            Say(legacyMark, tone: ItemLine.Warning);

        // The game's bracket, not the item's own affix range - this tooltip is a replica of
        // the one in game and has to read across against it. The two are different questions
        // and the item panel asks the other one; see AffixRanges.DisplayedFor. A runeword's
        // too, whatever its base's quality: its lines rolled in the word's ranges, and were
        // left bare.
        // A legacy item's too, by this season's rows, as the game prints them: a Saracen's Chance of Season 13 reads "+44%
        // Enhanced Damage [30 - 50]" (a screenshot).
        var ranges = (MagicAffixes.Carries(item) || item.IsRuneword || item.Quality is ItemQuality.Unique or ItemQuality.Set)
            ? AffixRanges.DisplayedFor(item)
            : new Dictionary<string, RollRange>(StringComparer.OrdinalIgnoreCase);

        // A blunt weapon's 50% against the undead is in no list and the game prints it all the same: added to the item's own
        // line where it has one - Bonesnap's stored 50 reads "+100% Damage to Undead", Rusthandle's 60 "+110% ... [50 - 60]" -
        // and otherwise a line of its own after all the rest, on a white Wand as on Holy Thunder (screenshots).
        bool blunt = ItemTypes.LineageOfBase(item.Code).Contains("blun", StringComparer.OrdinalIgnoreCase);
        int undeadAt = blunt ? stats.FindIndex(stat => string.Equals(stat.Stat, BluntUndeadStat, StringComparison.OrdinalIgnoreCase)) : -1;
        if (undeadAt >= 0)
            stats[undeadAt] = new ItemStat { Id = stats[undeadAt].Id, Stat = stats[undeadAt].Stat, Value = stats[undeadAt].Value + BluntUndeadBonus };

        foreach (var (text, range, from) in PropertyLines(stats, ranges, sockets, etherealsWorn, MagicAffixes.Carries(item), holderLevel))
            Say(text, range, ItemLine.Magic, from.Any(corruptedStats.Contains), from.Any(desecratedStats.Contains));

        if (blunt && undeadAt < 0 && StatText.Describe(BluntUndeadStat, BluntUndeadBonus) is { } undead)
            Say(undead, tone: ItemLine.Magic);

        foreach (string bonus in SetBonusLines(item))
            Say(bonus, tone: ItemLine.SetBonus);

        // Ethereal and the sockets on one line, as the game prints them: "Ethereal (Cannot be Repaired), Socketed (2)"
        // (the game's tooltip beside ours).
        string? ethereal = item.IsEthereal ? L("Ethereal (Cannot be Repaired)") : null;
        if (ethereal is not null && sockets == 0)
            Say(ethereal, tone: ItemLine.Magic);

        if (sockets > 0)
        {
            // And nothing under it: what is in the sockets is counted into the lines above, as in game,
            // not named here.
            string socketed = L($"Socketed ({sockets})");
            Say(ethereal is null ? socketed : $"{ethereal}, {socketed}", tone: ItemLine.Magic, corrupted: Corruptions.GaveSockets(item));

            // Read apart: each thing in a socket by its name - a jewel by the name any magic or rare item
            // has - and what it gives this item beneath, a jewel's its own properties and a gem's or a
            // rune's what gems.txt has for this kind of host.
            if (socketsApart && item.SocketedItems.Count > 0)
            {
                written.Add(new ItemLine("", Tone: ItemLine.Rule));

                foreach (var gem in item.SocketedItems)
                {
                    Say(ItemPresenter.Describe(gem).Name);

                    foreach (string gives in gem.IsCompact
                                 ? SocketEffects.On(gem, item)
                                 : Worded([.. gem.StatLists.SelectMany(list => list.Stats)]))
                    {
                        Say(gives, tone: ItemLine.Magic);
                    }
                }
            }
        }

        // A gem or rune lying loose says what it gives in each kind of host, as the game's own tooltip does. One in a
        // socket is worded by the item holding it, whose kind is known.
        if (item.Mode != ItemMode.Socket && SocketEffects.Known(item.Code))
        {
            var (weapon, armor, shield) = SocketEffects.Everywhere(item.Code);
            foreach (var (kind, gives) in new[] { (L("Weapon"), weapon), (L("Armor, Helm"), armor), (L("Shield"), shield) })
            {
                if (gives.Count == 0)
                    continue;

                Say(kind);
                foreach (string line in gives)
                    Say(line, tone: ItemLine.Magic);
            }
        }

        if (view.Warning is not null)
            Say(view.Warning, tone: ItemLine.Warning);

        return written;
    }

    /// <summary>
    /// The property lines of a list of stats, in the game's order and wording: an elemental run one line,
    /// the physical and enhanced damage one each, four resistances alike "All Resistances", and the rest a
    /// line a stat - each with the bracket it rolled in where <paramref name="ranges"/> knows one, and the
    /// stats it says, which is what a corruption's mark follows.
    /// </summary>
    /// <param name="sockets">How many sockets the item has: with any, a "Socketed (n)" worded as a property is left to the socket line.</param>
    /// <param name="etherealsWorn">The holder's ethereal items worn, for a per-ethereal line's total; null, its stored number.</param>
    /// <param name="holderLevel">The holder's level, for a per-level line's total; null, its figure a level.</param>
    private static IEnumerable<(string Text, string Range, IReadOnlyCollection<string> From)> PropertyLines(
        IReadOnlyList<ItemStat> stats, IReadOnlyDictionary<string, RollRange> ranges, int sockets, int? etherealsWorn = null, bool affixed = false, int? holderLevel = null)
    {
        int Held(string name) =>
            stats.Where(stat => string.Equals(stat.Stat, name, StringComparison.OrdinalIgnoreCase))
                 .Sum(stat => stat.Value);

        // An elemental damage run is one line in the game, not two or three, and its stored
        // numbers are not the numbers shown - poison and cold keep damage per frame in
        // 256ths. The followers are said with their leader, so they are not said again.
        var withLeader = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (leader, parts) in StatText.ElementalRuns)
        {
            if (!stats.Any(each => string.Equals(each.Stat, leader, StringComparison.OrdinalIgnoreCase)))
                continue;

            withLeader.Add(parts.Max);
            if (parts.Length.Length > 0)
                withLeader.Add(parts.Length);
        }

        int Alongside(string of) => stats
            .FirstOrDefault(each => string.Equals(each.Stat, of, StringComparison.OrdinalIgnoreCase))
            ?.Value ?? 0;

        // Physical and enhanced damage: six stats and two, said as one line each where the game
        // says one - see StatText.PhysicalDamage. Said where the first of them comes.
        int? HeldOrNone(string name) =>
            stats.Any(each => string.Equals(each.Stat, name, StringComparison.OrdinalIgnoreCase)) ? Held(name) : null;

        string Bracket(string? of) =>
            of is not null && ranges.TryGetValue(of, out var span) && span.Max > span.Min
                ? $"[{StatText.ShownNumber(of, span.Min)} - {StatText.ShownNumber(of, span.Max)}]"
                : "";

        bool physicalSaid = false, enhancedSaid = false;

        // Description groups: four resistances alike are "All Resistances +10", said once where the
        // first of them comes - StatText.Grouped.
        var describedGroups = StatText.Grouped(HeldOrNone);
        var groupsSaid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The property lines are gathered and then said in the game's order - by each stat's
        // descpriority, the highest first, as a tooltip in game reads - not in the order the item
        // happens to store them. A line said for a group takes the highest of its stats. Equal
        // priorities keep the stored order.
        // Each line with the stats it says, which is what decides its asterisks: a line the game merged
        // from several carries the mark where any of them was the corruption's.
        var lines = new List<(int Priority, int Order, string Text, string Range, IReadOnlyCollection<string> From)>();
        void Line(int priority, string text, string range, params IReadOnlyCollection<string> from) =>
            lines.Add((priority, lines.Count, text, range, from));

        int Priority(params string[] names) => names
            .Where(name => stats.Any(each => string.Equals(each.Stat, name, StringComparison.OrdinalIgnoreCase)))
            .Select(StatText.PriorityOf)
            .DefaultIfEmpty(0)
            .Max();

        foreach (var stat in stats)
        {
            if (stat.Stat is { Length: > 0 } member
                && describedGroups.FirstOrDefault(group => group.Stats.Contains(member, StringComparer.OrdinalIgnoreCase)) is { Text: { } oneLine } whole)
            {
                if (groupsSaid.Add(oneLine))
                    Line(Priority([.. whole.Stats]), oneLine, Bracket(whole.Stats[0]), [.. whole.Stats]);

                continue;
            }

            if (stat.Stat is { Length: > 0 } grouped
                && (StatText.PhysicalDamageStats.Contains(grouped) || StatText.EnhancedDamageStats.Contains(grouped)))
            {
                bool physical = StatText.PhysicalDamageStats.Contains(grouped);
                if (physical ? physicalSaid : enhancedSaid)
                    continue;

                int priority = Priority([.. physical ? StatText.PhysicalDamageStats : StatText.EnhancedDamageStats]);

                foreach (var (text, rangeOf) in physical
                             ? StatText.PhysicalDamage(HeldOrNone)
                             : StatText.EnhancedDamage(HeldOrNone))
                {
                    Line(priority, text, Bracket(rangeOf), [.. physical ? StatText.PhysicalDamageStats : StatText.EnhancedDamageStats]);
                }

                if (physical)
                    physicalSaid = true;
                else
                    enhancedSaid = true;

                continue;
            }

            if (stat.Stat is not { Length: > 0 } name || Hidden.Contains(name)
                || withLeader.Contains(name))
                continue;

            // "Socketed (1)" as a property's own wording (item_numsockets_textonly, The Third Eye's):
            // the socket line below says it for an item that has the sockets, and the tooltip said it
            // twice.
            if (sockets > 0 && name.Equals(SocketsTextOnly, StringComparison.OrdinalIgnoreCase))
                continue;

            // "+3 to Fire Skills" is two stats, the grant and the one that words it (ElementSkills), and the game
            // prints the words alone: the grant beside them said it again. A grant with no
            // words for its element keeps a line of its own, so what it gives is still read.
            if (name.Equals(ElementSkills.Grant, StringComparison.OrdinalIgnoreCase)
                && stats.Any(other => ElementSkills.ElementOf(other.Stat) is { } element && element == (stat.Param ?? 0)))
                continue;

            if (StatText.ElementalRuns.TryGetValue(name, out var run)
                && StatText.ElementalDamage(
                       name, stat.Value, Alongside(run.Max),
                       run.Length.Length > 0 ? Alongside(run.Length) : 0) is { Length: > 0 } together)
            {
                // Both ends bracketed on an item with affixes, fire's and lightning's as read: "Adds 90-146 fire damage
                // [1 - 306 to 325 - 432]" (screenshots). Poison's stays bare, unread.
                string span = affixed && name is "firemindam" or "lightmindam" or "coldmindam"
                              && ranges.TryGetValue(name, out var least) && ranges.TryGetValue(run.Max, out var most)
                    ? $"[{least.Min} - {least.Max} to {most.Min} - {most.Max}]"
                    : "";
                Line(Priority(name, run.Max), together, span, name, run.Max, run.Length);
                continue;
            }

            // The three whose parameter picks the wording come first: read through the
            // ordinary path they all word themselves as the first of their set, so every
            // skill tree on every item read as the Amazon's.
            // Purgatory's "+300% Enhanced Damage (based on Equipped Ethereals)": the total for the ethereals its holder
            // wears, five there; its "Gains +60% ... per equipped ethereal item" is in the string itself.
            int value = etherealsWorn is { } worn && Characters.Requirements.PaidByEthereals(name, stat.Value, worn) is { } total
                ? total
                : stat.Value;

            // "+99 to Maximum Damage (Based on Character Level)" on a level 99, where the file holds its eighths of one a
            // level. Rounded down whole, as a sheet counts it (Requirements.PaidByLevel), then back in the units the words read.
            if (holderLevel is { } level && StatText.PerLevelShift(name) is > 0 and var shift)
                value = (value * level >> shift) << shift;

            string said = StatText.ByParameter(name, value, stat.Param ?? 0)
                ?? StatText.Describe(
                    name, value, SkillNames.NameOf(StatText.SkillOf(name, stat.SkillId, stat.Param)),
                    stat.SkillLevel, stat.MaxCharges)
                ?? StatText.Unworded(name, value)
                ?? $"{name} {value}";

            // The bracket the game prints after a line that rolled, and leaves off one that
            // could only ever have been what it is.
            //
            // By the skill it names as well as by its own name, the way the dump and the item
            // panel ask. Asked by the bare name alone, every skill-shaped stat missed - the
            // map keys those as item_addskill_tab#33 - and the tooltip quietly dropped a
            // bracket the game draws: an Imp Casque's "+2 to Lightning Skills" reads [1 - 2]
            // in game and read bare here.
            if (!ranges.TryGetValue(AffixRanges.KeyOf(stat), out var roll))
                ranges.TryGetValue(name, out roll);

            // In the line's own units: a per-level line's eighths as the fraction it says.
            string range = roll is not null && roll.Max > roll.Min
                ? $"[{StatText.ShownNumber(name, roll.Min)} - {StatText.ShownNumber(name, roll.Max)}]"
                : "";

            Line(StatText.PriorityOf(name), said, range, name);
        }

        return lines.OrderByDescending(line => line.Priority).ThenBy(line => line.Order)
                    .Select(line => (line.Text, line.Range, line.From));
    }

    /// <summary>
    /// What a list of stats says, a line each, as an item holding them would say them - for what a gem or
    /// a rune gives its host (<see cref="SocketEffects"/>), which is no item's own list. No brackets: what a
    /// gem gives is fixed.
    /// </summary>
    public static IReadOnlyList<string> Worded(IReadOnlyList<ItemStat> stats) =>
        [.. PropertyLines(stats, new Dictionary<string, RollRange>(), 0).Select(line => line.Text)];

    /// <summary>
    /// The item's stats with what its sockets give counted in, as the game's tooltip counts them: a stat a
    /// socket gives that the item has too is one line with the sum - 524% enhanced damage and a jewel's 38%
    /// read "+562% Enhanced Damage", and the damage line is worked from that - and one the item has not is a
    /// line more. Seen in game on a Stone Barb with two Shael runes, a Perfect Skull and a jewel (a
    /// screenshot): "+40% Increased Attack Speed", "7% Life stolen per hit" from its own 3 and
    /// the skull's 4, the skull's "3% Mana stolen per hit", the jewel's poison.
    /// <para>
    /// A jewel's stats are its own; a gem's or a rune's are what <c>gems.txt</c> gives this kind of host
    /// (<see cref="SocketEffects"/>). A flat damage comes in the kinds the host holds and no other
    /// (<see cref="DamageKinds"/>). The item's own stats are not touched: the sums are copies.
    /// </para>
    /// <para>
    /// Poison from several sources lasts their average, not their sum - three Perfect Emeralds are 300
    /// damage over the 7 seconds of one. Not seen in game on sources of unequal lengths.
    /// </para>
    /// </summary>
    internal static List<ItemStat> WithSockets(Item item, IEnumerable<ItemStat> own)
    {
        var stats = own.ToList();
        if (item.SocketedItems.Count == 0)
            return stats;

        const string PoisonLength = "poisonlength";
        int poisons = stats.Any(stat => string.Equals(stat.Stat, PoisonLength, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

        // The copies made here, so a second socket adds to the first's copy and never to the item's own.
        var copies = new HashSet<ItemStat>(ReferenceEqualityComparer.Instance);

        foreach (var gem in item.SocketedItems)
        {
            IEnumerable<ItemStat> given = gem.IsCompact
                ? SocketEffects.StatsIn(gem.Code, item)
                : gem.StatLists.SelectMany(list => list.Stats);

            bool poisoned = false;

            foreach (var stat in given)
            {
                if (stat.Stat is not { Length: > 0 } name || !DamageKinds.Holds(item, name))
                    continue;

                poisoned |= name.Equals(PoisonLength, StringComparison.OrdinalIgnoreCase);

                int at = stats.FindIndex(each => ItemStats.SameLine(each, stat) && each.MaxCharges == stat.MaxCharges);

                if (at >= 0 && copies.Contains(stats[at]))
                {
                    stats[at].Value += stat.Value;
                    continue;
                }

                var copy = new ItemStat
                {
                    Id = stat.Id, Stat = stat.Stat, SkillTab = stat.SkillTab, CharacterClass = stat.CharacterClass,
                    SkillId = stat.SkillId, SkillLevel = stat.SkillLevel, MaxCharges = stat.MaxCharges,
                    Param = stat.Param, Value = stat.Value + (at >= 0 ? stats[at].Value : 0),
                };
                copies.Add(copy);

                if (at >= 0)
                    stats[at] = copy;
                else
                    stats.Add(copy);
            }

            if (poisoned)
                poisons++;
        }

        if (poisons > 1 && stats.Find(stat => string.Equals(stat.Stat, PoisonLength, StringComparison.OrdinalIgnoreCase)) is { } length
            && copies.Contains(length))
        {
            length.Value /= poisons;
        }

        return stats;
    }

    /// <summary>
    /// A set piece's green bonuses, tier by tier, each line with how much of the set turns it on -
    /// "(2 Items)", as the wiki prints it. Tier k is the list of the k-th set bit of the mask,
    /// bit 0 two pieces (see ItemForge.AddSetBonuses). An elemental run is one line, a group of
    /// four resistances alike one line, as in the item's own.
    /// </summary>
    private static IEnumerable<string> SetBonusLines(Item item)
    {
        if (item.Quality != ItemQuality.Set || item.SetItemMask == 0)
            yield break;

        int list = 0;
        for (int tier = 1; tier <= 5; tier++)
        {
            if ((item.SetItemMask & (1 << (tier - 1))) == 0)
                continue;

            if (++list >= item.StatLists.Count)
                yield break;

            var green = item.StatLists[list].Stats.Where(stat => stat.Stat is { Length: > 0 }).ToList();
            int? Held(string name) =>
                green.Any(stat => string.Equals(stat.Stat, name, StringComparison.OrdinalIgnoreCase))
                    ? green.Where(stat => string.Equals(stat.Stat, name, StringComparison.OrdinalIgnoreCase)).Sum(stat => stat.Value)
                    : null;

            int pieces = tier + 1;
            var said = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // One wording for every line of the tier. Two of the three places added the number alone - "+1 to All
            // Skills2" for "+1 to All Skills (2 Items)" - on every line that was not a group.
            string Tiered(string text) => L($"{text} ({pieces} Items)");

            foreach (var (text, members) in StatText.Grouped(Held))
            {
                said.UnionWith(members);
                yield return Tiered(text);
            }

            foreach (var (_, run) in StatText.ElementalRuns)
            {
                said.Add(run.Max);
                if (run.Length.Length > 0)
                    said.Add(run.Length);
            }

            // As the item's own lines word them: Berserker's Hatchet's green bonus reads "+50% Enhanced Damage" in game,
            // one line (LookTest, a screenshot). Apart, it said a maximum and a minimum.
            foreach (var (text, _) in StatText.PhysicalDamage(Held).Concat(StatText.EnhancedDamage(Held)))
                yield return Tiered(text);
            said.UnionWith(StatText.PhysicalDamageStats);
            said.UnionWith(StatText.EnhancedDamageStats);

            foreach (var stat in green)
            {
                string name = stat.Stat!;
                if (Hidden.Contains(name))
                    continue;

                if (StatText.ElementalRuns.TryGetValue(name, out var run))
                {
                    if (StatText.ElementalDamage(name, stat.Value, Held(run.Max) ?? 0, run.Length.Length > 0 ? Held(run.Length) ?? 0 : 0) is { Length: > 0 } together)
                        yield return Tiered(together);
                    continue;
                }

                if (said.Contains(name))
                    continue;

                string line = StatText.ByParameter(name, stat.Value, stat.Param ?? 0)
                    ?? StatText.Describe(name, stat.Value, SkillNames.NameOf(StatText.SkillOf(name, stat.SkillId, stat.Param)),
                                         stat.SkillLevel, stat.MaxCharges)
                    ?? StatText.Unworded(name, stat.Value)
                    ?? $"{name} {stat.Value}";

                yield return Tiered(line);
            }
        }
    }

    /// <summary>
    /// Stats the game does not print, because they are not properties: the corruption's id
    /// and the flag that paints the red marks. The tooltip says "Corrupted" instead.
    /// </summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "corrupted", "corruptor", "desecrated", "desecrator",

        // A dye tints the picture and prints nothing - see Dyes.
        "transform_dye",

        // Said above "Corrupted" instead, as a fact - see the skin line.
        "item_shiny_appearance",
    };
}
