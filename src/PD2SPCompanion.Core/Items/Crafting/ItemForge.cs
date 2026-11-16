using System.Collections;
using D2SLib.Model.Save;
using D2SLib.Model.TXT;

namespace PD2SPCompanion.Core.Items;

/// <summary>Where in its range each of an item's properties is set when it is made.</summary>
public enum RollAs
{
    /// <summary>The top of every range - what a perfect one looks like.</summary>
    Maximum,

    /// <summary>The bottom of every range.</summary>
    Minimum,

    /// <summary>Somewhere inside each range, as the game would roll it.</summary>
    Random,
}

/// <summary>An item that was made, and anything worth saying about how it came out.</summary>
/// <param name="Item">The item.</param>
/// <param name="Notes">Everything that happened while making it, in plain words.</param>
/// <param name="Sound">
/// Whether it was made without compromise. False when a row asked for something that could not
/// be written as asked - a value wider than its own field, or a property this season cannot
/// write - which means the item is <b>not what its row describes</b>.
/// <para>
/// A caller filling a vault should skip anything unsound rather than store a plausible-looking
/// wrong item. A value that will not fit is usually a sign the row's shape is misread rather
/// than a number to trim: <c>skill-rand</c> looked exactly like this until the real specimens
/// showed its min and max were skill ids.
/// </para>
/// </param>
public sealed record Forged(Item Item, IReadOnlyList<string> Notes, bool Sound = true);

/// <summary>
/// Making an item out of its own table row, rather than finding one and editing it.
/// <para>
/// Proven in game with Tyrael's Might: built from <c>uniqueitems.txt</c> alone,
/// loaded, and read exactly as the row says - Defense 1827, every ranged property at its
/// maximum, Indestructible, and no Required Strength line at all because
/// <c>Requirements -100%</c> took it to zero. Compared against a genuine specimen from the
/// item pack it was <b>identical on all twelve properties</b>.
/// </para>
/// <para>
/// Nothing here is copied from an existing item. The flag bits were measured across 6,120
/// real items instead: see <see cref="Identified"/> and <see cref="Always"/>.
/// </para>
/// </summary>
public static class ItemForge
{
    /// <summary>
    /// The bit that says an item has been identified. Set on 5,425 of 6,120 real items, and
    /// on every compact item, ear, runeword and socketed item without exception.
    /// </summary>
    public const int Identified = 4;

    /// <summary>The bit set when an item has sockets, empty or filled.</summary>
    public const int Socketed = 11;

    /// <summary>The stat <c>sock</c> names, which is never written - see <see cref="From"/>.</summary>
    public const string SocketCount = "item_numsockets";

    /// <summary>
    /// The bit every item carries. Measured at <b>6,120 of 6,120</b> across a player's
    /// characters and the whole item pack, with no exception of any kind - so it is set here
    /// without knowing what it means, because leaving it off would make an item unlike every
    /// real one.
    /// </summary>
    public const int Always = 23;

    /// <summary>The bit that marks an item ethereal. Measured as exactly that, 1,700 items.</summary>
    public const int Ethereal = 22;

    /// <summary>How wide an item's flag array is.</summary>
    private const int FlagBits = 32;

    /// <summary>The one row of a quality table the game does not count. See <see cref="QualityRows"/>.</summary>
    internal const string Marker = "Expansion";

    /// <summary>
    /// A material as the shared stash's materials tab hands it out: a stack item such as
    /// <c>r01s</c> holding <paramref name="quantity"/>, or - for the two rejuvenation potions,
    /// which are not stack items - a plain compact one.
    /// <para>
    /// Checked against the game rather than trusted: <c>pd2spc materials --check</c> makes each
    /// of the 98 and compares it, id and position wiped, byte for byte with the one the game
    /// itself took out of the tab.
    /// </para>
    /// </summary>
    /// <param name="stampAs">The item version this character's items carry - see <see cref="Unique"/>.</param>
    public static Item Material(string code, int quantity, string stampAs)
    {
        var item = new Item
        {
            Header = 0x4D4A,
            Code = code.PadRight(4, ' '),
            Quality = ItemQuality.Normal,
            Version = stampAs,
            Mode = ItemMode.Stored,
            Location = ItemLocation.None,
            Flags = new BitArray(FlagBits),
            ItemLevel = 99,
        };

        item.Flags[Identified] = true;
        item.Flags[Always] = true;

        if (Stacks.IsStackable(code))
        {
            // One is written as one. Stacks.Shown records a single Eth Rune in a stack stored as
            // nought, but every stack of one the materials tab handed out stores 1, and this
            // makes what the tab makes. The game shows either as "Quantity: 1".
            item.Quantity = (ushort)Math.Max(1, quantity);

            // An extended item always carries a property list, if only its terminator.
            _ = ItemStats.Home(item);
        }
        else
        {
            item.IsCompact = true;
        }

        return item;
    }

    /// <summary>
    /// A Horadric Cube, as the game drops one: an extended item of normal quality, identified, with
    /// an empty property list - checked, as the materials are, against every cube in the corpus
    /// (<c>pd2spc cubecheck</c>), id, place, item level and the game's "just picked up" flag aside.
    /// <paramref name="itemLevel"/> is where it dropped: the Halls of the Dead in Normal, 13, as
    /// a game-written save's reads.
    /// </summary>
    /// <param name="stampAs">The item version this character's items carry - see <see cref="Unique"/>.</param>
    public static Item Cube(string stampAs, byte itemLevel = 13)
    {
        var item = new Item
        {
            Header = 0x4D4A,
            Code = "box ",
            Quality = ItemQuality.Normal,
            Version = stampAs,
            Mode = ItemMode.Stored,
            Location = ItemLocation.None,
            Flags = new BitArray(FlagBits),
            ItemLevel = itemLevel,
        };

        item.Flags[Identified] = true;
        item.Flags[Always] = true;

        // An extended item always carries a property list, if only its terminator.
        _ = ItemStats.Home(item);
        return item;
    }

    /// <summary>
    /// A quest item as the game hands one over - a compact item, identified, whose three bits after
    /// the code (<see cref="Item.NumberOfSocketedItems"/> on a compact item) hold the difficulty it
    /// belongs to, 0 Normal to 2 Hell. Checked against every Mephisto's Soulstone in the corpus and
    /// the one the game gave QuestTest (<c>pd2spc soulstonecheck</c>), place aside.
    /// </summary>
    /// <param name="stampAs">The item version this character's items carry - see <see cref="Unique"/>.</param>
    public static Item QuestItem(string code, int difficulty, string stampAs)
    {
        var item = new Item
        {
            Header = 0x4D4A,
            Code = code.PadRight(4, ' '),
            Quality = ItemQuality.Normal,
            Version = stampAs,
            Mode = ItemMode.Stored,
            Location = ItemLocation.None,
            Flags = new BitArray(FlagBits),
            NumberOfSocketedItems = (byte)Math.Clamp(difficulty, 0, 2),
        };

        item.Flags[Identified] = true;
        item.Flags[Always] = true;
        item.IsCompact = true;
        return item;
    }

    /// <summary>Every unique this season has, in the order the table lists them.</summary>
    public static IReadOnlyList<string> UniqueNames()
    {
        var table = D2SLib.Core.TXT.ItemsTXT.UniqueTXT;
        if (table is null)
            return [];

        return [.. table.Rows
            .Select(row => Cell(row, "index"))
            .Where(name => name is { Length: > 0 }
                           && !name.Equals(Marker, StringComparison.OrdinalIgnoreCase))
            .Select(name => name!)];
    }

    /// <summary>
    /// Makes the unique of this name, as the installed season describes it.
    /// </summary>
    /// <param name="name">Its <c>index</c> in <c>uniqueitems.txt</c>, such as "Tyrael's Might".</param>
    /// <param name="stampAs">
    /// The item version this season writes. It is not derivable from the tables - a save
    /// holds one era of items and only a save the game wrote says which - so the caller must
    /// supply it rather than have a guess baked in here.
    /// </param>
    /// <param name="roll">Where in each range the properties are set.</param>
    /// <param name="random">
    /// Used only for <see cref="RollAs.Random"/>. Pass a seeded one to make the same item
    /// twice; see <see cref="SeasonFit.Seeded"/>.
    /// </param>
    public static Forged Unique(
        string name, string stampAs, RollAs roll = RollAs.Maximum, Random? random = null)
    {
        var table = D2SLib.Core.TXT.ItemsTXT.UniqueTXT
            ?? throw new InvalidOperationException(
                "uniqueitems.txt is not loaded. Call SaveTables.Install first.");

        // Walked rather than indexed, so the FileIndex comes out of the same pass that finds
        // the row. The game does not count the marker row, so a raw row number is the wrong
        // answer by one for everything below it - writing 312 for Tyrael's Might instead of
        // 311 produced an item that read as a plain Sacred Armor with every property
        // unaccounted for.
        //
        // The enabled row of that name when there is one: Azurewrath has a disabled row before the
        // live one, and taking the first made the retired item. A name with several enabled rows
        // (Rainbow Facet) gives the first of them - UniqueAt picks one by number.
        int fileIndex = -1, firstAt = -1, enabledAt = -1;
        TXTRow? first = null, enabled = null;

        foreach (var row in table.Rows)
        {
            string? index = Cell(row, "index");
            if (index is null or { Length: 0 })
                continue;

            if (index.Equals(Marker, StringComparison.OrdinalIgnoreCase))
                continue;

            fileIndex++;

            if (!index.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (first is null)
                (first, firstAt) = (row, fileIndex);

            if (Cell(row, "enabled") == "1")
            {
                (enabled, enabledAt) = (row, fileIndex);
                break;
            }
        }

        var (found, at) = enabled is not null ? (enabled, enabledAt) : (first, firstAt);

        if (found is null)
            throw new ArgumentException($"This season has no unique called '{name}'.", nameof(name));

        return From(found, (uint)at, ItemQuality.Unique, "code", stampAs, roll, random);
    }

    /// <summary>
    /// Every enabled unique, one per row, with the number the game knows it by - its FileIndex,
    /// which is what an item of it carries. By row rather than by name: some names have several
    /// rows (Rainbow Facet eight, one per element and trigger; Arm of King Leoric, Cyclopean
    /// Roar and Akarat's Devotion two each in S13), and looking one up by name finds the first.
    /// </summary>
    public static IReadOnlyList<int> EnabledUniques()
    {
        var table = D2SLib.Core.TXT.ItemsTXT.UniqueTXT;
        if (table is null)
            return [];

        var found = new List<int>();
        int fileIndex = -1;

        foreach (var row in table.Rows)
        {
            if (Cell(row, "index") is not { Length: > 0 } index
                || index.Equals(Marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fileIndex++;

            if (Cell(row, "enabled") == "1")
                found.Add(fileIndex);
        }

        return found;
    }

    /// <summary>
    /// Makes the unique of this FileIndex - the one row, where <see cref="Unique"/> takes a name
    /// and so the first row of that name. Counted as <see cref="Unique"/> counts, skipping the
    /// marker row the game does not number.
    /// </summary>
    /// <param name="ethereal">Made ethereal whatever the row says.</param>
    public static Forged UniqueAt(
        int fileIndex, string stampAs, RollAs roll = RollAs.Maximum, Random? random = null,
        bool ethereal = false)
    {
        var table = D2SLib.Core.TXT.ItemsTXT.UniqueTXT
            ?? throw new InvalidOperationException(
                "uniqueitems.txt is not loaded. Call SaveTables.Install first.");

        int at = -1;

        foreach (var row in table.Rows)
        {
            if (Cell(row, "index") is not { Length: > 0 } index
                || index.Equals(Marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (++at == fileIndex)
                return From(row, (uint)fileIndex, ItemQuality.Unique, "code", stampAs, roll, random, ethereal);
        }

        throw new ArgumentOutOfRangeException(nameof(fileIndex), $"This season has no unique number {fileIndex}.");
    }

    /// <summary>
    /// Every set piece, by FileIndex - the number an item of it carries. <c>setitems.txt</c> has
    /// no <c>enabled</c> column: every row but the marker is a piece.
    /// </summary>
    public static IReadOnlyList<int> SetItems()
    {
        var table = D2SLib.Core.TXT.ItemsTXT.SetItemsTXT;
        if (table is null)
            return [];

        var found = new List<int>();
        int fileIndex = -1;

        foreach (var row in table.Rows)
        {
            if (Cell(row, "index") is not { Length: > 0 } index
                || index.Equals(Marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            found.Add(++fileIndex);
        }

        return found;
    }

    /// <summary>
    /// The FileIndex of the set piece <c>setitems.txt</c> lists under this index - the name the game's own
    /// clipboard text gives a piece by - counted as <see cref="SetItems"/>; null where no row has it.
    /// </summary>
    public static int? SetItemNamed(string name)
    {
        var table = D2SLib.Core.TXT.ItemsTXT.SetItemsTXT;
        if (table is null)
            return null;

        int at = -1;
        foreach (var row in table.Rows)
        {
            if (Cell(row, "index") is not { Length: > 0 } index
                || index.Equals(Marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            at++;
            if (index.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                return at;
        }

        return null;
    }

    /// <summary>
    /// The set a piece of this FileIndex belongs to, by the name <c>setitems.txt</c> gives it in
    /// its <c>set</c> column; null for a number past the table. Counted as <see cref="SetItems"/>.
    /// </summary>
    public static string? SetOf(int fileIndex)
    {
        var table = D2SLib.Core.TXT.ItemsTXT.SetItemsTXT;
        if (table is null)
            return null;

        // The pieces' sets by FileIndex, made once a table: asked of every set piece worn for every count of the
        // gear's stats - a walk of the table each time, and every judge of what can be used counts several times
        // A season's table is a new one, and made again.
        var kept = _setsOf;
        if (!ReferenceEquals(kept.Table, table) || kept.Rows != table.Rows.Count)
        {
            var sets = new List<string?>();
            foreach (var row in table.Rows)
            {
                if (Cell(row, "index") is not { Length: > 0 } index
                    || index.Equals(Marker, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sets.Add(Cell(row, "set"));
            }

            _setsOf = kept = (table, table.Rows.Count, [.. sets]);
        }

        return fileIndex >= 0 && fileIndex < kept.Sets.Length ? kept.Sets[fileIndex] : null;
    }

    private static (object? Table, int Rows, string?[] Sets) _setsOf = (null, -1, []);

    /// <summary>
    /// Makes the set piece of this FileIndex, counted as <see cref="SetItems"/> counts - the
    /// marker row not numbered. Its base is in the row's <c>item</c> column, not <c>code</c>.
    /// </summary>
    /// <param name="ethereal">Made ethereal whatever the row says.</param>
    public static Forged SetItemAt(
        int fileIndex, string stampAs, RollAs roll = RollAs.Maximum, Random? random = null,
        bool ethereal = false)
    {
        var row = SetRowAt(fileIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(fileIndex), $"This season has no set piece number {fileIndex}.");

        var forged = From(row, (uint)fileIndex, ItemQuality.Set, "item", stampAs, roll, random, ethereal);
        var notes = new List<string>(forged.Notes);
        AddSetBonuses(forged.Item, row, roll, random, notes);
        DamageKinds.Settle(forged.Item);
        return forged with { Notes = notes, Sound = IsSound(notes) };
    }

    /// <summary>The <c>setitems.txt</c> row of this FileIndex, counted as <see cref="SetItems"/>; null past the table.</summary>
    private static TXTRow? SetRowAt(int fileIndex)
    {
        var table = D2SLib.Core.TXT.ItemsTXT.SetItemsTXT
            ?? throw new InvalidOperationException(
                "setitems.txt is not loaded. Call SaveTables.Install first.");

        int at = -1;

        foreach (var row in table.Rows)
        {
            if (Cell(row, "index") is not { Length: > 0 } index
                || index.Equals(Marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (++at == fileIndex)
                return row;
        }

        return null;
    }

    /// <summary>
    /// Gives a set piece an older forge made the green bonus lists it was made without
    /// (<see cref="AddSetBonuses"/>), as the forge makes them now. Only a piece that stores none -
    /// no set mask, no list past its own - and whose row has bonus tiers; a game drop always
    /// stores them, so one is never touched. Returns how many lists went on; 0 leaves the item as
    /// it was.
    /// </summary>
    public static int RestoreSetBonuses(Item item, RollAs roll, Random? random, List<string> notes)
    {
        if (item.Quality != ItemQuality.Set || item.SetItemMask != 0 || item.StatLists.Count != 1
            || SetRowAt((int)item.FileIndex) is not { } row)
        {
            return 0;
        }

        int before = item.StatLists.Count;
        AddSetBonuses(item, row, roll, random, notes);
        DamageKinds.Settle(item);
        return item.StatLists.Count - before;
    }

    /// <summary>
    /// A set piece's own green bonuses, kept on the item as the game keeps them: one property
    /// list for each <c>aprop</c> tier the row fills (its <c>a</c> and <c>b</c> halves), and a
    /// bit of the item's set mask for each - bit 0 for <c>aprop1</c>, the two-piece bonus. Every
    /// real set piece in the live saves stores them so, mask matching its row's tiers exactly (nine
    /// game drops); left off, a forged piece gives no green
    /// bonus at any count worn. The wearing turns them on; the item only carries them.
    /// </summary>
    private static void AddSetBonuses(Item item, TXTRow row, RollAs roll, Random? random, List<string> notes)
    {
        for (int tier = 1; tier <= 5; tier++)
        {
            var halves = new List<(string Code, int Min, int Max, int Param)>();
            foreach (string half in new[] { "a", "b" })
            {
                if (Cell(row, $"aprop{tier}{half}") is not { Length: > 0 } code)
                    continue;

                int min = int.TryParse(Cell(row, $"amin{tier}{half}"), out int lo) ? lo : 0;
                int max = int.TryParse(Cell(row, $"amax{tier}{half}"), out int hi) ? hi : min;
                int param = AffixRanges.ParameterOf(code, Cell(row, $"apar{tier}{half}") ?? "");
                halves.Add((code, min, max, param));
            }

            if (halves.Count == 0)
                continue;

            var list = new ItemStatList();
            foreach (var (code, min, max, param) in halves)
                Write(list, code, AffixRanges.Written(code, min, max, Chosen(min, max, roll, random), param), notes);

            Complete(list, notes);
            item.SetItemMask |= (byte)(1 << (tier - 1));
            item.StatLists.Add(list);
            notes.Add($"set bonus for {tier + 1} pieces");
        }
    }

    /// <summary>
    /// The shared body of making a fixed-property item - a unique or a set piece. They differ
    /// only in which table they come from and what the column holding their base is called:
    /// a unique keeps it in <c>code</c> and a set item in <c>item</c>.
    /// </summary>
    private static Forged From(
        TXTRow row, uint fileIndex, ItemQuality quality, string baseColumn,
        string stampAs, RollAs roll, Random? random, bool ethereal = false)
    {
        var notes = new List<string>();
        string code = Cell(row, baseColumn)
            ?? throw new InvalidDataException($"That row names no base in '{baseColumn}'.");

        var item = new Item
        {
            Header = 0x4D4A,
            Code = code.PadRight(4, ' '),
            FileIndex = fileIndex,
            Quality = quality,
            Version = stampAs,
            Mode = ItemMode.Stored,
            Location = ItemLocation.None,
            Flags = new BitArray(FlagBits),
        };

        item.Flags[Identified] = true;
        item.Flags[Always] = true;

        if (int.TryParse(Cell(row, "lvl"), out int level))
            item.ItemLevel = (byte)Math.Clamp(level, 1, 99);

        // A ring, an amulet, a charm or a jewel wears one of several pictures, and says which: all
        // 34 real ones in the live saves carry the flag and a number. Left off, the item fell back
        // to its base's own file - a plain ring, and for a jewel a gem (invgswe). The first picture,
        // where the game would roll one.
        item.HasMultipleGraphics = ItemTypes.VariantCount(code) > 1;

        // ---- what the base brings ----------------------------------------------------
        if (ethereal)
        {
            item.Flags[Ethereal] = true;
            notes.Add("ethereal, as asked");
        }

        DressBase(item, code, Properties(row).Any(p => p.Code.Equals("ac%", StringComparison.OrdinalIgnoreCase)), notes);

        // ---- its properties ------------------------------------------------------------
        var list = ItemStats.Home(item);

        foreach (var (propCode, min, max, param) in Properties(row))
        {
            // Ethereal is not a property at all - it is a flag on the item, which is why
            // its row names no stat. Four uniques were coming out solid because of it.
            // On a set piece's row it does nothing: Sazabi's Cobalt Redeemer is never ethereal (Ethereals.Of).
            if (AffixRanges.FunctionOf(propCode) == AffixRanges.EtherealFunction)
            {
                if (quality != ItemQuality.Set)
                {
                    item.Flags[Ethereal] = true;
                    notes.Add("ethereal");
                }
                continue;
            }

            // Ormus' Robes' random Sorceress skill and its cast: a skill the row cannot name (RandomSkillCast).
            if (RandomSkillCast.Is(propCode))
            {
                int skill = RandomSkillCast.Drawn(random);
                Write(list, propCode, RandomSkillCast.Written(param, min, max, skill), notes);
                continue;
            }

            int value = Chosen(min, max, roll, random);
            var written = AffixRanges.Written(propCode, min, max, value, param);

            // Nor are sockets: the number is the count in the item's header, and no
            // item_numsockets stat is ever written - measured on a real Witchwild String, three
            // sockets and no such stat, and on the whole PD2 market, where not one listing of
            // any quality stores it. A row with no range gives the count as its parameter
            // (Hone Sundan, sock par 3); AffixRanges.Written reads that. Not held to the base's
            // ceiling - at each row's own level none of the 27 reaches it. Known by the stat
            // it writes, because FunctionOf only knows the functions of rows that name no stat.
            if (written is [{ Stat: var only, Value: var sockets }]
                && only.Equals(SocketCount, StringComparison.OrdinalIgnoreCase))
            {
                item.TotalNumberOfSockets = (byte)Math.Clamp(sockets, 0, 15);
                item.Flags[Socketed] = sockets > 0;
                notes.Add($"{sockets} sockets");
                continue;
            }

            Write(list, propCode, written, notes);
        }

        // An ethereal armour stores its defence with the bonus in it - known only now, since
        // a row's own ethereal property is met in the loop above.
        if (item.Flags[Ethereal] && item.Armor > 0)
        {
            item.Armor = (ushort)ItemBases.Ethereal(item.Armor);
            notes.Add($"defense {item.Armor}, ethereal");
        }

        // A cast or charges the row leaves to the item level - Snowclash's level 0 Blizzard.
        AffixRanges.DerivePacked(item);

        Complete(list, notes);

        bool sound = IsSound(notes);

        // A flat damage as the base holds it: every copy on what is no weapon, a weapon its own kinds.
        DamageKinds.Settle(item);
        return new Forged(item, notes, sound);
    }

    /// <summary>Whether every property written went on as it should: no note says one would not fit or writes nothing.</summary>
    private static bool IsSound(IEnumerable<string> notes) =>
        !notes.Any(note => note.Contains("cannot hold") || note.Contains("writes no property"));

    /// <summary>
    /// A property's stats onto a list, each held to what its field can store - the half of
    /// making an item every maker here shares, whatever table the property came from.
    /// </summary>
    private static void Write(
        ItemStatList list, string propCode, IReadOnlyList<AffixRanges.Wrote> written, List<string> notes)
    {
        if (written.Count == 0)
        {
            notes.Add($"'{propCode}' writes no property this season knows, so it was left off");
            return;
        }

        foreach (var wrote in written)
        {
            int held = Fits(wrote.Stat, wrote.Value);
            if (held != wrote.Value)
            {
                notes.Add($"'{propCode}' asked for {wrote.Stat} {wrote.Value}, which its field "
                          + $"cannot hold; written as {held}");
            }

            list.Stats.Add(ItemStats.LineFrom(wrote with { Value = held }));
        }
    }

    /// <summary>
    /// A runeword <b>to look at</b>: the word's own properties at their maximum, on a base it may
    /// be made in, its runes in the sockets, so the item panel's tooltip words it and brackets its
    /// ranges as it does a real one - what its runes add counted in from the sockets, as on a real one
    /// (<see cref="ItemLines"/>). The grail's cards are what this is for.
    /// <para>
    /// <b>Not an item to write anywhere.</b> A real runeword keeps its properties in a list of their
    /// own and its recipe as a runeword id in the header; this has neither, and a save holding it
    /// would not hold the item the game makes. Which base is taken says nothing - the grail shows
    /// runewords without one.
    /// </para>
    /// </summary>
    /// <param name="types">
    /// The bases to make it in, when not every one the word allows - a version of it for one kind
    /// of base, since what its runes add depends on the kind.
    /// </param>
    public static Forged RunewordShown(Runewords.Word word, string stampAs, IReadOnlyList<string>? types = null)
    {
        var notes = new List<string>();
        var allowed = types ?? word.Types;

        // Never a base the row excludes: Wind's melee row takes no throwing weapon.
        string code = BaseFor(allowed, word.ExcludedTypes)
            ?? throw new InvalidDataException(L($"No base this season can hold {Runewords.GameName(word)}."));

        var item = new Item
        {
            Header = 0x4D4A,
            Code = code.PadRight(4, ' '),
            Quality = ItemQuality.Normal,
            Version = stampAs,
            Mode = ItemMode.Stored,
            Location = ItemLocation.None,
            Flags = new BitArray(FlagBits),
            ItemLevel = 99,
        };

        item.Flags[Identified] = true;
        item.Flags[Always] = true;
        item.Flags[Socketed] = true;
        item.IsRuneword = true;
        item.TotalNumberOfSockets = (byte)word.Runes.Length;

        foreach (string rune in word.Runes)
            Socket(item, Material(rune, 1, stampAs));

        var list = ItemStats.Home(item);

        foreach (var mod in word.Mods)
            Write(list, mod.Code, AffixRanges.Written(mod.Code, mod.Min, mod.Max, mod.Max, mod.Param), notes);

        // Not what its runes add in this kind of base - an Infinity's two Bers its 40% Crushing Blow,
        // its Mal the Prevent Monster Heal, its Ist the 30% Magic Find. They are not written in here:
        // the tooltip counts them in from the runes in the sockets, for this and for a
        // real one alike, and written here as well they would be said twice.
        Merge(list);
        Complete(list, notes);

        // A flat damage as the base holds it: every copy on what is no weapon, a weapon its own kinds.
        DamageKinds.Settle(item);
        return new Forged(item, notes, true);
    }

    /// <summary>
    /// Puts <paramref name="gem"/> in <paramref name="host"/>'s next hole, marked as the game marks
    /// a socketed item: mode Socket, its x the hole's number, no page or location of its own.
    /// <para>
    /// Measured, not assumed: all 13 socketed runes and gems on a Spear Amazon - Duriel's
    /// Shell, Peace, Purity, Insight and a helm - carry mode Socket and x 0, 1, 2 in hole order.
    /// A jewel left as a stored item at x 0 read back in the editor and was dropped by the game:
    /// a Hawk Helm bought from the trade site loaded "Socketed (3)" with nothing in it.
    /// </para>
    /// <para>
    /// <b>A rune or a gem goes in compact</b>, whatever form it came as. Season 14 keeps a loose one as a stack of
    /// its own (<c>r04</c>, extended, a quantity) and a socketed one as Season 13 did: a Nef socketed into
    /// Bonehew came back a simple item, version 105, flags identified and compact, nothing else. Put in
    /// as it was, the socket would hold a stack.
    /// </para>
    /// </summary>
    public static void Socket(Item host, Item gem)
    {
        if (!gem.IsCompact && Stacks.IsStackable(gem.Code?.Trim() ?? ""))
        {
            gem.IsCompact = true;
            gem.Quantity = 0;
            gem.ItemLevel = 0;
            gem.Quality = 0;
            gem.StatLists.Clear();
            gem.NumberOfSocketedItems = 0;
        }

        gem.Mode = ItemMode.Socket;
        gem.Location = ItemLocation.None;
        gem.Page = 0;
        gem.X = (byte)host.SocketedItems.Count;
        gem.Y = 0;

        host.SocketedItems.Add(gem);
        host.NumberOfSocketedItems = (byte)host.SocketedItems.Count;
    }

    /// <summary>
    /// A gem or rune taken out of a socket, in the form it lies loose in: where the season stacks them (Season 14),
    /// the stack of one its tab hands out (<see cref="Material"/>, a new id), since a socket keeps it compact
    /// (<see cref="Socket"/>); itself everywhere else - a jewel, or any Season 13 filler.
    /// </summary>
    public static Item Loose(Item gem)
    {
        string code = gem.Code?.Trim() ?? "";
        if (!gem.IsCompact || !Stacks.IsStackable(code))
            return gem;

        var loose = Material(code, 1, gem.Version);
        loose.Id = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
        return loose;
    }

    /// <summary>
    /// Numbers <paramref name="host"/>'s gems again 0, 1, 2 in list order, after one came out. A
    /// gem's x is its hole, and the hole picks where the game draws it (SocketLayout): a gap left
    /// by an unsocket would put the next gem in the wrong place, or a place with no hole.
    /// </summary>
    public static void Renumber(Item host)
    {
        for (int hole = 0; hole < host.SocketedItems.Count; hole++)
            host.SocketedItems[hole].X = (byte)hole;
    }

    /// <summary>
    /// A base the given types allow, by code - the first, so the same word always shows the same.
    /// Which one says nothing: the grail shows runewords without their base.
    /// </summary>
    /// <remarks>
    /// Kept per question while the tables stand: each asking walked every base and sorted what it
    /// found, and the grail's runewords and the crafting wizard's cards ask it some hundreds of times at
    /// start-up and again at every character opened (a trace: 94 ms and 5).
    /// </remarks>
    public static string? BaseFor(IReadOnlyList<string> types, IReadOnlyList<string>? excluded = null)
    {
        int tables = GameFiles.InstalledTables.Generation;
        if (_baseForTables != tables)
        {
            _baseFor.Clear();
            _baseForTables = tables;
        }

        string key = string.Join(',', types) + '|' + (excluded is null ? "" : string.Join(',', excluded));
        return _baseFor.GetOrAdd(key, _ => ItemBases.Codes
            .Where(each => ItemTypes.LineageOfBase(each) is { Count: > 0 } lineage
                           && types.Any(lineage.Contains)
                           && !(excluded ?? []).Any(lineage.Contains))
            .OrderBy(each => each, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault());
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> _baseFor = new();
    private static volatile int _baseForTables = -1;

    /// <summary>
    /// What a base brings before any property: its stack where it stacks, its durability, and its
    /// defence at the top of the table's range. <paramref name="enhanced"/> says the item spawns with
    /// +% Enhanced Defense, where the base defence is <c>maxac + 1</c> - one above the top of the range,
    /// not a roll inside it: Tyrael's Might stores 731 against a maximum of 730, and 731 x 2.5 is the
    /// 1827 the game prints.
    /// <para>
    /// A stackable base - a throwing weapon, a quiver - is forged a full stack, the table's
    /// <c>maxstack</c> (Stacks.MaximumFor). Left at nought, a library javelin was gone with its first
    /// throw. Real ladder throwing weapons store 50 or 100 of the table's 25-100,
    /// and a durability beside it; quivers a quantity alone.
    /// </para>
    /// </summary>
    private static void DressBase(Item item, string code, bool enhanced, List<string> notes)
    {
        // The automod a base always carries, where its group has one row and nothing to choose - a melee weapon's
        // splash, a staff's cast rate, a boot's run/walk - put on as the game puts it on every drop.
        // Forged without it, a mace had no splash. Never on a unique or a set piece (no rows).
        if (AffixRanges.AutomagicRowsFor(item) is [var only] && !item.IsAutoAffix)
        {
            AffixRanges.PutAutomagic(item, only.Id);
            notes.Add($"automod {only.Id}, its base's only one");
        }

        if (Stacks.MaximumFor(code) is { } stack)
            item.Quantity = (ushort)stack;

        var basis = D2SLib.Core.TXT.ItemsTXT.GetByCode(code);
        if (basis is null)
        {
            notes.Add($"this season has no base '{code}', so defense and durability are unset");
            return;
        }

        if (int.TryParse(Cell(basis, "durability"), out int durability) && durability > 0)
        {
            item.MaxDurability = (ushort)durability;
            item.Durability = (ushort)durability;
        }

        if (int.TryParse(Cell(basis, "maxac"), out int maxac) && maxac > 0)
        {
            item.Armor = (ushort)(enhanced ? maxac + 1 : maxac);
            if (enhanced)
                notes.Add($"defense {item.Armor}, being the base maximum {maxac} plus one for the enhanced defense");
        }
    }

    /// <summary>
    /// A runeword made for real - in <paramref name="code"/>, a base with its defence and
    /// durability, its runes socketed, the word's number the game reads it by, and every property
    /// at the top of its range - where <see cref="RunewordShown"/> is a picture for a card.
    /// Item level 99, so the base holds as many sockets as it can.
    /// </summary>
    public static Forged Runeword(Runewords.Word word, string code, string stampAs)
    {
        var notes = new List<string>();

        var item = new Item
        {
            Header = 0x4D4A,
            Code = code.PadRight(4, ' '),
            Quality = ItemQuality.Normal,
            Version = stampAs,
            Mode = ItemMode.Stored,
            Location = ItemLocation.None,
            Flags = new BitArray(FlagBits),
            ItemLevel = 99,
        };

        item.Flags[Identified] = true;
        item.Flags[Always] = true;
        item.Flags[Socketed] = true;
        item.IsRuneword = true;
        item.RunewordId = (uint)(Runewords.IdOf(word) ?? 0);
        item.TotalNumberOfSockets = (byte)word.Runes.Length;

        DressBase(item, code, enhanced: false, notes);

        foreach (string rune in word.Runes)
            Socket(item, Material(rune, 1, stampAs));

        // The word's properties are its own list in the save, beside the base's - the slot the runeword header
        // names (5). One list only, the writer finds no list there and the vault could not copy the item; as
        // TradeItems lays a bought runeword out. Made apart from the start: the base's own list keeps what the base
        // brings - its automod (DressBase) - which a list written as one and moved over took with it.
        ItemStats.Home(item);
        var list = new ItemStatList();
        item.StatLists.Add(list);
        item.RunewordListSlot = 5;

        foreach (var mod in word.Mods)
            Write(list, mod.Code, AffixRanges.Written(mod.Code, mod.Min, mod.Max, mod.Max, mod.Param), notes);

        // Not what its runes add. A tooltip or a trade listing counts them in (ItemLines);
        // a real item does not store them - the game reads them off the socketed
        // runes, and written here as well they would count twice. The legitimacy check said so:
        // Lore's Ort and Sol, 30% lightning resist and 7 damage reduced in a helm, "unaccounted".
        Merge(list);
        AffixRanges.DerivePacked(item);
        Complete(list, notes);

        bool sound = !notes.Any(note => note.Contains("cannot hold") || note.Contains("writes no property"));
        // A flat damage as the base holds it: every copy on what is no weapon, a weapon its own kinds.
        DamageKinds.Settle(item);
        return new Forged(item, notes, sound);
    }

    /// <summary>
    /// A plain base with <paramref name="sockets"/> empty holes - Normal quality, its defence and
    /// durability, nothing else - as <see cref="Runeword"/> makes its base before the runes go in.
    /// For putting a shape in front of the game to see where it draws its sockets
    /// (a Troll Belt [S], the one 2x1 base that takes one).
    /// </summary>
    public static Forged PlainBase(string code, int sockets, string stampAs)
    {
        var notes = new List<string>();

        var item = new Item
        {
            Header = 0x4D4A,
            Code = code.PadRight(4, ' '),
            Quality = ItemQuality.Normal,
            Version = stampAs,
            Mode = ItemMode.Stored,
            Location = ItemLocation.None,
            Flags = new BitArray(FlagBits),
            ItemLevel = 99,
        };

        item.Flags[Identified] = true;
        item.Flags[Always] = true;
        if (sockets > 0)
        {
            item.Flags[Socketed] = true;
            item.TotalNumberOfSockets = (byte)sockets;
        }

        DressBase(item, code, enhanced: false, notes);

        var list = ItemStats.Home(item);
        Merge(list);
        AffixRanges.DerivePacked(item);
        Complete(list, notes);

        bool sound = !notes.Any(note => note.Contains("cannot hold") || note.Contains("writes no property"));
        // A flat damage as the base holds it: every copy on what is no weapon, a weapon its own kinds.
        DamageKinds.Settle(item);
        return new Forged(item, notes, sound);
    }

    /// <summary>
    /// Whether the Forge makes this base at all (<see cref="ForgeScope.Makes"/>): gear, what rolls affixes
    /// - a ring, a charm, a jewel, a map - and PD2's loose materials that the materials tab does not keep.
    /// Never a quest item, nor a row the game has no use for. Runes, gems and the rest are the materials
    /// tab's.
    /// </summary>
    public static bool Makes(string code) => ForgeScope.Makes(code);

    /// <summary>
    /// The qualities a base can be made in with nothing named - no unique, no set piece, no runeword - by
    /// what its type says of itself (<see cref="ItemTypes.QualityOf"/>): a ring is never Normal, a charm
    /// never Rare. Superior where the superior table has a row for it; Crafted where a recipe takes it.
    /// <para>
    /// <b>A type that is only ever normal is normal and nothing else</b> - a throwing potion, a material:
    /// the superior table's <c>thrown</c> column reaches a potion through its parents, but the type's own
    /// word comes first, as it does for affixes (<see cref="ItemTypes.CanRollAffixes"/>). And <b>a base
    /// that exists only as its unique</b> (<see cref="ForgeScope.OnlyNamed"/>) has no quality of its own
    /// to be made in: none is listed.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ItemQuality> QualitiesOf(string code)
    {
        if (ForgeScope.OnlyNamed(code))
            return [];

        string? type = ItemTypes.TypeOf(code);
        var says = ItemTypes.QualityOf(type);
        bool rolls = ItemTypes.CanRollAffixes(type);
        var probe = new Item { Code = code.PadRight(4, ' '), Quality = ItemQuality.Superior, ItemLevel = 99, Flags = new BitArray(FlagBits) };

        var found = new List<ItemQuality>();
        if (!says.Magic)
        {
            found.Add(ItemQuality.Normal);
            if (!says.Normal && QualityItems.For(probe).Count > 0)
                found.Add(ItemQuality.Superior);
        }

        if (rolls)
            found.Add(ItemQuality.Magic);
        if (rolls && says.Rare)
            found.Add(ItemQuality.Rare);
        if (rolls && AffixRanges.CraftFamilies(probe).Count > 0)
            found.Add(ItemQuality.Craft);

        // A dungeon map is rare and nothing else: the cube makes it so and nothing drops one.
        if (ForgeScope.MapQualities(code) is { } only)
            found.RemoveAll(quality => !only.Contains(quality));

        return found;
    }

    /// <summary>
    /// A base in a quality with nothing rolled onto it, for the Forge:
    /// what it becomes is the item panel's to say. <see cref="PlainBase"/>, then the quality's own fields -
    /// <list type="bullet">
    /// <item>Superior: the quality alone. Which bonuses it has are properties, added as any other.</item>
    /// <item>Magic: no prefix and no suffix yet - the affix slots stand empty.</item>
    /// <item>Rare: a name of two halves, from the ones its type allows, and six empty slots.</item>
    /// <item>Crafted: put through the first recipe that takes it (<see cref="Crafts.CraftFrom"/>), which
    /// rolls its affixes; the panel's crafting line changes the recipe.</item>
    /// </list>
    /// </summary>
    public static Forged BaseAs(string code, ItemQuality quality, string stampAs, Random? random = null)
    {
        var forged = PlainBase(code, 0, stampAs);
        var item = forged.Item;
        var notes = new List<string>(forged.Notes);
        random ??= Random.Shared;

        // Which of its base's pictures it wears, said as every ring, amulet, charm and jewel says it.
        item.HasMultipleGraphics = ItemTypes.VariantCount(code) > 1;

        // What is carried, not worn out - a map, a key, an ear - is made one of: a stack is the Basic
        // section's to raise. A throwing weapon or a quiver is made full (DressBase), being gear.
        // A quiver's type runs up to misc, not gear: asked by that alone, a magic or rare one held a single arrow.
        if (!ItemTypes.IsGear(ItemTypes.TypeOf(code)) && Stacks.IsStackable(code)
            && !ItemTypes.LineageOfBase(code).Contains("misl", StringComparer.OrdinalIgnoreCase))
            item.Quantity = 1;

        switch (quality)
        {
            case ItemQuality.Normal:
                break;

            case ItemQuality.Superior:
            case ItemQuality.Magic:
                item.Quality = quality;
                break;

            case ItemQuality.Rare:
            {
                item.Quality = ItemQuality.Rare;
                var first = AffixNames.FirstNames(item);
                var second = AffixNames.SecondNames(item);
                if (first.Count > 0 && second.Count > 0)
                {
                    item.RarePrefixId = (ushort)first[random.Next(first.Count)].Id;
                    item.RareSuffixId = (ushort)second[random.Next(second.Count)].Id;
                }

                break;
            }

            case ItemQuality.Craft:
            {
                item.Quality = ItemQuality.Magic;
                if (AffixRanges.CraftFamilies(item) is not [var recipe, ..])
                    throw new InvalidDataException($"No craft recipe takes {code}.");

                var crafted = Crafts.CraftFrom(item, recipe, random);
                notes.Add($"crafted: {crafted.Family}");
                break;
            }

            default:
                throw new InvalidDataException($"A base is not made {quality}.");
        }

        // What the cube makes a map of this type with: a dungeon is the scarab's, five properties and all. Before the
        // roll below: its numbers are maxed with every source credited, the scarab's among them - written after,
        // they counted twice (experience 121 against 58-71).
        MapRecipes.Dress(item);

        // Rolled as a drop is, every number at its top, as a forged unique is (a random roll
        // but maxed stats): a magic or rare item's affixes (MagicAffixes.RollFresh - PD2's counts at its level),
        // the automod where its base offers a choice, a class base's staffmods. A craft has its own roll above.
        if (quality is ItemQuality.Magic or ItemQuality.Rare)
        {
            // Written as the Perfect Skull writes a fresh set (ApplyFresh) - Apply rebuilds a touched stat from every
            // source rolled again, and a map's Monster Rarity came out 60 where its sources make 31.
            var picks = MagicAffixes.RollFresh(item, random);
            MagicAffixes.ApplyFresh(item, picks, random);
            notes.Add($"rolled {picks.Count(pick => pick.Value.Id > 0)} affixes");
        }

        if (quality is ItemQuality.Magic or ItemQuality.Rare or ItemQuality.Craft)
        {
            // By frequency, as the game draws it (AffixRanges.RollAutomagic), not evenly.
            if (AffixRanges.AutomagicRowsFor(item).Count > 1 && AffixRanges.RollAutomagic(item, random) is { } drawn)
                AffixRanges.PutAutomagic(item, drawn.Id);

            // A craft's are CraftFrom's, as the cube's are the recipe's.
            if (quality is not ItemQuality.Craft && StaffMods.Rolls(item))
                StaffMods.Put(item, StaffMods.Roll(item, random));

            AffixRanges.TopOwn(item);
        }

        DamageKinds.Settle(item);
        return new Forged(item, notes, forged.Sound);
    }

    /// <summary>
    /// Makes <paramref name="item"/> the drafted runeword (the runeword builder): the rolled properties written into a list of
    /// their own and put on with <see cref="ApplyRuneword"/>. The item is changed - the panel
    /// hands it a shadow copy to stage on, and KEEP puts the same list on the item itself.
    /// </summary>
    public static Forged MakeRuneword(Item item, RunewordBuilder.Draft draft)
    {
        var notes = new List<string>();

        var words = new ItemStatList();
        foreach (var (mod, values) in draft.Rolled)
            Write(words, mod.Code, values, notes);

        Merge(words);
        ApplyRuneword(item, draft.Word, words);

        AffixRanges.DerivePacked(item, words.Stats.Select(stat => stat.Stat ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase));
        Complete(words, notes);

        bool sound = !notes.Any(note => note.Contains("cannot hold") || note.Contains("writes no property"));
        // A flat damage as the base holds it: every copy on what is no weapon, a weapon its own kinds.
        DamageKinds.Settle(item);
        return new Forged(item, notes, sound);
    }

    /// <summary>
    /// Puts a word on a base: a runeword already is cleaned back to its base first
    /// (<see cref="RunewordBuilder.Clean"/>), then its sockets are filled with the word's runes,
    /// the word's number set, and <paramref name="words"/> made the runeword's own stat list - the
    /// slot the runeword header names (5), as <see cref="Runeword"/> lays one out. What the base
    /// had stays: its own list with a superior's bonuses, the sockets, ethereal, item level. The
    /// runes' bonuses are not written: the game reads them off the runes.
    /// <para>
    /// A base already holding the word's own runes in the word's order keeps them - the last of them just set
    /// in by hand (<see cref="Socketing"/>): they are the player's runes, with their identifiers.
    /// </para>
    /// </summary>
    public static void ApplyRuneword(Item item, Runewords.Word word, ItemStatList words)
    {
        if (item.IsRuneword)
            RunewordBuilder.Clean(item);

        bool holdsThem = item.SocketedItems.Count == word.Runes.Length
                         && item.SocketedItems.Select(rune => rune.Code.Trim()).SequenceEqual(word.Runes, StringComparer.OrdinalIgnoreCase);

        if (item.TotalNumberOfSockets != word.Runes.Length || (item.SocketedItems.Count > 0 && !holdsThem))
            throw new InvalidOperationException(L($"Require {word.Runes.Length} empty sockets for {Runewords.DisplayName(word)}."));

        string stampAs = item.Version is { Length: > 0 } version ? version : "";

        item.Flags[Socketed] = true;
        item.IsRuneword = true;
        item.RunewordId = (uint)(Runewords.IdOf(word) ?? 0);

        if (!holdsThem)
        {
            foreach (string rune in word.Runes)
                Socket(item, Material(rune, 1, stampAs));
        }

        // The base's own list stays first; the word's goes after it and nothing else does.
        ItemStats.Home(item);
        while (item.StatLists.Count > 1)
            item.StatLists.RemoveAt(1);

        item.StatLists.Add(words);
        item.RunewordListSlot = 5;
    }

    /// <summary>
    /// The base a forged runeword is best made in: of the types it may be made in and none it
    /// excludes, one that takes its sockets at item level 99 - the highest tier, then the most
    /// defence or damage, then by code so the choice is the same every time. Never a quest item, a
    /// row the game has no use for, or a base that exists only as its unique - "Boneweave [S]" has six
    /// sockets and no white one was ever found.
    /// </summary>
    public static string? BestBaseFor(IReadOnlyList<string> types, IReadOnlyList<string>? excluded, int sockets) =>
        ItemBases.Codes
            .Where(each => ItemTypes.LineageOfBase(each) is { Count: > 0 } lineage
                           && types.Any(lineage.Contains)
                           && !(excluded ?? []).Any(lineage.Contains)
                           && !ItemBases.IsQuestItem(each)
                           && ForgeScope.Makes(each) && !ForgeScope.OnlyNamed(each)
                           && ItemBases.MaximumSockets(new Item { Code = each.PadRight(4, ' '), ItemLevel = 99 }) >= sockets)
            .OrderByDescending(each => ItemTier.Rank(each) ?? 0)
            .ThenByDescending(each => ItemBases.Of(each) is { } basis
                ? (basis.MaximumDefense ?? 0) + (basis.MaximumDamage ?? 0) + (basis.TwoHandMaximum ?? 0)
                : 0)
            .ThenBy(each => each, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    /// <summary>
    /// One line per property: stats written twice for the same subject are added together, as
    /// the game keeps them.
    /// </summary>
    private static void Merge(ItemStatList list)
    {
        var merged = new List<ItemStat>();

        foreach (var stat in list.Stats)
        {
            var same = merged.FirstOrDefault(each => ItemStats.SameFieldsAndCharges(each, stat));

            if (same is null)
                merged.Add(stat);
            else
                same.Value += stat.Value;
        }

        list.Stats.Clear();
        list.Stats.AddRange(merged);
    }

    /// <summary>
    /// The value as its own field can actually hold it.
    /// <para>
    /// Some rows ask for more than the stat can store, and the overflow is <b>silent</b>:
    /// Eaglehorn's <c>eaglehorn-raven 1250</c> goes into six bits and reads back as 34,
    /// Imperial Palace Map's <c>map-glob-monsterrarity 50</c> into five bits and reads back
    /// as 18. Wrapping is never what was meant, so the value is held at the edge of what
    /// fits and the caller is told.
    /// </para>
    /// <para>
    /// This is the same clamp <see cref="Migration"/> applies when carrying an item between
    /// seasons, for the same reason - a number the field cannot hold is not a number the
    /// item can have.
    /// </para>
    /// </summary>
    private static int Fits(string stat, int value) => StatFields.FieldAsSwitched(stat)?.Hold(value) ?? value;

    /// <summary>
    /// Fills in the rest of any stat run the item has started.
    /// <para>
    /// Six properties lead runs whose followers are written with no id of their own, and the
    /// reader takes whatever follows a leader as the rest of its run. A row may name only
    /// part of one - Odium gives <c>cold-min</c> and <c>cold-max</c> as separate properties
    /// and never mentions <c>coldlength</c> - so the missing members are added here at
    /// nought. The writer would do it anyway; doing it now means the item in hand is the
    /// same item that comes back off disk, which is what makes a round-trip check mean
    /// anything.
    /// </para>
    /// </summary>
    private static void Complete(ItemStatList list, List<string> notes)
    {
        foreach (var stat in list.Stats.ToList())
        {
            if (stat.Id is not { } id)
                continue;

            var run = ItemStats.RunContaining(id);
            if (run.Count <= 1 || run[0] != id)
                continue;

            foreach (int member in run.Skip(1))
            {
                if (list.Stats.Any(s => s.Id == member))
                    continue;

                string? name = StatCatalogue.NameOf(member);
                if (name is null)
                    continue;

                list.Stats.Add(new ItemStat { Id = (ushort)member, Stat = name, Value = 0 });
                notes.Add($"added {name} at nought to finish the run led by {stat.Stat}");
            }
        }
    }

    /// <summary>
    /// The property columns of a fixed-item row, in order, skipping the blanks - each its own line, as the game
    /// writes them: Sacred Totem's two "Reanimate as" are two (a totem the Demonic Cube remade).
    /// </summary>
    private static IEnumerable<(string Code, int Min, int Max, int Param)> Properties(TXTRow row) => Columns(row);

    private static IEnumerable<(string Code, int Min, int Max, int Param)> Columns(TXTRow row)
    {
        for (int n = 1; n <= 12; n++)
        {
            if (Cell(row, $"prop{n}") is not { Length: > 0 } code)
                continue;

            int min = int.TryParse(Cell(row, $"min{n}"), out int lo) ? lo : 0;
            int max = int.TryParse(Cell(row, $"max{n}"), out int hi) ? hi : min;
            // A number, or the skill or state the row names - Cyclopean Roar's proc is written
            // "Battle Cry", and read as a number it cast nothing. See AffixRanges.ParameterOf.
            int param = AffixRanges.ParameterOf(code, Cell(row, $"par{n}") ?? "");

            yield return (code, min, max, param);
        }
    }

    /// <summary>
    /// Where in a range a value lands. Ranges are not always written low to high - Cage of
    /// the Unsullied gives <c>light -4 to -8</c>, which counts down - so the maximum here
    /// means the strongest end rather than the larger number.
    /// </summary>
    private static int Chosen(int min, int max, RollAs roll, Random? random)
    {
        if (min == max)
            return min;

        int low = Math.Min(min, max), high = Math.Max(min, max);

        return roll switch
        {
            RollAs.Maximum => Math.Abs(max) >= Math.Abs(min) ? max : min,
            RollAs.Minimum => Math.Abs(max) >= Math.Abs(min) ? min : max,
            _ => (random ?? Random.Shared).Next(low, high + 1),
        };
    }

    /// <summary>
    /// A column's text, or null when the row has no such column. Asked by index a row throws
    /// rather than answering null - which once took a whole character down over a single set
    /// item - and catching that cost some 1,200 exceptions a minute; Get asks first.
    /// </summary>
    private static string? Cell(TXTRow row, string column) => row.Get(column)?.Value?.Trim();
}
